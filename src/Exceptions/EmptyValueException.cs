namespace Aspose.Pdf;

/// <summary>Thrown when an operation requires a value the caller left empty
/// (e.g. initialising a <see cref="Aspose.Pdf.Forms.DateField"/> whose
/// <c>PartialName</c> was never assigned).</summary>
public class EmptyValueException : PdfException
{
    public EmptyValueException() { }
    public EmptyValueException(string message) : base(message) { }
    public EmptyValueException(string message, System.Exception innerException)
        : base(message, innerException) { }
}
