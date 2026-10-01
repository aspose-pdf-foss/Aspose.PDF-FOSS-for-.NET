namespace Aspose.Pdf.Text;

/// <summary>A collection of font sources used by <see cref="FontRepository"/>.
/// Thread-safe: the repository instance is process-global (static), so one thread can
/// register a source while another resolves a font — mutations lock, and enumeration
/// walks a snapshot so an in-flight FindFont never throws "collection was modified".</summary>
public sealed class FontSourceCollection : System.Collections.Generic.IEnumerable<FontSource>
{
    private readonly System.Collections.Generic.List<FontSource> _sources = new();

    /// <summary>Creates a collection holding the default sources: a <c>SystemFontSource</c>, plus the per-user Windows fonts folder when it exists.</summary>
    public FontSourceCollection()
    {
        _sources.Add(new SystemFontSource());
        // The default source set also carries the per-user Windows
        // fonts folder (%LOCALAPPDATA%\Microsoft\Windows\Fonts) as a
        // FolderFontSource when it exists — callers enumerate Sources expecting
        // to find it without ever calling LoadFonts. Resolution
        // is unaffected on machines without the folder, and the system source
        // already exposes those fonts for lookup.
        var userFonts = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "Windows", "Fonts");
        if (Directory.Exists(userFonts))
            _sources.Add(new FolderFontSource(userFonts) { IsDefaultUserFolder = true });
    }

    /// <summary>Gets the number of font sources in the collection.</summary>
    public int Count { get { lock (SyncRoot) return _sources.Count; } }

    public bool IsSynchronized => true;
    public object SyncRoot { get; } = new();

    /// <summary>Gets the font source at the given zero-based index.</summary>
    public FontSource this[int index] { get { lock (SyncRoot) return _sources[index]; } }

    /// <summary>Adds a font source; a second <c>SystemFontSource</c> or a <c>FolderFontSource</c> for a folder already present (compared case-insensitively) is ignored. Throws when <c>fontSource</c> is <c>null</c>.</summary>
    public void Add(FontSource fontSource)
    {
        if (fontSource is null) throw new ArgumentNullException(nameof(fontSource));
        lock (SyncRoot)
        {
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
    }

    /// <summary>Returns <c>true</c> when the collection contains a source equal to the given one.</summary>
    public bool Contains(FontSource item) { lock (SyncRoot) return _sources.Contains(item); }

    /// <summary>Removes the given font source, if present.</summary>
    public void Delete(FontSource fontSource) { lock (SyncRoot) _sources.Remove(fontSource); }

    /// <summary>Removes the given font source; returns <c>true</c> when it was found.</summary>
    public bool Remove(FontSource item) { lock (SyncRoot) return _sources.Remove(item); }

    /// <summary>Copies the font sources into <c>array</c>, starting at the zero-based <c>index</c>.</summary>
    public void CopyTo(FontSource[] array, int index) { lock (SyncRoot) _sources.CopyTo(array, index); }

    /// <summary>Removes all font sources, including the default ones.</summary>
    public void Clear() { lock (SyncRoot) _sources.Clear(); }

    public System.Collections.Generic.IEnumerator<FontSource> GetEnumerator()
    {
        FontSource[] snapshot;
        lock (SyncRoot) snapshot = _sources.ToArray();
        return ((System.Collections.Generic.IEnumerable<FontSource>)snapshot).GetEnumerator();
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
