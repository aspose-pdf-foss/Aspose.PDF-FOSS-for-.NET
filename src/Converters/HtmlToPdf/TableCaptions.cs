using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The page with each table's caption lifted out of the table into a centred block of its own, above
    /// the table or - for caption-side: bottom - below it: the grid paths lay out rows only, and would drop it.</summary>
    private static string LiftTableCaptions(string html)
    {
        if (html.IndexOf("<caption", StringComparison.OrdinalIgnoreCase) < 0) return html;
        var opens = SafeRegex.Matches(html, @"<table\b[^>]*>\s*<caption\b([^>]*)>([\s\S]*?)</caption\s*>", RegexOptions.IgnoreCase);
        if (opens.Count == 0) return html;
        var sb = new StringBuilder(html);
        var edited = html.Length;
        // back to front, so the earlier tables' offsets stay valid
        foreach (Match m in opens.Cast<Match>().Reverse())
        {
            var attrs = m.Groups[1].Value;
            var block = CaptionBlock(attrs, m.Groups[2].Value);
            var captionAt = m.Value.IndexOf("<caption", StringComparison.OrdinalIgnoreCase);
            var tableOpen = m.Value.Substring(0, captionAt);
            var bottom = SafeRegex.IsMatch(attrs, @"caption-side\s*:\s*bottom", RegexOptions.IgnoreCase);
            var tableEnd = bottom ? MatchingTableEnd(html, m.Index) : -1;
            // (a table whose end lies past a caption already lifted - a nested one - keeps its caption on top)
            if (tableEnd > edited) tableEnd = -1;
            if (tableEnd >= 0) sb.Insert(tableEnd, block);
            edited = m.Index;
            sb.Remove(m.Index, m.Length).Insert(m.Index, (tableEnd >= 0 ? "" : block) + tableOpen);
        }
        return sb.ToString();
    }

    /// <summary>A caption as a block: centred, with the caption's own attributes.</summary>
    private static string CaptionBlock(string attrs, string content)
    {
        var style = SafeRegex.Match(attrs, @"\bstyle\s*=\s*([""'])(.*?)\1", RegexOptions.IgnoreCase);
        var centred = "text-align:center" + (style.Success ? ";" + style.Groups[2].Value.Replace("\"", "'") : "");
        var rest = style.Success ? attrs.Remove(style.Index, style.Length) : attrs;
        return $"<div style=\"{centred}\"{rest}>{content}</div>";
    }

    /// <summary>The offset just past the &lt;/table&gt; that closes the table opening at <paramref name="openAt"/>,
    /// nested tables counted; -1 when the table is not closed.</summary>
    private static int MatchingTableEnd(string html, int openAt)
    {
        var depth = 0;
        foreach (Match t in SafeRegex.Matches(html.Substring(openAt), @"<(/?)table\b[^>]*>", RegexOptions.IgnoreCase))
        {
            depth += t.Groups[1].Length == 0 ? 1 : -1;
            if (depth == 0) return openAt + t.Index + t.Length;
        }
        return -1;
    }
}
