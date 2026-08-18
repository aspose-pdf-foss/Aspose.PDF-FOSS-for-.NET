namespace Aspose.Pdf;

/// <summary>
/// Thrown when an operation requests a feature that the document's PDF version
/// has deprecated — e.g. RC4 encryption or SHA-1-based signature subfilters on
/// a PDF 2.0 (ISO 32000-2) document. Mirrors the public exception type.
/// </summary>
public sealed class DeprecatedFeatureException : PdfException
{
    /// <summary>Initializes a new instance of the <see cref="DeprecatedFeatureException"/> class.</summary>
    public DeprecatedFeatureException() { }

    /// <summary>Initializes a new instance of the <see cref="DeprecatedFeatureException"/> class with a message.</summary>
    public DeprecatedFeatureException(string message) : base(message) { }
}
