using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests;

public class ImageStampTests
{
    [Fact]
    public void FromRgb_AddsImageToPage()
    {
        var pdf = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);

        var pixels = new byte[6 * 4 * 3]; // 6x4 RGB
        var stamp = ImageStamp.FromRgb(pixels, 6, 4);
        stamp.X = 72;
        stamp.Y = 700;
        stamp.ApplyTo(doc.Pages[1]);

        // Verify the image was added to page resources
        var images = doc.Pages[1].Images;
        Assert.Single(images);
        Assert.Equal(6, images[1].Width);
        Assert.Equal(4, images[1].Height);
        Assert.Equal("DeviceRGB", images[1].ColorSpace);
    }

    [Fact]
    public void FromGrayscale_AddsGrayImage()
    {
        var pdf = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);

        var pixels = new byte[10 * 10]; // 10x10 gray
        var stamp = ImageStamp.FromGrayscale(pixels, 10, 10);
        stamp.ApplyTo(doc.Pages[1]);

        var img = doc.Pages[1].Images[1];
        Assert.Equal("DeviceGray", img.ColorSpace);
        Assert.Equal(1, img.ComponentCount);
    }

    [Fact]
    public void FromJpeg_PassesThrough()
    {
        var pdf = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);

        var jpegData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x02, 0xFF, 0xD9 };
        var stamp = ImageStamp.FromJpeg(jpegData, 100, 50);
        stamp.X = 0;
        stamp.Y = 0;
        stamp.ApplyTo(doc.Pages[1]);

        var img = doc.Pages[1].Images[1];
        Assert.True(img.IsJpeg);
        Assert.Equal(100, img.Width);
        Assert.Equal(50, img.Height);
        Assert.Equal(jpegData, img.GetRawData());
    }

    [Fact]
    public void FromRgb_InvalidLength_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            ImageStamp.FromRgb(new byte[10], 5, 5)); // 10 != 5*5*3
    }

    [Fact]
    public void MultipleImages_UniqueNames()
    {
        var pdf = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);

        var stamp1 = ImageStamp.FromRgb(new byte[3], 1, 1);
        var stamp2 = ImageStamp.FromRgb(new byte[3], 1, 1);
        stamp1.ApplyTo(doc.Pages[1]);
        stamp2.ApplyTo(doc.Pages[1]);

        var images = doc.Pages[1].Images;
        Assert.Equal(2, images.Count);
        Assert.NotEqual(images[1].Name, images[2].Name);
    }

    [Fact]
    public void Image_SurvivesSaveRoundtrip()
    {
        var pdf = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);

        var pixels = new byte[4 * 4 * 3];
        for (var i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i % 200);

        var stamp = ImageStamp.FromRgb(pixels, 4, 4);
        stamp.X = 100;
        stamp.Y = 200;
        stamp.DisplayWidth = 200;
        stamp.DisplayHeight = 150;
        stamp.ApplyTo(doc.Pages[1]);

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        var img = doc2.Pages[1].Images[1];
        Assert.Equal(4, img.Width);
        Assert.Equal(4, img.Height);
        var decoded = img.GetDecodedData();
        Assert.Equal(pixels, decoded);
    }
}
