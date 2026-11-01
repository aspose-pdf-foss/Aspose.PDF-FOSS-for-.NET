using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Text.OpenType;

namespace Aspose.Pdf.Text;

public sealed partial class TextAbsorber
{
    private const int GsubLigatureLookupType = 4;
    private const int CoverageGlyphList = 1;
    private const int CoverageRangeList = 2;

    private sealed class LigatureEntry { public Dictionary<int, int[]>? Map; }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PdfDictionary, LigatureEntry> _ligatureCache = new();

    /// <summary>
    /// The text of a CID that the font's /ToUnicode CMap leaves out, read from the embedded program:
    /// its cmap names the character of a plain glyph, and a ligature glyph (which no cmap reaches)
    /// reads as the characters of the glyphs its GSUB ligature rule is built from. A producer that
    /// maps only some codes (a printer driver writing just the space) leaves the rest to the program.
    /// Null when the program does not say.
    /// </summary>
    private static string? UnmappedCidText(PdfDictionary? fontDict, PdfReader? reader, int code)
    {
        if (fontDict is null || reader is null) return null;
        if (GetGidToUnicode(fontDict, reader) is not { } characters) return null;
        if (characters.TryGetValue(code, out var single)) return char.ConvertFromUtf32(single);
        if (GetLigatureComponents(fontDict, reader) is not { } ligatures
            || !ligatures.TryGetValue(code, out var parts)) return null;
        var sb = new StringBuilder(parts.Length);
        foreach (var part in parts)
        {
            if (!characters.TryGetValue(part, out var letter)) return null;
            sb.Append(char.ConvertFromUtf32(letter));
        }
        return sb.ToString();
    }

    /// <summary>Ligature glyph id -> the glyph ids it replaces, for an Identity-mapped embedded TrueType
    /// program (a CID is then the glyph id). Built once per font, on the first code the CMap misses.</summary>
    private static Dictionary<int, int[]>? GetLigatureComponents(PdfDictionary fontDict, PdfReader reader)
    {
        if (_ligatureCache.TryGetValue(fontDict, out var cached)) return cached.Map;
        var entry = new LigatureEntry();
        try
        {
            var descendants = reader.Resolve(fontDict.Get("DescendantFonts")) as PdfArray;
            var descendant = descendants is { Count: > 0 } ? reader.ResolveDict(descendants[0]) : null;
            var cidToGid = descendant?.Get("CIDToGIDMap");
            var identity = cidToGid is null || (reader.Resolve(cidToGid) as PdfName)?.Value == "Identity";
            var descriptor = descendant is null ? null : reader.ResolveDict(descendant.Get("FontDescriptor"));
            var program = reader.ResolveStream(descriptor?.Get("FontFile2"));
            if (identity && program is not null && OtfLayout.Open(reader.DecodeStream(program)) is { } layout)
                entry.Map = ReadLigatures(layout);
        }
        catch { entry.Map = null; }
        _ligatureCache.AddOrUpdate(fontDict, entry);
        return entry.Map;
    }

    private static Dictionary<int, int[]>? ReadLigatures(OtfLayout layout)
    {
        var table = layout.TableOffset("GSUB");
        if (table < 0) return null;
        var lookupList = table + layout.U16(table + 8);
        var lookups = layout.U16(lookupList);
        var map = new Dictionary<int, int[]>();
        for (var i = 0; i < lookups; i++)
        {
            var (type, _, subtables) = layout.Lookup("GSUB", i);
            if (type != GsubLigatureLookupType) continue;
            foreach (var sub in subtables) ReadLigatureSubtable(layout, sub, map);
        }
        return map.Count > 0 ? map : null;
    }

    /// <summary>Ligature substitution format 1: one ligature set per covered first glyph, each
    /// ligature naming its glyph and the components after the first.</summary>
    private static void ReadLigatureSubtable(OtfLayout l, int sub, Dictionary<int, int[]> map)
    {
        if (l.U16(sub) != 1) return;
        var firsts = CoverageGlyphs(l, sub + l.U16(sub + 2));
        var sets = Math.Min((int)l.U16(sub + 4), firsts.Count);
        for (var s = 0; s < sets; s++)
        {
            var set = sub + l.U16(sub + 6 + s * 2);
            var count = l.U16(set);
            for (var k = 0; k < count; k++)
            {
                var ligature = set + l.U16(set + 2 + k * 2);
                var glyph = l.U16(ligature);
                var components = l.U16(ligature + 2);
                if (components < 2 || map.ContainsKey(glyph)) continue;
                var parts = new int[components];
                parts[0] = firsts[s];
                for (var c = 1; c < components; c++) parts[c] = l.U16(ligature + 2 + c * 2);
                map[glyph] = parts;
            }
        }
    }

    /// <summary>The glyphs of a Coverage table in coverage-index order.</summary>
    private static List<int> CoverageGlyphs(OtfLayout l, int coverage)
    {
        var glyphs = new List<int>();
        var format = l.U16(coverage);
        var count = l.U16(coverage + 2);
        if (format == CoverageGlyphList)
        {
            for (var i = 0; i < count; i++) glyphs.Add(l.U16(coverage + 4 + i * 2));
        }
        else if (format == CoverageRangeList)
        {
            for (var i = 0; i < count; i++)
            {
                var record = coverage + 4 + i * 6;
                for (int g = l.U16(record), end = l.U16(record + 2); g <= end; g++) glyphs.Add(g);
            }
        }
        return glyphs;
    }
}
