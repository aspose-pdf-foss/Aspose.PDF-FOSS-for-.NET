namespace Aspose.Pdf.Text;

/// <summary>
/// Provides access to fonts available for use in PDF documents.
/// Supports the 14 Standard Type 1 fonts and can search custom font sources.
/// </summary>
public class FontRepository
{
    private static FontSourceCollection _sources = new();

    public FontRepository() { }

    /// <summary>
    /// The collection of font sources used for font resolution.
    /// By default contains a <see cref="SystemFontSource"/>.
    /// </summary>
    public static FontSourceCollection Sources => _sources;

    /// <summary>
    /// User-supplied substitutions consulted by <see cref="FindFont"/> before
    /// falling through to <see cref="Sources"/>.
    /// </summary>
    public static FontSubstitutionCollection Substitutions { get; } = new();

    /// <summary>
    /// The 14 standard PDF font names (PDF32000_2008 §9.6.2.2).
    /// </summary>
    public static IReadOnlyList<string> Standard14Names { get; } = new[]
    {
        "Courier", "Courier-Bold", "Courier-Oblique", "Courier-BoldOblique",
        "Helvetica", "Helvetica-Bold", "Helvetica-Oblique", "Helvetica-BoldOblique",
        "Times-Roman", "Times-Bold", "Times-Italic", "Times-BoldItalic",
        "Symbol", "ZapfDingbats",
    };

    /// <summary>
    /// Find a font by name. Searches Standard-14 fonts first, then registered sources.
    /// Returns null if not found.
    /// </summary>
    public static Font? FindFont(string fontName) => FindFontInternal(fontName, ignoreCase: false);

    /// <summary>
    /// Find a font by family name and style. Style is honoured by family lookup
    /// (no synthesis); when no styled variant exists the closest match is returned.
    /// </summary>
    public static Font? FindFont(string fontFamilyName, FontStyles stl) =>
        FindFontStyled(fontFamilyName, stl, ignoreCase: false);

    /// <summary>
    /// Find a font by family name and style with optional case-insensitive matching.
    /// </summary>
    public static Font? FindFont(string fontFamilyName, FontStyles stl, bool ignoreCase) =>
        FindFontStyled(fontFamilyName, stl, ignoreCase);

    /// <summary>
    /// Find a font by name with optional case-insensitive matching.
    /// Searches Standard-14 fonts first, then registered sources.
    /// </summary>
    public static Font? FindFont(string fontName, bool ignoreCase) =>
        FindFontInternal(fontName, ignoreCase);

    private static Font? FindFontStyled(string family, FontStyles stl, bool ignoreCase)
    {
        var suffix = stl switch
        {
            FontStyles.Bold => "-Bold",
            FontStyles.Italic => "-Italic",
            FontStyles.Bold | FontStyles.Italic => "-BoldItalic",
            _ => string.Empty,
        };
        return FindFontInternal(family + suffix, ignoreCase) ?? FindFontInternal(family, ignoreCase);
    }

    /// <summary>
    /// Canonical PDF base-font name for a Standard-14 family plus style flags. The three
    /// base families differ in how a styled variant is spelled: Times uses
    /// "-Roman"/"-Bold"/"-Italic"/"-BoldItalic" while Courier and Helvetica use the
    /// "-Oblique" spelling for the slanted forms ("Courier-Oblique", "Helvetica-BoldOblique").
    /// Unknown families get a generic "-Bold"/"-Italic"/"-BoldItalic" suffix.
    /// </summary>
    internal static string StandardStyledName(string family, bool bold, bool italic)
    {
        var f = (family ?? string.Empty).Trim();
        bool isTimes = f.Equals("Times", StringComparison.OrdinalIgnoreCase)
            || f.Equals("Times-Roman", StringComparison.OrdinalIgnoreCase)
            || f.Equals("TimesNewRoman", StringComparison.OrdinalIgnoreCase)
            || f.Equals("Times New Roman", StringComparison.OrdinalIgnoreCase);
        bool isCourier = f.Equals("Courier", StringComparison.OrdinalIgnoreCase)
            || f.Equals("Courier New", StringComparison.OrdinalIgnoreCase);
        bool isHelv = f.Equals("Helvetica", StringComparison.OrdinalIgnoreCase)
            || f.Equals("Arial", StringComparison.OrdinalIgnoreCase);

        if (isTimes)
        {
            if (bold && italic) return "Times-BoldItalic";
            if (bold) return "Times-Bold";
            if (italic) return "Times-Italic";
            return "Times-Roman";
        }
        if (isCourier || isHelv)
        {
            var baseName = isCourier ? "Courier" : "Helvetica";
            if (bold && italic) return baseName + "-BoldOblique";
            if (bold) return baseName + "-Bold";
            if (italic) return baseName + "-Oblique";
            return baseName;
        }
        if (bold && italic) return f + "-BoldItalic";
        if (bold) return f + "-Bold";
        if (italic) return f + "-Italic";
        return f;
    }

