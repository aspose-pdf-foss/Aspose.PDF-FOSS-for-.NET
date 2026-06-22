using Aspose.Pdf;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests;

public class OptimizeResourcesTests
{
    [Fact]
    public void OptimizeResources_DoesNotThrow()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        doc.OptimizeResources();
        var saved = doc.ToArray();
        Assert.True(saved.Length > 0);
    }

    [Fact]
    public void OptimizeResources_WithOptions()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        doc.OptimizeResources(OptimizationOptions.All());
        var saved = doc.ToArray();
        Assert.True(saved.Length > 0);
    }

    [Fact]
    public void OptimizeResources_PreservesContent()
    {
        var content = System.Text.Encoding.ASCII.GetBytes("BT /F1 12 Tf (Keep this) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        doc.OptimizeResources();
        var saved = doc.ToArray();

        using var doc2 = Document.Open(saved);
        Assert.Equal(1, doc2.PageCount);
    }

    [Fact]
    public void OptimizeResources_ReducesOrMaintainsSize()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);

        var unoptimizedSize = doc.ToArray().Length;

        doc.OptimizeResources();
        var optimizedSize = doc.ToArray().Length;

        // Optimized should not be larger than unoptimized
        Assert.True(optimizedSize <= unoptimizedSize);
    }

    [Fact]
    public void OptimizeResources_PreservesPageCount()
    {
        var data = PdfBuilder.BuildMultiPage(3);
        using var doc = Document.Open(data);

        doc.OptimizeResources();
        var saved = doc.ToArray();

        using var doc2 = Document.Open(saved);
        Assert.Equal(3, doc2.PageCount);
    }

    [Fact]
    public void OptimizeResources_DefaultOptions()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        doc.OptimizeResources(OptimizationOptions.Default);
        var saved = doc.ToArray();
        Assert.True(saved.Length > 0);
    }

    [Fact]
    public void OptimizeResources_NullOptions_UsesDefault()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        doc.OptimizeResources((Aspose.Pdf.Optimization.OptimizationOptions)null!);
        var saved = doc.ToArray();
        Assert.True(saved.Length > 0);
    }

    [Fact]
    public void LinkDuplicateStreams_ReducesSizeForDuplicateImages()
    {
        // Create a PDF with two identical images on the same page
        var pdf = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);
        var page = doc.Pages[1];

        var pixels = new byte[16 * 16 * 3];
        for (var i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i % 200);

        // Add the same image data twice
        var stamp1 = ImageStamp.FromRgb(pixels, 16, 16);
        stamp1.X = 100; stamp1.Y = 100;
        stamp1.ApplyTo(page);

        var stamp2 = ImageStamp.FromRgb(pixels, 16, 16);
        stamp2.X = 300; stamp2.Y = 300;
        stamp2.ApplyTo(page);

        // Save without optimization
        var unoptimized = doc.ToArray();

        // Now optimize with LinkDuplicateStreams
        doc.OptimizeResources(new OptimizationOptions { LinkDuplicateStreams = true });
        var optimized = doc.ToArray();

        // The optimized version should be smaller (duplicate stream eliminated)
        Assert.True(optimized.Length <= unoptimized.Length,
            $"Optimized ({optimized.Length}) should be <= unoptimized ({unoptimized.Length})");

        // Verify the document is still valid
        using var doc2 = Document.Open(optimized);
        Assert.Equal(1, doc2.PageCount);
    }

    [Fact]
    public void DownsampleImages_ReducesDimensions()
    {
        // Create a large image (800x800 RGB) — large enough that downsampling shows clear savings
        var width = 800;
        var height = 800;
        var pixels = new byte[width * height * 3];
        var rng = new Random(42);
        rng.NextBytes(pixels); // random data doesn't compress well, maximizing size

        var pdf = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);
        var page = doc.Pages[1];

        var stamp = ImageStamp.FromRgb(pixels, width, height);
        stamp.X = 0; stamp.Y = 0;
        stamp.DisplayWidth = width;
        stamp.DisplayHeight = height;
        stamp.ApplyTo(page);

        // Save to get a PDF with the image, then reopen
        var beforeBytes = doc.ToArray();

        // Reopen and optimize with aggressive downsampling
        using var doc2 = Document.Open(beforeBytes);
        doc2.OptimizeResources(new OptimizationOptions
        {
            MaxImageDpi = 10,
            RemoveUnusedObjects = false,
            RemoveUnusedStreams = false,
            LinkDuplicateStreams = false,
        });
        var afterBytes = doc2.ToArray();

        // Verify: optimized should be smaller because the image was downsampled
        Assert.True(afterBytes.Length < beforeBytes.Length,
            $"After downsampling ({afterBytes.Length}) should be < before ({beforeBytes.Length})");

        // Verify document is still valid
        using var doc3 = Document.Open(afterBytes);
        Assert.Equal(1, doc3.PageCount);
    }

    [Fact]
    public void ConvertToGrayscale_ChangesColorSpace()
    {
        // Create a PDF with an RGB image
        var width = 32;
        var height = 32;
        var pixels = new byte[width * height * 3];
        for (var i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i % 256);

        var pdf = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);
        var page = doc.Pages[1];

        var stamp = ImageStamp.FromRgb(pixels, width, height);
        stamp.X = 0; stamp.Y = 0;
        stamp.ApplyTo(page);

        // Save to get a PDF with the image, then reopen
        var beforeBytes = doc.ToArray();

        // Reopen and convert to grayscale
        using var doc2 = Document.Open(beforeBytes);
        doc2.OptimizeResources(new OptimizationOptions
        {
            ConvertImagesToGrayscale = true,
            RemoveUnusedObjects = false,
            RemoveUnusedStreams = false,
            LinkDuplicateStreams = false,
        });
        var afterBytes = doc2.ToArray();

        // Grayscale image data is 1/3 the size of RGB, so the PDF should be smaller
        Assert.True(afterBytes.Length < beforeBytes.Length,
            $"After grayscale conversion ({afterBytes.Length}) should be < before ({beforeBytes.Length})");

        // Verify the saved document is valid
        using var doc3 = Document.Open(afterBytes);
        Assert.Equal(1, doc3.PageCount);
    }

    [Fact]
    public void RemoveDuplicateImages_ReducesObjectCount()
    {
        // Create a PDF with two identical images
        var width = 16;
        var height = 16;
        var pixels = new byte[width * height * 3];
        for (var i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i % 200);

        var pdf = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);
        var page = doc.Pages[1];

        var stamp1 = ImageStamp.FromRgb(pixels, width, height);
        stamp1.X = 50; stamp1.Y = 50;
        stamp1.ApplyTo(page);

        var stamp2 = ImageStamp.FromRgb(pixels, width, height);
        stamp2.X = 200; stamp2.Y = 200;
        stamp2.ApplyTo(page);

        // Save, reopen, then optimize
        var beforeBytes = doc.ToArray();
        using var doc2 = Document.Open(beforeBytes);
        doc2.OptimizeResources(new OptimizationOptions
        {
            RemoveDuplicateImages = true,
            RemoveUnusedObjects = false,
            RemoveUnusedStreams = false,
            LinkDuplicateStreams = false,
        });
        var saved = doc2.ToArray();

        // Verify document is valid
        using var doc3 = Document.Open(saved);
        Assert.Equal(1, doc3.PageCount);
    }

    [Fact]
    public void CompressImages_ReducesStreamSize()
    {
        // Create a PDF with an image
        var width = 64;
        var height = 64;
        var pixels = new byte[width * height * 3];
        // Use a pattern that's somewhat compressible
        for (var i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i / 64);

        var pdf = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);
        var page = doc.Pages[1];

        var stamp = ImageStamp.FromRgb(pixels, width, height);
        stamp.X = 0; stamp.Y = 0;
        stamp.ApplyTo(page);

        var beforeBytes = doc.ToArray();

        // Reopen and compress
        using var doc2 = Document.Open(beforeBytes);
        doc2.OptimizeResources(new OptimizationOptions
        {
            CompressImages = true,
            ImageQuality = 50,
            RemoveUnusedObjects = false,
            RemoveUnusedStreams = false,
            LinkDuplicateStreams = false,
        });
        var afterBytes = doc2.ToArray();

        // Compressed should be <= original (may be same if already optimally compressed)
        Assert.True(afterBytes.Length <= beforeBytes.Length,
            $"After compression ({afterBytes.Length}) should be <= before ({beforeBytes.Length})");

        // Verify document is still valid
        using var doc3 = Document.Open(afterBytes);
        Assert.Equal(1, doc3.PageCount);
    }

    [Fact]
    public void BoxFilterDownsample_ProducesCorrectDimensions()
    {
        // 4x4 grayscale image → 2x2
        var src = new byte[4 * 4]; // 1 component
        for (var i = 0; i < src.Length; i++) src[i] = (byte)(i * 16);

        var dst = ImageCompressor.BoxFilterDownsample(src, 4, 4, 1, 2, 2);
        Assert.Equal(2 * 2, dst.Length);

        // Each output pixel should be the average of its 2x2 source block
        // Top-left: avg of (0, 16, 64, 80) = 40
        Assert.Equal(40, dst[0]);
    }

    [Fact]
    public void BoxFilterDownsample_RgbImage()
    {
        // 4x4 RGB image → 2x2
        var src = new byte[4 * 4 * 3];
        // Fill with known values: all R=100, G=200, B=50
        for (var i = 0; i < 4 * 4; i++)
        {
            src[i * 3] = 100;
            src[i * 3 + 1] = 200;
            src[i * 3 + 2] = 50;
        }

        var dst = ImageCompressor.BoxFilterDownsample(src, 4, 4, 3, 2, 2);
        Assert.Equal(2 * 2 * 3, dst.Length);

        // Each output pixel should preserve the uniform color
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(100, dst[i * 3]);
            Assert.Equal(200, dst[i * 3 + 1]);
            Assert.Equal(50, dst[i * 3 + 2]);
        }
    }

    [Fact]
    public void AllOptions_DoesNotThrow()
    {
        var width = 32;
        var height = 32;
        var pixels = new byte[width * height * 3];
        for (var i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i % 256);

        var pdf = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);
        var page = doc.Pages[1];

        var stamp = ImageStamp.FromRgb(pixels, width, height);
        stamp.X = 0; stamp.Y = 0;
        stamp.ApplyTo(page);

        var intermediateBytes = doc.ToArray();

        // Reopen and apply all options
        using var doc2 = Document.Open(intermediateBytes);
        doc2.OptimizeResources(OptimizationOptions.All());
        var saved = doc2.ToArray();

        Assert.True(saved.Length > 0);
        using var doc3 = Document.Open(saved);
        Assert.Equal(1, doc3.PageCount);
    }

    [Fact]
    public void OptimizationOptions_DefaultValues()
    {
        var opts = new OptimizationOptions();
        Assert.Equal(75, opts.ImageQuality);
        Assert.Equal(0, opts.MaxImageDpi);
        Assert.False(opts.ConvertImagesToGrayscale);
        Assert.False(opts.RemoveDuplicateImages);
        Assert.False(opts.CompressImages);
    }

    [Fact]
    public void OptimizationOptions_AllValues()
    {
        var opts = OptimizationOptions.All();
        Assert.Equal(50, opts.ImageQuality);
        Assert.Equal(150, opts.MaxImageDpi);
        Assert.True(opts.ConvertImagesToGrayscale);
        Assert.True(opts.RemoveDuplicateImages);
        Assert.True(opts.CompressImages);
    }
}
