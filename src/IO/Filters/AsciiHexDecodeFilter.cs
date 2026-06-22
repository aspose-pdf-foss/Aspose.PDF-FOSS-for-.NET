namespace Aspose.Pdf.IO.Filters;

/// <summary>
/// Decodes ASCII hexadecimal encoded data (PDF 32000 §7.4.2).
/// Each pair of hex digits encodes one byte; '>' marks end of data.
/// </summary>
internal static class AsciiHexDecodeFilter
{
    public static byte[] Decode(byte[] data)
    {
        var output = new List<byte>();
        var nibbleCount = 0;
        var current = 0;

        for (var i = 0; i < data.Length; i++)
        {
            var b = data[i];

            if (b == '>') break; // EOD marker

            // Skip whitespace
            if (b == ' ' || b == '\t' || b == '\r' || b == '\n' || b == '\f')
                continue;

            var nibble = HexVal(b);
            if (nibble < 0) continue;

            if (nibbleCount % 2 == 0)
            {
                current = nibble << 4;
            }
            else
            {
                current |= nibble;
                output.Add((byte)current);
            }
            nibbleCount++;
        }

        // Odd nibble — pad with 0
        if (nibbleCount % 2 == 1)
            output.Add((byte)current);

        return output.ToArray();
    }

    private static int HexVal(byte b) => b switch
    {
        >= (byte)'0' and <= (byte)'9' => b - '0',
        >= (byte)'a' and <= (byte)'f' => b - 'a' + 10,
        >= (byte)'A' and <= (byte)'F' => b - 'A' + 10,
        _ => -1,
    };
}
