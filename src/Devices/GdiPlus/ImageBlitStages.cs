using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Text;
using GdiColor = System.Drawing.Color;
using GdiMatrix = System.Drawing.Drawing2D.Matrix;
using GraphicsState = Aspose.Pdf.Content.GraphicsState;
using GdiState = System.Drawing.Drawing2D.GraphicsState;

namespace Aspose.Pdf.Devices;

public sealed partial class GdiPlusPageRenderer
{
    /// <summary>The stages of the image blit: the plain (unmasked, non-overprint) draw.</summary>
    private void BlitImagePlain(Bitmap bmp, System.Drawing.Drawing2D.Matrix world, PointF[] dest, float[] e, BlitQuad quad, double alpha)
    {
        // WrapMode.TileFlipXY: at the image boundary a high-quality (bicubic)
        // resample otherwise samples the pixels *outside* the source — which are
        // transparent since the page backdrop is bare paper — bleeding partial
        // alpha and a darkened colour into the edge row/column. Over the former
        // opaque-white backdrop this went unnoticed; on the coverage-alpha page it
        // flattens to off-white (e.g. an opaque white scan edge lands at 254
        // not 255). Clamping the sampler to the edge texel keeps the border
        // exact. The alpha branch also carries the /ca image opacity via a matrix.
        using var ia = new ImageAttributes();
        ia.SetWrapMode(System.Drawing.Drawing2D.WrapMode.TileFlipXY);
        if (PrintedPageImage)
        {
            if (alpha < 0.999)
                ia.SetColorMatrix(new ColorMatrix { Matrix33 = (float)Math.Max(0.0, Math.Min(1.0, alpha)) });
            DrawPrintedImage(_g, bmp, world, dest, ia);
            return;
        }
        if (alpha < 0.999)
        {
            var cm = new ColorMatrix { Matrix33 = (float)Math.Max(0.0, Math.Min(1.0, alpha)) };
            ia.SetColorMatrix(cm);
        }
        // LAW (the border, magnified only): an axis-aligned magnified
        // image covers device pixels [ceil(x0), ceil(x1)) × [ceil(y0), ceil(y1))
        // of its UNSEATED extent, painted hard — probed on a 300 dpi ladder of
        // sub-pixel left edges (50.0/50.17/…/50.83 start at columns 50/51/51/51/
        // 51/51), witnessed on a scan whose banner at 87.5 px starts hard at 88.
        // Enforced as a CLIP around the seated blit: the seat owns the interior
        // phase (it measured exact), the clip owns the border, and neither
        // disturbs the other — the source-rewindow and destination-translation
        // forms of this rule were both tried and measured worse. The seated
        // content edge sits half a pixel inside the clip on each side, and the
        // sampler's mirrored edge texel carries full strength to the clip line.
        // A MINIFIED border stays soft (its half-covered edge pixels match the
        // reference as-is — clipping them was measured and lost the match).
        // The clip is put back through a native save of the surface rather than a copy of
        // the clip region: a page of many small images under a clip paid for the copy and
        // its reinstatement at every one (see NativeGraphicsState).
        var source = new RectangleF(0, 0, bmp.Width, bmp.Height);
        if (quad.Magnified && Math.Abs(e[1]) < 1e-4f && Math.Abs(e[2]) < 1e-4f
            && e[0] > 0f && e[3] < 0f)
        {
            var hardL = (float)Math.Ceiling(e[4]);
            var hardT = (float)Math.Ceiling(e[5] + e[3]);
            var hardR = (float)Math.Ceiling(e[4] + e[0]);
            var hardB = (float)Math.Ceiling(e[5]);
            (hardL, hardT) = YieldNearBorderToClip(hardL, hardT, e);
            if (hardR - hardL >= 1 && hardB - hardT >= 1)
            {
                BlitInsideHardBorder(bmp, world, dest, source, e, quad, ia,
                    RectangleF.FromLTRB(hardL, hardT, hardR, hardB));
                return;
            }
        }
        _g.DrawImage(bmp, dest, source, GraphicsUnit.Pixel, ia);
    }

    /// <summary>The shorter side, in device pixels, from which a hard border has its body and
    /// its far bands drawn apart: a smaller image, or a strip, is drawn by the one carried blit.</summary>
    private const float SplitBlitMinSide = 64;

