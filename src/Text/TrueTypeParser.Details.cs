using System.Text;

namespace Aspose.Pdf.Text;

/// <summary>
/// The rest of what a font program says about itself, read on demand: the glyph count, the
/// widest advance, every OS/2 field a PDF font descriptor or a layout engine asks for, every
/// record of the name table, each glyph's bounding box, the kerning pairs of the kern table,
/// and any cmap subtable by its platform and encoding.
/// </summary>
internal sealed partial class TrueTypeParser
{
    /// <summary>A record of the name table: its platform, encoding, language and name ids, and its text.</summary>
    internal readonly record struct NameRecord(int PlatformId, int EncodingId, int LanguageId, int NameId, string Value);

    private List<NameRecord>? _nameRecords;
    private Dictionary<(int Left, int Right), int>? _kernPairs;
    private List<(int PlatformId, int EncodingId)>? _cmapSubtables;

    // ── maxp / hhea / head ─────────────────────────────────────────

    /// <summary>How many glyphs the font has (maxp); 0 when the table is missing.</summary>
    public int NumGlyphs => Table("maxp") is { } t && t.offset + 6 <= _data.Length ? ReadUInt16(t.offset + 4) : 0;

    /// <summary>The widest advance of any glyph, in font units (hhea).</summary>
    public int AdvanceWidthMax => Table("hhea") is { } t && t.offset + 12 <= _data.Length ? ReadUInt16(t.offset + 10) : 0;

    /// <summary>Whether glyph offsets in loca are long (1) or short (0) (head).</summary>
    public int IndexToLocFormat => Table("head") is { } t && t.offset + 52 <= _data.Length ? ReadInt16(t.offset + 50) : 0;

    // ── OS/2 ──────────────────────────────────────────────────────

    /// <summary>Whether the font has an OS/2 table.</summary>
    public bool HasOs2 => Table("OS/2") is not null;

    /// <summary>The OS/2 table version; -1 when there is no table.</summary>
    public int Os2Version => Os2UInt16(0, -1);

    /// <summary>The average advance of the lower-case letters, in font units (OS/2 xAvgCharWidth).</summary>
    public int AvgCharWidth => Os2Int16(2);

    /// <summary>The width class, 1 (ultra-condensed) to 9 (ultra-expanded); 5 is normal.</summary>
    public int WidthClass => Os2UInt16(6, 5);

    /// <summary>The embedding licensing flags (OS/2 fsType).</summary>
    public int FsType => Os2UInt16(8, 0);

    /// <summary>The subscript's vertical size, in font units.</summary>
    public int SubscriptYSize => Os2Int16(12);

    /// <summary>The subscript's vertical offset below the baseline, in font units.</summary>
    public int SubscriptYOffset => Os2Int16(16);

    /// <summary>The superscript's vertical size, in font units.</summary>
    public int SuperscriptYSize => Os2Int16(20);

    /// <summary>The superscript's vertical offset above the baseline, in font units.</summary>
    public int SuperscriptYOffset => Os2Int16(24);

    /// <summary>The strikeout stroke's thickness, in font units.</summary>
    public int StrikeoutSize => Os2Int16(26);

    /// <summary>The strikeout stroke's position above the baseline, in font units.</summary>
    public int StrikeoutPosition => Os2Int16(28);

    /// <summary>The font family class and subclass (OS/2 sFamilyClass).</summary>
    public int FamilyClass => Os2Int16(30);

    /// <summary>The ten PANOSE classification bytes; empty when there is no OS/2 table.</summary>
    public byte[] Panose => Table("OS/2") is { } t && t.offset + 42 <= _data.Length ? _data[(t.offset + 32)..(t.offset + 42)] : [];

    /// <summary>The style flags (OS/2 fsSelection).</summary>
    public int FsSelection => Os2UInt16(62, 0);

    /// <summary>
    /// The code page ranges the font covers: bits 0-31 and 32-63 (OS/2 version 1 and later);
    /// both 0 when the table is older.
    /// </summary>
    public (uint Range1, uint Range2) CodePageRanges =>
        Table("OS/2") is { } t && Os2Version >= 1 && t.offset + 86 <= _data.Length
            ? (ReadUInt32(t.offset + 78), ReadUInt32(t.offset + 82))
            : (0u, 0u);

    // ── name ──────────────────────────────────────────────────────

