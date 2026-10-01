using System.Buffers.Binary;

namespace Aspose.Pdf.IO.Filters;

/// <summary>How hard the deflater searches for back-references.</summary>
internal enum DeflateLevel
{
    /// <summary>Stored blocks only: no compression.</summary>
    Store,
    /// <summary>Greedy matching over short hash chains.</summary>
    Fast,
    /// <summary>Lazy matching over medium chains: the everyday setting.</summary>
    Default,
    /// <summary>Lazy matching over longer chains than <see cref="Default"/>, searching until a match is long enough to keep.</summary>
    Best,
}

/// <summary>
/// RFC 1951 deflate encoder with the RFC 1950 zlib wrapper, in managed code: LZ77 over hash
/// chains, per-block choice of stored, fixed or dynamic Huffman coding. The library owns its
/// compressor for the same reason it owns its inflater - every target framework and every
/// host must write the same bytes for the same input, which no framework-supplied deflate
/// stream promises from one runtime version to the next.
/// </summary>
internal static class ManagedDeflater
{
    private const int ZlibHeaderLength = 2;
    private const int Adler32Length = 4;
    private const byte CompressionMethodAndFlags = 0x78;   // deflate, 32K window
    private const byte FlagsFastest = 0x01;
    private const byte FlagsDefault = 0x9C;
    private const byte FlagsBest = 0xDA;
    private const uint AdlerModulus = 65521;
    private const int AdlerBlock = 5552;   // the largest run whose sums fit in 32 bits without reducing

    public static byte[] DeflateZlib(byte[] data, DeflateLevel level = DeflateLevel.Default)
        => DeflateZlib(data, 0, data.Length, level);

    public static byte[] DeflateZlib(byte[] data, int offset, int count, DeflateLevel level = DeflateLevel.Default)
    {
        var raw = DeflateRaw(data, offset, count, level);
        var result = new byte[ZlibHeaderLength + raw.Length + Adler32Length];
        result[0] = CompressionMethodAndFlags;
        result[1] = level switch
        {
            DeflateLevel.Store or DeflateLevel.Fast => FlagsFastest,
            DeflateLevel.Best => FlagsBest,
            _ => FlagsDefault,
        };
        Array.Copy(raw, 0, result, ZlibHeaderLength, raw.Length);
        var adler = Adler32(data, offset, count);
        var trailer = ZlibHeaderLength + raw.Length;
        result[trailer] = (byte)(adler >> 24);
        result[trailer + 1] = (byte)(adler >> 16);
        result[trailer + 2] = (byte)(adler >> 8);
        result[trailer + 3] = (byte)adler;
        return result;
    }

    public static byte[] DeflateRaw(byte[] data, int offset, int count, DeflateLevel level = DeflateLevel.Default)
    {
        var writer = new DeflateBitWriter(count / 2 + 64);
        if (level == DeflateLevel.Store)
            DeflateBlockWriter.WriteStored(writer, data, offset, count, final: true);
        else
            new Lz77Encoder(data, offset, count, level, writer).Run();
        return writer.ToArray();
    }

    public static uint Adler32(byte[] data, int offset, int count)
    {
        uint a = 1, b = 0;
        var end = offset + count;
        while (offset < end)
        {
            var runEnd = Math.Min(end, offset + AdlerBlock);
            for (; offset < runEnd; offset++)
            {
                a += data[offset];
                b += a;
            }
            a %= AdlerModulus;
            b %= AdlerModulus;
        }
        return (b << 16) | a;
    }
}

/// <summary>Bits go out least-significant first, as deflate reads them.</summary>
internal sealed class DeflateBitWriter
{
    private byte[] _buffer;
    private int _length;
    private ulong _accumulator;
    private int _bitCount;

    public DeflateBitWriter(int capacity)
    {
        _buffer = new byte[Math.Max(capacity, 16)];
    }

    private const int FlushThreshold = 32;
    private static readonly byte[] ReversedByte = BuildReversedBytes();

    public void WriteBits(uint value, int count)
    {
        _accumulator |= (ulong)value << _bitCount;
        _bitCount += count;
        if (_bitCount < FlushThreshold) return;
        // Four whole bytes out at once: at most 31 bits stay, so the next write of up to
        // 32 bits still fits in the accumulator.
        if (_length + sizeof(uint) > _buffer.Length) EnsureCapacity(_length + sizeof(uint));
        var bits = (uint)_accumulator;
        _buffer[_length] = (byte)bits;
        _buffer[_length + 1] = (byte)(bits >> 8);
        _buffer[_length + 2] = (byte)(bits >> 16);
        _buffer[_length + 3] = (byte)(bits >> 24);
        _length += sizeof(uint);
        _accumulator >>= FlushThreshold;
        _bitCount -= FlushThreshold;
    }

    /// <summary>A Huffman code is defined most-significant bit first; the stream wants it reversed.</summary>
    public void WriteCode(uint code, int length)
    {
        var reversed = ((uint)ReversedByte[code & 0xFF] << 8 | ReversedByte[(code >> 8) & 0xFF]) >> (16 - length);
        WriteBits(reversed, length);
    }

