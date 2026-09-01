using Aspose.Pdf.Content;

namespace Aspose.Pdf.Drawing;

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

    /// <summary>Bounding-box ctor. Parameters match the public API.</summary>
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
