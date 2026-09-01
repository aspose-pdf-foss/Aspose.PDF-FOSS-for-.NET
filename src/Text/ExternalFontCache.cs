using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Aspose.Pdf.Text;

/// <summary>
/// Manages the set of folders searched for external (non-embedded) TrueType/OpenType
/// faces during rendering and conversion. Matches the public API surface used to register
/// a custom fonts folder alongside the platform defaults: <see cref="Instance"/>,
/// <see cref="GetDefaultFontsFolders"/> and <see cref="SetFontsFolders(string[], bool)"/>.
/// </summary>
public sealed class ExternalFontCache
{
    private static readonly ExternalFontCache _instance = new();

    /// <summary>The shared cache instance.</summary>
    public static ExternalFontCache Instance => _instance;

    public ExternalFontCache() { }

    /// <summary>The platform's default font folders (existing directories only).</summary>
    public string[] GetDefaultFontsFolders()
    {
        var dirs = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            dirs.Add(Environment.GetFolderPath(Environment.SpecialFolder.Fonts));
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(localAppData))
                dirs.Add(Path.Combine(localAppData, "Microsoft", "Windows", "Fonts"));
        }
        else if (OperatingSystem.IsMacOS())
        {
            dirs.Add("/System/Library/Fonts");
            dirs.Add("/Library/Fonts");
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home))
                dirs.Add(Path.Combine(home, "Library", "Fonts"));
        }
        else
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
        var existing = dirs.Where(d => !string.IsNullOrEmpty(d) && Directory.Exists(d))
            .SelectMany(SystemFontResolver.WithSubdirectories)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        // Always return at least one entry so callers can index [0] on any platform.
        return existing.Length > 0 ? existing : new[] { Directory.GetCurrentDirectory() };
    }

    /// <summary>
    /// Register the given folders as font sources so their faces resolve by family name.
    /// When <paramref name="reset"/> is true the previously-registered folder sources are
    /// dropped first (the built-in system source is always retained).
    /// </summary>
    public void SetFontsFolders(string[] folders, bool reset)
    {
        if (reset)
        {
            var keep = new List<FontSource>();
            foreach (var src in FontRepository.Sources)
                if (src is not FolderFontSource)
                    keep.Add(src);
            FontRepository.Sources.Clear();
            foreach (var src in keep)
                FontRepository.Sources.Add(src);
        }
        if (folders is null) return;
        foreach (var folder in folders)
        {
            if (string.IsNullOrEmpty(folder)) continue;
            var source = new FolderFontSource(folder);
            if (!FontRepository.Sources.Contains(source))
                FontRepository.Sources.Add(source);
        }
    }
}
