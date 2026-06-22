using System.Collections;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

/// <summary>
/// Information about a font used in a PDF.
/// </summary>
public class FontInfo
{
    private FontMetrics? _metrics;
    private readonly PdfDictionary _fontDict;
    private readonly PdfReader _reader;

    internal FontInfo(string resourceName, PdfDictionary fontDict, PdfReader reader)
    {
        ResourceName = resourceName;
        _fontDict = fontDict;
        _reader = reader;

        var baseFont = fontDict.GetName("BaseFont");
        BaseFont = baseFont ?? "Unknown";
        Subtype = fontDict.GetName("Subtype") ?? "Unknown";
        Encoding = fontDict.GetName("Encoding");

        var descriptor = reader.ResolveDict(fontDict.Get("FontDescriptor"));
        if (descriptor is null && Subtype == "Type0"
            && reader.Resolve(fontDict.Get("DescendantFonts")) is PdfArray descendants
            && descendants.Count > 0)
        {
            // Type0 composite fonts carry the descriptor on the descendant CIDFont.
            var cidFont = reader.ResolveDict(descendants[0]);
            descriptor = reader.ResolveDict(cidFont?.Get("FontDescriptor"));
        }
        if (descriptor is not null)
        {
            IsEmbedded = descriptor.ContainsKey("FontFile") ||
                         descriptor.ContainsKey("FontFile2") ||
                         descriptor.ContainsKey("FontFile3");
            _isSubset = baseFont is not null && baseFont.Length > 7 && baseFont[6] == '+';
            var flagsVal = (int)descriptor.GetInt("Flags");
            IsItalic = (flagsVal & 64) != 0;
            IsBold = (flagsVal & (1 << 18)) != 0; // ForceBold in descriptor
        }
    }

    internal FontInfo(string baseFont, string subtype)
    {
        ResourceName = "F0";
        _fontDict = new PdfDictionary();
        _fontDict.Set("BaseFont", new PdfName(baseFont));
        _fontDict.Set("Subtype", new PdfName(subtype));
        _reader = null!;
        BaseFont = baseFont;
        Subtype = subtype;
    }

    /// <summary>Default Helvetica font for detached text fragments. Returned as
    /// <see cref="Font"/> so it can be assigned to <see cref="TextState.Font"/>
    /// without a downcast; Font derives from FontInfo so existing FontInfo-typed
    /// references still bind.</summary>
    public static Font DefaultHelvetica { get; } = new Font("Helvetica", "Type1");

    /// <summary>Resource name in the page's font dictionary (e.g., "F1").</summary>
    public string ResourceName { get; }

    /// <summary>The base font name (e.g., "Helvetica", "ABCDEF+ArialMT").</summary>
    public string BaseFont { get; }

    /// <summary>Synthesised display name for fonts that carry no /BaseFont (Type3).
    /// Assigned by the owning <see cref="FontCollection"/> in enumeration order.</summary>
    internal string? SynthesizedFontName { get; set; }

    /// <summary>True when this font is a Type3 font with no /BaseFont entry — the
    /// case that needs a synthesised name.</summary>
    internal bool IsNamelessType3 =>
        Subtype == "Type3" && _fontDict.GetName("BaseFont") is null;

    /// <summary>
    /// Normalized font name — strips subset prefix (ABCDEF+) and comma separators.
    /// Matches the the public Text.FontInfo.FontName behavior.
    /// </summary>
    public string FontName
    {
        get
        {
            // A Type3 font carries no /BaseFont, so there is no name to report.
            // A synthesised "T3Font_<n>" handle is surfaced instead; the
            // owning collection assigns the index in enumeration order.
            if (SynthesizedFontName is not null)
                return SynthesizedFontName;
            var name = BaseFont;
            // Strip subset prefix: "ABCDEF+ArialMT" → "ArialMT"
            if (name.Length > 7 && name[6] == '+')
                name = name[7..];
            // Normalize comma-separated style: "Arial,Bold" → "ArialBold"
            name = name.Replace(",", "");
            return name;
        }
    }

