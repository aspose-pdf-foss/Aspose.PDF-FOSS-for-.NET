// PDF content stream operators — PDF32000_2008 §8–9
using System.Globalization;
using System.Text;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Operators;

/// <summary>rg — Set RGB fill color.</summary>
public sealed class SetRGBColor : Operator
{
    /// <summary>Gets or sets the red component of the fill color, from 0 (none) to 1 (full).</summary>
    public double R { get; set; }
    /// <summary>Gets or sets the green component of the fill color, from 0 (none) to 1 (full).</summary>
    public double G { get; set; }
    /// <summary>Gets or sets the blue component of the fill color, from 0 (none) to 1 (full).</summary>
    public double B { get; set; }

    /// <summary>Creates an <c>rg</c> operator that sets the fill color to the given red, green and blue components, each from 0 (none) to 1 (full).</summary>
    public SetRGBColor(double r, double g, double b) { R = r; G = g; B = b; }
    /// <summary>Creates an <c>rg</c> operator from a <c>System.Drawing.Color</c>: each 0-255 channel is divided by 255, and the alpha channel is ignored.</summary>
    public SetRGBColor(System.Drawing.Color color)
        : this(color.R / 255.0, color.G / 255.0, color.B / 255.0) { }
    public override string ToPdf() => $"{FmtColor(R)} {FmtColor(G)} {FmtColor(B)} rg";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
    /// <summary>Returns the fill color as a <c>System.Drawing.Color</c>, with each component scaled to 0-255 and clamped.</summary>
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
            // The operator reports the reference's own profile answer (sampled on an
            // 11-level grid): a CMYK black (K=1) is the rich (35, 31, 32), not (0, 0, 0),
            // and (0.1, 0.2, 0.2, 1) is (31, 23, 21).
            var (r, g, b) = CmykOperatorColors.Convert(color[0], color[1], color[2], color[3]);
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
    /// <summary>Gets the color components. Always an empty array when read through this base type; the concrete operator (for example <c>SetGray</c>) exposes its own value.</summary>
    public virtual double[] Color => Array.Empty<double>();
    /// <summary>Gets the gray level. Always 0 when read through this base type; the concrete operator (for example <c>SetGray</c>) exposes its own value.</summary>
    public virtual double Gray => 0;
    /// <summary>Gets the red component. Always 0 when read through this base type; the concrete operator (for example <c>SetRGBColor</c>) exposes its own value.</summary>
    public virtual double R => 0;
    /// <summary>Gets the green component. Always 0 when read through this base type; the concrete operator (for example <c>SetRGBColor</c>) exposes its own value.</summary>
    public virtual double G => 0;
    /// <summary>Gets the blue component. Always 0 when read through this base type; the concrete operator (for example <c>SetRGBColor</c>) exposes its own value.</summary>
    public virtual double B => 0;
    /// <summary>Gets the cyan component. Always 0 when read through this base type; the concrete operator (for example <c>SetCMYKColor</c>) exposes its own value.</summary>
    public virtual double C => 0;
    /// <summary>Gets the magenta component. Always 0 when read through this base type; the concrete operator (for example <c>SetCMYKColor</c>) exposes its own value.</summary>
    public virtual double M => 0;
    /// <summary>Gets the yellow component. Always 0 when read through this base type; the concrete operator (for example <c>SetCMYKColor</c>) exposes its own value.</summary>
    public virtual double Y => 0;
    /// <summary>Gets the black component. Always 0 when read through this base type; the concrete operator (for example <c>SetCMYKColor</c>) exposes its own value.</summary>
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
    /// <summary>Gets or sets the gray level of the fill color, from 0 (black) to 1 (white).</summary>
    public new double Gray { get; set; }
    /// <summary>Creates a <c>g</c> operator that sets the fill color to the given gray level, from 0 (black) to 1 (white).</summary>
    public SetGray(double gray) { Gray = gray; }
    public override string ToPdf() => $"{FmtColor(Gray)} g";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
    public override System.Drawing.Color getColor() => SetColorOperator.ToSystemColor(new[] { Gray });
}