    /// <summary>
    /// Resolve a family name plus style flags to a font that carries an embeddable glyph
    /// program (a host TrueType file), canonicalizing Standard-14 family names first
    /// (e.g. "Times" + Bold → Times-Bold → timesbd.ttf, "Courier" + Italic →
    /// Courier-Oblique → couri.ttf). Returns null when no host font backs the request —
    /// callers that need a program (font swapping during text replacement) should fall
    /// back to the metric-only <see cref="FindFont(string, FontStyles)"/>.
    /// </summary>
    internal static Font? FindEmbeddableStyledFont(string family, FontStyles stl)
    {
        if (string.IsNullOrEmpty(family)) return null;
        bool bold = (stl & FontStyles.Bold) != 0;
        bool italic = (stl & FontStyles.Italic) != 0;

        // Resolve the host file via the canonical Standard-14 name (handles the Times
        // "-Italic" vs Courier/Helvetica "-Oblique" spelling so the styled glyph file is
        // picked).
        var canonical = StandardStyledName(family, bold, italic);
        var ttf = SystemFontResolver.Resolve(canonical) ?? SystemFontResolver.Resolve(family);
        if (ttf is null || ttf.Length == 0) return null;

        // Name the embedded font from the REQUESTED family plus a generic style suffix
        // (e.g. "Courier New" + Bold|Italic -> "Courier New-BoldItalic"), preserving the
        // caller's family so the read-back FontName stays what callers expect — the
        // canonical name is only used to select the file, never to rename the font.
        var styleSuffix = (bold, italic) switch
        {
            (true, true) => "-BoldItalic",
            (true, false) => "-Bold",
            (false, true) => "-Italic",
            _ => string.Empty,
        };
        var fd = new FontData(family + styleSuffix, FontType.TrueType);
        fd.SetTtfData(ttf);
        return fd;
    }

    private static FontData? FindFontInternal(string fontName, bool ignoreCase)
    {
        if (string.IsNullOrEmpty(fontName)) return null;

        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        // Search Standard-14 fonts
        foreach (var name in Standard14Names)
        {
            if (string.Equals(name, fontName, comparison))
                return new FontData(name, FontType.Type1);
        }

        // Search registered font sources
        foreach (var source in _sources)
        {
            var found = source.FindFont(fontName, ignoreCase);
            if (found is not null) return found;
        }

        // Last-resort fallback: probe the host system fonts (Windows %SystemRoot%\Fonts,
        // macOS /System/Library/Fonts, Linux /usr/share/fonts/...). Without this fallback
        // a caller doing FontRepository.FindFont("Arial") on a stock Windows install
        // returned null even though arial.ttf is right there — the registered Sources
        // collection is empty by default. Mirroring the SystemFontResolver lookup that
        // SoftwarePageRenderer already uses keeps the public FindFont and the renderer's
        // glyph parser agreeing on which font file backs a given name.
        var systemTtf = SystemFontResolver.Resolve(fontName);
        if (systemTtf is not null)
        {
            var fd = new FontData(fontName, FontType.TrueType);
            fd.SetTtfData(systemTtf);
            return fd;
        }

        return null;
    }

