using System;
using System.Collections.Generic;

namespace Aspose.Pdf.Text;

internal static partial class CffToTrueType
{
    /// <summary>The sfnt as a browser accepts it. A font sanitiser refuses a program that lacks an
    /// OS/2, name or post table, or whose hmtx is shorter than hhea and maxp declare; a PDF viewer
    /// draws such a subset without complaint, so embedded TrueType subsets routinely ship that way.
    /// The missing tables get the minimal forms the CFF conversion writes and the hmtx is padded;
    /// a program that already carries them comes back untouched.</summary>
    public static byte[] MakeShippable(byte[] sfnt)
    {
        try
        {
            if (sfnt.Length < 12) return sfnt;
            static ushort U16(byte[] b, int o) => (ushort)((b[o] << 8) | b[o + 1]);
            static uint U32(byte[] b, int o) =>
                (uint)((b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]);
            int numTables = U16(sfnt, 4);
            var tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
            for (var i = 0; i < numTables; i++)
            {
                var e = 12 + i * 16;
                if (e + 16 > sfnt.Length) return sfnt;
                var tag = System.Text.Encoding.ASCII.GetString(sfnt, e, 4);
                var off = (int)U32(sfnt, e + 8);
                var len = (int)U32(sfnt, e + 12);
                if (off < 0 || len < 0 || off + len > sfnt.Length) return sfnt;
                var data = new byte[len];
                Array.Copy(sfnt, off, data, 0, len);
                tables[tag] = data;
            }
            if (!tables.TryGetValue("hhea", out var hhea) || !tables.TryGetValue("maxp", out var maxp)
                || !tables.TryGetValue("hmtx", out var hmtx) || !tables.TryGetValue("head", out var head)
                || hhea.Length < 36 || maxp.Length < 6 || head.Length < 44)
                return sfnt;
            var changed = false;
            int metrics = U16(hhea, 34), glyphs = U16(maxp, 4);
            // hmtx holds one 4-byte record per metric and a 2-byte bearing for every glyph after them.
            var needed = metrics * 4 + Math.Max(0, glyphs - metrics) * 2;
            if (hmtx.Length < needed)
            {
                var padded = new byte[needed];
                Array.Copy(hmtx, padded, hmtx.Length);
                tables["hmtx"] = hmtx = padded;
                changed = true;
            }
            if (!tables.ContainsKey("name")) { tables["name"] = BuildName(); changed = true; }
            if (!tables.ContainsKey("post")) { tables["post"] = BuildPost(); changed = true; }
            if (!tables.ContainsKey("OS/2"))
            {
                var advances = new int[metrics];
                for (var g = 0; g < metrics; g++) advances[g] = U16(hmtx, g * 4);
                tables["OS/2"] = BuildOs2(advances, (short)U16(head, 42), (short)U16(head, 38), new Dictionary<int, int>());
                changed = true;
            }
            return changed ? AssembleSfnt(tables, flavour: U32(sfnt, 0)) : sfnt;
        }
        catch { return sfnt; }
    }
}
