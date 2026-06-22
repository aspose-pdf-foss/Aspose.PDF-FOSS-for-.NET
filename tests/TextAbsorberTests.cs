using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Text;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests;

public class TextAbsorberTests
{
    [Fact]
    public void Visit_PageWithTextContent_ExtractsText()
    {
        // Build a PDF with a simple text content stream
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (Hello World) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);
        var absorber = new TextAbsorber();
        absorber.Visit(doc.Pages[1]);
        Assert.Contains("Hello World", absorber.Text);
    }

    [Fact]
    public void Visit_Document_ExtractsFromAllPages()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        var absorber = new TextAbsorber();
        absorber.Visit(doc);
        // Minimal PDF has no content, so text should be empty
        Assert.NotNull(absorber.Text);
    }

    [Fact]
    public void Visit_PageWithTJOperator_ExtractsText()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf [(Hello) -100 ( ) -100 (World)] TJ ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);
        var absorber = new TextAbsorber();
        absorber.Visit(doc.Pages[1]);
        Assert.Contains("Hello", absorber.Text);
        Assert.Contains("World", absorber.Text);
    }

    [Fact]
    public void ParseCMap_BfCharMapping()
    {
        var cmap = @"
/CIDInit /ProcSet findresource begin
12 dict begin
begincmap
1 beginbfchar
<0041> <0048>
endbfchar
endcmap";
        var map = TextAbsorber.ParseCMap(cmap);
        Assert.Equal("H", map[0x41]);
    }

    [Fact]
    public void ParseCMap_BfRangeMapping()
    {
        var cmap = @"
1 beginbfrange
<0041> <0043> <0061>
endbfrange";
        var map = TextAbsorber.ParseCMap(cmap);
        Assert.Equal("a", map[0x41]);
        Assert.Equal("b", map[0x42]);
        Assert.Equal("c", map[0x43]);
    }
}
