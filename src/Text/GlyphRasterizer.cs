using Aspose.Pdf.Devices.Rasterizer;

namespace Aspose.Pdf.Text;

/// <summary>
/// Rasterizes TrueType glyph outlines into alpha masks using the scanline rasterizer.
/// Uses 4x supersampling for anti-aliased rendering.
/// </summary>
internal static class GlyphRasterizer
{
    private const int SuperSample = 4; // 4x supersampling for anti-aliasing

    /// <summary>Largest glyph mask side, in device pixels. Beyond a few thousand pixels a
    /// single glyph is no longer page content, and the mask alone would dwarf the page
    /// buffer.</summary>
    private const int MaxGlyphSide = 4096;

    /// <summary>Supersample factor for a mask of this size. Small glyphs keep the full 4x
    /// (they need it — one pixel of edge error is a large fraction of the shape); bigger
    /// ones step down so the intermediate buffer stays within the same allocation budget
    /// the 4x factor implies at the old 512 px cap.</summary>
    private static int SuperSampleFor(int width, int height)
    {
        long area = (long)width * height;
        if (area <= 512L * 512L) return SuperSample;      // <=  4 MB supersampled RGBA
        if (area <= 1024L * 1024L) return 2;
        return 1;
    }

    /// <summary>
    /// Rasterize a glyph outline to a single-channel alpha mask with anti-aliasing.
    /// </summary>
    public static byte[]? Rasterize(GlyphOutline outline, int unitsPerEm, double fontSize,
        double scale, out int width, out int height, out int bearingX, out int bearingY,
        double horizontalScale = 1.0)
    {
        // The upright case stated as the general one: x grows right and device y grows
        // DOWN, which is what the negated d says.
        var pixelScale = fontSize * scale / unitsPerEm;
        return RasterizeTransformed(outline, pixelScale * horizontalScale, 0, 0, -pixelScale,
            out width, out height, out bearingX, out bearingY);
    }

