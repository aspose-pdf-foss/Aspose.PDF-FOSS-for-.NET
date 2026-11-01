namespace Aspose.Pdf.IO;

/// <summary>CRC-32 (IEEE 802.3, reflected polynomial 0xEDB88320) as PNG and ZIP use it.</summary>
internal static class Crc32
{
    private const uint Polynomial = 0xEDB88320;
    private const uint Initial = 0xFFFFFFFF;
    // Tables[k][b] carries byte b through k + 1 steps of the register: the slicing-by-eight
    // update of eight bytes at once reads one entry from each table.
    private static readonly uint[][] Tables = BuildTables();

    public static uint Compute(byte[] data) => Compute(data, 0, data.Length);

    public static uint Compute(byte[] data, int offset, int count) => Finish(Update(Initial, data, offset, count));

    /// <summary>Feeds more bytes into a running value that started at <see cref="Start"/>.</summary>
    public static uint Update(uint crc, byte[] data, int offset, int count)
    {
        var end = offset + count;
        var t = Tables;
        for (; end - offset >= 8; offset += 8)
        {
            var low = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset)) ^ crc;
            var high = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 4));
            crc = t[7][low & 0xFF] ^ t[6][(low >> 8) & 0xFF] ^ t[5][(low >> 16) & 0xFF] ^ t[4][low >> 24]
                ^ t[3][high & 0xFF] ^ t[2][(high >> 8) & 0xFF] ^ t[1][(high >> 16) & 0xFF] ^ t[0][high >> 24];
        }
        for (; offset < end; offset++)
            crc = t[0][(crc ^ data[offset]) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    public static uint Start => Initial;

    public static uint Finish(uint crc) => crc ^ Initial;

    private static uint[][] BuildTables()
    {
        var tables = new uint[8][];
        tables[0] = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? Polynomial ^ (c >> 1) : c >> 1;
            tables[0][n] = c;
        }
        for (var k = 1; k < 8; k++)
        {
            tables[k] = new uint[256];
            for (var n = 0; n < 256; n++)
            {
                var c = tables[k - 1][n];
                tables[k][n] = tables[0][c & 0xFF] ^ (c >> 8);
            }
        }
        return tables;
    }
}
