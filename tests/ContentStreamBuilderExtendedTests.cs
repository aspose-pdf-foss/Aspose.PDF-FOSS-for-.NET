using Aspose.Pdf.Content;
using Xunit;

namespace Aspose.Pdf.Tests;

public class ContentStreamBuilderExtendedTests
{
    [Fact]
    public void SetStrokeColor()
    {
        var builder = new ContentStreamBuilder();
        builder.SetStrokeColor(0, 0, 1);
        Assert.Contains("0 0 1 RG", builder.ToString());
    }

    [Fact]
    public void SetFillGray()
    {
        var builder = new ContentStreamBuilder();
        builder.SetFillGray(0.5);
        Assert.Contains("0.5 g", builder.ToString());
    }

    [Fact]
    public void SetStrokeGray()
    {
        var builder = new ContentStreamBuilder();
        builder.SetStrokeGray(0.3);
        Assert.Contains("0.3 G", builder.ToString());
    }

    [Fact]
    public void SetLineWidth()
    {
        var builder = new ContentStreamBuilder();
        builder.SetLineWidth(2);
        Assert.Contains("2 w", builder.ToString());
    }

    [Fact]
    public void MoveTo_LineTo()
    {
        var builder = new ContentStreamBuilder();
        builder.MoveTo(10, 20).LineTo(100, 200);
        var s = builder.ToString();
        Assert.Contains("10 20 m", s);
        Assert.Contains("100 200 l", s);
    }

    [Fact]
    public void CurveTo()
    {
        var builder = new ContentStreamBuilder();
        builder.CurveTo(1, 2, 3, 4, 5, 6);
        Assert.Contains("1 2 3 4 5 6 c", builder.ToString());
    }

    [Fact]
    public void ClosePath()
    {
        var builder = new ContentStreamBuilder();
        builder.ClosePath();
        Assert.Contains("h\n", builder.ToString());
    }

    [Fact]
    public void FillAndStroke()
    {
        var builder = new ContentStreamBuilder();
        builder.FillAndStroke();
        Assert.Contains("B\n", builder.ToString());
    }

    [Fact]
    public void FillEvenOdd()
    {
        var builder = new ContentStreamBuilder();
        builder.FillEvenOdd();
        Assert.Contains("f*\n", builder.ToString());
    }

    [Fact]
    public void CloseAndStroke()
    {
        var builder = new ContentStreamBuilder();
        builder.CloseAndStroke();
        Assert.Contains("s\n", builder.ToString());
    }

    [Fact]
    public void EndPath()
    {
        var builder = new ContentStreamBuilder();
        builder.EndPath();
        Assert.Contains("n\n", builder.ToString());
    }

    [Fact]
    public void Clip()
    {
        var builder = new ContentStreamBuilder();
        builder.Clip();
        Assert.Contains("W n", builder.ToString());
    }

    [Fact]
    public void ClipEvenOdd()
    {
        var builder = new ContentStreamBuilder();
        builder.ClipEvenOdd();
        Assert.Contains("W* n", builder.ToString());
    }

    [Fact]
    public void SetTextMatrix()
    {
        var builder = new ContentStreamBuilder();
        builder.SetTextMatrix(1, 0, 0, 1, 72, 700);
        Assert.Contains("1 0 0 1 72 700 Tm", builder.ToString());
    }

    [Fact]
    public void SetMatrix()
    {
        var builder = new ContentStreamBuilder();
        builder.SetMatrix(1, 0, 0, 1, 50, 100);
        Assert.Contains("1 0 0 1 50 100 cm", builder.ToString());
    }

    [Fact]
    public void SetLeading()
    {
        var builder = new ContentStreamBuilder();
        builder.SetLeading(14);
        Assert.Contains("14 TL", builder.ToString());
    }

    [Fact]
    public void NextLine()
    {
        var builder = new ContentStreamBuilder();
        builder.NextLine();
        Assert.Contains("T*\n", builder.ToString());
    }

    [Fact]
    public void SetCharSpacing()
    {
        var builder = new ContentStreamBuilder();
        builder.SetCharSpacing(2);
        Assert.Contains("2 Tc", builder.ToString());
    }

    [Fact]
    public void SetWordSpacing()
    {
        var builder = new ContentStreamBuilder();
        builder.SetWordSpacing(5);
        Assert.Contains("5 Tw", builder.ToString());
    }

    [Fact]
    public void SetTextRenderingMode()
    {
        var builder = new ContentStreamBuilder();
        builder.SetTextRenderingMode(2);
        Assert.Contains("2 Tr", builder.ToString());
    }

    [Fact]
    public void Raw_AppendsDirectly()
    {
        var builder = new ContentStreamBuilder();
        builder.Raw("custom operator");
        Assert.Contains("custom operator", builder.ToString());
    }

    [Fact]
    public void Raw_AddsNewline()
    {
        var builder = new ContentStreamBuilder();
        builder.Raw("op");
        Assert.EndsWith("op\n", builder.ToString());
    }

    [Fact]
    public void ShowText_EscapesBackslash()
    {
        var builder = new ContentStreamBuilder();
        builder.ShowText(@"a\b");
        Assert.Contains(@"(a\\b) Tj", builder.ToString());
    }
}
