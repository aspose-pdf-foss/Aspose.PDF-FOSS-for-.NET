using Aspose.Pdf;
using Xunit;

namespace Aspose.Pdf.Tests;

public sealed class PageBoxTests
{
    [Fact]
    public void SetMediaBox_RoundTrip()
    {
        using var doc = Document.Create();
        doc.Pages.Add();
        var page = doc.Pages.At(1);

        page.SetMediaBox(new Rectangle(0, 0, 400, 600));
        var saved = doc.ToArray();

        using var reopened = Document.Open(saved);
        var mb = reopened.Pages.At(1).MediaBox;
        Assert.Equal(400, mb.Width, 0.01);
        Assert.Equal(600, mb.Height, 0.01);
    }

    [Fact]
    public void SetCropBox_RoundTrip()
    {
        using var doc = Document.Create();
        doc.Pages.Add();
        var page = doc.Pages.At(1);

        page.SetMediaBox(new Rectangle(0, 0, 612, 792));
        page.SetCropBox(new Rectangle(36, 36, 576, 756));
        var saved = doc.ToArray();

        using var reopened = Document.Open(saved);
        var cb = reopened.Pages.At(1).CropBox;
        Assert.Equal(36, cb.LLX, 0.01);
        Assert.Equal(36, cb.LLY, 0.01);
        Assert.Equal(576, cb.URX, 0.01);
        Assert.Equal(756, cb.URY, 0.01);
    }

    [Fact]
    public void Rotate_SetAndGet()
    {
        using var doc = Document.Create();
        doc.Pages.Add();
        var page = doc.Pages.At(1);

        page.RotateDegrees = 90;
        var saved = doc.ToArray();

        using var reopened = Document.Open(saved);
        Assert.Equal(90, reopened.Pages.At(1).RotateDegrees);
    }

    [Fact]
    public void Rotate_AffectsWidthHeight()
    {
        var pdf = Helpers.PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);
        var page = doc.Pages.At(1);

        // Default: 612 x 792
        Assert.Equal(612, page.Width, 0.01);
        Assert.Equal(792, page.Height, 0.01);

        page.RotateDegrees = 90;
        Assert.Equal(792, page.Width, 0.01);
        Assert.Equal(612, page.Height, 0.01);
    }

    [Fact]
    public void SetTrimBox()
    {
        using var doc = Document.Create();
        doc.Pages.Add();
        var page = doc.Pages.At(1);
        page.SetTrimBox(new Rectangle(10, 10, 602, 782));

        var saved = doc.ToArray();
        using var reopened = Document.Open(saved);
        var tb = reopened.Pages.At(1).TrimBox;
        Assert.Equal(10, tb.LLX, 0.01);
    }
}