    /// <summary>
    /// Rasterize an outline through an arbitrary 2x2 map from FONT UNITS to device pixels
    /// (device y pointing down), so a glyph can carry the rotation or skew its text matrix
    /// asks for. The mask comes back in its own upright buffer;
    /// <paramref name="bearingX"/> and <paramref name="bearingY"/> place its top-left
    /// corner relative to the glyph origin.
    /// </summary>
    public static byte[]? RasterizeTransformed(GlyphOutline outline,
        double a, double b, double c, double d,
        out int width, out int height, out int bearingX, out int bearingY)
    {
        width = height = bearingX = bearingY = 0;
        if (outline.Contours.Length == 0) return null;

        // XMin/XMax describe the upright box only, so a rotated map has to measure the
        // transformed points. Control points count too: they can overshoot the curve, and
        // a mask a shade too big is harmless where a clipped one is not.
        double pxMin = double.MaxValue, pyMin = double.MaxValue;
        double pxMax = double.MinValue, pyMax = double.MinValue;
        foreach (var contour in outline.Contours)
        {
            foreach (var pt in contour)
            {
                var tx = pt.X * a + pt.Y * c;
                var ty = pt.X * b + pt.Y * d;
                if (tx < pxMin) pxMin = tx;
                if (tx > pxMax) pxMax = tx;
                if (ty < pyMin) pyMin = ty;
                if (ty > pyMax) pyMax = ty;
            }
        }
        if (pxMin > pxMax) return null;

        width = (int)Math.Ceiling(pxMax - pxMin) + 2;
        height = (int)Math.Ceiling(pyMax - pyMin) + 2;
        // Display sizes are legitimate content: a 175 pt plate number at 300 dpi is ~730 px
        // tall, and a hard 512 px side dropped it SILENTLY — the whole run vanished while
        // the 12 pt labels beside it drew. Bound the WORK instead of the size: the glyph is
        // capped only where a mask could no longer be a page feature, and the supersample
        // factor steps down as the glyph grows, which keeps the intermediate buffer inside
        // the same budget. Large glyphs need less AA anyway — the edge error is a shrinking
        // fraction of the shape.
        if (width <= 0 || height <= 0 || width > MaxGlyphSide || height > MaxGlyphSide) return null;

        bearingX = (int)Math.Floor(pxMin);
        bearingY = (int)Math.Floor(pyMin);

        var superSample = SuperSampleFor(width, height);

        // Render at superSample× resolution
        var ssW = width * superSample;
        var ssH = height * superSample;
        var ssA = a * superSample;
        var ssB = b * superSample;
        var ssC = c * superSample;
        var ssD = d * superSample;
        var ssOffX = -bearingX * superSample;
        var ssOffY = -bearingY * superSample;

        var edgeTable = new EdgeTable();
        foreach (var contour in outline.Contours)
        {
            if (contour.Length < 2) continue;
            BuildContourEdges(edgeTable, contour, ssA, ssB, ssC, ssD, ssOffX, ssOffY);
        }

        // Rasterize at high resolution
        var rgbaSize = ssW * ssH * 4;
        if (rgbaSize <= 0 || rgbaSize > 16 * 1024 * 1024) return null;

        var rgba = new byte[rgbaSize];
        ScanlineFiller.Fill(edgeTable, rgba, ssW, ssH, 255, 255, 255, 255, false);

        // Downsample: average superSample×superSample blocks to produce anti-aliased alpha
        var alpha = new byte[width * height];
        var ss2 = superSample * superSample;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var sum = 0;
                for (var sy = 0; sy < superSample; sy++)
                {
                    var row = y * superSample + sy;
                    if (row >= ssH) continue;
                    for (var sx = 0; sx < superSample; sx++)
                    {
                        var col = x * superSample + sx;
                        if (col >= ssW) continue;
                        sum += rgba[(row * ssW + col) * 4]; // R channel
                    }
                }
                alpha[y * width + x] = (byte)(sum / ss2);
            }
        }

        return alpha;
    }

    private static void BuildContourEdges(EdgeTable edgeTable, ContourPoint[] contour,
        double a, double b, double c, double d, double offsetX, double offsetY)
    {
        // Step 1: Expand TrueType contour — insert implied on-curve midpoints
        // between consecutive off-curve points
        var expanded = new List<ContourPoint>();
        var n = contour.Length;
        for (var i = 0; i < n; i++)
        {
            var curr = contour[i];
            var next = contour[(i + 1) % n];
            expanded.Add(curr);
            if (!curr.OnCurve && !next.OnCurve)
                expanded.Add(new ContourPoint((curr.X + next.X) * 0.5, (curr.Y + next.Y) * 0.5, true));
        }

        // Step 2: Collect on-curve points as segment endpoints.
        // Between each pair of on-curve points there is either:
        //   - nothing (straight line)
        //   - one off-curve point (quadratic bezier)
        var onCurveIndices = new List<int>();
        for (var i = 0; i < expanded.Count; i++)
            if (expanded[i].OnCurve) onCurveIndices.Add(i);

        if (onCurveIndices.Count < 2) return;

        // Step 3: For each consecutive pair of on-curve points, emit line or bezier
        for (var k = 0; k < onCurveIndices.Count; k++)
        {
            var i0 = onCurveIndices[k];
            var i1 = onCurveIndices[(k + 1) % onCurveIndices.Count];
            var p0 = expanded[i0];

            var x0 = p0.X * a + p0.Y * c + offsetX;
            var y0 = p0.X * b + p0.Y * d + offsetY;

            // Check if there's an off-curve point between i0 and i1
            var midIdx = (i0 + 1) % expanded.Count;
            if (midIdx == i1)
            {
                // Direct line between on-curve points
                var p1 = expanded[i1];
                edgeTable.AddLine(x0, y0, p1.X * a + p1.Y * c + offsetX, p1.X * b + p1.Y * d + offsetY);
            }
            else
            {
                // Quadratic bezier: on-curve → off-curve → on-curve
                var cp = expanded[midIdx];
                var p1 = expanded[i1];
                edgeTable.AddQuadBezier(x0, y0,
                    cp.X * a + cp.Y * c + offsetX, cp.X * b + cp.Y * d + offsetY,
                    p1.X * a + p1.Y * c + offsetX, p1.X * b + p1.Y * d + offsetY);
            }
        }
    }
}
