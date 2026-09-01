using Aspose.Pdf.IO;
using Xunit;

namespace Aspose.Pdf.Tests.IO;

public class GifDecoderTests
{
    // A real 4x3 GIF89a written by an independent encoder (GDI+), so this asserts
    // agreement with a shipping implementation rather than with a hand-rolled one.
    // 256-entry web-safe global palette; the pixels cycle red / green / blue / white
    // across each row, and the encoder marked palette entry 252 - the red - transparent.
    private const string FourByThreeGif =
        "R0lGODlhBAADAPcAAAAAAAAAMwAAZgAAmQAAzAAA/wArAAArMwArZgArmQArzAAr/wBVAABVMwBVZgBVmQBVzABV/wCAAACA"
        + "MwCAZgCAmQCAzACA/wCqAACqMwCqZgCqmQCqzACq/wDVAADVMwDVZgDVmQDVzADV/wD/AAD/MwD/ZgD/mQD/zAD//zMAADMA"
        + "MzMAZjMAmTMAzDMA/zMrADMrMzMrZjMrmTMrzDMr/zNVADNVMzNVZjNVmTNVzDNV/zOAADOAMzOAZjOAmTOAzDOA/zOqADOq"
        + "MzOqZjOqmTOqzDOq/zPVADPVMzPVZjPVmTPVzDPV/zP/ADP/MzP/ZjP/mTP/zDP//2YAAGYAM2YAZmYAmWYAzGYA/2YrAGYr"
        + "M2YrZmYrmWYrzGYr/2ZVAGZVM2ZVZmZVmWZVzGZV/2aAAGaAM2aAZmaAmWaAzGaA/2aqAGaqM2aqZmaqmWaqzGaq/2bVAGbV"
        + "M2bVZmbVmWbVzGbV/2b/AGb/M2b/Zmb/mWb/zGb//5kAAJkAM5kAZpkAmZkAzJkA/5krAJkrM5krZpkrmZkrzJkr/5lVAJlV"
        + "M5lVZplVmZlVzJlV/5mAAJmAM5mAZpmAmZmAzJmA/5mqAJmqM5mqZpmqmZmqzJmq/5nVAJnVM5nVZpnVmZnVzJnV/5n/AJn/"
        + "M5n/Zpn/mZn/zJn//8wAAMwAM8wAZswAmcwAzMwA/8wrAMwrM8wrZswrmcwrzMwr/8xVAMxVM8xVZsxVmcxVzMxV/8yAAMyA"
        + "M8yAZsyAmcyAzMyA/8yqAMyqM8yqZsyqmcyqzMyq/8zVAMzVM8zVZszVmczVzMzV/8z/AMz/M8z/Zsz/mcz/zMz///8AAP8A"
        + "M/8AZv8Amf8AzP8A//8rAP8rM/8rZv8rmf8rzP8r//9VAP9VM/9VZv9Vmf9VzP9V//+AAP+AM/+AZv+Amf+AzP+A//+qAP+q"
        + "M/+qZv+qmf+qzP+q///VAP/VM//VZv/Vmf/VzP/V////AP//M///Zv//mf//zP///wAAAAAAAAAAAAAAACH5BAEAAPwALAAA"
        + "AAAEAAMAAAgNAKWRKLBv4D5pBAUGBAA7";

    private static byte[] Fixture() => System.Convert.FromBase64String(FourByThreeGif);

    /// <summary>Offset of the transparent-index byte inside the Graphic Control
    /// Extension: signature(6) + screen descriptor(7) + 256 palette entries(768) then
    /// 21 F9 04 packed delay-lo delay-hi -> the index.</summary>
    private const int TransparentIndexOffset = 6 + 7 + 768 + 6;

    [Fact]
    public void IsGif_RecognisesBothSignatures()
    {
        Assert.True(GifDecoder.IsGif(System.Text.Encoding.ASCII.GetBytes("GIF87a...")));
        Assert.True(GifDecoder.IsGif(System.Text.Encoding.ASCII.GetBytes("GIF89a...")));
        Assert.False(GifDecoder.IsGif(System.Text.Encoding.ASCII.GetBytes("PNG89a...")));
        Assert.False(GifDecoder.IsGif(new byte[] { 1, 2 }));
    }

    [Fact]
    public void TryDecode_ReadsPaletteAndPixels()
    {
        Assert.True(GifDecoder.TryDecode(Fixture(), out var rgb, out _, out var w, out var h));
        Assert.Equal(4, w);
        Assert.Equal(3, h);

        byte[] red = [255, 0, 0], green = [0, 255, 0], blue = [0, 0, 255], white = [255, 255, 255];
        byte[][] expected =
        [
            red, green, blue, white,
            green, blue, white, red,
            blue, white, red, green,
        ];
        for (var i = 0; i < expected.Length; i++)
            Assert.Equal(expected[i], rgb[(i * 3)..(i * 3 + 3)]);
    }

    [Fact]
    public void TryDecode_LeavesAnUnusedTransparentIndexAlone()
    {
        // The encoder set a transparent index (252) that no pixel actually uses, which is
        // a perfectly ordinary thing for it to do - nothing may come back transparent.
        Assert.True(GifDecoder.TryDecode(Fixture(), out _, out var alpha, out _, out _));
        Assert.All(alpha, a => Assert.Equal(255, a));
    }

    [Fact]
    public void TryDecode_HonoursTheTransparentIndex()
    {
        // Point the Graphic Control Extension at 210, the palette entry the red pixels
        // really use, and exactly those three pixels must come back fully transparent.
        var gif = Fixture();
        gif[TransparentIndexOffset] = 210;
        Assert.True(GifDecoder.TryDecode(gif, out _, out var alpha, out _, out _));
        Assert.Equal(new byte[]
        {
            0, 255, 255, 255,
            255, 255, 255, 0,
            255, 255, 0, 255,
        }, alpha);
    }

    [Fact]
    public void TryDecode_RejectsWhatIsNotAGif()
    {
        Assert.False(GifDecoder.TryDecode(
            System.Text.Encoding.ASCII.GetBytes("not an image at all"), out _, out _, out var w, out _));
        Assert.Equal(0, w);
    }

    [Fact]
    public void TryDecode_RefusesAHeaderWithNoFrame()
    {
        // Signature and screen descriptor only: there is no image descriptor to decode,
        // and the caller must be told so rather than handed a blank raster.
        Assert.False(GifDecoder.TryDecode(Fixture()[..13], out _, out _, out _, out _));
    }
}
