using Aspose.Pdf.Devices.Rasterizer;

namespace Aspose.Pdf.Text;

/// <summary>
/// Rasterizes TrueType glyph outlines into alpha masks using the scanline rasterizer.
/// Uses 4x supersampling for anti-aliased rendering.
/// </summary>
internal static class GlyphRasterizer
{
    private const int SuperSample = 4; // 4x supersampling for anti-aliasing

    /// <summary>
    /// Rasterize a glyph outline to a single-channel alpha mask with anti-aliasing.
    /// </summary>
    public static byte[]? Rasterize(GlyphOutline outline, int unitsPerEm, double fontSize,
        double scale, out int width, out int height, out int bearingX, out int bearingY,
        double horizontalScale = 1.0)
    {
        width = height = bearingX = bearingY = 0;
        if (outline.Contours.Length == 0) return null;

        var pixelScale = fontSize * scale / unitsPerEm;
        var hPixelScale = pixelScale * horizontalScale; // narrower for condensed fonts

        // Compute bounding box in pixel space
        var pxMin = outline.XMin * hPixelScale;
        var pyMin = -outline.YMax * pixelScale;
        var pxMax = outline.XMax * hPixelScale;
        var pyMax = -outline.YMin * pixelScale;

        width = (int)Math.Ceiling(pxMax - pxMin) + 2;
        height = (int)Math.Ceiling(pyMax - pyMin) + 2;
        if (width <= 0 || height <= 0 || width > 512 || height > 512) return null;

        bearingX = (int)Math.Floor(pxMin);
        bearingY = (int)Math.Floor(pyMin);

        // Render at SuperSample× resolution
        var ssW = width * SuperSample;
        var ssH = height * SuperSample;
        var ssScaleY = pixelScale * SuperSample;
        var ssScaleX = hPixelScale * SuperSample;
        var ssOffX = -bearingX * SuperSample;
        var ssOffY = -bearingY * SuperSample;

        var edgeTable = new EdgeTable();
        foreach (var contour in outline.Contours)
        {
            if (contour.Length < 2) continue;
            BuildContourEdges(edgeTable, contour, ssScaleX, ssScaleY, ssOffX, ssOffY);
        }

        // Rasterize at high resolution
        var rgbaSize = ssW * ssH * 4;
        if (rgbaSize <= 0 || rgbaSize > 16 * 1024 * 1024) return null;

        var rgba = new byte[rgbaSize];
        ScanlineFiller.Fill(edgeTable, rgba, ssW, ssH, 255, 255, 255, 255, false);

        // Downsample: average SuperSample×SuperSample blocks to produce anti-aliased alpha
        var alpha = new byte[width * height];
        var ss2 = SuperSample * SuperSample;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var sum = 0;
                for (var sy = 0; sy < SuperSample; sy++)
                {
                    var row = y * SuperSample + sy;
                    if (row >= ssH) continue;
                    for (var sx = 0; sx < SuperSample; sx++)
                    {
                        var col = x * SuperSample + sx;
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
        double scaleX, double scaleY, double offsetX, double offsetY)
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

            var x0 = p0.X * scaleX + offsetX;
            var y0 = -p0.Y * scaleY + offsetY;

            // Check if there's an off-curve point between i0 and i1
            var midIdx = (i0 + 1) % expanded.Count;
            if (midIdx == i1)
            {
                // Direct line between on-curve points
                var p1 = expanded[i1];
                edgeTable.AddLine(x0, y0, p1.X * scaleX + offsetX, -p1.Y * scaleY + offsetY);
            }
            else
            {
                // Quadratic bezier: on-curve → off-curve → on-curve
                var cp = expanded[midIdx];
                var p1 = expanded[i1];
                edgeTable.AddQuadBezier(x0, y0,
                    cp.X * scaleX + offsetX, -cp.Y * scaleY + offsetY,
                    p1.X * scaleX + offsetX, -p1.Y * scaleY + offsetY);
            }
        }
    }
}
