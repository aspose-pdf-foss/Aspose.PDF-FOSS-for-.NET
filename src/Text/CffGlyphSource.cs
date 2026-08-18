namespace Aspose.Pdf.Text;

/// <summary>
/// Loads CFF-embedded fonts (PDF <c>/FontFile3</c> with <c>/Type1C</c> or
/// <c>/CIDFontType0C</c>) and serves glyph outlines to the software rasterizer.
/// Bridges CFF's header+INDEX+DICT layout with the renderer-facing
/// <see cref="IGlyphOutlineSource"/> contract; the actual Type 2 CharString
/// execution lives in <see cref="CffType2Interpreter"/>.
///
/// CID-keyed fonts (CIDFontType0C) carry an FDSelect that maps each glyph to
/// one of several Font DICT entries — each FD can own distinct Private DICT
/// values and local subroutines. The source caches per-FD state and looks up
/// the right FD on every <see cref="GetOutline"/> so a single glyph call stays
/// O(log N) in the FDSelect or O(1) for non-CID fonts.
/// </summary>
internal sealed class CffGlyphSource : IGlyphOutlineSource
{
    private readonly byte[] _data;
    private CffParser.IndexInfo _charStrings;
    private CffParser.IndexInfo _globalSubrs;
    private CffParser.IndexInfo _stringIndex; // String INDEX — non-CID fonts only
    private FontDict[] _fonts = Array.Empty<FontDict>();
    private byte[]? _fdSelect; // GID → FD index; null for non-CID fonts
    private Dictionary<int, int>? _cidToGid; // CID → GID, only for CID-keyed fonts
    /// <summary>Glyph-name → GID, populated for non-CID Type1C fonts at parse
    /// time so renderers can resolve PDF /Differences names to outlines.</summary>
    public Dictionary<string, int> NameToGid { get; } = new(StringComparer.Ordinal);
    /// <summary>Byte → GID, populated from the CFF Encoding section (Top DICT
    /// op 16) when present. Used by renderers as a fallback for fonts whose
    /// Charset SIDs don't match the glyph names PDF /Differences references.</summary>
    public int[]? EncodingByteToGid { get; private set; }

    public int UnitsPerEm { get; private set; } = 1000;
    public Dictionary<int, int> CMap { get; } = new();
    public int GlyphCount => _charStrings.count;

    /// <summary>Is this font CID-keyed (subset) CFF? If so, PDF CIDs need
    /// translation to CFF GIDs via the charset before <see cref="GetOutline"/>.</summary>
    public bool IsCidKeyed => _cidToGid is not null;

    /// <summary>Translate a PDF CID to a CFF GID using the font's Charset. Returns
    /// 0 (.notdef) when the CID isn't in this subset. Meaningful only when
    /// <see cref="IsCidKeyed"/> is true.</summary>
    public int CidToGid(int cid) =>
        _cidToGid is not null && _cidToGid.TryGetValue(cid, out var gid) ? gid : 0;

    public int GidForName(string name) => NameToGid.TryGetValue(name, out var gid) ? gid : 0;

    /// <summary>Returns a parsed source, or null when the CFF is malformed /
    /// incomplete. Callers are expected to fall back to system-font resolution.
    /// Accepts both raw CFF data (CIDFontType0C / Type1C) and OpenType containers
    /// with a <c>CFF </c> table (/FontFile3 with /Subtype /OpenType) — the OpenType
    /// wrapper is a 12-byte SFNT header + table directory, and the /CFF / /CFF2
    /// table body is the same data a raw CFF embedding would carry.</summary>
    public static CffGlyphSource? TryLoad(byte[] cffData)
    {
        if (cffData is null || cffData.Length < 4) return null;
        var unwrapped = TryUnwrapOpenType(cffData) ?? cffData;
        var src = new CffGlyphSource(unwrapped);
        if (!src.Parse()) return null;

        // When the input was an OpenType container, borrow its SFNT cmap so simple
        // Type 1C fonts can be rendered against Unicode codepoints.
        if (!ReferenceEquals(unwrapped, cffData))
        {
            var stub = new GlyphOutlineParser(cffData);
            foreach (var kv in stub.CMap)
                src.CMap.TryAdd(kv.Key, kv.Value);
        }

        return src;
    }

