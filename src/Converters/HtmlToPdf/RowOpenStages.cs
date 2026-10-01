using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The stages of the table-row open: the row attributes and the declared cell widths.

    /// <summary>Reads the row attributes: the tr height, class and style declarations.</summary>
    private static void ReadRowAttributes(TableStyleConfig cfg, TableParseState ps, Token tok)
    {
        if (tok.Attributes is not null)
        {
            if (tok.Attributes.TryGetValue("style", out var trSt) && trSt is not null
                && Regex.Match(trSt, @"background(?:-color)?\s*:\s*([^;]+)",
                    RegexOptions.IgnoreCase) is { Success: true } trBgm
                && ParseCssColor(trBgm.Groups[1].Value) is { } trBg)
                ps.row!.BackgroundColor = trBg;
            else if (tok.Attributes.TryGetValue("bgcolor", out var trBgAttr)
                && ParseCssColor(trBgAttr) is { } trBgA)
                ps.row!.BackgroundColor = trBgA;
            // A class rule's fill paints the row band too
            // (`<tr class="colourlightgreen">` — the filing form's
            // section headers). Scoped to the over-declared grid
            // dialect; legacy dialects were calibrated without it.
            if (ps.row!.BackgroundColor is null && cfg.fullWidthCjkMin
                && tok.Attributes.TryGetValue("class", out var trBandCls))
                foreach (var cn in trBandCls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!cfg.css.TryGetValue("." + cn, out var trRule))
                        cfg.docCss?.TryGetValue("." + cn, out trRule);
                    if (trRule is not null
                        && trRule.TryGetValue("background-color", out var trCbg)
                        && ParseCssColor(trCbg) is { } trCbgc)
                    {
                        ps.row.BackgroundColor = trCbgc;
                        break;
                    }
                }
        }
    }

    /// <summary>Reads the declared cell widths of the row when the dialect honours them.</summary>
    private static void ReadRowDeclaredWidths(TableStyleConfig cfg, TableParseState ps, Token tok)
    {
        if ((cfg.overDeclaredDraw || cfg.ptCellWidths) && tok.Attributes is not null
            && tok.Attributes.TryGetValue("style", out var trHSt) && trHSt is not null
            && Regex.Match(trHSt, @"(?<![-\w])height\s*:\s*([\d.]+)\s*(px|pt)",
                RegexOptions.IgnoreCase) is { Success: true } trHm
            && double.TryParse(trHm.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var trHPx))
        {
            var trHPt2 = trHm.Groups[2].Value.Equals("px",
                StringComparison.OrdinalIgnoreCase) ? trHPx * PxToPt : trHPx;
            if (trHPt2 > ps.rowMinHeightPt) ps.rowMinHeightPt = trHPt2;
        }
    }
}
