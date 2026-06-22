using System.Text;

namespace Aspose.Pdf.Text;

/// <summary>
/// Creates a subset of a TrueType font containing only the glyphs needed
/// for a specific set of character codes. Produces a valid TrueType file
/// with renumbered glyph IDs.
/// </summary>
internal sealed class TrueTypeSubsetter
{
    private readonly byte[] _data;
    private readonly TrueTypeParser _parser;

    // Table directory from the original font
    private readonly Dictionary<string, (int offset, int length)> _tables = new();
    private int _numGlyphs;
    private bool _isLongLoca;

    public TrueTypeSubsetter(byte[] fontData, TrueTypeParser parser)
    {
        _data = fontData;
        _parser = parser;
        ParseTableDirectory();
        ParseMaxp();
        ParseHead();
    }

    /// <summary>
    /// Create a subset font containing only the glyphs for the given character codes.
    /// Returns (subsetFontData, oldGlyphId -> newGlyphId mapping).
    /// Glyph 0 (.notdef) is always included.
    /// </summary>
    public (byte[] fontData, Dictionary<int, int> glyphMap) Subset(IEnumerable<int> charCodes)
    {
        // 1. Collect glyph IDs needed (always include glyph 0)
        var neededGlyphs = new SortedSet<int> { 0 };
        foreach (var code in charCodes)
        {
            if (_parser.CMap.TryGetValue(code, out var gid) && gid > 0 && gid < _numGlyphs)
                neededGlyphs.Add(gid);
        }

        // 2. Resolve composite glyph dependencies
        var locaOffsets = ParseLoca();
        if (locaOffsets is null) return (_data, new Dictionary<int, int>()); // no loca = can't subset

        ResolveCompositeGlyphs(neededGlyphs, locaOffsets);

        // 3. Build glyph mapping (old -> new sequential IDs)
        var glyphMap = new Dictionary<int, int>();
        var newGid = 0;
        foreach (var oldGid in neededGlyphs)
        {
            glyphMap[oldGid] = newGid++;
        }

        // 4. Build subset tables
        var subsetGlyphCount = glyphMap.Count;

        // Build new glyf and loca tables
        var (newGlyf, newLoca) = BuildGlyfAndLoca(neededGlyphs, locaOffsets, glyphMap);

        // Build new hmtx table
        var newHmtx = BuildHmtx(neededGlyphs);

        // Build new maxp table
        var newMaxp = BuildMaxp(subsetGlyphCount);

        // BuildGlyfAndLoca always emits a long (32-bit) loca table, so head's
        // indexToLocFormat must declare long unconditionally. Previously this was derived
        // from the glyf size and reported "short" for any subset under 128 KB, which left
        // the loca format inconsistent with the table actually written — the consumer then
        // read 16-bit offsets from 32-bit data and saw a corrupt glyf (every glyph .notdef).
        var newHead = BuildHead(useLongLoca: true);

        // Build new cmap (identity mapping for subset — not needed for PDF Type 2 CID fonts,
        // but included for font validity)
        var newCmap = BuildCmap(neededGlyphs, glyphMap);

        // Copy remaining required tables
        var tables = new Dictionary<string, byte[]>();
        tables["head"] = newHead;
        tables["hhea"] = BuildHhea(subsetGlyphCount);
        tables["maxp"] = newMaxp;
        tables["hmtx"] = newHmtx;
        tables["loca"] = newLoca;
        tables["glyf"] = newGlyf;
        tables["cmap"] = newCmap;

        // Copy optional tables that don't reference glyph IDs
        foreach (var tag in new[] { "OS/2", "name", "post", "cvt ", "fpgm", "prep" })
        {
            if (_tables.TryGetValue(tag, out var t))
                tables[tag] = CopyBytes(t.offset, t.length);
        }

        // 5. Assemble the final font file
        var result = AssembleFont(tables);
        return (result, glyphMap);
    }

    #region Table parsing

    private void ParseTableDirectory()
    {
        if (_data.Length < 12) return;
        var numTables = ReadUInt16(4);
        for (var i = 0; i < numTables; i++)
        {
            var entryOffset = 12 + i * 16;
            if (entryOffset + 16 > _data.Length) break;
            var tag = Encoding.ASCII.GetString(_data, entryOffset, 4);
            var offset = (int)ReadUInt32(entryOffset + 8);
            var length = (int)ReadUInt32(entryOffset + 12);
            _tables[tag] = (offset, length);
        }
    }

    private void ParseMaxp()
    {
        if (!_tables.TryGetValue("maxp", out var t) || t.offset + 6 > _data.Length) return;
        _numGlyphs = ReadUInt16(t.offset + 4);
    }

