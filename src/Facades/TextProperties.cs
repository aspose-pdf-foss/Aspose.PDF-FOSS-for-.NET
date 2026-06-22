namespace Aspose.Pdf.Facades;

/// <summary>Text-display properties consumed by the *TextOperator family.
/// Stored only — the FOSS renderer reads TextState directly from operators.</summary>
public class TextProperties
{
    public TextProperties(double textSize)
    {
        TextSize = textSize;
        IsTextSizeSpecified = true;
    }

    private System.Drawing.Color _color;
    private bool _colorSet;
    private double _textSize;
    private bool _textSizeSet;

    /// <summary>Text colour. Setting marks <see cref="IsColorSpecified"/>.</summary>
    public System.Drawing.Color Color
    {
        get => _color;
        set { _color = value; _colorSet = true; }
    }

    /// <summary>Whether <see cref="Color"/> has been explicitly set.</summary>
    public bool IsColorSpecified => _colorSet;

    /// <summary>Text size in points. Setting marks <see cref="IsTextSizeSpecified"/>.</summary>
    public double TextSize
    {
        get => _textSize;
        set { _textSize = value; _textSizeSet = true; }
    }

    /// <summary>Whether <see cref="TextSize"/> has been explicitly set.</summary>
    public bool IsTextSizeSpecified
    {
        get => _textSizeSet;
        private set => _textSizeSet = value;
    }
}
