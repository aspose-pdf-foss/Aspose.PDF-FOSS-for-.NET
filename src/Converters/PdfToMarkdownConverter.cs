using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Converters;

/// <summary>
/// Options for PDF-to-Markdown conversion.
/// </summary>
public sealed class MarkdownConverterOptions
{
    /// <summary>Minimum font size to classify as H1.</summary>
    public double H1Threshold { get; set; } = 24;

    /// <summary>Minimum font size to classify as H2.</summary>
    public double H2Threshold { get; set; } = 18;

    /// <summary>Minimum font size to classify as H3.</summary>
    public double H3Threshold { get; set; } = 14;

    /// <summary>Whether to detect and format tables.</summary>
    public bool IncludeTables { get; set; } = true;

    /// <summary>String to insert between pages.</summary>
    public string PageBreak { get; set; } = "\n---\n\n";

    /// <summary>
    /// Directory path for saving extracted images. When set, images are saved as files
    /// and referenced in markdown as ![Image](images/image_p{page}_{index}.{ext}).
    /// When null, images are skipped.
    /// </summary>
    public string? ImageOutputDirectory { get; set; }
}

/// <summary>
/// Converts PDF pages to Markdown text.
/// </summary>
public sealed class PdfToMarkdownConverter
{
    private readonly MarkdownConverterOptions _options;

    public PdfToMarkdownConverter(MarkdownConverterOptions? options = null)
    {
        _options = options ?? new MarkdownConverterOptions();
    }

    /// <summary>
    /// Convert a single page to Markdown.
    /// </summary>
    public string SavePageAsMarkdown(Document doc, int pageNumber)
    {
        var page = doc.Pages.At(pageNumber);
        return ConvertPage(page);
    }

    /// <summary>
    /// Convert each page to Markdown separately.
    /// </summary>
    public string[] SaveAllPagesAsMarkdown(Document doc)
    {
        var results = new string[doc.PageCount];
        for (var i = 1; i <= doc.PageCount; i++)
            results[i - 1] = ConvertPage(doc.Pages[i]);
        return results;
    }

    /// <summary>
    /// Convert the entire document to a single Markdown string.
    /// </summary>
    public string SaveAsMarkdown(Document doc)
    {
        var sb = new StringBuilder();
        for (var i = 1; i <= doc.PageCount; i++)
        {
            if (i > 1) sb.Append(_options.PageBreak);
            sb.Append(ConvertPage(doc.Pages[i]));
        }
        return sb.ToString();
    }

    private string ConvertPage(Page page)
    {
        var sb = new StringBuilder();

        // Extract tables if enabled — only render tables that were detected from actual grid lines
        // (Rect != null). Text-layout-only detected tables are too error-prone (false positives
        // from custom-encoded fonts, resume layouts, etc.) so we rely on text extraction for those.
        if (_options.IncludeTables)
        {
            var tableAbsorber = new TableAbsorber();
            tableAbsorber.Visit(page);
            foreach (var table in tableAbsorber.Tables.Where(t => t.Rect != null))
            {
                RenderTable(table, sb);
                sb.AppendLine();
            }
        }

        // Collect link annotations
        var links = CollectLinks(page);

        // Detect horizontal rules from content stream
        var horizontalRules = DetectHorizontalRules(page);

        // Resolve base font names from page resources
        var baseFontNames = ResolveBaseFontNames(page);

        // Extract images
        ExtractImages(page, sb);

        // Fall back to plain text extraction with font-size-based heading detection
        var fragmentAbsorber = new TextFragmentAbsorber();
        fragmentAbsorber.Visit(page);

        // Track which links have been matched to text
        var matchedLinks = new HashSet<int>();

        // Sort horizontal rule Y positions descending (PDF Y is bottom-up, process top-to-bottom)
        var ruleYPositions = horizontalRules.OrderByDescending(y => y).ToList();
        var nextRuleIndex = 0;

        foreach (var fragment in fragmentAbsorber.TextFragments)
        {
            var text = fragment.Text.Trim();
            if (string.IsNullOrEmpty(text)) continue;

            // Check if a horizontal rule should be inserted before this fragment
            // (rules with Y position above the current text fragment)
            if (fragment.Rectangle is not null)
            {
                while (nextRuleIndex < ruleYPositions.Count &&
                       ruleYPositions[nextRuleIndex] > fragment.Rectangle.LLY)
                {
                    sb.AppendLine("---");
                    sb.AppendLine();
                    nextRuleIndex++;
                }
            }

            // Check for link annotation overlap
            var linkUri = FindOverlappingLink(fragment, links, matchedLinks);

            // Detect bold/italic from font name.
            // TextState.FontName is already the resolved base font name (e.g. "Helvetica-Bold"),
            // so check it directly rather than going through the baseFontNames resource-key lookup.
            var isBold = false;
            var isItalic = false;

            var fontName = fragment.TextState.FontName;
            if (fontName is not null)
            {
                isBold = fontName.Contains("Bold", StringComparison.OrdinalIgnoreCase);
                isItalic = fontName.Contains("Italic", StringComparison.OrdinalIgnoreCase) ||
                           fontName.Contains("Oblique", StringComparison.OrdinalIgnoreCase);
            }

            // Font size based heading detection
            string formattedText;
            if (fragment.FontSize >= _options.H1Threshold)
                formattedText = $"# {EscapeMarkdown(text)}";
            else if (fragment.FontSize >= _options.H2Threshold)
                formattedText = $"## {EscapeMarkdown(text)}";
            else if (fragment.FontSize >= _options.H3Threshold)
                formattedText = $"### {EscapeMarkdown(text)}";
            else
            {
                var escaped = EscapeMarkdown(text);
                formattedText = ApplyInlineFormatting(escaped, isBold, isItalic);
            }

            // Wrap in link if applicable
            if (linkUri is not null)
                formattedText = $"[{formattedText}]({linkUri})";

            sb.AppendLine(formattedText);
        }

        // Emit remaining horizontal rules after all text
        while (nextRuleIndex < ruleYPositions.Count)
        {
            sb.AppendLine("---");
            sb.AppendLine();
            nextRuleIndex++;
        }

        // Emit standalone links (links not matched to any text fragment)
        for (var i = 0; i < links.Count; i++)
        {
            if (!matchedLinks.Contains(i) && links[i].Uri is not null)
            {
                sb.AppendLine($"[Link]({links[i].Uri})");
            }
        }

        return sb.ToString();
    }

