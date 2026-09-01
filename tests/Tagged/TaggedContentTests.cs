using Aspose.Pdf;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Tagged;

public class TaggedContentTests
{
    [Fact]
    public void TaggedContent_NotNull_ForTaggedPdf()
    {
        var data = PdfBuilder.BuildTagged();
        using var doc = Document.Open(data);
        Assert.NotNull(doc.TaggedContent);
    }

    [Fact]
    public void TaggedContent_StructTreeRoot_Null_ForUntaggedPdf()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        // TaggedContent auto-creates on access (auto-created on access),
        // but StructTreeRoot is null when the PDF has no /StructTreeRoot.
        Assert.NotNull(doc.TaggedContent);
        Assert.Null(((Aspose.Pdf.Tagged.TaggedContent)doc.TaggedContent).StructTreeRoot);
    }

    [Fact]
    public void TaggedContent_RootElement_IsDocument()
    {
        var data = PdfBuilder.BuildTagged();
        using var doc = Document.Open(data);
        var tc = doc.TaggedContent!;
        Assert.NotNull(tc.RootElement);
        Assert.Equal("Document", tc.RootElement!.StructureType?.Tag);
    }

    [Fact]
    public void TaggedContent_StructTreeRoot_NotNull()
    {
        var data = PdfBuilder.BuildTagged();
        using var doc = Document.Open(data);
        Assert.NotNull(((Aspose.Pdf.Tagged.TaggedContent)doc.TaggedContent!).StructTreeRoot);
    }

    [Fact]
    public void TaggedContent_SetTitle_PersistsAfterSave()
    {
        var data = PdfBuilder.BuildTaggedWithInfo();
        using var doc = Document.Open(data);
        var tc = doc.TaggedContent!;

        tc.SetTitle("New Title");

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        Assert.Equal("New Title", doc2.Info.Title);
    }

    [Fact]
    public void TaggedContent_SetLanguage_PersistsAfterSave()
    {
        var data = PdfBuilder.BuildTagged();
        using var doc = Document.Open(data);
        var tc = doc.TaggedContent!;

        tc.SetLanguage("fr-FR");

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        Assert.Equal("fr-FR", doc2.Language);
    }

    [Fact]
    public void TaggedContent_Title_ReadsFromInfoDict()
    {
        var data = PdfBuilder.BuildTaggedWithInfo(title: "Test Title");
        using var doc = Document.Open(data);
        var tc = (Aspose.Pdf.Tagged.TaggedContent)doc.TaggedContent!;
        Assert.Equal("Test Title", tc.Title);
    }

    [Fact]
    public void TaggedContent_Language_ReadsFromCatalog()
    {
        var data = PdfBuilder.BuildTaggedWithLanguage("en-US");
        using var doc = Document.Open(data);
        var tc = (Aspose.Pdf.Tagged.TaggedContent)doc.TaggedContent!;
        Assert.Equal("en-US", tc.Language);
    }

    [Fact]
    public void TaggedContent_SetLanguage_UpdatesDocumentLanguage()
    {
        var data = PdfBuilder.BuildTagged();
        using var doc = Document.Open(data);
        var tc = doc.TaggedContent!;

        tc.SetLanguage("de-DE");
        Assert.Equal("de-DE", doc.Language);
    }

    [Fact]
    public void TaggedContent_SetTitle_UpdatesDocumentInfoTitle()
    {
        var data = PdfBuilder.BuildTaggedWithInfo();
        using var doc = Document.Open(data);
        var tc = doc.TaggedContent!;

        tc.SetTitle("Updated");
        Assert.Equal("Updated", doc.Info.Title);
    }

    [Fact]
    public void Document_Language_Setter_Works()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        Assert.Null(doc.Language);

        doc.Language = "ja-JP";
        Assert.Equal("ja-JP", doc.Language);

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        Assert.Equal("ja-JP", doc2.Language);
    }

    [Fact]
    public void DocumentInfo_TitleSetter_Works()
    {
        var data = PdfBuilder.BuildWithDocumentInfo(title: "Original");
        using var doc = Document.Open(data);
        Assert.Equal("Original", doc.Info.Title);

        doc.Info.Title = "Modified";
        Assert.Equal("Modified", doc.Info.Title);

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        Assert.Equal("Modified", doc2.Info.Title);
    }

    [Fact]
    public void DocumentInfo_AuthorSetter_Works()
    {
        var data = PdfBuilder.BuildWithDocumentInfo(author: "Alice");
        using var doc = Document.Open(data);

        doc.Info.Author = "Bob";
        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        Assert.Equal("Bob", doc2.Info.Author);
    }
}
