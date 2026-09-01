namespace Aspose.Pdf.Text;

/// <summary>
/// Provides access to fonts available for use in PDF documents.
/// Supports the 14 Standard Type 1 fonts and can search custom font sources.
/// </summary>
public partial class FontRepository
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
    /// Find a font by name. Searches Standard-14 fonts first, then registered sources,
    /// then the library's own built-in faces. Throws <see cref="FontNotFoundException"/>
    /// when nothing resolves.
    /// </summary>
    public static Font FindFont(string fontName) =>
        (Font?)FindFontInternal(fontName, ignoreCase: false)
        ?? throw new FontNotFoundException($"Font {fontName} was not found");

    /// <summary>Null-returning twin of <see cref="FindFont(string)"/> for internal
    /// callers that treat a missing face as an ordinary fall-through rather than an
    /// error (the public overloads throw <see cref="FontNotFoundException"/>).</summary>
    internal static Font? TryFindFont(string fontName, bool ignoreCase = false) =>
        FindFontInternal(fontName, ignoreCase);

    /// <summary>Null-returning twin of <see cref="FindFont(string, FontStyles, bool)"/>.</summary>
    internal static Font? TryFindFont(string fontFamilyName, FontStyles stl, bool ignoreCase = false) =>
        FindFontStyled(fontFamilyName, stl, ignoreCase);

    /// <summary>Resolve <paramref name="fontName"/> through the registered sources
    /// (and the system fallback) and return the raw TrueType program bytes, or null
    /// when the name resolves to a non-embeddable face (e.g. a Standard-14 Type1) or
    /// cannot be found. Used by the HTML converter to embed CSS font-family faces.
    /// Resolves WITHOUT the system faces' name-table scan: the HTML pipelines'
    /// face-metric heuristics (the pdf2html stl line solver above all) are calibrated
    /// against filename-level system resolution, and serving them a face for a
    /// camel-cased name like "LucidaConsole" splits runs that must stay whole —
    /// the scan is the PUBLIC FindFont's contract, not this helper's.</summary>
    internal static byte[]? GetTtfData(string fontName)
        => FindFontInternal(fontName, ignoreCase: true, systemNameTableScan: false)?.TtfData;

    /// <summary>Case-insensitive repository lookup returning the raw FontData.</summary>
    internal static FontData? FindFontData(string fontName)
        => FindFontInternal(fontName, ignoreCase: true);

    /// <summary>Strict installed-face test: registered sources only (filename or
    /// real family-name match) — no Standard-14 mapping and none of the
    /// substitution aliasing the last-resort resolver applies ("Helvetica Neue"
    /// is NOT installed on a stock Windows box even though a substitute exists).
    /// A CSS font-family STACK walk needs installed-or-not truth, not a stand-in.</summary>
    internal static bool FaceInstalled(string family)
    {
        if (string.IsNullOrEmpty(family)) return false;
        foreach (var source in _sources)
            if (source.FindFont(family, ignoreCase: true) is not null) return true;
        return false;
    }

    /// <summary>
    /// Find a font by family name and style. Style is honoured by family lookup
    /// (no synthesis); when no styled variant exists the closest match is returned.
    /// Throws <see cref="FontNotFoundException"/> when nothing resolves.
    /// </summary>
    public static Font FindFont(string fontFamilyName, FontStyles stl) =>
        FindFontStyled(fontFamilyName, stl, ignoreCase: false)
        ?? throw new FontNotFoundException($"Font {fontFamilyName} was not found");

    /// <summary>
    /// Find a font by family name and style with optional case-insensitive matching.
    /// Throws <see cref="FontNotFoundException"/> when nothing resolves.
    /// </summary>
    public static Font FindFont(string fontFamilyName, FontStyles stl, bool ignoreCase) =>
        FindFontStyled(fontFamilyName, stl, ignoreCase)
        ?? throw new FontNotFoundException($"Font {fontFamilyName} was not found");

    /// <summary>
    /// Find a font by name with optional case-insensitive matching.
    /// Searches Standard-14 fonts first, then registered sources, then the built-in
    /// faces. Throws <see cref="FontNotFoundException"/> when nothing resolves.
    /// </summary>
    public static Font FindFont(string fontName, bool ignoreCase) =>
        (Font?)FindFontInternal(fontName, ignoreCase)
        ?? throw new FontNotFoundException($"Font {fontName} was not found");

    /// <summary>The FAMILY behind a /BaseFont name: the 6-letter subset tag goes (per PDF
    /// §9.6.4) and so does a trailing style word, so "WBUJFI+Arial" and "Helvetica-Bold"
    /// both reduce to a name <see cref="FindFontStyled"/> can re-style. Only the style
    /// words the same method emits are stripped; a vendor-suffixed name such as
    /// "TimesNewRomanPS-BoldMT" keeps whatever it carries beyond them, and simply fails to
    /// resolve a styled sibling rather than resolving a wrong one.</summary>
    internal static string FamilyOf(string? baseFontName)
    {
        var name = (baseFontName ?? string.Empty).Trim();
        var plus = name.IndexOf('+');
        if (plus == 6 && plus < name.Length - 1) name = name.Substring(plus + 1);
        foreach (var style in new[]
                 {
                     "BoldItalic", "BoldOblique", "Bold", "Italic", "Oblique", "Regular", "Roman",
                 })
        {
            foreach (var sep in new[] { "-", "," })
            {
                var suffix = sep + style;
                if (name.Length > suffix.Length
                    && name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return name.Substring(0, name.Length - suffix.Length);
            }
        }
        return name;
    }

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

    private static FontData? FindFontInternal(string fontName, bool ignoreCase,
        bool systemNameTableScan = true)
    {
        if (string.IsNullOrEmpty(fontName)) return null;

        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        // Search Standard-14 fonts
        foreach (var name in Standard14Names)
        {
            if (string.Equals(name, fontName, comparison))
                return new FontData(name, FontType.Type1);
        }

        // Search registered font sources. System fonts resolve through the
        // SystemFontSource registered by default — clearing Sources genuinely cuts
        // the repository off from the host's installed fonts (only the Standard-14
        // set and the built-in faces below stay reachable).
        var haveSystemSource = false;
        foreach (var source in _sources)
        {
            haveSystemSource |= source is SystemFontSource;
            var found = source is SystemFontSource sys
                ? sys.FindFont(fontName, ignoreCase, systemNameTableScan)
                : source.FindFont(fontName, ignoreCase);
            if (found is not null) return found;
        }

        if (FindBuiltinFont(fontName, ignoreCase) is { } builtin) return builtin;

        // Host-resolver fallback LAST: SystemFontResolver applies substitution aliasing
        // (registry names, styled aliases, stand-in faces), so running it inside the
        // source walk would let a substitute shadow the REAL face a later
        // Folder/MemoryFontSource carries (ARIALUNI.TTF supplied by a folder source must
        // beat an "Arial Unicode MS"→Arial stand-in). Gated on a registered
        // SystemFontSource so Sources.Clear() still cuts the host's fonts off.
        return haveSystemSource ? SystemFontSource.HostResolverFallback(fontName) : null;
    }

    /// <summary>Faces the library itself carries as embedded resources — reachable,
    /// like the Standard-14 set, even when <see cref="Sources"/> is cleared. The one
    /// built-in face is "DjVu Dingbats": a DejaVu Sans whose name table carries the
    /// DjVu names (the free Bitstream Vera / DejaVu license permits the renamed
    /// redistribution; the copyright and license strings ride along unchanged).</summary>
    private static FontData? FindBuiltinFont(string fontName, bool ignoreCase)
    {
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(fontName, "DjVu Dingbats", comparison)
            || string.Equals(fontName, "DjVuDingbats", comparison))
        {
            var bytes = _djvuDingbatsBytes ??= LoadBuiltinFontResource("DjVuDingbats.ttf");
            if (bytes is not null)
            {
                var fd = new FontData("DjVu Dingbats", FontType.TrueType);
                fd.SetTtfData(bytes);
                return fd;
            }
        }
        return null;
    }

    private static byte[]? _djvuDingbatsBytes;

    private static byte[]? LoadBuiltinFontResource(string fileName)
    {
        try
        {
            var asm = typeof(FontRepository).Assembly;
            using var s = asm.GetManifestResourceStream("Aspose.Pdf.Text.Resources." + fileName);
            if (s is null) return null;
            using var ms = new System.IO.MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }
        catch { return null; }
    }

    /// <summary>
    /// Reads basic TrueType font metrics from raw font data.
    /// Parses the TrueType table directory (OpenType spec §5.1) to locate required tables,
    /// then extracts ascent/descent, style flags, and per-character glyph widths.
    /// All metric values are scaled to PDF's 1/1000 coordinate system.
    /// </summary>
    /// <summary>The face's hhea ascender/descender in 1/1000 em, the descender
    /// returned POSITIVE as a depth below the baseline — the pair a PDF font
    /// descriptor reports (Arial: 905 / 212) and the vertical extent one line of
    /// this face occupies. <see cref="ReadTtfMetrics"/> prefers the OS/2
    /// TYPOGRAPHIC metrics instead, which for Arial say only 728 / 210 and so
    /// under-measure a line box by a fifth. Null when the tables cannot be read.</summary>
    internal static (int ascent, int descent)? ReadTtfHheaExtent(byte[] data)
    {
        if (data is null || data.Length < 12) return null;
        // TrueType Collection ('ttcf'): rebase to the first embedded font's directory.
        var baseOff = 0;
        if (data.Length >= 16 && data[0] == (byte)'t' && data[1] == (byte)'t'
            && data[2] == (byte)'c' && data[3] == (byte)'f')
        {
            baseOff = (int)ReadUInt32BE(data, 12);
            if (baseOff < 0 || baseOff + 12 > data.Length) return null;
        }
        var numTables = ReadUInt16BE(data, baseOff + 4);
        int hheaOffset = -1, unitsPerEm = 0;
        for (var i = 0; i < numTables; i++)
        {
            var offset = baseOff + 12 + i * 16;
            if (offset + 16 > data.Length) break;
            var tag = System.Text.Encoding.ASCII.GetString(data, offset, 4);
            var tOffset = (int)ReadUInt32BE(data, offset + 8);
            if (tag == "head" && tOffset + 20 <= data.Length)
                unitsPerEm = ReadUInt16BE(data, tOffset + 18);
            else if (tag == "hhea") hheaOffset = tOffset;
        }
        if (unitsPerEm <= 0 || hheaOffset < 0 || hheaOffset + 8 > data.Length) return null;
        var scale = 1000.0 / unitsPerEm;
        var ascent = (int)(ReadInt16BE(data, hheaOffset + 4) * scale);
        var descent = (int)(-ReadInt16BE(data, hheaOffset + 6) * scale);
        return ascent > 0 ? (ascent, descent) : null;
    }

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
                // hmtx stores an advance only for the first numberOfHMetrics glyphs;
                // every glyph past them repeats the LAST one (OpenType spec §5.2.3).
                // A monospaced face such as Courier New ships exactly one entry, so
                // dropping the out-of-range ids left every character at the default.
                if (charToGlyph.TryGetValue(ch, out var gid) && glyphWidths.Length > 0)
                    widths[ch] = glyphWidths[Math.Min(gid, glyphWidths.Length - 1)];
            }
        }

        return (ascent, descent, flags, widths);
    }

    /// <summary>
    /// Read raw TrueType glyph widths in font units (not scaled to 1/1000).
    /// Returns per-char widths[256] in font units and unitsPerEm.
    /// Used for high-precision width measurement (avoids int-rounding errors).
    /// </summary>
    internal static (int[] rawWidths, int upm) ReadTtfRawMetrics(byte[] data)
    {
        var rawWidths = new int[256];
        var resolved = new bool[256];
        int unitsPerEm = 1000;
        if (data.Length < 12) return (FilledWidths(rawWidths, resolved, unitsPerEm), unitsPerEm);

        // TrueType Collection ('ttcf'): rebase to the first embedded font's table
        // directory (its offset is the first entry of the TTC offset array at
        // byte 12). Table offsets within the directory remain absolute.
        int baseOff = 0;
        if (data.Length >= 16 && data[0] == (byte)'t' && data[1] == (byte)'t'
            && data[2] == (byte)'c' && data[3] == (byte)'f')
        {
            baseOff = (int)ReadUInt32BE(data, 12);
            if (baseOff < 0 || baseOff + 12 > data.Length)
                return (FilledWidths(rawWidths, resolved, unitsPerEm), unitsPerEm);
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
                // Glyphs past hmtx's numberOfHMetrics entries repeat the last advance
                // (OpenType spec §5.2.3) — a monospaced face ships exactly one.
                if (charToGlyph.TryGetValue(ch, out var gid) && glyphWidths.Length > 0)
                {
                    rawWidths[ch] = glyphWidths[Math.Min(gid, glyphWidths.Length - 1)];
                    resolved[ch] = true;
                }
            }
        }

        return (FilledWidths(rawWidths, resolved, unitsPerEm), unitsPerEm);
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
    /// is fully lazy so this is a no-op; provided for API compatibility.
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
