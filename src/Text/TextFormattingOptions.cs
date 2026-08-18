namespace Aspose.Pdf.Text;

/// <summary>
/// Represents text formatting options for a <see cref="TextParagraph"/>.
/// </summary>
public sealed class TextFormattingOptions
{
    /// <summary>
    /// Word wrap mode that controls how text wraps within a paragraph rectangle.
    /// </summary>
    public enum WordWrapMode
    {
        /// <summary>No wrapping — text may overflow the rectangle.</summary>
        NoWrap,

        /// <summary>Wrap at word boundaries.</summary>
        ByWords,

        /// <summary>Allow discretionary hyphenation when wrapping.</summary>
        DiscretionaryHyphenation,

        /// <summary>Wrap mode not yet resolved — defer to surrounding context.</summary>
        Undefined,
    }

    /// <summary>How line spacing is interpreted (font-derived vs full glyph extent).</summary>
    public enum LineSpacingMode
    {
        FontSize = 0,
        FullSize = 1,
    }

    public TextFormattingOptions() { }

    /// <summary>Construct with an explicit wrap mode.</summary>
    public TextFormattingOptions(WordWrapMode wrapMode) { WrapMode = wrapMode; }

    /// <summary>
    /// Gets or sets the word wrap mode for the paragraph.
    /// Default is <see cref="WordWrapMode.Undefined"/> (the ctor sets the
    /// backing field to Undefined). The
    /// flow layout treats Undefined as "wrap by width" -- callers that want
    /// the no-wrap behaviour have to opt in explicitly.
    /// </summary>
    public WordWrapMode WrapMode { get; set; } = WordWrapMode.Undefined;

    /// <summary>Line-spacing interpretation mode. Default is <see cref="LineSpacingMode.FontSize"/>.</summary>
    public LineSpacingMode LineSpacing { get; set; } = LineSpacingMode.FontSize;

    /// <summary>Line-spacing value in points (internal extension — the public API exposes the mode only).</summary>
    public double LineSpacingPoints { get; set; }

    /// <summary>Indent applied to the first line of the paragraph (points).</summary>
    public float FirstLineIndent { get; set; }

    /// <summary>Indent applied to lines after the first (points).</summary>
    public float SubsequentLinesIndent { get; set; }

    /// <summary>Symbol used at line breaks when hyphenation is in effect (default "-").</summary>
    public string HyphenSymbol { get; set; } = "-";
}
