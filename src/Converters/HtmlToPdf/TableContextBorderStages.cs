using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The table's class rules settle its text colour, the frame it draws and the side borders its cells take, header row included.</summary>
    private static void ResolveTableColoursAndBorders(TableStyleConfig cfg, TableParseState ps, Color? bodyTextColor)
    {
        // Cell-grid styling addressed through the table's class: side-specific cell
        // borders (".listTable td { border-top: … }" — the row-rule style), cell
        // padding, the header row's own bottom rule and alignment, and the class's
        // text colour. The class's own border strokes the table frame, not cells.
        cfg.outerBorder = null;
        cfg.cellSideBorder = null;
        if (cfg.tblClassDecl is not null && cfg.tblClassDecl.TryGetValue("color", out var ctv))
            ps.cellTextColor = ParseCssColor(ctv);
        else if (cfg.tblStyle.TryGetValue("color", out var ctv2))
            ps.cellTextColor = ParseCssColor(ctv2);
        // …else the page stylesheet's own body colour, which the grid inherits: these
        // pages set a soft grey (`body { color: #444 }`) that the black default ignores.
        else ps.cellTextColor ??= bodyTextColor;
        if (cfg.tblClassDecl is not null && cfg.tblClassDecl.TryGetValue("border", out var obv))
        {
            var t = obv.Trim();
            if (!t.StartsWith("0") && t.IndexOf("none", StringComparison.OrdinalIgnoreCase) < 0)
            {
                double obw = 1 * PxToPt;
                var wm = Regex.Match(t, @"(\d+(?:\.\d+)?)\s*px");
                if (wm.Success && double.TryParse(wm.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var obwv) && obwv > 0)
                    obw = obwv * PxToPt;
                cfg.outerBorder = new BorderInfo(BorderSide.Box, obw, ParseCssColor(t) ?? Color.Black);
            }
        }
        // A class rule that declares all FOUR border-side longhands (".blackBorder {
        // border-top: 1px solid black; border-right: …; }") boxes what carries the
        // class. This markup shape repeats the class on the table AND on every cell
        // (the same repetition CellClassRule reads), so the sides become a box on
        // every cell and the collapsed grid's outer frame alike — the "top border
        // missing on every table" defect class.
        if (cfg.outerBorder is null && cfg.tblClassDecl is not null
            && cfg.tblClassDecl.TryGetValue("border-top", out var clsBt)
            && cfg.tblClassDecl.TryGetValue("border-right", out var clsBr)
            && cfg.tblClassDecl.TryGetValue("border-bottom", out var clsBb)
            && cfg.tblClassDecl.TryGetValue("border-left", out var clsBl))
        {
            static bool Visible(string v) =>
                !v.Trim().StartsWith("0") && v.IndexOf("none", StringComparison.OrdinalIgnoreCase) < 0;
            if (Visible(clsBt) && Visible(clsBr) && Visible(clsBb) && Visible(clsBl))
            {
                double clsBw = 1 * PxToPt;
                var wm = Regex.Match(clsBt, @"(\d+(?:\.\d+)?)\s*px");
                if (wm.Success && double.TryParse(wm.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var clsBwv) && clsBwv > 0)
                    clsBw = clsBwv * PxToPt;
                var clsBc = ParseCssColor(clsBt) ?? Color.Black;
                cfg.outerBorder = new BorderInfo(BorderSide.Box, clsBw, clsBc);
                cfg.cellSideBorder ??= new BorderInfo(BorderSide.Box, clsBw, clsBc);
            }
        }
        if (TableOwnClassRule(cfg, cfg.tblClasses, " td") is { } tdRule)
        {
            if (tdRule.TryGetValue("border-top", out var btv))
            {
                var t = btv.Trim();
                if (!t.StartsWith("0") && t.IndexOf("none", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    double bw2 = 1 * PxToPt;
                    var wm = Regex.Match(t, @"(\d+(?:\.\d+)?)\s*px");
                    if (wm.Success && double.TryParse(wm.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var bwv) && bwv > 0)
                        bw2 = bwv * PxToPt;
                    cfg.cellSideBorder = new BorderInfo(BorderSide.Top, bw2, ParseCssColor(t) ?? Color.Black);
                }
            }
            if (cfg.pad <= 0 && tdRule.TryGetValue("padding", out var pv3) && TryParseLength(pv3) is { } pp3)
                cfg.pad = pp3;
        }
        if (TableOwnClassRule(cfg, cfg.tblClasses, " th") is { } thRule)
        {
            if (thRule.TryGetValue("border-bottom", out var hbv))
            {
                var t = hbv.Trim();
                if (!t.StartsWith("0") && t.IndexOf("none", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    double hw = 1 * PxToPt;
                    var wm = Regex.Match(t, @"(\d+(?:\.\d+)?)\s*px");
                    if (wm.Success && double.TryParse(wm.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var hwv) && hwv > 0)
                        hw = hwv * PxToPt;
                    ps.headerBorder = new BorderInfo(BorderSide.Bottom, hw, ParseCssColor(t) ?? Color.Black);
                }
            }
            if (thRule.TryGetValue("text-align", out var hav))
                ps.headerAlign = hav.Trim().ToLowerInvariant() switch
                {
                    "left" => HorizontalAlignment.Left,
                    "right" => HorizontalAlignment.Right,
                    "center" => HorizontalAlignment.Center,
                    _ => null,
                };
            if (cfg.pad <= 0 && thRule.TryGetValue("padding", out var pv4) && TryParseLength(pv4) is { } pp4)
                cfg.pad = pp4;
        }
    }
}
