using Aspose.Pdf;
using Xunit;

namespace Aspose.Pdf.Tests.Content;

public sealed class PageAddImageTests
{
    [Fact]
    public void AddImage_Jpeg_AddsImageToPage()
    {
        // Use ImageStamp.FromJpeg directly with known dimensions
        using var doc = Document.Create();
        doc.Pages.Add();
        var page = doc.Pages.At(1);

        // Build a fake JPEG (just FF D8 + minimal data + FF D9)
        var jpegData = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 };
        var stamp = ImageStamp.FromJpeg(jpegData, 4, 4);
        stamp.X = 100;
        stamp.Y = 500;
        stamp.DisplayWidth = 100;
        stamp.DisplayHeight = 100;
        stamp.ApplyTo(page);

        var saved = doc.ToArray();
        using var reopened = Document.Open(saved);
        var images = reopened.Pages.At(1).Images;
        Assert.Single(images);
        Assert.Equal("DCTDecode", images[1].Filter);
    }

    [Fact]
    public void AddImage_FromStream_Works()
    {
        var jpeg = CreateMinimalJpeg(4, 4);

        using var doc = Document.Create();
        doc.Pages.Add();
        var page = doc.Pages.At(1);
        using var stream = new MemoryStream(jpeg);
        page.AddImage(stream, new Rectangle(100, 500, 200, 600));

        var saved = doc.ToArray();
        using var reopened = Document.Open(saved);
        Assert.Single(reopened.Pages.At(1).Images);
    }

    [Fact]
    public void AddImage_ImageStamp_ApplyTo()
    {
        using var doc = Document.Create();
        doc.Pages.Add();
        var page = doc.Pages.At(1);

        // Create a 2x2 RGB image
        var pixels = new byte[2 * 2 * 3];
        for (var i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i * 20);

        var stamp = ImageStamp.FromRgb(pixels, 2, 2);
        stamp.X = 100;
        stamp.Y = 500;
        stamp.DisplayWidth = 100;
        stamp.DisplayHeight = 100;
        stamp.ApplyTo(page);

        var saved = doc.ToArray();
        using var reopened = Document.Open(saved);
        Assert.Single(reopened.Pages.At(1).Images);
    }

    /// <summary>
    /// Create a minimal valid JPEG byte array.
    /// </summary>
    private static byte[] CreateMinimalJpeg(int width, int height)
    {
        using var ms = new MemoryStream();
        // SOI
        ms.Write(new byte[] { 0xFF, 0xD8 });

        // JFIF APP0 marker
        ms.Write(new byte[] { 0xFF, 0xE0 });
        ms.Write(new byte[] { 0x00, 0x10 }); // length 16
        ms.Write("JFIF\0"u8);
        ms.Write(new byte[] { 0x01, 0x01 }); // version 1.1
        ms.Write(new byte[] { 0x00 }); // units: no units
        ms.Write(new byte[] { 0x00, 0x01, 0x00, 0x01 }); // X/Y density
        ms.Write(new byte[] { 0x00, 0x00 }); // thumbnail

        // DQT (quantization table)
        ms.Write(new byte[] { 0xFF, 0xDB });
        ms.Write(new byte[] { 0x00, 0x43 }); // length 67
        ms.WriteByte(0x00); // table 0, 8-bit
        for (var i = 0; i < 64; i++) ms.WriteByte(1); // all 1s

        // SOF0 (start of frame)
        ms.Write(new byte[] { 0xFF, 0xC0 });
        ms.Write(new byte[] { 0x00, 0x0B }); // length 11
        ms.WriteByte(0x08); // 8-bit precision
        ms.WriteByte((byte)(height >> 8));
        ms.WriteByte((byte)(height & 0xFF));
        ms.WriteByte((byte)(width >> 8));
        ms.WriteByte((byte)(width & 0xFF));
        ms.WriteByte(0x01); // 1 component (grayscale for simplicity)
        ms.WriteByte(0x01); // component ID
        ms.WriteByte(0x11); // sampling 1x1
        ms.WriteByte(0x00); // quant table 0

        // DHT (Huffman table - minimal DC table)
        ms.Write(new byte[] { 0xFF, 0xC4 });
        ms.Write(new byte[] { 0x00, 0x1F }); // length 31
        ms.WriteByte(0x00); // DC table 0
        // 16 bytes of code counts
        var counts = new byte[16];
        counts[0] = 1; // 1 code of length 1
        ms.Write(counts);
        ms.Write(new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B });

        // SOS (start of scan)
        ms.Write(new byte[] { 0xFF, 0xDA });
        ms.Write(new byte[] { 0x00, 0x08 }); // length
        ms.WriteByte(0x01); // 1 component
        ms.WriteByte(0x01); // component 1
        ms.WriteByte(0x00); // DC/AC table 0/0
        ms.Write(new byte[] { 0x00, 0x3F, 0x00 }); // spectral selection

        // Scan data (all zeros)
        for (var i = 0; i < width * height; i++) ms.WriteByte(0x00);

        // EOI
        ms.Write(new byte[] { 0xFF, 0xD9 });

        return ms.ToArray();
    }
}
