using System.Collections.Generic;
using System.IO;

namespace Aspose.Pdf.IO;

internal static partial class PdfLinearizer
{
    // Widths of the header items of the hint tables (ISO 32000-1 Tables F.3, F.5, F.6).
    private const int CountBits = 32;
    private const int OffsetBits = 32;
    private const int LengthBits = 32;
    private const int WidthBits = 16;
    // The denominator for the fractional position of a shared object reference within a
    // page's content: the numerators are written with zero bits, so it carries nothing.
    private const int SharedDenominator = 4;

    /// <summary>Packs values most significant bit first, each table column starting on a byte.</summary>
    private sealed class HintBitWriter
    {
        private readonly MemoryStream _bytes = new();
        private int _pending;
        private int _pendingBits;

        public long Position => _bytes.Length;

        public void Write(long value, int bits)
        {
            for (var i = bits - 1; i >= 0; i--)
            {
                _pending = (_pending << 1) | (int)((value >> i) & 1);
                if (++_pendingBits == 8) { _bytes.WriteByte((byte)_pending); _pending = 0; _pendingBits = 0; }
            }
        }

        public void Align()
        {
            if (_pendingBits > 0) Write(0, 8 - _pendingBits);
        }

        public byte[] ToArray() { Align(); return _bytes.ToArray(); }
    }

    /// <summary>The number of bits that hold every value up to <paramref name="max"/>.</summary>
    private static int BitsFor(long max)
    {
        var bits = 0;
        for (; max > 0; max >>= 1) bits++;
        return bits;
    }

    private static void WriteColumn(HintBitWriter w, IReadOnlyList<long> values, long least, int bits)
    {
        foreach (var v in values) w.Write(v - least, bits);
        w.Align();
    }

    /// <summary>
    /// The primary hint stream data: the page offset hint table, then the shared object hint
    /// table at <paramref name="sharedTableOffset"/>, then the outline hint table at
    /// <paramref name="outlineTableOffset"/> (0 when the document has no outlines). Offsets are
    /// hint-free (Annex F.4): where the object would lie without the hint stream.
    /// </summary>
    private static byte[] BuildHintTables(LinearizeState lz, Dictionary<RawObject, long> hintFree,
        out long sharedTableOffset, out long outlineTableOffset)
    {
        var w = new HintBitWriter();
        var sharedIndex = new Dictionary<RawObject, int>();
        foreach (var o in lz.part6) sharedIndex[o] = sharedIndex.Count;
        foreach (var o in lz.part8) sharedIndex[o] = sharedIndex.Count;
        WritePageOffsetTable(lz, w, hintFree, sharedIndex);
        sharedTableOffset = w.Position;
        WriteSharedObjectTable(lz, w, hintFree, sharedIndex.Count);
        outlineTableOffset = 0;
        if (lz.outlinesRoot is { } outlines)
        {
            outlineTableOffset = w.Position;
            w.Write(outlines.NewNum, CountBits);
            w.Write(hintFree[outlines], OffsetBits);
            w.Write(lz.outlineObjectCount, CountBits);
            w.Write(RunLength(lz, outlines.NewNum, lz.outlineObjectCount), LengthBits);
        }
        return w.ToArray();
    }

