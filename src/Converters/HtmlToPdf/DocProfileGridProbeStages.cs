using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>What the probe built says about the document: an over-declared or pre-grown grid, the dialect floors its cells carry, and the widest table the sheet must hold.</summary>
    private static void ReadProbedGridIntoProfile(Table probedTable, double natW, Block b, ConvertState cv, HtmlDocProfile profile, double availContentW, HtmlLoadOptions? options)
    {
        if (probedTable.HtmlOverDeclaredGrid) profile.overDeclaredGridDoc = true;
        if (probedTable.HtmlPreGrownGrid) cv.preGrownGridDoc = true;
        // A UA table declaring an absolute width narrower than its columns' natural sum keeps
        // that width - its over-declared columns yield inside it - so the sheet is not grown to
        // columns the grid never draws (measured on the valuation report: 656 px of columns in
        // a 640 px table leave the page at the framing div's 672.75).
        // (…unless its cells cannot wrap: a nowrap grid keeps its content and the sheet grows
        // to it - probed: a `width:670px; table-layout:fixed; white-space:nowrap` table pages 871.25)
        if (profile.uaStdSerif && !profile.deadExternalCss
            && Regex.Match(b.TableHtml ?? "", @"<table\b[^>]*>", RegexOptions.IgnoreCase) is { Success: true } natTag
            && !Regex.IsMatch(natTag.Value, @"white-space\s*:\s*(nowrap|pre(?![-\w]))", RegexOptions.IgnoreCase)
            && DeclaredTableWidthPt(natTag.Value) is var natDeclared && natDeclared > 0 && natW > natDeclared)
            natW = natDeclared;
        // MEASURED (the no-doctype evaluation form, quirks table font reset + ink sheet): a UA grid
        // declaring no width of its own lays its width-ATTRIBUTE columns out inside the box it
        // has - the attributes are max-content hints an auto table shrinks below - so its probed
        // natural width never grows the sheet (the form's 720 px attribute grids leave the sheet
        // at its 7in table's ink, and alone leave it A4; the grown sheet re-lays them at 429.57).
        // (…never below the grid's MIN-content: a nowrap grid's columns are their whole text - the
        //  sheet grows to it, as the calibrated nowrap greens have it)
        if (profile.uaStdSerif && !profile.deadExternalCss && _quirksRowStrut && natW > availContentW
            // (a natural width the probe already floored at min-content - a nowrap grid's - stands)
            && !probedTable.HtmlPctMinNatural && !profile.uaNoWrapCellRule
            && Regex.Match(b.TableHtml ?? "", @"<table\b[^>]*>", RegexOptions.IgnoreCase) is { Success: true } attrTag
            && DeclaredTableWidthPt(attrTag.Value) <= 0 && AttributeColumnsOnly(b.TableHtml ?? ""))
            natW = Math.Max(availContentW, MetricTableMinContentPt(cv, options, b.TableHtml ?? "", availContentW));
        // MEASURED (the change-control print page, min-content sheet): an auto grid whose MIN-content
        // exceeds the box it has cannot shrink into it - it lays out at min-content, overflowing, and
        // the sheet ends one page margin past it: 99.8 + 580.252 + 90. Its natural width for the
        // widen is that min-content, past the fields box's inset it stands in.
        if (profile.fieldListDoc && b.TableHtml is { Length: > 0 } flHtml
            && MetricTableMinContentPt(cv, options, flHtml, availContentW) is var flMin
            && flMin > availContentW - UaBodyMarginPt - 2 * profile.fieldsInsetPt)
            natW = Math.Max(natW, profile.fieldsInsetPt + flMin);
        // MEASURED (the helpdesk request form, pt form grid): a grid whose MIN-content is wider than
        // the body lays out at min-content, uncentred, and the sheet ends one page margin past it
        // (96 + 550.609 + 90 for the request grid).
        if (profile.ptFormDoc && b.TableHtml is { Length: > 0 } pfHtml
            && PtFormTableMinContentPt(cv, options, pfHtml, availContentW) is var pfMin && pfMin > availContentW)
            natW = Math.Max(natW, pfMin);
        // MEASURED (the Word mail's 1289 pt grid): a Word grid's natural width for the widen is the
        // width of its PAINTED columns - the sheet ends one page margin past the last cell border,
        // and the bare columns declared past it are laid out off the sheet (773.27, not 1469).
        if (profile.wordMailDoc && b.TableHtml is { Length: > 0 } wgHtml
            && WordGridPaintedWidthPt(wgHtml, 0) is var wgPainted && wgPainted > 0 && wgPainted < natW)
            natW = wgPainted;
        if (natW > cv.widestTable)
        {
            cv.widestTable = natW;
            cv.widestIsPctMin = probedTable.HtmlPctMinNatural;
            cv.widestIsRowDemand = probedTable.HtmlRowDemandNatural;
            // (a phantom column - a colspan past the cells - is no column of the ink)
            var inkCols = 0;
            foreach (var cw in (probedTable.ColumnWidths ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (double.TryParse(cw.TrimEnd('%'), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var cwv) && cwv > 0) inkCols++;
            (cv.widestTableChromePt, cv.widestTableColGapPt, cv.widestTableTrailPt) = TableInkChromePt(b.TableHtml ?? "", cv.css);
            cv.widestTableCols = inkCols;
            cv.widestTableTrailingPt = probedTable.HtmlMinFloorTrailingPt;
            profile.minFloorSheet = probedTable.HtmlPctMinNatural;
        }
    }
}