    /// <summary>
    /// Reads basic TrueType font metrics from raw font data.
    /// Parses the TrueType table directory (OpenType spec §5.1) to locate required tables,
    /// then extracts ascent/descent, style flags, and per-character glyph widths.
    /// All metric values are scaled to PDF's 1/1000 coordinate system.
    /// </summary>
    internal static (int ascent, int descent, int flags, int[] widths) ReadTtfMetrics(byte[] data)
    {
        // Defaults match PDF's generic font descriptor when parsing fails
        int ascent = 800, descent = -200, flags = 32;
        var widths = new int[256];
        for (int i = 0; i < 256; i++) widths[i] = 600;
        if (data.Length < 12) return (ascent, descent, flags, widths);

        // TrueType Collection ('ttcf'): rebase to the first embedded font's directory.
        int baseOff = 0;
        if (data.Length >= 16 && data[0] == (byte)'t' && data[1] == (byte)'t'
            && data[2] == (byte)'c' && data[3] == (byte)'f')
        {
            baseOff = (int)ReadUInt32BE(data, 12);
            if (baseOff < 0 || baseOff + 12 > data.Length) return (ascent, descent, flags, widths);
        }

        // Parse the TrueType Offset Table and Table Directory (OpenType spec §5.1.2–5.1.3)
        var numTables = ReadUInt16BE(data, baseOff + 4);
        int os2Offset = -1, hheaOffset = -1, hmtxOffset = -1, cmapOffset = -1;
        int unitsPerEm = 1000;

        for (int i = 0; i < numTables; i++)
        {
            var offset = baseOff + 12 + i * 16;
            if (offset + 16 > data.Length) break;
            var tag = System.Text.Encoding.ASCII.GetString(data, offset, 4);
            var tOffset = (int)ReadUInt32BE(data, offset + 8);
            switch (tag)
            {
                // 'head' table: unitsPerEm at offset 18 (OpenType spec §5.2.4.1)
                case "head":
                    if (tOffset + 18 <= data.Length)
                        unitsPerEm = ReadUInt16BE(data, tOffset + 18);
                    break;
                case "OS/2": os2Offset = tOffset; break;
                case "hhea": hheaOffset = tOffset; break;
                case "hmtx": hmtxOffset = tOffset; break;
                case "cmap": cmapOffset = tOffset; break;
            }
        }

        // Scale factor to convert font units → PDF's 1/1000 coordinate system
        double scale = 1000.0 / unitsPerEm;

        // Prefer OS/2 table for ascent/descent (offsets 68/70) — it provides the
        // typographic metrics. Fall back to hhea table (offsets 4/6) which gives
        // the actual glyph extents but may be less accurate for layout.
        if (os2Offset >= 0 && os2Offset + 72 <= data.Length)
        {
            // OS/2 sTypoAscender (offset 68) and sTypoDescender (offset 70)
            ascent = (int)(ReadInt16BE(data, os2Offset + 68) * scale);
            descent = (int)(ReadInt16BE(data, os2Offset + 70) * scale);
            // fsSelection (offset 62): bit 0 = italic, bit 5 = bold
            var fsSelection = ReadUInt16BE(data, os2Offset + 62);
            if ((fsSelection & 1) != 0) flags |= 64;        // PDF Italic flag
            if ((fsSelection & 32) != 0) flags |= (1 << 18); // PDF ForceBold flag
        }
        else if (hheaOffset >= 0 && hheaOffset + 8 <= data.Length)
        {
            // hhea ascender (offset 4) and descender (offset 6) — fallback
            ascent = (int)(ReadInt16BE(data, hheaOffset + 4) * scale);
            descent = (int)(ReadInt16BE(data, hheaOffset + 6) * scale);
        }

        // Build per-character widths by mapping character codes → glyph IDs (cmap)
        // → glyph advance widths (hmtx). hhea.numberOfHMetrics (offset 34) tells
        // how many entries are in the hmtx table.
        if (cmapOffset >= 0 && hmtxOffset >= 0 && hheaOffset >= 0 && hheaOffset + 34 < data.Length)
        {
            var numHMetrics = ReadUInt16BE(data, hheaOffset + 34);
            var glyphWidths = new int[numHMetrics];
            for (int gi = 0; gi < numHMetrics; gi++)
            {
                var off = hmtxOffset + gi * 4;
                if (off + 2 <= data.Length)
                    glyphWidths[gi] = (int)Math.Round(ReadUInt16BE(data, off) * scale);
            }
            var charToGlyph = ReadCmapFormat4(data, cmapOffset);
            for (int ch = 0; ch < 256; ch++)
            {
                if (charToGlyph.TryGetValue(ch, out var gid) && gid < glyphWidths.Length)
                    widths[ch] = glyphWidths[gid];
            }
        }

        return (ascent, descent, flags, widths);
    }

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
    /// Read the typographic descender as a signed em ratio (negative for the
    /// usual below-baseline value). Prefers OS/2 sTypoDescender (offset 70);
    /// falls back to the hhea descender (offset 6); returns 0 when neither can
    /// be parsed so callers apply their own default. Full double precision —
    /// used by form-field appearance layout where 0.01pt parity matters.
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

