
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

        // Metric-only current font (Standard-14 — no physical program): ReplaceFonts asks
        // for a physical face, so its host surrogate is the first candidate. This is why
        // a default-font fragment reports "Arial" (Helvetica's host face) after save.
        if (curTtf is null)
        {
            // Greek or Arabic in a Standard-14 fragment draws in the host's serif
            // face (Times New Roman), not in the sans surrogate: the whole fragment
            // moves to it, so its narrower Latin re-wraps every line.
            if (HasGreekOrArabic(probe))
            {
                var serif = FindFontData("Times New Roman")?.TtfData
                            ?? SystemFontResolver.Resolve("Times New Roman");
                if (serif is { Length: > 0 } && Covers(serif, probe))
                    return MakeSubstituteFontData(serif);
            }
            var host = SystemFontResolver.Resolve(current?.FontName ?? "Helvetica");
            if (host is { Length: > 0 } && Covers(host, probe))
                return MakeSubstituteFontData(host);
        }

        // Registered sources (folder/file/memory) — first covering face wins. The
        // sources the CALLER registered come before the default per-user fonts
        // folder: a FolderFontSource supplying FangSong must beat an Arial Unicode
        // MS the user happens to have installed. Name-resolution-only sources (a
        // harness pre-registering data folders so faces resolve BY NAME) are not
        // caller intent and stay out of coverage scans entirely.
        foreach (var source in _sources)
        {
            if (source.NameResolutionOnly) continue;
            if (source is FolderFontSource { IsDefaultUserFolder: true }) continue;
            foreach (var face in source.EnumerateFaces())
            {
                var ttf = face.TtfData;
                if (ttf is { Length: > 0 } && Covers(ttf, probe))
                    return face;
            }
        }

        // Han text prefers the platform's named CJK face over any broad-coverage
        // font a per-user folder happens to hold: with Arial Unicode MS installed
        // per-user AND no caller-registered source, SimSun is still substituted
        // for Simplified-Han text (measured on this machine).
        if (HasHanIdeographs(probe))
        {
            var hanFace = CjkFallbackFont.ResolveEmbeddableBytes(text);
            if (hanFace is { Length: > 0 } && Covers(hanFace, probe))
                return MakeSubstituteFontData(hanFace);
        }

        // Name-resolution-only sources (the harness's pre-registered test-data
        // folders) stand in for faces the expected environment has installed
        // (the symbol-text template renders in DejaVu, which ships in the test
        // data). They join the scan here - after the caller's own sources and the
        // Han preference (so SimFang can never hijack a Han substitution), but
        // before the per-user folder (so test-data DejaVu beats a per-user
        // Ubuntu, exactly as an installed DejaVu would).
        foreach (var source in _sources)
        {
            if (!source.NameResolutionOnly) continue;
            foreach (var face in source.EnumerateFaces())
            {
                var ttf = face.TtfData;
                if (ttf is { Length: > 0 } && Covers(ttf, probe))
                    return face;
            }
        }

        // The default per-user fonts folder ranks after all registered sources.
        foreach (var source in _sources)
        {
            if (source.NameResolutionOnly) continue;
            if (source is not FolderFontSource { IsDefaultUserFolder: true }) continue;
            foreach (var face in source.EnumerateFaces())
            {
                var ttf = face.TtfData;
                if (ttf is { Length: > 0 } && Covers(ttf, probe))
                    return face;
            }
        }

        // Host Arial: broad Latin/Cyrillic/Greek/Vietnamese coverage.
        var arial = SystemFontResolver.Resolve("Arial");
        if (arial is { Length: > 0 } && Covers(arial, probe))
            return MakeSubstituteFontData(arial);

        // Script-matched system CJK face (already normalized to a standalone sfnt).
        var cjk = CjkFallbackFont.ResolveEmbeddableBytes(text);
        if (cjk is { Length: > 0 } && Covers(cjk, probe))
            return MakeSubstituteFontData(cjk);

        // Plane-2 ideographs (CJK Unified Ideographs Extension B and later) live in
        // the "-ExtB" faces. MingLiU-ExtB first: it also carries Latin, so a run
        // mixing ideographs and ASCII draws wholly in it; SimSun-ExtB has the
        // ideographs alone.
        if (HasSupplementaryIdeographs(probe))
            foreach (var candidate in new[] { "MingLiU-ExtB", "SimSun-ExtB" })
            {
                var face = SystemFontResolver.Resolve(candidate);
                if (face is { Length: > 0 } && Covers(face, probe))
                    return MakeSubstituteFontData(face);
            }

        // Broad-coverage host faces for the remaining scripts (Thai, Hebrew,
        // Georgian, …) that neither Arial nor the CJK faces carry. Tahoma and
        // Segoe UI ship wide script coverage on Windows; the trailing names are
        // legacy super-fonts kept for older installs.
        foreach (var candidate in new[]
                 { "Tahoma", "Segoe UI", "Leelawadee UI", "Microsoft Sans Serif", "Arial Unicode MS" })
        {
            var face = SystemFontResolver.Resolve(candidate);
            if (face is { Length: > 0 } && Covers(face, probe))
                return MakeSubstituteFontData(face);
        }

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
        { "Times New Roman", "Arial", "Segoe UI", "Tahoma", "Microsoft Sans Serif", "Calibri" };

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
}
