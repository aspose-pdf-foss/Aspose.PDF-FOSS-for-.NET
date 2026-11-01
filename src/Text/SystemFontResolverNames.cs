namespace Aspose.Pdf.Text;

internal static partial class SystemFontResolver
{
// The name-table lookup behind the file-name resolution, for fonts a document names.

    /// <summary>The program for a font a document names without embedding it: the face its name
    /// leads to by file name (<see cref="Resolve"/>), else the face of a registered font source
    /// that carries the name in its name table - its family, full or PostScript name, in any
    /// language. Installed faces filed under other names (Broadway in BROADW.TTF, Lucida Sans
    /// Unicode in l_10646.ttf), faces a /BaseFont names by PostScript name (MalgunGothic) and
    /// faces only a FolderFontSource carries were drawn and embedded as stand-ins. Null when no
    /// source has the face.</summary>
    internal static byte[]? ResolveDocumentFont(string baseFontName) =>
        Resolve(baseFontName) ?? ByFaceName(baseFontName, FaceScope.AllSources);

    /// <summary><see cref="ResolveDocumentFont"/> over the host's installed faces alone: what a
    /// viewer of the document on this host can be expected to find.</summary>
    internal static byte[]? ResolveInstalledFont(string baseFontName) =>
        Resolve(baseFontName) ?? ByFaceName(baseFontName, FaceScope.Installed);

    /// <summary><see cref="ResolveDocumentFont"/> over the sources the caller registered - folders,
    /// files and memory - and the installed faces by file name only: what a conversion that falls
    /// back to a shared stand-in for any other face embeds.</summary>
    internal static byte[]? ResolveRegisteredFont(string baseFontName) =>
        Resolve(baseFontName) ?? ByFaceName(baseFontName, FaceScope.Registered);

    /// <summary>Which faces a name-table lookup walks.</summary>
    private enum FaceScope
    {
        AllSources,
        Installed,
        Registered,
    }

    private static byte[]? ByFaceName(string baseFontName, FaceScope scope)
    {
        var name = NormalizeBaseFontName(baseFontName);
        if (name.Length == 0) return null;
        var version = FontRepository.Sources.Version;
        var key = (name, scope);
        lock (_lock)
        {
            if (_byFaceName.TryGetValue(key, out var known) && known.Version == version) return known.Data;
        }
        var data = scope == FaceScope.Installed
            ? SystemFontSource.FindFont(name, ignoreCase: true, nameTableScan: true)?.TtfData
            : FontRepository.FindSourceFaceProgram(name, includeInstalled: scope == FaceScope.AllSources);
        lock (_lock)
        {
            _byFaceName[key] = (version, data);
        }
        return data;
    }

    // The name-table answers per name, kept while the registered font sources are unchanged: a
    // miss walks the names of every face the sources carry, and a renderer asks for each font
    // of every page.
    private static readonly Dictionary<(string Name, FaceScope Scope), (int Version, byte[]? Data)> _byFaceName = new();
}
