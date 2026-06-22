using Aspose.Pdf.Content;

namespace Aspose.Pdf.Drawing;

/// <summary>
/// Color for drawing shapes.
/// </summary>
public sealed class Color
{
    public double R { get; }
    public double G { get; }
    public double B { get; }

    /// <summary>
    /// Optional gradient/pattern color space. When set, the fill uses this
    /// gradient instead of the solid R/G/B color.
    /// </summary>
    public GradientAxialShading? PatternColorSpace { get; set; }

    public Color() : this(0, 0, 0) { }

    public Color(double r, double g, double b)
    {
        R = r; G = g; B = b;
    }

    public static Color Black => new(0, 0, 0);
    public static Color White => new(1, 1, 1);
    public static Color Red => new(1, 0, 0);
    public static Color Green => new(0, 1, 0);
    public static Color Blue => new(0, 0, 1);
    public static Color Purple => new(0.5, 0, 0.5);
    public static Color Gray => new(0.5, 0.5, 0.5);
    public static Color LightGray => new(0.83, 0.83, 0.83);
    public static Color Tomato => new(1.0, 0.388, 0.278);
    public static Color Yellow => new(1, 1, 0);
    public static Color Aqua => new(0, 1, 1);

    public static Color FromRgb(int r, int g, int b) =>
        new(r / 255.0, g / 255.0, b / 255.0);

    /// <summary>Implicit conversion from Aspose.Pdf.Color to Drawing.Color.</summary>
    public static implicit operator Color(Aspose.Pdf.Color c) =>
        new(c.R / 255.0, c.G / 255.0, c.B / 255.0);

    /// <summary>
    /// Parse a hex color string like "#RRGGBB" or "#RGB".
    /// </summary>
    public static Color Parse(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 3)
            hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
        var r = Convert.ToInt32(hex.Substring(0, 2), 16);
        var g = Convert.ToInt32(hex.Substring(2, 2), 16);
        var b = Convert.ToInt32(hex.Substring(4, 2), 16);
        return FromRgb(r, g, b);
    }
}

/// <summary>
/// Defines a linear (axial) gradient between two colors for use as a fill pattern.
/// Mirrors <c>Aspose.Pdf.Drawing.GradientAxialShading</c>.
/// </summary>
public sealed class GradientAxialShading : PatternColorSpace
{
    /// <summary>Start color of the gradient.</summary>
    public Aspose.Pdf.Color? StartColor { get; set; }

    /// <summary>End color of the gradient.</summary>
    public Aspose.Pdf.Color? EndColor { get; set; }

    /// <summary>Start point of the gradient axis.</summary>
    public Aspose.Pdf.Point Start { get; set; } = new Aspose.Pdf.Point(0, 0);

    /// <summary>End point of the gradient axis.</summary>
    public Aspose.Pdf.Point End { get; set; } = new Aspose.Pdf.Point(1, 0);

    /// <summary>Construct an empty gradient. Colours and endpoints can be set via properties.</summary>
    public GradientAxialShading() { }

    /// <summary>Construct with start/end colours; endpoints default to (0,0)→(1,0).</summary>
    public GradientAxialShading(Aspose.Pdf.Color startColor, Aspose.Pdf.Color endColor)
    {
        StartColor = startColor;
        EndColor = endColor;
    }
}

/// <summary>A 2D point (x, y).</summary>
public sealed class Point
{
    public double X { get; set; }
    public double Y { get; set; }

    public Point(double x, double y) { X = x; Y = y; }
}

/// <summary>
/// Base class for drawable shapes.
/// </summary>
public abstract class Shape
{
    public Aspose.Pdf.GraphInfo GraphInfo { get; set; } = new();

    /// <summary>Optional text label rendered with the shape. Stored only —
    /// concrete shapes don't currently emit the label.</summary>
    public Aspose.Pdf.Text.TextFragment? Text { get; set; }

    /// <summary>Whether this shape lies within a <paramref name="containerWidth"/>×<paramref name="containerHeight"/>
    /// box anchored at the origin. Override on concrete subclasses; the base returns true.</summary>
    public virtual bool CheckBounds(double containerWidth, double containerHeight) => true;

    internal abstract void Render(ContentStreamBuilder builder, Page? page = null);

