using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using GdiColor = System.Drawing.Color;
using GdiMatrix = System.Drawing.Drawing2D.Matrix;
using GraphicsState = Aspose.Pdf.Content.GraphicsState;

namespace Aspose.Pdf.Devices;

/// <summary>How a page printed as an image draws its images and soft masks: as the reference's own
/// render of the page does (see <see cref="GdiPlusPageRenderer.PrintedPageImage"/>).</summary>
public sealed partial class GdiPlusPageRenderer
{
    /// <summary>
    /// Draw an image into <paramref name="dest"/> (the unit square's corners under
    /// <paramref name="world"/>) the way the reference's page render does: plain bilinear
    /// resampling, and hard edges.
    /// </summary>
    /// <remarks>
    /// <para>Measured on a photo and a logo drawn at 0.6x, 0.98x, 1x, 1.03x and 2x and turned 3.5
    /// degrees, against the reference's render at 150 DPI (pixels 10 or more levels off):</para>
    /// <para>Resampled with plain bilinear and seated where the page puts it, not half a pixel back:
    /// the 1.03x photo 37,057 -> 0.</para>
    /// <para>Hard edges: an upright image covers exactly the pixels [ceil(left), ceil(right)) x
    /// [ceil(top), ceil(bottom)), each fully painted - a 0.6x photo whose left edge lies at 42.29
    /// left column 42 bare and column 43 solid. So the image is clipped to that rectangle and
    /// drawn, and the ring of pixels just inside the clip is painted again from a draw one pixel
    /// larger every way round, whose mirrored edge texels reach past the clip. The interior keeps
    /// the unextended draw: extending a shrunk image's draw moves its sampling phase. A turned
    /// image is clipped to its own parallelogram, which GDI+ rasterises aliased, and drawn one
    /// pixel larger. Together: every case 0 but the 2x photo (4) and the turned one (28).</para>
    /// </remarks>
    private void DrawPrintedImage(Graphics g, Bitmap bmp, GdiMatrix world, PointF[] dest, ImageAttributes? attributes)
    {
        using var ownAttributes = attributes is null ? new ImageAttributes() : null;
        var ia = attributes ?? ownAttributes!;
        ia.SetWrapMode(WrapMode.TileFlipXY);
        var savedInterpolation = g.InterpolationMode;
        var savedClip = g.Clip;
        g.InterpolationMode = InterpolationMode.Bilinear;
        try
        {
            var e = world.Elements;
            var source = new RectangleF(0, 0, bmp.Width, bmp.Height);
            var (extendedDest, extendedSource) = ExtendByOnePixel(bmp, e, dest);
            if (Math.Abs(e[1]) < UprightTolerance && Math.Abs(e[2]) < UprightTolerance && e[0] > 0f && e[3] < 0f)
            {
                var hard = RectangleF.FromLTRB((float)Math.Ceiling(e[4]), (float)Math.Ceiling(e[5] + e[3]),
                    (float)Math.Ceiling(e[4] + e[0]), (float)Math.Ceiling(e[5]));
                if (hard.Width < 1 || hard.Height < 1)
                {
                    g.DrawImage(bmp, dest, source, GraphicsUnit.Pixel, ia);
                    return;
                }
                IntersectDeviceClip(g, world, hard);
                g.DrawImage(bmp, dest, source, GraphicsUnit.Pixel, ia);
                ExcludeDeviceClip(g, world, RectangleF.Inflate(hard, -1, -1));
                g.DrawImage(bmp, extendedDest, extendedSource, GraphicsUnit.Pixel, ia);
                return;
            }
            using (var parallelogram = new GraphicsPath())
            {
                parallelogram.AddPolygon(new[] { dest[0], dest[1], new PointF(dest[1].X, dest[2].Y), dest[2] });
                parallelogram.Transform(world);
                using var region = new Region(parallelogram);
                g.ResetTransform();
                g.IntersectClip(region);
                g.Transform = world;
            }
            g.DrawImage(bmp, extendedDest, extendedSource, GraphicsUnit.Pixel, ia);
        }
        finally
        {
            g.Transform = world;
            g.Clip = savedClip;
            savedClip.Dispose();
            g.InterpolationMode = savedInterpolation;
        }
    }

    /// <summary>The largest shear or rotation term an image mapping may carry and still count as
    /// upright.</summary>
    private const float UprightTolerance = 1e-4f;