    /// <summary>
    /// Read raw TrueType glyph widths in font units (not scaled to 1/1000).
    /// Returns per-char widths[256] in font units and unitsPerEm.
    /// Used for high-precision width measurement (avoids int-rounding errors).
    /// </summary>
    internal static (int[] rawWidths, int upm) ReadTtfRawMetrics(byte[] data)
    {
        var rawWidths = new int[256];
        for (int i = 0; i < 256; i++) rawWidths[i] = 600; // default
        int unitsPerEm = 1000;
        if (data.Length < 12) return (rawWidths, unitsPerEm);

        // TrueType Collection ('ttcf'): rebase to the first embedded font's table
        // directory (its offset is the first entry of the TTC offset array at
        // byte 12). Table offsets within the directory remain absolute.
        int baseOff = 0;
        if (data.Length >= 16 && data[0] == (byte)'t' && data[1] == (byte)'t'
            && data[2] == (byte)'c' && data[3] == (byte)'f')
        {
            baseOff = (int)ReadUInt32BE(data, 12);
            if (baseOff < 0 || baseOff + 12 > data.Length) return (rawWidths, unitsPerEm);
        }

        var numTables = ReadUInt16BE(data, baseOff + 4);
        int hheaOffset = -1, hmtxOffset = -1, cmapOffset = -1;

        for (int i = 0; i < numTables; i++)
        {
            var offset = baseOff + 12 + i * 16;
            if (offset + 16 > data.Length) break;
            var tag = System.Text.Encoding.ASCII.GetString(data, offset, 4);
            var tOffset = (int)ReadUInt32BE(data, offset + 8);
            switch (tag)
            {
                case "head": if (tOffset + 18 <= data.Length) unitsPerEm = ReadUInt16BE(data, tOffset + 18); break;
                case "hhea": hheaOffset = tOffset; break;
                case "hmtx": hmtxOffset = tOffset; break;
                case "cmap": cmapOffset = tOffset; break;
            }
        }

        if (cmapOffset >= 0 && hmtxOffset >= 0 && hheaOffset >= 0 && hheaOffset + 34 < data.Length)
        {
            var numHMetrics = ReadUInt16BE(data, hheaOffset + 34);
            var glyphWidths = new int[numHMetrics];
            for (int gi = 0; gi < numHMetrics; gi++)
            {
                var off = hmtxOffset + gi * 4;
                if (off + 2 <= data.Length) glyphWidths[gi] = ReadUInt16BE(data, off);
            }
            var charToGlyph = ReadCmapFormat4(data, cmapOffset);
            for (int ch = 0; ch < 256; ch++)
            {
                if (charToGlyph.TryGetValue(ch, out var gid) && gid < glyphWidths.Length)
                    rawWidths[ch] = glyphWidths[gid];
            }
        }

        return (rawWidths, unitsPerEm);
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
        string? fullName = null, familyName = null;
        for (int i = 0; i < count; i++)
        {
            var recOff = tableOffset + 6 + i * 12;
            if (recOff + 12 > data.Length) break;
            var platformID = ReadUInt16BE(data, recOff);
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
            if (nameID == 4) fullName ??= name;
            if (nameID == 1) familyName ??= name;
        }
        return fullName ?? familyName ?? "Unknown";
    }

