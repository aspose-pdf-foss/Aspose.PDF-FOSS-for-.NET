using System.Text;
using System.Text.RegularExpressions;
using CellLineSpec = (string Text, double FontPt, string? Family, bool Keep, bool JoinNext, System.Collections.Generic.List<(string Text, string Url)>? Anchors, bool Bold, double MarginTopPt, double MarginLeftPt, Aspose.Pdf.Color? Color, bool Italic);

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Each line of the closing cell contributes its drawn width and its breakable minimum to the cell's extents.</summary>
    private static void MeasureCellLineWidths(CloseCellState cc)
    {
        foreach (var spec in cc.ps.lines)
        {
            var ln = spec.Text;
            // The probe measures each line with the styles it renders with: its own
            // font size (a 9pt header row in a 10pt table measures at 9), real bold
            // metrics for an all-bold line, and its own family.
            // …and so does the CSS run dialect, whose column floors must hold the
            // widest token AT ITS OWN SIZE (a 24 pt run in a 10 pt table).
            var mPt = (cc.widenProbe || cc.cssRunFace is not null || cc.chainBase is not null
                || cc.ptCellWidths || cc.dwFormCells) && spec.FontPt > 0 ? spec.FontPt
                // (a NOWRAP cell's whole line measures at its class size: the 12 pt stand-in would
                // floor the grid for a line half again as wide as the one it draws)
                // …and a line that states no size of its own measures at the size its CELL's class
                // gives it: the stand-in default would floor the column for text a size it never draws.
                : (cc.ps.cell!.HtmlNoWrap || (cc.ps.sheetTdBoxRule)) && cc.ps.cellClassPt > 0 ? cc.ps.cellClassPt : 0.0;
            // (…and a line that states no weight of its own is measured in the weight its CELL's class
            //  gives it - a bold label measured light floors its column for text it never draws)
            var mBold = cc.ps.isHeader || ((cc.widenProbe || cc.chainBase is not null || cc.ptCellWidths) && spec.Bold)
                || (cc.ps.sheetTdBoxRule && cc.ps.cellBold);
            var mFam = cc.widenProbe ? spec.Family
                : cc.ptCellWidths || cc.dwFormCells || cc.ps.uaControlGrid ? spec.Family ?? cc.ps.cellFamily : null;
            // pt-styled fragment: the paragraph's own margins ride the cell's
            // min-content footprint (probed: the squeeze sheds almost nothing
            // from the header column whose word + pads + margins ≈ its box).
            var mMargins = cc.ptCellWidths
                ? spec.MarginLeftPt + cc.ps.cellPMarginRightPt + cc.ps.cellCssPadPt : 0;
            // A cell whose class `font:` shorthand names an installed face measures in that face at
            // that size - the stand-in face would floor the column for a line nothing draws.
            if (cc.ps.cellClassFamily is { } clsFam && cc.ps.cellClassPt > 0)
            {
                var (clsMin, clsMax) = MeasureClassFaceExtents(clsFam, cc.ps.cellClassPt, ln);
                cc.cellMin = Math.Max(cc.cellMin, clsMin + mMargins);
                cc.cellMinBrk = Math.Max(cc.cellMinBrk, clsMin + mMargins);
                cc.cellMax = Math.Max(cc.cellMax, clsMax);
                continue;
            }
            cc.cellMin = Math.Max(cc.cellMin, MeasureMinContent(cc.ps, cc.options, cc.cellFontSize, cc.dwFormCells, cc.fullWidthCjkMin, cc.widenProbe, ln, mBold, mPt, mFam) + mMargins);
            cc.cellMinBrk = Math.Max(cc.cellMinBrk, MeasureMinContent(cc.ps, cc.options, cc.cellFontSize, cc.dwFormCells, cc.fullWidthCjkMin, cc.widenProbe, ln, mBold, mPt, mFam, breakDashes: true) + mMargins);
            cc.cellMax = Math.Max(cc.cellMax, MeasureLine(cc.ps, cc.options, cc.cellFontSize, cc.dwFormCells, cc.fullWidthCjkMin, cc.widenProbe, ln, mBold, mPt, mFam));
            // A header cell's full (unwrapped) line width — used to keep <th> on one line when
            // the whole table still fits the available width (a browser does not wrap headers to
            // their widest word). Recorded separately so it never forces the page/table wider.
            if (cc.ps.isHeader) cc.cellHdr = Math.Max(cc.cellHdr, MeasureLine(cc.ps, cc.options, cc.cellFontSize, cc.dwFormCells, cc.fullWidthCjkMin, cc.widenProbe, ln, bold: true));
        }
    }

    /// <summary>A cell holding tables of its own takes their natural width as its floor, by the dialect's rule for the chrome around them.</summary>
    private static void ApplyPendingCellTableFloor(CloseCellState cc)
    {
        if (cc.ps.pendingCellTablesNatW > 0)
        {
            // (the host's true min-content counts the nested grid at ITS min-content: the floor a
            //  declared host box cannot cap below - probed on the e-mail statement, whose 600 px
            //  box holds sixteen 40 px boxes that yield to their ink)
            cc.cellMinFloor = Math.Max(cc.cellMin, cc.ps.pendingCellTablesMinW);
            // Break-anywhere sheet: the nested grid's box-filling natural
            // width is NOT a floor on its host — every token inside it can
            // break, so the grid shrinks with its column (its 100% width is
            // 100% OF THAT COLUMN). It stays the cell's preference (cellMax).
            // A UA-boxed host cell sizes on the nested grid's INTRINSIC widths: its min-content
            // is the floor and its max-content the preference - a `width="100%"` grid's box is a
            // percent of THIS cell, no size of its own (measured: a two-column wrapper whose first
            // cell holds a 100% grid splits at the grids' max-contents plus the surplus in proportion,
            // 287.7 / 162.3 of 450).
            if ((cc.uaCellBoxes || (cc.ps.sheetTdBoxRule)) && cc.ps.pendingCellTablesMaxW > 0)
            {
                cc.cellMin = Math.Max(cc.cellMin, cc.ps.pendingCellTablesMinW);
                cc.cellMinBrk = Math.Max(cc.cellMinBrk, cc.ps.pendingCellTablesMinW);
                cc.cellMax = Math.Max(cc.cellMax, cc.ps.pendingCellTablesMaxW);
                if (cc.ps.pendingCellTablesDeclW > 0)
                    cc.colModel.hardMinPt = Math.Max(cc.colModel.hardMinPt, cc.ps.pendingCellTablesDeclW + cc.ps.cellCssPadPt + cc.ps.cellShortPadPt);
            }
            else if (!cc.breakAnywhereDoc)
            {
                cc.cellMin = Math.Max(cc.cellMin, cc.ps.pendingCellTablesNatW);
                cc.cellMinBrk = Math.Max(cc.cellMinBrk, cc.ps.pendingCellTablesNatW);
                // (…and a nested grid's DECLARED box is a HARD floor on the host grid: a host box
                // narrower than that box and its host pads overflows to them - probed on the e-mail statement, whose
                // 600 px class box holds a 600 px grid in 15 px pads and inks 22.5 pt past its box)
                if (cc.ps.pendingCellTablesDeclW > 0)
                    cc.colModel.hardMinPt = Math.Max(cc.colModel.hardMinPt, cc.ps.pendingCellTablesDeclW + cc.ps.cellCssPadPt + cc.ps.cellShortPadPt);
            }
            // The cell WANTS the grid's preferred (max-content) width — a
            // percent grid's natural is only its min floors, and sizing the
            // column off that leaves the grid squeezed below its due
            // width.
            if (!(cc.uaCellBoxes && cc.ps.pendingCellTablesMaxW > 0))
                cc.cellMax = Math.Max(cc.cellMax,
                    Math.Max(cc.ps.pendingCellTablesNatW, cc.ps.pendingCellTablesPrefW));
            cc.ps.pendingCellTablesMaxW = 0;
            cc.ps.pendingCellTablesNatW = 0;
            cc.ps.pendingCellTablesPrefW = 0;
            cc.ps.pendingCellTablesDeclW = 0;
            cc.ps.pendingCellTablesMinW = 0;
        }
    }
}
