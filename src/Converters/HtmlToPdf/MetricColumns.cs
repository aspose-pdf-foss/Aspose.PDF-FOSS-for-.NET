using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    private static void SolveMetricColumns(
        MetricTableState mt)
    {
        OrderMetricRows(mt);

        // colspan: a spanning cell keeps its own column and occupies phantom
        // empty slots after it, so per-column index arithmetic stays intact;
        // the wrap and draw passes extend the real cell's box over its phantoms.
        CountMetricColumns(mt);
        MeasureMetricColumnMinima(mt);
        // A colgroup's own absolute width IS its column's width — not a floor and not a
        // maximum. Probed through the reference on 5, 7 and 10 em declarations under a
        // 17.59 cm body: a column whose max-content is WIDER than the declaration wraps
        // inside it ("Modified Narrative:" breaks in its 5 em box) and one whose content
        // is narrower still fills it (a 10 em box around a 108.5 pt label), so the
        // declaration pins the column and the auto passes leave it alone. A spanning
        // cell's own content can still grow it, which is what the reference does.
        // ⚠ The PIN is bounded to a grid that does NOT declare an absolute box of its own.
        // Where a table states `WIDTH: 567pt` and its colgroup partitions exactly that width,
        // pinning every column stops the auto pass growing any of them and the cells wrap
        // instead - measured on the 297 KB requirements document, which then runs to 61 pages
        // against the reference's 60. Those declarations were not what the em probes measured,
        // so they keep the FLOOR they had until someone probes that shape.
        var cgPins = mt.tableWpt <= 0;
        for (var cg = 0; cg < mt.nCols; cg++)
            if (mt.colGroupPt[cg] > 0 && (cgPins || mt.colW[cg] < mt.colGroupPt[cg]))
            {
                mt.colW[cg] = mt.colGroupPt[cg];
                if (cgPins) mt.colFixed[cg] = true;
            }
        // Outer-frame collapse grid: every column box (content + 2·padding)
        // shares the symmetric grid box minus the two half-frames; an
        // over-declared set gives its deficit back ∝ slack (declared −
        // min-content), floored at min-content — the banked auto-width rule.
        // (a declared width is the grid's MINIMUM: a nested declared box wider than it, with that
        //  box's own padding and border, widens the grid - probed on the e-mail cards, whose 755 px
        //  wrapper grows to its 767 px card)
        if (mt.stdSerif && mt.tableWpt > 0 && mt.nestedDeclaredFloorPt > mt.tableWpt) mt.tableWpt = mt.nestedDeclaredFloorPt;
        if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_MCOL") == "1") Console.Error.WriteLine($"[mstage] minima w=[{string.Join(",", Array.ConvertAll(mt.colW, w => w.ToString("0.#")))}] fixed=[{string.Join(",", mt.colFixed)}]");
        SolveMetricCollapseBoxes(mt);
        SolveMetricPercentGrid(mt);
        FitMetricColumnsToUsable(mt);
        if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_MCOL") == "1") Console.Error.WriteLine($"[mstage] usable w=[{string.Join(",", Array.ConvertAll(mt.colW, w => w.ToString("0.#")))}] fixed=[{string.Join(",", mt.colFixed)}]");
        SpreadSpanningContentWidths(mt);
        if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_MCOL") == "1") Console.Error.WriteLine($"[mstage] spread w=[{string.Join(",", Array.ConvertAll(mt.colW, w => w.ToString("0.#")))}] fixed=[{string.Join(",", mt.colFixed)}]");
        // A pixel table width the grid fills exactly: the surplus over the natural
        // columns distributes proportionally to each column's content width
        // (auto-layout distribution — the measured 285/305.2 boxes).
        ApplyMetricDeclaredWidth(mt);
        if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_MCOL") == "1") Console.Error.WriteLine($"[mstage] declared w=[{string.Join(",", Array.ConvertAll(mt.colW, w => w.ToString("0.#")))}] fixed=[{string.Join(",", mt.colFixed)}]");
        // width:100% from the sheet's table rule: the column grid fills the
        // content box — the leftover joins the last column (a centered single
        // cell then centers across the sheet, as the corpus letter's title).
        FillMetricTableWidth(mt);

        // A UA form grid's column never goes under its min-content, a declared table width
        // notwithstanding: the grid grows to what its cells hold (measured on the test
        // request: the 506 px container draws 400 wide around its fieldset-framed 506 px table).
        if (mt.mps.uaFormCells)
            for (var c = 0; c < mt.nCols; c++)
                if (!(mt.colVoid?[c] ?? false))
                    mt.colW[c] = Math.Max(mt.colW[c], MetricColumnMinContentPt(mt, c));
        mt.total = (mt.nCols + 1) * mt.s;
        foreach (var w in mt.colW) mt.total += w + 2 * mt.p;
        ShrinkMetricColumnsToFit(mt);
        if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_MCOL") == "1") Console.Error.WriteLine($"[mstage] shrunk w=[{string.Join(",", Array.ConvertAll(mt.colW, w => w.ToString("0.#")))}] fixed=[{string.Join(",", mt.colFixed)}]");
        // A column can never be NEGATIVE. An over-constrained percent grid can drive one there:
        // when every column is pinned by its own declared share, the deficit pass - which is what
        // keeps a column at its min-content - is skipped (it only runs for AUTO columns), and the
        // remainder lands on a pinned neighbour. Measured on the 10%/90% grid whose 90% cell's
        // min-content is 740.7 against a 610 box: the 10% column solved to -133.5.
        for (var cn = 0; cn < mt.nCols; cn++) if (mt.colW[cn] < 0 && !(mt.colVoid?[cn] ?? false)) mt.colW[cn] = 0;
        if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_MCOL") == "1")
            Console.Error.WriteLine($"[mcol] nCols={mt.nCols} availW={mt.availW:0.##} usableW={mt.usableW:0.##} tableWpt={mt.tableWpt:0.##} tablePct={mt.tablePct} s={mt.s} p={mt.p} pct=[{string.Join(",", mt.colPct)}] w=[{string.Join(",", Array.ConvertAll(mt.colW, w => w.ToString("0.#")))}] fixed=[{string.Join(",", mt.colFixed)}] cells=[{(mt.rows.Count > 0 ? string.Join(" | ", mt.rows[0].ConvertAll(mc => $"'{(mc.Text.Length > 12 ? mc.Text[..12] : mc.Text)}' b={mc.Bold} fs={mc.FontSize} face={mc.Face} nw={mc.NoWrap} span={mc.ColSpan} pct={mc.WidthPct} img={mc.ImgWPt:0.#}/{mc.ImgPlaceholder}/{mc.ImgRemoteBroken} segs={(mc.DivSegs?.Count ?? 0)}")) : "")}] '{Regex.Replace(mt.tableHtml.Length > 90 ? mt.tableHtml[..90] : mt.tableHtml, @"\s+", " ")}'");

        // RTL grid: the (mirrored-LTR) table RIGHT-anchors one right inset
        // inside the page edge — the widest grid's left edge then sits on the
        // 90 pt page margin the RTL page-width model left for it.
        if (mt.rtl)
        {
            mt.rtlTotal = (mt.nCols + 1) * mt.s;
            foreach (var w in mt.colW) mt.rtlTotal += w + 2 * mt.p;
            mt.tableX = Math.Max(0, mt.pageWidth - RtlGridRightInsetPt - mt.rtlTotal);
        }

        // Wrap cell text and size rows. An inline-table span grows the cell's first
        // line box by 3 pt (22px vs 18px line).
        mt.invc = System.Globalization.CultureInfo.InvariantCulture;
        // Per-cell face/metrics: a <font face> cell wraps, paces and seats with its
        // own family's win metrics (the flow face otherwise).
        // Font-tag-sized cells pace on the face's HHEA line (the quirks strut
        // model, measured: a size-4 cell's 18px font sits in a 21px line and
        // never under the table base font's own 18px strut); CSS-sized cells
        // keep the calibrated win-metric line.
        mt.hheaSum = mt.stdSerif ? (HheaLineSumFor(mt.face) ?? mt.fmSum) : mt.fmSum;
        // …and their baselines align on the shared line: the drop is whichever
        // is deeper — the table base font's strut baseline or the cell font's
        // own seat (measured: size-2 rows seat on the 12pt strut's 10.8, the
        // size-4 row on its own 12.43).
        MeasureMetricCellLines(mt);
    }
}
