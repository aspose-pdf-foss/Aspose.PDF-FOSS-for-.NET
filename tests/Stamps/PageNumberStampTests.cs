using Aspose.Pdf;
using Aspose.Pdf.Stamps;
using Aspose.Pdf.Tests.Helpers;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Stamps;

public class PageNumberStampTests
{
    [Fact]
    public void PageNumberStamp_ApplyToAll()
    {
        var input = PdfBuilder.BuildMultiPage(3);
        using var doc = Document.Open(input);
        var stamp = new PageNumberStamp
        {
            Format = "Page {0} of {1}",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        stamp.ApplyToAll(doc);

        var saved = doc.ToArray();
        Assert.True(saved.Length > input.Length);
    }

    [Fact]
    public void PageNumberStamp_CustomFormat()
    {
        var stamp = new PageNumberStamp { Format = "- {0} -" };
        Assert.Equal("- {0} -", stamp.Format);
    }

    [Fact]
    public void PageNumberStamp_StartingNumber()
    {
        var stamp = new PageNumberStamp { StartingNumber = 5 };
        Assert.Equal(5, stamp.StartingNumber);
    }
}