    /// <summary>
    /// Decoded font name as a human-readable display name. For most fonts this
    /// equals <see cref="FontName"/>; for CJK fonts whose BaseFont is hex- or
    /// EUC-encoded, the bytes are decoded to their script-native characters.
    /// </summary>
    public virtual string DecodedFontName => FontName;

    /// <summary>
    /// Unique identifier combining resource name and base font.
    /// </summary>
    public string UniqueId => $"{ResourceName}+{BaseFont}";

    /// <summary>Font subtype (Type1, TrueType, Type0, Type3, etc.).</summary>
    public string Subtype { get; }

    /// <summary>Encoding name (WinAnsiEncoding, Identity-H, etc.).</summary>
    public string? Encoding { get; }

    /// <summary>Whether the font is embedded in the PDF. Setting this explicitly
    /// (true or false) pins the choice so the auto-embed-on-assign behaviour in
    /// <see cref="Aspose.Pdf.Text.TextState"/> won't override it.</summary>
    public bool IsEmbedded
    {
        get => _isEmbedded;
        set { _isEmbedded = value; _isEmbeddedExplicit = true; }
    }
    private bool _isEmbedded;
    private bool _isEmbeddedExplicit;

    /// <summary>Set the embedded flag as a default that yields to an explicit caller
    /// choice. Used by the text pipeline to auto-embed an assigned font without
    /// clobbering a caller's prior explicit <see cref="IsEmbedded"/> assignment.</summary>
    internal void SetEmbeddedDefault(bool value)
    {
        if (!_isEmbeddedExplicit) _isEmbedded = value;
    }

    /// <summary>
    /// Whether the font is a subset (name starts with XXXXXX+).
    /// Setting this modifies the BaseFont name in the underlying font dictionary.
    /// </summary>
    public bool IsSubset
    {
        get => _isSubset;
        set
        {
            if (_isSubset == value) return;
            _isSubset = value;
            if (value)
            {
                // Add subset prefix if not present
                if (BaseFont.Length < 7 || BaseFont[6] != '+')
                {
                    var tag = new string(Enumerable.Range(0, 6).Select(_ => (char)('A' + Random.Shared.Next(26))).ToArray());
                    _fontDict.Set("BaseFont", new PdfName($"{tag}+{BaseFont}"));
                }
            }
            else
            {
                // Remove subset prefix
                if (BaseFont.Length > 7 && BaseFont[6] == '+')
                    _fontDict.Set("BaseFont", new PdfName(BaseFont[7..]));
            }
        }
    }
    private bool _isSubset;

    /// <summary>Whether the font is italic.</summary>
    public bool IsItalic { get; }

    /// <summary>Whether the font is bold.</summary>
    public bool IsBold { get; }

    /// <summary>Whether the font is a CID (Type0) font — used for Arabic, CJK, etc.</summary>
    public bool IsCid => Subtype == "Type0";

    /// <summary>
    /// Get font metrics for glyph width calculations.
    /// Lazily initialized on first access.
    /// </summary>
    internal FontMetrics Metrics => _metrics ??= FontMetrics.FromFontDict(_fontDict, _reader);

    /// <summary>Get the font metrics (ascent, descent, glyph widths). May return null for detached fonts.</summary>
    internal FontMetrics? GetMetrics() => _reader is not null ? Metrics : null;

    /// <summary>
    /// Create a FontInfo from a FontData (system font found via FontRepository.FindFont).
    /// This allows assigning FontData to TextState.Font directly.
    /// </summary>
    public static FontInfo FromFontData(FontData fontData)
    {
        var info = new FontInfo(fontData.FontName, fontData.Type == FontType.TrueType ? "TrueType" : "Type1");
        // Preserve the original FontData so TextBuilder can embed the font
        info.SourceFontData = fontData;
        return info;
    }

