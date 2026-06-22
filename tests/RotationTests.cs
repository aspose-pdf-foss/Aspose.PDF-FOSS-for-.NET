using Aspose.Pdf;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests;

public class RotationTests
{
    [Fact]
    public void Page_Rotate90_WidthHeightSwapped()
    {
        var data = PdfBuilder.BuildWithRotation(90);
        using var doc = Document.Open(data);
        var page = doc.Pages[1];
        Assert.Equal(90, page.RotateDegrees);
        // Original MediaBox is 612x792, rotation swaps visible width/height
        Assert.Equal(792, page.Width);
        Assert.Equal(612, page.Height);
    }

    [Fact]
    public void Page_Rotate180_WidthHeightUnchanged()
    {
        var data = PdfBuilder.BuildWithRotation(180);
        using var doc = Document.Open(data);
        var page = doc.Pages[1];
        Assert.Equal(180, page.RotateDegrees);
        Assert.Equal(612, page.Width);
        Assert.Equal(792, page.Height);
    }

    [Fact]
    public void Page_Rotate270_WidthHeightSwapped()
    {
        var data = PdfBuilder.BuildWithRotation(270);
        using var doc = Document.Open(data);
        var page = doc.Pages[1];
        Assert.Equal(270, page.RotateDegrees);
        Assert.Equal(792, page.Width);
        Assert.Equal(612, page.Height);
    }

    [Fact]
    public void Page_Rotate0_Default()
    {
        var data = PdfBuilder.BuildWithRotation(0);
        using var doc = Document.Open(data);
        var page = doc.Pages[1];
        Assert.Equal(0, page.RotateDegrees);
        Assert.Equal(612, page.Width);
        Assert.Equal(792, page.Height);
    }

    [Fact]
    public void Page_MediaBox_UnaffectedByRotation()
    {
        var data = PdfBuilder.BuildWithRotation(90);
        using var doc = Document.Open(data);
        var page = doc.Pages[1];
        // MediaBox is always the raw values regardless of rotation
        Assert.Equal(612, page.MediaBox.URX);
        Assert.Equal(792, page.MediaBox.URY);
    }
}
