using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>How far a cell link's box reaches below its baseline, in em (the bordered grid's link box).</summary>
    private const double CellLinkDescentEm = 0.25;

    private static readonly Regex ElementIdRx = new(
        @"<[a-zA-Z][\w-]*\b[^>]*?\s(?:id|name)\s*=\s*(?:""([^""]+)""|'([^']+)'|([^\s>""']+))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexLimits.Timeout);

    /// <summary>The ids and anchor names the tags of a markup run declare, in document order.</summary>
    private static List<string> ElementIds(string markup)
    {
        var ids = new List<string>();
        foreach (Match m in SafeRegex.Matches(ElementIdRx, markup))
        {
            var id = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
            if (id.Length > 0) ids.Add(id);
        }
        return ids;
    }

    /// <summary>Every id (and anchor name) inside a table is a link target at the table's top, where the flow
    /// stands when the table starts; a target the table's own layout places more exactly replaces it.</summary>
    private static void RegisterTableAnchorTargets(ConvertState cv, Block block)
    {
        if (block.TableHtml is not { Length: > 0 } html) return;
        foreach (var id in ElementIds(html).Where(id => !cv.anchorTargets.ContainsKey(id)))
            cv.anchorTargets[id] = (cv.flow.page, cv.flow.y);
    }
}
