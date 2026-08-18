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
            // Assign the backing field, not the property: what the file says is not a
            // caller choice, so it must neither be pinned as explicit nor trigger the
            // setter's embed-on-demand side effect.
            _isEmbedded = descriptor.ContainsKey("FontFile") ||
                          descriptor.ContainsKey("FontFile2") ||
                          descriptor.ContainsKey("FontFile3");
            _isEmbeddedExplicit = true;
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
            // A subset prefix ("AAAAAB+Arial,Bold") marks a real embedded/subsetted font
            // (kept even after UnembedFonts): its style comma is part of the genuine name
            // and is preserved verbatim once the prefix is stripped ("Arial,Bold"). A bare
            // /BaseFont with no subset prefix is either a non-embedded reference or a
            // FOSS-generated (stamp) name; there the style comma is a separators that is
            // normalised away ("Arial,Bold" → "ArialBold", "Courier New,Bold Italic" →
            // "CourierNewBoldItalic"). This discriminator lets a bare (stripped) name and a
            // subset-prefixed (kept) name both round-trip consistently. Spaces are always
            // removed (PDF names carry none).
            bool hadSubsetPrefix = name.Length > 7 && name[6] == '+';
            if (hadSubsetPrefix)
                name = name[7..];
            if (!hadSubsetPrefix)
                name = name.Replace(",", "");
            name = name.Replace(" ", "");
            return name;
        }
    }

    /// <summary>
    /// Decoded font name as a human-readable display name. For most fonts this
    /// equals <see cref="FontName"/>; for CJK fonts whose BaseFont is hex- or
    /// EUC-encoded (e.g. "NEPBJB+#BC#D0#B7#A2#C5#E9", a Big5-encoded 標楷體),
    /// the bytes are decoded to their script-native characters via the font's
    /// legacy codepage. The subset prefix is kept verbatim.
    /// </summary>
    public virtual string DecodedFontName
    {
        get
        {
            var name = BaseFont;
            if (string.IsNullOrEmpty(name)) return FontName;
            var hasHigh = false;
            foreach (var c in name)
                if (c > 0x7F) { hasHigh = true; break; }
            if (!hasHigh) return FontName;

            // Codepage: the Type0 /Encoding CMap name is authoritative (ETen-B5-H →
            // Big5, GBK-EUC-H → GBK, …); fall back to the descendant CIDSystemInfo
            // /Ordering when the encoding name doesn't identify one.
            var cp = CidFontInfo.CodepageForCMapName(_fontDict.GetName("Encoding"));
            if (cp == 0) cp = CodepageForOrdering();
            if (cp == 0) return FontName;

            // Keep a subset tag ("ABCDEF+") verbatim; decode the remainder's bytes.
            var prefix = string.Empty;
            var body = name;
            if (name.Length > 7 && name[6] == '+')
            {
                prefix = name[..7];
                body = name[7..];
            }
            var sb = new System.Text.StringBuilder(prefix);
            for (var i = 0; i < body.Length; i++)
            {
                var c = body[i];
                if (c <= 0x7F || i + 1 >= body.Length)
                {
                    sb.Append(c);
                    continue;
                }
                var code = (c << 8) | (body[i + 1] & 0xFF);
                if (CidFontInfo.LegacyLookup(cp, code) is int u)
                {
                    sb.Append(char.ConvertFromUtf32(u));
                    i++;
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }

    /// <summary>Legacy codepage inferred from the descendant CIDFont's
    /// /CIDSystemInfo /Ordering (Adobe-CNS1 → Big5, Adobe-GB1 → GBK, …).</summary>
    private int CodepageForOrdering()
    {
        if (_reader is null) return 0;
        var desc = _reader.Resolve(_fontDict.Get("DescendantFonts")) as PdfArray;
        if (desc is null || desc.Count == 0) return 0;
        var cidFont = _reader.ResolveDict(desc[0]);
        var csi = cidFont is null ? null : _reader.ResolveDict(cidFont.Get("CIDSystemInfo"));
        var orderingObj = csi?.Get("Ordering");
        var ordering = orderingObj is PdfString os ? os.ToText()
            : (orderingObj is PdfName on ? on.Value : null);
        return ordering switch
        {
            "CNS1" => 950,
            "GB1" => 936,
            "Japan1" => 932,
            "Korea1" or "KR" => 949,
            _ => 0,
        };
    }

    /// <summary>
    /// Unique identifier combining resource name and base font.
    /// </summary>
    public string UniqueId => $"{ResourceName}+{BaseFont}";

    /// <summary>The underlying PDF font dictionary — the identity the absorber
    /// dedupes on (one instance per resolved indirect object).</summary>
    internal PdfDictionary FontDict => _fontDict;

    /// <summary>Decoded embedded font program (FontFile2 / FontFile3 / FontFile),
    /// or null when the font is not embedded or has no reachable program.</summary>
    internal byte[]? GetEmbeddedProgramBytes()
    {
        if (_reader is null) return null;
        try
        {
            var descriptor = _reader.ResolveDict(_fontDict.Get("FontDescriptor"));
            if (descriptor is null && Subtype == "Type0"
                && _reader.Resolve(_fontDict.Get("DescendantFonts")) is PdfArray df && df.Count > 0)
                descriptor = _reader.ResolveDict(_reader.ResolveDict(df[0])?.Get("FontDescriptor"));
            if (descriptor is null) return null;
            foreach (var key in new[] { "FontFile2", "FontFile3", "FontFile" })
            {
                if (_reader.ResolveStream(descriptor.Get(key)) is { } s)
                {
                    var d = _reader.DecodeStream(s);
                    if (d.Length > 0) return d;
                }
            }
        }
        catch { }
        return null;
    }

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
        set
        {
            var was = _isEmbedded;
            _isEmbedded = value;
            _isEmbeddedExplicit = true;
            // A font that is not embedded cannot carry a subset — keep the two
            // consistent unless the caller pins the subset flag separately later.
            if (!value && !_isSubsetExplicit) _isSubset = false;

            if (value)
            {
                // Turning embedding ON for a font read out of the document embeds the
                // whole program under the bare name, so the reloaded font reads as
                // embedded and non-subset.
                if (!was) EmbedInPlace();
            }
            else
            {
                // Turning it OFF discards a program this font already put into the
                // document as a replacement resource.
                UnembedMaterialised();
            }
        }
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

    /// <summary>Set the subset flag as a default that yields to an explicit caller
    /// choice, without mangling <see cref="BaseFont"/> (unlike the <see cref="IsSubset"/>
    /// setter). The subset prefix is applied later, at embed/save time.</summary>
    internal void SetSubsetDefault(bool value)
    {
        if (!_isSubsetExplicit) _isSubset = value;
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
            _isSubsetExplicit = true;
            if (value)
            {
                _fontDict.Remove("AsposeEmbedFull");
                if (!_isSubset)
                {
                    _isSubset = true;
                    // Add subset prefix if not present
                    if (BaseFont.Length < 7 || BaseFont[6] != '+')
                    {
                        var tag = new string(Enumerable.Range(0, 6).Select(_ => (char)('A' + Random.Shared.Next(26))).ToArray());
                        _fontDict.Set("BaseFont", new PdfName($"{tag}+{BaseFont}"));
                    }
                }
            }
            else
            {
                _isSubset = false;
                // Remove subset prefix
                if (BaseFont.Length > 7 && BaseFont[6] == '+')
                    _fontDict.Set("BaseFont", new PdfName(BaseFont[7..]));
                // Record the explicit "do not subset" intent so the save-time embed pass
                // (EmbedNonEmbeddedFonts) embeds the full program instead of subsetting and
                // adding a tag — which would make the reloaded font read as a subset again.
                // Recorded even when already non-subset (a non-embedded face has no prefix
                // yet still gets embedded on save). Transient: the embed pass removes it.
                _fontDict.Set("AsposeEmbedFull", PdfBoolean.True);
                // A replacement font is put into the document as an embedded subset when
                // it is assigned to a TextState. Only subset embedding is offered for
                // those, so clearing the subset flag drops the program entirely and
                // leaves a plain by-name reference behind.
                UnembedMaterialised();
            }
        }
    }
    private bool _isSubset;
    private bool _isSubsetExplicit;

    /// <summary>Font resources the text pipeline created in a document to render this
    /// font, along with the objects that carry the embedded program. A font is assigned
    /// to a <see cref="TextState"/> first and configured afterwards, so a later
    /// IsEmbedded/IsSubset change has to reach back into what was already written.</summary>
    private List<(Document doc, PdfDictionary fontDict, string bareName,
        int descriptorObjNum, int fontFileObjNum)>? _materialised;

    /// <summary>Record a font resource created from this font, so cancelling the
    /// embedding later can rewrite it.</summary>
    internal void TrackMaterialised(Document doc, PdfDictionary fontDict, string bareName,
        int descriptorObjNum, int fontFileObjNum)
    {
        _materialised ??= [];
        _materialised.Add((doc, fontDict, bareName, descriptorObjNum, fontFileObjNum));
    }

    /// <summary>Turn every font resource created from this font into a by-name reference:
    /// drop the subset tag, detach the embedded program, and free the now-stranded
    /// descriptor and font-file objects so they don't bloat the output.</summary>
    private void UnembedMaterialised()
    {
        if (_materialised is null) return;
        foreach (var (doc, fontDict, bareName, descriptorObjNum, fontFileObjNum) in _materialised)
        {
            fontDict.Set("BaseFont", new PdfName(bareName));
            fontDict.Remove("FontDescriptor");
            doc.RemoveNewObject(descriptorObjNum);
            doc.RemoveNewObject(fontFileObjNum);
        }
        _materialised.Clear();
    }

    /// <summary>Embed the full program of the face this font names into its own
    /// dictionary, in place. Applies to a font read out of a document that carries no
    /// program of its own; the resource reference that points at the dictionary is
    /// preserved, so the page keeps rendering the same run.</summary>
    private void EmbedInPlace()
    {
        var doc = _reader?.OwnerDocument;
        if (doc is null) return;
        // EmbedIntoFontDict rewrites the dictionary as a simple WinAnsi TrueType. Doing
        // that to a composite (Type0/CID) font would orphan its DescendantFonts and
        // rewrite Subtype, so the 2-byte codes decode one byte at a time and the
        // ToUnicode CMap no longer matches — text extraction turns to mojibake. A
        // non-embedded composite is embedded through the CID path at save time
        // (EmbedNonEmbeddedCidFont); leave it by-name here.
        if (IsCid) return;
        // A font that already carries a program, or one assigned from FontRepository
        // (embedded through the text pipeline when it reaches a TextState), is not this
        // case.
        if (SourceFontData is not null) return;
        if (GetEmbeddedProgramBytes() is { Length: > 0 }) return;
        try
        {
            var ttf = SystemFontResolver.Resolve(FontName);
            if (ttf is null || ttf.Length == 0) return;
            FontEmbedder.EmbedIntoFontDict(doc, ttf, _fontDict, FontName, subset: false);
            _isSubset = false;
            // The face that got embedded is a host substitute whenever its family
            // differs from the requested one (Helvetica→Arial, Courier→Courier New);
            // report that replacement through the document's substitution event.
            var reported = SubstitutedFaceDisplayName(FontName, ttf);
            if (reported is not null)
                doc.RaiseFontSubstitution(new Font(FontName, Subtype ?? "Type1"),
                    // SynthesizedFontName carries the display name verbatim — FontName's
                    // space-stripping is for /BaseFont-derived names, not event reporting.
                    new Font(reported, "TrueType") { SynthesizedFontName = reported });
        }
        catch { /* best-effort: leave the font by-name if the face can't be embedded */ }
    }

    /// <summary>The user-facing name of the substitute face actually embedded for
    /// <paramref name="requestedName"/> — the resolved program's family plus the style
    /// the requested standard name asked for ("Helvetica-BoldOblique" over an Arial
    /// program → "Arial Bold Italic"). Null when the resolved family IS the requested
    /// family (no substitution to report) or the program can't be parsed.</summary>
    internal static string? SubstitutedFaceDisplayName(string requestedName, byte[] ttf)
    {
        var dash = requestedName.IndexOf('-');
        var requestedFamily = dash > 0 ? requestedName[..dash] : requestedName;
        var suffix = dash > 0 ? requestedName[(dash + 1)..] : string.Empty;
        string family;
        try
        {
            var ttp = new TrueTypeParser(ttf);
            ttp.Parse();
            family = ttp.FamilyName;
        }
        catch { return null; }
        if (string.IsNullOrWhiteSpace(family) || family == "Unknown") return null;
        if (string.Equals(family.Replace(" ", ""), requestedFamily.Replace(" ", ""),
                StringComparison.OrdinalIgnoreCase))
            return null;
        var style = (suffix.Contains("Bold") ? " Bold" : "")
                  + (suffix.Contains("Oblique") || suffix.Contains("Italic") ? " Italic" : "");
        return family + style;
    }

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

    private IGlyphOutlineSource? _outlineSource;
    private bool _outlineSourceTried;

    /// <summary>Glyph-outline source for this font — the program embedded in the PDF
    /// font dictionary (/FontFile2, /FontFile3, /FontFile) when present, otherwise the
    /// raw font data attached via <see cref="FontRepository"/> (FindFont / OpenFont).
    /// Null when no outline program is reachable. Built lazily and cached.</summary>
    private IGlyphOutlineSource? OutlineSource
    {
        get
        {
            if (_outlineSourceTried) return _outlineSource;
            _outlineSourceTried = true;
            try { _outlineSource = BuildOutlineSource(); }
            catch { _outlineSource = null; }
            return _outlineSource;
        }
    }

    private IGlyphOutlineSource? BuildOutlineSource()
    {
        // A font assigned from FontRepository (system or stream-opened) carries its raw
        // program directly; prefer it so a reassigned Font measures with its own glyphs.
        var raw = SourceFontData?.TtfData;
        if (raw is { Length: > 12 })
        {
            // 'OTTO' marks an OpenType container with CFF (not TrueType glyf) outlines.
            var isOpenTypeCff = raw[0] == 0x4F && raw[1] == 0x54 && raw[2] == 0x54 && raw[3] == 0x4F;
            return isOpenTypeCff ? CffGlyphSource.TryLoad(raw) : new GlyphOutlineParser(raw);
        }

        // Otherwise read the program embedded in the PDF font dictionary. Type0 fonts
        // carry the descriptor on the descendant CIDFont.
        if (_reader is null) return null;
        var descriptor = _reader.ResolveDict(_fontDict.Get("FontDescriptor"));
        if (descriptor is null && Subtype == "Type0"
            && _reader.Resolve(_fontDict.Get("DescendantFonts")) is PdfArray df && df.Count > 0)
            descriptor = _reader.ResolveDict(_reader.ResolveDict(df[0])?.Get("FontDescriptor"));
        if (descriptor is null) return null;

        if (_reader.ResolveStream(descriptor.Get("FontFile2")) is { } ff2)
        {
            var d = _reader.DecodeStream(ff2);
            if (d.Length > 0) return new GlyphOutlineParser(d);
        }
        if (_reader.ResolveStream(descriptor.Get("FontFile3")) is { } ff3)
        {
            var d = _reader.DecodeStream(ff3);
            if (d.Length > 0) return CffGlyphSource.TryLoad(d);
        }
        if (_reader.ResolveStream(descriptor.Get("FontFile")) is { } ff1)
        {
            var d = _reader.DecodeStream(ff1);
            if (d.Length > 0)
                return Type1GlyphSource.TryLoad(d,
                    (int)ff1.Dict.GetInt("Length1"), (int)ff1.Dict.GetInt("Length2"));
        }
        return null;
    }

    /// <summary>Bounding-box height (yMax − yMin) of the glyph drawn for
    /// <paramref name="c"/>, in the font program's native glyph-design units. Returns 0
    /// when the font carries no glyph for the character (e.g. a subset that never used it)
    /// or no outline program is reachable. The caller maps the value to text space as
    /// <c>height × FontSize / 1000</c> (the PDF 1/1000 glyph-space convention).</summary>
    internal double GlyphHeightUnits(char c)
    {
        var src = OutlineSource;
        if (src is null) return 0;
        if (!src.CMap.TryGetValue(c, out var gid) || gid <= 0) return 0;
        var outline = src.GetOutline(gid);
        if (outline is null) return 0;
        var h = outline.YMax - outline.YMin;
        return h > 0 ? h : 0;
    }

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
        // /Resources is an inheritable page attribute: a page dict frequently carries no
        // /Resources of its own and inherits the nearest ancestor's in the /Pages tree.
        // Resolve the effective resources so fonts referenced by the page content (e.g.
        // /FAAAAI Tf) are discoverable rather than reporting an empty collection.
        var resources = ResolveEffectiveResources(pageDict, reader);
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

    /// <summary>Resolve a page's effective /Resources, walking the /Parent chain for the
    /// inherited dictionary when the page itself declares none. Returns null when no page
    /// or ancestor carries /Resources.</summary>
    private static PdfDictionary? ResolveEffectiveResources(PdfDictionary pageDict, PdfReader reader)
    {
        var resources = reader.ResolveDict(pageDict.Get("Resources"));
        if (resources is not null) return resources;

        var parentObj = pageDict.Get("Parent");
        var visited = new HashSet<int>();
        while (parentObj is not null)
        {
            if (parentObj is PdfIndirectRef iref && !visited.Add(iref.ObjectNumber))
                break;
            var parent = reader.ResolveDict(parentObj);
            if (parent is null) break;
            var res = reader.ResolveDict(parent.Get("Resources"));
            if (res is not null) return res;
            parentObj = parent.Get("Parent");
        }
        return null;
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

    /// <summary>Lower-level PDF-font view of this Font. The public API exposes
    /// the engine's IPdfFont through here; FOSS returns a thin wrapper
    /// that surfaces just <c>BaseFontNameOnly</c>.</summary>
    public PdfFontView iPdfFont => new PdfFontView(this);

    /// <summary>Last error encountered embedding this font in a PDF; empty when none.</summary>
    public string GetLastFontEmbeddingError() => _lastEmbeddingError ?? string.Empty;
    private string? _lastEmbeddingError;

    /// <summary>Measure the rendered width of a string at the given size, in points.</summary>
    public double MeasureString(string str, float fontSize) =>
        MeasureString(str, (double)fontSize);

    /// <summary>Write the raw font file data to a stream: data loaded via
    /// FontRepository.OpenFont, the program embedded in the source PDF (an absorbed
    /// font), or the installed system face resolved by name — in that order.</summary>
    public void Save(System.IO.Stream stream)
    {
        if (stream is null) throw new ArgumentNullException(nameof(stream));
        var data = SourceFontData?.TtfData ?? GetEmbeddedProgramBytes();
        if (data is null || data.Length == 0)
        {
            try { data = FontRepository.GetTtfData(FontName); }
            catch { data = null; }
        }
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

/// <summary>Thin engine-font view used by public-API parity (Font.iPdfFont).
/// Stripped down to the members the test corpus actually reads.</summary>
public sealed class PdfFontView
{
    private readonly Font _font;
    internal PdfFontView(Font font) { _font = font; }

    /// <summary>The PDF /BaseFont name with the subset prefix removed but the full font name
    /// (including any style suffix) preserved — e.g. "ABCDEF+TimesNewRomanPS-BoldMT" becomes
    /// "TimesNewRomanPS-BoldMT" and "Helvetica-Bold" stays "Helvetica-Bold". The 6-letter
    /// subset tag (per PDF §9.6.4) is the only part stripped.</summary>
    public string BaseFontNameOnly
    {
        get
        {
            var bf = _font.BaseFont ?? string.Empty;
            var plus = bf.IndexOf('+');
            if (plus >= 0 && plus < bf.Length - 1) bf = bf.Substring(plus + 1);
            return bf;
        }
    }
}
