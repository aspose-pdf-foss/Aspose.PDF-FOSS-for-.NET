using System.Text;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests;

public class PageImportTests
{
    [Fact]
    public void ImportPage_AppendSinglePage_IncreasesPageCount()
    {
        // Create a 1-page target document
        var targetBytes = PdfBuilder.BuildMinimal();
        using var target = Document.Open(targetBytes);
        Assert.Equal(1, target.PageCount);

        // Create a 3-page source document
        var sourceBytes = PdfBuilder.BuildMultiPage(3);
        using var source = Document.Open(sourceBytes);
        Assert.Equal(3, source.PageCount);

        // Import page 2 from source
        target.ImportPage(source, 2);

        // Save and reopen
        var saved = target.ToArray();
        using var result = Document.Open(saved);
        Assert.Equal(2, result.PageCount);
    }

    [Fact]
    public void ImportPages_MultiplePages_AllAdded()
    {
        var targetBytes = PdfBuilder.BuildMinimal();
        using var target = Document.Open(targetBytes);

        var sourceBytes = PdfBuilder.BuildMultiPage(5);
        using var source = Document.Open(sourceBytes);

        // Import pages 1, 3, 5
        target.ImportPages(source, [1, 3, 5]);

        var saved = target.ToArray();
        using var result = Document.Open(saved);
        Assert.Equal(4, result.PageCount); // 1 original + 3 imported
    }

    [Fact]
    public void ImportPage_AtSpecificPosition_InsertsCorrectly()
    {
        // Create a 2-page target with different-sized pages
        var targetBytes = PdfBuilder.BuildMultiPage(2);
        using var target = Document.Open(targetBytes);
        var originalPage1Width = target.Pages[1].MediaBox.Width;
        var originalPage2Width = target.Pages[2].MediaBox.Width;

        // Create a source document
        var sourceBytes = PdfBuilder.BuildMinimal();
        using var source = Document.Open(sourceBytes);
        var sourcePageWidth = source.Pages[1].MediaBox.Width;

        // Insert at position 2 (between page 1 and page 2)
        target.ImportPage(source, 1, insertAt: 2);

        var saved = target.ToArray();
        using var result = Document.Open(saved);
        Assert.Equal(3, result.PageCount);

        // Page 1 should be original page 1
        Assert.Equal(originalPage1Width, result.Pages[1].MediaBox.Width);
        // Page 2 should be the imported page
        Assert.Equal(sourcePageWidth, result.Pages[2].MediaBox.Width);
        // Page 3 should be original page 2
        Assert.Equal(originalPage2Width, result.Pages[3].MediaBox.Width);
    }

    [Fact]
    public void ImportPages_AtSpecificPosition_InsertsInOrder()
    {
        var targetBytes = PdfBuilder.BuildMinimal();
        using var target = Document.Open(targetBytes);

        var sourceBytes = PdfBuilder.BuildMultiPage(3);
        using var source = Document.Open(sourceBytes);

        // Insert 2 pages at position 1 (before existing page)
        target.ImportPages(source, [1, 2], insertAt: 1);

        var saved = target.ToArray();
        using var result = Document.Open(saved);
        Assert.Equal(3, result.PageCount); // 1 original + 2 imported
    }

    [Fact]
    public void ImportPages_PreservesMediaBox()
    {
        var targetBytes = PdfBuilder.BuildMinimal();
        using var target = Document.Open(targetBytes);

        // BuildMultiPage creates pages with widths 612, 622, 632, ...
        var sourceBytes = PdfBuilder.BuildMultiPage(3);
        using var source = Document.Open(sourceBytes);

        // Verify source pages before import
        Assert.Equal(612, source.Pages[1].MediaBox.Width);
        Assert.Equal(622, source.Pages[2].MediaBox.Width);
        Assert.Equal(632, source.Pages[3].MediaBox.Width);

        target.ImportPage(source, 2);

        // Verify in-memory before save
        Assert.Equal(2, target.PageCount);
        // The imported page should preserve source page 2's MediaBox in memory
        Assert.Equal(622, target.Pages[2].MediaBox.Width);

        var saved = target.ToArray();
        using var result = Document.Open(saved);
        Assert.Equal(2, result.PageCount);
        Assert.Equal(622, result.Pages[2].MediaBox.Width);
    }

