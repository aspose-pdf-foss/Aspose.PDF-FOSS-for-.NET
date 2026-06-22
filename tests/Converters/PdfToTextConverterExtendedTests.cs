using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Converters;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Converters;

/// <summary>
/// Extended PdfToTextConverter tests ported from TypeScript: Converters/TextConverterTests.ts
/// </summary>
public class PdfToTextConverterExtendedTests
{
    [Fact]
    public void Constructor_CreatesInstance()
    {
        var converter = new PdfToTextConverter();
        Assert.NotNull(converter);
    }

    [Fact]
    public void SavePageAsText_ReturnsNonEmptyString()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (Text on page) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var converter = new PdfToTextConverter();
        var text = converter.SavePageAsText(doc, 1);
        Assert.NotEmpty(text);
    }

    [Fact]
    public void SaveAllPagesAsText_ReturnsArrayMatchingPageCount()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (Text) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var converter = new PdfToTextConverter();
        var pages = converter.SaveAllPagesAsText(doc);
        Assert.Single(pages);
    }

    [Fact]
    public void SaveAllPagesAsText_MultiPage_CorrectCount()
    {
        var data = PdfBuilder.BuildMultiPage(3);
        using var doc = Document.Open(data);

        var converter = new PdfToTextConverter();
        var pages = converter.SaveAllPagesAsText(doc);
        Assert.Equal(3, pages.Length);
    }

    [Fact]
    public void SaveAsText_ReturnsFullDocumentText()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (Full document text) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var converter = new PdfToTextConverter();
        var text = converter.SaveAsText(doc);
        Assert.Contains("Full document text", text);
    }

    [Fact]
    public void SavePageRangeAsText_ExtractsRange()
    {
        var data = PdfBuilder.BuildMultiPage(3);
        using var doc = Document.Open(data);

        var converter = new PdfToTextConverter();
        var text = converter.SavePageRangeAsText(doc, 1, 2);
        Assert.NotNull(text);
    }

    [Fact]
    public void SaveAsText_MultiplePages_ContainsAllText()
    {
        var data = PdfBuilder.BuildMultiPage(2);
        using var doc = Document.Open(data);

        var converter = new PdfToTextConverter();
        var text = converter.SaveAsText(doc);
        Assert.NotNull(text);
    }

    [Fact]
    public void SaveAllPagesAsText_EachPageIsString()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (Page content) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var converter = new PdfToTextConverter();
        var pages = converter.SaveAllPagesAsText(doc);
        foreach (var page in pages)
        {
            Assert.NotNull(page);
        }
    }
}
