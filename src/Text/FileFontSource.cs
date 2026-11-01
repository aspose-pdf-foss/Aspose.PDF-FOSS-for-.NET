namespace Aspose.Pdf.Text;

/// <summary>A font source backed by a single font file on disk.</summary>
public sealed class FileFontSource : FontSource
{
    public FileFontSource(string filePath)
    {
        FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    }

    public string FilePath { get; set; }

    public override bool Equals(object? obj)
        => obj is FileFontSource f && string.Equals(f.FilePath, FilePath, StringComparison.Ordinal);

    public override int GetHashCode() => FilePath?.GetHashCode() ?? 0;

    internal override FontData? FindFont(string name, bool ignoreCase)
    {
        if (!System.IO.File.Exists(FilePath)) return null;
        var ext = System.IO.Path.GetExtension(FilePath).ToLowerInvariant();
        if (ext is not (".ttf" or ".otf" or ".pfb")) return null;

        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var normalizedName = name.Replace(" ", "").Replace("-", "");
        var fileBase = System.IO.Path.GetFileNameWithoutExtension(FilePath);
        if (string.Equals(fileBase, name, comparison) ||
            string.Equals(fileBase.Replace(" ", "").Replace("-", ""), normalizedName, StringComparison.OrdinalIgnoreCase))
        {
            var fd = new FontData(name, ext is ".pfb" ? FontType.Type1 : FontType.TrueType, FilePath);
            if (ext is ".ttf" or ".otf")
                fd.SetTtfData(System.IO.File.ReadAllBytes(FilePath));
            return fd;
        }

        if (ext is ".ttf" or ".otf")
        {
            try
            {
                // Every name the face carries, as the folder and system sources match: a
                // styled-only file ("Aharoni Bold") is asked for by its family ("Aharoni").
                var data = System.IO.File.ReadAllBytes(FilePath);
                var names = FontFaceNames.Read(data);
                if (names.Match(name, comparison, normalizedName) != FaceNameMatch.None)
                {
                    var fd = new FontData(names.Full, FontType.TrueType, FilePath);
                    fd.SetTtfData(data);
                    return fd;
                }
            }
            catch { /* a font file that cannot be read is not this font */ }
        }
        return null;
    }

    internal override IEnumerable<FontData> EnumerateFaces()
    {
        var ext = System.IO.Path.GetExtension(FilePath ?? "").ToLowerInvariant();
        if (ext is not (".ttf" or ".otf") || !System.IO.File.Exists(FilePath)) yield break;
        byte[] data;
        try { data = System.IO.File.ReadAllBytes(FilePath); }
        catch { yield break; }
        var name = "Unknown";
        try { name = FontRepository.ReadTtfFamilyName(data); } catch { /* an unreadable name table keeps the fallback name */ }
        if (name == "Unknown") name = System.IO.Path.GetFileNameWithoutExtension(FilePath);
        var fd = new FontData(name, FontType.TrueType, FilePath);
        fd.SetTtfData(data);
        yield return fd;
    }
}
