namespace Aspose.Pdf;

/// <summary>
/// Stroke / fill settings for drawable shapes and table-cell borders.
/// </summary>
public sealed class GraphInfo
{
    public Color? Color { get; set; }
    public Color? FillColor { get; set; }

    public int[]? DashArray { get; set; }
    public int DashPhase { get; set; }

    public float LineWidth { get; set; } = 1f;

    public bool IsDoubled { get; set; }

    public double RotationAngle { get; set; }
    public double ScalingRateX { get; set; } = 1.0;
    public double ScalingRateY { get; set; } = 1.0;
    public double SkewAngleX { get; set; }
    public double SkewAngleY { get; set; }

    public double X { get; internal set; }
    public double Y { get; internal set; }

    internal Drawing.Color? StrokeColor
    {
        get => Color is { } c ? (Drawing.Color)c : null;
        set => Color = value is { } v ? Aspose.Pdf.Color.FromRgb((int)(v.R * 255), (int)(v.G * 255), (int)(v.B * 255)) : null;
    }

    internal Drawing.Color? FillColorInternal
    {
        get => FillColor is { } c ? (Drawing.Color)c : null;
        set => FillColor = value is { } v ? Aspose.Pdf.Color.FromRgb((int)(v.R * 255), (int)(v.G * 255), (int)(v.B * 255)) : null;
    }

    // Opacity is carried by the alpha channel of the fill/stroke colours: a colour
    // built with Color.FromArgb(a, ...) (or from a System.Drawing.Color with alpha)
    // renders through an ExtGState /ca or /CA. Opaque colours (A == 1) emit no gs.
    // An explicitly assigned opacity wins over the colour-derived value.
    private double? _fillOpacity;
    private double? _strokeOpacity;
    internal double FillOpacity { get => _fillOpacity ?? FillColor?.A ?? 1.0; set => _fillOpacity = value; }
    internal double StrokeOpacity { get => _strokeOpacity ?? Color?.A ?? 1.0; set => _strokeOpacity = value; }

    internal double[]? DashPattern
    {
        get => DashArray?.Select(d => (double)d).ToArray();
        set => DashArray = value?.Select(d => (int)d).ToArray();
    }

    public object Clone() => MemberwiseClone();
}
