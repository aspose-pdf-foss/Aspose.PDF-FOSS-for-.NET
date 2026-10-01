namespace Aspose.Pdf.IO.Filters;

/// <summary>
/// Pure-managed deflate decoder per RFC 1950 (zlib wrapper) and RFC 1951 (deflate).
/// Self-contained so behavior does not depend on the host's native zlib —
/// the same compressed bytes always produce the same output across .NET runtimes
/// and operating systems.
/// </summary>
internal static partial class ManagedInflater
{
    /// <summary>
    /// Decompresses a zlib-wrapped (RFC 1950) byte stream.
    /// Header (CMF/FLG) is validated; the trailing adler32 is not checked
    /// (PDF readers traditionally accept streams with mismatched checksums).
    /// </summary>
    public static byte[] InflateZlib(byte[] input)
    {
        // An empty stream is empty data, not a broken header: nothing to read is nothing read.
        if (input.Length == 0) return Array.Empty<byte>();
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

    /// <summary>
    /// Inflates as far as the stream is well-formed and keeps EVERY byte produced, for a caller
    /// that decides for itself what a damaged stream is worth; <paramref name="complete"/> says
    /// whether the final block was reached without a fault. With <paramref name="zlibWrapper"/>
    /// the two header bytes are skipped unchecked (the caller has looked at them).
    /// </summary>
    internal static byte[] InflateAll(byte[] input, bool zlibWrapper, out bool complete)
    {
        var headerSize = zlibWrapper ? 2 : 0;
        var reader = new BitReader(input, headerSize);
        var output = new OutputBuffer(input.Length);
        complete = false;
        try
        {
            InflateBlocks(reader, output);
            complete = true;
        }
        catch (InvalidDataException)
        {
        }
        catch (IndexOutOfRangeException)
        {
        }
        return output.ToArray();
    }

    /// <summary>
    /// The first <paramref name="maxBytes"/> bytes a stream inflates to (fewer when it holds
    /// fewer), decoding no further than that: a caller that only needs a header does not
    /// materialise the whole payload. Like <see cref="InflateAll"/> it keeps what a damaged
    /// stream yields before its fault. <paramref name="offset"/> is where the blocks start.
    /// </summary>
    internal static byte[] InflatePrefix(byte[] input, int offset, int maxBytes)
    {
        var reader = new BitReader(input, offset);
        var output = new OutputBuffer(Math.Min(maxBytes, input.Length)) { Limit = maxBytes };
        try
        {
            InflateBlocks(reader, output);
        }
        catch (InvalidDataException)
        {
        }
        catch (IndexOutOfRangeException)
        {
        }
        if (output.Length > maxBytes) output.Length = maxBytes;
        return output.ToArray();
    }

    private static byte[] InflateCore(byte[] input, int headerSize)
    {
        var reader = new BitReader(input, headerSize);
        var output = new OutputBuffer(input.Length);

        // PDF zlib streams are sometimes truncated or corrupt past a certain
        // point (incremental updates, faulty encoders). Decode as far as the
        // bit stream is well-formed; surface InvalidDataException only if
        // we can't even start.
        try
        {
            InflateBlocks(reader, output);
        }
        catch (InvalidDataException)
        {
            // If we never produced anything, let the caller know — they may
            // want to try a different filter. If we have partial output, keep
            // it: that's what every real PDF reader does. Readers consume
            // inflate through fixed-size chunked reads and lose the partial
            // chunk in flight when the error surfaces, so keep only whole
            // 4096-byte chunks — the bytes past that boundary are exactly the
            // garbled span other readers never see.
            if (output.Length == 0) throw;
            output.Length &= ~0xFFF;
        }

        return output.ToArray();
    }

    /// <summary>Every block up to and including the final one, or until the output reaches its limit.</summary>
    private static void InflateBlocks(BitReader reader, OutputBuffer output)
    {
        while (output.Length < output.Limit)
        {
            int bfinal = reader.ReadBits(1);
            int btype = reader.ReadBits(2);

            switch (btype)
            {
                case 0:
                    InflateStored(reader, output);
                    break;
                case 1:
                    InflateHuffman(reader, output, FixedLitLen, FixedDist);
                    break;
                case 2:
                    var (litLen, dist) = DecodeDynamicTables(reader);
                    InflateHuffman(reader, output, litLen, dist);
                    break;
                default:
                    throw Faulted(InflateFault.InvalidBlockType, "deflate: reserved BTYPE 3");
            }

            if (bfinal == 1) break;
        }
    }

    /// <summary>The inflated bytes so far: a buffer that doubles as it fills, and the length written into it.</summary>
    private sealed class OutputBuffer
    {
        // Deflated content streams typically inflate to a few times their size; starting there
        // saves most of the doublings on the way up.
        private const int InitialExpansion = 4;
        private const int MinimumCapacity = 8192;
        private const int MaximumInitialCapacity = 4 << 20;

        public byte[] Data;
        public int Length;
        /// <summary>Decoding stops once this many bytes are out (the last copy may overshoot it).</summary>
        public int Limit = int.MaxValue;

        public OutputBuffer(int inputLength)
        {
            var hint = (long)inputLength * InitialExpansion;
            Data = new byte[(int)Math.Max(MinimumCapacity, Math.Min(hint, MaximumInitialCapacity))];
        }

        public void EnsureCapacity(int needed)
        {
            if (needed <= Data.Length) return;
            int newSize = Data.Length;
            while (newSize < needed) newSize *= 2;
            var grown = new byte[newSize];
            System.Array.Copy(Data, grown, Length);
            Data = grown;
        }

        public byte[] ToArray()
        {
            var result = new byte[Length];
            System.Array.Copy(Data, 0, result, 0, Length);
            return result;
        }
    }

    private static void InflateStored(BitReader reader, OutputBuffer output)
    {
        reader.AlignToByte();
        int len  = reader.ReadByte() | (reader.ReadByte() << 8);
        int nlen = reader.ReadByte() | (reader.ReadByte() << 8);
        if ((len ^ 0xFFFF) != nlen)
            throw Faulted(InflateFault.InvalidStoredBlockLengths, "deflate stored block: LEN/NLEN mismatch");

        output.EnsureCapacity(output.Length + len);
        // A stream cut short keeps the bytes it did hold, and counts none it never read.
        var copied = reader.ReadBytes(output.Data, output.Length, len);
        output.Length += copied;
        if (copied < len)
            throw Faulted(InflateFault.Truncated, "deflate: unexpected end of input (stored block)");
    }

    private static void InflateHuffman(BitReader reader, OutputBuffer output,
        HuffmanTable litLen, HuffmanTable dist)
    {
        var data = output.Data;
        var length = output.Length;
        var limit = output.Limit;
        try
        {
            while (length < limit)
            {
                int symbol = DecodeSymbol(reader, litLen);
                if (symbol < 256)
                {
                    if (length == data.Length) data = Grow(output, length, length + 1);
                    data[length++] = (byte)symbol;
                    continue;
                }
                if (symbol == 256)
                    break;

                int lenIdx = symbol - 257;
                if (lenIdx >= DeflateTables.LengthBase.Length)
                    throw Faulted(InflateFault.InvalidLiteralLengthCode, $"deflate: invalid length symbol {symbol}");
                int count = DeflateTables.LengthBase[lenIdx];
                int extra = DeflateTables.LengthExtraBits[lenIdx];
                if (extra > 0) count += reader.ReadBits(extra);

                int distSym = DecodeSymbol(reader, dist);
                if (distSym >= DeflateTables.DistanceBase.Length)
                    throw Faulted(InflateFault.InvalidDistanceCode, $"deflate: invalid distance symbol {distSym}");
                int distance = DeflateTables.DistanceBase[distSym];
                extra = DeflateTables.DistanceExtraBits[distSym];
                if (extra > 0) distance += reader.ReadBits(extra);

                int src = length - distance;
                if (src < 0)
                    throw Faulted(InflateFault.InvalidDistanceTooFarBack, "deflate: distance points before start of output");

                if (length + count > data.Length) data = Grow(output, length, length + count);
                if (count <= ShortCopy)
                {
                    // Byte by byte: when the distance is shorter than the length, each
                    // appended byte may itself be one just appended (a run).
                    for (int i = 0; i < count; i++)
                        data[length++] = data[src + i];
                }
                else if (distance == 1)
                {
                    data.AsSpan(length, count).Fill(data[src]);
                    length += count;
                }
                else
                {
                    // The copy repeats the distance-long pattern that starts at src. Everything
                    // from src up to what is already written holds whole periods of it, so each
                    // step may copy all of that at once and the span doubles every step.
                    for (int done = 0; done < count;)
                    {
                        int step = Math.Min(count - done, distance + done);
                        System.Buffer.BlockCopy(data, src, data, length + done, step);
                        done += step;
                    }
                    length += count;
                }
            }
        }
        finally
        {
            output.Length = length;
        }
    }

    // Below this a back-reference is copied byte by byte: cheaper than a block copy's setup.
    private const int ShortCopy = 16;

    private static byte[] Grow(OutputBuffer output, int length, int needed)
    {
        output.Length = length;
        output.EnsureCapacity(needed);
        return output.Data;
    }

    // ── Dynamic Huffman table decoding (RFC 1951 §3.2.7) ────────────────────

    private const int LiteralLookupBits = 10;
    private const int DistanceLookupBits = 8;
    private const int CodeLengthLookupBits = 7;

    private static (HuffmanTable litLen, HuffmanTable dist) DecodeDynamicTables(BitReader reader)
    {
        int hlit  = reader.ReadBits(5) + 257; // # literal/length codes (257..286)
        int hdist = reader.ReadBits(5) + 1;   // # distance codes       (1..32)
        int hclen = reader.ReadBits(4) + 4;   // # code length codes    (4..19)

        var clLens = new int[19];
        for (int i = 0; i < hclen; i++)
            clLens[DeflateTables.CodeLengthOrder[i]] = reader.ReadBits(3);
        var clTable = BuildHuffmanTable(clLens, CodeLengthLookupBits);

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
                    throw Faulted(InflateFault.InvalidBitLengthRepeat, "deflate: code-length repeat without previous value");
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
                throw Faulted(InflateFault.InvalidBitLengthRepeat, $"deflate: invalid code-length symbol {sym}");
            }
        }

