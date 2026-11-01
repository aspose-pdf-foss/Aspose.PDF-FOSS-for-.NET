using System.Text;
using Aspose.Pdf.Devices;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Devices;

/// <summary>
/// The portable renderer paints text the way its rendering mode says (PDF 32000 §9.3.6): mode 1
/// strokes the glyph outlines without filling them, mode 2 fills and then strokes them, mode 0 fills
/// only. Each test shows large text filled red and stroked blue, so the colours on the page tell
/// which of the two paints happened.
/// </summary>
public class SoftwareTextRenderModeTests
{
    private const int Dpi = 72;

    private static (int red, int blue) Paints(int mode)
    {
        var content = Encoding.ASCII.GetBytes(
            $"BT /F1 72 Tf 3 w 1 0 0 rg 0 0 1 RG {mode} Tr 100 600 Td (HIM) Tj ET");
        var page = new SoftwarePageRenderer().RenderPage(PdfBuilder.BuildWithTextContent(content), 1, Dpi);
        int red = 0, blue = 0;
        for (var i = 0; i + 3 < page.Data.Length; i += 4)
        {
            int r = page.Data[i], g = page.Data[i + 1], b = page.Data[i + 2];
            if (r > 200 && g < 80 && b < 80) red++;
            if (b > 200 && r < 80 && g < 80) blue++;
        }
        return (red, blue);
    }

    [Fact]
    public void StrokeModeDrawsTheOutlineInTheStrokingColourAndNoFill()
    {
        var (red, blue) = Paints(1);
        Assert.True(blue > 500, $"the outlines are stroked: {blue} blue pixels");
        Assert.Equal(0, red);
    }

    [Fact]
    public void FillThenStrokeModeDrawsBoth()
    {
        var (red, blue) = Paints(2);
        Assert.True(red > 500, $"the glyphs are filled: {red} red pixels");
        Assert.True(blue > 500, $"the outlines are stroked: {blue} blue pixels");
    }

    [Fact]
    public void FillModeDrawsNoOutline()
    {
        var (red, blue) = Paints(0);
        Assert.True(red > 500, $"the glyphs are filled: {red} red pixels");
        Assert.Equal(0, blue);
    }
}
