using System;
using System.Collections.Generic;
using System.IO;

namespace Aspose.Pdf.Text;

/// <summary>
/// Converts a charstring-outline font program — a bare CFF (FontFile3 /Type1C) or
/// an Adobe Type 1 program (FontFile) — into a TrueType-flavoured sfnt (glyf/loca
/// outlines), so it can be packaged in web containers (WOFF) that need an sfnt
/// wrapper. Outlines come from the respective charstring interpreter as flattened
/// contours and are written as straight-segment glyf records; the cmap is rebuilt
/// from the source font's encoding.
/// </summary>
internal static class CffToTrueType
{
    /// <summary>Convert a bare CFF; returns null when it cannot be parsed.</summary>
    public static byte[]? Convert(byte[] cffData)
    {
        var src = CffGlyphSource.TryLoad(cffData);
        if (src is null || src.GlyphCount == 0) return null;
        return Build(src, src.GlyphCount, src.CMap);
    }

    /// <summary>Convert an Adobe Type 1 program; <paramref name="length1"/> and
    /// <paramref name="length2"/> come from the /FontFile stream dict. Returns null
    /// when the program cannot be parsed.
    /// <paramref name="codeToName"/> (the font dict's /Differences) and
    /// <paramref name="codeToUnicode"/> (its /ToUnicode) supplement the cmap for
    /// glyphs whose NAME resolves to no codepoint (a /equalx mapped by ToUnicode
    /// still reaches its glyph in the served face).</summary>
    public static byte[]? ConvertType1(byte[] type1Data, int length1, int length2,
        Dictionary<int, string>? codeToName = null,
        Dictionary<int, int>? codeToUnicode = null)
    {
        var src = Type1GlyphSource.TryLoad(type1Data, length1, length2);
        if (src is null || src.GlyphCount == 0) return null;
        var cmap = src.CMap;
        if (codeToUnicode is not null)
        {
            cmap = new Dictionary<int, int>(src.CMap);
            foreach (var (code, uni) in codeToUnicode)
            {
                var gid = codeToName is not null && codeToName.TryGetValue(code, out var nm)
                    ? src.GidForName(nm)
                    : 0;
                if (gid > 0) cmap.TryAdd(uni, gid);
            }
        }
        return Build(src, src.GlyphCount, cmap);
    }

    /// <summary>Builds a standalone TrueType subset over an existing outline
    /// source: the source glyphs behind <paramref name="uniToSrcGid"/> are
    /// renumbered densely (new gid 0 = the source's notdef) and addressed by a
    /// (3,1) format-4 cmap. Composite source glyphs arrive flattened through
    /// <see cref="IGlyphOutlineSource.GetOutline"/>, so no component remapping
    /// is needed. This is how a non-embedded font ships: a subset of its
    /// substitute face covering exactly the codepoints the document draws.</summary>
    public static byte[]? BuildSubset(IGlyphOutlineSource src, Dictionary<int, int> uniToSrcGid)
    {
        if (uniToSrcGid.Count == 0) return null;
        var srcGids = new List<int> { 0 };
        var srcToNew = new Dictionary<int, int> { [0] = 0 };
        var cmap = new Dictionary<int, int>();
        foreach (var (uni, sg) in uniToSrcGid)
        {
            if (sg <= 0) continue;
            if (!srcToNew.TryGetValue(sg, out var ng))
            {
                ng = srcGids.Count;
                srcGids.Add(sg);
                srcToNew[sg] = ng;
            }
            cmap[uni] = ng;
        }
        if (cmap.Count == 0) return null;
        return Build(new RemappedOutlineSource(src, srcGids, cmap), srcGids.Count, cmap);
    }

    private sealed class RemappedOutlineSource : IGlyphOutlineSource
    {
        private readonly IGlyphOutlineSource _src;
        private readonly List<int> _srcGids;
        public RemappedOutlineSource(IGlyphOutlineSource src, List<int> srcGids, Dictionary<int, int> cmap)
        {
            _src = src;
            _srcGids = srcGids;
            CMap = cmap;
        }
        public int UnitsPerEm => _src.UnitsPerEm;
        public Dictionary<int, int> CMap { get; }
        public GlyphOutline? GetOutline(int glyphId)
            => glyphId >= 0 && glyphId < _srcGids.Count ? _src.GetOutline(_srcGids[glyphId]) : null;
        public int GetAdvanceWidth(int glyphId)
            => glyphId >= 0 && glyphId < _srcGids.Count ? _src.GetAdvanceWidth(_srcGids[glyphId]) : 0;
    }

