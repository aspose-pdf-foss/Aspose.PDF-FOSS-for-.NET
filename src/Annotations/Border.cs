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
    /// <summary>Border width in points. When this border is bound to an annotation
    /// (returned by <c>annotation.Border</c>), reads and writes pass through to the
    /// annotation's /BS and /Border entries so e.g. <c>annot.Border.Width = 0</c> persists.</summary>
    public int Width
    {
        get => Owner is Annotation a ? a.GetBorderWidthValue() : _width;
        set { _width = value; if (Owner is Annotation a) a.SetBorderWidthValue(value); }
    }
    private int _width = 1;

    /// <summary>Dash pattern for dashed borders. Bound to the annotation's /BS /D
    /// entry when this border belongs to an annotation, so a dash set after
    /// <c>annotation.Border = border</c> still persists.</summary>
    public Dash? Dash
    {
        get => Owner is Annotation a
            ? (a.GetBorderDashValue() is { } p ? new Dash(p) : _dash)
            : _dash;
        set { _dash = value; if (Owner is Annotation a) a.SetBorderDashValue(value?.Pattern); }
    }
    private Dash? _dash;

    /// <summary>Border style. Bound to the annotation's /BS /S entry when this border
    /// belongs to an annotation (mirrors <see cref="Width"/>).</summary>
    public BorderStyle Style
    {
        get => Owner is Annotation a ? a.GetBorderStyleValue() : _style;
        set { _style = value; if (Owner is Annotation a) a.SetBorderStyleValue(value); }
    }
    private BorderStyle _style = BorderStyle.Solid;

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