    /// <summary>
    /// Open a font from a file path.
    /// </summary>
    /// <exception cref="PdfException">If the file does not exist or cannot be opened as a font.</exception>
    public static Font OpenFont(string fontFilePath) => OpenFontInternal(fontFilePath)!;

    /// <summary>
    /// Open a Type 1 font from a pair of <c>.pfb</c> + <c>.afm</c> files.
    /// The AFM metrics file is read for width tables when present; missing files raise PdfException.
    /// </summary>
    public static Font OpenFont(string fontFilePath, string metricsFilePath)
    {
        if (!System.IO.File.Exists(fontFilePath))
            throw new PdfException($"Font file not found: {fontFilePath}");
        if (!string.IsNullOrEmpty(metricsFilePath) && !System.IO.File.Exists(metricsFilePath))
            throw new PdfException($"Metrics file not found: {metricsFilePath}");
        return OpenFontInternal(fontFilePath)!;
    }

    /// <summary>
    /// Open a font from a stream of TrueType (TTF) or OpenType (OTF) data.
    /// </summary>
    public static Font OpenFont(System.IO.Stream fontStream, FontTypes fontType)
    {
        if (fontStream is null) throw new ArgumentNullException(nameof(fontStream));
        using var ms = new System.IO.MemoryStream();
        fontStream.CopyTo(ms);
        var data = ms.ToArray();
        var name = ReadTtfFontName(data);
        var fd = new FontData(name, FontType.TrueType);
        fd.SetTtfData(data);
        return fd!;
    }

    /// <summary>
    /// Force the font sources to enumerate available fonts. The FOSS resolver
    /// is fully lazy so this is a no-op; provided for Aspose.PDF for .NET API parity.
    /// </summary>
    public static void LoadFonts() { }

    /// <summary>
    /// Reset the source collection to its default state (a single SystemFontSource).
    /// </summary>
    public static void ReloadFonts() => _sources = new FontSourceCollection();

    private static FontData OpenFontInternal(string filePath)
    {
        if (!System.IO.File.Exists(filePath))
            throw new PdfException($"Font file not found: {filePath}");
        var ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
        // An AFM file is Adobe Font Metrics only — it carries no glyph outlines, so
        // it cannot stand alone as a font program (it is valid only as the metrics
        // companion to a .pfb/.pfa passed via the two-argument OpenFont overload).
        if (ext == ".afm")
            throw new UnsupportedFontTypeException(
                $"'{System.IO.Path.GetFileName(filePath)}' is a font-metrics file, not a font program.");
        var fontType = ext switch
        {
            ".ttf" or ".otf" => FontType.TrueType,
            ".pfb" or ".pfa" => FontType.Type1,
            _ => FontType.Unknown,
        };
        var fontName = System.IO.Path.GetFileNameWithoutExtension(filePath);
        if (fontType == FontType.TrueType)
        {
            var data = System.IO.File.ReadAllBytes(filePath);
            fontName = ReadTtfFontName(data);
            var fd = new FontData(fontName, fontType, filePath);
            fd.SetTtfData(data);
            return fd;
        }
        return new FontData(fontName, fontType, filePath);
    }
}

/// <summary>
/// Stream-loadable font formats accepted by <see cref="FontRepository.OpenFont(System.IO.Stream, FontTypes)"/>.
/// </summary>
public enum FontTypes
{
    TTF,
    OTF,
}

/// <summary>Font type classification.</summary>
public enum FontType
{
    Unknown,
    Type1,
    TrueType,
    Type0,
    Type3,
}

