using System.IO;
using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Devices;
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

    /// <summary>
    /// A shading carries no alpha, so a fill that FADES across a box is painted
    /// through a luminosity soft mask instead. Registering one and painting a
    /// black rectangle under it has to leave the box black at the end the mask
    /// is white, untouched at the end it is black, and half way between in the
    /// middle -- which is the whole of what the entry is for.
    /// </summary>
    [Fact]
    public void AddLuminositySoftMask_FadesTheFillAcrossTheBox()
    {
        var box = new Rectangle(200, 600, 400, 700);
        var data = PdfBuilder.BuildMinimal();
        byte[] saved;

        using (var doc = Document.Open(data))
        {
            var page = doc.Pages[1];
            var ramp = new Aspose.Pdf.Drawing.GradientAxialShading
            {
                Start = new Point(box.LLX, box.LLY),
                End = new Point(box.URX, box.LLY),
            };
            ramp.Stops.Add(Color.FromRgb(1.0, 1.0, 1.0));
            ramp.Stops.Add(Color.FromRgb(0.0, 0.0, 0.0));

            var state = page.AddLuminositySoftMask(ramp, box);
            page.AddContentStream(Encoding.ASCII.GetBytes(
                $"q /{state} gs 0 0 0 rg " +
                $"{box.LLX} {box.LLY} {box.URX - box.LLX} {box.URY - box.LLY} re f Q"));

            using var ms = new MemoryStream();
            doc.Save(ms);
            saved = ms.ToArray();
        }

        var rgba = new SoftwarePageRenderer().RenderPage(saved, 1, 72).Data;

        // The page is 612 wide and 792 tall, and user y runs up from the bottom,
        // so the band's middle row is 792 - 650.
        // The ramp is straight, so a sample says where it had got to: a fiftieth
        // of the way along the fill is still all but black.
        AssertGrey(rgba, width: 612, x: 204, y: 792 - 650, expected: 5, tolerance: 12);
        AssertGrey(rgba, width: 612, x: 300, y: 792 - 650, expected: 128, tolerance: 12);
        AssertGrey(rgba, width: 612, x: 396, y: 792 - 650, expected: 250, tolerance: 12);

        // And nothing outside the mask's box is painted at all.
        AssertGrey(rgba, width: 612, x: 300, y: 792 - 750, expected: 255, tolerance: 2);
    }

    private static void AssertGrey(byte[] rgba, int width, int x, int y, int expected, int tolerance)
    {
        var at = (y * width + x) * 4;
        for (var channel = 0; channel < 3; channel++)
            Assert.InRange(rgba[at + channel], expected - tolerance, expected + tolerance);
    }
}
