using Aspose.Pdf;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests;

public class DocumentTests
{
    [Fact]
    public void Open_MinimalPdf_Succeeds()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        Assert.NotNull(doc);
    }

    [Fact]
    public void PageCount_MinimalPdf_Returns1()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        Assert.Equal(1, doc.PageCount);
    }

    [Fact]
    public void Pages_MinimalPdf_SinglePage()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        Assert.Single(doc.Pages);
    }

    [Fact]
    public void Pages_At_OneBasedAccess()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        var page = doc.Pages.At(1);
        Assert.Equal(1, page.Number);
    }

    [Fact]
    public void Pages_At_OutOfRange_Throws()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        Assert.Throws<ArgumentOutOfRangeException>(() => doc.Pages.At(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => doc.Pages.At(2));
    }

    [Fact]
    public void Pages_Indexer_ZeroBased()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        var page = doc.Pages[1];
        Assert.Equal(1, page.Number);
    }

    [Fact]
    public void Page_MediaBox_CorrectDimensions()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        var page = doc.Pages[1];
        var mb = page.MediaBox;
        Assert.Equal(0, mb.LLX);
        Assert.Equal(0, mb.LLY);
        Assert.Equal(612, mb.URX);
        Assert.Equal(792, mb.URY);
    }

    [Fact]
    public void Page_CropBox_DefaultsToMediaBox()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        var page = doc.Pages[1];
        Assert.Equal(page.MediaBox.Width, page.CropBox.Width);
        Assert.Equal(page.MediaBox.Height, page.CropBox.Height);
    }

    [Fact]
    public void Page_Rotate_DefaultsToZero()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        Assert.Equal(0, doc.Pages[1].RotateDegrees);
    }

    [Fact]
    public void PdfVersion_MinimalPdf_Returns14()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        Assert.Equal("1.4", doc.PdfVersion);
    }

    [Fact]
    public void Info_MinimalPdf_NoFields()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        Assert.Null(doc.Info.Title);
        Assert.Null(doc.Info.Author);
    }

    [Fact]
    public void Pages_Enumerable_ForEach()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        var count = 0;
        foreach (var page in doc.Pages)
        {
            count++;
            Assert.NotNull(page.MediaBox);
        }
        Assert.Equal(1, count);
    }
}
