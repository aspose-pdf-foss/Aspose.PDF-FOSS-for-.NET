using Aspose.Pdf;
using Xunit;

namespace Aspose.Pdf.Tests;

public class PageLabelTests
{
    [Fact]
    public void FormatLabel_Decimal()
    {
        var label = new PageLabel { StartPage = 0, Style = NumberingStyle.Decimal, Start = 1 };
        Assert.Equal("1", label.FormatLabel(0));
        Assert.Equal("5", label.FormatLabel(4));
    }

    [Fact]
    public void FormatLabel_WithPrefix()
    {
        var label = new PageLabel { StartPage = 0, Style = NumberingStyle.Decimal, Start = 1, Prefix = "A-" };
        Assert.Equal("A-1", label.FormatLabel(0));
        Assert.Equal("A-3", label.FormatLabel(2));
    }

    [Fact]
    public void FormatLabel_UpperRoman()
    {
        var label = new PageLabel { StartPage = 0, Style = NumberingStyle.UpperRoman, Start = 1 };
        Assert.Equal("I", label.FormatLabel(0));
        Assert.Equal("IV", label.FormatLabel(3));
        Assert.Equal("X", label.FormatLabel(9));
    }

    [Fact]
    public void FormatLabel_LowerRoman()
    {
        var label = new PageLabel { StartPage = 0, Style = NumberingStyle.LowerRoman, Start = 1 };
        Assert.Equal("i", label.FormatLabel(0));
        Assert.Equal("iv", label.FormatLabel(3));
    }

    [Fact]
    public void FormatLabel_UpperAlpha()
    {
        var label = new PageLabel { StartPage = 0, Style = NumberingStyle.UpperAlpha, Start = 1 };
        Assert.Equal("A", label.FormatLabel(0));
        Assert.Equal("C", label.FormatLabel(2));
        Assert.Equal("Z", label.FormatLabel(25));
    }

    [Fact]
    public void FormatLabel_LowerAlpha()
    {
        var label = new PageLabel { StartPage = 0, Style = NumberingStyle.LowerAlpha, Start = 1 };
        Assert.Equal("a", label.FormatLabel(0));
        Assert.Equal("z", label.FormatLabel(25));
    }

    [Fact]
    public void FormatLabel_None_EmptyNumbering()
    {
        var label = new PageLabel { StartPage = 0, Style = NumberingStyle.None, Prefix = "Cover" };
        Assert.Equal("Cover", label.FormatLabel(0));
    }

    [Fact]
    public void FormatLabel_CustomStart()
    {
        var label = new PageLabel { StartPage = 5, Style = NumberingStyle.Decimal, Start = 10 };
        Assert.Equal("10", label.FormatLabel(5));
        Assert.Equal("13", label.FormatLabel(8));
    }

    [Fact]
    public void Document_NoPageLabels()
    {
        var data = Helpers.PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        Assert.False(doc.HasPageLabels);
        // PageLabels is always a usable (non-null) collection so callers can add
        // labels to a document that has none; emptiness is reported via Count.
        Assert.NotNull(doc.PageLabels);
        Assert.Empty(doc.PageLabels);
    }
}
