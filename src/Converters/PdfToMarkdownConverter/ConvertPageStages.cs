using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Converters;

public sealed partial class PdfToMarkdownConverter
{
    /// <summary>The stages of one page's markdown conversion: one text fragment at a time.</summary>
    private void ConvertFragment(ConvertPageState cp, TextFragment fragment)
    {
        var text = fragment.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        // Check if a horizontal rule should be inserted before this fragment
        // (rules with Y position above the current text fragment)
        if (fragment.Rectangle is not null)
        {
            while (cp.nextRuleIndex < cp.ruleYPositions.Count &&
                   cp.ruleYPositions[cp.nextRuleIndex] > fragment.Rectangle.LLY)
            {
                cp.sb.AppendLine("---");
                cp.sb.AppendLine();
                cp.nextRuleIndex++;
            }
        }

        // Check for link annotation overlap
        var linkUri = FindOverlappingLink(fragment, cp.links, cp.matchedLinks);

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

        cp.sb.AppendLine(formattedText);
    }
}