    private static byte[] BuildReversedBytes()
    {
        var table = new byte[256];
        for (var i = 0; i < table.Length; i++)
        {
            var reversed = 0;
            for (var bit = 0; bit < 8; bit++)
                if ((i & (1 << bit)) != 0) reversed |= 0x80 >> bit;
            table[i] = (byte)reversed;
        }
        return table;
    }

    /// <summary>Every pending bit out, the last byte padded with zeros.</summary>
    public void AlignToByte()
    {
        while (_bitCount > 0)
        {
            Append((byte)_accumulator);
            _accumulator >>= 8;
            _bitCount -= 8;
        }
        _accumulator = 0;
        _bitCount = 0;
    }

    /// <summary>Whole bytes; the writer must be byte-aligned.</summary>
    public void WriteBytes(byte[] data, int offset, int count)
    {
        EnsureCapacity(_length + count);
        Array.Copy(data, offset, _buffer, _length, count);
        _length += count;
    }

    public byte[] ToArray()
    {
        AlignToByte();
        var result = new byte[_length];
        Array.Copy(_buffer, result, _length);
        return result;
    }

    private void Append(byte b)
    {
        EnsureCapacity(_length + 1);
        _buffer[_length++] = b;
    }

    private void EnsureCapacity(int needed)
    {
        if (needed <= _buffer.Length) return;
        Array.Resize(ref _buffer, Math.Max(needed, _buffer.Length * 2));
    }
}

/// <summary>
/// The LZ77 half: hash chains over three-byte prefixes find back-references, tokens
/// accumulate until a block is full, and each block is handed to the block writer.
/// </summary>
internal sealed class Lz77Encoder
{
    private const int HashBits = 15;
    private const int HashSize = 1 << HashBits;
    private const int HashMask = HashSize - 1;
    private const int WindowMask = DeflateTables.WindowSize - 1;
    private const int MaxBlockTokens = 16384;
    private const int NoPosition = -1;
    private const int NoLimit = int.MaxValue;
    private const int ReducedChainShift = 2;
    // A minimum-length match that far back costs more bits than the three literals it replaces.
    private const int TooFar = 4096;

    private readonly byte[] _data;
    private readonly int _start;
    private readonly int _end;
    private readonly int _chainLimit;
    private readonly int _niceLength;
    private readonly bool _lazy;
    private readonly int _goodLength;
    private readonly int _lazyLength;
    private readonly DeflateBitWriter _writer;
    private readonly int[] _head;
    private readonly int[] _prev;
    private readonly ushort[] _tokenLength;   // 0 = literal
    private readonly ushort[] _tokenValue;    // the literal, or the distance
    private int _tokenCount;
    private int _blockStart;
    private int _blockBytes;

    // The window tables are sized by the 32K window whatever the input is, so a
    // document that writes ten thousand small streams - a page of one text object
    // per fragment - would allocate and zero a quarter megabyte for each of them.
    // They are per-thread scratch instead. Only the head table is refilled per run,
    // because a chain must never reach into the previous one; the chain table and
    // the token tables are written before they are read, so what they still hold is
    // never consulted and the encoder writes the same bytes as a fresh one would.
    [ThreadStatic] private static int[]? t_head;
    [ThreadStatic] private static int[]? t_prev;
    [ThreadStatic] private static ushort[]? t_tokenLength;
    [ThreadStatic] private static ushort[]? t_tokenValue;

    public Lz77Encoder(byte[] data, int offset, int count, DeflateLevel level, DeflateBitWriter writer)
    {
        _data = data;
        _start = offset;
        _end = offset + count;
        _blockStart = offset;
        _writer = writer;
        // The search bounds zlib's levels are known by: a chain is walked a quarter as far once
        // the match to beat is already good, and not at all once it is long enough to keep.
        // Walking further finds little more in PDF content: four times the chain and no bounds
        // came out about half a percent smaller in twice the time on a corpus of page, font
        // and image streams.
        (_chainLimit, _niceLength, _lazy, _goodLength, _lazyLength) = level switch
        {
            DeflateLevel.Fast => (16, 32, false, NoLimit, NoLimit),
            DeflateLevel.Best => (256, DeflateTables.MaxMatch, true, 16, 128),
            _ => (128, 128, true, 8, 16),
        };
        _head = t_head ??= new int[HashSize];
        _prev = t_prev ??= new int[DeflateTables.WindowSize];
        _tokenLength = t_tokenLength ??= new ushort[MaxBlockTokens];
        _tokenValue = t_tokenValue ??= new ushort[MaxBlockTokens];
        Compat.Fill(_head, NoPosition);
    }

    public void Run()
    {
        if (_lazy) RunLazy();
        else RunGreedy();
        FlushBlock(final: true);
    }

    private void RunGreedy()
    {
        var pos = _start;
        while (pos < _end)
        {
            Insert(pos);
            var length = FindMatch(pos, 0, out var distance);
            if (length >= DeflateTables.MinMatch)
            {
                AddMatch(length, distance);
                for (var i = pos + 1; i < pos + length; i++) Insert(i);
                pos += length;
            }
            else
            {
                AddLiteral(_data[pos]);
                pos++;
            }
        }
    }

