using System.IO;
using Aspose.Pdf.Devices;
using Aspose.Pdf.IO;
using Xunit;

namespace Aspose.Pdf.Tests.Devices;

public class TiffDeviceOffsetLimitTests
{
    // Three blank A4 pages at 36 dpi: 297 x 421 RGBA frames, half a megabyte each uncompressed.
    private static Document ThreeBlankPages()
    {
        var doc = new Document();
        for (var i = 0; i < 3; i++) doc.Pages.Add();
        return doc;
    }

    private static TiffDevice UncompressedRgba() =>
        new(new Resolution(36), new TiffSettings { Compression = CompressionType.None, Depth = ColorDepth.Default });

    [Fact]
    public void ProcessRange_OutputThatWouldPassTheClassicOffsetLimit_StopsWithAnError()
    {
        using var doc = ThreeBlankPages();
        var device = UncompressedRgba();
        device.ClassicTiffLimit = 1_000_000;
        using var output = new MemoryStream();

        var error = Assert.Throws<PdfException>(() => device.ProcessRange(doc, 1, 3, output));

        Assert.Contains("32-bit offsets", error.Message);
    }

    [Fact]
    public void ProcessRange_OutputWithinTheLimit_WritesEveryFrame()
    {
        using var doc = ThreeBlankPages();
        using var output = new MemoryStream();

        UncompressedRgba().ProcessRange(doc, 1, 3, output);

        var last = TiffDecoder.DecodeRaster(output.ToArray(), 2, blankUnreadable: false);
        Assert.NotNull(last);
        Assert.Equal((297, 421), (last!.Width, last.Height));
    }
}
