
namespace Aspose.Pdf.Text;

public partial class FontRepository
{
    /// <summary>
    /// Read the font's full line pitch as a fraction of the em — the vertical
    /// advance used for the full-size line-spacing mode. This is the line height
    /// the font itself recommends, distinct from the typographic ascent/descent
    /// returned by <see cref="ReadTtfMetrics"/> (which feed the PDF font
    /// descriptor): it comes from the hhea ascender/descender/lineGap, falling
    /// back to the OS/2 win metrics, then the typo metrics, then a 1.2 default.
    /// Returns 0 when the data cannot be parsed so callers can apply their own
    /// fallback.
    /// </summary>
    internal static double ReadTtfLineHeightEm(byte[] data)
    {
        if (data.Length < 12) return 0;

        int baseOff = 0;
        if (data.Length >= 16 && data[0] == (byte)'t' && data[1] == (byte)'t'
            && data[2] == (byte)'c' && data[3] == (byte)'f')
        {
            baseOff = (int)ReadUInt32BE(data, 12);
            if (baseOff < 0 || baseOff + 12 > data.Length) return 0;
        }

        var numTables = ReadUInt16BE(data, baseOff + 4);
        int os2Offset = -1, hheaOffset = -1;
        int unitsPerEm = 1000;
        for (int i = 0; i < numTables; i++)
        {
            var offset = baseOff + 12 + i * 16;
            if (offset + 16 > data.Length) break;
            var tag = System.Text.Encoding.ASCII.GetString(data, offset, 4);
            var tOffset = (int)ReadUInt32BE(data, offset + 8);
            switch (tag)
            {
                case "head":
                    if (tOffset + 18 <= data.Length) unitsPerEm = ReadUInt16BE(data, tOffset + 18);
                    break;
                case "OS/2": os2Offset = tOffset; break;
                case "hhea": hheaOffset = tOffset; break;
            }
        }
        if (unitsPerEm <= 0) unitsPerEm = 1000;

        // hhea ascender (offset 4), descender (offset 6), lineGap (offset 8).
        if (hheaOffset >= 0 && hheaOffset + 10 <= data.Length)
        {
            int asc = ReadInt16BE(data, hheaOffset + 4);
            int desc = ReadInt16BE(data, hheaOffset + 6);
            int gap = ReadInt16BE(data, hheaOffset + 8);
            var sum = asc - desc + gap;
            if (sum > 0) return sum / (double)unitsPerEm;
        }
        // OS/2 usWinAscent (offset 74) + usWinDescent (offset 76), both unsigned.
        if (os2Offset >= 0 && os2Offset + 78 <= data.Length)
        {
            int wAsc = ReadUInt16BE(data, os2Offset + 74);
            int wDesc = ReadUInt16BE(data, os2Offset + 76);
            var sum = wAsc + wDesc;
            if (sum > 0) return sum / (double)unitsPerEm;
        }
        return 0;
    }

    /// <summary>
    /// The face's full vertical EXTENT as an em ratio — hhea ascender plus descender,
    /// else the OS/2 win metrics. Unlike <see cref="ReadTtfLineHeightEm"/> this leaves
    /// the hhea line GAP out: it is the box a full-size line occupies, which the
    /// reference measures without the gap. 0 when unparsable.
    /// </summary>
    internal static double ReadTtfFullExtentEm(byte[] data)
    {
        var ascent = ReadTtfLineAscentEm(data);
        if (ascent <= 0) return 0;
        var descent = ReadTtfLineDescentEm(data);
        var sum = ascent + Math.Abs(descent);
        return sum > 0 ? sum : 0;
    }

