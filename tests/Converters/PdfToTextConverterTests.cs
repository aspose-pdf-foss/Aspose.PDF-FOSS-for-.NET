using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Converters;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Converters;

public class PdfToTextConverterTests
{
    [Fact]
    public void SaveAsText_ExtractsAllText()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (Hello World) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var converter = new PdfToTextConverter();
        var text = converter.SaveAsText(doc);
        Assert.Contains("Hello World", text);
    }

    [Fact]
    public void SavePageAsText_ExtractsSinglePage()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (Page one) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var converter = new PdfToTextConverter();
        var text = converter.SavePageAsText(doc, 1);
        Assert.Contains("Page one", text);
    }

    [Fact]
    public void SaveAllPagesAsText_ReturnsArrayPerPage()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (Text) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var converter = new PdfToTextConverter();
        var pages = converter.SaveAllPagesAsText(doc);
        Assert.Single(pages);
        Assert.Contains("Text", pages[0]);
    }

    [Fact]
    public void SavePageRangeAsText_ExtractsRange()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (Range text) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var converter = new PdfToTextConverter();
        var text = converter.SavePageRangeAsText(doc, 1, 1);
        Assert.Contains("Range text", text);
    }

    [Fact]
    public void SaveAsText_EmptyPage_ReturnsEmptyString()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);

        var converter = new PdfToTextConverter();
        var text = converter.SaveAsText(doc);
        Assert.NotNull(text);
    }
}