    [Fact]
    public void ImportPages_EmptyArray_NoChange()
    {
        var targetBytes = PdfBuilder.BuildMinimal();
        using var target = Document.Open(targetBytes);

        var sourceBytes = PdfBuilder.BuildMinimal();
        using var source = Document.Open(sourceBytes);

        target.ImportPages(source, []);

        Assert.Equal(1, target.PageCount);
    }

    [Fact]
    public void ImportPages_InvalidPageNumber_Throws()
    {
        var targetBytes = PdfBuilder.BuildMinimal();
        using var target = Document.Open(targetBytes);

        var sourceBytes = PdfBuilder.BuildMinimal();
        using var source = Document.Open(sourceBytes);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            target.ImportPage(source, 5));
    }

    [Fact]
    public void ImportPage_WithContentStream_ContentSurvivesRoundtrip()
    {
        var targetBytes = PdfBuilder.BuildMinimal();
        using var target = Document.Open(targetBytes);

        // Build a source with text content
        var contentBytes = Encoding.ASCII.GetBytes("BT /F1 12 Tf 100 700 Td (Hello) Tj ET");
        var sourceBytes = PdfBuilder.BuildWithTextContent(contentBytes);
        using var source = Document.Open(sourceBytes);

        target.ImportPage(source, 1);

        var saved = target.ToArray();
        using var result = Document.Open(saved);
        Assert.Equal(2, result.PageCount);
    }

    [Fact]
    public void AddContent_AppendsToExistingPage()
    {
        // Build a source with initial text content
        var initialContent = Encoding.ASCII.GetBytes("BT /F1 12 Tf 100 700 Td (Initial) Tj ET");
        var sourceBytes = PdfBuilder.BuildWithTextContent(initialContent);
        using var doc = Document.Open(sourceBytes);

        // Add new content
        var newContent = Encoding.ASCII.GetBytes("BT /F1 12 Tf 100 600 Td (Added) Tj ET");
        doc.Pages[1].AddContent(newContent);

        var saved = doc.ToArray();
        using var result = Document.Open(saved);
        Assert.Equal(1, result.PageCount);
    }

    [Fact]
    public void AddContent_ToBlankPage_SetsContentStream()
    {
        using var doc = Document.Create();
        var page = doc.Pages.Add();

        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf 100 700 Td (Hello) Tj ET");
        page.AddContent(content);

        var saved = doc.ToArray();
        using var result = Document.Open(saved);
        Assert.Equal(1, result.PageCount);
    }

    [Fact]
    public void AddContent_MultipleTimes_AllContentPresent()
    {
        using var doc = Document.Create();
        var page = doc.Pages.Add();

        var content1 = Encoding.ASCII.GetBytes("BT /F1 12 Tf 100 700 Td (First) Tj ET");
        page.AddContent(content1);

        var content2 = Encoding.ASCII.GetBytes("BT /F1 12 Tf 100 600 Td (Second) Tj ET");
        page.AddContent(content2);

        var saved = doc.ToArray();
        using var result = Document.Open(saved);
        Assert.Equal(1, result.PageCount);
    }

    [Fact]
    public void SetVersion_ChangesOutputVersion()
    {
        using var doc = Document.Create();
        // New documents default to PDF 1.7; SetVersion overrides it.
        Assert.Equal("1.7", doc.PdfVersion);

        doc.SetVersion("1.6");

        var saved = doc.ToArray();
        using var result = Document.Open(saved);
        Assert.Equal("1.6", result.PdfVersion);
    }

    [Fact]
    public void SetVersion_To20_Roundtrips()
    {
        using var doc = Document.Create();
        doc.SetVersion("2.0");

        var saved = doc.ToArray();
        using var result = Document.Open(saved);
        Assert.Equal("2.0", result.PdfVersion);
    }

    [Fact]
    public void SetVersion_OverridesOriginalVersion()
    {
        // Open a 1.4 document
        var originalBytes = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(originalBytes);
        Assert.Equal("1.4", doc.PdfVersion);

        doc.SetVersion("1.6");

        var saved = doc.ToArray();
        using var result = Document.Open(saved);
        Assert.Equal("1.6", result.PdfVersion);
    }
}
