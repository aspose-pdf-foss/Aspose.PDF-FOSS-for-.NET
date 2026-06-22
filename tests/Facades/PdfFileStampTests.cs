using Aspose.Pdf;
using Aspose.Pdf.Facades;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Facades;

public class PdfFileStampTests
{
    [Fact]
    public void AddTextStamp_ModifiesPdf()
    {
        var input = PdfBuilder.BuildMinimal();
        var facade = new PdfFileStamp();
        var result = facade.AddTextStamp(input, "APPROVED");
        Assert.True(result.Length > input.Length);
    }

    [Fact]
    public void AddPageNumbers_MultiPage()
    {
        var input = PdfBuilder.BuildMultiPage(3);
        var facade = new PdfFileStamp();
        var result = facade.AddPageNumbers(input);
        Assert.True(result.Length > input.Length);

        using var doc = Document.Open(result);
        Assert.Equal(3, doc.PageCount);
    }

    [Fact]
    public void AddHeader_AddsContent()
    {
        var input = PdfBuilder.BuildMinimal();
        var facade = new PdfFileStamp();
        var result = facade.AddHeader(input, "Company Name");
        Assert.True(result.Length > input.Length);
    }

    [Fact]
    public void AddFooter_AddsContent()
    {
        var input = PdfBuilder.BuildMinimal();
        var facade = new PdfFileStamp();
        var result = facade.AddFooter(input, "Confidential");
        Assert.True(result.Length > input.Length);
    }

    [Fact]
    public void AddWatermark_AddsContent()
    {
        var input = PdfBuilder.BuildMinimal();
        var facade = new PdfFileStamp();
        var result = facade.AddWatermark(input, "DRAFT");
        Assert.True(result.Length > input.Length);
    }

    [Fact]
    public void AddImageStamp_AddsImageToAllPages()
    {
        var input = PdfBuilder.BuildMultiPage(2);
        var facade = new PdfFileStamp();

        var pixels = new byte[4 * 4 * 3];
        for (var i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i % 128);

        var result = facade.AddImageStamp(input, pixels, 4, 4,
            displayWidth: 100, displayHeight: 100);

        Assert.True(result.Length > input.Length);
        using var doc = Document.Open(result);
        Assert.Equal(2, doc.PageCount);
        // Each page should have at least one image
        Assert.True(doc.Pages[1].Images.Count >= 1);
        Assert.True(doc.Pages[2].Images.Count >= 1);
    }

    [Fact]
    public void AddGrayscaleImageStamp_AddsImage()
    {
        var input = PdfBuilder.BuildMinimal();
        var facade = new PdfFileStamp();

        var pixels = new byte[8 * 8]; // 8x8 grayscale
        for (var i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i * 4);

        var result = facade.AddGrayscaleImageStamp(input, pixels, 8, 8,
            displayWidth: 50, displayHeight: 50);

        Assert.True(result.Length > input.Length);
        using var doc = Document.Open(result);
        Assert.True(doc.Pages[1].Images.Count >= 1);
    }
}
