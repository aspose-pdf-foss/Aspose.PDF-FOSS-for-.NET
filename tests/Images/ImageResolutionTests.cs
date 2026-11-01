using Aspose.Pdf.IO;
using Xunit;

namespace Aspose.Pdf.Tests.Images;

/// <summary>The resolution a picture's file states, read on every OS the way the Windows image codec reads it
/// (a table cell lays a picture out at its pixels over that resolution): 96 DPI where the file states none.</summary>
public class ImageResolutionTests
{
    // A JPEG's header as far as its frame: SOI, the given segments, then a baseline frame of 37 x 23 pixels.
    private static byte[] Jpeg(params byte[][] segments) =>
        [0xFF, 0xD8, .. segments.SelectMany(s => s), 0xFF, 0xC0, 0, 11, 8, 0, 23, 0, 37, 1, 1, 0x11, 0, 0xFF, 0xD9];

    // A JFIF segment: units 0 (no unit, an aspect ratio), 1 (dots per inch) or 2 (dots per centimetre).
    private static byte[] Jfif(byte units, int x, int y) =>
        [0xFF, 0xE0, 0, 16, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, units, (byte)(x >> 8), (byte)x, (byte)(y >> 8), (byte)y, 0, 0];

    // An EXIF segment whose first directory holds X and Y resolutions of x/1 and y/1 and a ResolutionUnit
    // (1 none, 2 inch, 3 centimetre).
    private static byte[] Exif(int x, int y, int unit)
    {
        byte[] tiff =
        [
            (byte)'I', (byte)'I', 42, 0, 8, 0, 0, 0, 3, 0,
            0x1A, 1, 5, 0, 1, 0, 0, 0, 50, 0, 0, 0,
            0x1B, 1, 5, 0, 1, 0, 0, 0, 58, 0, 0, 0,
            0x28, 1, 3, 0, 1, 0, 0, 0, (byte)unit, 0, 0, 0,
            0, 0, 0, 0,
            (byte)x, (byte)(x >> 8), 0, 0, 1, 0, 0, 0,
            (byte)y, (byte)(y >> 8), 0, 0, 1, 0, 0, 0,
        ];
        var length = 2 + 6 + tiff.Length;
        return [0xFF, 0xE1, (byte)(length >> 8), (byte)length, (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0, .. tiff];
    }

    [Fact]
    public void APngStatesItsResolutionInItsPhysChunkAndWithoutOneReadsAt96Dpi()
    {
        Assert.Equal((96f, 96f), ImageDimensions.Resolution(Aspose.Pdf.Tests.FlowImageTests.Png(560, 200)));
        var stated = PngEncoder.Encode(new byte[3], 1, 1, pixelsPerMetre: (3780, 11811));
        Assert.Equal((96.012f, 299.9994f), ImageDimensions.Resolution(stated));
    }

    [Fact]
    public void AJpegsJfifDensityIsReadInItsUnitAndAnAspectRatioReadsAt96Dpi()
    {
        Assert.Equal((96f, 96f), ImageDimensions.Resolution(Jpeg()));
        Assert.Equal((96f, 96f), ImageDimensions.Resolution(Jpeg(Jfif(0, 72, 72))));
        Assert.Equal((300f, 150f), ImageDimensions.Resolution(Jpeg(Jfif(1, 300, 150))));
        Assert.Equal((299.72f, 299.72f), ImageDimensions.Resolution(Jpeg(Jfif(2, 118, 118))));
    }

    [Fact]
    public void AJpegsExifDensityRestatesItsJfifOneInFileOrder()
    {
        // A camera's EXIF segment after its JFIF one decides; one before it is overruled by it; an EXIF unit of
        // none leaves no resolution stated.
        Assert.Equal((300f, 300f), ImageDimensions.Resolution(Jpeg(Jfif(1, 72, 72), Exif(300, 300, 2))));
        Assert.Equal((72f, 72f), ImageDimensions.Resolution(Jpeg(Exif(300, 300, 2), Jfif(1, 72, 72))));
        Assert.Equal((96f, 96f), ImageDimensions.Resolution(Jpeg(Jfif(1, 72, 72), Exif(300, 300, 1))));
    }

    [Fact]
    public void AGifStatesNoResolutionAndABmpItsPixelsPerMetre()
    {
        byte[] gif = [(byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a', 37, 0, 23, 0, 0, 0, 0, 0x3B];
        Assert.Equal((96f, 96f), ImageDimensions.Resolution(gif));

        static byte[] Bmp(int pixelsPerMetre)
        {
            var bmp = new byte[58];
            bmp[0] = (byte)'B';
            bmp[1] = (byte)'M';
            bmp[14] = 40;
            bmp[18] = 1;
            bmp[22] = 1;
            bmp[26] = 1;
            bmp[28] = 24;
            BitConverter.GetBytes(pixelsPerMetre).CopyTo(bmp, 38);
            BitConverter.GetBytes(pixelsPerMetre).CopyTo(bmp, 42);
            return bmp;
        }
        Assert.Equal((72.009f, 72.009f), ImageDimensions.Resolution(Bmp(2835)));
        Assert.Equal((96f, 96f), ImageDimensions.Resolution(Bmp(0)));
    }

    // A BMP of 37 x 23 pixels whose DIB header is headerSize bytes long, every byte after the fields its size has
    // room for - a shorter header's palette or pixels - set.
    private static byte[] BmpWithHeader(int headerSize)
    {
        var bmp = Enumerable.Repeat((byte)0x7F, 14 + headerSize + 64).ToArray();
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        BitConverter.GetBytes(headerSize).CopyTo(bmp, 14);
        if (headerSize == 12)
        {
            BitConverter.GetBytes((short)37).CopyTo(bmp, 18);
            BitConverter.GetBytes((short)23).CopyTo(bmp, 20);
            BitConverter.GetBytes((short)1).CopyTo(bmp, 22);
            BitConverter.GetBytes((short)24).CopyTo(bmp, 24);
        }
        else
        {
            BitConverter.GetBytes(37).CopyTo(bmp, 18);
            BitConverter.GetBytes(23).CopyTo(bmp, 22);
            BitConverter.GetBytes((short)1).CopyTo(bmp, 26);
            BitConverter.GetBytes((short)24).CopyTo(bmp, 28);
        }
        return bmp;
    }

    [Theory]
    [InlineData(12)]
    [InlineData(16)]
    public void ABmpWhoseHeaderHasNoRoomForAResolutionStatesNone_AndItsOwnSize(int headerSize)
    {
        // An OS/2 BMP's core header (12 bytes) holds a 16-bit width and height, a short OS/2 2.x one (16) a 32-bit
        // pair, and neither the pixels per metre: the bytes where a 40-byte header keeps those belong to the
        // palette or the pixels.
        var bmp = BmpWithHeader(headerSize);
        Assert.Equal((37.0, 23.0), ImageDimensions.Read(bmp));
        Assert.Equal((96f, 96f), ImageDimensions.Resolution(bmp));
    }

    [Fact]
    public void ABmpStoredTopDownStatesItsHeightAsANegativeNumber()
    {
        // A BMP whose rows run top to bottom says so by a negative height: the picture is that many rows high.
        var bmp = BmpWithHeader(40);
        BitConverter.GetBytes(-23).CopyTo(bmp, 22);
        Assert.Equal((37.0, 23.0), ImageDimensions.Read(bmp));
    }
}
