using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Security;

public sealed partial class PdfSigner
{
    /// <summary>
    /// Sign a PDF document and return the signed bytes.
    /// Uses incremental update to preserve original content.
    /// </summary>
    public static byte[] Sign(byte[] pdfData, PdfCertificate certificate,
        SignatureOptions? options = null)
    {
        var sg = new SignFlowState();
        sg.pdfData = pdfData;
        sg.certificate = certificate;
        options ??= new SignatureOptions();
        sg.options = options;
        sg.contentsSize = sg.options.ContentsSize;

        // Step 1: Parse the original document to find AcroForm and allocate objects
        using var doc = OpenDoc(sg.pdfData, sg.options.Password);
        sg.doc = doc;
        sg.reader = sg.doc.Reader;
        sg.trailer = sg.reader.Trailer;
        sg.xref = sg.reader.XRefTable;

        AllocateSignatureObjects(sg);

        // Step 2: Build the incremental update with placeholder
        using var ms = new MemoryStream();
        sg.ms = ms;

        // Copy original PDF
        WriteSignatureObjects(sg);

        // An adopted inline field is referenced by the PAGE as well: its widget rides in
        // /Annots, and a viewer paints annotations from there. Repoint that slot at the
        // object the signer just wrote, or the page keeps drawing the blank placeholder.
        ReplaceAdoptedFieldAnnot(sg);

        WriteCatalogAndXref(sg);

        EmbedSignature(sg);

        return sg.fileBytes;
    }
}
