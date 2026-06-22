using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Converters;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Converters;

public class PdfToHtmlConverterTests
{
    [Fact]
    public void SaveAsHtml_BasicText_ProducesSpans()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (Hello HTML) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var converter = new PdfToHtmlConverter();
        var html = converter.SaveAsHtml(doc);

        Assert.Contains("Hello HTML", html);
        Assert.Contains("<span", html);
        Assert.Contains("class=\"pdf-text\"", html);
        Assert.Contains("<!DOCTYPE html>", html);
    }

    [Fact]
    public void SavePageAsHtml_ReturnsDiv()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (Page text) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var converter = new PdfToHtmlConverter();
        var html = converter.SavePageAsHtml(doc, 1);

        Assert.Contains("Page text", html);
        Assert.Contains("<div class=\"pdf-page\"", html);
        // Should be a fragment, not a full document
        Assert.DoesNotContain("<!DOCTYPE", html);
    }

    // ── Image support ───────────────────────────────────────────────────

    [Fact]
    public void SaveAsHtml_WithImage_ProducesImgTag()
    {
        // Build a PDF with an uncompressed RGB image
        var data = PdfBuilder.BuildWithUncompressedImage(4, 4);
        using var doc = Document.Open(data);

        var converter = new PdfToHtmlConverter();
        var html = converter.SaveAsHtml(doc);

        // Should contain an img tag with a data URI
        Assert.Contains("<img", html);
        Assert.Contains("class=\"pdf-image\"", html);
        Assert.Contains("data:image/png;base64,", html);
    }

    [Fact]
    public void SaveAsHtml_ImagePosition_UsesCTM()
    {
        var data = PdfBuilder.BuildWithUncompressedImage(4, 4);
        using var doc = Document.Open(data);

        var converter = new PdfToHtmlConverter();
        var html = converter.SaveAsHtml(doc);

        // The BuildWithUncompressedImage uses: q 4 0 0 4 100 500 cm /Im1 Do Q
        // So image should be positioned at left:100pt
        Assert.Contains("left:100pt", html);
        // Width should be 4pt (from CTM a=4)
        Assert.Contains("width:4pt", html);
    }

    [Fact]
    public void SaveAsHtml_JpegImage_UsesJpegDataUri()
    {
        // Build a PDF with a JPEG image (DCTDecode filter)
        var data = BuildWithJpegImage();
        using var doc = Document.Open(data);

        var converter = new PdfToHtmlConverter();
        var html = converter.SaveAsHtml(doc);

        Assert.Contains("<img", html);
        Assert.Contains("data:image/jpeg;base64,", html);
    }

    // ── Link annotations ────────────────────────────────────────────────

    [Fact]
    public void SaveAsHtml_WithLinkAnnotation_ProducesAnchorTag()
    {
        var data = PdfBuilder.BuildWithLinkAnnotation("https://example.com");
        using var doc = Document.Open(data);

        var converter = new PdfToHtmlConverter();
        var html = converter.SaveAsHtml(doc);

        Assert.Contains("<a", html);
        Assert.Contains("class=\"pdf-link\"", html);
        Assert.Contains("href=\"https://example.com\"", html);
    }

    [Fact]
    public void SaveAsHtml_LinkAnnotation_HasPositionStyles()
    {
        var data = PdfBuilder.BuildWithLinkAnnotation("https://example.com");
        using var doc = Document.Open(data);

        var converter = new PdfToHtmlConverter();
        var html = converter.SaveAsHtml(doc);

        // The link rect is [72 700 200 720] on a 792pt page
        // cssTop = 792 - 720 = 72
        Assert.Contains("left:72pt", html);
        Assert.Contains("top:72pt", html);
    }

    // ── ToUnicode CMap decoding ─────────────────────────────────────────

    [Fact]
    public void SaveAsHtml_WithToUnicode_DecodesText()
    {
        // Build a PDF with a font that has a ToUnicode CMap mapping
        // code 0x01 -> 'A', 0x02 -> 'B', 0x03 -> 'C'
        var data = BuildWithToUnicodeFont();
        using var doc = Document.Open(data);

        var converter = new PdfToHtmlConverter();
        var html = converter.SaveAsHtml(doc);

        // The content stream uses bytes [0x01, 0x02, 0x03]
        // which should be decoded via ToUnicode to "ABC"
        Assert.Contains("ABC", html);
    }

    // ── Path/Shape rendering as SVG ─────────────────────────────────────

    [Fact]
    public void SaveAsHtml_WithPaths_ProducesSvgOverlay()
    {
        // Content stream with a rectangle path
        var content = Encoding.ASCII.GetBytes("100 200 50 30 re S");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var converter = new PdfToHtmlConverter();
        var html = converter.SaveAsHtml(doc);

        Assert.Contains("<svg", html);
        Assert.Contains("class=\"pdf-svg\"", html);
        Assert.Contains("<path", html);
        Assert.Contains("xmlns=\"http://www.w3.org/2000/svg\"", html);
    }

    [Fact]
    public void SaveAsHtml_FilledPath_HasFillColor()
    {
        // Red filled rectangle
        var content = Encoding.ASCII.GetBytes("1 0 0 rg 10 20 100 50 re f");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var converter = new PdfToHtmlConverter();
        var html = converter.SaveAsHtml(doc);

        Assert.Contains("<path", html);
        // Fill should be red
        Assert.Contains("fill=\"rgb(255,0,0)\"", html);
        Assert.Contains("stroke=\"none\"", html);
    }

    [Fact]
    public void SaveAsHtml_StrokedPath_HasStrokeColor()
    {
        // Blue stroked line
        var content = Encoding.ASCII.GetBytes("0 0 1 RG 10 10 m 100 100 l S");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var converter = new PdfToHtmlConverter();
        var html = converter.SaveAsHtml(doc);

        Assert.Contains("<path", html);
        Assert.Contains("stroke=\"rgb(0,0,255)\"", html);
        Assert.Contains("fill=\"none\"", html);
    }

    [Fact]
    public void SaveAsHtml_NoPath_NoSvgElement()
    {
        // Plain text, no paths
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (No paths here) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var converter = new PdfToHtmlConverter();
        var html = converter.SaveAsHtml(doc);

        Assert.DoesNotContain("<svg", html);
    }

    [Fact]
    public void SaveAsHtml_FillAndStroke_BothPresent()
    {
        // Fill+stroke with B operator
        var content = Encoding.ASCII.GetBytes("1 0 0 rg 0 1 0 RG 50 50 200 100 re B");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var converter = new PdfToHtmlConverter();
        var html = converter.SaveAsHtml(doc);

        Assert.Contains("<path", html);
        Assert.Contains("fill=\"rgb(255,0,0)\"", html);
        Assert.Contains("stroke=\"rgb(0,255,0)\"", html);
    }

    [Fact]
    public void SaveAsHtml_SvgOverlay_CoversFullPage()
    {
        var content = Encoding.ASCII.GetBytes("10 20 30 40 re S");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var converter = new PdfToHtmlConverter();
        var html = converter.SaveAsHtml(doc);

        // SVG should have Y-axis flip transform
        Assert.Contains("scale(1,-1)", html);
        // SVG should match page dimensions
        Assert.Contains("viewBox=\"0 0 612 792\"", html);
    }

    [Fact]
    public void SaveAsHtml_Stream_WritesUtf8()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (Stream test) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var converter = new PdfToHtmlConverter();
        using var ms = new MemoryStream();
        converter.SaveAsHtml(doc, ms);

        var html = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("Stream test", html);
        Assert.Contains("<!DOCTYPE html>", html);
    }

    // ── Helper methods to build test PDFs ───────────────────────────────

    /// <summary>
    /// Builds a minimal PDF with a fake JPEG image (DCTDecode filter).
    /// Uses a minimal valid JPEG (SOI + EOI markers).
    /// </summary>
    private static byte[] BuildWithJpegImage()
    {
        // Minimal JPEG: SOI (FF D8) + APP0 header + EOI (FF D9)
        // This is technically a valid but empty JPEG
        var jpegBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x02, 0x00, 0x00, 0xFF, 0xD9 };

        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.4\n");

        var catalogOffset = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        // Image XObject with DCTDecode
        var imgOffset = ms.Position;
        Write($"4 0 obj\n<< /Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpegBytes.Length} >>\nstream\n");
        ms.Write(jpegBytes);
        Write("\nendstream\nendobj\n");

        // Content stream that draws the image
        var contentBytes = Encoding.ASCII.GetBytes("q 100 0 0 100 50 400 cm /Im1 Do Q");
        var contentOffset = ms.Position;
        Write($"5 0 obj\n<< /Length {contentBytes.Length} >>\nstream\n");
        ms.Write(contentBytes);
        Write("\nendstream\nendobj\n");

        var pageOffset = ms.Position;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 5 0 R /Resources << /XObject << /Im1 4 0 R >> >> >>\nendobj\n");

        var xrefOffset = ms.Position;
        Write("xref\n0 6\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        Write($"{imgOffset:D10} 00000 n \n");
        Write($"{contentOffset:D10} 00000 n \n");
        Write("trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }

    /// <summary>
    /// Builds a PDF with a font that has a ToUnicode CMap.
    /// Codes 0x01->A, 0x02->B, 0x03->C. Content stream shows bytes [01 02 03].
    /// </summary>
    private static byte[] BuildWithToUnicodeFont()
    {
        var cmapContent = @"/CIDInit /ProcSet findresource begin
12 dict begin
begincmap
/CIDSystemInfo
<< /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def
/CMapName /Adobe-Identity-UCS def
/CMapType 2 def
1 begincodespacerange
<00> <FF>
endcodespacerange
3 beginbfchar
<01> <0041>
<02> <0042>
<03> <0043>
endbfchar
endcmap
CMapName currentdict /CMap defineresource pop
end
end";
        var cmapBytes = Encoding.ASCII.GetBytes(cmapContent);

        // Content stream: select font, show bytes 01 02 03
        var contentStr = "BT /F1 12 Tf <010203> Tj ET";
        var contentBytes = Encoding.ASCII.GetBytes(contentStr);

        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.4\n");

        var catalogOffset = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        // ToUnicode CMap stream (obj 6)
        var cmapOffset = ms.Position;
        Write($"6 0 obj\n<< /Length {cmapBytes.Length} >>\nstream\n");
        ms.Write(cmapBytes);
        Write("\nendstream\nendobj\n");

        // Font with ToUnicode reference (obj 5)
        var fontOffset = ms.Position;
        Write("5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /ToUnicode 6 0 R >>\nendobj\n");

        // Content stream (obj 4)
        var contentOffset = ms.Position;
        Write($"4 0 obj\n<< /Length {contentBytes.Length} >>\nstream\n");
        ms.Write(contentBytes);
        Write("\nendstream\nendobj\n");

        // Page (obj 3)
        var pageOffset = ms.Position;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>\nendobj\n");

        var xrefOffset = ms.Position;
        Write("xref\n0 7\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        Write($"{contentOffset:D10} 00000 n \n");
        Write($"{fontOffset:D10} 00000 n \n");
        Write($"{cmapOffset:D10} 00000 n \n");
        Write("trailer\n<< /Size 7 /Root 1 0 R >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }
}
