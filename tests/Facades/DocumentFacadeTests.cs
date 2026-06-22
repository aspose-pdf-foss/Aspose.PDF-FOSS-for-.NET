using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Text;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Facades;

/// <summary>
/// Ported from TypeScript: Facades/DocumentTests.ts
/// Tests page resources, page numbers, document info, and error handling.
/// </summary>
public class DocumentFacadeTests
{
    // ── Page resources — font and image enumeration ─────────────────────

    [Fact]
    public void PageResources_FontsAreEnumerable()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (Resource test) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var fonts = doc.Pages[1].Fonts;
        var fontCount = 0;
        foreach (var _ in fonts) fontCount++;
        Assert.Equal(fonts.Count, fontCount);
    }

    [Fact]
    public void PageResources_ImagesAreEnumerable()
    {
        var data = PdfBuilder.BuildWithUncompressedImage(4, 4);
        using var doc = Document.Open(data);

        var images = doc.Pages[1].Images;
        var imageCount = 0;
        foreach (var _ in images) imageCount++;
        Assert.Equal(images.Count, imageCount);
    }

    // ── Page number matches index ───────────────────────────────────────

    [Fact]
    public void PageNumber_MatchesSequentialIndex_SinglePage()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);

        Assert.Equal(1, doc.Pages[1].Number);
    }

    [Fact]
    public void PageNumber_MatchesSequentialIndex_MultiPage()
    {
        var data = PdfBuilder.BuildMultiPage(4);
        using var doc = Document.Open(data);

        for (var i = 1; i <= doc.PageCount; i++)
        {
            Assert.Equal(i, doc.Pages[i].Number);
        }
    }

    // ── Page count ──────────────────────────────────────────────────────

    [Fact]
    public void PageCount_SinglePage()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);

        Assert.Equal(1, doc.PageCount);
    }

    [Fact]
    public void PageCount_MultiPage()
    {
        var data = PdfBuilder.BuildMultiPage(5);
        using var doc = Document.Open(data);

        Assert.Equal(5, doc.PageCount);
    }

    // ── Document info ───────────────────────────────────────────────────

    [Fact]
    public void DocumentInfo_ReadTitleFromBuilder()
    {
        var data = PdfBuilder.BuildWithDocumentInfo(title: "Test Title", author: "Author");
        using var doc = Document.Open(data);

        Assert.Equal("Test Title", doc.Info.Title);
        Assert.Equal("Author", doc.Info.Author);
    }

    [Fact]
    public void DocumentInfo_MutationDoesNotThrow()
    {
        var doc = Document.Create();
        doc.Pages.Add();
        doc.Info.Author = "Aspose";
        doc.Info.Title = "Test";
        doc.Info.Subject = "Subject";
        doc.Info.Keywords = "test, keywords";
        // ModDate and CreationDate are read-only in this FOSS API

        var bytes = doc.ToArray();
        Assert.True(bytes.Length > 0);
    }

    [Fact]
    public void DocumentInfo_RoundTrip()
    {
        // Use PdfBuilder to create a document with info dict baked in
        var data = PdfBuilder.BuildWithDocumentInfo(
            title: "Test Title", author: "TestAuthor",
            subject: "Test Subject");
        using var doc = Document.Open(data);

        Assert.Equal("TestAuthor", doc.Info.Author);
        Assert.Equal("Test Title", doc.Info.Title);
        Assert.Equal("Test Subject", doc.Info.Subject);
    }

    // ── Text extraction via TextAbsorber (PdfExtractor equivalent) ─────

    [Fact]
    public void TextAbsorber_ExtractsText()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (hello world) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var abs = new TextAbsorber();
        abs.Visit(doc.Pages[1]);
        Assert.Contains("hello world", abs.Text);
    }

    [Fact]
    public void TextAbsorber_VisitDocument_CollectsAllPages()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (Page text) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var abs = new TextAbsorber();
        abs.Visit(doc);
        Assert.Contains("Page text", abs.Text);
    }

    [Fact]
    public void TextAbsorber_EmptyPage_DoesNotThrow()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);

        var abs = new TextAbsorber();
        abs.Visit(doc.Pages[1]);
        Assert.NotNull(abs.Text);
    }

    [Fact]
    public void TextAbsorber_MultiPage_ExtractsPerPage()
    {
        var data = PdfBuilder.BuildMultiPage(3);
        using var doc = Document.Open(data);

        foreach (var page in doc.Pages)
        {
            var abs = new TextAbsorber();
            abs.Visit(page);
            Assert.NotNull(abs.Text);
        }
    }

    // ── Invalid PDF detection ───────────────────────────────────────────

    [Fact]
    public void Open_GarbageBytes_Throws()
    {
        var garbage = Encoding.ASCII.GetBytes("This is not a PDF document at all.\n");
        Assert.ThrowsAny<Exception>(() => Document.Open(garbage));
    }

    [Fact]
    public void Open_EmptyBytes_Throws()
    {
        var empty = Array.Empty<byte>();
        Assert.ThrowsAny<Exception>(() => Document.Open(empty));
    }

    [Fact]
    public void Open_PdfHeaderButNoRoot_Throws()
    {
        var fake = Encoding.ASCII.GetBytes("%PDF-1.4\n%%EOF\n");
        Assert.ThrowsAny<Exception>(() => Document.Open(fake));
    }

    // ── Page media box ──────────────────────────────────────────────────

    [Fact]
    public void Page_MediaBox_HasDimensions()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);

        var mb = doc.Pages[1].MediaBox;
        Assert.True(mb.Width > 0);
        Assert.True(mb.Height > 0);
    }

    [Fact]
    public void Page_Width_IsPositive()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);

        Assert.True(doc.Pages[1].Width > 0);
    }

    [Fact]
    public void Page_Height_IsPositive()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);

        Assert.True(doc.Pages[1].Height > 0);
    }

    // ── Page rotation ───────────────────────────────────────────────────

    [Fact]
    public void Page_Rotation_DefaultIsZero()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);

        Assert.Equal(0, doc.Pages[1].RotateDegrees);
    }

    [Fact]
    public void Page_Rotation_ReadsRotation()
    {
        var data = PdfBuilder.BuildWithRotation(90);
        using var doc = Document.Open(data);

        Assert.Equal(90, doc.Pages[1].RotateDegrees);
    }
}
