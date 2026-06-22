using System.IO.Compression;

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
    public static byte[] Encode(byte[] pixels, int width, int height,
        int colorType = 2, int bitDepth = 8)
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

        using var output = new MemoryStream();

        // PNG signature
        output.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        // IHDR chunk
        WriteChunk(output, "IHDR", writer =>
        {
            WriteUInt32BE(writer, (uint)width);
            WriteUInt32BE(writer, (uint)height);
            writer.WriteByte((byte)bitDepth);
            writer.WriteByte((byte)colorType);
            writer.WriteByte(0); // compression method (deflate)
            writer.WriteByte(0); // filter method (adaptive)
            writer.WriteByte(0); // interlace method (none)
        });

        // IDAT chunk — filtered + compressed pixel data
        WriteChunk(output, "IDAT", writer =>
        {
            using var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionMode.Compress, leaveOpen: true))
            {
                for (var y = 0; y < height; y++)
                {
                    zlib.WriteByte(0); // filter type: None
                    var rowStart = y * rowBytes;
                    // Clamp to [0, rowBytes]: once rowStart runs past the buffer
                    // (pixels shorter than height*rowBytes) the available length
                    // would go negative, which previously made the pad loop below
                    // iterate billions of times. Clamp so short buffers just pad.
                    var rowLen = Math.Max(0, Math.Min(rowBytes, pixels.Length - rowStart));
                    if (rowLen > 0)
                        zlib.Write(pixels, rowStart, rowLen);
                    // Pad with zeros if pixel data is short
                    for (var p = rowLen; p < rowBytes; p++)
                        zlib.WriteByte(0);
                }
            }
            writer.Write(compressed.ToArray());
        });

        // IEND chunk
        WriteChunk(output, "IEND", _ => { });

        return output.ToArray();
    }

    /// <summary>
    /// Encode 1-bit image data as a PNG file.
    /// </summary>
    /// <param name="pixels">1-bit packed pixel data (MSB first, row-aligned to byte boundary).</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="blackIs1">If true, bit 1 = black; otherwise bit 1 = white.</param>
    public static byte[] Encode1Bit(byte[] pixels, int width, int height, bool blackIs1 = false)
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

        return Encode(gray, width, height, colorType: 0, bitDepth: 8);
    }

    private static void WriteChunk(Stream output, string type, Action<MemoryStream> writeData)
    {
        using var data = new MemoryStream();
        writeData(data);
        var dataBytes = data.ToArray();
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);

        // Length
        WriteUInt32BE(output, (uint)dataBytes.Length);
        // Type
        output.Write(typeBytes);
        // Data
        output.Write(dataBytes);
        // CRC (over type + data)
        var crc = CalculateCrc(typeBytes, dataBytes);
        WriteUInt32BE(output, crc);
    }

    private static void WriteUInt32BE(Stream s, uint value)
    {
        s.WriteByte((byte)(value >> 24));
        s.WriteByte((byte)(value >> 16));
        s.WriteByte((byte)(value >> 8));
        s.WriteByte((byte)(value & 0xFF));
    }

    #region CRC-32

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                if ((c & 1) != 0)
                    c = 0xEDB88320 ^ (c >> 1);
                else
                    c >>= 1;
            }
            table[n] = c;
        }
        return table;
    }

    private static uint CalculateCrc(byte[] type, byte[] data)
    {
        var crc = 0xFFFFFFFF;
        foreach (var b in type)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        foreach (var b in data)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFF;
    }

    #endregion
}
