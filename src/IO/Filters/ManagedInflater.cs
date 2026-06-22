namespace Aspose.Pdf.IO.Filters;

/// <summary>
/// Pure-managed deflate decoder per RFC 1950 (zlib wrapper) and RFC 1951 (deflate).
/// Self-contained so behavior does not depend on the host's native zlib —
/// the same compressed bytes always produce the same output across .NET runtimes
/// and operating systems.
/// </summary>
internal static class ManagedInflater
{
    private static readonly int[] LengthBase = {
        3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31,
        35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258
    };

    private static readonly int[] LengthExtraBits = {
        0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2,
        3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0
    };

    private static readonly int[] DistanceBase = {
        1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193,
        257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145,
        8193, 12289, 16385, 24577
    };

    private static readonly int[] DistanceExtraBits = {
        0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6,
        7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13
    };

    // The code-length-alphabet symbols appear in this permuted order in the bit stream.
    private static readonly int[] CodeLengthOrder = {
        16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15
    };

    /// <summary>
    /// Decompresses a zlib-wrapped (RFC 1950) byte stream.
    /// Header (CMF/FLG) is validated; the trailing adler32 is not checked
    /// (PDF readers traditionally accept streams with mismatched checksums).
    /// </summary>
    public static byte[] InflateZlib(byte[] input)
    {
        if (input.Length < 2)
            throw new InvalidDataException("zlib: input shorter than header");

        int cmf = input[0];
        int flg = input[1];
        if ((cmf & 0x0F) != 8)
            throw new InvalidDataException($"zlib: CM={cmf & 0x0F}, expected 8 (deflate)");
        if ((cmf * 256 + flg) % 31 != 0)
            throw new InvalidDataException("zlib: header check failed");
        if ((flg & 0x20) != 0)
            throw new InvalidDataException("zlib: preset dictionary not supported");

        return InflateCore(input, headerSize: 2);
    }

    /// <summary>
    /// Decompresses a raw deflate (RFC 1951) byte stream — no zlib wrapper.
    /// </summary>
    public static byte[] InflateRaw(byte[] input)
        => InflateCore(input, headerSize: 0);

    private static byte[] InflateCore(byte[] input, int headerSize)
    {
        var reader = new BitReader(input, headerSize);
        var output = new byte[8192];
        int outPos = 0;

        // PDF zlib streams are sometimes truncated or corrupt past a certain
        // point (incremental updates, faulty encoders). Decode as far as the
        // bit stream is well-formed; surface InvalidDataException only if
        // we can't even start.
        try
        {
            while (true)
            {
                int bfinal = reader.ReadBits(1);
                int btype = reader.ReadBits(2);

                switch (btype)
                {
                    case 0:
                        InflateStored(reader, ref output, ref outPos);
                        break;
                    case 1:
                        InflateHuffman(reader, ref output, ref outPos, FixedLitLen, FixedDist);
                        break;
                    case 2:
                        var (litLen, dist) = DecodeDynamicTables(reader);
                        InflateHuffman(reader, ref output, ref outPos, litLen, dist);
                        break;
                    default:
                        throw new InvalidDataException("deflate: reserved BTYPE 3");
                }

                if (bfinal == 1) break;
            }
        }
        catch (InvalidDataException)
        {
            // If we never produced anything, let the caller know — they may
            // want to try a different filter. If we have partial output, keep
            // it: that's what every real PDF reader does.
            if (outPos == 0) throw;
        }

        var result = new byte[outPos];
        System.Array.Copy(output, 0, result, 0, outPos);
        return result;
    }

    private static void InflateStored(BitReader reader, ref byte[] output, ref int outPos)
    {
        reader.AlignToByte();
        int len  = reader.ReadByte() | (reader.ReadByte() << 8);
        int nlen = reader.ReadByte() | (reader.ReadByte() << 8);
        if ((len ^ 0xFFFF) != nlen)
            throw new InvalidDataException("deflate stored block: LEN/NLEN mismatch");

        EnsureCapacity(ref output, outPos + len);
        for (int i = 0; i < len; i++)
            output[outPos++] = (byte)reader.ReadByte();
    }