/// <summary>G — Set gray stroke color.</summary>
public sealed class SetGrayStroke : BasicSetColorOperator
{
    /// <summary>Gets or sets the gray level of the stroke color, from 0 (black) to 1 (white).</summary>
    public new double Gray { get; set; }
    /// <summary>Creates a <c>G</c> operator that sets the stroke color to the given gray level, from 0 (black) to 1 (white).</summary>
    public SetGrayStroke(double gray) { Gray = gray; }
    public override string ToPdf() => $"{FmtColor(Gray)} G";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
    public override System.Drawing.Color getColor() => SetColorOperator.ToSystemColor(new[] { Gray });
}

/// <summary>RG — Set RGB stroke color.</summary>
public sealed class SetRGBColorStroke : BasicSetColorOperator
{
    /// <summary>Gets or sets the red component of the stroke color, from 0 (none) to 1 (full).</summary>
    public new double R { get; set; }
    /// <summary>Gets or sets the green component of the stroke color, from 0 (none) to 1 (full).</summary>
    public new double G { get; set; }
    /// <summary>Gets or sets the blue component of the stroke color, from 0 (none) to 1 (full).</summary>
    public new double B { get; set; }
    /// <summary>Creates an <c>RG</c> operator that sets the stroke color to the given red, green and blue components, each from 0 (none) to 1 (full).</summary>
    public SetRGBColorStroke(double r, double g, double b) { R = r; G = g; B = b; }
    /// <summary>Creates an <c>RG</c> operator from a <c>System.Drawing.Color</c>: each 0-255 channel is divided by 255, and the alpha channel is ignored.</summary>
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
    /// <summary>Gets or sets the cyan component of the fill color, from 0 to 1.</summary>
    public new double C { get; set; }
    /// <summary>Gets or sets the magenta component of the fill color, from 0 to 1.</summary>
    public new double M { get; set; }
    /// <summary>Gets or sets the yellow component of the fill color, from 0 to 1.</summary>
    public new double Y { get; set; }
    /// <summary>Gets or sets the black component of the fill color, from 0 to 1.</summary>
    public new double K { get; set; }
    /// <summary>Creates a <c>k</c> operator that sets the fill color to the given cyan, magenta, yellow and black components, each from 0 to 1.</summary>
    public SetCMYKColor(double c, double m, double y, double k) { C = c; M = m; Y = y; K = k; }
    public override string ToPdf() => $"{FmtColor(C)} {FmtColor(M)} {FmtColor(Y)} {FmtColor(K)} k";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
    public override System.Drawing.Color getColor() => SetColorOperator.ToSystemColor(new[] { C, M, Y, K });
}

/// <summary>K — Set CMYK stroke color.</summary>
public sealed class SetCMYKColorStroke : BasicSetColorOperator
{
    /// <summary>Gets or sets the cyan component of the stroke color, from 0 to 1.</summary>
    public new double C { get; set; }
    /// <summary>Gets or sets the magenta component of the stroke color, from 0 to 1.</summary>
    public new double M { get; set; }
    /// <summary>Gets or sets the yellow component of the stroke color, from 0 to 1.</summary>
    public new double Y { get; set; }
    /// <summary>Gets or sets the black component of the stroke color, from 0 to 1.</summary>
    public new double K { get; set; }
    /// <summary>Creates a <c>K</c> operator that sets the stroke color to the given cyan, magenta, yellow and black components, each from 0 to 1.</summary>
    public SetCMYKColorStroke(double c, double m, double y, double k) { C = c; M = m; Y = y; K = k; }
    public override string ToPdf() => $"{FmtColor(C)} {FmtColor(M)} {FmtColor(Y)} {FmtColor(K)} K";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
    public override System.Drawing.Color getColor() => SetColorOperator.ToSystemColor(new[] { C, M, Y, K });
}