    /// <summary>
    /// The original FontData when this FontInfo was created via implicit conversion
    /// from FontRepository.FindFont. Used by TextBuilder for font embedding.
    /// </summary>
    internal FontData? SourceFontData { get; set; }

    /// <summary>
    /// Whether the font program is available — either embedded in the PDF,
    /// already loaded as source data, or installed on the system under the
    /// same name (resolvable via <see cref="FontRepository.FindFont"/>).
    /// </summary>
    public bool IsAccessible => SourceFontData is not null || IsEmbedded || IsSystemFontAvailable();

    private bool? _systemFontAvailable;

    private bool IsSystemFontAvailable()
        => _systemFontAvailable ??= !string.IsNullOrEmpty(FontName) && FontRepository.FindFont(FontName) is not null;

    /// <summary>
    /// Implicit conversion from FontData to FontInfo.
    /// Enables: textState.Font = FontRepository.FindFont("Arial");
    /// </summary>
    public static implicit operator FontInfo?(FontData? fontData)
        => fontData is null ? null : FromFontData(fontData);

    /// <summary>
    /// Get the width of a character code in 1/1000 text space units.
    /// </summary>
    public int GetGlyphWidth(int charCode) => Metrics.GetWidth(charCode);

    /// <summary>
    /// Measure the width of a string in points, given a font size.
    /// </summary>
    public double MeasureString(string text, double fontSize) => Metrics.MeasureString(text, fontSize);

    private HashSet<char>? _representable;
    private bool _coverageComputed;

    /// <summary>Whether this font can represent <paramref name="c"/>, judged by the
    /// characters present in the font's /ToUnicode CMap (which, for a subset font,
    /// is exactly the set of glyphs the font carries). Returns true when coverage
    /// can't be determined — no /ToUnicode, or a font with no backing dictionary —
    /// so callers never over-report a missing glyph.</summary>
    internal bool CanRepresent(char c)
    {
        if (!_coverageComputed)
        {
            _coverageComputed = true;
            if (_reader is not null)
                _representable = BuildCoverage();
        }
        return _representable is null || _representable.Contains(c);
    }

    /// <summary>Build the set of characters this font carries glyphs for, read from
    /// the embedded font program's own glyph table — the authoritative signal, since
    /// a subset embeds only the glyphs actually used (its /Widths and /ToUnicode may
    /// span a wider code range than the real glyph set). Returns null when there is
    /// no embedded program or it can't be parsed, so callers never over-report.</summary>
    private HashSet<char>? BuildCoverage()
    {
        var descriptor = _reader.ResolveDict(_fontDict.Get("FontDescriptor"));
        if (descriptor is null && Subtype == "Type0"
            && _reader.Resolve(_fontDict.Get("DescendantFonts")) is PdfArray df && df.Count > 0)
            descriptor = _reader.ResolveDict(_reader.ResolveDict(df[0])?.Get("FontDescriptor"));
        if (descriptor is null) return null;

        var set = new HashSet<char>();
        try
        {
            // Type 1 (/FontFile): glyph names → Unicode.
            if (_reader.ResolveStream(descriptor.Get("FontFile")) is { } t1)
            {
                var data = _reader.DecodeStream(t1);
                var src = data.Length > 0
                    ? Type1GlyphSource.TryLoad(data, (int)t1.Dict.GetInt("Length1"), (int)t1.Dict.GetInt("Length2"))
                    : null;
                if (src is not null)
                    foreach (var name in src.NameToGid.Keys)
                        if (TextAbsorber.GlyphNameToUnicode.TryGetValue(name, out var u))
                            foreach (var ch in u) set.Add(ch);
            }
            // TrueType (/FontFile2): the cmap's Unicode keys.
            else if (_reader.ResolveStream(descriptor.Get("FontFile2")) is { } tt)
            {
                var data = _reader.DecodeStream(tt);
                if (data.Length > 0)
                    foreach (var cp in new GlyphOutlineParser(data).CMap.Keys)
                        if (cp is >= 0 and <= 0xFFFF) set.Add((char)cp);
            }
            // CFF / OpenType-CFF (/FontFile3): the cmap's Unicode keys.
            else if (_reader.ResolveStream(descriptor.Get("FontFile3")) is { } cff)
            {
                var data = _reader.DecodeStream(cff);
                var src = data.Length > 0 ? CffGlyphSource.TryLoad(data) : null;
                if (src is not null)
                    foreach (var cp in src.CMap.Keys)
                        if (cp is >= 0 and <= 0xFFFF) set.Add((char)cp);
            }
        }
        catch { return null; }

        return set.Count > 0 ? set : null;
    }
}

