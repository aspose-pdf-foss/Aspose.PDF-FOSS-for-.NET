using Aspose.Pdf;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests;

public class OutlineTests
{
    [Fact]
    public void Outlines_MinimalPdf_ReturnsEmptyCollection()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        Assert.False(doc.HasOutlines);
        Assert.NotNull(doc.Outlines);
        Assert.Empty(doc.Outlines);
    }

    [Fact]
    public void Outlines_WithBookmarks_ParsesCorrectly()
    {
        var data = PdfBuilder.BuildWithOutlines("Chapter 1", "Chapter 2", "Chapter 3");
        using var doc = Document.Open(data);
        Assert.True(doc.HasOutlines);
        Assert.NotNull(doc.Outlines);
        Assert.Equal(3, doc.Outlines!.Count);
    }

    [Fact]
    public void OutlineItem_Title_CorrectValues()
    {
        var data = PdfBuilder.BuildWithOutlines("Introduction", "Conclusion");
        using var doc = Document.Open(data);
        var items = doc.Outlines!.Items;
        Assert.Equal("Introduction", items[0].Title);
        Assert.Equal("Conclusion", items[1].Title);
    }

    [Fact]
    public void Outlines_EmptyList_ReturnsEmptyCollection()
    {
        var data = PdfBuilder.BuildWithOutlines();
        using var doc = Document.Open(data);
        // Empty Outlines dict has Count=0, so HasOutlines is false (no actual bookmarks)
        Assert.False(doc.HasOutlines);
        // But the Outlines property still returns the collection (it exists in the catalog)
        Assert.NotNull(doc.Outlines);
        Assert.Equal(0, doc.Outlines!.Count);
    }

    [Fact]
    public void OutlineItem_SingleBookmark()
    {
        var data = PdfBuilder.BuildWithOutlines("Only One");
        using var doc = Document.Open(data);
        Assert.Equal(1, doc.Outlines!.Count);
        Assert.Equal("Only One", doc.Outlines!.Items[0].Title);
    }

    [Fact]
    public void OutlineItem_Children_EmptyForFlatBookmarks()
    {
        var data = PdfBuilder.BuildWithOutlines("Flat 1", "Flat 2");
        using var doc = Document.Open(data);
        Assert.Empty(doc.Outlines!.Items[0].Children);
        Assert.Empty(doc.Outlines!.Items[1].Children);
    }
}