    /// <summary>
    /// Draws a seated blit inside its hard border, carried to the border's far lines (see
    /// <see cref="ReachHardBorder"/>). A blit whose source window reaches past the image is
    /// resampled by the surface's slow path over its whole area - a third longer for a page that
    /// is one picture - so a large image is drawn in two parts: its body as the plain blit, and
    /// only the bands along the far lines, where the two differ, as the carried one.
    /// </summary>
    private void BlitInsideHardBorder(Bitmap bmp, GdiMatrix world, PointF[] dest, RectangleF source, float[] e,
        BlitQuad quad, ImageAttributes ia, RectangleF hard)
    {
        // The body ends a pixel before the row and column the seated blit's own edge falls in.
        var body = RectangleF.FromLTRB(hard.Left, hard.Top,
            quad.MagnifiedX ? Math.Min(hard.Right, (float)Math.Floor(e[4] + e[0] - HalfDevicePixel) - 1) : hard.Right,
            quad.MagnifiedY ? Math.Min(hard.Bottom, (float)Math.Floor(e[5] - HalfDevicePixel) - 1) : hard.Bottom);
        var split = Math.Min(hard.Width, hard.Height) >= SplitBlitMinSide && body.Width >= 1 && body.Height >= 1;
        if (split)
        {
            var bodySurface = SaveSurface();
            try
            {
                ClipToDeviceRectangle(body, world, exclude: false);
                _g.DrawImage(bmp, dest, source, GraphicsUnit.Pixel, ia);
            }
            finally { RestoreSurface(bodySurface); }
        }

        // The clip is put back through a native save of the surface rather than a copy of
        // the clip region: a page of many small images under a clip paid for the copy and
        // its reinstatement at every one (see NativeGraphicsState).
        var saved = SaveSurface();
        try
        {
            ClipToDeviceRectangle(hard, world, exclude: false);
            if (split) ClipToDeviceRectangle(body, world, exclude: true);
            var carried = ReachHardBorder(dest, source, e, quad);
            _g.DrawImage(bmp, dest, carried, GraphicsUnit.Pixel, ia);
        }
        finally { RestoreSurface(saved); }
    }

    /// <summary>Narrows the clip to a rectangle of device pixels, or takes the rectangle out of
    /// it, and leaves the blit's world transform in force.</summary>
    private void ClipToDeviceRectangle(RectangleF rect, GdiMatrix world, bool exclude)
    {
        // The rectangle is in device pixels; it is applied under an identity transform.
        using (var id = new GdiMatrix())
        {
            _g.Transform = id;
            if (exclude) _g.SetClip(rect, CombineMode.Exclude);
            else _g.IntersectClip(rect);
        }
        _g.Transform = world;
    }

    /// <summary>How near, in device pixels, the clip's first row lies to an image's edge for the
    /// image to be a slice cut along that edge: the clip takes a row by its centre, half a pixel
    /// from where the image's own border would start.</summary>
    private const double ClipSeamTolerance = HalfDevicePixel + StripEdgeSlack;

    /// <summary>
    /// An image cut by the clip along its own edge is a slice of a larger picture, and the row
    /// the clip starts on is that slice's to paint. The border law starts an image on the first
    /// pixel line inside it, the clip takes the row whose centre is inside: where the two differ
    /// the row belonged to neither slice and stayed white. There the border starts with the clip.
    /// </summary>
    private (float left, float top) YieldNearBorderToClip(float hardL, float hardT, float[] e)
    {
        // The clip's bounds come back in the unit square the world transform carries (v grows
        // up the page), as whole pixels moved by the surface's pixel offset.
        var clip = _g.ClipBounds;
        var centre = PixelCentre;
        var clipLeft = Math.Round(clip.Left * e[0] + e[4] - centre);
        var clipTop = Math.Round(clip.Bottom * e[3] + e[5] - centre);
        if (clipLeft < hardL && Math.Abs(clipLeft - e[4]) <= ClipSeamTolerance) hardL = (float)clipLeft;
        if (clipTop < hardT && Math.Abs(clipTop - (e[5] + e[3])) <= ClipSeamTolerance) hardT = (float)clipTop;
        return (hardL, hardT);
    }

    /// <summary>How far past its seated extent a magnified blit is carried on the far sides, in
    /// device pixels: the half pixel of the seat and the whole pixel the border's ceiling can add.</summary>
    private const double HardBorderReach = 1.5;

    /// <summary>
    /// Carries a seated blit to the far lines of its hard border. Seated half a pixel back, the
    /// image ended half a pixel to a pixel and a half short of the border's right and bottom
    /// lines, so its last column and row were painted in part or not at all: a picture set as
    /// one-row strips, each magnified to two and a half pixels, showed a white row at every strip.
    /// The source window grows with the destination - the sampler's mirrored edge texel - so the
    /// interior keeps its phase, and the border clip cuts what lies beyond.
    /// </summary>
    /// <returns>The source window of the carried blit; <paramref name="dest"/> is moved in place.</returns>
    private static RectangleF ReachHardBorder(PointF[] dest, RectangleF source, float[] e, BlitQuad quad)
    {
        // dest = upper-left, upper-right, lower-left of the unit rectangle; v grows up the page.
        float spanU = dest[1].X - dest[0].X, spanV = dest[0].Y - dest[2].Y;
        if (quad.MagnifiedX && spanU > 0f)
        {
            var reach = (float)(HardBorderReach / e[0]);
            dest[1].X += reach;
            source.Width += source.Width * reach / spanU;
        }
        if (quad.MagnifiedY && spanV > 0f)
        {
            var reach = (float)(HardBorderReach / -e[3]);
            dest[2].Y -= reach;
            source.Height += source.Height * reach / spanV;
        }
        return source;
    }
}
