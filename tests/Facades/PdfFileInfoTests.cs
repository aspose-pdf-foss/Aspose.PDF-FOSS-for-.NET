using Aspose.Pdf;
using Aspose.Pdf.Facades;
using Xunit;

namespace Aspose.Pdf.Tests.Facades;

public sealed class PdfFileInfoTests
{
    [Fact]
    public void NumberOfPages()
    {
        var pdf = Helpers.PdfBuilder.BuildMinimal();
        using var info = new PdfFileInfo(pdf);
        Assert.Equal(1, info.NumberOfPages);
    }

    [Fact]
    public void PdfVersion_Is_Available()
    {
        var pdf = Helpers.PdfBuilder.BuildMinimal();
        using var info = new PdfFileInfo(pdf);
        Assert.NotNull(info.PdfVersion);
    }

    [Fact]
    public void IsEncrypted_False_ForPlainPdf()
    {
        var pdf = Helpers.PdfBuilder.BuildMinimal();
        using var info = new PdfFileInfo(pdf);
        Assert.False(info.IsEncrypted);
    }

    [Fact]
    public void PageDimensions_Accessible()
    {
        var pdf = Helpers.PdfBuilder.BuildMinimal();
        using var info = new PdfFileInfo(pdf);
        Assert.True(info.GetPageWidth(1) > 0);
        Assert.True(info.GetPageHeight(1) > 0);
    }

    [Fact]
    public void PageRotation_DefaultZero()
    {
        var pdf = Helpers.PdfBuilder.BuildMinimal();
        using var info = new PdfFileInfo(pdf);
        Assert.Equal(0, info.GetPageRotation(1));
    }

    [Fact]
    public void GetDocumentInfo_ReturnsAllKeys()
    {
        var pdf = Helpers.PdfBuilder.BuildMinimal();
        using var info = new PdfFileInfo(pdf);
        var dict = info.GetDocumentInfo();
        Assert.Contains("Title", dict.Keys);
        Assert.Contains("Author", dict.Keys);
        Assert.Contains("Subject", dict.Keys);
        Assert.Contains("Creator", dict.Keys);
        Assert.Contains("Producer", dict.Keys);
    }

    [Fact]
    public void HasForm_FalseForMinimalPdf()
    {
        var pdf = Helpers.PdfBuilder.BuildMinimal();
        using var info = new PdfFileInfo(pdf);
        Assert.False(info.HasForm);
    }

    [Fact]
    public void HasOutlines_FalseForMinimalPdf()
    {
        var pdf = Helpers.PdfBuilder.BuildMinimal();
        using var info = new PdfFileInfo(pdf);
        Assert.False(info.HasOutlines);
    }

    [Fact]
    public void Stream_Constructor_Works()
    {
        var pdf = Helpers.PdfBuilder.BuildMinimal();
        using var stream = new MemoryStream(pdf);
        using var info = new PdfFileInfo(stream);
        Assert.Equal(1, info.NumberOfPages);
    }

    [Fact]
    public void MultiPage_Document()
    {
        var pdf = Helpers.PdfBuilder.BuildMultiPage(5);
        using var info = new PdfFileInfo(pdf);
        Assert.Equal(5, info.NumberOfPages);
    }

    [Fact]
    public void DocumentInfo_Title_RoundTrip()
    {
        using var doc = Document.Create();
        doc.Pages.Add();
        doc.Info.Title = "Test Title";
        var saved = doc.ToArray();

        using var info = new PdfFileInfo(saved);
        Assert.Equal("Test Title", info.Title);
    }
}
