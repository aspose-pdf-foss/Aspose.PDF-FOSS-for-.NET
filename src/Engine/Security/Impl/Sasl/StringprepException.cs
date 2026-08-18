namespace Aspose.Pdf.Engine.Security.Impl.Sasl;

/// <summary>
/// Thrown by <see cref="Stringprep.Process"/> when the input violates the
/// SASLprep (RFC 4013) profile — a prohibited code point, a bidirectional-text
/// rule violation, or (optionally) an unassigned code point.
/// </summary>
internal class StringprepException : PdfException
{
    public StringprepException() { }

    public StringprepException(string message) : base(message)
    {
    }
}
