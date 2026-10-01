using System;
using System.IO;
using Aspose.Pdf;
using Aspose.Pdf.IO;
using Xunit;

namespace Aspose.Pdf.Tests.Images;

public class RasterDecodeTests
{
    // A 3x2 RGBA picture: black, white, red on top; a half-transparent grey, a clear pixel and blue below.
    private static readonly byte[] Rgba =
    {
        0, 0, 0, 255,   255, 255, 255, 255,   255, 0, 0, 255,
        128, 128, 128, 128,   0, 0, 0, 0,   0, 0, 255, 255,
    };

    [Fact]
    public void Png_DecodesToItsPixelsAndAlpha()
    {
        var decoded = ImageStamp.DecodeRaster(PngEncoder.Encode(Rgba, 3, 2, colorType: 6));
        Assert.NotNull(decoded);
        var (rgb, alpha, width, height) = decoded!.Value;
        Assert.Equal((3, 2), (width, height));
        for (var i = 0; i < 6; i++)
        {
            Assert.Equal(Rgba[i * 4], rgb[i * 3]);
            Assert.Equal(Rgba[i * 4 + 1], rgb[i * 3 + 1]);
            Assert.Equal(Rgba[i * 4 + 2], rgb[i * 3 + 2]);
            Assert.Equal(Rgba[i * 4 + 3], alpha![i]);
        }
    }

    [Fact]
    public void OpaquePng_HasNoAlpha()
    {
        var rgb = new byte[] { 10, 20, 30, 40, 50, 60 };
        var decoded = ImageStamp.DecodeRaster(PngEncoder.Encode(rgb, 2, 1, colorType: 2));
        Assert.NotNull(decoded);
        Assert.Null(decoded!.Value.alpha);
        Assert.Equal(rgb, decoded.Value.rgb);
    }

    [Fact]
    public void Jpeg_DecodesToItsSize()
    {
        var rgba = new byte[16 * 8 * 4];
        for (var i = 0; i < 16 * 8; i++)
        {
            rgba[i * 4] = 200;
            rgba[i * 4 + 3] = 255;
        }
        var decoded = ImageStamp.DecodeRaster(JpegEncoderImpl.Encode(rgba, 16, 8, 90));
        Assert.NotNull(decoded);
        Assert.Equal((16, 8), (decoded!.Value.width, decoded.Value.height));
        Assert.InRange(decoded.Value.rgb[0], 190, 210);
    }

    [Fact]
    public void UnknownBytes_AreNotARaster()
    {
        Assert.Null(ImageStamp.DecodeRaster(new byte[] { 1, 2, 3, 4, 5, 6 }));
    }
}
