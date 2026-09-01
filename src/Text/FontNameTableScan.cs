namespace Aspose.Pdf.Text;

/// <summary>Face-name lookup over font FILES: reads each file's embedded `name` table
/// and matches the query against the full name and the family — the resolution filename
/// matching can't provide (comic.ttf carries "Comic Sans MS"; a query "ComicSansMS"
/// fuzzy-matches it with spaces/hyphens stripped). Shared by <see cref="FolderFontSource"/>
/// and <see cref="SystemFontSource"/>. The names read per file are cached process-wide by
/// path so repeated misses don't re-read every font (a .ttc can be &gt;10 MB).
/// TrueType Collections carry one name PER FACE (SourceHanSerif-Regular.ttc =
/// "Source Han Serif TC" / "... SC" / …), so every face is scanned and a matching face is
/// rebuilt to a standalone sfnt so the name read and a later FontFile2 embed both see a
/// valid table directory.</summary>
internal static class FontNameTableScan
{
    // Process-wide font-file → per-face "full|family" name-table names.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string[]> _namesByPath = new();

    // Process-wide (file, face) → normalized sfnt bytes. Besides saving the re-read,
    // repeat lookups of the same face return the SAME array instance — save-time font
    // consolidation dedups embedded programs by reference, so two fragments assigned
    // FindFont("Times New Roman") twice must share one program, not embed two copies.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string path, int face), byte[]?> _sfntByFace = new();

    internal static FontData? FindByFaceName(IEnumerable<string> files, string name,
        StringComparison comparison, string normalizedName)
    {
        // A family-name query matches EVERY face of that family, so returning the first
        // one found lets the directory order pick the style: "CourierNew" resolved to
        // Courier New Italic wherever the file system handed back couri.ttf first.
        FontData? styledFallback = null;
        foreach (var file in files)
        {
            var faceNames = _namesByPath.GetOrAdd(file, static f =>
            {
                try
                {
                    var raw = System.IO.File.ReadAllBytes(f);
                    var names = new string[CjkFallbackFont.TtcFaceCount(raw)];
                    for (int i = 0; i < names.Length; i++)
                    {
                        // Store BOTH the full name and the family ("full|family"):
                        // a query may target either ("HelveticaNeueLTStd" is the
                        // family of the face whose full name is "…LTStd-Roman").
                        try
                        {
                            var sfnt = CjkFallbackFont.NormalizeToSfnt(raw, i);
                            var full = FontRepository.ReadTtfFontName(sfnt);
                            string fam;
                            try { fam = FontRepository.ReadTtfFamilyName(sfnt); } catch { fam = "Unknown"; }
                            names[i] = full + "|" + fam;
                        }
                        catch { names[i] = "Unknown"; }
                    }
                    return names;
                }
                catch { return new[] { "Unknown" }; }
            });
            for (int face = 0; face < faceNames.Length; face++)
            {
                var faceEntry = faceNames[face];
                if (string.IsNullOrEmpty(faceEntry) || faceEntry == "Unknown") continue;
                var sep = faceEntry.IndexOf('|');
                var fullN = sep < 0 ? faceEntry : faceEntry[..sep];
                var famN = sep < 0 ? string.Empty : faceEntry[(sep + 1)..];
                var actualName = fullN;
                bool Matches(string candidate) =>
                    candidate.Length > 0 && candidate != "Unknown"
                    && (string.Equals(candidate, name, comparison)
                        || string.Equals(candidate.Replace(" ", "").Replace("-", ""), normalizedName, StringComparison.OrdinalIgnoreCase));
                var fullHit = Matches(fullN);
                if (!fullHit && !Matches(famN)) continue;
                {
                    var data = _sfntByFace.GetOrAdd((file, face), static key =>
                    {
                        try { return CjkFallbackFont.NormalizeToSfnt(System.IO.File.ReadAllBytes(key.path), key.face); }
                        catch { return null; }
                    });
                    if (data is null) continue;
                    var fd = new FontData(actualName, FontType.TrueType, file);
                    fd.SetTtfData(data);
                    // The regular face carries no style in its full name, so it equals the
                    // family. Take it, or an exact full-name hit, at once; hold anything
                    // styled back for families that ship no regular face at all.
                    if (fullHit || string.Equals(fullN, famN, StringComparison.OrdinalIgnoreCase))
                        return fd;
                    styledFallback ??= fd;
                }
            }
        }
        return styledFallback;
    }
}
