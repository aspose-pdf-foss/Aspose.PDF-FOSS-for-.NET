using System.Text;

namespace Aspose.Pdf.Text;

/// <summary>
/// Minimal TrueType/OpenType font parser that extracts metadata needed for PDF font embedding.
/// Reads: head, hhea, OS/2, post, name, cmap, hmtx tables.
/// </summary>
internal sealed class TrueTypeParser
{
    private readonly byte[] _data;
    private readonly Dictionary<string, (int offset, int length)> _tables = new();

    public TrueTypeParser(byte[] fontData)
    {
        _data = fontData;
        ParseTableDirectory();
    }

    public byte[] FontData => _data;

    // ── head table ──────────────────────────────────────────────────

    /// <summary>Units per em (head table).</summary>
    public int UnitsPerEm { get; private set; } = 1000;

    /// <summary>Font bounding box [xMin, yMin, xMax, yMax] in font units.</summary>
    public int[] BBox { get; private set; } = [0, 0, 0, 0];

    /// <summary>Mac style flags (head table).</summary>
    public int MacStyle { get; private set; }

    // ── hhea table ──────────────────────────────────────────────────

    public int Ascent { get; private set; }
    public int Descent { get; private set; }
    public int LineGap { get; private set; }
    public int NumberOfHMetrics { get; private set; }

    // ── OS/2 table ──────────────────────────────────────────────────

    public int WeightClass { get; private set; } = 400;
    public int CapHeight { get; private set; }
    public int STypoAscender { get; private set; }
    public int STypoDescender { get; private set; }
    public int STypoLineGap { get; private set; }
    public int UsWinAscent { get; private set; }
    public int UsWinDescent { get; private set; }
    public int SxHeight { get; private set; }

    // ── post table ──────────────────────────────────────────────────

    public double ItalicAngle { get; private set; }
    public bool IsFixedPitch { get; private set; }
    /// <summary>Underline position below baseline in font units (negative = below).</summary>
    public int UnderlinePosition { get; private set; }
    /// <summary>Underline thickness in font units.</summary>
    public int UnderlineThickness { get; private set; }

    // ── name table ──────────────────────────────────────────────────

    public string FamilyName { get; private set; } = "Unknown";

    /// <summary>Name-table subfamily (style) name — "Regular", "Bold Italic", ….</summary>
    public string? SubfamilyName { get; private set; }
    public string PostScriptName { get; private set; } = "Unknown";

    // ── cmap + hmtx ─────────────────────────────────────────────────

    /// <summary>Character code → glyph ID mapping (from cmap).</summary>
    public Dictionary<int, int> CMap { get; } = new();

    /// <summary>Glyph ID → PostScript glyph name (from a version-2.0 post table).
    /// Empty when the font has no post names (version 1.0/3.0). Used to recover
    /// Unicode for embedded subset fonts that carry neither /ToUnicode nor an
    /// /Encoding — the name maps back to Unicode through the Adobe Glyph List.</summary>
    public Dictionary<int, string> GlyphNames { get; } = new();

    /// <summary>Glyph widths in font units (from hmtx).</summary>
    public int[] GlyphWidths { get; private set; } = [];

    /// <summary>
    /// Parse all required tables.
    /// </summary>
    public void Parse()
    {
        ParseHead();
        ParseHhea();
        ParseOS2();
        ParsePost();
        ParseName();
        ParseCMap();
        ParseHmtx();
    }

    /// <summary>Get the width of a character code in font units.</summary>
    public int GetCharWidth(int charCode)
    {
        if (CMap.TryGetValue(charCode, out var glyphId) && glyphId < GlyphWidths.Length)
            return GlyphWidths[glyphId];
        return GlyphWidths.Length > 0 ? GlyphWidths[0] : 0;
    }

    /// <summary>
    /// PDF font flags (§9.8.2, Table 123).
    /// </summary>
    public int GetPdfFlags()
    {
        var flags = 0;
        if (IsFixedPitch) flags |= 1;         // FixedPitch
        flags |= (1 << 5);                     // Nonsymbolic (assume Latin)
        if (ItalicAngle != 0) flags |= (1 << 6); // Italic
        return flags;
    }

    #region Table directory

