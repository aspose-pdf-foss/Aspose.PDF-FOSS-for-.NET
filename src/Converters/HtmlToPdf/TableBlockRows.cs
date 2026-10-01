using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    private const string RowCloseTag = "</tr>";

    /// <summary>The marker <see cref="BlockRowsAsGrids"/> leaves on the grid it makes: that grid lays out
    /// at its own min-content, because the cells of a block row size themselves and nothing stretches them.</summary>
    internal const string BlockRowGridAttr = "htmlblockrowgrid";

    /// <summary>The rule an hr draws, and the width its cell keeps clear of the column edges.</summary>
    private const double HrRulePt = 0.75;
    private const double HrRuleInsetPt = 3.0;

    /// <summary>The em a bare hr's half-em margins resolve against: the quirks grid's own cell size.</summary>
    private const double UaHrEmPt = 12.0;

    /// <summary>A row the sheet takes out of the table (<c>&lt;tr style="display:block"&gt;</c>) is no row:
    /// the browser leaves its cells to lay themselves out side by side at their own min-content, and the
    /// whole assembly stands in the FIRST column as a block of its own. Rewriting it as a one-row grid in a
    /// single unpadded cell states exactly that: the nested-grid model then floors the column at the
    /// assembly's min-content and draws the cells where the reference draws them.</summary>
    private static string BlockRowsAsGrids(string html)
    {
        if (html.IndexOf("display", StringComparison.OrdinalIgnoreCase) < 0) return html;
        var sb = new System.Text.StringBuilder(html.Length + 64);
        var at = 0;
        foreach (Match open in Regex.Matches(html, @"<tr\b[^>]*>", RegexOptions.IgnoreCase))
        {
            if (open.Index < at || !IsDisplayBlockRow(open.Value)) continue;
            var close = RowCloseIndex(html, open.Index + open.Length);
            if (close < 0) continue;
            sb.Append(html, at, open.Index - at)
              .Append("<tr><td style=\"padding-left:0px;padding-right:0px\">")
              .Append("<table " + BlockRowGridAttr + "=\"1\" cellspacing=\"0\" cellpadding=\"0\"><tr>")
              .Append(html, open.Index + open.Length, close - open.Index - open.Length)
              .Append("</tr></table></td></tr>");
            at = close + RowCloseTag.Length;
        }
        return at == 0 ? html : sb.Append(html, at, html.Length - at).ToString();
    }

    /// <summary>The row's own style attribute takes it out of the table flow.</summary>
    private static bool IsDisplayBlockRow(string tag)
        => Regex.IsMatch(tag, @"style\s*=\s*[""'][^""']*display\s*:\s*block", RegexOptions.IgnoreCase);

    /// <summary>The index of the row's own <c>&lt;/tr&gt;</c> - the first one no nested table owns.</summary>
    private static int RowCloseIndex(string html, int from)
    {
        var depth = 0;
        for (var i = from; i < html.Length; i++)
        {
            if (html[i] != '<') continue;
            if (string.Compare(html, i, "<table", 0, 6, StringComparison.OrdinalIgnoreCase) == 0) depth++;
            else if (string.Compare(html, i, "</table", 0, 7, StringComparison.OrdinalIgnoreCase) == 0) depth--;
            else if (depth == 0 && string.Compare(html, i, RowCloseTag, 0, RowCloseTag.Length, StringComparison.OrdinalIgnoreCase) == 0)
                return i;
            if (depth < 0) return -1;
        }
        return -1;
    }
    /// <summary>A row whose cells hold NOTHING draws nothing: the browser gives an empty row no line box,
    /// and a sheet's grids are full of them as spacers between their sections. A row that could paint -
    /// a cell with a class, a style, a background or a declared box - is left alone, because its chrome
    /// is real even when its content is not.</summary>
    private static string DropEmptyRows(string html)
    {
        if (html.IndexOf("<tr", StringComparison.OrdinalIgnoreCase) < 0) return html;
        var sb = new System.Text.StringBuilder(html.Length);
        var at = 0;
        foreach (Match open in Regex.Matches(html, @"<tr\b[^>]*>", RegexOptions.IgnoreCase))
        {
            if (open.Index < at) continue;
            var close = RowCloseIndex(html, open.Index + open.Length);
            if (close < 0) continue;
            var inner = html.Substring(open.Index + open.Length, close - open.Index - open.Length);
            if (!RowDrawsNothing(open.Value, inner)) continue;
            sb.Append(html, at, open.Index - at);
            at = close + RowCloseTag.Length;
        }
        return at == 0 ? html : sb.Append(html, at, html.Length - at).ToString();
    }

    /// <summary>True when the row carries no ink of its own and holds no cell that could paint one.</summary>
    private static bool RowDrawsNothing(string openTag, string inner)
    {
        if (Regex.IsMatch(openTag, @"\b(class|style|bgcolor|background|height)\s*=", RegexOptions.IgnoreCase)) return false;
        if (Regex.IsMatch(inner, @"<(table|img|input|select|textarea|hr|object|iframe)\b", RegexOptions.IgnoreCase)) return false;
        var cells = Regex.Matches(inner, @"<t[dh]\b[^>]*>", RegexOptions.IgnoreCase);
        if (cells.Count == 0) return false;
        foreach (Match c in cells)
            if (Regex.IsMatch(c.Value, @"\b(class|style|bgcolor|background|width|height|colspan|rowspan)\s*=", RegexOptions.IgnoreCase))
                return false;
        return Regex.Replace(Regex.Replace(inner, @"<[^>]*>", ""), @"[\s\u00a0]+", "").Length == 0;
    }
    /// <summary>A row whose only content is an <c>&lt;hr&gt;</c> is as tall as the rule's own box - its
    /// margins and its height - and no taller. The browser gives it no line box, and a rule declaring
    /// `margin: 5px 0; height: 1px` bands its row at eleven pixels where a line would take eighteen.
    /// The band is spelled on the row so the grid's own band reader seats it.</summary>
    private static string HrRowsAsBands(string html)
    {
        if (html.IndexOf("<hr", StringComparison.OrdinalIgnoreCase) < 0) return html;
        var sb = new System.Text.StringBuilder(html.Length);
        var at = 0;
        foreach (Match open in Regex.Matches(html, @"<tr\b[^>]*>", RegexOptions.IgnoreCase))
        {
            if (open.Index < at) continue;
            var close = RowCloseIndex(html, open.Index + open.Length);
            if (close < 0) continue;
            var inner = html.Substring(open.Index + open.Length, close - open.Index - open.Length);
            if (Regex.Matches(inner, @"<hr\b[^>]*>", RegexOptions.IgnoreCase) is not { Count: 1 } hrs) continue;
            if (Regex.IsMatch(open.Value, @"\bstyle\s*=", RegexOptions.IgnoreCase)) continue;
            if (Regex.Replace(Regex.Replace(inner, @"<[^>]*>", ""), @"[\s\u00a0]+", "").Length != 0) continue;
            if (HrBandPt(hrs[0].Value, UaHrEmPt) is not { } bandPt) continue;
            var cellOpen = Regex.Match(inner, @"<t[dh]\b[^>]*>", RegexOptions.IgnoreCase);
            if (!cellOpen.Success) continue;
            sb.Append(html, at, open.Index - at)
              .Append("<tr style=\"height:").Append(bandPt.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))
              .Append("pt\">").Append(cellOpen.Value).Append(hrs[0].Value).Append("</td></tr>");
            at = close + RowCloseTag.Length;
        }
        return at == 0 ? html : sb.Append(html, at, html.Length - at).ToString();
    }

    /// <summary>The band an <c>&lt;hr&gt;</c> asks for: its declared vertical margins plus its declared
    /// height, in points; when it declares neither, the UA's own box - half an em of margin above and
    /// below a two-pixel rule.</summary>
    private static double? HrBandPt(string hrTag, double emPt)
    {
        var style = Regex.Match(hrTag, @"style\s*=\s*[""']([^""']*)[""']", RegexOptions.IgnoreCase);
        var css = style.Success ? style.Groups[1].Value : "";
        var h = Regex.Match(css, @"(?<![-\w])height\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
        var m = Regex.Match(css, @"(?<![-\w])margin\s*:\s*([^;""]+)", RegexOptions.IgnoreCase);
        if (!h.Success && !m.Success)
            return emPt > 0 ? 2 * UaHrMarginEm * emPt + UaHrRulePt : null;
        var rulePx = h.Success ? double.Parse(h.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;
        double topPx = 0, botPx = 0;
        if (m.Success)
        {
            var parts = m.Groups[1].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            double Px(int i) => i < parts.Length
                && double.TryParse(parts[i].TrimEnd('p', 'x', 'P', 'X'), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
            topPx = Px(0);
            botPx = parts.Length >= 3 ? Px(2) : topPx;
        }
        return (topPx + rulePx + botPx) * PxToPt;
    }
}