    /// <summary>Table F.3 and one F.4 entry per page: each page's object count and length, the
    /// shared objects it uses, and content figures that repeat the page's (Acrobat's convention:
    /// the content stream is not located within the page).</summary>
    private static void WritePageOffsetTable(LinearizeState lz, HintBitWriter w,
        Dictionary<RawObject, long> hintFree, Dictionary<RawObject, int> sharedIndex)
    {
        var n = lz.pages.Count;
        var counts = new long[n];
        var lengths = new long[n];
        var shared = new List<int>[n];
        var maxShared = 0;
        for (var i = 0; i < n; i++)
        {
            var page = lz.objs[lz.pages[i]];
            counts[i] = lz.pageObjectCounts[i];
            lengths[i] = RunLength(lz, page.NewNum, lz.pageObjectCounts[i]);
            shared[i] = new List<int>();
            // The first page's shared objects are part of its own section (F.4.1 item 4 is 0).
            // A packed object is reached through its object stream, which is listed once.
            var listed = new HashSet<int>();
            if (i > 0)
                foreach (var num in lz.pageObjects[i])
                {
                    var o = lz.objs[num];
                    if (sharedIndex.TryGetValue(o.Container ?? o, out var index) && listed.Add(index))
                        shared[i].Add(index);
                }
            if (shared[i].Count > maxShared) maxShared = shared[i].Count;
        }
        long minCount = Min(counts), maxCount = Max(counts), minLength = Min(lengths), maxLength = Max(lengths);
        var countBits = BitsFor(maxCount - minCount);
        var lengthBits = BitsFor(maxLength - minLength);
        var sharedCountBits = BitsFor(maxShared);
        var identifierBits = BitsFor(sharedIndex.Count);

        w.Write(minCount, CountBits);
        w.Write(hintFree[lz.objs[lz.pages[0]]], OffsetBits);
        w.Write(countBits, WidthBits);
        w.Write(minLength, LengthBits);
        w.Write(lengthBits, WidthBits);
        w.Write(0, OffsetBits);          // least content stream offset
        w.Write(0, WidthBits);
        w.Write(minLength, LengthBits);  // least content stream length
        w.Write(lengthBits, WidthBits);
        w.Write(sharedCountBits, WidthBits);
        w.Write(identifierBits, WidthBits);
        w.Write(0, WidthBits);           // numerator bits
        w.Write(SharedDenominator, WidthBits);

        WriteColumn(w, counts, minCount, countBits);
        WriteColumn(w, lengths, minLength, lengthBits);
        var sharedCounts = new long[n];
        for (var i = 0; i < n; i++) sharedCounts[i] = shared[i].Count;
        WriteColumn(w, sharedCounts, 0, sharedCountBits);
        foreach (var list in shared)
            foreach (var index in list) w.Write(index, identifierBits);
        w.Align();
        w.Align(); // the numerators, zero bits each
        WriteColumn(w, new long[n], 0, 0); // content stream offsets
        WriteColumn(w, lengths, minLength, lengthBits); // content stream lengths
    }

    /// <summary>Tables F.5 and F.6: one group per object - every part 6 object, then part 8.</summary>
    private static void WriteSharedObjectTable(LinearizeState lz, HintBitWriter w,
        Dictionary<RawObject, long> hintFree, int total)
    {
        var lengths = new List<long>(total);
        foreach (var o in lz.part6) lengths.Add(o.Length);
        foreach (var o in lz.part8) lengths.Add(o.Length);
        var minLength = Min(lengths);
        var lengthBits = BitsFor(Max(lengths) - minLength);
        var firstShared = lz.part8.Count > 0 ? lz.part8[0] : null;

        w.Write(firstShared?.NewNum ?? 0, CountBits);
        w.Write(firstShared is null ? 0 : hintFree[firstShared], OffsetBits);
        w.Write(lz.part6.Count, CountBits);
        w.Write(total, CountBits);
        w.Write(0, WidthBits);            // objects per group, less one: always one object
        w.Write(minLength, LengthBits);
        w.Write(lengthBits, WidthBits);
        WriteColumn(w, lengths, minLength, lengthBits);
        WriteColumn(w, new long[total], 0, 1); // no MD5 signatures
        WriteColumn(w, new long[total], 0, 0); // group object counts, less one
    }

    /// <summary>The length of <paramref name="count"/> objects numbered consecutively from
    /// <paramref name="first"/>.</summary>
    private static long RunLength(LinearizeState lz, int first, int count)
    {
        long length = 0;
        for (var i = 0; i < count; i++) length += lz.byNewNum[first + i].Length;
        return length;
    }

    private static long Min(IReadOnlyList<long> values)
    {
        var min = long.MaxValue;
        foreach (var v in values) if (v < min) min = v;
        return values.Count == 0 ? 0 : min;
    }

    private static long Max(IReadOnlyList<long> values)
    {
        long max = 0;
        foreach (var v in values) if (v > max) max = v;
        return max;
    }
}
