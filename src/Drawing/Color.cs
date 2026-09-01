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