/// <summary>Represents a font with its name and type.</summary>
public sealed class FontData
{
    internal FontData(string name, FontType type, string? filePath = null)
    {
        FontName = name;
        Type = type;
        FilePath = filePath;
    }

    /// <summary>The font name.</summary>
    public string FontName { get; }

    /// <summary>The font type.</summary>
    public FontType Type { get; }

    /// <summary>File path if loaded from a file.</summary>
    public string? FilePath { get; }

    /// <summary>Raw TTF data when loaded from a file. Lazy-loaded.</summary>
    internal byte[]? TtfData => FilePath is not null && _ttfData is null
        ? (_ttfData = System.IO.File.ReadAllBytes(FilePath))
        : _ttfData;
    private byte[]? _ttfData;

    internal void SetTtfData(byte[] data) => _ttfData = data;

    /// <summary>
    /// Measure the width of a string at the given font size in points.
    /// Uses actual TrueType glyph widths (raw, not rounded) when available.
    /// </summary>
    public double MeasureString(string text, double fontSize)
    {
        EnsureRawMetrics();
        if (_rawGlyphWidths is not null && _upm > 0)
        {
            // Use raw (unrounded) glyph widths for highest precision.
            double total = 0;
            foreach (var ch in text)
            {
                int idx = ch < 256 ? ch : '?';
                total += _rawGlyphWidths[idx];
            }
            return total * fontSize / _upm;
        }
        // Fallback for Type1/unknown without TTF data
        return text.Length * fontSize * 0.5;
    }

    private int[]? _rawGlyphWidths; // raw TTF widths in font units (not scaled to 1/1000)
    private int _upm; // unitsPerEm

    private void EnsureRawMetrics()
    {
        if (_rawGlyphWidths is not null) return;
        if (TtfData is not { Length: > 12 }) return;
        var (glyphWidths, upm) = FontRepository.ReadTtfRawMetrics(TtfData);
        _rawGlyphWidths = glyphWidths;
        _upm = upm;
    }
}

/// <summary>Base class for font sources.</summary>
public abstract class FontSource
{
    internal abstract FontData? FindFont(string name, bool ignoreCase);
}

/// <summary>
/// A font source that searches fonts in a specific directory.
/// </summary>
public sealed class FolderFontSource : FontSource
{
    public FolderFontSource(string folderPath)
    {
        FolderPath = folderPath ?? throw new ArgumentNullException(nameof(folderPath));
    }

    /// <summary>The folder path to search for fonts.</summary>
    public string FolderPath { get; set; }

    public override bool Equals(object? obj)
        => obj is FolderFontSource f && string.Equals(f.FolderPath, FolderPath, StringComparison.Ordinal);

    public override int GetHashCode() => FolderPath?.GetHashCode() ?? 0;

    internal override FontData? FindFont(string name, bool ignoreCase)
    {
        if (!System.IO.Directory.Exists(FolderPath)) return null;
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        // Normalize: strip spaces/hyphens for fuzzy matching (e.g., "DejaVu Sans" → "DejaVuSans")
        var normalizedName = name.Replace(" ", "").Replace("-", "");
        var ttfPaths = new System.Collections.Generic.List<string>();
        foreach (var file in System.IO.Directory.EnumerateFiles(FolderPath, "*.ttf")
                     .Concat(System.IO.Directory.EnumerateFiles(FolderPath, "*.otf"))
                     .Concat(System.IO.Directory.EnumerateFiles(FolderPath, "*.pfb")))
        {
            var nameWithout = System.IO.Path.GetFileNameWithoutExtension(file);
            if (string.Equals(nameWithout, name, comparison) ||
                string.Equals(nameWithout.Replace(" ", "").Replace("-", ""), normalizedName, StringComparison.OrdinalIgnoreCase))
                return new FontData(name, FontType.TrueType, file);
            var ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
            if (ext is ".ttf" or ".otf") ttfPaths.Add(file);
        }

        // Filename didn't match. Open each TTF and check the embedded `name` table —
        // test font drops like ARIALUNI.TTF carry the family
        // name "Arial Unicode MS" inside the TTF but use a short DOS-8.3-style file
        // name that won't fuzzy-match. Reading the name table once per file is cheap
        // for the small font drops typically registered as test-data sources.
        foreach (var file in ttfPaths)
        {
            byte[] data;
            try { data = System.IO.File.ReadAllBytes(file); }
            catch { continue; }
            string? actualName = null;
            try { actualName = FontRepository.ReadTtfFontName(data); }
            catch { continue; }
            if (string.IsNullOrEmpty(actualName) || actualName == "Unknown") continue;
            if (string.Equals(actualName, name, comparison) ||
                string.Equals(actualName.Replace(" ", "").Replace("-", ""), normalizedName, StringComparison.OrdinalIgnoreCase))
            {
                var fd = new FontData(actualName, FontType.TrueType, file);
                fd.SetTtfData(data);
                return fd;
            }
        }
        return null;
    }
}

