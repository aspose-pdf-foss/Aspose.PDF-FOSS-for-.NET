namespace Aspose.Pdf.Text;

/// <summary>A font source backed by an in-memory font byte buffer.</summary>
public sealed class MemoryFontSource : FontSource, IDisposable
{
    public MemoryFontSource(byte[] fontBytes)
    {
        FontBytes = fontBytes ?? throw new ArgumentNullException(nameof(fontBytes));
    }

    /// <summary>The raw font bytes used to back this source.</summary>
    public byte[] FontBytes { get; }

    /// <summary>Equal when both sources wrap the same byte array reference.</summary>
    public override bool Equals(object? obj)
        => obj is MemoryFontSource m && ReferenceEquals(m.FontBytes, FontBytes);

    public override int GetHashCode() => FontBytes.GetHashCode();

    /// <summary>No-op: FOSS doesn't hold native handles for in-memory sources.</summary>
    public void Dispose() { }

    internal override FontData? FindFont(string name, bool ignoreCase)
    {
        try
        {
            var actualName = FontRepository.ReadTtfFontName(FontBytes);
            if (string.IsNullOrEmpty(actualName)) return null;
            var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var nName = name.Replace(" ", "").Replace("-", "");
            var nActual = actualName.Replace(" ", "").Replace("-", "");
            if (string.Equals(actualName, name, comparison) ||
                string.Equals(nActual, nName, StringComparison.OrdinalIgnoreCase))
            {
                var fd = new FontData(actualName, FontType.TrueType);
                fd.SetTtfData(FontBytes);
                return fd;
            }
        }
        catch { }
        return null;
    }

    internal override IEnumerable<FontData> EnumerateFaces()
    {
        var name = "Unknown";
        try { name = FontRepository.ReadTtfFamilyName(FontBytes); } catch { }
        if (name == "Unknown") yield break;
        var fd = new FontData(name, FontType.TrueType);
        fd.SetTtfData(FontBytes);
        yield return fd;
    }
}
