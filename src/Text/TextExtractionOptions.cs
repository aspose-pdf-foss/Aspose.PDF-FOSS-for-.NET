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
        ScaleFactor = 1.0;
    }

    /// <summary>The text formatting mode.</summary>
    public TextFormattingMode FormattingMode { get; set; }

    /// <summary>Scale factor for the Pure-mode character grid: the grid cell is
    /// <c>ScaleFactor · 0.6 · (F − 2)</c> with F the page's dominant (ceiled)
    /// font size. The default is 1. Setting 0 lets the algorithm pick the cell
    /// automatically from the page's measured mean glyph advance (a tighter
    /// grid for dense multi-column text).</summary>
    public double ScaleFactor { get; set; }

    /// <summary>When set, tolerate a malformed content stream that shows text with no
    /// font in effect (no preceding <c>Tf</c>) instead of throwing
    /// <see cref="Aspose.Pdf.IncorrectFontUsageException"/>. Mirrors the same flag on
    /// <see cref="TextSearchOptions.IgnoreResourceFontErrors"/>; the
    /// <see cref="TextDevice"/> path reads it from here.</summary>
    public bool IgnoreResourceFontErrors { get; set; }
}
