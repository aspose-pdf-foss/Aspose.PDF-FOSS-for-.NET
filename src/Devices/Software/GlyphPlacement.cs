using Aspose.Pdf.Content;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Devices;

public sealed partial class SoftwarePageRenderer
{
    /// <summary>What the switch answers when the environment does not: exact placement
    /// draws every glyph at the fractional device position its text matrix gives it; the
    /// snapped placement truncates the pen to the pixel before the outline is scanned.</summary>
    private const bool ExactGlyphPlacementDefault = true;

    /// <summary>Switch of the glyph placement, read once: <c>ASPOSE_PDF_GLYPH_PLACEMENT</c>
    /// set to <c>exact</c> or <c>snapped</c> overrides <see cref="ExactGlyphPlacementDefault"/>,
    /// so the two placements can be compared over a corpus without a rebuild; a test sets it
    /// for the span of its render. A glyph origin lands anywhere inside its pixel, and
    /// snapping it to the pixel's corner moved the ink up to a pixel left and up of its
    /// place - half a pixel on average, which is what a render compared against another
    /// rasteriser's page shows at every glyph top.</summary>
    internal static bool ExactGlyphPlacement { get; set; } =
        Environment.GetEnvironmentVariable("ASPOSE_PDF_GLYPH_PLACEMENT") switch
        {
            "exact" => true,
            "snapped" => false,
            _ => ExactGlyphPlacementDefault,
        };

    /// <summary>Rasterise one glyph with its origin at the exact fractional pen position and
    /// put it on the page. The outline is shifted by the origin's position inside its pixel
    /// before it is scanned, so the anti-aliased coverage carries the fraction and the mask
    /// is blitted at the pixel the origin is in. An upright run maps through the axis-aligned
    /// scale; a rotated or skewed run through its em matrix along its own baseline, where
    /// <paramref name="penX"/> is a distance along that baseline.</summary>
    private static void BlitGlyphAtPen(RenderContext ctx, GlyphOutline outline, int unitsPerEm,
        double effectiveSize, double hScale, double penX, double penY,
        byte r, byte g, byte b, byte a)
    {
        var (ma, mb, mc, md, ox, oy) = GlyphDeviceMap(ctx, unitsPerEm, effectiveSize, hScale, penX, penY);
        var pixelX = (int)Math.Floor(ox);
        var pixelY = (int)Math.Floor(oy);
        if (GlyphRasterizer.RasterizeTransformed(outline, ma, mb, mc, md, ox - pixelX, oy - pixelY)
            is (var mask, var width, var height, var bearingX, var bearingY))
            BlitAlphaMask(ctx, mask, width, height, pixelX + bearingX, pixelY + bearingY, r, g, b, a);
    }

    /// <summary>The map of a glyph's font units onto device pixels, as the 2x2 part (a, b, c, d)
    /// and the glyph origin (x, y): through the run's em matrix along its own baseline when the
    /// text matrix rotates or skews, else the axis-aligned scale with the font's y axis negated,
    /// device y growing down.</summary>
    private static (double a, double b, double c, double d, double x, double y) GlyphDeviceMap(
        RenderContext ctx, int unitsPerEm, double effectiveSize, double hScale, double penX, double penY)
    {
        if (ctx.GlyphEmMatrix is { } em)
        {
            var (x, y) = EmPenOrigin(ctx, penX, penY);
            return (em[0] / unitsPerEm, em[1] / unitsPerEm, em[2] / unitsPerEm, em[3] / unitsPerEm, x, y);
        }
        var pixelScale = effectiveSize * ctx.Scale / unitsPerEm;
        return (pixelScale * hScale, 0, 0, -pixelScale, penX, penY);
    }

    /// <summary>The device position of the upright pen (penX, penY) of a run drawn through its em
    /// matrix: the distance along the baseline, and the distance down - a vertical run's column
    /// and its glyphs' vertical origins - along the text space's -y axis.</summary>
    private static (double x, double y) EmPenOrigin(RenderContext ctx, double penX, double penY)
    {
        var along = penX - ctx.GlyphOriginX;
        var down = penY - ctx.GlyphOriginY;
        return (ctx.GlyphOriginX + along * ctx.BaselineUx + down * ctx.PenDownX,
            ctx.GlyphOriginY + along * ctx.BaselineUy + down * ctx.PenDownY);
    }

