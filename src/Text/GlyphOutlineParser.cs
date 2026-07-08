using System.Text;

namespace Aspose.Pdf.Text;

/// <summary>A single point in a glyph contour.</summary>
internal readonly struct ContourPoint(double x, double y, bool onCurve)
{
    public double X { get; } = x;
    public double Y { get; } = y;
    public bool OnCurve { get; } = onCurve;
}

/// <summary>Parsed glyph outline: an array of contours.</summary>
internal sealed class GlyphOutline
{
    public ContourPoint[][] Contours { get; }
    public double XMin { get; }
    public double YMin { get; }
    public double XMax { get; }
    public double YMax { get; }

    public GlyphOutline(ContourPoint[][] contours, double xMin, double yMin, double xMax, double yMax)
    {
        Contours = contours;
        XMin = xMin; YMin = yMin; XMax = xMax; YMax = yMax;
    }
}

/// <summary>
/// Parses TrueType glyph outlines from the glyf table.
/// Handles both simple and composite glyphs.
/// </summary>
internal sealed class GlyphOutlineParser : IGlyphOutlineSource
{
    private readonly byte[] _data;
    private readonly Dictionary<string, (int offset, int length)> _tables = new();
    private int _numGlyphs;
    private bool _isLongLoca;
    private int[]? _locaOffsets;
    private int _glyfOffset;

    /// <summary>Font units per em.</summary>
    public int UnitsPerEm { get; private set; } = 1000;

    /// <summary>CMap: character code → glyph ID.</summary>
    public Dictionary<int, int> CMap { get; } = new();

    /// <summary>Per-glyph advance widths in font units, indexed by glyph id.
    /// Sourced from the TrueType hmtx table; empty when the font omits hmtx.</summary>
    private int[] _advanceWidths = [];

    public GlyphOutlineParser(byte[] fontData)
    {
        _data = fontData;
        ParseTableDirectory();
        ParseHead();
        ParseMaxp();
        _locaOffsets = ParseLoca();
        if (_tables.TryGetValue("glyf", out var glyf))
            _glyfOffset = glyf.offset;
        ParseCMap();
        MirrorSymbolPuaEntries();
        ParseHmtx();
    }

    /// <inheritdoc />
    public int GetAdvanceWidth(int glyphId)
    {
        if (glyphId < 0 || glyphId >= _advanceWidths.Length) return 0;
        return _advanceWidths[glyphId];
    }

    // hmtx is laid out as a sequence of `longHorMetric` entries (advanceWidth u16 + lsb i16)
    // followed by trailing lsb-only entries — but for advance widths we only need the first
    // `numberOfHMetrics` (from hhea); glyphs beyond that all share the last metric's advance.
    private void ParseHmtx()
    {
        if (!_tables.TryGetValue("hmtx", out var hmtx)) return;
        if (!_tables.TryGetValue("hhea", out var hhea)) return;
        if (hhea.offset + 36 > _data.Length) return;
        // hhea.numberOfHMetrics is the last field, at offset +34 (u16).
        var numHMetrics = ReadUInt16(hhea.offset + 34);
        if (numHMetrics == 0) return;

        _advanceWidths = new int[_numGlyphs];
        var lastWidth = 0;
        for (var i = 0; i < _numGlyphs; i++)
        {
            if (i < numHMetrics)
            {
                var entry = hmtx.offset + i * 4;
                if (entry + 2 > _data.Length) break;
                lastWidth = ReadUInt16(entry);
            }
            _advanceWidths[i] = lastWidth;
        }
    }

    /// <summary>Get glyph outline by glyph ID, or null for empty/missing glyphs.</summary>
    public GlyphOutline? GetOutline(int glyphId)
    {
        if (_locaOffsets is null || glyphId < 0 || glyphId >= _numGlyphs)
            return null;

        return ParseGlyph(glyphId, 0);
    }

    private GlyphOutline? ParseGlyph(int glyphId, int depth)
    {
        if (depth > 10 || _locaOffsets is null) return null;

        var start = _glyfOffset + _locaOffsets[glyphId];
        var end = _glyfOffset + _locaOffsets[glyphId + 1];
        if (start >= end || start + 10 > _data.Length) return null;

        var numContours = ReadInt16(start);
        var xMin = ReadInt16(start + 2);
        var yMin = ReadInt16(start + 4);
        var xMax = ReadInt16(start + 6);
        var yMax = ReadInt16(start + 8);

        if (numContours >= 0)
            return ParseSimpleGlyph(start + 10, numContours, xMin, yMin, xMax, yMax);
        else
            return ParseCompositeGlyph(start + 10, depth, xMin, yMin, xMax, yMax);
    }

