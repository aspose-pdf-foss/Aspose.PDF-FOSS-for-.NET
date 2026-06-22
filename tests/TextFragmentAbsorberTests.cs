using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Text;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests;

public class TextFragmentAbsorberTests
{
    [Fact]
    public void Visit_CollectsAllFragments()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (Hello) Tj (World) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var absorber = new TextFragmentAbsorber();
        absorber.Visit(doc.Pages[1]);

        Assert.True(absorber.TextFragments.Count >= 2);
    }

    [Fact]
    public void Visit_SearchPhrase_FindsMatch()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (Hello World) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var absorber = new TextFragmentAbsorber("Hello");
        absorber.Visit(doc.Pages[1]);

        Assert.Single(absorber.TextFragments);
        Assert.Equal("Hello", absorber.TextFragments[1].Text);
    }

    [Fact]
    public void Visit_SearchPhrase_NoMatch_Empty()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (Hello World) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var absorber = new TextFragmentAbsorber("NotFound");
        absorber.Visit(doc.Pages[1]);

        Assert.Empty(absorber.TextFragments);
    }

    [Fact]
    public void Visit_RegexSearch()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (Order 12345) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var absorber = new TextFragmentAbsorber(@"\d{5}", isRegex: true);
        absorber.Visit(doc.Pages[1]);

        Assert.Single(absorber.TextFragments);
        Assert.Equal("12345", absorber.TextFragments[1].Text);
    }

    [Fact]
    public void Visit_Document_CollectsFromAllPages()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (Page text) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var absorber = new TextFragmentAbsorber("Page text");
        absorber.Visit(doc);

        Assert.Single(absorber.TextFragments);
    }
}
