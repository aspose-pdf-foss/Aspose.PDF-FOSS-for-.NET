using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The page-widening dialects the pinned body, the element table, the form document and the scaled sheet leave undecided: the collapse grid's declared width, the fieldset and RTL sheets, the widest table past the page box and the over-constrained grid.</summary>
    private static void WidenPageForRemainingTableDialects(ConvertState cv, HtmlLoadOptions? options)
    {
        if ((cv.profile.uaStdSerif || cv.collapseTableW > cv.widestTable)
            && cv.collapseTableW > cv.availContentW
            && !(cv.pageInfo?.WidthAssigned ?? false))
        {
            // A collapse grid's DECLARED width sizes the sheet exactly (probed:
            // the widest width:491.4pt collapse table grows the page to
            // 96 + 491.4 + 90 with no slack) — on the legacy path too, whenever
            // the declaration exceeds every probed natural width.
            cv.pageHeight = Math.Max(cv.pageWidth, cv.pageHeight);
            cv.pageWidth = cv.marginLeft + cv.collapseTableW + 90.0;
        }
        // Fieldset worksheet: the page grows to the widest declared table plus
        // the whole left chrome chain and the frame's right pad (probed:
        // 90 + 39 + 9.75 + 450 + 8.25 + 90.75 = 687.75).
        else if (cv.profile.uaStdSerif && cv.fieldsetDoc && cv.declaredTableW > 0
            && FieldsetWorksheetSheetPt(cv) > cv.pageWidth && !(cv.pageInfo?.WidthAssigned ?? false))
        {
            cv.pageHeight = Math.Max(cv.pageWidth, cv.pageHeight);
            cv.pageWidth = FieldsetWorksheetSheetPt(cv);
        }
        // RTL attribute-grid sheet: the page grows to the widest DECLARED table
        // between the 90 pt page margin (its left edge) and the RTL right inset
        // the grids anchor against (measured: 90 + 600 + 91.78 = 781.78).
        else if (cv.profile.uaStdSerif && cv.profile.rtlDoc && cv.declaredTableW > 0
            && 90.0 + cv.declaredTableW + RtlGridRightInsetPt > cv.pageWidth
            && !(cv.pageInfo?.WidthAssigned ?? false))
        {
            cv.pageHeight = Math.Max(cv.pageWidth, cv.pageHeight);
            cv.pageWidth = 90.0 + cv.declaredTableW + RtlGridRightInsetPt;
        }
        // A FLOAT-HEADER document keeps its sheet and CLIPS a wide table instead of
        // growing to it: the certificate page declares a 720 px grid on a 595 pt A4
        // sheet and the page is left alone, its text ending at 505 where
        // the content box does. Widening it there moved every float and heading with it.
        else if (WidestTablePastPageBox(cv) && !(cv.pageInfo?.WidthAssigned ?? false) && !cv.profile.floatBothSidesDoc)
        {
            WidenPageToWidestTable(cv, options);
        }
        // An over-constrained UA grid - the declared boxes of its nested grids wider than the
        // content box - keeps its columns and grows the sheet to its ink: one page margin past the
        // furthest ink its min-floor layout draws (probed on the Words letter: a 341.25 pt address
        // grid, a 3 pt nbsp spacer and a 150 pt block whose ruled 126 pt grid inks 127.5 page
        // 96 + 471.75 + 90 = 657.75 - not the 680.25 the 150 pt box would make, and not A4).
        else WidenPageToOverConstrainedGrid(cv, options);
    }

    /// <summary>A page too narrow for what the tables need grows to it, keeping the layout engine's portrait rule and settling the right margin by dialect.</summary>
    private static void GrowPageToNeededWidth(ConvertState cv, double neededPage, double widenRight, bool inkWiden, bool chainWiden, bool declaredInkSheet)
    {
        if (neededPage > cv.pageWidth && !cv.profile.escapedAttrDoc)
        {
            // The layout engine keeps the page's larger (portrait-height) dimension as the
            // height and widens the width to fit the table, rather than the swapped landscape
            // short edge — so a wide table on an A4 page lands ~1129 × 842, not 1129 × 595.
            cv.pageHeight = Math.Max(cv.pageWidth, cv.pageHeight);
            cv.pageWidth = neededPage;
            // A pre-grown grid's sheet opens at the UA top (72 + the body
            // margin) — the legacy calibrated top was measured on flows
            // this dialect never rides (probed: the first header row's
            // baseline sits 91.2 from the page top).
            if (cv.preGrownGridDoc) cv.marginTop = 72.0 + UaBodyMarginPt;
            if (chainWiden)
            {
                cv.marginLeft = 90.0 + cv.bodyMarginLeftPt;
                cv.marginRight = widenRight;
            }
            // The body's own right margin still insets the content box, mirroring
            // the left — the page margin alone sized the sheet. The standard-serif flow
            // charges that inset on the right of every text block already, so the sheet
            // its percent grid's min floors grew keeps the page margin alone (measured:
            // the list text on the 694.18 sheet wraps at 598.18 = W − 96).
            // (…and so does a sheet grown to a cell-rule grid's min-content: the page margin alone
            //  stands to the right of it - the nest is measured from that edge)
            else if (inkWiden)
                cv.marginRight = widenRight
                    + ((cv.profile.uaStdSerif && !cv.profile.deadExternalCss && cv.widestIsPctMin)
                        || cv.profile.docChainCellRulesOnly ? 0.0 : UaBodyMarginPt);
            // (a sheet grown to a table's text ink keeps the symmetric body inset on its right: the
            // safety data sheet's centred code seats on a 96..579.31 box)
            // (…the body's own right margin where its tag states one)
            else if (declaredInkSheet)
                cv.marginRight = widenRight + cv.bodyMarginRightPt;
        }
    }
}
