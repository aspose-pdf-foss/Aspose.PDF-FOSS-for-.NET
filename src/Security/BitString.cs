namespace Aspose.Pdf.Security;

/// <summary>
/// A variable-length bit string supporting bitwise operations, concatenation,
/// substring extraction, and conversion to/from bytes and hex.
/// Bit indexing: bit 0 is the least-significant (rightmost) bit in the string
/// representation returned by ToString(). This matches .NET BitString semantics.
/// </summary>
public sealed class BitString : IEquatable<BitString>
{
    // Internal storage: 32-bit words, MSB-first.
    // Bit at string position p (0 = leftmost = string MSB) lives in:
    //   _words[p >> 5], at shift position (31 - (p & 31))
    private uint[] _words;
    private int _length;

    // ── Constructors ──────────────────────────────────────────────────────────

    /// <summary>Create a zero-filled BitString of the given length.</summary>
    public BitString(int length) : this(length, false) { }

    /// <summary>Create a BitString of the given length filled with 0 or 1.</summary>
    public BitString(int length, bool fill)
    {
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
        _length = length;
        _words = new uint[WordCount(length)];
        if (fill && length > 0)
        {
            for (var i = 0; i < _words.Length; i++)
                _words[i] = 0xFFFFFFFFu;
            // Clear unused high bits in last word
            var rem = length & 31;
            if (rem > 0)
                _words[_words.Length - 1] = 0xFFFFFFFFu << (32 - rem);
        }
    }

    /// <summary>
    /// Create from a byte array (bytes in reverse order: last byte → leftmost bits).
    /// </summary>
    public BitString(byte[] bytes)
    {
        var tmp = FromBytes(bytes);
        _words = tmp._words;
        _length = tmp._length;
    }

    /// <summary>
    /// Create from bytes with trim to minBitLength then left-pad to totalBitLength.
    /// </summary>
    public BitString(byte[] bytes, int minBitLength, int totalBitLength)
    {
        var tmp = FromBytes(bytes);
        if (minBitLength < tmp._length)
            tmp = tmp.ExtractBits(tmp._length - minBitLength, minBitLength);
        tmp.PadWithZeroes(totalBitLength);
        _words = tmp._words;
        _length = tmp._length;
    }

    /// <summary>Copy an existing BitString and prepend <paramref name="padding"/> zero bits.</summary>
    public BitString(BitString source) : this(source, 0) { }

