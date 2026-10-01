
namespace Aspose.Pdf.Devices.Rasterizer;

internal static partial class ScanlineFiller
{
    /// <summary>The stages of the scanline fill: one scanline row at a time.</summary>
    private static void FillScanlineRow(ScanlineFillState sf, int y)
    {
        // Clear only the columns we touched last row (not the full pixelW).
        if (sf.maxTouchedX >= 0)
        {
            Array.Clear(sf.coverage, sf.rowXMin, Math.Min(sf.maxTouchedX - sf.rowXMin + 1, sf.coverage.Length - sf.rowXMin));
            sf.maxTouchedX = -1;
        }
        sf.rowXMin = int.MaxValue; sf.rowXMax = int.MinValue;

        // Admit edges that become sf.active somewhere inside this row's SubSamples range.
        // Row's latest subY is y + (SubSamples - 0.5) / SubSamples ≤ y + 1.
        while (sf.pending < sf.sorted.Length && sf.sorted[sf.pending].YMin < y + 1)
            sf.active.Add(sf.sorted[sf.pending++]);
        // Retire edges whose YMax has already passed this row. Swap-and-pop avoids
        // the O(n) shift of List.RemoveAt, keeping the full sweep linear in |edges|
        // instead of quadratic on polygons with many simultaneously-sf.active edges.
        for (var i = sf.active.Count - 1; i >= 0; i--)
        {
            if (sf.active[i].YMax <= y)
            {
                var last = sf.active.Count - 1;
                if (i != last) sf.active[i] = sf.active[last];
                sf.active.RemoveAt(last);
            }
        }

        for (var s = 0; s < SubSamples; s++)
        {
            // Sample at the centre of each sub-scanline slice so a rectangle spanning
            // y ∈ [32.5, 33.5] contributes to rows 32 AND 33 instead of collapsing
            // into one row at full opacity.
            var subY = y + (s + 0.5) / SubSamples;

            sf.hits.Clear();
            foreach (var e in sf.active)
            {
                if (e.YMin <= subY && subY < e.YMax)
                {
                    var x = e.XAtYMin + (subY - e.YMin) * e.InvSlope;
                    sf.hits.Add(new EdgeHit(x, e.Direction));
                }
            }
            if (sf.hits.Count < 2) continue;
            sf.hits.Sort(static (p, q) => p.X.CompareTo(q.X));

            // The outermost sf.hits bound this sub-sample's sf.coverage. Clip to [0, pixelW).
            var xLo = Math.Max(0, (int)sf.hits[0].X);
            var xHi = Math.Min(sf.pixelW - 1, (int)sf.hits[sf.hits.Count - 1].X + 1);
            if (xLo < sf.rowXMin) sf.rowXMin = xLo;
            if (xHi > sf.rowXMax) sf.rowXMax = xHi;

            if (sf.evenOdd)
                AccumulateEvenOdd(sf.hits, sf.coverage, sf.pixelW);
            else
                AccumulateNonZero(sf.hits, sf.coverage, sf.pixelW);
        }

        if (sf.rowXMax >= sf.rowXMin)
        {
            sf.maxTouchedX = sf.rowXMax;
            BlendRowCoverageRange(sf.pixels, sf.pixelW, y, sf.coverage, sf.rowXMin, sf.rowXMax, sf.r, sf.g, sf.b, sf.a, sf.clipMask, sf.blendMode, sf.knockout, sf.softMask);
        }
    }
}
