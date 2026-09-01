// PDF content stream operators — PDF32000_2008 §8–9
using System.Globalization;
using System.Text;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Operators;

/// <summary>rg — Set RGB fill color.</summary>
public sealed class SetRGBColor : Operator
{
    public double R { get; set; }
    public double G { get; set; }
    public double B { get; set; }

    public SetRGBColor(double r, double g, double b) { R = r; G = g; B = b; }
    public SetRGBColor(System.Drawing.Color color)
        : this(color.R / 255.0, color.G / 255.0, color.B / 255.0) { }
    public override string ToPdf() => $"{FmtColor(R)} {FmtColor(G)} {FmtColor(B)} rg";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
    public System.Drawing.Color getColor() => SetColorOperator.ToSystemColor(new[] { R, G, B });
}

/// <summary>Common base for the SC/SCN/sc/scn family.</summary>
public abstract class SetColorOperator : Operator
{
    /// <summary>Return the operator's colour as a System.Drawing.Color.
    /// Override on concrete subclasses; the base returns black.</summary>
    public virtual System.Drawing.Color getColor() => System.Drawing.Color.Black;

    /// <summary>Map a PDF color array (1=gray, 3=RGB, 4=CMYK) to a System.Drawing.Color.</summary>
    internal static System.Drawing.Color ToSystemColor(double[] color)
    {
        if (color is null || color.Length == 0) return System.Drawing.Color.Black;
        if (color.Length == 1)
        {
            var g = Clamp255(color[0]);
            return System.Drawing.Color.FromArgb(g, g, g);
        }
        if (color.Length == 3)
            return System.Drawing.Color.FromArgb(Clamp255(color[0]), Clamp255(color[1]), Clamp255(color[2]));
        if (color.Length >= 4)
        {
            // Use the SWOP-style CMYK→sRGB profile LUT (the same transform the renderer
            // uses) rather than a naive cutoff — a CMYK black (K=1) maps to a rich black,
            // not pure (0,0,0), matching the colour-managed result.
            var (r, g, b) = Aspose.Pdf.Devices.CmykToRgbLut.Convert(color[0], color[1], color[2], color[3]);
            return System.Drawing.Color.FromArgb(r, g, b);
        }
        return System.Drawing.Color.Black;
    }

    private static int Clamp255(double v) => v <= 0 ? 0 : v >= 1 ? 255 : (int)System.Math.Round(v * 255);
}

/// <summary>Common base for the rg/RG/g/G/k/K family. Exposes get-only
/// projections over the color components; concrete subclasses (SetGray,
/// SetRGBColor, SetCMYKColor, …) shadow with their own typed get/set
/// storage via the `new` keyword.</summary>
public abstract class BasicSetColorOperator : SetColorOperator
{
    public virtual double[] Color => Array.Empty<double>();
    public virtual double Gray => 0;
    public virtual double R => 0;
    public virtual double G => 0;
    public virtual double B => 0;
    public virtual double C => 0;
    public virtual double M => 0;
    public virtual double Y => 0;
    public virtual double K => 0;
}

/// <summary>Common base for SCN/scn (basic color or pattern).</summary>
public abstract class BasicSetColorAndPatternOperator : SetColorOperator
{
    /// <summary>Pattern resource name used by this operator, or empty when this is a plain colour.</summary>
    public virtual string PatternName => string.Empty;
}

// =====================================================================
// Enums used by graphics-state operators.
// =====================================================================

/// <summary>g — Set gray fill color.</summary>
public sealed class SetGray : BasicSetColorOperator
{
    public new double Gray { get; set; }
    public SetGray(double gray) { Gray = gray; }
    public override string ToPdf() => $"{FmtColor(Gray)} g";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
    public override System.Drawing.Color getColor() => SetColorOperator.ToSystemColor(new[] { Gray });
}

/// <summary>G — Set gray stroke color.</summary>
public sealed class SetGrayStroke : BasicSetColorOperator
{
    public new double Gray { get; set; }
    public SetGrayStroke(double gray) { Gray = gray; }
    public override string ToPdf() => $"{FmtColor(Gray)} G";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
    public override System.Drawing.Color getColor() => SetColorOperator.ToSystemColor(new[] { Gray });
}

/// <summary>RG — Set RGB stroke color.</summary>
public sealed class SetRGBColorStroke : BasicSetColorOperator
{
    public new double R { get; set; }
    public new double G { get; set; }
    public new double B { get; set; }
    public SetRGBColorStroke(double r, double g, double b) { R = r; G = g; B = b; }
    public SetRGBColorStroke(System.Drawing.Color color)
        : this(color.R / 255.0, color.G / 255.0, color.B / 255.0) { }
    public override string ToPdf() => $"{FmtColor(R)} {FmtColor(G)} {FmtColor(B)} RG";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
    public override System.Drawing.Color getColor() => SetColorOperator.ToSystemColor(new[] { R, G, B });
}

