using System.IO;
using System.Linq;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Vector;
using Xunit;

namespace Aspose.Pdf.Tests.Drawing;

/// <summary>An absorbed graphic element names the page it came from and the operators that draw it.</summary>
public class GraphicElementSourceTests
{
    private static Document PageWithShapes()
    {
        var document = new Document();
        var page = document.Pages.Add();
        var graph = new Graph(400, 300);
        graph.Shapes.Add(new Aspose.Pdf.Drawing.Rectangle(20, 20, 200, 100));
        graph.Shapes.Add(new Line(new float[] { 20, 200, 300, 250 }));
        page.Paragraphs.Add(graph);
        using var saved = new MemoryStream();
        document.Save(saved);
        return new Document(new MemoryStream(saved.ToArray()));
    }

    [Fact]
    public void AbsorbedElements_NameTheirPageAndTheirOperators()
    {
        var document = PageWithShapes();
        var page = document.Pages[1];
        using var absorber = new GraphicsAbsorber();
        absorber.Visit(page);

        Assert.NotEqual(0, absorber.Elements.Count);
        foreach (var element in absorber.Elements)
        {
            Assert.Same(page, element.SourcePage);
            Assert.NotEmpty(element.Operators);
        }
    }

    [Fact]
    public void AnElementNoAbsorberProduced_HasNoSource()
    {
        var element = new GraphicElement();
        Assert.Null(element.SourcePage);
        Assert.Empty(element.Operators);
    }
}
