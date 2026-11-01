using System.Text;

namespace Aspose.Pdf.Text;

internal sealed partial class GlyphOutlineParser
{
    private Dictionary<string, int>? _postNames;

    /// <summary>True when the program's post table names its glyphs (format 1.0: the
    /// Macintosh order; format 2.0: its own index table). A format 3.0 post carries no
    /// names, and so does a program without one.</summary>
    public bool HasPostNames => _postNames is { Count: > 0 };

    /// <summary>The glyph the post table gives a name, or 0 when the table lacks it.</summary>
    public int GidForPostName(string name)
        => _postNames is not null && _postNames.TryGetValue(name, out var gid) ? gid : 0;

    private const uint PostFormat1 = 0x00010000;
    private const uint PostFormat2 = 0x00020000;
    private const int PostHeaderLength = 32;
    private const int MacGlyphNameCount = 258;

    /// <summary>Read the glyph names of the post table (OpenType §post): format 1.0 names the
    /// first 258 glyphs in the Macintosh order; format 2.0 gives each glyph an index into that
    /// order or, from 258 on, into its own string pool. The first glyph of a name keeps it.</summary>
    private void ParsePost()
    {
        if (!_tables.TryGetValue("post", out var t)) return;
        var o = t.offset;
        if (o + PostHeaderLength > _data.Length) return;
        var version = ReadUInt32(o);
        if (version == PostFormat1)
        {
            _postNames = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var g = 1; g < Math.Min(_numGlyphs, MacGlyphNameCount); g++)
                _postNames.TryAdd(TrueTypeParser.MacGlyphNames[g], g);
            return;
        }
        if (version != PostFormat2 || o + PostHeaderLength + 2 > _data.Length) return;
        var numGlyphs = ReadUInt16(o + PostHeaderLength);
        var indexOffset = o + PostHeaderLength + 2;
        if (indexOffset + numGlyphs * 2 > _data.Length) return;
        var pool = new List<string>();
        var p = indexOffset + numGlyphs * 2;
        var tableEnd = Math.Min(o + t.length, _data.Length);
        while (p < tableEnd)
        {
            int len = _data[p];
            if (p + 1 + len > _data.Length) break;
            pool.Add(Encoding.ASCII.GetString(_data, p + 1, len));
            p += 1 + len;
        }
        _postNames = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var g = 1; g < numGlyphs; g++)
        {
            var idx = ReadUInt16(indexOffset + g * 2);
            var name = idx < MacGlyphNameCount
                ? TrueTypeParser.MacGlyphNames[idx]
                : idx - MacGlyphNameCount < pool.Count ? pool[idx - MacGlyphNameCount] : null;
            if (!string.IsNullOrEmpty(name) && name != ".notdef") _postNames.TryAdd(name, g);
        }
    }
}
