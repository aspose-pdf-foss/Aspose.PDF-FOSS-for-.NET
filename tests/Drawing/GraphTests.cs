using Aspose.Pdf.Drawing;
using Xunit;

namespace Aspose.Pdf.Tests.Drawing;

public class GraphTests
{
    [Fact]
    public void Graph_AddLine_Renders()
    {
        var graph = new Graph(200, 100);
        graph.Add(new Line(0, 0, 100, 50)
        {
            GraphInfo = { StrokeColor = Color.Red, LineWidth = 2 }
        });
        var content = graph.Build();
        var text = System.Text.Encoding.ASCII.GetString(content);
        Assert.Contains("0 0 m", text);
        Assert.Contains("100 50 l", text);
        Assert.Contains("S\n", text);
        Assert.Contains("1 0 0 RG", text);
    }

    [Fact]
    public void Graph_AddRectangle_FillAndStroke()
    {
        var graph = new Graph(200, 100);
        graph.Add(new DrawingRectangle(10, 10, 80, 40)
        {
            GraphInfo =
            {
                FillColor = Color.Blue,
                StrokeColor = Color.Black,
            }
        });
        var content = graph.Build();
        var text = System.Text.Encoding.ASCII.GetString(content);
        Assert.Contains("10 10 80 40 re", text);
        Assert.Contains("B\n", text); // FillAndStroke
    }

    [Fact]
    public void Graph_AddCircle_RendersBezier()
    {
        var graph = new Graph(200, 200);
        graph.Add(new Circle(100, 100, 50)
        {
            GraphInfo = { StrokeColor = Color.Green }
        });
        var content = graph.Build();
        var text = System.Text.Encoding.ASCII.GetString(content);
        Assert.Contains("150 100 m", text); // startpoint at cx+r
        Assert.Contains("c\n", text); // Bézier curve
        Assert.Contains("h\n", text); // closepath
    }

    [Fact]
    public void Color_FromRgb()
    {
        var c = Aspose.Pdf.Drawing.Color.FromRgb(255, 128, 0);
        Assert.Equal(1.0, c.R, 0.01);
        Assert.Equal(0.502, c.G, 0.01);
        Assert.Equal(0.0, c.B, 0.01);
    }

    [Fact]
    public void Graph_MultipleShapes()
    {
        var graph = new Graph(300, 300);
        graph.Add(new DrawingRectangle(0, 0, 300, 300)
        {
            GraphInfo = { FillColor = Color.White }
        });
        graph.Add(new Circle(150, 150, 100)
        {
            GraphInfo = { FillColor = Color.Red }
        });
        graph.Add(new Line(50, 50, 250, 250)
        {
            GraphInfo = { StrokeColor = Color.Black, LineWidth = 3 }
        });
        var content = graph.Build();
        Assert.True(content.Length > 100);
    }
}