    private void ParseTableDirectory()
    {
        if (_data.Length < 12) return;

        // TrueType Collection ('ttcf'): the file starts with a TTC header, not
        // an sfnt table directory. The first embedded font's offset table lives
        // at the first entry of the TTC offset array (byte 12); rebase there.
        // Table offsets in the directory are absolute, so nothing else moves.
        var baseOff = 0;
        if (_data.Length >= 16 && _data[0] == (byte)'t' && _data[1] == (byte)'t'
            && _data[2] == (byte)'c' && _data[3] == (byte)'f')
        {
            baseOff = (int)ReadUInt32(12);
            if (baseOff < 0 || baseOff + 12 > _data.Length) return;
        }

        var numTables = ReadUInt16(baseOff + 4);
        for (var i = 0; i < numTables; i++)
        {
            var entryOffset = baseOff + 12 + i * 16;
            if (entryOffset + 16 > _data.Length) break;

            var tag = Encoding.ASCII.GetString(_data, entryOffset, 4);
            var offset = (int)ReadUInt32(entryOffset + 8);
            var length = (int)ReadUInt32(entryOffset + 12);
            _tables[tag] = (offset, length);
        }
    }

    /// <summary>The 258 standard Macintosh glyph names (post table format 2.0,
    /// OpenType §post). Glyph-name indices below 258 reference this set.</summary>
    private static readonly string[] MacGlyphNames =
    [
        ".notdef", ".null", "nonmarkingreturn", "space", "exclam", "quotedbl",
        "numbersign", "dollar", "percent", "ampersand", "quotesingle", "parenleft",
        "parenright", "asterisk", "plus", "comma", "hyphen", "period", "slash",
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight",
        "nine", "colon", "semicolon", "less", "equal", "greater", "question", "at",
        "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O",
        "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z", "bracketleft",
        "backslash", "bracketright", "asciicircum", "underscore", "grave", "a", "b",
        "c", "d", "e", "f", "g", "h", "i", "j", "k", "l", "m", "n", "o", "p", "q",
        "r", "s", "t", "u", "v", "w", "x", "y", "z", "braceleft", "bar",
        "braceright", "asciitilde", "Adieresis", "Aring", "Ccedilla", "Eacute",
        "Ntilde", "Odieresis", "Udieresis", "aacute", "agrave", "acircumflex",
        "adieresis", "atilde", "aring", "ccedilla", "eacute", "egrave",
        "ecircumflex", "edieresis", "iacute", "igrave", "icircumflex", "idieresis",
        "ntilde", "oacute", "ograve", "ocircumflex", "odieresis", "otilde", "uacute",
        "ugrave", "ucircumflex", "udieresis", "dagger", "degree", "cent", "sterling",
        "section", "bullet", "paragraph", "germandbls", "registered", "copyright",
        "trademark", "acute", "dieresis", "notequal", "AE", "Oslash", "infinity",
        "plusminus", "lessequal", "greaterequal", "yen", "mu", "partialdiff",
        "summation", "product", "pi", "integral", "ordfeminine", "ordmasculine",
        "Omega", "ae", "oslash", "questiondown", "exclamdown", "logicalnot",
        "radical", "florin", "approxequal", "Delta", "guillemotleft",
        "guillemotright", "ellipsis", "nonbreakingspace", "Agrave", "Atilde",
        "Otilde", "OE", "oe", "endash", "emdash", "quotedblleft", "quotedblright",
        "quoteleft", "quoteright", "divide", "lozenge", "ydieresis", "Ydieresis",
        "fraction", "currency", "guilsinglleft", "guilsinglright", "fi", "fl",
        "daggerdbl", "periodcentered", "quotesinglbase", "quotedblbase",
        "perthousand", "Acircumflex", "Ecircumflex", "Aacute", "Edieresis", "Egrave",
        "Iacute", "Icircumflex", "Idieresis", "Igrave", "Oacute", "Ocircumflex",
        "apple", "Ograve", "Uacute", "Ucircumflex", "Ugrave", "dotlessi",
        "circumflex", "tilde", "macron", "breve", "dotaccent", "ring", "cedilla",
        "hungarumlaut", "ogonek", "caron", "Lslash", "lslash", "Scaron", "scaron",
        "Zcaron", "zcaron", "brokenbar", "Eth", "eth", "Yacute", "yacute", "Thorn",
        "thorn", "minus", "multiply", "onesuperior", "twosuperior", "threesuperior",
        "onehalf", "onequarter", "threequarters", "franc", "Gbreve", "gbreve",
        "Idotaccent", "Scedilla", "scedilla", "Cacute", "cacute", "Ccaron", "ccaron",
        "dcroat"
    ];