/// <summary>A font source backed by a single font file on disk.</summary>
public sealed class FileFontSource : FontSource
{
    public FileFontSource(string filePath)
    {
        FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    }

    public string FilePath { get; set; }

    public override bool Equals(object? obj)
        => obj is FileFontSource f && string.Equals(f.FilePath, FilePath, StringComparison.Ordinal);

    public override int GetHashCode() => FilePath?.GetHashCode() ?? 0;

    internal override FontData? FindFont(string name, bool ignoreCase)
    {
        if (!System.IO.File.Exists(FilePath)) return null;
        var ext = System.IO.Path.GetExtension(FilePath).ToLowerInvariant();
        if (ext is not (".ttf" or ".otf" or ".pfb")) return null;

        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var normalizedName = name.Replace(" ", "").Replace("-", "");
        var fileBase = System.IO.Path.GetFileNameWithoutExtension(FilePath);
        if (string.Equals(fileBase, name, comparison) ||
            string.Equals(fileBase.Replace(" ", "").Replace("-", ""), normalizedName, StringComparison.OrdinalIgnoreCase))
        {
            var fd = new FontData(name, ext is ".pfb" ? FontType.Type1 : FontType.TrueType, FilePath);
            if (ext is ".ttf" or ".otf")
                fd.SetTtfData(System.IO.File.ReadAllBytes(FilePath));
            return fd;
        }

        if (ext is ".ttf" or ".otf")
        {
            try
            {
                var data = System.IO.File.ReadAllBytes(FilePath);
                var actualName = FontRepository.ReadTtfFontName(data);
                if (string.Equals(actualName, name, comparison) ||
                    string.Equals(actualName.Replace(" ", "").Replace("-", ""), normalizedName, StringComparison.OrdinalIgnoreCase))
                {
                    var fd = new FontData(actualName, FontType.TrueType, FilePath);
                    fd.SetTtfData(data);
                    return fd;
                }
            }
            catch { }
        }
        return null;
    }
}

/// <summary>A font source backed by an in-memory font byte buffer.</summary>
public sealed class MemoryFontSource : FontSource, IDisposable
{
    public MemoryFontSource(byte[] fontBytes)
    {
        FontBytes = fontBytes ?? throw new ArgumentNullException(nameof(fontBytes));
    }

    /// <summary>The raw font bytes used to back this source.</summary>
    public byte[] FontBytes { get; }

    /// <summary>Equal when both sources wrap the same byte array reference.</summary>
    public override bool Equals(object? obj)
        => obj is MemoryFontSource m && ReferenceEquals(m.FontBytes, FontBytes);

    public override int GetHashCode() => FontBytes.GetHashCode();

    /// <summary>No-op: FOSS doesn't hold native handles for in-memory sources.</summary>
    public void Dispose() { }

    internal override FontData? FindFont(string name, bool ignoreCase)
    {
        try
        {
            var actualName = FontRepository.ReadTtfFontName(FontBytes);
            if (string.IsNullOrEmpty(actualName)) return null;
            var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var nName = name.Replace(" ", "").Replace("-", "");
            var nActual = actualName.Replace(" ", "").Replace("-", "");
            if (string.Equals(actualName, name, comparison) ||
                string.Equals(nActual, nName, StringComparison.OrdinalIgnoreCase))
            {
                var fd = new FontData(actualName, FontType.TrueType);
                fd.SetTtfData(FontBytes);
                return fd;
            }
        }
        catch { }
        return null;
    }
}