    /// <summary>
    /// If opacity is non-default and a page context is available, register an ExtGState
    /// and emit the gs operator.
    /// </summary>
    protected void ApplyOpacity(ContentStreamBuilder builder, Page? page)
    {
        if (page is null) return;
        var needsGs = GraphInfo.FillOpacity < 1.0 || GraphInfo.StrokeOpacity < 1.0;
        if (!needsGs) return;

        var gs = new Content.ExtGState
        {
            FillAlpha = GraphInfo.FillOpacity,
            StrokeAlpha = GraphInfo.StrokeOpacity,
        };
        var name = page.AddExtGState(gs);
        builder.SetExtGState(name);
    }

    /// <summary>
    /// Apply common style: line width, colors, dash pattern, opacity.
    /// </summary>
    protected void ApplyStyle(ContentStreamBuilder builder, Page? page)
    {
        ApplyOpacity(builder, page);
        builder.SetLineWidth(GraphInfo.LineWidth);
        if (GraphInfo.DashPattern is { Length: > 0 })
            builder.SetDashPattern(GraphInfo.DashPattern, GraphInfo.DashPhase);
        if (GraphInfo.StrokeColor is { } sc)
            builder.SetStrokeColor(sc.R, sc.G, sc.B);
        if (GraphInfo.FillColorInternal is { } fc)
            builder.SetFillColor(fc.R, fc.G, fc.B);
    }

    /// <summary>Apply appropriate paint operator based on fill/stroke settings.</summary>
    protected void Paint(ContentStreamBuilder builder)
    {
        // A shape is always stroked (the outline falls back to the default black
        // when no explicit Color is set); a FillColor additionally fills the region.
        if (GraphInfo.FillColor is not null)
            builder.FillAndStroke();
        else
            builder.Stroke();
    }
}

/// <summary>
/// A line shape.
/// </summary>
public sealed class Line : Shape
{
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

        builder.MoveTo(_coords[0], _coords[1]);
        for (var i = 2; i + 1 < _coords.Length; i += 2)
            builder.LineTo(_coords[i], _coords[i + 1]);

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

/// <summary>
/// A rectangle shape.
/// </summary>
public sealed class DrawingRectangle : Shape
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    /// <summary>Corner radius for rounded rectangles. Zero (default) means sharp corners.</summary>
    public double RoundedCornerRadius { get; set; }

    public DrawingRectangle(double x, double y, double width, double height)
    {
        X = x; Y = y; Width = width; Height = height;
    }

    internal override void Render(ContentStreamBuilder builder, Page? page = null)
    {
        ApplyOpacity(builder, page);
        builder.SetLineWidth(GraphInfo.LineWidth);
        if (GraphInfo.StrokeColor is { } sc)
            builder.SetStrokeColor(sc.R, sc.G, sc.B);
        if (GraphInfo.FillColorInternal is { } fc)
            builder.SetFillColor(fc.R, fc.G, fc.B);

        double r = RoundedCornerRadius;
        if (r <= 0)
        {
            builder.Rectangle(X, Y, Width, Height);
        }
        else
        {
            // Rounded rectangle using 4 quarter-arcs approximated by cubic Bézier curves.
            double x = X, y = Y, w = Width, h = Height;
            builder.MoveTo(x + r, y);
            builder.LineTo(x + w - r, y);
            AppendArcBeziers(builder, x + w - r, y + r, r, -90, 0);
            builder.LineTo(x + w, y + h - r);
            AppendArcBeziers(builder, x + w - r, y + h - r, r, 0, 90);
            builder.LineTo(x + r, y + h);
            AppendArcBeziers(builder, x + r, y + h - r, r, 90, 180);
            builder.LineTo(x, y + r);
            AppendArcBeziers(builder, x + r, y + r, r, 180, 270);
            builder.ClosePath();
        }

        if (GraphInfo.FillColor is not null && GraphInfo.StrokeColor is not null)
            builder.FillAndStroke();
        else if (GraphInfo.FillColor is not null)
            builder.Fill();
        else
            builder.Stroke();
    }

