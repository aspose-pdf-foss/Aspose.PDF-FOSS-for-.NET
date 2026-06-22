namespace Aspose.Pdf.Annotations;

/// <summary>Horizontal text alignment for annotation text boxes.</summary>
public enum TextAlignment
{
    Left,
    Center,
    Right,
}

/// <summary>Justification of a free-text annotation's content.</summary>
public enum Justification
{
    Left,
    Center,
    Right,
}

/// <summary>Intent (sub-purpose) of a free-text annotation.</summary>
public enum FreeTextIntent
{
    /// <summary>Plain typewriter free text.</summary>
    FreeTextTypeWriter,

    /// <summary>Free text used as a callout (text + leader line + endpoint).</summary>
    FreeTextCallout,

    /// <summary>Intent missing or unrecognised.</summary>
    Undefined,
}

/// <summary>Rich-text run styles applied via
/// <see cref="FreeTextAnnotation.SetTextStyle(RichTextFontStyles, string, double, System.Drawing.Color)"/>.</summary>
[System.Flags]
public enum RichTextFontStyles
{
    /// <summary>No styles applied; clears existing styles when used as the only flag.</summary>
    ClearExisting = 0,

    /// <summary>Bold weight.</summary>
    Bold = 1 << 0,

    /// <summary>Italic style.</summary>
    Italic = 1 << 1,

    /// <summary>Underline decoration.</summary>
    Underline = 1 << 2,
}

/// <summary>Bundled font / colour / alignment style applied to a free-text
/// annotation's rich text.</summary>
public class TextStyle
{
    /// <summary>Font name (e.g. "Helvetica").</summary>
    public string FontName { get; set; } = "Helvetica";

    /// <summary>Font size in points.</summary>
    public double FontSize { get; set; } = 12;

    /// <summary>Foreground colour.</summary>
    public System.Drawing.Color Color { get; set; } = System.Drawing.Color.Black;

    /// <summary>Annotation-specific text alignment.</summary>
    public TextAlignment Alignment { get; set; } = TextAlignment.Left;

    /// <summary>Top-level horizontal alignment (the Aspose.PDF for .NET surface
    /// exposes both <see cref="Alignment"/> and this one; they're not
    /// linked — callers may set each independently).</summary>
    public Aspose.Pdf.HorizontalAlignment HorizontalAlignment { get; set; }
        = Aspose.Pdf.HorizontalAlignment.Left;

    /// <inheritdoc />
    public override string ToString()
        => $"TextStyle({FontName} {FontSize:G} {Color.Name} {Alignment})";
}
