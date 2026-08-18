namespace Aspose.Pdf.Devices.Rasterizer;

/// <summary>
/// Scanline polygon fill with anti-aliasing. Uses 4× vertical supersampling and
/// exact horizontal-area coverage so that thin sub-pixel strokes and polygon edges
/// produce fractional-opacity pixels instead of a binary 0/255 split. This is what
/// matches GDI+'s rasterization for the Template-PNG visual regressions.
/// Supports both even-odd and non-zero winding fill rules.
/// </summary>
internal static class ScanlineFiller
{
    // Each output row is split into this many sub-scanlines. Each sub-scanline
    // contributes 1/SubSamples of the total per-pixel coverage. 4 matches the
    // GlyphRasterizer's supersampling factor and is the common GDI+ default.
    private const int SubSamples = 4;

    /// <summary>
    /// Fill a polygon defined by the edge table into an RGBA pixel buffer with AA.
    /// When <paramref name="clipMask"/> is non-null, every pixel column with a 0 byte
    /// in the mask is skipped — this enforces a previously-installed <c>W</c>/<c>W*</c>
    /// clipping path on the fill.
    /// </summary>
    public static void Fill(EdgeTable edgeTable, byte[] pixels, int pixelW, int pixelH,
        byte r, byte g, byte b, byte a, bool evenOdd, byte[]? clipMask = null, string blendMode = "Normal",
        bool knockout = false, byte[]? softMask = null)
    {
        var edges = edgeTable.Edges;
        if (edges.Count == 0) return;

        // Sort edges by YMin so we can sweep an active-edge window row-by-row.
        // Without this, each row's inner loop scans the full edge list, which is
        // O(|edges| × rows) and dominates rendering time on clip-heavy pages.
        var sorted = new Edge[edges.Count];
        for (var i = 0; i < edges.Count; i++) sorted[i] = edges[i];
        Array.Sort(sorted, static (a, b) => a.YMin.CompareTo(b.YMin));

        // Polygon's integer-pixel vertical extent. Floor/ceiling outward so sub-pixel
        // strokes that barely enter row (y-1) or (y+1) still get their partial coverage.
        double yMaxD = double.MinValue;
        foreach (var e in edges) if (e.YMax > yMaxD) yMaxD = e.YMax;
        var yStart = Math.Max(0, (int)Math.Floor(sorted[0].YMin));
        var yEnd = Math.Min(pixelH, (int)Math.Ceiling(yMaxD));
        if (yStart >= yEnd) return;

        // Per-row coverage accumulator. Each pixel collects contributions from up to
        // SubSamples sub-scanlines, each sub-scanline contributing [0, 255] (horizontal
        // area coverage). Final alpha = accumulator / SubSamples, capped at 255.
        var coverage = new int[pixelW];
        var hits = new List<EdgeHit>(Math.Min(edges.Count, 64));
        var active = new List<Edge>(Math.Min(edges.Count, 64));
        int pending = 0;

        // Track the pixel-column range that actually has coverage on this row so the
        // blender only iterates over the polygon's footprint — not the full page width.
        // Saves ~60% of blend time on small shapes (short-line glyphs etc.).
        int rowXMin = int.MaxValue, rowXMax = int.MinValue;
        int maxTouchedX = -1; // columns that still hold stale coverage from last row

        for (var y = yStart; y < yEnd; y++)
        {
            // Clear only the columns we touched last row (not the full pixelW).
            if (maxTouchedX >= 0)
            {
                Array.Clear(coverage, rowXMin, Math.Min(maxTouchedX - rowXMin + 1, coverage.Length - rowXMin));
                maxTouchedX = -1;
            }
            rowXMin = int.MaxValue; rowXMax = int.MinValue;

            // Admit edges that become active somewhere inside this row's SubSamples range.
            // Row's latest subY is y + (SubSamples - 0.5) / SubSamples ≤ y + 1.
            while (pending < sorted.Length && sorted[pending].YMin < y + 1)
                active.Add(sorted[pending++]);
            // Retire edges whose YMax has already passed this row. Swap-and-pop avoids
            // the O(n) shift of List.RemoveAt, keeping the full sweep linear in |edges|
            // instead of quadratic on polygons with many simultaneously-active edges.
            for (var i = active.Count - 1; i >= 0; i--)
            {
                if (active[i].YMax <= y)
                {
                    var last = active.Count - 1;
                    if (i != last) active[i] = active[last];
                    active.RemoveAt(last);
                }
            }

            for (var s = 0; s < SubSamples; s++)
            {
                // Sample at the centre of each sub-scanline slice so a rectangle spanning
                // y ∈ [32.5, 33.5] contributes to rows 32 AND 33 instead of collapsing
                // into one row at full opacity.
                var subY = y + (s + 0.5) / SubSamples;

                hits.Clear();
                foreach (var e in active)
                {
                    if (e.YMin <= subY && subY < e.YMax)
                    {
                        var x = e.XAtYMin + (subY - e.YMin) * e.InvSlope;
                        hits.Add(new EdgeHit(x, e.Direction));
                    }
                }
                if (hits.Count < 2) continue;
                hits.Sort(static (p, q) => p.X.CompareTo(q.X));

                // The outermost hits bound this sub-sample's coverage. Clip to [0, pixelW).
                var xLo = Math.Max(0, (int)hits[0].X);
                var xHi = Math.Min(pixelW - 1, (int)hits[hits.Count - 1].X + 1);
                if (xLo < rowXMin) rowXMin = xLo;
                if (xHi > rowXMax) rowXMax = xHi;

                if (evenOdd)
                    AccumulateEvenOdd(hits, coverage, pixelW);
                else
                    AccumulateNonZero(hits, coverage, pixelW);
            }

            if (rowXMax >= rowXMin)
            {
                maxTouchedX = rowXMax;
                BlendRowCoverageRange(pixels, pixelW, y, coverage, rowXMin, rowXMax, r, g, b, a, clipMask, blendMode, knockout, softMask);
            }
        }
    }