    /// <summary>
    /// Append a circular arc (approximated with cubic Bézier segments) centered at (cx,cy)
    /// from <paramref name="startDeg"/> to <paramref name="endDeg"/> (angles in degrees,
    /// counter-clockwise, PDF coordinate system).
    /// </summary>
    private static void AppendArcBeziers(ContentStreamBuilder builder, double cx, double cy,
        double radius, double startDeg, double endDeg)
    {
        double sweep = endDeg - startDeg;
        while (sweep < 0) sweep += 360;
        while (sweep > 360) sweep -= 360;
        if (sweep == 0) sweep = 360;

        int steps = (int)Math.Ceiling(Math.Abs(sweep) / 90);
        double stepAngle = sweep / steps;
        double currentDeg = startDeg;

        for (int i = 0; i < steps; i++)
        {
            double a0 = currentDeg * Math.PI / 180;
            double a1 = (currentDeg + stepAngle) * Math.PI / 180;
            double alpha = (4.0 / 3.0) * Math.Tan((a1 - a0) / 4.0);

            double p0x = cx + radius * Math.Cos(a0);
            double p0y = cy + radius * Math.Sin(a0);
            double p3x = cx + radius * Math.Cos(a1);
            double p3y = cy + radius * Math.Sin(a1);

            double cp1x = p0x - alpha * radius * Math.Sin(a0);
            double cp1y = p0y + alpha * radius * Math.Cos(a0);
            double cp2x = p3x + alpha * radius * Math.Sin(a1);
            double cp2y = p3y - alpha * radius * Math.Cos(a1);

            builder.CurveTo(cp1x, cp1y, cp2x, cp2y, p3x, p3y);
            currentDeg += stepAngle;
        }
    }
}

/// <summary>
/// A circle shape.
/// </summary>
public sealed class Circle : Shape
{
    public double CenterX { get; set; }
    public double CenterY { get; set; }
    public double Radius { get; set; }

    /// <summary>Centre X (alias for <see cref="CenterX"/>).</summary>
    public double PosX { get => CenterX; set => CenterX = value; }

    /// <summary>Centre Y (alias for <see cref="CenterY"/>).</summary>
    public double PosY { get => CenterY; set => CenterY = value; }

    public Circle(double centerX, double centerY, double radius)
    {
        CenterX = centerX; CenterY = centerY; Radius = radius;
    }

    /// <summary>Single-precision overload matching the Aspose.PDF for .NET public API.</summary>
    public Circle(float posX, float posY, float radius)
        : this((double)posX, (double)posY, (double)radius)
    {
    }

    /// <summary>Whether this circle lies entirely within a <paramref name="containerWidth"/>×<paramref name="containerHeight"/> box anchored at the origin.</summary>
    public override bool CheckBounds(double containerWidth, double containerHeight)
        => CenterX - Radius >= 0 && CenterY - Radius >= 0
           && CenterX + Radius <= containerWidth && CenterY + Radius <= containerHeight;

    internal override void Render(ContentStreamBuilder builder, Page? page = null)
    {
        ApplyOpacity(builder, page);
        builder.SetLineWidth(GraphInfo.LineWidth);
        if (GraphInfo.StrokeColor is { } sc)
            builder.SetStrokeColor(sc.R, sc.G, sc.B);
        if (GraphInfo.FillColorInternal is { } fc)
            builder.SetFillColor(fc.R, fc.G, fc.B);

        // Approximate circle with 4 Bézier curves
        var k = 0.5522847498; // magic constant
        var r = Radius;
        var cx = CenterX;
        var cy = CenterY;

        builder.MoveTo(cx + r, cy);
        builder.CurveTo(cx + r, cy + r * k, cx + r * k, cy + r, cx, cy + r);
        builder.CurveTo(cx - r * k, cy + r, cx - r, cy + r * k, cx - r, cy);
        builder.CurveTo(cx - r, cy - r * k, cx - r * k, cy - r, cx, cy - r);
        builder.CurveTo(cx + r * k, cy - r, cx + r, cy - r * k, cx + r, cy);
        builder.ClosePath();

        if (GraphInfo.FillColor is not null && GraphInfo.StrokeColor is not null)
            builder.FillAndStroke();
        else if (GraphInfo.FillColor is not null)
            builder.Fill();
        else
            builder.Stroke();
    }
}

/// <summary>
/// An ellipse shape.
/// </summary>
public sealed class Ellipse : Shape
{
    public double CenterX { get; set; }
    public double CenterY { get; set; }
    public double RadiusX { get; set; }
    public double RadiusY { get; set; }

    /// <summary>Left edge of the bounding box (CenterX − RadiusX).</summary>
    public double Left
    {
        get => CenterX - RadiusX;
        set { var rx = RadiusX; CenterX = value + rx; }
    }

