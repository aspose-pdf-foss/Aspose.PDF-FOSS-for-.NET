using System.Text;
using Aspose.Pdf.Optimization;
using Xunit;

namespace Aspose.Pdf.Tests.Optimization;

public class PdfAValidatorTests
{
    /// <summary>
    /// Helper: build a PDF with a font that has no FontDescriptor (not embedded).
    /// </summary>
    private static byte[] BuildWithUnembeddedFont(string fontName = "ArialMT")
    {
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.4\n");

        var catalogOffset = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        // Font without FontDescriptor
        var fontOffset = ms.Position;
        Write($"5 0 obj\n<< /Type /Font /Subtype /TrueType /BaseFont /{fontName} /Encoding /WinAnsiEncoding >>\nendobj\n");

        var pageOffset = ms.Position;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] " +
              "/Resources << /Font << /F1 5 0 R >> >> >>\nendobj\n");

        var xrefOffset = ms.Position;
        Write("xref\n0 6\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        Write("0000000000 65535 f \n"); // obj 4 free
        Write($"{fontOffset:D10} 00000 n \n");
        Write("trailer\n<< /Size 6 /Root 1 0 R /ID [<abc> <abc>] >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }

    /// <summary>
    /// Helper: build a PDF with a transparency group on the page.
    /// </summary>
    private static byte[] BuildWithTransparencyGroup()
    {
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.4\n");

        var catalogOffset = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        var pageOffset = ms.Position;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] " +
              "/Group << /Type /Group /S /Transparency /CS /DeviceRGB >> >>\nendobj\n");

        var xrefOffset = ms.Position;
        Write("xref\n0 4\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        Write("trailer\n<< /Size 4 /Root 1 0 R /ID [<abc> <abc>] >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }

    /// <summary>
    /// Helper: build a PDF with ExtGState transparency (alpha < 1).
    /// </summary>
    private static byte[] BuildWithExtGStateTransparency()
    {
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.4\n");

        var catalogOffset = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        var pageOffset = ms.Position;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] " +
              "/Resources << /ExtGState << /GS0 << /ca 0.5 >> >> >> >>\nendobj\n");

        var xrefOffset = ms.Position;
        Write("xref\n0 4\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        Write("trailer\n<< /Size 4 /Root 1 0 R /ID [<abc> <abc>] >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }

    /// <summary>
    /// Helper: build a PDF with a prohibited annotation type.
    /// </summary>
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

    /// <summary>
    /// Helper: build a PDF with an annotation missing the Print flag.
    /// </summary>
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

    /// <summary>
    /// Helper: build a PDF with a Launch action (prohibited in PDF/A).
    /// </summary>
    private static byte[] BuildWithLaunchOpenAction()
    {
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.4\n");

        var actionOffset = ms.Position;
        Write("4 0 obj\n<< /S /Launch /F (test.exe) >>\nendobj\n");

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

    /// <summary>
    /// Helper: build a minimal PDF that passes most PDF/A structural checks.
    /// Has metadata with required fields, ID array, standard 14 font (exempt in PDF/A-1).
    /// </summary>
    private static byte[] BuildPdfALikeMinimal()
    {
        // Build using XMP metadata stream approach
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

        // XMP metadata stream
        var metaOffset = ms.Position;
        Write($"4 0 obj\n<< /Type /Metadata /Subtype /XML /Length {xmpBytes.Length} >>\nstream\n");
        ms.Write(xmpBytes);
        Write("\nendstream\nendobj\n");

        var catalogOffset = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R /Metadata 4 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        // Font: Helvetica (standard 14, exempt in PDF/A-1)
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

    /// <summary>
    /// Helper: build a PDF with an image using DeviceRGB and no OutputIntent.
    /// </summary>
    private static byte[] BuildWithDeviceRgbImage()
    {
        var pixelData = new byte[3 * 2 * 2]; // 2x2 RGB

        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.4\n");

        var catalogOffset = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        var imgOffset = ms.Position;
        Write($"4 0 obj\n<< /Type /XObject /Subtype /Image /Width 2 /Height 2 " +
              $"/ColorSpace /DeviceRGB /BitsPerComponent 8 /Length {pixelData.Length} >>\nstream\n");
        ms.Write(pixelData);
        Write("\nendstream\nendobj\n");

        var pageOffset = ms.Position;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] " +
              "/Resources << /XObject << /Im1 4 0 R >> >> >>\nendobj\n");

        var xrefOffset = ms.Position;
        Write("xref\n0 5\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        Write($"{imgOffset:D10} 00000 n \n");
        Write("trailer\n<< /Size 5 /Root 1 0 R /ID [<abc> <abc>] >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }

    [Fact]
    public void FontEmbedding_DetectsUnembeddedFont()
    {
        var data = BuildWithUnembeddedFont("ArialMT");
        using var doc = Document.Open(data);

        var result = PdfAValidator.Validate(doc, PdfFormat.PDF_A_1B);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Contains("Font 'ArialMT' is not embedded"));
        Assert.Contains(result.Violations, v => v.Rule == "FontEmbedding" && v.Description.Contains("ArialMT"));
    }

    [Fact]
    public void FontEmbedding_Standard14ExemptInPdfA1()
    {
        // Helvetica is a standard 14 font, exempt in PDF/A-1
        var data = BuildWithUnembeddedFont("Helvetica");
        using var doc = Document.Open(data);

        var result = PdfAValidator.Validate(doc, PdfFormat.PDF_A_1B);

        // Should not report font embedding violation for Helvetica in PDF/A-1
        Assert.DoesNotContain(result.Violations, v => v.Rule == "FontEmbedding");
    }

    [Fact]
    public void FontEmbedding_Standard14NotExemptInPdfA2()
    {
        var data = BuildWithUnembeddedFont("Helvetica");
        using var doc = Document.Open(data);

        var result = PdfAValidator.Validate(doc, PdfFormat.PDF_A_2B);

        // In PDF/A-2, standard 14 fonts must also be embedded
        Assert.Contains(result.Violations, v => v.Rule == "FontEmbedding" && v.Description.Contains("Helvetica"));
    }

    [Fact]
    public void Transparency_DetectsTransparencyGroup()
    {
        var data = BuildWithTransparencyGroup();
        using var doc = Document.Open(data);

        var result = PdfAValidator.Validate(doc, PdfFormat.PDF_A_1B);

        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v =>
            v.Rule == "Transparency" && v.PageNumber == 1);
    }

    [Fact]
    public void Transparency_DetectsExtGStateAlpha()
    {
        var data = BuildWithExtGStateTransparency();
        using var doc = Document.Open(data);

        var result = PdfAValidator.Validate(doc, PdfFormat.PDF_A_1B);

        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v =>
            v.Rule == "Transparency" && v.PageNumber == 1);
    }

    [Fact]
    public void Transparency_AllowedInPdfA2()
    {
        var data = BuildWithTransparencyGroup();
        using var doc = Document.Open(data);

        var result = PdfAValidator.Validate(doc, PdfFormat.PDF_A_2B);

        // PDF/A-2 allows transparency — should not flag transparency group
        Assert.DoesNotContain(result.Violations, v => v.Rule == "Transparency");
    }

    [Fact]
    public void ValidPdfALikeDocument_Passes()
    {
        var data = BuildPdfALikeMinimal();
        using var doc = Document.Open(data);

        var result = PdfAValidator.Validate(doc, PdfFormat.PDF_A_1B);

        Assert.True(result.IsValid);
        Assert.True(result.IsCompliant);
        Assert.Empty(result.Issues);
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void AnnotationRestriction_DetectsFileAttachment()
    {
        var data = BuildWithProhibitedAnnotation("FileAttachment");
        using var doc = Document.Open(data);

        var result = PdfAValidator.Validate(doc, PdfFormat.PDF_A_1B);

        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v =>
            v.Rule == "AnnotationType" && v.Description.Contains("FileAttachment"));
    }

    [Fact]
    public void AnnotationRestriction_DetectsSound()
    {
        var data = BuildWithProhibitedAnnotation("Sound");
        using var doc = Document.Open(data);

        var result = PdfAValidator.Validate(doc, PdfFormat.PDF_A_1B);

        Assert.Contains(result.Violations, v =>
            v.Rule == "AnnotationType" && v.Description.Contains("Sound"));
    }

    [Fact]
    public void AnnotationRestriction_DetectsMovie()
    {
        var data = BuildWithProhibitedAnnotation("Movie");
        using var doc = Document.Open(data);

        var result = PdfAValidator.Validate(doc, PdfFormat.PDF_A_1B);

        Assert.Contains(result.Violations, v =>
            v.Rule == "AnnotationType" && v.Description.Contains("Movie"));
    }

    [Fact]
    public void AnnotationRestriction_Detects3D()
    {
        var data = BuildWithProhibitedAnnotation("3D");
        using var doc = Document.Open(data);

        var result = PdfAValidator.Validate(doc, PdfFormat.PDF_A_1B);

        Assert.Contains(result.Violations, v =>
            v.Rule == "AnnotationType" && v.Description.Contains("3D"));
    }

    [Fact]
    public void AnnotationPrintFlag_DetectedMissing()
    {
        var data = BuildWithAnnotationMissingPrintFlag();
        using var doc = Document.Open(data);

        var result = PdfAValidator.Validate(doc, PdfFormat.PDF_A_1B);

        Assert.Contains(result.Violations, v =>
            v.Rule == "AnnotationPrintFlag" && v.Description.Contains("Print flag"));
    }

    [Fact]
    public void ActionRestriction_DetectsLaunch()
    {
        var data = BuildWithLaunchOpenAction();
        using var doc = Document.Open(data);

        var result = PdfAValidator.Validate(doc, PdfFormat.PDF_A_1B);

        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v =>
            v.Rule == "ActionType" && v.Description.Contains("Launch"));
    }

    [Fact]
    public void ActionRestriction_DetectsJavaScript()
    {
        var data = Helpers.PdfBuilder.BuildWithJavaScriptAction("alert('hi')");
        // The JS action is on annotation, not OpenAction — let's build with OpenAction
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.4\n");

        var actionOffset = ms.Position;
        Write("4 0 obj\n<< /S /JavaScript /JS (alert) >>\nendobj\n");

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

        var jsData = ms.ToArray();
        using var doc = Document.Open(jsData);

        var result = PdfAValidator.Validate(doc, PdfFormat.PDF_A_1B);

        Assert.Contains(result.Violations, v =>
            v.Rule == "ActionType" && v.Description.Contains("JavaScript"));
    }

    [Fact]
    public void MetadataCompleteness_DetectsMissingFields()
    {
        // Use a PDF with empty XMP metadata (no required fields)
        var xmpXml = """
            <?xpacket begin="" id="W5M0MpCehiHzreSzNTczkc9d"?>
            <x:xmpmeta xmlns:x="adobe:ns:meta/">
              <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
                <rdf:Description rdf:about="" />
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

        var pageOffset = ms.Position;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>\nendobj\n");

        var xrefOffset = ms.Position;
        Write("xref\n0 5\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        Write($"{metaOffset:D10} 00000 n \n");
        Write("trailer\n<< /Size 5 /Root 1 0 R /ID [<abc> <abc>] >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        var data = ms.ToArray();
        using var doc = Document.Open(data);

        var result = PdfAValidator.Validate(doc, PdfFormat.PDF_A_1B);

        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v => v.Rule == "MetadataPdfAId");
        Assert.Contains(result.Violations, v => v.Rule == "MetadataPdfAConformance");
        Assert.Contains(result.Violations, v => v.Rule == "MetadataDcTitle");
        Assert.Contains(result.Violations, v => v.Rule == "MetadataPdfProducer");
    }

    [Fact]
    public void ColorSpace_DetectsDeviceRgbWithoutOutputIntent()
    {
        var data = BuildWithDeviceRgbImage();
        using var doc = Document.Open(data);

        var result = PdfAValidator.Validate(doc, PdfFormat.PDF_A_1B);

        Assert.Contains(result.Violations, v =>
            v.Rule == "ColorSpace" && v.Description.Contains("device-dependent color space"));
    }

    [Fact]
    public void ValidateWithDetails_ReturnsSameAsValidate()
    {
        var data = BuildWithUnembeddedFont("ArialMT");
        using var doc = Document.Open(data);

        var result1 = PdfAValidator.Validate(doc, PdfFormat.PDF_A_1B);
        // Re-open doc since Reader might cache
        using var doc2 = Document.Open(data);
        var result2 = PdfAValidator.ValidateWithDetails(doc2, PdfFormat.PDF_A_1B);

        Assert.Equal(result1.IsValid, result2.IsValid);
        Assert.Equal(result1.Format, result2.Format);
        Assert.Equal(result1.Issues.Count, result2.Issues.Count);
        Assert.Equal(result1.Violations.Count, result2.Violations.Count);
    }

    [Fact]
    public void Violations_HavePageNumbers()
    {
        var data = BuildWithTransparencyGroup();
        using var doc = Document.Open(data);

        var result = PdfAValidator.Validate(doc, PdfFormat.PDF_A_1B);

        var transparencyViolation = result.Violations.FirstOrDefault(v => v.Rule == "Transparency");
        Assert.NotNull(transparencyViolation);
        Assert.Equal(1, transparencyViolation.PageNumber);
    }

    [Fact]
    public void MissingMetadata_Detected()
    {
        // Minimal PDF with no metadata
        var data = Helpers.PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);

        var result = PdfAValidator.Validate(doc, PdfFormat.PDF_A_1B);

        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v => v.Rule == "Metadata");
    }
}