    private void ParseHead()
    {
        if (!_tables.TryGetValue("head", out var t) || t.offset + 54 > _data.Length) return;
        _isLongLoca = ReadInt16(t.offset + 50) == 1;
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

    #endregion

    #region Composite glyph resolution

    private void ResolveCompositeGlyphs(SortedSet<int> needed, int[] locaOffsets)
    {
        if (!_tables.TryGetValue("glyf", out var glyfTable)) return;

        var toProcess = new Queue<int>(needed);
        while (toProcess.Count > 0)
        {
            var gid = toProcess.Dequeue();
            if (gid >= _numGlyphs) continue;

            var glyphStart = glyfTable.offset + locaOffsets[gid];
            var glyphEnd = glyfTable.offset + locaOffsets[gid + 1];
            if (glyphStart >= glyphEnd || glyphStart + 10 > _data.Length) continue;

            var numContours = ReadInt16(glyphStart);
            if (numContours >= 0) continue; // simple glyph, no dependencies

            // Composite glyph — parse component glyph references
            var offset = glyphStart + 10; // skip header
            while (offset + 4 <= _data.Length)
            {
                var flags = ReadUInt16(offset);
                var componentGid = ReadUInt16(offset + 2);
                offset += 4;

                if (componentGid < _numGlyphs && needed.Add(componentGid))
                    toProcess.Enqueue(componentGid);

                // Skip arguments based on flags
                if ((flags & 0x0001) != 0) // ARG_1_AND_2_ARE_WORDS
                    offset += 4;
                else
                    offset += 2;

                // Skip transform
                if ((flags & 0x0008) != 0) // WE_HAVE_A_SCALE
                    offset += 2;
                else if ((flags & 0x0040) != 0) // WE_HAVE_AN_X_AND_Y_SCALE
                    offset += 4;
                else if ((flags & 0x0080) != 0) // WE_HAVE_A_TWO_BY_TWO
                    offset += 8;

                if ((flags & 0x0020) == 0) // MORE_COMPONENTS
                    break;
            }
        }
    }

    #endregion

    #region Build subset tables

    private (byte[] glyf, byte[] loca) BuildGlyfAndLoca(
        SortedSet<int> glyphs, int[] locaOffsets, Dictionary<int, int> glyphMap)
    {
        if (!_tables.TryGetValue("glyf", out var glyfTable))
            return ([], new byte[(glyphs.Count + 1) * 4]);

        using var glyfMs = new MemoryStream();
        var newLocaOffsets = new int[glyphs.Count + 1];
        var idx = 0;

        foreach (var oldGid in glyphs)
        {
            newLocaOffsets[idx] = (int)glyfMs.Position;

            var start = glyfTable.offset + locaOffsets[oldGid];
            var end = glyfTable.offset + locaOffsets[oldGid + 1];
            var glyphLen = end - start;

            if (glyphLen > 0 && start + glyphLen <= _data.Length)
            {
                var glyphData = new byte[glyphLen];
                Array.Copy(_data, start, glyphData, 0, glyphLen);

                // For composite glyphs, rewrite component glyph IDs
                var numContours = (short)((glyphData[0] << 8) | glyphData[1]);
                if (numContours < 0)
                    RewriteCompositeGlyphIds(glyphData, glyphMap);

                glyfMs.Write(glyphData);

                // Pad to 4-byte boundary
                var pad = (4 - (int)(glyfMs.Position % 4)) % 4;
                for (var p = 0; p < pad; p++) glyfMs.WriteByte(0);
            }

            idx++;
        }
        newLocaOffsets[idx] = (int)glyfMs.Position;

        // Build loca table (always use long format for simplicity)
        var loca = new byte[(glyphs.Count + 1) * 4];
        for (var i = 0; i <= glyphs.Count; i++)
        {
            WriteUInt32(loca, i * 4, (uint)newLocaOffsets[i]);
        }

        return (glyfMs.ToArray(), loca);
    }

    private static void RewriteCompositeGlyphIds(byte[] glyphData, Dictionary<int, int> glyphMap)
    {
        var offset = 10; // skip glyph header
        while (offset + 4 <= glyphData.Length)
        {
            var flags = (glyphData[offset] << 8) | glyphData[offset + 1];
            var oldGid = (glyphData[offset + 2] << 8) | glyphData[offset + 3];

            if (glyphMap.TryGetValue(oldGid, out var newGid))
            {
                glyphData[offset + 2] = (byte)(newGid >> 8);
                glyphData[offset + 3] = (byte)(newGid & 0xFF);
            }

            offset += 4;

            if ((flags & 0x0001) != 0) offset += 4; else offset += 2;
            if ((flags & 0x0008) != 0) offset += 2;
            else if ((flags & 0x0040) != 0) offset += 4;
            else if ((flags & 0x0080) != 0) offset += 8;

            if ((flags & 0x0020) == 0) break;
        }
    }

    private byte[] BuildHmtx(SortedSet<int> glyphs)
    {
        var result = new byte[glyphs.Count * 4];
        var idx = 0;
        foreach (var gid in glyphs)
        {
            var width = gid < _parser.GlyphWidths.Length ? _parser.GlyphWidths[gid] : 0;
            WriteUInt16(result, idx * 4, (ushort)width);
            // LSB = 0 (simplified; proper subsetting would copy from the original hmtx)
            WriteUInt16(result, idx * 4 + 2, 0);
            idx++;
        }
        return result;
    }

    private byte[] BuildMaxp(int numGlyphs)
    {
        if (!_tables.TryGetValue("maxp", out var t)) return [];
        var result = CopyBytes(t.offset, t.length);
        WriteUInt16(result, 4, (ushort)numGlyphs);
        return result;
    }

    private byte[] BuildHead(bool useLongLoca)
    {
        if (!_tables.TryGetValue("head", out var t)) return [];
        var result = CopyBytes(t.offset, t.length);
        // Set indexToLocFormat: 0 = short, 1 = long
        WriteInt16(result, 50, (short)(useLongLoca ? 1 : 0));
        // Clear checksum adjustment (will be recalculated)
        WriteUInt32(result, 8, 0);
        return result;
    }

    private byte[] BuildHhea(int numHMetrics)
    {
        if (!_tables.TryGetValue("hhea", out var t)) return [];
        var result = CopyBytes(t.offset, t.length);
        WriteUInt16(result, 34, (ushort)numHMetrics);
        return result;
    }

    private byte[] BuildCmap(SortedSet<int> glyphs, Dictionary<int, int> glyphMap)
    {
        // Build a minimal cmap with a format 4 subtable
        // Map original char codes to new glyph IDs
        var mappings = new List<(int charCode, int newGid)>();
        foreach (var (charCode, oldGid) in _parser.CMap)
        {
            if (glyphMap.TryGetValue(oldGid, out var newGid))
                mappings.Add((charCode, newGid));
        }
        mappings.Sort((a, b) => a.charCode.CompareTo(b.charCode));

        // Build segments for format 4
        var segments = new List<(int start, int end, int delta)>();
        for (var i = 0; i < mappings.Count; i++)
        {
            var start = mappings[i].charCode;
            var startGid = mappings[i].newGid;
            var end = start;

            // Extend segment while consecutive
            while (i + 1 < mappings.Count &&
                   mappings[i + 1].charCode == end + 1 &&
                   mappings[i + 1].newGid == startGid + (end - start) + 1)
            {
                end = mappings[++i].charCode;
            }

            var delta = (startGid - start) & 0xFFFF;
            segments.Add((start, end, delta));
        }
        // Add sentinel segment
        segments.Add((0xFFFF, 0xFFFF, 1));

        var segCount = segments.Count;
        var searchRange = 2 * (int)Math.Pow(2, Math.Floor(Math.Log2(segCount)));
        var entrySelector = (int)Math.Floor(Math.Log2(segCount));
        var rangeShift = 2 * segCount - searchRange;

        // Format 4 subtable
        var subtableLen = 14 + segCount * 8;
        var subtable = new byte[subtableLen];
        WriteUInt16(subtable, 0, 4); // format
        WriteUInt16(subtable, 2, (ushort)subtableLen); // length
        WriteUInt16(subtable, 4, 0); // language
        WriteUInt16(subtable, 6, (ushort)(segCount * 2)); // segCountX2
        WriteUInt16(subtable, 8, (ushort)searchRange);
        WriteUInt16(subtable, 10, (ushort)entrySelector);
        WriteUInt16(subtable, 12, (ushort)rangeShift);

        var endCodeOff = 14;
        var startCodeOff = endCodeOff + segCount * 2 + 2; // +2 for reservedPad
        var idDeltaOff = startCodeOff + segCount * 2;
        var idRangeOff = idDeltaOff + segCount * 2;

        // Recalculate subtable length to include reservedPad
        subtableLen = idRangeOff + segCount * 2;
        subtable = new byte[subtableLen];
        WriteUInt16(subtable, 0, 4);
        WriteUInt16(subtable, 2, (ushort)subtableLen);
        WriteUInt16(subtable, 4, 0);
        WriteUInt16(subtable, 6, (ushort)(segCount * 2));
        WriteUInt16(subtable, 8, (ushort)searchRange);
        WriteUInt16(subtable, 10, (ushort)entrySelector);
        WriteUInt16(subtable, 12, (ushort)rangeShift);

        for (var i = 0; i < segCount; i++)
        {
            WriteUInt16(subtable, endCodeOff + i * 2, (ushort)segments[i].end);
            WriteUInt16(subtable, startCodeOff + i * 2, (ushort)segments[i].start);
            WriteInt16(subtable, idDeltaOff + i * 2, (short)segments[i].delta);
            WriteUInt16(subtable, idRangeOff + i * 2, 0); // no range offset (using delta only)
        }

        // Build cmap header: version(2) + numTables(2) + encoding record(8) + subtable
        var cmapLen = 4 + 8 + subtableLen;
        var cmap = new byte[cmapLen];
        WriteUInt16(cmap, 0, 0); // version
        WriteUInt16(cmap, 2, 1); // 1 subtable
        // Encoding record: platform 3, encoding 1, offset 12
        WriteUInt16(cmap, 4, 3); // platformID (Windows)
        WriteUInt16(cmap, 6, 1); // encodingID (Unicode BMP)
        WriteUInt32(cmap, 8, 12); // offset to subtable
        Array.Copy(subtable, 0, cmap, 12, subtableLen);

        return cmap;
    }

    #endregion

    #region Font assembly

    private static byte[] AssembleFont(Dictionary<string, byte[]> tables)
    {
        var numTables = tables.Count;
        var searchRange = (int)Math.Pow(2, Math.Floor(Math.Log2(numTables))) * 16;
        var entrySelector = (int)Math.Floor(Math.Log2(numTables));
        var rangeShift = numTables * 16 - searchRange;

        // Calculate total size
        var headerSize = 12 + numTables * 16;
        var dataOffset = headerSize;

        // Pad each table to 4-byte boundary
        var paddedSizes = new Dictionary<string, int>();
        foreach (var (tag, data) in tables)
        {
            paddedSizes[tag] = (data.Length + 3) & ~3;
        }

        var totalSize = headerSize;
        foreach (var size in paddedSizes.Values)
            totalSize += size;

        var result = new byte[totalSize];

        // Write font header
        WriteUInt32(result, 0, 0x00010000); // sfVersion (TrueType)
        WriteUInt16(result, 4, (ushort)numTables);
        WriteUInt16(result, 6, (ushort)searchRange);
        WriteUInt16(result, 8, (ushort)entrySelector);
        WriteUInt16(result, 10, (ushort)rangeShift);

        // Sort tables by tag for consistency
        var sortedTags = tables.Keys.OrderBy(t => t).ToList();

        // Write table directory and data
        var tableIdx = 0;
        var currentOffset = headerSize;
        foreach (var tag in sortedTags)
        {
            var data = tables[tag];
            var dirOffset = 12 + tableIdx * 16;

            // Tag (4 bytes)
            var tagBytes = Encoding.ASCII.GetBytes(tag.PadRight(4));
            Array.Copy(tagBytes, 0, result, dirOffset, 4);

            // Checksum
            var checksum = CalculateChecksum(data);
            WriteUInt32(result, dirOffset + 4, checksum);

            // Offset and length
            WriteUInt32(result, dirOffset + 8, (uint)currentOffset);
            WriteUInt32(result, dirOffset + 12, (uint)data.Length);

            // Copy table data
            Array.Copy(data, 0, result, currentOffset, data.Length);
            currentOffset += paddedSizes[tag];
            tableIdx++;
        }

        return result;
    }

    private static uint CalculateChecksum(byte[] data)
    {
        uint sum = 0;
        var nLongs = (data.Length + 3) / 4;
        for (var i = 0; i < nLongs; i++)
        {
            var offset = i * 4;
            uint val = 0;
            for (var j = 0; j < 4 && offset + j < data.Length; j++)
                val = (val << 8) | data[offset + j];
            sum += val;
        }
        return sum;
    }

    #endregion

    #region Binary helpers

    private byte[] CopyBytes(int offset, int length)
    {
        var result = new byte[length];
        Array.Copy(_data, offset, result, 0, Math.Min(length, _data.Length - offset));
        return result;
    }

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

    private static void WriteUInt16(byte[] data, int offset, ushort value)
    {
        data[offset] = (byte)(value >> 8);
        data[offset + 1] = (byte)(value & 0xFF);
    }

    private static void WriteInt16(byte[] data, int offset, short value)
    {
        data[offset] = (byte)((ushort)value >> 8);
        data[offset + 1] = (byte)(value & 0xFF);
    }

    private static void WriteUInt32(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)(value >> 24);
        data[offset + 1] = (byte)(value >> 16);
        data[offset + 2] = (byte)(value >> 8);
        data[offset + 3] = (byte)(value & 0xFF);
    }

    #endregion
}