    /// <summary>Bottom edge of the bounding box (CenterY − RadiusY).</summary>
    public double Bottom
    {
        get => CenterY - RadiusY;
        set { var ry = RadiusY; CenterY = value + ry; }
    }

    /// <summary>Width of the bounding box (2 × RadiusX).</summary>
    public double Width
    {
        get => 2 * RadiusX;
        set
        {
            var left = Left;
            RadiusX = value / 2.0;
            CenterX = left + RadiusX;
        }
    }

    /// <summary>Height of the bounding box (2 × RadiusY).</summary>
    public double Height
    {
        get => 2 * RadiusY;
        set
        {
            var bottom = Bottom;
            RadiusY = value / 2.0;
            CenterY = bottom + RadiusY;
        }
    }

    /// <summary>Bounding-box ctor. Parameters match the Aspose.PDF for .NET public API.</summary>
    public Ellipse(double left, double bottom, double width, double height)
    {
        RadiusX = width / 2.0;
        RadiusY = height / 2.0;
        CenterX = left + RadiusX;
        CenterY = bottom + RadiusY;
    }

    /// <summary>Whether this ellipse lies entirely within a <paramref name="containerWidth"/>×<paramref name="containerHeight"/> box anchored at the origin.</summary>
    public override bool CheckBounds(double containerWidth, double containerHeight)
        => Left >= 0 && Bottom >= 0 && Left + Width <= containerWidth && Bottom + Height <= containerHeight;

    internal override void Render(ContentStreamBuilder builder, Page? page = null)
    {
        ApplyOpacity(builder, page);
        builder.SetLineWidth(GraphInfo.LineWidth);
        if (GraphInfo.StrokeColor is { } sc)
            builder.SetStrokeColor(sc.R, sc.G, sc.B);
        if (GraphInfo.FillColorInternal is { } fc)
            builder.SetFillColor(fc.R, fc.G, fc.B);

        var k = 0.5522847498;
        var rx = RadiusX;
        var ry = RadiusY;
        var cx = CenterX;
        var cy = CenterY;

        builder.MoveTo(cx + rx, cy);
        builder.CurveTo(cx + rx, cy + ry * k, cx + rx * k, cy + ry, cx, cy + ry);
        builder.CurveTo(cx - rx * k, cy + ry, cx - rx, cy + ry * k, cx - rx, cy);
        builder.CurveTo(cx - rx, cy - ry * k, cx - rx * k, cy - ry, cx, cy - ry);
        builder.CurveTo(cx + rx * k, cy - ry, cx + rx, cy - ry * k, cx + rx, cy);
        builder.ClosePath();

        if (GraphInfo.FillColor is not null && GraphInfo.StrokeColor is not null)
            builder.FillAndStroke();
        else if (GraphInfo.FillColor is not null)
            builder.Fill();
        else
            builder.Stroke();
    }
}

/// <summary>
/// A polygon shape (closed path with arbitrary vertices).
/// </summary>
public sealed class Polygon : Shape
{
    public (double X, double Y)[] Points { get; set; }

    public Polygon(params (double X, double Y)[] points)
    {
        Points = points;
    }

    internal override void Render(ContentStreamBuilder builder, Page? page = null)
    {
        if (Points.Length < 2) return;
        ApplyStyle(builder, page);

        builder.MoveTo(Points[0].X, Points[0].Y);
        for (var i = 1; i < Points.Length; i++)
            builder.LineTo(Points[i].X, Points[i].Y);
        builder.ClosePath();

        Paint(builder);
    }
}

/// <summary>
/// An arc shape (portion of an ellipse).
/// </summary>
public sealed class Arc : Shape
{
    public double CenterX { get; set; }
    public double CenterY { get; set; }
    public double RadiusX { get; set; }
    public double RadiusY { get; set; }

    /// <summary>Start angle in degrees (0 = 3 o'clock, counter-clockwise).</summary>
    public double StartAngle { get; set; }

    /// <summary>Sweep angle in degrees (positive = counter-clockwise).</summary>
    public double SweepAngle { get; set; }

    /// <summary>Centre X (alias of <see cref="CenterX"/>).</summary>
    public double PosX { get => CenterX; set => CenterX = value; }

    /// <summary>Centre Y (alias of <see cref="CenterY"/>).</summary>
    public double PosY { get => CenterY; set => CenterY = value; }

