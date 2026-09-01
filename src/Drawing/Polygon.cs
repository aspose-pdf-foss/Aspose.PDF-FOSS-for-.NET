using Aspose.Pdf.Content;

namespace Aspose.Pdf.Drawing;

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
