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
}
