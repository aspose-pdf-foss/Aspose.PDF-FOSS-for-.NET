using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.Versioning;
using Aspose.Pdf.Devices.Rasterizer;
using GdiColor = System.Drawing.Color;
using GdiMatrix = System.Drawing.Drawing2D.Matrix;

namespace Aspose.Pdf.Devices;

public sealed partial class GdiPlusPageRenderer
{
    /// <summary>
    /// Intersects the clip with a path that has figures thinner than a pixel, those thickened
    /// about their outline to one pixel across; false when the path has no such figure. A clip
    /// region takes a pixel by its centre, so a band half a pixel wide - a drawing's hairlines set
    /// as shadings painted through the outline of each line - selected a pixel here and there or
    /// none, and the lines came out broken or not at all. A figure that encloses nothing still
    /// selects nothing.
    /// </summary>
    private bool TryIntersectThinClip(GraphicsPath path, GdiMatrix? world)
    {
        // A path of no points or of a single line encloses nothing (and has no point array to read).
        if (path.PointCount < MinFigurePoints) return false;
        var points = path.PathPoints;
        var types = path.PathTypes;
        world?.TransformPoints(points);
        using var thin = ThinFigures(points, types, out var width);
        if (thin is null) return false;
        try
        {
            using var pen = new Pen(GdiColor.Black, (float)(OutlineMeasure.ThinWidth - width));
            thin.Widen(pen);
            using var device = new GraphicsPath(points, types, path.FillMode);
            using var region = new Region(device);
            region.Union(thin);
            _g.ResetTransform();
            _g.SetClip(region, CombineMode.Intersect);
            if (world is not null) _g.Transform = world;
            return true;
        }
        catch (Exception ex) when (ex is OutOfMemoryException or ArgumentException)
        {
            // The surface could not widen the outline: the path clips as it stands.
            if (world is not null) _g.Transform = world;
            return false;
        }
    }

    /// <summary>The fewest points a figure that encloses something has.</summary>
    private const int MinFigurePoints = 3;

    /// <summary>A device rectangle thinner than a pixel, grown about its middle to one pixel
    /// across; any other rectangle as it stands.</summary>
    private static RectangleF WidenThinClipRectangle(RectangleF rect)
    {
        if (OutlineMeasure.IsThin(rect.Width) && rect.Height > OutlineMeasure.DegenerateWidth)
            rect = RectangleF.FromLTRB(rect.Left - (float)(OutlineMeasure.ThinWidth - rect.Width) / 2, rect.Top,
                rect.Right + (float)(OutlineMeasure.ThinWidth - rect.Width) / 2, rect.Bottom);
        if (OutlineMeasure.IsThin(rect.Height) && rect.Width > OutlineMeasure.DegenerateWidth)
            rect = RectangleF.FromLTRB(rect.Left, rect.Top - (float)(OutlineMeasure.ThinWidth - rect.Height) / 2,
                rect.Right, rect.Bottom + (float)(OutlineMeasure.ThinWidth - rect.Height) / 2);
        return rect;
    }

    /// <summary>The figures of a path that are thinner than a pixel, each as the polygon of its
    /// points (a curve along its control points), with the width of the thinnest; null when
    /// there is none.</summary>
    private static GraphicsPath? ThinFigures(PointF[] points, byte[] types, out double thinnest)
    {
        const byte PointKindMask = 0x07;
        const byte FigureStart = 0x00;
        GraphicsPath? thin = null;
        thinnest = OutlineMeasure.ThinWidth;
        var measure = new OutlineMeasure();
        var first = 0;
        for (var i = 1; i <= points.Length; i++)
        {
            if (i < points.Length && (types[i] & PointKindMask) != FigureStart) continue;
            if (i - first > 1)
            {
                measure.MoveTo(points[first].X, points[first].Y);
                for (var j = first + 1; j < i; j++) measure.LineTo(points[j].X, points[j].Y);
                var width = measure.CloseFigure();
                if (OutlineMeasure.IsThin(width))
                {
                    thin ??= new GraphicsPath(FillMode.Winding);
                    var polygon = new PointF[i - first];
                    Array.Copy(points, first, polygon, 0, polygon.Length);
                    thin.AddPolygon(polygon);
                    thinnest = Math.Min(thinnest, width);
                }
            }
            first = i;
        }
        return thin;
    }
}
