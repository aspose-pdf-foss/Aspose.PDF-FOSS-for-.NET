
namespace Aspose.Pdf.IO;

/// <summary>
/// Pure .NET PNG encoder. Produces valid PNG files from raw pixel data.
/// Supports DeviceRGB (24-bit), DeviceGray (8-bit), and RGBA (32-bit).
/// </summary>
internal static class PngEncoder
{
    /// <summary>
    /// Encode raw pixel data as a PNG file.
    /// </summary>
    /// <param name="pixels">Row-major pixel data (top to bottom).</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="colorType">PNG color type: 0=Grayscale, 2=RGB, 4=GrayAlpha, 6=RGBA.</param>
    /// <param name="bitDepth">Bits per channel (typically 8).</param>
    /// <param name="level">The deflate level of the image data.</param>
    /// <param name="pixelsPerMetre">The resolution written as a pHYs chunk; none when null.</param>
    // Fast, as for the PDF sample streams: on filtered raster rows greedy matching is faster
    // and no larger than the lazy default (measured on corpus images).
    public static byte[] Encode(byte[] pixels, int width, int height,
        int colorType = 2, int bitDepth = 8, Filters.DeflateLevel level = Filters.DeflateLevel.Fast,
        (int X, int Y)? pixelsPerMetre = null)
    {
        var channelCount = colorType switch
        {
            0 => 1, // Grayscale
            2 => 3, // RGB
            4 => 2, // Gray + Alpha
            6 => 4, // RGBA
            _ => 3,
        };

        var bytesPerPixel = channelCount * (bitDepth / 8);
        var rowBytes = width * bytesPerPixel;

        // The filtered rows (filter type None, so a zero byte before each row's samples) in
        // one buffer of their exact size; pixels shorter than the image leave their rows zero.
        var raw = new byte[(rowBytes + 1) * height];
        for (var y = 0; y < height; y++)
        {
            var rowStart = y * rowBytes;
            var rowLen = Math.Max(0, Math.Min(rowBytes, pixels.Length - rowStart));
            if (rowLen > 0)
                Buffer.BlockCopy(pixels, rowStart, raw, y * (rowBytes + 1) + 1, rowLen);
        }
        var idat = Filters.ManagedDeflater.DeflateZlib(raw, level);

        var ihdr = new MemoryStream(13);
        WriteUInt32BE(ihdr, (uint)width);
        WriteUInt32BE(ihdr, (uint)height);
        ihdr.WriteByte((byte)bitDepth);
        ihdr.WriteByte((byte)colorType);
        ihdr.WriteByte(0); // compression method (deflate)
        ihdr.WriteByte(0); // filter method (adaptive)
        ihdr.WriteByte(0); // interlace method (none)

        using var output = new MemoryStream(idat.Length + 64);
        output.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }); // PNG signature
        WriteChunk(output, "IHDR", ihdr.ToArray());
        if (pixelsPerMetre is { } ppm)
            WritePhysChunk(output, ppm.X, ppm.Y);
        WriteChunk(output, "IDAT", idat);
        WriteChunk(output, "IEND", Array.Empty<byte>());
        return output.ToArray();
    }

    /// <summary>
    /// Encode 1-bit image data as a PNG file.
    /// </summary>
    /// <param name="pixels">1-bit packed pixel data (MSB first, row-aligned to byte boundary).</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="blackIs1">If true, bit 1 = black; otherwise bit 1 = white.</param>
    /// <param name="level">The deflate level, as for <see cref="Encode"/>.</param>
    public static byte[] Encode1Bit(byte[] pixels, int width, int height, bool blackIs1 = false,
        Filters.DeflateLevel level = Filters.DeflateLevel.Fast)
    {
        // Convert 1-bit to 8-bit grayscale for simplicity
        var gray = new byte[width * height];
        var srcBytesPerRow = (width + 7) / 8;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var byteIdx = y * srcBytesPerRow + (x / 8);
                var bitIdx = 7 - (x % 8);
                var bit = (byteIdx < pixels.Length)
                    ? (pixels[byteIdx] >> bitIdx) & 1
                    : 0;

                // Map bit to grayscale
                if (blackIs1)
                    gray[y * width + x] = bit == 1 ? (byte)0 : (byte)255;
                else
                    gray[y * width + x] = bit == 1 ? (byte)255 : (byte)0;
            }
        }

        return Encode(gray, width, height, colorType: 0, bitDepth: 8, level);
    }

    /// <summary>Write a pHYs chunk: the pixel density, in pixels per metre along each axis.</summary>
    internal static void WritePhysChunk(Stream output, int xPixelsPerMetre, int yPixelsPerMetre)
    {
        var phys = new MemoryStream(9);
        WriteUInt32BE(phys, (uint)xPixelsPerMetre);
        WriteUInt32BE(phys, (uint)yPixelsPerMetre);
        phys.WriteByte(1); // unit: metre
        WriteChunk(output, "pHYs", phys.ToArray());
    }

    private static void WriteChunk(Stream output, string type, byte[] dataBytes)
    {
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);

        // Length
        WriteUInt32BE(output, (uint)dataBytes.Length);
        // Type
        output.Write(typeBytes);
        // Data
        output.Write(dataBytes);
        // CRC (over type + data)
        var crc = Crc32.Finish(Crc32.Update(Crc32.Update(Crc32.Start, typeBytes, 0, typeBytes.Length), dataBytes, 0, dataBytes.Length));
        WriteUInt32BE(output, crc);
    }

    private static void WriteUInt32BE(Stream s, uint value)
    {
        s.WriteByte((byte)(value >> 24));
        s.WriteByte((byte)(value >> 16));
        s.WriteByte((byte)(value >> 8));
        s.WriteByte((byte)(value & 0xFF));
    }
}
