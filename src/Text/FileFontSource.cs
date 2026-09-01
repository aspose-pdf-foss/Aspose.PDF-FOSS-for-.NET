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
                var data = System.IO.File.ReadAllBytes(FilePath);
                var actualName = FontRepository.ReadTtfFontName(data);
                if (string.Equals(actualName, name, comparison) ||
                    string.Equals(actualName.Replace(" ", "").Replace("-", ""), normalizedName, StringComparison.OrdinalIgnoreCase))
                {
                    var fd = new FontData(actualName, FontType.TrueType, FilePath);
                    fd.SetTtfData(data);
                    return fd;
                }
            }
            catch { }
        }
        return null;
    }

    internal override IEnumerable<FontData> EnumerateFaces()
    {
        var ext = System.IO.Path.GetExtension(FilePath ?? "").ToLowerInvariant();
        if (ext is not (".ttf" or ".otf") || !System.IO.File.Exists(FilePath)) yield break;
        byte[] data;
        try { data = System.IO.File.ReadAllBytes(FilePath!); }
        catch { yield break; }
        var name = "Unknown";
        try { name = FontRepository.ReadTtfFamilyName(data); } catch { }
        if (name == "Unknown") name = System.IO.Path.GetFileNameWithoutExtension(FilePath!);
        var fd = new FontData(name, FontType.TrueType, FilePath);
        fd.SetTtfData(data);
        yield return fd;
    }
}