/// <summary>k — Set CMYK fill color.</summary>
public sealed class SetCMYKColor : BasicSetColorOperator
{
    public new double C { get; set; }
    public new double M { get; set; }
    public new double Y { get; set; }
    public new double K { get; set; }
    public SetCMYKColor(double c, double m, double y, double k) { C = c; M = m; Y = y; K = k; }
    public override string ToPdf() => $"{FmtColor(C)} {FmtColor(M)} {FmtColor(Y)} {FmtColor(K)} k";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
    public override System.Drawing.Color getColor() => SetColorOperator.ToSystemColor(new[] { C, M, Y, K });
}

/// <summary>K — Set CMYK stroke color.</summary>
public sealed class SetCMYKColorStroke : BasicSetColorOperator
{
    public new double C { get; set; }
    public new double M { get; set; }
    public new double Y { get; set; }
    public new double K { get; set; }
    public SetCMYKColorStroke(double c, double m, double y, double k) { C = c; M = m; Y = y; K = k; }
    public override string ToPdf() => $"{FmtColor(C)} {FmtColor(M)} {FmtColor(Y)} {FmtColor(K)} K";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
    public override System.Drawing.Color getColor() => SetColorOperator.ToSystemColor(new[] { C, M, Y, K });
}

/// <summary>cs — Set color space for non-stroking operations.</summary>
public sealed class SetColorSpace : Operator
{
    public string Name { get; set; }
    public SetColorSpace(string name) { Name = name; }
    public override string ToPdf() => $"/{Name} cs";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>CS — Set color space for stroking operations.</summary>
public sealed class SetColorSpaceStroke : Operator
{
    public string Name { get; set; }
    public SetColorSpaceStroke(string name) { Name = name; }
    public override string ToPdf() => $"/{Name} CS";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>sc — Set color in current color space (non-stroking).</summary>
public sealed class SetColor : SetColorOperator
{
    public double[] Color { get; private set; }
    public SetColor() { Color = Array.Empty<double>(); }
    public SetColor(double g) { Color = new[] { g }; }
    public SetColor(double r, double g, double b) { Color = new[] { r, g, b }; }
    public SetColor(double c, double m, double y, double k) { Color = new[] { c, m, y, k }; }
    public SetColor(double[] color) { Color = color ?? Array.Empty<double>(); }
    public override string ToPdf()
    {
        var sb = new StringBuilder();
        foreach (var c in Color) sb.Append(Fmt(c)).Append(' ');
        sb.Append("sc");
        return sb.ToString();
    }
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);

    public double R { get => Color.Length >= 3 ? Color[0] : 0; set => SetSlotRgb(0, value); }
    public double G { get => Color.Length == 1 ? Color[0] : (Color.Length >= 3 ? Color[1] : 0); set => SetSlotG(value); }
    public double B { get => Color.Length >= 3 ? Color[2] : 0; set => SetSlotRgb(2, value); }
    public double C { get => Color.Length == 4 ? Color[0] : 0; set => SetSlotCmyk(0, value); }
    public double M { get => Color.Length == 4 ? Color[1] : 0; set => SetSlotCmyk(1, value); }
    public double Y { get => Color.Length == 4 ? Color[2] : 0; set => SetSlotCmyk(2, value); }
    public double K { get => Color.Length == 4 ? Color[3] : 0; set => SetSlotCmyk(3, value); }

    public override System.Drawing.Color getColor() => SetColorOperator.ToSystemColor(Color);

    private void SetSlotRgb(int idx, double v) { if (Color.Length < 3) Color = new double[3]; Color[idx] = v; }
    private void SetSlotCmyk(int idx, double v) { if (Color.Length < 4) Color = new double[4]; Color[idx] = v; }
    private void SetSlotG(double v)
    {
        if (Color.Length == 0) Color = new double[1];
        if (Color.Length == 1) Color[0] = v;
        else if (Color.Length >= 3) Color[1] = v;
    }
}

/// <summary>SC — Set color in current color space (stroking).</summary>
public sealed class SetColorStroke : SetColorOperator
{
    public double[] Color { get; private set; }
    public SetColorStroke() { Color = Array.Empty<double>(); }
    public SetColorStroke(double g) { Color = new[] { g }; }
    public SetColorStroke(double r, double g, double b) { Color = new[] { r, g, b }; }
    public SetColorStroke(double c, double m, double y, double k) { Color = new[] { c, m, y, k }; }
    public SetColorStroke(double[] color) { Color = color ?? Array.Empty<double>(); }
    public override string ToPdf()
    {
        var sb = new StringBuilder();
        foreach (var c in Color) sb.Append(Fmt(c)).Append(' ');
        sb.Append("SC");
        return sb.ToString();
    }
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);

