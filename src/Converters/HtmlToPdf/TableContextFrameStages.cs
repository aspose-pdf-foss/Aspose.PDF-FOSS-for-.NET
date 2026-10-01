using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>A table with no border of its own takes one from the sheet: the element rules, the pinned body grid and the UA cell boxes each state a frame.</summary>
    private static void ResolveTableFrameFromSheet(TableStyleConfig cfg, TableColumnModel colModel, string html)
    {

        // A LONGHAND border triplet (`table, td { border-style: solid; border-color:
        // #333 }` + `td { border-width: 1px }`) boxes the cells like the shorthand —
        // form documents commonly split the declaration across document-level rules.
        // A table whose own style says border-style:none opts out.
        ResolveElementRuleFrame(cfg);

        // The pinned-body report's grid lines: the TABLE's own bgcolor shows
        // through a 1-2px cellspacing between white cells — every cell reads
        // as a thin box in the table's colour. Drawn as the cell border it
        // visually is (dialect-gated; the legacy paths never read table bg).
        if (!cfg.hasBorder && cfg.pinnedBodyGrid && cfg.tblTag.Success
            && cfg.tblCellSpacingDeclared && colModel.tblCellSpacingPt is > 0 and <= 1.6
            && Regex.Match(cfg.tblTag.Value, @"\bbgcolor\s*=\s*[""']?([^""'\s>]+)",
                RegexOptions.IgnoreCase) is { Success: true } tbgm
            && ParseCssColor(tbgm.Groups[1].Value) is { } tbgc)
        {
            cfg.hasBorder = true;
            cfg.borderWidth = colModel.tblCellSpacingPt;
            cfg.borderColor = tbgc;
            // The spacing line rides INSIDE the row pitch (a 22px row
            // = the line box + the cellpadding pair + the 1px band) — the border
            // this arm draws must not grow the row, so the vertical padding
            // yields the border's width back.
            cfg.pad = Math.Max(0, cfg.pad - cfg.borderWidth);
        }

        // Cells styled inline (`<td style="…border: #000 1px solid…">`) draw a cell box just
        // like a `td { border: … }` stylesheet rule — CMS/spreadsheet exports style each cell
        // inline and have no <style> block at all. Sample the first bordered cell.
        // A Word grid declares every cell's sides on the cell itself (the mail
        // arm reads them one cell at a time), so its sample boxes nothing table-wide.
        if (!cfg.hasBorder && !cfg.wordMailCells)
        {
            // (…sampling this grid's OWN cells: a bordered cell of a grid nested inside one of its cells is that
            //  grid's - measured on the mailing, whose 600 px content grid boxed every cell for a button deep inside)
            var cbm = FirstOwnCellStyleBorder(html);
            if (cbm.Success)
            {
                var bd = cbm.Groups[1].Value.Trim();
                if (!bd.StartsWith("0") && bd.IndexOf("none", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    cfg.hasBorder = true;
                    var wm = Regex.Match(bd, @"(\d+(?:\.\d+)?)\s*px");
                    if (wm.Success && double.TryParse(wm.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var bw) && bw > 0)
                        cfg.borderWidth = bw * PxToPt;
                    var bc = ParseCssColor(bd); if (bc is not null) cfg.borderColor = bc;
                }
            }
        }
    }

    /// <summary>The grid's border and padding defaults: the tag's own attributes, the sheet's cell rules and the class rules that dress them.</summary>
    private static void ResolveBorderAndPaddingDefaults(TableStyleConfig cfg, TableParseState ps, string? cssRunFace, string html)
    {
        cfg.hasBorder = false;
        cfg.borderWidth = 1;
        cfg.borderColor = Color.Black;
        cfg.pad = 0;
        cfg.elemRuleBorder = false;
        cfg.padSide = -1;
        cfg.padBottom = -1;
        // border="1"/"1px" attribute on the table draws a 1px box on every cell.
        if (cfg.tblBorderAttr is not null && !cfg.tblBorderAttr.StartsWith("0"))
        {
            cfg.hasBorder = true;
            var wm = Regex.Match(cfg.tblBorderAttr, @"(\d+(?:\.\d+)?)");
            if (wm.Success && double.TryParse(wm.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var bwa) && bwa > 0)
                cfg.borderWidth = bwa * PxToPt;
            // The legacy BORDERCOLOR attribute colours the grid (form-grid dialect
            // only — the calibrated dialects keep their black default).
            if (cfg.formGridDialect && cfg.tblBorderColorAttr is not null
                && ParseCssColor(cfg.tblBorderColorAttr) is { } bcaCol)
                cfg.borderColor = bcaCol;
        }
        if (cfg.tblCellPadAttr is not null && double.TryParse(Regex.Match(cfg.tblCellPadAttr, @"[\d.]+").Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var cpa) && cpa > 0)
            cfg.pad = cpa * PxToPt;
        // The document sheet's cell padding outranks the presentational attribute, as the
        // metric writer already reads it (`th,td { padding: 15px }` over cellpadding=30, measured).
        if (cfg.tblCellPadAttr is not null && cfg.docCss is not null && SheetCellPaddingPt(cfg.docCss) is >= 0 and var sheetCellPad)
            cfg.pad = sheetCellPad;
        // The reset-sheet grid: the sheet zeroes every margin and boxes its cells by rule - only then do
        ApplySheetCellPadding(cfg, ps, html);
        // The page sheet's paragraph margin (`p { margin: 10px 0 }`): the cell's paragraphs keep their
        // own lines, each opening that far below the previous one.
        foreach (var pSrc in new[] { cfg.css, cfg.docCss })
        {
            if (!ps.resetSheetGrid || pSrc is null || !pSrc.TryGetValue("p", out var pRule)) continue;
            if (pRule.TryGetValue("margin-top", out var pmt) && TryParseLength(pmt) is { } pmtPt) ps.sheetPMarginTopPt = pmtPt;
            else if (pRule.TryGetValue("margin", out var pm)) ps.sheetPMarginTopPt = ChainPadPt(pm.Trim(), cfg.cellFontSize).T;
            break;
        }
        // Cell-qualified class rules — ".cls td" (from a ".cls > tbody > tr > td" chain) and
        // "td.cls" — name the CELL, so unlike a bare ".cls" border they DO box every cell and
        // pad its text. Editor-generated table styles carry their grid this way.
        foreach (var d in new[] { TableOwnClassRule(cfg, cfg.tblClasses, " td"), TableOwnClassRule(cfg, cfg.tblClasses, " th"), TableOwnCellClassRule(cfg, cfg.tblClasses, "td"), TableOwnCellClassRule(cfg, cfg.tblClasses, "th") })
        {
            if (d is null) continue;
            if (d.TryGetValue("border", out var cbd))
            {
                var t = cbd.Trim();
                cfg.hasBorder = !t.StartsWith("0") && t.IndexOf("none", StringComparison.OrdinalIgnoreCase) < 0;
                var wm = Regex.Match(cbd, @"(\d+(?:\.\d+)?)\s*px");
                if (wm.Success && double.TryParse(wm.Groups[1].Value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var cbw))
                    cfg.borderWidth = cbw * PxToPt;
                var cbc = ParseCssColor(cbd); if (cbc is not null) cfg.borderColor = cbc;
            }
            // `padding: 7px 5px 6px` — the shorthand's TOP value seeds the vertical
            // inset the row height is measured with, and its SIDE value (the second
            // entry, or the first when the shorthand is a single length) the horizontal
            // one the column footprint is measured with.
            if (d.TryGetValue("padding", out var cpv))
            {
                var cps = cpv.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (cps.Length > 0 && TryParseLength(cps[0]) is { } cpp)
                {
                    cfg.pad = cpp;
                    cfg.padSide = cps.Length > 1 && TryParseLength(cps[1]) is { } cpsv ? cpsv : cpp;
                    // The bottom entry only under the CSS run dialect: the legacy grid is
                    // calibrated against the top value standing in for both.
                    cfg.padBottom = cssRunFace is not null && cps.Length > 2
                        && TryParseLength(cps[2]) is { } cpbv ? cpbv : cpp;
                }
            }
        }
        // A `border: Npx …` declaration on the <table> tag's own style boxes every cell
        // like the border attribute; cell text then insets by the stroke width plus
        // the UA-default 1px cell padding. The stroke share of that inset comes from
        // the bordered cell box itself, so only the UA padding is added here.
        if (!cfg.hasBorder && cfg.tblStyle.TryGetValue("border", out var tbstv))
        {
            var tb = tbstv.Trim();
            if (!tb.StartsWith("0") && tb.IndexOf("none", StringComparison.OrdinalIgnoreCase) < 0)
            {
                cfg.hasBorder = true;
                var wm = Regex.Match(tb, @"(\d+(?:\.\d+)?)\s*px");
                if (wm.Success && double.TryParse(wm.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var tbw) && tbw > 0)
                    cfg.borderWidth = tbw * PxToPt;
                var tbc = ParseCssColor(tb); if (tbc is not null) cfg.borderColor = tbc;
                if (cfg.pad <= 0) cfg.pad = 1 * PxToPt;
            }
        }
    }
}
