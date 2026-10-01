using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Converters;

/// <summary>
/// Converts Markdown files to PDF documents with a fixed layout
/// (an HTML-grade flow — see the geometry constants below).
/// Supports: ATX and setext headings, paragraphs with inline bold/italic/code/links
/// (blue, underlined, annotated), hard line breaks, fenced and indented code,
/// unordered/ordered lists, block quotes, pipe tables (wrapped cells, centred bold
/// headers), thematic breaks (the UA groove), and HTML image blocks — including
/// remote sources.
/// </summary>
internal static partial class MarkdownToPdfConverter
{
    // Font resource names (set up by EnsureFonts).
    private const string Normal = "F1";       // TimesNewRoman
    private const string Bold = "F2";         // TimesNewRomanBold
    private const string Italic = "F3";       // TimesNewRomanItalic
    private const string BoldItalic = "F4";   // TimesNewRomanBoldItalic
    private const string Mono = "F5";         // CourierNew

    private const double BaseFontSize = 12.0;
    private const double CodeBlockSize = 9.0;   // fenced / indented code
    private const double InlineCodeSize = 10.4; // a whole line wrapped in `...`

    // ── Flow geometry, probed ──────────────
    // (measured on the shipped template shapes).
    private const double BodyMargin = 6.0;      // 8px UA body margin
    private const double LineHeightEm = 1.125;  // 13.5pt baseline pitch at 12pt
    private const double AscentEm = 0.915417;   // first baseline = block top + ascent·size (probed 27.97 = 6 + 24·this)
    private const double DescentEm = 0.216;     // Times descent; block bottom = last baseline + descent·size
    private const double ParaGap = 13.36;       // vertical margin of a paragraph/table/image block (probed)
    private const double ListGap = 13.86;       // vertical margin of a list block (probed)
    // A heading's vertical margin scales with its size but never drops under the
    // paragraph gap (probed: h1 16.49 at 24pt; h2 collapses under the 13.86 list gap).
    private const double HeadingMarginEm = 0.687;
    private const double ListBulletIndent = 21.3;  // bullet/number x from the margin (probed 27.3)
    private const double ListContentIndent = 30.0; // item text x from the margin (40px; probed 36)
    private const double QuoteIndent = 30.0;       // blockquote padding-left (40px; the corpus asserts x=36)
    private const double UnderlineDrop = 1.2;      // link underline sits this far below the baseline
    private const double UnderlineW = 1.2;
    private const double PxToPt = 0.75;            // CSS pixels → points

    // Pipe-table geometry (the era template gates it): cells sit a
    // 2.25pt (3px) pad inside the table edge, columns are separated by a small
    // gutter beyond the widest cell, a row advances by its line count · 13.5 plus a
    // 3pt pad, and the first baseline sits 2.21pt + ascent under the table top.
    private const double CellPad = 2.25;
    private const double CellGutter = 2.9;
    private const double RowPad = 3.0;
    private const double HeaderTopPad = 2.21;

    // Horizontal-rule (thematic break) geometry, probed from the era
    // renderer: an <hr> is the UA 3-D groove — a 1.5pt-tall (2px) box spanning the
    // page inside the 6pt body margin, drawn as four 0.75pt strokes seated half a
    // width inside each edge: top/left black, bottom/right #555. Rules pace on a
    // collapsed 6pt margin: box top = previous bottom + 6.
    private const double HrGrooveH = 1.5;
    private const double HrStrokeW = 0.75;
    private const double HrMargin = 6.0;

    public static Document Convert(string mdPath, MdLoadOptions? options = null)
    {
        var mdText = File.ReadAllText(mdPath, Encoding.UTF8);
        return ConvertFromText(mdText, options);
    }

    public static Document Convert(byte[] mdData, MdLoadOptions? options = null)
    {
        var mdText = Encoding.UTF8.GetString(mdData);
        return ConvertFromText(mdText, options);
    }

    // ── Inline model ─────────────────────────────────────────────────────────────

    // Style bits: 1 bold, 2 italic, 4 mono.
    private sealed record Run(string Text, byte Style, string? Uri);

    // ── Block model ──────────────────────────────────────────────────────────────

    private abstract record Blk;
    // HardLines: source lines an explicit hard break (two trailing spaces) keeps apart;
    // each wraps independently.
    private sealed record ParaBlk(List<List<Run>> HardLines) : Blk;
    private sealed record HeadBlk(int Level, List<Run> Runs) : Blk;
    private sealed record CodeBlk(List<string> Lines, double Size) : Blk;
    private sealed record QuoteBlk(List<List<Run>> HardLines) : Blk;
    private sealed record ListBlk(List<(string Marker, List<Run> Runs)> Items) : Blk;
    private sealed record TableBlk(List<List<List<Run>>> Rows) : Blk;
    private sealed record HrBlk : Blk;
    private sealed record ImgBlk(byte[] Data, double W, double H, bool Center) : Blk;