    private List<LinkInfo> CollectLinks(Page page)
    {
        var links = new List<LinkInfo>();
        foreach (var annotation in page.Annotations)
        {
            if (annotation is LinkAnnotation linkAnnot && linkAnnot.Uri is not null && linkAnnot.Rect is not null)
            {
                links.Add(new LinkInfo(linkAnnot.Rect, linkAnnot.Uri));
            }
        }
        return links;
    }

    private static string? FindOverlappingLink(TextFragment fragment, List<LinkInfo> links,
        HashSet<int> matchedLinks)
    {
        if (fragment.Rectangle is null) return null;

        for (var i = 0; i < links.Count; i++)
        {
            if (!fragment.Rectangle.Intersect(links[i].Rect).IsEmpty)
            {
                matchedLinks.Add(i);
                return links[i].Uri;
            }
        }

        return null;
    }

    private List<double> DetectHorizontalRules(Page page)
    {
        var rules = new List<double>();
        var pageWidth = page.MediaBox.Width;
        var minRuleWidth = pageWidth * 0.8;

        // Parse the content stream looking for long horizontal lines
        // A horizontal line is: x1 y m x2 y l S (or s)
        // We track m (moveto) and l (lineto) operators
        try
        {
            var contentStreams = GetContentStreams(page);
            foreach (var streamBytes in contentStreams)
            {
                DetectRulesInStream(streamBytes, minRuleWidth, rules);
            }
        }
        catch
        {
            // If content stream parsing fails, just skip rule detection
        }

        return rules;
    }

    private static void DetectRulesInStream(byte[] streamBytes, double minRuleWidth, List<double> rules)
    {
        var lexer = new PdfLexer(streamBytes);
        var operands = new List<PdfObject>();

        double moveX = 0, moveY = 0;
        double lineX = 0, lineY = 0;
        var hasMoveToForLine = false;

        while (true)
        {
            var token = lexer.NextToken();
            if (token.Kind == TokenKind.Eof) break;

            switch (token.Kind)
            {
                case TokenKind.Integer:
                    operands.Add(new PdfInteger(token.IntValue));
                    break;
                case TokenKind.Real:
                    operands.Add(new PdfReal(token.RealValue));
                    break;
                case TokenKind.Name:
                    operands.Add(new PdfName(token.StringValue!));
                    break;
                case TokenKind.Keyword:
                {
                    var op = token.StringValue!;
                    switch (op)
                    {
                        case "m" when operands.Count >= 2:
                            moveX = Num(operands[0]);
                            moveY = Num(operands[1]);
                            hasMoveToForLine = true;
                            break;
                        case "l" when operands.Count >= 2 && hasMoveToForLine:
                            lineX = Num(operands[0]);
                            lineY = Num(operands[1]);
                            break;
                        case "S" or "s":
                            // Check if we have a horizontal line spanning enough width
                            if (hasMoveToForLine &&
                                Math.Abs(moveY - lineY) < 1.0 &&
                                Math.Abs(lineX - moveX) >= minRuleWidth)
                            {
                                rules.Add(moveY);
                            }
                            hasMoveToForLine = false;
                            break;
                        case "re" when operands.Count >= 4:
                        {
                            // Thin filled rectangle can also be a horizontal rule
                            var rx = Num(operands[0]);
                            var ry = Num(operands[1]);
                            var rw = Num(operands[2]);
                            var rh = Num(operands[3]);
                            if (Math.Abs(rw) >= minRuleWidth && Math.Abs(rh) < 3.0)
                            {
                                rules.Add(ry);
                            }
                            break;
                        }
                        case "BI":
                            SkipInlineImage(lexer);
                            operands.Clear();
                            continue;
                        default:
                            if (op is "f" or "F" or "f*" or "B" or "B*" or "b" or "b*")
                            {
                                hasMoveToForLine = false;
                            }
                            break;
                    }
                    operands.Clear();
                    break;
                }
                default:
                    operands.Clear();
                    break;
            }
        }
    }

