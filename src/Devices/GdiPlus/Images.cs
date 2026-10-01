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
    private void DrawXObject(string name, GraphicsState state)
    {
        if (_scope.XObjects is null || !_scope.XObjects.TryGetValue(name, out var xobj)) return;
        var subtype = xobj.Dict.GetName("Subtype");
        if (subtype == "Image") DrawImageXObject(xobj, state);
        else if (subtype == "Form") DrawFormXObject(xobj, state);
    }

    private void DrawImageXObject(PdfStream xobj, GraphicsState state)
    {
        // Skip images hidden by the default optional-content configuration.
        if (SoftwarePageRenderer.IsOcHidden(xobj.Dict.Get("OC"), _reader, _ocgHidden)) return;

        // An ImageMask painted while a /Pattern fill is selected (e.g. PowerPoint
        // exports a gradient as `/Pattern cs /P scn … /Mask Do`) is a stencil through
        // which the pattern shows — not a solid colour. Paint the pattern clipped to
        // the stencil; otherwise the mask renders with the stale solid fill colour
        // (which is dark/black: "squares render black"), grossly over-inking the page.
        if (state.FillPatternName is not null
            && xobj.Dict.Get("ImageMask") is PdfBoolean imb && imb.Value)
        {
            DrawPatternMaskedImage(xobj, state);
            return;
        }

        // Very large plain-gray/bilevel scans: the generic decode path expands to a
        // W×H×4 BGRA buffer (a 740-megapixel fax scan would need ~3 GB) and dies with
        // OutOfMemory — swallowed by SafeDraw, so the image simply vanished from the
        // page. Decode the packed samples straight into a device-sized box-averaged
        // bitmap instead: correct area-averaged appearance (a halftone screen reduces
        // to smooth grey, as it should), bounded memory, and far faster
        // than resampling the full-resolution expansion.
        {
            var iw = (int)xobj.Dict.GetInt("Width");
            var ih = (int)xobj.Dict.GetInt("Height");
            var ibpc = (int)xobj.Dict.GetInt("BitsPerComponent");
            if (Environment.GetEnvironmentVariable("Q_HUGEGRAY") != "0"
                && (long)iw * ih > 100_000_000 && (ibpc == 1 || ibpc == 8))
            {
                var csi = SoftwarePageRenderer.ResolveImageColorSpace(xobj.Dict.Get("ColorSpace"), _reader);
                if (csi.BaseName == "DeviceGray" && csi.Palette is null && csi.TintTransform is null)
                {
                    using var small = DecodeHugeGrayDownsampled(xobj, iw, ih, ibpc, state);
                    if (small is not null)
                    {
                        var sm2 = state.SoftMask is { } smk ? GetSoftMaskAlpha(smk) : null;
                        BlitImage(small, state.Ctm, overprint: false, state.FillAlpha, sm2);
                        return;
                    }
                }
            }
        }

        if (_vectorTarget && TryBlitJpegAsItself(xobj, state)) return;

        using var bmp = ImageDecoder.TryDecode(xobj, state, _reader, _pdfxOverprintSim, replicateChroma: PrintedPageImage);
        if (bmp is null) return;
        var softMask = state.SoftMask is { } sm ? GetSoftMaskAlpha(sm) : null;
        BlitImage(bmp, state.Ctm, state.OverprintFill && IsSubtractiveImage(xobj.Dict), state.FillAlpha, softMask);
    }

    /// <summary>
    /// Hands a plain JPEG image to a printer as the JPEG it is: the platform decodes the stream's
    /// own bytes and the printer driver, given that image unmodified, keeps them. False, with
    /// nothing drawn, for any image whose samples need more than a decode to reach the page.
    /// </summary>
    /// <remarks>
    /// The reference's XPS print of a DCTDecode DeviceRGB logo carries pixels identical to a
    /// plain decode of the PDF's JPEG. Decoded here and handed over as a new bitmap, the driver
    /// re-encodes it, and 152 of a half-scale page's 167 pixels outside the corpus template's
    /// match window sat inside that logo. An image in an ICC or other colour space is left to
    /// the decoder: the reference re-encodes those itself.
    /// </remarks>
    private bool TryBlitJpegAsItself(PdfStream xobj, GraphicsState state)
    {
        var dict = xobj.Dict;
        if (state.SoftMask is not null || state.FillAlpha < FullyOpaqueImage || state.OverprintFill) return false;
        if (dict.Get("SMask") is not null || dict.Get("Mask") is not null || dict.Get("Decode") is not null
            || dict.Get("ImageMask") is PdfBoolean { Value: true })
            return false;
        var filter = _reader.Resolve(dict.Get("Filter"));
        var isJpeg = filter is PdfName { Value: "DCTDecode" }
            || filter is PdfArray { Count: 1 } one && _reader.Resolve(one[0]) is PdfName { Value: "DCTDecode" };
        if (!isJpeg || _reader.Resolve(dict.Get("ColorSpace")) is not PdfName { Value: "DeviceRGB" or "DeviceGray" })
            return false;

        // The reader decrypts; a DCTDecode stream decodes to its JPEG bytes as they are.
        byte[] bytes;
        try { bytes = _reader.DecodeStream(xobj); }
        catch { return false; }
        using var stream = new MemoryStream(bytes, writable: false);
        Bitmap jpeg;
        try { jpeg = new Bitmap(stream); }
        catch (ArgumentException) { return false; }
        using (jpeg)
        {
            if (jpeg.Width != (int)dict.GetInt("Width") || jpeg.Height != (int)dict.GetInt("Height")) return false;
            var saved = _g.Transform;
            using var world = WorldMatrix(state.Ctm);
            try { BlitImageAsDrawingCommand(jpeg, world); }
            finally { _g.Transform = saved; }
        }
        return true;
    }

    /// <summary>Paint an ImageMask whose current fill is a pattern: build a clip from
    /// the stencil's painted pixels and fill it with the pattern (tiling or shading),
    /// so the pattern shows through the mask instead of a flat colour.</summary>
    private void DrawPatternMaskedImage(PdfStream xobj, GraphicsState state)
    {
        var w = (int)xobj.Dict.GetInt("Width");
        var h = (int)xobj.Dict.GetInt("Height");
        if (w <= 0 || h <= 0) return;
        byte[] bits;
        try { bits = _reader.DecodeStream(xobj); } catch { return; }
        var rowBytes = (w + 7) / 8;
        // Default /Decode [0 1]: bit 0 paints, bit 1 is transparent; [1 0] flips it.
        var invert = xobj.Dict.Get("Decode") is PdfArray dec && dec.Count >= 2 && NumFrom(dec[0]) > NumFrom(dec[1]);
        var paintBit = invert ? 1 : 0;

        // Build the stencil as a path in the image unit square (top row -> v=1, matching
        // the blit convention), coalescing horizontal runs of painted pixels per row.
        using var stencil = new GraphicsPath();
        for (int y = 0; y < h; y++)
        {
            var rb = y * rowBytes;
            int x = 0;
            while (x < w)
            {
                var bi = rb + (x >> 3);
                var bit = bi < bits.Length ? (bits[bi] >> (7 - (x & 7))) & 1 : 1 - paintBit;
                if (bit != paintBit) { x++; continue; }
                int start = x;
                while (x < w)
                {
                    var b2 = rb + (x >> 3);
                    if (b2 >= bits.Length || ((bits[b2] >> (7 - (x & 7))) & 1) != paintBit) break;
                    x++;
                }
                stencil.AddRectangle(new RectangleF((float)start / w, 1f - (float)(y + 1) / h,
                    (float)(x - start) / w, 1f / h));
            }
        }
        if (stencil.PointCount == 0) return;

        var gs = _g.Save();
        try
        {
            using var world = WorldMatrix(state.Ctm);
            _g.Transform = world;
            _g.SetClip(stencil, CombineMode.Intersect);
            using var quad = new GraphicsPath();
            quad.AddRectangle(new RectangleF(0f, 0f, 1f, 1f));
            if (state.FillPatternName is not null)
                FillWithTilingPattern(quad, state, world, state.FillPatternName);
        }
        finally { _g.Restore(gs); }
    }

    private void DrawInlineImage(PdfDictionary dict, byte[] data, GraphicsState state)
    {
        using var bmp = ImageDecoder.TryDecodeInline(dict, data, state, _reader, _pdfxOverprintSim);
        if (bmp is null) return;
        var softMask = state.SoftMask is { } sm ? GetSoftMaskAlpha(sm) : null;
        BlitImage(bmp, state.Ctm, state.OverprintFill && IsSubtractiveImage(dict), state.FillAlpha, softMask);
    }

    /// <summary>
    /// True when an image is painted in a subtractive colour space (DeviceCMYK or a
    /// /Separation / /DeviceN spot space). Overprint (PDF 32000 §8.6.7) only changes
    /// the result for such spaces — an overprinted spot plate composites onto, rather
    /// than knocking out, the process colour underneath.
    /// </summary>
    private bool IsSubtractiveImage(PdfDictionary dict)
    {
        var cs = SoftwarePageRenderer.ResolveImageColorSpace(dict.Get("ColorSpace"), _reader);
        return cs.TintTransform is not null || cs.BaseName == "DeviceCMYK";
    }

    /// <summary>
    /// Place a decoded bitmap into the PDF unit square via the supplied CTM. The
    /// destination parallelogram (upper-left, upper-right, lower-left) in user space
    /// maps the bitmap's top row to unit-square y=1, so GDI+ resamples and orients
    /// the image — handling any CTM rotation/flip/skew natively.
    /// </summary>
    private void BlitImage(Bitmap bmp, double[] ctm, bool overprint = false, double alpha = 1.0, byte[]? softMask = null)
    {
        // Heavy-downscale prefilter: a very large source mapped onto a much smaller
        // device area (a 300+ MP 1-bit halftone scan on an A4 page) must be AREA-
        // AVERAGED — GDI+'s bicubic samples a fixed window, not the full footprint of
        // each destination pixel, so a 25× decimation of a dot screen comes out as
        // binary moiré instead of the smooth grey area averaging produces (and takes
        // minutes on the way). Box-average into a device-sized intermediate first;
        // the normal high-quality blit then only resamples by a small factor.
        Bitmap? shrunk = null;
        using (var worldProbe = WorldMatrix(ctm))
        {
            var ep = worldProbe.Elements;
            var pdW = Math.Sqrt(ep[0] * ep[0] + ep[1] * ep[1]);
            var pdH = Math.Sqrt(ep[2] * ep[2] + ep[3] * ep[3]);
            if (Environment.GetEnvironmentVariable("Q_BOXPRE") != "0"
                && pdW >= 1 && pdH >= 1
                && bmp.Width > pdW * 3 && bmp.Height > pdH * 3
                && (long)bmp.Width * bmp.Height > 4_000_000)
            {
                shrunk = BoxDownsample(bmp, (int)Math.Ceiling(pdW), (int)Math.Ceiling(pdH));
            }
        }
        if (shrunk is not null) bmp = shrunk;
        try
        {
            BlitImageCore(bmp, ctm, overprint, alpha, softMask);
        }
        finally { shrunk?.Dispose(); }
    }

    private void BlitImageCore(Bitmap bmp, double[] ctm, bool overprint, double alpha, byte[]? softMask)
    {
        BlitImageWithMasks(bmp, ctm, softMask, overprint, alpha);
    }

    /// <summary>A /ca at or above this draws an image as opaque.</summary>
    private const double FullyOpaqueImage = 0.999;

    /// <summary>
    /// Hand a translucent image to a printer: the plain bilinear kernel and the opacity as a
    /// whole number of alpha levels, drawn into the unit square under the page transform.
    /// </summary>
    /// <remarks>
    /// Probed against the reference's print of a watermark drawn at /ca 0.1 (a 509x460 picture
    /// on a Letter page, 2026-09-17, twelve GDI+ variants through the XPS Document Writer): the
    /// writer rasterises the translucent picture itself, and its pixels equal the reference's
    /// only under plain bilinear resampling - the high-quality kernels leave 1,198 pixels 20 or
    /// more levels off. Its alpha plane equals the reference's only when the opacity is the
    /// WHOLE alpha level below /ca times 255 (25 of 255 for 0.1): the exact 25.5 dithers to a
    /// 23/27 checkerboard where the reference's plane carries 23 on three pixels in four. The
    /// seat and border clip of the raster blit do not apply - the printer resamples.
    /// </remarks>
    private void BlitTranslucentImageAsDrawingCommand(Bitmap bmp, double alpha)
    {
        using var attributes = new ImageAttributes();
        attributes.SetWrapMode(WrapMode.TileFlipXY);
        var levels = Math.Floor(Math.Max(0.0, Math.Min(1.0, alpha)) * AlphaLevels) / AlphaLevels;
        attributes.SetColorMatrix(new ColorMatrix { Matrix33 = (float)levels });
        var savedInterpolation = _g.InterpolationMode;
        _g.InterpolationMode = InterpolationMode.Bilinear;
        try
        {
            var dest = new[] { new PointF(0, 1), new PointF(1, 1), new PointF(0, 0) };
            _g.DrawImage(bmp, dest, new RectangleF(0, 0, bmp.Width, bmp.Height), GraphicsUnit.Pixel, attributes);
        }
        finally { _g.InterpolationMode = savedInterpolation; }
    }

    /// <summary>The alpha levels of an 8-bit plane: an opacity is printed as a whole number of them.</summary>
    private const double AlphaLevels = 255.0;

    /// <summary>
    /// Hand an image to a printer as itself: its own pixels into the parallelogram the page puts
    /// it in. None of the pixel-grid laws of the raster blit apply - the printer resamples.
    /// </summary>
    /// <remarks>
    /// <para>The corners are taken all the way to the printer surface's own units FIRST, and the
    /// image drawn with nothing left to transform. Drawn through the page's Y-flipping world
    /// transform instead, GDI+ resamples the image itself before the driver sees it (a 125x83
    /// picture reached the XPS writer as 126x84, and a 250x232 one as two 253x117 bands); drawn this
    /// way the driver receives the native pixels, byte for byte what the reference printer job sends
    /// for the same page.</para>
    /// <para>Not only to layout pixels: under a page scaled to fit the sheet, an image drawn in layout
    /// pixels reached the XPS writer offset as if the scale did not apply to where it sits. Of 80
    /// small images at fractional positions on a fitted A4 page, 45 landed a printer pixel right of
    /// the reference's and 59 below; drawn in the sheet's units, all 80 land on the reference's
    /// edges - each the exact device coordinate, rounded up.</para>
    /// </remarks>
    private void BlitImageAsDrawingCommand(Bitmap bmp, GdiMatrix world)
    {
        var corners = new[] { new PointF(0, 1), new PointF(1, 1), new PointF(0, 0) };
        world.TransformPoints(corners);
        _layoutToSheet!.TransformPoints(corners);
        using var sheetToLayout = _layoutToSheet.Clone();
        sheetToLayout.Invert();
        _g.Transform = sheetToLayout;
        var savedInterpolation = _g.InterpolationMode;
        _g.InterpolationMode = InterpolationMode.Bilinear;
        try { _g.DrawImage(bmp, corners, new RectangleF(0, 0, bmp.Width, bmp.Height), GraphicsUnit.Pixel); }
        finally { _g.InterpolationMode = savedInterpolation; }
    }

    /// <summary>Seat a magnified image half a DEVICE pixel back, up and to the left.</summary>
    /// <remarks>
    /// The expected render samples a magnified image half a pixel earlier than a naive
    /// blit does. The correction is half a pixel of the OUTPUT grid — it does not grow with the
    /// magnification. Asking GDI+ for it with <see cref="PixelOffsetMode.None"/> looks
    /// equivalent and is not: that offsets the SOURCE lookup by half a texel, so the
    /// content moves half a texel × the scale — right at 2× (one device pixel, which is
    /// what a 300 dpi scan compare needs), and six pixels out at 13×, where a 56-pixel
    /// swatch blown up over a third of a page lands visibly high and left of its
    /// template. Half a device pixel is the constant that satisfies both.
    /// <para>
    /// Applied as a shift of the destination only. The source window is untouched, so
    /// this moves where the image sits without disturbing the phase it resamples on.
    /// Restricted to the upright, unmirrored case (no rotation or skew): a rotated blit
    /// has no axis-aligned pixel grid to seat against.
    /// </para>
    /// </remarks>
    /// <returns>The unit rectangle, seated back half a device pixel on each magnified axis when the mapping is upright.</returns>
    private static (float x0, float x1, float y0, float y1) ShiftBlitByHalfDevicePixel(GdiMatrix world,
        float x0, float x1, float y0, float y1, bool seatX, bool seatY)
    {
        var e = world.Elements;
        // Elements = [m11, m12, m21, m22, dx, dy]; (u,v) → (u·m11 + v·m21 + dx, u·m12 + v·m22 + dy).
        const float AxisAlignedTol = 1e-4f;
        if (Math.Abs(e[1]) > AxisAlignedTol || Math.Abs(e[2]) > AxisAlignedTol) return (x0, x1, y0, y1);
        // Upright page mapping: x grows with u, and device y grows DOWN while v grows up.
        if (e[0] <= 0f || e[3] >= 0f) return (x0, x1, y0, y1);

        // Half a device pixel expressed in the unit square the caller draws into: the
        // u-edge spans e[0] device pixels across, the v-edge e[3] down (negative, so the
        // same subtraction moves the image UP the page).
        var shift = (float)(HalfDevicePixel);
        var du = seatX ? shift / e[0] : 0f;
        var dv = seatY ? shift / e[3] : 0f;
        return (x0 - du, x1 - du, y0 - dv, y1 - dv);
    }

    /// <summary>Half a device pixel — the distance a magnified image is seated back.</summary>
    /// <remarks>⚠ A whole-pixel BORDER snap (`[ceil(x0), ceil(x1))` hard, per the probed
    /// edge ladder) was implemented on top of this seat as a source re-window and
    /// measured WORSE everywhere it was scored (4671 vs 1160 unmatched on the 300 dpi
    /// scan, and it re-broke the case the seat had closed) — the seat alone
    /// already reproduces the expected edge placement, because ceiling the seated
    /// extent is what the resampler does. Do not re-add a snap.</remarks>
    private const double HalfDevicePixel = 0.5;

    /// <summary>How close to source scale a blit must be to collapse to an exact texel copy: a
    /// tenth of a percent. The full-bleed 96-dpi scan that proved the 1:1 rule sits at 0.014 %;
    /// a full-page scan at 0.66 % is resampled by the reference (its bilinear kernel turns the
    /// scan's halftone into flat grey), which the exact copy kept at full contrast.</summary>
    private const double IdentityBlitTolerance = 0.001;

    /// <summary>Where the magnified regime (bicubic with the half-pixel seat) begins: one percent
    /// over source scale, as measured on the magnified fixtures. A blit between the identity
    /// tolerance and this margin resamples plainly, without the seat.</summary>
    private const double MagnifiedBlitMargin = 0.01;
    /// <summary>A device extent under this share of the source is a minified axis.</summary>
    private const double MinifiedBlitMargin = 0.99;

    /// <summary>Collapse a near-identity blit to an exact 1:1 copy: one texel per device
    /// pixel, from the rounded device origin. See the call site for the law. The
    /// caller has already established the extent is within 1% of the source size; this
    /// adds the geometric guards (axis-aligned, upright, the un-expanded unit square)
    /// and rewrites the unit rect so the world transform lands each texel on a whole
    /// pixel — where the bicubic sampler degenerates to a copy.</summary>
    /// <returns>The unit rectangle, rewritten onto whole device pixels when the blit is near-identity.</returns>
    private static (float x0, float x1, float y0, float y1) SnapBlitToIdentity(GdiMatrix world, Bitmap bmp,
        float x0, float x1, float y0, float y1)
    {
        if (x0 != 0f || x1 != 1f || y0 != 0f || y1 != 1f) return (x0, x1, y0, y1);

        var e = world.Elements;
        const float AxisAlignedTol = 1e-4f;
        if (Math.Abs(e[1]) > AxisAlignedTol || Math.Abs(e[2]) > AxisAlignedTol) return (x0, x1, y0, y1);
        // Upright page mapping: x grows with u, and device y grows DOWN while v grows up.
        if (e[0] <= 0f || e[3] >= 0f) return (x0, x1, y0, y1);

        double mx = e[0], my = e[3], dx = e[4], dy = e[5];
        var rx = Math.Round(dx);            // left edge  (u = 0)
        var ry = Math.Round(dy + my);       // top edge   (v = 1)
        // Unit-space u/v that place [rx, rx+W) × [ry, ry+H) under the SAME transform.
        return ((float)((rx - dx) / mx),
                (float)((rx + bmp.Width - dx) / mx),
                (float)((ry + bmp.Height - dy) / my),
                (float)((ry - dy) / my));
    }

    /// <summary>Decode a very large packed DeviceGray image (1 or 8 bpc) directly into a
    /// device-sized box-averaged 32bpp bitmap, without ever materialising the full
    /// W×H×4 expansion. Returns null when the decode fails or the image maps to a
    /// larger-than-source device area (no downsample needed — the generic path can
    /// handle it). Honours /Decode [1 0] inversion.</summary>
    private Bitmap? DecodeHugeGrayDownsampled(PdfStream xobj, int w, int h, int bpc, GraphicsState state)
    {
        byte[] data;
        try { data = _reader.DecodeStream(xobj); } catch { return null; }
        if (data.Length == 0) return null;

        int dw, dh;
        using (var worldProbe = WorldMatrix(state.Ctm))
        {
            var ep = worldProbe.Elements;
            dw = (int)Math.Ceiling(Math.Sqrt(ep[0] * ep[0] + ep[1] * ep[1]));
            dh = (int)Math.Ceiling(Math.Sqrt(ep[2] * ep[2] + ep[3] * ep[3]));
        }
        if (dw < 1 || dh < 1 || dw >= w || dh >= h) return null;

        bool invert = xobj.Dict.Get("Decode") is PdfArray dec && dec.Count >= 2
            && NumFrom(dec[0]) > NumFrom(dec[1]);
        int inv = invert ? 1 : 0;

        var sum = new long[dw * dh];
        var cnt = new long[dw * dh];
        int rowBytes = bpc == 1 ? (w + 7) / 8 : w;
        for (int y = 0; y < h; y++)
        {
            long rowBase = (long)y * rowBytes;
            if (rowBase >= data.Length) break;
            int dy = (int)((long)y * dh / h);
            int db = dy * dw;
            if (bpc == 1)
            {
                for (int x = 0; x < w; x++)
                {
                    long bi = rowBase + (x >> 3);
                    int v = bi < data.Length ? ((((data[bi] >> (7 - (x & 7))) & 1) ^ inv) == 1 ? 255 : 0) : 255;
                    int di = db + (int)((long)x * dw / w);
                    sum[di] += v; cnt[di]++;
                }
            }
            else
            {
                for (int x = 0; x < w; x++)
                {
                    long bi = rowBase + x;
                    int v = bi < data.Length ? (invert ? 255 - data[bi] : data[bi]) : 255;
                    int di = db + (int)((long)x * dw / w);
                    sum[di] += v; cnt[di]++;
                }
            }
        }

        var dst = new Bitmap(dw, dh, PixelFormat.Format32bppArgb);
        var ddata = dst.LockBits(new System.Drawing.Rectangle(0, 0, dw, dh), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var drow = new byte[dw * 4];
            for (int y = 0; y < dh; y++)
            {
                int b = y * dw;
                for (int x = 0; x < dw; x++)
                {
                    var g = (byte)(sum[b + x] / Math.Max(1, cnt[b + x]));
                    int o = x * 4;
                    drow[o] = g; drow[o + 1] = g; drow[o + 2] = g; drow[o + 3] = 255;
                }
                System.Runtime.InteropServices.Marshal.Copy(drow, 0, ddata.Scan0 + (nint)y * ddata.Stride, drow.Length);
            }
        }
        finally { dst.UnlockBits(ddata); }
        return dst;
    }
}