    /// <summary>
    /// If <paramref name="data"/> is an OpenType SFNT, extract and return the
    /// <c>CFF </c>/<c>CFF2</c> table body. Returns null when the input is already
    /// raw CFF or when the font has no CFF outlines (glyf-only OpenType).
    /// </summary>
    private static byte[]? TryUnwrapOpenType(byte[] data)
    {
        if (data.Length < 12) return null;
        // SFNT magic: 'OTTO' for CFF-flavoured OpenType, 0x00010000 for TTF-flavoured.
        // Raw CFF begins with (major 1, minor 0, hdrSize 4, offSize N) — a byte 0x01 at
        // offset 0. We only need to unwrap OTTO; TTF-flavoured OpenType has no CFF.
        var magic = (data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3];
        if (magic != 0x4F54544F) return null; // 'OTTO'

        var numTables = (data[4] << 8) | data[5];
        var dirOffset = 12;
        for (var i = 0; i < numTables; i++)
        {
            var entry = dirOffset + i * 16;
            if (entry + 16 > data.Length) return null;
            var tag = (data[entry] << 24) | (data[entry + 1] << 16) |
                      (data[entry + 2] << 8) | data[entry + 3];
            // 'CFF ' = 0x43464620, 'CFF2' = 0x43464632
            if (tag != 0x43464620 && tag != 0x43464632) continue;
            var off = (data[entry + 8] << 24) | (data[entry + 9] << 16) |
                      (data[entry + 10] << 8) | data[entry + 11];
            var len = (data[entry + 12] << 24) | (data[entry + 13] << 16) |
                      (data[entry + 14] << 8) | data[entry + 15];
            if (off < 0 || len <= 0 || (long)off + len > data.Length) return null;
            var body = new byte[len];
            Array.Copy(data, off, body, 0, len);
            return body;
        }
        return null;
    }

    private CffGlyphSource(byte[] data) { _data = data; }

    public GlyphOutline? GetOutline(int glyphId)
    {
        if (glyphId < 0 || glyphId >= _charStrings.count) return null;
        var csData = CffParser.ReadIndexEntry(_data, _charStrings, glyphId);
        if (csData.Length == 0) return null;

        var fd = _fonts[GetFdIndex(glyphId)];
        var interp = new CffType2Interpreter(_data, _globalSubrs, fd.LocalSubrs);
        try { return interp.Run(csData); }
        catch { return null; }
    }

    /// <summary>Advance width in font units: the charstring's own width operand
    /// (nominalWidthX + delta) when it carries one, else the private dict's
    /// defaultWidthX. 0 for an out-of-range or unreadable glyph.</summary>
    public int GetAdvanceWidth(int glyphId)
    {
        if (glyphId < 0 || glyphId >= _charStrings.count) return 0;
        var csData = CffParser.ReadIndexEntry(_data, _charStrings, glyphId);
        if (csData.Length == 0) return 0;
        var fd = _fonts[GetFdIndex(glyphId)];
        var interp = new CffType2Interpreter(_data, _globalSubrs, fd.LocalSubrs);
        try
        {
            interp.Run(csData);
            return interp.WidthDelta is { } d
                ? (int)Math.Round(fd.NominalWidthX + d)
                : fd.DefaultWidthX;
        }
        catch { return 0; }
    }

    private int GetFdIndex(int glyphId)
    {
        if (_fdSelect is null || glyphId < 0 || glyphId >= _fdSelect.Length) return 0;
        return _fdSelect[glyphId];
    }

    // ── Parse ───────────────────────────────────────────────────────────

