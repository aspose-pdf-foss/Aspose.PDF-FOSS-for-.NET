namespace Aspose.Pdf.Text;

/// <summary>
/// Options for text extraction operations.
/// </summary>
public sealed class TextExtractionOptions
{
    /// <summary>Specifies how extracted text is formatted.</summary>
    public enum TextFormattingMode
    {
        /// <summary>Pure text without formatting.</summary>
        Pure,
        /// <summary>Raw text as it appears in the content stream.</summary>
        Raw,
        /// <summary>Attempts to preserve the original document flow.</summary>
        Flatten,
        /// <summary>Memory-saving mode for large documents.</summary>
        MemorySaving,
    }

    /// <summary>
    /// Initializes a new instance of <see cref="TextExtractionOptions"/>.
    /// </summary>
    public TextExtractionOptions(TextFormattingMode formattingMode = TextFormattingMode.Pure)
    {
        FormattingMode = formattingMode;
    }

    /// <summary>The text formatting mode.</summary>
    public TextFormattingMode FormattingMode { get; set; }

    /// <summary>Scale factor used by the column-detection step. 0 lets the algorithm
    /// pick automatically; values such as 0.5 force a tighter split. Stored only;
    /// the absorber currently treats it as a hint.</summary>
    public double ScaleFactor { get; set; }

    /// <summary>When set, tolerate a malformed content stream that shows text with no
    /// font in effect (no preceding <c>Tf</c>) instead of throwing
    /// <see cref="Aspose.Pdf.IncorrectFontUsageException"/>. Mirrors the same flag on
    /// <see cref="TextSearchOptions.IgnoreResourceFontErrors"/>; the
    /// <see cref="TextDevice"/> path reads it from here.</summary>
    public bool IgnoreResourceFontErrors { get; set; }
}
