using Aspose.Pdf;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests;

public class ImageCompressionTests
{
    [Fact]
    public void CompressImages_DoesNotThrow()
    {
        var data = PdfBuilder.BuildWithUncompressedImage(100, 100);
        using var doc = Document.Open(data);
        var opts = new OptimizationOptions { CompressImages = true, ImageQuality = 75 };
        doc.OptimizeResources(opts);
        var saved = doc.ToArray();
        Assert.True(saved.Length > 0);
    }

    [Fact]
    public void CompressImages_ProducesValidPdf_ForUncompressedImage()
    {
        var data = PdfBuilder.BuildWithUncompressedImage(50, 50);
        using var doc = Document.Open(data);

        var opts = new OptimizationOptions { CompressImages = true };
        doc.OptimizeResources(opts);
        var saved = doc.ToArray();

        // The saved PDF should be smaller than the original (which has raw uncompressed image data)
        Assert.True(saved.Length < data.Length,
            $"Expected saved ({saved.Length}) < original ({data.Length})");

        // Verify it's still a valid PDF
        using var doc2 = Document.Open(saved);
        Assert.Equal(1, doc2.PageCount);
    }

    [Fact]
    public void CompressImages_PreservesPageCount()
    {
        var data = PdfBuilder.BuildWithUncompressedImage(20, 20);
        using var doc = Document.Open(data);
        var opts = new OptimizationOptions { CompressImages = true };
        doc.OptimizeResources(opts);
        var saved = doc.ToArray();

        using var doc2 = Document.Open(saved);
        Assert.Equal(1, doc2.PageCount);
    }

    [Fact]
    public void CompressImages_WithMinimalPdf_DoesNotThrow()
    {
        // No images — should be a no-op
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        var opts = new OptimizationOptions { CompressImages = true };
        doc.OptimizeResources(opts);
        var saved = doc.ToArray();
        Assert.True(saved.Length > 0);
    }
}

public class FontSubsetTests
{
    [Fact]
    public void SubsetFonts_DoesNotThrow()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        var opts = new OptimizationOptions { SubsetFonts = true };
        doc.OptimizeResources(opts);
        var saved = doc.ToArray();
        Assert.True(saved.Length > 0);
    }

    [Fact]
    public void SubsetFonts_WithTextContent_DoesNotThrow()
    {
        var content = System.Text.Encoding.ASCII.GetBytes("BT /F1 12 Tf (Test) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);
        var opts = new OptimizationOptions { SubsetFonts = true };
        doc.OptimizeResources(opts);
        var saved = doc.ToArray();
        Assert.True(saved.Length > 0);

        using var doc2 = Document.Open(saved);
        Assert.Equal(1, doc2.PageCount);
    }

    [Fact]
    public void RemoveMetadata_RemovesMetadataEntry()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        var opts = new OptimizationOptions { RemoveMetadata = true };
        doc.OptimizeResources(opts);
        var saved = doc.ToArray();

        using var doc2 = Document.Open(saved);
        Assert.False(doc2.HasMetadata);
    }
}
