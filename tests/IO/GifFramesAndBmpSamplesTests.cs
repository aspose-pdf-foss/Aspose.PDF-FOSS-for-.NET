using Aspose.Pdf.IO;
using Xunit;

namespace Aspose.Pdf.Tests.IO;

public class GifFramesAndBmpSamplesTests
{
    // The GDI+-written 4x3 GIF the decoder's own tests use: a 256-entry global palette, pixels
    // cycling red / green / blue / white along each row, entry 252 marked transparent.
    private static byte[] Gif() => System.Convert.FromBase64String(GifFixture);

    private const int HeaderAndPalette = 6 + 7 + 768;

    [Fact]
    public void DecodeFrames_ReadsIndicesTableAndTransparency()
    {
        var gif = GifDecoder.DecodeFrames(Gif());
        Assert.NotNull(gif);
        Assert.Equal(4, gif!.LogicalWidth);
        Assert.Equal(3, gif.LogicalHeight);
        Assert.Equal(8, gif.GlobalTableBits);
        var frame = Assert.Single(gif.Frames);
        Assert.Equal(4, frame.Width);
        Assert.Equal(3, frame.Height);
        Assert.False(frame.LocalTable);
        Assert.Equal(768, frame.ColourTable.Length);
        Assert.Equal(252, frame.TransparentIndex);
        Assert.Equal(12, frame.Indices.Length);
        var first = frame.Indices[0] * 3;
        Assert.Equal(new byte[] { 255, 0, 0 }, frame.ColourTable[first..(first + 3)]);
    }

    [Fact]
    public void DecodeFrames_TransparencyBelongsToOneFrame()
    {
        // The fixture's frame (control extension + image) followed by the bare image again:
        // the second frame has no control extension of its own, so nothing is transparent.
        var gif = Gif();
        var frameBlocks = gif[HeaderAndPalette..(gif.Length - 1)];
        var imageOnly = frameBlocks[8..];
        var twice = new byte[HeaderAndPalette + frameBlocks.Length + imageOnly.Length + 1];
        System.Array.Copy(gif, twice, HeaderAndPalette);
        System.Array.Copy(frameBlocks, 0, twice, HeaderAndPalette, frameBlocks.Length);
        System.Array.Copy(imageOnly, 0, twice, HeaderAndPalette + frameBlocks.Length, imageOnly.Length);
        twice[^1] = 0x3B;

        var frames = GifDecoder.DecodeFrames(twice)!.Frames;
        Assert.Equal(2, frames.Count);
        Assert.Equal(252, frames[0].TransparentIndex);
        Assert.Equal(-1, frames[1].TransparentIndex);
        Assert.Equal(frames[0].Indices, frames[1].Indices);
        Assert.Single(GifDecoder.DecodeFrames(twice, 1)!.Frames);
    }

    [Fact]
    public void DecodeFrames_RefusesAShortPalette()
    {
        Assert.Null(GifDecoder.DecodeFrames(Gif()[..40]));
    }

    // A 6x3 8-bit RLE bitmap with four palette entries: rows (bottom first) of 1,1,1,2,2,2, then
    // an absolute run 5,6,7 (word padded), then six 3s.
    private static readonly byte[] Rle8 = BuildRle8();

    private static byte[] BuildRle8()
    {
        byte[] pixels = { 3, 1, 3, 2, 0, 0, 0, 3, 5, 6, 7, 0, 0, 0, 6, 3, 0, 1 };
        var info = new byte[40];
        void Put(int at, int v) { info[at] = (byte)v; info[at + 1] = (byte)(v >> 8); info[at + 2] = (byte)(v >> 16); info[at + 3] = (byte)(v >> 24); }
        Put(0, 40); Put(4, 6); Put(8, 3);
        info[12] = 1; info[14] = 8;
        Put(16, 1); Put(20, pixels.Length); Put(24, 2835); Put(28, 2835);
        var palette = new byte[] { 0, 0, 0, 0, 10, 20, 30, 0, 40, 50, 60, 0, 70, 80, 90, 0 };
        var offset = 14 + info.Length + palette.Length;
        var file = new byte[offset + pixels.Length];
        file[0] = (byte)'B'; file[1] = (byte)'M';
        file[10] = (byte)offset;
        System.Array.Copy(info, 0, file, 14, info.Length);
        System.Array.Copy(palette, 0, file, 54, palette.Length);
        System.Array.Copy(pixels, 0, file, offset, pixels.Length);
        return file;
    }

    [Fact]
    public void DecodeSamples_Rle8_TopRowFirst_PaletteBoundedByThePixels()
    {
        var bmp = BmpDecoder.DecodeSamples(Rle8, false, out var failure);
        Assert.Equal(BmpDecoder.BmpFailure.None, failure);
        Assert.NotNull(bmp);
        Assert.Equal(8, bmp!.BitCount);
        Assert.Equal(12, bmp.Palette!.Length);
        Assert.Equal(new byte[] { 30, 20, 10 }, bmp.Palette[3..6]);
        Assert.Equal(new byte[] { 3, 3, 3, 3, 3, 3, 5, 6, 7, 0, 0, 0, 1, 1, 1, 2, 2, 2 }, bmp.Samples);
        Assert.Equal(2835, bmp.XPelsPerMeter);
    }

    [Fact]
    public void DecodeAsPng_NowReadsRle()
    {
        Assert.NotNull(BmpDecoder.DecodeAsPng(Rle8));
    }

    [Fact]
    public void DecodeSamples_UnknownCompression_IsNamed()
    {
        var bad = (byte[])Rle8.Clone();
        bad[14 + 16] = 9;
        Assert.Null(BmpDecoder.DecodeSamples(bad, false, out var failure));
        Assert.Equal(BmpDecoder.BmpFailure.UnsupportedCompression, failure);
    }

    private const string GifFixture =
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
}
