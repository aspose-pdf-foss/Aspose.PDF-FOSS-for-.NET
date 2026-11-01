namespace Aspose.Pdf.Text;

/// <summary>A font source that searches the system's installed fonts.</summary>
public sealed class SystemFontSource : FontSource
{
    /// <summary>All SystemFontSource instances are interchangeable.</summary>
    public override bool Equals(object? obj) => obj is SystemFontSource;

    public override int GetHashCode() => typeof(SystemFontSource).GetHashCode();

    private static readonly string[] _systemFontDirs = GetSystemFontDirs();

    /// <summary>
    /// Every font file under a system font directory, INCLUDING nested ones. Windows keeps
    /// its fonts in one flat folder, so a plain EnumerateFiles was enough there - but Linux
    /// nests by convention (ttf-mscorefonts-installer lands in
    /// /usr/share/fonts/truetype/msttcorefonts, a user drop in ~/.local/share/fonts/&lt;vendor&gt;)
    /// and fontconfig recurses, so a flat walk made every one of those faces invisible: a
    /// document naming Times New Roman resolved to whatever generic face came last instead of
    /// the times.ttf sitting one directory down. SystemFontResolver has walked subdirectories
    /// for a while; this is the repository's half of the same rule.
    /// </summary>
    private static IEnumerable<string> FontFilesUnder(string dir)
    {
        var stamp = TreeStamp(dir);
        if (stamp is not null && _filesByDir.TryGetValue(dir, out var listed) && listed.Stamp == stamp)
            return listed.Files;
        IEnumerable<string> files;
        try
        {
            // Ordered so the file system does not decide which face of a family is seen
            // first - that differs between NTFS and ext4.
            files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".ttf" or ".otf" or ".ttc")
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
        }
        catch { return Array.Empty<string>(); }
        var array = files.ToArray(); // an unreadable folder throws here, as the walk over it did
        if (stamp is not null) _filesByDir[dir] = (stamp.Value, array);
        return array;
    }

    // Font files per directory, listed again only once the directory tree has changed: every
    // lookup walks the system fonts, and a document looks a font up for each fragment.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime Stamp, string[] Files)> _filesByDir = new();

    /// <summary>The latest write time over a directory and every directory under it - a file
    /// added, removed or renamed anywhere in the tree moves it; null when it cannot be read.
    /// Taken again at most once a second: walking the tree costs more than the lookup it
    /// guards, and a document looks a font up for every fragment.</summary>
    private static DateTime? TreeStamp(string dir)
    {
        var now = DateTime.UtcNow;
        if (_stampByDir.TryGetValue(dir, out var known) && now - known.CheckedAt < StampRecheck) return known.Stamp;
        var stamp = WalkStamp(dir);
        _stampByDir[dir] = (now, stamp);
        return stamp;
    }

    private static readonly TimeSpan StampRecheck = TimeSpan.FromSeconds(1);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime CheckedAt, DateTime? Stamp)> _stampByDir = new();

    private static DateTime? WalkStamp(string dir)
    {
        try
        {
            var stamp = Directory.GetLastWriteTimeUtc(dir);
            foreach (var sub in Directory.EnumerateDirectories(dir, "*", SearchOption.AllDirectories))
            {
                var written = Directory.GetLastWriteTimeUtc(sub);
                if (written > stamp) stamp = written;
            }
            return stamp;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static string[] GetSystemFontDirs()
    {
        var dirs = new List<string>();
        if (Compat.IsWindows())
        {
            dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts)));
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(localAppData))
                dirs.Add(Path.Combine(localAppData, "Microsoft", "Windows", "Fonts"));
        }
        else if (Compat.IsMacOS())
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
            var xdgData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (!string.IsNullOrEmpty(xdgData))
                dirs.Add(Path.Combine(xdgData, "fonts"));
            else if (!string.IsNullOrEmpty(home))
                dirs.Add(Path.Combine(home, ".local", "share", "fonts"));
        }
        // The roots only: FontFilesUnder lists each root's whole tree, so a nested folder's
        // faces are seen under its root, and a pass of its own found nothing new.
        return dirs.Where(Directory.Exists).Distinct(StringComparer.Ordinal).ToArray();
    }

    internal override FontData? FindFont(string name, bool ignoreCase)
        => FindFont(name, ignoreCase, nameTableScan: true);

    /// <summary>Finds an installed font by name in the system font folders.</summary>
    /// <param name="name">The font name to look for.</param>
    /// <param name="ignoreCase">True to compare names case-insensitively.</param>
    /// <param name="nameTableScan">Match installed faces by their embedded name tables
    /// after the filename walk. The public FindFont contract needs it (comic.ttf carries
    /// "Comic Sans MS"); internal pipeline helpers calibrated against filename-level
    /// resolution (<see cref="FontRepository.GetTtfData"/>) pass false.</param>
    internal static FontData? FindFont(string name, bool ignoreCase, bool nameTableScan)
    {
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var normalizedName = name.Replace(" ", "").Replace("-", "");

        foreach (var dir in _systemFontDirs)
        {
            var result = SearchDir(dir, name, normalizedName, comparison);
            if (result is not null) return result;
        }

        if (!nameTableScan) return null;

        // The filename walk misses faces whose family doesn't resemble the file name
        // (comic.ttf = "Comic Sans MS"). Match against the embedded name tables the way
        // FolderFontSource does — "ComicSansMS" and "Tahoma-Bold" both resolve through
        // the fuzzy (space/hyphen-stripped) face-name comparison.
        foreach (var dir in _systemFontDirs)
        {
            var files = FontFilesUnder(dir);
            if (FontNameTableScan.FindByFaceName(files, name, comparison, normalizedName) is { } byFace)
                return byFace;
        }

        return null;
    }

    /// <summary>The SystemFontResolver lookup the SoftwarePageRenderer already uses, so
    /// the repository and the renderer's glyph parser agree on which font file backs a
    /// given name. It applies substitution aliasing, so <see cref="FontRepository"/>
    /// consults it only AFTER every registered source missed — a stand-in must never
    /// shadow a real face a later source carries — and only while a SystemFontSource is
    /// registered, so clearing Sources cuts the host's fonts off.</summary>
    internal static FontData? HostResolverFallback(string name)
    {
        var systemTtf = SystemFontResolver.Resolve(name);
        if (systemTtf is not null)
        {
            // Name the resolved face from its own 'name' table rather than the caller's
            // query string: FindFont("arial") must yield a font whose FontName is "Arial"
            // (the real family), so an embedded round-trip reports "Arial" not "arial".
            // Falls back to the query when the family can't be parsed.
            var realName = name;
            try
            {
                var ttp = new TrueTypeParser(systemTtf);
                ttp.Parse();
                var fam = ttp.FamilyName;
                if (!string.IsNullOrWhiteSpace(fam) && fam != "Unknown")
                    realName = FaceName(fam, ttp.SubfamilyName);
            }
            catch { /* keep the query name if the face can't be parsed */ }

            var fd = new FontData(realName, FontType.TrueType);
            fd.SetTtfData(systemTtf);
            return fd;
        }
        return null;
    }

    /// <summary>The name a face reports: its family, and its subfamily after it when that is
    /// a style the family name does not already carry ("Times New Roman Bold Italic"), so an
    /// embedded round-trip reports the styled face, not the family.</summary>
    internal static string FaceName(string family, string? subfamily)
        => !string.IsNullOrWhiteSpace(subfamily)
           && !subfamily.Equals("Regular", StringComparison.OrdinalIgnoreCase)
           && !subfamily.Equals("Normal", StringComparison.OrdinalIgnoreCase)
           && !family.EndsWith(subfamily, StringComparison.OrdinalIgnoreCase)
            ? family + " " + subfamily
            : family;

    internal static FontData? SearchDir(string dir, string name, string normalizedName, StringComparison comparison)
    {
        try
        {
            foreach (var file in FontFilesUnder(dir))
            {

                var fileBase = Path.GetFileNameWithoutExtension(file);
                if (string.Equals(fileBase, name, comparison) ||
                    string.Equals(fileBase.Replace(" ", "").Replace("-", ""), normalizedName, StringComparison.OrdinalIgnoreCase))
                    // Name the face from its own 'name' table, not the query string, so
                    // FindFont("arial") yields a font whose FontName is "Arial" (real
                    // family) and "Times New Roman Italic.ttf", as macOS names it, a face
                    // whose name keeps its style. The query string is only used to locate
                    // the file; fall back to it if the face can't be parsed (e.g. an
                    // unusual .ttc layout).
                    return new FontData(RealFaceName(file) ?? name, FontType.TrueType, file);
            }
        }
        catch (UnauthorizedAccessException) { /* a folder the process cannot read holds no font for it */ }
        catch (DirectoryNotFoundException) { /* a folder that vanished while being searched holds no font */ }
        return null;
    }

    /// <summary>Read a font file's face name (<see cref="FaceName"/>) from its 'name' table;
    /// null when it can't be parsed. Used to report the real font name from a filename match.</summary>
    private static string? RealFaceName(string file)
    {
        DateTime written;
        try { written = File.GetLastWriteTimeUtc(file); }
        catch (IOException) { return ReadFaceName(file); }
        catch (UnauthorizedAccessException) { return ReadFaceName(file); }
        if (_faceByFile.TryGetValue(file, out var known) && known.Written == written) return known.Face;
        var face = ReadFaceName(file);
        _faceByFile[file] = (written, face);
        return face;
    }

    // A file's face name, kept while the file is unchanged: reading it parses the whole font.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime Written, string? Face)> _faceByFile = new();

    private static string? ReadFaceName(string file)
    {
        try
        {
            var ttp = new TrueTypeParser(File.ReadAllBytes(file));
            ttp.Parse();
            var fam = ttp.FamilyName;
            return string.IsNullOrWhiteSpace(fam) || fam == "Unknown" ? null : FaceName(fam, ttp.SubfamilyName);
        }
        catch { return null; }
    }
}
