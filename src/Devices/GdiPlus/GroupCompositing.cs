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
}
