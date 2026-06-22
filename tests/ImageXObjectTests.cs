using System.IO.Compression;
using System.Text;
using Xunit;

namespace Aspose.Pdf.Tests;

public class ImageXObjectTests
{
    [Fact]
    public void Image_FlateDecoded_Properties()
    {
        var pdf = BuildPdfWithFlateImage(10, 10, 3); // 10x10 RGB
        using var doc = Document.Open(pdf);
        var images = doc.Pages[1].Images;

        Assert.Single(images);
        var img = images[1];
        Assert.Equal("Im0", img.Name);
        Assert.Equal(10, img.Width);
        Assert.Equal(10, img.Height);
        Assert.Equal(8, img.BitsPerComponent);
        Assert.Equal("DeviceRGB", img.ColorSpace);
        Assert.Equal("FlateDecode", img.Filter);
        Assert.False(img.IsJpeg);
        Assert.Equal(3, img.ComponentCount);
    }

    [Fact]
    public void Image_FlateDecoded_GetDecodedData()
    {
        var width = 4;
        var height = 3;
        var components = 3;
        var pixelData = new byte[width * height * components];
        // Fill with a pattern
        for (var i = 0; i < pixelData.Length; i++)
            pixelData[i] = (byte)(i % 256);

        var pdf = BuildPdfWithFlateImage(width, height, components, pixelData);
        using var doc = Document.Open(pdf);
        var img = doc.Pages[1].Images[1];
        var decoded = img.GetDecodedData();

        Assert.Equal(pixelData, decoded);
    }

    [Fact]
    public void Image_Grayscale()
    {
        var pdf = BuildPdfWithFlateImage(5, 5, 1, colorSpace: "DeviceGray");
        using var doc = Document.Open(pdf);
        var img = doc.Pages[1].Images[1];
        Assert.Equal("DeviceGray", img.ColorSpace);
        Assert.Equal(1, img.ComponentCount);
    }

    [Fact]
    public void Image_CMYK()
    {
        var pdf = BuildPdfWithFlateImage(5, 5, 4, colorSpace: "DeviceCMYK");
        using var doc = Document.Open(pdf);
        var img = doc.Pages[1].Images[1];
        Assert.Equal("DeviceCMYK", img.ColorSpace);
        Assert.Equal(4, img.ComponentCount);
    }

    [Fact]
    public void Image_DCTDecode_IsJpeg()
    {
        // Build a PDF with a fake JPEG (just the markers for testing)
        var jpegData = BuildMinimalJpeg();
        var pdf = BuildPdfWithRawImage(4, 3, jpegData, "DCTDecode", "DeviceRGB");
        using var doc = Document.Open(pdf);
        var img = doc.Pages[1].Images[1];
        Assert.True(img.IsJpeg);
        Assert.Equal("DCTDecode", img.Filter);
    }

    [Fact]
    public void Image_GetJpegBytes_ReturnsRawForJpeg()
    {
        var jpegData = BuildMinimalJpeg();
        var pdf = BuildPdfWithRawImage(4, 3, jpegData, "DCTDecode", "DeviceRGB");
        using var doc = Document.Open(pdf);
        var img = doc.Pages[1].Images[1];
        var jpeg = img.GetJpegBytes();
        Assert.NotNull(jpeg);
        Assert.Equal(jpegData, jpeg);
    }

    [Fact]
    public void Image_GetJpegBytes_ReturnsNullForNonJpeg()
    {
        var pdf = BuildPdfWithFlateImage(5, 5, 3);
        using var doc = Document.Open(pdf);
        var img = doc.Pages[1].Images[1];
        Assert.Null(img.GetJpegBytes());
    }

