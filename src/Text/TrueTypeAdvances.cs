using Aspose.Pdf.Core;

namespace Aspose.Pdf.Text;

/// <summary>
/// Sets glyph advances in a TrueType program, so a program embedded under a font dictionary
/// that states its own widths agrees with them (a PDF reader positions glyphs by the
/// dictionary; a validator requires the program to say the same).
/// </summary>
internal static class TrueTypeAdvances
{
    /// <summary>A copy of <paramref name="program"/> whose hmtx gives each glyph of
    /// <paramref name="advances"/> (glyph id → font units) that advance. Glyphs past the
    /// table's own advances (those that share the last one) are left as they are; the
    /// program is returned unchanged when it has no hhea or hmtx.</summary>
    public static byte[] Patch(byte[] program, IReadOnlyDictionary<int, int> advances)
    {
        if (advances.Count == 0 || program.Length < 12) return program;
        int U16(byte[] b, int o) => (b[o] << 8) | b[o + 1];
        uint U32(byte[] b, int o) => (uint)((b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]);

        var numTables = U16(program, 4);
        int hhea = -1, hmtx = -1, hmtxLength = 0, hmtxRecord = -1;
        for (var i = 0; i < numTables; i++)
        {
            var record = 12 + i * 16;
            if (record + 16 > program.Length) return program;
            var tag = System.Text.Encoding.ASCII.GetString(program, record, 4);
            if (tag == "hhea") hhea = (int)U32(program, record + 8);
            else if (tag == "hmtx")
            {
                hmtx = (int)U32(program, record + 8);
                hmtxLength = (int)U32(program, record + 12);
                hmtxRecord = record;
            }
        }
        if (hhea < 0 || hmtx < 0 || hhea + 36 > program.Length || hmtx + hmtxLength > program.Length) return program;

        var patched = (byte[])program.Clone();
        var numberOfHMetrics = U16(program, hhea + 34);
        foreach (var (gid, advance) in advances)
        {
            if (gid < 0 || gid >= numberOfHMetrics || gid * 4 + 2 > hmtxLength) continue;
            var clamped = Math.Max(0, Math.Min(ushort.MaxValue, advance));
            patched[hmtx + gid * 4] = (byte)(clamped >> 8);
            patched[hmtx + gid * 4 + 1] = (byte)clamped;
        }

        // The table's directory checksum: the sum of its big-endian words, zero-padded.
        uint sum = 0;
        for (var o = 0; o < hmtxLength; o += 4)
        {
            uint word = 0;
            for (var k = 0; k < 4; k++)
                word = (word << 8) | (o + k < hmtxLength ? patched[hmtx + o + k] : (byte)0);
            sum += word;
        }
        patched[hmtxRecord + 4] = (byte)(sum >> 24);
        patched[hmtxRecord + 5] = (byte)(sum >> 16);
        patched[hmtxRecord + 6] = (byte)(sum >> 8);
        patched[hmtxRecord + 7] = (byte)sum;
        return patched;
    }

    /// <summary>The (platform, encoding) pairs of the program's cmap subtables; empty when the
    /// program is not an sfnt or has no cmap.</summary>
    public static HashSet<(int Platform, int Encoding)> CmapSubtables(byte[] program)
    {
        var result = new HashSet<(int, int)>();
        if (program.Length < 12) return result;
        int U16(int o) => (program[o] << 8) | program[o + 1];
        var numTables = U16(4);
        for (var i = 0; i < numTables; i++)
        {
            var record = 12 + i * 16;
            if (record + 16 > program.Length) return result;
            if (System.Text.Encoding.ASCII.GetString(program, record, 4) != "cmap") continue;
            var cmap = (program[record + 8] << 24) | (program[record + 9] << 16) | (program[record + 10] << 8) | program[record + 11];
            if (cmap < 0 || cmap + 4 > program.Length) return result;
            var count = U16(cmap + 2);
            for (var k = 0; k < count && cmap + 4 + k * 8 + 4 <= program.Length; k++)
                result.Add((U16(cmap + 4 + k * 8), U16(cmap + 4 + k * 8 + 2)));
        }
        return result;
    }

    /// <summary>The CID widths a CIDFont's /W array states, in thousandths of an em
    /// (PDF 32000-1 §9.7.4.3: <c>c [w1 w2 …]</c> or <c>cFirst cLast w</c>).</summary>
    public static Dictionary<int, double> CidWidths(PdfArray? w, IO.PdfReader reader)
    {
        var widths = new Dictionary<int, double>();
        if (w is null) return widths;
        double? Number(PdfObject? o) => reader.Resolve(o) switch
        {
            PdfInteger i => i.Value,
            PdfReal r => r.Value,
            _ => null,
        };
        for (var i = 0; i + 1 < w.Count;)
        {
            if (Number(w[i]) is not { } first) break;
            if (reader.Resolve(w[i + 1]) is PdfArray run)
            {
                for (var k = 0; k < run.Count; k++)
                    if (Number(run[k]) is { } width) widths[(int)first + k] = width;
                i += 2;
            }
            else
            {
                if (i + 2 >= w.Count || Number(w[i + 1]) is not { } last || Number(w[i + 2]) is not { } width) break;
                for (var c = (int)first; c <= (int)last && c - (int)first < 65536; c++) widths[c] = width;
                i += 3;
            }
        }
        return widths;
    }
}