    #endregion

    #region Table parsers

    private void ParseHead()
    {
        if (!_tables.TryGetValue("head", out var t)) return;
        var o = t.offset;
        if (o + 54 > _data.Length) return;

        UnitsPerEm = ReadUInt16(o + 18);
        BBox = [ReadInt16(o + 36), ReadInt16(o + 38), ReadInt16(o + 40), ReadInt16(o + 42)];
        MacStyle = ReadUInt16(o + 44);
    }

    private void ParseHhea()
    {
        if (!_tables.TryGetValue("hhea", out var t)) return;
        var o = t.offset;
        if (o + 36 > _data.Length) return;

        Ascent = ReadInt16(o + 4);
        Descent = ReadInt16(o + 6);
        LineGap = ReadInt16(o + 8);
        NumberOfHMetrics = ReadUInt16(o + 34);
    }

    private void ParseOS2()
    {
        if (!_tables.TryGetValue("OS/2", out var t)) return;
        var o = t.offset;
        if (o + 78 > _data.Length) return;

        WeightClass = ReadUInt16(o + 4);
        STypoAscender = ReadInt16(o + 68);
        STypoDescender = ReadInt16(o + 70);
        STypoLineGap = ReadInt16(o + 72);
        // usWinAscent/usWinDescent (unsigned) at offsets 74/76
        if (o + 78 <= _data.Length)
        {
            UsWinAscent = ReadUInt16(o + 74);
            UsWinDescent = ReadUInt16(o + 76);
        }

        // Cap height and x-height are in version 2+
        if (t.length >= 96)
        {
            SxHeight = ReadInt16(o + 86);
            CapHeight = ReadInt16(o + 88);
        }
        else
        {
            CapHeight = Ascent > 0 ? Ascent : (int)(UnitsPerEm * 0.7);
            SxHeight = (int)(CapHeight * 0.7);
        }
    }

    private void ParsePost()
    {
        if (!_tables.TryGetValue("post", out var t)) return;
        var o = t.offset;
        if (o + 32 > _data.Length) return;

        // Italic angle is a Fixed (16.16)
        var intPart = ReadInt16(o + 4);
        var fracPart = ReadUInt16(o + 6);
        ItalicAngle = intPart + fracPart / 65536.0;

        IsFixedPitch = ReadUInt32(o + 12) != 0;
        UnderlinePosition = ReadInt16(o + 8);
        UnderlineThickness = ReadInt16(o + 10);

        // Version 2.0 carries an explicit glyph-name table: numGlyphs(2) then a
        // uint16 name-index per glyph, then Pascal strings for the non-standard
        // (>= 258) indices. Indices < 258 name the Macintosh standard glyph set.
        var version = ReadUInt32(o + 0);
        if (version != 0x00020000) return;
        if (o + 34 > _data.Length) return;
        var numGlyphs = ReadUInt16(o + 32);
        var idxOffset = o + 34;
        if (idxOffset + numGlyphs * 2 > _data.Length) return;
        var indices = new int[numGlyphs];
        for (var i = 0; i < numGlyphs; i++)
            indices[i] = ReadUInt16(idxOffset + i * 2);
        // Read the Pascal-string pool that follows the index array.
        var pool = new List<string>();
        var p = idxOffset + numGlyphs * 2;
        var tableEnd = o + t.length;
        while (p < tableEnd && p < _data.Length)
        {
            int len = _data[p];
            if (p + 1 + len > _data.Length) break;
            pool.Add(Encoding.ASCII.GetString(_data, p + 1, len));
            p += 1 + len;
        }
        for (var g = 0; g < numGlyphs; g++)
        {
            var idx = indices[g];
            string? name = idx < 258
                ? (idx < MacGlyphNames.Length ? MacGlyphNames[idx] : null)
                : (idx - 258 < pool.Count ? pool[idx - 258] : null);
            if (!string.IsNullOrEmpty(name) && name != ".notdef")
                GlyphNames[g] = name!;
        }
    }