/// <summary>
/// Collection of fonts referenced by a page.
/// </summary>
public sealed class FontCollection : IEnumerable<Font>
{
    private readonly List<Font> _fonts = new();
    private int _nextResId = 1;

    /// <summary>Empty collection — used by <see cref="FontAbsorber"/> to expose accumulated fonts.</summary>
    internal FontCollection() { }

    internal FontCollection(PdfDictionary pageDict, PdfReader reader)
    {
        var resources = reader.ResolveDict(pageDict.Get("Resources"));
        if (resources is not null)
        {
            var fontDict = reader.ResolveDict(resources.Get("Font"));
            if (fontDict is not null)
            {
                var t3 = 0;
                foreach (var key in fontDict.Keys)
                {
                    var font = reader.ResolveDict(fontDict.Get(key));
                    if (font is not null)
                    {
                        var fi = new Font(key, font, reader);
                        if (fi.IsNamelessType3) fi.SynthesizedFontName = $"T3Font_{t3++}";
                        _fonts.Add(fi);
                    }
                }
            }
        }
    }

    /// <summary>Build a collection from a resource dictionary that carries /Font
    /// directly (e.g. the AcroForm /DR), as opposed to a page dict whose fonts
    /// live under /Resources/Font.</summary>
    internal static FontCollection ForResources(PdfDictionary resourceDict, PdfReader reader)
    {
        var fc = new FontCollection();
        var fontDict = reader.ResolveDict(resourceDict.Get("Font"));
        if (fontDict is not null)
        {
            var t3 = 0;
            foreach (var key in fontDict.Keys)
            {
                var font = reader.ResolveDict(fontDict.Get(key));
                if (font is not null)
                {
                    var fi = new Font(key, font, reader);
                    if (fi.IsNamelessType3) fi.SynthesizedFontName = $"T3Font_{t3++}";
                    fc._fonts.Add(fi);
                }
            }
        }
        return fc;
    }

    public int Count => _fonts.Count;

    /// <summary>1-based indexer for API parity.</summary>
    public Font this[int index] => _fonts[index - 1];

    /// <summary>Look up a font by its PDF resource name (e.g. "F1") or BaseFont name.</summary>
    public Font this[string name]
    {
        get
        {
            foreach (var f in _fonts)
                if (f.ResourceName == name || f.BaseFont == name || f.FontName == name)
                    return f;
            throw new KeyNotFoundException($"Font '{name}' not found in collection.");
        }
    }

    public IEnumerator<Font> GetEnumerator() => _fonts.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool IsReadOnly => false;
    public bool IsSynchronized => false;
    public object SyncRoot { get; } = new();

    public bool Contains(Font item) => _fonts.Contains(item);

    public bool Contains(string name)
    {
        foreach (var f in _fonts)
            if (f.ResourceName == name || f.BaseFont == name || f.FontName == name)
                return true;
        return false;
    }

    public void CopyTo(Font[] array, int index) => _fonts.CopyTo(array, index);

    public bool Remove(Font item) => _fonts.Remove(item);

    /// <summary>
    /// Add a font and emit the PDF resource name assigned to it (e.g. "F1", "F2", ...).
    /// </summary>
    public void Add(Font newFont, out string resName)
    {
        if (newFont is null) throw new ArgumentNullException(nameof(newFont));
        resName = $"F{_nextResId++}";
        _fonts.Add(newFont);
    }
}