    private bool Parse()
    {
        var hdrSize = _data[2];
        if (hdrSize > _data.Length) return false;
        var pos = (int)hdrSize;

        // Name INDEX — font name, not needed for rendering.
        pos = SkipIndex(pos);
        if (pos < 0) return false;

        // Top DICT INDEX — the first entry holds everything we care about.
        var topIdx = CffParser.ParseIndex(_data, pos);
        if (topIdx.count == 0) return false;
        pos = topIdx.dataEnd;

        var topDictBytes = CffParser.ReadIndexEntry(_data, topIdx, 0);
        var topDict = CffParser.ParseDict(topDictBytes);

        // String INDEX — glyph names beyond SID 390. Retained so non-CID
        // Type1C parsing can resolve subset-specific names like /Adieresis
        // (Ä) that the spec's standard-strings table doesn't contain.
        _stringIndex = CffParser.ParseIndex(_data, pos);
        pos = SkipIndex(pos);
        if (pos < 0) return false;

        // Global Subr INDEX — needed by callgsubr during glyph interpretation.
        _globalSubrs = CffParser.ParseIndex(_data, pos);

        // Top DICT operator 17 = CharStrings offset (from CFF start).
        var csOffset = DictInt(topDict, 17, 0);
        if (csOffset <= 0 || csOffset >= _data.Length) return false;
        _charStrings = CffParser.ParseIndex(_data, csOffset);
        if (_charStrings.count == 0) return false;

        // FontMatrix (op 12:7) can override the default em-square. Scale is
        // typically 0.001 (1/1000 em) for Type 1 families → UnitsPerEm = 1000.
        if (topDict.TryGetValue(1200 + 7, out var fm) && fm.Count >= 1 && fm[0] > 0)
        {
            var scale = fm[0];
            if (scale > 0) UnitsPerEm = (int)Math.Round(1.0 / scale);
            if (UnitsPerEm <= 0) UnitsPerEm = 1000;
        }

        // CID-keyed? If ROS (op 12:30) is present we need FDSelect + FDArray.
        var isCid = topDict.ContainsKey(1200 + 30);
        if (isCid)
        {
            var fdSelectOffset = DictInt(topDict, 1200 + 37, 0);
            var fdArrayOffset = DictInt(topDict, 1200 + 36, 0);
            if (fdSelectOffset <= 0 || fdArrayOffset <= 0) return false;

            _fdSelect = ParseFdSelect(fdSelectOffset, _charStrings.count);
            _fonts = ParseFdArray(fdArrayOffset);
            if (_fonts.Length == 0 || _fdSelect is null) return false;

            // CID-keyed subsets need the Charset to map PDF CIDs → CFF GIDs.
            // GID 0 is always .notdef; GID 1..N-1 map to the CIDs stored in the
            // charset (which is a list, range-1 table, or range-2 table).
            var charsetOffset = DictInt(topDict, 15, 0);
            _cidToGid = ParseCharsetCidMap(charsetOffset, _charStrings.count);
        }
        else
        {
            // Single Font DICT — Top DICT doubles as the FD.
            _fonts = new[] { BuildFontDict(topDict) };

            // Type 1C subset fonts encode used glyphs via Charset → SID →
            // glyph name. /Differences in the PDF font dict references those
            // same names; without resolving name → GID we have no way to
            // draw text from such fonts (CMap empty, byte-lookup fails).
            // Standard SIDs (≤ 390) come from CffStandardStrings, higher
            // SIDs index into the per-font String INDEX.
            BuildNameAndUnicodeCmap(DictInt(topDict, 15, 0));

            // Also parse the CFF Encoding section (Top DICT op 16) — a direct
            // byte → GID table that subset Type1C fonts often emit so the
            // renderer can map content-stream bytes straight to outlines
            // without round-tripping through glyph names (whose Charset SIDs
            // are sometimes only loosely related to the actual glyphs).
            ParseCffEncoding(DictInt(topDict, 16, 0));
        }

        return true;
    }

    /// <summary>
    /// Parse the Charset (Top DICT op 15) for a non-CID font into a
    /// glyph-name → GID map, then derive a Unicode → GID CMap so that
    /// renderers using the existing simple-text path find embedded outlines
    /// keyed by char rather than glyph name.
    /// </summary>
    private void BuildNameAndUnicodeCmap(int charsetOffset)
    {
        var glyphCount = _charStrings.count;
        if (glyphCount <= 0) return;

        // GID 0 is always .notdef and not encoded in the charset payload —
        // the table only lists SIDs for GID 1..count-1.
        var gidToSid = new int[glyphCount];
        gidToSid[0] = 0;

        if (charsetOffset == 0)
        {
            // Predefined charset 0: ISOAdobe (228 glyphs).
            // GID 1..227 → SID 1..227 directly.
            for (var gid = 1; gid < glyphCount && gid < 228; gid++)
                gidToSid[gid] = gid;
        }
        else if (charsetOffset == 1 || charsetOffset == 2)
        {
            // Predefined Expert / ExpertSubset — uncommon for the subset
            // fonts we care about. Leave gidToSid at zero so the StringIndex
            // path can pick up subset-emitted names instead.
        }
        else if (charsetOffset > 2 && charsetOffset < _data.Length)
        {
            var fmt = _data[charsetOffset];
            var cursor = charsetOffset + 1;
            if (fmt == 0)
            {
                for (var gid = 1; gid < glyphCount; gid++)
                {
                    if (cursor + 2 > _data.Length) break;
                    gidToSid[gid] = (_data[cursor] << 8) | _data[cursor + 1];
                    cursor += 2;
                }
            }
            else if (fmt == 1 || fmt == 2)
            {
                var countSize = fmt == 1 ? 1 : 2;
                var gid = 1;
                while (gid < glyphCount)
                {
                    if (cursor + 2 + countSize > _data.Length) break;
                    var firstSid = (_data[cursor] << 8) | _data[cursor + 1];
                    cursor += 2;
                    int nLeft = countSize == 1
                        ? _data[cursor]
                        : (_data[cursor] << 8) | _data[cursor + 1];
                    cursor += countSize;
                    for (var k = 0; k <= nLeft && gid < glyphCount; k++, gid++)
                        gidToSid[gid] = firstSid + k;
                }
            }
        }

        // Resolve SID → glyph name → optional Unicode → GID mapping.
        for (var gid = 1; gid < glyphCount; gid++)
        {
            var sid = gidToSid[gid];
            if (sid <= 0) continue;
            var name = ResolveSidName(sid);
            if (string.IsNullOrEmpty(name)) continue;
            NameToGid[name!] = gid;
            if (TextAbsorber.GlyphNameToUnicode.TryGetValue(name!, out var u)
                && u.Length > 0)
            {
                CMap.TryAdd(u[0], gid);
            }
        }
    }

