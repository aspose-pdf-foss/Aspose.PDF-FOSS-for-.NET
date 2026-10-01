using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>One declaration of a cell-box class rule, by property: the box, its borders, its padding and the typography it states.</summary>
    private static void ApplyCellBoxClassProperty(MetricParseState mps, MetricCell mc, string prop, string bProp, string bVal)
    {
        switch (prop)
        {
            case "text-align":
                mc.Align = bVal.Trim().ToLowerInvariant() switch
                {
                    "right" => HorizontalAlignment.Right,
                    "center" => HorizontalAlignment.Center,
                    _ => HorizontalAlignment.Left,
                };
                break;
            case "vertical-align":
                // last declaration wins: a cell's own Ab overrides the
                // row's At (and vice versa)
                if (bVal.Contains("top", StringComparison.OrdinalIgnoreCase))
                { mc.VAlignTop = true; mc.VAlignBottom = false; }
                else if (bVal.Contains("bottom", StringComparison.OrdinalIgnoreCase))
                { mc.VAlignBottom = true; mc.VAlignTop = false; }
                break;
            case "width":
            {
                var bwm2 = Regex.Match(bVal, @"([\d.]+)\s*px");
                if (bwm2.Success) mc.WidthPx = DtpNum(bwm2.Groups[1].Value) * PxPt;
                // a class PERCENT width pins its column only when the
                // table is over-constrained (see the width solve)
                else if (Regex.Match(bVal, @"([\d.]+)\s*%") is { Success: true } bwPct)
                    mc.ClassWidthPct = DtpNum(bwPct.Groups[1].Value);
                break;
            }
            case "padding-top":
            case "padding-left":
            case "padding-right":
            case "padding-bottom":
                ApplyCellClassPadding(mps, mc, prop, bVal);
                break;
            case "height":
            {
                var bph = Regex.Match(bVal, @"([\d.]+)\s*px");
                if (bph.Success) mc.HeightPt = DtpNum(bph.Groups[1].Value) * PxPt;
                // (a UA form cell's class height is its CONTENT box, banding the row like an
                // inline one - measured: 26 px rows pitch 22.5 = 19.5 + the pads + the spacing)
                if (bph.Success && mps.uaFormCells) mc.HeightStylePt = Math.Max(mc.HeightStylePt, DtpNum(bph.Groups[1].Value) * PxPt);
                break;
            }
            case "border-left":
            case "border-right":
            case "border-bottom":
            case "border-top":
                ApplyCellClassBorder(mc, bProp, bVal);
                break;
        }
    }

    /// <summary>A cell-box class rule's padding, in px, pt or em against the cell's resolved
    /// size. A pt form cell's class padding REPLACES the cellpadding attribute: the title band
    /// is its line plus 1.5 a side, not plus 1.5 and the attribute's 0.75 (probed).</summary>
    private static void ApplyCellClassPadding(MetricParseState mps, MetricCell mc, string prop, string bVal)
    {
        var bp = Regex.Match(bVal, @"([\d.]+)\s*(px|pt|em)");
        if (!bp.Success) return;
        var bpPt = bp.Groups[2].Value.ToLowerInvariant() switch
        {
            "px" => DtpNum(bp.Groups[1].Value) * PxPt,
            "em" => DtpNum(bp.Groups[1].Value) * (mc.FontSize ?? mps.fontSize),
            _ => DtpNum(bp.Groups[1].Value),
        };
        switch (prop)
        {
            // th { padding-top: 1em } grows the row above its text.
            case "padding-top": mc.PadTopPt = mps.ptFormCells ? Math.Max(0, bpPt - mps.cellPadPt) : bpPt; break;
            case "padding-left": mc.PadLeft = bpPt; break;
            case "padding-right": mc.PadRight = bpPt; break;
            default: mc.PadBottomPt = mps.ptFormCells ? Math.Max(0, bpPt - mps.cellPadPt) : bpPt; break;
        }
    }

    /// <summary>A cell-box class rule's border on one side: its width, whether it is dashed,
    /// and its own colour (a white rule is real, invisible ink).</summary>
    private static void ApplyCellClassBorder(MetricCell mc, string bProp, string bVal)
    {
        var bbw = Regex.Match(bVal, @"([\d.]+)\s*(px|pt)", RegexOptions.IgnoreCase);
        var dashed = bVal.Contains("dashed", StringComparison.OrdinalIgnoreCase);
        if (!bbw.Success
            || !(dashed || bVal.Contains("solid", StringComparison.OrdinalIgnoreCase)))
            return;
        var sidePt = DtpNum(bbw.Groups[1].Value)
            * (bbw.Groups[2].Value.Equals("px", StringComparison.OrdinalIgnoreCase) ? PxPt : 1.0);
        // the rule's own colour (a white rule is real, invisible ink)
        var sideCol = ParseCssColor(Regex.Replace(bVal, @"solid|dashed|[\d.]+\s*(?:px|pt)", "", RegexOptions.IgnoreCase).Trim());
        switch (bProp.ToLowerInvariant())
        {
            case "border-left": mc.BorderLeftW = sidePt; if (sideCol is { } lc) mc.BorderLeftCol = lc; break;
            case "border-right": mc.BorderRightW = sidePt; if (sideCol is { } rc) mc.BorderRightCol = rc; break;
            case "border-bottom": mc.BorderBottomW = sidePt; if (sideCol is { } bc) mc.BorderBottomCol = bc; break;
            default:
                mc.BorderTopW = sidePt;
                mc.BorderTopDashed = dashed;
                if (sideCol is { } tc) mc.BorderTopCol = tc;
                break;
        }
    }
}