    /// <summary>
    /// Every record of the name table, in table order: Windows and Unicode records decoded
    /// from UTF-16, Macintosh records from Mac Roman.
    /// </summary>
    public IReadOnlyList<NameRecord> NameRecords => _nameRecords ??= ReadNameRecords();

    private List<NameRecord> ReadNameRecords()
    {
        var records = new List<NameRecord>();
        if (Table("name") is not { } t) return records;
        var o = t.offset;
        if (o + 6 > _data.Length) return records;
        var count = ReadUInt16(o + 2);
        var strings = ReadUInt16(o + 4) + o;
        for (var i = 0; i < count; i++)
        {
            var record = o + 6 + i * 12;
            if (record + 12 > _data.Length) break;
            var platform = ReadUInt16(record);
            var encoding = ReadUInt16(record + 2);
            var language = ReadUInt16(record + 4);
            var nameId = ReadUInt16(record + 6);
            var length = ReadUInt16(record + 8);
            var offset = ReadUInt16(record + 10) + strings;
            if (offset + length > _data.Length) continue;
            var value = platform is 0 or 3 || (platform == 2 && encoding == 1)
                ? Encoding.BigEndianUnicode.GetString(_data, offset, length & ~1)
                : MacRoman(offset, length);
            records.Add(new NameRecord(platform, encoding, language, nameId, value));
        }
        return records;
    }

    /// <summary>Mac Roman text: the ASCII half as is, the upper half through the standard table.</summary>
    private string MacRoman(int offset, int length)
    {
        var text = new StringBuilder(length);
        for (var i = 0; i < length; i++)
        {
            var b = _data[offset + i];
            text.Append(b < 0x80 ? (char)b : MacRomanUpper[b - 0x80]);
        }
        return text.ToString();
    }

    /// <summary>Mac Roman bytes 0x80-0xFF as Unicode.</summary>
    private const string MacRomanUpper =
        "ÄÅÇÉÑÖÜáàâäãåçéèêëíìîïñóòôöõúùûü†°¢£§•¶ß®©™´¨≠ÆØ∞±≤≥¥µ∂∑∏π∫ªºΩæø¿¡¬√ƒ≈∆«»… ÀÃÕŒœ–—“”‘’÷◊ÿŸ⁄€‹›ﬁﬂ‡·‚„‰ÂÊÁËÈÍÎÏÌÓÔÒÚÛÙıˆ˜¯˘˙˚¸˝˛ˇ";

    // ── glyf ──────────────────────────────────────────────────────

    /// <summary>
    /// A glyph's bounding box [xMin, yMin, xMax, yMax] in font units, from its glyf header;
    /// null for a glyph with no outline or a font without glyf/loca.
    /// </summary>
    public int[]? GetGlyphBBox(int glyphId)
    {
        if (Table("loca") is not { } loca || Table("glyf") is not { } glyf) return null;
        if (glyphId < 0 || glyphId >= NumGlyphs) return null;
        int start, end;
        if (IndexToLocFormat == 0)
        {
            var at = loca.offset + glyphId * 2;
            if (at + 4 > _data.Length) return null;
            start = ReadUInt16(at) * 2;
            end = ReadUInt16(at + 2) * 2;
        }
        else
        {
            var at = loca.offset + glyphId * 4;
            if (at + 8 > _data.Length) return null;
            start = (int)ReadUInt32(at);
            end = (int)ReadUInt32(at + 4);
        }
        if (end <= start) return null;
        var header = glyf.offset + start;
        if (header + 10 > _data.Length) return null;
        return [ReadInt16(header + 2), ReadInt16(header + 4), ReadInt16(header + 6), ReadInt16(header + 8)];
    }

    // ── kern ──────────────────────────────────────────────────────

    /// <summary>
    /// The kerning pairs of the kern table's horizontal format-0 subtables, by left and right
    /// glyph id, in font units. Empty when the font has no kern table.
    /// </summary>
    public IReadOnlyDictionary<(int Left, int Right), int> KernPairs => _kernPairs ??= ReadKernPairs();

