namespace Aspose.Pdf;

/// <summary>
/// Represents errors that occur during PDF application execution.
/// Mirrors the public exception type.
/// </summary>
public class PdfException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="PdfException"/> class.</summary>
    public PdfException() { }

    /// <summary>Initializes a new instance of the <see cref="PdfException"/> class with a message.</summary>
    public PdfException(string message) : base(message) { }

    /// <summary>Initializes a new instance of the <see cref="PdfException"/> class with a message and inner exception.</summary>
    public PdfException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>Initializes a new instance of the <see cref="PdfException"/> class wrapping the inner exception.</summary>
    public PdfException(Exception innerException) : base(string.Empty, innerException) { }

    /// <summary>Write a crash report to disk per <paramref name="options"/>. Stored only —
    /// FOSS doesn't currently emit crash reports.</summary>
    public void GenerateCrashReport(CrashReportOptions options) { _ = options; }
}

/// <summary>Configures crash-report emission for <see cref="PdfException.GenerateCrashReport"/>.</summary>
public class CrashReportOptions
{
    public CrashReportOptions(Exception exception) { Exception = exception; }

    /// <summary>Application title shown on the report header.</summary>
    public string ApplicationTitle => Exception?.Source ?? string.Empty;

    /// <summary>Filesystem directory the report is written to.</summary>
    public string CrashReportDirectory { get; set; } = string.Empty;

    /// <summary>Filename the report is written under.</summary>
    public string CrashReportFilename { get; set; } = "crash-report.txt";

    /// <summary>Full filesystem path to the emitted crash report.</summary>
    public string CrashReportPath
        => string.IsNullOrEmpty(CrashReportDirectory)
            ? CrashReportFilename
            : System.IO.Path.Combine(CrashReportDirectory, CrashReportFilename);

    /// <summary>Caller-supplied note attached to the report.</summary>
    public string CustomMessage { get; set; } = string.Empty;

    /// <summary>The exception that triggered the report.</summary>
    public Exception Exception { get; }

    /// <summary>Version of Aspose.Pdf.Foss that emitted the report.</summary>
    public string LibraryVersion
        => typeof(CrashReportOptions).Assembly.GetName().Version?.ToString() ?? "0.0.0";
}

/// <summary>
/// Thrown when a password-protected operation is attempted without
/// supplying a valid password (e.g. reading HasEditPassword on an
/// encrypted PDF whose open password has not been provided).
/// </summary>
public class InvalidPasswordException : PdfException
{
    public InvalidPasswordException() { }
    public InvalidPasswordException(string message) : base(message) { }
    public InvalidPasswordException(string message, Exception innerException) : base(message, innerException) { }
    public InvalidPasswordException(Exception innerException) : base(innerException) { }
}

/// <summary>Thrown when a stream cannot be opened as a PDF (bad header,
/// truncated file, or otherwise unrecognisable as PDF).</summary>
public class InvalidPdfFileFormatException : PdfException
{
    public InvalidPdfFileFormatException() { }
    public InvalidPdfFileFormatException(string message) : base(message) { }
    public InvalidPdfFileFormatException(string message, Exception innerException) : base(message, innerException) { }
    public InvalidPdfFileFormatException(Exception innerException) : base(innerException) { }
}

/// <summary>Thrown when an operation is attempted on the wrong form
/// type (e.g. an XFA-only operation on an AcroForm, or vice versa).</summary>
public class InvalidFormTypeOperationException : PdfException
{
    // Default to the standard "invalid operation" message rather than the generic
    // "Exception of type '…' was thrown." that Exception() would synthesise.
    public InvalidFormTypeOperationException() : base(new System.InvalidOperationException().Message) { }
    public InvalidFormTypeOperationException(string message) : base(message) { }
    public InvalidFormTypeOperationException(string message, Exception innerException) : base(message, innerException) { }
    public InvalidFormTypeOperationException(Exception innerException) : base(innerException) { }
}

/// <summary>Thrown when text content cannot be decoded — e.g. a show-string
/// references character codes a font's encoding/CMap can't map to Unicode, so
/// the run can't be extracted or edited. Mirrors the public type.</summary>
public class PdfTextDecodingException : PdfException
{
    public PdfTextDecodingException() { }
    public PdfTextDecodingException(string message) : base(message) { }
    public PdfTextDecodingException(string message, Exception innerException) : base(message, innerException) { }
    public PdfTextDecodingException(Exception innerException) : base(innerException) { }
}

/// <summary>Thrown during text extraction when the content stream issues a
/// text-showing operator (Tj/TJ/'/") while no font is set in the current
/// graphics state — i.e. no preceding <c>Tf</c> is in effect (the document is
/// malformed). Strict by default; suppressed when
/// <see cref="Aspose.Pdf.Text.TextSearchOptions.IgnoreResourceFontErrors"/> is set,
/// in which case extraction proceeds tolerantly. Mirrors the public type.</summary>
public class IncorrectFontUsageException : PdfException
{
    public IncorrectFontUsageException() { }
    public IncorrectFontUsageException(string message) : base(message) { }
    public IncorrectFontUsageException(string message, Exception innerException) : base(message, innerException) { }
    public IncorrectFontUsageException(Exception innerException) : base(innerException) { }
}

/// <summary>Thrown when a requested font cannot be located (no system
/// font with that name, no matching custom-font source, etc.).</summary>
public class FontNotFoundException : PdfException
{
    public FontNotFoundException() { }
    public FontNotFoundException(string message) : base(message) { }
    public FontNotFoundException(string message, Exception innerException) : base(message, innerException) { }
    public FontNotFoundException(Exception innerException) : base(innerException) { }
}

/// <summary>Thrown when a file cannot be opened as a font because its
/// format is not a supported font program (e.g. an Adobe Font Metrics
/// file supplied on its own, with no accompanying outline data).</summary>
public class UnsupportedFontTypeException : PdfException
{
    public UnsupportedFontTypeException() { }
    public UnsupportedFontTypeException(string message) : base(message) { }
    public UnsupportedFontTypeException(string message, Exception innerException) : base(message, innerException) { }
    public UnsupportedFontTypeException(Exception innerException) : base(innerException) { }
}
