namespace Aspose.Pdf.Text;

/// <summary>
/// Options for text search operations in PDF documents.
/// </summary>
public class TextSearchOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TextSearchOptions"/> class.
    /// </summary>
    public TextSearchOptions() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="TextSearchOptions"/> class
    /// with the specified regular expression mode.
    /// </summary>
    /// <param name="isRegularExpressionUsed">When true, the search phrase is treated as a regular expression.</param>
    public TextSearchOptions(bool isRegularExpressionUsed)
    {
        IsRegularExpression = isRegularExpressionUsed;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TextSearchOptions"/> class
    /// constraining the search to a rectangle in page space.
    /// </summary>
    /// <param name="rectangle">Search area; only text fragments inside are returned.</param>
    public TextSearchOptions(Rectangle rectangle)
    {
        Rectangle = rectangle;
    }

    /// <summary>
    /// Initializes a new instance constraining the search to a rectangle and
    /// optionally treating the phrase as a regular expression.
    /// </summary>
    public TextSearchOptions(Rectangle rectangle, bool isRegularExpressionUsed)
    {
        Rectangle = rectangle;
        IsRegularExpression = isRegularExpressionUsed;
    }

    /// <summary>
    /// Gets or sets whether the search phrase is a regular expression.
    /// </summary>
    public bool IsRegularExpression { get; set; }

    /// <summary>
    /// Gets or sets whether the search is case-sensitive. Default is true.
    /// </summary>
    public bool CaseSensitive { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to match whole words only.
    /// </summary>
    public bool WholeWord { get; set; }

    /// <summary>
    /// Gets or sets a rectangle to limit the text search area.
    /// When set, only text fragments within this rectangle are returned.
    /// </summary>
    public Rectangle? Rectangle { get; set; }

    /// <summary>
    /// Gets or sets areas in which text is NOT absorbed. Characters that fall
    /// inside any of these rectangles are dropped and the surrounding run is
    /// split into separate fragments at the excluded region's edges.
    /// </summary>
    public Rectangle[]? ExcludeRectangles { get; set; }

    /// <summary>
    /// Alias for IsRegularExpression (matches the public API).
    /// </summary>
    public bool IsRegularExpressionUsed
    {
        get => IsRegularExpression;
        set => IsRegularExpression = value;
    }

    /// <summary>
    /// Gets or sets whether text search is limited to page boundaries.
    /// When true, text outside the visible page area is excluded.
    /// </summary>
    public bool LimitToPageBounds { get; set; }

    /// <summary>
    /// Gets or sets whether text-search results should also collect graphics
    /// (lines, rectangles, fills) positionally related to the matched text —
    /// for example the underline of a hyperlink or the highlight rectangle
    /// around a phrase. Property is stored only; honouring the flag during
    /// absorption is a separate feature.
    /// </summary>
    public bool SearchForTextRelatedGraphics { get; set; } = true;

    /// <summary>
    /// When true, the absorber suppresses exceptions raised by malformed or
    /// missing font resources during page traversal — the offending text run
    /// is silently skipped instead of bubbling up. The extraction path is
    /// already tolerant to most font-resource failures, so this flag toggles
    /// the few remaining strict throws.
    /// </summary>
    public bool IgnoreResourceFontErrors { get; set; }

    /// <summary>Whether annotation-bound text contributes to search results. Stored only.</summary>
    public bool SearchInAnnotations { get; set; }

    /// <summary>Whether text drawn off-page or behind opaque shapes is ignored. Stored only.</summary>
    public bool IgnoreShadowText { get; set; }

    /// <summary>Whether the font engine's encoding tables drive decoding instead of /ToUnicode. Stored only.</summary>
    public bool UseFontEngineEncoding { get; set; }

    /// <summary>Whether text-extraction errors are routed through the document logger. Stored only.</summary>
    public bool LogTextExtractionErrors { get; set; }

    /// <summary>Maximum number of graphics elements retained by the absorber alongside matched text. Stored only.</summary>
    public int StoredGraphicElementsMaxCount { get; set; }
}
