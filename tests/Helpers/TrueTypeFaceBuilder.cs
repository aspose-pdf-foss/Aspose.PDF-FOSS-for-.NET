using System.Text;

namespace Aspose.Pdf.Tests.Helpers;

/// <summary>Variants of the minimal TrueType face (<see cref="PdfBuilder.BuildMinimalTrueTypeFont"/>) in the
/// shapes host fonts come in: a face whose only character map is a format 12 one, a Macintosh one or a
/// Windows symbol one, faces of one family that tell their style only where the face itself says it, and a
/// collection of such faces.</summary>
internal static class TrueTypeFaceBuilder
{
    // The minimal face maps the printable ASCII characters to glyphs 1..95, glyph 0 being .notdef.
    private const int FirstChar = 32;
    private const int LastChar = 126;
    private const int GlyphCount = LastChar - FirstChar + 2;
    private const int DefaultAdvance = 500;
    private const int MacintoshPlatform = 1;
    private const int WindowsPlatform = 3;
    private const int RomanEncoding = 0;
    private const int SymbolEncoding = 0;
    private const int UnicodeFullEncoding = 10;
    private const int UnicodeBmpEncoding = 1;
    private const int EnglishUnitedStates = 0x409;
    private const int MacStyleOffset = 44;
    private const int FsSelectionOffset = 62;
    // A Windows symbol table keys code c at U+F000 + c.
    private const int SymbolCodeBase = 0xF000;

    /// <summary>The minimal face with each listed character's glyph advancing by its own width (the
    /// others by 500 units), every character mapped by one (3,10) format 12 table and no format 4 one.</summary>
    internal static byte[] WithFormat12CmapOnly(IReadOnlyDictionary<char, int> advances)
    {
        var tables = Tables(PdfBuilder.BuildMinimalTrueTypeFont());
        tables["hmtx"] = Advances(advances);

        // One encoding record, then a format 12 table of one group: 32..126 to glyphs 1..95.
        var cmap = new byte[4 + 8 + 28];
        Put16(cmap, 2, 1);
        Put16(cmap, 4, WindowsPlatform);
        Put16(cmap, 6, UnicodeFullEncoding);
        Put32(cmap, 8, 12);
        Put16(cmap, 12, 12);
        Put32(cmap, 16, 28);
        Put32(cmap, 24, 1);
        Put32(cmap, 28, FirstChar);
        Put32(cmap, 32, LastChar);
        Put32(cmap, 36, 1);
        tables["cmap"] = cmap;
        return PdfBuilder.AssembleTrueTypeFont(tables);
    }

    /// <summary>The minimal face with each listed character's glyph advancing by its own width (the others by
    /// 500 units), every character mapped by one (1,0) format 0 table - a Macintosh byte table, which keys the
    /// printable ASCII characters by their codes - and no Unicode one.</summary>
    internal static byte[] WithMacintoshCmapOnly(IReadOnlyDictionary<char, int> advances)
    {
        var tables = Tables(PdfBuilder.BuildMinimalTrueTypeFont());
        tables["hmtx"] = Advances(advances);

        // One encoding record, then a format 0 table: a glyph byte for each of the 256 codes.
        var cmap = new byte[4 + 8 + 6 + 256];
        Put16(cmap, 2, 1);
        Put16(cmap, 4, MacintoshPlatform);
        Put16(cmap, 6, RomanEncoding);
        Put32(cmap, 8, 12);
        Put16(cmap, 14, 6 + 256);
        for (var code = FirstChar; code <= LastChar; code++) cmap[18 + code] = (byte)(code - FirstChar + 1);
        tables["cmap"] = cmap;
        return PdfBuilder.AssembleTrueTypeFont(tables);
    }

    /// <summary>The minimal face as a symbol font names its glyphs: one (3,0) format 4 table keying each code
    /// 32..126 at U+F000 + code, and no Unicode table.</summary>
    internal static byte[] WithSymbolCmapOnly()
    {
        var tables = Tables(PdfBuilder.BuildMinimalTrueTypeFont());
        // One encoding record, then a format 4 table of two segments: F020..F07E to glyphs 1..95, and the
        // closing 0xFFFF one.
        const int segments = 2;
        var cmap = new byte[4 + 8 + 16 + 8 * segments];
        Put16(cmap, 2, 1);
        Put16(cmap, 4, WindowsPlatform);
        Put16(cmap, 6, SymbolEncoding);
        Put32(cmap, 8, 12);
        var table = 12;
        Put16(cmap, table, 4);
        Put16(cmap, table + 2, 16 + 8 * segments);
        Put16(cmap, table + 6, 2 * segments);
        Put16(cmap, table + 8, 4);
        Put16(cmap, table + 10, 1);
        var ends = table + 14;
        var starts = ends + 2 * segments + 2;
        var deltas = starts + 2 * segments;
        Put16(cmap, ends, SymbolCodeBase + LastChar);
        Put16(cmap, ends + 2, 0xFFFF);
        Put16(cmap, starts, SymbolCodeBase + FirstChar);
        Put16(cmap, starts + 2, 0xFFFF);
        Put16(cmap, deltas, (1 - (SymbolCodeBase + FirstChar)) & 0xFFFF);
        Put16(cmap, deltas + 2, 1);
        tables["cmap"] = cmap;
        return PdfBuilder.AssembleTrueTypeFont(tables);
    }