    private void ParseName()
    {
        if (!_tables.TryGetValue("name", out var t)) return;
        var o = t.offset;
        if (o + 6 > _data.Length) return;

        var count = ReadUInt16(o + 2);
        var stringOffset = ReadUInt16(o + 4) + o;

        // Prefer the Windows English (lang 0x409) record per name id: localized
        // installs carry every UI language ("Gras Italique", …) and iteration
        // order must not decide which one wins.
        int famScore = -1, subScore = -1, psScore = -1;
        for (var i = 0; i < count; i++)
        {
            var recordOffset = o + 6 + i * 12;
            if (recordOffset + 12 > _data.Length) break;

            var platformId = ReadUInt16(recordOffset);
            var languageId = ReadUInt16(recordOffset + 4);
            var nameId = ReadUInt16(recordOffset + 6);
            var length = ReadUInt16(recordOffset + 8);
            var offset = ReadUInt16(recordOffset + 10) + stringOffset;

            if (offset + length > _data.Length) continue;

            // Prefer platform 3 (Windows) or platform 1 (Mac)
            string value;
            int score;
            if (platformId == 3) // Windows — UTF-16 BE
            {
                value = Encoding.BigEndianUnicode.GetString(_data, offset, length);
                score = languageId == 0x409 ? 3 : 2;
            }
            else if (platformId == 1) // Mac — ASCII/Latin
            {
                value = Encoding.ASCII.GetString(_data, offset, length);
                score = 1;
            }
            else
                continue;

            if (string.IsNullOrEmpty(value)) continue;

            switch (nameId)
            {
                case 1: // Family name
                    if (score > famScore) { FamilyName = value; famScore = score; }
                    break;
                case 2: // Subfamily (style) name
                    if (score > subScore) { SubfamilyName = value; subScore = score; }
                    break;
                case 6: // PostScript name
                    if (score > psScore) { PostScriptName = value; psScore = score; }
                    break;
            }
        }
    }

    private void ParseCMap()
    {
        if (!_tables.TryGetValue("cmap", out var t)) return;
        var o = t.offset;
        if (o + 4 > _data.Length) return;

        var numSubtables = ReadUInt16(o + 2);

        // Preference: Windows Unicode BMP/full > generic Unicode > Mac Roman > Symbol.
        // Mac Roman (platform 1) is the cmap subset PDF tools emit for embedded subset
        // TrueType fonts — the bytes in the content stream (1:1 with the /ToUnicode
        // entries) are direct codes into this cmap, not Unicode code points. Without
        // it the cmap is empty and DrawSimpleText finds no glyph for every char.
        var bestOffset = -1;
        var bestPriority = -1;
        for (var i = 0; i < numSubtables; i++)
        {
            var entryOffset = o + 4 + i * 8;
            if (entryOffset + 8 > _data.Length) break;

            var platformId = ReadUInt16(entryOffset);
            var encodingId = ReadUInt16(entryOffset + 2);
            var subtableOffset = (int)ReadUInt32(entryOffset + 4) + o;

            int priority = -1;
            if (platformId == 3 && encodingId == 10) priority = 4;       // Win Unicode Full
            else if (platformId == 3 && encodingId == 1) priority = 3;   // Win Unicode BMP
            else if (platformId == 0) priority = 2;                       // Generic Unicode
            else if (platformId == 1 && encodingId == 0) priority = 1;    // Mac Roman
            else if (platformId == 3 && encodingId == 0) priority = 0;    // Win Symbol

            if (priority > bestPriority)
            {
                bestPriority = priority;
                bestOffset = subtableOffset;
            }
        }

        if (bestOffset < 0 || bestOffset + 6 > _data.Length) return;

        var format = ReadUInt16(bestOffset);
        if (format == 4) ParseCMapFormat4(bestOffset);
        else if (format == 6) ParseCMapFormat6(bestOffset);
        else if (format == 12) ParseCMapFormat12(bestOffset);
    }

    /// <summary>
    /// cmap format 6 — trimmed table mapping (OpenType §cmap). Used by Mac Roman
    /// subset cmaps in PDF embedded subsets. Layout: format(2) length(2) language(2)
    /// firstCode(2) entryCount(2) glyphIdArray[entryCount](2 each). Maps codes
    /// [firstCode, firstCode+entryCount) linearly to glyph IDs.
    /// </summary>
    private void ParseCMapFormat6(int offset)
    {
        if (offset + 10 > _data.Length) return;
        var firstCode = ReadUInt16(offset + 6);
        var entryCount = ReadUInt16(offset + 8);
        var arrayOffset = offset + 10;
        if (arrayOffset + entryCount * 2 > _data.Length) return;
        for (var i = 0; i < entryCount; i++)
        {
            var glyphId = ReadUInt16(arrayOffset + i * 2);
            if (glyphId != 0) CMap[firstCode + i] = glyphId;
        }
    }

