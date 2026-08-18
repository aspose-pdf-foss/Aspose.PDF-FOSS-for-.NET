using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Converters;

/// <summary>
/// Converts Markdown files to PDF documents.
/// Supports: ATX and setext headings, paragraphs, bold/italic/bold-italic,
/// fenced and indented code, inline code, unordered/ordered lists, block
/// quotes, horizontal rules and links.
/// </summary>
internal static class MarkdownToPdfConverter
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

    // Pipe-table geometry. A table hugs the page edge rather than the text
    // margin: columns anchor at x = 9pt, and a table opening the page puts its
    // first-row baseline 19.2pt below the page top. Rows advance on a 16.48pt
    // pitch and columns are separated by a 2.9pt gutter beyond the widest cell.
    private const double TableLeft = 9.0;
    private const double TableTopBaselineOffset = 19.2;
    private const double TableFirstBaselineDrop = BaseFontSize * 0.85;
    private const double TableRowPitch = 16.48;
    private const double TableColumnGutter = 2.9;

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

    private static Document ConvertFromText(string mdText, MdLoadOptions? options)
    {
        var pageWidth = options?.PageInfo?.Width ?? 595.276;
        var pageHeight = options?.PageInfo?.Height ?? 841.89;
        var marginLeft = options?.PageInfo?.Margin?.Left ?? 36;
        var marginTop = options?.PageInfo?.Margin?.Top ?? 36;
        var marginRight = options?.PageInfo?.Margin?.Right ?? 36;
        var marginBottom = options?.PageInfo?.Margin?.Bottom ?? 36;

        var doc = Document.Create();
        var page = doc.Pages.Add(pageWidth, pageHeight);
        EnsureFonts(page);

        var sb = new StringBuilder();
        var contentWidth = pageWidth - marginLeft - marginRight;
        var y = pageHeight - marginTop;
        var x = marginLeft;
        var lineHeight = BaseFontSize * 1.4;

        var lines = mdText.Split('\n');
        var inFence = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');

            // Start a new page when the cursor runs off the bottom margin.
            if (y < marginBottom + lineHeight * 2)
            {
                page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
                sb.Clear();
                page = doc.Pages.Add(pageWidth, pageHeight);
                EnsureFonts(page);
                y = pageHeight - marginTop;
            }

            // Fenced code block toggling.
            if (line.TrimStart().StartsWith("```"))
            {
                inFence = !inFence;
                continue;
            }
            if (inFence)
            {
                EmitTextLine(sb, Mono, CodeBlockSize, x, y, EscapePdf(line.TrimEnd()));
                y -= lineHeight;
                continue;
            }

            // Blank line = paragraph break.
            if (string.IsNullOrWhiteSpace(line))
            {
                y -= lineHeight * 0.5;
                continue;
            }

            // Horizontal rule (3+ of * - _).
            if (Regex.IsMatch(line, @"^\s*(\*{3,}|-{3,}|_{3,})\s*$"))
            {
                sb.Append($"q 0.5 G 0.5 w {F(x)} {F(y)} m {F(x + contentWidth)} {F(y)} l S Q\n");
                y -= lineHeight;
                continue;
            }

            var trimmed = line.Trim();

            // GitHub pipe table: a cell row directly above a divider row of dashes
            // (optionally with colons/pipes). Rendered as a borderless grid — bold
            // header centred over each column, body cells left-aligned, column
            // width = the column's widest cell.
            if (line.Contains('|') && i + 1 < lines.Length
                && IsTableDividerLine(lines[i + 1].TrimEnd('\r')))
            {
                var header = SplitTableRow(line);
                var body = new List<List<string>>();
                var j = i + 2;
                for (; j < lines.Length; j++)
                {
                    var rowLine = lines[j].TrimEnd('\r');
                    if (string.IsNullOrWhiteSpace(rowLine) || !rowLine.Contains('|')) break;
                    body.Add(SplitTableRow(rowLine));
                }
                var atPageTop = Math.Abs(y - (pageHeight - marginTop)) < 0.01;
                EmitTable(sb, header, body, pageHeight, atPageTop, ref y);
                i = j - 1;
                continue;
            }

            // Setext heading: a plain text line underlined by a run of = (H1) or - (H2).
            if (!IsBlockLine(line) && i + 1 < lines.Length)
            {
                var next = lines[i + 1].TrimEnd('\r').Trim();
                if (Regex.IsMatch(next, @"^=+$"))
                {
                    EmitHeading(sb, 1, x, ref y, EscapePdf(TrimText(StripInlineMarkdown(trimmed))));
                    i++;
                    continue;
                }
                if (Regex.IsMatch(next, @"^-+$"))
                {
                    EmitHeading(sb, 2, x, ref y, EscapePdf(TrimText(StripInlineMarkdown(trimmed))));
                    i++;
                    continue;
                }
            }

            // ATX heading (# .. ######).
            var headingMatch = Regex.Match(line, @"^(#{1,6})\s+(.+)$");
            if (headingMatch.Success)
            {
                var level = headingMatch.Groups[1].Value.Length;
                EmitHeading(sb, level, x, ref y, EscapePdf(TrimText(StripInlineMarkdown(headingMatch.Groups[2].Value))));
                continue;
            }

            // Block quote (one or more leading '>').
            if (line.StartsWith(">"))
            {
                var quoteText = line;
                while (quoteText.StartsWith(">")) quoteText = quoteText.TrimStart('>').TrimStart();
                EmitTextLine(sb, Normal, BaseFontSize, x, y, EscapePdf(TrimText(StripInlineMarkdown(quoteText))));
                y -= lineHeight;
                continue;
            }

            // Unordered list item.
            var ulMatch = Regex.Match(line, @"^(\s*)[*+\-]\s+(.+)$");
            if (ulMatch.Success)
            {
                var indent = ulMatch.Groups[1].Value.Length * 12 + 18;
                var text = TrimText(StripInlineMarkdown(ulMatch.Groups[2].Value));
                // Bullet (WinAnsi 0x95 = U+2022) as its own fragment.
                EmitTextLine(sb, Normal, BaseFontSize, x + indent - 12, y, "\\225");
                EmitTextLine(sb, Normal, BaseFontSize, x + indent, y, EscapePdf(text));
                y -= lineHeight;
                continue;
            }

            // Ordered list item.
            var olMatch = Regex.Match(line, @"^(\s*)(\d+)[.)]\s+(.+)$");
            if (olMatch.Success)
            {
                var indent = olMatch.Groups[1].Value.Length * 12 + 18;
                var numStr = olMatch.Groups[2].Value;
                var text = TrimText(StripInlineMarkdown(olMatch.Groups[3].Value));
                EmitTextLine(sb, Normal, BaseFontSize, x + indent - 15, y, numStr + ".");
                EmitTextLine(sb, Normal, BaseFontSize, x + indent, y, EscapePdf(text));
                y -= lineHeight;
                continue;
            }

            // Indented code (4 spaces or a tab).
            if (line.StartsWith("    ") || line.StartsWith("\t"))
            {
                EmitTextLine(sb, Mono, CodeBlockSize, x, y, EscapePdf(line.Trim()));
                y -= lineHeight;
                continue;
            }

            // A whole line wrapped in single backticks = inline code.
            if (trimmed.Length >= 2 && trimmed.StartsWith("`") && trimmed.EndsWith("`"))
            {
                var code = trimmed.Substring(1, trimmed.Length - 2);
                EmitTextLine(sb, Mono, InlineCodeSize, x, y, EscapePdf(code));
                y -= lineHeight;
                continue;
            }

            // A whole line that is a single link: [text](url).
            var linkMatch = Regex.Match(trimmed, @"^\[([^\]]+)\]\(([^)]+)\)$");
            if (linkMatch.Success)
            {
                var linkText = linkMatch.Groups[1].Value;
                var url = linkMatch.Groups[2].Value;
                EmitTextLine(sb, Normal, BaseFontSize, x, y, EscapePdf(linkText));
                var w = linkText.Length * BaseFontSize * 0.5;
                var link = new Aspose.Pdf.Annotations.LinkAnnotation(page,
                    new Rectangle(x, y, x + w, y + BaseFontSize))
                {
                    Action = new Aspose.Pdf.Annotations.GoToURIAction(url),
                };
                page.Annotations.Add(link);
                y -= lineHeight;
                continue;
            }

            // Regular paragraph — pick a font from whole-line emphasis.
            var fontRes = Normal;
            if (Regex.IsMatch(trimmed, @"^\*\*\*.+\*\*\*$") || Regex.IsMatch(trimmed, @"^___.+___$"))
                fontRes = BoldItalic;
            else if (Regex.IsMatch(trimmed, @"^\*\*.+\*\*$") || Regex.IsMatch(trimmed, @"^__.+__$"))
                fontRes = Bold;
            else if (Regex.IsMatch(trimmed, @"^\*.+\*$") || Regex.IsMatch(trimmed, @"^_.+_$"))
                fontRes = Italic;

            EmitTextLine(sb, fontRes, BaseFontSize, x, y, EscapePdf(TrimText(StripInlineMarkdown(line))));
            y -= lineHeight;
        }

        if (sb.Length > 0)
            page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));

        return doc;
    }

    /// <summary>A table divider row: only pipes, colons, dashes and spaces,
    /// with at least one dash (e.g. <c>--- | :--- | ---:</c>).</summary>
    private static bool IsTableDividerLine(string line)
        => line.Contains('-') && Regex.IsMatch(line, @"^\s*\|?[\s:|\-]+\|?\s*$");

    /// <summary>Split a pipe-table row into trimmed, entity-decoded, de-markdowned
    /// cell texts. Outer pipes are optional; a cell that decodes to whitespace
    /// (e.g. <c>&amp;nbsp;</c>) becomes the empty string.</summary>
    private static List<string> SplitTableRow(string line)
    {
        var t = line.Trim();
        if (t.StartsWith("|")) t = t[1..];
        if (t.EndsWith("|")) t = t[..^1];
        var cells = new List<string>();
        foreach (var raw in t.Split('|'))
        {
            var text = System.Net.WebUtility.HtmlDecode(raw).Replace('\u00A0', ' ');
            cells.Add(TrimText(StripInlineMarkdown(text.Trim())).Trim());
        }
        return cells;
    }

    /// <summary>Render a pipe table as a borderless grid: bold header cells
    /// centred over their column, body cells left-aligned in the base font;
    /// each column is as wide as its widest cell plus a small gutter. The grid
    /// anchors at the page-edge <see cref="TableLeft"/> (not the text margin);
    /// a table opening the page starts at <see cref="TableTopBaselineOffset"/>
    /// below the page top.</summary>
    private static void EmitTable(StringBuilder sb, List<string> header,
        List<List<string>> body, double pageHeight, bool atPageTop, ref double y)
    {
        var x = TableLeft;
        var columnCount = header.Count;
        foreach (var row in body) columnCount = Math.Max(columnCount, row.Count);
        if (columnCount == 0) return;

        var normal = Text.TextPaginator.CreateMeasurer("Times-Roman", BaseFontSize, null);
        var bold = Text.TextPaginator.CreateMeasurer("Times-Bold", BaseFontSize, null);

        var widths = new double[columnCount];
        for (var c = 0; c < columnCount; c++)
        {
            if (c < header.Count && header[c].Length > 0)
                widths[c] = bold(header[c]);
            foreach (var row in body)
                if (c < row.Count && row[c].Length > 0)
                    widths[c] = Math.Max(widths[c], normal(row[c]));
        }

        var baseline = atPageTop ? pageHeight - TableTopBaselineOffset : y - TableFirstBaselineDrop;
        for (var c = 0; c < header.Count; c++)
        {
            if (header[c].Length == 0) continue;
            var cellX = x + ColumnOffset(widths, c);
            EmitTextLine(sb, Bold, BaseFontSize,
                cellX + (widths[c] - bold(header[c])) / 2, baseline, EscapePdf(header[c]));
        }
        foreach (var row in body)
        {
            baseline -= TableRowPitch;
            for (var c = 0; c < row.Count && c < columnCount; c++)
            {
                if (row[c].Length == 0) continue;
                EmitTextLine(sb, Normal, BaseFontSize,
                    x + ColumnOffset(widths, c), baseline, EscapePdf(row[c]));
            }
        }
        y = baseline - TableRowPitch;
    }

    private static double ColumnOffset(double[] widths, int column)
    {
        var offset = 0.0;
        for (var c = 0; c < column; c++) offset += widths[c] + TableColumnGutter;
        return offset;
    }

    /// <summary>Whether a line already opens a Markdown block construct
    /// (heading, quote, list, rule or code) — used to avoid mistaking such
    /// a line for the text of a setext heading.</summary>
    private static bool IsBlockLine(string line)
    {
        if (line.StartsWith("#") || line.StartsWith(">")) return true;
        if (line.StartsWith("    ") || line.StartsWith("\t")) return true;
        if (Regex.IsMatch(line, @"^\s*(\*{3,}|-{3,}|_{3,})\s*$")) return true;
        if (Regex.IsMatch(line, @"^(\s*)[*+\-]\s+")) return true;
        if (Regex.IsMatch(line, @"^(\s*)\d+[.)]\s+")) return true;
        return false;
    }

    private static void EmitHeading(StringBuilder sb, int level, double x, ref double y, string text)
    {
        var mult = level switch
        {
            1 => 2.0,
            2 => 1.5,
            3 => 1.17,
            4 => 1.0,
            5 => 0.83,
            _ => 0.67,
        };
        var size = BaseFontSize * mult;
        y -= size * 0.3; // spacing before heading
        EmitTextLine(sb, Bold, size, x, y, text);
        y -= size * 1.5;
    }

    private static void EmitTextLine(StringBuilder sb, string fontRes, double fontSize, double x, double y, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        sb.Append($"BT /{fontRes} {F(fontSize)} Tf {F(x)} {F(y)} Td ({text}) Tj ET\n");
    }

    private static string TrimText(string text) => text.TrimEnd();

    private static string StripInlineMarkdown(string text)
    {
        // Bold+italic
        text = Regex.Replace(text, @"\*\*\*(.+?)\*\*\*", "$1");
        text = Regex.Replace(text, @"___(.+?)___", "$1");
        // Bold
        text = Regex.Replace(text, @"\*\*(.+?)\*\*", "$1");
        text = Regex.Replace(text, @"__(.+?)__", "$1");
        // Italic
        text = Regex.Replace(text, @"\*(.+?)\*", "$1");
        text = Regex.Replace(text, @"_(.+?)_", "$1");
        // Inline code
        text = Regex.Replace(text, @"`(.+?)`", "$1");
        // Links [text](url)
        text = Regex.Replace(text, @"\[([^\]]+)\]\([^)]+\)", "$1");
        // Images ![alt](url)
        text = Regex.Replace(text, @"!\[([^\]]*)\]\([^)]+\)", "$1");
        // Strikethrough
        text = Regex.Replace(text, @"~~(.+?)~~", "$1");
        return text;
    }

    private static string EscapePdf(string text)
    {
        return text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
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