    /// <summary>The DESCENT half of <see cref="ReadTtfFullExtentEm"/> as a signed em
    /// ratio (negative below the baseline): the hhea descender, else the OS/2
    /// usWinDescent. 0 when unparsable.</summary>
    internal static double ReadTtfLineDescentEm(byte[] data)
    {
        if (data.Length < 12) return 0;

        int baseOff = 0;
        if (data.Length >= 16 && data[0] == (byte)'t' && data[1] == (byte)'t'
            && data[2] == (byte)'c' && data[3] == (byte)'f')
        {
            baseOff = (int)ReadUInt32BE(data, 12);
            if (baseOff < 0 || baseOff + 12 > data.Length) return 0;
        }

        var numTables = ReadUInt16BE(data, baseOff + 4);
        int os2Offset = -1, hheaOffset = -1;
        int unitsPerEm = 1000;
        for (int i = 0; i < numTables; i++)
        {
            var offset = baseOff + 12 + i * 16;
            if (offset + 16 > data.Length) break;
            var tag = System.Text.Encoding.ASCII.GetString(data, offset, 4);
            var tOffset = (int)ReadUInt32BE(data, offset + 8);
            switch (tag)
            {
                case "head":
                    if (tOffset + 18 <= data.Length) unitsPerEm = ReadUInt16BE(data, tOffset + 18);
                    break;
                case "OS/2": os2Offset = tOffset; break;
                case "hhea": hheaOffset = tOffset; break;
            }
        }
        if (unitsPerEm <= 0) unitsPerEm = 1000;

        if (hheaOffset >= 0 && hheaOffset + 8 <= data.Length)
        {
            int desc = ReadInt16BE(data, hheaOffset + 6);
            if (desc != 0) return desc / (double)unitsPerEm;
        }
        if (os2Offset >= 0 && os2Offset + 78 <= data.Length)
        {
            int wDesc = ReadUInt16BE(data, os2Offset + 76);
            if (wDesc > 0) return -wDesc / (double)unitsPerEm;
        }
        return 0;
    }

    /// <summary>
    /// The ASCENT half of <see cref="ReadTtfLineHeightEm"/>, as an em ratio: the hhea
    /// ascender, else the OS/2 usWinAscent. It is what a full-size line box hangs its
    /// baseline from, so the two must come from the same table pair. 0 when unparsable.
    /// </summary>
    internal static double ReadTtfLineAscentEm(byte[] data)
    {
        if (data.Length < 12) return 0;

        int baseOff = 0;
        if (data.Length >= 16 && data[0] == (byte)'t' && data[1] == (byte)'t'
            && data[2] == (byte)'c' && data[3] == (byte)'f')
        {
            baseOff = (int)ReadUInt32BE(data, 12);
            if (baseOff < 0 || baseOff + 12 > data.Length) return 0;
        }

        var numTables = ReadUInt16BE(data, baseOff + 4);
        int os2Offset = -1, hheaOffset = -1;
        int unitsPerEm = 1000;
        for (int i = 0; i < numTables; i++)
        {
            var offset = baseOff + 12 + i * 16;
            if (offset + 16 > data.Length) break;
            var tag = System.Text.Encoding.ASCII.GetString(data, offset, 4);
            var tOffset = (int)ReadUInt32BE(data, offset + 8);
            switch (tag)
            {
                case "head":
                    if (tOffset + 18 <= data.Length) unitsPerEm = ReadUInt16BE(data, tOffset + 18);
                    break;
                case "OS/2": os2Offset = tOffset; break;
                case "hhea": hheaOffset = tOffset; break;
            }
        }
        if (unitsPerEm <= 0) unitsPerEm = 1000;

        if (hheaOffset >= 0 && hheaOffset + 10 <= data.Length)
        {
            int asc = ReadInt16BE(data, hheaOffset + 4);
            if (asc > 0) return asc / (double)unitsPerEm;
        }
        if (os2Offset >= 0 && os2Offset + 76 <= data.Length)
        {
            int wAsc = ReadUInt16BE(data, os2Offset + 74);
            if (wAsc > 0) return wAsc / (double)unitsPerEm;
        }
        return 0;
    }

    /// <summary>
    /// Read the typographic descender as a signed em ratio (negative for the
    /// usual below-baseline value). Prefers OS/2 sTypoDescender (offset 70);
    /// falls back to the hhea descender (offset 6); returns 0 when neither can
    /// be parsed so callers apply their own default. Full double precision —
    /// used by form-field appearance layout where 0.01pt precision matters.
    /// </summary>
    internal static double ReadTtfTypoDescentEm(byte[] data)
    {
        if (data.Length < 12) return 0;

        int baseOff = 0;
        if (data.Length >= 16 && data[0] == (byte)'t' && data[1] == (byte)'t'
            && data[2] == (byte)'c' && data[3] == (byte)'f')
        {
            baseOff = (int)ReadUInt32BE(data, 12);
            if (baseOff < 0 || baseOff + 12 > data.Length) return 0;
        }

        var numTables = ReadUInt16BE(data, baseOff + 4);
        int os2Offset = -1, hheaOffset = -1;
        int unitsPerEm = 1000;
        for (int i = 0; i < numTables; i++)
        {
            var offset = baseOff + 12 + i * 16;
            if (offset + 16 > data.Length) break;
            var tag = System.Text.Encoding.ASCII.GetString(data, offset, 4);
            var tOffset = (int)ReadUInt32BE(data, offset + 8);
            switch (tag)
            {
                case "head":
                    if (tOffset + 18 <= data.Length) unitsPerEm = ReadUInt16BE(data, tOffset + 18);
                    break;
                case "OS/2": os2Offset = tOffset; break;
                case "hhea": hheaOffset = tOffset; break;
            }
        }
        if (unitsPerEm <= 0) unitsPerEm = 1000;

        // OS/2 sTypoDescender (offset 70, signed Int16).
        if (os2Offset >= 0 && os2Offset + 72 <= data.Length)
            return ReadInt16BE(data, os2Offset + 70) / (double)unitsPerEm;
        // hhea descender (offset 6, signed Int16).
        if (hheaOffset >= 0 && hheaOffset + 8 <= data.Length)
            return ReadInt16BE(data, hheaOffset + 6) / (double)unitsPerEm;
        return 0;
    }

