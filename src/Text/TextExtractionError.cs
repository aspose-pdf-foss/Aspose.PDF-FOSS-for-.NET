namespace Aspose.Pdf.Text;

/// <summary>
/// Diagnostic produced by <see cref="TextFragmentAbsorber"/> when a page-level
/// extraction fails or partially succeeds. Reported via the absorber's
/// <see cref="TextFragmentAbsorber.Errors"/> collection.
/// </summary>
public sealed class TextExtractionError
{
    /// <summary>1-based page number where the error occurred. 0 when not tied to a specific page.</summary>
    public int PageIndex { get; internal set; }

    /// <summary>Human-readable error message.</summary>
    public string Message { get; internal set; } = string.Empty;

    /// <summary>Longer-form description of the error (Aspose.Pdf sibling of <see cref="Message"/>).</summary>
    public string Description { get; internal set; } = string.Empty;

    /// <summary>Raw text that was extracted before the failure (best-effort).</summary>
    public string ExtractedText { get; internal set; } = string.Empty;

    /// <summary>Resource-dict font key (e.g. "F1") associated with the error.</summary>
    public string FontKey { get; internal set; } = string.Empty;

    /// <summary>Human-readable font name reported by the failed font lookup.</summary>
    public string FontName { get; internal set; } = string.Empty;

    /// <summary>One-line summary suitable for logs.</summary>
    public string Summary { get; internal set; } = string.Empty;

    /// <summary>Where in the content stream the error originated.</summary>
    public TextExtractionErrorLocation Location { get; internal set; } = new TextExtractionErrorLocation();

    public override string ToString() =>
        string.IsNullOrEmpty(Summary) ? Message : Summary;
}

/// <summary>
/// Structured location of a <see cref="TextExtractionError"/> within a page's
/// content stream. All members are stored-only diagnostics.
/// </summary>
public sealed class TextExtractionErrorLocation
{
    public string FontUsedKey { get; internal set; } = string.Empty;
    public string FormKey { get; internal set; } = string.Empty;
    public string ObjectType { get; internal set; } = string.Empty;
    public int OperatorIndex { get; internal set; }
    public string OperatorString { get; internal set; } = string.Empty;
    public int PageNumber { get; internal set; }
    public string Path { get; internal set; } = string.Empty;
    public Aspose.Pdf.Point TextStartPoint { get; internal set; } = new Aspose.Pdf.Point(0, 0);

    public override string ToString() =>
        $"page {PageNumber} op#{OperatorIndex} {OperatorString} ({ObjectType})";
}
