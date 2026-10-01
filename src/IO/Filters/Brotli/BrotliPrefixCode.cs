namespace Aspose.Pdf.IO.Filters.Brotli;

/// <summary>
/// A Brotli prefix code (RFC 7932 section 3): read in its simple or complex form, then used to
/// decode symbols. Codes are canonical -- assigned by length, then by symbol -- and are read from
/// the stream most significant bit first.
/// </summary>
internal sealed class BrotliPrefixCode
{
    private const int MaxLength = 15;
    private const int CodeLengthCodes = 18;
    private const int RepeatPreviousCode = 16;
    private const int DefaultCodeLength = 8;
    private static readonly int[] CodeLengthCodeOrder = { 1, 2, 3, 4, 0, 5, 17, 6, 16, 7, 8, 9, 10, 11, 12, 13, 14, 15 };

    // The static code the code length code lengths are read with, indexed by the next four bits:
    // (bits used << 16) | length.
    private static readonly int[] FixedTable =
    {
        0x020000, 0x020004, 0x020003, 0x030002, 0x020000, 0x020004, 0x020003, 0x040001,
        0x020000, 0x020004, 0x020003, 0x030002, 0x020000, 0x020004, 0x020003, 0x040005,
    };

    private readonly int[] _counts = new int[MaxLength + 1];
    private readonly int[] _symbols;
    private readonly int _single = -1;

    private BrotliPrefixCode(int[] lengths)
    {
        var used = 0;
        var last = -1;
        foreach (var length in lengths)
        {
            _counts[length]++;
        }
        for (var s = 0; s < lengths.Length; s++)
        {
            if (lengths[s] == 0) continue;
            used++;
            last = s;
        }
        _counts[0] = 0;
        if (used == 1) _single = last;
        var offsets = new int[MaxLength + 2];
        for (var l = 1; l <= MaxLength; l++) offsets[l + 1] = offsets[l] + _counts[l];
        _symbols = new int[used];
        for (var s = 0; s < lengths.Length; s++)
        {
            if (lengths[s] != 0) _symbols[offsets[lengths[s]]++] = s;
        }
    }

    /// <summary>The next symbol; a code of one symbol takes no bits.</summary>
    internal int Read(BrotliBitReader bits)
    {
        if (_single >= 0) return _single;
        int code = 0, first = 0, index = 0;
        for (var length = 1; length <= MaxLength; length++)
        {
            code |= bits.ReadBit();
            var count = _counts[length];
            if (code - first < count) return _symbols[index + code - first];
            index += count;
            first = (first + count) << 1;
            code <<= 1;
        }
        return _symbols.Length > 0 ? _symbols[_symbols.Length - 1] : 0;
    }

    /// <summary>Reads a prefix code over an alphabet of the given size (symbols above
    /// <paramref name="limit"/> are refused).</summary>
    internal static BrotliPrefixCode ReadFrom(BrotliBitReader bits, int alphabetSize, int limit)
    {
        var kind = bits.ReadBits(2);
        return kind == 1 ? ReadSimple(bits, alphabetSize, limit) : ReadComplex(bits, kind, limit);
    }

    // One to four symbols, listed, with lengths fixed by how many there are.
    private static BrotliPrefixCode ReadSimple(BrotliBitReader bits, int alphabetSize, int limit)
    {
        var lengths = new int[limit];
        var symbols = new int[4];
        var maxBits = 1 + Log2Floor(alphabetSize - 1);
        var count = bits.ReadBits(2) + 1;
        for (var i = 0; i < count; i++)
        {
            var symbol = bits.ReadBits(maxBits);
            if (symbol >= limit) throw new BrotliDecodeException(BrotliErrors.SymbolOutOfRange);
            symbols[i] = symbol;
        }
        for (var i = 0; i < count - 1; i++)
        {
            for (var j = i + 1; j < count; j++)
            {
                if (symbols[i] == symbols[j]) throw new BrotliDecodeException(BrotliErrors.DuplicateSimpleHuffmanSymbol);
            }
        }
        var histogram = count == 4 ? count + bits.ReadBits(1) : count;
        switch (histogram)
        {
            case 1:
                lengths[symbols[0]] = 1;
                break;
            case 2:
                lengths[symbols[0]] = 1;
                lengths[symbols[1]] = 1;
                break;
            case 3:
                lengths[symbols[0]] = 1;
                lengths[symbols[1]] = 2;
                lengths[symbols[2]] = 2;
                break;
            case 4:
                for (var i = 0; i < 4; i++) lengths[symbols[i]] = 2;
                break;
            default:
                lengths[symbols[0]] = 1;
                lengths[symbols[1]] = 2;
                lengths[symbols[2]] = 3;
                lengths[symbols[3]] = 3;
                break;
        }
        return new BrotliPrefixCode(lengths);
    }

    // Code lengths coded by a code of their own, with runs of the previous length and of zeros.
    private static BrotliPrefixCode ReadComplex(BrotliBitReader bits, int skip, int limit)
    {
        var lengthCodeLengths = new int[CodeLengthCodes];
        var space = 32;
        var codes = 0;
        for (var i = skip; i < CodeLengthCodes; i++)
        {
            var entry = FixedTable[bits.PeekBits(4)];
            bits.Skip(entry >> 16);
            var v = entry & 0xFFFF;
            lengthCodeLengths[CodeLengthCodeOrder[i]] = v;
            if (v == 0) continue;
            space -= 32 >> v;
            codes++;
            if (space <= 0) break;
        }
        if (space != 0 && codes != 1) throw new BrotliDecodeException(BrotliErrors.CorruptedHuffmanCodeHistogram);

        var lengthCode = new BrotliPrefixCode(lengthCodeLengths);
        var lengths = new int[limit];
        var symbol = 0;
        var previous = DefaultCodeLength;
        var repeat = 0;
        var repeatLength = 0;
        var room = 32768;
        while (symbol < limit && room > 0)
        {
            var code = lengthCode.Read(bits);
            if (code < RepeatPreviousCode)
            {
                repeat = 0;
                lengths[symbol++] = code;
                if (code == 0) continue;
                previous = code;
                room -= 32768 >> code;
                continue;
            }
            var extraBits = code - 14;
            var newLength = code == RepeatPreviousCode ? previous : 0;
            if (repeatLength != newLength)
            {
                repeat = 0;
                repeatLength = newLength;
            }
            var oldRepeat = repeat;
            if (repeat > 0)
            {
                repeat -= 2;
                repeat <<= extraBits;
            }
            repeat += bits.ReadBits(extraBits) + 3;
            var delta = repeat - oldRepeat;
            if (symbol + delta > limit) throw new BrotliDecodeException(BrotliErrors.CorruptedCodeLengthTable);
            for (var i = 0; i < delta; i++) lengths[symbol++] = repeatLength;
            if (repeatLength != 0) room -= delta << (15 - repeatLength);
        }
        if (room != 0) throw new BrotliDecodeException(BrotliErrors.UnusedHuffmanSpace);
        return new BrotliPrefixCode(lengths);
    }

    internal static int Log2Floor(int value)
    {
        var result = -1;
        while (value > 0)
        {
            value >>= 1;
            result++;
        }
        return result;
    }
}
