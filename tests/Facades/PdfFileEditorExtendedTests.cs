using Aspose.Pdf;
using Aspose.Pdf.Facades;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Facades;

public class PdfFileEditorExtendedTests
{
    [Fact]
    public void Concatenate_ThreeDocuments()
    {
        var editor = new PdfFileEditor();
        var result = editor.Concatenate(
            PdfBuilder.BuildMinimal(),
            PdfBuilder.BuildMinimal(),
            PdfBuilder.BuildMinimal());

        using var doc = Document.Open(result);
        Assert.Equal(3, doc.PageCount);
    }

    [Fact]
    public void Concatenate_MultiPageDocuments()
    {
        var editor = new PdfFileEditor();
        var result = editor.Concatenate(
            PdfBuilder.BuildMultiPage(3),
            PdfBuilder.BuildMultiPage(2));

        using var doc = Document.Open(result);
        Assert.Equal(5, doc.PageCount);
    }

    [Fact]
    public void Concatenate_NoDocuments_Throws()
    {
        var editor = new PdfFileEditor();
        Assert.Throws<ArgumentException>(() => editor.Concatenate());
    }

    [Fact]
    public void Extract_MiddlePages()
    {
        var editor = new PdfFileEditor();
        var source = PdfBuilder.BuildMultiPage(5);

        var extracted = editor.Extract(source, 2, 4);
        using var doc = Document.Open(extracted);
        Assert.Equal(3, doc.PageCount);
    }

    [Fact]
    public void Extract_LastPage()
    {
        var editor = new PdfFileEditor();
        var source = PdfBuilder.BuildMultiPage(3);

        var extracted = editor.Extract(source, 3, 3);
        using var doc = Document.Open(extracted);
        Assert.Equal(1, doc.PageCount);
    }

    [Fact]
    public void Extract_AllPages()
    {
        var editor = new PdfFileEditor();
        var source = PdfBuilder.BuildMultiPage(4);

        var extracted = editor.Extract(source, 1, 4);
        using var doc = Document.Open(extracted);
        Assert.Equal(4, doc.PageCount);
    }

    [Fact]
    public void Split_FourPages()
    {
        var editor = new PdfFileEditor();
        var source = PdfBuilder.BuildMultiPage(4);

        var pages = editor.Split(source);
        Assert.Equal(4, pages.Length);
        foreach (var p in pages)
        {
            using var doc = Document.Open(p);
            Assert.Equal(1, doc.PageCount);
        }
    }

    [Fact]
    public void Delete_MultiplePages()
    {
        var editor = new PdfFileEditor();
        var source = PdfBuilder.BuildMultiPage(5);

        var result = editor.Delete(source, 1, 3, 5);
        using var doc = Document.Open(result);
        Assert.Equal(2, doc.PageCount);
    }

    [Fact]
    public void Concatenate_ThenSplit_Roundtrip()
    {
        var editor = new PdfFileEditor();
        var combined = editor.Concatenate(
            PdfBuilder.BuildMinimal(),
            PdfBuilder.BuildMinimal(),
            PdfBuilder.BuildMinimal());

        var split = editor.Split(combined);
        Assert.Equal(3, split.Length);

        // Re-concatenate
        var recombined = editor.Concatenate(split);
        using var doc = Document.Open(recombined);
        Assert.Equal(3, doc.PageCount);
    }

    [Fact]
    public void Extract_ClampsToBounds()
    {
        var editor = new PdfFileEditor();
        var source = PdfBuilder.BuildMultiPage(3);

        // startPage < 1 should be clamped to 1, endPage > count clamped to count
        var extracted = editor.Extract(source, -1, 100);
        using var doc = Document.Open(extracted);
        Assert.Equal(3, doc.PageCount);
    }
}