    /// <summary>Stroke one glyph's outline with the run's stroking state (Tr 1, 2, 5 and 6, PDF
    /// 32000 §9.3.6). The outline reaches the page through the map its fill takes, and comes
    /// back through the page's and the CTM's inverse into user space, so it is stroked as a
    /// path drawn there would be: the line width, dash, joins, stroking colour and /CA, blend
    /// mode and clip all apply. Without it a run that strokes its text drew the fill alone,
    /// and an outlined stamp whose fill is transparent left its page blank.</summary>
    private static void StrokeGlyphOutline(RenderContext ctx, GlyphOutline outline, int unitsPerEm,
        double effectiveSize, double hScale, double penX, double penY, GraphicsState stroke)
    {
        var ctm = stroke.Ctm;
        var det = ctm[0] * ctm[3] - ctm[1] * ctm[2];
        if (Math.Abs(det) < SingularCtmDeterminant) return;
        var (ma, mb, mc, md, ox, oy) = GlyphDeviceMap(ctx, unitsPerEm, effectiveSize, hScale, penX, penY);
        (double X, double Y) ToUser(ContourPoint p)
        {
            var x = (ox + p.X * ma + p.Y * mc) / ctx.Scale + ctx.MediaBox.LLX - ctm[4];
            var y = (ctx.PixelH - (oy + p.X * mb + p.Y * md)) / ctx.Scale + ctx.MediaBox.LLY - ctm[5];
            return ((ctm[3] * x - ctm[2] * y) / det, (ctm[0] * y - ctm[1] * x) / det);
        }

        var segments = new List<PathCommand>();
        foreach (var contour in outline.Contours) AddGlyphContour(segments, contour, ToUser);
        // The stroke picks its own blend; the run's next glyph fills with the text's.
        var blend = ctx.CurrentBlendMode;
        var softMask = ctx.SoftMaskAlpha;
        DrawPath(ctx, segments, "S", stroke);
        ctx.CurrentBlendMode = blend;
        ctx.SoftMaskAlpha = softMask;
    }

    /// <summary>Below this a CTM maps user space onto a line or a point, and nothing it
    /// strokes has an inverse to come back through.</summary>
    private const double SingularCtmDeterminant = 1e-12;

    /// <summary>One closed contour of a glyph as path commands: on-curve points joined by lines,
    /// an off-curve point between two of them a quadratic segment, written as its cubic; two
    /// off-curve points in a row imply the on-curve point midway, as the glyph rasteriser
    /// reads them.</summary>
    private static void AddGlyphContour(List<PathCommand> segments, ContourPoint[] contour,
        Func<ContourPoint, (double X, double Y)> toUser)
    {
        var expanded = new List<ContourPoint>(contour.Length * 2);
        for (var i = 0; i < contour.Length; i++)
        {
            var current = contour[i];
            var next = contour[(i + 1) % contour.Length];
            expanded.Add(current);
            if (!current.OnCurve && !next.OnCurve)
                expanded.Add(new ContourPoint((current.X + next.X) * 0.5, (current.Y + next.Y) * 0.5, true));
        }
        var start = expanded.FindIndex(p => p.OnCurve);
        if (start < 0 || expanded.Count < 2) return;

        var from = toUser(expanded[start]);
        segments.Add(new PathCommand(PathOp.MoveTo, from.X, from.Y));
        for (var k = 1; k <= expanded.Count; k++)
        {
            var point = expanded[(start + k) % expanded.Count];
            if (point.OnCurve)
            {
                if (k == expanded.Count) break; // back at the start: the close draws that side
                from = toUser(point);
                segments.Add(new PathCommand(PathOp.LineTo, from.X, from.Y));
                continue;
            }
            // An off-curve point is never last: the contour closes on its first on-curve one.
            k++;
            var control = toUser(point);
            var to = toUser(expanded[(start + k) % expanded.Count]);
            segments.Add(new PathCommand(PathOp.CurveTo,
                from.X + (control.X - from.X) * QuadToCubic, from.Y + (control.Y - from.Y) * QuadToCubic,
                to.X + (control.X - to.X) * QuadToCubic, to.Y + (control.Y - to.Y) * QuadToCubic,
                to.X, to.Y));
            from = to;
        }
        segments.Add(new PathCommand(PathOp.Close));
    }

    /// <summary>How far toward a quadratic segment's control point its cubic's two control
    /// points lie, from either end.</summary>
    private const double QuadToCubic = 2.0 / 3.0;
}
