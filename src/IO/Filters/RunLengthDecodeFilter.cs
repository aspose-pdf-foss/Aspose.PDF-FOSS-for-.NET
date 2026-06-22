namespace Aspose.Pdf.IO.Filters;

/// <summary>
/// Decodes run-length encoded data (PDF 32000 §7.4.5).
/// Length byte 0–127 = copy N+1 literal bytes; 129–255 = repeat next byte 257−N times; 128 = EOD.
/// </summary>
internal static class RunLengthDecodeFilter
{
    public static byte[] Decode(byte[] data)
    {
        var output = new List<byte>();
        var i = 0;

        while (i < data.Length)
        {
            var length = data[i];
            i++;

            if (length == 128) // EOD
                break;

            if (length < 128)
            {
                // Copy next (length + 1) bytes literally
                var count = length + 1;
                for (var j = 0; j < count && i < data.Length; j++)
                {
                    output.Add(data[i++]);
                }
            }
            else
            {
                // Repeat next byte (257 - length) times
                var count = 257 - length;
                if (i < data.Length)
                {
                    var b = data[i++];
                    for (var j = 0; j < count; j++)
                    {
                        output.Add(b);
                    }
                }
            }
        }

        return output.ToArray();
    }
}