    /// <summary>A match found at one position is only taken when the next position
    /// cannot do better; otherwise that byte goes out as a literal and the search moves on.</summary>
    private void RunLazy()
    {
        var pos = _start;
        var pendingLength = 0;
        var pendingDistance = 0;
        var pendingLiteral = false;   // the byte at pos - 1 is not yet emitted
        while (pos < _end)
        {
            Insert(pos);
            var distance = 0;
            var length = pendingLength >= _lazyLength ? 0 : FindMatch(pos, pendingLength, out distance);
            if (length == DeflateTables.MinMatch && distance > TooFar) length = 0;
            if (pendingLength >= DeflateTables.MinMatch && length <= pendingLength)
            {
                AddMatch(pendingLength, pendingDistance);
                var matchEnd = pos - 1 + pendingLength;
                for (var i = pos + 1; i < matchEnd; i++) Insert(i);
                pos = matchEnd;
                pendingLength = 0;
                pendingLiteral = false;
                continue;
            }
            if (pendingLiteral) AddLiteral(_data[pos - 1]);
            pendingLiteral = true;
            pendingLength = length;
            pendingDistance = distance;
            pos++;
        }
        if (pendingLiteral) AddLiteral(_data[_end - 1]);
    }

    private int Hash(int pos) => ((_data[pos] << 10) ^ (_data[pos + 1] << 5) ^ _data[pos + 2]) & HashMask;

    private void Insert(int pos)
    {
        if (pos + DeflateTables.MinMatch > _end) return;
        var h = Hash(pos);
        _prev[pos & WindowMask] = _head[h];
        _head[h] = pos;
    }

    /// <summary>Longest match for the (already inserted) position, walking its hash chain.</summary>
    /// <remarks>Only a match longer than <paramref name="toBeat"/> is looked for: a lazy step keeps
    /// the pending match unless the next position does better, so a match no longer than it would
    /// be dropped anyway. The search then rejects most candidates on the one byte past that
    /// length, and finds the same longest match (the first one in the chain) when there is one.</remarks>
    private int FindMatch(int pos, int toBeat, out int bestDistance)
    {
        bestDistance = 0;
        var maxLength = Math.Min(DeflateTables.MaxMatch, _end - pos);
        if (maxLength < DeflateTables.MinMatch) return 0;
        var bestLength = Math.Max(DeflateTables.MinMatch - 1, toBeat);
        if (bestLength >= maxLength) return 0;
        var data = _data;
        var prev = _prev;
        var candidate = prev[pos & WindowMask];
        var limit = Math.Max(_start, pos - DeflateTables.WindowSize);
        var remaining = toBeat >= _goodLength ? _chainLimit >> ReducedChainShift : _chainLimit;
        var firstByte = data[pos];
        var byteToBeat = data[pos + bestLength];
        while (candidate >= limit && remaining-- > 0)
        {
            if (data[candidate + bestLength] == byteToBeat && data[candidate] == firstByte)
            {
                var length = MatchLength(candidate, pos, maxLength);
                if (length > bestLength)
                {
                    bestLength = length;
                    bestDistance = pos - candidate;
                    if (length >= _niceLength || length >= maxLength) break;
                    byteToBeat = data[pos + bestLength];
                }
            }
            // A chain slot overwritten by a newer position would send the walk forward; stop there.
            var next = prev[candidate & WindowMask];
            if (next >= candidate) break;
            candidate = next;
        }
        return bestDistance != 0 ? bestLength : 0;
    }

    private int MatchLength(int candidate, int pos, int maxLength)
    {
        // Eight bytes at a time while eight remain: the first differing byte is the lowest
        // non-zero byte of the difference, the words being read least significant first.
        var length = 0;
        var data = _data.AsSpan();
        while (length + sizeof(ulong) <= maxLength)
        {
            var difference = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(candidate + length))
                ^ BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(pos + length));
            if (difference != 0)
            {
                while ((difference & 0xFF) == 0)
                {
                    difference >>= 8;
                    length++;
                }
                return length;
            }
            length += sizeof(ulong);
        }
        while (length < maxLength && _data[candidate + length] == _data[pos + length]) length++;
        return length;
    }

    private void AddLiteral(byte value)
    {
        _tokenLength[_tokenCount] = 0;
        _tokenValue[_tokenCount] = value;
        _tokenCount++;
        _blockBytes++;
        if (_tokenCount == MaxBlockTokens) FlushBlock(final: false);
    }

    private void AddMatch(int length, int distance)
    {
        _tokenLength[_tokenCount] = (ushort)length;
        _tokenValue[_tokenCount] = (ushort)distance;
        _tokenCount++;
        _blockBytes += length;
        if (_tokenCount == MaxBlockTokens) FlushBlock(final: false);
    }

    private void FlushBlock(bool final)
    {
        if (_tokenCount == 0 && !final) return;
        DeflateBlockWriter.Write(_writer, _tokenLength, _tokenValue, _tokenCount, _data, _blockStart, _blockBytes, final);
        _blockStart += _blockBytes;
        _blockBytes = 0;
        _tokenCount = 0;
    }
}
