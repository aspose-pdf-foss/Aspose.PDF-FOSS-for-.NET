namespace Aspose.Pdf.Security;

/// <summary>
/// Thrown when a produced signature does not fit the space reserved for the
/// /Contents hex string because length estimation was skipped
/// (<see cref="Forms.Signature.AvoidEstimatingSignatureLength"/>) and the
/// actual signature exceeded the fixed reservation.
/// </summary>
public class SignatureLengthMismatchException : PdfException
{
    public SignatureLengthMismatchException() { }

    internal SignatureLengthMismatchException(int actualSignatureLength)
        : base($"The produced signature is {actualSignatureLength} bytes and does not fit the reserved signature length. " +
               "Set Signature.DefaultSignatureLength to at least that size or enable length estimation.")
    {
    }
}