    /// <summary>
    /// Parse the optional CFF Encoding section (Top DICT op 16). Format 0 is
    /// a list of byte codes per GID; format 1 is range-based. The resulting
    /// byte → GID table is what subset Type1C fonts typically rely on to
    /// connect content-stream bytes directly to glyph outlines.
    /// </summary>
    private void ParseCffEncoding(int encOffset)
    {
        // Predefined: 0 = StandardEncoding, 1 = ExpertEncoding. We don't
        // synthesise those tables here — both are rarely used by subsets.
        if (encOffset <= 1 || encOffset >= _data.Length) return;

        var fmt = _data[encOffset];
        // The top bit of the format byte signals that a Supplemental Encoding
        // table follows; the format proper is the lower 7 bits.
        var baseFmt = fmt & 0x7F;
        var cursor = encOffset + 1;
        var table = new int[256];

        if (baseFmt == 0)
        {
            if (cursor >= _data.Length) return;
            int nCodes = _data[cursor++];
            for (var gid = 1; gid <= nCodes && cursor < _data.Length; gid++, cursor++)
                table[_data[cursor]] = gid;
        }
        else if (baseFmt == 1)
        {
            if (cursor >= _data.Length) return;
            int nRanges = _data[cursor++];
            var gid = 1;
            for (var r = 0; r < nRanges; r++)
            {
                if (cursor + 2 > _data.Length) return;
                int firstCode = _data[cursor];
                int nLeft = _data[cursor + 1];
                cursor += 2;
                for (var k = 0; k <= nLeft && (firstCode + k) < 256; k++, gid++)
                    table[firstCode + k] = gid;
            }
        }
        else
        {
            return;
        }
        EncodingByteToGid = table;
    }

    /// <summary>
    /// Resolve a CFF SID to its glyph name. SIDs 0..390 are predefined; the
    /// rest index the font's own String INDEX (SID 391 = entry 0).
    /// </summary>
    private string? ResolveSidName(int sid)
    {
        if (sid < CffStandardStrings.Count) return CffStandardStrings.Get(sid);
        var idx = sid - CffStandardStrings.Count;
        if (_stringIndex.count == 0 || idx < 0 || idx >= _stringIndex.count) return null;
        var bytes = CffParser.ReadIndexEntry(_data, _stringIndex, idx);
        return bytes.Length == 0 ? null : System.Text.Encoding.ASCII.GetString(bytes);
    }

    /// <summary>
    /// Parse the CFF Charset (Top DICT op 15) for a CID-keyed font and return a
    /// CID → GID dictionary. Supports the three charset formats from §18 of the
    /// CFF spec: 0 = array of 2-byte CIDs, 1 = ranges with 1-byte count, 2 =
    /// ranges with 2-byte count. Predefined charsets (offset 0, 1, 2) don't
    /// apply to CID-keyed fonts per the spec, so an offset ≤ 2 returns null.
    /// </summary>
    private Dictionary<int, int>? ParseCharsetCidMap(int offset, int glyphCount)
    {
        if (offset <= 2 || offset >= _data.Length) return null;
        var map = new Dictionary<int, int>(glyphCount) { [0] = 0 }; // .notdef
        var fmt = _data[offset];
        var cursor = offset + 1;

        if (fmt == 0)
        {
            // Each entry is a 2-byte CID for GIDs 1..glyphCount-1.
            for (var gid = 1; gid < glyphCount; gid++)
            {
                if (cursor + 2 > _data.Length) return map;
                var cid = (_data[cursor] << 8) | _data[cursor + 1];
                cursor += 2;
                map[cid] = gid;
            }
            return map;
        }
        if (fmt == 1 || fmt == 2)
        {
            var countSize = fmt == 1 ? 1 : 2;
            var gid = 1;
            while (gid < glyphCount)
            {
                if (cursor + 2 + countSize > _data.Length) return map;
                var first = (_data[cursor] << 8) | _data[cursor + 1];
                cursor += 2;
                int nLeft = countSize == 1
                    ? _data[cursor]
                    : (_data[cursor] << 8) | _data[cursor + 1];
                cursor += countSize;
                // Range is (first, first+1, …, first+nLeft) — spans nLeft+1 glyphs.
                for (var k = 0; k <= nLeft && gid < glyphCount; k++, gid++)
                    map[first + k] = gid;
            }
            return map;
        }
        return null;
    }