    /// <summary>Radius (sets both <see cref="RadiusX"/> and <see cref="RadiusY"/>).</summary>
    public double Radius
    {
        get => RadiusX;
        set { RadiusX = value; RadiusY = value; }
    }

    /// <summary>Start angle in degrees (alias of <see cref="StartAngle"/>).</summary>
    public double Alpha { get => StartAngle; set => StartAngle = value; }

    /// <summary>End angle in degrees; setting recomputes <see cref="SweepAngle"/> as <c>value − Alpha</c>.</summary>
    public double Beta
    {
        get => StartAngle + SweepAngle;
        set => SweepAngle = value - StartAngle;
    }

    public Arc(double centerX, double centerY, double radiusX, double radiusY,
        double startAngle, double sweepAngle)
    {
        CenterX = centerX; CenterY = centerY;
        RadiusX = radiusX; RadiusY = radiusY;
        StartAngle = startAngle; SweepAngle = sweepAngle;
    }

    /// <summary>
    /// Constructor matching the public API: Arc(posX, posY, radius, alpha, beta)
    /// where alpha = start angle, beta = end angle.
    /// </summary>
    public Arc(double centerX, double centerY, double radius, double alpha, double beta)
        : this(centerX, centerY, radius, radius, alpha, beta - alpha)
    {
    }

    /// <summary>Single-precision overload matching the Aspose.PDF for .NET public API.</summary>
    public Arc(float posX, float posY, float radius, float alpha, float beta)
        : this((double)posX, (double)posY, (double)radius, (double)alpha, (double)beta)
    {
    }

    /// <summary>Whether this arc lies entirely within a <paramref name="containerWidth"/>×<paramref name="containerHeight"/> box anchored at the origin.</summary>
    public override bool CheckBounds(double containerWidth, double containerHeight)
    {
        var minX = CenterX - RadiusX;
        var maxX = CenterX + RadiusX;
        var minY = CenterY - RadiusY;
        var maxY = CenterY + RadiusY;
        return minX >= 0 && maxX <= containerWidth && minY >= 0 && maxY <= containerHeight;
    }

    internal override void Render(ContentStreamBuilder builder, Page? page = null)
    {
        ApplyStyle(builder, page);

        // Approximate arc with Bézier curves (max 90° per segment)
        var startRad = StartAngle * Math.PI / 180;
        var sweepRad = SweepAngle * Math.PI / 180;
        var segments = (int)Math.Ceiling(Math.Abs(sweepRad) / (Math.PI / 2));
        if (segments < 1) segments = 1;

        var segmentAngle = sweepRad / segments;
        var currentAngle = startRad;

        var startX = CenterX + RadiusX * Math.Cos(currentAngle);
        var startY = CenterY + RadiusY * Math.Sin(currentAngle);
        builder.MoveTo(startX, startY);

        for (var i = 0; i < segments; i++)
        {
            var endAngle = currentAngle + segmentAngle;
            AppendArcSegment(builder, currentAngle, endAngle);
            currentAngle = endAngle;
        }

        // A FillColor closes the arc (the PDF fill operator implicitly closes the
        // subpath, chord-style) and paints the enclosed region; otherwise stroke only.
        Paint(builder);
    }

    private void AppendArcSegment(ContentStreamBuilder builder, double a1, double a2)
    {
        // Bézier approximation for an arc segment
        var alpha = (a2 - a1) / 2;
        var cosAlpha = Math.Cos(alpha);
        var sinAlpha = Math.Sin(alpha);
        var cotAlpha = cosAlpha / sinAlpha;
        var phi = (a1 + a2) / 2;
        var cosPhi = Math.Cos(phi);
        var sinPhi = Math.Sin(phi);
        var lambda = (4.0 - cosAlpha) / 3.0;

        // Avoid division by zero for very small angles
        if (Math.Abs(sinAlpha) < 1e-10) return;

        var mu = sinAlpha + (cosAlpha - lambda) * cotAlpha;

        var p2x = CenterX + RadiusX * (lambda * cosPhi + mu * sinPhi);
        var p2y = CenterY + RadiusY * (lambda * sinPhi - mu * cosPhi);
        var p3x = CenterX + RadiusX * (lambda * cosPhi - mu * sinPhi);
        var p3y = CenterY + RadiusY * (lambda * sinPhi + mu * cosPhi);
        var p4x = CenterX + RadiusX * Math.Cos(a2);
        var p4y = CenterY + RadiusY * Math.Sin(a2);

        builder.CurveTo(p2x, p2y, p3x, p3y, p4x, p4y);
    }
}

