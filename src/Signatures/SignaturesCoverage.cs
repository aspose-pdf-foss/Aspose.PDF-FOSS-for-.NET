namespace Aspose.Pdf.Signatures;

/// <summary>
/// How much of a document the signatures it carries actually cover.
/// </summary>
public enum SignaturesCoverage
{
    /// <summary>The document carries no signature to measure against.</summary>
    Undefined,

    /// <summary>Every byte of the document lies inside a signature's range.</summary>
    EntirelySigned,

    /// <summary>Content was added after the last signature: incremental updates lie
    /// past the covered range, so the document has unsigned content.</summary>
    PartiallySigned,
}
