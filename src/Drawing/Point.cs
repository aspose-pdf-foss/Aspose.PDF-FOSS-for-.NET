using Aspose.Pdf.Content;

namespace Aspose.Pdf.Drawing;

/// <summary>A 2D point (x, y).</summary>
public sealed class Point
{
    /// <summary>Gets or sets the X coordinate.</summary>
    public double X { get; set; }
    /// <summary>Gets or sets the Y coordinate.</summary>
    public double Y { get; set; }

    /// <summary>Creates a point with the given X and Y coordinates.</summary>
    public Point(double x, double y) { X = x; Y = y; }
}