        var litLenLens = new int[hlit];
        var distLens = new int[hdist];
        System.Array.Copy(lens, 0, litLenLens, 0, hlit);
        System.Array.Copy(lens, hlit, distLens, 0, hdist);

        return (BuildHuffmanTable(litLenLens, LiteralLookupBits), BuildHuffmanTable(distLens, DistanceLookupBits));
    }

    // ── Canonical Huffman decoder ────────────────────────────────────────────

    private sealed class HuffmanTable
    {
        // For each code length L in 1..15: how many codes of that length exist.
        // Symbols are packed in canonical order: shorter codes first, then by
        // symbol value within each length.
        public readonly int[] Counts = new int[16];
        public readonly int[] Symbols;
        // The next LookupBits input bits (first-arriving bit lowest) decoded at once:
        // symbol << LookupLengthBits | code length, or 0 when the code is longer than
        // LookupBits or the canonical walk does not end in a symbol within them.
        public int[] Lookup = Array.Empty<int>();
        public int LookupBits;
        public HuffmanTable(int symbolCount) { Symbols = new int[symbolCount]; }
    }

    private const int LookupLengthBits = 4;
    private const int LookupLengthMask = (1 << LookupLengthBits) - 1;

    private static HuffmanTable BuildHuffmanTable(int[] codeLengths, int lookupBits)
    {
        var t = new HuffmanTable(codeLengths.Length);

        for (int i = 0; i < codeLengths.Length; i++)
        {
            int len = codeLengths[i];
            if (len < 0 || len > 15) throw Faulted(InflateFault.InvalidLiteralLengthCode, $"deflate: code length {len} out of range");
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

        BuildLookup(t, lookupBits);
        return t;
    }

    /// <summary>
    /// Fills the lookup by running the canonical walk of <see cref="DecodeSymbolBitwise"/>
    /// over every <paramref name="lookupBits"/>-bit prefix. The table is therefore the walk
    /// itself, precomputed: an over-subscribed or incomplete set of lengths decodes through it
    /// exactly as it does a bit at a time, and whatever the walk cannot settle within the
    /// prefix (a longer code, a symbol index outside the table) is left to the walk.
    /// </summary>
    private static void BuildLookup(HuffmanTable t, int lookupBits)
    {
        var lookup = new int[1 << lookupBits];
        for (int prefix = 0; prefix < lookup.Length; prefix++)
        {
            int code = 0, first = 0, index = 0;
            for (int len = 1; len <= lookupBits; len++)
            {
                code = (code << 1) | ((prefix >> (len - 1)) & 1);
                int count = t.Counts[len];
                if (code - count < first)
                {
                    int at = index + (code - first);
                    if (at >= 0 && at < t.Symbols.Length)
                        lookup[prefix] = (t.Symbols[at] << LookupLengthBits) | len;
                    break;
                }
                index += count;
                first = (first + count) << 1;
            }
        }
        t.Lookup = lookup;
        t.LookupBits = lookupBits;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static int DecodeSymbol(BitReader reader, HuffmanTable t)
    {
        if (reader.TryPeek(t.LookupBits, out var bits))
        {
            var entry = t.Lookup[bits];
            if (entry != 0)
            {
                reader.Skip(entry & LookupLengthMask);
                return entry >> LookupLengthBits;
            }
        }
        return DecodeSymbolBitwise(reader, t);
    }

    private static int DecodeSymbolBitwise(BitReader reader, HuffmanTable t)
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
        throw Faulted(InflateFault.InvalidLiteralLengthCode, "deflate: ran off end of Huffman table");
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
        return BuildHuffmanTable(lens, LiteralLookupBits);
    }

    private static HuffmanTable BuildFixedDist()
    {
        // All 30 distance symbols use 5-bit codes; the 32-entry alphabet
        // reserves two unused symbols (also 5 bits) so canonical Huffman works.
        var lens = new int[32];
        for (int i = 0; i < 32; i++) lens[i] = 5;
        return BuildHuffmanTable(lens, DistanceLookupBits);
    }

    // ── Bit reader (LSB-first within bytes per RFC 1951 §3.1.1) ─────────────

    private sealed class BitReader
    {
        // Refills stop with at least this many bits held (a byte more would not fit in 64).
        private const int RefillTarget = 56;

        private readonly byte[] _data;
        private int _bytePos;
        private ulong _bitBuffer;
        private int _bitCount;
        private bool _exhausted;

        public BitReader(byte[] data, int offset)
        {
            _data = data;
            _bytePos = offset;
        }

        /// <summary>How far into the data the reader has got: the bytes whose bits it has used,
        /// a partly used one included. Bytes fetched ahead and not yet used are not counted,
        /// except once the input has run out, when everything was pulled in trying.</summary>
        public int Position => _exhausted ? _bytePos : _bytePos - (_bitCount >> 3);

        private void Refill()
        {
            while (_bitCount <= RefillTarget && _bytePos < _data.Length)
            {
                _bitBuffer |= (ulong)_data[_bytePos++] << _bitCount;
                _bitCount += 8;
            }
        }

        /// <summary>The next <paramref name="n"/> bits without using them; false when the input holds fewer.</summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public bool TryPeek(int n, out int bits)
        {
            if (_bitCount < n) Refill();
            bits = (int)(_bitBuffer & ((1ul << n) - 1ul));
            return _bitCount >= n;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void Skip(int n)
        {
            _bitBuffer >>= n;
            _bitCount -= n;
        }

        public int ReadBits(int n)
        {
            if (_bitCount < n)
            {
                Refill();
                if (_bitCount < n)
                {
                    _exhausted = true;
                    throw Faulted(InflateFault.Truncated, "deflate: unexpected end of input");
                }
            }
            int result = (int)(_bitBuffer & ((1ul << n) - 1ul));
            _bitBuffer >>= n;
            _bitCount -= n;
            return result;
        }

        public int ReadByte()
        {
            // Used after AlignToByte() for BTYPE=00 stored blocks, when nothing is held ahead.
            if (_bytePos >= _data.Length)
            {
                _exhausted = true;
                throw Faulted(InflateFault.Truncated, "deflate: unexpected end of input (stored block)");
            }
            return _data[_bytePos++];
        }

        /// <summary>Up to <paramref name="count"/> whole bytes into <paramref name="target"/> (after
        /// <see cref="AlignToByte"/>); returns how many were copied, fewer when the input ended first.</summary>
        public int ReadBytes(byte[] target, int offset, int count)
        {
            var available = Math.Max(0, Math.Min(count, _data.Length - _bytePos));
            System.Array.Copy(_data, _bytePos, target, offset, available);
            _bytePos += available;
            if (available < count) _exhausted = true;
            return available;
        }

        /// <summary>Drops the rest of the partly used byte; bytes fetched ahead go back to the input.</summary>
        public void AlignToByte()
        {
            _bytePos -= _bitCount >> 3;
            _bitBuffer = 0;
            _bitCount = 0;
        }
    }
}
