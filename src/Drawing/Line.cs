using Aspose.Pdf.Content;

namespace Aspose.Pdf.Drawing;

/// <summary>
/// A line shape.
/// </summary>
public sealed class Line : Shape
{
    internal override bool TryAppendGeometry(ContentStreamBuilder builder)
    {
        AppendOutline(builder);
        return true;
    }

    private void AppendOutline(ContentStreamBuilder builder)
    {
        builder.MoveTo(_coords[0], _coords[1]);
        for (var i = 2; i + 1 < _coords.Length; i += 2)
            builder.LineTo(_coords[i], _coords[i + 1]);
    }

    // Full vertex list as a flat [x0,y0, x1,y1, ...] array. A simple line has
    // two vertices; a longer position array produces a connected polyline.
    private double[] _coords;

    public double X1 { get => _coords[0]; set => _coords[0] = value; }
    public double Y1 { get => _coords[1]; set => _coords[1] = value; }
    public double X2 { get => _coords[2]; set => _coords[2] = value; }
    public double Y2 { get => _coords[3]; set => _coords[3] = value; }

    public Line(double x1, double y1, double x2, double y2)
    {
        _coords = new[] { x1, y1, x2, y2 };
    }

    /// <summary>
    /// Create a line (or polyline) from a flat coordinate array [x0, y0, x1, y1, ...].
    /// Two points produce a straight segment; more produce a connected polyline that,
    /// when a FillColor is set, is closed and filled as a polygon.
    /// </summary>
    public Line(float[] positionArray)
    {
        if (positionArray is null || positionArray.Length < 4)
            throw new ArgumentException("positionArray must have at least 4 elements", nameof(positionArray));
        _coords = Array.ConvertAll(positionArray, f => (double)f);
    }

    /// <summary>Polyline vertices as a flat [x0, y0, x1, y1, ...] array.</summary>
    public float[] PositionArray
    {
        get => Array.ConvertAll(_coords, d => (float)d);
        set
        {
            if (value is null || value.Length < 4) return;
            _coords = Array.ConvertAll(value, f => (double)f);
        }
    }

    /// <summary>Whether every vertex lies within the container's AABB anchored at the origin.</summary>
    public override bool CheckBounds(double containerWidth, double containerHeight)
    {
        for (var i = 0; i + 1 < _coords.Length; i += 2)
            if (_coords[i] < 0 || _coords[i + 1] < 0
                || _coords[i] > containerWidth || _coords[i + 1] > containerHeight)
                return false;
        return true;
    }

    /// <summary>
    /// Create a line (or polyline) from a flat coordinate array [x0, y0, x1, y1, ...].
    /// </summary>
    public Line(double[] coordinates)
    {
        if (coordinates is null || coordinates.Length < 4)
            throw new ArgumentException("coordinates must have at least 4 elements", nameof(coordinates));
        _coords = (double[])coordinates.Clone();
    }

    internal override void Render(ContentStreamBuilder builder, Page? page = null)
    {
        ApplyOpacity(builder, page);
        builder.SetLineWidth(GraphInfo.LineWidth);
        if (GraphInfo.StrokeColor is { } sc)
            builder.SetStrokeColor(sc.R, sc.G, sc.B);
        if (GraphInfo.FillColorInternal is { } fc)
            builder.SetFillColor(fc.R, fc.G, fc.B);

        AppendOutline(builder);

        // A line is always stroked (default black when no Color is set). When a
        // FillColor is supplied the polyline is closed and the enclosed region
        // is filled as well.
        if (GraphInfo.FillColor is not null)
        {
            builder.ClosePath();
            builder.FillAndStroke();
        }
        else
        {
            builder.Stroke();
        }
    }
}
