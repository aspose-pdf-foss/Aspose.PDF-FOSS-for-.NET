using Aspose.Pdf.Content;

namespace Aspose.Pdf.Drawing;

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
