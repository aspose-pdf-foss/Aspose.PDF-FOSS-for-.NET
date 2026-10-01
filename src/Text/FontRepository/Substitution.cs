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
        if (SubstituteFromSources(probe, text) is { } registered) return registered;
        if (SubstituteFromHostFaces(probe, text) is { } host) return host;
        return null;
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
        if (ttf is not { Length: > 0 } || string.IsNullOrEmpty(text)) return true;
        var probe = ProbeCodePoints(text);
        return probe.Count == 0 || Covers(ttf, probe);
    }

    /// <summary>How many distinct non-ASCII chars of <paramref name="text"/> the face
    /// maps to a real glyph (0 for an unreadable face).</summary>
    internal static int CoverCount(byte[]? ttf, string text)
    {
        if (ttf is not { Length: > 0 } || string.IsNullOrEmpty(text)) return 0;
        var probe = ProbeCodePoints(text);
        try
        {
            var parser = new GlyphOutlineParser(ttf);
            var n = 0;
            foreach (var c in probe)
                if (parser.CMap.TryGetValue(c, out var gid) && gid > 0) n++;
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
        for (var i = 0; i < text.Length; i++)
        {
            int cp = text[i];
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                cp = char.ConvertToUtf32(text[i], text[i + 1]);
                i++;
            }
            else if (char.IsSurrogate(text[i]) || char.IsControl(text[i]))
                continue;
            // Zero-width format characters (joiners, the byte-order mark) draw
            // nothing: no face needs a glyph for them.
            if (cp == 0xFEFF || (cp >= 0x200B && cp <= 0x200F) || cp == 0x2060 || cp == 0x00AD)
                continue;
            if (cp > 0x7F) probe.Add(cp);
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
        foreach (var cp in probe)
            if (cp >= 0x20000) return true;
        return false;
    }

    /// <summary>Whether <paramref name="ttf"/> has a real glyph for every probe char.</summary>
    private static bool Covers(byte[] ttf, HashSet<int> probe)
    {
        try
        {
            var parser = new GlyphOutlineParser(ttf);
            foreach (var c in probe)
                if (!parser.CMap.TryGetValue(c, out var gid) || gid <= 0)
                    return false;
            return true;
        }
        catch { return false; }
    }

    /// <summary>Wrap raw font bytes as a FontData named by the face's family name.</summary>
    private static FontData MakeSubstituteFontData(byte[] ttf)
    {
        var fd = new FontData(ReadTtfFamilyName(ttf), FontType.TrueType);
        fd.SetTtfData(ttf);
        return fd;
    }

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
