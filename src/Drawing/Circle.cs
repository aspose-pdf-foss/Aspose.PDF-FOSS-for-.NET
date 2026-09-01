using Aspose.Pdf.Content;

namespace Aspose.Pdf.Drawing;

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

    /// <summary>Single-precision overload matching the public API.</summary>
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