    [Fact]
    public void Image_Save_ToStream()
    {
        var pixelData = new byte[12]; // 2x2 RGB
        for (var i = 0; i < pixelData.Length; i++)
            pixelData[i] = (byte)(i + 100);

        var pdf = BuildPdfWithFlateImage(2, 2, 3, pixelData);
        using var doc = Document.Open(pdf);
        var img = doc.Pages[1].Images[1];

        using var ms = new MemoryStream();
        img.Save(ms);
        var result = ms.ToArray();
        // Save outputs PNG format — check PNG signature
        Assert.True(result.Length > 8);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, result[..8]);
    }

    [Fact]
    public void NoImages_EmptyCollection()
    {
        var pdf = Helpers.PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);
        Assert.Empty(doc.Pages[1].Images);
    }

    [Fact]
    public void MultipleImages()
    {
        var pdf = BuildPdfWithMultipleImages();
        using var doc = Document.Open(pdf);
        Assert.Equal(2, doc.Pages[1].Images.Count);
        Assert.Equal("Im0", doc.Pages[1].Images[1].Name);
        Assert.Equal("Im1", doc.Pages[1].Images[2].Name);
    }

    #region PDF builders

    private static byte[] BuildPdfWithFlateImage(int width, int height, int components,
        byte[]? pixelData = null, string colorSpace = "DeviceRGB")
    {
        pixelData ??= new byte[width * height * components];

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionMode.Compress, leaveOpen: true))
            zlib.Write(pixelData);
        var compressedBytes = compressed.ToArray();

        return BuildPdfWithRawImage(width, height, compressedBytes, "FlateDecode", colorSpace,
            bpc: 8, length: compressedBytes.Length);
    }

    private static byte[] BuildPdfWithRawImage(int width, int height, byte[] imageData,
        string filter, string colorSpace, int bpc = 8, int? length = null)
    {
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.4\n");

        var catalogOffset = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        var imgOffset = ms.Position;
        Write($"4 0 obj\n<< /Type /XObject /Subtype /Image /Width {width} /Height {height} " +
              $"/BitsPerComponent {bpc} /ColorSpace /{colorSpace} /Filter /{filter} " +
              $"/Length {length ?? imageData.Length} >>\nstream\n");
        ms.Write(imageData);
        Write("\nendstream\nendobj\n");

        var pageOffset = ms.Position;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] " +
              "/Resources << /XObject << /Im0 4 0 R >> >> >>\nendobj\n");

        var xrefOffset = ms.Position;
        Write("xref\n0 5\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        Write($"{imgOffset:D10} 00000 n \n");
        Write("trailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");
        return ms.ToArray();
    }

    private static byte[] BuildPdfWithMultipleImages()
    {
        var pix1 = new byte[12]; // 2x2 RGB
        var pix2 = new byte[12];
        using var c1 = new MemoryStream();
        using (var z1 = new ZLibStream(c1, CompressionMode.Compress, leaveOpen: true)) z1.Write(pix1);
        using var c2 = new MemoryStream();
        using (var z2 = new ZLibStream(c2, CompressionMode.Compress, leaveOpen: true)) z2.Write(pix2);
        var cd1 = c1.ToArray();
        var cd2 = c2.ToArray();

        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.4\n");

        var catalogOffset = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        var img1Offset = ms.Position;
        Write($"4 0 obj\n<< /Type /XObject /Subtype /Image /Width 2 /Height 2 /BitsPerComponent 8 " +
              $"/ColorSpace /DeviceRGB /Filter /FlateDecode /Length {cd1.Length} >>\nstream\n");
        ms.Write(cd1);
        Write("\nendstream\nendobj\n");

        var img2Offset = ms.Position;
        Write($"5 0 obj\n<< /Type /XObject /Subtype /Image /Width 2 /Height 2 /BitsPerComponent 8 " +
              $"/ColorSpace /DeviceRGB /Filter /FlateDecode /Length {cd2.Length} >>\nstream\n");
        ms.Write(cd2);
        Write("\nendstream\nendobj\n");

        var pageOffset = ms.Position;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] " +
              "/Resources << /XObject << /Im0 4 0 R /Im1 5 0 R >> >> >>\nendobj\n");

        var xrefOffset = ms.Position;
        Write("xref\n0 6\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        Write($"{img1Offset:D10} 00000 n \n");
        Write($"{img2Offset:D10} 00000 n \n");
        Write("trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");
        return ms.ToArray();
    }

    private static byte[] BuildMinimalJpeg()
    {
        // Minimal valid JPEG: SOI + APP0 + SOF0 + SOS + EOI
        // This is just enough to pass as JPEG data in a PDF
        return [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x02, 0xFF, 0xD9];
    }

    #endregion
}
