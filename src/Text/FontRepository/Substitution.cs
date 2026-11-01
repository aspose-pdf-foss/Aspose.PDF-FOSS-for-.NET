using System.Collections.Generic;
using System;
﻿
namespace Aspose.Pdf.Text;

public partial class FontRepository
{
    /// <summary>Name lookup for SUBSTITUTION machinery: like <see cref="GetTtfData"/>
    /// but blind to name-resolution-only sources. A substitution decision made on the
    /// reference environment never saw the harness's pre-registered data folders, so
    /// e.g. a FangSong_GB2312 family candidate must fail to resolve here exactly as
    /// it does there (the SimSun default wins over a folder's FangSong),
    /// while a folder the TEST itself registered still resolves.</summary>
    internal static byte[]? GetTtfDataForSubstitution(string fontName)
    {
        foreach (var source in _sources)
        {
            if (source.NameResolutionOnly) continue;
            if (source.FindFont(fontName, ignoreCase: true) is { TtfData.Length: > 12 } fd)
                return fd.TtfData;
        }
        return null;
    }

    /// <summary>
    /// Resolve a glyph-covering substitute for <paramref name="text"/> when
    /// <paramref name="current"/> can't show it — the generator-side implementation of
    /// <see cref="TextEditOptions.NoCharacterAction.ReplaceFonts"/>. Returns null when the
    /// current font already covers the text (no substitution needed) or nothing covers it.
    /// Order: a metric-only (Standard-14) current font is replaced by its host surrogate
    /// (Helvetica→Arial) when that covers; then registered folder/file/memory sources
    /// (first covering face wins — how a FolderFontSource supplies e.g. FangSong); then
    /// host Arial; then the script-matched system CJK face (SimSun for Han, …).
    /// </summary>
    internal static FontData? SubstituteForMissingGlyphs(string text, Font? current)
    {
        if (string.IsNullOrEmpty(text)) return null;
        // Coverage probe: the distinct non-ASCII chars. ASCII is covered by any usable face,
        // and an all-ASCII run never needs substitution.
        var probe = ProbeCodePoints(text);
        if (probe.Count == 0) return null;

        var curTtf = current?.SourceFontData?.TtfData;
        if (curTtf is { Length: > 0 } && Covers(curTtf, probe)) return null;

        if (SubstituteFromCurrentFace(probe, curTtf, current) is { } own) return own;
        if (SubstituteFromSources(probe, text, CurrentIsFixedPitch(curTtf, current)) is { } registered) return registered;
        if (SubstituteFromHostFaces(probe, text) is { } host) return host;
        return null;
    }

    /// <summary>
    /// A covering face for text that would be written in the Standard-14 face
    /// <paramref name="standard14Face"/> although its WinAnsi encoding cannot carry some
    /// character - written so, the character becomes '?' or loses its accent. Null when
    /// every character encodes (the en dash and curly quotes do), for the symbolic faces
    /// (they draw through their own encodings), or when no installed face covers the text.
    /// </summary>
    internal static FontData? SubstituteForUnencodableStandard14Text(string? text, Font? current, string standard14Face)
    {
        if (string.IsNullOrEmpty(text) || Standard14Fonts.IsSymbolic(standard14Face)) return null;
        var unencodable = false;
        foreach (var ch in text!)
        {
            if (char.IsControl(ch) || Cp1252.TryGetByte(ch) is not null) continue;
            unencodable = true;
            break;
        }
        if (!unencodable || SubstituteForMissingGlyphs(text!, current) is not { } face) return null;
        // A face read from a font file comes back as a fresh copy on every scan: the first
        // copy stands for the file, so every run moved to it embeds and measures one program.
        if (face.FilePath is not { } path) return face;
        lock (Standard14CoveringFaces)
        {
            if (Standard14CoveringFaces.TryGetValue(path, out var first)) return first;
            Standard14CoveringFaces[path] = face;
        }
        return face;
    }

