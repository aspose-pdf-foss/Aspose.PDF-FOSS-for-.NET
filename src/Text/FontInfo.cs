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
            if (name.Any(c => c > 0x7F)) { hasHigh = true; }
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
            var i = 0;
            while (i < body.Length)
            {
                var c = body[i];
                if (c <= 0x7F || i + 1 >= body.Length)
                {
                    sb.Append(c);
                    i++;
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
                i++;
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
        var ordering = orderingObj switch { PdfString os => os.ToText(), PdfName on => on.Value, _ => null };
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

    /// <summary>The reader of the document the font was read from; null for a font opened from a host program.</summary>
    internal PdfReader? SourceReader => _reader;

    /// <summary>The name this font was registered under in a page's resources,
    /// set when it is added to a page and used when text is shown against it.</summary>
    internal string? PageResourceName { get; set; }

    /// <summary>The /BaseFont name this font was embedded under on a page.</summary>
    internal string? EmbeddedBaseFontName { get; set; }

    /// <summary>
    /// The face program behind this font, wherever it came from: the program
    /// embedded in a document it was read out of, or the file it was opened from.
    /// </summary>
    internal byte[]? ProgramBytes() => GetEmbeddedProgramBytes() ?? SourceFontData?.TtfData;

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
        catch { /* a font program stream that does not decode - no program */ }
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
            // A composite font whose CIDs are its own program's keeps that program: no installed face
            // numbers its glyphs that way, so without it the font's text could not be drawn at all.
            if (!value && was && CodesNeedTheirProgram())
            {
                _isEmbeddedExplicit = true;
                return;
            }
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
                // document as a replacement resource, and detaches the program of a font
                // read out of the document.
                UnembedMaterialised();
                _reader?.OwnerDocument?.CancelFontEmbed(_fontDict);
                if (was) UnembedInPlace();
            }
        }
    }
    private bool _isEmbedded;
    private bool _isEmbeddedExplicit;

    /// <summary>True when the caller pinned <see cref="IsEmbedded"/> to false: the face
    /// carries a program but was told not to travel with the document.</summary>
    internal bool EmbeddingRefusedExplicitly => _isEmbeddedExplicit && !_isEmbedded;

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
                        var tag = new string(Enumerable.Range(0, 6).Select(_ => (char)('A' + Compat.SharedRandom.Next(26))).ToArray());
                        _fontDict.Set("BaseFont", new PdfName($"{tag}+{BaseFont}"));
                        TagDescriptorName(tag);
                    }
                    // A subset name on a font read from the document needs a program behind it: the
                    // save embeds the face as a subset, or takes the tag off again when none resolves.
                    if (DocumentOfReadFont is { } doc && GetEmbeddedProgramBytes() is not { Length: > 0 })
                        doc.RequestFontEmbed(_fontDict, subset: true, taggedName: BaseFont);
                }
            }
            else
            {
                _isSubset = false;
                var tagged = BaseFont.Length > SubsetPrefixLength && BaseFont[SubsetPrefixLength - 1] == '+';
                var embeddedProgram = DocumentOfReadFont is not null && GetEmbeddedProgramBytes() is { Length: > 0 };
                // A subset becomes the whole face at save - for a composite font only a face whose glyph
                // numbering the subset kept, the program the caller opened or the installed face - and
                // its tag comes back there when no such face is found.
                if (tagged && embeddedProgram)
                    DocumentOfReadFont!.RequestFontEmbed(_fontDict, subset: false, taggedName: BaseFont, replaceProgram: true);
                else if (!embeddedProgram) DocumentOfReadFont?.CancelSubsetRequest(_fontDict);
                // Remove subset prefix
                if (tagged)
                {
                    _fontDict.Set("BaseFont", new PdfName(BaseFont[7..]));
                    TagDescriptorName(null);
                }
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

    /// <summary>The document a font read from it belongs to; null for a font from the repository.</summary>
    private Document? DocumentOfReadFont => SourceFontData is null ? _reader?.OwnerDocument : null;

    /// <summary>The length of a subset tag with its plus sign: six letters and the sign.</summary>
    private const int SubsetPrefixLength = 7;

    /// <summary>A simple font's descriptor names the font as its /BaseFont does: the subset tag goes onto
    /// /FontName, or comes off it, with the tag of the font dictionary (null takes it off).</summary>
    private void TagDescriptorName(string? tag)
    {
        if (_reader?.ResolveDict(_fontDict.Get("FontDescriptor")) is not { } descriptor) return;
        if (descriptor.GetName("FontName") is not { Length: > 0 } name) return;
        var tagged = name.Length > SubsetPrefixLength && name[SubsetPrefixLength - 1] == '+';
        var bare = tagged ? name[SubsetPrefixLength..] : name;
        descriptor.Set("FontName", new PdfName(tag is null ? bare : $"{tag}+{bare}"));
    }

    /// <summary>Font resources the text pipeline created in a document to render this
    /// font, along with the objects that carry the embedded program. A font is assigned
    /// to a <see cref="TextState"/> first and configured afterwards, so a later
    /// IsEmbedded/IsSubset change has to reach back into what was already written.</summary>
    private List<(Document doc, PdfDictionary fontDict, string bareName,
        PdfDictionary? descriptor, int fontFileObjNum)>? _materialised;

    /// <summary>Record a font resource created from this font, so cancelling the
    /// embedding later can rewrite it.</summary>
    internal void TrackMaterialised(Document doc, PdfDictionary fontDict, string bareName,
        PdfDictionary? descriptor, int fontFileObjNum)
    {
        _materialised ??= [];
        _materialised.Add((doc, fontDict, bareName, descriptor, fontFileObjNum));
    }

    /// <summary>Turn every font resource created from this font into a by-name reference:
    /// drop the subset tag, detach the embedded program from the descriptor, and free the
    /// now-stranded font-file object so it doesn't bloat the output.</summary>
    private void UnembedMaterialised()
    {
        if (_materialised is null) return;
        foreach (var (doc, fontDict, bareName, descriptor, fontFileObjNum) in _materialised)
        {
            fontDict.Set("BaseFont", new PdfName(bareName));
            // The font keeps its descriptor - a font other than the standard 14 must have one - with
            // only the program taken off, and named as the font is.
            if (descriptor is not null)
            {
                descriptor.Remove("FontFile2");
                descriptor.Set("FontName", new PdfName(bareName));
            }
            else
            {
                fontDict.Remove("FontDescriptor");
            }
            doc.RemoveNewObject(fontFileObjNum);
        }
        _materialised.Clear();
    }

    /// <summary>Detach the embedded program of a font read out of a document: the
    /// /FontFile* entries come off its FontDescriptor and the program objects are freed,
    /// so the saved file references the face by name. The descriptor keeps its metrics.
    /// ⚠ Font dictionaries commonly SHARE one descriptor object — a simple font and the
    /// Type0 built over the same face routinely do — so this un-embeds every font backed
    /// by that program, which is what un-embedding a shared program means.</summary>
    private void UnembedInPlace()
    {
        if (_reader is null) return;
        var doc = _reader.OwnerDocument;
        try
        {
            foreach (var descriptor in Descriptors())
            {
                foreach (var key in new[] { "FontFile", "FontFile2", "FontFile3" })
                {
                    if (!descriptor.ContainsKey(key)) continue;
                    if (doc is not null && descriptor.Get(key) is PdfIndirectRef fileRef)
                        doc.RemoveNewObject(fileRef.ObjectNumber);
                    descriptor.Remove(key);
                }
                // The glyph sets of the program went with it.
                descriptor.Remove("CharSet");
                descriptor.Remove("CIDSet");
            }
            // A font with no program is no subset: the tag comes off its name, or a conversion that
            // cannot embed the face again leaves a subset name with nothing behind it.
            if (BaseFont.Length > SubsetPrefixLength && BaseFont[SubsetPrefixLength - 1] == '+')
            {
                _fontDict.Set("BaseFont", new PdfName(BaseFont[SubsetPrefixLength..]));
                TagDescriptorName(null);
            }
        }
        catch { /* best-effort: a font that can't be detached stays embedded */ }
    }

    /// <summary>True for a composite font with a program whose CIDs are that program's own (the Identity
    /// ordering): its codes reach their glyphs through nothing else.</summary>
    private bool CodesNeedTheirProgram()
    {
        if (_reader is null || !IsCid) return false;
        if (_reader.Resolve(_fontDict.Get("DescendantFonts")) is not PdfArray { Count: > 0 } descendants) return false;
        var cidFont = _reader.ResolveDict(descendants[0]);
        var info = _reader.ResolveDict(cidFont?.Get("CIDSystemInfo"));
        if (_reader.Resolve(info?.Get("Ordering")) is not PdfString { } ordering || ordering.ToText() != "Identity") return false;
        return Descriptors().Any(d => d.ContainsKey("FontFile") || d.ContainsKey("FontFile2") || d.ContainsKey("FontFile3"));
    }

    /// <summary>This font's FontDescriptor dictionaries — its own, plus every descendant
    /// CIDFont's for a composite font.</summary>
    private IEnumerable<PdfDictionary> Descriptors()
    {
        if (_reader is null) yield break;
        if (_reader.ResolveDict(_fontDict.Get("FontDescriptor")) is { } own) yield return own;
        if (_reader.Resolve(_fontDict.Get("DescendantFonts")) is PdfArray df)
            foreach (var entry in df)
                if (_reader.ResolveDict(_reader.ResolveDict(entry)?.Get("FontDescriptor")) is { } d)
                    yield return d;
    }

    /// <summary>Embed the full program of the face this font names into its own
    /// dictionary, in place. Applies to a font read out of a document that carries no
    /// program of its own; the resource reference that points at the dictionary is
    /// preserved, so the page keeps rendering the same run.</summary>
    private void EmbedInPlace()
    {
        var doc = _reader?.OwnerDocument;
        if (doc is null) return;
        // A font that already carries a program, or one assigned from FontRepository
        // (embedded through the text pipeline when it reaches a TextState), is not this
        // case.
        if (SourceFontData is not null) return;
        if (GetEmbeddedProgramBytes() is { Length: > 0 }) return;
        // EmbedIntoFontDict rewrites the dictionary as a simple WinAnsi TrueType. Doing
        // that to a composite (Type0/CID) font would orphan its DescendantFonts and
        // rewrite Subtype, so the 2-byte codes decode one byte at a time and the
        // ToUnicode CMap no longer matches — text extraction turns to mojibake. A
        // non-embedded composite is embedded through the CID path when the document is saved.
        if (IsCid)
        {
            doc.RequestFontEmbed(_fontDict, subset: _isSubset, taggedName: _isSubset ? BaseFont : null);
            return;
        }
        try
        {
            var ttf = SystemFontResolver.Resolve(FontName);
            // A face no installed family resolves is substituted at save, as the embedding pass does: the
            // stand-in is not the face the font names, so it is cut to the glyphs shown rather than embedded
            // whole once per font.
            if (ttf is null || ttf.Length == 0)
            {
                doc.RequestFontEmbed(_fontDict, subset: true);
                return;
            }
            FontEmbedder.EmbedIntoFontDict(doc, ttf, _fontDict, FontName, subset: false);
            _isSubset = false;
            // The face that got embedded is a host substitute whenever its family
            // differs from the requested one (Helvetica→Arial, Courier→Courier New); report
            // that replacement through the document's substitution event.
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

    /// <summary>True for a font read out of a document that carries its program there: text
    /// written in it is shown through its own dictionary, as there is no program to embed afresh.</summary>
    internal bool IsDocumentEmbeddedFont => IsDocumentFont && IsEmbedded;

    /// <summary>True for a font read out of a document, embedded there or not, rather than one
    /// opened from a host program.</summary>
    internal bool IsDocumentFont => _reader is not null && SourceFontData is null;

    private Dictionary<char, int>? _codesByChar;
    private bool _codesByCharTried;

    /// <summary>The show string for <paramref name="text"/> in this font's own codes, found by
    /// reversing its /ToUnicode map, or null when the font has no map or the map lacks a character.
    /// A code is two bytes for a composite font and one for a simple one.</summary>
    internal byte[]? TryEncode(string text)
    {
        if (!_codesByCharTried)
        {
            _codesByCharTried = true;
            if (_reader is not null && TextAbsorber.ParseToUnicodeFromDict(_fontDict, _reader) is { } toUnicode)
            {
                _codesByChar = new Dictionary<char, int>();
                foreach (var (code, unicode) in toUnicode)
                {
                    if (unicode.Length == 1 && !_codesByChar.ContainsKey(unicode[0])) _codesByChar[unicode[0]] = code;
                }
            }
            else if (_reader is not null && !IsCid)
                _codesByChar = CodesByEncoding();
        }
        if (_codesByChar is null) return null;
        var bytes = new byte[IsCid ? text.Length * 2 : text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            if (!_codesByChar.TryGetValue(text[i], out var code)) return null;
            if (IsCid)
            {
                bytes[2 * i] = (byte)(code >> 8);
                bytes[2 * i + 1] = (byte)code;
            }
            else if (code <= byte.MaxValue) bytes[i] = (byte)code;
            else return null;
        }
        return bytes;
    }

    /// <summary>The codes of a simple font with no /ToUnicode, read through its /Encoding as the text
    /// is extracted - a Word subset in WinAnsi carries none - and only those its /Widths gives an
    /// advance: a subset states zero for every code whose glyph it left out.</summary>
    private Dictionary<char, int>? CodesByEncoding()
    {
        if (_reader!.Resolve(_fontDict.Get("Widths")) is not PdfArray widths) return null;
        var first = (int)_fontDict.GetInt("FirstChar");
        var codes = new Dictionary<char, int>();
        for (var i = 0; i < widths.Count && first + i <= byte.MaxValue; i++)
        {
            var code = first + i;
            var advance = _reader.Resolve(widths[i]) switch { PdfInteger n => n.Value, PdfReal r => r.Value, _ => 0d };
            if (code < 0 || advance <= 0) continue;
            var text = TextAbsorber.DecodeStringPublic([(byte)code], null, _fontDict, _reader);
            if (text.Length == 1 && !codes.ContainsKey(text[0])) codes[text[0]] = code;
        }
        return codes;
    }

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
    /// same name (resolvable via <c>FontRepository.FindFont</c>).
    /// </summary>
    public bool IsAccessible => SourceFontData is not null || IsEmbedded || IsSystemFontAvailable();

    private bool? _systemFontAvailable;

    private bool IsSystemFontAvailable()
        => _systemFontAvailable ??= !string.IsNullOrEmpty(FontName) && FontRepository.TryFindFont(FontName) is not null;

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
    public double MeasureString(string text, double fontSize) =>
        MeasuresByOwnProgram ? SourceFontData!.MeasureString(text, fontSize) : Metrics.MeasureString(text, fontSize);

    /// <summary>A font opened from a file or found among the installed ones is a bare
    /// dictionary with no /Widths: unless its name is a Standard-14 face, its metrics would
    /// answer the default width for every glyph (1 em — Liberation Sans, the Linux Arial, measured
    /// "A" and "z" alike). The program it was opened from holds the real advances.</summary>
    private bool MeasuresByOwnProgram =>
        SourceFontData?.PredefinedCid is not null
        || ((SourceFontData?.TtfData is { Length: > 12 } || SourceFontData?.Type1 is not null) && !_fontDict.ContainsKey("Widths")
            && !_fontDict.ContainsKey("DescendantFonts") && !Metrics.IsStandard14);

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
                    foreach (var cp in new GlyphOutlineParser(data).CMap.Keys.Where(cp => cp is >= 0 and <= 0xFFFF))
                        set.Add((char)cp);
            }
            // CFF / OpenType-CFF (/FontFile3): the cmap's Unicode keys.
            else if (_reader.ResolveStream(descriptor.Get("FontFile3")) is { } cff)
            {
                var data = _reader.DecodeStream(cff);
                var src = data.Length > 0 ? CffGlyphSource.TryLoad(data) : null;
                if (src is not null)
                    foreach (var cp in src.CMap.Keys.Where(cp => cp is >= 0 and <= 0xFFFF))
                        set.Add((char)cp);
            }
        }
        catch { return null; }

        return set.Count > 0 ? set : null;
    }
}
