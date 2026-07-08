namespace Aspose.Pdf;

/// <summary>
/// Represents a color value used in PDF documents.
/// ARGB color value with optional ColorSpace marker.
/// </summary>
public sealed class Color
{
    /// <summary>RGB components in 0–255 range.</summary>
    public byte R { get; }
    public byte G { get; }
    public byte B { get; }

    /// <summary>Byte-typed alpha (0–255). FOSS-internal companion to <see cref="A"/>.</summary>
    public byte AByte { get; }

    /// <summary>Alpha in 0..1 range (Aspose.Pdf public surface).</summary>
    public double A => AByte / 255.0;

    /// <summary>Whether this color has been set (non-empty).</summary>
    public bool IsEmpty => AByte == 0 && R == 0 && G == 0 && B == 0;

    private double[]? _data;
    /// <summary>The colour-space component values: one entry for DeviceGray,
    /// three for DeviceRGB. Computed from the colour unless explicitly set.</summary>
    public double[]? Data
    {
        get => _data ?? (ColorType == ColorType.Grayscale ? [R / 255.0] : ToRgbArray());
        internal set => _data = value;
    }

    /// <summary>The pattern color space (for advanced use).</summary>
    public Aspose.Pdf.Drawing.PatternColorSpace? PatternColorSpace { get; set; }

    /// <summary>Top-level Aspose.Pdf.ColorSpace selector.</summary>
    public Aspose.Pdf.ColorSpace ColorSpace
    {
        get => ColorType switch
        {
            ColorType.Cmyk => Aspose.Pdf.ColorSpace.DeviceCMYK,
            ColorType.Grayscale => Aspose.Pdf.ColorSpace.DeviceGray,
            _ => Aspose.Pdf.ColorSpace.DeviceRGB,
        };
    }

    /// <summary>FOSS-internal colour-space marker (ColorType.Rgb / Cmyk / Gray).</summary>
    public ColorType ColorType { get; set; } = ColorType.Rgb;

    /// <summary>Creates an empty (transparent black) color.</summary>
    public Color() : this(0, 0, 0, 0) { }

    private Color(byte a, byte r, byte g, byte b)
    {
        AByte = a; R = r; G = g; B = b;
    }