    // An hmtx table giving each listed character's glyph its own advance and every other glyph 500 units.
    private static byte[] Advances(IReadOnlyDictionary<char, int> advances)
    {
        var hmtx = new byte[GlyphCount * 4];
        for (var glyph = 0; glyph < GlyphCount; glyph++)
        {
            var advance = glyph > 0 && advances.TryGetValue((char)(FirstChar + glyph - 1), out var own) ? own : DefaultAdvance;
            Put16(hmtx, glyph * 4, advance);
        }
        return hmtx;
    }

    /// <summary>The minimal face named as one face of <paramref name="family"/>: its family (1), subfamily (2),
    /// full (4) and PostScript (6) names, head.macStyle <paramref name="macStyle"/> and OS/2 fsSelection
    /// <paramref name="fsSelection"/> - or no OS/2 table at all where that is null.</summary>
    internal static byte[] Named(string family, string subfamily, int macStyle, int? fsSelection)
    {
        var tables = Tables(PdfBuilder.BuildMinimalTrueTypeFont());
        var regular = subfamily == "Regular";
        var full = regular ? family : family + " " + subfamily;
        var postScript = family.Replace(" ", "") + (regular ? "" : "-" + subfamily.Replace(" ", ""));
        tables["name"] = NameTable((1, family), (2, subfamily), (4, full), (6, postScript));
        Put16(tables["head"], MacStyleOffset, macStyle);
        if (fsSelection is { } selection) Put16(tables["OS/2"], FsSelectionOffset, selection);
        else tables.Remove("OS/2");
        return PdfBuilder.AssembleTrueTypeFont(tables);
    }

    /// <summary>A TrueType collection ('ttcf') of the faces, in their order.</summary>
    internal static byte[] Collection(params byte[][] faces)
    {
        var header = 12 + 4 * faces.Length;
        var result = new byte[header + faces.Sum(f => f.Length)];
        Encoding.ASCII.GetBytes("ttcf").CopyTo(result, 0);
        Put32(result, 4, 0x00010000);
        Put32(result, 8, faces.Length);
        var at = header;
        for (var i = 0; i < faces.Length; i++)
        {
            var face = faces[i];
            Put32(result, 12 + 4 * i, at);
            face.CopyTo(result, at);
            // A collection's table offsets count from the start of the file, not of the face.
            var count = Get16(face, 4);
            for (var t = 0; t < count; t++)
            {
                var entry = at + 12 + 16 * t;
                Put32(result, entry + 8, Get32(result, entry + 8) + at);
            }
            at += face.Length;
        }
        return result;
    }

    private static Dictionary<string, byte[]> Tables(byte[] face)
    {
        var tables = new Dictionary<string, byte[]>();
        var count = Get16(face, 4);
        for (var t = 0; t < count; t++)
        {
            var entry = 12 + 16 * t;
            var tag = Encoding.ASCII.GetString(face, entry, 4);
            var data = new byte[Get32(face, entry + 12)];
            Array.Copy(face, Get32(face, entry + 8), data, 0, data.Length);
            tables[tag] = data;
        }
        return tables;
    }

    private static byte[] NameTable(params (int Id, string Text)[] names)
    {
        var strings = names.Select(n => Encoding.BigEndianUnicode.GetBytes(n.Text)).ToArray();
        var storage = 6 + 12 * names.Length;
        var table = new byte[storage + strings.Sum(s => s.Length)];
        Put16(table, 2, names.Length);
        Put16(table, 4, storage);
        var offset = 0;
        for (var i = 0; i < names.Length; i++)
        {
            var record = 6 + 12 * i;
            Put16(table, record, WindowsPlatform);
            Put16(table, record + 2, UnicodeBmpEncoding);
            Put16(table, record + 4, EnglishUnitedStates);
            Put16(table, record + 6, names[i].Id);
            Put16(table, record + 8, strings[i].Length);
            Put16(table, record + 10, offset);
            strings[i].CopyTo(table, storage + offset);
            offset += strings[i].Length;
        }
        return table;
    }

    private static int Get16(byte[] d, int o) => (d[o] << 8) | d[o + 1];

    private static int Get32(byte[] d, int o) => (d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3];

    private static void Put16(byte[] d, int o, int v)
    {
        d[o] = (byte)(v >> 8);
        d[o + 1] = (byte)v;
    }

    private static void Put32(byte[] d, int o, int v)
    {
        d[o] = (byte)(v >> 24);
        d[o + 1] = (byte)(v >> 16);
        d[o + 2] = (byte)(v >> 8);
        d[o + 3] = (byte)v;
    }
}
