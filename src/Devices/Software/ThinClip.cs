using Aspose.Pdf.Content;
using Aspose.Pdf.Devices.Rasterizer;

namespace Aspose.Pdf.Devices;

public sealed partial class SoftwarePageRenderer
{
    /// <summary>
    /// A figure of a clip path thinner than a pixel keeps the pixels it runs along. The clip mask
    /// takes a pixel by its centre, so a band half a pixel wide - a drawing's hairlines set as
    /// shadings painted through the outline of each line - selected a pixel here and there or
    /// none, and the lines came out broken or not at all. Such a figure is thickened about its
    /// outline to one pixel across, which a mask of centres always holds; a figure that encloses
    /// nothing still selects nothing.
    /// </summary>
    private static void WidenThinClip(RenderContext ctx, IReadOnlyList<PathCommand> segments, double[] ctm, byte[] mask)
    {
        EdgeTable? band = null;
        var figure = new List<(double X, double Y)>();
        void EndFigure()
        {
            if (figure.Count > 1)
            {
                var measure = new OutlineMeasure();
                measure.MoveTo(figure[0].X, figure[0].Y);
                for (var i = 1; i < figure.Count; i++) measure.LineTo(figure[i].X, figure[i].Y);
                var width = measure.CloseFigure();
                if (OutlineMeasure.IsThin(width))
                    AddOutlineBand(band ??= new EdgeTable(), figure, (OutlineMeasure.ThinWidth - width) / 2);
            }
            figure.Clear();
        }
        void Point(double x, double y) => figure.Add(DevicePoint(ctx, ctm, x, y));

        foreach (var seg in segments)
        {
            switch (seg.Op)
            {
                case PathOp.MoveTo: EndFigure(); Point(seg.X1, seg.Y1); break;
                case PathOp.LineTo: Point(seg.X1, seg.Y1); break;
                // A curve is walked along its control points.
                case PathOp.CurveTo: Point(seg.X1, seg.Y1); Point(seg.X2, seg.Y2); Point(seg.X3, seg.Y3); break;
                case PathOp.CurveToV:
                case PathOp.CurveToY: Point(seg.X1, seg.Y1); Point(seg.X2, seg.Y2); break;
                case PathOp.Rect:
                    EndFigure();
                    Point(seg.X1, seg.Y1);
                    Point(seg.X1 + seg.X2, seg.Y1);
                    Point(seg.X1 + seg.X2, seg.Y1 + seg.Y2);
                    Point(seg.X1, seg.Y1 + seg.Y2);
                    EndFigure();
                    break;
                case PathOp.Close: EndFigure(); break;
            }
        }
        EndFigure();
        if (band is not null) ScanlineFiller.BuildMask(band, mask, ctx.PixelW, ctx.PixelH, evenOdd: false);
    }

    /// <summary>Adds a band of the given half width about every edge of the closed figure.</summary>
    private static void AddOutlineBand(EdgeTable band, List<(double X, double Y)> figure, double half)
    {
        for (var i = 0; i < figure.Count; i++)
        {
            var (x0, y0) = figure[i];
            var (x1, y1) = figure[(i + 1) % figure.Count];
            var length = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
            if (length <= 0) continue;
            // The edge's normal, turned the same way for every edge so the quads wind alike.
            var nx = -(y1 - y0) / length * half;
            var ny = (x1 - x0) / length * half;
            band.AddLine(x0 + nx, y0 + ny, x1 + nx, y1 + ny);
            band.AddLine(x1 + nx, y1 + ny, x1 - nx, y1 - ny);
            band.AddLine(x1 - nx, y1 - ny, x0 - nx, y0 - ny);
            band.AddLine(x0 - nx, y0 - ny, x0 + nx, y0 + ny);
        }
    }

    private static (double x, double y) DevicePoint(RenderContext ctx, double[] ctm, double x, double y)
    {
        var tx = ctm[0] * x + ctm[2] * y + ctm[4];
        var ty = ctm[1] * x + ctm[3] * y + ctm[5];
        return ((tx - ctx.MediaBox.LLX) * ctx.Scale, ctx.PixelH - (ty - ctx.MediaBox.LLY) * ctx.Scale);
    }
}
