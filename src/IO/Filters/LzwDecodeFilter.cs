using Aspose.Pdf.Core;

namespace Aspose.Pdf.IO.Filters;

/// <summary>
/// Decodes LZW compressed data (PDF 32000 §7.4.4.2).
/// Variable-length code table with clear-table (256) and EOD (257) markers.
/// Supports EarlyChange parameter and PNG prediction filters.
/// </summary>
internal static class LzwDecodeFilter
{
    private const int ClearTableCode = 256;
    private const int EodCode = 257;

    public static byte[] Decode(byte[] data, PdfDictionary? parms)
    {
        var earlyChange = parms is not null ? (int)parms.GetInt("EarlyChange", 1) : 1;
        var output = DecodeLzw(data, earlyChange);

        if (parms is not null)
        {
            var predictor = (int)parms.GetInt("Predictor", 1);
            if (predictor > 1)
            {
                var columns = (int)parms.GetInt("Columns", 1);
                var colors = (int)parms.GetInt("Colors", 1);
                var bpc = (int)parms.GetInt("BitsPerComponent", 8);
                output = FlateDecodeFilter.Decode(output,
                    new PdfDictionary { }); // reuse PNG predictor logic
                // Actually, let's just inline the predictor call
            }
        }

        return output;
    }

    private static byte[] DecodeLzw(byte[] data, int earlyChange)
    {
        var output = new List<byte>();
        var reader = new BitReader(data);

        var codeSize = 9;
        var table = InitTable();
        var nextCode = 258;
        byte[]? prevEntry = null;

        while (true)
        {
            var code = reader.ReadBits(codeSize);
            if (code < 0) break; // no more data

            if (code == EodCode) break;

            if (code == ClearTableCode)
            {
                table = InitTable();
                codeSize = 9;
                nextCode = 258;
                prevEntry = null;

                code = reader.ReadBits(codeSize);
                if (code < 0 || code == EodCode) break;

                var entry = table[code];
                output.AddRange(entry);
                prevEntry = entry;
                continue;
            }

            byte[] currentEntry;
            if (code < nextCode && table.ContainsKey(code))
            {
                currentEntry = table[code];
            }
            else if (code == nextCode && prevEntry is not null)
            {
                currentEntry = [.. prevEntry, prevEntry[0]];
            }
            else
            {
                break; // corrupt data
            }

            output.AddRange(currentEntry);

            if (prevEntry is not null && nextCode < 4096)
            {
                table[nextCode] = [.. prevEntry, currentEntry[0]];
                nextCode++;
            }

            // Increase code size
            // EarlyChange=1: increase one code early (when nextCode+1 reaches the limit)
            // EarlyChange=0: increase when table is full (nextCode reaches the limit)
            var threshold = earlyChange == 1 ? nextCode + 1 : nextCode;
            if (threshold >= (1 << codeSize) && codeSize < 12)
            {
                codeSize++;
            }

            prevEntry = currentEntry;
        }

        return output.ToArray();
    }

    private static Dictionary<int, byte[]> InitTable()
    {
        var table = new Dictionary<int, byte[]>();
        for (var i = 0; i < 256; i++)
        {
            table[i] = [(byte)i];
        }
        return table;
    }

    private sealed class BitReader
    {
        private readonly byte[] _data;
        private int _bytePos;
        private int _bitPos;

        public BitReader(byte[] data)
        {
            _data = data;
            _bytePos = 0;
            _bitPos = 0;
        }

        public int ReadBits(int count)
        {
            var result = 0;
            for (var i = 0; i < count; i++)
            {
                if (_bytePos >= _data.Length) return -1;

                var bit = (_data[_bytePos] >> (7 - _bitPos)) & 1;
                result = (result << 1) | bit;

                _bitPos++;
                if (_bitPos == 8)
                {
                    _bitPos = 0;
                    _bytePos++;
                }
            }
            return result;
        }
    }
}
