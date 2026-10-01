using System.Collections.Generic;
using System.Linq;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Xunit;

namespace Aspose.Pdf.Tests.IO;

public class TiffRasterTests
{
    private const int Short = 3;
    private const int Long = 4;

    // A little-endian TIFF, one IFD per page, each page one strip: (tag, type, value) entries,
    // the strip offset and byte count added.
    private static byte[] Tiff(params (int Tag, int Type, int Value)[][] pages) =>
        Tiff(pages.Select(p => (p, new byte[] { 0 })).ToArray());

    private static byte[] Tiff(params ((int Tag, int Type, int Value)[] Entries, byte[] Strip)[] pages)
    {
        var o = new List<byte> { 0x49, 0x49, 42, 0, 0, 0, 0, 0 };
        void Put32(int at, int v) { for (var k = 0; k < 4; k++) o[at + k] = (byte)(v >> (8 * k)); }
        void Add16(int v) { o.Add((byte)v); o.Add((byte)(v >> 8)); }
        void Add32(int v) { for (var k = 0; k < 4; k++) o.Add((byte)(v >> (8 * k))); }
        var link = 4;
        foreach (var (entries, strip) in pages)
        {
            var offset = o.Count;
            o.AddRange(strip);
            if (o.Count % 2 == 1) o.Add(0);
            var all = entries.Concat(new[] { (273, Long, offset), (279, Long, strip.Length) }).OrderBy(e => e.Item1).ToList();
            Put32(link, o.Count);
            Add16(all.Count);
            foreach (var (tag, type, value) in all)
            {
                Add16(tag); Add16(type); Add32(1);
                if (type == Short) { Add16(value); Add16(0); } else Add32(value);
            }
            link = o.Count;
            Add32(0);
        }
        return o.ToArray();
    }

    private static (int, int, int)[] Gray(int width, int height, int bits, int compression, int photometric = 1) =>
        new[] { (256, Short, width), (257, Short, height), (258, Short, bits), (259, Short, compression), (262, Short, photometric), (278, Short, height) };

    [Fact]
    public void DecodeRaster_UncompressedGray_KeepsSamplesAndTags()
    {
        var samples = new byte[] { 1, 2, 3, 4, 5, 6 };
        var raster = TiffDecoder.DecodeRaster(Tiff((Gray(3, 2, 8, 1, photometric: 0), samples)), 0, false);
        Assert.NotNull(raster);
        Assert.Equal(3, raster!.Width);
        Assert.Equal(2, raster.Height);
        Assert.Equal(8, raster.BitsPerSample);
        Assert.Equal(1, raster.SamplesPerPixel);
        Assert.Equal(0, raster.Photometric);
        Assert.Equal(3, raster.RowBytes);
        Assert.Equal(samples, raster.Samples);
    }

    [Fact]
    public void DecodeRaster_Group4_RoundTripsWithoutAColumnShift()
    {
        var bits = new byte[] { 0xFF, 0x00, 0xF0, 0x0F, 0xAA, 0x55, 0x00, 0xFF };
        var g4 = CcittG4Encoder.Encode(bits, 16, 4, 2);
        var raster = TiffDecoder.DecodeRaster(Tiff((Gray(16, 4, 1, 4, photometric: 0), g4)), 0, false);
        Assert.NotNull(raster);
        Assert.Equal(4, raster!.Compression);
        Assert.Equal(bits, raster.Samples);
    }

    [Fact]
    public void DecodeRaster_UnreadableStrip_FailsUnlessBlanked()
    {
        var tiff = Tiff((Gray(16, 4, 1, 4, photometric: 0), new byte[] { 0, 0, 0 }));
        Assert.Null(TiffDecoder.DecodeRaster(tiff, 0, false));
        var blank = TiffDecoder.DecodeRaster(tiff, 0, true);
        Assert.NotNull(blank);
        Assert.All(blank!.Samples, b => Assert.Equal(0, b));
    }

    [Fact]
    public void DecodeRaster_PicksThePageAlongTheChain()
    {
        var tiff = Tiff((Gray(2, 1, 8, 1), new byte[] { 7, 8 }), (Gray(1, 1, 8, 1), new byte[] { 9 }));
        Assert.Equal(new byte[] { 9 }, TiffDecoder.DecodeRaster(tiff, 1, false)!.Samples);
        Assert.Null(TiffDecoder.DecodeRaster(tiff, 2, false));
        Assert.Null(TiffDecoder.DecodeRaster(tiff, -1, false));
        Assert.Null(TiffDecoder.DecodeRaster(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, 0, false));
    }

    [Fact]
    public void DecodeRaster_SixteenBitLittleEndian_ComesBackBigEndian()
    {
        var raster = TiffDecoder.DecodeRaster(Tiff((Gray(2, 1, 16, 1), new byte[] { 0x34, 0x12, 0x78, 0x56 })), 0, false);
        Assert.Equal(new byte[] { 0x12, 0x34, 0x56, 0x78 }, raster!.Samples);
    }
}