    private static void SkipInlineImage(PdfLexer lexer)
    {
        while (true)
        {
            var t = lexer.NextToken();
            if (t.Kind == TokenKind.Eof) return;
            if (t.Kind == TokenKind.Keyword && t.StringValue == "ID") break;
        }

        var pos = lexer.Position + 1;
        var len = lexer.Length;

        while (pos < len - 2)
        {
            var b = lexer.ByteAt(pos);
            if (b is 0x00 or 0x09 or 0x0A or 0x0C or 0x0D or 0x20 &&
                lexer.ByteAt(pos + 1) == (byte)'E' &&
                lexer.ByteAt(pos + 2) == (byte)'I')
            {
                var after = pos + 3;
                if (after >= len || lexer.ByteAt(after) is 0x00 or 0x09 or 0x0A or 0x0C or 0x0D or 0x20)
                {
                    lexer.Position = after;
                    return;
                }
            }
            pos++;
        }
        lexer.Position = len;
    }

    private void ExtractImages(Page page, StringBuilder sb)
    {
        if (_options.ImageOutputDirectory is null) return;

        var images = page.Images;
        if (images.Count == 0) return;

        if (!Directory.Exists(_options.ImageOutputDirectory))
            Directory.CreateDirectory(_options.ImageOutputDirectory);

        var i = 0;
        foreach (var image in images)
        {
            var ext = image.IsJpeg ? "jpg" : "png";
            var fileName = $"image_p{page.Number}_{i}.{ext}";
            var filePath = Path.Combine(_options.ImageOutputDirectory, fileName);

            try
            {
                image.Save(filePath);
            }
            catch
            {
                // If image save fails, still emit the reference
            }

            sb.AppendLine($"![Image](images/{fileName})");
            sb.AppendLine();
            i++;
        }
    }

    private static Dictionary<string, string> ResolveBaseFontNames(Page page)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            var reader = page.Reader;
            var resources = reader.ResolveDict(page.Dict.Get("Resources"));
            if (resources is null) return result;
            var fontDict = reader.ResolveDict(resources.Get("Font"));
            if (fontDict is null) return result;
            foreach (var key in fontDict.Keys)
            {
                var font = reader.ResolveDict(fontDict.Get(key));
                if (font is not null)
                {
                    var baseFont = font.GetName("BaseFont");
                    if (baseFont is not null)
                        result[key] = baseFont;
                }
            }
        }
        catch
        {
            // If font resolution fails, return empty
        }
        return result;
    }

    private static List<byte[]> GetContentStreams(Page page)
    {
        var result = new List<byte[]>();
        var reader = page.Reader;
        var obj = reader.Resolve(page.Dict.Get("Contents"));
        if (obj is PdfStream stream)
            result.Add(reader.DecodeStream(stream));
        else if (obj is PdfArray arr)
        {
            foreach (var item in arr)
            {
                var s = reader.ResolveStream(item);
                if (s is not null) result.Add(reader.DecodeStream(s));
            }
        }
        return result;
    }

    private static string ApplyInlineFormatting(string text, bool isBold, bool isItalic)
    {
        if (isBold && isItalic)
            return $"***{text}***";
        if (isBold)
            return $"**{text}**";
        if (isItalic)
            return $"*{text}*";
        return text;
    }

    private static void RenderTable(AbsorbedTable table, StringBuilder sb)
    {
        if (table.Rows.Count == 0) return;

        var colCount = table.Rows.Max(r => r.Cells.Count);

        // Header row
        var headerRow = table.Rows[0];
        sb.Append('|');
        for (var c = 0; c < colCount; c++)
        {
            var cellText = c < headerRow.Cells.Count ? headerRow.Cells[c].Text : "";
            sb.Append($" {EscapeMarkdown(cellText)} |");
        }
        sb.AppendLine();

        // Separator
        sb.Append('|');
        for (var c = 0; c < colCount; c++)
            sb.Append(" --- |");
        sb.AppendLine();

        // Data rows
        for (var r = 1; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];
            sb.Append('|');
            for (var c = 0; c < colCount; c++)
            {
                var cellText = c < row.Cells.Count ? row.Cells[c].Text : "";
                sb.Append($" {EscapeMarkdown(cellText)} |");
            }
            sb.AppendLine();
        }
    }

    private static string EscapeMarkdown(string text)
    {
        return text
            .Replace("\\", "\\\\")
            .Replace("|", "\\|")
            .Replace("*", "\\*")
            .Replace("_", "\\_")
            .Replace("[", "\\[")
            .Replace("]", "\\]");
    }

    private static double Num(PdfObject obj) => obj switch
    {
        PdfInteger i => i.Value, PdfReal r => r.Value, _ => 0,
    };

    private sealed record LinkInfo(Rectangle Rect, string Uri);
}
