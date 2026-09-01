namespace Aspose.Pdf.Text;

/// <summary>A collection of font sources used by <see cref="FontRepository"/>.
/// Thread-safe: the repository instance is process-global (static), so one thread can
/// register a source while another resolves a font — mutations lock, and enumeration
/// walks a snapshot so an in-flight FindFont never throws "collection was modified".</summary>
public sealed class FontSourceCollection : System.Collections.Generic.IEnumerable<FontSource>
{
    private readonly System.Collections.Generic.List<FontSource> _sources = new();

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

    public int Count { get { lock (SyncRoot) return _sources.Count; } }

    public bool IsSynchronized => true;
    public object SyncRoot { get; } = new();

    public FontSource this[int index] { get { lock (SyncRoot) return _sources[index]; } }

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

    public bool Contains(FontSource item) { lock (SyncRoot) return _sources.Contains(item); }

    public void Delete(FontSource fontSource) { lock (SyncRoot) _sources.Remove(fontSource); }

    public bool Remove(FontSource item) { lock (SyncRoot) return _sources.Remove(item); }

    public void CopyTo(FontSource[] array, int index) { lock (SyncRoot) _sources.CopyTo(array, index); }

    public void Clear() { lock (SyncRoot) _sources.Clear(); }

    public System.Collections.Generic.IEnumerator<FontSource> GetEnumerator()
    {
        FontSource[] snapshot;
        lock (SyncRoot) snapshot = _sources.ToArray();
        return ((System.Collections.Generic.IEnumerable<FontSource>)snapshot).GetEnumerator();
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
