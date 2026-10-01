namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>A row's own height before any row-spanning cell grows it: the strut line (when the
    /// table pitches on one) or its cells' ink - and, for a quirks-mode row of class-sized cells, the
    /// height its cells declare (probed on the land-register order: a 21 px row holding nothing but
    /// the start of a three-line spanning cell still bands 15.75).</summary>
    private static double MetricRowOwnHeight(MetricTableState mt, int rj)
    {
        var row = mt.rows[rj];
        var declaresHeight = rj < mt.mps.rowHeights.Count && mt.mps.rowHeights[rj] > 0;
        foreach (var mc in row)
            if (mc.RowSpan <= 1 && (mc.HeightPt > 0 || mc.HeightStylePt > 0)) declaresHeight = true;
        var classSized = _quirksRowStrut && declaresHeight && MetricRowTextAllClassSized(row);
        var h = mt.tableHasText && !classSized ? mt.lineH : 0;
        foreach (var mc in row)
            if (mc.RowSpan <= 1) h = Math.Max(h, mc.ContentH);
        if (!classSized) return h;
        foreach (var mc in row)
            if (mc.RowSpan <= 1) h = Math.Max(h, Math.Max(mc.HeightPt, mc.HeightStylePt));
        if (rj < mt.mps.rowHeights.Count) h = Math.Max(h, mt.mps.rowHeights[rj] - 2 * mt.p);
        return h;
    }

    /// <summary>A row's own height, where the pt form grid's bare row - every cell empty, or none of its
    /// own under a spanning neighbour - is nothing, as the row stage bands it (probed: the title's
    /// rowspan=2 over an empty row makes a 24.75 table: the cell's line and its chrome alone).</summary>
    private static double PtFormBareRowHeightOr(MetricTableState mt, int rj)
    {
        if (!mt.mps.ptFormCells) return MetricRowOwnHeight(mt, rj);
        // (no strut: a pt form row is as tall as its own cells' content or declared height)
        var h = 0.0;
        foreach (var mc in mt.rows[rj])
            if (mc.RowSpan <= 1) h = Math.Max(h, Math.Max(mc.ContentH, Math.Max(mc.HeightPt, mc.HeightStylePt)));
        return h;
    }

    /// <summary>Row-spanning cells: a cell taller than the rows it spans grows each of them by an equal
    /// share (its declared height counts as content - it spans the rows, banding none alone), and every
    /// spanning cell then seats in the band its rows make, that growth included.</summary>
    private static void ComputeRowSpanBands(MetricTableState mt)
    {
        mt.rowSpanExtra = new double[mt.rows.Count];
        if (!mt.stdSerif) return;
        var spans = new List<(int ri0, int kSpan, double spanH, MetricCell mc)>();
        for (var ri0 = 0; ri0 < mt.rows.Count; ri0++)
            foreach (var mcSpan in mt.rows[ri0])
                if (mcSpan.RowSpan > 1 && (mcSpan.ContentH > 0 || mcSpan.HeightPt > 0 || mcSpan.HeightStylePt > 0))
                {
                    var spanH = Math.Max(mcSpan.ContentH, Math.Max(mcSpan.HeightPt, mcSpan.HeightStylePt));
                    var kSpan = Math.Min(mcSpan.RowSpan, mt.rows.Count - ri0);
                    if (kSpan > 0) spans.Add((ri0, kSpan, spanH, mcSpan));
                }
        if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_MROW") == "1")
            Console.WriteLine($"[rowspan] rows={mt.rows.Count} spans={spans.Count} lineH={mt.lineH:0.##} hasText={mt.tableHasText}");
        foreach (var (ri0, kSpan, spanH, _) in spans)
        {
            var have = (kSpan - 1) * (mt.s + 2 * mt.p);
            for (var rj = ri0; rj < ri0 + kSpan; rj++) have += PtFormBareRowHeightOr(mt, rj);
            if (spanH <= have) continue;
            var addEach = (spanH - have) / kSpan;
            for (var rj = ri0; rj < ri0 + kSpan; rj++)
                mt.rowSpanExtra[rj] = Math.Max(mt.rowSpanExtra[rj], addEach);
        }
        foreach (var (ri0, kSpan, spanH, mc) in spans)
        {
            var band = (kSpan - 1) * (mt.s + 2 * mt.p);
            for (var rj = ri0; rj < ri0 + kSpan; rj++) band += PtFormBareRowHeightOr(mt, rj) + mt.rowSpanExtra[rj];
            mc.SpanBandPt = Math.Max(band, spanH);
        }
    }
}
