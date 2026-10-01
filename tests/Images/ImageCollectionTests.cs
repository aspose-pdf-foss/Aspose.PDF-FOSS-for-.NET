using Aspose.Pdf;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Images;

/// <summary>
/// Ported from TypeScript: Images/ImageTests.ts
/// Tests ImageCollection, ImageXObject properties, and image extraction.
/// </summary>
public class ImageCollectionTests
{
    // ── ImageCollection — PDF with images ────────────────────────────────

    [Fact]
    public void Page_Images_ReturnsImageCollection()
    {
        var data = PdfBuilder.BuildWithUncompressedImage(4, 4);
        using var doc = Document.Open(data);
        var images = doc.Pages[1].Images;
        Assert.NotNull(images);
    }

    [Fact]
    public void ImageCollection_HasAtLeastOneImage()
    {
        var data = PdfBuilder.BuildWithUncompressedImage(4, 4);
        using var doc = Document.Open(data);
        Assert.True(doc.Pages[1].Images.Count > 0);
    }

    [Fact]
    public void ImageCollection_Count_IsNonNegativeInteger()
    {
        var data = PdfBuilder.BuildWithUncompressedImage(4, 4);
        using var doc = Document.Open(data);
        Assert.True(doc.Pages[1].Images.Count >= 0);
    }

    [Fact]
    public void ImageCollection_IsEnumerable()
    {
        var data = PdfBuilder.BuildWithUncompressedImage(4, 4);
        using var doc = Document.Open(data);
        var images = doc.Pages[1].Images;
        var n = 0;
        foreach (var _ in images) n++;
        Assert.Equal(images.Count, n);
    }

    // ── ImageXObject properties ─────────────────────────────────────────

    [Fact]
    public void ImageXObject_HasNonEmptyName()
    {
        var data = PdfBuilder.BuildWithUncompressedImage(4, 4);
        using var doc = Document.Open(data);
        foreach (var img in doc.Pages[1].Images)
        {
            Assert.NotNull(img.Name);
            Assert.NotEmpty(img.Name);
        }
    }

    [Fact]
    public void ImageXObject_HasPositiveWidth()
    {
        var data = PdfBuilder.BuildWithUncompressedImage(4, 4);
        using var doc = Document.Open(data);
        foreach (var img in doc.Pages[1].Images)
        {
            Assert.True(img.Width > 0);
        }
    }

    [Fact]
    public void ImageXObject_HasPositiveHeight()
    {
        var data = PdfBuilder.BuildWithUncompressedImage(4, 4);
        using var doc = Document.Open(data);
        foreach (var img in doc.Pages[1].Images)
        {
            Assert.True(img.Height > 0);
        }
    }

    [Fact]
    public void ImageXObject_HasBitsPerComponent()
    {
        var data = PdfBuilder.BuildWithUncompressedImage(4, 4);
        using var doc = Document.Open(data);
        foreach (var img in doc.Pages[1].Images)
        {
            Assert.True(img.BitsPerComponent >= 1);
        }
    }

    [Fact]
    public void ImageXObject_HasColorSpace()
    {
        var data = PdfBuilder.BuildWithUncompressedImage(4, 4);
        using var doc = Document.Open(data);
        foreach (var img in doc.Pages[1].Images)
        {
            Assert.NotNull(img.ColorSpace);
        }
    }

    // ── Pages without images ────────────────────────────────────────────

