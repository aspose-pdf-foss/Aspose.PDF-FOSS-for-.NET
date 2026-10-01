namespace Aspose.Pdf.IO.Filters.Brotli;

/// <summary>
/// Bits of a Brotli stream, least significant first within each byte. Reading runs on past the
/// end of the input as zero bits; whether that happened is checked where the format allows it to
/// be told apart from a stream that simply ended.
/// </summary>
internal sealed class BrotliBitReader
{
    private readonly byte[] _data;
    private long _bitPosition;

    internal BrotliBitReader(byte[] data) => _data = data;

    /// <summary>The next n bits (n up to 24), the first read in the lowest bit.</summary>
    internal int ReadBits(int n)
    {
        var value = 0;
        for (var i = 0; i < n; i++) value |= ReadBit() << i;
        return value;
    }

    internal int ReadBit()
    {
        var index = _bitPosition >> 3;
        var bit = index < _data.Length ? (_data[index] >> (int)(_bitPosition & 7)) & 1 : 0;
        _bitPosition++;
        return bit;
    }

    /// <summary>The next n bits without consuming them.</summary>
    internal int PeekBits(int n)
    {
        var saved = _bitPosition;
        var value = ReadBits(n);
        _bitPosition = saved;
        return value;
    }

    internal void Skip(int n) => _bitPosition += n;

    /// <summary>Moves to the next byte boundary; the bits passed over must be zero.</summary>
    internal void JumpToByteBoundary()
    {
        var padding = (int)((8 - (_bitPosition & 7)) & 7);
        if (padding != 0 && ReadBits(padding) != 0) throw new BrotliDecodeException(BrotliErrors.CorruptedPaddingBits);
    }

    /// <summary>A whole byte, at a byte boundary.</summary>
    internal int ReadByteAligned()
    {
        var index = _bitPosition >> 3;
        _bitPosition += 8;
        return index < _data.Length ? _data[index] : 0;
    }

    /// <summary>Fails when bytes beyond the input have been read.</summary>
    internal void CheckNotPastEnd()
    {
        if ((_bitPosition + 7) >> 3 > _data.Length) throw new BrotliDecodeException(BrotliErrors.ReadAfterEnd);
    }

    /// <summary>Fails when input is left over after the last meta-block.</summary>
    internal void CheckAtEnd()
    {
        CheckNotPastEnd();
        if ((_bitPosition + 7) >> 3 != _data.Length) throw new BrotliDecodeException(BrotliErrors.UnusedBytesAfterEnd);
    }
}
