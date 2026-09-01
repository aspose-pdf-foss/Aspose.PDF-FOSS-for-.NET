namespace Aspose.Pdf.Text;

/// <summary>
/// A font source that searches fonts in a specific directory.
/// </summary>
public sealed class FolderFontSource : FontSource
{
    /// <summary>True for the per-user Windows fonts folder the default source set
    /// carries; it yields to every source the caller registered.</summary>
    internal bool IsDefaultUserFolder { get; init; }

    public FolderFontSource(string folderPath)
    {
        FolderPath = folderPath ?? throw new ArgumentNullException(nameof(folderPath));
    }

    /// <summary>The folder path to search for fonts.</summary>
    public string FolderPath { get; set; }

    public override bool Equals(object? obj)
        => obj is FolderFontSource f && string.Equals(f.FolderPath, FolderPath, StringComparison.Ordinal);

    public override int GetHashCode() => FolderPath?.GetHashCode() ?? 0;

    /// <summary>The folder's font files, matched by extension WITHOUT regard to case and
    /// yielded in the extension order the caller listed, then by name. A `"*.ttf"` search
    /// pattern is case-insensitive on NTFS but case-SENSITIVE on ext4, so globbing hid
    /// every SIMFANG.TTF / MANGAL.TTF the test data ships whenever the folder was read on
    /// Linux. The ordering keeps the preference the concatenated globs used to give (all
    /// .ttf before any .otf) and makes it deterministic: unordered enumeration lets the
    /// file system pick which face of a family is seen first, and NTFS and ext4 differ.</summary>
    private static IEnumerable<string> FontFilesIn(string folder, params string[] extensions)
    {
        string[] files;
        try { files = System.IO.Directory.GetFiles(folder); }
        catch { return Enumerable.Empty<string>(); }
        return files
            .Select(f => (Path: f, Rank: Array.FindIndex(extensions,
                e => string.Equals(System.IO.Path.GetExtension(f), e, StringComparison.OrdinalIgnoreCase))))
            .Where(t => t.Rank >= 0)
            .OrderBy(t => t.Rank)
            .ThenBy(t => t.Path, StringComparer.OrdinalIgnoreCase)
            .Select(t => t.Path);
    }

    internal override FontData? FindFont(string name, bool ignoreCase)
    {
        if (!System.IO.Directory.Exists(FolderPath)) return null;
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        // Normalize: strip spaces/hyphens for fuzzy matching (e.g., "DejaVu Sans" → "DejaVuSans")
        var normalizedName = name.Replace(" ", "").Replace("-", "");
        var ttfPaths = new System.Collections.Generic.List<string>();
        foreach (var file in FontFilesIn(FolderPath, ".ttf", ".otf", ".ttc", ".pfb"))
        {
            var nameWithout = System.IO.Path.GetFileNameWithoutExtension(file);
            if (string.Equals(nameWithout, name, comparison) ||
                string.Equals(nameWithout.Replace(" ", "").Replace("-", ""), normalizedName, StringComparison.OrdinalIgnoreCase))
                return new FontData(name, FontType.TrueType, file);
            var ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
            if (ext is ".ttf" or ".otf" or ".ttc") ttfPaths.Add(file);
        }

        // Filename didn't match — fall back to the embedded name-table scan.
        return FontNameTableScan.FindByFaceName(ttfPaths, name, comparison, normalizedName);
    }

    internal override IEnumerable<FontData> EnumerateFaces()
    {
        if (!System.IO.Directory.Exists(FolderPath)) yield break;
        var files = FontFilesIn(FolderPath, ".ttf", ".otf");
        foreach (var file in files)
        {
            byte[] data;
            try { data = System.IO.File.ReadAllBytes(file); }
            catch { continue; }
            var name = "Unknown";
            try { name = FontRepository.ReadTtfFamilyName(data); } catch { }
            if (name == "Unknown")
                name = System.IO.Path.GetFileNameWithoutExtension(file);
            var fd = new FontData(name, FontType.TrueType, file);
            fd.SetTtfData(data);
            yield return fd;
        }
    }
}