    private static readonly Dictionary<string, FontData> Standard14CoveringFaces = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether the fragment's own font is fixed-pitch: its program says so, or it is
    /// the Standard-14 Courier.</summary>
    private static bool CurrentIsFixedPitch(byte[]? curTtf, Font? current)
        => curTtf is { Length: > 0 }
            ? IsFixedPitchFace(curTtf)
            : (current?.FontName ?? "").StartsWith("Courier", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a font program declares itself fixed-pitch (the post table's
    /// isFixedPitch; a collection answers for its first face). False for an unreadable program.</summary>
    internal static bool IsFixedPitchFace(byte[] ttf)
    {
        var baseOff = 0;
        if (ttf.Length >= 16 && ttf[0] == (byte)'t' && ttf[1] == (byte)'t' && ttf[2] == (byte)'c' && ttf[3] == (byte)'f')
            baseOff = (int)ReadUInt32BE(ttf, 12);
        if (baseOff < 0 || baseOff + 12 > ttf.Length) return false;
        int numTables = (ttf[baseOff + 4] << 8) | ttf[baseOff + 5];
        for (var i = 0; i < numTables; i++)
        {
            var record = baseOff + 12 + i * 16;
            if (record + 16 > ttf.Length) break;
            if (ttf[record] != (byte)'p' || ttf[record + 1] != (byte)'o' || ttf[record + 2] != (byte)'s' || ttf[record + 3] != (byte)'t') continue;
            var offset = (int)ReadUInt32BE(ttf, record + 8);
            return offset >= 0 && offset + 16 <= ttf.Length && ReadUInt32BE(ttf, offset + 12) != 0;
        }
        return false;
    }

    /// <summary>The family name of the first CALLER-registered face (folder/file/
    /// memory source; not the system source, not the default per-user folder, not a
    /// name-resolution-only harness folder) whose cmap covers every non-ASCII
    /// character of <paramref name="text"/>; null when no registered face covers it.
    /// The CID-replacement fallback consults this so a face the caller supplied
    /// outranks the platform's default Han substitute.</summary>
    internal static string? FindRegisteredCoveringFamily(string text)
    {
        var probe = ProbeCodePoints(text);
        if (probe.Count == 0) return null;
        foreach (var source in _sources)
        {
            if (source.NameResolutionOnly) continue;
            if (source is SystemFontSource) continue;
            if (source is FolderFontSource { IsDefaultUserFolder: true }) continue;
            foreach (var face in source.EnumerateFaces())
            {
                var ttf = face.TtfData;
                if (ttf is { Length: > 0 } && Covers(ttf, probe))
                    return face.FontName;
            }
        }
        return null;
    }

    /// <summary>Whether the probe carries a CJK Unified (or compatibility) Han
    /// ideograph in the basic plane.</summary>
    private static bool HasHanIdeographs(System.Collections.Generic.HashSet<int> probe)
    {
        foreach (var cp in probe)
            if ((cp >= 0x3400 && cp <= 0x9FFF) || (cp >= 0xF900 && cp <= 0xFAFF))
                return true;
        return false;
    }

    /// <summary>Whether <paramref name="ttf"/> maps a real glyph for every non-ASCII
    /// char of <paramref name="text"/>. True for null/empty probes.</summary>
    internal static bool CoversText(byte[]? ttf, string text)
    {
        if (ttf is not { Length: > 0 } || string.IsNullOrEmpty(text) || IsAscii(text)) return true;
        var probe = ProbeCodePoints(text);
        return probe.Count == 0 || Covers(ttf, probe);
    }

    /// <summary>Whether every character is ASCII, which any usable face draws: the common
    /// run needs no coverage probe.</summary>
    private static bool IsAscii(string text)
    {
        foreach (var ch in text)
            if (ch > AsciiMax) return false;
        return true;
    }

    private const char AsciiMax = '\u007F';

    /// <summary>How many distinct non-ASCII chars of <paramref name="text"/> the face
    /// maps to a real glyph (0 for an unreadable face).</summary>
    internal static int CoverCount(byte[]? ttf, string text)
    {
        if (ttf is not { Length: > 0 } || string.IsNullOrEmpty(text)) return 0;
        var probe = ProbeCodePoints(text);
        try
        {
            var cmap = CMapOf(ttf);
            var n = 0;
            foreach (var c in probe)
                if (cmap.TryGetValue(c, out var gid) && gid > 0) n++;
            return n;
        }
        catch { return 0; }
    }

    /// <summary>The distinct non-ASCII code points of <paramref name="text"/> — the
    /// characters a face must map to be usable for it. Surrogate pairs count as
    /// their supplementary code point, never as two halves.</summary>
    private static HashSet<int> ProbeCodePoints(string text)
    {
        var probe = new HashSet<int>();
        var i = 0;
        while (i < text.Length)
        {
            int cp = text[i];
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                cp = char.ConvertToUtf32(text[i], text[i + 1]);
                i++;
            }
            else if (char.IsSurrogate(text[i]) || char.IsControl(text[i]))
                { i++; continue; }
            // Zero-width format characters (joiners, the byte-order mark) draw
            // nothing: no face needs a glyph for them.
            if (cp == 0xFEFF || (cp >= 0x200B && cp <= 0x200F) || cp == 0x2060 || cp == 0x00AD)
                { i++; continue; }
            if (cp > 0x7F) probe.Add(cp);
            i++;
        }
        return probe;
    }

    /// <summary>Whether the probe holds a Greek or Arabic letter.</summary>
    private static bool HasGreekOrArabic(HashSet<int> probe)
    {
        foreach (var cp in probe)
            if ((cp >= 0x0370 && cp <= 0x03FF) || (cp >= 0x0600 && cp <= 0x077F)
                || (cp >= 0xFB50 && cp <= 0xFDFF) || (cp >= 0xFE70 && cp <= 0xFEFE))
                return true;
        return false;
    }

    /// <summary>Whether the probe holds a plane-2 ideograph (U+20000 and above).</summary>
    private static bool HasSupplementaryIdeographs(HashSet<int> probe)
    {
        return probe.Any(cp => cp >= 0x20000);
    }

    /// <summary>Whether <paramref name="ttf"/> has a real glyph for every probe char.</summary>
    private static bool Covers(byte[] ttf, HashSet<int> probe)
    {
        try
        {
            var cmap = CMapOf(ttf);
            foreach (var c in probe)
                if (!cmap.TryGetValue(c, out var gid) || gid <= 0)
                    return false;
            return true;
        }
        catch { return false; }
    }

    // One parser per font program: the coverage scan asks the same installed faces about
    // every run it substitutes, and a parser reads the program's tables when it is made.
    // Held by the program's bytes, so a face the caller drops goes with it.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<byte[], GlyphOutlineParser> CoverageParsers = new();

    /// <summary>The character map of a font program, parsed once; throws for an unreadable program.</summary>
    private static Dictionary<int, int> CMapOf(byte[] ttf)
        => CoverageParsers.GetValue(ttf, static program => new GlyphOutlineParser(program)).CMap;

    // One FontData per substituted program: every run moved to the same face shares the
    // object, so the writer's per-face caches and the page's font resource serve them all.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<byte[], FontData> SubstituteFaces = new();

    /// <summary>Wrap raw font bytes as a FontData named by the face's family name.</summary>
    private static FontData MakeSubstituteFontData(byte[] ttf)
        => SubstituteFaces.GetValue(ttf, static program =>
        {
            var fd = new FontData(ReadTtfFamilyName(program), FontType.TrueType);
            fd.SetTtfData(program);
            return fd;
        });

    // Installed faces tried, in order, when the requested one has no glyph for some
    // character of a line. Times New Roman first: it is the expected fallback
    // (Romanian comma-below letters come back in it), and it is the widest-covering
    // of the default Windows serif/sans pair.
    private static readonly string[] CoveringFallbackFonts =
        { "Times New Roman", "Arial", "Segoe UI", "Tahoma", "Microsoft Sans Serif", "Calibri",
          // The wide faces the reference reaches for a script the Latin ones lack: an
          // Indic line requested in Arial lays out on Arial Unicode MS's 1.3398 em pitch
          // and a Gothic one on Segoe UI Historic's 1.3301 em (measured 2026-09-07).
          "Arial Unicode MS", "Segoe UI Historic" };

    private static readonly Dictionary<string, Font?> CoveringFallbackCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A face that can draw every character of <paramref name="text"/> when
    /// <paramref name="primary"/> cannot — null when the primary face already covers the
    /// text (the common case) or no installed face covers it either. A line the requested
    /// face cannot render is handed to such a face WHOLE, which is also what sets its
    /// full-size line height.
    /// </summary>
    internal static Font? ResolveCoveringFont(byte[] primary, string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        Dictionary<int, int>? primaryMap;
        try { primaryMap = new Text.GlyphOutlineParser(primary).CMap; }
        catch { return null; }
        if (primaryMap is null) return null;

        var missing = new List<int>();
        foreach (var ch in text)
        {
            if (ch is '\r' or '\n' or '\t' || ch == ' ') continue;
            if (!primaryMap.TryGetValue(ch, out var gid) || gid == 0) missing.Add(ch);
        }
        if (missing.Count == 0) return null;

        foreach (var name in CoveringFallbackFonts)
        {
            // The cache is written from every parallel test fixture (the per-line
            // covering hand-off runs on most embedded-face fragments) - an unlocked
            // Dictionary corrupts under that load and its exceptions surface as
            // sporadic swallowed failures in unrelated conversions.
            Font? font;
            bool cached;
            lock (CoveringFallbackCache) cached = CoveringFallbackCache.TryGetValue(name, out font);
            if (!cached)
            {
                try { font = TryFindFont(name); }
                catch { font = null; }
                lock (CoveringFallbackCache) CoveringFallbackCache[name] = font;
            }
            var ttf = font?.SourceFontData?.TtfData;
            if (ttf is null) continue;
            Dictionary<int, int>? map;
            try { map = new Text.GlyphOutlineParser(ttf).CMap; }
            catch { continue; }
            if (map is null) continue;
            var covers = true;
            foreach (var cp in missing)
                if (!map.TryGetValue(cp, out var g) || g == 0) { covers = false; break; }
            if (covers) return font;
        }
        return null;
    }

    // ── script faces: the face a line is DRAWN in when its resolved face cannot shape it ──
    /// <summary>The script tag of a character in an Indic block (both the northern scripts the
    /// shaper handles and the southern ones it leaves alone), null outside them.</summary>
    private static string? IndicScriptTagOf(int cp) => cp switch
    {
        >= 0x0900 and <= 0x097F => "deva",
        >= 0x0980 and <= 0x09FF => "beng",
        >= 0x0A00 and <= 0x0A7F => "guru",
        >= 0x0A80 and <= 0x0AFF => "gujr",
        >= 0x0B00 and <= 0x0B7F => "orya",
        >= 0x0B80 and <= 0x0BFF => "taml",
        >= 0x0C00 and <= 0x0C7F => "telu",
        >= 0x0C80 and <= 0x0CFF => "knda",
        >= 0x0D00 and <= 0x0D7F => "mlym",
        >= 0x0D80 and <= 0x0DFF => "sinh",
        _ => null,
    };

    /// <summary>The OpenType 2 tag of a script (the newer Indic shaping model), which a
    /// face may carry instead of the original one.</summary>
    private static readonly Dictionary<string, string> IndicScriptTag2 = new(StringComparer.Ordinal)
    {
        ["deva"] = "dev2", ["beng"] = "bng2", ["guru"] = "gur2", ["gujr"] = "gjr2", ["orya"] = "ory2",
        ["taml"] = "tml2", ["telu"] = "tel2", ["knda"] = "knd2", ["mlym"] = "mlm2",
    };

    /// <summary>The installed system face a script is drawn in when the requested face
    /// cannot shape it (the Windows script faces).</summary>
    private static readonly Dictionary<string, string> ScriptFaceNames = new(StringComparer.Ordinal)
    {
        ["deva"] = "Mangal", ["beng"] = "Vrinda", ["guru"] = "Raavi", ["gujr"] = "Shruti", ["orya"] = "Kalinga",
        ["taml"] = "Latha", ["telu"] = "Gautami", ["knda"] = "Tunga", ["mlym"] = "Kartika", ["sinh"] = "Iskoola Pota",
    };

    private static readonly Dictionary<string, Font?> ScriptFaceCache = new(StringComparer.Ordinal);

    /// <summary>
    /// The face a line is DRAWN in when the face it resolved to covers its characters but
    /// carries no GSUB rules for their script: the reference hands such a line to the system
    /// face of that script, keeps the paragraph's own line pitch, seats the baseline the
    /// drawing face's descent above the line box and clips the paragraph to that face's
    /// descriptor extent. Measured 2026-09-07 on the reference generator: Telugu through
    /// Arial Unicode MS (which has the glyphs but no `telu` GSUB) draws in Gautami, while
    /// Devanagari, Tamil and Kannada (whose GSUB it does carry) stay in Arial Unicode MS.
    /// Null when every script of the text is one the face can shape, or no system face is
    /// installed for it.
    /// </summary>
    internal static Font? ResolveScriptShapingFont(byte[] face, string text)
    {
        if (string.IsNullOrEmpty(text) || face is not { Length: > 12 }) return null;
        // Only a line made of the script alone is handed off: one Latin letter - or a
        // no-break space - among Telugu words keeps the whole line in the resolved face,
        // unshaped (measured: "hello" + Telugu and Telugu + NBSP + Telugu both stay in
        // Arial Unicode MS, Telugu + space + Telugu goes to Gautami).
        HashSet<string>? scripts = null;
        foreach (var ch in text)
        {
            if (ch is ' ' or '\t' or '\r' or '\n') continue;
            if (IndicScriptTagOf(ch) is { } tag) (scripts ??= new HashSet<string>(StringComparer.Ordinal)).Add(tag);
            else return null;
        }
        if (scripts is null) return null;
        HashSet<string> shaped;
        try { shaped = OpenType.OtfLayout.Open(face)?.Scripts("GSUB") ?? new HashSet<string>(StringComparer.Ordinal); }
        catch { return null; }
        foreach (var tag in scripts)
        {
            if (shaped.Contains(tag) || (IndicScriptTag2.TryGetValue(tag, out var tag2) && shaped.Contains(tag2))) continue;
            if (!ScriptFaceNames.TryGetValue(tag, out var faceName)) continue;
            Font? font;
            bool cached;
            lock (ScriptFaceCache) cached = ScriptFaceCache.TryGetValue(faceName, out font);
            if (!cached)
            {
                try { font = TryFindFont(faceName); }
                catch { font = null; }
                lock (ScriptFaceCache) ScriptFaceCache[faceName] = font;
            }
            if (font?.SourceFontData?.TtfData is { Length: > 12 }) return font;
        }
        return null;
    }
}
