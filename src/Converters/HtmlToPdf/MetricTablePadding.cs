using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The padding a STYLESHEET rule gives the cells themselves (`th, td { padding:
    /// 15px }`), in points, or −1 when no such rule exists. A declaration outranks a
    /// presentational hint, so this beats the table's own <c>cellpadding</c> attribute
    /// (probed: a `cellpadding="30"` table whose sheet pads its cells 15px lays its first
    /// column at the 15px inset, not the 30px one).</summary>
    private static double SheetCellPaddingPt(IReadOnlyDictionary<string, Dictionary<string, string>> css)
    {
        foreach (var cell in new[] { "td", "th" })
            if (css.TryGetValue(cell, out var rule)
                && rule.TryGetValue("padding", out var pad)
                && Regex.Match(pad, @"^\s*([\d.]+)\s*(px|pt|(?<=^\s*0))\s*$", RegexOptions.IgnoreCase) is { Success: true } m
                && double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v))
                return m.Groups[2].Value.Equals("px", StringComparison.OrdinalIgnoreCase) ? v * PxPt : v;
        return -1;
    }

    /// <summary>Metric table open: the cells' own per-side padding read from the CSS 1-4 value grammar.</summary>
    private static void ReadMetricCellPadding(MetricTableState mt)
    {
        // Per-side cell padding on the cells: the CSS 1-4
        // value grammar in px/pt/cm (top and left matter here).
        var wtp = Regex.Match(mt.tableHtml,
            @"<td[^>]*style\s*=\s*[""'][^""']*padding:\s*((?:[\d.]+(?:px|pt|cm)\s*){1,4})",
            RegexOptions.IgnoreCase);
        if (wtp.Success)
        {
            double PadLen(string v)
            {
                var pm2 = Regex.Match(v, @"([\d.]+)(px|pt|cm)",
                    RegexOptions.IgnoreCase);
                var n = double.Parse(pm2.Groups[1].Value,
                    System.Globalization.CultureInfo.InvariantCulture);
                return pm2.Groups[2].Value.ToLowerInvariant() switch
                {
                    "px" => n * PxPt,
                    "cm" => n * 72.0 / 2.54,
                    _ => n,
                };
            }
            var padVals = Regex.Matches(wtp.Groups[1].Value,
                @"[\d.]+(?:px|pt|cm)", RegexOptions.IgnoreCase);
            if (padVals.Count > 0)
            {
                mt.mps.wtPadV = PadLen(padVals[0].Value);
                // CSS: 1 value = all; 2/3 = [T, LR(,B)]; 4 = T R B L
                // (kept aside: the cellpadding ATTRIBUTE parse
                // below must not override the cells' own style)
                mt.mps.wtPadH = PadLen(padVals[Math.Min(
                    padVals.Count == 4 ? 3 : padVals.Count == 1 ? 0 : 1,
                    padVals.Count - 1)].Value);
                // …and the BOTTOM value: the email grid's
                // `0.75pt 4.55pt 0cm` pads the top only.
                mt.mps.wtPadB = PadLen(padVals[
                    padVals.Count >= 3 ? 2 : 0].Value);
            }
        }
    }
}
