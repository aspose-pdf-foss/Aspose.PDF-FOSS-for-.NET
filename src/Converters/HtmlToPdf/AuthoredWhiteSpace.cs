using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    // Elements whose text the parser keeps as written already, or never draws.
    private static readonly HashSet<string> VerbatimTextTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "pre", "textarea", "listing", "xmp", "script", "style",
    };

    /// <summary>The page with the text of every element whose white-space is pre, pre-wrap or break-spaces written
    /// out as the browser keeps it - each source line break a &lt;br&gt;, each space after a space or at a line start
    /// a no-break space - and the line breaks of a pre-line element as &lt;br&gt;s; the flow collapses everything
    /// else. A &lt;pre&gt; keeps its own model (see PreBlocks).</summary>
    private static string PreserveAuthoredWhiteSpace(string html, IReadOnlyDictionary<string, Dictionary<string, string>>? css)
    {
        if (!SafeRegex.IsMatch(html, @"white-space\s*:\s*(pre|break-spaces)", RegexOptions.IgnoreCase)) return html;
        HtmlNode dom;
        try
        {
            dom = ParseDom(SafeRegex.Replace(SafeRegex.Replace(html, @"<!--[\s\S]*?-->", m => new string(' ', m.Length)),
                @"<(script|style|head)[^>]*>[\s\S]*?</\1>", m => new string(' ', m.Length), RegexOptions.IgnoreCase));
        }
        catch { return html; }
        var ranges = new List<(int start, int end, bool keepSpaces)>();
        CollectWhiteSpaceRanges(dom, html, css, ranges);
        if (ranges.Count == 0) return html;
        var sb = new StringBuilder(html);
        // back to front, so the earlier ranges' offsets stay valid
        foreach (var (start, end, keepSpaces) in ranges.OrderByDescending(r => r.start))
        {
            var kept = KeepWhiteSpace(html.Substring(start, end - start), keepSpaces);
            sb.Remove(start, end - start).Insert(start, kept);
        }
        return sb.ToString();
    }

    /// <summary>The content ranges of the outermost elements that keep their white space (keepSpaces: pre,
    /// pre-wrap, break-spaces; line breaks only: pre-line).</summary>
    private static void CollectWhiteSpaceRanges(HtmlNode node, string html,
        IReadOnlyDictionary<string, Dictionary<string, string>>? css, List<(int start, int end, bool keepSpaces)> ranges)
    {
        foreach (var el in node.Children)
        {
            if (el.Tag.Length == 0 || VerbatimTextTags.Contains(el.Tag)) continue;
            var ws = DomDecl(el, "white-space", css)?.Trim().ToLowerInvariant();
            var keepSpaces = ws is "pre" or "pre-wrap" or "break-spaces";
            if (!keepSpaces && ws != "pre-line")
            {
                CollectWhiteSpaceRanges(el, html, css, ranges);
                continue;
            }
            var open = html.IndexOf('>', el.SrcIndex);
            if (open < 0 || open >= el.SrcEnd) continue;
            var end = el.SrcEnd;
            var close = SafeRegex.Match(html.Substring(open + 1, end - open - 1), @"</" + Regex.Escape(el.Tag) + @"\s*>$", RegexOptions.IgnoreCase);
            if (close.Success) end -= close.Length;
            if (end > open + 1) ranges.Add((open + 1, end, keepSpaces));
        }
    }

    /// <summary>The markup with the text between its tags written as kept white space.</summary>
    private static string KeepWhiteSpace(string markup, bool keepSpaces)
    {
        var sb = new StringBuilder(markup.Length);
        var lineStart = true;
        var afterSpace = false;
        var pos = 0;
        foreach (Match tag in SafeRegex.Matches(markup, @"<[^>]*>"))
        {
            KeepTextWhiteSpace(sb, markup, pos, tag.Index, keepSpaces, ref lineStart, ref afterSpace);
            sb.Append(tag.Value);
            if (SafeRegex.IsMatch(tag.Value, @"^<br\b", RegexOptions.IgnoreCase)) (lineStart, afterSpace) = (true, false);
            pos = tag.Index + tag.Length;
        }
        KeepTextWhiteSpace(sb, markup, pos, markup.Length, keepSpaces, ref lineStart, ref afterSpace);
        return sb.ToString();
    }

    private static void KeepTextWhiteSpace(StringBuilder sb, string markup, int from, int to, bool keepSpaces,
        ref bool lineStart, ref bool afterSpace)
    {
        for (var i = from; i < to; i++)
        {
            var c = markup[i];
            if (c == '\r') continue;
            if (c == '\n')
            {
                sb.Append("<br>");
                (lineStart, afterSpace) = (true, false);
                continue;
            }
            if (keepSpaces && c is ' ' or '\t')
            {
                sb.Append(lineStart || afterSpace ? "&nbsp;" : " ");
                afterSpace = true;
                continue;
            }
            sb.Append(c);
            (lineStart, afterSpace) = (false, false);
        }
    }
}