    private GlyphOutline? ParseSimpleGlyph(int offset, int numContours,
        double xMin, double yMin, double xMax, double yMax)
    {
        if (numContours == 0) return null;

        // Read endPtsOfContours
        var endPts = new int[numContours];
        for (var i = 0; i < numContours; i++)
        {
            if (offset + 2 > _data.Length) return null;
            endPts[i] = ReadUInt16(offset);
            offset += 2;
        }

        var numPoints = endPts[numContours - 1] + 1;

        // Skip instructions
        if (offset + 2 > _data.Length) return null;
        var instrLen = ReadUInt16(offset);
        offset += 2 + instrLen;

        // Read flags
        var flags = new byte[numPoints];
        for (var i = 0; i < numPoints; i++)
        {
            if (offset >= _data.Length) return null;
            flags[i] = _data[offset++];
            if ((flags[i] & 0x08) != 0) // repeat flag
            {
                if (offset >= _data.Length) return null;
                var repeatCount = _data[offset++];
                for (var j = 0; j < repeatCount && i + 1 < numPoints; j++)
                {
                    i++;
                    flags[i] = flags[i - 1];
                }
            }
        }

        // Read X coordinates (deltas)
        var xCoords = new double[numPoints];
        double x = 0;
        for (var i = 0; i < numPoints; i++)
        {
            var f = flags[i];
            if ((f & 0x02) != 0) // x is 1 byte
            {
                if (offset >= _data.Length) return null;
                var dx = _data[offset++];
                x += (f & 0x10) != 0 ? dx : -dx;
            }
            else if ((f & 0x10) == 0) // x is 2 bytes (signed)
            {
                if (offset + 2 > _data.Length) return null;
                x += ReadInt16(offset);
                offset += 2;
            }
            // else: x is same as previous (delta = 0)
            xCoords[i] = x;
        }

        // Read Y coordinates (deltas)
        var yCoords = new double[numPoints];
        double y = 0;
        for (var i = 0; i < numPoints; i++)
        {
            var f = flags[i];
            if ((f & 0x04) != 0) // y is 1 byte
            {
                if (offset >= _data.Length) return null;
                var dy = _data[offset++];
                y += (f & 0x20) != 0 ? dy : -dy;
            }
            else if ((f & 0x20) == 0) // y is 2 bytes (signed)
            {
                if (offset + 2 > _data.Length) return null;
                y += ReadInt16(offset);
                offset += 2;
            }
            yCoords[i] = y;
        }

        // Build contours
        var contours = new ContourPoint[numContours][];
        var ptIdx = 0;
        for (var c = 0; c < numContours; c++)
        {
            var count = endPts[c] - ptIdx + 1;
            var pts = new ContourPoint[count];
            for (var i = 0; i < count; i++)
            {
                var idx = ptIdx + i;
                var onCurve = (flags[idx] & 0x01) != 0;
                pts[i] = new ContourPoint(xCoords[idx], yCoords[idx], onCurve);
            }
            contours[c] = pts;
            ptIdx = endPts[c] + 1;
        }

        return new GlyphOutline(contours, xMin, yMin, xMax, yMax);
    }

    private GlyphOutline? ParseCompositeGlyph(int offset, int depth,
        double xMin, double yMin, double xMax, double yMax)
    {
        var allContours = new List<ContourPoint[]>();

        while (offset + 4 <= _data.Length)
        {
            var compFlags = ReadUInt16(offset);
            var compGid = ReadUInt16(offset + 2);
            offset += 4;

            // Read arguments (translation offsets)
            double dx = 0, dy = 0;
            if ((compFlags & 0x0001) != 0) // ARG_1_AND_2_ARE_WORDS
            {
                if (offset + 4 > _data.Length) break;
                dx = ReadInt16(offset);
                dy = ReadInt16(offset + 2);
                offset += 4;
            }
            else
            {
                if (offset + 2 > _data.Length) break;
                dx = (sbyte)_data[offset];
                dy = (sbyte)_data[offset + 1];
                offset += 2;
            }

            // Read transform
            double a = 1, b = 0, c = 0, d = 1;
            if ((compFlags & 0x0008) != 0) // WE_HAVE_A_SCALE
            {
                if (offset + 2 > _data.Length) break;
                a = d = ReadFixed2Dot14(offset);
                offset += 2;
            }
            else if ((compFlags & 0x0040) != 0) // WE_HAVE_AN_X_AND_Y_SCALE
            {
                if (offset + 4 > _data.Length) break;
                a = ReadFixed2Dot14(offset);
                d = ReadFixed2Dot14(offset + 2);
                offset += 4;
            }
            else if ((compFlags & 0x0080) != 0) // WE_HAVE_A_TWO_BY_TWO
            {
                if (offset + 8 > _data.Length) break;
                a = ReadFixed2Dot14(offset);
                b = ReadFixed2Dot14(offset + 2);
                c = ReadFixed2Dot14(offset + 4);
                d = ReadFixed2Dot14(offset + 6);
                offset += 8;
            }

            // Recursively get component outline
            var compOutline = ParseGlyph(compGid, depth + 1);
            if (compOutline is not null)
            {
                foreach (var contour in compOutline.Contours)
                {
                    var transformed = new ContourPoint[contour.Length];
                    for (var i = 0; i < contour.Length; i++)
                    {
                        var p = contour[i];
                        var tx = a * p.X + c * p.Y + dx;
                        var ty = b * p.X + d * p.Y + dy;
                        transformed[i] = new ContourPoint(tx, ty, p.OnCurve);
                    }
                    allContours.Add(transformed);
                }
            }

            if ((compFlags & 0x0020) == 0) // no MORE_COMPONENTS
                break;
        }

        if (allContours.Count == 0) return null;
        return new GlyphOutline(allContours.ToArray(), xMin, yMin, xMax, yMax);
    }

