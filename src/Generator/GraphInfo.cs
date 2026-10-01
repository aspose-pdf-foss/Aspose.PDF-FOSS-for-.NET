namespace Aspose.Pdf;

/// <summary>
/// Stroke / fill settings for drawable shapes and table-cell borders.
/// </summary>
public sealed class GraphInfo
{
    /// <summary>Creates default settings: a solid 1-point line with no stroke or fill colour set.</summary>
    public GraphInfo() { }

    /// <summary>Gets or sets the stroke (line) colour. Its alpha channel sets the stroke opacity.</summary>
    public Color? Color { get; set; }
    /// <summary>Gets or sets the fill colour. Its alpha channel sets the fill opacity; <c>null</c> means no fill.</summary>
    public Color? FillColor { get; set; }

    /// <summary>Gets or sets the dash pattern as whole-point lengths, alternating dash and gap (e.g. <c>{ 3, 2 }</c>). <c>null</c> or empty draws a solid line.</summary>
    public int[]? DashArray { get; set; }
    /// <summary>Gets or sets how far into the dash pattern, in points, the line starts.</summary>
    public int DashPhase { get; set; }

    /// <summary>The dash pattern in points, dash then gap, fractions allowed --
    /// what <see cref="DashArray"/> cannot carry (a 5 pt dash with a 3.33 gap).
    /// Wins over <see cref="DashArray"/> when set.</summary>
    public double[]? DashLengths { get; set; }

    /// <summary>How far into the dash pattern the line starts, in points, fractions
    /// allowed -- what <see cref="DashPhase"/> cannot carry (a dot pattern with a 5 pt
    /// gap starts half a gap in, at 2.5). Wins over <see cref="DashPhase"/> when set.</summary>
    public double? DashOffset { get; set; }

    /// <summary>The dash pattern is fitted to each rule it draws: the gap is
    /// stretched so the rule holds a whole number of dashes, and the pattern is
    /// centred, half a gap at each end. A 200 pt rule dashed 5/3.5 is drawn with
    /// 24 dashes and a 3.33 gap, its phase 6.67. A pattern of a single dash and
    /// gap only; anything else is drawn as given.</summary>
    public bool DashesFitTheLength { get; set; }

    /// <summary>The dashes are drawn with round caps (a dash of length 0 is then a
    /// round dot of the rule's width).</summary>
    public bool RoundDashCaps { get; set; }

    /// <summary>How the rule is built across its width; solid unless told otherwise.</summary>
    public RuleStyle Style { get; set; }

    /// <summary>Gets or sets the line width in points (1/72 inch). Defaults to 1.</summary>
    public float LineWidth { get; set; } = 1f;

    public bool IsDoubled { get; set; }

    public double RotationAngle { get; set; }
    public double ScalingRateX { get; set; } = 1.0;
    public double ScalingRateY { get; set; } = 1.0;
    public double SkewAngleX { get; set; }
    public double SkewAngleY { get; set; }

    /// <summary>Gets the X coordinate. It is read-only and always 0 in this library.</summary>
    public double X { get; internal set; }
    /// <summary>Gets the Y coordinate. It is read-only and always 0 in this library.</summary>
    public double Y { get; internal set; }

    internal Drawing.Color? StrokeColor
    {
        get => Color is { } c ? (Drawing.Color)c : null;
        set => Color = value is { } v ? Aspose.Pdf.Color.FromRgbBytes((int)(v.R * 255), (int)(v.G * 255), (int)(v.B * 255)) : null;
    }

    internal Drawing.Color? FillColorInternal
    {
        get => FillColor is { } c ? (Drawing.Color)c : null;
        set => FillColor = value is { } v ? Aspose.Pdf.Color.FromRgbBytes((int)(v.R * 255), (int)(v.G * 255), (int)(v.B * 255)) : null;
    }

    // Opacity is carried by the alpha channel of the fill/stroke colours: a colour
    // built with Color.FromArgb(a, ...) (or from a System.Drawing.Color with alpha)
    // renders through an ExtGState /ca or /CA. Opaque colours (A == 1) emit no gs.
    // An explicitly assigned opacity wins over the colour-derived value.
    private double? _fillOpacity;
    private double? _strokeOpacity;
    // A pattern-bearing colour (gradient fill) carries no meaningful RGB alpha —
    // its unset colour bytes would read as fully transparent and blank the shading.
    internal double FillOpacity
    {
        get => _fillOpacity ?? (FillColor?.PatternColorSpace is not null ? 1.0 : FillColor?.A ?? 1.0);
        set => _fillOpacity = value;
    }
    internal double StrokeOpacity { get => _strokeOpacity ?? Color?.A ?? 1.0; set => _strokeOpacity = value; }

    internal double[]? DashPattern
    {
        get => DashLengths ?? DashArray?.Select(d => (double)d).ToArray();
        set => DashArray = value?.Select(d => (int)d).ToArray();
    }

    /// <summary>The phase the dash pattern starts at: <see cref="DashOffset"/> when set,
    /// else <see cref="DashPhase"/>.</summary>
    internal double DashStart => DashOffset ?? DashPhase;

    public object Clone() => MemberwiseClone();
}
