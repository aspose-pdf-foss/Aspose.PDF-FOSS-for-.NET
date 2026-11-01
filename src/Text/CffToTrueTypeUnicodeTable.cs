using System.Collections.Generic;

namespace Aspose.Pdf.Text;

internal static partial class CffToTrueType
{
    /// <summary>The characters a Unicode cmap subtable (format 4 or 12) maps, with their glyphs; null for
    /// another format or a malformed table.</summary>
    private static Dictionary<int, int>? ReadUnicodeSubtable(byte[] cmap, int off)
    {
        static int U16(byte[] b, int o) => (b[o] << 8) | b[o + 1];
        static long U32(byte[] b, int o) => ((long)b[o] << 24) | ((long)b[o + 1] << 16) | ((long)b[o + 2] << 8) | b[o + 3];
        var map = new Dictionary<int, int>();
        if (off + 16 > cmap.Length) return null;
        var format = U16(cmap, off);
        if (format == 12)
        {
            const int GroupSize = 12, HeaderSize = 16;
            var groups = U32(cmap, off + 12);
            if (off + HeaderSize + groups * GroupSize > cmap.Length) return null;
            for (var g = 0; g < groups; g++)
            {
                var r = off + HeaderSize + g * GroupSize;
                long start = U32(cmap, r), end = U32(cmap, r + 4), gid = U32(cmap, r + 8);
                for (var c = start; c <= end && c <= char.MaxValue; c++) map[(int)c] = (int)(gid + c - start);
            }
            return map;
        }
        if (format != 4) return null;
        var segCount = U16(cmap, off + 6) / 2;
        int ends = off + 14, starts = ends + segCount * 2 + 2, deltas = starts + segCount * 2, ranges = deltas + segCount * 2;
        if (ranges + segCount * 2 > cmap.Length) return null;
        for (var i = 0; i < segCount; i++)
        {
            int end = U16(cmap, ends + i * 2), start = U16(cmap, starts + i * 2);
            int delta = (short)U16(cmap, deltas + i * 2), range = U16(cmap, ranges + i * 2);
            for (var c = start; c <= end && c < char.MaxValue; c++)
            {
                var gid = (c + delta) & char.MaxValue;
                if (range != 0)
                {
                    var at = ranges + i * 2 + range + (c - start) * 2;
                    if (at + 2 > cmap.Length) continue;
                    gid = U16(cmap, at);
                    if (gid != 0) gid = (gid + delta) & char.MaxValue;
                }
                if (gid != 0) map[c] = gid;
            }
        }
        return map;
    }

    /// <summary>True when every character of <paramref name="unicodeToGid"/> reaches the same glyph through
    /// <paramref name="existing"/>.</summary>
    private static bool AgreesWith(Dictionary<int, int> existing, Dictionary<int, int> unicodeToGid, int glyphCount)
    {
        foreach (var kv in unicodeToGid)
        {
            if (kv.Key is <= 0 or > 0xFFFE || kv.Value <= 0 || kv.Value >= glyphCount) continue;
            if (!existing.TryGetValue(kv.Key, out var gid) || gid != kv.Value) return false;
        }
        return true;
    }
}
