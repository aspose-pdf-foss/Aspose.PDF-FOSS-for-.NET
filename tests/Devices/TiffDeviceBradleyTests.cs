using System.IO;
using Aspose.Pdf.Devices;
using Aspose.Pdf.IO;
using Xunit;

namespace Aspose.Pdf.Tests.Devices;

public class TiffDeviceBradleyTests
{
    // A grey page lit unevenly - darker towards the right - with one black bar across it.
    private static byte[] UnevenlyLitBar(int w, int h)
    {
        var rgba = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var o = (y * w + x) * 4;
                var level = (byte)(y >= h / 2 - 1 && y <= h / 2 + 1 ? 0 : 220 - 100 * x / w);
                rgba[o] = level; rgba[o + 1] = level; rgba[o + 2] = level; rgba[o + 3] = 255;
            }
        return rgba;
    }

    [Fact]
    public void BinarizeBradley_KeepsTheBarBlackAndTheUnevenBackgroundWhite()
    {
        const int w = 96, h = 24;
        using var input = new MemoryStream();
        TiffDevice.EncodeRgbaImage(UnevenlyLitBar(w, h), w, h, input, CompressionType.LZW);
        using var output = new MemoryStream();

        TiffDevice.BinarizeBradley(input, output, 0.1);

        var raster = TiffDecoder.DecodeRaster(output.ToArray(), 0, blankUnreadable: false);
        Assert.NotNull(raster);
        Assert.Equal((w, h, 1), (raster!.Width, raster.Height, raster.BitsPerSample));
        bool Black(int x, int y)
        {
            var bit = (raster.Samples[y * raster.RowBytes + x / 8] >> (7 - x % 8)) & 1;
            return raster.Photometric == 0 ? bit == 1 : bit == 0;  // WhiteIsZero: 1 is black
        }
        Assert.True(Black(10, h / 2), "the bar is black on the bright side");
        Assert.True(Black(w - 10, h / 2), "the bar is black on the dim side");
        Assert.False(Black(10, 3), "the bright background is white");
        Assert.False(Black(w - 10, h - 4), "the dim background is white too, not darker than its own surroundings");
    }

    [Fact]
    public void BinarizeBradley_RefusesWhatIsNotATiff()
    {
        using var output = new MemoryStream();
        Assert.Throws<System.ArgumentException>(() => TiffDevice.BinarizeBradley(new MemoryStream(new byte[] { 1, 2, 3 }), output, 0.1));
    }
}
