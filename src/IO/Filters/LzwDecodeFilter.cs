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
        if (parms is null) return DecodeLzw(data, earlyChange: 1);
        return Decode(data, (int)parms.GetInt("EarlyChange", 1), (int)parms.GetInt("Predictor", 1),
            (int)parms.GetInt("Columns", 1), (int)parms.GetInt("Colors", 1), (int)parms.GetInt("BitsPerComponent", 8));
    }

    /// <summary>The decode with its parameters given as values (the /DecodeParms keys of the same names).</summary>
    public static byte[] Decode(byte[] data, int earlyChange, int predictor, int columns, int colors, int bpc)
    {
        var output = DecodeLzw(data, earlyChange);
        // LZWDecode and FlateDecode share the /Predictor parameters of PDF 32000 §7.4.4.4, so the
        // TIFF or PNG predictor is removed from the decoded output exactly as FlateDecode does.
        return predictor > 1 ? FlateDecodeFilter.RemovePredictor(output, predictor, columns, colors, bpc) : output;
    }

    private const int FirstFreeCode = 258;
    private const int MaxTableSize = 4096;
    private const int FirstCodeSize = 9;
    private const int LastCodeSize = 12;

    // The table holds each string as its prefix code and last byte - a string is written by
    // walking the prefixes back - instead of a byte array per entry that copied the whole
    // string each time it grew.
    private static byte[] DecodeLzw(byte[] data, int earlyChange)
    {
        var output = new MemoryStream(data.Length * 3);
        var reader = new BitReader(data);
        var prefix = new int[MaxTableSize];
        var suffix = new byte[MaxTableSize];
        var length = new int[MaxTableSize];
        for (var i = 0; i < 256; i++) { suffix[i] = (byte)i; length[i] = 1; }
        var entry = new byte[MaxTableSize];

        var codeSize = FirstCodeSize;
        var nextCode = FirstFreeCode;
        var prevCode = -1;
        while (true)
        {
            var code = reader.ReadBits(codeSize);
            if (code < 0 || code == EodCode) break;
            if (code == ClearTableCode)
            {
                // The next code may legally be another Clear, EOD or a literal: with no
                // previous code the loop takes it as the first after the clear.
                codeSize = FirstCodeSize;
                nextCode = FirstFreeCode;
                prevCode = -1;
                continue;
            }

            int len;
            if (code < nextCode) len = WriteString(code, prefix, suffix, length, entry);
            else if (code == nextCode && prevCode >= 0)
            {
                // The code being defined right now: the previous string plus its own first byte.
                len = WriteString(prevCode, prefix, suffix, length, entry);
                entry[len++] = entry[0];
            }
            else break; // corrupt data

            output.Write(entry, 0, len);
            if (prevCode >= 0 && nextCode < MaxTableSize)
            {
                prefix[nextCode] = prevCode;
                suffix[nextCode] = entry[0];
                length[nextCode] = length[prevCode] + 1;
                nextCode++;
            }
            // EarlyChange 1 widens the code one entry early; 0 when the table reaches the limit.
            var threshold = earlyChange == 1 ? nextCode + 1 : nextCode;
            if (threshold >= (1 << codeSize) && codeSize < LastCodeSize) codeSize++;
            prevCode = code;
        }
        return output.ToArray();
    }

    /// <summary>Write the table's string for <paramref name="code"/> to the start of
    /// <paramref name="entry"/>, last byte first along the prefixes; returns its length.</summary>
    private static int WriteString(int code, int[] prefix, byte[] suffix, int[] length, byte[] entry)
    {
        var len = length[code];
        for (var i = len - 1; i >= 0; i--)
        {
            entry[i] = suffix[code];
            code = prefix[code];
        }
        return len;
    }

    private sealed class BitReader
    {
        private readonly byte[] _data;
        private int _pos;
        private uint _acc;
        private int _bits;

        public BitReader(byte[] data) => _data = data;

        /// <summary>The next <paramref name="count"/> bits, most significant first; -1 once the
        /// data holds fewer.</summary>
        public int ReadBits(int count)
        {
            while (_bits < count)
            {
                if (_pos >= _data.Length) return -1;
                _acc = (_acc << 8) | _data[_pos++];
                _bits += 8;
            }
            _bits -= count;
            return (int)((_acc >> _bits) & ((1u << count) - 1));
        }
    }
}
