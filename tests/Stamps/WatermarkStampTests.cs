using Aspose.Pdf;
using Aspose.Pdf.Stamps;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Stamps;

public class WatermarkStampTests
{
    [Fact]
    public void WatermarkStamp_AppliesWithoutError()
    {
        var input = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(input);
        var stamp = new WatermarkStamp("CONFIDENTIAL")
        {
            FontSize = 48,
            Rotate = 45,
        };
        doc.Pages[1].AddStamp(stamp);
        var saved = doc.ToArray();
        Assert.True(saved.Length > input.Length);
    }

    [Fact]
    public void WatermarkStamp_DefaultsAreReasonable()
    {
        var stamp = new WatermarkStamp("DRAFT");
        Assert.Equal("DRAFT", stamp.Text);
        Assert.True(stamp.IsBackground);
        Assert.Equal(0.3, stamp.Opacity, 2);
        Assert.Equal(45, stamp.Rotate, 2);
        Assert.Equal(48, stamp.FontSize, 2);
    }
}
