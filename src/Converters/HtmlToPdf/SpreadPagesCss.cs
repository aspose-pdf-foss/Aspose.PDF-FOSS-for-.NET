using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// Spread page helpers: CSS em and px lengths.
    // ── the stylesheet's em chain ──
    private static double CssEm(SpreadPagesState sp, string sel, string prop, double fallbackEm)
    {
        if (sp.css.TryGetValue(sel, out var r) && r.TryGetValue(prop, out var v))
        {
            var m = Regex.Match(v, "([\\d.]+)\\s*em");
            if (m.Success && double.TryParse(m.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var em))
                return em;
        }
        return fallbackEm;
    }

    private static double CssPx(SpreadPagesState sp, string sel, string prop, double fallbackPt)
        => sp.css.TryGetValue(sel, out var r) && r.TryGetValue(prop, out var v)
           && TryParseLength(v) is { } pt && pt > 0 ? pt : fallbackPt;
}
