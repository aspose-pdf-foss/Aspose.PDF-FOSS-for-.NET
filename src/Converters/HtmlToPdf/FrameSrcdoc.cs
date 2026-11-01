using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// The two parser rules a page needs before its markup is scanned: a body that is nothing but one
// inline frame carrying its own document, and a head that a body element closes early.
internal static partial class HtmlToPdfConverter
{
    // A body holding one frame and nothing else, the frame's document in its srcdoc attribute.
    private const string SoleSrcdocFramePattern =
        @"<body\b[^>]*>\s*<iframe\b[^>]*\bsrcdoc\s*=\s*(?:""([^""]*)""|'([^']*)')[^>]*>\s*(?:</iframe\s*>)?\s*(?:</body\b|</html\b|$)";

    // The elements the head may hold; the first start tag of a flow element ends it.
    private const string HeadScanPattern =
        @"<!--[\s\S]*?-->|<(title|style|script|noscript|template)\b[^>]*>[\s\S]*?</\1\s*>"
        + @"|<(?<flow>(?:h[1-6]|p|div|table|ul|ol|dl|img|span|a|b|i|u|strong|em|br|hr|form|pre|blockquote|center|font"
        + @"|section|article|header|footer|nav|main|label|input|button|iframe|svg|textarea|select))\b";

    /// <summary>The document a frame filling the body carries in its srcdoc attribute, decoded; the page itself when the body holds anything else.</summary>
    /// <remarks>Such a page draws nothing of its own: the frame IS its content, so the frame's document converts as the page.</remarks>
    internal static string UnwrapSoleSrcdocFrame(string html)
    {
        if (!html.Contains("srcdoc", StringComparison.OrdinalIgnoreCase)) return html;
        var frame = SafeRegex.Match(html, SoleSrcdocFramePattern, RegexOptions.IgnoreCase);
        if (!frame.Success) return html;
        // The attribute carries the document with its quotes as entities.
        var inner = System.Net.WebUtility.HtmlDecode(frame.Groups[1].Success ? frame.Groups[1].Value : frame.Groups[2].Value);
        return string.IsNullOrWhiteSpace(inner) ? html : inner;
    }

    /// <summary>The markup with its head closed where a flow element opens inside it - a heading written before the body - and the body opened there, as the parser reads it.</summary>
    internal static string CloseHeadAtFlowContent(string html)
    {
        var head = SafeRegex.Match(html, @"<head\b[^>]*>([\s\S]*?)</head\s*>", RegexOptions.IgnoreCase);
        if (!head.Success) return html;
        var inner = head.Groups[1];
        foreach (Match scan in SafeRegex.Matches(inner.Value, HeadScanPattern, RegexOptions.IgnoreCase))
        {
            if (!scan.Groups["flow"].Success) continue;
            var flowAt = inner.Index + scan.Index;
            var afterHead = html[(head.Index + head.Length)..];
            // The body tag written later keeps its attributes but opens here; a second one is ignored.
            var bodyTag = SafeRegex.Match(afterHead, @"<body\b[^>]*>", RegexOptions.IgnoreCase);
            var bodyOpen = bodyTag.Success ? bodyTag.Value : "<body>";
            if (bodyTag.Success) afterHead = afterHead.Remove(bodyTag.Index, bodyTag.Length);
            return html[..flowAt] + "</head>" + bodyOpen + html[flowAt..(inner.Index + inner.Length)] + afterHead;
        }
        return html;
    }
}
