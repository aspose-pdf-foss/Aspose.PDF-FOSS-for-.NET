using Aspose.Pdf.Content;

namespace Aspose.Pdf.Drawing;

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
