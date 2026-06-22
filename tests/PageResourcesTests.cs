using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests;

public class PageResourcesTests
{
    [Fact]
    public void Page_Fonts_WithFont_ReturnsFontInfo()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (test) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var fonts = doc.Pages[1].Fonts;
        Assert.Single(fonts);
        Assert.Equal("F1", fonts[1].ResourceName);
        Assert.Equal("Helvetica", fonts[1].BaseFont);
        Assert.Equal("Type1", fonts[1].Subtype);
    }

    [Fact]
    public void Page_Fonts_MinimalPdf_Empty()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        Assert.Empty(doc.Pages[1].Fonts);
    }

    [Fact]
    public void Page_Images_MinimalPdf_Empty()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        Assert.Empty(doc.Pages[1].Images);
    }
}
