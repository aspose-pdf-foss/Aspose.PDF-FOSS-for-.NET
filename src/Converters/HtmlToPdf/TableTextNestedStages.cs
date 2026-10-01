using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The nested table's natural, preferred, minimum and declared widths fold into the pending measurements of the cell that holds it.</summary>
    private static void FoldNestedTableWidthsIntoCell(TableStyleConfig cfg, TableParseState ps, Table inner, double innerNatW, double innerAvailW)
    {
        var capOut = 2 * inner.HtmlCapsuleOutsetHPt;
        // The host column's min EXCLUDES the widget reserve the
        // grid still draws — the grid overflows its box by it.
        var innerHostW = innerNatW + capOut
            - (cfg.dwFormCells ? inner.HtmlDwGapReservePt : 0);
        // A grid that FILLS its box (its natural width was capped AT the box) can
        // still shrink: its host's floor is the grid's min-content, not the box
        // (measured: a letter's outer grid holds a reasons grid whose long lines
        // fill the cell - the sheet stays A4, the box does not grow by the chrome).
        // A grid PAST the box overflowed it with content that could not break and
        // keeps its natural (measured: the CJK statement's sheet still grows to it).
        if (innerAvailW > 0 && Math.Abs(innerNatW - innerAvailW) < 1e-6
            && inner.HtmlMinContentPt > 0 && inner.HtmlMinContentPt + capOut < innerHostW)
            innerHostW = inner.HtmlMinContentPt + capOut;
        // (a UA-boxed host: a percent-declared grid's box-filling natural is no floor at all - the
        //  host takes its min-content, an empty value grid nothing)
        if (cfg.uaCellBoxes && !inner.HtmlDeclaredBoxAbs)
            innerHostW = inner.HtmlMinContentPt + capOut;
        if (innerHostW > ps.pendingCellTablesNatW)
            ps.pendingCellTablesNatW = innerHostW;
        if (inner.HtmlPreferredWidthPt + capOut > ps.pendingCellTablesPrefW)
            ps.pendingCellTablesPrefW = inner.HtmlPreferredWidthPt + capOut;
        var innerMinW = (inner.HtmlMinContentPt > 0 ? Math.Min(inner.HtmlMinContentPt, innerNatW)
            // (a UA-boxed host takes an EMPTY percent grid at nothing - its box-filling natural is no content)
            : cfg.uaCellBoxes && !inner.HtmlDeclaredBoxAbs ? 0 : innerNatW) + capOut;
        if (innerMinW > ps.pendingCellTablesMinW) ps.pendingCellTablesMinW = innerMinW;
        // A grid whose host measures on the DECLARED cell box takes the grid's own intrinsic
        // widths: its min-content floors the host and its max-content is what the host prefers,
        // exactly as a UA-boxed host reads them.
        if (ps.sheetTdBoxRule && !cfg.uaCellBoxes)
        {
            innerHostW = inner.HtmlMinContentPt + capOut;
            if (inner.HtmlMaxContentPt + capOut > ps.pendingCellTablesMaxW)
                ps.pendingCellTablesMaxW = inner.HtmlMaxContentPt + capOut;
        }
        if (cfg.uaCellBoxes)
        {
            // (a nested grid's DECLARED absolute box is its min-content too: a 600 px grid never
            //  shrinks below 450, and the percent columns beside its host cell yield to it)
            // (…an ABSOLUTE box: a percent of the host is no intrinsic size - a `width=100%` value grid
            //  floors its host at its own min-content only, or every such cell demands the whole box)
            var innerDeclAbs = inner.HtmlDeclaredBoxAbs ? inner.HtmlDeclaredBoxPt : 0;
            var innerBoxMin = Math.Max(inner.HtmlMinContentPt, innerDeclAbs) + capOut;
            if (innerBoxMin > ps.pendingCellTablesMinW) ps.pendingCellTablesMinW = innerBoxMin;
            var innerBoxMax = Math.Max(inner.HtmlMaxContentPt, innerDeclAbs) + capOut;
            if (innerBoxMax > ps.pendingCellTablesMaxW) ps.pendingCellTablesMaxW = innerBoxMax;
        }
        if (inner.HtmlDeclaredBoxPt + capOut > ps.pendingCellTablesDeclW
            && (inner.HtmlDeclaredBoxAbs || !cfg.uaCellBoxes))
            ps.pendingCellTablesDeclW = inner.HtmlDeclaredBoxPt + capOut;
    }

    /// <summary>The nested table takes the host's face and list indent, closes the line above it and joins the cell's pending tables at the line it was found on.</summary>
    private static void SeatNestedTableInCellFlow(TableStyleConfig cfg, TableParseState ps, Table table, Table inner, int ni)
    {
        // …and draws its cells in the document face the host draws in, through the
        // same Type0 path (a nested grid is the same document).
        if (cfg.uaCellBoxes) inner.HonorCellTtfFaces = true;
        // The over-declared fingerprint bubbles to the outer table:
        // the width probe only sees top-level segments.
        if (inner.HtmlOverDeclaredGrid) table.HtmlOverDeclaredGrid = true;
        PushLine(ps, cfg.redlineCells, cfg.dwFormCells, cfg.widenProbe);
        // The grid's own CSS `margin-top` is real space above it in
        // the host cell (`<table style="…margin-top:35px">` — the
        // columns section clears its heading by exactly that band).
        if (cfg.liftNestedTables
            && Regex.Match(cfg.nestedHtml[ni], @"<table\b[^>]*>",
                RegexOptions.IgnoreCase) is { Success: true } inTag
            && Regex.Match(inTag.Value,
                @"(?<![-\w])margin-top\s*:\s*([\d.]+)\s*px",
                RegexOptions.IgnoreCase) is { Success: true } inMt
            && double.TryParse(inMt.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var inMtPx)
            && inMtPx > 0)
            inner.HtmlMarginTopPt = inMtPx * PxToPt;
        // The block margin left pending above a nested grid collapses with the grid's own
        // top margin, the larger standing (measured on the job status band: the enclosing
        // <p>'s 6.72 over the painted div's 3.75 - one 6.72 above the band).
        if (ps.uaCellBoxes && ps.uaPendingMarginPt > 0)
        {
            inner.HtmlMarginTopPt = Math.Max(inner.HtmlMarginTopPt, ps.uaPendingMarginPt);
            ps.uaPendingMarginPt = 0;
        }
        // ...and its own bottom margin is space under it in the host cell.
        if (ps.uaCellBoxes
            && Regex.Match(cfg.nestedHtml[ni], @"<table\b[^>]*>", RegexOptions.IgnoreCase) is { Success: true } inTag2
            && Regex.Match(inTag2.Value, @"style\s*=\s*[""']([^""']*)[""']", RegexOptions.IgnoreCase) is { Success: true } inSt2
            && CssBoxSidePt(inSt2.Groups[1].Value, "margin", "margin-bottom", 2,
                ps.curFontPt > 0 ? ps.curFontPt : ps.uaBaseFontPt) is { } inMb && inMb > 0)
            inner.HtmlMarginBottomPt = inMb;
        // The rounded capsule the enclosing div declared paints
        // behind this grid.
        if (ps.pendingCapsule is { } cap)
        {
            inner.HtmlCapsuleFill = cap.Fill;
            inner.HtmlCapsuleRadiusPt = cap.RadiusPt;
            inner.HtmlCapsulePadHPt = cap.PadHPt;
            inner.HtmlCapsulePadVPt = cap.PadVPt;
            inner.HtmlCapsuleMarginPt = cap.MarginPt;
            ps.pendingCapsule = null;
        }
        // A grid inside a list item sits ON the item's standing
        // indent, like every other line of the item.
        inner.HtmlListIndentPt = ps.liStandingIndentPt;
        // DataWorks results grid: inner rows pitch on the 1.125-em
        // line box while the grid total keeps the 16-per-row model
        // (the last row absorbs the slack) — see DwNestedRowPitchPt.
        if (cfg.dwFormCells && inner.HtmlDwGapReservePt > 0
            && inner.Rows.Count > 1)
        {
            var nR = inner.Rows.Count;
            for (var ri = 0; ri < nR - 1; ri++)
                inner.Rows[ri].MinRowHeight = DwNestedRowPitchPt;
            inner.Rows[nR - 1].MinRowHeight =
                DwCheckboxRowHPt * nR - DwNestedRowPitchPt * (nR - 1);
        }
        (ps.pendingCellTables ??= new List<(Table, int)>()).Add((inner, ps.lines.Count));
        // The nested grid's natural width IS this cell's content
        // width — the flattened text lines under-measure it badly,
        // and the page-widen probe needs the real number.
        // A capsule wrapper is part of the grid's footprint in its
        // host column (its padding/spacing/margin band).
    }
}
