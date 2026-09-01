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

    /// <summary>Render the class-width form letter if <paramref name="html"/> is one.
    /// False leaves the fragment to the ordinary block flow.</summary>
    private bool TryRenderQuoteScheduleFragment(string html, FlowLayout flow, Page page,
        double marginLeft, double marginBottom, HtmlLoadOptions? options)
    {
        // Gate: a stylesheet that sizes table columns by class, used by tables whose
        // cells carry those classes — the shape the column solver above describes.
        var css = QsParseCss(html);
        if (css.Count == 0) return false;
        var colClasses = 0;
        foreach (var kv in css)
            if (kv.Value.width > 0 && Regex.IsMatch(html,
                    @"<td\b[^>]*class\s*=\s*""[^""]*\b" + Regex.Escape(kv.Key) + @"\b",
                    RegexOptions.IgnoreCase))
                colClasses++;
        if (colClasses < 3) return false;

        var bodyPx = css.TryGetValue("body", out var bodyRule) && bodyRule.width > 0
            ? bodyRule.width : QsBodyPx;
        var bodyW = bodyPx * QsPxToPt;

        var blocks = QsParseBlocks(html, css, options);
        if (blocks.Count == 0) return false;

        var regular = SafeFindFont("Arial");
        var bold = SafeFindFontStyled("Arial", Text.FontStyles.Bold) ?? regular;
        var italic = SafeFindFontStyled("Arial", Text.FontStyles.Italic) ?? regular;
        var boldItalic = SafeFindFontStyled("Arial", Text.FontStyles.Bold | Text.FontStyles.Italic)
            ?? bold;
        if (regular is null) return false;

        Text.Font Face(bool b, bool i) => b && i ? boldItalic! : b ? bold! : i ? italic! : regular;
        double Measure(string t, double fontPx, bool b, bool i)
        {
            if (t.Length == 0) return 0;
            try { return Face(b, i).MeasureString(t, (float)(fontPx * QsPxToPt)); }
            catch { return t.Length * fontPx * QsPxToPt * 0.5; }
        }

        List<string> Wrap(string text, double fontPx, double budget, bool b, bool i)
        {
            var lines = new List<string>();
            var cur = new StringBuilder();
            foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var trial = cur.Length == 0 ? word : cur + " " + word;
                if (cur.Length == 0 || Measure(trial, fontPx, b, i) <= budget)
                { cur.Clear(); cur.Append(trial); }
                else { lines.Add(cur.ToString()); cur.Clear(); cur.Append(word); }
            }
            if (cur.Length > 0) lines.Add(cur.ToString());
            if (lines.Count == 0) lines.Add("");
            return lines;
        }

        var inv = CultureInfo.InvariantCulture;
        void Rule(double x0, double x1, double y, double w)
            => flow.AddContentToSlot(flow.CurrentSlot, Encoding.ASCII.GetBytes(
                string.Create(inv,
                    $"q 0 0 0 RG {w:0.###} w {x0:F2} {y:F2} m {x1:F2} {y:F2} l S Q\n")));

        var left = marginLeft;
        var contentTop = flow.ContentTop;
        var y = flow.CurrentY;                        // PDF coords, decreasing downward

        void Break()
        {
            flow.ForceNewPage();
            // An overflow slot only becomes a Page when its CONTENT buffer holds
            // something, and this arm writes through the deferred text queue — so
            // seed the buffer or the slot (and everything queued on it) is dropped.
            flow.InjectContentAtCursor(new byte[] { (byte)'\n' });
            y = contentTop;
        }

        foreach (var b in blocks)
        {
            switch (b.Kind)
            {
                case "pagebreak":
                    Break();
                    break;

                case "img":
                {
                    if (b.Image is null) break;
                    flow.MoveCursorTo(y);
                    flow.PlaceImageBlock(b.Image, b.ImgW, b.ImgH);
                    // the image sits ON the line's baseline; the line keeps the
                    // ambient strut's descent below it
                    y = flow.CurrentY
                        - (QsLineBox(QsDefaultFontPx) - QsBaselineInLine(QsDefaultFontPx));
                    break;
                }

                case "line":
                    y -= QsLineBox(b.FontPx);
                    break;

                case "para":
                {
                    foreach (var ln in Wrap(b.Text, b.FontPx, bodyW, b.Bold, b.Italic))
                    {
                        var box = QsLineBox(b.FontPx);
                        if (y - box < marginBottom) Break();
                        var w = Measure(ln, b.FontPx, b.Bold, b.Italic);
                        var x = b.Centre ? left + (bodyW - w) / 2 : left;
                        var baseY = y - QsBaselineInLine(b.FontPx);
                        // WriteAbsoluteText seats a fragment by its BOX BOTTOM, which
                        // sits one descent under the baseline.
                        flow.WriteAbsoluteText(x, baseY - QsDescentEm * b.FontPx * QsPxToPt,
                            ln, b.FontPx * QsPxToPt, Face(b.Bold, b.Italic));
                        if (b.Underline && w > 0)
                            Rule(x, x + w, baseY - b.FontPx * QsPxToPt * QsUnderlineDrop,
                                b.FontPx * QsPxToPt * QsUnderlineW);
                        y -= box;
                    }
                    break;
                }

                case "table":
                {
                    // Solve the columns: every column takes its declared px share of
                    // the table's own content budget. The widest row that declares
                    // its cells owns the grid (a colspan band row does not).
                    var declared = new List<double>();
                    foreach (var row in b.Rows)
                    {
                        if (row.Count <= declared.Count) continue;
                        var ok = true;
                        foreach (var c in row) if (c.ColSpan > 1 || c.WidthPx <= 0) { ok = false; break; }
                        if (!ok) continue;
                        declared.Clear();
                        foreach (var c in row) declared.Add(c.WidthPx);
                    }
                    if (declared.Count == 0) declared.Add(bodyPx);
                    var n = declared.Count;
                    var declaredSum = 0.0;
                    foreach (var d in declared) declaredSum += d * QsPxToPt;
                    if (declaredSum <= 0) break;
                    var scale = (bodyW - 2 * QsCellInset - (n - 1) * QsColStep) / declaredSum;
                    var colX = new double[n];
                    var colW = new double[n];
                    var cx = left + QsCellInset;
                    for (var i = 0; i < n; i++)
                    {
                        colX[i] = cx;
                        colW[i] = declared[i] * QsPxToPt * scale;
                        cx += colW[i] + QsColStep;
                    }
                    double SpanW(int ci, int span)
                    {
                        var w = 0.0;
                        for (var s = 0; s < span && ci + s < n; s++)
                            w += colW[ci + s] + (s > 0 ? QsColStep : 0);
                        return w;
                    }

                    // Measure every row, then place the table: one that does not fit
                    // below the cursor moves to the next page whole.
                    var wrapped = new List<List<List<string>>>();
                    var rowH = new List<double>();
                    var rowFont = new List<double>();
                    foreach (var row in b.Rows)
                    {
                        var cellLines = new List<List<string>>();
                        var maxLines = 1;
                        var fontPx = QsDefaultFontPx;
                        for (var ci = 0; ci < row.Count; ci++)
                        {
                            var c = row[ci];
                            fontPx = Math.Max(fontPx, c.FontPx);
                            var ls = c.Text.Length > 0
                                ? Wrap(c.Text, c.FontPx, SpanW(ci, c.ColSpan), c.Bold, c.Italic)
                                : new List<string> { "" };
                            cellLines.Add(ls);
                            if (ls.Count > maxLines) maxLines = ls.Count;
                        }
                        wrapped.Add(cellLines);
                        rowFont.Add(fontPx);
                        rowH.Add(fontPx * QsPxToPt * QsCellPadTopEm
                                 + maxLines * QsLineBox(fontPx) + QsCellPadBottom);
                    }
                    var tableH = QsRowSpacing;
                    foreach (var h in rowH) tableH += h + QsRowSpacing;
                    if (y - tableH < marginBottom && y < contentTop - 0.5) Break();

                    var rowTop = y - QsRowSpacing;
                    for (var ri = 0; ri < b.Rows.Count; ri++)
                    {
                        // a table taller than a whole page still has to break somewhere
                        if (rowTop - rowH[ri] < marginBottom)
                        {
                            Break();
                            rowTop = y - QsRowSpacing;
                        }
                        var row = b.Rows[ri];
                        var padTop = rowFont[ri] * QsPxToPt * QsCellPadTopEm;
                        var innerH = rowH[ri] - padTop - QsCellPadBottom;
                        for (var ci = 0; ci < row.Count && ci < n; ci++)
                        {
                            var c = row[ci];
                            var ls = wrapped[ri][ci];
                            var box = QsLineBox(c.FontPx);
                            // middle alignment inside the row's content box
                            var top = rowTop - padTop - (innerH - ls.Count * box) / 2;
                            var cellW = SpanW(ci, c.ColSpan);
                            for (var li = 0; li < ls.Count; li++)
                            {
                                if (ls[li].Length == 0) continue;
                                var w = Measure(ls[li], c.FontPx, c.Bold, c.Italic);
                                var x = c.Centre ? colX[ci] + (cellW - w) / 2 : colX[ci];
                                flow.WriteAbsoluteText(x,
                                    top - li * box - QsBaselineInLine(c.FontPx)
                                        - QsDescentEm * c.FontPx * QsPxToPt,
                                    ls[li], c.FontPx * QsPxToPt, Face(c.Bold, c.Italic));
                            }
                        }
                        rowTop -= rowH[ri] + QsRowSpacing;
                    }
                    y = rowTop;
                    break;
                }
            }
        }
        flow.MoveCursorTo(y);
        _ = page;
        return true;
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
    private static List<QsBlock> QsParseBlocks(string html,
        Dictionary<string, (double width, double fontPx, bool centre, bool underline)> css,
        HtmlLoadOptions? options)
    {
        var blocks = new List<QsBlock>();
        var bodyAt = html.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
        var body = bodyAt >= 0 ? html[bodyAt..] : html;

        static string Flat(string s) => Regex.Replace(
            System.Net.WebUtility.HtmlDecode(Regex.Replace(s, @"<[^>]+>", ""))
                .Replace(' ', ' '), @"\s+", " ").Trim();

        var rx = new Regex(
            @"<table[^>]*>[\s\S]*?</table>|<p\b[^>]*>[\s\S]*?</p>|<br\s*/?>|<img\b[^>]*>|<div\b[^>]*>",
            RegexOptions.IgnoreCase);
        foreach (Match m in rx.Matches(body))
        {
            var t = m.Value;
            if (t.StartsWith("<div", StringComparison.OrdinalIgnoreCase))
            {
                if (Regex.IsMatch(t, @"page-break-after\s*:\s*always", RegexOptions.IgnoreCase))
                    blocks.Add(new QsBlock { Kind = "pagebreak" });
                continue;
            }
            if (t.StartsWith("<br", StringComparison.OrdinalIgnoreCase))
            {
                blocks.Add(new QsBlock { Kind = "line" });
                continue;
            }
            if (t.StartsWith("<img", StringComparison.OrdinalIgnoreCase))
            {
                var srcM = Regex.Match(t, @"src\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
                if (!srcM.Success) continue;
                var raw = System.Net.WebUtility.HtmlDecode(srcM.Groups[1].Value);
                var path = raw;
                if (!File.Exists(path) && options?.BasePath is { Length: > 0 } bp)
                    path = Path.Combine(bp, raw);
                if (!File.Exists(path)) continue;
                byte[] data;
                try { data = File.ReadAllBytes(path); } catch { continue; }
                if (!QsTryPngSize(data, out var pw, out var ph)) continue;
                blocks.Add(new QsBlock
                {
                    Kind = "img", Image = data,
                    ImgW = pw * QsPxToPt, ImgH = ph * QsPxToPt,
                });
                continue;
            }
            if (t.StartsWith("<p", StringComparison.OrdinalIgnoreCase))
            {
                var blk = new QsBlock { Kind = "para" };
                if (Regex.Match(t, @"class\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase)
                        is { Success: true } clsM)
                    foreach (var cn in clsM.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                        if (css.TryGetValue(cn, out var rule))
                        {
                            if (rule.fontPx > 0) blk.FontPx = rule.fontPx;
                            blk.Centre |= rule.centre;
                            blk.Underline |= rule.underline;
                        }
                blk.Bold = Regex.IsMatch(t, @"<b\b|<strong\b", RegexOptions.IgnoreCase);
                blk.Italic = Regex.IsMatch(t, @"<i\b|<em\b", RegexOptions.IgnoreCase);
                blk.Underline |= Regex.IsMatch(t, @"<u\b", RegexOptions.IgnoreCase);
                blk.Text = Flat(t);
                // a paragraph holding only a hard space is a bare line box
                if (blk.Text.Length == 0)
                {
                    blocks.Add(new QsBlock { Kind = "line", FontPx = blk.FontPx });
                    continue;
                }
                blocks.Add(blk);
                continue;
            }
            var tbl = new QsBlock { Kind = "table" };
            foreach (Match rm in Regex.Matches(t, @"<tr\b[^>]*>([\s\S]*?)</tr>", RegexOptions.IgnoreCase))
            {
                var cells = new List<QsCell>();
                foreach (Match cm in Regex.Matches(rm.Groups[1].Value,
                    @"<td\b([^>]*)>([\s\S]*?)</td>", RegexOptions.IgnoreCase))
                {
                    var attrs = cm.Groups[1].Value;
                    var inner = cm.Groups[2].Value;
                    var cell = new QsCell { Text = Flat(inner) };
                    if (Regex.Match(attrs, @"class\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase)
                            is { Success: true } ccm)
                        foreach (var cn in ccm.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                            if (css.TryGetValue(cn, out var rule))
                            {
                                if (rule.width > 0) cell.WidthPx = rule.width;
                                if (rule.fontPx > 0) cell.FontPx = rule.fontPx;
                                cell.Centre |= rule.centre;
                            }
                    if (Regex.Match(attrs, @"colspan\s*=\s*""?(\d+)", RegexOptions.IgnoreCase)
                            is { Success: true } spanM
                        && int.TryParse(spanM.Groups[1].Value, out var sp) && sp > 1)
                        cell.ColSpan = sp;
                    cell.Bold = Regex.IsMatch(inner, @"<b\b|<strong\b", RegexOptions.IgnoreCase);
                    cell.Italic = Regex.IsMatch(inner, @"<i\b|<em\b", RegexOptions.IgnoreCase);
                    cells.Add(cell);
                }
                if (cells.Count > 0) tbl.Rows.Add(cells);
            }
            if (tbl.Rows.Count > 0) blocks.Add(tbl);
        }
        return blocks;
    }

    /// <summary>Pixel size of a PNG or JPEG payload, read from its own header.</summary>
    private static bool QsTryPngSize(byte[] data, out int w, out int h)
    {
        w = h = 0;
        if (data.Length > 24 && data[0] == 0x89 && data[1] == 0x50)
        {
            w = (data[16] << 24) | (data[17] << 16) | (data[18] << 8) | data[19];
            h = (data[20] << 24) | (data[21] << 16) | (data[22] << 8) | data[23];
            return w > 0 && h > 0;
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
                    return w > 0 && h > 0;
                }
                i += 2 + len;
            }
        return false;
    }
}
