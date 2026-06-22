namespace Aspose.Pdf.Devices;

/// <summary>
/// Renders PDF document pages into BMP image format.
/// </summary>
public sealed class BmpDevice : ImageDevice
{
    public BmpDevice(IPageRenderer renderer) : base(renderer) { }
    public BmpDevice(IPageRenderer renderer, Resolution resolution) : base(renderer, resolution) { }
    public BmpDevice() : base() { }
    public BmpDevice(Resolution resolution) : base(resolution) { }
    public BmpDevice(int width, int height) : base(width, height) { }
    public BmpDevice(int width, int height, Resolution resolution) : base(width, height, resolution) { }
    public BmpDevice(Aspose.Pdf.PageSize pageSize) : base(pageSize) { }
    public BmpDevice(Aspose.Pdf.PageSize pageSize, Resolution resolution) : base(pageSize, resolution) { }

    /// <inheritdoc />
    public override void Process(Page page, Stream output)
    {
        var rgba = RenderPage(page);
        var bmp = EncodeBmp(rgba.Data, rgba.Width, rgba.Height);
        output.Write(bmp);
    }

    /// <summary>
    /// Encode RGBA pixels to a 24-bit BMP file (bottom-to-top row order).
    /// </summary>
    private static byte[] EncodeBmp(byte[] rgba, int width, int height)
    {
        var rowBytes = width * 3;
        var paddedRowBytes = (rowBytes + 3) & ~3; // pad to 4-byte boundary
        var pixelDataSize = paddedRowBytes * height;
        var fileSize = 14 + 40 + pixelDataSize; // header + DIB + pixels

        var bmp = new byte[fileSize];

        // BMP file header (14 bytes)
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        WriteLE32(bmp, 2, fileSize);
        WriteLE32(bmp, 10, 54); // pixel data offset

        // BITMAPINFOHEADER (40 bytes)
        WriteLE32(bmp, 14, 40); // DIB header size
        WriteLE32(bmp, 18, width);
        WriteLE32(bmp, 22, height);
        WriteLE16(bmp, 26, 1);  // color planes
        WriteLE16(bmp, 28, 24); // bits per pixel
        WriteLE32(bmp, 34, pixelDataSize);

        // Pixel data: RGBA top-to-bottom → BGR bottom-to-top
        for (var y = 0; y < height; y++)
        {
            var srcRow = y * width * 4;
            var dstRow = 54 + (height - 1 - y) * paddedRowBytes;
            for (var x = 0; x < width; x++)
            {
                var si = srcRow + x * 4;
                var di = dstRow + x * 3;
                bmp[di] = rgba[si + 2];     // B
                bmp[di + 1] = rgba[si + 1]; // G
                bmp[di + 2] = rgba[si];     // R
            }
        }

        return bmp;
    }

    private static void WriteLE16(byte[] buf, int offset, int value)
    {
        buf[offset] = (byte)(value & 0xFF);
        buf[offset + 1] = (byte)((value >> 8) & 0xFF);
    }

    private static void WriteLE32(byte[] buf, int offset, int value)
    {
        buf[offset] = (byte)(value & 0xFF);
        buf[offset + 1] = (byte)((value >> 8) & 0xFF);
        buf[offset + 2] = (byte)((value >> 16) & 0xFF);
        buf[offset + 3] = (byte)((value >> 24) & 0xFF);
    }
}