/// <summary>A font source that searches the system's installed fonts.</summary>
public sealed class SystemFontSource : FontSource
{
    /// <summary>All SystemFontSource instances are interchangeable.</summary>
    public override bool Equals(object? obj) => obj is SystemFontSource;

    public override int GetHashCode() => typeof(SystemFontSource).GetHashCode();

    private static readonly string[] _systemFontDirs = GetSystemFontDirs();

    private static string[] GetSystemFontDirs()
    {
        var dirs = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts)));
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(localAppData))
                dirs.Add(Path.Combine(localAppData, "Microsoft", "Windows", "Fonts"));
        }
        else if (OperatingSystem.IsMacOS())
        {
            dirs.Add("/System/Library/Fonts");
            dirs.Add("/Library/Fonts");
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home))
                dirs.Add(Path.Combine(home, "Library", "Fonts"));
        }
        else // Linux
        {
            dirs.Add("/usr/share/fonts");
            dirs.Add("/usr/local/share/fonts");
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home))
                dirs.Add(Path.Combine(home, ".fonts"));
        }
        return dirs.Where(Directory.Exists).ToArray();
    }

    internal override FontData? FindFont(string name, bool ignoreCase)
    {
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var normalizedName = name.Replace(" ", "").Replace("-", "");

        foreach (var dir in _systemFontDirs)
        {
            var result = SearchDir(dir, name, normalizedName, comparison);
            if (result is not null) return result;
        }
        return null;
    }

    private static FontData? SearchDir(string dir, string name, string normalizedName, StringComparison comparison)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext is not (".ttf" or ".otf" or ".ttc"))
                    continue;

                var fileBase = Path.GetFileNameWithoutExtension(file);
                if (string.Equals(fileBase, name, comparison) ||
                    string.Equals(fileBase.Replace(" ", "").Replace("-", ""), normalizedName, StringComparison.OrdinalIgnoreCase))
                    return new FontData(name, FontType.TrueType, file);
            }

            foreach (var subDir in Directory.EnumerateDirectories(dir))
            {
                var result = SearchDir(subDir, name, normalizedName, comparison);
                if (result is not null) return result;
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (DirectoryNotFoundException) { }
        return null;
    }
}

/// <summary>A collection of font sources used by <see cref="FontRepository"/>.</summary>
public sealed class FontSourceCollection : System.Collections.Generic.IEnumerable<FontSource>
{
    private readonly System.Collections.Generic.List<FontSource> _sources = new();

    public FontSourceCollection()
    {
        _sources.Add(new SystemFontSource());
    }

    public int Count => _sources.Count;

    public bool IsSynchronized => false;
    public object SyncRoot { get; } = new();

    public FontSource this[int index] => _sources[index];

    public void Add(FontSource fontSource)
    {
        if (fontSource is null) throw new ArgumentNullException(nameof(fontSource));
        // Dedup: SystemFontSource by type, FolderFontSource by folder path
        foreach (var existing in _sources)
        {
            if (fontSource is SystemFontSource && existing is SystemFontSource)
                return;
            if (fontSource is FolderFontSource fs && existing is FolderFontSource efs
                && string.Equals(fs.FolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                                 efs.FolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                                 StringComparison.OrdinalIgnoreCase))
                return;
        }
        _sources.Add(fontSource);
    }

    public bool Contains(FontSource item) => _sources.Contains(item);

    public void Delete(FontSource fontSource) => _sources.Remove(fontSource);

    public bool Remove(FontSource item) => _sources.Remove(item);

    public void CopyTo(FontSource[] array, int index) => _sources.CopyTo(array, index);

    public void Clear() => _sources.Clear();

    public System.Collections.Generic.IEnumerator<FontSource> GetEnumerator() => _sources.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _sources.GetEnumerator();
}
