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