/// <summary>
/// Type alias for FontInfo, matching the Font class name.
/// </summary>
public class Font : FontInfo
{
    internal Font(string resourceName, PdfDictionary fontDict, PdfReader reader)
        : base(resourceName, fontDict, reader) { }

    internal Font(string baseFont, string subtype) : base(baseFont, subtype) { }

    public new string BaseFont => base.BaseFont;
    public new string FontName => base.FontName;
    public new string DecodedFontName => base.DecodedFontName;
    public new bool IsEmbedded { get => base.IsEmbedded; set => base.IsEmbedded = value; }
    public new bool IsSubset { get => base.IsSubset; set => base.IsSubset = value; }
    public new bool IsAccessible => base.IsAccessible;

    public IFontOptions FontOptions { get; } = new FontOptionsImpl();

    /// <summary>Lower-level PDF-font view of this Font. Aspose.PDF for .NET exposes
    /// the engine's IPdfFont through here; FOSS returns a thin wrapper
    /// that surfaces just <c>BaseFontNameOnly</c>.</summary>
    public PdfFontView iPdfFont => new PdfFontView(this);

    /// <summary>Last error encountered embedding this font in a PDF; empty when none.</summary>
    public string GetLastFontEmbeddingError() => _lastEmbeddingError ?? string.Empty;
    private string? _lastEmbeddingError;

    /// <summary>Measure the rendered width of a string at the given size, in points.</summary>
    public double MeasureString(string str, float fontSize) =>
        MeasureString(str, (double)fontSize);

    /// <summary>Write the raw font file data to a stream. Requires the font to
    /// have been loaded with embeddable data (via FontRepository.OpenFont).</summary>
    public void Save(System.IO.Stream stream)
    {
        if (stream is null) throw new ArgumentNullException(nameof(stream));
        var data = SourceFontData?.TtfData;
        if (data is null || data.Length == 0)
        {
            _lastEmbeddingError = "No embeddable font data is available for this Font.";
            return;
        }
        stream.Write(data, 0, data.Length);
    }

    /// <summary>
    /// Implicit conversion from FontData (returned by FontRepository.FindFont)
    /// to Font, so tests can write <c>TextState.Font = FontRepository.FindFont("Arial")</c>
    /// without an explicit cast.
    /// </summary>
    public static implicit operator Font?(FontData? fontData)
    {
        if (fontData is null) return null;
        var font = new Font(fontData.FontName,
            fontData.Type == FontType.TrueType ? "TrueType" : "Type1");
        font.SourceFontData = fontData;
        return font;
    }

    private sealed class FontOptionsImpl : IFontOptions
    {
        public bool NotifyAboutFontEmbeddingError { get; set; }
    }
}

/// <summary>Per-font runtime options (currently only the font-embedding error toggle).</summary>
public interface IFontOptions
{
    bool NotifyAboutFontEmbeddingError { get; set; }
}

/// <summary>Thin engine-font view used by Aspose.PDF for .NET parity (Font.iPdfFont).
/// Stripped down to the members the test corpus actually reads.</summary>
public sealed class PdfFontView
{
    private readonly Font _font;
    internal PdfFontView(Font font) { _font = font; }

    /// <summary>The PDF /BaseFont name with no subset prefix or style suffix
    /// (e.g. "Arial" rather than "ABCDEF+Arial-Bold").</summary>
    public string BaseFontNameOnly
    {
        get
        {
            var bf = _font.BaseFont ?? string.Empty;
            var plus = bf.IndexOf('+');
            if (plus >= 0 && plus < bf.Length - 1) bf = bf.Substring(plus + 1);
            var dash = bf.IndexOf('-');
            if (dash > 0) bf = bf.Substring(0, dash);
            return bf;
        }
    }
}
