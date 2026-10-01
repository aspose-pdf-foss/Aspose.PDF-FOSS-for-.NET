using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The sheet's own cell rules set the grid's default padding, the reset sheet's selectors taking precedence over the plain element ones.</summary>
    private static void ApplySheetCellPadding(TableStyleConfig cfg, TableParseState ps, string html)
    {
        // the descendant cell rules (`table th, table td { border: 1px solid #000 }`) and the document
        // sheet's own cell rules read; every other page keeps the bare `td`/`th` reads of its table sheet.
        ps.resetSheetGrid = IsResetSheetGrid(cfg.docCss) || IsResetSheetGrid(cfg.css);
        ps.uaControlGrid = cfg.uaDocGrid && cfg.makeRadio is not null && CheckboxGridControls(html);
        foreach (var sel in ps.resetSheetGrid ? new[] { "td", "th", "table td", "table th" } : new[] { "td", "th" })
        {
            if (!cfg.css.TryGetValue(sel, out var d) && (!ps.resetSheetGrid || cfg.docCss is null || !cfg.docCss.TryGetValue(sel, out d))) continue;
            if (d.TryGetValue("border", out var bd))
            {
                var t = bd.Trim();
                cfg.hasBorder = !t.StartsWith("0") && t.IndexOf("none", StringComparison.OrdinalIgnoreCase) < 0;
                var wm = Regex.Match(bd, @"(\d+(?:\.\d+)?)\s*px");
                if (wm.Success && double.TryParse(wm.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var bw))
                    cfg.borderWidth = bw * PxToPt;
                var bc = ParseCssColor(bd); if (bc is not null) cfg.borderColor = bc;
            }
            if (d.TryGetValue("padding", out var pv) && TryParseLength(pv) is { } pp) cfg.pad = pp;
            if (ps.resetSheetGrid && sel.EndsWith("th", StringComparison.Ordinal))
            {
                if ((d.TryGetValue("background-color", out var thBgV) || d.TryGetValue("background", out thBgV)) && ParseCssColor(thBgV) is { } thBg)
                    ps.thBackground = thBg;
                if (d.TryGetValue("text-align", out var thTa) && ParseAlignAttr(thTa) is { } thAl) ps.thAlign = thAl;
            }
        }
    }

    /// <summary>A table with no border of its own takes the sheet's element-rule frame, unless its own style opts out of one.</summary>
    private static void ResolveElementRuleFrame(TableStyleConfig cfg)
    {
        var tblBorderNone = cfg.tblStyle.TryGetValue("border-style", out var tbsNone)
            && tbsNone.IndexOf("none", StringComparison.OrdinalIgnoreCase) >= 0;
        if (!cfg.hasBorder && !tblBorderNone)
        {
            foreach (var cssSrc in new[] { cfg.css, cfg.docCss })
            {
                if (cssSrc is null || cfg.hasBorder) continue;
                foreach (var sel in new[] { "td", "table" })
                {
                    if (!cssSrc.TryGetValue(sel, out var d0)) continue;
                    // The SHORTHAND spelling of the same rule (`td { border: 1px
                    // solid #000 }`) boxes the cells identically — it stays one
                    // key in the rule map (no longhand expansion), so it is its
                    // own arm here (element-grid dialect only).
                    if (cfg.docElementGrid && d0.TryGetValue("border", out var bshv)
                        && ChainBorder(bshv) is { } bshi)
                    {
                        cfg.hasBorder = true;
                        cfg.elemRuleBorder = true;
                        cfg.borderWidth = bshi.Width;
                        cfg.borderColor = bshi.Color is { } bshc ? bshc : cfg.borderColor;
                        if (cfg.pad <= 0 && d0.TryGetValue("padding", out var bshPad)
                            && TryParseLength(bshPad) is { } bshPadPt)
                            cfg.pad = bshPadPt;
                        break;
                    }
                    if (!d0.TryGetValue("border-style", out var bsv)
                        || bsv.IndexOf("solid", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    cfg.hasBorder = true;
                    double maxPx = 1;
                    foreach (var src2 in new[] { cfg.css, cfg.docCss })
                        if (src2 is not null && src2.TryGetValue("td", out var dtd2)
                            && dtd2.TryGetValue("border-width", out var bwv))
                            foreach (Match mm in Regex.Matches(bwv, @"([\d.]+)\s*(px)?"))
                                if (double.TryParse(mm.Groups[1].Value,
                                        System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture, out var bwp)
                                    && bwp > 0 && bwp < 10)
                                    maxPx = Math.Max(maxPx, bwp);
                    cfg.borderWidth = maxPx * PxToPt;
                    foreach (var src2 in new[] { cfg.css, cfg.docCss })
                        if (src2 is not null)
                            foreach (var sel2 in new[] { "td", "table" })
                                if (src2.TryGetValue(sel2, out var d2)
                                    && d2.TryGetValue("border-color", out var bcv)
                                    && ParseCssColor(bcv) is { } bcc)
                                { cfg.borderColor = bcc; break; }
                    if (cfg.pad <= 0)
                        foreach (var src2 in new[] { cfg.css, cfg.docCss })
                            if (src2 is not null && src2.TryGetValue("td", out var dtp)
                                && dtp.TryGetValue("padding", out var pdv) && TryParseLength(pdv) is { } pdp)
                                cfg.pad = pdp;
                    break;
                }
            }
        }
    }

    /// <summary>The run face the document carries in dresses the cells when the sheet names none of its own.</summary>
    private static string? ApplyRunFaceToCells(TableStyleConfig cfg, string? cssRunFace, string html)
    {
        // one em per glyph and every column comes out ~4 pt WIDER, not the ~1.4 pt
        // narrower the real metrics would give. Left on the Standard-14 stand-in.
        if (cssRunFace is not null)
        {
            var mixed = false;
            foreach (Match rt in Regex.Matches(html, @"<(?:span|font|p)\b[^>]*\bclass\s*=\s*[""']([^""']*)[""']",
                         RegexOptions.IgnoreCase))
            {
                foreach (var rc in rt.Groups[1].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                {
                    var rk = "." + rc;
                    if ((!cfg.css.TryGetValue(rk, out var rcd)
                            && (cfg.docCss is null || !cfg.docCss.TryGetValue(rk, out rcd)))
                        || !rcd.TryGetValue("font-size", out var rfs)
                        || TryParseLength(rfs) is not { } rfsp || rfsp <= 0
                        || Math.Abs(rfsp - cfg.cellFontSize) < 0.01) continue;
                    mixed = true;
                    break;
                }
                if (mixed) break;
            }
            if (!mixed) cssRunFace = null;
        }
        return cssRunFace;
    }
}