/// <summary>cs — Set color space for non-stroking operations.</summary>
public sealed class SetColorSpace : Operator
{
    /// <summary>Gets or sets the color space name, written without the leading slash.</summary>
    public string Name { get; set; }
    /// <summary>Creates a <c>cs</c> operator that selects the named color space for filling: a device space such as <c>DeviceRGB</c> or a color space resource name, without the leading slash.</summary>
    public SetColorSpace(string name) { Name = name; }
    public override string ToPdf() => $"/{Name} cs";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>CS — Set color space for stroking operations.</summary>
public sealed class SetColorSpaceStroke : Operator
{
    /// <summary>Gets or sets the color space name, written without the leading slash.</summary>
    public string Name { get; set; }
    /// <summary>Creates a <c>CS</c> operator that selects the named color space for stroking: a device space such as <c>DeviceRGB</c> or a color space resource name, without the leading slash.</summary>
    public SetColorSpaceStroke(string name) { Name = name; }
    public override string ToPdf() => $"/{Name} CS";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>sc — Set color in current color space (non-stroking).</summary>
public sealed class SetColor : SetColorOperator
{
    /// <summary>Gets the fill color components, interpreted in the current fill color space.</summary>
    public double[] Color { get; private set; }
    /// <summary>Creates an <c>sc</c> operator with no color components.</summary>
    public SetColor() { Color = Array.Empty<double>(); }
    /// <summary>Creates an <c>sc</c> operator with one component, such as a gray level.</summary>
    public SetColor(double g) { Color = new[] { g }; }
    /// <summary>Creates an <c>sc</c> operator with three components, such as red, green and blue.</summary>
    public SetColor(double r, double g, double b) { Color = new[] { r, g, b }; }
    /// <summary>Creates an <c>sc</c> operator with four components, such as cyan, magenta, yellow and black.</summary>
    public SetColor(double c, double m, double y, double k) { Color = new[] { c, m, y, k }; }
    /// <summary>Creates an <c>sc</c> operator with the given components; a null array gives no components.</summary>
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

    /// <summary>Gets or sets the red component (the first of three); reads 0 unless the color has at least three components. Setting it on a color with fewer than three components replaces the color with three zeroed components first.</summary>
    public double R { get => Color.Length >= 3 ? Color[0] : 0; set => SetSlotRgb(0, value); }
    /// <summary>Gets or sets the green component (the second of three), or the gray level when the color has a single component; reads 0 otherwise. Setting it on an empty color creates a one-component gray.</summary>
    public double G { get => Color.Length == 1 ? Color[0] : (Color.Length >= 3 ? Color[1] : 0); set => SetSlotG(value); }
    /// <summary>Gets or sets the blue component (the third of three); reads 0 unless the color has at least three components. Setting it on a color with fewer than three components replaces the color with three zeroed components first.</summary>
    public double B { get => Color.Length >= 3 ? Color[2] : 0; set => SetSlotRgb(2, value); }
    /// <summary>Gets or sets the cyan component (the first of four); reads 0 unless the color has exactly four components. Setting it on a color with fewer than four components replaces the color with four zeroed components first.</summary>
    public double C { get => Color.Length == 4 ? Color[0] : 0; set => SetSlotCmyk(0, value); }
    /// <summary>Gets or sets the magenta component (the second of four); reads 0 unless the color has exactly four components. Setting it on a color with fewer than four components replaces the color with four zeroed components first.</summary>
    public double M { get => Color.Length == 4 ? Color[1] : 0; set => SetSlotCmyk(1, value); }
    /// <summary>Gets or sets the yellow component (the third of four); reads 0 unless the color has exactly four components. Setting it on a color with fewer than four components replaces the color with four zeroed components first.</summary>
    public double Y { get => Color.Length == 4 ? Color[2] : 0; set => SetSlotCmyk(2, value); }
    /// <summary>Gets or sets the black component (the fourth of four); reads 0 unless the color has exactly four components. Setting it on a color with fewer than four components replaces the color with four zeroed components first.</summary>
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
    /// <summary>Gets the stroke color components, interpreted in the current stroke color space.</summary>
    public double[] Color { get; private set; }
    /// <summary>Creates an <c>SC</c> operator with no color components.</summary>
    public SetColorStroke() { Color = Array.Empty<double>(); }
    /// <summary>Creates an <c>SC</c> operator with one component, such as a gray level.</summary>
    public SetColorStroke(double g) { Color = new[] { g }; }
    /// <summary>Creates an <c>SC</c> operator with three components, such as red, green and blue.</summary>
    public SetColorStroke(double r, double g, double b) { Color = new[] { r, g, b }; }
    /// <summary>Creates an <c>SC</c> operator with four components, such as cyan, magenta, yellow and black.</summary>
    public SetColorStroke(double c, double m, double y, double k) { Color = new[] { c, m, y, k }; }
    /// <summary>Creates an <c>SC</c> operator with the given components; a null array gives no components.</summary>
    public SetColorStroke(double[] color) { Color = color ?? Array.Empty<double>(); }
    public override string ToPdf()
    {
        var sb = new StringBuilder();
        foreach (var c in Color) sb.Append(Fmt(c)).Append(' ');
        sb.Append("SC");
        return sb.ToString();
    }
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);

