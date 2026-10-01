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
            if (a.Length == n) { Array.Clear(a, 0, a.Length); return a; }
        }
        return new float[n];
    }

    private byte[] RentCovByte(int n)
    {
        while (_covBytePool.Count > 0)
        {
            var a = _covBytePool.Pop();
            if (a.Length == n) { Array.Clear(a, 0, a.Length); return a; }
        }
        return new byte[n];
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
        UpsampleBackdropCopy(savedBmp, ssBmp, rect, ssRect, k);

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
        DownsampleSupersampledLayer(ssBmp, layer, rect, ssRect, k);
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
        var cg = new GroupLayerCompositeState();
        cg.layer = layer;
        cg.state = state;
        cg.blendMode = blendMode;
        cg.bounds = bounds;
        cg.covWeight = covWeight;
        cg.stampMask = stampMask;
        cg.koBackdrop = koBackdrop;
        cg.koReplace = koReplace;
        cg.w = _bitmap.Width;
        cg.h = _bitmap.Height;
        cg.ga = Compat.Clamp(cg.state.FillAlpha, 0.0, 1.0);
        if (cg.ga <= 0.0) return;
        // Clamp the work region to the page; only this rectangle (the group's BBox)
        // can contain non-transparent layer pixels.
        cg.bounds = System.Drawing.Rectangle.Intersect(cg.bounds, new System.Drawing.Rectangle(0, 0, cg.w, cg.h));
        if (cg.bounds.Width <= 0 || cg.bounds.Height <= 0) return;
        cg.mode = Rasterizer.BlendModes.Parse(cg.blendMode);
        cg.softMask = cg.state.SoftMask is { } sm ? GetSoftMaskAlpha(sm) : null;

        _g.Flush();
        cg.rect = new System.Drawing.Rectangle(0, 0, cg.w, cg.h);
        cg.dst = _bitmap.LockBits(cg.rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        cg.src = cg.layer.LockBits(cg.rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        cg.ko = cg.koBackdrop?.LockBits(cg.rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            cg.x0 = cg.bounds.Left;
            cg.x1 = cg.bounds.Right;
            cg.segBytes = (cg.x1 - cg.x0) * 4;
            cg.drow = new byte[cg.segBytes];
            cg.srow = new byte[cg.segBytes];
            cg.krow = cg.ko is not null ? new byte[cg.segBytes] : null;
            for (int y = cg.bounds.Top; y < cg.bounds.Bottom; y++)
            {
                if (!CompositeGroupRow(cg, y)) break;
            }
        }
        finally
        {
            _bitmap.UnlockBits(cg.dst);
            cg.layer.UnlockBits(cg.src);
            if (cg.ko is not null) cg.koBackdrop!.UnlockBits(cg.ko);
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
