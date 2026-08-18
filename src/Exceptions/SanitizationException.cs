using System;

namespace Aspose.Pdf.Sanitization;

/// <summary>
/// Thrown when a document structure is recognised as a signature-forgery attack
/// (e.g. Universal Signature Forgery / USF or Signature Wrapping / SWA) during
/// signature verification.
/// </summary>
public sealed class SanitizationException : PdfException
{
    /// <summary>Create a sanitization exception.</summary>
    public SanitizationException() : base() { }

    /// <summary>Create a sanitization exception with a message.</summary>
    public SanitizationException(string message) : base(message) { }

    /// <summary>Create a sanitization exception with a message and inner exception.</summary>
    public SanitizationException(string message, Exception innerException)
        : base(message, innerException) { }

    /// <summary>Create a sanitization exception wrapping an inner exception.</summary>
    public SanitizationException(Exception innerException)
        : base(string.Empty, innerException) { }
}