    private static Document ConvertFromText(string mdText, MdLoadOptions? options)
    {
        var pageWidth = options?.PageInfo?.Width ?? 595.276;
        var pageHeight = options?.PageInfo?.Height ?? 841.89;
        var margin = BodyMargin;
        var marginTop = BodyMargin;
        var marginBottom = BodyMargin;
        // The page margin is the paper's; the body margin stays inside it, as in the
        // HTML model this converter mirrors. The reference render of a default PageInfo
        // (90/72 pt) seats its text at 96 pt from the left and 78 pt from the top.
        if (options?.PageInfo?.Margin is { } mi)
        {
            margin = mi.Left + BodyMargin;
            marginTop = mi.Top + BodyMargin;
            marginBottom = mi.Bottom + BodyMargin;
        }

        // A <style> block in the source never renders as text; when the caller set
        // IsPriorityCssPageRule its @page rule overrides the PageInfo geometry.
        var styleMatch = Regex.Match(mdText, @"<style[^>]*>(.*?)</style>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (styleMatch.Success)
        {
            mdText = mdText.Remove(styleMatch.Index, styleMatch.Length);
            if (options?.IsPriorityCssPageRule == true
                && TryReadCssPage(styleMatch.Groups[1].Value) is (var cssW, var cssH, var cssMargin))
            {
                pageWidth = cssW;
                pageHeight = cssH;
                if (cssMargin.HasValue)
                    margin = marginTop = marginBottom = cssMargin.Value;
            }
        }

        var blocks = ParseBlocks(mdText.Split('\n'));
        return LayoutBlocks(blocks, pageWidth, pageHeight, margin, marginTop, marginBottom);
    }

    // ── Parsing ──────────────────────────────────────────────────────────────────

    private static List<Blk> ParseBlocks(string[] lines)
    {
        var mb = new MarkdownBlocksState();
        mb.lines = lines;
        mb.blocks = new List<Blk>();
        mb.i = 0;
        mb.pendingCenter = false;
        while (mb.i < mb.lines.Length)
        {
            if (!ParseMarkdownBlock(mb)) break;
        }
        return mb.blocks;
    }

    /// <summary>Whether a line opens a non-paragraph block construct.</summary>
    private static bool IsBlockLine(string line)
    {
        if (line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith(">", StringComparison.Ordinal)) return true;
        if (line.StartsWith("    ", StringComparison.Ordinal) || line.StartsWith("\t", StringComparison.Ordinal)) return true;
        if (Regex.IsMatch(line, @"^\s*([-*_])(\s*\1){2,}\s*$")) return true;
        if (Regex.IsMatch(line, @"^(\s*)[*+\-]\s+")) return true;
        if (Regex.IsMatch(line, @"^(\s*)\d+[.)]\s+")) return true;
        if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)) return true;
        var t = line.Trim();
        if (t.Length >= 2 && t.StartsWith("`", StringComparison.Ordinal)
            && t.EndsWith("`", StringComparison.Ordinal)) return true; // whole-line inline code
        return false;
    }

    /// <summary>Tokenise markdown inline syntax into styled runs: ***…***, **…**, *…*
    /// (and the _ variants), `code`, ~~strike~~ (markers dropped), [text](url) and
    /// ![alt](url) (the alt text). HTML tags are dropped, entities decoded.</summary>
    private static List<Run> ParseInline(string text)
    {
        var runs = new List<Run>();
        text = Regex.Replace(text, "<[^>]+>", "");
        text = System.Net.WebUtility.HtmlDecode(text).Replace('\u00A0', ' ');
        text = HideBackslashEscapes(text);

        var pattern = new Regex(
            @"(\*\*\*(?<bi>.+?)\*\*\*)|(___(?<bi2>.+?)___)"
            + @"|(\*\*(?<b>.+?)\*\*)|(__(?<b2>.+?)__)"
            + @"|(\*(?<i>[^*]+)\*)|(\b_(?<i2>[^_]+)_\b)"
            + @"|(`(?<c>[^`]+)`)"
            + @"|(~~(?<s>.+?)~~)"
            + @"|(!\[(?<ia>[^\]]*)\]\((?<iu>[^)]+)\))"
            + @"|(\[(?<lt>[^\]]+)\]\((?<lu>[^)]+)\))");

        var pos = 0;
        foreach (Match m in pattern.Matches(text))
        {
            if (m.Index > pos) runs.Add(Plain(text.Substring(pos, m.Index - pos)));
            if (m.Groups["bi"].Success) runs.Add(Styled(m.Groups["bi"].Value, 3));
            else if (m.Groups["bi2"].Success) runs.Add(Styled(m.Groups["bi2"].Value, 3));
            else if (m.Groups["b"].Success) runs.Add(Styled(m.Groups["b"].Value, 1));
            else if (m.Groups["b2"].Success) runs.Add(Styled(m.Groups["b2"].Value, 1));
            else if (m.Groups["i"].Success) runs.Add(Styled(m.Groups["i"].Value, 2));
            else if (m.Groups["i2"].Success) runs.Add(Styled(m.Groups["i2"].Value, 2));
            else if (m.Groups["c"].Success) runs.Add(Styled(m.Groups["c"].Value, 4));
            else if (m.Groups["s"].Success) runs.Add(Plain(m.Groups["s"].Value));
            else if (m.Groups["ia"].Success || m.Groups["iu"].Success) runs.Add(Plain(m.Groups["ia"].Value));
            else if (m.Groups["lt"].Success)
                runs.Add(new Run(RestoreBackslashEscapes(m.Groups["lt"].Value), 0,
                    RestoreBackslashEscapes(m.Groups["lu"].Value.Trim())));
            pos = m.Index + m.Length;
        }
        if (pos < text.Length) runs.Add(Plain(text.Substring(pos)));
        return runs;

        static Run Plain(string s) => new Run(RestoreBackslashEscapes(s), 0, null);
        static Run Styled(string s, byte style) => new Run(RestoreBackslashEscapes(s), style, null);
    }

    // CommonMark backslash escapes: a backslash before ASCII punctuation makes that one
    // character literal. Each pair is hidden from the tokeniser as a single private-use
    // character (the punctuation's code point moved up into U+E000..), so an escaped '*'
    // or '_' can never open emphasis, and every run puts the character back on the way out.
    private const int EscapedPunctuationBase = 0xE000;
    private static readonly Regex BackslashEscape = new(@"\\([!-/:-@\[-`{-~])", RegexOptions.Compiled);

    private static string HideBackslashEscapes(string text) =>
        text.IndexOf('\\') < 0
            ? text
            : BackslashEscape.Replace(text, m => ((char)(EscapedPunctuationBase + m.Groups[1].Value[0])).ToString());

    private static string RestoreBackslashEscapes(string text)
    {
        var chars = text.ToCharArray();
        var changed = false;
        for (int i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (c >= (char)(EscapedPunctuationBase + '!') && c <= (char)(EscapedPunctuationBase + '~'))
            {
                chars[i] = (char)(c - EscapedPunctuationBase);
                changed = true;
            }
        }
        return changed ? new string(chars) : text;
    }

    /// <summary>A table divider row: only pipes, colons, dashes and spaces,
    /// with at least one dash (e.g. <c>--- | :--- | ---:</c>).</summary>
    private static bool IsTableDividerLine(string line)
        => line.Contains('-') && Regex.IsMatch(line, @"^\s*\|?[\s:|\-]+\|?\s*$");

    /// <summary>Split a pipe-table row into per-cell inline runs. Outer pipes are
    /// optional; a cell that decodes to whitespace (e.g. <c>&amp;nbsp;</c>) becomes empty.</summary>
    private static List<List<Run>> SplitTableRow(string line)
    {
        var t = line.Trim();
        if (t.StartsWith("|", StringComparison.Ordinal)) t = t[1..];
        if (t.EndsWith("|", StringComparison.Ordinal)) t = t[..^1];
        var cells = new List<List<Run>>();
        foreach (var raw in t.Split('|'))
        {
            var runs = ParseInline(raw.Trim());
            // Collapse to nothing when only whitespace survives decoding.
            if (runs.All(r => string.IsNullOrWhiteSpace(r.Text))) runs = new List<Run>();
            cells.Add(runs);
        }
        return cells;
    }

    private static byte[]? LoadImage(string src)
    {
        if (src.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || src.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return HtmlToPdfConverter.FetchRemoteImage(src);
        try
        {
            return File.Exists(src) ? File.ReadAllBytes(src) : null;
        }
        catch { return null; }
    }

    private static (int w, int h)? TryReadPngSize(byte[] data)
    {
        int w = default;
        int h = default;
        w = h = 0;
        if (data.Length < 24 || data[0] != 0x89 || data[1] != (byte)'P') return null;
        w = (data[16] << 24) | (data[17] << 16) | (data[18] << 8) | data[19];
        h = (data[20] << 24) | (data[21] << 16) | (data[22] << 8) | data[23];
        return (w > 0 && h > 0) ? (w, h) : null;
    }

    // ── Layout ───────────────────────────────────────────────────────────────────

    // ── Wrapping and emission ────────────────────────────────────────────────────

    // Fixtures run in parallel; the memoised measurers must tolerate concurrent misses.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(byte style, double size), Func<string, double>> Measurers = new();

    // ── CSS @page ────────────────────────────────────────────────────────────────

    // Named CSS page sizes in points (portrait width/height).
    private static readonly Dictionary<string, (double w, double h)> CssPageSizes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A3"] = (841.89, 1190.55),
        ["A4"] = (595.276, 841.89),
        ["A5"] = (419.53, 595.276),
        ["Letter"] = (612, 792),
        ["Legal"] = (612, 1008),
    };

    /// <summary>Read a CSS <c>@page { size: …; margin: … }</c> rule. Supports named sizes
    /// with an optional <c>landscape</c>/<c>portrait</c> keyword, an explicit length pair,
    /// and a single-value margin. Lengths accept pt/px/mm/cm/in.</summary>
    private static (double width, double height, double? margin)? TryReadCssPage(string css)
    {
        double width = 0, height = 0;
        double? margin = null;
        var page = Regex.Match(css, @"@page[^{]*\{([^}]*)\}", RegexOptions.IgnoreCase);
        if (!page.Success) return null;
        var body = page.Groups[1].Value;

        var size = Regex.Match(body, @"size\s*:\s*([^;}]+)", RegexOptions.IgnoreCase);
        if (!size.Success) return null;
        var parts = size.Groups[1].Value.Trim()
            .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        var landscape = false;
        double? w = null, h = null;
        foreach (var part in parts)
        {
            if (part.Equals("landscape", StringComparison.OrdinalIgnoreCase)) landscape = true;
            else if (part.Equals("portrait", StringComparison.OrdinalIgnoreCase)) { }
            else if (CssPageSizes.TryGetValue(part, out var named)) (w, h) = named;
            else if (TryParseCssLength(part) is { } len)
            {
                if (w is null) w = len;
                else h = len;
            }
        }
        if (w is null) return null;
        width = w.Value;
        height = h ?? w.Value;
        if (landscape && height > width) (width, height) = (height, width);

        var m = Regex.Match(body, @"margin\s*:\s*([^;}]+)", RegexOptions.IgnoreCase);
        if (m.Success && TryParseCssLength(m.Groups[1].Value.Trim().Split(' ')[0]) is { } mv)
            margin = mv;
        return (width, height, margin);
    }

    private static double? TryParseCssLength(string s)
    {
        double points = 0;
        var m = Regex.Match(s.Trim(), @"^(-?[\d.]+)(pt|px|mm|cm|in)?$", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        if (!double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            return null;
        points = m.Groups[2].Value.ToLowerInvariant() switch
        {
            "px" => v * 72.0 / 96.0,
            "mm" => v * 72.0 / 25.4,
            "cm" => v * 72.0 / 2.54,
            "in" => v * 72.0,
            _ => v,
        };
        return points;
    }

    // ── Low-level emission ───────────────────────────────────────────────────────

    /// <summary>Escape for a PDF literal string; non-ASCII characters emit as WinAnsi
    /// octal escapes (the fonts declare WinAnsiEncoding).</summary>
    private static string EscapePdf(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (ch == '\\' || ch == '(' || ch == ')') sb.Append('\\').Append(ch);
            else if (ch < 127) sb.Append(ch);
            else
            {
                // The engine's own WinAnsi table: the BCL code page 1252 exists on .NET (Core)
                // only once a code-pages provider is registered, and without it every
                // non-ASCII character (bullets, accents, curly quotes) fell back to '?'.
                var mapped = Content.ContentStreamBuilder.ToWinAnsi(ch);
                var code = mapped <= 0xFF ? (byte)mapped : (byte)'?';
                sb.Append('\\').Append(System.Convert.ToString(code, 8).PadLeft(3, '0'));
            }
        }
        return sb.ToString();
    }

    private static string F(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

    private static void EnsureFonts(Page page)
    {
        EnsureFont(page, "TimesNewRoman", Normal);
        EnsureFont(page, "TimesNewRomanBold", Bold);
        EnsureFont(page, "TimesNewRomanItalic", Italic);
        EnsureFont(page, "TimesNewRomanBoldItalic", BoldItalic);
        EnsureFont(page, "CourierNew", Mono);
    }

    private static string EnsureFont(Page page, string baseFontName, string resName)
    {
        var resources = page.Dict.Get("Resources") as PdfDictionary;
        if (resources is null)
        {
            resources = new PdfDictionary();
            page.Dict.Set("Resources", resources);
        }
        var fontDict = resources.Get("Font") as PdfDictionary;
        if (fontDict is null)
        {
            fontDict = new PdfDictionary();
            resources.Set("Font", fontDict);
        }
        if (!fontDict.ContainsKey(resName))
        {
            var font = new PdfDictionary();
            font.Set("Type", new PdfName("Font"));
            font.Set("Subtype", new PdfName("Type1"));
            font.Set("BaseFont", new PdfName(baseFontName));
            font.Set("Encoding", new PdfName("WinAnsiEncoding"));
            fontDict.Set(resName, font);
        }
        return resName;
    }
}
