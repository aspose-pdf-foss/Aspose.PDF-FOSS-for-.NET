using System.Text;

namespace Aspose.Pdf.Text;

/// <summary>
/// Resolves Standard 14 and common PDF font names to system TrueType font files.
/// Cross-platform: searches macOS, Linux, and Windows font directories.
/// </summary>
internal static class SystemFontResolver
{
    private static readonly Dictionary<string, byte[]?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _lock = new();

    /// <summary>
    /// Try to load TrueType font data for a PDF base font name.
    /// Returns null if the font cannot be found on the system.
    /// </summary>
    public static byte[]? Resolve(string baseFontName) =>
        Resolve(baseFontName, out _);

    /// <summary>
    /// Try to load TrueType font data for a PDF base font name.
    /// Also reports a horizontal scale factor for condensed/narrow font substitution.
    /// </summary>
    public static byte[]? Resolve(string baseFontName, out double horizontalScale)
    {
        horizontalScale = 1.0;
        baseFontName = NormalizeBaseFontName(baseFontName);
        // No horizontal scaling needed when the correct narrow font is available

        lock (_lock)
        {
            if (_cache.TryGetValue(baseFontName, out var cached))
                return cached;
        }

        var data = FindFont(baseFontName);

        lock (_lock)
        {
            _cache[baseFontName] = data;
        }
        return data;
    }

