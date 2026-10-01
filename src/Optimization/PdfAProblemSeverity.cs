namespace Aspose.Pdf.Optimization;

/// <summary>
/// How severely a conformance problem is reported in the validation log. Only
/// <see cref="Error"/> fails the verdict — a warning or a manual-check note is
/// recorded for the reader and leaves a document valid.
/// </summary>
internal enum PdfAProblemSeverity
{
    /// <summary>A conformance violation; the document does not conform.</summary>
    Error,

    /// <summary>A suspicious construct that does not by itself break conformance.</summary>
    Warning,

    /// <summary>A requirement no automatic check can settle, listed so a human reads it.</summary>
    NeedManualCheck,
}
