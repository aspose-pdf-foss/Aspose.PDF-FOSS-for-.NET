using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf;

public sealed partial class Document
{
    // ── The class-width form letter (an HtmlFragment quote schedule) ───────────
    // A generated insurance schedule added through Page.Paragraphs: a stylesheet
    // that sizes every table column by CLASS (`.ThreeCol1 { width: 350px }`), a
    // body box of its own declared width, a logo image, centred/underlined
    // headings, and a run of flat `label : value` tables.
    //
    // The engine's model for this shape, derived from the expected render
    // (see the generator HtmlFragment table-column law):
    //
    //   Blocks stack line boxes with NO paragraph margins. A line box is
    //   round(fontPx x 1.15) px; its baseline sits halfLeading + ascent below the
    //   top, ascent = 1854/2048 em and descent = 434/2048 em (Arial). An image
    //   sits its own height on the line and the line keeps the strut's descent.
    //
    //   A table lays its columns out to fill the DECLARED body width B, not the
    //   page: with n columns the content budget is B - 2*2.25 - (n-1)*3.064 and
    //   every column takes its declared px share of it. The first cell's text
    //   starts 2.25 pt inside the table, and each following column's text starts
    //   3.064 pt past the previous column's width (the 2 px border-spacing plus
    //   the cell's own 1 px side padding).
    //
    //   A row is padding-top (.5 em of its own font) + its tallest cell's lines +
    //   1 px padding-bottom, and rows are separated by the 2 px border-spacing.
    //   Cells are MIDDLE-aligned: a one-line label in a two-line row seats at the
    //   mean of that row's line baselines.
    //
    //   `page-break-inside: avoid` (which the corpus injects) moves a table that
    //   does not fit whole to the next page; `page-break-after: always` on an
    //   empty div breaks there.

    private const double QsPxToPt = 0.75;
    private const double QsLineFactor = 1.15;        // CSS "normal" line height
    private const double QsAscentEm = 1854.0 / 2048;  // Arial hhea ascender
    private const double QsDescentEm = 434.0 / 2048;  // Arial hhea descender
    private const double QsCellInset = 2.25;         // table edge → first cell text (3 px)
    private const double QsColStep = 3.064;          // one column's width → the next text x
    private const double QsRowSpacing = 1.5;         // border-spacing (2 px)
    private const double QsCellPadBottom = 0.75;     // the cell's own 1 px bottom padding
    private const double QsCellPadTopEm = 0.5;       // `.TableDefault td { padding-top: .5em }`
    private const double QsBodyPx = 800.0;           // fallback `body { width }`
    private const double QsDefaultFontPx = 13.0;
    private const double QsUnderlineDrop = 0.1;      // title rule below the baseline, em
    private const double QsUnderlineW = 0.1;         // …and its stroke width, em (1.125 at 15 px)

    private sealed class QsCell
    {
        public string Text = "";
        public double WidthPx;
        public bool Bold;
        public bool Italic;
        public double FontPx = QsDefaultFontPx;
        public bool Centre;
        public int ColSpan = 1;
    }

    private sealed class QsBlock
    {
        public string Kind = "";                     // img / line / para / table / pagebreak
        public string Text = "";
        public byte[]? Image;
        public double ImgW, ImgH;
        public double FontPx = QsDefaultFontPx;
        public bool Bold, Italic, Centre, Underline;
        public List<List<QsCell>> Rows = new();
    }

    private static double QsLineBox(double fontPx)
        => Math.Round(fontPx * QsLineFactor, MidpointRounding.AwayFromZero) * QsPxToPt;

    private static double QsBaselineInLine(double fontPx)
    {
        var box = Math.Round(fontPx * QsLineFactor, MidpointRounding.AwayFromZero);
        var half = (box - fontPx * (QsAscentEm + QsDescentEm)) / 2;
        return (half + fontPx * QsAscentEm) * QsPxToPt;
    }

