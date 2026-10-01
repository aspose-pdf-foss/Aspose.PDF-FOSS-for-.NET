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
    /// <summary>The stages of the tiling-pattern fill: the tile extent and the composite paint.</summary>
    private void PaintTilingPatternComposite(TilingPatternFillState tl, Bitmap savedLBmp, Bitmap? savedLScratch)
    {
        try
        {
            // Isolated source: start from a transparent backdrop under the fill rect.
            tl.lg.CompositingMode = CompositingMode.SourceCopy;
            using (var clear = new SolidBrush(GdiColor.Transparent))
                tl.lg.FillRectangle(clear, tl.compRect);
            tl.lg.CompositingMode = CompositingMode.SourceOver;
            tl.lg.SmoothingMode = tl.savedLG.SmoothingMode;
            tl.lg.PixelOffsetMode = tl.savedLG.PixelOffsetMode;
            tl.lg.InterpolationMode = tl.savedLG.InterpolationMode;
            tl.lg.TextRenderingHint = tl.savedLG.TextRenderingHint;
            tl.lg.CompositingQuality = tl.savedLG.CompositingQuality;
            if (tl.deviceClip is not null) tl.lg.Clip = tl.deviceClip;
            // RenderTilingCells reads `path` in user space then drops to identity, so the
            // layer transform must be the fill CTM (= world) when it sets the clip.
            using (var wclone = tl.world.Clone()) tl.lg.Transform = wclone;

            _g = tl.lg; _bitmap = tl.layer; _blendScratch = null;
            RenderTilingCells(tl.path, tl.pd, tl.content, tl.patMatrix, tl.xstep, tl.ystep, tl.iMin, tl.iMax, tl.jMin, tl.jMax);
            _g.Flush();
        }
        finally
        {
            _g = tl.savedLG; _bitmap = savedLBmp;
            _blendScratch?.Dispose(); _blendScratch = savedLScratch;
            tl.lg.Dispose(); tl.deviceClip?.Dispose();
        }

        CompositeGroupLayer(tl.layer, tl.state, tl.state.BlendMode, tl.compRect);
    }

    /// <summary></summary>
    private bool ComputeTilingExtent(TilingPatternFillState tl)
    {
        if (tl.patObj is not PdfStream patStream) return false;
        tl.pd = patStream.Dict;
        if ((int)tl.pd.GetInt("PatternType") != 1) return false;
        if (tl.pd.Get("BBox") is not PdfArray bbox || bbox.Count < 4) return false;
        tl.bx0 = NumFrom(bbox[0]);
        tl.by0 = NumFrom(bbox[1]);
        tl.bx1 = NumFrom(bbox[2]);
        tl.by1 = NumFrom(bbox[3]);
        tl.xstep = NumFrom(tl.pd.Get("XStep"));
        tl.ystep = NumFrom(tl.pd.Get("YStep"));
        if (Math.Abs(tl.xstep) < 1e-6) tl.xstep = tl.bx1 - tl.bx0;
        if (Math.Abs(tl.ystep) < 1e-6) tl.ystep = tl.by1 - tl.by0;
        if (Math.Abs(tl.xstep) < 1e-6 || Math.Abs(tl.ystep) < 1e-6) return false;
        tl.patMatrix = PatternSpaceMatrix(tl.pd);

        try { tl.content = _reader.DecodeStream(patStream); } catch { return false; }
        if (tl.content.Length == 0) return false;

        using var patWorld = WorldMatrix(tl.patMatrix);
        using var inv = patWorld.Clone();
        if (!inv.IsInvertible) return false;
        inv.Invert();

        tl.db = tl.path.GetBounds(tl.world);
        tl.corners = new[]
        {
            new PointF(tl.db.Left, tl.db.Top), new PointF(tl.db.Right, tl.db.Top),
            new PointF(tl.db.Left, tl.db.Bottom), new PointF(tl.db.Right, tl.db.Bottom),
        };
        inv.TransformPoints(tl.corners);
        float pMinX = tl.corners[0].X;
        float pMaxX = tl.corners[0].X;
        float pMinY = tl.corners[0].Y;
        float pMaxY = tl.corners[0].Y;
        foreach (var c in tl.corners)
        {
            pMinX = Math.Min(pMinX, c.X); pMaxX = Math.Max(pMaxX, c.X);
            pMinY = Math.Min(pMinY, c.Y); pMaxY = Math.Max(pMaxY, c.Y);
        }
        tl.iMin = (int)Math.Floor((pMinX - tl.bx1) / tl.xstep);
        tl.iMax = (int)Math.Ceiling((pMaxX - tl.bx0) / tl.xstep);
        tl.jMin = (int)Math.Floor((pMinY - tl.by1) / tl.ystep);
        tl.jMax = (int)Math.Ceiling((pMaxY - tl.by0) / tl.ystep);
        if (tl.iMax < tl.iMin || tl.jMax < tl.jMin) return false;
        if ((long)(tl.iMax - tl.iMin + 1) * (tl.jMax - tl.jMin + 1) > 8000)
        {
            // Too many tiles to execute the cell per-tile (a fine screen/dither over a
            // large area). Rasterise one cell to a device-sized tile and let GDI+ repeat
            // it with a TextureBrush instead of bailing (which would leave the region
            // blank). Scoped to the over-guard case, so the exact per-tile path for
            // normal-sized fills is unchanged.
            FillWithTiledBrush(tl.path, tl.pd, tl.content, tl.bx0, tl.by0, tl.bx1, tl.by1, tl.xstep, tl.ystep, tl.patMatrix, tl.world);
            return false;
        }
        return true;
    }
}