    /// <summary>Gets or sets the red component (the first of three); reads 0 unless the color has at least three components. Setting it on a color with fewer than three components replaces the color with three zeroed components first.</summary>
    public double R { get => Color.Length >= 3 ? Color[0] : 0; set => SetSlotRgb(0, value); }
    /// <summary>Gets or sets the green component (the second of three), or the gray level when the color has a single component; reads 0 otherwise. Setting it on an empty color creates a one-component gray.</summary>
    public double G { get => Color.Length == 1 ? Color[0] : (Color.Length >= 3 ? Color[1] : 0); set => SetSlotG(value); }
    /// <summary>Gets or sets the blue component (the third of three); reads 0 unless the color has at least three components. Setting it on a color with fewer than three components replaces the color with three zeroed components first.</summary>
    public double B { get => Color.Length >= 3 ? Color[2] : 0; set => SetSlotRgb(2, value); }
    /// <summary>Gets or sets the cyan component (the first of four); reads 0 unless the color has exactly four components. Setting it on a color with fewer than four components replaces the color with four zeroed components first.</summary>
    public double C { get => Color.Length == 4 ? Color[0] : 0; set => SetSlotCmyk(0, value); }
    /// <summary>Gets or sets the magenta component (the second of four); reads 0 unless the color has exactly four components. Setting it on a color with fewer than four components replaces the color with four zeroed components first.</summary>
    public double M { get => Color.Length == 4 ? Color[1] : 0; set => SetSlotCmyk(1, value); }
    /// <summary>Gets or sets the yellow component (the third of four); reads 0 unless the color has exactly four components. Setting it on a color with fewer than four components replaces the color with four zeroed components first.</summary>
    public double Y { get => Color.Length == 4 ? Color[2] : 0; set => SetSlotCmyk(2, value); }
    /// <summary>Gets or sets the black component (the fourth of four); reads 0 unless the color has exactly four components. Setting it on a color with fewer than four components replaces the color with four zeroed components first.</summary>
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
    /// <summary>Gets the fill color components, written before the optional pattern name.</summary>
    public double[] Color { get; }
    public new string? PatternName { get; }
    /// <summary>Creates an <c>scn</c> operator with no color components and no pattern.</summary>
    public SetAdvancedColor() { Color = Array.Empty<double>(); }
    /// <summary>Creates an <c>scn</c> operator with one component, such as a gray level, and no pattern.</summary>
    public SetAdvancedColor(double g) { Color = new[] { g }; }
    /// <summary>Creates an <c>scn</c> operator that selects the named pattern resource (without the leading slash) with no color components.</summary>
    public SetAdvancedColor(string patternName) { Color = Array.Empty<double>(); PatternName = patternName; }
    /// <summary>Creates an <c>scn</c> operator with one component and the named pattern resource (without the leading slash); a null name writes no pattern.</summary>
    public SetAdvancedColor(double g, string patternName)
    { Color = new[] { g }; PatternName = patternName; }
    /// <summary>Creates an <c>scn</c> operator with the given components (a null array gives none) and the named pattern resource (without the leading slash); a null name writes no pattern.</summary>
    public SetAdvancedColor(double[] colors, string patternName)
    { Color = colors ?? Array.Empty<double>(); PatternName = patternName; }
    /// <summary>Creates an <c>scn</c> operator with three components, such as red, green and blue, and the named pattern resource (without the leading slash); a null name writes no pattern.</summary>
    public SetAdvancedColor(double r, double g, double b, string patternName)
    { Color = new[] { r, g, b }; PatternName = patternName; }
    /// <summary>Creates an <c>scn</c> operator with four components, such as cyan, magenta, yellow and black, and the named pattern resource (without the leading slash); a null name writes no pattern.</summary>
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
    /// <summary>Gets the stroke color components, written before the optional pattern name.</summary>
    public double[] Color { get; }
    public new string? PatternName { get; }
    /// <summary>Creates an <c>SCN</c> operator with no color components and no pattern.</summary>
    public SetAdvancedColorStroke() { Color = Array.Empty<double>(); }
    /// <summary>Creates an <c>SCN</c> operator with one component, such as a gray level, and no pattern.</summary>
    public SetAdvancedColorStroke(double g) { Color = new[] { g }; }
    /// <summary>Creates an <c>SCN</c> operator that selects the named pattern resource (without the leading slash) with no color components.</summary>
    public SetAdvancedColorStroke(string patternName) { Color = Array.Empty<double>(); PatternName = patternName; }
    /// <summary>Creates an <c>SCN</c> operator with one component and the named pattern resource (without the leading slash); a null name writes no pattern.</summary>
    public SetAdvancedColorStroke(double g, string patternName)
    { Color = new[] { g }; PatternName = patternName; }
    /// <summary>Creates an <c>SCN</c> operator with the given components (a null array gives none) and the named pattern resource (without the leading slash); a null name writes no pattern.</summary>
    public SetAdvancedColorStroke(double[] colors, string patternName)
    { Color = colors ?? Array.Empty<double>(); PatternName = patternName; }
    /// <summary>Creates an <c>SCN</c> operator with three components, such as red, green and blue, and the named pattern resource (without the leading slash); a null name writes no pattern.</summary>
    public SetAdvancedColorStroke(double r, double g, double b, string patternName)
    { Color = new[] { r, g, b }; PatternName = patternName; }
    /// <summary>Creates an <c>SCN</c> operator with four components, such as cyan, magenta, yellow and black, and the named pattern resource (without the leading slash); a null name writes no pattern.</summary>
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
    /// <summary>Creates an <c>ri</c> operator with the default rendering intent <c>RelativeColorimetric</c>.</summary>
    public SetColorRenderingIntent() { RenderingIntent = "RelativeColorimetric"; }
    /// <summary>Creates an <c>ri</c> operator with the named rendering intent, such as <c>Perceptual</c> or <c>AbsoluteColorimetric</c>, without the leading slash.</summary>
    public SetColorRenderingIntent(string intentName) { RenderingIntent = intentName; }
    public override string ToPdf() => $"/{RenderingIntent} ri";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

// =====================================================================
// Text-state operators (PDF 32000-1 §9.3).
// =====================================================================