    /// <summary>Read the fragment's own &lt;style&gt; rules into a selector → (width px,
    /// font px, centre, underline) map — the declarations this dialect uses.</summary>
    private static Dictionary<string, (double width, double fontPx, bool centre, bool underline)>
        QsParseCss(string html)
    {
        var map = new Dictionary<string, (double, double, bool, bool)>(StringComparer.OrdinalIgnoreCase);
        foreach (Match sm in Regex.Matches(html, @"<style[^>]*>([\s\S]*?)</style>", RegexOptions.IgnoreCase))
            foreach (Match rm in Regex.Matches(sm.Groups[1].Value, @"([^{}]+)\{([^}]*)\}"))
            {
                var body = rm.Groups[2].Value;
                double width = 0, fontPx = 0;
                if (Regex.Match(body, @"(?<![-\w])width\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase)
                        is { Success: true } wm)
                    width = double.Parse(wm.Groups[1].Value, CultureInfo.InvariantCulture);
                if (Regex.Match(body, @"font-size\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase)
                        is { Success: true } fm)
                    fontPx = double.Parse(fm.Groups[1].Value, CultureInfo.InvariantCulture);
                var centre = Regex.IsMatch(body, @"text-align\s*:\s*center", RegexOptions.IgnoreCase);
                var underline = Regex.IsMatch(body, @"text-decoration\s*:\s*underline", RegexOptions.IgnoreCase);
                foreach (var selRaw in rm.Groups[1].Value.Split(','))
                {
                    var sel = selRaw.Trim();
                    // the last simple selector names the rule (`.A .B` styles B)
                    var lastSpace = sel.LastIndexOf(' ');
                    if (lastSpace >= 0) sel = sel[(lastSpace + 1)..];
                    var key = sel.StartsWith('.') ? sel[1..] : sel;
                    if (key.Length == 0 || key.Contains('.') || key.Contains(':')) continue;
                    map.TryGetValue(key, out var prev);
                    map[key] = (width > 0 ? width : prev.Item1,
                        fontPx > 0 ? fontPx : prev.Item2,
                        centre || prev.Item3, underline || prev.Item4);
                }
            }
        return map;
    }

    /// <summary>Walk the fragment's body into the flat block list the renderer places:
    /// image, empty line, paragraph, table, forced page break.</summary>
    private static List<QsBlock> QsParseBlocks(string html, Dictionary<string, (double width, double fontPx, bool centre, bool underline)> css, HtmlLoadOptions? options)
    {
        var qb = new QuoteBlockParseState();
        qb.html = html;
        qb.css = css;
        qb.options = options;
        qb.blocks = new List<QsBlock>();
        qb.bodyAt = qb.html.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
        qb.body = qb.bodyAt >= 0 ? qb.html[qb.bodyAt..] : qb.html;

        qb.rx = new Regex(
            @"<table[^>]*>[\s\S]*?</table>|<p\b[^>]*>[\s\S]*?</p>|<br\s*/?>|<img\b[^>]*>|<div\b[^>]*>",
            RegexOptions.IgnoreCase);
        foreach (Match m in qb.rx.Matches(qb.body))
        {
            ParseQuoteBlockMatch(qb, m);
        }
        return qb.blocks;
    }

    /// <summary>Pixel size of a PNG or JPEG payload, read from its own header.</summary>
    private static (int w, int h)? QsTryPngSize(byte[] data)
    {
        int w = default;
        int h = default;
        w = h = 0;
        if (data.Length > 24 && data[0] == 0x89 && data[1] == 0x50)
        {
            w = (data[16] << 24) | (data[17] << 16) | (data[18] << 8) | data[19];
            h = (data[20] << 24) | (data[21] << 16) | (data[22] << 8) | data[23];
            return (w > 0 && h > 0) ? (w, h) : null;
        }
        if (data.Length > 4 && data[0] == 0xFF && data[1] == 0xD8)
            for (var i = 2; i + 9 < data.Length;)
            {
                if (data[i] != 0xFF) { i++; continue; }
                var marker = data[i + 1];
                var len = (data[i + 2] << 8) | data[i + 3];
                if (marker is >= 0xC0 and <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
                {
                    h = (data[i + 5] << 8) | data[i + 6];
                    w = (data[i + 7] << 8) | data[i + 8];
                    return (w > 0 && h > 0) ? (w, h) : null;
                }
                i += 2 + len;
            }
        return null;
    }
}
