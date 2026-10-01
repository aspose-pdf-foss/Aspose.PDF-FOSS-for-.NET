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

public sealed partial class Document : IDisposable
{
    /// <summary>PDF/A conversion: the ZUGFeRD and associated-file specs applied.</summary>
    private void ApplyAssociatedFileFixes(PdfAConvertState pa)
    {
        // 12. ZUGFeRD (factur-x): mark the embedded invoice XML as an associated file —
        // /AFRelationship /Alternative + MIME type text/xml — and reference every embedded
        // file from the catalog /AF array, per the ZUGFeRD/PDF-A-3 associated-files profile.
        // The profile also requires the ZUGFeRD XMP extension schema naming the invoice.
        if (pa.fix && pa.format == PdfFormat.ZUGFeRD)
        {
            ApplyZugferdAssociatedFiles();
            ApplyZugferdXmp(pa.meta);
        }

        // 12a. PDF/A-3 (ISO 19005-3 §6.9): every embedded file is an associated file —
        // the spec carries /AFRelationship (Unspecified when the relationship is
        // unknown), its embedded stream carries a MIME /Subtype (application/pdf when
        // nothing better is known), and the catalog /AF array references every spec.
        if (pa.fix && pa.part == "3" && pa.format != PdfFormat.ZUGFeRD)
            ApplyPdfA3AssociatedFiles();

        // 12b. PDF/A-2 (ISO 19005-2 §6.9): an embedded file must itself be a PDF/A
        // document. Convert every embedded PDF attachment to PDF/A-2B in place —
        // so the output attachments then claim 2B
        // (so a Validate(PDF_A_2B) of the extracted attachment passes and a
        // Validate(PDF_A_3B) fails the claim gate).
        ApplyPartSpecificFixes(pa);
    }
}
