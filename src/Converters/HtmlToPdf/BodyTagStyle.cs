namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>No custom properties: a tag's style resolves none of its own.</summary>
    private static readonly Dictionary<string, string> NoCssVars = new(StringComparer.Ordinal);

    /// <summary>The body tag's own style merged into the sheet's body rule, each of its declarations winning over
    /// the sheet's, as the cascade has it: every reader of the body rule - its box, its typography, its pinned
    /// width - then sees the one rule the browser applies, whether a declaration stood in the sheet or on the
    /// tag. A document whose sheet has no body rule gets none here: the readers that tell a styled body from a
    /// bare one by the rule's presence keep their answer, and the tag's style reaches them as it did. A relative
    /// or auto width stays on the tag: it pins nothing (see PinnedBodyWidthPt), and in the rule it would take
    /// a page laid out at the full column for one whose body the sheet sizes.</summary>
    private static void MergeBodyTagStyle(ConvertState cv)
    {
        if (!cv.css.TryGetValue("body", out var rule) || BodyTagStyleAttr(cv.html) is not { } style) return;
        foreach (var decl in SheetRuleDeclarations(style, NoCssVars))
        {
            if (decl.Key == "width" && PinnedBodyWidthPt("width:" + decl.Value) is null) continue;
            rule[decl.Key] = decl.Value;
        }
    }

    /// <summary>The absolute width a body rule's declarations pin the body to, in points; null for none, or a
    /// relative one.</summary>
    private static double? PinnedBodyWidthPt(string declarations)
    {
        var width = SafeRegex.Match(declarations, @"(?<![\w-])width\s*:\s*([\d.]+\s*(?:px|pt|in|cm|mm))",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return width.Success ? TryParseLength(width.Groups[1].Value.Replace(" ", "")) : null;
    }
}
