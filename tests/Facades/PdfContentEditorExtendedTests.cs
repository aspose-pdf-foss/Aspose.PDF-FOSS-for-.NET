using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Facades;
using Xunit;

namespace Aspose.Pdf.Tests.Facades;

public sealed class PdfContentEditorExtendedTests
{
    [Fact]
    public void CreateWebLink_AddsLinkAnnotation()
    {
        var pdf = Helpers.PdfBuilder.BuildMinimal();
        var editor = new PdfContentEditor();
        var result = editor.CreateWebLink(pdf, new Rectangle(72, 700, 200, 720), 1, "https://example.com");

        using var doc = Document.Open(result);
        var page = doc.Pages.At(1);
        Assert.True(page.Annotations.Count > 0);
        var annot = page.Annotations[1];
        Assert.Equal("Link", annot.AnnotationType.ToString());
    }

    [Fact]
    public void CreateLocalLink_AddsLinkWithDestination()
    {
        var pdf = Helpers.PdfBuilder.BuildMultiPage(3);
        var editor = new PdfContentEditor();
        var result = editor.CreateLocalLink(pdf, new Rectangle(72, 700, 200, 720), 1, 3);

        using var doc = Document.Open(result);
        var page = doc.Pages.At(1);
        Assert.True(page.Annotations.Count > 0);
    }

    [Fact]
    public void CreateFreeText_AddsFreeTextAnnotation()
    {
        var pdf = Helpers.PdfBuilder.BuildMinimal();
        var editor = new PdfContentEditor();
        var result = editor.CreateFreeText(pdf, new Rectangle(72, 700, 300, 750), 1, "Hello World");

        using var doc = Document.Open(result);
        var page = doc.Pages.At(1);
        Assert.True(page.Annotations.Count > 0);
        var annot = page.Annotations[1];
        Assert.Equal("FreeText", annot.AnnotationType.ToString());
    }

    [Fact]
    public void CreateText_AddsStickyNote()
    {
        var pdf = Helpers.PdfBuilder.BuildMinimal();
        var editor = new PdfContentEditor();
        var result = editor.CreateText(pdf, new Rectangle(72, 700, 100, 730), 1, "Note", "Some content");

        using var doc = Document.Open(result);
        var page = doc.Pages.At(1);
        Assert.True(page.Annotations.Count > 0);
        var annot = page.Annotations[1];
        Assert.Equal("Text", annot.AnnotationType.ToString());
    }

    [Fact]
    public void DeleteAnnotations_RemovesAll()
    {
        var pdf = Helpers.PdfBuilder.BuildWithAnnotation();
        using var before = Document.Open(pdf);
        Assert.True(before.Pages.At(1).Annotations.Count > 0);

        var editor = new PdfContentEditor();
        var result = editor.DeleteAnnotations(pdf, 1);

        using var after = Document.Open(result);
        Assert.Empty(after.Pages.At(1).Annotations);
    }

    [Fact]
    public void DeleteAnnotations_BySubtype()
    {
        // Add both a Text and a Link annotation, then delete only Text
        var pdf = Helpers.PdfBuilder.BuildWithAnnotation();
        var editor = new PdfContentEditor();
        // Add a link annotation too
        pdf = editor.CreateWebLink(pdf, new Rectangle(10, 10, 100, 30), 1, "https://example.com");

        // Now delete only Text annotations
        var result = editor.DeleteAnnotations(pdf, 1, "Text");
        using var doc = Document.Open(result);
        var annots = doc.Pages.At(1).Annotations;
        foreach (var annot in annots)
        {
            Assert.NotEqual("Text", annot.AnnotationType.ToString());
        }
    }

    [Fact]
    public void ExtractText_AllPages()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf 100 700 Td (Hello World) Tj ET");
        var pdf = Helpers.PdfBuilder.BuildWithTextContent(content);
        var editor = new PdfContentEditor();
        var text = editor.ExtractText(pdf);
        Assert.Contains("Hello World", text);
    }

    [Fact]
    public void ExtractText_SinglePage()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf 100 700 Td (Page One) Tj ET");
        var pdf = Helpers.PdfBuilder.BuildWithTextContent(content);
        var editor = new PdfContentEditor();
        var text = editor.ExtractText(pdf, 1);
        Assert.Contains("Page One", text);
    }

    [Fact]
    public void InvalidPageNumber_Throws()
    {
        var pdf = Helpers.PdfBuilder.BuildMinimal();
        var editor = new PdfContentEditor();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            editor.CreateWebLink(pdf, new Rectangle(0, 0, 100, 100), 5, "https://example.com"));
    }
}