    // ── Value equality ─────────────────────────────────────────────
    // Color is a value-like type: two FromRgb(0,0,0) calls compare equal, and the
    // ColorSpace marker keeps RGB and CMYK distinct even when the byte values match
    // (so FromRgb(1,0,0) != FromCmyk(1,0,0,0)).

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        if (obj is Color other)
            return AByte == other.AByte && R == other.R && G == other.G && B == other.B
                && ColorType == other.ColorType;
        // Support comparison with packed RGB int: (R << 16) | (G << 8) | B
        if (obj is int rgb)
            return R == ((rgb >> 16) & 0xFF) && G == ((rgb >> 8) & 0xFF) && B == (rgb & 0xFF);
        return false;
    }

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(AByte, R, G, B, ColorType);

    /// <summary>Value equality operator. Compares ARGB bytes plus ColorSpace.</summary>
    public static bool operator ==(Color? left, Color? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        return left.Equals(right);
    }

    /// <summary>Value inequality operator.</summary>
    public static bool operator !=(Color? left, Color? right) => !(left == right);

    // ── Factory methods ────────────────────────────────────────────

    public static Color FromArgb(int r, int g, int b) => new(255, (byte)r, (byte)g, (byte)b);
    public static Color FromArgb(int a, int r, int g, int b) => new((byte)a, (byte)r, (byte)g, (byte)b);
    public static Color FromRgb(int r, int g, int b) => new(255, (byte)r, (byte)g, (byte)b);

    public static Color FromRgb(double r, double g, double b) =>
        new(255, ToByte(r), ToByte(g), ToByte(b));

    public static Color FromRgb(System.Drawing.Color color) =>
        new(color.A, color.R, color.G, color.B);

    public static Color FromCmyk(double c, double m, double y, double k)
    {
        // Convert through the same SWOP-style CMYK→sRGB profile LUT the content
        // operators (SetCMYKColor) and the renderer use, so a CMYK colour resolves
        // to the same RGB regardless of which API produced it. A
        // naive (1-c)(1-k) cutoff diverges from the colour-managed operator path.
        var (r, g, b) = Aspose.Pdf.Devices.CmykToRgbLut.Convert(c, m, y, k);
        return new Color(255, r, g, b) { ColorType = ColorType.Cmyk };
    }

    public static Color FromGray(double g) =>
        new(255, ToByte(g), ToByte(g), ToByte(g));

    // Quantise a 0..1 colour component to an 8-bit channel by rounding (Adobe
    // convention), matching how XFDF/PDF colour values round-trip through #RRGGBB.
    private static byte ToByte(double component)
    {
        var v = System.Math.Round(component * 255.0);
        if (v < 0) v = 0; else if (v > 255) v = 255;
        return (byte)v;
    }

    public static Color Parse(string value)
    {
        var hex = value.TrimStart('#');
        if (hex.Length == 6)
            return new Color(255,
                Convert.ToByte(hex[..2], 16),
                Convert.ToByte(hex[2..4], 16),
                Convert.ToByte(hex[4..6], 16));
        if (hex.Length == 8)
            return new Color(
                Convert.ToByte(hex[..2], 16),
                Convert.ToByte(hex[2..4], 16),
                Convert.ToByte(hex[4..6], 16),
                Convert.ToByte(hex[6..8], 16));
        return Black;
    }

    /// <summary>Convert to PDF color array [r, g, b] in 0–1 range.</summary>
    public double[] ToRgbArray() => [R / 255.0, G / 255.0, B / 255.0];

    public System.Drawing.Color ToRgb() => System.Drawing.Color.FromArgb(AByte, R, G, B);

    // ── Named colors ──

    public static readonly Color Empty = new(0, 0, 0, 0);
    public static Color Transparent { get; } = new(0, 255, 255, 255);
    public static Color AliceBlue { get; } = new(255, 240, 248, 255);
    public static Color AntiqueWhite { get; } = new(255, 250, 235, 215);
    public static Color Aqua { get; } = new(255, 0, 255, 255);
    public static Color Aquamarine { get; } = new(255, 127, 255, 212);
    public static Color Azure { get; } = new(255, 240, 255, 255);
    public static Color Beige { get; } = new(255, 245, 245, 220);
    public static Color Bisque { get; } = new(255, 255, 228, 196);
    public static Color Black { get; } = new(255, 0, 0, 0);
    public static Color BlanchedAlmond { get; } = new(255, 255, 235, 205);
    public static Color Blue { get; } = new(255, 0, 0, 255);
    public static Color BlueViolet { get; } = new(255, 138, 43, 226);
    public static Color Brown { get; } = new(255, 165, 42, 42);
    public static Color BurlyWood { get; } = new(255, 222, 184, 135);
    public static Color CadetBlue { get; } = new(255, 95, 158, 160);
    public static Color Chartreuse { get; } = new(255, 127, 255, 0);
    public static Color Chocolate { get; } = new(255, 210, 105, 30);
    public static Color Coral { get; } = new(255, 255, 127, 80);
    public static Color CornflowerBlue { get; } = new(255, 100, 149, 237);
    public static Color Cornsilk { get; } = new(255, 255, 248, 220);
    public static Color Crimson { get; } = new(255, 220, 20, 60);
    public static Color Cyan { get; } = new(255, 0, 255, 255);
    public static Color DarkBlue { get; } = new(255, 0, 0, 139);
    public static Color DarkCyan { get; } = new(255, 0, 139, 139);
    public static Color DarkGoldenrod { get; } = new(255, 184, 134, 11);
    public static Color DarkGray { get; } = new(255, 169, 169, 169);
    public static Color DarkGreen { get; } = new(255, 0, 100, 0);
    public static Color DarkKhaki { get; } = new(255, 189, 183, 107);
    public static Color DarkMagenta { get; } = new(255, 139, 0, 139);
    public static Color DarkOliveGreen { get; } = new(255, 85, 107, 47);
    public static Color DarkOrange { get; } = new(255, 255, 140, 0);
    public static Color DarkOrchid { get; } = new(255, 153, 50, 204);
    public static Color DarkRed { get; } = new(255, 139, 0, 0);
    public static Color DarkSalmon { get; } = new(255, 233, 150, 122);
    public static Color DarkSeaGreen { get; } = new(255, 143, 188, 143);
    public static Color DarkSlateBlue { get; } = new(255, 72, 61, 139);
    public static Color DarkSlateGray { get; } = new(255, 47, 79, 79);
    public static Color DarkTurquoise { get; } = new(255, 0, 206, 209);
    public static Color DarkViolet { get; } = new(255, 148, 0, 211);
    public static Color DeepPink { get; } = new(255, 255, 20, 147);
    public static Color DeepSkyBlue { get; } = new(255, 0, 191, 255);
    public static Color DimGray { get; } = new(255, 105, 105, 105);
    public static Color DodgerBlue { get; } = new(255, 30, 144, 255);
    public static Color Firebrick { get; } = new(255, 178, 34, 34);
    public static Color FloralWhite { get; } = new(255, 255, 250, 240);
    public static Color ForestGreen { get; } = new(255, 34, 139, 34);
    public static Color Fuchsia { get; } = new(255, 255, 0, 255);
    public static Color Gainsboro { get; } = new(255, 220, 220, 220);
    public static Color GhostWhite { get; } = new(255, 248, 248, 255);
    public static Color Gold { get; } = new(255, 255, 215, 0);
    public static Color Goldenrod { get; } = new(255, 218, 165, 32);
    public static Color Gray { get; } = new(255, 128, 128, 128);
    public static Color Green { get; } = new(255, 0, 128, 0);
    public static Color GreenYellow { get; } = new(255, 173, 255, 47);
    public static Color Honeydew { get; } = new(255, 240, 255, 240);
    public static Color HotPink { get; } = new(255, 255, 105, 180);
    public static Color IndianRed { get; } = new(255, 205, 92, 92);
    public static Color Indigo { get; } = new(255, 75, 0, 130);
    public static Color Ivory { get; } = new(255, 255, 255, 240);
    public static Color Khaki { get; } = new(255, 240, 230, 140);
    public static Color Lavender { get; } = new(255, 230, 230, 250);
    public static Color LavenderBlush { get; } = new(255, 255, 240, 245);
    public static Color LawnGreen { get; } = new(255, 124, 252, 0);
    public static Color LemonChiffon { get; } = new(255, 255, 250, 205);
    public static Color LightBlue { get; } = new(255, 173, 216, 230);
    public static Color LightCoral { get; } = new(255, 240, 128, 128);
    public static Color LightCyan { get; } = new(255, 224, 255, 255);
    public static Color LightGoldenrodYellow { get; } = new(255, 250, 250, 210);
    public static Color LightGray { get; } = new(255, 211, 211, 211);
    public static Color LightGreen { get; } = new(255, 144, 238, 144);
    public static Color LightPink { get; } = new(255, 255, 182, 193);
    public static Color LightSalmon { get; } = new(255, 255, 160, 122);
    public static Color LightSeaGreen { get; } = new(255, 32, 178, 170);
    public static Color LightSkyBlue { get; } = new(255, 135, 206, 250);
    public static Color LightSlateGray { get; } = new(255, 119, 136, 153);
    public static Color LightSteelBlue { get; } = new(255, 176, 196, 222);
    public static Color LightYellow { get; } = new(255, 255, 255, 224);
    public static Color Lime { get; } = new(255, 0, 255, 0);
    public static Color LimeGreen { get; } = new(255, 50, 205, 50);
    public static Color Linen { get; } = new(255, 250, 240, 230);
    public static Color Magenta { get; } = new(255, 255, 0, 255);
    public static Color Maroon { get; } = new(255, 128, 0, 0);
    public static Color MediumAquamarine { get; } = new(255, 102, 205, 170);
    public static Color MediumBlue { get; } = new(255, 0, 0, 205);
    public static Color MediumOrchid { get; } = new(255, 186, 85, 211);
    public static Color MediumPurple { get; } = new(255, 147, 112, 219);
    public static Color MediumSeaGreen { get; } = new(255, 60, 179, 113);
    public static Color MediumSlateBlue { get; } = new(255, 123, 104, 238);
    public static Color MediumSpringGreen { get; } = new(255, 0, 250, 154);
    public static Color MediumTurquoise { get; } = new(255, 72, 209, 204);
    public static Color MediumVioletRed { get; } = new(255, 199, 21, 133);
    public static Color MidnightBlue { get; } = new(255, 25, 25, 112);
    public static Color MintCream { get; } = new(255, 245, 255, 250);
    public static Color MistyRose { get; } = new(255, 255, 228, 225);
    public static Color Moccasin { get; } = new(255, 255, 228, 181);
    public static Color NavajoWhite { get; } = new(255, 255, 222, 173);
    public static Color Navy { get; } = new(255, 0, 0, 128);
    public static Color OldLace { get; } = new(255, 253, 245, 230);
    public static Color Olive { get; } = new(255, 128, 128, 0);
    public static Color OliveDrab { get; } = new(255, 107, 142, 35);
    public static Color Orange { get; } = new(255, 255, 165, 0);
    public static Color OrangeRed { get; } = new(255, 255, 69, 0);
    public static Color Orchid { get; } = new(255, 218, 112, 214);
    public static Color PaleGoldenrod { get; } = new(255, 238, 232, 170);
    public static Color PaleGreen { get; } = new(255, 152, 251, 152);
    public static Color PaleTurquoise { get; } = new(255, 175, 238, 238);
    public static Color PaleVioletRed { get; } = new(255, 219, 112, 147);
    public static Color PapayaWhip { get; } = new(255, 255, 239, 213);
    public static Color PeachPuff { get; } = new(255, 255, 218, 185);
    public static Color Peru { get; } = new(255, 205, 133, 63);
    public static Color Pink { get; } = new(255, 255, 192, 203);
    public static Color Plum { get; } = new(255, 221, 160, 221);
    public static Color PowderBlue { get; } = new(255, 176, 224, 230);
    public static Color Purple { get; } = new(255, 128, 0, 128);
    public static Color Red { get; } = new(255, 255, 0, 0);
    public static Color RosyBrown { get; } = new(255, 188, 143, 143);
    public static Color RoyalBlue { get; } = new(255, 65, 105, 225);
    public static Color SaddleBrown { get; } = new(255, 139, 69, 19);
    public static Color Salmon { get; } = new(255, 250, 128, 114);
    public static Color SandyBrown { get; } = new(255, 244, 164, 96);
    public static Color SeaGreen { get; } = new(255, 46, 139, 87);
    public static Color SeaShell { get; } = new(255, 255, 245, 238);
    public static Color Sienna { get; } = new(255, 160, 82, 45);
    public static Color Silver { get; } = new(255, 192, 192, 192);
    public static Color SkyBlue { get; } = new(255, 135, 206, 235);
    public static Color SlateBlue { get; } = new(255, 106, 90, 205);
    public static Color SlateGray { get; } = new(255, 112, 128, 144);
    public static Color Snow { get; } = new(255, 255, 250, 250);
    public static Color SpringGreen { get; } = new(255, 0, 255, 127);
    public static Color SteelBlue { get; } = new(255, 70, 130, 180);
    public static Color Tan { get; } = new(255, 210, 180, 140);
    public static Color Teal { get; } = new(255, 0, 128, 128);
    public static Color Thistle { get; } = new(255, 216, 191, 216);
    public static Color Tomato { get; } = new(255, 255, 99, 71);
    public static Color Turquoise { get; } = new(255, 64, 224, 208);
    public static Color Violet { get; } = new(255, 238, 130, 238);
    public static Color Wheat { get; } = new(255, 245, 222, 179);
    public static Color White { get; } = new(255, 255, 255, 255);
    public static Color WhiteSmoke { get; } = new(255, 245, 245, 245);
    public static Color Yellow { get; } = new(255, 255, 255, 0);
    public static Color YellowGreen { get; } = new(255, 154, 205, 50);

    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
}
