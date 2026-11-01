using System.IO;
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
    /// <summary>The raster blit path: the image is drawn through its soft mask and alpha, with the overprint simulation where the state asks for it.</summary>
    private void BlitImageWithMasks(Bitmap bmp, double[] ctm, byte[]? softMask, bool overprint, double alpha)
    {
        var saved = _g.Transform;
        using var world = WorldMatrix(ctm);
        _g.Transform = world;
        if (TryBlitAsDrawingCommand(bmp, world, softMask, overprint, alpha))
        {
            _g.Transform = saved;
            return;
        }
        // Composite a semi-transparent image in straight sRGB (no gamma) so its alpha
        // blend matches the platform renderer — the same reason the shape-fill path
        // forces AssumeLinear (PDF §11.3.6 composites in the device colour space, not
        // linear light). Without this a soft-masked overlay (e.g. a slide's translucent
        // blue/photo panels) composites a few levels too light. Opaque images are
        // unaffected (src fully replaces dst).
        var savedCq = _g.CompositingQuality;
        _g.CompositingQuality = CompositingQuality.AssumeLinear;
        var savedIm = _g.InterpolationMode;
        try
        {
            // Device-space extent of the unit square under the world transform.
            // Elements = [m11, m12, m21, m22, dx, dy]; the u-edge (1,0) and v-edge
            // (0,1) map to (m11,m12) and (m21,m22).
            var e = world.Elements;
            var devW = Math.Sqrt(e[0] * e[0] + e[1] * e[1]);
            var devH = Math.Sqrt(e[2] * e[2] + e[3] * e[3]);
            ChooseBlitInterpolation(bmp, devW, devH);
            var q = MeasureBlitQuad(world, bmp, softMask, overprint, devW, devH);

            var dest = new[]
            {
                new PointF(q.X0, q.Y1), // upper-left  → image top-left
                new PointF(q.X1, q.Y1), // upper-right → image top-right
                new PointF(q.X0, q.Y0), // lower-left  → image bottom-left
            };
            if (softMask is not null) BlitImageMasked(bmp, world, dest, alpha, softMask);
            else if (overprint) BlitImageMultiply(bmp, world, dest);
            else BlitImagePlain(bmp, world, dest, e, q, alpha);
        }
        finally { _g.Transform = saved; _g.CompositingQuality = savedCq; _g.InterpolationMode = savedIm; }
    }

    /// <summary>A vector target takes an opaque unmasked image as a drawing command, and a
    /// translucent one as its own; true when the image was drawn that way.</summary>
    private bool TryBlitAsDrawingCommand(Bitmap bmp, GdiMatrix world, byte[]? softMask,
        bool overprint, double alpha)
    {
        if (!_vectorTarget || softMask is not null || overprint) return false;
        if (alpha >= FullyOpaqueImage) BlitImageAsDrawingCommand(bmp, world);
        else BlitTranslucentImageAsDrawingCommand(bmp, alpha);
        return true;
    }

    /// <summary>The unit-square corners the blit maps the image onto, and whether the blit is
    /// magnified on either axis.</summary>
    private readonly record struct BlitQuad(float X0, float X1, float Y0, float Y1, bool MagnifiedX, bool MagnifiedY)
    {
        public bool Magnified => MagnifiedX || MagnifiedY;
    }

    /// <summary>The corners the image is drawn to, in the unit square the world transform
    /// carries: a sub-pixel-thin strip grown to one device pixel, a magnified blit seated back
    /// half a device pixel, and a near-identity blit snapped to exact 1:1.</summary>
    private BlitQuad MeasureBlitQuad(GdiMatrix world, Bitmap bmp, byte[]? softMask, bool overprint,
        double devW, double devH)
    {
        // LAW: a MAGNIFIED image is seated half a pixel earlier than
        // a naive blit does — see ShiftBlitByHalfDevicePixel for the correction and
        // the evidence. The 1% margin keeps a 1:1 blit — where the correction is
        // nothing and rounding can put the device extent a hair over the source — off
        // the magnified path entirely.
        // The seat is PER AXIS: a picture decimated one way and magnified the other
        // (477x233 into a 417x417 box: x 0.87x, y 1.79x) is seated on y alone. Seated on both, its
        // columns land one device pixel LEFT of the expected render - the exact Version2(4, 0.6)
        // gate fails on one anchor box at its right edge - and seated on neither they sit right
        // but its rows drift: 12,621 pixels 20 or more levels off inside the picture seated both
        // ways, 13,601 unseated, 8,429 seated on the magnified axis only (measured 2026-09-17).
        var magnifiedX = devW > bmp.Width * (1 + MagnifiedBlitMargin);
        var magnifiedY = devH > bmp.Height * (1 + MagnifiedBlitMargin);
        var magnified = magnifiedX || magnifiedY;
        // Sub-pixel-thin blits (e.g. a gradient or raster logo sliced into
        // 1-row scanline strips, each mapped to a fraction of a pixel) average
        // away to nothing under high-quality resampling. Grow such a strip to
        // cover at least one device pixel, centred on its band, so stacked
        // strips accumulate into the intended image instead of vanishing.
        float x0 = 0f, x1 = 1f, y0 = 0f, y1 = 1f;
        if (devW > 1e-6 && devW < 1f) { var f = (float)(1.0 / devW); x0 = 0.5f - f / 2f; x1 = 0.5f + f / 2f; }
        if (devH > 1e-6 && devH < 1f) { var f = (float)(1.0 / devH); y0 = 0.5f - f / 2f; y1 = 0.5f + f / 2f; }
        if (devW < 1f || devH < 1f)
            (x0, x1, y0, y1) = SeatThinStripOnItsPixel(world, devW, devH, x0, x1, y0, y1);

        // A printed page image seats no image back: see DrawPrintedImage.
        if (magnified && !PrintedPageImage)
            (x0, x1, y0, y1) = ShiftBlitByHalfDevicePixel(world, x0, x1, y0, y1, magnifiedX, magnifiedY);

        // LAW (the 1:1 rule): a blit within 1% of source scale is drawn
        // EXACTLY 1:1, one texel per device pixel from the rounded origin. A
        // full-bleed 96 dpi scan (a 962-px source on a 721.601 pt page = 962.135
        // device px) is expected pixel-crisp; resampling 962 texels
        // onto 962.135 px instead smears every pixel by a phase that grows to a
        // seventh of a pixel across the page. The true-DPI page scale (see
        // RenderPageAtPixelSize) is proven at 300 dpi, so the crisp 96 dpi scan can
        // only mean a near-identity blit collapses to identity. Same
        // 1% margin as the magnified test, so every blit lands in exactly one regime.
        if (softMask is null && !overprint
            && Math.Abs(devW - bmp.Width) <= IdentityBlitTolerance * bmp.Width
            && Math.Abs(devH - bmp.Height) <= IdentityBlitTolerance * bmp.Height)
            (x0, x1, y0, y1) = SnapBlitToIdentity(world, bmp, x0, x1, y0, y1);
        return new BlitQuad(x0, x1, y0, y1, magnifiedX, magnifiedY);
    }

    /// <summary>How far, in device pixels, a strip's edge may lie off a pixel line and count as on
    /// it: the page matrix is carried in single precision, a thousandth of a pixel out on a page
    /// of a few thousand rows.</summary>
    private const double StripEdgeSlack = 1.0 / 128;

    /// <summary>
    /// A strip thinner than a pixel whose band holds the centre of a pixel row (or column) is
    /// drawn onto exactly that row. A picture set as hundreds of one-row images, each half a
    /// pixel high under the clip rectangle of its own band, is painted by the strips whose clip
    /// selects a row; grown about the centre of its band such a strip covered that row only in
    /// part, and the paper showed through as a lighter stripe at every row. A strip whose band
    /// holds no row centre keeps the centred growth.
    /// </summary>
    private (float x0, float x1, float y0, float y1) SeatThinStripOnItsPixel(GdiMatrix world, double devW, double devH,
        float x0, float x1, float y0, float y1)
    {
        var e = world.Elements;
        const float AxisAlignedTol = 1e-4f;
        if (Math.Abs(e[1]) > AxisAlignedTol || Math.Abs(e[2]) > AxisAlignedTol || e[0] <= 0f || e[3] >= 0f)
            return (x0, x1, y0, y1);
        var centre = PixelCentre;
        if (devW < 1f && PixelInBand(e[4], e[4] + e[0], centre) is { } column)
            (x0, x1) = ((float)((column - e[4]) / e[0]), (float)((column + 1 - e[4]) / e[0]));
        if (devH < 1f && PixelInBand(e[5] + e[3], e[5], centre) is { } row)
            (y0, y1) = ((float)((row + 1 - e[5]) / e[3]), (float)((row - e[5]) / e[3]));
        return (x0, x1, y0, y1);
    }

    /// <summary>Where a pixel's centre lies past its coordinate on the surface: half a pixel with
    /// the half-pixel offset in force, on the coordinate itself without it.</summary>
    private double PixelCentre => _g.PixelOffsetMode == PixelOffsetMode.None ? 0.0 : HalfDevicePixel;

    /// <summary>The near line of the pixel whose centre lies in the band [from, to), if one does.</summary>
    private static double? PixelInBand(double from, double to, double centre)
    {
        var pixel = Math.Ceiling(from - centre - StripEdgeSlack);
        return pixel + centre < to - StripEdgeSlack ? pixel + centre - HalfDevicePixel : null;
    }

    /// <summary>The resampling kernel this blit takes.</summary>
    private void ChooseBlitInterpolation(Bitmap bmp, double devW, double devH)
    {
        // LAW (minification): an image MINIFIED on EITHER axis resamples through the
        // SOFT kernel. Decimating a scanned text page ~0.95× through bicubic keeps full
        // contrast and rings — 255|9 across a glyph edge where the expected render
        // lands 225|70 — because bicubic's negative lobes sharpen what is already
        // aliasing. The expected minified output matches the prefiltered
        // bilinear, and switching to it removed every mismatch in the minified band
        // of a 300 dpi scan (330 pixels past tolerance → 0). The axes are not
        // weighed together: a 477×233 picture drawn into a 417×417 device box (x
        // decimated 0.87×, y magnified 1.79×) rings 22 pixels past the exact
        // Version2(4, 0.6) gate at its edges under bicubic and none under bilinear —
        // for our own document and for the reference's PDF alike (measured 2026-09-07).
        // A printed page image takes plain bilinear instead, unfiltered: the reference's page
        // image of a 4x-minified one-bit circle mask came out near-binary, every one of its
        // 2,600 pixels matched by plain bilinear, 89 more than 20 levels off under the soft
        // kernel (see PrintedPageImage).
        if (devW < bmp.Width * MinifiedBlitMargin || devH < bmp.Height * MinifiedBlitMargin)
            _g.InterpolationMode = PrintedPageImage ? InterpolationMode.Bilinear : InterpolationMode.HighQualityBilinear;
        // LAW (pixel art): a small image enlarged four times or more keeps hard pixel edges - a 20x20
        // datamatrix drawn 12.7x stays two-level - whatever its depth, colour space or /Interpolate,
        // as the reference draws it (measured: the switch lies between 3.98x and 4.02x on the larger
        // axis scale, and 30 source pixels on the longer side already smooth).
        else if (Math.Max(bmp.Width, bmp.Height) <= PixelArtMaxSourcePixels
                 && Math.Max(devW / bmp.Width, devH / bmp.Height) >= PixelArtMinScale)
            _g.InterpolationMode = InterpolationMode.NearestNeighbor;
    }

    /// <summary>The longest side, in source pixels, of an image drawn as pixel art when enlarged.</summary>
    private const int PixelArtMaxSourcePixels = 29;

    /// <summary>The enlargement (device pixels per source pixel, the larger axis) from which it is.</summary>
    private const double PixelArtMinScale = 4.0;
}