    /// <summary>Give every character the metrics could not resolve the default
    /// 0.6 em advance, expressed in the font's OWN units — the widths are divided
    /// by unitsPerEm, so a 1000-based constant would measure a 2048-unit face at
    /// less than a third of its real width.</summary>
    private static int[] FilledWidths(int[] rawWidths, bool[] resolved, int unitsPerEm)
    {
        var fallback = 600 * unitsPerEm / 1000;
        for (int i = 0; i < rawWidths.Length; i++)
            if (!resolved[i]) rawWidths[i] = fallback;
        return rawWidths;
    }

    private static Dictionary<int, int> ReadCmapFormat4(byte[] data, int cmapOffset)
    {
        var map = new Dictionary<int, int>();
        if (cmapOffset + 4 > data.Length) return map;
        var numSubtables = ReadUInt16BE(data, cmapOffset + 2);
        for (int i = 0; i < numSubtables; i++)
        {
            var recOff = cmapOffset + 4 + i * 8;
            if (recOff + 8 > data.Length) break;
            var platformID = ReadUInt16BE(data, recOff);
            var encodingID = ReadUInt16BE(data, recOff + 2);
            var subtableOffset = cmapOffset + (int)ReadUInt32BE(data, recOff + 4);
            if (!((platformID == 3 && encodingID == 1) || platformID == 0)) continue;
            if (subtableOffset + 14 > data.Length) continue;
            var format = ReadUInt16BE(data, subtableOffset);
            if (format != 4) continue;
            var segCount = ReadUInt16BE(data, subtableOffset + 6) / 2;
            var endCodesOff = subtableOffset + 14;
            var startCodesOff = endCodesOff + segCount * 2 + 2;
            var idDeltaOff = startCodesOff + segCount * 2;
            var idRangeOff = idDeltaOff + segCount * 2;
            for (int s = 0; s < segCount; s++)
            {
                if (endCodesOff + s * 2 + 2 > data.Length) break;
                var endCode = ReadUInt16BE(data, endCodesOff + s * 2);
                var startCode = ReadUInt16BE(data, startCodesOff + s * 2);
                var idDelta = ReadInt16BE(data, idDeltaOff + s * 2);
                var idRangeOffset = ReadUInt16BE(data, idRangeOff + s * 2);
                if (startCode == 0xFFFF) break;
                for (int c = startCode; c <= endCode && c < 0xFFFF; c++)
                {
                    int glyphId;
                    if (idRangeOffset == 0) { glyphId = (c + idDelta) & 0xFFFF; }
                    else
                    {
                        var glyphOff = idRangeOff + s * 2 + idRangeOffset + (c - startCode) * 2;
                        if (glyphOff + 2 > data.Length) continue;
                        glyphId = ReadUInt16BE(data, glyphOff);
                        if (glyphId != 0) glyphId = (glyphId + idDelta) & 0xFFFF;
                    }
                    if (glyphId != 0) map.TryAdd(c, glyphId);
                }
            }
            break;
        }
        return map;
    }

    private static int ReadUInt16BE(byte[] data, int offset) =>
        (data[offset] << 8) | data[offset + 1];

    private static int ReadInt16BE(byte[] data, int offset)
    {
        var val = (data[offset] << 8) | data[offset + 1];
        return val >= 0x8000 ? val - 0x10000 : val;
    }

