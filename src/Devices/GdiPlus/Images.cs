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

        using var bmp = ImageDecoder.TryDecode(xobj, state, _reader, _pdfxOverprintSim);
        if (bmp is null) return;
        var softMask = state.SoftMask is { } sm ? GetSoftMaskAlpha(sm) : null;
        BlitImage(bmp, state.Ctm, state.OverprintFill && IsSubtractiveImage(xobj.Dict), state.FillAlpha, softMask);
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
        var saved = _g.Transform;
        using var world = WorldMatrix(ctm);
        _g.Transform = world;
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
            // LAW: a MAGNIFIED image is seated half a pixel earlier than
            // a naive blit does — see ShiftBlitByHalfDevicePixel for the correction and
            // the evidence. Judged at the OUTPUT scale: a supersampling caller renders
            // large and averages down, and the law is about what the viewer sees, so an
            // image at 1:1 on a 2x intermediate is not "magnified". The 1% margin keeps a
            // 1:1 blit — where the correction is nothing and rounding can put the device
            // extent a hair over the source — off the magnified path entirely.
            var outScale = Math.Max(1, OutputSupersample);
            var magnified = devW > bmp.Width * 1.01 * outScale || devH > bmp.Height * 1.01 * outScale;
            // Sub-pixel-thin blits (e.g. a gradient or raster logo sliced into
            // 1-row scanline strips, each mapped to a fraction of a pixel) average
            // away to nothing under high-quality resampling. Grow such a strip to
            // cover at least one device pixel, centred on its band, so stacked
            // strips accumulate into the intended image instead of vanishing.
            float x0 = 0f, x1 = 1f, y0 = 0f, y1 = 1f;
            if (devW > 1e-6 && devW < 1f) { var f = (float)(1.0 / devW); x0 = 0.5f - f / 2f; x1 = 0.5f + f / 2f; }
            if (devH > 1e-6 && devH < 1f) { var f = (float)(1.0 / devH); y0 = 0.5f - f / 2f; y1 = 0.5f + f / 2f; }

            if (magnified)
                ShiftBlitByHalfDevicePixel(world, outScale, ref x0, ref x1, ref y0, ref y1);

            // LAW (minification): a MINIFIED image resamples through the SOFT
            // kernel. Decimating a scanned text page ~0.95× through bicubic keeps full
            // contrast and rings — 255|9 across a glyph edge where the expected render
            // lands 225|70 — because bicubic's negative lobes sharpen what is already
            // aliasing. The expected minified output matches the prefiltered
            // bilinear, and switching to it removed every mismatch in the minified band
            // of a 300 dpi scan (330 pixels past tolerance → 0).
            if (devW < bmp.Width * 0.99 && devH < bmp.Height * 0.99)
                _g.InterpolationMode = InterpolationMode.HighQualityBilinear;

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
                && Math.Abs(devW - bmp.Width) <= 0.01 * bmp.Width
                && Math.Abs(devH - bmp.Height) <= 0.01 * bmp.Height)
                SnapBlitToIdentity(world, bmp, ref x0, ref x1, ref y0, ref y1);

            var dest = new[]
            {
                new PointF(x0, y1), // upper-left  → image top-left
                new PointF(x1, y1), // upper-right → image top-right
                new PointF(x0, y0), // lower-left  → image bottom-left
            };
            if (softMask is not null) BlitImageMasked(bmp, world, dest, alpha, softMask);
            else if (overprint) BlitImageMultiply(bmp, world, dest);
            else
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
                Region? savedClip = null;
                if (magnified && Math.Abs(e[1]) < 1e-4f && Math.Abs(e[2]) < 1e-4f
                    && e[0] > 0f && e[3] < 0f)
                {
                    var hardL = (float)Math.Ceiling(e[4]);
                    var hardT = (float)Math.Ceiling(e[5] + e[3]);
                    var hardR = (float)Math.Ceiling(e[4] + e[0]);
                    var hardB = (float)Math.Ceiling(e[5]);
                    if (hardR - hardL >= 1 && hardB - hardT >= 1)
                    {
                        savedClip = _g.Clip;
                        // The hard rect is in device pixels; intersect it under an
                        // identity transform, then restore the blit's world matrix.
                        using (var id = new GdiMatrix())
                        {
                            _g.Transform = id;
                            _g.IntersectClip(new RectangleF(hardL, hardT, hardR - hardL, hardB - hardT));
                        }
                        _g.Transform = world;
                    }
                }
                try
                {
                    _g.DrawImage(bmp, dest, new RectangleF(0, 0, bmp.Width, bmp.Height),
                        GraphicsUnit.Pixel, ia);
                }
                finally
                {
                    if (savedClip is not null) { _g.Clip = savedClip; savedClip.Dispose(); }
                }
            }
        }
        finally { _g.Transform = saved; _g.CompositingQuality = savedCq; _g.InterpolationMode = savedIm; }
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
    private static void ShiftBlitByHalfDevicePixel(GdiMatrix world, int outScale,
        ref float x0, ref float x1, ref float y0, ref float y1)
    {
        var e = world.Elements;
        // Elements = [m11, m12, m21, m22, dx, dy]; (u,v) → (u·m11 + v·m21 + dx, u·m12 + v·m22 + dy).
        const float AxisAlignedTol = 1e-4f;
        if (Math.Abs(e[1]) > AxisAlignedTol || Math.Abs(e[2]) > AxisAlignedTol) return;
        // Upright page mapping: x grows with u, and device y grows DOWN while v grows up.
        if (e[0] <= 0f || e[3] >= 0f) return;

        // Half a device pixel expressed in the unit square the caller draws into: the
        // u-edge spans e[0] device pixels across, the v-edge e[3] down (negative, so the
        // same subtraction moves the image UP the page). On a supersampled intermediate
        // half an OUTPUT pixel is outScale of its own, since that is the grid the rule
        // is stated against.
        var shift = (float)(HalfDevicePixel * outScale);
        var du = shift / e[0];
        var dv = shift / e[3];
        x0 -= du; x1 -= du;
        y0 -= dv; y1 -= dv;
    }

    /// <summary>Half a device pixel — the distance a magnified image is seated back.</summary>
    /// <remarks>⚠ A whole-pixel BORDER snap (`[ceil(x0), ceil(x1))` hard, per the probed
    /// edge ladder) was implemented on top of this seat as a source re-window and
    /// measured WORSE everywhere it was scored (4671 vs 1160 unmatched on the 300 dpi
    /// scan, and it re-broke the case the seat had closed) — the seat alone
    /// already reproduces the expected edge placement, because ceiling the seated
    /// extent is what the resampler does. Do not re-add a snap.</remarks>
    private const double HalfDevicePixel = 0.5;

    /// <summary>Collapse a near-identity blit to an exact 1:1 copy: one texel per device
    /// pixel, from the rounded device origin. See the call site for the law. The
    /// caller has already established the extent is within 1% of the source size; this
    /// adds the geometric guards (axis-aligned, upright, the un-expanded unit square)
    /// and rewrites the unit rect so the world transform lands each texel on a whole
    /// pixel — where the bicubic sampler degenerates to a copy.</summary>
    private static void SnapBlitToIdentity(GdiMatrix world, Bitmap bmp,
        ref float x0, ref float x1, ref float y0, ref float y1)
    {
        if (x0 != 0f || x1 != 1f || y0 != 0f || y1 != 1f) return;

        var e = world.Elements;
        const float AxisAlignedTol = 1e-4f;
        if (Math.Abs(e[1]) > AxisAlignedTol || Math.Abs(e[2]) > AxisAlignedTol) return;
        // Upright page mapping: x grows with u, and device y grows DOWN while v grows up.
        if (e[0] <= 0f || e[3] >= 0f) return;

        double mx = e[0], my = e[3], dx = e[4], dy = e[5];
        var rx = Math.Round(dx);            // left edge  (u = 0)
        var ry = Math.Round(dy + my);       // top edge   (v = 1)
        // Unit-space u/v that place [rx, rx+W) × [ry, ry+H) under the SAME transform.
        x0 = (float)((rx - dx) / mx);
        x1 = (float)((rx + bmp.Width - dx) / mx);
        y1 = (float)((ry - dy) / my);
        y0 = (float)((ry + bmp.Height - dy) / my);
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