/// <summary>
/// A Bézier curve shape.
/// </summary>
public sealed class Curve : Shape
{
    public double X1 { get; set; }
    public double Y1 { get; set; }
    public double Cx1 { get; set; }
    public double Cy1 { get; set; }
    public double Cx2 { get; set; }
    public double Cy2 { get; set; }
    public double X2 { get; set; }
    public double Y2 { get; set; }

    /// <summary>
    /// Create a cubic Bézier curve from (x1,y1) to (x2,y2) with control points (cx1,cy1) and (cx2,cy2).
    /// </summary>
    public Curve(double x1, double y1, double cx1, double cy1,
        double cx2, double cy2, double x2, double y2)
    {
        X1 = x1; Y1 = y1; Cx1 = cx1; Cy1 = cy1;
        Cx2 = cx2; Cy2 = cy2; X2 = x2; Y2 = y2;
    }

    /// <summary>
    /// Constructor matching the public API: Curve(float[] positionArray)
    /// where positionArray = [x1, y1, cx1, cy1, cx2, cy2, x2, y2].
    /// </summary>
    public Curve(float[] positionArray) : this(
        positionArray[0], positionArray[1], positionArray[2], positionArray[3],
        positionArray[4], positionArray[5], positionArray[6], positionArray[7])
    {
    }

    /// <summary>Curve control points as [x1, y1, cx1, cy1, cx2, cy2, x2, y2].</summary>
    public float[] PositionArray
    {
        get => new[] { (float)X1, (float)Y1, (float)Cx1, (float)Cy1,
                       (float)Cx2, (float)Cy2, (float)X2, (float)Y2 };
        set
        {
            if (value is null || value.Length < 8) return;
            X1 = value[0]; Y1 = value[1]; Cx1 = value[2]; Cy1 = value[3];
            Cx2 = value[4]; Cy2 = value[5]; X2 = value[6]; Y2 = value[7];
        }
    }

    /// <summary>Whether every control point lies within the container's origin-anchored AABB.</summary>
    public override bool CheckBounds(double containerWidth, double containerHeight)
        => X1 >= 0 && Y1 >= 0 && X2 >= 0 && Y2 >= 0
           && Cx1 >= 0 && Cy1 >= 0 && Cx2 >= 0 && Cy2 >= 0
           && X1 <= containerWidth && X2 <= containerWidth
           && Cx1 <= containerWidth && Cx2 <= containerWidth
           && Y1 <= containerHeight && Y2 <= containerHeight
           && Cy1 <= containerHeight && Cy2 <= containerHeight;

    internal override void Render(ContentStreamBuilder builder, Page? page = null)
    {
        ApplyStyle(builder, page);
        builder.MoveTo(X1, Y1);
        builder.CurveTo(Cx1, Cy1, Cx2, Cy2, X2, Y2);
        Paint(builder);
    }
}

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

/// <summary>
/// A rectangle shape in the Drawing namespace (distinct from Aspose.Pdf.Rectangle which is a page rectangle).
/// </summary>
public sealed class Rectangle : Shape
{
    public double Left { get; set; }
    public double Bottom { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    /// <summary>Corner radius for rounded-rectangle rendering (0 = sharp corners). Stored only.</summary>
    public double RoundedCornerRadius { get; set; }

    public Rectangle(double left, double bottom, double width, double height)
    {
        Left = left; Bottom = bottom; Width = width; Height = height;
    }

    /// <summary>Single-precision overload matching the Aspose.PDF for .NET public API.</summary>
    public Rectangle(float left, float bottom, float width, float height)
        : this((double)left, (double)bottom, (double)width, (double)height) { }

    /// <summary>Whether the rectangle lies entirely within an origin-anchored container.</summary>
    public override bool CheckBounds(double containerWidth, double containerHeight)
        => Left >= 0 && Bottom >= 0
           && Left + Width <= containerWidth
           && Bottom + Height <= containerHeight;

    internal override void Render(ContentStreamBuilder builder, Page? page = null)
    {
        ApplyStyle(builder, page);
        builder.Rectangle(Left, Bottom, Width, Height);
        Paint(builder);
    }
}

/// <summary>
/// A graph container that holds drawable shapes and renders them to a content stream.
/// </summary>
public sealed class Graph : BaseParagraph
{
    public double Width { get; set; }
    public double Height { get; set; }

