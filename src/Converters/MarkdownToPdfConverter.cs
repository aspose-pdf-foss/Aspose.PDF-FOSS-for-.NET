using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Converters;

/// <summary>
/// Converts Markdown files to PDF documents.
/// Supports: headings, paragraphs, bold, italic, code blocks, lists, quotes, horizontal rules, links.
/// </summary>
internal static class MarkdownToPdfConverter
{
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
        var pageWidth = options?.PageInfo?.Width ?? 612;
        var pageHeight = options?.PageInfo?.Height ?? 792;
        var marginLeft = options?.PageInfo?.Margin?.Left ?? 72;
        var marginTop = options?.PageInfo?.Margin?.Top ?? 72;
        var marginRight = options?.PageInfo?.Margin?.Right ?? 72;
        var marginBottom = options?.PageInfo?.Margin?.Bottom ?? 72;

        var doc = Document.Create();
        var page = doc.Pages.Add(pageWidth, pageHeight);

        // Ensure font resources
        var fontResName = EnsureFont(page, "Helvetica", "F1");
        var boldResName = EnsureFont(page, "Helvetica-Bold", "F2");
        var italicResName = EnsureFont(page, "Helvetica-Oblique", "F3");
        var monoResName = EnsureFont(page, "Courier", "F4");

        var sb = new StringBuilder();
        var contentWidth = pageWidth - marginLeft - marginRight;
        var y = pageHeight - marginTop;
        var x = marginLeft;
        var baseFontSize = 11.0;
        var lineHeight = baseFontSize * 1.4;

        var lines = mdText.Split('\n');
        var inCodeBlock = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');

            // Check for new page needed
            if (y < marginBottom + lineHeight * 2)
            {
                page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
                sb.Clear();
                page = doc.Pages.Add(pageWidth, pageHeight);
                EnsureFont(page, "Helvetica", "F1");
                EnsureFont(page, "Helvetica-Bold", "F2");
                EnsureFont(page, "Helvetica-Oblique", "F3");
                EnsureFont(page, "Courier", "F4");
                y = pageHeight - marginTop;
            }

            // Code block (fenced)
            if (line.StartsWith("```"))
            {
                inCodeBlock = !inCodeBlock;
                continue;
            }

            if (inCodeBlock)
            {
                EmitTextLine(sb, monoResName, baseFontSize * 0.9, x + 10, y, EscapePdf(line));
                y -= lineHeight;
                continue;
            }

            // Empty line = paragraph break
            if (string.IsNullOrWhiteSpace(line))
            {
                y -= lineHeight * 0.5;
                continue;
            }

            // Horizontal rule
            if (Regex.IsMatch(line, @"^(\*{3,}|-{3,}|_{3,})\s*$"))
            {
                // Draw a line
                sb.Append($"q 0.5 G 0.5 w {F(x)} {F(y)} m {F(x + contentWidth)} {F(y)} l S Q\n");
                y -= lineHeight;
                continue;
            }

            // Headings
            var headingMatch = Regex.Match(line, @"^(#{1,6})\s+(.+)$");
            if (headingMatch.Success)
            {
                var level = headingMatch.Groups[1].Value.Length;
                var text = headingMatch.Groups[2].Value;
                var fontSize = level switch
                {
                    1 => baseFontSize * 2.0,
                    2 => baseFontSize * 1.6,
                    3 => baseFontSize * 1.3,
                    4 => baseFontSize * 1.1,
                    _ => baseFontSize,
                };
                y -= fontSize * 0.3; // spacing before heading
                EmitTextLine(sb, boldResName, fontSize, x, y, EscapePdf(StripInlineMarkdown(text)));
                y -= fontSize * 1.5;
                continue;
            }

            // Blockquote
            if (line.StartsWith("> ") || line.StartsWith(">"))
            {
                var quoteText = line.TrimStart('>').TrimStart();
                // Draw a gray left bar
                sb.Append($"q 0.8 G 2 w {F(x)} {F(y + 2)} m {F(x)} {F(y - lineHeight + 4)} l S Q\n");
                EmitTextLine(sb, italicResName, baseFontSize, x + 15, y, EscapePdf(StripInlineMarkdown(quoteText)));
                y -= lineHeight;
                continue;
            }

            // Unordered list
            var ulMatch = Regex.Match(line, @"^(\s*)[*+-]\s+(.+)$");
            if (ulMatch.Success)
            {
                var indent = ulMatch.Groups[1].Value.Length * 10 + 15;
                var text = ulMatch.Groups[2].Value;
                // Bullet
                sb.Append($"q 0 0 0 rg BT /{fontResName} {F(baseFontSize)} Tf {F(x + indent - 8)} {F(y)} Td (\\267) Tj ET Q\n");
                EmitTextLine(sb, fontResName, baseFontSize, x + indent, y, EscapePdf(StripInlineMarkdown(text)));
                y -= lineHeight;
                continue;
            }

            // Ordered list
            var olMatch = Regex.Match(line, @"^(\s*)\d+[.)]\s+(.+)$");
            if (olMatch.Success)
            {
                var indent = olMatch.Groups[1].Value.Length * 10 + 15;
                var text = olMatch.Groups[2].Value;
                var numStr = Regex.Match(line.TrimStart(), @"^(\d+)").Groups[1].Value;
                EmitTextLine(sb, fontResName, baseFontSize, x + indent - 15, y, numStr + ".");
                EmitTextLine(sb, fontResName, baseFontSize, x + indent, y, EscapePdf(StripInlineMarkdown(text)));
                y -= lineHeight;
                continue;
            }

            // Indented code (4 spaces or tab)
            if (line.StartsWith("    ") || line.StartsWith("\t"))
            {
                var codeText = line.TrimStart();
                EmitTextLine(sb, monoResName, baseFontSize * 0.9, x + 20, y, EscapePdf(codeText));
                y -= lineHeight;
                continue;
            }

            // Regular paragraph — handle inline formatting
            var plainText = StripInlineMarkdown(line);
            // Detect bold/italic for first-pass font selection
            var usedFont = fontResName;
            if (Regex.IsMatch(line, @"\*\*\*.+?\*\*\*|___.+?___")) usedFont = boldResName; // simplification
            else if (Regex.IsMatch(line, @"\*\*.+?\*\*|__.+?__")) usedFont = boldResName;
            else if (Regex.IsMatch(line, @"\*.+?\*|_.+?_")) usedFont = italicResName;

            EmitTextLine(sb, usedFont, baseFontSize, x, y, EscapePdf(plainText));
            y -= lineHeight;
        }

        // Flush remaining content
        if (sb.Length > 0)
            page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));

        return doc;
    }

    private static void EmitTextLine(StringBuilder sb, string fontRes, double fontSize, double x, double y, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        sb.Append($"BT /{fontRes} {F(fontSize)} Tf {F(x)} {F(y)} Td ({text}) Tj ET\n");
    }

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