    /// <summary>
    /// Blend only columns in [<paramref name="xMin"/>, <paramref name="xMax"/>] — the
    /// polygon's per-row x footprint — rather than scanning every pixel in the row.
    /// </summary>
    private static void BlendRowCoverageRange(byte[] pixels, int pixelW, int y, int[] coverage,
        int xMin, int xMax, byte r, byte g, byte b, byte a, byte[]? clipMask, string blendMode = "Normal",
        bool knockout = false, byte[]? softMask = null)
    {
        if (xMin < 0) xMin = 0;
        if (xMax >= pixelW) xMax = pixelW - 1;
        var rowBase = y * pixelW;
        var pxBase = rowBase * 4;
        int ir = r, ig = g, ib = b, ia = a;
        // PDF 32000 §11.3.5: apply the blend formula B(Cb, Cs) per channel, then
        // alpha-blend the result with the destination at the source's effective
        // alpha (Porter-Duff source-over). Non-separable HSL modes fall back to
        // Normal until BlendModes.BlendChannel grows triple-channel support.
        var mode = BlendModes.Parse(blendMode);
        for (var x = xMin; x <= xMax; x++)
        {
            var cov = coverage[x];
            if (cov <= 0) continue;
            if (clipMask is not null && clipMask[rowBase + x] == 0) continue;
            cov /= SubSamples;
            if (cov > 255) cov = 255;
            var effectiveA = (cov * ia) / 255;
            // Soft mask (PDF 32000 §11.6.5.4): per-pixel alpha multiplied into the
            // fragment's effective alpha; 0 means the pixel is fully masked.
            if (softMask is not null)
            {
                var m = softMask[rowBase + x];
                if (m == 0) continue;
                effectiveA = (effectiveA * m + 127) / 255;
            }
            if (effectiveA <= 0) continue;
            var idx = pxBase + x * 4;

            // Knockout group (PDF 32000 §11.4.4): each fragment composites against
            // the group's original transparent backdrop, so we replace the dst pixel
            // with (src.rgb, effectiveA) — overlapping fills show only the topmost,
            // and partial-coverage edges land at fractional alpha for the parent
            // composite to honour. Blend mode is irrelevant against a transparent
            // backdrop, so it's skipped here.
            if (knockout)
            {
                pixels[idx]     = (byte)ir;
                pixels[idx + 1] = (byte)ig;
                pixels[idx + 2] = (byte)ib;
                pixels[idx + 3] = (byte)effectiveA;
                continue;
            }

            int sR = ir, sG = ig, sB = ib;
            if (mode != BlendMode.Normal)
            {
                BlendModes.Blend(mode, pixels[idx], pixels[idx + 1], pixels[idx + 2],
                    ir, ig, ib, out sR, out sG, out sB);
            }

            if (effectiveA == 255)
            {
                pixels[idx]     = (byte)sR;
                pixels[idx + 1] = (byte)sG;
                pixels[idx + 2] = (byte)sB;
                pixels[idx + 3] = 255;
            }
            else
            {
                int iaInv = 255 - effectiveA;
                pixels[idx]     = (byte)((sR * effectiveA + pixels[idx]     * iaInv + 127) / 255);
                pixels[idx + 1] = (byte)((sG * effectiveA + pixels[idx + 1] * iaInv + 127) / 255);
                pixels[idx + 2] = (byte)((sB * effectiveA + pixels[idx + 2] * iaInv + 127) / 255);
                pixels[idx + 3] = 255;
            }
        }
    }

