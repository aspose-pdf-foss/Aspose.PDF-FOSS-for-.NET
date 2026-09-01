namespace Aspose.Pdf.Text;

/// <summary>Base class for font sources.</summary>
public abstract class FontSource
{
    internal abstract FontData? FindFont(string name, bool ignoreCase);

    /// <summary>A source that answers NAME lookups only: it never participates in
    /// glyph-coverage substitution scans. Set (via the harness) on pre-registered
    /// test-data folders — the expected environment does not register them at
    /// all, so a coverage scan finding faces there diverges from it.</summary>
    internal bool NameResolutionOnly { get; set; }

    /// <summary>Enumerate every face this source can provide, for glyph-coverage
    /// scans (NoCharacterAction.ReplaceFonts substitution). Default: none.
    /// SystemFontSource deliberately does NOT enumerate — walking the whole OS
    /// font directory per fragment is too costly; system substitution goes
    /// through the targeted Arial / CJK fallbacks instead.</summary>
    internal virtual IEnumerable<FontData> EnumerateFaces()
        => System.Linq.Enumerable.Empty<FontData>();
}