    /// <summary>The destination and source of the same draw, one device pixel larger on every side:
    /// the source grows by the texels that pixel maps to, so the mapping itself is unchanged.</summary>
    private static (PointF[] Dest, RectangleF Source) ExtendByOnePixel(Bitmap bmp, float[] e, PointF[] dest)
    {
        var du = 1f / (float)Math.Sqrt(e[0] * e[0] + e[1] * e[1]);
        var dv = 1f / (float)Math.Sqrt(e[2] * e[2] + e[3] * e[3]);
        float left = dest[0].X, right = dest[1].X, top = dest[0].Y, bottom = dest[2].Y;
        var tx = bmp.Width * du / (right - left);
        var ty = bmp.Height * dv / Math.Abs(top - bottom);
        return (
            new[] { new PointF(left - du, top + dv), new PointF(right + du, top + dv), new PointF(left - du, bottom - dv) },
            new RectangleF(-tx, -ty, bmp.Width + 2 * tx, bmp.Height + 2 * ty));
    }

    private static void IntersectDeviceClip(Graphics g, GdiMatrix world, RectangleF deviceRect)
    {
        g.ResetTransform();
        g.IntersectClip(deviceRect);
        g.Transform = world;
    }

    private static void ExcludeDeviceClip(Graphics g, GdiMatrix world, RectangleF deviceRect)
    {
        g.ResetTransform();
        g.ExcludeClip(System.Drawing.Rectangle.Round(deviceRect));
        g.Transform = world;
    }

    /// <summary>
    /// Render an ExtGState soft-mask group to a page-sized 8-bit alpha buffer with this renderer,
    /// under the printed page image's rules, instead of with the software rasteriser.
    /// </summary>
    /// <remarks>
    /// The reference draws a soft mask the way it draws the page. A leaflet's photo masked by a
    /// luminosity group left 441 pixels 20 or more levels off the reference's render with the
    /// software rasteriser's mask, where the same photo drawn without the mask left none; with
    /// the mask rendered here, the page's print lands inside its template's match window.
    /// </remarks>
    private byte[]? RenderPrintedSoftMaskAlpha(SoftMaskInfo sm)
    {
        var groupStream = _reader.ResolveStream(sm.Dict.Get("G"));
        if (groupStream is null) return null;
        int w = _bitmap.Width, h = _bitmap.Height;
        var luminosity = sm.Subtype != "Alpha";
        using var maskBitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        if (luminosity)
        {
            using var fill = Graphics.FromImage(maskBitmap);
            var (r, gr, b) = SoftwarePageRenderer.SampleBackdropRgb(_reader.Resolve(sm.Dict.Get("BC")) as PdfArray);
            fill.Clear(GdiColor.FromArgb(255, r, gr, b));
        }

        var savedG = _g;
        var savedBitmap = _bitmap;
        var savedScratch = _blendScratch;
        var maskGraphics = Graphics.FromImage(maskBitmap);
        try
        {
            maskGraphics.SmoothingMode = savedG.SmoothingMode;
            maskGraphics.PixelOffsetMode = savedG.PixelOffsetMode;
            maskGraphics.InterpolationMode = savedG.InterpolationMode;
            maskGraphics.TextRenderingHint = savedG.TextRenderingHint;
            maskGraphics.CompositingQuality = savedG.CompositingQuality;
            _g = maskGraphics;
            _bitmap = maskBitmap;
            _blendScratch = null;
            DrawFormXObject(groupStream, new GraphicsState { Ctm = (double[])sm.Ctm.Clone() });
            _g.Flush();
        }
        finally
        {
            _g = savedG;
            _bitmap = savedBitmap;
            _blendScratch?.Dispose();
            _blendScratch = savedScratch;
            maskGraphics.Dispose();
        }

        var alpha = MaskValues(maskBitmap, luminosity);
        SoftwarePageRenderer.ApplyTransferFunction(alpha, sm.Dict.Get("TR"), _reader);
        return alpha;
    }

    /// <summary>A rendered mask group's values: its alpha, or its luminosity (Rec. 601) weighted by
    /// its alpha.</summary>
    private static byte[] MaskValues(Bitmap maskBitmap, bool luminosity)
    {
        int w = maskBitmap.Width, h = maskBitmap.Height;
        var alpha = new byte[w * h];
        var bits = maskBitmap.LockBits(new System.Drawing.Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[w * 4];
            for (var y = 0; y < h; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(bits.Scan0 + y * bits.Stride, row, 0, row.Length);
                for (var x = 0; x < w; x++)
                {
                    int b = row[x * 4], g = row[x * 4 + 1], r = row[x * 4 + 2], a = row[x * 4 + 3];
                    alpha[y * w + x] = luminosity
                        ? (byte)(((r * 299 + g * 587 + b * 114 + 500) / 1000 * a + 127) / 255)
                        : (byte)a;
                }
            }
        }
        finally
        {
            maskBitmap.UnlockBits(bits);
        }
        return alpha;
    }
}
