using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class DocumentHocrTests
{
    private const string SampleHocr = """
        <html><body>
        <div class='ocr_page'>
        <p class='ocr_par'>
        <span class='ocrx_word' title='bbox 100 100 200 130'>Hello</span>
        <span class='ocrx_word' title='bbox 210 100 320 130'>World</span>
        </p>
        </div>
        </body></html>
        """;

    [WindowsOnlyFact]
    public void Convert_CallBackGetHocr_OverlaysInvisibleText()
    {
        var pdf = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);

        var pageCount = 0;
        var ok = doc.Convert(img =>
        {
            Assert.NotNull(img);
            pageCount++;
            return SampleHocr;
        }, flattenImages: false);

        Assert.True(ok);
        Assert.Equal(doc.Pages.Count, pageCount);

        // The invisible text overlay should be findable via TextAbsorber.
        var absorber = new Aspose.Pdf.Text.TextAbsorber();
        doc.Pages.Accept(absorber);
        Assert.Contains("Hello", absorber.Text);
        Assert.Contains("World", absorber.Text);
    }

    [WindowsOnlyFact]
    public void Convert_CallBackGetHocrWithPage_PassesPage()
    {
        var pdf = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);

        Page? seenPage = null;
        var ok = doc.Convert((img, page) =>
        {
            seenPage = page;
            return SampleHocr;
        }, flattenImages: false);

        Assert.True(ok);
        Assert.NotNull(seenPage);
    }

    [Fact]
    public void Convert_NullCallback_ReturnsFalse()
    {
        var pdf = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);

        var ok = doc.Convert((Document.CallBackGetHocr)null!, flattenImages: false);
        Assert.False(ok);
    }
}
