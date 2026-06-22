using Aspose.Pdf.IO;
using Xunit;

namespace Aspose.Pdf.Tests.IO;

public class PngEncoderTests
{
    [Fact]
    public void Encode_RGB_ProducesValidPng()
    {
        // 2x2 red image
        var pixels = new byte[]
        {
            255, 0, 0, 255, 0, 0,
            255, 0, 0, 255, 0, 0,
        };
        var png = PngEncoder.Encode(pixels, 2, 2, colorType: 2);

        // PNG signature
        Assert.Equal(0x89, png[0]);
        Assert.Equal((byte)'P', png[1]);
        Assert.Equal((byte)'N', png[2]);
        Assert.Equal((byte)'G', png[3]);
        Assert.True(png.Length > 50);
    }

    [Fact]
    public void Encode_Grayscale_ProducesValidPng()
    {
        var pixels = new byte[] { 0, 128, 255, 64 };
        var png = PngEncoder.Encode(pixels, 2, 2, colorType: 0);

        Assert.Equal(0x89, png[0]);
        Assert.True(png.Length > 30);
    }

    [Fact]
    public void Encode_1Bit_ProducesValidPng()
    {
        // 8x2 black and white (checkerboard)
        var pixels = new byte[] { 0xAA, 0x55 };
        var png = PngEncoder.Encode1Bit(pixels, 8, 2);

        Assert.Equal(0x89, png[0]);
        Assert.True(png.Length > 30);
    }

    [Fact]
    public void Encode_LargerImage_ProducesValidPng()
    {
        var width = 100;
        var height = 50;
        var pixels = new byte[width * height * 3];
        for (var i = 0; i < pixels.Length; i += 3)
        {
            pixels[i] = (byte)(i % 256);
            pixels[i + 1] = 128;
            pixels[i + 2] = 64;
        }
        var png = PngEncoder.Encode(pixels, width, height);

        Assert.Equal(0x89, png[0]);
        // Should be significantly compressed
        Assert.True(png.Length < pixels.Length);
    }

    [Fact]
    public void Encode_ContainsIHDR()
    {
        var pixels = new byte[12]; // 2x2 RGB
        var png = PngEncoder.Encode(pixels, 2, 2);
        var pngStr = System.Text.Encoding.ASCII.GetString(png);
        Assert.Contains("IHDR", pngStr);
    }

    [Fact]
    public void Encode_ContainsIDAT()
    {
        var pixels = new byte[12];
        var png = PngEncoder.Encode(pixels, 2, 2);
        var pngStr = System.Text.Encoding.ASCII.GetString(png);
        Assert.Contains("IDAT", pngStr);
    }

    [Fact]
    public void Encode_ContainsIEND()
    {
        var pixels = new byte[12];
        var png = PngEncoder.Encode(pixels, 2, 2);
        var pngStr = System.Text.Encoding.ASCII.GetString(png);
        Assert.Contains("IEND", pngStr);
    }
}
