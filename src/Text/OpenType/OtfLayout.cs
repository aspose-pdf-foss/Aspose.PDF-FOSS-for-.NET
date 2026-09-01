namespace Aspose.Pdf.Text.OpenType;

/// <summary>
/// The shared plumbing of the OpenType layout tables (GSUB/GPOS/GDEF): the font's
/// table directory, and the Coverage / ClassDef / ScriptList / FeatureList / LookupList
/// structures they are all built out of (OpenType spec, chapter 5).
/// <para>
/// This exists because a script like Devanagari cannot be drawn from the cmap alone:
/// its conjuncts and its reph are GLYPHS THE FONT SUBSTITUTES IN, reachable only
/// through GSUB. Latin and Arabic get by without it — Latin needs no substitution and
/// Arabic has precomposed Unicode presentation forms — which is why the font layer had
/// no layout-table reader before.
/// </para>
/// </summary>
internal sealed class OtfLayout
{
    private readonly byte[] _data;

    private OtfLayout(byte[] data) { _data = data; }

    /// <summary>Table tag → (offset, length) for the whole font.</summary>
    private readonly Dictionary<string, (int offset, int length)> _tables = new(StringComparer.Ordinal);

    /// <summary>Parse a font's table directory. Returns null when the bytes are not a
    /// readable sfnt (a caller then falls back to unshaped output).</summary>
    internal static OtfLayout? Open(byte[] data)
    {
        if (data is null || data.Length < 12) return null;
        var layout = new OtfLayout(data);
        try
        {
            var baseOff = 0;
            // A TrueType Collection: take the first font's directory.
            if (data[0] == (byte)'t' && data[1] == (byte)'t' && data[2] == (byte)'c' && data[3] == (byte)'f')
            {
                if (data.Length < 16) return null;
                baseOff = (int)layout.U32(12);
                if (baseOff < 0 || baseOff + 12 > data.Length) return null;
            }
            var numTables = layout.U16(baseOff + 4);
            for (var i = 0; i < numTables; i++)
            {
                var rec = baseOff + 12 + i * 16;
                if (rec + 16 > data.Length) break;
                var tag = System.Text.Encoding.ASCII.GetString(data, rec, 4);
                var off = (int)layout.U32(rec + 8);
                var len = (int)layout.U32(rec + 12);
                if (off >= 0 && off < data.Length) layout._tables[tag] = (off, len);
            }
        }
        catch { return null; }
        return layout;
    }

    internal bool Has(string tag) => _tables.ContainsKey(tag);
    internal int TableOffset(string tag) => _tables.TryGetValue(tag, out var t) ? t.offset : -1;

    // ── primitive reads ───────────────────────────────────────────────────────
    internal ushort U16(int p) => p + 1 < _data.Length ? (ushort)((_data[p] << 8) | _data[p + 1]) : (ushort)0;
    internal short S16(int p) => (short)U16(p);
    internal uint U32(int p) => p + 3 < _data.Length
        ? ((uint)_data[p] << 24) | ((uint)_data[p + 1] << 16) | ((uint)_data[p + 2] << 8) | _data[p + 3]
        : 0u;

    // ── Coverage (spec 5.1) ───────────────────────────────────────────────────
    /// <summary>Index of <paramref name="glyph"/> within a Coverage table, or -1 when the
    /// table does not cover it. The index is what every lookup uses to find its data.</summary>
    internal int CoverageIndex(int coverageOffset, ushort glyph)
    {
        if (coverageOffset <= 0 || coverageOffset >= _data.Length) return -1;
        var format = U16(coverageOffset);
        if (format == 1)
        {
            var count = U16(coverageOffset + 2);
            // sorted list of glyph ids
            int lo = 0, hi = count - 1;
            while (lo <= hi)
            {
                var mid = (lo + hi) / 2;
                var g = U16(coverageOffset + 4 + mid * 2);
                if (g == glyph) return mid;
                if (g < glyph) lo = mid + 1; else hi = mid - 1;
            }
            return -1;
        }
        if (format == 2)
        {
            var count = U16(coverageOffset + 2);
            int lo = 0, hi = count - 1;
            while (lo <= hi)
            {
                var mid = (lo + hi) / 2;
                var rec = coverageOffset + 4 + mid * 6;
                var start = U16(rec);
                var end = U16(rec + 2);
                if (glyph < start) { hi = mid - 1; continue; }
                if (glyph > end) { lo = mid + 1; continue; }
                return U16(rec + 4) + (glyph - start);
            }
        }
        return -1;
    }