    /// <summary>
    /// When false the graph is placed at an absolute position (<see cref="Left"/>, <see cref="Top"/>)
    /// and does not participate in the document flow. Default: true.
    /// </summary>
    public bool IsChangePosition { get; set; } = true;

    /// <summary>Absolute horizontal position from the left page edge (used when <see cref="IsChangePosition"/> is false).</summary>
    public double Left { get; set; }

    /// <summary>Absolute vertical position from the top page edge (used when <see cref="IsChangePosition"/> is false).</summary>
    public double Top { get; set; }

    /// <summary>Optional border around the graph area.</summary>
    public BorderInfo? Border { get; set; }

    /// <summary>Default stroke/fill settings inherited by shapes added to this graph.</summary>
    public Aspose.Pdf.GraphInfo GraphInfo { get; set; } = new();

    /// <summary>Z-index for layering; higher draws on top.</summary>
    public new int ZIndex { get; set; }

    /// <summary>Optional title rendered above the graph. Stored only.</summary>
    public Aspose.Pdf.Text.TextFragment? Title { get; set; }

    /// <summary>The list of shapes inside this graph. Alias for <see cref="Add"/>.</summary>
    public BoundsCheckableList<Shape> Shapes { get; set; } = new();

    public Graph(double width, double height)
    {
        Width = width;
        Height = height;
    }

    /// <summary>Single-precision overload matching the Aspose.PDF for .NET public API.</summary>
    public Graph(float width, float height) : this((double)width, (double)height) { }

    public void Add(Shape shape) => Shapes.Add(shape);

    /// <summary>Shallow clone — shapes are shared by reference.</summary>
    public override object Clone()
    {
        var copy = new Graph(Width, Height)
        {
            IsChangePosition = IsChangePosition,
            Left = Left,
            Top = Top,
            Border = Border,
            GraphInfo = GraphInfo,
            ZIndex = ZIndex,
            Title = Title,
        };
        foreach (var s in Shapes) copy.Shapes.Add(s);
        return copy;
    }

    /// <summary>Render all shapes to a content stream.</summary>
    public byte[] Build()
    {
        return Build(null);
    }

    /// <summary>Render all shapes to a content stream, registering ExtGState resources on the page if needed.</summary>
    public byte[] Build(Page? page)
    {
        return Build(page, 0, 0);
    }

    /// <summary>
    /// Render all shapes to a content stream translated so the graph's local origin
    /// (bottom-left corner) lands at (<paramref name="offsetX"/>, <paramref name="offsetY"/>)
    /// in page coordinates. Shape coordinates are relative to the graph box.
    /// </summary>
    public byte[] Build(Page? page, double offsetX, double offsetY)
    {
        var builder = new ContentStreamBuilder();
        builder.SaveState();
        if (offsetX != 0 || offsetY != 0)
            builder.SetMatrix(1, 0, 0, 1, offsetX, offsetY);
        if (Border is not null && Border.Side != BorderSide.None)
            RenderBorder(builder);
        foreach (var shape in Shapes)
        {
            // Isolate each shape's graphics state (colour, line width, dash) so a
            // shape that sets no explicit colour falls back to the default black
            // rather than inheriting the previous shape's stroke colour.
            builder.SaveState();
            shape.Render(builder, page);
            builder.RestoreState();
        }
        builder.RestoreState();
        return builder.Build();
    }

    /// <summary>Draw the configured border around the graph box (local coordinates).</summary>
    private void RenderBorder(ContentStreamBuilder builder)
    {
        builder.SetLineWidth(Border!.Width);
        builder.SetStrokeColor(Border.Color);
        var side = Border.Side;
        if (side.HasFlag(BorderSide.Bottom))
            builder.MoveTo(0, 0).LineTo(Width, 0).Stroke();
        if (side.HasFlag(BorderSide.Top))
            builder.MoveTo(0, Height).LineTo(Width, Height).Stroke();
        if (side.HasFlag(BorderSide.Left))
            builder.MoveTo(0, 0).LineTo(0, Height).Stroke();
        if (side.HasFlag(BorderSide.Right))
            builder.MoveTo(Width, 0).LineTo(Width, Height).Stroke();
    }
}
