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
    public void ParseCMap_BfRangeArrayWrappedOverLines()
    {
        // An array-form range wraps over lines as its writer pleases, and a destination may be
        // two characters (a ligature). Read per line, the continuation lines were taken for
        // sequential triples: <0057><00660069><003B> became a range up to 0x660069 that shadowed
        // every 2-byte code of the text.
        var cmap = "1 begincodespacerange\n<00><FF>endcodespacerange\n1 beginbfrange\n"
            + "<01><09>[<0050><0065><0072>\n<0063><006E><0074><0020>\n<0057><00660069>]endbfrange";
        var map = TextAbsorber.ParseCMap(cmap);
        Assert.Equal("P", map[1]);
        Assert.Equal("t", map[6]);
        Assert.Equal(" ", map[7]);
        Assert.Equal("W", map[8]);
        Assert.Equal(9, map.Count);
        Assert.False(map.ContainsKey(0x0203));
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
