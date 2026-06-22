using System.Text;
using Aspose.Pdf.Optimization;
using Xunit;

namespace Aspose.Pdf.Tests.Optimization;

public class PdfAConversionTests
{
    [Fact]
    public void Convert_MinimalPdf_AddsRequiredMetadata()
    {
        var data = Helpers.PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);

        var options = new PdfFormatConversionOptions(PdfFormat.PDF_A_1B);
        var result = doc.Convert(options);

        Assert.True(result);
        Assert.NotNull(doc.Metadata);
        Assert.Equal("1", doc.Metadata!.PdfAidPart);
        Assert.Equal("B", doc.Metadata.PdfAidConformance);
        Assert.False(string.IsNullOrEmpty(doc.Metadata.Get("dc:title")));
        Assert.False(string.IsNullOrEmpty(doc.Metadata.Get("pdf:Producer")));

        // Log should contain metadata violations that were fixed
        Assert.Contains(options.ConversionLog, v => v.Rule == "MetadataPdfAId");
        Assert.Contains(options.ConversionLog, v => v.Rule == "MetadataPdfAConformance");
    }

    [Fact]
    public void Convert_AddsFileId()
    {
        var data = Helpers.PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);

        var options = new PdfFormatConversionOptions(PdfFormat.PDF_A_1B);
        doc.Convert(options);

        Assert.Contains(options.ConversionLog, v => v.Rule == "FileId");

        // Save and re-open to verify the ID was written
        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        var idObj = doc2.Reader.Trailer.Get("ID");
        Assert.NotNull(idObj);
    }

    [Fact]
    public void Convert_FixesPdfVersion()
    {
        // Build a PDF with version 1.2
        var data = BuildPdfWithVersion("1.2");
        using var doc = Document.Open(data);

        var options = new PdfFormatConversionOptions(PdfFormat.PDF_A_1B);
        doc.Convert(options);

        Assert.Contains(options.ConversionLog, v => v.Rule == "PdfVersion");
    }

    [Fact]
    public void Convert_RemovesProhibitedActions()
    {
        var data = BuildWithJavaScriptOpenAction();
        using var doc = Document.Open(data);

        // Verify the action exists before conversion
        var openActionBefore = doc.Catalog.Get("OpenAction");
        Assert.NotNull(openActionBefore);

        var options = new PdfFormatConversionOptions(PdfFormat.PDF_A_1B);
        doc.Convert(options);

        Assert.Contains(options.ConversionLog, v => v.Rule == "ActionType" && v.Description.Contains("JavaScript"));

        // Verify the action was removed
        var openActionAfter = doc.Catalog.Get("OpenAction");
        Assert.Null(openActionAfter);
    }

    [Fact]
    public void Convert_FixesAnnotationPrintFlags()
    {
        var data = BuildWithAnnotationMissingPrintFlag();
        using var doc = Document.Open(data);

        var options = new PdfFormatConversionOptions(PdfFormat.PDF_A_1B);
        doc.Convert(options);

        Assert.Contains(options.ConversionLog, v => v.Rule == "AnnotationPrintFlag");

        // Verify the flag was set: save and re-open
        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        var annotsObj = doc2.Reader.Resolve(doc2.Pages[1].Dict.Get("Annots")) as Core.PdfArray;
        Assert.NotNull(annotsObj);
        var annotDict = doc2.Reader.ResolveDict(annotsObj![0]);
        Assert.NotNull(annotDict);
        var flags = (int)annotDict!.GetInt("F");
        Assert.True((flags & 4) != 0, "Print flag (bit 3) should be set");
    }

    [Fact]
    public void Convert_RemovesProhibitedAnnotations()
    {
        var data = BuildWithProhibitedAnnotation("FileAttachment");
        using var doc = Document.Open(data);

        var options = new PdfFormatConversionOptions(PdfFormat.PDF_A_1B);
        doc.Convert(options);

        Assert.Contains(options.ConversionLog, v => v.Rule == "AnnotationType");

        // Verify the annotation was removed: save and re-open
        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        var annotsObj = doc2.Reader.Resolve(doc2.Pages[1].Dict.Get("Annots"));
        // Either no Annots key or empty array
        Assert.True(annotsObj is null or Core.PdfArray { Count: 0 });
    }

    [Fact]
    public void Convert_ErrorActionNone_LogsButDoesNotFix()
    {
        var data = BuildWithJavaScriptOpenAction();
        using var doc = Document.Open(data);

        var options = new PdfFormatConversionOptions(PdfFormat.PDF_A_1B, ConvertErrorAction.None);
        doc.Convert(options);

        // Log should contain violations
        Assert.Contains(options.ConversionLog, v => v.Rule == "ActionType");

        // But the action should still be there
        var openAction = doc.Catalog.Get("OpenAction");
        Assert.NotNull(openAction);
    }

    [Fact]
    public void RemovePdfaCompliance_StripsPdfAIdMarkers()
    {
        var data = BuildPdfALikeMinimal();
        using var doc = Document.Open(data);

        // Verify it has pdfaid markers
        Assert.NotNull(doc.Metadata);
        Assert.Equal("1", doc.Metadata!.PdfAidPart);
        Assert.Equal("B", doc.Metadata.PdfAidConformance);

        doc.RemovePdfaCompliance();

        Assert.Null(doc.Metadata.PdfAidPart);
        Assert.Null(doc.Metadata.PdfAidConformance);
    }

    [Fact]
    public void Convert_RoundTrip_ValidatesClean()
    {
        var data = Helpers.PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);

        var options = new PdfFormatConversionOptions(PdfFormat.PDF_A_1B);
        doc.Convert(options);

        // Save and re-open
        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        // Validate the converted document
        var result = PdfAValidator.Validate(doc2, PdfFormat.PDF_A_1B);

        Assert.Empty(result.Violations);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Convert_PdfA2b_SetsCorrectMetadata()
    {
        var data = Helpers.PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);

        var options = new PdfFormatConversionOptions(PdfFormat.PDF_A_2B);
        doc.Convert(options);

        Assert.Equal("2", doc.Metadata!.PdfAidPart);
        Assert.Equal("B", doc.Metadata.PdfAidConformance);
    }

    // ── Helper builders ────────────────────────────────────────────────────

    private static byte[] BuildPdfWithVersion(string version)
    {
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write($"%PDF-{version}\n");

        var catalogOffset = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        var pageOffset = ms.Position;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>\nendobj\n");

        var xrefOffset = ms.Position;
        Write("xref\n0 4\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        Write("trailer\n<< /Size 4 /Root 1 0 R >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }

    private static byte[] BuildWithJavaScriptOpenAction()
    {
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.4\n");

        var actionOffset = ms.Position;
        Write("4 0 obj\n<< /S /JavaScript /JS (alert('hi')) >>\nendobj\n");

        var catalogOffset = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R /OpenAction 4 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        var pageOffset = ms.Position;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>\nendobj\n");

        var xrefOffset = ms.Position;
        Write("xref\n0 5\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        Write($"{actionOffset:D10} 00000 n \n");
        Write("trailer\n<< /Size 5 /Root 1 0 R /ID [<abc> <abc>] >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }

    private static byte[] BuildWithAnnotationMissingPrintFlag()
    {
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.4\n");

        var catalogOffset = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        // Annotation with F=0 (no Print flag)
        var annotOffset = ms.Position;
        Write("4 0 obj\n<< /Type /Annot /Subtype /Text /Rect [100 200 200 300] /F 0 >>\nendobj\n");

        var pageOffset = ms.Position;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Annots [4 0 R] >>\nendobj\n");

        var xrefOffset = ms.Position;
        Write("xref\n0 5\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        Write($"{annotOffset:D10} 00000 n \n");
        Write("trailer\n<< /Size 5 /Root 1 0 R /ID [<abc> <abc>] >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }

    private static byte[] BuildWithProhibitedAnnotation(string subtype)
    {
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.4\n");

        var catalogOffset = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        var annotOffset = ms.Position;
        Write($"4 0 obj\n<< /Type /Annot /Subtype /{subtype} /Rect [100 200 200 300] /F 4 >>\nendobj\n");

        var pageOffset = ms.Position;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Annots [4 0 R] >>\nendobj\n");

        var xrefOffset = ms.Position;
        Write("xref\n0 5\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        Write($"{annotOffset:D10} 00000 n \n");
        Write("trailer\n<< /Size 5 /Root 1 0 R /ID [<abc> <abc>] >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }

    private static byte[] BuildPdfALikeMinimal()
    {
        var xmpXml = """
            <?xpacket begin="" id="W5M0MpCehiHzreSzNTczkc9d"?>
            <x:xmpmeta xmlns:x="adobe:ns:meta/">
              <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
                <rdf:Description rdf:about=""
                  xmlns:dc="http://purl.org/dc/elements/1.1/"
                  xmlns:pdf="http://ns.adobe.com/pdf/1.3/"
                  xmlns:pdfaid="http://www.aiim.org/pdfa/ns/id/">
                  <dc:title>Test Document</dc:title>
                  <pdf:Producer>Aspose.PDF FOSS for .NET</pdf:Producer>
                  <pdfaid:part>1</pdfaid:part>
                  <pdfaid:conformance>B</pdfaid:conformance>
                </rdf:Description>
              </rdf:RDF>
            </x:xmpmeta>
            <?xpacket end="w"?>
            """;
        var xmpBytes = Encoding.UTF8.GetBytes(xmpXml);

        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.4\n");

        var metaOffset = ms.Position;
        Write($"4 0 obj\n<< /Type /Metadata /Subtype /XML /Length {xmpBytes.Length} >>\nstream\n");
        ms.Write(xmpBytes);
        Write("\nendstream\nendobj\n");

        var catalogOffset = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R /Metadata 4 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        var fontOffset = ms.Position;
        Write("5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n");

        var pageOffset = ms.Position;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] " +
              "/Resources << /Font << /F1 5 0 R >> >> >>\nendobj\n");

        var xrefOffset = ms.Position;
        Write("xref\n0 6\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        Write($"{metaOffset:D10} 00000 n \n");
        Write($"{fontOffset:D10} 00000 n \n");
        Write("trailer\n<< /Size 6 /Root 1 0 R /ID [<abc123> <abc123>] >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }
}
