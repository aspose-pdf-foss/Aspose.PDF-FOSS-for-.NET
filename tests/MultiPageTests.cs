using Aspose.Pdf;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests;

public class MultiPageTests
{
    [Fact]
    public void MultiPage_CorrectCount()
    {
        var data = PdfBuilder.BuildMultiPage(5);
        using var doc = Document.Open(data);
        Assert.Equal(5, doc.PageCount);
    }

    [Fact]
    public void MultiPage_EachPageHasDifferentWidth()
    {
        var data = PdfBuilder.BuildMultiPage(3);
        using var doc = Document.Open(data);
        Assert.Equal(612, doc.Pages[1].MediaBox.URX);
        Assert.Equal(622, doc.Pages[2].MediaBox.URX);
        Assert.Equal(632, doc.Pages[3].MediaBox.URX);
    }

    [Fact]
    public void MultiPage_SinglePage()
    {
        var data = PdfBuilder.BuildMultiPage(1);
        using var doc = Document.Open(data);
        Assert.Equal(1, doc.PageCount);
    }

    [Fact]
    public void MultiPage_TenPages()
    {
        var data = PdfBuilder.BuildMultiPage(10);
        using var doc = Document.Open(data);
        Assert.Equal(10, doc.PageCount);
        for (var i = 1; i <= 10; i++)
            Assert.Equal(i, doc.Pages[i].Number);
    }

    [Fact]
    public void MultiPage_Enumeration()
    {
        var data = PdfBuilder.BuildMultiPage(3);
        using var doc = Document.Open(data);
        var count = 0;
        foreach (var page in doc.Pages)
        {
            Assert.NotNull(page.MediaBox);
            count++;
        }
        Assert.Equal(3, count);
    }

    [Fact]
    public void MultiPage_DeleteMiddlePage()
    {
        var data = PdfBuilder.BuildMultiPage(3);
        using var doc = Document.Open(data);
        doc.Pages.Delete(2);
        Assert.Equal(2, doc.PageCount);
        // Remaining pages should be the first and third original pages
        Assert.Equal(612, doc.Pages[1].MediaBox.URX);
        Assert.Equal(632, doc.Pages[2].MediaBox.URX);
    }

    [Fact]
    public void MultiPage_DeleteMultiplePages()
    {
        var data = PdfBuilder.BuildMultiPage(5);
        using var doc = Document.Open(data);
        doc.Pages.Delete(2, 4); // delete pages 2 and 4
        Assert.Equal(3, doc.PageCount);
    }

    [Fact]
    public void MultiPage_SaveAndReload()
    {
        var data = PdfBuilder.BuildMultiPage(3);
        using var doc = Document.Open(data);
        var saved = doc.ToArray();

        using var doc2 = Document.Open(saved);
        Assert.Equal(3, doc2.PageCount);
    }
}
