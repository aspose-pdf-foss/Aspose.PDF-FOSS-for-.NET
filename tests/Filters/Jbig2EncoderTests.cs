using Aspose.Pdf.Core;
using Aspose.Pdf.IO.Filters;
using Xunit;

namespace Aspose.Pdf.Tests.Filters;

/// <summary>What <see cref="Jbig2Encoder"/> writes decodes, through the JBIG2Decode filter, to exactly
/// the image it was given - PDF samples, 0 = black - and a scan-like page costs less than CCITT G4.</summary>
public class Jbig2EncoderTests
{
    private static byte[] RoundTrip(byte[] samples, int width, int height)
    {
        var stride = (width + 7) / 8;
        var bitmap = samples.Select(b => (byte)~b).ToArray();
        var encoded = Jbig2Encoder.Encode(bitmap, width, height, stride);
        var dict = new PdfDictionary();
        dict.Set("Filter", new PdfName("JBIG2Decode"));
        return StreamFilter.Decode(encoded, dict);
    }

    /// <summary>White rows, then rows of dark strokes and gaps; the padding bits of each row are white.</summary>
    private static byte[] ScanLike(int width, int height)
    {
        var stride = (width + 7) / 8;
        var samples = Enumerable.Repeat((byte)0xFF, stride * height).ToArray();
        var random = new Random(7);
        for (var y = 0; y < height; y++)
        {
            if (y % 40 < 25) continue;
            for (var x = 0; x < width; x++)
                if ((x / 3 + y / 2) % 5 == 0 || random.Next(50) == 0)
                    samples[y * stride + (x >> 3)] &= (byte)~(0x80 >> (x & 7));
        }
        return samples;
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(7, 3)]
    [InlineData(64, 64)]
    [InlineData(333, 97)]
    public void AScanLikeImageDecodesToItself(int width, int height)
    {
        var samples = ScanLike(width, height);

        Assert.Equal(samples, RoundTrip(samples, width, height));
    }

    [Fact]
    public void ARandomImageDecodesToItself()
    {
        const int width = 257, height = 61;
        var samples = new byte[(width + 7) / 8 * height];
        new Random(11).NextBytes(samples);
        // The padding bits past the last column are not image: the decoder gives them as white.
        for (var y = 0; y < height; y++) samples[y * 33 + 32] |= 0x7F;

        Assert.Equal(samples, RoundTrip(samples, width, height));
    }

    [Fact]
    public void AScanLikePageIsSmallerThanCcittG4()
    {
        const int width = 1700, height = 2200;
        var samples = ScanLike(width, height);
        var bitmap = samples.Select(b => (byte)~b).ToArray();

        var jbig2 = Jbig2Encoder.Encode(bitmap, width, height, (width + 7) / 8);
        var g4 = CcittG4Encoder.Encode(bitmap, width, height, (width + 7) / 8);

        Assert.True(jbig2.Length < g4.Length, $"JBIG2 {jbig2.Length} bytes, G4 {g4.Length}");
    }
}
