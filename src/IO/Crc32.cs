namespace Aspose.Pdf.IO;

/// <summary>CRC-32 (IEEE 802.3, reflected polynomial 0xEDB88320) as PNG and ZIP use it.</summary>
internal static class Crc32
{
    private const uint Polynomial = 0xEDB88320;
    private const uint Initial = 0xFFFFFFFF;
    private static readonly uint[] Table = BuildTable();

    public static uint Compute(byte[] data) => Compute(data, 0, data.Length);

    public static uint Compute(byte[] data, int offset, int count) => Finish(Update(Initial, data, offset, count));

    /// <summary>Feeds more bytes into a running value that started at <see cref="Start"/>.</summary>
    public static uint Update(uint crc, byte[] data, int offset, int count)
    {
        for (var i = offset; i < offset + count; i++)
            crc = Table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    public static uint Start => Initial;

    public static uint Finish(uint crc) => crc ^ Initial;

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? Polynomial ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }
}