    /// <summary>Copy an existing BitString and prepend <paramref name="padding"/> zero bits.</summary>
    public BitString(BitString source, int padding)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        var newLen = source._length + padding;
        _length = newLen;
        _words = new uint[WordCount(newLen)];
        CopyBits(source._words, 0, _words, padding, source._length);
    }

    private BitString(uint[] words, int length)
    {
        _words = words;
        _length = length;
    }

    // ── Properties ────────────────────────────────────────────────────────────

    /// <summary>Number of bits.</summary>
    public int Length => _length;

    // ── Indexer ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Get or set bit at position (0 = LSB / rightmost in ToString()).
    /// Throws <see cref="IndexOutOfRangeException"/> if out of range.
    /// </summary>
    public bool this[int index]
    {
        get
        {
            if (index < 0 || index >= _length)
                throw new IndexOutOfRangeException($"Index {index} out of range [0, {_length}).");
            var pos = _length - 1 - index; // map to string position (0=leftmost)
            var wi = pos >> 5;
            var bi = 31 - (pos & 31);
            return ((_words[wi] >> bi) & 1u) == 1u;
        }
        set
        {
            if (index < 0 || index >= _length)
                throw new IndexOutOfRangeException($"Index {index} out of range [0, {_length}).");
            var pos = _length - 1 - index;
            var wi = pos >> 5;
            var bi = 31 - (pos & 31);
            if (value)
                _words[wi] |= (1u << bi);
            else
                _words[wi] &= ~(1u << bi);
        }
    }

    // ── Static factories ──────────────────────────────────────────────────────

    public static BitString Empty() => new(Array.Empty<uint>(), 0);
    public static BitString Zero() => FromBitsString("0");
    public static BitString One() => FromBitsString("1");
    public static BitString Zeroes(int count) => new(count, false);
    public static BitString Ones(int count) => new(count, true);

    public static BitString FromBitsString(string s)
    {
        if (s is null) throw new ArgumentNullException(nameof(s));
        var len = s.Length;
        var words = new uint[WordCount(len)];
        for (var i = 0; i < len; i++)
        {
            if (s[i] == '1')
            {
                var wi = i >> 5;
                var bi = 31 - (i & 31);
                words[wi] |= 1u << bi;
            }
        }
        return new BitString(words, len);
    }

    /// <summary>
    /// Parse from hex string (big-endian bytes), optionally trim to <paramref name="bitLength"/> LSBs.
    /// </summary>
    public static BitString FromHexString(string hex, int? bitLength = null)
    {
        if (hex is null) throw new ArgumentNullException(nameof(hex));
        var byteCount = hex.Length / 2;
        var bytes = new byte[byteCount];
        for (var i = 0; i < byteCount; i++)
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        var bs = FromBytes(bytes);
        if (bitLength.HasValue && bitLength.Value < bs._length)
            bs = bs.ExtractBits(bs._length - bitLength.Value, bitLength.Value);
        return bs;
    }

    public static BitString FromBytesBuffer(byte[] buffer, int offset, int count)
    {
        var slice = new byte[count];
        Array.Copy(buffer, offset, slice, 0, count);
        return FromBytes(slice);
    }

    // ── Conversion ────────────────────────────────────────────────────────────

    /// <summary>Binary string of '0' and '1' (leftmost = MSB = bit (Length-1)).</summary>
    public override string ToString()
    {
        if (_length == 0) return string.Empty;
        var chars = new char[_length];
        for (var i = 0; i < _length; i++)
        {
            var wi = i >> 5;
            var bi = 31 - (i & 31);
            chars[i] = ((_words[wi] >> bi) & 1u) == 1u ? '1' : '0';
        }
        return new string(chars);
    }

    /// <summary>Hex byte string (same byte order as constructor: byte[0] is LSB → "0d19ff20").</summary>
    public string ToHexByteString()
    {
        var bytes = ToBytes();
        var sb = new System.Text.StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
            sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    /// <summary>
    /// Convert to byte array. byte[0] holds LSBs (bits 0-7), byte[n-1] holds MSBs.
    /// </summary>
    public byte[] ToBytes()
    {
        var byteCount = (_length + 7) / 8;
        var bytes = new byte[byteCount];
        for (var i = 0; i < byteCount; i++)
        {
            byte b = 0;
            for (var j = 0; j < 8; j++)
            {
                var bitPos = i * 8 + j;
                if (bitPos < _length && this[bitPos])
                    b |= (byte)(1 << j);
            }
            bytes[i] = b;
        }
        return bytes;
    }

    // ── Equality ──────────────────────────────────────────────────────────────

    public bool Equals(BitString? other)
    {
        if (other is null) return false;
        if (_length != other._length) return false;
        for (var i = 0; i < _words.Length; i++)
            if (_words[i] != other._words[i]) return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is BitString bs && Equals(bs);

    public override int GetHashCode()
    {
        var h = HashCode.Combine(_length);
        foreach (var w in _words) h = HashCode.Combine(h, w);
        return h;
    }

    public static bool operator ==(BitString? a, BitString? b) =>
        a is null ? b is null : a.Equals(b);
    public static bool operator !=(BitString? a, BitString? b) => !(a == b);

    // ── Bitwise operations ────────────────────────────────────────────────────

    public static BitString Xor(BitString a, BitString b)
    {
        var (longer, shorter) = GetLongerShorter(a, b);
        var result = Copy(longer);
        var offset = result._length - shorter._length;
        for (var i = 0; i < shorter._length; i++)
        {
            var p = offset + i;
            var wi = p >> 5;
            var bi = 31 - (p & 31);
            var swi = i >> 5;
            var sbi = 31 - (i & 31);
            if (((shorter._words[swi] >> sbi) & 1u) == 1u)
                result._words[wi] ^= 1u << bi;
        }
        return result;
    }

    public void Xor(BitString other)
    {
        var res = Xor(this, other);
        _words = res._words;
        _length = res._length;
    }

    public static BitString Or(BitString a, BitString b)
    {
        var (longer, shorter) = GetLongerShorter(a, b);
        var result = Copy(longer);
        var offset = result._length - shorter._length;
        for (var i = 0; i < shorter._length; i++)
        {
            var p = offset + i;
            var wi = p >> 5;
            var bi = 31 - (p & 31);
            var swi = i >> 5;
            var sbi = 31 - (i & 31);
            if (((shorter._words[swi] >> sbi) & 1u) == 1u)
                result._words[wi] |= 1u << bi;
        }
        return result;
    }

    public void Or(BitString other)
    {
        var res = Or(this, other);
        _words = res._words;
        _length = res._length;
    }

    public static BitString And(BitString a, BitString b)
    {
        var (longer, shorter) = GetLongerShorter(a, b);
        var result = new BitString(longer._length);
        var offset = longer._length - shorter._length;
        // Copy the longer bits, AND-ing with shorter where they overlap
        for (var i = 0; i < longer._length; i++)
        {
            var wi = i >> 5;
            var bi = 31 - (i & 31);
            var longBit = (longer._words[wi] >> bi) & 1u;
            uint shortBit = 0;
            var si = i - offset;
            if (si >= 0)
            {
                var swi = si >> 5;
                var sbi = 31 - (si & 31);
                shortBit = (shorter._words[swi] >> sbi) & 1u;
            }
            if ((longBit & shortBit) == 1u)
                result._words[wi] |= 1u << bi;
        }
        return result;
    }

    public void And(BitString other)
    {
        var res = And(this, other);
        _words = res._words;
        _length = res._length;
    }

    public static BitString Not(BitString a)
    {
        var result = Copy(a);
        for (var i = 0; i < result._words.Length; i++)
            result._words[i] = ~result._words[i];
        // Clear unused bits in last word
        var rem = result._length & 31;
        if (rem > 0 && result._words.Length > 0)
            result._words[result._words.Length - 1] &= 0xFFFFFFFFu << (32 - rem);
        return result;
    }

    public void Not()
    {
        var res = Not(this);
        _words = res._words;
        _length = res._length;
    }

    // ── Concatenation ─────────────────────────────────────────────────────────

    /// <summary>Static: result = b bits followed by a bits (b is prepended).</summary>
    public static BitString Concatenate(BitString a, BitString b)
    {
        var newLen = a._length + b._length;
        var words = new uint[WordCount(newLen)];
        // b goes first (leftmost), then a
        CopyBits(b._words, 0, words, 0, b._length);
        CopyBits(a._words, 0, words, b._length, a._length);
        return new BitString(words, newLen);
    }

    /// <summary>In-place: prepend <paramref name="other"/> bits before this.</summary>
    public void Concatenate(BitString other)
    {
        var res = Concatenate(this, other);
        _words = res._words;
        _length = res._length;
    }

    // ── Substring ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Extract all bits except the last <paramref name="startFromEnd"/> bits.
    /// </summary>
    public BitString Substring(int startFromEnd)
    {
        if (startFromEnd < 0)
            throw new ArgumentOutOfRangeException(nameof(startFromEnd), "Must be >= 0.");
        if (startFromEnd > _length)
            throw new ArgumentOutOfRangeException(nameof(startFromEnd),
                $"startFromEnd ({startFromEnd}) > length ({_length}).");
        var count = _length - startFromEnd;
        return ExtractBits(0, count);
    }

    /// <summary>
    /// Extract <paramref name="count"/> bits starting at position
    /// (<see cref="Length"/> - <paramref name="startFromEnd"/> - <paramref name="count"/>).
    /// </summary>
    public BitString Substring(int startFromEnd, int count)
    {
        if (startFromEnd < 0)
            throw new ArgumentOutOfRangeException(nameof(startFromEnd), "Must be >= 0.");
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Must be >= 0.");
        if (startFromEnd + count > _length)
            throw new ArgumentOutOfRangeException(nameof(count),
                $"startFromEnd ({startFromEnd}) + count ({count}) > length ({_length}).");
        var stringStart = _length - startFromEnd - count;
        return ExtractBits(stringStart, count);
    }

    // ── Padding ───────────────────────────────────────────────────────────────

    /// <summary>Prepend <paramref name="count"/> zero bits.</summary>
    public void PadWithZeroes(int count)
    {
        var newLen = _length + count;
        var newWords = new uint[WordCount(newLen)];
        CopyBits(_words, 0, newWords, count, _length);
        _words = newWords;
        _length = newLen;
    }

    // ── 64-bit access ─────────────────────────────────────────────────────────

    /// <summary>
    /// Read 64 bits starting at bit position <paramref name="pos"/> (must be multiple of 64).
    /// Bit <paramref name="pos"/> is LSB (2^0), bit <paramref name="pos"/>+63 is MSB (2^63).
    /// </summary>
    public ulong Get64Bits(int pos)
    {
        if (pos % 64 != 0)
            throw new ArgumentException("Position must be a multiple of 64.", nameof(pos));
        if (pos + 64 > _length)
            throw new ArgumentOutOfRangeException(nameof(pos), $"pos ({pos}) + 64 > length ({_length}).");
        ulong result = 0;
        for (var i = 0; i < 64; i++)
        {
            if (this[pos + i])
                result |= 1uL << i;
        }
        return result;
    }

    /// <summary>
    /// Write 64 bits starting at bit position <paramref name="pos"/> (must be multiple of 64).
    /// LSB of <paramref name="value"/> → bit <paramref name="pos"/>.
    /// </summary>
    public void Set64Bits(int pos, ulong value)
    {
        if (pos % 64 != 0)
            throw new ArgumentException("Position must be a multiple of 64.", nameof(pos));
        if (pos + 64 > _length)
            throw new ArgumentOutOfRangeException(nameof(pos), $"pos ({pos}) + 64 > length ({_length}).");
        for (var i = 0; i < 64; i++)
            this[pos + i] = ((value >> i) & 1uL) == 1uL;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static int WordCount(int bits) => (bits + 31) / 32;

    private static BitString Copy(BitString src)
    {
        var words = new uint[src._words.Length];
        Array.Copy(src._words, words, src._words.Length);
        return new BitString(words, src._length);
    }

    private static (BitString longer, BitString shorter) GetLongerShorter(BitString a, BitString b) =>
        a._length >= b._length ? (a, b) : (b, a);

    /// <summary>
    /// Create a BitString from bytes (reverse order: last byte → leftmost bits).
    /// </summary>
    private static BitString FromBytes(byte[] bytes)
    {
        // Reverse: last byte becomes the leftmost (most significant) bits
        var len = bytes.Length * 8;
        var words = new uint[WordCount(len)];
        for (var i = 0; i < bytes.Length; i++)
        {
            // byte[bytes.Length-1-i] maps to string position (i*8 . i*8+7)
            var srcIdx = bytes.Length - 1 - i;
            var b = bytes[srcIdx];
            for (var bit = 0; bit < 8; bit++)
            {
                var strPos = i * 8 + (7 - bit); // MSB of byte → smaller string index
                var wi = strPos >> 5;
                var bi = 31 - (strPos & 31);
                if (((b >> bit) & 1) == 1)
                    words[wi] |= 1u << bi;
            }
        }
        return new BitString(words, len);
    }

    /// <summary>Extract <paramref name="count"/> bits starting at string position <paramref name="start"/>.</summary>
    private BitString ExtractBits(int start, int count)
    {
        var words = new uint[WordCount(count)];
        CopyBits(_words, start, words, 0, count);
        return new BitString(words, count);
    }

    /// <summary>
    /// Copy <paramref name="count"/> bits from <paramref name="src"/> starting at string position
    /// <paramref name="srcOffset"/> into <paramref name="dst"/> at string position <paramref name="dstOffset"/>.
    /// </summary>
    private static void CopyBits(uint[] src, int srcOffset, uint[] dst, int dstOffset, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var sp = srcOffset + i;
            var swi = sp >> 5;
            var sbi = 31 - (sp & 31);
            var dp = dstOffset + i;
            var dwi = dp >> 5;
            var dbi = 31 - (dp & 31);
            var bit = (src[swi] >> sbi) & 1u;
            if (bit == 1u)
                dst[dwi] |= 1u << dbi;
            else
                dst[dwi] &= ~(1u << dbi);
        }
    }
}
