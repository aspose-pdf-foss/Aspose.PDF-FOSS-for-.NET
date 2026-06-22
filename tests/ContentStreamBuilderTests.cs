using Aspose.Pdf.Content;
using Xunit;

namespace Aspose.Pdf.Tests;

public class ContentStreamBuilderTests
{
    [Fact]
    public void BeginText_EndText()
    {
        var builder = new ContentStreamBuilder();
        builder.BeginText().EndText();
        Assert.Equal("BT\nET\n", builder.ToString());
    }

    [Fact]
    public void SetFont_And_ShowText()
    {
        var builder = new ContentStreamBuilder();
        builder.BeginText()
            .SetFont("F1", 12)
            .ShowText("Hello")
            .EndText();
        var result = builder.ToString();
        Assert.Contains("/F1 12 Tf", result);
        Assert.Contains("(Hello) Tj", result);
    }

    [Fact]
    public void ShowText_EscapesParens()
    {
        var builder = new ContentStreamBuilder();
        builder.ShowText("a(b)c");
        Assert.Contains(@"(a\(b\)c) Tj", builder.ToString());
    }

    [Fact]
    public void Rectangle_Stroke()
    {
        var builder = new ContentStreamBuilder();
        builder.Rectangle(10, 20, 100, 50).Stroke();
        Assert.Contains("10 20 100 50 re", builder.ToString());
        Assert.Contains("S\n", builder.ToString());
    }

    [Fact]
    public void SetFillColor()
    {
        var builder = new ContentStreamBuilder();
        builder.SetFillColor(1, 0, 0);
        Assert.Contains("1 0 0 rg", builder.ToString());
    }

    [Fact]
    public void SaveRestore_State()
    {
        var builder = new ContentStreamBuilder();
        builder.SaveState().RestoreState();
        Assert.Equal("q\nQ\n", builder.ToString());
    }

    [Fact]
    public void MoveTextPosition()
    {
        var builder = new ContentStreamBuilder();
        builder.MoveTextPosition(72, 700);
        Assert.Contains("72 700 Td", builder.ToString());
    }

    [Fact]
    public void DrawXObject()
    {
        var builder = new ContentStreamBuilder();
        builder.DrawXObject("Im0");
        Assert.Contains("/Im0 Do", builder.ToString());
    }

    [Fact]
    public void Build_ReturnsBytes()
    {
        var builder = new ContentStreamBuilder();
        builder.BeginText().ShowText("Test").EndText();
        var bytes = builder.Build();
        Assert.True(bytes.Length > 0);
    }

    [Fact]
    public void ComplexContent_Fluent()
    {
        var builder = new ContentStreamBuilder();
        var content = builder
            .SaveState()
            .SetFillColor(0.5, 0.5, 0.5)
            .Rectangle(50, 50, 200, 100)
            .Fill()
            .RestoreState()
            .BeginText()
            .SetFont("F1", 24)
            .MoveTextPosition(72, 700)
            .ShowText("Hello World")
            .EndText()
            .Build();
        Assert.True(content.Length > 50);
    }
}
