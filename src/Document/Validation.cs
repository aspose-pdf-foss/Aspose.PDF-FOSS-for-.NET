using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>
    /// Validate the document structure and return any issues found.
    /// </summary>
    public ValidationIssue[] Validate() => DocumentValidator.Validate(this);

    /// <summary>
    /// Validate the document against a specific PDF format (PDF/A, PDF/X).
    /// </summary>
    /// <param name="outputLogStream">Stream for logging validation results (can be Stream.Null).</param>
    /// <param name="format">Target format to validate against.</param>
    /// <returns>True if the document conforms to the specified format.</returns>
    public bool Validate(Stream outputLogStream, PdfFormat format)
    {
        var logStream = outputLogStream;
        var result = Optimization.PdfAValidator.Validate(this, format);
        // Ownership of the log stream depends on what the caller handed over, and both
        // behaviours are pinned by the corpus:
        //   - a FILE stream is consumed: `var f = new FileStream(path, Create);
        //     doc.Validate(f, fmt); f = new FileStream(path, Create);` re-opens the same
        //     path immediately, which throws unless the first stream was disposed here;
        //   - any OTHER stream stays OPEN: callers pass a MemoryStream and read the log
        //     back with Seek(0) after the call.
        if (logStream is not null)
        {
            try
            {
                if (logStream is FileStream)
                {
                    using var writer = new StreamWriter(logStream, System.Text.Encoding.UTF8);
                    WriteValidationLogXml(writer, format, result);
                }
                else
                {
                    using var writer = new StreamWriter(logStream, System.Text.Encoding.UTF8,
                        bufferSize: 1024, leaveOpen: true);
                    WriteValidationLogXml(writer, format, result);
                }
            }
            catch { }
        }
        return result.IsValid;
    }

    /// <summary>
    /// Validate the document against a specific PDF format using conversion options.
    /// </summary>
    public bool Validate(PdfFormatConversionOptions options)
    {
        var result = Optimization.PdfAValidator.Validate(this, options.TargetFormat);
        return result.IsValid;
    }

    /// <summary>
    /// Validate the document against a specific PDF format, writing log to a file.
    /// </summary>
    /// <param name="outputLogFileName">Path to write validation log.</param>
    /// <param name="format">Target format to validate against.</param>
    /// <returns>True if the document conforms to the specified format.</returns>
    public bool Validate(string outputLogFileName, PdfFormat format)
    {
        var result = Optimization.PdfAValidator.Validate(this, format);
        if (!string.IsNullOrEmpty(outputLogFileName))
        {
            try
            {
                using var writer = new StreamWriter(outputLogFileName, append: false, System.Text.Encoding.UTF8);
                WriteValidationLogXml(writer, format, result);
            }
            catch
            {
                // Log write failure should not prevent validation result
            }
        }
        return result.IsValid;
    }

    /// <summary>Invalidate the cached Form so it re-reads from the AcroForm dict.</summary>
    internal void InvalidateForm() => _form = null;
}
