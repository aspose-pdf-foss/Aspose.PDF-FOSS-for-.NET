using Aspose.Pdf;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests;

public class PageManipulationTests
{
    [Fact]
    public void Add_DefaultSize_AddsPage()
    {
        using var doc = Document.Create();
        Assert.Equal(0, doc.PageCount);

        var page = doc.Pages.Add();
        Assert.Equal(1, doc.PageCount);
        // Aspose.Pdf defaults to A4 (595 x 842 — PageInfo ctor +
        // PageSize.A4 literal), not US Letter; FOSS matches.
        Assert.Equal(595, page.MediaBox.URX);
        Assert.Equal(842, page.MediaBox.URY);
    }

    [Fact]
    public void Add_CustomSize()
    {
        using var doc = Document.Create();
        var page = doc.Pages.Add(width: 842, height: 595); // A4 landscape
        Assert.Equal(842, page.MediaBox.URX);
        Assert.Equal(595, page.MediaBox.URY);
    }

    [Fact]
    public void Add_MultiplePagesPreservesOrder()
    {
        using var doc = Document.Create();
        doc.Pages.Add(100, 200);
        doc.Pages.Add(300, 400);
        doc.Pages.Add(500, 600);
        Assert.Equal(3, doc.PageCount);
        Assert.Equal(100, doc.Pages[1].MediaBox.URX);
        Assert.Equal(300, doc.Pages[2].MediaBox.URX);
        Assert.Equal(500, doc.Pages[3].MediaBox.URX);
    }

    [Fact]
    public void Delete_RemovesPage()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        Assert.Equal(1, doc.PageCount);

        doc.Pages.Delete(1);
        Assert.Equal(0, doc.PageCount);
    }

    [Fact]
    public void Delete_OutOfRange_Throws()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        Assert.Throws<ArgumentOutOfRangeException>(() => doc.Pages.Delete(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => doc.Pages.Delete(2));
    }

    [Fact]
    public void Insert_AtBeginning()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        var inserted = doc.Pages.Insert(1, 100, 200);
        Assert.Equal(2, doc.PageCount);
        Assert.Equal(100, doc.Pages[1].MediaBox.URX);
        Assert.Equal(612, doc.Pages[2].MediaBox.URX); // original page moved
    }

    [Fact]
    public void Page_Width_Height()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        var page = doc.Pages[1];
        Assert.Equal(612, page.Width);
        Assert.Equal(792, page.Height);
    }

    [Fact]
    public void Page_Number_IsOneBased()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        Assert.Equal(1, doc.Pages[1].Number);
    }

    [Fact]
    public void Add_SaveAndReopen_PreservesCount()
    {
        using var doc = Document.Create();
        doc.Pages.Add();
        doc.Pages.Add();
        var saved = doc.ToArray();

        using var doc2 = Document.Open(saved);
        Assert.Equal(2, doc2.PageCount);
    }
}