    private Dictionary<(int, int), int> ReadKernPairs()
    {
        var pairs = new Dictionary<(int, int), int>();
        if (Table("kern") is not { } t || t.offset + 4 > _data.Length) return pairs;
        var o = t.offset;
        var tables = ReadUInt16(o + 2);
        var p = o + 4;
        for (var i = 0; i < tables && p + 6 <= _data.Length; i++)
        {
            var length = ReadUInt16(p + 2);
            var coverage = ReadUInt16(p + 4);
            var format = coverage >> 8;
            var horizontal = (coverage & 1) != 0;
            if (format == 0 && horizontal && p + 14 <= _data.Length)
            {
                var count = ReadUInt16(p + 6);
                var entries = p + 14;
                for (var k = 0; k < count && entries + k * 6 + 6 <= _data.Length; k++)
                {
                    var at = entries + k * 6;
                    pairs[(ReadUInt16(at), ReadUInt16(at + 2))] = ReadInt16(at + 4);
                }
            }
            if (length == 0) break;
            p += length;
        }
        return pairs;
    }

    // ── cmap ──────────────────────────────────────────────────────

    /// <summary>The cmap subtables the font carries, by platform and encoding, in table order.</summary>
    public IReadOnlyList<(int PlatformId, int EncodingId)> CMapSubtables => _cmapSubtables ??= ReadCMapSubtableIds();

    private List<(int, int)> ReadCMapSubtableIds()
    {
        var ids = new List<(int, int)>();
        if (Table("cmap") is not { } t || t.offset + 4 > _data.Length) return ids;
        var count = ReadUInt16(t.offset + 2);
        for (var i = 0; i < count; i++)
        {
            var entry = t.offset + 4 + i * 8;
            if (entry + 8 > _data.Length) break;
            ids.Add((ReadUInt16(entry), ReadUInt16(entry + 2)));
        }
        return ids;
    }

    /// <summary>
    /// The code to glyph id map of the subtable with the platform and encoding; null when the
    /// font has none or it is in a shape this reader cannot follow. Codes of a format-12
    /// subtable keep their full range.
    /// </summary>
    public Dictionary<int, int>? ReadCMapSubtable(int platformId, int encodingId)
    {
        if (Table("cmap") is not { } t || t.offset + 4 > _data.Length) return null;
        var count = ReadUInt16(t.offset + 2);
        for (var i = 0; i < count; i++)
        {
            var entry = t.offset + 4 + i * 8;
            if (entry + 8 > _data.Length) break;
            if (ReadUInt16(entry) != platformId || ReadUInt16(entry + 2) != encodingId) continue;
            var subtable = (int)ReadUInt32(entry + 4) + t.offset;
            if (!IsReadableSubtable(subtable)) return null;
            return ReadSubtable(subtable);
        }
        return null;
    }

    /// <summary>One subtable read on its own, into a map of its own, the parser's chosen map left as it is.</summary>
    private Dictionary<int, int> ReadSubtable(int offset)
    {
        var saved = new Dictionary<int, int>(CMap);
        CMap.Clear();
        var format = ReadUInt16(offset);
        if (format == 0) ParseCMapFormat0(offset);
        else if (format == 4) ParseCMapFormat4(offset);
        else if (format == 6) ParseCMapFormat6(offset);
        else if (format == 12) ReadFullFormat12(offset);
        var read = new Dictionary<int, int>(CMap);
        CMap.Clear();
        foreach (var (code, glyph) in saved) CMap[code] = glyph;
        return read;
    }

    /// <summary>A format-12 subtable over its whole range, supplementary planes included.</summary>
    private void ReadFullFormat12(int offset)
    {
        if (offset + 16 > _data.Length) return;
        var groups = offset + 16;
        var count = (int)Math.Min(ReadUInt32(offset + 12), (uint)Math.Max((_data.Length - groups) / 12, 0));
        for (var i = 0; i < count; i++)
        {
            var group = groups + i * 12;
            var start = ReadUInt32(group);
            var end = ReadUInt32(group + 4);
            var glyph = (int)ReadUInt32(group + 8);
            if (end > 0x10FFFF || end < start) continue;
            for (var c = start; c <= end; c++) CMap[(int)c] = glyph + (int)(c - start);
        }
    }

    // ── helpers ───────────────────────────────────────────────────

    private (int offset, int length)? Table(string tag) => _tables.TryGetValue(tag, out var t) ? t : null;

    private int Os2Int16(int field) => Table("OS/2") is { } t && t.offset + field + 2 <= _data.Length ? ReadInt16(t.offset + field) : 0;

    private int Os2UInt16(int field, int missing) => Table("OS/2") is { } t && t.offset + field + 2 <= _data.Length ? ReadUInt16(t.offset + field) : missing;
}
