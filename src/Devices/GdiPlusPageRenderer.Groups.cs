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

public sealed partial class GdiPlusPageRenderer : IPageRenderer
{
    /// <summary>Exact box-filter downsample: every destination pixel is the average of
    /// its full source footprint. Fast paths for 1bpp-indexed (bit counts via popcount),
    /// 8bpp-indexed, and 24/32bpp sources; other formats return null (caller keeps the
    /// original bitmap). Output is 32bpp ARGB.</summary>
    private static Bitmap? BoxDownsample(Bitmap src, int dw, int dh)
    {
        int sw = src.Width, sh = src.Height;
        if (dw <= 0 || dh <= 0 || dw >= sw || dh >= sh) return null;
        var fmt = src.PixelFormat;
        if (fmt is not (PixelFormat.Format1bppIndexed or PixelFormat.Format8bppIndexed
            or PixelFormat.Format24bppRgb or PixelFormat.Format32bppArgb or PixelFormat.Format32bppRgb))
            return null;

        // Per-destination-pixel channel sums; accumulate row by row so the source is
        // touched once, sequentially (the sources this path exists for are huge).
        var sumR = new long[dw * dh];
        var sumG = new long[dw * dh];
        var sumB = new long[dw * dh];
        var sumA = new long[dw * dh];
        var cnt = new long[dw * dh];

        // Palette lookups for indexed formats.
        GdiColor[]? pal = fmt is PixelFormat.Format1bppIndexed or PixelFormat.Format8bppIndexed
            ? src.Palette.Entries : null;

        var data = src.LockBits(new System.Drawing.Rectangle(0, 0, sw, sh), ImageLockMode.ReadOnly, fmt);
        try
        {
            int stride = data.Stride;
            var row = new byte[Math.Abs(stride)];
            for (int y = 0; y < sh; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + (nint)y * stride, row, 0, row.Length);
                int dy = (int)((long)y * dh / sh);
                int rowBase = dy * dw;
                switch (fmt)
                {
                    case PixelFormat.Format1bppIndexed:
                    {
                        var c0 = pal![0]; var c1 = pal[1];
                        for (int x = 0; x < sw; x++)
                        {
                            int bit = (row[x >> 3] >> (7 - (x & 7))) & 1;
                            var c = bit == 0 ? c0 : c1;
                            int di = rowBase + (int)((long)x * dw / sw);
                            sumR[di] += c.R; sumG[di] += c.G; sumB[di] += c.B; sumA[di] += c.A; cnt[di]++;
                        }
                        break;
                    }
                    case PixelFormat.Format8bppIndexed:
                    {
                        for (int x = 0; x < sw; x++)
                        {
                            var c = pal![row[x]];
                            int di = rowBase + (int)((long)x * dw / sw);
                            sumR[di] += c.R; sumG[di] += c.G; sumB[di] += c.B; sumA[di] += c.A; cnt[di]++;
                        }
                        break;
                    }
                    case PixelFormat.Format24bppRgb:
                    {
                        for (int x = 0; x < sw; x++)
                        {
                            int o = x * 3;
                            int di = rowBase + (int)((long)x * dw / sw);
                            sumB[di] += row[o]; sumG[di] += row[o + 1]; sumR[di] += row[o + 2]; sumA[di] += 255; cnt[di]++;
                        }
                        break;
                    }
                    default: // 32bpp
                    {
                        bool hasAlpha = fmt == PixelFormat.Format32bppArgb;
                        for (int x = 0; x < sw; x++)
                        {
                            int o = x * 4;
                            int di = rowBase + (int)((long)x * dw / sw);
                            sumB[di] += row[o]; sumG[di] += row[o + 1]; sumR[di] += row[o + 2];
                            sumA[di] += hasAlpha ? row[o + 3] : 255; cnt[di]++;
                        }
                        break;
                    }
                }
            }
        }
        finally { src.UnlockBits(data); }

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
                    long n = Math.Max(1, cnt[b + x]);
                    int o = x * 4;
                    drow[o] = (byte)(sumB[b + x] / n);
                    drow[o + 1] = (byte)(sumG[b + x] / n);
                    drow[o + 2] = (byte)(sumR[b + x] / n);
                    drow[o + 3] = (byte)(sumA[b + x] / n);
                }
                System.Runtime.InteropServices.Marshal.Copy(drow, 0, ddata.Scan0 + (nint)y * ddata.Stride, drow.Length);
            }
        }
        finally { dst.UnlockBits(ddata); }
        return dst;
    }

    /// <summary>
    /// Composite an image onto the page with the Multiply blend used to approximate
    /// overprint (PDF 32000 §8.6.7): out = dst·src/255. A "white" (no-ink) source
    /// pixel leaves the destination unchanged, so an overprinted spot plate tints the
    /// process colour beneath it instead of knocking it out. The image is rasterised
    /// into a scratch layer (honouring the active transform and clip, matching the
    /// native blit) then multiplied into the backing bitmap per pixel.
    /// </summary>
    private void BlitImageMultiply(Bitmap bmp, GdiMatrix world, PointF[] dest)
    {
        int w = _bitmap.Width, h = _bitmap.Height;

        // Device-space bounds of the destination parallelogram (3 given corners plus
        // the implied fourth), clamped to the canvas.
        var corners = new[] { dest[0], dest[1], dest[2], new PointF(dest[1].X + dest[2].X - dest[0].X, dest[1].Y + dest[2].Y - dest[0].Y) };
        world.TransformPoints(corners);
        float fminX = corners[0].X, fminY = corners[0].Y, fmaxX = corners[0].X, fmaxY = corners[0].Y;
        foreach (var c in corners) { fminX = Math.Min(fminX, c.X); fminY = Math.Min(fminY, c.Y); fmaxX = Math.Max(fmaxX, c.X); fmaxY = Math.Max(fmaxY, c.Y); }
        int x0 = Math.Max(0, (int)Math.Floor(fminX)), y0 = Math.Max(0, (int)Math.Floor(fminY));
        int x1 = Math.Min(w, (int)Math.Ceiling(fmaxX)), y1 = Math.Min(h, (int)Math.Ceiling(fmaxY));
        if (x1 <= x0 || y1 <= y0) return;

        // Rasterise the image into the scratch layer with the same transform and clip.
        _blendScratch ??= new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var sgfx = Graphics.FromImage(_blendScratch))
        {
            sgfx.Clear(GdiColor.Transparent);
            sgfx.InterpolationMode = InterpolationMode.HighQualityBicubic;
            sgfx.PixelOffsetMode = PagePom;
            sgfx.CompositingQuality = CompositingQuality.HighQuality;
            sgfx.Transform = world;
            sgfx.Clip = _g.Clip;
            sgfx.DrawImage(bmp, dest);
        }

        _g.Flush();
        var rect = new System.Drawing.Rectangle(x0, y0, x1 - x0, y1 - y0);
        var dst = _bitmap.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var src = _blendScratch.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            // LockBits with a sub-rectangle returns Scan0 at the rect origin but the full
            // image stride, so copy only the rect's row width to avoid overrunning the row.
            int rowBytes = rect.Width * 4;
            var drow = new byte[rowBytes];
            var srow = new byte[rowBytes];
            for (int y = 0; y < rect.Height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(dst.Scan0 + y * dst.Stride, drow, 0, rowBytes);
                System.Runtime.InteropServices.Marshal.Copy(src.Scan0 + y * src.Stride, srow, 0, rowBytes);
                bool dirty = false;
                for (int x = 0; x < rect.Width; x++)
                {
                    int i = x * 4;
                    int sa = srow[i + 3]; // BGRA: scratch coverage/alpha
                    if (sa == 0) continue;
                    double a = sa / 255.0;
                    // Overprint Multiply weighted by backdrop coverage, writing alpha. Over
                    // bare paper (dn=0) the image keeps its own colour and raises coverage;
                    // over content it multiplies. Without the alpha write it would vanish at
                    // flatten on the coverage-alpha page.
                    double dn = drow[i + 3] / 255.0;
                    double outA = a + dn * (1 - a);
                    if (outA <= 0.0) continue;
                    double inv = dn * (1 - a);
                    for (int c = 0; c < 3; c++)
                    {
                        int s = srow[i + c];
                        double bb = dn > 0.0 ? (1 - dn) * s + dn * (drow[i + c] * s / 255.0) : s;
                        drow[i + c] = (byte)((bb * a + drow[i + c] * inv) / outA + 0.5);
                    }
                    drow[i + 3] = (byte)(outA * 255 + 0.5);
                    dirty = true;
                }
                if (dirty)
                    System.Runtime.InteropServices.Marshal.Copy(drow, 0, dst.Scan0 + y * dst.Stride, rowBytes);
            }
        }
        finally
        {
            _bitmap.UnlockBits(dst);
            _blendScratch.UnlockBits(src);
        }
    }

    /// <summary>
    /// Blit an image while modulating its coverage by an ExtGState soft mask (and the /ca
    /// fill alpha) per pixel — the image source-over the backing bitmap, scaled by the
    /// page-aligned mask alpha. Mirrors <see cref="BlitImageMultiply"/>'s scratch approach.
    /// </summary>
    private void BlitImageMasked(Bitmap bmp, GdiMatrix world, PointF[] dest, double alpha, byte[] softMask)
    {
        int w = _bitmap.Width, h = _bitmap.Height;
        var corners = new[] { dest[0], dest[1], dest[2], new PointF(dest[1].X + dest[2].X - dest[0].X, dest[1].Y + dest[2].Y - dest[0].Y) };
        world.TransformPoints(corners);
        float fminX = corners[0].X, fminY = corners[0].Y, fmaxX = corners[0].X, fmaxY = corners[0].Y;
        foreach (var c in corners) { fminX = Math.Min(fminX, c.X); fminY = Math.Min(fminY, c.Y); fmaxX = Math.Max(fmaxX, c.X); fmaxY = Math.Max(fmaxY, c.Y); }
        int x0 = Math.Max(0, (int)Math.Floor(fminX)), y0 = Math.Max(0, (int)Math.Floor(fminY));
        int x1 = Math.Min(w, (int)Math.Ceiling(fmaxX)), y1 = Math.Min(h, (int)Math.Ceiling(fmaxY));
        if (x1 <= x0 || y1 <= y0) return;

        _blendScratch ??= new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var sgfx = Graphics.FromImage(_blendScratch))
        {
            sgfx.Clear(GdiColor.Transparent);
            sgfx.InterpolationMode = InterpolationMode.HighQualityBicubic;
            sgfx.PixelOffsetMode = PagePom;
            sgfx.CompositingQuality = CompositingQuality.HighQuality;
            sgfx.Transform = world;
            sgfx.Clip = _g.Clip;
            sgfx.DrawImage(bmp, dest);
        }

        _g.Flush();
        var rect = new System.Drawing.Rectangle(x0, y0, x1 - x0, y1 - y0);
        var dst = _bitmap.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var src = _blendScratch.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            // LockBits with a sub-rectangle returns Scan0 at the rect origin but the full
            // image stride, so copy only the rect's row width (not the whole stride) or a
            // row that starts past column 0 overruns the buffer on the final rows.
            int rowBytes = rect.Width * 4;
            var drow = new byte[rowBytes];
            var srow = new byte[rowBytes];
            for (int y = 0; y < rect.Height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(dst.Scan0 + y * dst.Stride, drow, 0, rowBytes);
                System.Runtime.InteropServices.Marshal.Copy(src.Scan0 + y * src.Stride, srow, 0, rowBytes);
                bool dirty = false;
                for (int x = 0; x < rect.Width; x++)
                {
                    int i = x * 4;
                    int sa = srow[i + 3];
                    if (sa == 0) continue;
                    double a = sa / 255.0 * alpha * (softMask[(y0 + y) * w + (x0 + x)] / 255.0);
                    if (a <= 0.0) continue;
                    // Straight "over" weighted by backdrop coverage, writing alpha — over bare
                    // paper (dn=0) the masked image keeps its own colour and raises coverage
                    // instead of blending toward white and leaving alpha 0 (which would vanish
                    // at flatten).
                    double dn = drow[i + 3] / 255.0;
                    double outA = a + dn * (1 - a);
                    if (outA <= 0.0) continue;
                    double inv = dn * (1 - a);
                    for (int c = 0; c < 3; c++)
                        drow[i + c] = (byte)((srow[i + c] * a + drow[i + c] * inv) / outA + 0.5);
                    drow[i + 3] = (byte)(outA * 255 + 0.5);
                    dirty = true;
                }
                if (dirty)
                    System.Runtime.InteropServices.Marshal.Copy(drow, 0, dst.Scan0 + y * dst.Stride, rowBytes);
            }
        }
        finally
        {
            _bitmap.UnlockBits(dst);
            _blendScratch.UnlockBits(src);
        }
    }

    private void DrawFormXObject(PdfStream formStream, GraphicsState state,
        bool forceComposite = false)
    {
        // A form hidden by the default optional-content configuration renders as
        // if absent (e.g. a print-only /Background layer wrapping the page scan).
        if (SoftwarePageRenderer.IsOcHidden(formStream.Dict.Get("OC"), _reader, _ocgHidden)) return;
        if (_formDepth > 64) return;
        _formDepth++;
        var savedScope = _scope;
        var savedGdi = _g.Save();
        try
        {
            byte[] content;
            try { content = _reader.DecodeStream(formStream); }
            catch { return; }

            var formResources = _reader.ResolveDict(formStream.Dict.Get("Resources"));
            var formScope = BuildScope(formResources);
            // Does this group's OWN content potentially blend against its backdrop, i.e. does
            // it define a non-Normal blend ExtGState it can apply to an interior fill? If so it
            // must render onto a copy of the real backdrop so that interior blend sees it;
            // otherwise it can render in isolation (transparent layer) and composite at its
            // Do-time alpha/blend — which composes correctly against the coverage-alpha page.
            // Checked before the parent merge so only the group's own gstates count.
            bool hasInternalBlend = false;
            if (formScope.ExtGStates is not null)
                foreach (var eg in formScope.ExtGStates.Values)
                    if (eg.GetName("BM") is { } bm && bm != "Normal") { hasInternalBlend = true; break; }
            // Merge parent resources for fallback lookups (PDF 32000 §8.10 forms may
            // reference names defined only in the enclosing scope).
            MergeInto(formScope.XObjects, savedScope.XObjects);
            MergeInto(formScope.Fonts, savedScope.Fonts);
            MergeInto(formScope.ExtGStates, savedScope.ExtGStates);
            formScope.Patterns ??= savedScope.Patterns;
            formScope.Shadings ??= savedScope.Shadings;
            formScope.ColorSpaces ??= savedScope.ColorSpaces;
            formScope.Properties ??= savedScope.Properties;
            _scope = formScope;

            var formMatrix = ExtractFormMatrix(formStream.Dict);
            var effectiveCtm = formMatrix is not null
                ? GraphicsState.MultiplyMatrices(formMatrix, state.Ctm)
                : (double[])state.Ctm.Clone();

            var bboxClip = BuildBBoxClip(formStream.Dict, effectiveCtm);

            // Transparency group compositing (PDF 32000 §11.6.6): when the form is a
            // transparency group invoked with a non-trivial composite — group fill-alpha
            // (ca via /gs at the Do) below 1, a non-Normal blend mode, or an active soft
            // mask — its contents must render onto a transparent backdrop in a separate
            // layer, which is then composited back to the page at the Do-time alpha /
            // blend / mask. Drawing the contents straight onto the page (the else branch)
            // ignores the group alpha entirely, producing opaque overlays where
            // blended, semi-transparent overlap should appear.
            // forceComposite: an annotation appearance drawn under a /CA constant alpha
            // is composited as a transparency group even without a /Group declaration
            // (PDF 32000 §12.5.2 treats the whole annotation as one group).
            var groupDict = _reader.ResolveDict(formStream.Dict.Get("Group"));
            bool isTransparencyGroup = groupDict is not null && groupDict.GetName("S") == "Transparency";
            // An isolated group (/I true) establishes a transparent backdrop: its contents
            // blend only against each other, shielded from the page/parent backdrop
            // (PDF 32000 §11.4.5). Rendering it inline would let a child's blend mode reach
            // the real backdrop (e.g. a Multiply circle multiplying the page instead of only
            // its sibling), so isolated groups must always composite through their own layer —
            // even when invoked with a trivial Normal / ca=1 composite.
            bool isIsolatedGroup = isTransparencyGroup
                && groupDict!.Get("I") is PdfBoolean iso0 && iso0.Value;
            // A knockout group (/K true) must composite through its own layer even when
            // invoked trivially: its elements replace each other and blend only against
            // the group's INITIAL backdrop, which rendering inline cannot express — an
            // interior Multiply child would blend with its sibling instead of knocking
            // it out.
            bool isKnockoutGroup = isTransparencyGroup
                && groupDict!.Get("K") is PdfBoolean ko0 && ko0.Value;
            bool needsComposite = (isTransparencyGroup || forceComposite) &&
                (state.FillAlpha < 0.999
                 || (!string.IsNullOrEmpty(state.BlendMode) && state.BlendMode != "Normal")
                 || state.SoftMask is not null
                 || isIsolatedGroup
                 || isKnockoutGroup);

            // Inside a coverage pre-pass, a transparency-group Do contributes a BINARY
            // footprint to the enclosing group's outer-blend mask: any pixel its content
            // touches counts as fully covered (the outer blend applies at
            // full strength across a nested layer's whole footprint, keeping fractional
            // weights only for direct content). Q_OBM=bin experiment.
            if (_inCoveragePass && isTransparencyGroup && ObMode is "bin" or "nal" or "bin2")
            {
                if (ObMode == "bin2") StampCenterCellGroupCoverage(content, effectiveCtm, bboxClip);
                else StampBinarizedGroupCoverage(content, effectiveCtm, bboxClip);
                bboxClip?.Dispose();
                return;
            }

            if (needsComposite)
            {
                // /I true = isolated group: contents blend against a transparent backdrop.
                // Default (/I false) = non-isolated: contents blend against the page backdrop,
                // so backdrop-dependent blend modes (e.g. Difference vs the white page) resolve
                // correctly only if the group renders onto a copy of that backdrop.
                // A forced (annotation /CA) composite has no group dict and is isolated.
                bool isolated = groupDict is null
                    || (groupDict.Get("I") is PdfBoolean iso && iso.Value);
                // /K true = knockout group: each element composites against the group's
                // INITIAL backdrop, not the accumulated result — later opaque elements knock
                // out earlier ones (topmost wins) and a blend mode on a child sees only the
                // initial backdrop, so overlapping children do NOT blend with each other
                // (PDF 32000 §11.4.5, §7.3.4).
                bool isKnockout = groupDict is not null
                    && groupDict.Get("K") is PdfBoolean kn && kn.Value;
                RenderGroupComposited(content, effectiveCtm, bboxClip, state, isolated, isKnockout, hasInternalBlend);
            }
            else
                RenderContentStream(content, effectiveCtm, bboxClip, state);
            bboxClip?.Dispose();
        }
        finally
        {
            _scope = savedScope;
            _g.Restore(savedGdi);
            _formDepth--;
        }
    }

    /// <summary>
    /// Render a transparency-group form into a transparent page-sized layer, then
    /// composite that layer back onto the backing bitmap at the Do-time group
    /// alpha / blend mode / soft mask. Mirrors the software renderer's scratch-buffer
    /// group path (CompositeGroupBuffer). The layer renders with a fresh graphics state
    /// (group alpha applies once, at composite time — not inherited into the contents),
    /// inheriting only the outer device-space clip so the group stays bounded.
    /// </summary>
    // Device-space pixel rectangle a transparency group can actually touch: its
    // /BBox (forms clip their content to it) transformed to device space, clamped
    // to the page. Used to bound the backdrop copy and the composite loop so a
    // small group on a huge page costs O(group area), not O(page area) — without
    // this, a 4362×3622pt page at 300 dpi with many groups composites ~274M
    // pixels per group and effectively never finishes.
    private static System.Drawing.Rectangle GroupDeviceBounds(GraphicsPath? bboxClip, int w, int h)
    {
        if (bboxClip is null) return new System.Drawing.Rectangle(0, 0, w, h);
        var b = bboxClip.GetBounds();
        int x0 = Math.Max(0, (int)Math.Floor(b.Left));
        int y0 = Math.Max(0, (int)Math.Floor(b.Top));
        int x1 = Math.Min(w, (int)Math.Ceiling(b.Right));
        int y1 = Math.Min(h, (int)Math.Ceiling(b.Bottom));
        return (x1 > x0 && y1 > y0) ? new System.Drawing.Rectangle(x0, y0, x1 - x0, y1 - y0)
                                    : System.Drawing.Rectangle.Empty;
    }

    // Rent a page-sized ARGB layer bitmap from the pool, or allocate one if the pool
    // is empty (or holds a stale-sized bitmap from a different page). Pooled bitmaps
    // carry stale pixels; callers must re-initialise the region they composite.
    private Bitmap RentLayer(int w, int h)
    {
        while (_layerPool.Count > 0)
        {
            var b = _layerPool.Pop();
            if (b.Width == w && b.Height == h) return b;
            b.Dispose();
        }
        return new Bitmap(w, h, PixelFormat.Format32bppArgb);
    }

    // Page-sized coverage scratch pools. A group-heavy page runs dozens of
    // composites, each needing full-page float/byte coverage buffers; renting
    // them (zeroed) instead of allocating keeps the render's heap spike at a
    // couple of buffers rather than one set per group. Stack discipline makes
    // nested group recursion safe — inner rents while outer's are checked out.
    private readonly Stack<float[]> _covFloatPool = new();

    private readonly Stack<byte[]> _covBytePool = new();

    private float[] RentCovFloat(int n)
    {
        while (_covFloatPool.Count > 0)
        {
            var a = _covFloatPool.Pop();
            if (a.Length == n) { System.Array.Clear(a); return a; }
        }
        return new float[n];
    }

    private byte[] RentCovByte(int n)
    {
        while (_covBytePool.Count > 0)
        {
            var a = _covBytePool.Pop();
            if (a.Length == n) { System.Array.Clear(a); return a; }
        }
        return new byte[n];
    }

    private void RenderGroupComposited(byte[] content, double[] effectiveCtm, GraphicsPath? bboxClip, GraphicsState state, bool isolated, bool isKnockout = false, bool hasInternalBlend = false)
    {
        int w = _bitmap.Width, h = _bitmap.Height;

        // Am I an element of an enclosing knockout group? If so, my Do-time blend mode must
        // act against that group's initial backdrop (already applied as the layer content
        // there), not against my siblings — so composite me plainly (Normal). Captured before
        // rendering my own content flips the flag for my interior.
        bool parentKnockout = _knockoutGroup;

        // Render the group's contents onto a TRANSPARENT backdrop, capturing the group's own
        // colour+coverage, then composite that with the Do-time alpha / blend mode. This is
        // correct for isolated groups (which shield the backdrop) and — because a group's
        // Normal-content result composited at its ca reaches the backdrop identically whether
        // rendered in isolation or onto a backdrop copy — for the common non-isolated case
        // too. It also composes cleanly against the coverage-alpha page (the backdrop-copy
        // path double-counts a partially-covered paper backdrop).
        // The exception is a non-isolated group whose INTERIOR uses a blend mode against its
        // backdrop (hasInternalBlend): that interior blend must see the real backdrop, so such
        // a group renders onto a copy of it. If it composites back Normal the untouched pixels
        // stay equal to the backdrop; if it ALSO carries an outer non-Normal Do-blend, that
        // blend then applies to the backdrop-copy result at the pixels the group actually
        // painted (so a nested Difference and the outer Difference both act on the real
        // backdrop — the blend applies twice).
        bool doBlendNonNormal = !string.IsNullOrEmpty(state.BlendMode) && state.BlendMode != "Normal";
        // Q_OBM=raw: EVERY non-isolated group with an outer non-Normal Do-blend renders on
        // a backdrop copy (not only those with interior blends), and the outer blend is
        // applied to the layer result AS-IS (no backdrop removal), weighted by coverage —
        // the blend hits the backdrop-mixed result a second time, so nested Difference
        // stacks double-apply.
        bool rawOuterBlend = ObMode == "raw";
        bool backdropCopy = !isolated && (hasInternalBlend || (rawOuterBlend && doBlendNonNormal)) && !parentKnockout;
        bool useTransparentLayer = !backdropCopy;
        // A backdrop-copy group with an outer non-Normal Do-blend re-applies that blend to the
        // painted result; the plain backdrop-copy case (Normal Do) just lerps back.
        bool backdropCopyOuterBlend = backdropCopy && doBlendNonNormal;

        // The group only paints within its /BBox; compositing (and the non-isolated
        // backdrop copy) outside that rect is wasted work on large pages.
        var compRect = GroupDeviceBounds(bboxClip, w, h);
        if (compRect.Width <= 0 || compRect.Height <= 0) return; // group maps off-page

        // Capture the inherited clip in device space so the group respects any clip
        // active at the Do (e.g. a page-level `re W n`). GDI+ stores the clip in device
        // coordinates; read it with an identity transform to get device-space geometry.
        var savedT = _g.Transform;
        _g.ResetTransform();
        var deviceClip = _g.Clip;
        _g.Transform = savedT;
        savedT.Dispose();

        // A backdrop-copy group carrying an outer non-Normal Do-blend also needs the group's
        // true per-pixel COVERAGE (so the outer blend is weighted at anti-aliased edges, not
        // applied at full strength). Capture it with a throwaway transparent pre-pass; its
        // alpha channel is the coverage. (The pass renders the same content in isolation, so
        // its colours are discarded — only alpha is used.)
        // Supersampled outer-blend path (Q_SSCOV): render BOTH the group's coverage and its
        // backdrop-copy colour at K× resolution and box-downsample, so the outer blend is
        // weighted by true geometric coverage (finer than 8-bit, with fractional stroke-edge
        // tails) and the colour layer shares the exact same footprint. Using a supersampled
        // mask against the ordinary 1×-rasterized layer is NOT an option — hairline strokes
        // land on different pixels at 1× than their true geometry, and every disagreement
        // re-applies the blend to an unpainted pixel.
        if (backdropCopyOuterBlend && SsCoverage > 1 && !_inCoveragePass)
        {
            try
            {
                float[] covW = CaptureGroupCoverageSS(content, effectiveCtm, bboxClip, deviceClip, compRect, isKnockout, SsCoverage);
                var ssLayer = RenderGroupBackdropCopySS(content, effectiveCtm, bboxClip, deviceClip, compRect, isKnockout, SsCoverage);
                RemoveBackdropF(ssLayer, _bitmap, covW, compRect);
                CompositeGroupLayer(ssLayer, state, state.BlendMode, compRect, covW);
                _layerPool.Push(ssLayer);
                _covFloatPool.Push(covW);
            }
            finally { deviceClip?.Dispose(); }
            return;
        }

        // Q_OBM=alias: the outer blend applies to the raw backdrop-copy layer at full
        // strength under a binary aliased content mask (stair-step tails one px past the
        // AA ink — where the layer still equals the backdrop, Difference gives |B−B|=0
        // exactly, yielding solid-black stroke tails).
        bool aliasMask = backdropCopyOuterBlend && ObMode == "alias";
        float[]? aliasCov = null;
        if (aliasMask)
        {
            var mb = CaptureGroupCoverage(content, effectiveCtm, bboxClip, deviceClip, compRect, isKnockout, aliased: true);
            aliasCov = RentCovFloat(w * h);
            for (int i = 0; i < mb.Length; i++) if (mb[i] > 0) aliasCov[i] = 1f;
            _covBytePool.Push(mb);
        }

        // bin2: collect which pre-pass pixels come from nested-group stamps (they take
        // replace-semantics in the composite). Save/restore around the recursive call.
        var savedStampMask = _stampMask;
        _stampMask = backdropCopyOuterBlend && !aliasMask && ObMode == "bin2" ? RentCovByte(w * h) : null;
        byte[]? coverage = backdropCopyOuterBlend && !aliasMask
            ? CaptureGroupCoverage(content, effectiveCtm, bboxClip, deviceClip, compRect, isKnockout)
            : null;
        byte[]? stampMask = _stampMask;
        _stampMask = savedStampMask;

        var groupBmp = RentLayer(w, h);
        var savedG = _g;
        var savedBmp = _bitmap;
        var savedScratch = _blendScratch;
        var savedKoBackdrop = _koBackdrop;
        var savedKoReplace = _koReplace;
        var gg = Graphics.FromImage(groupBmp);
        try
        {
            // Re-initialise only the BBox sub-rect (the pooled bitmap may carry stale
            // content elsewhere, which is never composited). SourceCopy writes the
            // exact pixels, overwriting any leftovers.
            gg.CompositingMode = CompositingMode.SourceCopy;
            if (useTransparentLayer)
                // Transparent backdrop under the BBox — capture the group's own contribution
                // alone, then composite it with the Do-time blend mode.
                using (var clear = new SolidBrush(GdiColor.Transparent))
                    gg.FillRectangle(clear, compRect);
            else
                // Non-isolated composite → start from a copy of the current backdrop under the
                // BBox so internal blend modes see it. Copy the raw pixels (not DrawImage, which
                // round-trips through premultiplied alpha and perturbs the values).
                CopyRegion(savedBmp, groupBmp, compRect);
            gg.CompositingMode = CompositingMode.SourceOver;
            gg.SmoothingMode = savedG.SmoothingMode;
            gg.PixelOffsetMode = savedG.PixelOffsetMode;
            gg.InterpolationMode = savedG.InterpolationMode;
            gg.TextRenderingHint = savedG.TextRenderingHint;
            gg.CompositingQuality = savedG.CompositingQuality;
            if (deviceClip is not null) gg.Clip = deviceClip;

            // Knockout: freeze this group's INITIAL backdrop (the just-initialised layer
            // content — transparent, or the backdrop copy). Child-group elements blend
            // against this snapshot with their own blend mode and REPLACE whatever
            // earlier siblings painted (PDF 32000 §11.4.5).
            if (isKnockout && !_inCoveragePass)
            {
                _koBackdrop = RentLayer(w, h);
                CopyRegion(groupBmp, _koBackdrop, compRect);
                _koReplace = KnockoutOverride switch
                {
                    "replace" => true,
                    "over" => false,
                    _ => !isolated,
                };
            }
            else
                _koBackdrop = null;

            _g = gg;
            _bitmap = groupBmp;
            _blendScratch = null; // a fresh same-size scratch is allocated on demand for blended fills inside the group
            _knockoutGroup = isKnockout; // my own elements composite against my initial backdrop
            RenderContentStream(content, effectiveCtm, bboxClip);
            _g.Flush();
        }
        finally
        {
            _g = savedG;
            _bitmap = savedBmp;
            _blendScratch?.Dispose();
            _blendScratch = savedScratch;
            _knockoutGroup = parentKnockout;
            if (_koBackdrop is not null) _layerPool.Push(_koBackdrop);
            _koBackdrop = savedKoBackdrop;
            _koReplace = savedKoReplace;
            gg.Dispose();
            deviceClip?.Dispose();
        }

        // Transparent-backdrop layers carry the Do-time blend mode (they composite onto the
        // backdrop like any source). Backdrop-copy layers already blended internally against
        // the backdrop, so they composite back with a plain Normal lerp (untouched pixels
        // equal the backdrop and stay unchanged). Inside a knockout parent, blend is forced
        // Normal so this element does not blend with earlier siblings.
        var compositeBlend = useTransparentLayer && !parentKnockout ? state.BlendMode : "Normal";
        if (backdropCopyOuterBlend)
        {
            if (rawOuterBlend)
            {
                // KD4 rule as measured: blend the layer result AS-IS against the backdrop,
                // weighted by the group's own coverage — no backdrop removal.
                var covF = RentCovFloat(w * h);
                for (int i = 0; i < coverage!.Length; i++) covF[i] = coverage[i] / 255f;
                CompositeGroupLayer(groupBmp, state, state.BlendMode, compRect, covF);
                _covFloatPool.Push(covF);
            }
            else if (aliasCov is not null)
                // Raw-layer blend under the aliased mask: Cs = the layer as painted
                // (backdrop mix included), applied at full strength wherever masked.
                CompositeGroupLayer(groupBmp, state, state.BlendMode, compRect, aliasCov);
            else
            {
                // Remove the backdrop from the backdrop-copy result to recover the group's own
                // colour, tag each pixel with its true coverage, then composite with the outer
                // Do-blend. Uncovered pixels (coverage 0) are naturally skipped; edge pixels are
                // weighted by fractional coverage. Stamped nested-footprint pixels are left
                // as-is by the removal and take replace-semantics in the composite.
                RemoveBackdrop(groupBmp, savedBmp, coverage!, compRect, stampMask);
                CompositeGroupLayer(groupBmp, state, state.BlendMode, compRect, null, stampMask);
            }
        }
        else if (parentKnockout && savedKoBackdrop is not null)
            // Element of a knockout group: blend with MY OWN mode against the group's
            // frozen initial backdrop. Sibling pixels underneath are REPLACED when the
            // group is non-isolated or this element carries partial constant alpha
            // (stacking would double-apply it); an opaque element in an isolated
            // knockout composites over them, keeping sibling AA at shared edges.
            CompositeGroupLayer(groupBmp, state, state.BlendMode, compRect,
                koBackdrop: savedKoBackdrop, koReplace: savedKoReplace || state.FillAlpha < 0.999);
        else
            CompositeGroupLayer(groupBmp, state, compositeBlend, compRect);
        _layerPool.Push(groupBmp); // return to pool for reuse instead of disposing
        if (aliasCov is not null) _covFloatPool.Push(aliasCov);
        if (coverage is not null) _covBytePool.Push(coverage);
        if (stampMask is not null) _covBytePool.Push(stampMask);
    }

    /// <summary>
    /// Render group content onto a throwaway transparent layer and return its alpha channel
    /// (page-indexed, one byte per pixel) as the group's own coverage. Used to weight the
    /// outer blend of a non-isolated backdrop-copy group at anti-aliased edges.
    /// </summary>
    /// <summary>
    /// Coverage-pre-pass handling of a nested transparency-group Do: render the group's
    /// content into a scratch transparent layer, then stamp every touched pixel into the
    /// pre-pass bitmap at FULL alpha. The enclosing group's outer blend applies at full
    /// strength across a nested layer's footprint; only direct content keeps fractional
    /// anti-aliased weights.
    /// </summary>
    private void StampBinarizedGroupCoverage(byte[] content, double[] effectiveCtm, GraphicsPath? bboxClip)
    {
        int w = _bitmap.Width, h = _bitmap.Height;
        var rect = System.Drawing.Rectangle.Intersect(GroupDeviceBounds(bboxClip, w, h),
            new System.Drawing.Rectangle(0, 0, w, h));
        if (rect.Width <= 0 || rect.Height <= 0) return;

        var savedT = _g.Transform;
        _g.ResetTransform();
        var deviceClip = _g.Clip;
        _g.Transform = savedT;
        savedT.Dispose();

        var scratch = RentLayer(w, h);
        var savedG = _g; var savedBmp = _bitmap; var savedScratch = _blendScratch;
        var sg = Graphics.FromImage(scratch);
        try
        {
            sg.CompositingMode = CompositingMode.SourceCopy;
            using (var clear = new SolidBrush(GdiColor.Transparent))
                sg.FillRectangle(clear, rect);
            sg.CompositingMode = CompositingMode.SourceOver;
            // "nal": rasterize the nested footprint ALIASED (center-sample binary) —
            // correct at fill edges, where binarized anti-aliased coverage would count
            // the whole low-coverage half of the edge as covered.
            sg.SmoothingMode = ObMode == "nal" ? SmoothingMode.None : savedG.SmoothingMode;
            sg.PixelOffsetMode = savedG.PixelOffsetMode;
            sg.InterpolationMode = savedG.InterpolationMode;
            sg.TextRenderingHint = ObMode == "nal" ? System.Drawing.Text.TextRenderingHint.SingleBitPerPixel : savedG.TextRenderingHint;
            sg.CompositingQuality = savedG.CompositingQuality;
            if (deviceClip is not null) sg.Clip = deviceClip;
            _g = sg; _bitmap = scratch; _blendScratch = null;
            RenderContentStream(content, effectiveCtm, bboxClip);
            _g.Flush();
        }
        finally
        {
            _g = savedG; _bitmap = savedBmp; _blendScratch?.Dispose(); _blendScratch = savedScratch;
            sg.Dispose();
            deviceClip?.Dispose();
        }

        // Stamp: any touched scratch pixel becomes fully covered in the pre-pass bitmap.
        savedG.Flush();
        var sr = scratch.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var dr = _bitmap.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            int rb = rect.Width * 4;
            var srow = new byte[rb];
            var drow = new byte[rb];
            for (int y = 0; y < rect.Height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(sr.Scan0 + y * sr.Stride, srow, 0, rb);
                System.Runtime.InteropServices.Marshal.Copy(dr.Scan0 + y * dr.Stride, drow, 0, rb);
                bool dirty = false;
                for (int x = 0; x < rect.Width; x++)
                {
                    if (srow[x * 4 + 3] < BinThreshold) continue;
                    drow[x * 4 + 3] = 255;
                    dirty = true;
                }
                if (dirty)
                    System.Runtime.InteropServices.Marshal.Copy(drow, 0, dr.Scan0 + y * dr.Stride, rb);
            }
        }
        finally { scratch.UnlockBits(sr); _bitmap.UnlockBits(dr); }
        _layerPool.Push(scratch);
    }

    /// <summary>
    /// Center-cell variant of the nested-footprint stamp: a pixel counts as covered iff
    /// the nested group's geometry touches the half-open CELL CENTERED on the pixel,
    /// [x−0.5,x+0.5)×[y−0.5,y+0.5) — the measured convention for both
    /// strokes and fills. Computed from a K=2 supersampled render: pixel (x,y) is covered
    /// iff any of the four subsamples {2x−1,2x}×{2y−1,2y} has non-zero alpha.
    /// </summary>
    private void StampCenterCellGroupCoverage(byte[] content, double[] effectiveCtm, GraphicsPath? bboxClip)
    {
        int w = _bitmap.Width, h = _bitmap.Height;
        var rect = System.Drawing.Rectangle.Intersect(GroupDeviceBounds(bboxClip, w, h),
            new System.Drawing.Rectangle(0, 0, w, h));
        if (rect.Width <= 0 || rect.Height <= 0) return;

        // Degrade to the plain any-touch stamp when a ×2 page buffer would be excessive
        // (deep nesting inside an already-supersampled scratch multiplies the size).
        if ((long)w * h * 16 > 512L * 1024 * 1024) { StampBinarizedGroupCoverage(content, effectiveCtm, bboxClip); return; }
        const int k = 2;
        int sw = w * k, sh = h * k;

        var savedT = _g.Transform;
        _g.ResetTransform();
        var deviceClip = _g.Clip;
        _g.Transform = savedT;
        savedT.Dispose();
        using var devToSS = new GdiMatrix(k, 0, 0, k, 0, 0);

        var savedG = _g; var savedBmp = _bitmap; var savedScratch = _blendScratch;
        var savedScale = _scale; var savedScaleY = _scaleY; var savedPixelH = _pixelH;
        using var ssBmp = new Bitmap(sw, sh, PixelFormat.Format32bppArgb);
        var sg = Graphics.FromImage(ssBmp);
        GraphicsPath? bboxSS = null;
        Region? clipSS = null;
        try
        {
            sg.SmoothingMode = savedG.SmoothingMode;
            // Half-pixel offset so supersample pixel c integrates [c,c+1) — the {2x−1,2x}
            // subsample window then covers exactly the device pixel's center cell. (The
            // page renders with PixelOffsetMode.None, whose grid is half-shifted; reusing
            // it here would misalign the window by one subsample on the low side.)
            sg.PixelOffsetMode = PixelOffsetMode.Half;
            sg.InterpolationMode = savedG.InterpolationMode;
            sg.TextRenderingHint = savedG.TextRenderingHint;
            sg.CompositingQuality = savedG.CompositingQuality;
            if (bboxClip is not null) { bboxSS = (GraphicsPath)bboxClip.Clone(); bboxSS.Transform(devToSS); }
            if (deviceClip is not null) { clipSS = deviceClip.Clone(); clipSS.Transform(devToSS); sg.Clip = clipSS; }
            _g = sg; _bitmap = ssBmp; _blendScratch = null;
            _scale = savedScale * k; _scaleY = savedScaleY * k; _pixelH = sh;
            RenderContentStream(content, effectiveCtm, bboxSS);
            _g.Flush();
        }
        finally
        {
            _g = savedG; _bitmap = savedBmp; _blendScratch?.Dispose(); _blendScratch = savedScratch;
            _scale = savedScale; _scaleY = savedScaleY; _pixelH = savedPixelH;
            sg.Dispose(); bboxSS?.Dispose(); clipSS?.Dispose();
            deviceClip?.Dispose();
        }

        savedG.Flush();
        // Subsample window: rows/cols 2x−1..2x (clamped) — the half-open center cell.
        var ssRect = new System.Drawing.Rectangle(
            Math.Max(0, rect.Left * k - 1), Math.Max(0, rect.Top * k - 1),
            Math.Min(sw, rect.Right * k) - Math.Max(0, rect.Left * k - 1),
            Math.Min(sh, rect.Bottom * k) - Math.Max(0, rect.Top * k - 1));
        var sr = ssBmp.LockBits(ssRect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var dr = _bitmap.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            int srb = ssRect.Width * 4;
            var rows = new byte[2][] { new byte[srb], new byte[srb] };
            var drow = new byte[rect.Width * 4];
            for (int y = 0; y < rect.Height; y++)
            {
                int py = rect.Top + y;
                int sy0 = Math.Max(0, py * k - 1), sy1 = py * k;
                System.Runtime.InteropServices.Marshal.Copy(sr.Scan0 + (sy0 - ssRect.Top) * sr.Stride, rows[0], 0, srb);
                System.Runtime.InteropServices.Marshal.Copy(sr.Scan0 + (sy1 - ssRect.Top) * sr.Stride, rows[1], 0, srb);
                System.Runtime.InteropServices.Marshal.Copy(dr.Scan0 + y * dr.Stride, drow, 0, drow.Length);
                bool dirty = false;
                for (int x = 0; x < rect.Width; x++)
                {
                    int px = rect.Left + x;
                    int sx0 = Math.Max(0, px * k - 1), sx1 = px * k;
                    int o0 = (sx0 - ssRect.Left) * 4 + 3, o1 = (sx1 - ssRect.Left) * 4 + 3;
                    // Q_BTH: minimum subsample alpha to count as touched — trims graze-level
                    // anti-aliasing tails where GDI's stroke geometry overshoots the true footprint.
                    if (rows[0][o0] < BinThreshold && rows[0][o1] < BinThreshold && rows[1][o0] < BinThreshold && rows[1][o1] < BinThreshold) continue;
                    drow[x * 4 + 3] = 255;
                    // Record the stamp for the composite's replace-semantics — only when
                    // this pre-pass runs at page resolution (a deeper stamp inside a ×2
                    // scratch has scaled coordinates that must not index the page mask).
                    if (_stampMask is not null && _stampMask.Length == w * h)
                        _stampMask[py * w + px] = 1;
                    dirty = true;
                }
                if (dirty)
                    System.Runtime.InteropServices.Marshal.Copy(drow, 0, dr.Scan0 + y * dr.Stride, drow.Length);
            }
        }
        finally { ssBmp.UnlockBits(sr); _bitmap.UnlockBits(dr); }
    }

    // aliased: render the pass without anti-aliasing (SmoothingMode.None, aliased text) —
    // used to build the binary any-coverage content mask for the raw-layer outer blend.
    private byte[] CaptureGroupCoverage(byte[] content, double[] effectiveCtm, GraphicsPath? bboxClip,
        Region? deviceClip, System.Drawing.Rectangle compRect, bool isKnockout, bool aliased = false)
    {
        int w = _bitmap.Width, h = _bitmap.Height;
        var covBmp = RentLayer(w, h);
        var savedG = _g; var savedBmp = _bitmap; var savedScratch = _blendScratch; var savedKo = _knockoutGroup;
        var savedCov = _inCoveragePass;
        var cg = Graphics.FromImage(covBmp);
        try
        {
            cg.CompositingMode = CompositingMode.SourceCopy;
            using (var clear = new SolidBrush(GdiColor.Transparent))
                cg.FillRectangle(clear, compRect);
            cg.CompositingMode = CompositingMode.SourceOver;
            cg.SmoothingMode = aliased ? SmoothingMode.None : savedG.SmoothingMode;
            cg.PixelOffsetMode = savedG.PixelOffsetMode;
            cg.InterpolationMode = savedG.InterpolationMode;
            cg.TextRenderingHint = aliased ? System.Drawing.Text.TextRenderingHint.SingleBitPerPixel : savedG.TextRenderingHint;
            cg.CompositingQuality = savedG.CompositingQuality;
            if (deviceClip is not null) cg.Clip = deviceClip;
            _g = cg; _bitmap = covBmp; _blendScratch = null; _knockoutGroup = isKnockout;
            _inCoveragePass = true;
            RenderContentStream(content, effectiveCtm, bboxClip);
            _g.Flush();
        }
        finally
        {
            _g = savedG; _bitmap = savedBmp; _blendScratch?.Dispose(); _blendScratch = savedScratch; _knockoutGroup = savedKo;
            _inCoveragePass = savedCov;
            cg.Dispose();
        }

        var alpha = RentCovByte(w * h);
        var rect = System.Drawing.Rectangle.Intersect(compRect, new System.Drawing.Rectangle(0, 0, w, h));
        if (rect.Width > 0 && rect.Height > 0)
        {
            var bd = covBmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var row = new byte[rect.Width * 4];
                for (int y = 0; y < rect.Height; y++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(bd.Scan0 + y * bd.Stride, row, 0, row.Length);
                    int py = rect.Top + y;
                    for (int x = 0; x < rect.Width; x++)
                        alpha[py * w + rect.Left + x] = row[x * 4 + 3];
                }
            }
            finally { covBmp.UnlockBits(bd); }
        }
        _layerPool.Push(covBmp);
        return alpha;
    }

    /// <summary>
    /// Supersampled variant of <see cref="CaptureGroupCoverage"/>: render the group's
    /// content at K× resolution over its device rect and box-downsample the alpha to a
    /// page-indexed float coverage in [0,1]. This is true geometric coverage — finer
    /// than 8 bits, with fractional tails at stroke edges that the 1× rasterization
    /// (hairline pens, sample-grid phase) misses entirely.
    /// </summary>
    private float[] CaptureGroupCoverageSS(byte[] content, double[] effectiveCtm, GraphicsPath? bboxClip,
        Region? deviceClip, System.Drawing.Rectangle compRect, bool isKnockout, int k)
    {
        int w = _bitmap.Width, h = _bitmap.Height;
        // The pixel mapping runs through the page matrix (_scale/_scaleY/_pixelH), so the
        // supersample layer covers the FULL page at K× and the page matrix is scaled — the
        // CTM stays in PDF space, untouched. Cap the buffer at ~512MB; degrade K rather
        // than throw (K=1 → coverage equivalent to the 1× capture).
        while (k > 1 && (long)w * k * h * k * 4 > 512L * 1024 * 1024) k--;
        int sw = w * k, sh = h * k;

        // 1× device-pixel space → supersample space: uniform scale K about the origin.
        using var devToSS = new GdiMatrix(k, 0, 0, k, 0, 0);

        var savedG = _g; var savedBmp = _bitmap; var savedScratch = _blendScratch; var savedKo = _knockoutGroup;
        var savedCov = _inCoveragePass;
        var savedScale = _scale; var savedScaleY = _scaleY; var savedPixelH = _pixelH;
        using var ssBmp = new Bitmap(sw, sh, PixelFormat.Format32bppArgb);
        var sg = Graphics.FromImage(ssBmp);
        GraphicsPath? bboxSS = null;
        Region? clipSS = null;
        try
        {
            sg.SmoothingMode = savedG.SmoothingMode;
            sg.PixelOffsetMode = savedG.PixelOffsetMode;
            sg.InterpolationMode = savedG.InterpolationMode;
            sg.TextRenderingHint = savedG.TextRenderingHint;
            sg.CompositingQuality = savedG.CompositingQuality;
            if (bboxClip is not null) { bboxSS = (GraphicsPath)bboxClip.Clone(); bboxSS.Transform(devToSS); }
            if (deviceClip is not null) { clipSS = deviceClip.Clone(); clipSS.Transform(devToSS); sg.Clip = clipSS; }
            _g = sg; _bitmap = ssBmp; _blendScratch = null; _knockoutGroup = isKnockout;
            _inCoveragePass = true;
            _scale = savedScale * k; _scaleY = savedScaleY * k; _pixelH = sh;
            RenderContentStream(content, effectiveCtm, bboxSS);
            _g.Flush();
        }
        finally
        {
            _g = savedG; _bitmap = savedBmp; _blendScratch?.Dispose(); _blendScratch = savedScratch; _knockoutGroup = savedKo;
            _inCoveragePass = savedCov;
            _scale = savedScale; _scaleY = savedScaleY; _pixelH = savedPixelH;
            sg.Dispose(); bboxSS?.Dispose(); clipSS?.Dispose();
        }

        // Box-downsample alpha K×K → float coverage, page-indexed, compRect only.
        var cov = RentCovFloat(w * h);
        var rect = System.Drawing.Rectangle.Intersect(compRect, new System.Drawing.Rectangle(0, 0, w, h));
        if (rect.Width <= 0 || rect.Height <= 0) return cov;
        var ssRect = new System.Drawing.Rectangle(rect.Left * k, rect.Top * k, rect.Width * k, rect.Height * k);
        var bd = ssBmp.LockBits(ssRect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var rows = new byte[k][];
            for (int i = 0; i < k; i++) rows[i] = new byte[ssRect.Width * 4];
            float norm = 1f / (k * k * 255f);
            for (int y = 0; y < rect.Height; y++)
            {
                for (int i = 0; i < k; i++)
                    System.Runtime.InteropServices.Marshal.Copy((IntPtr)(bd.Scan0.ToInt64() + (long)(y * k + i) * bd.Stride), rows[i], 0, ssRect.Width * 4);
                int py = rect.Top + y;
                for (int x = 0; x < rect.Width; x++)
                {
                    int sum = 0;
                    for (int i = 0; i < k; i++)
                    {
                        var row = rows[i];
                        int o = x * k * 4 + 3;
                        for (int j = 0; j < k; j++) sum += row[o + j * 4];
                    }
                    cov[py * w + rect.Left + x] = sum * norm;
                }
            }
        }
        finally { ssBmp.UnlockBits(bd); }
        return cov;
    }

    /// <summary>
    /// Companion to <see cref="CaptureGroupCoverageSS"/>: render the group's content at K×
    /// onto a K×-upsampled (pixel-replicated) copy of the current backdrop, then
    /// box-downsample the result into a device-resolution layer. The colour footprint then
    /// coincides with the supersampled coverage mask, byte-exact on untouched pixels.
    /// </summary>
    private Bitmap RenderGroupBackdropCopySS(byte[] content, double[] effectiveCtm, GraphicsPath? bboxClip,
        Region? deviceClip, System.Drawing.Rectangle compRect, bool isKnockout, int k)
    {
        int w = _bitmap.Width, h = _bitmap.Height;
        while (k > 1 && (long)w * k * h * k * 4 > 512L * 1024 * 1024) k--;
        int sw = w * k, sh = h * k;
        var rect = System.Drawing.Rectangle.Intersect(compRect, new System.Drawing.Rectangle(0, 0, w, h));
        var ssRect = new System.Drawing.Rectangle(rect.Left * k, rect.Top * k, rect.Width * k, rect.Height * k);
        using var devToSS = new GdiMatrix(k, 0, 0, k, 0, 0);

        var savedG = _g; var savedBmp = _bitmap; var savedScratch = _blendScratch; var savedKo = _knockoutGroup;
        var savedCov = _inCoveragePass;
        var savedScale = _scale; var savedScaleY = _scaleY; var savedPixelH = _pixelH;
        using var ssBmp = new Bitmap(sw, sh, PixelFormat.Format32bppArgb);

        // Upsample the backdrop under the group rect: replicate each backdrop pixel into a
        // K×K block (raw bytes — no resampling filter may perturb the values).
        if (rect.Width > 0 && rect.Height > 0)
        {
            var br = savedBmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var sr = ssBmp.LockBits(ssRect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                var srcRow = new byte[rect.Width * 4];
                var ssRow = new byte[ssRect.Width * 4];
                for (int y = 0; y < rect.Height; y++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(br.Scan0 + y * br.Stride, srcRow, 0, srcRow.Length);
                    for (int x = 0; x < rect.Width; x++)
                        for (int j = 0; j < k; j++)
                            System.Array.Copy(srcRow, x * 4, ssRow, (x * k + j) * 4, 4);
                    for (int i = 0; i < k; i++)
                        System.Runtime.InteropServices.Marshal.Copy(ssRow, 0, (IntPtr)(sr.Scan0.ToInt64() + (long)(y * k + i) * sr.Stride), ssRow.Length);
                }
            }
            finally { savedBmp.UnlockBits(br); ssBmp.UnlockBits(sr); }
        }

        var sg = Graphics.FromImage(ssBmp);
        GraphicsPath? bboxSS = null;
        Region? clipSS = null;
        try
        {
            sg.SmoothingMode = savedG.SmoothingMode;
            sg.PixelOffsetMode = savedG.PixelOffsetMode;
            sg.InterpolationMode = savedG.InterpolationMode;
            sg.TextRenderingHint = savedG.TextRenderingHint;
            sg.CompositingQuality = savedG.CompositingQuality;
            if (bboxClip is not null) { bboxSS = (GraphicsPath)bboxClip.Clone(); bboxSS.Transform(devToSS); }
            if (deviceClip is not null) { clipSS = deviceClip.Clone(); clipSS.Transform(devToSS); sg.Clip = clipSS; }
            _g = sg; _bitmap = ssBmp; _blendScratch = null; _knockoutGroup = isKnockout;
            _inCoveragePass = true;
            _scale = savedScale * k; _scaleY = savedScaleY * k; _pixelH = sh;
            RenderContentStream(content, effectiveCtm, bboxSS);
            _g.Flush();
        }
        finally
        {
            _g = savedG; _bitmap = savedBmp; _blendScratch?.Dispose(); _blendScratch = savedScratch; _knockoutGroup = savedKo;
            _inCoveragePass = savedCov;
            _scale = savedScale; _scaleY = savedScaleY; _pixelH = savedPixelH;
            sg.Dispose(); bboxSS?.Dispose(); clipSS?.Dispose();
        }

        // Box-downsample RGB into a device-resolution layer.
        var layer = RentLayer(w, h);
        if (rect.Width > 0 && rect.Height > 0)
        {
            var bd = ssBmp.LockBits(ssRect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var ld = layer.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                var rows = new byte[k][];
                for (int i = 0; i < k; i++) rows[i] = new byte[ssRect.Width * 4];
                var outRow = new byte[rect.Width * 4];
                float norm = 1f / (k * k);
                for (int y = 0; y < rect.Height; y++)
                {
                    for (int i = 0; i < k; i++)
                        System.Runtime.InteropServices.Marshal.Copy((IntPtr)(bd.Scan0.ToInt64() + (long)(y * k + i) * bd.Stride), rows[i], 0, ssRect.Width * 4);
                    for (int x = 0; x < rect.Width; x++)
                    {
                        int sb = 0, sg2 = 0, sr2 = 0, sa = 0;
                        for (int i = 0; i < k; i++)
                        {
                            var row = rows[i];
                            int o = x * k * 4;
                            for (int j = 0; j < k; j++)
                            {
                                sb += row[o + j * 4]; sg2 += row[o + j * 4 + 1];
                                sr2 += row[o + j * 4 + 2]; sa += row[o + j * 4 + 3];
                            }
                        }
                        int o2 = x * 4;
                        outRow[o2] = (byte)(sb * norm + 0.5f);
                        outRow[o2 + 1] = (byte)(sg2 * norm + 0.5f);
                        outRow[o2 + 2] = (byte)(sr2 * norm + 0.5f);
                        outRow[o2 + 3] = (byte)(sa * norm + 0.5f);
                    }
                    System.Runtime.InteropServices.Marshal.Copy(outRow, 0, ld.Scan0 + y * ld.Stride, outRow.Length);
                }
            }
            finally { ssBmp.UnlockBits(bd); layer.UnlockBits(ld); }
        }
        return layer;
    }

    /// <summary>
    /// Float-coverage variant of <see cref="RemoveBackdrop"/>: recover the group's own
    /// colour from a backdrop-copy layer using the supersampled coverage. Where coverage
    /// is positive but the layer pixel equals the backdrop, the recovered colour IS the
    /// backdrop — the outer blend then re-applies to (B,B), giving the
    /// fractional stroke-tail behaviour.
    /// </summary>
    private static void RemoveBackdropF(Bitmap layer, Bitmap backdrop, float[] coverage, System.Drawing.Rectangle bounds)
    {
        int w = layer.Width, h = layer.Height;
        bounds = System.Drawing.Rectangle.Intersect(bounds, new System.Drawing.Rectangle(0, 0, w, h));
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        var lr = layer.LockBits(bounds, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var br = backdrop.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int rb = bounds.Width * 4;
            var lrow = new byte[rb];
            var brow = new byte[rb];
            for (int y = 0; y < bounds.Height; y++)
            {
                int py = bounds.Top + y;
                System.Runtime.InteropServices.Marshal.Copy(lr.Scan0 + y * lr.Stride, lrow, 0, rb);
                System.Runtime.InteropServices.Marshal.Copy(br.Scan0 + y * br.Stride, brow, 0, rb);
                for (int x = 0; x < bounds.Width; x++)
                {
                    float a = coverage[py * w + bounds.Left + x];
                    int o = x * 4;
                    if (a <= 0f) { lrow[o + 3] = 0; continue; }
                    for (int c = 0; c < 3; c++)
                    {
                        double cg = (lrow[o + c] - (1 - a) * brow[o + c]) / a;
                        lrow[o + c] = (byte)(cg < 0 ? 0 : cg > 255 ? 255 : cg + 0.5);
                    }
                    lrow[o + 3] = (byte)(a * 255f + 0.5f);
                }
                System.Runtime.InteropServices.Marshal.Copy(lrow, 0, lr.Scan0 + y * lr.Stride, rb);
            }
        }
        finally { layer.UnlockBits(lr); backdrop.UnlockBits(br); }
    }

    /// <summary>
    /// Turn a backdrop-copy layer (backdrop B with the group painted over it) into the group's
    /// own straight-alpha contribution: recover Cg = (Cbc − (1−α)·B)/α per pixel and store the
    /// true coverage α (from <paramref name="coverage"/>) in the layer's alpha channel, so a
    /// following CompositeGroupLayer applies the outer blend weighted by real coverage.
    /// </summary>
    private static void RemoveBackdrop(Bitmap layer, Bitmap backdrop, byte[] coverage, System.Drawing.Rectangle bounds, byte[]? stampMask = null)
    {
        int w = layer.Width, h = layer.Height;
        bounds = System.Drawing.Rectangle.Intersect(bounds, new System.Drawing.Rectangle(0, 0, w, h));
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        var lr = layer.LockBits(bounds, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var br = backdrop.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int rb = bounds.Width * 4;
            var lrow = new byte[rb];
            var brow = new byte[rb];
            for (int y = 0; y < bounds.Height; y++)
            {
                int py = bounds.Top + y;
                System.Runtime.InteropServices.Marshal.Copy(lr.Scan0 + y * lr.Stride, lrow, 0, rb);
                System.Runtime.InteropServices.Marshal.Copy(br.Scan0 + y * br.Stride, brow, 0, rb);
                for (int x = 0; x < bounds.Width; x++)
                {
                    int a = coverage[py * w + bounds.Left + x];
                    int o = x * 4;
                    // Stamped nested-footprint pixel: keep the layer value AND alpha as
                    // rendered — the composite applies the outer blend to it directly.
                    if (stampMask is not null && stampMask[py * w + bounds.Left + x] != 0) continue;
                    if (a == 0) { lrow[o + 3] = 0; continue; }
                    double af = a / 255.0;
                    // Straight-alpha un-compositing: the layer holds the group's colour
                    // OVER the backdrop copy, both with alpha — Cl·αl = Cg·a + Cb·αb·(1−a).
                    // The backdrop is NOT necessarily opaque (bare transparent paper has
                    // αb=0; anti-aliased backdrop edges sit in between), so both sides
                    // must be alpha-weighted before removal. With αl=αb=1 this reduces to
                    // the plain opaque-backdrop formula.
                    double al = lrow[o + 3] / 255.0;
                    double ab = brow[o + 3] / 255.0;
                    for (int c = 0; c < 3; c++)
                    {
                        double cg = (lrow[o + c] * al - brow[o + c] * ab * (1 - af)) / af;
                        lrow[o + c] = (byte)(cg < 0 ? 0 : cg > 255 ? 255 : cg + 0.5);
                    }
                    lrow[o + 3] = (byte)a;
                }
                System.Runtime.InteropServices.Marshal.Copy(lrow, 0, lr.Scan0 + y * lr.Stride, rb);
            }
        }
        finally { layer.UnlockBits(lr); backdrop.UnlockBits(br); }
    }

    /// <summary>
    /// Per-pixel composite of a rendered group layer onto the backing bitmap using the
    /// general PDF "over" formula with backdrop alpha (so a layer composited onto another
    /// transparent group layer is not darkened toward black). a = srcAlpha·groupAlpha·softMask.
    /// </summary>
    // covWeight (optional, page-indexed [0,1]): overrides the layer's 8-bit alpha as the
    // per-pixel source coverage — used by the backdrop-copy outer-blend path with a
    // supersampled geometric mask. Where it is positive but the 1× render painted
    // nothing, the layer pixel still holds the backdrop colour, so the blend is
    // re-applied to (B,B) — exactly the stroke-tail behaviour described above.
    // stampMask (optional, page-indexed): pixels stamped by a nested-group footprint take
    // replace-semantics — the blend applies to the raw layer value at full mask weight and
    // the pixel takes the layer's own alpha: Cnew·αnew = (1−a)·Cb·αb + a·Cs'·αl with
    // Cs' = (1−αb)·L + αb·Blend(B,L), αnew = (1−a)·αb + a·αl, a = groupAlpha·softMask.
    private void CompositeGroupLayer(Bitmap layer, GraphicsState state, string blendMode, System.Drawing.Rectangle bounds, float[]? covWeight = null, byte[]? stampMask = null, Bitmap? koBackdrop = null, bool koReplace = true)
    {
        int w = _bitmap.Width, h = _bitmap.Height;
        double ga = System.Math.Clamp(state.FillAlpha, 0.0, 1.0);
        if (ga <= 0.0) return;
        // Clamp the work region to the page; only this rectangle (the group's BBox)
        // can contain non-transparent layer pixels.
        bounds = System.Drawing.Rectangle.Intersect(bounds, new System.Drawing.Rectangle(0, 0, w, h));
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        var mode = Rasterizer.BlendModes.Parse(blendMode);
        byte[]? softMask = state.SoftMask is { } sm ? GetSoftMaskAlpha(sm) : null;

        _g.Flush();
        var rect = new System.Drawing.Rectangle(0, 0, w, h);
        var dst = _bitmap.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var src = layer.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var ko = koBackdrop?.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int x0 = bounds.Left, x1 = bounds.Right;
            int segBytes = (x1 - x0) * 4;
            var drow = new byte[segBytes];
            var srow = new byte[segBytes];
            var krow = ko is not null ? new byte[segBytes] : null;
            for (int y = bounds.Top; y < bounds.Bottom; y++)
            {
                long dRow = dst.Scan0.ToInt64() + (long)y * dst.Stride + (long)x0 * 4;
                long sRow = src.Scan0.ToInt64() + (long)y * src.Stride + (long)x0 * 4;
                System.Runtime.InteropServices.Marshal.Copy((IntPtr)dRow, drow, 0, segBytes);
                System.Runtime.InteropServices.Marshal.Copy((IntPtr)sRow, srow, 0, segBytes);
                if (ko is not null)
                    System.Runtime.InteropServices.Marshal.Copy((IntPtr)(ko.Scan0.ToInt64() + (long)y * ko.Stride + (long)x0 * 4), krow!, 0, segBytes);
                bool dirty = false;
                for (int x = x0; x < x1; x++)
                {
                    int i = (x - x0) * 4;
                    if (krow is not null)
                    {
                        // Knockout element: the blend mode acts against the group's frozen
                        // INITIAL backdrop, never against earlier siblings
                        // (PDF 32000 §11.4.5). Q_KO selects what happens to the sibling
                        // pixels underneath: "replace" = spec knockout (element over b0
                        // replaces the accumulated pixel wherever it has coverage);
                        // default = blend-vs-b0 but alpha-composite over the accumulated
                        // result, which keeps sibling AA at fractional-coverage edges.
                        double ka = (covWeight is not null ? covWeight[y * w + x] : srow[i + 3] / 255.0) * ga;
                        if (softMask is not null) ka *= softMask[y * w + x] / 255.0;
                        if (ka <= 0.0) continue;
                        int ksb = srow[i], ksg = srow[i + 1], ksr = srow[i + 2];
                        int kdb = krow[i], kdg = krow[i + 1], kdr = krow[i + 2];
                        double kdn = krow[i + 3] / 255.0;
                        double kbr = ksr, kbg = ksg, kbb = ksb;
                        if (mode != Rasterizer.BlendMode.Normal && kdn > 0.0)
                        {
                            Rasterizer.BlendModes.Blend(mode, kdr, kdg, kdb, ksr, ksg, ksb, out int zr, out int zg, out int zb);
                            kbr = (1 - kdn) * ksr + kdn * zr;
                            kbg = (1 - kdn) * ksg + kdn * zg;
                            kbb = (1 - kdn) * ksb + kdn * zb;
                        }
                        double kbdn, kbb2, kbg2, kbr2;
                        if (koReplace)
                        { kbdn = kdn; kbb2 = kdb; kbg2 = kdg; kbr2 = kdr; }
                        else
                        { kbdn = drow[i + 3] / 255.0; kbb2 = drow[i]; kbg2 = drow[i + 1]; kbr2 = drow[i + 2]; }
                        double koutA = ka + kbdn * (1 - ka);
                        if (koutA <= 0.0) continue;
                        double kinv = kbdn * (1 - ka);
                        drow[i]     = (byte)((kbb * ka + kbb2 * kinv) / koutA + 0.5);
                        drow[i + 1] = (byte)((kbg * ka + kbg2 * kinv) / koutA + 0.5);
                        drow[i + 2] = (byte)((kbr * ka + kbr2 * kinv) / koutA + 0.5);
                        drow[i + 3] = (byte)(koutA * 255 + 0.5);
                        dirty = true;
                        continue;
                    }
                    if (stampMask is not null && stampMask[y * w + x] != 0)
                    {
                        // Replace-semantics for a stamped nested-footprint pixel.
                        double aEff = ga;
                        if (softMask is not null) aEff *= softMask[y * w + x] / 255.0;
                        if (aEff <= 0.0) continue;
                        double al = srow[i + 3] / 255.0;                 // layer's own alpha
                        double dnb = drow[i + 3] / 255.0;                // backdrop alpha
                        int lb = srow[i], lg = srow[i + 1], lr2 = srow[i + 2];
                        double cbr = lr2, cbg = lg, cbb = lb;
                        if (mode != Rasterizer.BlendMode.Normal && dnb > 0.0)
                        {
                            Rasterizer.BlendModes.Blend(mode, drow[i + 2], drow[i + 1], drow[i], lr2, lg, lb, out int xr, out int xg, out int xb);
                            cbr = (1 - dnb) * lr2 + dnb * xr;
                            cbg = (1 - dnb) * lg + dnb * xg;
                            cbb = (1 - dnb) * lb + dnb * xb;
                        }
                        double aNew = (1 - aEff) * dnb + aEff * al;
                        if (aNew <= 0.0)
                        {
                            drow[i] = drow[i + 1] = drow[i + 2] = drow[i + 3] = 0;
                            dirty = true;
                            continue;
                        }
                        drow[i]     = (byte)System.Math.Clamp((drow[i] * dnb * (1 - aEff) + cbb * al * aEff) / aNew + 0.5, 0, 255);
                        drow[i + 1] = (byte)System.Math.Clamp((drow[i + 1] * dnb * (1 - aEff) + cbg * al * aEff) / aNew + 0.5, 0, 255);
                        drow[i + 2] = (byte)System.Math.Clamp((drow[i + 2] * dnb * (1 - aEff) + cbr * al * aEff) / aNew + 0.5, 0, 255);
                        drow[i + 3] = (byte)(aNew * 255 + 0.5);
                        dirty = true;
                        continue;
                    }
                    // Source coverage: the supersampled mask when supplied, else the
                    // layer's own BGRA straight alpha.
                    double sca = covWeight is not null ? covWeight[y * w + x] : srow[i + 3] / 255.0;
                    if (sca <= 0.0) continue;
                    double a = sca * ga; // effective source alpha
                    if (softMask is not null) a *= softMask[y * w + x] / 255.0;
                    if (a <= 0.0) continue;
                    int sb = srow[i], sg = srow[i + 1], sr = srow[i + 2];
                    int db = drow[i], dg = drow[i + 1], dr = drow[i + 2];
                    double dn = drow[i + 3] / 255.0; // backdrop alpha (0 for a transparent group layer, 1 for the page)

                    // PDF 32000 §11.3.6 general "over" with blend and backdrop alpha:
                    //   Cs' = (1-αb)·Cs + αb·B(Cb,Cs)          (blend only acts where a backdrop exists)
                    //   αr  = a + αb·(1-a)
                    //   Cr  = (Cs'·a + Cb·αb·(1-a)) / αr        (straight, un-premultiplied)
                    // With αb=1 (opaque page) this reduces to Cr = B·a + Cb·(1-a); with
                    // αb=0 (transparent nested layer) to Cr = Cs — no darkening toward black.
                    double bbr = sr, bbg = sg, bbb = sb;
                    if (mode != Rasterizer.BlendMode.Normal && dn > 0.0)
                    {
                        Rasterizer.BlendModes.Blend(mode, dr, dg, db, sr, sg, sb, out int ibr, out int ibg, out int ibb);
                        bbr = (1 - dn) * sr + dn * ibr;
                        bbg = (1 - dn) * sg + dn * ibg;
                        bbb = (1 - dn) * sb + dn * ibb;
                    }
                    double outA = a + dn * (1 - a);
                    if (outA <= 0.0) continue;
                    double inv = dn * (1 - a);
                    drow[i]     = (byte)((bbb * a + db * inv) / outA + 0.5);
                    drow[i + 1] = (byte)((bbg * a + dg * inv) / outA + 0.5);
                    drow[i + 2] = (byte)((bbr * a + dr * inv) / outA + 0.5);
                    drow[i + 3] = (byte)(outA * 255 + 0.5);
                    dirty = true;
                }
                if (dirty)
                    System.Runtime.InteropServices.Marshal.Copy(drow, 0, (IntPtr)dRow, segBytes);
            }
        }
        finally
        {
            _bitmap.UnlockBits(dst);
            layer.UnlockBits(src);
            if (ko is not null) koBackdrop!.UnlockBits(ko);
        }
    }

    // Byte-exact copy of a device-pixel rectangle from src into dst (both page-sized ARGB).
    private static void CopyRegion(Bitmap src, Bitmap dst, System.Drawing.Rectangle r)
    {
        r = System.Drawing.Rectangle.Intersect(r, new System.Drawing.Rectangle(0, 0, src.Width, src.Height));
        if (r.Width <= 0 || r.Height <= 0) return;
        var sr = src.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var dr = dst.LockBits(r, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            int rowBytes = r.Width * 4;
            var buf = new byte[rowBytes];
            for (int y = 0; y < r.Height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(sr.Scan0 + y * sr.Stride, buf, 0, rowBytes);
                System.Runtime.InteropServices.Marshal.Copy(buf, 0, dr.Scan0 + y * dr.Stride, rowBytes);
            }
        }
        finally { src.UnlockBits(sr); dst.UnlockBits(dr); }
    }

    private static void MergeInto<T>(Dictionary<string, T>? target, Dictionary<string, T>? source)
    {
        if (target is null || source is null) return;
        foreach (var kv in source) target.TryAdd(kv.Key, kv.Value);
    }

    private static double[]? ExtractFormMatrix(PdfDictionary dict)
    {
        if (dict.Get("Matrix") is not PdfArray arr || arr.Count < 6) return null;
        var m = new double[6];
        for (int i = 0; i < 6; i++) m[i] = NumFrom(arr[i]);
        return m;
    }

    private GraphicsPath? BuildBBoxClip(PdfDictionary dict, double[] ctm)
    {
        if (dict.Get("BBox") is not PdfArray arr || arr.Count < 4) return null;
        double x0 = NumFrom(arr[0]), y0 = NumFrom(arr[1]), x1 = NumFrom(arr[2]), y1 = NumFrom(arr[3]);
        var segs = new[]
        {
            new PathCommand(PathOp.MoveTo, x0, y0),
            new PathCommand(PathOp.LineTo, x1, y0),
            new PathCommand(PathOp.LineTo, x1, y1),
            new PathCommand(PathOp.LineTo, x0, y1),
            new PathCommand(PathOp.Close),
        };
        var path = BuildPath(segs, evenOdd: false);
        using var world = WorldMatrix(ctm);
        path.Transform(world);
        return path;
    }

    internal static double NumFrom(PdfObject? o) => o switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => 0.0,
    };

    // ── Annotations ─────────────────────────────────────────────────

    private void DrawAnnotations(PdfDictionary pageDict)
    {
        if (_reader.Resolve(pageDict.Get("Annots")) is not PdfArray annots) return;
        foreach (var item in annots)
        {
            var annot = _reader.ResolveDict(item);
            if (annot is null) continue;
            var flags = (int)annot.GetInt("F");
            if ((flags & 0x02) != 0) continue; // Hidden
            var subtype = annot.GetName("Subtype");

            if (_reader.ResolveDict(annot.Get("AP")) is not null)
            {
                SafeDraw(() => DrawAppearanceAnnotation(annot));
                continue;
            }
            if (subtype == "Square" || subtype == "Circle")
                SafeDraw(() => DrawSquareCircleDefault(annot, subtype == "Circle"));
            else if (subtype == "Highlight")
                SafeDraw(() => DrawHighlightDefault(annot));
            // A /Text (sticky-note) annotation without /AP renders as its standard
            // note icon (PDF 32000 §12.5.6.4): NoZoom notes stretch the icon into
            // /Rect; otherwise the icon paints at its natural size anchored at the
            // rectangle's top-left corner.
            else if (subtype == "Text")
            {
                var form = Aspose.Pdf.Annotations.TextAnnotationIcons.BuildIconForm(annot, _reader);
                var target = (flags & 0x08) != 0 ? null : TextIconNaturalRect(annot);
                SafeDraw(() => DrawAppearanceForm(annot, form, target));
            }
            else if (subtype == "Popup")
            {
                var form = Aspose.Pdf.Annotations.PopupAppearance.BuildOpenPopupForm(annot, _reader);
                if (form is not null) SafeDraw(() => DrawAppearanceForm(annot, form));
            }
        }
    }

    private void DrawAppearanceAnnotation(PdfDictionary annot)
    {
        var ap = _reader.ResolveDict(annot.Get("AP"));
        if (ap is null) return;
        var nEntry = _reader.Resolve(ap.Get("N"));
        PdfStream? formStream = null;
        if (nEntry is PdfStream direct) formStream = direct;
        else if (nEntry is PdfDictionary stateDict)
        {
            var asName = annot.GetName("AS");
            if (!string.IsNullOrEmpty(asName)) formStream = _reader.ResolveStream(stateDict.Get(asName));
        }
        if (formStream is null) return;
        DrawAppearanceForm(annot, formStream);
    }

    /// <summary>Natural-size target box for a note icon: the icon's own box
    /// anchored at the annotation rectangle's top-left corner.</summary>
    private (double MinX, double MinY, double MaxX, double MaxY)? TextIconNaturalRect(PdfDictionary annot)
    {
        if (annot.Get("Rect") is not PdfArray rect || rect.Count < 4) return null;
        double rx1 = NumFrom(rect[0]), ry1 = NumFrom(rect[1]), rx2 = NumFrom(rect[2]), ry2 = NumFrom(rect[3]);
        double minX = Math.Min(rx1, rx2), maxY = Math.Max(ry1, ry2);
        var s = Aspose.Pdf.Annotations.TextAnnotationIcons.BoxSize;
        return (minX, maxY - s, minX + s, maxY);
    }

    /// <summary>Paint a Form XObject appearance into the annotation's /Rect, mapping the
    /// form's transformed /BBox onto the rectangle (PDF 32000 §12.5.5). Shared by the
    /// /AP path and by synthesised appearances (e.g. open popups). An explicit
    /// <paramref name="targetRect"/> paints the form there instead of at /Rect.</summary>
    private void DrawAppearanceForm(PdfDictionary annot, PdfStream formStream,
        (double MinX, double MinY, double MaxX, double MaxY)? targetRect = null)
    {
        double rMinX, rMinY, rMaxX, rMaxY;
        if (targetRect is { } tr)
        {
            (rMinX, rMinY, rMaxX, rMaxY) = tr;
        }
        else
        {
            if (annot.Get("Rect") is not PdfArray rect || rect.Count < 4) return;
            double rx1 = NumFrom(rect[0]), ry1 = NumFrom(rect[1]), rx2 = NumFrom(rect[2]), ry2 = NumFrom(rect[3]);
            rMinX = Math.Min(rx1, rx2); rMaxX = Math.Max(rx1, rx2);
            rMinY = Math.Min(ry1, ry2); rMaxY = Math.Max(ry1, ry2);
        }
        double rW = rMaxX - rMinX, rH = rMaxY - rMinY;
        if (rW <= 0 || rH <= 0) return;

        if (formStream.Dict.Get("BBox") is not PdfArray bbox || bbox.Count < 4) return;
        double bx1 = NumFrom(bbox[0]), by1 = NumFrom(bbox[1]), bx2 = NumFrom(bbox[2]), by2 = NumFrom(bbox[3]);
        var fm = ExtractFormMatrix(formStream.Dict) ?? new double[] { 1, 0, 0, 1, 0, 0 };
        double tMinX = double.PositiveInfinity, tMinY = double.PositiveInfinity, tMaxX = double.NegativeInfinity, tMaxY = double.NegativeInfinity;
        foreach (var (cx, cy) in new[] { (bx1, by1), (bx2, by1), (bx2, by2), (bx1, by2) })
        {
            var tx = fm[0] * cx + fm[2] * cy + fm[4];
            var ty = fm[1] * cx + fm[3] * cy + fm[5];
            if (tx < tMinX) tMinX = tx; if (tx > tMaxX) tMaxX = tx;
            if (ty < tMinY) tMinY = ty; if (ty > tMaxY) tMaxY = ty;
        }
        double tW = tMaxX - tMinX, tH = tMaxY - tMinY;
        if (tW <= 0 || tH <= 0) return;
        double sx = rW / tW, sy = rH / tH;
        var outerCtm = new double[] { sx, 0, 0, sy, rMinX - tMinX * sx, rMinY - tMinY * sy };

        // Annotation constant alpha (/CA, PDF 32000 §12.5.2): the whole appearance is
        // composited at this opacity, as if it were an isolated transparency group.
        var ca = annot.Get("CA") is { } caObj ? NumFrom(caObj) : 1.0;
        if (ca <= 0) return; // fully transparent: nothing to paint
        var state = new GraphicsState { Ctm = outerCtm };
        if (ca < 1) { state.FillAlpha = ca; state.StrokeAlpha = ca; }

        DrawFormXObject(formStream, state, forceComposite: ca < 0.999);
    }

    private void DrawSquareCircleDefault(PdfDictionary annot, bool isCircle)
    {
        if (annot.Get("Rect") is not PdfArray rect || rect.Count < 4) return;
        double rx1 = NumFrom(rect[0]), ry1 = NumFrom(rect[1]), rx2 = NumFrom(rect[2]), ry2 = NumFrom(rect[3]);
        float x = (float)Math.Min(rx1, rx2), y = (float)Math.Min(ry1, ry2);
        float w = (float)Math.Abs(rx2 - rx1), h = (float)Math.Abs(ry2 - ry1);
        if (w <= 0 || h <= 0) return;

        var saved = _g.Transform;
        using var world = WorldMatrix(new double[] { 1, 0, 0, 1, 0, 0 });
        _g.Transform = world;
        try
        {
            var ic = ParseAnnotColor(annot.Get("IC"));
            var bc = ParseAnnotColor(annot.Get("C"));
            if (ic is { } fillCol)
            {
                using var b = new SolidBrush(fillCol);
                if (isCircle) _g.FillEllipse(b, x, y, w, h); else _g.FillRectangle(b, x, y, w, h);
            }
            float bw = 1f;
            if (_reader.ResolveDict(annot.Get("BS")) is { } bs) bw = (float)NumFrom(bs.Get("W"));
            if (bw > 0 && bc is { } borderCol)
            {
                using var p = new Pen(borderCol, bw);
                if (isCircle) _g.DrawEllipse(p, x, y, w, h); else _g.DrawRectangle(p, x, y, w, h);
            }
        }
        finally { _g.Transform = saved; }
    }

    /// <summary>Default appearance for a text-markup Highlight annotation that
    /// carries no /AP stream: paint each QuadPoints quadrilateral (or the /Rect when
    /// QuadPoints is absent) in the annotation colour using the Multiply blend mode
    /// (PDF 32000 §12.5.6.10), so underlying text shows through.</summary>
    private void DrawHighlightDefault(PdfDictionary annot)
    {
        var col = ParseAnnotColor(annot.Get("C"));
        if (col is not { } c) return;

        using var path = new GraphicsPath { FillMode = FillMode.Winding };
        if (_reader.Resolve(annot.Get("QuadPoints")) is PdfArray qp && qp.Count >= 8 && qp.Count % 8 == 0)
        {
            for (int i = 0; i + 7 < qp.Count; i += 8)
            {
                // Each quad: (x1,y1)(x2,y2)(x3,y3)(x4,y4). Use the quad's bounding box.
                double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
                double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
                for (int j = 0; j < 8; j += 2)
                {
                    double px = NumFrom(qp[i + j]), py = NumFrom(qp[i + j + 1]);
                    if (px < minX) minX = px; if (px > maxX) maxX = px;
                    if (py < minY) minY = py; if (py > maxY) maxY = py;
                }
                float w = (float)(maxX - minX), h = (float)(maxY - minY);
                if (w > 0 && h > 0) path.AddRectangle(new RectangleF((float)minX, (float)minY, w, h));
            }
        }
        else if (annot.Get("Rect") is PdfArray rect && rect.Count >= 4)
        {
            double rx1 = NumFrom(rect[0]), ry1 = NumFrom(rect[1]), rx2 = NumFrom(rect[2]), ry2 = NumFrom(rect[3]);
            float x = (float)Math.Min(rx1, rx2), y = (float)Math.Min(ry1, ry2);
            float w = (float)Math.Abs(rx2 - rx1), h = (float)Math.Abs(ry2 - ry1);
            if (w <= 0 || h <= 0) return;
            path.AddRectangle(new RectangleF(x, y, w, h));
        }
        if (path.PointCount == 0) return;

        using var world = WorldMatrix(new double[] { 1, 0, 0, 1, 0, 0 });
        FillPathBlended(path, world, Rasterizer.BlendMode.Multiply,
            new GraphicsState { FillR = c.R / 255.0, FillG = c.G / 255.0, FillB = c.B / 255.0, FillAlpha = 1.0 });
    }

    private GdiColor? ParseAnnotColor(PdfObject? o)
    {
        if (_reader.Resolve(o) is not PdfArray arr) return null;
        switch (arr.Count)
        {
            case 1: { int v = Clamp255(NumFrom(arr[0])); return GdiColor.FromArgb(v, v, v); }
            case 3: return GdiColor.FromArgb(Clamp255(NumFrom(arr[0])), Clamp255(NumFrom(arr[1])), Clamp255(NumFrom(arr[2])));
            case 4:
                double c = NumFrom(arr[0]), m = NumFrom(arr[1]), yv = NumFrom(arr[2]), k = NumFrom(arr[3]);
                return GdiColor.FromArgb(Clamp255((1 - c) * (1 - k)), Clamp255((1 - m) * (1 - k)), Clamp255((1 - yv) * (1 - k)));
            default: return null; // empty array = transparent / no colour
        }
    }

    // ── Output ──────────────────────────────────────────────────────

    /// <summary>Convert a 32bpp ARGB GDI+ bitmap (BGRA byte order) to an RGBA buffer.</summary>
    private static RgbaBuffer ToRgbaBuffer(Bitmap bmp, int w, int h)
    {
        var data = new byte[w * h * 4];
        var rect = new System.Drawing.Rectangle(0, 0, w, h);
        var bits = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var stride = bits.Stride;
            var scan = bits.Scan0;
            var row = new byte[stride];
            for (int y = 0; y < h; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(scan + y * stride, row, 0, stride);
                int di = y * w * 4;
                for (int x = 0; x < w; x++)
                {
                    int si = x * 4;
                    // GDI+ Format32bppArgb is little-endian BGRA in memory.
                    data[di + 0] = row[si + 2]; // R
                    data[di + 1] = row[si + 1]; // G
                    data[di + 2] = row[si + 0]; // B
                    data[di + 3] = row[si + 3]; // A
                    di += 4;
                }
            }
        }
        finally { bmp.UnlockBits(bits); }
        return new RgbaBuffer(data, w, h);
    }
}
