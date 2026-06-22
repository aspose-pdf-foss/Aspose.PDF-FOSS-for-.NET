using Aspose.Pdf.Stamps;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests;

public class StampTests
{
    [Fact]
    public void TextStamp_BuildContentStream_ContainsText()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        var page = doc.Pages[1];

        var stamp = new TextStamp("CONFIDENTIAL")
        {
            FontSize = 24,
            Color = Color.Red,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var content = stamp.BuildContentStream(page);
        var text = System.Text.Encoding.ASCII.GetString(content);
        Assert.Contains("CONFIDENTIAL", text);
        Assert.Contains("Tf", text);
        Assert.Contains("Tj", text);
    }

    [Fact]
    public void TextStamp_Properties()
    {
        var stamp = new TextStamp("Hello")
        {
            FontSize = 18,
            FontName = "Courier",
            Opacity = 0.5,
            IsBackground = true,
        };

        Assert.Equal("Hello", stamp.Text);
        Assert.Equal(18f, stamp.FontSize);
        Assert.Equal("Courier", stamp.FontName);
        Assert.Equal(0.5, stamp.Opacity);
        Assert.True(stamp.IsBackground);
    }

    [Fact]
    public void TextStamp_LeftBottom_Position()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        var page = doc.Pages[1];

        var stamp = new TextStamp("Test")
        {
            XIndent = 72,
            YIndent = 72,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
        };

        var content = stamp.BuildContentStream(page);
        var text = System.Text.Encoding.ASCII.GetString(content);
        // The stamp positions its text block with a cm transform at the XIndent/YIndent
        // anchor (Left/Bottom), then draws each line relative to it — so a 72,72 stamp
        // anchors the block at "72 72 cm" rather than the old single-line "72 72 Td".
        Assert.Contains("72 72 cm", text);
    }
}