    /// <summary>
    /// Paint the filled interior of the polygon as a 1-byte-per-pixel stencil (255 = inside).
    /// Used for clip masks. Binary (no AA): a clip mask that softens edges would let
    /// content bleed through the clipped boundary at partial opacity, which is not what
    /// the PDF clipping model prescribes.
    /// </summary>
    public static void BuildMask(EdgeTable edgeTable, byte[] mask, int pixelW, int pixelH, bool evenOdd)
    {
        var edges = edgeTable.Edges;
        if (edges.Count == 0) return;

        // Sort edges by YMin once, then sweep a "pending" pointer alongside an active
        // edge list. Per-row cost drops from O(|edges|) to O(|active edges|), which
        // is the difference between 12s and sub-second on clip-heavy documents
        // (some documents build 534 clip masks, one per text block).
        var sorted = new Edge[edges.Count];
        for (var i = 0; i < edges.Count; i++) sorted[i] = edges[i];
        Array.Sort(sorted, static (a, b) => a.YMin.CompareTo(b.YMin));

        double yMinD = sorted[0].YMin;
        double yMaxD = double.MinValue;
        foreach (var e in edges) if (e.YMax > yMaxD) yMaxD = e.YMax;

        var yStart = Math.Max(0, (int)Math.Floor(yMinD));
        var yEnd = Math.Min(pixelH, (int)Math.Ceiling(yMaxD));

        var active = new List<Edge>(Math.Min(edges.Count, 64));
        var hits = new List<EdgeHit>(Math.Min(edges.Count, 64));
        int pending = 0;
        for (var y = yStart; y < yEnd; y++)
        {
            var sampleY = y + 0.5;

            // Admit newly-starting edges.
            while (pending < sorted.Length && sorted[pending].YMin <= sampleY)
                active.Add(sorted[pending++]);

            // Retire finished edges (swap-and-pop for O(1) removal), then compute hits.
            hits.Clear();
            for (var i = active.Count - 1; i >= 0; i--)
            {
                var e = active[i];
                if (e.YMax <= sampleY)
                {
                    var last = active.Count - 1;
                    if (i != last) active[i] = active[last];
                    active.RemoveAt(last);
                    continue;
                }
                if (e.YMin <= sampleY)
                {
                    var x = e.XAtYMin + (sampleY - e.YMin) * e.InvSlope;
                    hits.Add(new EdgeHit(x, e.Direction));
                }
            }
            if (hits.Count < 2) continue;
            hits.Sort(static (p, q) => p.X.CompareTo(q.X));

            if (evenOdd)
                MaskEvenOdd(hits, mask, pixelW, y);
            else
                MaskNonZero(hits, mask, pixelW, y);
        }
    }

    /// <summary>
    /// Stroke a path with the given line width by expanding each line segment into a
    /// thin rectangle, then filling it. Axis-aligned ~1px strokes are hinted to the
    /// pixel grid to keep hairlines crisp; without hinting, a 1.39-pixel stroke at a
    /// fractional-pixel position smears across three rows at partial coverage, where
    /// the hinted stroke produces either 2-row 50% AA or 1-row bilevel depending
    /// on how close the stroke's sub-pixel position is to the pixel grid.
    /// </summary>
    public static void StrokeLine(byte[] pixels, int pixelW, int pixelH,
        double x0, double y0, double x1, double y1,
        byte r, byte g, byte b, byte a, double lineWidth, byte[]? clipMask = null,
        string blendMode = "Normal", bool knockout = false, byte[]? softMask = null)
    {
        HintAxisAlignedStroke(ref x0, ref y0, ref x1, ref y1, ref lineWidth);

        var hw = lineWidth * 0.5;
        // Clamp to 0.5 so a zero-width line still appears as a single-pixel-wide band
        // (PDF allows line-width 0 meaning "thinnest the device can draw"). Without
        // the clamp, a line-width of 0 would leave the polygon degenerate and invisible.
        if (hw < 0.5) hw = 0.5;

        var dx = x1 - x0;
        var dy = y1 - y0;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 0.001) return;

