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

    /// <summary>Serialise a validation result in the established log schema:
    /// <c>&lt;Compliance&gt;&lt;File&gt;…&lt;Fonts&gt;&lt;Problem Severity Clause&gt;</c> —
    /// font problems nest under &lt;Fonts&gt;, everything else sits directly under
    /// &lt;File&gt; alongside the empty section markers.</summary>
    private void WriteValidationLogXml(TextWriter writer, PdfFormat format,
        Optimization.PdfAValidationResult result, string operation = "Validation")
    {
        static string ClauseFor(string rule) => rule switch
        {
            "FontCmap" => "7.21.4.2",
            "FontEmbedding" or "FontNotEmbedded" => "6.2.11.4",
            "MetadataPdfAId" or "MetadataPdfAConformance" or "Metadata" => "6.6.4",
            "TaggedPdf" or "StructureTree" => "6.7.3.3",
            "DocumentTitle" => "7.1",
            _ => "",
        };
        static string Problem(Optimization.PdfAViolation v)
        {
            var clause = v.Clause ?? ClauseFor(v.Rule);
            var page = v.PageNumber is int p ? $" Page=\"{p}\"" : "";
            var objId = v.ObjectId is not null ? $" ObjectID=\"{EscapeXml(v.ObjectId)}\"" : "";
            // Convertable defaults to true — every regular violation class this
            // validator reports is either repaired structurally (fonts, metadata,
            // OutputIntent, version, file ID, xref form) or stripped under
            // ConvertErrorAction.Delete. Implementation-limit violations baked into
            // the content mark themselves unconvertable instead.
            var convertable = v.Convertable ? "True" : "False";
            return $"<Problem Severity=\"Error\" Clause=\"{clause}\" Code=\"{clause}\"{objId} Convertable=\"{convertable}\"{page}>{EscapeXml(v.Description)}</Problem>";
        }

        var fontProblems = new System.Text.StringBuilder();
        var catalogProblems = new System.Text.StringBuilder();
        var otherProblems = new System.Text.StringBuilder();
        foreach (var v in result.Violations)
        {
            if (v.Rule.StartsWith("Font", StringComparison.Ordinal)) fontProblems.Append(Problem(v));
            // Whole-document refusals live in the log's Catalog section (the shape
            // used for a signed-file refusal).
            else if (v.Rule == "SignedFile") catalogProblems.Append(Problem(v));
            else otherProblems.Append(Problem(v));
        }

        int pages;
        try { pages = Pages.Count; } catch { pages = 0; }
        writer.Write(
            $"<Compliance Name=\"Log\" Operation=\"{operation}\" Target=\"{EscapeXml(GetVersionString(format))}\">" +
            "<Version>1.0</Version>" +
            $"<Date>{DateTime.Now}</Date>" +
            $"<File Version=\"{EscapeXml(PdfVersion ?? string.Empty)}\" Name=\"{EscapeXml(Path.GetFileName(FileName ?? string.Empty))}\" Pages=\"{pages}\">" +
            "<Security />" +
            (catalogProblems.Length > 0 ? $"<Catalog>{catalogProblems}</Catalog>" : "<Catalog />") +
            "<Header /><Annotations />" +
            (fontProblems.Length > 0 ? $"<Fonts>{fontProblems}</Fonts>" : "<Fonts />") +
            "<trailer />" + otherProblems +
            "<Metadata /><objects /><xObjects /><actions /><xmpmeta /><EmbeddedFiles />" +
            "</File></Compliance>");
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