    private static void InflateHuffman(BitReader reader, ref byte[] output, ref int outPos,
        HuffmanTable litLen, HuffmanTable dist)
    {
        while (true)
        {
            int symbol = DecodeSymbol(reader, litLen);
            if (symbol < 256)
            {
                EnsureCapacity(ref output, outPos + 1);
                output[outPos++] = (byte)symbol;
                continue;
            }
            if (symbol == 256)
                break;

            int lenIdx = symbol - 257;
            if (lenIdx >= LengthBase.Length)
                throw new InvalidDataException($"deflate: invalid length symbol {symbol}");
            int length = LengthBase[lenIdx];
            int extra = LengthExtraBits[lenIdx];
            if (extra > 0) length += reader.ReadBits(extra);

            int distSym = DecodeSymbol(reader, dist);
            if (distSym >= DistanceBase.Length)
                throw new InvalidDataException($"deflate: invalid distance symbol {distSym}");
            int distance = DistanceBase[distSym];
            extra = DistanceExtraBits[distSym];
            if (extra > 0) distance += reader.ReadBits(extra);

            int src = outPos - distance;
            if (src < 0)
                throw new InvalidDataException("deflate: distance points before start of output");

            EnsureCapacity(ref output, outPos + length);
            // Byte-by-byte (not block-copy): distance==length is a run-of-byte
            // where each appended byte may itself be a byte we just appended.
            for (int i = 0; i < length; i++)
                output[outPos++] = output[src + i];
        }
    }

    private static void EnsureCapacity(ref byte[] buf, int needed)
    {
        if (needed <= buf.Length) return;
        int newSize = buf.Length;
        while (newSize < needed) newSize *= 2;
        var grown = new byte[newSize];
        System.Array.Copy(buf, grown, buf.Length);
        buf = grown;
    }

    // ── Dynamic Huffman table decoding (RFC 1951 §3.2.7) ────────────────────

    private static (HuffmanTable litLen, HuffmanTable dist) DecodeDynamicTables(BitReader reader)
    {
        int hlit  = reader.ReadBits(5) + 257; // # literal/length codes (257..286)
        int hdist = reader.ReadBits(5) + 1;   // # distance codes       (1..32)
        int hclen = reader.ReadBits(4) + 4;   // # code length codes    (4..19)

        var clLens = new int[19];
        for (int i = 0; i < hclen; i++)
            clLens[CodeLengthOrder[i]] = reader.ReadBits(3);
        var clTable = BuildHuffmanTable(clLens);

        var lens = new int[hlit + hdist];
        int idx = 0;
        while (idx < lens.Length)
        {
            int sym = DecodeSymbol(reader, clTable);
            if (sym < 16)
            {
                lens[idx++] = sym;
            }
            else if (sym == 16)
            {
                if (idx == 0)
                    throw new InvalidDataException("deflate: code-length repeat without previous value");
                int repeat = reader.ReadBits(2) + 3;
                int prev = lens[idx - 1];
                for (int i = 0; i < repeat; i++) lens[idx++] = prev;
            }
            else if (sym == 17)
            {
                int repeat = reader.ReadBits(3) + 3;
                for (int i = 0; i < repeat; i++) lens[idx++] = 0;
            }
            else if (sym == 18)
            {
                int repeat = reader.ReadBits(7) + 11;
                for (int i = 0; i < repeat; i++) lens[idx++] = 0;
            }
            else
            {
                throw new InvalidDataException($"deflate: invalid code-length symbol {sym}");
            }
        }

        var litLenLens = new int[hlit];
        var distLens = new int[hdist];
        System.Array.Copy(lens, 0, litLenLens, 0, hlit);
        System.Array.Copy(lens, hlit, distLens, 0, hdist);

        return (BuildHuffmanTable(litLenLens), BuildHuffmanTable(distLens));
    }

    // ── Canonical Huffman decoder ────────────────────────────────────────────