        // Rectangle around the segment: offsets ±hw along the perpendicular.
        var nx = -dy / len * hw;
        var ny = dx / len * hw;

        var et = new EdgeTable();
        et.AddLine(x0 + nx, y0 + ny, x1 + nx, y1 + ny);
        et.AddLine(x1 + nx, y1 + ny, x1 - nx, y1 - ny);
        et.AddLine(x1 - nx, y1 - ny, x0 - nx, y0 - ny);
        et.AddLine(x0 - nx, y0 - ny, x0 + nx, y0 + ny);

        Fill(et, pixels, pixelW, pixelH, r, g, b, a, false, clipMask, blendMode, knockout, softMask);
    }

    /// <summary>
    /// Position a horizontal or vertical 1-pixel-wide stroke on the device pixel
    /// grid. Covers borders, form-field underlines and mid-page rules:
    ///   1. Offset the path coordinate by <c>+hw</c> (half the raw pixel line width).
    ///      PDF strokes are centred on the path, but this device places them so the
    ///      path becomes the **top** edge in pixel coordinates — without this shift,
    ///      interior 1pt strokes land one row too high.
    ///   2. Snap to the nearest X.0 or X.5 — X.0 produces 2-pixel 50/50 AA straddling
    ///      the row boundary, X.5 produces 1-pixel bilevel on a single row.
    ///   3. When the shifted position lands on X.25 or X.75 (equidistant from both
    ///      snap candidates), don't snap — render with pure AA. This yields the
    ///      75/25 coverage split form-field underlines show.
    /// Line width rounds to 1 (on 100-DPI pages a 1pt PDF stroke is 1.39 device
    /// pixels, quantised down to a clean single device pixel). Non-axis-aligned or
    /// non-~1px strokes pass through unchanged; the rule applies to width=1 only.
    /// </summary>
    private static void HintAxisAlignedStroke(ref double x0, ref double y0,
        ref double x1, ref double y1, ref double lineWidth)
    {
        var roundedW = (int)Math.Round(lineWidth);
        if (roundedW != 1) return;

        var dxAbs = Math.Abs(x1 - x0);
        var dyAbs = Math.Abs(y1 - y0);
        var isHorizontal = dyAbs < 0.001 && dxAbs > 0.001;
        var isVertical = dxAbs < 0.001 && dyAbs > 0.001;
        if (!isHorizontal && !isVertical) return;

        var pos = isHorizontal ? y0 : x0;
        var shifted = pos + lineWidth * 0.5;
        // Nearest snap candidate on the 0.5-pixel grid. The midway positions — X.25
        // and X.75 — sit 0.25 away from both the integer and the half-integer, which
        // we deliberately leave unsnapped so pure-AA 75/25 splits emerge there.
        var snapCandidate = Math.Round(shifted * 2.0) / 2.0;
        const double snapBand = 0.24; // just below 0.25 — excludes the midway case
        var newPos = Math.Abs(shifted - snapCandidate) < snapBand ? snapCandidate : shifted;

        if (isHorizontal)
        {
            y0 = newPos;
            y1 = newPos;
        }
        else
        {
            x0 = newPos;
            x1 = newPos;
        }
        lineWidth = 1.0;
    }

    // ── Fill accumulators ───────────────────────────────────────────────────────

    private static void AccumulateEvenOdd(List<EdgeHit> hits, int[] coverage, int pixelW)
    {
        for (var i = 0; i + 1 < hits.Count; i += 2)
            AccumulateSpan(coverage, hits[i].X, hits[i + 1].X, pixelW);
    }

    private static void AccumulateNonZero(List<EdgeHit> hits, int[] coverage, int pixelW)
    {
        // Non-zero winding: sum edge directions; a span runs from the transition
        // out-of-zero into back-to-zero. Multiple overlapping edges collapse into a
        // single filled region instead of alternating like even-odd.
        var winding = 0;
        var startX = 0.0;
        for (var i = 0; i < hits.Count; i++)
        {
            var prev = winding;
            winding += hits[i].Direction;
            if (prev == 0 && winding != 0)
                startX = hits[i].X;
            else if (prev != 0 && winding == 0)
                AccumulateSpan(coverage, startX, hits[i].X, pixelW);
        }
    }

    /// <summary>
    /// Add the fractional area of the horizontal span [x0, x1] to each pixel column it
    /// overlaps. A span entirely inside one column contributes (x1-x0)*255; spans that
    /// cross multiple columns give full 255 to interior columns and partial 255 to the
    /// left-edge and right-edge columns.
    /// </summary>
    private static void AccumulateSpan(int[] coverage, double x0, double x1, int pixelW)
    {
        if (x1 <= x0) return;
        if (x0 < 0) x0 = 0;
        if (x1 > pixelW) x1 = pixelW;
        if (x0 >= pixelW || x1 <= 0) return;

        var xStart = (int)x0;
        var xEnd = (int)x1;
        if (xEnd >= pixelW) xEnd = pixelW - 1;
        if (xStart >= pixelW) return;

        if (xStart == xEnd)
        {
            // Span falls entirely within one pixel column.
            coverage[xStart] += (int)((x1 - x0) * 255.0 + 0.5);
            return;
        }

        // Left partial: fraction from x0 to xStart+1.
        coverage[xStart] += (int)((xStart + 1 - x0) * 255.0 + 0.5);
        // Fully covered interior columns.
        for (var x = xStart + 1; x < xEnd; x++)
            coverage[x] += 255;
        // Right partial: fraction from xEnd to x1.
        if (xEnd > xStart)
            coverage[xEnd] += (int)((x1 - xEnd) * 255.0 + 0.5);
    }

    private static void BlendRowCoverage(byte[] pixels, int pixelW, int y, int[] coverage,
        byte r, byte g, byte b, byte a, byte[]? clipMask)
    {
        var rowBase = y * pixelW;
        for (var x = 0; x < pixelW; x++)
        {
            if (coverage[x] <= 0) continue;
            // Skip clipped columns before doing any alpha math — honours W / W*.
            if (clipMask is not null && clipMask[rowBase + x] == 0) continue;
            var cov = coverage[x] / SubSamples;
            if (cov > 255) cov = 255;
            var effectiveA = (cov * a) / 255;
            if (effectiveA <= 0) continue;
            BlendPixel(pixels, pixelW, x, y, r, g, b, (byte)effectiveA);
        }
    }

    // ── Mask builders (binary clip) ─────────────────────────────────────────────

    private static void MaskEvenOdd(List<EdgeHit> hits, byte[] mask, int pixelW, int y)
    {
        for (var i = 0; i + 1 < hits.Count; i += 2)
        {
            var x0 = Math.Max(0, (int)Math.Ceiling(hits[i].X));
            var x1 = Math.Min(pixelW - 1, (int)Math.Floor(hits[i + 1].X));
            var row = y * pixelW;
            for (var x = x0; x <= x1; x++) mask[row + x] = 255;
        }
    }

    private static void MaskNonZero(List<EdgeHit> hits, byte[] mask, int pixelW, int y)
    {
        var winding = 0;
        var startX = 0.0;
        for (var i = 0; i < hits.Count; i++)
        {
            var prev = winding;
            winding += hits[i].Direction;
            if (prev == 0 && winding != 0)
                startX = hits[i].X;
            else if (prev != 0 && winding == 0)
            {
                var x0 = Math.Max(0, (int)Math.Ceiling(startX));
                var x1 = Math.Min(pixelW - 1, (int)Math.Floor(hits[i].X));
                var row = y * pixelW;
                for (var x = x0; x <= x1; x++) mask[row + x] = 255;
            }
        }
    }

    // ── Pixel blend ────────────────────────────────────────────────────────────

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static void BlendPixel(byte[] pixels, int pixelW, int x, int y, byte r, byte g, byte b, byte a)
    {
        var idx = (y * pixelW + x) * 4;
        if (idx < 0 || idx + 3 >= pixels.Length) return;

        if (a == 255)
        {
            pixels[idx] = r;
            pixels[idx + 1] = g;
            pixels[idx + 2] = b;
            pixels[idx + 3] = 255;
        }
        else if (a > 0)
        {
            // Integer alpha blend: (src * a + dst * (255 - a)) / 255 with rounding.
            // Cheaper than double-precision math on every covered pixel.
            int ia = a, iaInv = 255 - a;
            pixels[idx]     = (byte)((r * ia + pixels[idx]     * iaInv + 127) / 255);
            pixels[idx + 1] = (byte)((g * ia + pixels[idx + 1] * iaInv + 127) / 255);
            pixels[idx + 2] = (byte)((b * ia + pixels[idx + 2] * iaInv + 127) / 255);
            pixels[idx + 3] = 255;
        }
    }

    private readonly struct EdgeHit
    {
        public readonly double X;
        public readonly int Direction;
        public EdgeHit(double x, int direction) { X = x; Direction = direction; }
    }
}