    private void ParseCMapFormat4(int offset)
    {
        if (offset + 14 > _data.Length) return;

        var segCount = ReadUInt16(offset + 6) / 2;
        var endCodeOffset = offset + 14;
        var startCodeOffset = endCodeOffset + segCount * 2 + 2; // +2 for reservedPad
        var idDeltaOffset = startCodeOffset + segCount * 2;
        var idRangeOffset = idDeltaOffset + segCount * 2;

        for (var i = 0; i < segCount; i++)
        {
            var endCode = ReadUInt16(endCodeOffset + i * 2);
            var startCode = ReadUInt16(startCodeOffset + i * 2);
            var idDelta = ReadInt16(idDeltaOffset + i * 2);
            var idRangeOffsetVal = ReadUInt16(idRangeOffset + i * 2);

            if (startCode == 0xFFFF) break;

            for (var c = startCode; c <= endCode; c++)
            {
                int glyphId;
                if (idRangeOffsetVal == 0)
                {
                    glyphId = (c + idDelta) & 0xFFFF;
                }
                else
                {
                    var glyphIdOffset = idRangeOffset + i * 2 + idRangeOffsetVal + (c - startCode) * 2;
                    if (glyphIdOffset + 2 > _data.Length) continue;
                    glyphId = ReadUInt16(glyphIdOffset);
                    if (glyphId != 0) glyphId = (glyphId + idDelta) & 0xFFFF;
                }
                CMap[c] = glyphId;
            }
        }
    }

    private void ParseCMapFormat12(int offset)
    {
        if (offset + 16 > _data.Length) return;

        // Format 12: segmented coverage (32-bit)
        // Header: format(2) + reserved(2) + length(4) + language(4) + numGroups(4)
        var groupsOffset = offset + 16;
        var numGroupsU = ReadUInt32(offset + 12);
        // Sanity-check: a font cannot have more groups than the data can hold
        var maxGroups = Math.Max((_data.Length - groupsOffset) / 12, 0);
        var numGroups = (int)Math.Min(numGroupsU, (uint)maxGroups);

        for (var i = 0; i < numGroups; i++)
        {
            var groupOffset = groupsOffset + i * 12;
            if (groupOffset + 12 > _data.Length) break;

            // Use uint to avoid negative values when casting large 32-bit char codes
            var startCharCodeU = ReadUInt32(groupOffset);
            var endCharCodeU   = ReadUInt32(groupOffset + 4);
            var startGlyphId   = (int)ReadUInt32(groupOffset + 8);

            // Only map BMP range for PDF (0-65535); skip non-BMP groups entirely
            if (startCharCodeU > 0xFFFF) continue;

            var start = (int)startCharCodeU;
            var end   = (int)Math.Min(endCharCodeU, 0xFFFF);
            for (var c = start; c <= end; c++)
            {
                CMap[c] = startGlyphId + (c - start);
            }
        }
    }

    private void ParseHmtx()
    {
        if (!_tables.TryGetValue("hmtx", out var t)) return;
        var o = t.offset;

        // Determine total glyphs from maxp table
        var numGlyphs = NumberOfHMetrics;
        if (_tables.TryGetValue("maxp", out var maxpTable) && maxpTable.offset + 6 <= _data.Length)
            numGlyphs = ReadUInt16(maxpTable.offset + 4);

        GlyphWidths = new int[numGlyphs];
        var lastWidth = 0;

        for (var i = 0; i < numGlyphs; i++)
        {
            if (i < NumberOfHMetrics)
            {
                var entryOffset = o + i * 4;
                if (entryOffset + 2 <= _data.Length)
                {
                    lastWidth = ReadUInt16(entryOffset);
                }
            }
            GlyphWidths[i] = lastWidth;
        }
    }

    #endregion

    #region Binary readers

    private int ReadUInt16(int offset) =>
        (_data[offset] << 8) | _data[offset + 1];

    private int ReadInt16(int offset)
    {
        var val = (_data[offset] << 8) | _data[offset + 1];
        return val >= 0x8000 ? val - 0x10000 : val;
    }

    private uint ReadUInt32(int offset) =>
        ((uint)_data[offset] << 24) | ((uint)_data[offset + 1] << 16) |
        ((uint)_data[offset + 2] << 8) | _data[offset + 3];

    #endregion
}
