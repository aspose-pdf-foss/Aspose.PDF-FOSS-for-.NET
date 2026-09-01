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

    // Page-sized coverage scratch pools. A group-heavy page runs dozens of
    // composites, each needing full-page float/byte coverage buffers; renting
    // them (zeroed) instead of allocating keeps the render's heap spike at a
    // couple of buffers rather than one set per group. Stack discipline makes
    // nested group recursion safe — inner rents while outer's are checked out.
    private readonly Stack<float[]> _covFloatPool = new();

    private readonly Stack<byte[]> _covBytePool = new();

    private static void MergeInto<T>(Dictionary<string, T>? target, Dictionary<string, T>? source)
    {
        if (target is null || source is null) return;
        foreach (var kv in source) target.TryAdd(kv.Key, kv.Value);
    }

    // ── Annotations ─────────────────────────────────────────────────

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

    // ── Output ──────────────────────────────────────────────────────

}