    private FontDict[] ParseFdArray(int offset)
    {
        if (offset <= 0 || offset >= _data.Length) return Array.Empty<FontDict>();
        var idx = CffParser.ParseIndex(_data, offset);
        if (idx.count == 0) return Array.Empty<FontDict>();

        var result = new FontDict[idx.count];
        for (var i = 0; i < idx.count; i++)
        {
            var dictBytes = CffParser.ReadIndexEntry(_data, idx, i);
            var dict = CffParser.ParseDict(dictBytes);
            result[i] = BuildFontDict(dict);
        }
        return result;
    }

    private FontDict BuildFontDict(Dictionary<int, List<double>> dict)
    {
        var fd = new FontDict();
        if (dict.TryGetValue(18, out var priv) && priv.Count >= 2)
        {
            var privSize = (int)priv[0];
            var privOffset = (int)priv[1];
            if (privSize > 0 && privOffset > 0 && privOffset + privSize <= _data.Length)
            {
                var privBytes = new byte[privSize];
                Array.Copy(_data, privOffset, privBytes, 0, privSize);
                var privDict = CffParser.ParseDict(privBytes);
                fd.DefaultWidthX = DictInt(privDict, 20, 0);
                fd.NominalWidthX = DictInt(privDict, 21, 0);

                // Private DICT op 19 = offset of local Subr INDEX *relative to
                // the Private DICT start*.
                var subrsRel = DictInt(privDict, 19, 0);
                if (subrsRel > 0 && privOffset + subrsRel < _data.Length)
                    fd.LocalSubrs = CffParser.ParseIndex(_data, privOffset + subrsRel);
            }
        }
        return fd;
    }

    // FDSelect (§19) comes in two encodings: format 0 is a raw byte per glyph,
    // format 3 is run-length ranges. Both flatten to a GID → FD-index array.
    private byte[]? ParseFdSelect(int offset, int glyphCount)
    {
        if (offset <= 0 || offset >= _data.Length) return null;
        var fmt = _data[offset];
        var result = new byte[glyphCount];

        if (fmt == 0)
        {
            if (offset + 1 + glyphCount > _data.Length) return null;
            Array.Copy(_data, offset + 1, result, 0, glyphCount);
            return result;
        }
        if (fmt == 3)
        {
            if (offset + 3 > _data.Length) return null;
            var nRanges = (_data[offset + 1] << 8) | _data[offset + 2];
            var cursor = offset + 3;
            var prevFirst = 0;
            var prevFd = (byte)0;
            for (var r = 0; r < nRanges; r++)
            {
                if (cursor + 3 > _data.Length) return null;
                var first = (_data[cursor] << 8) | _data[cursor + 1];
                var fd = _data[cursor + 2];
                cursor += 3;
                if (r > 0)
                {
                    for (var g = prevFirst; g < first && g < glyphCount; g++) result[g] = prevFd;
                }
                prevFirst = first;
                prevFd = fd;
            }
            if (cursor + 2 > _data.Length) return null;
            var sentinel = (_data[cursor] << 8) | _data[cursor + 1];
            for (var g = prevFirst; g < sentinel && g < glyphCount; g++) result[g] = prevFd;
            return result;
        }
        return null; // Unknown FDSelect format.
    }

    private int SkipIndex(int pos)
    {
        var idx = CffParser.ParseIndex(_data, pos);
        if (idx.count == 0 && idx.dataEnd == pos + 2)
            return pos + 2; // empty INDEX is 2 bytes of zero
        return idx.dataEnd;
    }

    private static int DictInt(Dictionary<int, List<double>> dict, int op, int defaultValue)
    {
        if (dict.TryGetValue(op, out var vs) && vs.Count > 0)
            return (int)vs[0];
        return defaultValue;
    }

    private sealed class FontDict
    {
        public int DefaultWidthX;
        public int NominalWidthX;
        public CffParser.IndexInfo LocalSubrs;
    }
}