    public double R { get => Color.Length >= 3 ? Color[0] : 0; set => SetSlotRgb(0, value); }
    public double G { get => Color.Length == 1 ? Color[0] : (Color.Length >= 3 ? Color[1] : 0); set => SetSlotG(value); }
    public double B { get => Color.Length >= 3 ? Color[2] : 0; set => SetSlotRgb(2, value); }
    public double C { get => Color.Length == 4 ? Color[0] : 0; set => SetSlotCmyk(0, value); }
    public double M { get => Color.Length == 4 ? Color[1] : 0; set => SetSlotCmyk(1, value); }
    public double Y { get => Color.Length == 4 ? Color[2] : 0; set => SetSlotCmyk(2, value); }
    public double K { get => Color.Length == 4 ? Color[3] : 0; set => SetSlotCmyk(3, value); }

    public override System.Drawing.Color getColor() => SetColorOperator.ToSystemColor(Color);

    private void SetSlotRgb(int idx, double v) { if (Color.Length < 3) Color = new double[3]; Color[idx] = v; }
    private void SetSlotCmyk(int idx, double v) { if (Color.Length < 4) Color = new double[4]; Color[idx] = v; }
    private void SetSlotG(double v)
    {
        if (Color.Length == 0) Color = new double[1];
        if (Color.Length == 1) Color[0] = v;
        else if (Color.Length >= 3) Color[1] = v;
    }
}

/// <summary>scn — Set color (with optional pattern name) in current color space (non-stroking).</summary>
public sealed class SetAdvancedColor : BasicSetColorAndPatternOperator
{
    public double[] Color { get; }
    public new string? PatternName { get; }
    public SetAdvancedColor() { Color = Array.Empty<double>(); }
    public SetAdvancedColor(double g) { Color = new[] { g }; }
    public SetAdvancedColor(string patternName) { Color = Array.Empty<double>(); PatternName = patternName; }
    public SetAdvancedColor(double g, string patternName)
    { Color = new[] { g }; PatternName = patternName; }
    public SetAdvancedColor(double[] colors, string patternName)
    { Color = colors ?? Array.Empty<double>(); PatternName = patternName; }
    public SetAdvancedColor(double r, double g, double b, string patternName)
    { Color = new[] { r, g, b }; PatternName = patternName; }
    public SetAdvancedColor(double c, double m, double y, double k, string patternName)
    { Color = new[] { c, m, y, k }; PatternName = patternName; }
    public override string ToPdf()
    {
        var sb = new StringBuilder();
        foreach (var c in Color) sb.Append(Fmt(c)).Append(' ');
        if (PatternName is not null) sb.Append('/').Append(PatternName).Append(' ');
        sb.Append("scn");
        return sb.ToString();
    }
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
    public override System.Drawing.Color getColor() => SetColorOperator.ToSystemColor(Color);
}

/// <summary>SCN — Set color (with optional pattern name) in current color space (stroking).</summary>
public sealed class SetAdvancedColorStroke : BasicSetColorAndPatternOperator
{
    public double[] Color { get; }
    public new string? PatternName { get; }
    public SetAdvancedColorStroke() { Color = Array.Empty<double>(); }
    public SetAdvancedColorStroke(double g) { Color = new[] { g }; }
    public SetAdvancedColorStroke(string patternName) { Color = Array.Empty<double>(); PatternName = patternName; }
    public SetAdvancedColorStroke(double g, string patternName)
    { Color = new[] { g }; PatternName = patternName; }
    public SetAdvancedColorStroke(double[] colors, string patternName)
    { Color = colors ?? Array.Empty<double>(); PatternName = patternName; }
    public SetAdvancedColorStroke(double r, double g, double b, string patternName)
    { Color = new[] { r, g, b }; PatternName = patternName; }
    public SetAdvancedColorStroke(double c, double m, double y, double k, string patternName)
    { Color = new[] { c, m, y, k }; PatternName = patternName; }
    public override string ToPdf()
    {
        var sb = new StringBuilder();
        foreach (var c in Color) sb.Append(Fmt(c)).Append(' ');
        if (PatternName is not null) sb.Append('/').Append(PatternName).Append(' ');
        sb.Append("SCN");
        return sb.ToString();
    }
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
    public override System.Drawing.Color getColor() => SetColorOperator.ToSystemColor(Color);
}

/// <summary>ri — Set color rendering intent.</summary>
public sealed class SetColorRenderingIntent : Operator
{
    public string RenderingIntent { get; set; }
    /// <summary>Public-API-shape alias for <see cref="RenderingIntent"/>.</summary>
    public string IntentName { get => RenderingIntent; set => RenderingIntent = value; }
    public SetColorRenderingIntent() { RenderingIntent = "RelativeColorimetric"; }
    public SetColorRenderingIntent(string intentName) { RenderingIntent = intentName; }
    public override string ToPdf() => $"/{RenderingIntent} ri";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

// =====================================================================
// Text-state operators (PDF 32000-1 §9.3).
// =====================================================================
