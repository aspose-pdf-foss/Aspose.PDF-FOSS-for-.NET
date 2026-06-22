using Aspose.Pdf;
using Xunit;

namespace Aspose.Pdf.Tests;

public class OutlineBuilderTests
{
    [Fact]
    public void CreateBookmarks_RoundTrip()
    {
        using var doc = Document.Create();
        doc.Pages.Add();

        var builder = new OutlineBuilder(doc);
        builder.Add("Chapter 1", 0);
        builder.Add("Chapter 2", 0);

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        Assert.True(doc2.HasOutlines);
        Assert.Equal(2, doc2.Outlines!.Count);
        Assert.Equal("Chapter 1", doc2.Outlines.Items[0].Title);
        Assert.Equal("Chapter 2", doc2.Outlines.Items[1].Title);
    }

    [Fact]
    public void NestedBookmarks_RoundTrip()
    {
        using var doc = Document.Create();
        doc.Pages.Add();

        var builder = new OutlineBuilder(doc);
        var ch1 = builder.Add("Chapter 1", 0);
        ch1.AddChild("Section 1.1", 0);
        ch1.AddChild("Section 1.2", 0);
        builder.Add("Chapter 2", 0);

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        Assert.Equal(2, doc2.Outlines!.Count);
        var ch1Read = doc2.Outlines.Items[0];
        Assert.Equal("Chapter 1", ch1Read.Title);
        Assert.Equal(2, ch1Read.Children.Count);
        Assert.Equal("Section 1.1", ch1Read.Children[0].Title);
        Assert.Equal("Section 1.2", ch1Read.Children[1].Title);
        Assert.Empty(doc2.Outlines.Items[1].Children);
    }

    [Fact]
    public void SingleBookmark()
    {
        using var doc = Document.Create();
        doc.Pages.Add();

        var builder = new OutlineBuilder(doc);
        builder.Add("Only One", 0);

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        Assert.Equal(1, doc2.Outlines!.Count);
        Assert.Equal("Only One", doc2.Outlines.Items[0].Title);
    }

    [Fact]
    public void BookmarkWithBoldItalic()
    {
        using var doc = Document.Create();
        doc.Pages.Add();

        var builder = new OutlineBuilder(doc);
        builder.Add("Bold Title", 0).SetBold(true);
        builder.Add("Italic Title", 0).SetItalic(true);

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        Assert.Equal(2, doc2.Outlines!.Count);
        Assert.Equal("Bold Title", doc2.Outlines.Items[0].Title);
        Assert.Equal("Italic Title", doc2.Outlines.Items[1].Title);
    }

    [Fact]
    public void DeeplyNestedBookmarks()
    {
        using var doc = Document.Create();
        doc.Pages.Add();

        var builder = new OutlineBuilder(doc);
        var part = builder.Add("Part I", 0);
        var ch = part.AddChild("Chapter 1", 0);
        ch.AddChild("Section 1.1", 0);

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        var partRead = doc2.Outlines!.Items[0];
        Assert.Equal("Part I", partRead.Title);
        Assert.Single(partRead.Children);
        Assert.Equal("Chapter 1", partRead.Children[0].Title);
        Assert.Single(partRead.Children[0].Children);
        Assert.Equal("Section 1.1", partRead.Children[0].Children[0].Title);
    }

    [Fact]
    public void BookmarkWithPage()
    {
        using var doc = Document.Create();
        var page = doc.Pages.Add();

        var builder = new OutlineBuilder(doc);
        builder.Add("Go to page", page);

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        Assert.Equal(1, doc2.Outlines!.Count);
        Assert.Equal("Go to page", doc2.Outlines.Items[0].Title);
    }
}