    /// <summary>
    /// Clean a PDF /BaseFont name into a plain family-and-style string suitable for
    /// looking up a host font. Handles several embedding conventions seen in the wild:
    ///   * "ABCDEF+Family" — six-letter subset prefix (PDF 32000 §9.6.4)
    ///   * "*Family" — leading asterisk marking a modified/subsetted copy
    ///   * "Family-12345" — trailing numeric subset id
    ///   * "Family-Identity-H" / "-Identity-V" / "-UCS2" — CID-encoding suffix that
    ///     isn't part of the font family name
    /// </summary>
    internal static string NormalizeBaseFontName(string name)
    {
        // 6-letter subset prefix: "ABCDEF+Helvetica"
        var plus = name.IndexOf('+');
        if (plus >= 0 && plus <= 6)
            name = name[(plus + 1)..];

        // Asterisk prefix: "*Comic Sans MS-Bold-27586-Identity-H"
        while (name.Length > 0 && name[0] == '*')
            name = name[1..];

        // CID encoding suffix — cid fonts tack on the encoding for identification
        foreach (var suffix in new[] { "-Identity-H", "-Identity-V", "-UCS2", "-UniJIS-UTF16-H" })
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                name = name[..^suffix.Length];
                break;
            }
        }

        // Trailing "-<digits>" is typically a subset ID (e.g. "-27586"); strip it.
        var dash = name.LastIndexOf('-');
        if (dash > 0 && dash < name.Length - 1)
        {
            var tail = name.AsSpan(dash + 1);
            var allDigits = true;
            foreach (var c in tail) { if (c < '0' || c > '9') { allDigits = false; break; } }
            if (allDigits) name = name[..dash];
        }

        return name;
    }

    private static byte[]? FindFont(string name)
    {
        // Map PDF Standard 14 names to common system font names/files
        var (fileName, familyName, isBold, isItalic) = MapFontName(name);

        foreach (var dir in FontDirectories())
        {
            if (!Directory.Exists(dir)) continue;

            // Try exact file name match
            if (fileName is not null)
            {
                var path = Path.Combine(dir, fileName);
                if (File.Exists(path))
                    return LoadFont(path, familyName, isBold, isItalic);
            }

            // Try common variations
            foreach (var candidate in GetCandidateFiles(familyName, isBold, isItalic))
            {
                var path = Path.Combine(dir, candidate);
                if (File.Exists(path))
                    return LoadFont(path, familyName, isBold, isItalic);
            }
        }

        return null;
    }

    private static (string? fileName, string familyName, bool isBold, bool isItalic) MapFontName(string name)
    {
        // Detect style from suffix
        var lower = name.ToLowerInvariant();
        var bold = lower.Contains("bold");
        var italic = lower.Contains("italic") || lower.Contains("oblique");

        // Extract base family by removing style suffixes and common noise
        var family = name;
        foreach (var suffix in new[] { "-BoldOblique", "-BoldItalic", "-Bold", "-Oblique",
            "-Italic", "-Roman", "-Narrow", ",Bold", ",Italic", "MT", "PS", "PSMat" })
        {
            family = family.Replace(suffix, "", StringComparison.OrdinalIgnoreCase);
        }
        // Strip trailing hyphens/commas
        family = family.TrimEnd('-', ',', ' ');

        // Detect narrow/condensed variants
        var isNarrow = lower.Contains("narrow") || lower.Contains("condensed");

        // Map known PDF families to system font files
        string? fileName = null;
        // ArialBlack must be checked before the generic Arial branch — otherwise
        // "Arial-Black" (heavy weight) gets aliased to plain Arial.
        if (family.Equals("ArialBlack", StringComparison.OrdinalIgnoreCase) ||
            family.StartsWith("Arial-Black", StringComparison.OrdinalIgnoreCase) ||
            family.StartsWith("Arial Black", StringComparison.OrdinalIgnoreCase))
        {
            family = "Arial Black";
        }
        else if (family.StartsWith("Helvetica", StringComparison.OrdinalIgnoreCase) ||
            family.StartsWith("Arial", StringComparison.OrdinalIgnoreCase))
        {
            if (isNarrow)
            {
                // Arial Narrow is metrically equivalent to Helvetica-Narrow
                fileName = null; // use candidate search
                family = "Arial Narrow";
            }
            else
            {
                fileName = "Helvetica.ttc";
                family = "Helvetica";
            }
        }
        else if (family.StartsWith("Times", StringComparison.OrdinalIgnoreCase))
        {
            fileName = "Times.ttc";
            family = "Times";
        }
        else if (family.StartsWith("Courier", StringComparison.OrdinalIgnoreCase))
        {
            fileName = "Courier.ttc";
            family = "Courier";
        }
        else if (family.Equals("Symbol", StringComparison.OrdinalIgnoreCase))
        {
            return ("Symbol.ttf", "Symbol", false, false);
        }
        else if (family.Equals("ZapfDingbats", StringComparison.OrdinalIgnoreCase))
        {
            return ("ZapfDingbats.ttf", "ZapfDingbats", false, false);
        }
        else if (family.Equals("Tahoma", StringComparison.OrdinalIgnoreCase))
        {
            // Tahoma ships with Windows; nothing to do but keep family name as-is so
            // GetCandidateFiles tries tahoma.ttf / Tahoma.ttf.
        }
        else if (family.Equals("TrebuchetMS", StringComparison.OrdinalIgnoreCase) ||
                 family.Equals("Trebuchet MS", StringComparison.OrdinalIgnoreCase) ||
                 family.Equals("TrebuchetMS-Bold", StringComparison.OrdinalIgnoreCase))
        {
            family = "Trebuchet MS";
        }
        else if (family.StartsWith("Verdana", StringComparison.OrdinalIgnoreCase))
        {
            family = "Verdana";
        }
        else if (family.StartsWith("Calibri", StringComparison.OrdinalIgnoreCase))
        {
            family = "Calibri";
        }
        else if (family.StartsWith("Cambria", StringComparison.OrdinalIgnoreCase))
        {
            family = "Cambria";
        }
        else if (family.StartsWith("Georgia", StringComparison.OrdinalIgnoreCase))
        {
            family = "Georgia";
        }
        else if (family.Replace(" ", "").StartsWith("MicrosoftSansSerif", StringComparison.OrdinalIgnoreCase))
        {
            // Microsoft Sans Serif (Windows micross.ttf, regular only) is metrically
            // close to Arial/Helvetica and lacks separate bold/italic files. Alias it to
            // Helvetica so every style resolves (arial*.ttf on Windows, Liberation/DejaVu
            // on Linux) instead of dropping the text when the named font can't be found.
            fileName = "Helvetica.ttc";
            family = "Helvetica";
        }
        else if (family.StartsWith("Univers", StringComparison.OrdinalIgnoreCase))
        {
            // Univers is a Linotype font that doesn't ship with Windows or macOS by
            // default. Fall back to Helvetica/Arial — metrically close enough that
            // the text is at least readable instead of rendering as a blank box.
            fileName = "Helvetica.ttc";
            family = "Helvetica";
        }
        else if (family.Replace(" ", "").StartsWith("CenturyGothic", StringComparison.OrdinalIgnoreCase))
        {
            // Century Gothic (a geometric sans) doesn't ship with Windows; without an
            // alias its non-embedded text drops entirely (the whole letter body
            // rendered blank). Fall back to Helvetica/Arial so the text
            // renders — another sans-serif, metrically close enough to be legible.
            fileName = "Helvetica.ttc";
            family = "Helvetica";
        }

        return (fileName, family, bold, italic);
    }

    private static IEnumerable<string> GetCandidateFiles(string family, bool bold, bool italic)
    {
        var style = (bold, italic) switch
        {
            (true, true) => "Bold Italic",
            (true, false) => "Bold",
            (false, true) => "Italic",
            _ => "",
        };

        // TrueType Collections
        yield return $"{family}.ttc";

        // Individual TTF files
        if (style.Length > 0)
        {
            yield return $"{family} {style}.ttf";
            yield return $"{family}-{style.Replace(" ", "")}.ttf";
            yield return $"{family}{style.Replace(" ", "")}.ttf";
        }
        else
        {
            // Without a requested style the bare family file is the right match; try it
            // early. When a style IS requested the bare file (often the regular face) must
            // NOT preempt the styled short-name candidates below — on case-insensitive
            // filesystems "Times.ttf" resolves to the regular times.ttf and would mask
            // timesbd.ttf/timesi.ttf — so it is deferred to the end of this method.
            yield return $"{family}.ttf";
            yield return $"{family}.otf";
        }

        // Windows-specific short filenames (lowercase, no spaces, ASCII abbreviations).
        // These are the file names Windows actually ships in C:\Windows\Fonts; they
        // don't follow the family-name convention so the generic candidates above miss
        // them. Mapping the well-known families fixes 18331_p1 (Arial Black, Trebuchet,
        // Arial Narrow), 31992 (Arial Narrow), and similar PDFs that name the system
        // font without embedding it.
        var winShort = (family, bold, italic) switch
        {
            ("Arial Narrow", false, false) => "ARIALN.TTF",
            ("Arial Narrow", true, false) => "ARIALNB.TTF",
            ("Arial Narrow", false, true) => "ARIALNI.TTF",
            ("Arial Narrow", true, true) => "ARIALNBI.TTF",
            ("Arial Black", _, _) => "ariblk.ttf",
            ("Trebuchet MS", false, false) => "trebuc.ttf",
            ("Trebuchet MS", true, false) => "trebucbd.ttf",
            ("Trebuchet MS", false, true) => "trebucit.ttf",
            ("Trebuchet MS", true, true) => "trebucbi.ttf",
            ("Tahoma", false, _) => "tahoma.ttf",
            ("Tahoma", true, _) => "tahomabd.ttf",
            ("Verdana", false, false) => "verdana.ttf",
            ("Verdana", true, false) => "verdanab.ttf",
            ("Verdana", false, true) => "verdanai.ttf",
            ("Verdana", true, true) => "verdanaz.ttf",
            ("Calibri", false, false) => "calibri.ttf",
            ("Calibri", true, false) => "calibrib.ttf",
            ("Calibri", false, true) => "calibrii.ttf",
            ("Calibri", true, true) => "calibriz.ttf",
            ("Georgia", false, false) => "georgia.ttf",
            ("Georgia", true, false) => "georgiab.ttf",
            ("Georgia", false, true) => "georgiai.ttf",
            ("Georgia", true, true) => "georgiaz.ttf",
            ("Cambria", false, false) => "cambria.ttc",
            ("Cambria", true, _) => "cambriab.ttf",
            _ => null,
        };
        if (winShort is not null) yield return winShort;

        // Arial Narrow is shipped in Office (ARIALN*.TTF) but not always in the
        // base Windows fonts directory. When ARIALN.TTF isn't there, fall back
        // to regular arial.ttf — the line spacing / hinting will be off (Arial
        // is wider than Arial Narrow) but at least the text renders. Without
        // this fallback PDFs that name /Helvetica-Narrow on machines without
        // Office produce blank pages (their `Tj` operators draw nothing because
        // the glyph parser is null).
        if (family is "Arial Narrow")
        {
            var arialStyle = (bold, italic) switch
            {
                (true, true) => "bi",
                (true, false) => "bd",
                (false, true) => "i",
                _ => "",
            };
            yield return $"arial{arialStyle}.ttf";
        }

        // On Windows there's no Helvetica.ttf — fall through to Arial which is
        // metric-compatible. Mirrors the family/Arial branch in MapFontName so a
        // /BaseFont /Helvetica request resolves to arial.ttf when Helvetica is absent.
        if (family is "Helvetica")
        {
            var arialStyle = (bold, italic) switch
            {
                (true, true) => "bi",
                (true, false) => "bd",
                (false, true) => "i",
                _ => "",
            };
            yield return $"arial{arialStyle}.ttf";
            yield return $"Arial{(arialStyle.Length > 0 ? " " + style : "")}.ttf";
        }
        // Same idea for Times → Times New Roman on Windows.
        else if (family is "Times")
        {
            var timesStyle = (bold, italic) switch
            {
                (true, true) => "bi",
                (true, false) => "bd",
                (false, true) => "i",
                _ => "",
            };
            yield return $"times{timesStyle}.ttf";
            yield return $"Times New Roman{(timesStyle.Length > 0 ? " " + style : "")}.ttf";
        }
        // Same for Courier → Courier New on Windows.
        else if (family is "Courier")
        {
            var courierStyle = (bold, italic) switch
            {
                (true, true) => "bi",
                (true, false) => "bd",
                (false, true) => "i",
                _ => "",
            };
            yield return $"cour{courierStyle}.ttf";
            yield return $"Courier New{(courierStyle.Length > 0 ? " " + style : "")}.ttf";
        }

        // Linux liberation/dejavu equivalents
        if (family is "Helvetica" or "Arial")
        {
            var lf = bold ? (italic ? "LiberationSans-BoldItalic" : "LiberationSans-Bold")
                : (italic ? "LiberationSans-Italic" : "LiberationSans-Regular");
            yield return $"{lf}.ttf";
            var df = bold ? (italic ? "DejaVuSans-BoldOblique" : "DejaVuSans-Bold")
                : (italic ? "DejaVuSans-Oblique" : "DejaVuSans");
            yield return $"{df}.ttf";
        }
        else if (family is "Times" or "Times New Roman")
        {
            var lf = bold ? (italic ? "LiberationSerif-BoldItalic" : "LiberationSerif-Bold")
                : (italic ? "LiberationSerif-Italic" : "LiberationSerif-Regular");
            yield return $"{lf}.ttf";
        }
        else if (family is "Courier" or "Courier New")
        {
            var lf = bold ? (italic ? "LiberationMono-BoldItalic" : "LiberationMono-Bold")
                : (italic ? "LiberationMono-Italic" : "LiberationMono-Regular");
            yield return $"{lf}.ttf";
        }

        // Last-resort bare-family fallback (deferred from above when a style was requested)
        // so a regular face renders rather than nothing when no styled file exists.
        if (style.Length > 0)
        {
            yield return $"{family}.ttf";
            yield return $"{family}.otf";
        }
    }

    private static IEnumerable<string> FontDirectories()
    {
        // macOS
        yield return "/System/Library/Fonts";
        yield return "/System/Library/Fonts/Supplemental";
        yield return "/Library/Fonts";
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (home.Length > 0)
            yield return Path.Combine(home, "Library/Fonts");

        // Linux
        yield return "/usr/share/fonts/truetype";
        yield return "/usr/share/fonts/truetype/liberation";
        yield return "/usr/share/fonts/truetype/dejavu";
        yield return "/usr/share/fonts/TTF";
        yield return "/usr/local/share/fonts";

        // Windows
        var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (winDir.Length > 0)
            yield return Path.Combine(winDir, "Fonts");
    }

    /// <summary>
    /// Load a TrueType font from a file. Handles .ttc (TrueType Collection) files
    /// by extracting the font matching the desired style.
    /// </summary>
    private static byte[]? LoadFont(string path, string familyName, bool bold, bool italic)
    {
        try
        {
            var data = File.ReadAllBytes(path);
            if (data.Length < 12) return null;

            // Check if it's a TTC (TrueType Collection)
            var tag = Encoding.ASCII.GetString(data, 0, 4);
            if (tag == "ttcf")
                return ExtractFromTtc(data, familyName, bold, italic);

            // Regular TTF/OTF — return as-is
            return data;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extract a single font from a TrueType Collection (.ttc) file.
    /// Matches by checking the name table for family name and OS/2 style flags.
    /// Falls back to the first font if no match is found.
    /// </summary>
    private static byte[]? ExtractFromTtc(byte[] data, string familyName, bool bold, bool italic)
    {
        if (data.Length < 12) return null;
        var numFonts = ReadUInt32BE(data, 8);
        if (numFonts == 0 || 12 + numFonts * 4 > data.Length) return null;

        // Try each font in the collection
        int bestOffset = -1;
        int firstOffset = -1;

        for (var i = 0; i < numFonts; i++)
        {
            var fontOffset = (int)ReadUInt32BE(data, 12 + i * 4);
            if (fontOffset + 12 > data.Length) continue;
            if (firstOffset < 0) firstOffset = fontOffset;

            // Check if this font matches the desired style
            if (MatchesTtcFont(data, fontOffset, bold, italic))
            {
                bestOffset = fontOffset;
                break;
            }
        }

        var offset = bestOffset >= 0 ? bestOffset : firstOffset;
        if (offset < 0) return null;

        // Return the full TTC data but with the offset information
        // The GlyphOutlineParser needs to read from this offset
        // Simplest approach: create a synthetic standalone TTF from the TTC offset
        return CreateStandaloneTtf(data, offset);
    }

    private static bool MatchesTtcFont(byte[] data, int fontOffset, bool wantBold, bool wantItalic)
    {
        // Parse table directory to find OS/2 table
        if (fontOffset + 12 > data.Length) return false;
        var numTables = ReadUInt16BE(data, fontOffset + 4);

        for (var i = 0; i < numTables; i++)
        {
            var entryOff = fontOffset + 12 + i * 16;
            if (entryOff + 16 > data.Length) break;
            var tag = Encoding.ASCII.GetString(data, entryOff, 4);

            if (tag == "OS/2")
            {
                var tableOff = (int)ReadUInt32BE(data, entryOff + 8);
                if (tableOff + 64 > data.Length) return false;

                // fsSelection at offset 62
                var fsSelection = ReadUInt16BE(data, tableOff + 62);
                var isBold = (fsSelection & 0x20) != 0; // bit 5
                var isItalic = (fsSelection & 0x01) != 0; // bit 0

                return isBold == wantBold && isItalic == wantItalic;
            }
        }
        return false;
    }

    /// <summary>
    /// Create a standalone TTF from a font within a TTC.
    /// Copies the table directory and adjusts offsets so it reads as a regular TTF.
    /// </summary>
    private static byte[]? CreateStandaloneTtf(byte[] ttcData, int fontOffset)
    {
        if (fontOffset + 12 > ttcData.Length) return null;
        var numTables = ReadUInt16BE(ttcData, fontOffset + 4);

        // Calculate total size needed
        var headerSize = 12 + numTables * 16;
        var totalSize = headerSize;
        var tables = new List<(string tag, int origOffset, int length)>();

        for (var i = 0; i < numTables; i++)
        {
            var entryOff = fontOffset + 12 + i * 16;
            if (entryOff + 16 > ttcData.Length) break;
            var tag = Encoding.ASCII.GetString(ttcData, entryOff, 4);
            var offset = (int)ReadUInt32BE(ttcData, entryOff + 8);
            var length = (int)ReadUInt32BE(ttcData, entryOff + 12);
            tables.Add((tag, offset, length));
            totalSize += (length + 3) & ~3; // pad to 4 bytes
        }

        var result = new byte[totalSize];

        // Copy the font header (sfVersion, numTables, searchRange, entrySelector, rangeShift)
        Array.Copy(ttcData, fontOffset, result, 0, 12);

        // Write table directory with new offsets and copy table data
        var dataOffset = headerSize;
        for (var i = 0; i < tables.Count; i++)
        {
            var (tag, origOffset, length) = tables[i];
            var entryOff = 12 + i * 16;

            // Copy tag and checksum from original entry
            var origEntry = fontOffset + 12 + i * 16;
            Array.Copy(ttcData, origEntry, result, entryOff, 8); // tag + checksum

            // Write new offset
            result[entryOff + 8] = (byte)(dataOffset >> 24);
            result[entryOff + 9] = (byte)(dataOffset >> 16);
            result[entryOff + 10] = (byte)(dataOffset >> 8);
            result[entryOff + 11] = (byte)(dataOffset);

            // Copy length
            Array.Copy(ttcData, origEntry + 12, result, entryOff + 12, 4);

            // Copy table data
            if (origOffset + length <= ttcData.Length)
                Array.Copy(ttcData, origOffset, result, dataOffset, length);

            dataOffset += (length + 3) & ~3;
        }

        return result;
    }

    private static uint ReadUInt32BE(byte[] data, int offset) =>
        ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) |
        ((uint)data[offset + 2] << 8) | data[offset + 3];

    private static int ReadUInt16BE(byte[] data, int offset) =>
        (data[offset] << 8) | data[offset + 1];
}
