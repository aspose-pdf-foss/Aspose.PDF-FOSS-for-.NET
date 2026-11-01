namespace Aspose.Pdf.Devices;

/// <summary>
/// LZW encoder producing TIFF 6.0-compatible compressed strips.
/// Uses MSB-first bit packing, 9-bit start, 12-bit max, early-change code-size
/// bumping. Mirrors the conventions of <see cref="IO.Filters.LzwDecodeFilter"/>
/// so our own decoder (and any spec-compliant reader, e.g. ImageSharp) can
/// round-trip the output.
/// </summary>
internal static class LzwTiffEncoder
{
    private const int ClearCode = 256;
    private const int EoiCode = 257;
    private const int FirstFreeCode = 258;
    private const int MaxCodeSize = 12;
    private const int MaxCode = 1 << MaxCodeSize; // 4096 — first unusable code

    public static byte[] Encode(byte[] input)
    {
        var writer = new MsbBitWriter();
        var trie = new StringTable();

        var codeSize = 9;
        var nextCode = FirstFreeCode;

        writer.WriteBits(ClearCode, codeSize);

        if (input.Length == 0)
        {
            writer.WriteBits(EoiCode, codeSize);
            return writer.ToArray();
        }

        int currentCode = input[0];

        for (var i = 1; i < input.Length; i++)
        {
            var c = input[i];
            // Key: (prefixCode << 8) | nextByte — fits in int since prefixCode <= 4095.
            var key = (currentCode << 8) | c;

            if (trie.TryGet(key, out var found))
            {
                currentCode = found;
                continue;
            }

            writer.WriteBits(currentCode, codeSize);

            // Add to dict when space remains. The threshold must match the
            // decoder (LzwDecodeFilter: `nextCode < 4096`) so both sides
            // grow their tables identically.
            if (nextCode < MaxCode)
            {
                trie.Add(key, nextCode);
                nextCode++;
                // Bump condition. The decoder uses (nextCode+1 >= 2^codeSize)
                // with earlyChange=1 — that +1 compensates for the Clear
                // handler skipping the first add, so the decoder's nextCode
                // trails the encoder's by exactly one. Here, on the encoder
                // side, we drop that +1 so both sides flip codeSize at the
                // same byte boundary in the stream.
                if (nextCode >= (1 << codeSize) && codeSize < MaxCodeSize)
                {
                    codeSize++;
                }
            }

            // Table just filled. Emit a ClearCode so both sides resynchronize
            // on an empty dict. AFTER the add, not before — otherwise the
            // decoder adds the entry at 4095 and the encoder never did.
            if (nextCode == MaxCode)
            {
                writer.WriteBits(ClearCode, codeSize);
                trie.Clear();
                codeSize = 9;
                nextCode = FirstFreeCode;
            }

            currentCode = c;
        }

        writer.WriteBits(currentCode, codeSize);
        writer.WriteBits(EoiCode, codeSize);
        return writer.ToArray();
    }

    /// <summary>The strings seen so far, by (prefix code, next byte): an open-addressed table
    /// with a generation stamp per slot, so clearing it costs a new stamp and nothing else.
    /// The dictionary this replaces was most of what encoding a strip cost.</summary>
    private sealed class StringTable
    {
        private const int SizeBits = 14;                 // four slots per code: short probes
        private const int Size = 1 << SizeBits;
        private const uint GoldenRatio = 2654435769;     // Fibonacci hashing
        private readonly int[] _keys = new int[Size];
        private readonly int[] _codes = new int[Size];
        private readonly int[] _generations = new int[Size];
        private int _generation = 1;

        public void Clear() => _generation++;

        public bool TryGet(int key, out int code)
        {
            var slot = Find(key);
            code = _codes[slot];
            return _generations[slot] == _generation;
        }

        public void Add(int key, int code)
        {
            var slot = Find(key);
            _keys[slot] = key;
            _codes[slot] = code;
            _generations[slot] = _generation;
        }

        // The slot holding the key, or the free slot it goes into.
        private int Find(int key)
        {
            var slot = (int)(((uint)key * GoldenRatio) >> (32 - SizeBits));
            while (_generations[slot] == _generation && _keys[slot] != key)
                slot = (slot + 1) & (Size - 1);
            return slot;
        }
    }

    private sealed class MsbBitWriter
    {
        private readonly List<byte> _bytes = new();
        private uint _accum;
        private int _nbits;

        public void WriteBits(int value, int count)
        {
            _accum = (_accum << count) | (uint)(value & ((1 << count) - 1));
            _nbits += count;
            while (_nbits >= 8)
            {
                _nbits -= 8;
                _bytes.Add((byte)((_accum >> _nbits) & 0xFF));
            }
        }

        public byte[] ToArray()
        {
            if (_nbits > 0)
            {
                _bytes.Add((byte)((_accum << (8 - _nbits)) & 0xFF));
                _nbits = 0;
                _accum = 0;
            }
            return _bytes.ToArray();
        }
    }
}
