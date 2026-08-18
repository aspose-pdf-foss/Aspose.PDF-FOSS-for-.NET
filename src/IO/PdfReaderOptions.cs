namespace Aspose.Pdf.IO;

/// <summary>
/// Options controlling PdfReader parsing behavior.
/// </summary>
internal sealed class PdfReaderOptions
{
    /// <summary>
    /// When true, the reader tolerates malformed objects, missing endobj/endstream markers,
    /// extra whitespace in xref entries, and objects at wrong offsets. Default: true.
    /// </summary>
    public bool LenientMode { get; set; } = true;

    /// <summary>
    /// When true, the reader automatically recovers corrupt or missing xref tables
    /// by scanning the file for object headers. Default: true.
    /// </summary>
    public bool RepairXref { get; set; } = true;

    /// <summary>Handler to decrypt the document with when its /Encrypt /Filter
    /// names something other than the Standard handler. Null leaves the built-in
    /// Standard handler in charge.</summary>
    public Aspose.Pdf.Security.ICustomSecurityHandler? CustomSecurityHandler { get; set; }
}
