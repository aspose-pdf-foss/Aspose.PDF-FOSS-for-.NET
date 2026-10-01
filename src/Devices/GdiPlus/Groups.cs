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
        int sw = src.Width;
        int sh = src.Height;
        if (dw <= 0 || dh <= 0 || dw >= sw || dh >= sh) return null;
        var fmt = src.PixelFormat;
        if (fmt is not (PixelFormat.Format1bppIndexed or PixelFormat.Format8bppIndexed
            or PixelFormat.Format24bppRgb or PixelFormat.Format32bppArgb or PixelFormat.Format32bppRgb))
            return null;

        // Per-destination-pixel channel sums; accumulate row by row so the source is
        // touched once, sequentially (the sources this path exists for are huge).
        var sums = new BoxSums(dw * dh);

        // Palette lookups for indexed formats.
        GdiColor[]? pal = fmt is PixelFormat.Format1bppIndexed or PixelFormat.Format8bppIndexed
            ? src.Palette.Entries : null;

        var data = src.LockBits(new System.Drawing.Rectangle(0, 0, sw, sh), ImageLockMode.ReadOnly, fmt);
        try
        {
            ReadSourceBoxes(sw, sh, dw, dh, fmt, sums, pal, data);
        }
        finally { src.UnlockBits(data); }

        var dst = new Bitmap(dw, dh, PixelFormat.Format32bppArgb);
        var ddata = dst.LockBits(new System.Drawing.Rectangle(0, 0, dw, dh), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            WriteDownsampledRows(dw, dh, sums, ddata);
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
            if (PrintedPageImage)
            {
                sgfx.CompositingQuality = CompositingQuality.AssumeLinear;
                DrawPrintedImage(sgfx, bmp, world, dest, null);
            }
            else
            {
                sgfx.DrawImage(bmp, dest);
            }
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
            if (PrintedPageImage)
            {
                sgfx.CompositingQuality = CompositingQuality.AssumeLinear;
                DrawPrintedImage(sgfx, bmp, world, dest, null);
            }
            else
            {
                sgfx.DrawImage(bmp, dest);
            }
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

    private void DrawFormXObject(PdfStream formStream, GraphicsState state, bool forceComposite = false)
    {
        var gf = new GdiFormXObjectDrawState();
        gf.formStream = formStream;
        gf.state = state;
        gf.forceComposite = forceComposite;
        // A form hidden by the default optional-gf.content configuration renders as
        // if absent (e.g. a print-only /Background layer wrapping the page scan).
        if (SoftwarePageRenderer.IsOcHidden(gf.formStream.Dict.Get("OC"), _reader, _ocgHidden)) return;
        if (_formDepth > 64) return;
        _formDepth++;
        gf.savedScope = _scope;
        gf.savedGdi = _g.Save();
        try
        {
            try { gf.content = _reader.DecodeStream(gf.formStream); }
            catch { return; }

            var formResources = _reader.ResolveDict(gf.formStream.Dict.Get("Resources"));
            gf.formScope = BuildScope(formResources);
            // Does this group's OWN gf.content potentially blend against its backdrop, i.e. does
            // it define a non-Normal blend ExtGState it can apply to an interior fill? If so it
            // must render onto a copy of the real backdrop so that interior blend sees it;
            // otherwise it can render in isolation (transparent layer) and composite at its
            // Do-time alpha/blend — which composes correctly against the coverage-alpha page.
            // Checked before the parent merge so only the group's own gstates count.
            bool hasInternalBlend = false;
            if (gf.formScope.ExtGStates is not null)
                foreach (var eg in gf.formScope.ExtGStates.Values)
                    if (eg.GetName("BM") is { } bm && bm != "Normal") { hasInternalBlend = true; break; }
            // Merge parent resources for fallback lookups (PDF 32000 §8.10 forms may
            // reference names defined only in the enclosing scope).
            MergeInto(gf.formScope.XObjects, gf.savedScope.XObjects);
            MergeInto(gf.formScope.Fonts, gf.savedScope.Fonts);
            MergeInto(gf.formScope.ExtGStates, gf.savedScope.ExtGStates);
            gf.formScope.Patterns ??= gf.savedScope.Patterns;
            gf.formScope.Shadings ??= gf.savedScope.Shadings;
            gf.formScope.ColorSpaces ??= gf.savedScope.ColorSpaces;
            gf.formScope.Properties ??= gf.savedScope.Properties;
            _scope = gf.formScope;

            gf.formMatrix = ExtractFormMatrix(gf.formStream.Dict);
            gf.effectiveCtm = gf.formMatrix is not null
                ? GraphicsState.MultiplyMatrices(gf.formMatrix, gf.state.Ctm)
                : (double[])gf.state.Ctm.Clone();

            gf.bboxClip = BuildBBoxClip(gf.formStream.Dict, gf.effectiveCtm);

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
            ClassifyFormTransparency(gf);

            // Inside a coverage pre-pass, a transparency-group Do contributes a BINARY
            // footprint to the enclosing group's outer-blend mask: any pixel its gf.content
            // touches counts as fully covered (the outer blend applies at
            // full strength across a nested layer's whole footprint, keeping fractional
            // weights only for direct gf.content). Q_OBM=bin experiment.
            if (_inCoveragePass && gf.isTransparencyGroup && ObMode is "bin" or "nal" or "bin2")
            {
                if (ObMode == "bin2") StampCenterCellGroupCoverage(gf.content, gf.effectiveCtm, gf.bboxClip);
                else StampBinarizedGroupCoverage(gf.content, gf.effectiveCtm, gf.bboxClip);
                gf.bboxClip?.Dispose();
                return;
            }

            if (gf.needsComposite)
            {
                CompositeFormLayer(gf, hasInternalBlend);
            }
            else
                RenderContentStream(gf.content, gf.effectiveCtm, gf.bboxClip, gf.state);
            gf.bboxClip?.Dispose();
        }
        finally
        {
            _scope = gf.savedScope;
            _g.Restore(gf.savedGdi);
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