    private static uint ReadUInt32BE(byte[] data, int offset) =>
        ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) |
        ((uint)data[offset + 2] << 8) | data[offset + 3];

    /// <summary>
    /// Read the font family name from a TrueType name table.
    /// </summary>
    /// <summary>Read a font's FAMILY name (name-table ID 1), preferring the
    /// ENGLISH record: Windows/lang 1033 first, then Mac/lang 0, then any Windows
    /// record. CJK faces carry a localized family too (e.g. SIMFANG.TTF has both
    /// "FangSong" and lang-2052 "仿宋") and a last-record-wins read surfaces the
    /// localized one — substituted faces must report the English family.</summary>
    internal static string ReadTtfFamilyName(byte[] data)
    {
        try
        {
            if (data.Length < 12) return "Unknown";
            var numTables = ReadUInt16BE(data, 4);
            for (int i = 0; i < numTables; i++)
            {
                var offset = 12 + i * 16;
                if (offset + 16 > data.Length) break;
                if (System.Text.Encoding.ASCII.GetString(data, offset, 4) != "name") continue;
                var tableOffset = (int)ReadUInt32BE(data, offset + 8);
                if (tableOffset + 6 > data.Length) break;
                var count = ReadUInt16BE(data, tableOffset + 2);
                var stringOffset = tableOffset + ReadUInt16BE(data, tableOffset + 4);
                string? winEnglish = null, macEnglish = null, winAny = null;
                for (int r = 0; r < count; r++)
                {
                    var recOff = tableOffset + 6 + r * 12;
                    if (recOff + 12 > data.Length) break;
                    var platformID = ReadUInt16BE(data, recOff);
                    var languageID = ReadUInt16BE(data, recOff + 4);
                    var nameID = ReadUInt16BE(data, recOff + 6);
                    if (nameID != 1) continue;
                    var length = ReadUInt16BE(data, recOff + 8);
                    var strStart = stringOffset + ReadUInt16BE(data, recOff + 10);
                    if (strStart + length > data.Length) continue;
                    if (platformID == 3)
                    {
                        var v = System.Text.Encoding.BigEndianUnicode.GetString(data, strStart, length);
                        if (languageID == 0x409) winEnglish ??= v;
                        winAny ??= v;
                    }
                    else if (platformID == 1 && languageID == 0)
                    {
                        macEnglish ??= System.Text.Encoding.Latin1.GetString(data, strStart, length);
                    }
                }
                var fam = winEnglish ?? macEnglish ?? winAny;
                if (!string.IsNullOrWhiteSpace(fam)) return fam!;
                break;
            }
        }
        catch { }
        return ReadTtfFontName(data);
    }

    internal static string ReadTtfFontName(byte[] data)
    {
        if (data.Length < 12) return "Unknown";
        var numTables = ReadUInt16BE(data, 4);
        for (int i = 0; i < numTables; i++)
        {
            var offset = 12 + i * 16;
            if (offset + 16 > data.Length) break;
            var tag = System.Text.Encoding.ASCII.GetString(data, offset, 4);
            if (tag != "name") continue;
            var tableOffset = (int)ReadUInt32BE(data, offset + 8);
            return ParseNameTable(data, tableOffset);
        }
        return "Unknown";
    }

    private static string ParseNameTable(byte[] data, int tableOffset)
    {
        if (tableOffset + 6 > data.Length) return "Unknown";
        var count = ReadUInt16BE(data, tableOffset + 2);
        var stringOffset = tableOffset + ReadUInt16BE(data, tableOffset + 4);
        // Multi-language name tables (CJK fonts) can list a localized record before
        // the English one — e.g. SourceHanSerif's TC face leads with 思源宋體 —
        // so prefer English-language records and fall back to any language.
        string? fullNameEn = null, familyNameEn = null, fullName = null, familyName = null;
        for (int i = 0; i < count; i++)
        {
            var recOff = tableOffset + 6 + i * 12;
            if (recOff + 12 > data.Length) break;
            var platformID = ReadUInt16BE(data, recOff);
            var languageID = ReadUInt16BE(data, recOff + 4);
            var nameID = ReadUInt16BE(data, recOff + 6);
            var length = ReadUInt16BE(data, recOff + 8);
            var strOff = ReadUInt16BE(data, recOff + 10);
            var strStart = stringOffset + strOff;
            if (strStart + length > data.Length) continue;
            string name;
            if (platformID == 3 || platformID == 0)
                name = System.Text.Encoding.BigEndianUnicode.GetString(data, strStart, length);
            else if (platformID == 1)
                name = System.Text.Encoding.Latin1.GetString(data, strStart, length);
            else continue;
            var english = (platformID == 3 && (languageID & 0x3FF) == 0x009) // any en-* LCID
                          || (platformID == 1 && languageID == 0)
                          || platformID == 0;
            if (nameID == 4) { if (english) fullNameEn ??= name; fullName ??= name; }
            if (nameID == 1) { if (english) familyNameEn ??= name; familyName ??= name; }
        }
        return fullNameEn ?? familyNameEn ?? fullName ?? familyName ?? "Unknown";
    }
}