    [Fact]
    public void TextOnlyPage_HasZeroImages()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        Assert.True(doc.Pages[1].Images.Count == 0);
    }

    // ── Image filter detection ──────────────────────────────────────────

    [Fact]
    public void UncompressedImage_IsNotJpeg()
    {
        var data = PdfBuilder.BuildWithUncompressedImage(4, 4);
        using var doc = Document.Open(data);
        foreach (var img in doc.Pages[1].Images)
        {
            Assert.False(img.IsJpeg);
        }
    }

    [Fact]
    public void ImageXObject_Filter_IsNullForUncompressed()
    {
        var data = PdfBuilder.BuildWithUncompressedImage(4, 4);
        using var doc = Document.Open(data);
        foreach (var img in doc.Pages[1].Images)
        {
            // Uncompressed image has no filter
            Assert.Null(img.Filter);
        }
    }

    [Fact]
    public void ImageXObject_CorrectDimensions()
    {
        var data = PdfBuilder.BuildWithUncompressedImage(8, 6);
        using var doc = Document.Open(data);
        var img = doc.Pages[1].Images[1];
        Assert.Equal(8, img.Width);
        Assert.Equal(6, img.Height);
    }

    // ── Named images ────────────────────────────────────────────────────

    [Fact]
    public void NamedImage_HasExpectedName()
    {
        var data = PdfBuilder.BuildWithNamedImage("TestImg", 255, 0, 0, 2, 2);
        using var doc = Document.Open(data);
        var img = doc.Pages[1].Images[1];
        Assert.Equal("TestImg", img.Name);
    }

    [Fact]
    public void NamedImage_HasCorrectSize()
    {
        var data = PdfBuilder.BuildWithNamedImage("Im0", 0, 128, 255, 3, 5);
        using var doc = Document.Open(data);
        var img = doc.Pages[1].Images[1];
        Assert.Equal(3, img.Width);
        Assert.Equal(5, img.Height);
    }

    // ── ComponentCount ───────────────────────────────────────────────────

    [Fact]
    public void ImageXObject_ComponentCount_RgbIs3()
    {
        var data = PdfBuilder.BuildWithUncompressedImage(2, 2);
        using var doc = Document.Open(data);
        foreach (var img in doc.Pages[1].Images)
        {
            Assert.Equal(3, img.ComponentCount);
        }
    }

    // ── Soft mask / Image mask ──────────────────────────────────────────

    [Fact]
    public void UncompressedImage_HasNoSoftMask()
    {
        var data = PdfBuilder.BuildWithUncompressedImage(2, 2);
        using var doc = Document.Open(data);
        foreach (var img in doc.Pages[1].Images)
        {
            Assert.False(img.HasSoftMask);
        }
    }

    [Fact]
    public void UncompressedImage_IsNotImageMask()
    {
        var data = PdfBuilder.BuildWithUncompressedImage(2, 2);
        using var doc = Document.Open(data);
        foreach (var img in doc.Pages[1].Images)
        {
            Assert.False(img.IsImageMask);
        }
    }

    // ── ToPng / GetRawData ──────────────────────────────────────────────

    [Fact]
    public void ImageXObject_ToPng_ReturnsNonEmptyData()
    {
        var data = PdfBuilder.BuildWithUncompressedImage(2, 2);
        using var doc = Document.Open(data);
        var img = doc.Pages[1].Images[1];
        var pngBytes = img.ToPng();
        Assert.NotNull(pngBytes);
        Assert.True(pngBytes.Length > 0);
        // PNG signature
        Assert.Equal(0x89, pngBytes[0]);
        Assert.Equal(0x50, pngBytes[1]);
    }

    [Fact]
    public void ImageXObject_ToPng_AnIndexedImagesSoftMaskIsItsAlpha()
    {
        // Two pixels of an indexed image (palette: black, red), a soft mask hiding the first: what it hides shows the page.
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Contents 4 0 R /Resources << /XObject << /Im0 5 0 R >> >> >>",
            "<< /Length 32 >>\nstream\nq 100 0 0 50 50 50 cm /Im0 Do Q\n\nendstream",
            "<< /Type /XObject /Subtype /Image /Width 2 /Height 1 /BitsPerComponent 8 /ColorSpace [/Indexed /DeviceRGB 1 <000000FF0000>] "
            + "/SMask 6 0 R /Filter /ASCIIHexDecode /Length 5 >>\nstream\n0001>\nendstream",
            "<< /Type /XObject /Subtype /Image /Width 2 /Height 1 /BitsPerComponent 8 /ColorSpace /DeviceGray /Filter /ASCIIHexDecode /Length 5 >>\nstream\n00FF>\nendstream",
        };
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Compat.Latin1.GetBytes(s));
        Write("%PDF-1.7\n");
        var offsets = new long[objects.Count];
        for (var i = 0; i < objects.Count; i++)
        {
            offsets[i] = ms.Position;
            Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = ms.Position;
        Write($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) Write($"{o:D10} 00000 n \n");
        Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        using var doc = Document.Open(ms.ToArray());

        var png = doc.Pages[1].Images[1].ToPng();
        // The PNG's colour type (byte 25 of its header): 6, RGB with alpha.
        Assert.Equal(6, png[25]);
        var (pixels, _, _, hasAlpha) = Aspose.Pdf.Facades.PdfFileMend.DecodePng(png);
        Assert.True(hasAlpha);
        Assert.Equal(new byte[] { 0, 0, 0, 0, 255, 0, 0, 255 }, pixels);
    }

    [Fact]
    public void ImageXObject_GetRawData_ReturnsBytes()
    {
        var data = PdfBuilder.BuildWithUncompressedImage(2, 2);
        using var doc = Document.Open(data);
        var img = doc.Pages[1].Images[1];
        var rawBytes = img.GetRawData();
        Assert.NotNull(rawBytes);
        Assert.True(rawBytes.Length > 0);
    }
}
