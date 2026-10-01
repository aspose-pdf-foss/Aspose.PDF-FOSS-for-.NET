
namespace Aspose.Pdf.Text;

public partial class FontRepository
{
    /// <summary>The early-return heads of the missing-glyph substitution: the current face, the registered sources and the host faces.</summary>
    private static FontData? SubstituteFromHostFaces(HashSet<int> probe, string text)
    {
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

    /// <summary></summary>
    private static FontData? SubstituteFromSources(HashSet<int> probe, string text)
    {
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
        return null;
    }

    /// <summary></summary>
    private static FontData? SubstituteFromCurrentFace(HashSet<int> probe, byte[]? curTtf, Font? current)
    {
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
        return null;
    }
}
