namespace Aspose.Pdf.Text;

/// <summary>A font source backed by an in-memory font byte buffer.</summary>
public sealed class MemoryFontSource : FontSource, IDisposable
{
    /// <summary>Creates a font source over the given font file bytes (the array is kept, not copied); throws when <c>fontBytes</c> is <c>null</c>.</summary>
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

    // The face's names, read once: a document looks a font up for every fragment.
    private FontFaceNames? _names;

    internal override FontData? FindFont(string name, bool ignoreCase)
    {
        var names = _names ??= FontFaceNames.Read(FontBytes);
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        // Every name the face carries, as the folder and system sources match: a styled-only
        // face ("Aharoni Bold") is asked for by its family ("Aharoni").
        if (names.Match(name, comparison, name.Replace(" ", "").Replace("-", "")) == FaceNameMatch.None) return null;
        var fd = new FontData(names.Full, FontType.TrueType);
        fd.SetTtfData(FontBytes);
        return fd;
    }

    internal override IEnumerable<FontData> EnumerateFaces()
    {
        var name = "Unknown";
        try { name = FontRepository.ReadTtfFamilyName(FontBytes); } catch { /* an unreadable name table keeps the fallback name */ }
        if (name == "Unknown") yield break;
        var fd = new FontData(name, FontType.TrueType);
        fd.SetTtfData(FontBytes);
        yield return fd;
    }
}
