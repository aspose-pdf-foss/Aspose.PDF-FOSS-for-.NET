using Aspose.Pdf.Content;

namespace Aspose.Pdf.Drawing;

/// <summary>
/// A path shape composed of move, line, and curve segments.
/// </summary>
public sealed class DrawingPath : Shape
{
    private readonly List<PathSegment> _segments = [];

    public DrawingPath MoveTo(double x, double y)
    {
        _segments.Add(new PathSegment(PathOp.Move, x, y));
        return this;
    }

    public DrawingPath LineTo(double x, double y)
    {
        _segments.Add(new PathSegment(PathOp.Line, x, y));
        return this;
    }

    public DrawingPath CurveTo(double cx1, double cy1, double cx2, double cy2, double x, double y)
    {
        _segments.Add(new PathSegment(PathOp.Curve, x, y, cx1, cy1, cx2, cy2));
        return this;
    }

    public DrawingPath Close()
    {
        _segments.Add(new PathSegment(PathOp.Close, 0, 0));
        return this;
    }

    internal override void Render(ContentStreamBuilder builder, Page? page = null)
    {
        if (_segments.Count == 0) return;
        ApplyStyle(builder, page);

        foreach (var seg in _segments)
        {
            switch (seg.Op)
            {
                case PathOp.Move:
                    builder.MoveTo(seg.X, seg.Y);
                    break;
                case PathOp.Line:
                    builder.LineTo(seg.X, seg.Y);
                    break;
                case PathOp.Curve:
                    builder.CurveTo(seg.Cx1, seg.Cy1, seg.Cx2, seg.Cy2, seg.X, seg.Y);
                    break;
                case PathOp.Close:
                    builder.ClosePath();
                    break;
            }
        }

        Paint(builder);
    }

    private enum PathOp { Move, Line, Curve, Close }

    private readonly struct PathSegment(PathOp op, double x, double y,
        double cx1 = 0, double cy1 = 0, double cx2 = 0, double cy2 = 0)
    {
        public PathOp Op { get; } = op;
        public double X { get; } = x;
        public double Y { get; } = y;
        public double Cx1 { get; } = cx1;
        public double Cy1 { get; } = cy1;
        public double Cx2 { get; } = cx2;
        public double Cy2 { get; } = cy2;
    }
}
