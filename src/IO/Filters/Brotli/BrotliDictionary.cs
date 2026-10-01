namespace Aspose.Pdf.IO.Filters.Brotli;

/// <summary>
/// The static dictionary and its word transforms (RFC 7932 section 8, appendices A and B). Words
/// of length 4 to 24 sit one length after another, 2^bits of each length; a reference names a
/// word and one of 121 transforms that dress it with a prefix, a change and a suffix.
/// </summary>
internal static class BrotliDictionary
{
    internal const int MinWordLength = 4;
    internal const int MaxWordLength = 24;
    internal const int TransformCount = 121;

    private static readonly int[] StandardSizeBits =
    {
        0, 0, 0, 0, 10, 10, 11, 11, 10, 10, 10, 10, 10, 9, 9, 8, 7, 7, 8, 7, 7, 6, 6, 5, 5,
    };

    private static byte[]? _data;
    private static int[] _sizeBits = StandardSizeBits;
    private static int[] _offsets = Offsets(StandardSizeBits);
    private static readonly object Gate = new();

    /// <summary>The dictionary in use: the standard one unless another was set.</summary>
    internal static byte[] Data
    {
        get
        {
            lock (Gate)
            {
                return _data ??= ManagedInflater.InflateZlib(System.Convert.FromBase64String(BrotliData.DictionaryDeflated));
            }
        }
    }

    /// <summary>How many words of each length there are, as a power of two.</summary>
    internal static int[] SizeBits
    {
        get
        {
            lock (Gate) return _sizeBits;
        }
    }

    /// <summary>
    /// Replaces the dictionary. The data must hold exactly 2^bits words of each length (0 bits
    /// for a length it has none of); otherwise nothing changes and false comes back.
    /// </summary>
    internal static bool Set(byte[] data, int[] sizeBits)
    {
        if (data == null || sizeBits == null || sizeBits.Length < MaxWordLength + 1) return false;
        var offsets = Offsets(sizeBits);
        if (offsets[MaxWordLength + 1] != data.Length) return false;
        lock (Gate)
        {
            _data = data;
            _sizeBits = (int[])sizeBits.Clone();
            _offsets = offsets;
        }
        return true;
    }

    private static int[] Offsets(int[] sizeBits)
    {
        var offsets = new int[MaxWordLength + 2];
        for (var length = 0; length <= MaxWordLength; length++)
        {
            var words = sizeBits[length] > 0 ? 1 << sizeBits[length] : 0;
            offsets[length + 1] = offsets[length] + length * words;
        }
        return offsets;
    }

    /// <summary>
    /// The word a reference of that length and word id names, transformed; null when the id
    /// names no word or no transform.
    /// </summary>
    internal static byte[]? Word(int length, int wordId)
    {
        int[] sizeBits, offsets;
        lock (Gate)
        {
            sizeBits = _sizeBits;
            offsets = _offsets;
        }
        if (length < MinWordLength || length > MaxWordLength || sizeBits[length] == 0) return null;
        var bits = sizeBits[length];
        var index = wordId & ((1 << bits) - 1);
        var transform = wordId >> bits;
        if (transform >= TransformCount) return null;
        return Transform(Data, offsets[length] + index * length, length, transform);
    }

    private const int OmitLast9 = 9;
    private const int UppercaseFirst = 10;
    private const int UppercaseAll = 11;
    private const int OmitFirst1 = 12;
    private const int OmitFirst9 = 20;

    // Prefix, the word changed as the transform says, suffix.
    private static byte[] Transform(byte[] data, int start, int length, int transform)
    {
        var prefix = Affix(BrotliData.Transforms[transform * 3]);
        var type = BrotliData.Transforms[transform * 3 + 1];
        var suffix = Affix(BrotliData.Transforms[transform * 3 + 2]);
        if (type <= OmitLast9)
        {
            length -= type;
        }
        else if (type >= OmitFirst1 && type <= OmitFirst9)
        {
            var skip = type - (OmitFirst1 - 1);
            start += skip;
            length -= skip;
        }
        if (length < 0) length = 0;
        var result = new byte[prefix.Length + length + suffix.Length];
        System.Array.Copy(prefix, result, prefix.Length);
        System.Array.Copy(data, start, result, prefix.Length, length);
        System.Array.Copy(suffix, 0, result, prefix.Length + length, suffix.Length);
        if (type == UppercaseFirst && length > 0)
        {
            ToUpperCase(result, prefix.Length, prefix.Length + length);
        }
        else if (type == UppercaseAll)
        {
            var at = prefix.Length;
            var end = prefix.Length + length;
            while (at < end) at += ToUpperCase(result, at, end);
        }
        return result;
    }

    private static byte[] Affix(int id)
    {
        var start = BrotliData.PrefixSuffixMap[id];
        var length = BrotliData.PrefixSuffix[start];
        var affix = new byte[length];
        System.Array.Copy(BrotliData.PrefixSuffix, start + 1, affix, 0, length);
        return affix;
    }

    // RFC 7932's uppercasing: ASCII letters flip case, the second byte of a two-byte UTF-8
    // sequence flips bit 5, the third byte of a longer one flips bits 0 and 2.
    private static int ToUpperCase(byte[] p, int at, int end)
    {
        if (p[at] < 0xC0)
        {
            if (p[at] >= 'a' && p[at] <= 'z') p[at] ^= 32;
            return 1;
        }
        if (p[at] < 0xE0)
        {
            if (at + 1 < end) p[at + 1] ^= 32;
            return 2;
        }
        if (at + 2 < end) p[at + 2] ^= 5;
        return 3;
    }
}