    #region Table parsing helpers

    private void ParseTableDirectory()
    {
        if (_data.Length < 12) return;
        // TrueType Collection ('ttcf'): the file starts with a TTC header, not an
        // sfnt table directory. The offset table for the first embedded font lives
        // at the first entry of the TTC's offset array (byte 12); rebase there.
        // Table offsets inside the directory are absolute from the start of the
        // file, so no further adjustment is needed once we read the right directory.
        int baseOff = 0;
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

    private void ParseHead()
    {
        if (!_tables.TryGetValue("head", out var t) || t.offset + 54 > _data.Length) return;
        UnitsPerEm = ReadUInt16(t.offset + 18);
        // indexToLocFormat: 0 = short (2-byte) loca, anything else = long (4-byte).
        // The spec only defines 0/1, but some subsetters emit a non-standard value such
        // as 0x0100; FreeType/fontTools treat any non-zero as long, so a strict `== 1`
        // wrongly falls back to short loca and reads every glyph at the wrong offset
        // (outlines come out as .notdef boxes or empty).
        _isLongLoca = ReadInt16(t.offset + 50) != 0;
    }

    private void ParseMaxp()
    {
        if (!_tables.TryGetValue("maxp", out var t) || t.offset + 6 > _data.Length) return;
        _numGlyphs = ReadUInt16(t.offset + 4);
    }

    private int[]? ParseLoca()
    {
        if (!_tables.TryGetValue("loca", out var t)) return null;
        var offsets = new int[_numGlyphs + 1];
        for (var i = 0; i <= _numGlyphs; i++)
        {
            if (_isLongLoca)
            {
                var pos = t.offset + i * 4;
                if (pos + 4 > _data.Length) break;
                offsets[i] = (int)ReadUInt32(pos);
            }
            else
            {
                var pos = t.offset + i * 2;
                if (pos + 2 > _data.Length) break;
                offsets[i] = ReadUInt16(pos) * 2;
            }
        }
        return offsets;
    }

    private void ParseCMap()
    {
        if (!_tables.TryGetValue("cmap", out var t)) return;
        var off = t.offset;
        if (off + 4 > _data.Length) return;
        var numSubtables = ReadUInt16(off + 2);

        // Prefer formats by priority: 12 (full Unicode) > 4 (BMP) > 6 (Mac Roman trimmed)
        // > 0 (Mac byte table). Formats 6 and 0 / platform 1 are what PDF tools emit for
        // embedded subset TrueType fonts — the bytes in the content stream are direct
        // codes into these trimmed tables, not Unicode code points. Without parsing them
        // the cmap stays empty for those fonts and the renderer can't find any glyph.
        int fmt0Offset = -1, fmt4Offset = -1, fmt6Offset = -1, fmt12Offset = -1;
        for (var i = 0; i < numSubtables; i++)
        {
            var subOff = off + 4 + i * 8;
            if (subOff + 8 > _data.Length) break;
            var subtableOffset = off + (int)ReadUInt32(subOff + 4);

            if (subtableOffset + 2 > _data.Length) continue;
            var format = ReadUInt16(subtableOffset);
            if (format == 12) fmt12Offset = subtableOffset;
            else if (format == 4 && fmt4Offset < 0) fmt4Offset = subtableOffset;
            else if (format == 6 && fmt6Offset < 0) fmt6Offset = subtableOffset;
            else if (format == 0 && fmt0Offset < 0) fmt0Offset = subtableOffset;
        }

        if (fmt12Offset >= 0)
            ParseCMapFormat12(fmt12Offset);
        else if (fmt4Offset >= 0)
            ParseCMapFormat4(fmt4Offset);
        else if (fmt6Offset >= 0)
            ParseCMapFormat6(fmt6Offset);
        else if (fmt0Offset >= 0)
            ParseCMapFormat0(fmt0Offset);
    }

    // Format 0 (byte encoding table, OpenType §cmap): 2 (format) + 2 (length) +
    // 2 (language) followed by a 256-entry array mapping each byte code directly to a
    // glyph id. Common in subset TrueType fonts carrying only a (1,0) Mac cmap.
    private void ParseCMapFormat0(int off)
    {
        var arrayOff = off + 6;
        if (arrayOff + 256 > _data.Length) return;
        for (var c = 0; c < 256; c++)
        {
            int gid = _data[arrayOff + c];
            if (gid != 0) CMap.TryAdd(c, gid);
        }
    }

    private void ParseCMapFormat6(int off)
    {
        if (off + 10 > _data.Length) return;
        var firstCode = ReadUInt16(off + 6);
        var entryCount = ReadUInt16(off + 8);
        var arrayOff = off + 10;
        if (arrayOff + entryCount * 2 > _data.Length) return;
        for (var i = 0; i < entryCount; i++)
        {
            var gid = ReadUInt16(arrayOff + i * 2);
            if (gid != 0) CMap.TryAdd(firstCode + i, gid);
        }
    }

    /// <summary>
    /// Some PDF tools embed subset fonts whose only cmap subtable is Symbol-encoding
    /// (platform 3 / encoding 0). Symbol fonts map characters to the Private Use Area
    /// 0xF000-0xF0FF, so when the content stream contains byte 0x21 the cmap entry
    /// for it sits under code 0xF021. Subset fonts that number their glyphs from 1 use
    /// the low part of that range (0xF001-0xF01F) too, so mirror the whole 0xF000-0xF0FF
    /// block down to its low-byte equivalent so lookups keyed by the raw content-stream
    /// byte hit.
    /// </summary>
    internal void MirrorSymbolPuaEntries()
    {
        var puaPairs = new List<(int low, int gid)>();
        foreach (var kv in CMap)
        {
            if (kv.Key >= 0xF000 && kv.Key <= 0xF0FF)
                puaPairs.Add((kv.Key & 0xFF, kv.Value));
        }
        foreach (var (low, gid) in puaPairs)
            CMap.TryAdd(low, gid);
    }

    private void ParseCMapFormat4(int off)
    {
        if (off + 14 > _data.Length) return;
        var segCount = ReadUInt16(off + 6) / 2;
        var endCodesOff = off + 14;
        var startCodesOff = endCodesOff + segCount * 2 + 2; // +2 for reservedPad
        var idDeltaOff = startCodesOff + segCount * 2;
        var idRangeOff = idDeltaOff + segCount * 2;

        for (var i = 0; i < segCount; i++)
        {
            var endCode = ReadUInt16(endCodesOff + i * 2);
            var startCode = ReadUInt16(startCodesOff + i * 2);
            var idDelta = ReadInt16(idDeltaOff + i * 2);
            var idRangeOffset = ReadUInt16(idRangeOff + i * 2);

            if (startCode == 0xFFFF) break;

            for (var c = startCode; c <= endCode; c++)
            {
                int gid;
                if (idRangeOffset == 0)
                {
                    gid = (c + idDelta) & 0xFFFF;
                }
                else
                {
                    var glyphOff = idRangeOff + i * 2 + idRangeOffset + (c - startCode) * 2;
                    if (glyphOff + 2 > _data.Length) continue;
                    gid = ReadUInt16(glyphOff);
                    if (gid != 0) gid = (gid + idDelta) & 0xFFFF;
                }
                if (gid != 0)
                    CMap.TryAdd(c, gid);
            }
        }
    }

    private void ParseCMapFormat12(int off)
    {
        if (off + 16 > _data.Length) return;
        var numGroups = (int)ReadUInt32(off + 12);
        var groupOff = off + 16;
        for (var i = 0; i < numGroups; i++)
        {
            var gOff = groupOff + i * 12;
            if (gOff + 12 > _data.Length) break;
            var startCharCode = (int)ReadUInt32(gOff);
            var endCharCode = (int)ReadUInt32(gOff + 4);
            var startGlyphId = (int)ReadUInt32(gOff + 8);
            for (var c = startCharCode; c <= endCharCode; c++)
                CMap.TryAdd(c, startGlyphId + (c - startCharCode));
        }
    }

    #endregion

    #region Binary reading

    private short ReadInt16(int offset) =>
        (short)((_data[offset] << 8) | _data[offset + 1]);

    private int ReadUInt16(int offset) =>
        (_data[offset] << 8) | _data[offset + 1];

    private uint ReadUInt32(int offset) =>
        ((uint)_data[offset] << 24) | ((uint)_data[offset + 1] << 16) |
        ((uint)_data[offset + 2] << 8) | _data[offset + 3];

    private double ReadFixed2Dot14(int offset)
    {
        var raw = ReadInt16(offset);
        return raw / 16384.0;
    }

    #endregion
}
