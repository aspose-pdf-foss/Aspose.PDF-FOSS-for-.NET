using Aspose.Pdf.Facades;
using Xunit;
using DrawingFormat = Aspose.Pdf.Drawing.ImageFormat;

namespace Aspose.Pdf.Tests.Facades;

/// <summary>PdfConverter.MergeImages / MergeImagesAsTiff where there is no GDI+: read and written by the library's
/// own decoders and encoders, laid out as the GDI+ path lays them out.</summary>
public class MergeImagesManagedTests
{
    private static MemoryStream Png(int width, int height, byte r, byte g, byte b)
    {
        var rgb = new byte[width * height * 3];
        for (var at = 0; at < rgb.Length; at += 3) { rgb[at] = r; rgb[at + 1] = g; rgb[at + 2] = b; }
        return new MemoryStream(Aspose.Pdf.IO.PngEncoder.Encode(rgb, width, height));
    }

    private static (byte[] rgb, int width, int height) Decode(Stream merged)
    {
        using var copy = new MemoryStream();
        merged.CopyTo(copy);
        var (rgb, _, width, height) = ImageStamp.DecodeRaster(copy.ToArray())!.Value;
        return (rgb, width, height);
    }

    private static (int r, int g, int b) At(byte[] rgb, int width, int x, int y)
    {
        var at = (y * width + x) * 3;
        return (rgb[at], rgb[at + 1], rgb[at + 2]);
    }

    [Fact]
    public void Vertical_StacksTopLeftInOrder_OnWhite()
    {
        var merged = PdfConverter.MergeImagesManaged(
            new List<Stream> { Png(20, 10, 255, 0, 0), Png(10, 15, 0, 0, 255) }, DrawingFormat.Png, ImageMergeMode.Vertical);
        var (rgb, width, height) = Decode(merged);
        Assert.Equal((20, 25), (width, height));
        Assert.Equal((255, 0, 0), At(rgb, width, 19, 9));    // first input, top
        Assert.Equal((0, 0, 255), At(rgb, width, 0, 10));    // second input, below it
        Assert.Equal((255, 255, 255), At(rgb, width, 15, 20)); // beside the narrower second input: white
    }

    [Fact]
    public void Horizontal_SideBySide()
    {
        var merged = PdfConverter.MergeImagesManaged(
            new List<Stream> { Png(8, 8, 0, 255, 0), Png(12, 4, 255, 0, 0) }, DrawingFormat.Bmp, ImageMergeMode.Horizontal);
        var bytes = ((MemoryStream)merged).ToArray();
        Assert.Equal((byte)'B', bytes[0]);
        Assert.Equal((byte)'M', bytes[1]);
        Assert.Equal(20, BitConverter.ToInt32(bytes, 18));  // width: 8 + 12
        Assert.Equal(8, Math.Abs(BitConverter.ToInt32(bytes, 22))); // height: the taller
    }

    [Fact]
    public void Center_PlacesOnlyTheFirst_CentredOnTheLargest()
    {
        var merged = PdfConverter.MergeImagesManaged(
            new List<Stream> { Png(4, 4, 0, 0, 0), Png(12, 12, 255, 0, 0) }, DrawingFormat.Png, ImageMergeMode.Center);
        var (rgb, width, height) = Decode(merged);
        Assert.Equal((12, 12), (width, height));
        Assert.Equal((0, 0, 0), At(rgb, width, 6, 6));        // the first, centred
        Assert.Equal((255, 255, 255), At(rgb, width, 0, 0));  // the second is not drawn
    }

    [Fact]
    public void Jpeg_IsAJpegOfTheMergedSize()
    {
        var merged = PdfConverter.MergeImagesManaged(
            new List<Stream> { Png(16, 16, 10, 20, 30), Png(16, 16, 200, 100, 50) }, DrawingFormat.Jpeg, ImageMergeMode.Vertical);
        var (_, width, height) = Decode(merged);
        Assert.Equal((16, 32), (width, height));
    }

    [Fact]
    public void AsTiff_OnePagePerInput()
    {
        var merged = PdfConverter.MergeImagesAsTiffManaged(
            new List<Stream> { Png(5, 5, 1, 2, 3), Png(7, 3, 4, 5, 6), Png(2, 9, 7, 8, 9) });
        var frames = Aspose.Pdf.IO.TiffDecoder.DecodeFramesAsPng(((MemoryStream)merged).ToArray());
        Assert.NotNull(frames);
        Assert.Equal(3, frames!.Count);
    }

    [Fact]
    public void Gif_IsRefusedWithoutGdiPlus()
    {
        Assert.Throws<NotSupportedException>(() => PdfConverter.MergeImagesManaged(
            new List<Stream> { Png(2, 2, 0, 0, 0) }, DrawingFormat.Gif, ImageMergeMode.Vertical));
    }
}