    private sealed class HuffmanTable
    {
        // For each code length L in 1..15: how many codes of that length exist.
        // Symbols are packed in canonical order: shorter codes first, then by
        // symbol value within each length.
        public readonly int[] Counts = new int[16];
        public readonly int[] Symbols;
        public HuffmanTable(int symbolCount) { Symbols = new int[symbolCount]; }
    }

    private static HuffmanTable BuildHuffmanTable(int[] codeLengths)
    {
        var t = new HuffmanTable(codeLengths.Length);

        for (int i = 0; i < codeLengths.Length; i++)
        {
            int len = codeLengths[i];
            if (len < 0 || len > 15) throw new InvalidDataException($"deflate: code length {len} out of range");
            if (len > 0) t.Counts[len]++;
        }

        var offsets = new int[16];
        int acc = 0;
        for (int len = 1; len < 16; len++)
        {
            offsets[len] = acc;
            acc += t.Counts[len];
        }

        var work = (int[])offsets.Clone();
        for (int sym = 0; sym < codeLengths.Length; sym++)
        {
            int len = codeLengths[sym];
            if (len > 0)
                t.Symbols[work[len]++] = sym;
        }

        return t;
    }

    private static int DecodeSymbol(BitReader reader, HuffmanTable t)
    {
        // Canonical decode: accumulate bits MSB-first; for each length L,
        // check if the running code falls within the range allocated to L.
        int code = 0;
        int first = 0;
        int index = 0;
        for (int len = 1; len < 16; len++)
        {
            code = (code << 1) | reader.ReadBits(1);
            int count = t.Counts[len];
            if (code - count < first)
                return t.Symbols[index + (code - first)];
            index += count;
            first = (first + count) << 1;
        }
        throw new InvalidDataException("deflate: ran off end of Huffman table");
    }

    // ── Fixed Huffman tables (RFC 1951 §3.2.6) ───────────────────────────────

    private static readonly HuffmanTable FixedLitLen = BuildFixedLitLen();
    private static readonly HuffmanTable FixedDist = BuildFixedDist();

    private static HuffmanTable BuildFixedLitLen()
    {
        // Literal/length code lengths:
        //   0..143    → 8 bits
        //   144..255  → 9 bits
        //   256..279  → 7 bits
        //   280..287  → 8 bits
        var lens = new int[288];
        for (int i = 0;   i < 144; i++) lens[i] = 8;
        for (int i = 144; i < 256; i++) lens[i] = 9;
        for (int i = 256; i < 280; i++) lens[i] = 7;
        for (int i = 280; i < 288; i++) lens[i] = 8;
        return BuildHuffmanTable(lens);
    }

    private static HuffmanTable BuildFixedDist()
    {
        // All 30 distance symbols use 5-bit codes; the 32-entry alphabet
        // reserves two unused symbols (also 5 bits) so canonical Huffman works.
        var lens = new int[32];
        for (int i = 0; i < 32; i++) lens[i] = 5;
        return BuildHuffmanTable(lens);
    }

    // ── Bit reader (LSB-first within bytes per RFC 1951 §3.1.1) ─────────────

    private sealed class BitReader
    {
        private readonly byte[] _data;
        private int _bytePos;
        private uint _bitBuffer;
        private int _bitCount;

        public BitReader(byte[] data, int offset)
        {
            _data = data;
            _bytePos = offset;
        }

        public int ReadBits(int n)
        {
            while (_bitCount < n)
            {
                if (_bytePos >= _data.Length)
                    throw new InvalidDataException("deflate: unexpected end of input");
                _bitBuffer |= (uint)_data[_bytePos++] << _bitCount;
                _bitCount += 8;
            }
            int result = (int)(_bitBuffer & ((1u << n) - 1u));
            _bitBuffer >>= n;
            _bitCount -= n;
            return result;
        }

        public int ReadByte()
        {
            // Used after AlignToByte() for BTYPE=00 stored blocks.
            if (_bytePos >= _data.Length)
                throw new InvalidDataException("deflate: unexpected end of input (stored block)");
            return _data[_bytePos++];
        }

        public void AlignToByte()
        {
            _bitBuffer = 0;
            _bitCount = 0;
        }
    }
}
