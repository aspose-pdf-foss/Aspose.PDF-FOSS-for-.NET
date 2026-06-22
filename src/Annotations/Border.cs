namespace Aspose.Pdf.Annotations;

/// <summary>
/// Represents a dash pattern for borders.
/// </summary>
public sealed class Dash
{
    /// <summary>Length of the dash.</summary>
    public int On { get; set; }

    /// <summary>Length of the gap.</summary>
    public int Off { get; set; }

    /// <summary>Raw dash-pattern array (alternating on/off lengths).</summary>
    public int[] Pattern { get; }

    public Dash(int on, int off)
    {
        On = on;
        Off = off;
        Pattern = new[] { on, off };
    }

    /// <summary>Construct from an explicit dash pattern (on/off pairs).</summary>
    public Dash(int[] pattern)
    {
        Pattern = pattern ?? System.Array.Empty<int>();
        On = Pattern.Length > 0 ? Pattern[0] : 0;
        Off = Pattern.Length > 1 ? Pattern[1] : 0;
    }
}

/// <summary>
/// Represents the border of an annotation or field widget.
/// Corresponds to the /BS dictionary entry in PDF spec.
/// </summary>
public sealed class Border
{
    /// <summary>Border width in points.</summary>
    public int Width { get; set; } = 1;

    /// <summary>Dash pattern for dashed borders.</summary>
    public Dash? Dash { get; set; }

    /// <summary>Border style.</summary>
    public BorderStyle Style { get; set; } = BorderStyle.Solid;

    /// <summary>Border effect (cloudy, none).</summary>
    public BorderEffect Effect { get; set; } = BorderEffect.None;

    /// <summary>Intensity of the border effect.</summary>
    public int EffectIntensity { get; set; }

    /// <summary>Horizontal corner radius for rounded rectangle borders.</summary>
    public double HCornerRadius { get; set; }

    /// <summary>Vertical corner radius for rounded rectangle borders.</summary>
    public double VCornerRadius { get; set; }

    /// <summary>The annotation this border belongs to (may be null).</summary>
    internal object? Owner { get; }

    public Border(object? owner = null) { Owner = owner; }

    /// <summary>Construct a border bound to <paramref name="parent"/>.</summary>
    public Border(Annotation parent) : this((object?)parent) { }
}

/// <summary>
/// Border style for annotations and fields.
/// </summary>
public enum BorderStyle
{
    Solid,
    Dashed,
    Beveled,
    Inset,
    Underline,
}

/// <summary>
/// Border effect applied to annotations.
/// </summary>
public enum BorderEffect
{
    None,
    Cloudy,
}
