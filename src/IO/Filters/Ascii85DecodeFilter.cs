namespace Aspose.Pdf.IO.Filters;

/// <summary>
/// Decodes ASCII base-85 encoded data (PDF 32000 §7.4.3).
/// Each group of 5 ASCII characters encodes 4 bytes; 'z' is shorthand for 4 zero bytes.
/// </summary>
internal static class Ascii85DecodeFilter
{
    public static byte[] Decode(byte[] data)
    {
        var output = new List<byte>();
        var tuple = new byte[5];
        var tupleCount = 0;

        for (var i = 0; i < data.Length; i++)
        {
            var b = data[i];

            // Skip whitespace
            if (b == ' ' || b == '\t' || b == '\r' || b == '\n' || b == '\f')
                continue;

            // End of data marker
            if (b == '~')
            {
                // Expect '>' after '~'
                break;
            }

            // 'z' represents four zero bytes (tolerate even mid-group per lenient handling)
            if (b == 'z')
            {
                tupleCount = 0; // reset any partial group
                output.Add(0);
                output.Add(0);
                output.Add(0);
                output.Add(0);
                continue;
            }

            if (b < '!' || b > 'u')
                continue; // skip invalid

            tuple[tupleCount++] = (byte)(b - '!');

            if (tupleCount == 5)
            {
                DecodeTuple(tuple, 5, output);
                tupleCount = 0;
            }
        }

        // Handle remaining bytes
        if (tupleCount > 1)
        {
            // Pad with 'u' (84)
            for (var i = tupleCount; i < 5; i++)
                tuple[i] = 84;
            DecodeTuple(tuple, tupleCount, output);
        }

        return output.ToArray();
    }

    private static void DecodeTuple(byte[] tuple, int count, List<byte> output)
    {
        long value = 0;
        value = value * 85 + tuple[0];
        value = value * 85 + tuple[1];
        value = value * 85 + tuple[2];
        value = value * 85 + tuple[3];
        value = value * 85 + tuple[4];

        var numBytes = count - 1;
        if (numBytes >= 1) output.Add((byte)((value >> 24) & 0xFF));
        if (numBytes >= 2) output.Add((byte)((value >> 16) & 0xFF));
        if (numBytes >= 3) output.Add((byte)((value >> 8) & 0xFF));
        if (numBytes >= 4) output.Add((byte)(value & 0xFF));
    }
}
