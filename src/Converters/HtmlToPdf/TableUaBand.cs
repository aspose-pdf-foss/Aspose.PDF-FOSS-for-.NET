using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>A UA-boxed cell's painted div (`&lt;div style="padding:5px; margin-top:5px; background-color:#E3E3E3"&gt;`)
    /// is a block box: its background fills the cell's content width, its padding insets the lines,
    /// its top margin collapses with the block before it. The one-cell grid is exactly that box
    /// (measured: the status band spans 96..546, its lines at 99.75, 3.75 pt of band above and below).</summary>
    private static string UaPaintedDivsToGrids(string html)
    {
        var sb = new StringBuilder();
        var i = 0;
        while (i < html.Length)
        {
            var open = Regex.Match(html[i..], @"<div\b[^>]*\bstyle\s*=\s*(['""])(?<style>[^'""]*)\1[^>]*>", RegexOptions.IgnoreCase);
            if (!open.Success) { sb.Append(html[i..]); break; }
            var style = open.Groups["style"].Value;
            var bg = Regex.Match(style, @"(?<![-\w])background(?:-color)?\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
            var start = i + open.Index;
            var afterOpen = start + open.Length;
            if (!bg.Success || ParseCssColor(bg.Groups[1].Value.Trim()) is null)
            {
                sb.Append(html[i..afterOpen]);
                i = afterOpen;
                continue;
            }
            // the matching close, by depth
            var depth = 1;
            var end = -1;
            var contentEnd = -1;
            foreach (Match t in Regex.Matches(html[afterOpen..], @"<(/?)div\b[^>]*>", RegexOptions.IgnoreCase))
            {
                depth += t.Groups[1].Value.Length > 0 ? -1 : 1;
                if (depth == 0) { contentEnd = afterOpen + t.Index; end = contentEnd + t.Length; break; }
            }
            if (end < 0) { sb.Append(html[i..afterOpen]); i = afterOpen; continue; }
            sb.Append(html[i..start]);
            var padPx = Regex.Match(style, @"(?<![-\w])padding\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase) is { Success: true } pm ? pm.Groups[1].Value : "0";
            var mtPx = Regex.Match(style, @"(?<![-\w])margin-top\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase) is { Success: true } mm ? mm.Groups[1].Value : "0";
            var colour = bg.Groups[1].Value.Trim();
            sb.Append("<table width=\"100%\" cellpadding=\"").Append(padPx).Append("\" cellspacing=\"0\" style=\"margin-top:").Append(mtPx)
              .Append("px\"><tr><td bgcolor=\"").Append(colour).Append("\" style=\"background-color:").Append(colour).Append("\">")
              .Append(html[afterOpen..contentEnd]).Append("</td></tr></table>");
            i = end;
        }
        return sb.ToString();
    }
}