    // ── ClassDef (spec 5.2) ───────────────────────────────────────────────────
    /// <summary>The class a glyph belongs to in a ClassDef table (0 when unlisted).</summary>
    internal int ClassOf(int classDefOffset, ushort glyph)
    {
        if (classDefOffset <= 0 || classDefOffset >= _data.Length) return 0;
        var format = U16(classDefOffset);
        if (format == 1)
        {
            var start = U16(classDefOffset + 2);
            var count = U16(classDefOffset + 4);
            var idx = glyph - start;
            return idx >= 0 && idx < count ? U16(classDefOffset + 6 + idx * 2) : 0;
        }
        if (format == 2)
        {
            var count = U16(classDefOffset + 2);
            int lo = 0, hi = count - 1;
            while (lo <= hi)
            {
                var mid = (lo + hi) / 2;
                var rec = classDefOffset + 4 + mid * 6;
                var s = U16(rec);
                var e = U16(rec + 2);
                if (glyph < s) { hi = mid - 1; continue; }
                if (glyph > e) { lo = mid + 1; continue; }
                return U16(rec + 4);
            }
        }
        return 0;
    }

    // ── Script / Feature / Lookup lists (spec 5.1) ────────────────────────────
    /// <summary>
    /// The lookup indices a feature contributes, for one script, in table order. Features
    /// are looked up by TAG under the script's default language system; a script the font
    /// does not list yields nothing (the caller then leaves the run unshaped, which is the
    /// right answer — the font has no rules for it).
    /// </summary>
    internal List<int> LookupsForFeature(string tableTag, string scriptTag, string featureTag)
    {
        var result = new List<int>();
        var table = TableOffset(tableTag);
        if (table < 0) return result;

        var scriptListOff = table + U16(table + 4);
        var featureListOff = table + U16(table + 6);

        // find the script
        var scriptCount = U16(scriptListOff);
        var scriptOff = -1;
        for (var i = 0; i < scriptCount; i++)
        {
            var rec = scriptListOff + 2 + i * 6;
            var tag = System.Text.Encoding.ASCII.GetString(_data, rec, 4);
            if (tag == scriptTag) { scriptOff = scriptListOff + U16(rec + 4); break; }
        }
        if (scriptOff < 0) return result;

        // default language system (the Indic scripts in practice use it)
        var defLangSys = U16(scriptOff);
        if (defLangSys == 0) return result;
        var langSysOff = scriptOff + defLangSys;

        var featureCount = U16(langSysOff + 4);
        for (var i = 0; i < featureCount; i++)
        {
            var featureIndex = U16(langSysOff + 6 + i * 2);
            var frec = featureListOff + 2 + featureIndex * 6;
            var tag = System.Text.Encoding.ASCII.GetString(_data, frec, 4);
            if (tag != featureTag) continue;
            var featureOff = featureListOff + U16(frec + 4);
            var lookupCount = U16(featureOff + 2);
            for (var j = 0; j < lookupCount; j++)
                result.Add(U16(featureOff + 4 + j * 2));
        }
        return result;
    }

    /// <summary>Every script tag the table lists — lets a caller ask whether the font has
    /// any rules for the script at all before doing the work.</summary>
    internal HashSet<string> Scripts(string tableTag)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        var table = TableOffset(tableTag);
        if (table < 0) return set;
        var scriptListOff = table + U16(table + 4);
        var count = U16(scriptListOff);
        for (var i = 0; i < count; i++)
            set.Add(System.Text.Encoding.ASCII.GetString(_data, scriptListOff + 2 + i * 6, 4));
        return set;
    }

    /// <summary>Offset and type of one lookup in a table's LookupList, with the extension
    /// indirection (type 7 in GSUB / type 9 in GPOS) already resolved.</summary>
    internal (int type, int flag, List<int> subtables) Lookup(string tableTag, int index)
    {
        var subtables = new List<int>();
        var table = TableOffset(tableTag);
        if (table < 0) return (0, 0, subtables);
        var lookupListOff = table + U16(table + 8);
        var count = U16(lookupListOff);
        if (index < 0 || index >= count) return (0, 0, subtables);
        var lookupOff = lookupListOff + U16(lookupListOff + 2 + index * 2);
        var type = U16(lookupOff);
        var flag = U16(lookupOff + 2);
        var subCount = U16(lookupOff + 4);
        var extensionType = tableTag == "GSUB" ? 7 : 9;
        for (var i = 0; i < subCount; i++)
        {
            var sub = lookupOff + U16(lookupOff + 6 + i * 2);
            if (type == extensionType)
            {
                // Extension subtable: format 1, then the real type and a 32-bit offset.
                var realType = U16(sub + 2);
                var realOff = sub + (int)U32(sub + 4);
                type = realType;
                subtables.Add(realOff);
                continue;
            }
            subtables.Add(sub);
        }
        return (type, flag, subtables);
    }
}
