using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The chain rule's border and text alignment land on the cell that has none of its own.</summary>
    private static void ApplyChainCellBorderAndAlign(CellOpenState co, Dictionary<string, string> cd)
    {
        if (co.ps.cell!.Border is null && cd.TryGetValue("border", out var cbord)
            && ChainBorder(cbord) is { } cbi)
        {
            // Separate borders: the UA border-spacing shows as
            // extra stroke between the cells' individual borders —
            // but ONLY for white separator strokes (the Managers
            // grid); a real coloured border (the detail buttons'
            // 1px gray) keeps its declared width.
            var cbWhite = cbi.Color is { R: > 240, G: > 240, B: > 240 };
            var cbEff = co.chainBorderSeparate && cbWhite
                ? new BorderInfo(BorderSide.Box,
                    cbi.Width + SeparateBorderSpacingPt, cbi.Color)
                : cbi;
            // border-radius rounds the cell's box (the detail
            // buttons); the bg fill follows it at draw.
            if (cd.TryGetValue("border-radius", out var cbr)
                && ChainLenPt(cbr, co.ps.cellClassPt > 0 ? co.ps.cellClassPt : co.cellFontSize)
                    is > 0 and var cbrPt)
                cbEff.RoundedBorderRadius = cbrPt;
            co.ps.cell.Border = cbEff;
        }
        if (!co.ps.alignSet && cd.TryGetValue("text-align", out var cta))
        {
            var ca = cta.Trim().ToLowerInvariant() switch
            {
                "right" => HorizontalAlignment.Right,
                "center" => HorizontalAlignment.Center,
                "left" => HorizontalAlignment.Left,
                _ => (HorizontalAlignment?)null,
            };
            if (ca is { } cav) { co.ps.alignSet = true; co.ps.cellAlign = cav; }
        }
    }
}