    private static byte[]? Build(IGlyphOutlineSource src, int glyphCount,
        Dictionary<int, int> cmap)
    {
        var unitsPerEm = src.UnitsPerEm <= 0 ? 1000 : src.UnitsPerEm;

        // ── glyf + loca (long format) + per-glyph metrics ──
        using var glyf = new MemoryStream();
        var loca = new uint[glyphCount + 1];
        var advances = new int[glyphCount];
        var lsbs = new short[glyphCount];
        short xMin = short.MaxValue, yMin = short.MaxValue, xMax = short.MinValue, yMax = short.MinValue;
        int maxPoints = 0, maxContours = 0;

        for (var gid = 0; gid < glyphCount; gid++)
        {
            loca[gid] = (uint)glyf.Length;
            var adv = src.GetAdvanceWidth(gid);
            advances[gid] = adv > 0 ? adv : unitsPerEm / 2;

            GlyphOutline? outline = null;
            try { outline = src.GetOutline(gid); } catch { }
            if (outline is null || outline.Contours.Length == 0) continue;

            var gxMin = (short)Math.Round(outline.XMin);
            var gyMin = (short)Math.Round(outline.YMin);
            var gxMax = (short)Math.Round(outline.XMax);
            var gyMax = (short)Math.Round(outline.YMax);
            lsbs[gid] = gxMin;
            if (gxMin < xMin) xMin = gxMin;
            if (gyMin < yMin) yMin = gyMin;
            if (gxMax > xMax) xMax = gxMax;
            if (gyMax > yMax) yMax = gyMax;

            WriteGlyph(glyf, outline, gxMin, gyMin, gxMax, gyMax, ref maxPoints, ref maxContours);
        }
        loca[glyphCount] = (uint)glyf.Length;
        if (xMin > xMax) { xMin = 0; yMin = 0; xMax = 0; yMax = 0; }

        // ── assemble tables ──
        var tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["glyf"] = glyf.ToArray(),
            ["loca"] = BuildLoca(loca),
            ["head"] = BuildHead(unitsPerEm, xMin, yMin, xMax, yMax),
            ["hhea"] = BuildHhea(yMax, yMin, MaxOf(advances), glyphCount),
            ["hmtx"] = BuildHmtx(advances, lsbs),
            ["maxp"] = BuildMaxp(glyphCount, maxPoints, maxContours),
            ["cmap"] = BuildCmap(cmap, glyphCount),
            ["name"] = BuildName(),
            ["post"] = BuildPost(),
            ["OS/2"] = BuildOs2(advances, yMax, yMin, cmap),
        };
        return AssembleSfnt(tables);
    }

    private static int MaxOf(int[] values)
    {
        var m = 0;
        foreach (var v in values) if (v > m) m = v;
        return m;
    }

    /// <summary>One simple glyf record from a flattened outline: every point on-curve,
    /// coordinates as full 16-bit deltas (no repeat/short-form compression).</summary>
    private static void WriteGlyph(MemoryStream glyf, GlyphOutline outline,
        short xMin, short yMin, short xMax, short yMax, ref int maxPoints, ref int maxContours)
    {
        var contours = outline.Contours;
        var w = new BigEndianWriter(glyf);
        w.I16((short)contours.Length);
        w.I16(xMin); w.I16(yMin); w.I16(xMax); w.I16(yMax);

        var totalPoints = 0;
        foreach (var c in contours) totalPoints += c.Length;
        if (totalPoints > maxPoints) maxPoints = totalPoints;
        if (contours.Length > maxContours) maxContours = contours.Length;

        var end = -1;
        foreach (var c in contours) { end += c.Length; w.U16((ushort)end); }
        w.U16(0); // no instructions

        // flags: bit0 = on-curve. Flattened sources (Type 1 charstrings) carry
        // only on-curve points; a TrueType-parsed outline keeps its quadratic
        // off-curve controls and their flag must survive the round trip.
        foreach (var c in contours)
            foreach (var p in c) glyf.WriteByte(p.OnCurve ? (byte)0x01 : (byte)0x00);

        var prev = 0;
        foreach (var c in contours)
            foreach (var p in c) { var v = (int)Math.Round(p.X); w.I16((short)(v - prev)); prev = v; }
        prev = 0;
        foreach (var c in contours)
            foreach (var p in c) { var v = (int)Math.Round(p.Y); w.I16((short)(v - prev)); prev = v; }

        // 4-byte-align each glyph record (long loca permits any offset but alignment
        // keeps consumers that assume word-aligned records happy).
        while (glyf.Length % 4 != 0) glyf.WriteByte(0);
    }

    private static byte[] BuildLoca(uint[] offsets)
    {
        var ms = new MemoryStream();
        var w = new BigEndianWriter(ms);
        foreach (var o in offsets) w.U32(o);
        return ms.ToArray();
    }

    private static byte[] BuildHead(int unitsPerEm, short xMin, short yMin, short xMax, short yMax)
    {
        var ms = new MemoryStream();
        var w = new BigEndianWriter(ms);
        w.U32(0x00010000);            // version
        w.U32(0x00010000);            // fontRevision
        w.U32(0);                     // checkSumAdjustment (left 0)
        w.U32(0x5F0F3CF5);            // magicNumber
        w.U16(0);                     // flags
        w.U16((ushort)unitsPerEm);
        w.U32(0); w.U32(0);           // created
        w.U32(0); w.U32(0);           // modified
        w.I16(xMin); w.I16(yMin); w.I16(xMax); w.I16(yMax);
        w.U16(0);                     // macStyle
        w.U16(8);                     // lowestRecPPEM
        w.I16(2);                     // fontDirectionHint
        w.I16(1);                     // indexToLocFormat: long
        w.I16(0);                     // glyphDataFormat
        return ms.ToArray();
    }

    private static byte[] BuildHhea(short ascender, short descender, int advanceMax, int numHMetrics)
    {
        var ms = new MemoryStream();
        var w = new BigEndianWriter(ms);
        w.U32(0x00010000);
        w.I16(ascender > 0 ? ascender : (short)800);
        w.I16(descender < 0 ? descender : (short)-200);
        w.I16(0);                     // lineGap
        w.U16((ushort)advanceMax);
        w.I16(0); w.I16(0); w.I16(0); // min LSB/RSB, xMaxExtent (uncritical for web use)
        w.I16(1); w.I16(0);           // caretSlope rise/run
        w.I16(0);                     // caretOffset
        w.I16(0); w.I16(0); w.I16(0); w.I16(0); // reserved
        w.I16(0);                     // metricDataFormat
        w.U16((ushort)numHMetrics);
        return ms.ToArray();
    }

    private static byte[] BuildHmtx(int[] advances, short[] lsbs)
    {
        var ms = new MemoryStream();
        var w = new BigEndianWriter(ms);
        for (var i = 0; i < advances.Length; i++) { w.U16((ushort)advances[i]); w.I16(lsbs[i]); }
        return ms.ToArray();
    }

    private static byte[] BuildMaxp(int numGlyphs, int maxPoints, int maxContours)
    {
        var ms = new MemoryStream();
        var w = new BigEndianWriter(ms);
        w.U32(0x00010000);
        w.U16((ushort)numGlyphs);
        w.U16((ushort)maxPoints);
        w.U16((ushort)maxContours);
        w.U16(0); w.U16(0);           // composite points/contours
        w.U16(2);                     // maxZones
        w.U16(0); w.U16(0); w.U16(0); // twilight/storage/FDEFs
        w.U16(0); w.U16(0);           // IDEFs/stack
        w.U16(0);                     // maxSizeOfInstructions
        w.U16(0); w.U16(0);           // component elements/depth
        return ms.ToArray();
    }

    /// <summary>cmap with one format-4 subtable (platform 3, encoding 1) built from the
    /// CFF code→gid map. Codes above 0xFFFF are dropped (format 4 is BMP-only).</summary>
    /// <summary>Add a Windows-Unicode (3,1) format-4 cmap to an sfnt that lacks one —
    /// the shape of a CID-keyed subset extracted from a PDF, whose glyphs are
    /// addressed by GID and whose char mapping lives only in the PDF dictionaries.
    /// A program carrying only legacy byte subtables (a (1,0) Mac-Roman table is
    /// how CJK subsets commonly ship) keeps them and gains the Unicode subtable
    /// alongside — without it the HTML consumer cannot reach a single ideograph.
    /// Returns the rebuilt sfnt, or null when the program already maps Unicode,
    /// the mapping is empty, or the directory can't be parsed.</summary>
    internal static byte[]? TryAddUnicodeCmap(byte[] ttf, Dictionary<int, int> unicodeToGid)
    {
        try
        {
            if (ttf.Length < 12 || unicodeToGid.Count == 0) return null;
            static ushort U16At(byte[] b, int o) => (ushort)((b[o] << 8) | b[o + 1]);
            static uint U32At(byte[] b, int o) =>
                (uint)((b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]);
            int numTables = U16At(ttf, 4);
            var tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
            var glyphCount = 0;
            byte[]? existingCmap = null;
            for (var i = 0; i < numTables; i++)
            {
                var e = 12 + i * 16;
                var tag = System.Text.Encoding.ASCII.GetString(ttf, e, 4);
                var off = (int)U32At(ttf, e + 8);
                var len = (int)U32At(ttf, e + 12);
                if (off < 0 || len < 0 || off + len > ttf.Length) return null;
                var data = new byte[len];
                Array.Copy(ttf, off, data, 0, len);
                if (tag == "cmap") { existingCmap = data; continue; }
                tables[tag] = data;
                if (tag == "maxp" && len >= 6) glyphCount = U16At(data, 4);
            }
            if (glyphCount == 0) return null;
            var cmap = existingCmap is null
                ? BuildCmap(unicodeToGid, glyphCount)
                : MergeUnicodeIntoCmap(existingCmap, unicodeToGid, glyphCount);
            if (cmap is null) return null;   // program already maps Unicode
            tables["cmap"] = cmap;
            return AssembleSfnt(tables, flavour: U32At(ttf, 0));
        }
        catch { return null; }
    }

    /// <summary>Rebuild a cmap table keeping its existing subtables and appending a
    /// Windows-Unicode (3,1) format-4 one. Null when a Unicode-capable subtable
    /// (platform 0, or Windows BMP/full) is already present or the table is
    /// malformed.</summary>
    private static byte[]? MergeUnicodeIntoCmap(byte[] cmap, Dictionary<int, int> unicodeToGid, int glyphCount)
    {
        static ushort U16At(byte[] b, int o) => (ushort)((b[o] << 8) | b[o + 1]);
        static uint U32At(byte[] b, int o) =>
            (uint)((b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]);
        if (cmap.Length < 4) return null;
        int n = U16At(cmap, 2);
        if (cmap.Length < 4 + n * 8) return null;
        var records = new List<(int plat, int enc, int off)>();
        for (var i = 0; i < n; i++)
        {
            var r = 4 + i * 8;
            int plat = U16At(cmap, r), enc = U16At(cmap, r + 2);
            var off = (int)U32At(cmap, r + 4);
            // Unicode platform, or Windows Unicode BMP (1) / full (10): nothing to add.
            if (plat == 0 || (plat == 3 && enc is 1 or 10)) return null;
            if (off < 4 || off >= cmap.Length) return null;
            records.Add((plat, enc, off));
        }
        // Subtable length by its format header (U16 at +2 for formats 0-6,
        // U32 at +4 for 8-13, U32 at +2 for 14).
        static int SubtableLen(byte[] b, int off)
        {
            var fmt = (ushort)((b[off] << 8) | b[off + 1]);
            return fmt switch
            {
                <= 6 => (b[off + 2] << 8) | b[off + 3],
                14 => (b[off + 2] << 24) | (b[off + 3] << 16) | (b[off + 4] << 8) | b[off + 5],
                _ => (b[off + 4] << 24) | (b[off + 5] << 16) | (b[off + 6] << 8) | b[off + 7],
            };
        }
        var unicodeSub = BuildFormat4Subtable(unicodeToGid, glyphCount);
        using var ms = new MemoryStream();
        var w = new BigEndianWriter(ms);
        w.U16(0);                         // table version
        w.U16((ushort)(n + 1));
        // Encoding records sorted by platform, then encoding; identical source
        // offsets keep pointing at one shared copy of their subtable.
        var all = new List<(int plat, int enc, int srcOff)>(records) { (3, 1, -1) };
        all.Sort((a, b) => a.plat != b.plat ? a.plat - b.plat : a.enc - b.enc);
        var dataStart = 4 + (n + 1) * 8;
        var chunks = new List<byte[]>();
        var offsetOf = new Dictionary<int, int>();   // srcOff → new offset
        var newOffsets = new int[all.Count];
        var cursor = dataStart;
        for (var i = 0; i < all.Count; i++)
        {
            var srcOff = all[i].srcOff;
            if (offsetOf.TryGetValue(srcOff, out var known)) { newOffsets[i] = known; continue; }
            byte[] chunk;
            if (srcOff < 0) chunk = unicodeSub;
            else
            {
                var len = SubtableLen(cmap, srcOff);
                if (len <= 0 || srcOff + len > cmap.Length) return null;
                chunk = new byte[len];
                Array.Copy(cmap, srcOff, chunk, 0, len);
            }
            offsetOf[srcOff] = cursor;
            newOffsets[i] = cursor;
            chunks.Add(chunk);
            cursor += chunk.Length;
        }
        for (var i = 0; i < all.Count; i++)
        {
            w.U16((ushort)all[i].plat); w.U16((ushort)all[i].enc);
            w.U32((uint)newOffsets[i]);
        }
        foreach (var c in chunks) ms.Write(c, 0, c.Length);
        return ms.ToArray();
    }

    private static byte[] BuildCmap(Dictionary<int, int> cmap, int glyphCount)
    {
        var sub = BuildFormat4Subtable(cmap, glyphCount);
        using var ms = new MemoryStream();
        var w = new BigEndianWriter(ms);
        w.U16(0);                         // table version
        w.U16(1);                         // one subtable
        w.U16(3); w.U16(1);               // Windows / Unicode BMP
        w.U32(12);                        // offset to subtable
        ms.Write(sub, 0, sub.Length);
        return ms.ToArray();
    }

    private static byte[] BuildFormat4Subtable(Dictionary<int, int> cmap, int glyphCount)
    {
        var pairs = new SortedDictionary<int, int>();
        foreach (var kv in cmap)
            if (kv.Key is > 0 and <= 0xFFFE && kv.Value > 0 && kv.Value < glyphCount)
                pairs[kv.Key] = kv.Value;

        // Contiguous code runs become format-4 segments; the final 0xFFFF sentinel
        // segment is required by the format.
        var segments = new List<(int start, int end, List<int> gids)>();
        foreach (var kv in pairs)
        {
            if (segments.Count > 0 && segments[^1].end + 1 == kv.Key)
            {
                var last = segments[^1];
                last.gids.Add(kv.Value);
                segments[^1] = (last.start, kv.Key, last.gids);
            }
            else
                segments.Add((kv.Key, kv.Key, new List<int> { kv.Value }));
        }
        segments.Add((0xFFFF, 0xFFFF, new List<int> { 0 }));

        var segCount = segments.Count;
        using var sub = new MemoryStream();
        var sw = new BigEndianWriter(sub);
        sw.U16(4);                        // format
        // glyph ids go through the glyphIdArray (idRangeOffset path) so segments
        // need not be arithmetic progressions of gid.
        var glyphIdArray = new List<int>();
        var idRangeOffsets = new int[segCount];
        for (var i = 0; i < segCount; i++)
        {
            var (_, _, gids) = segments[i];
            if (i == segCount - 1) { idRangeOffsets[i] = 0; continue; } // sentinel maps via idDelta 1
            idRangeOffsets[i] = (segCount - i + glyphIdArray.Count) * 2;
            glyphIdArray.AddRange(gids);
        }
        var length = 16 + segCount * 8 + glyphIdArray.Count * 2;
        sw.U16((ushort)length);
        sw.U16(0);                        // language
        sw.U16((ushort)(segCount * 2));
        var searchRange = 2; while (searchRange * 2 <= segCount * 2) searchRange *= 2;
        sw.U16((ushort)searchRange);
        sw.U16((ushort)(Math.Log2(searchRange / 2) is var l && l > 0 ? (int)l : 0));
        sw.U16((ushort)(segCount * 2 - searchRange));
        foreach (var s in segments) sw.U16((ushort)s.end);
        sw.U16(0);                        // reservedPad
        foreach (var s in segments) sw.U16((ushort)s.start);
        for (var i = 0; i < segCount; i++) sw.I16((short)(i == segCount - 1 ? 1 : 0)); // idDelta
        foreach (var o in idRangeOffsets) sw.U16((ushort)o);
        foreach (var g in glyphIdArray) sw.U16((ushort)g);
        return sub.ToArray();
    }

    private static byte[] BuildName()
    {
        // A structurally valid, empty name table (0 records).
        var ms = new MemoryStream();
        var w = new BigEndianWriter(ms);
        w.U16(0); w.U16(0); w.U16(6);
        return ms.ToArray();
    }

    private static byte[] BuildPost()
    {
        var ms = new MemoryStream();
        var w = new BigEndianWriter(ms);
        w.U32(0x00030000);                // format 3.0: no glyph names
        w.U32(0);                         // italicAngle
        w.I16(0); w.I16(0);               // underline position/thickness
        w.U32(0);                         // isFixedPitch
        w.U32(0); w.U32(0); w.U32(0); w.U32(0); // memory hints
        return ms.ToArray();
    }

    private static byte[] BuildOs2(int[] advances, short yMax, short yMin, Dictionary<int, int> cmap)
    {
        long sum = 0;
        foreach (var a in advances) sum += a;
        var avg = advances.Length > 0 ? (short)(sum / advances.Length) : (short)500;
        int first = 0xFFFF, last = 0;
        foreach (var k in cmap.Keys)
        {
            if (k is <= 0 or > 0xFFFF) continue;
            if (k < first) first = k;
            if (k > last) last = k;
        }
        if (last == 0) { first = 0x20; last = 0x20; }

        var ms = new MemoryStream();
        var w = new BigEndianWriter(ms);
        w.U16(1);                         // version 1
        w.I16(avg);
        w.U16(400);                       // weight
        w.U16(5);                         // width
        w.U16(0);                         // fsType: installable
        w.I16(650); w.I16(699); w.I16(0); w.I16(140); // subscript x/y size, x/y offset
        w.I16(650); w.I16(699); w.I16(0); w.I16(479); // superscript
        w.I16(49); w.I16(258);            // strikeout size/position
        w.I16(0);                         // family class
        for (var i = 0; i < 10; i++) ms.WriteByte(0); // PANOSE
        w.U32(0); w.U32(0); w.U32(0); w.U32(0);       // unicode ranges
        ms.Write("    "u8.ToArray(), 0, 4);           // vendor id
        w.U16(0x40);                      // fsSelection: REGULAR
        w.U16((ushort)first);
        w.U16((ushort)last);
        w.I16(yMax > 0 ? yMax : (short)800);          // typoAscender
        w.I16(yMin < 0 ? yMin : (short)-200);         // typoDescender
        w.I16(0);                         // typoLineGap
        w.U16((ushort)(yMax > 0 ? yMax : 800));       // winAscent
        w.U16((ushort)(yMin < 0 ? -yMin : 200));      // winDescent
        w.U32(0); w.U32(0);               // code page ranges (v1)
        return ms.ToArray();
    }

    /// <summary>Assemble the table directory + tables into an sfnt with computed
    /// per-table checksums (padding tables to 4 bytes as the format requires).
    /// <paramref name="flavour"/> overrides the sfnt version for callers rebuilding
    /// an existing program (an OTTO face keeps its CFF flavour).</summary>
    private static byte[] AssembleSfnt(SortedDictionary<string, byte[]> tables, uint flavour = 0x00010000)
    {
        var count = tables.Count;
        var ms = new MemoryStream();
        var w = new BigEndianWriter(ms);
        w.U32(flavour);
        w.U16((ushort)count);
        var entrySelector = 0; var searchRange = 16;
        while (searchRange * 2 <= count * 16) { searchRange *= 2; entrySelector++; }
        w.U16((ushort)searchRange);
        w.U16((ushort)entrySelector);
        w.U16((ushort)(count * 16 - searchRange));

        var offset = 12 + count * 16;
        var dir = new List<(string tag, uint checksum, int offset, int length)>();
        foreach (var kv in tables)
        {
            dir.Add((kv.Key, Checksum(kv.Value), offset, kv.Value.Length));
            offset += (kv.Value.Length + 3) & ~3;
        }
        foreach (var (tag, checksum, off, length) in dir)
        {
            foreach (var ch in tag) ms.WriteByte((byte)ch);
            w.U32(checksum);
            w.U32((uint)off);
            w.U32((uint)length);
        }
        foreach (var kv in tables)
        {
            ms.Write(kv.Value, 0, kv.Value.Length);
            while (ms.Length % 4 != 0) ms.WriteByte(0);
        }
        return ms.ToArray();
    }

    private static uint Checksum(byte[] table)
    {
        uint sum = 0;
        for (var i = 0; i < table.Length; i += 4)
        {
            uint v = 0;
            for (var j = 0; j < 4; j++)
                v = (v << 8) | (i + j < table.Length ? table[i + j] : 0u);
            unchecked { sum += v; }
        }
        return sum;
    }

    private readonly struct BigEndianWriter(MemoryStream ms)
    {
        public void U16(ushort v) { ms.WriteByte((byte)(v >> 8)); ms.WriteByte((byte)v); }
        public void I16(short v) => U16((ushort)v);
        public void U32(uint v)
        {
            ms.WriteByte((byte)(v >> 24)); ms.WriteByte((byte)(v >> 16));
            ms.WriteByte((byte)(v >> 8)); ms.WriteByte((byte)v);
        }
    }
}
