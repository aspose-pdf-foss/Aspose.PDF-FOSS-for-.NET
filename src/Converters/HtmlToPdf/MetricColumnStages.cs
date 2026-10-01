using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Wrap every cell's text into its solved column and record the line boxes the row heights are paced by.</summary>
    private static void MeasureMetricCellLines(MetricTableState mt)
    {
        foreach (var r in mt.rows)
            for (var c = 0; c < r.Count; c++)
                if (!MeasureMetricCell(mt, r, c)) break;
    }

    /// <summary>What a cell's wrap may exceed its content box by: float noise only, never a step
    /// of the glyph grid. It used to be 0.05 pt, guarding a float equality in the class-padding
    /// arithmetic; but 0.05 pt is a third of a 12 pt space and it let a line run a whole word past
    /// its box (probed: the complaint report's narrative reproduces the reference's 23 lines at
    /// every box width from 427.00 to 427.38 with no slack, and at none of them with 0.05).</summary>
    private const double MetricWrapFitSlackPt = 1e-6;

    /// <summary>How far inside its frame a fieldset in a grid cell stands its content on each
    /// SIDE: the stroke plus the padding-inline (8.25 pt at 12 pt).</summary>
    private static double MetricFieldsetSideInsetPt(double cellFs)
        => UaFieldsetStrokePt + UaFieldsetSidePadEm * cellFs;

    /// <summary>…and in a UA form cell the frame itself stands the UA margin-inline inside the
    /// cell's content box (measured on the test request: the frame at 100.5 in a cell whose
    /// content opens at 99; its tables 5.75 further in at 8 pt).</summary>
    private static double MetricFieldsetSideInsetPt(MetricParseState mps, double cellFs)
        => MetricFieldsetSideInsetPt(cellFs) + (mps.uaFormCells ? UaFieldsetSideInsetPt : 0);

    /// <summary>…and ABOVE it: the stroke plus the padding-top (4.95 pt at 12 pt).</summary>
    private static double MetricFieldsetTopInsetPt(double cellFs)
        => UaFieldsetStrokePt + UaFieldsetLegendGapEm * cellFs;

    /// <summary>…and above it under a LEGEND (UA form cells): the legend's own line box, the
    /// frame's top line running through its middle, then the padding-top (measured: the
    /// legend line 125.25..135 and the first table at 137.8 at 8 pt).</summary>
    private static double MetricFieldsetTopInsetPt(MetricParseState mps, MetricCell mc, double cellFs, double legendLineH)
        => mps.uaFormCells && mc.LegendText is not null
            ? legendLineH + UaFieldsetLegendGapEm * cellFs
            : MetricFieldsetTopInsetPt(cellFs);

    /// <summary>…and BELOW it, where the box closes: the padding-bottom plus the stroke
    /// (9.75 pt at 12 pt).</summary>
    private static double MetricFieldsetBottomInsetPt(double cellFs)
        => UaFieldsetBottomPadEm * cellFs + UaFieldsetStrokePt;

    /// <summary>The line box a leading checkbox sits in, split at the baseline: above it the
    /// larger of the cell strut's ascent half and the box with its top margin (12 pt), below it
    /// the larger of the strut's descent half and the bottom margin (2.25 pt). Probed at 12 pt
    /// Times: 14.70 = 12 + 2.70 (the strut's descent half wins below); measured at 10 pt Arial:
    /// 14.25 = 12 + 2.25 (the box wins above, the margin below).</summary>
    private static (double above, double below) MetricCheckboxLine(MetricParseState mps, bool stdSerif, bool wrapperStacks, double hheaSum, string face, (double asc, double sum) fm, MetricCell mc, double cellFs)
    {
        var box = CellLineOf(mps, stdSerif, wrapperStacks, hheaSum, face, fm, mc, cellFs);
        var cfm = CellFm(fm, mc);
        var sum = cfm.sum <= 1.0 ? 1.2 : cfm.sum;
        var halfLeading = (box - cellFs * sum) / 2;
        var strutAbove = halfLeading + cellFs * cfm.asc;
        var strutBelow = box - strutAbove;
        return (Math.Max(strutAbove, Table.UaCheckboxBoxPt + Table.UaCheckboxMarginBlockPt),
            Math.Max(strutBelow, Table.UaCheckboxMarginBlockPt));
    }

    private static double MetricCheckboxLineHeight(MetricParseState mps, bool stdSerif, bool wrapperStacks, double hheaSum, string face, (double asc, double sum) fm, MetricCell mc, double cellFs)
    {
        var (above, below) = MetricCheckboxLine(mps, stdSerif, wrapperStacks, hheaSum, face, fm, mc, cellFs);
        return above + below;
    }

    /// <summary>What a cell's own `padding-left` costs its text BEYOND the padding every cell
    /// already spends on that side: the declaration REPLACES the default, it does not add to
    /// it. Measured on the reference: a `.label + td` cell whose sheet states 0.5 em seats its
    /// text one em from its box edge, not an em plus a pixel, and its column is exactly that
    /// em plus the text plus the ordinary right padding.</summary>
    private static double CellPadLeftExtra(MetricCell mc, double p)
        => mc.PadLeft > 0 ? mc.PadLeft - p : 0;

    /// <summary>What a cell's own `padding-right` costs its text beyond the table's padding.</summary>
    private static double CellPadRightExtra(MetricCell mc, double p)
        => mc.PadRight > 0 ? mc.PadRight - p : 0;

    /// <summary>A grid wider than the page shrinks: the widest columns give up their slack first.</summary>
    private static void ShrinkMetricColumnsToFit(MetricTableState mt)
    {
        var fitW = MetricFitWidth(mt);
        // A UA grid declaring a PIXEL width keeps that box inside a narrower host cell: it
        // overflows the cell's padding rather than shrinking to it (probed: a 600 px grid in a
        // cellpadding=10 cell of a 100% grid draws its full 450, its right edge where the host's
        // is - the sheet having grown one page margin past it).
        // (…as far as the host cell's own padding: a grid wider than the host CELL keeps the calibrated shrink)
        if (mt.stdSerif && mt.tableWpt > 0 && mt.tablePct <= 0 && mt.tableWpt - 2 * mt.frameW > fitW
            && mt.tableWpt - 2 * mt.frameW <= fitW + 2 * mt.hostCellPadPt + 0.01)
            fitW = mt.tableWpt - 2 * mt.frameW;
        // (…and on a sheet grown to its INK the declared box stands as declared, overflowing the
        // content box the ink left short of it - measured on the enterprise summary: the 800 px
        // table's three 200 px grids sit 200 pt apart on the 736.75 sheet, the third past the box)
        if (mt.stdSerif && mt.tableWpt > 0 && mt.tablePct <= 0 && (mt.pageWidth > DefaultSheetWidthLimitPt || mt.declaredBoxLayout)
            && mt.symInsetPt > 0 && mt.tableWpt - 2 * mt.frameW > fitW)
            fitW = mt.tableWpt - 2 * mt.frameW;
        if (!(mt.total > fitW && !mt.mps.bordered)) return;
        mt.autoCols = 0;
        for (var c = 0; c < mt.nCols; c++)
            if (!mt.colFixed[c] && mt.colW[c] > 0) mt.autoCols++;
        ShareMetricColumnDeficit(mt);
        mt.total = (mt.nCols + 1) * mt.s;
        foreach (var w in mt.colW) mt.total += w + 2 * mt.p;
        // still over-full and a column is declared wider than the table's OWN declared box: it
        // yields what the auto columns, now at their min-content, could not (the worksheet's
        // 936 px column beside its one-word `Completed` column in an 800 px table). The table's
        // own box is the measure, not its host's: a table that declares no width grows to its
        // columns (the report's 177.8 mm column overflows its 432 pt host, and the sheet grows
        // to it), and a 958 px table's 705 pt column keeps its width inside a 571 pt host.
        if (mt.total > fitW && mt.stdSerif && mt.tableWpt > 0)
            YieldOverDeclaredColumns(mt, mt.tableWpt - 2 * mt.frameW);
        // still over-full: the declared percents over-fill the box (a
        // nested grid's 99% column beside its labels) — the right-most
        // percent column takes the remainder instead of overflowing
        if (mt.total > fitW)
            for (var c = mt.nCols - 1; c >= 0; c--)
                if (mt.colPct[c] > 0)
                {
                    var others = (mt.nCols + 1) * mt.s;
                    for (var o = 0; o < mt.nCols; o++) if (o != c) others += mt.colW[o] + 2 * mt.p;
                    mt.colW[c] = Math.Max(mt.mps.fontSize, fitW - others - 2 * mt.p);
                    break;
                }
        if (mt.total > fitW) FitDeclaredPixelGridToBox(mt, fitW);
    }

    /// <summary>A UA grid that declares no width of its own but whose PIXEL-declared columns overfill
    /// the box fits the box: its empty columns collapse first (an empty 6 px sizer column draws no
    /// width), then the columns that hold content scale down together to the box (probed on the
    /// land-register order: declared 400 + 400 + 6 px scale to 200 + 200 + 3 in a 403 pt box, and the
    /// 15-column sizer ladder fits the 489.68 box its nowrap disclaimer opened - the sheet never grows
    /// to the declared sum).</summary>
    private static void FitDeclaredPixelGridToBox(MetricTableState mt, double fitW)
    {
        if (!mt.stdSerif || !mt.wrapperStacks || mt.tableWpt > 0 || mt.tablePct > 0 || fitW <= 0) return;
        for (var c = 0; c < mt.nCols; c++)
            if (mt.colW[c] > 0 && !(mt.colPx[c] > 0 && !mt.colPxStyle[c]) && MetricColumnHoldsText(mt, c)) return;
        double chrome = (mt.nCols + 1) * mt.s + mt.nCols * 2 * mt.p, held = 0;
        for (var c = 0; c < mt.nCols; c++)
        {
            if (!MetricColumnHoldsText(mt, c)) { mt.colW[c] = 0; mt.colFixed[c] = true; continue; }
            held += mt.colW[c];
        }
        if (held <= 0 || held + chrome <= fitW) { RecomputeMetricTotal(mt); return; }
        // …the columns that hold content sharing the box the way an auto table does: each keeps its
        // min-content and takes the rest in proportion to the slack its declaration leaves over that
        // minimum (MEASURED, the evaluation form: 168 / 360 / 192 px attribute columns in a 429.57 pt
        // box solve 117.47 / 193.88 / 118.22 - the plain scale the sizer probes saw is this rule
        // with equal minima).
        var minW = new double[mt.nCols];
        double minSum = 0, slackSum = 0;
        for (var c = 0; c < mt.nCols; c++)
        {
            if (mt.colW[c] <= 0) continue;
            minW[c] = Math.Max(mt.minCol is { } floors && c < floors.Length ? floors[c] : 0, MetricColumnMinContentPt(mt, c));
            minSum += minW[c];
            slackSum += Math.Max(0, mt.colW[c] - minW[c]);
        }
        var room = fitW - chrome - minSum;
        if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_MCOL") == "1")
            Console.Error.WriteLine($"[pxfit] fit={fitW:0.##} chrome={chrome:0.##} held={held:0.##} room={room:0.##} slack={slackSum:0.##} min=[{string.Join(",", Array.ConvertAll(minW, w => w.ToString("0.#")))}] w=[{string.Join(",", Array.ConvertAll(mt.colW, w => w.ToString("0.#")))}]");
        for (var c = 0; c < mt.nCols; c++)
            if (mt.colW[c] > 0)
                mt.colW[c] = room > 0 && slackSum > 0 ? minW[c] + room * Math.Max(0, mt.colW[c] - minW[c]) / slackSum : minW[c];
        RecomputeMetricTotal(mt);
    }

    private static void RecomputeMetricTotal(MetricTableState mt)
    {
        mt.total = (mt.nCols + 1) * mt.s;
        foreach (var w in mt.colW) mt.total += w + 2 * mt.p;
    }

    /// <summary>The box an over-full table shrinks into: a UA-flow table's symmetric body box
    /// (the available span less the body inset the flow keeps on its right - probed: the test
    /// form's wrapper table and the 100 % grids inside it span 96..499 on the default sheet),
    /// the available span itself for everything else (a nested grid's inset is already zero).</summary>
    private static double MetricFitWidth(MetricTableState mt)
        => Math.Min(mt.availW - MetricRightInsetPt(mt), MetricDeclaredCapPt(mt));

    /// <summary>The box a UA grid declaring a width NARROWER than its natural columns lays out in:
    /// its columns share the deficit down to their min-contents inside it (probed on the Words
    /// letter: the 126 pt grids inside a 150 pt cell wrap their lines at 126 - `Test Test Test
    /// TestTest`, then the rest - not at the cell's 150). No cap for any other grid.</summary>
    private static double MetricDeclaredCapPt(MetricTableState mt)
        => mt.stdSerif && mt.wrapperStacks && mt.tableWpt > 0 && mt.tablePct <= 0 && MetricColumnsAllAuto(mt)
            ? mt.tableWpt - 2 * mt.frameW : double.MaxValue;

    /// <summary>Whether no column declares a width of its own, in points or per cent (a grid of
    /// declared columns keeps them past a narrower declared box - the settlement statement's
    /// 762 px table round its 220 / 721 px columns draws them whole).</summary>
    private static bool MetricColumnsAllAuto(MetricTableState mt)
    {
        for (var c = 0; c < mt.nCols; c++)
            if (mt.colPx[c] > 0 || mt.colPct[c] > 0) return false;
        return true;
    }

    /// <summary>The body inset a UA table keeps on its RIGHT: the symmetric one on the default
    /// sheet, none on a sheet grown to its widest table (that sheet ends one page margin past
    /// the table - the dead-stylesheet invoice's 571 pt grid fills its grown span exactly and
    /// keeps its 60 pages; an inset there wrapped it onto a 61st).</summary>
    private static double MetricRightInsetPt(MetricTableState mt)
        => mt.stdSerif && (mt.pageWidth <= DefaultSheetWidthLimitPt || (mt.keepRightInset && mt.tablePct <= 0)) ? mt.symInsetPt : 0;

    /// <summary>The widest sheet that is still the default (A4 or letter), not one grown to its content.</summary>
    private const double DefaultSheetWidthLimitPt = 612.0;

    /// <summary>A table that must fill its declared width shares the surplus over its columns.</summary>
    private static void FillMetricTableWidth(MetricTableState mt)
    {
        if (!mt.mps.bordered && mt.tableFills)
        {
            mt.tfAvail = mt.elemCollapseGrid
                ? mt.availW - mt.symInsetPt - 2 * 0.75 : mt.availW;
            mt.sumW0 = (mt.nCols + 1) * mt.s;
            foreach (var w in mt.colW) mt.sumW0 += w + 2 * mt.p;
            if (mt.sumW0 < mt.tfAvail && mt.nCols > 0)
            {
                // pt-report grids spread the width:100% surplus over the auto
                // columns ∝ their content width (probed: the monitoring row's
                // 86/96 pt columns land at ~186/207, the nbsp spacers at ~5);
                // the UA-serif letter sheets keep their last-column stretch.
                // …and a NESTED UA grid spreads it the same way its parent does (probed on
                // the complaint report's right-hand label grid: its two columns take 25.28
                // and 22.68 of a 47.95 pt surplus, exactly their 104.17 : 93.44 max-content
                // ratio), so the inset no longer decides it — being a UA grid does.
                var ptSpread = (!mt.stdSerif && mt.wrapperStacks) || mt.stdSerif;
                double ptNatS = 0;
                // (the auto columns take the surplus; declared columns only when no auto column is left)
                var ptOverDeclared = false;
                if (ptSpread)
                {
                    for (var c = 0; c < mt.nCols; c++)
                        if (!mt.colFixed[c] && !mt.colDeclared[c]) ptNatS += mt.colW[c];
                    if (ptNatS <= 0)
                    {
                        ptOverDeclared = true;
                        for (var c = 0; c < mt.nCols; c++)
                            if (!mt.colFixed[c]) ptNatS += mt.colW[c];
                    }
                }
                if (ptNatS > 0 && ptSpread)
                {
                    var ptSur = mt.tfAvail - mt.sumW0;
                    for (var c = 0; c < mt.nCols; c++)
                        if (!mt.colFixed[c] && (ptOverDeclared || !mt.colDeclared[c])) mt.colW[c] += ptSur * mt.colW[c] / ptNatS;
                }
                else
                {
                    mt.colW[mt.nCols - 1] += mt.tfAvail - mt.sumW0;
                    mt.colFixed[mt.nCols - 1] = true;
                }
            }
        }
    }

    /// <summary>A declared table width - in points or per cent - grows the solved columns to meet it.</summary>
    /// <summary>Whether a column keeps its width out of the declared-box surplus: a solved
    /// fixed column, or - on the bordered grid, where every column is solved as fixed - one
    /// with its own declared pixel width.</summary>
    private static bool DeclaredCol(MetricTableState mt, int c)
        => mt.mps.bordered ? mt.colPx[c] > 0 : mt.colFixed[c];

    /// <summary>Whether the grid's pixel-declared columns declare DIFFERENT widths (equal
    /// declarations share a surplus equally either way).</summary>
    private static bool DeclaredSharesDiffer(MetricTableState mt)
    {
        double first = -1;
        for (var c = 0; c < mt.nCols; c++)
        {
            if (mt.colPx[c] <= 0) return false;
            if (first < 0) first = mt.colPx[c];
            else if (Math.Abs(mt.colPx[c] - first) > 0.01) return true;
        }
        return false;
    }

    /// <summary>The declared-box surplus shared in proportion to the columns' declared widths; a
    /// column its content already holds open past its declaration keeps that and takes none.</summary>
    private static void ShareSurplusByDeclaredWidth(MetricTableState mt)
    {
        double declSum = 0;
        var takes = new bool[mt.nCols];
        for (var c = 0; c < mt.nCols; c++)
        {
            takes[c] = mt.colW[c] <= mt.colPx[c] + 0.01;
            if (takes[c]) declSum += mt.colPx[c];
        }
        if (declSum <= 0) { for (var c = 0; c < mt.nCols; c++) mt.colW[c] += mt.surplus / mt.nCols; return; }
        for (var c = 0; c < mt.nCols; c++)
            if (takes[c]) mt.colW[c] += mt.surplus * mt.colPx[c] / declSum;
    }

    private static void ApplyMetricDeclaredWidth(MetricTableState mt)
    {
        // (a bordered grid shares its declared box the same way, except the Excel-fragment
        // grid, whose row-1 cells split the box on their own rule in the bordered fit)
        if ((!mt.mps.bordered || !mt.mps.wtInlineGrid) && mt.tableWpt > 0)
        {
            mt.natSum = 0;
            for (var c = 0; c < mt.nCols; c++) if (!DeclaredCol(mt, c)) mt.natSum += mt.colW[c];
            mt.fixedSum = (mt.nCols + 1) * mt.s + mt.nCols * 2 * mt.p
                // the declared box is a BORDER box: a bordered grid's rules take their
                // share of it (one per boundary when collapsed, two per column otherwise)
                + (mt.mps.bordered ? (mt.mps.attrCollapse ? (mt.nCols + 1) * mt.bw : mt.nCols * 2 * mt.bw) : 0)
                + 2 * mt.frameW;
            for (var c = 0; c < mt.nCols; c++) if (DeclaredCol(mt, c)) mt.fixedSum += mt.colW[c];
            mt.surplus = mt.tableWpt - mt.fixedSum - mt.natSum;
            if (mt.surplus > 0 && mt.natSum > 0)
            {
                for (var c = 0; c < mt.nCols; c++)
                    if (!DeclaredCol(mt, c)) mt.colW[c] += mt.surplus * mt.colW[c] / mt.natSum;
            }
            // an all-declared grid splits the surplus EQUALLY (measured on the
            // boleto: 666px over five declared cols lands +5.25 pt on each)…
            else if (mt.surplus > 0 && mt.nCols > 0)
            {
                // …but the RTL attr grid gives the remainder to the SPANNING
                // cell's open slots — its declared px columns keep their widths
                // exactly (measured: 561.75 − 19/98/91px = the 405.75 span box).
                var rtlOpen = 0;
                if (mt.rtl) for (var c = 0; c < mt.nCols; c++) if (!DeclaredCol(mt, c)) rtlOpen++;
                if (mt.rtl && rtlOpen > 0)
                    for (var c = 0; c < mt.nCols; c++)
                    { if (!DeclaredCol(mt, c)) mt.colW[c] += mt.surplus / rtlOpen; }
                // …and a UA grid whose declared columns differ shares it in PROPORTION to what they
                // declare, a column already held open past its declaration by its content (a broken
                // image's box) taking none (probed on the safety data sheet: 1 / 150 / 436 px columns
                // in a 768 px table with a 72 px logo in the first solve 65 / 130.65 / 380.35, and
                // with the first declared 100 px, 84 / 125.95 / 366.05 - both exact).
                else if (mt.stdSerif && DeclaredSharesDiffer(mt))
                    ShareSurplusByDeclaredWidth(mt);
                else
                    for (var c = 0; c < mt.nCols; c++) mt.colW[c] += mt.surplus / mt.nCols;
            }
            // Declared columns that over-fill their declared table (their pixel widths
            // already sum to it, leaving no room for the cell spacing) shrink in
            // proportion so the grid keeps its declared box (measured: a 721px grid of
            // 80px columns pitches 60.0, the 1px spacing squeezed out of every column).
            else if (mt.surplus < 0 && !mt.mps.bordered && mt.natSum <= 0
                     // (a column declared wider than the table itself yields alone first)
                     && !(mt.stdSerif && YieldOverDeclaredColumns(mt, mt.tableWpt - 2 * mt.frameW)))
            {
                double declaredSum = 0;
                for (var c = 0; c < mt.nCols; c++) if (DeclaredCol(mt, c) && mt.colPx[c] > 0) declaredSum += mt.colW[c];
                if (declaredSum > 0 && declaredSum + mt.surplus > 0)
                    for (var c = 0; c < mt.nCols; c++)
                        if (DeclaredCol(mt, c) && mt.colPx[c] > 0) mt.colW[c] *= (declaredSum + mt.surplus) / declaredSum;
            }
        }
        // A declared percent width scales the column grid UP to fill its share of
        // the content box — the extra width distributes proportionally to each
        // column's content width (browser auto-layout distribution). A BORDERED
        // grid already resolved its box against the avail (banked shrink /
        // hug) — re-inflating it here spills the border past the content edge.
        // A grid whose every text cell spans the whole row has no column of its own to
        // size by: its declared percent width is the only measure, in every dialect.
        if ((mt.stdSerif || mt.wrapperStacks || MetricTableOnlySpanningCells(mt)) && mt.tablePct > 0 && !mt.uaPctGrid && !mt.mps.bordered)
        {
            // (the percent resolves against the symmetric body box: the flow's right margin
            // keeps no body inset, the table's box does)
            mt.targetContent = (mt.availW - mt.symInsetPt) * mt.tablePct / 100.0 - (mt.nCols + 1) * mt.s - mt.nCols * 2 * mt.p;
            // A column pinned by its own absolute width keeps it; the rest of the box is
            // what the other columns scale into.
            // (...and so does a width-ATTRIBUTE column fixed at its declared box beside an auto
            //  column - probed on the e-mail cards: the 172 px label column of a 100% grid keeps
            //  its box and the value column takes the whole surplus)
            mt.sumW = 0; mt.pinnedW = 0;
            for (var c = 0; c < mt.nCols; c++)
            {
                if (PercentFillPinsColumn(mt, c)) mt.pinnedW += mt.colW[c];
                else mt.sumW += mt.colW[c];
            }
            if (mt.sumW > 0 && mt.sumW < mt.targetContent - mt.pinnedW)
                for (var c = 0; c < mt.nCols; c++)
                    if (!PercentFillPinsColumn(mt, c))
                        mt.colW[c] *= (mt.targetContent - mt.pinnedW) / mt.sumW;
        }
    }

    /// <summary>Whether the percent fill leaves column <paramref name="c"/> at its width: an empty
    /// style-px spacer, or a width-attribute column already fixed at its box beside an auto column.</summary>
    private static bool PercentFillPinsColumn(MetricTableState mt, int c)
        => mt.stdSerif && mt.colPx[c] > 0
           && ((mt.colPxStyle[c] && MetricColumnIsEmpty(mt, c))
               || (!mt.colPxStyle[c] && mt.colFixed[c] && mt.wrapperStacks && MetricGridHasAutoColumn(mt)));

    /// <summary>No cell of this column carries text: a spacer column, whose declared width is
    /// its whole used width (nothing inside it can push it wider).</summary>
    private static bool MetricColumnIsEmpty(MetricTableState mt, int c)
    {
        foreach (var r in mt.rows)
            if (c < r.Count && r[c].Text.Trim().Length > 0) return false;
        return true;
    }

    /// <summary>Whether any cell's text stands in column c - a cell starting there, or a spanning
    /// cell that crosses it (a column only ever crossed by spans still carries their ink).</summary>
    private static bool MetricColumnHoldsText(MetricTableState mt, int c)
    {
        foreach (var r in mt.rows)
            for (var k = 0; k <= c && k < r.Count; k++)
                if (!r[k].Phantom && MetricCellHoldsContent(r[k]) && k + Math.Max(1, r[k].ColSpan) > c) return true;
        return false;
    }

    /// <summary>Whether a cell holds anything that takes width: text (a non-breaking space is ink in
    /// its own font, so only plain whitespace is trimmed), an image, a nested grid, or a segment with text.</summary>
    private static bool MetricCellHoldsContent(MetricCell mc)
    {
        if (mc.Text.Trim(' ', '\t', '\r', '\n').Length > 0) return true;
        if (mc.ImgWPt > 0 || mc.SubTables is { Count: > 0 }) return true;
        if (mc.DivSegs is { Count: > 0 } segs)
            foreach (var seg in segs)
                if (seg.Text.Trim(' ', '\t', '\r', '\n').Length > 0 || seg.BoxWidthPt > 0) return true;
        return false;
    }

    /// <summary>A grid whose width attributes are over-constrained: some column's min-content stands past
    /// the width its attribute declares (the invoice email's 260 px cell holding a 300 px box). Only such a
    /// grid takes the declared-box law; every other declared grid keeps the calibrated solve.</summary>
    private static bool DeclaredGridOverConstrained(MetricTableState mt)
    {
        mt.declaredOverConstrained ??= ComputeDeclaredGridOverConstrained(mt);
        return mt.declaredOverConstrained.Value;
    }

    private static bool ComputeDeclaredGridOverConstrained(MetricTableState mt)
    {
        for (var c = 0; c < mt.nCols; c++)
            if (mt.colPx[c] > 0 && !mt.colPxStyle[c] && MetricColumnMinContentPt(mt, c) > mt.colPx[c] + 0.01)
                return true;
        return false;
    }

    /// <summary>The inline padding a width-declaring cell of column <paramref name="c"/> states past
    /// the grid's own cell padding, plus the collapsed rule where the grid collapses its borders:
    /// the box such a cell's declared content width stands in.</summary>
    private static double DeclaredCellExtraPadPt(MetricTableState mt, int c)
    {
        double extra = 0;
        foreach (var r in mt.rows)
            if (c < r.Count && r[c].ColSpan <= 1 && r[c].WidthPx > 0)
                extra = Math.Max(extra, Math.Max(0, r[c].PadLeft - mt.p) + Math.Max(0, r[c].PadRight - mt.p));
        return extra + (mt.collapseBoxW > 0 || mt.mps.collapsedGrid ? 0.75 : 0);
    }

    /// <summary>Whether any column of the grid declares neither a pixel nor a percent width.</summary>
    private static bool MetricGridHasAutoColumn(MetricTableState mt)
    {
        for (var c = 0; c < mt.nCols; c++)
            if (mt.colPx[c] <= 0 && mt.colPct[c] <= 0 && !(mt.colVoid?[c] ?? false)) return true;
        return false;
    }

    /// <summary>Each column takes its declared, percentage or natural width inside the usable span.</summary>
    private static void FitMetricColumnsToUsable(MetricTableState mt)
    {
        for (var c = 0; c < mt.nCols && !mt.mps.bordered; c++)
        {
            if (mt.colFixed[c]) continue;
            if (mt.colPct[c] > 0) { mt.colW[c] = mt.colPct[c] / 100.0 * mt.usableW - 2 * mt.p; mt.colFixed[c] = true; continue; }
            // An EMPTY column declared at ZERO width (and at no other width) is no column at
            // all: the report grid's `WIDTH: 0mm` spacer draws its neighbour at the table's
            // edge (measured: the header grid at 90 + the 3.18 mm column, not 12 pt further).
            if (mt.stdSerif && mt.colZero[c] && mt.colPx[c] <= 0 && MetricColumnIsEmpty(mt, c))
            { mt.colW[c] = 0; mt.colFixed[c] = true; continue; }
            // An EMPTY column's own absolute style width fixes it whatever the table's
            // width is: the percent box shares out only what is left (measured: the
            // dunning letter's `width: 9.03cm` spacer column seats its address at 357.2).
            // A column that carries TEXT keeps the ordinary solve - a declared width is a
            // MINIMUM there, and pinning it wrapped a 60 pt Word cell onto a 61st page.
            if (mt.stdSerif && mt.colPxStyle[c] && mt.colPx[c] > 0 && MetricColumnIsEmpty(mt, c))
            { mt.colW[c] = mt.colPx[c]; mt.colFixed[c] = true; continue; }
            // Modern-nesting model: a width attribute or class fixes its column —
            // the nested grids and class-framework grids wrap at their declared
            // cols instead of their natural text extents.
            if (mt.wrapperStacks && (mt.tablePct == 0 && !mt.tableFills || !mt.stdSerif)
                && mt.colPx[c] > 0)
            {
                // (a UA column a broken image holds open past its declaration keeps the image's box:
                // probed on the safety data sheet, a 1 px column holding a 72 px broken logo solves 65)
                mt.colW[c] = mt.stdSerif ? Math.Max(mt.colPx[c] + MetricColumnStyleChromePt(mt, c), MetricColumnBrokenImageFloorPt(mt, c)) : mt.colPx[c];
                mt.colFixed[c] = true; continue;
            }
            // A UA grid column a width ATTRIBUTE declares in a filling (percent) grid is that
            // box, floored at its min-content: it takes no surplus, and a deficit is shared with
            // the others in proportion to slack (probed on the invoice email: 320/20/260 px cells
            // in a 100% grid whose 260 px cell holds a 300 px box solve 211.8 / 13.2 / 225).
            // (a STYLE width on a text column stays the calibrated minimum: the Word cell)
            if (mt.stdSerif && mt.wrapperStacks && mt.colPx[c] > 0 && !mt.colPxStyle[c] && DeclaredGridOverConstrained(mt))
            {
                mt.colW[c] = Math.Max(mt.colPx[c], MetricColumnMinContentPt(mt, c));
                mt.colDeclared[c] = true;
                continue;
            }
            // ...and in a filling grid WITH SURPLUS such a column is exactly its declared box (the
            // content width plus its own paddings and rule), floored at its min-content: the
            // surplus goes to the auto columns beside it (probed on the e-mail cards: a
            // `width="172"` label cell with `padding-left: 20px` and 5 px class pads seats its
            // value column 183 px = 172 + 20 + 5 + 1 + 5 further in a 100% grid).
            if (mt.stdSerif && mt.wrapperStacks && mt.tablePct > 0 && mt.colPx[c] > 0 && !mt.colPxStyle[c]
                && MetricGridHasAutoColumn(mt))
            {
                mt.colW[c] = Math.Max(mt.colPx[c] + MetricColumnStyleChromePt(mt, c) + DeclaredCellExtraPadPt(mt, c), MetricColumnMinContentPt(mt, c));
                mt.colFixed[c] = true;
                continue;
            }
            mt.colW[c] = Math.Max(mt.colW[c], MetricColumnMaxContentPt(mt, c));
        }
    }

    /// <summary>A br-line as its width is measured: leading whitespace dropped, trailing plain spaces
    /// dropped, but a trailing run that holds an nbsp kept whole (probed: `the &amp;nbsp;&amp;nbsp; &lt;br>` is
    /// 13.3 pt wider than its visible text - the space, the two nbsps and the closing space).</summary>
    private static string BrLineMeasureText(string seg)
    {
        // (a leading non-breaking space is ink: the Words letter's nbsp spacer column measures its
        // 3 pt, where trimming every Unicode space left the column empty)
        var s = seg.TrimStart(' ', '\t', '\r', '\n');
        var end = s.Length;
        while (end > 0 && (s[end - 1] == ' ' || s[end - 1] == '\u00A0')) end--;
        return s[end..].IndexOf('\u00A0') >= 0 ? s : s[..end];
    }

    /// <summary>The widest content any NON-SPANNING cell of the column holds, unwrapped: its
    /// text lines in the face the cell draws, a report cell's image box, its div bands, a
    /// nested grid's max-content capped by the usable box, and a leading checkbox's margin box
    /// before its first line.</summary>
    private static double MetricColumnMaxContentPt(MetricTableState mt, int c)
    {
        var w = 0.0;
        foreach (var r in mt.rows)
        {
            if (c >= r.Count || r[c].ColSpan > 1) continue;
            var mc = r[c];
            var lead = mc.LeadCheckboxName is not null ? MetricCheckboxMarginBoxPt(mc) : 0.0;
            // a SPANNING cell stretches over several columns and must
            // not pin its first one to its whole width; an alt-text cell
            // sizes to its image BOX, not to the alt's unwrapped advance
            if (mc.Text.Length > 0 && !(mc.AltTextOnly && mc.ImgWPt > 0))
            {
                var first = true;
                foreach (var brSeg in mc.Text.Split('\u0001'))
                {
                    // (measured on the face the cell draws: a cell naming its own family and
                    // sized on the table's face broke its words whenever its own was wider)
                    w = Math.Max(w, MeasureFaceText(CellFaceName(mt.face, mt.boldFace, mc),
                        BrLineMeasureText(brSeg), mc.FontSize ?? mt.mps.fontSize)
                        // a class padding-left is part of the cell's box —
                        // the wrap pass subtracts it back out (and so is a padding-right)
                        + CellPadLeftExtra(mc, mt.p) + CellPadRightExtra(mc, mt.p) + (first ? lead : 0));
                    first = false;
                }
            }
            else if (lead > 0) w = Math.Max(w, lead + CellPadLeftExtra(mc, mt.p) + CellPadRightExtra(mc, mt.p));
            // a text input's box is content width too - its intrinsic box, or its width in points
            if (mc.InputBoxes is { Count: > 0 } inputs)
                w = Math.Max(w, MetricInputsMaxContentPt(mt, mc, inputs));
            // a report cell's declared IMAGE box is content width too — the
            // logo column sizes to its 210px box, not to its alt text
            if (mt.paragraphCells && mc.ImgWPt > 0)
                w = Math.Max(w, mc.ImgWPt);
            // Div-stacked cell content sizes its column the same way — each
            // segment's unwrapped advance is the cell's max-content.
            if (mc.DivSegs is { Count: > 0 } wSegs)
                foreach (var wSeg in wSegs)
                    if (wSeg.Text.Trim().Length > 0)
                        w = Math.Max(w, MeasureFaceText(
                            wSeg.Bold || mc.Bold ? mt.boldFace : wSeg.Face ?? mt.face,
                            wSeg.Text.Trim(), wSeg.FontSize ?? mc.FontSize ?? mt.mps.fontSize)
                            // (a UA band's own side insets - its paddings and margins - are part of
                            //  its box: the e-mail cards' `padding: 20px` heads size their cell for them)
                            + (mt.stdSerif ? wSeg.PadLeft + wSeg.PadRight : 0));
            // a cell whose content is a nested grid sizes for it: the
            // browser gives the container the sub-table's max-content,
            // capped by the available box (a width:100% sub then fills it)
            if (mc.SubTables is { Count: > 0 } natSubs)
            {
                double subMax = 0;
                foreach (var sub in natSubs)
                {
                    // …the grid's REAL max-content under the UA flow - the widest of its
                    // rows (measured on the test form: a two-column grid beside a nested
                    // phone/fax grid splits 263.8 / 126.4, the surplus shared in proportion
                    // to the max-contents; the flattened text over-asked for the column)
                    if (mt.stdSerif)
                    {
                        subMax = Math.Max(subMax, MetricGridMaxContentPt(mt, sub, mc));
                        if (NestedGridDeclaredWidthPt(mt, sub) is { } declSub && declSub > 0) subMax = Math.Max(subMax, declSub);
                        continue;
                    }
                    var subText = CollapseWs(DecodeEntities(
                        Regex.Replace(sub, "<[^>]+>", " "))).Trim();
                    if (subText.Length > 0)
                        subMax = Math.Max(subMax, MeasureFaceText(mt.face, subText,
                            mc.FontSize ?? mt.mps.fontSize));
                }
                if (subMax > 0)
                    w = Math.Max(w, Math.Min(subMax, mt.usableW - MetricRightInsetPt(mt) - 2 * mt.p));
            }
        }
        return w;
    }

    /// <summary>One cell's max-content, unwrapped, for a SPANNING cell's demand on the columns
    /// it crosses: its widest text line in its own face (the whole text when it cannot wrap),
    /// its div bands, and a nested grid's real max-content (the widest of its rows).</summary>
    private static double MetricCellMaxContentPt(MetricTableState mt, MetricCell mc)
    {
        var fs = mc.FontSize ?? mt.mps.fontSize;
        var faceName = CellFaceName(mt.face, mt.boldFace, mc);
        var w = 0.0;
        if (mc.Text.Length > 0)
        {
            if (mc.NoWrap) w = MeasureFaceText(faceName, mc.Text.Replace('\u0001', ' '), fs);
            else
                foreach (var seg in mc.Text.Split('\u0001'))
                    w = Math.Max(w, MeasureFaceText(faceName, seg.Trim(), fs));
            if (mc.LeadCheckboxName is not null) w += MetricCheckboxMarginBoxPt(mc);
        }
        else if (mc.LeadCheckboxName is not null) w = MetricCheckboxMarginBoxPt(mc);
        if (mc.InputBoxes is { Count: > 0 } spanInputs)
            w = Math.Max(w, MetricInputsMaxContentPt(mt, mc, spanInputs));
        if (mc.DivSegs is { Count: > 0 } segs)
            foreach (var sg in segs)
                if (sg.Text.Trim().Length > 0)
                    w = Math.Max(w, MeasureFaceText(sg.Bold || mc.Bold ? mt.boldFace : sg.Face ?? mt.face,
                        sg.Text.Trim(), sg.FontSize ?? fs) + sg.PadLeft);
        if (mc.SubTables is { Count: > 0 } subs)
            foreach (var sub in subs)
            {
                w = Math.Max(w, MetricGridMaxContentPt(mt, sub, mc));
                if (mt.stdSerif && NestedGridDeclaredWidthPt(mt, sub) is { } declSub && declSub > 0) w = Math.Max(w, declSub);
            }
        return w + CellPadLeftExtra(mc, mt.p) + CellPadRightExtra(mc, mt.p);
    }

    /// <summary>A nested grid's max-content: the widest of its rows, each row the sum of its
    /// cells' max-content boxes and the spacing around them, parsed in the typography the
    /// host cell hands it.</summary>
    private static double MetricGridMaxContentPt(MetricTableState host, string html, MetricCell hostCell)
    {
        var mt = ParseMetricTable(host.doc, html, host.css, host.marginLeft, host.contentWidth, host.pageWidth,
            host.pageHeight, host.marginTop, host.marginBottom, GridTypoCell(hostCell).Face ?? host.face, CellFm(host.fm, GridTypoCell(hostCell)),
            host.docFontDict, host.stdSerif, GridTypoCell(hostCell).FontSize ?? host.mps.fontSize, wrapperStacks: true,
            symInsetPt: 0, host.rtl, host.paragraphCells, host.serifReportCells, host.loadOptions, host.siblingCellRules,
            // (in the host's dialects, as the render parses it - a form grid's nested grid holds its controls'
            // boxes; measured: the signature/date host splits ∝ its nested grids' max-content with the inputs)
            uaBlockCells: host.mps.uaBlockCells, uaFormCells: host.mps.uaFormCells,
            hostCellPadPt: host.p);
        CloseRow(mt.mps, mt.rows, mt.text, mt.reportCells, mt.stdSerif);
        var widest = 0.0;
        foreach (var r in mt.rows)
        {
            var rowW = (r.Count + 1) * mt.s;
            foreach (var mc in r) rowW += MetricCellMaxContentPt(mt, mc) + 2 * mt.p;
            widest = Math.Max(widest, rowW);
        }
        return widest;
    }

    /// <summary>A nested grid's min-content: its columns' min-content boxes (each the widest over its
    /// rows) and the spacing around them, parsed in the typography the host cell hands it.</summary>
    private static double MetricGridMinContentPt(MetricTableState host, string html, MetricCell hostCell)
    {
        var mt = ParseMetricTable(host.doc, html, host.css, host.marginLeft, host.contentWidth, host.pageWidth,
            host.pageHeight, host.marginTop, host.marginBottom, GridTypoCell(hostCell).Face ?? host.face, CellFm(host.fm, GridTypoCell(hostCell)),
            host.docFontDict, host.stdSerif, GridTypoCell(hostCell).FontSize ?? host.mps.fontSize, wrapperStacks: true,
            symInsetPt: 0, host.rtl, host.paragraphCells, host.serifReportCells, host.loadOptions, host.siblingCellRules,
            uaBlockCells: host.mps.uaBlockCells, uaFormCells: host.mps.uaFormCells,
            hostCellPadPt: host.p);
        CloseRow(mt.mps, mt.rows, mt.text, mt.reportCells, mt.stdSerif);
        var nCols = 0;
        foreach (var r in mt.rows) nCols = Math.Max(nCols, r.Count);
        if (nCols == 0) return 0;
        var colMin = new double[nCols];
        foreach (var r in mt.rows)
            for (var c = 0; c < r.Count; c++)
                if (r[c].ColSpan <= 1) colMin[c] = Math.Max(colMin[c], MetricCellMinContentPt(mt, r[c]));
        var total = (nCols + 1) * mt.s;
        foreach (var w in colMin) total += w + 2 * mt.p;
        return total;
    }

    private const double WholeWidthPercent = 100.0;
    private const double PercentShareEpsilon = 1e-6;

    /// <summary>A spanning cell's share beyond what its columns already hold goes equally to the share-less columns of the span; a span whose every column holds a share scales them up to it.</summary>
    private static void SpreadSpanningShare(double[] colPct, int first, int span, double share)
    {
        double held = 0;
        var open = 0;
        for (var k = first; k < first + span; k++)
        {
            held += colPct[k];
            if (colPct[k] <= 0) open++;
        }
        if (share <= held) return;
        for (var k = first; k < first + span; k++)
        {
            if (open > 0) { if (colPct[k] <= 0) colPct[k] = (share - held) / open; }
            else colPct[k] *= share / held;
        }
    }

    /// <summary>Each percent share in column order is cut to what the shares before it leave of the whole width; a share left nothing becomes an auto column.</summary>
    private static void CapPercentSharesInOrder(double[] colPct)
    {
        double declared = 0;
        for (var c = 0; c < colPct.Length; c++)
        {
            if (colPct[c] <= 0) continue;
            var remaining = WholeWidthPercent - declared;
            if (colPct[c] > remaining) colPct[c] = remaining < PercentShareEpsilon ? 0 : remaining;
            declared += colPct[c];
        }
    }

    /// <summary>Count the grid's columns and seat the table box: its left edge, its available span and its cell padding.</summary>
    private static void CountMetricColumns(MetricTableState mt)
    {
        foreach (var r0 in mt.rows)
            for (var i0 = 0; i0 < r0.Count; i0++)
                if (r0[i0].ColSpan > 1)
                    for (var k0 = 1; k0 < r0[i0].ColSpan; k0++)
                        r0.Insert(i0 + k0, new MetricCell { Text = "", Phantom = true });
        mt.nCols = 0;
        foreach (var r in mt.rows) mt.nCols = Math.Max(mt.nCols, r.Count);
        // RTL document: cells fill columns from the RIGHT — mirror every row
        // onto the LTR grid (pad the visual-left slots, reverse, and move each
        // spanning cell back AHEAD of its phantom slots so the LTR draw loop's
        // spanner-then-phantoms convention holds).
        if (mt.rtl)
            foreach (var rr in mt.rows)
            {
                while (rr.Count < mt.nCols) rr.Add(new MetricCell { Text = "", Phantom = true });
                rr.Reverse();
                for (var i = 0; i < rr.Count; i++)
                    if (rr[i].ColSpan > 1)
                    {
                        var lead = i;
                        var k = rr[i].ColSpan - 1;
                        while (k-- > 0 && lead > 0 && rr[lead - 1].Phantom) lead--;
                        if (lead < i)
                        {
                            var spanner = rr[i];
                            rr.RemoveAt(i);
                            rr.Insert(lead, spanner);
                        }
                    }
            }
        mt.tableX = mt.marginLeft + mt.indent;
        // (an inline frame is inside the table's box: the cells share what it leaves)
        mt.availW = mt.contentWidth - mt.indent - 2 * mt.frameW;

        // Column content widths: an inline-table span fixes the column; a width="%"
        // attribute takes its share of the table box; otherwise the widest measured
        // cell line. Over-wide natural columns are clamped from the right.
        // The Excel-fragment grid's cells style their own padding — it wins
        // over whatever the cellpadding attribute set during the parse.
        if (mt.mps.wtInlineGrid && mt.mps.wtPadH >= 0) mt.p = mt.mps.wtPadH;
    }

    /// <summary>Order the row groups and scale the declared row heights to the table's own height.</summary>
    private static void OrderMetricRows(MetricTableState mt)
    {
        mt.rowsIn = mt.rows;

        // Reorder row groups: thead, then tbody, then tfoot — each group keeping
        // its source order (a tfoot authored before the tbody still closes the
        // table; rowHeights travels with its row).
        if (mt.mps.rowSections.Contains(0) || mt.mps.rowSections.Contains(2))
        {
            mt.order = Enumerable.Range(0, mt.rows.Count)
                .OrderBy(i => mt.mps.rowSections[i]).ToArray();
            mt.rows = mt.order.Select(i => mt.rowsIn[i]).ToList();
            mt.mps.rowHeights = mt.order.Select(i => mt.mps.rowHeights[i]).ToList();
            mt.mps.rowHeightExact = mt.order.Select(i => mt.mps.rowHeightExact[i]).ToList();
        }

        // A table HEIGHT attribute scales the declared row heights up
        // proportionally to fill it (probed: 19/69/22 px rows in a height=147
        // table land at 25.39/92.21/29.40 px).
        if (mt.mps.tableHeightPt > 0)
        {
            mt.declSum = 0;
            foreach (var rh in mt.mps.rowHeights) mt.declSum += rh;
            if (mt.declSum > 0 && mt.declSum < mt.mps.tableHeightPt)
                for (var ri = 0; ri < mt.mps.rowHeights.Count; ri++)
                    mt.mps.rowHeights[ri] += (mt.mps.tableHeightPt - mt.declSum) * mt.mps.rowHeights[ri] / mt.declSum;
        }

        // An inline-style table height with NO declared row heights: the rows share
        // the band equally, spacing between them kept (probed: the 135px single-row
        // band's cell box is 101.25 − 2 × 1.5 = 98.25 pt, content centred in it).
        if (mt.mps.tableStyleHPt > 0 && mt.rows.Count > 0)
        {
            mt.rhSum = 0;
            foreach (var rh in mt.mps.rowHeights) mt.rhSum += rh;
            if (mt.rhSum <= 0)
            {
                var share = (mt.mps.tableStyleHPt - (mt.rows.Count + 1) * mt.s) / mt.rows.Count;
                if (share > 0)
                {
                    while (mt.mps.rowHeights.Count < mt.rows.Count) { mt.mps.rowHeights.Add(0); mt.mps.rowHeightExact.Add(false); }
                    for (var ri = 0; ri < mt.rows.Count; ri++)
                        mt.mps.rowHeights[ri] = Math.Max(mt.mps.rowHeights[ri], share);
                }
            }
        }
    }

}
