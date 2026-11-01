namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>The pictures placed in this document, by the encoded bytes they came from: a picture
    /// placed again - the same logo in every table row, a header image repeated on each page - is
    /// decoded once and drawn from the one image XObject its first placement embedded, instead of
    /// being embedded once per placement. Each entry keeps the bytes it was decoded from, so a hit
    /// is confirmed byte for byte before the picture is shared.</summary>
    private readonly Dictionary<(string Variant, int Length, ulong A, ulong B), (ImageStamp Stamp, byte[] Source)> _placedPictures = new();

    /// <summary>A stamp of the picture <paramref name="source"/> holds, to place on its own: the
    /// copy shares the decoded picture and its image XObject with every earlier placement of the
    /// same bytes (and <paramref name="variant"/>, which names how they are decoded); null when
    /// <paramref name="decode"/> makes nothing of them.</summary>
    internal ImageStamp? PlacedPicture(byte[] source, string variant, Func<ImageStamp?> decode) =>
        PlacedPicture(source, variant, decode, ContentKey(source));

    /// <summary><see cref="PlacedPicture(byte[], string, Func{ImageStamp?})"/> under a content key the caller
    /// computed. Two different pictures that meet under one key are told apart by their bytes: the later
    /// one is decoded on its own, never drawn as the earlier.</summary>
    internal ImageStamp? PlacedPicture(byte[] source, string variant, Func<ImageStamp?> decode, (ulong A, ulong B) contentKey)
    {
        var key = (variant, source.Length, contentKey.A, contentKey.B);
        (ImageStamp Stamp, byte[] Source) entry;
        bool found;
        lock (_placedPictures) found = _placedPictures.TryGetValue(key, out entry);
        if (found)
        {
            // The key matched: the bytes decide (their length already matched, as part of the key).
            if (ReferenceEquals(entry.Source, source) || entry.Source.AsSpan().SequenceEqual(source))
                return entry.Stamp.ShareCopy();
            return decode();
        }
        var first = decode();
        if (first is null) return null;
        lock (_placedPictures) _placedPictures[key] = (first, source);
        return first.ShareCopy();
    }

    // Two independent 64-bit lanes over the bytes, read eight at a time: every placed picture is named by
    // its content at memory speed, where a cryptographic digest cost several percent of laying out pages
    // of distinct pictures.
    private const ulong LaneASeed = 0x9E3779B97F4A7C15UL;
    private const ulong LaneBSeed = 0xC2B2AE3D27D4EB4FUL;
    private const ulong LaneAPrime = 0xFF51AFD7ED558CCDUL;
    private const ulong LaneBPrime = 0xC4CEB9FE1A85EC53UL;
    private const int LaneARotation = 31;
    private const int LaneBRotation = 27;
    private const int WordBits = 64;
    private const int WordBytes = 8;

    private static (ulong A, ulong B) ContentKey(byte[] source)
    {
        ulong a = LaneASeed, b = LaneBSeed;
        var words = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, ulong>(source.AsSpan());
        foreach (var word in words)
        {
            a = RotateLeft(a ^ word, LaneARotation) * LaneAPrime;
            b = RotateLeft(b + word, LaneBRotation) * LaneBPrime;
        }
        for (var i = words.Length * WordBytes; i < source.Length; i++)
        {
            a = RotateLeft(a ^ source[i], LaneARotation) * LaneAPrime;
            b = RotateLeft(b + source[i], LaneBRotation) * LaneBPrime;
        }
        return (a ^ (a >> LaneARotation), b ^ (b >> LaneBRotation));
    }

    private static ulong RotateLeft(ulong value, int count) => (value << count) | (value >> (WordBits - count));
}
