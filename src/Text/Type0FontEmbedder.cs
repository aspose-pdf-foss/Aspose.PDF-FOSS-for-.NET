using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Text;

/// <summary>
/// Embeds a TrueType font program as a composite Type0 / CIDFontType2 font with
/// Identity-H encoding and a /ToUnicode CMap, and encodes a string to the 2-byte
/// glyph-id codes that font expects. Shared by the generator (<see cref="TextBuilder"/>)
/// and the stamp pipeline so Unicode/CJK text can be written with an embedded font.
///
/// The font program is embedded ONCE per (resource dict, ttf) — repeated <see cref="Embed"/>
/// calls for the same font on the same page reuse that single Type0 resource and grow its
/// /W widths + /ToUnicode incrementally. Without this, a CJK-heavy document (one call per
/// line) would write the full multi-MB font program hundreds of times. A caller that names the
/// document the dictionary belongs to shares the one Type0 font across all its pages.
/// </summary>
internal static class Type0FontEmbedder
{
    // Per resource-dict (≈ per page) → per ttf-program → the one embedded Type0 font.
    private static readonly ConditionalWeakTable<PdfDictionary, PageFonts> _cache = new();

    // Per document → per ttf-program → the Type0 font every page of that document files: a face
    // drawn on a thousand pages is one font with one program, not a thousand copies of it.
    private static readonly ConditionalWeakTable<object, Dictionary<byte[], FontState>> _byDocument = new();

    private sealed class PageFonts
    {
        public readonly Dictionary<byte[], FontSeat> ByTtf =
            new(ReferenceEqualityComparer.Instance);
    }

    /// <summary>A font as one resource dictionary files it: the font, under the name that
    /// dictionary gives it.</summary>
    private sealed class FontSeat
    {
        public FontState St = null!;
        public string ResName = "";
    }

    private sealed class FontState
    {
        public GlyphOutlineParser Parser = null!;
        public double Upm = 1000;
        public readonly Dictionary<int, int> UsedGlyphs = new(); // charCode → glyphId
        /// <summary>Glyphs a SHAPED run put in the stream that no single character maps
        /// to — a conjunct, a reph, a half form. They need /W entries like any other
        /// glyph, but they have no ToUnicode source character of their own.</summary>
        public readonly HashSet<ushort> ShapedGlyphs = new();
        /// <summary>Glyphs that stand for SEVERAL characters — an fi ligature for "fi" —
        /// with the text each one means. A ligature has no source character of its own,
        /// so without this the text under it cannot be extracted from the page at all;
        /// with it the glyph maps back to the whole string it replaced.</summary>
        public readonly Dictionary<ushort, string> GlyphText = new();
        public PdfDictionary CidFont = null!;
        public PdfDictionary Type0Font = null!;
        public PdfStream FontFile = null!;   // the embedded program: /FontFile2, or /FontFile3 for CFF
        public byte[] Ttf = null!;           // the ORIGINAL font program (subset source)
        public bool SubsetDirty;             // new glyphs since the last sparse subset
        public int Growth;                   // counts the calls that added glyphs
        public bool Cut;                     // FontFile holds a subset, not Ttf
        public bool WholeUnits;              // advances in whole units (WholeUnitAdvances)
        public bool Whole;                   // the program ships whole: the save leaves it unsubset
    }

    // Live embedded fonts across all documents — walked at save time so the
    // multi-MB system font program shrinks to a GID-preserving sparse subset
    // of the glyphs actually shown (the full font program is never shipped).
    // A save therefore reaches fonts that another document is still drawing into
    // on another thread: a font's glyph set is written and read under the font's
    // own lock (Embed, RegisterShapedGlyphs, SparseSubset, KeepWhole).
    private static readonly List<WeakReference<FontState>> _liveFonts = new();

    /// <summary>
    /// Sparse-subset every embedded Type0 font program that gained glyphs since
    /// its last subset: unused glyphs lose their outlines while the glyph
    /// numbering stays intact (CIDToGIDMap=Identity content bytes remain valid).
    /// Called from the document save funnel; idempotent between growths, and
    /// safe to run repeatedly — the subset is always cut from the ORIGINAL
    /// program, so glyphs added after an earlier save reappear correctly.
    /// </summary>
    internal static void SparseSubsetEmbeddedFontsForSave()
    {
        lock (_liveFonts)
        {
            for (var i = _liveFonts.Count - 1; i >= 0; i--)
            {
                if (!_liveFonts[i].TryGetTarget(out var st)) { _liveFonts.RemoveAt(i); continue; }
                SparseSubset(st);
            }
        }
    }

    // The glyph set is copied under the font's lock and the subset cut outside it, so a thread
    // drawing into the font never waits on the cut. A font that grew meanwhile keeps its old
    // program and stays dirty: marked up to date, it would ship without the new glyphs.
    private static void SparseSubset(FontState st)
    {
        HashSet<int> gids;
        int growth;
        lock (st)
        {
            if (st.Whole || !st.SubsetDirty || (st.UsedGlyphs.Count == 0 && st.ShapedGlyphs.Count == 0)) return;
            // The subsetter cuts a 'glyf' table; a CFF program has none and is embedded whole.
            if (FontProgramFlavour.IsOpenTypeCff(st.Ttf)) { st.SubsetDirty = false; return; }
            gids = new HashSet<int> { 0 }; // keep .notdef
            foreach (var (_, gid) in st.UsedGlyphs) gids.Add(gid);
            // A conjunct or reph the shaper put in the stream must survive the
            // subset too — it is reachable from no character map.
            foreach (var gid in st.ShapedGlyphs) gids.Add(gid);
            growth = st.Growth;
        }
        byte[] subset;
        try
        {
            var parser = new TrueTypeParser(st.Ttf);
            parser.Parse();
            subset = new TrueTypeSubsetter(st.Ttf, parser).SubsetSparse(gids);
        }
        catch
        {
            return; // A malformed program keeps its full embed — correctness over size.
        }
        lock (st)
        {
            if (st.Growth != growth || st.Whole) return;
            if (subset.Length > 0 && subset.Length < st.Ttf.Length)
            {
                st.FontFile.ReplaceData(subset);
                st.FontFile.Dict.Set("Length1", new PdfInteger(subset.Length));
                st.Cut = true;
            }
            st.SubsetDirty = false;
        }
    }

    /// <summary>Cut the program <paramref name="fontFile"/> holds, when it is one this session embedded, to the
    /// glyphs drawn so far, before a page that draws through it is copied into another document: the copy is
    /// written with that document, whose save does not reach the source's fonts.</summary>
    internal static void CutBeforeCopy(PdfStream fontFile)
    {
        if (_byFontFile.TryGetValue(fontFile, out var st)) SparseSubset(st);
    }

    private static void Grew(FontState st)
    {
        RefreshWidthsAndToUnicode(st);
        st.SubsetDirty = true;
        st.Growth++;
    }

    /// <summary>
    /// Register (or reuse) a Type0 font built from <paramref name="ttfData"/> in
    /// <paramref name="fontDict"/> and return its resource name plus the hex-encoded
    /// (2-byte) glyph ids for <paramref name="text"/>. When <paramref name="stripSpacesInBaseFont"/>
    /// is true the /BaseFont name has its spaces removed (PDF base-font names are space-free,
    /// so the extracted Font.FontName reads back cleanly, e.g. "ArialUnicodeMS").
    /// </summary>
    /// <param name="document">The document <paramref name="fontDict"/> belongs to, when the caller knows it:
    /// every dictionary of that document then files the same font for the program.</param>
    public static (string resName, byte[] hexGlyphIds) Embed(
        PdfDictionary fontDict, byte[] ttfData, string fontName, string text,
        bool stripSpacesInBaseFont = false, string? resNameHint = null,
        IReadOnlyList<string>? features = null, object? document = null)
    {
        var seat = Seat(fontDict, ttfData, fontName, stripSpacesInBaseFont, resNameHint, document);
        // A caller naming its resource gets that name even when the face was first embedded
        // under another (a band's Arial as F1): the same font object is filed under the hint
        // too, so content that continues onto an overflow page - which inherits fonts by
        // NAME and owns a default F1 of its own - still reaches this face.
        if (resNameHint is { Length: > 0 } && seat.ResName != resNameHint && !fontDict.ContainsKey(resNameHint)
            && fontDict.Get(seat.ResName) is { } fontObj)
        {
            fontDict.Set(resNameHint, fontObj);
            seat.ResName = resNameHint;
        }
        var st = seat.St;
        lock (st) return (seat.ResName, EncodeRun(st, text, features));
    }

    /// <summary>The font <paramref name="fontDict"/> files for the program: the one it filed before, else the
    /// one the document's other dictionaries file (put under a free name of this one), else a new font.</summary>
    private static FontSeat Seat(PdfDictionary fontDict, byte[] ttfData, string fontName,
        bool stripSpacesInBaseFont, string? resNameHint, object? document)
    {
        var pageFonts = _cache.GetValue(fontDict, static _ => new PageFonts());
        if (pageFonts.ByTtf.TryGetValue(ttfData, out var seat)) return seat;
        FontState? st = null;
        Dictionary<byte[], FontState>? shared = null;
        if (document is not null)
        {
            shared = _byDocument.GetValue(document,
                static _ => new Dictionary<byte[], FontState>(ReferenceEqualityComparer.Instance));
            lock (shared) shared.TryGetValue(ttfData, out st);
        }
        if (st is null)
        {
            st = BuildFont(ttfData, fontName, stripSpacesInBaseFont);
            if (shared is not null) lock (shared) shared[ttfData] = st;
        }
        var name = FreeName(fontDict, resNameHint);
        fontDict.Set(name, st.Type0Font);
        seat = new FontSeat { St = st, ResName = name };
        pageFonts.ByTtf[ttfData] = seat;
        return seat;
    }

    // A caller with its own naming convention (the SVG converter's C0_0/C1_0 …)
    // gets its name when it is still free; everything else keeps F1, F2, ….
    private static string FreeName(PdfDictionary fontDict, string? resNameHint)
    {
        var name = resNameHint is { Length: > 0 } && !fontDict.ContainsKey(resNameHint)
            ? resNameHint : "F1";
        var counter = 1;
        while (fontDict.ContainsKey(name))
            name = $"F{++counter}";
        return name;
    }

    private static byte[] EncodeRun(FontState st, string text, IReadOnlyList<string>? features)
    {
        // Encode the text to 2-byte glyph ids (CID = GID under Identity), accumulating any
        // newly-seen glyphs so the shared font's /W + /ToUnicode grow to cover them.
        // Iterate by CODEPOINT: a surrogate pair (emoji, CJK Ext-B) is ONE glyph — walking
        // UTF-16 units would look each half up in the cmap and emit two .notdef glyphs.
        var hex = new System.Collections.Generic.List<byte>(text.Length * 2);
        var added = false;
        // Features the CALLER asked for -- ligatures, old-style figures, a stylistic set --
        // are a different question from whether a script needs shaping to be legible, and
        // they apply to Latin too. A glyph that swallowed several characters is recorded
        // with the text it stands for, so the page can still be read back.
        if (features is { Count: > 0 } && ShapeFeatureRun(st, text, features) is { } featured)
        {
            for (var i = 0; i < featured.Length; i++)
            {
                var (gid, cluster) = featured[i];
                var next = i + 1 < featured.Length ? featured[i + 1].Cluster : text.Length;
                if (st.ShapedGlyphs.Add(gid)) added = true;

                // What this glyph replaced: everything from its own cluster up to the
                // next glyph's.
                //
                // ⚠ Recorded against the GLYPH even when it stands for a single
                // character, which the by-character map cannot do: `onum` gives the
                // digit zero a SECOND glyph on the same page, and a map keyed by
                // character has room for only the first. Measured as a defect -- the
                // old-style digits drew at exactly the right width and extracted as
                // Ž, ž, ſ, because the glyph they were drawn with had no entry at all.
                if (cluster >= 0 && next <= text.Length && next > cluster)
                {
                    var stands = text[cluster..next];
                    if (!st.GlyphText.TryGetValue(gid, out var known) || known != stands)
                    {
                        st.GlyphText[gid] = stands;
                        added = true;
                    }
                }

                hex.Add((byte)(gid >> 8));
                hex.Add((byte)(gid & 0xFF));
            }
            if (added) Grew(st);
            return hex.ToArray();
        }

        // A complex script (Devanagari, Telugu, …) is not one glyph per codepoint: its
        // conjuncts and its reph are glyphs the FONT substitutes in, and some marks are
        // drawn before the consonant they follow in memory. Shape the run first when the
        // font carries rules for it; every other script keeps the straight cmap walk.
        if (ShapeRun(st, text) is { } shaped)
        {
            foreach (var gid in shaped)
            {
                if (st.ShapedGlyphs.Add(gid)) added = true;
                hex.Add((byte)(gid >> 8));
                hex.Add((byte)(gid & 0xFF));
            }
            // A glyph the shaping left as a character's own (moved, not substituted) reads
            // back as that character.
            foreach (var ch in text)
                if (st.Parser.GlyphIdOrLookAlike(ch) is var own && own != 0 && Array.IndexOf(shaped, (ushort)own) >= 0
                    && st.UsedGlyphs.TryAdd(ch, own))
                    added = true;
            if (added) Grew(st);
            return hex.ToArray();
        }
        for (var i = 0; i < text.Length; i++)
        {
            int cp = text[i];
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                cp = char.ConvertToUtf32(text[i], text[i + 1]);
#pragma warning disable S127 // a surrogate pair is one code point - the scan steps over its low half
                i++;
#pragma warning restore S127
            }
            var gid = st.Parser.GlyphIdOrLookAlike(cp);
            if (st.UsedGlyphs.TryAdd(cp, gid)) added = true;
            hex.Add((byte)(gid >> 8));
            hex.Add((byte)(gid & 0xFF));
        }
        if (added) Grew(st);
        return hex.ToArray();
    }

    /// <summary>Ship the program embedded from <paramref name="ttfData"/> in <paramref name="fontDict"/> whole,
    /// under its bare name: a form's default-resource face is one a viewer types new values with, so it
    /// keeps every glyph while its /W and /ToUnicode still grow with the values drawn through it.</summary>
    internal static void KeepWhole(PdfDictionary fontDict, byte[] ttfData)
    {
        if (!_cache.TryGetValue(fontDict, out var pageFonts) || !pageFonts.ByTtf.TryGetValue(ttfData, out var seat)) return;
        var st = seat.St;
        lock (st)
        {
            st.Whole = true;
            // A save (this document's, or another's on another thread) may have cut it already.
            if (st.Cut)
            {
                st.FontFile.ReplaceData(st.Ttf);
                st.FontFile.Dict.Set("Length1", new PdfInteger(st.Ttf.Length));
                st.Cut = false;
            }
        }
        var tagged = st.Type0Font.GetName("BaseFont") ?? "";
        var plus = tagged.IndexOf('+');
        if (plus < 0) return;
        var bare = new PdfName(tagged[(plus + 1)..]);
        st.Type0Font.Set("BaseFont", bare);
        st.CidFont.Set("BaseFont", bare);
        if (st.CidFont.Get("FontDescriptor") is PdfDictionary descriptor) descriptor.Set("FontName", bare);
    }

    /// <summary>The source program of a font this session embedded, found by its font-file stream, so more
    /// text can be drawn through the same font; null for a font read from a file.</summary>
    internal static byte[]? SourceProgramOf(PdfStream fontFile) =>
        _byFontFile.TryGetValue(fontFile, out var st) ? st.Ttf : null;

    /// <summary>True when <paramref name="ttfData"/> is already embedded in <paramref name="fontDict"/>.</summary>
    internal static bool IsEmbedded(PdfDictionary fontDict, byte[] ttfData) =>
        _cache.TryGetValue(fontDict, out var pageFonts) && pageFonts.ByTtf.ContainsKey(ttfData);

    /// <summary>
    /// Measure <paramref name="text"/> at <paramref name="fontSize"/> using the same
    /// 1000-unit advances the embedded font's /W array declares, so stamp layout
    /// agrees exactly with what extraction later measures from the file. Neither
    /// side rounds: see PdfWidth for why the fraction is worth carrying.
    /// </summary>
    public static double MeasureText(PdfDictionary fontDict, byte[] ttfData, string fontName,
        string text, double fontSize, bool stripSpacesInBaseFont = false, string? resNameHint = null,
        object? document = null)
    {
        var st = Seat(fontDict, ttfData, fontName, stripSpacesInBaseFont, resNameHint, document).St;
        double total = 0;
        // Measure what will actually be WRITTEN: a shaped run is a different glyph
        // sequence (a conjunct is one glyph where the text had three), so measuring the
        // codepoints instead would over-measure every Indic line.
        // A complex script (Devanagari, Telugu, …) is not one glyph per codepoint: its
        // conjuncts and its reph are glyphs the FONT substitutes in, and some marks are
        // drawn before the consonant they follow in memory. Shape the run first when the
        // font carries rules for it; every other script keeps the straight cmap walk.
        if (ShapeRun(st, text) is { } shaped)
        {
            foreach (var gid in shaped)
                total += WholeUnitAdvances.PerMille(st.Parser.GetAdvanceWidth(gid), st.Upm, st.WholeUnits);
            return total * fontSize / 1000.0;
        }
        // Same codepoint walk as Embed: a surrogate pair is ONE glyph.
        var i = 0;
        while (i < text.Length)
        {
            int cp = text[i];
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                cp = char.ConvertToUtf32(text[i], text[i + 1]);
                i++;
            }
            var gid = st.Parser.GlyphIdOrLookAlike(cp);
            total += WholeUnitAdvances.PerMille(st.Parser.GetAdvanceWidth(gid), st.Upm, st.WholeUnits);
            i++;
        }
        return total * fontSize / 1000.0;
    }

    // Build the Type0 font structure once (descriptor + FontFile2 + CIDFont + Type0) and return
    // the mutable state used to grow /W + /ToUnicode later.
    private static FontState BuildFont(byte[] ttfData, string fontName, bool stripSpacesInBaseFont)
    {
        var nameForBaseFont = stripSpacesInBaseFont ? fontName.Replace(" ", "") : fontName;
        var baseFontName = $"{GenerateSubsetTag()}+{nameForBaseFont}";

        var glyphParser = new GlyphOutlineParser(ttfData);
        var (ascent, descent, flags, _) = FontRepository.ReadTtfMetrics(ttfData);
        // Prefer the hhea ascender/descender (like the simple-TrueType
        // embedder): OS/2 typographic values under-report the descent for
        // common UI fonts, shifting absorbed line boxes read back from this embed.
        try
        {
            var hheaParser = new TrueTypeParser(ttfData);
            hheaParser.Parse();
            var hheaScale = 1000.0 / (hheaParser.UnitsPerEm > 0 ? hheaParser.UnitsPerEm : 1000);
            if (hheaParser.Ascent != 0) ascent = (int)(hheaParser.Ascent * hheaScale);
            if (hheaParser.Descent != 0) descent = (int)(hheaParser.Descent * hheaScale);
        }
        catch { /* a font program that does not parse leaves the fallback metrics in place */ }

        var descriptorDict = new PdfDictionary();
        descriptorDict.Set("Type", new PdfName("FontDescriptor"));
        descriptorDict.Set("FontName", new PdfName(baseFontName));
        descriptorDict.Set("Flags", new PdfInteger(flags | 4)); // Symbolic
        descriptorDict.Set("Ascent", new PdfInteger(ascent));
        descriptorDict.Set("Descent", new PdfInteger(descent));
        descriptorDict.Set("ItalicAngle", new PdfInteger(0));
        descriptorDict.Set("CapHeight", new PdfInteger((int)(ascent * 0.8)));
        descriptorDict.Set("StemV", new PdfInteger(80));
        var bboxArr = new PdfArray();
        bboxArr.Add(new PdfInteger(0)); bboxArr.Add(new PdfInteger(descent));
        bboxArr.Add(new PdfInteger(1000)); bboxArr.Add(new PdfInteger(ascent));
        descriptorDict.Set("FontBBox", bboxArr);

        // An OpenType font with CFF outlines is not a TrueType program: it travels as a
        // /FontFile3 of subtype /OpenType under a CIDFontType0 descendant (PDF 32000-1
        // §9.9, Table 126). Written as a /FontFile2 under a CIDFontType2, the descendant
        // font disagrees with the program it carries, and a reader that checks says so.
        var openTypeCff = FontProgramFlavour.IsOpenTypeCff(ttfData);
        var fontFileStream = new PdfStream(new PdfDictionary(), ttfData);
        if (openTypeCff)
        {
            // /Length1 belongs to a Type1 or TrueType program; a /FontFile3 has none.
            fontFileStream.Dict.Set("Subtype", new PdfName("OpenType"));
            descriptorDict.Set("FontFile3", fontFileStream);
        }
        else
        {
            fontFileStream.Dict.Set("Length1", new PdfInteger(ttfData.Length));
            descriptorDict.Set("FontFile2", fontFileStream);
        }

        var cidFont = new PdfDictionary();
        cidFont.Set("Type", new PdfName("Font"));
        cidFont.Set("Subtype", new PdfName(openTypeCff ? "CIDFontType0" : "CIDFontType2"));
        cidFont.Set("BaseFont", new PdfName(baseFontName));
        var cidSystemInfo = new PdfDictionary();
        cidSystemInfo.Set("Registry", new PdfString(System.Text.Encoding.ASCII.GetBytes("Adobe")));
        cidSystemInfo.Set("Ordering", new PdfString(System.Text.Encoding.ASCII.GetBytes("Identity")));
        cidSystemInfo.Set("Supplement", new PdfInteger(0));
        cidFont.Set("CIDSystemInfo", cidSystemInfo);
        cidFont.Set("FontDescriptor", descriptorDict);
        cidFont.Set("DW", new PdfInteger(500));
        // /CIDToGIDMap is a CIDFontType2 entry; a CIDFontType0's CIDs reach its glyphs
        // through the charset of the CFF program itself.
        if (!openTypeCff) cidFont.Set("CIDToGIDMap", new PdfName("Identity"));

        var type0Font = new PdfDictionary();
        type0Font.Set("Type", new PdfName("Font"));
        type0Font.Set("Subtype", new PdfName("Type0"));
        type0Font.Set("BaseFont", new PdfName(baseFontName));
        type0Font.Set("Encoding", new PdfName("Identity-H"));
        var descendantFonts = new PdfArray();
        descendantFonts.Add(cidFont);
        type0Font.Set("DescendantFonts", descendantFonts);

        var state = new FontState
        {
            Parser = glyphParser,
            Upm = glyphParser.UnitsPerEm > 0 ? glyphParser.UnitsPerEm : 1000,
            CidFont = cidFont,
            Type0Font = type0Font,
            FontFile = fontFileStream,
            Ttf = ttfData,
            WholeUnits = WholeUnitAdvances.Holds(ttfData),
        };
        lock (_liveFonts) _liveFonts.Add(new WeakReference<FontState>(state));
        // The state lives as long as the program's stream, which the document holds until the
        // save writes it. Held only through the page's font dictionary it could be collected
        // before the save - a converter that assembles fonts in a working dictionary drops that
        // one - and the program then shipped whole, or not, as the collector happened to run.
        _byFontFile.Add(fontFileStream, state);
        return state;
    }

    private static readonly ConditionalWeakTable<PdfStream, FontState> _byFontFile = new();

    // Rebuild /W (per-glyph advances) and /ToUnicode from the accumulated glyph set. Cheap
    // relative to the one-time font-program embed; only runs when a call introduced new glyphs.
    /// <summary>
    /// Declare glyphs a SHAPED run put in the stream for a font program that is already
    /// embedded, so they gain /W advances and survive subsetting. Writers that encode
    /// their own glyph ids (the multi-line CID path) call this after shaping, because the
    /// substituted glyphs are reachable from no character map.
    /// </summary>
    internal static void RegisterShapedGlyphs(byte[] ttfData, ushort[] glyphs)
    {
        if (glyphs.Length == 0) return;
        lock (_liveFonts)
        {
            for (var i = _liveFonts.Count - 1; i >= 0; i--)
            {
                if (!_liveFonts[i].TryGetTarget(out var st)) { _liveFonts.RemoveAt(i); continue; }
                if (!ReferenceEquals(st.Ttf, ttfData)) continue;
                lock (st)
                {
                    var added = false;
                    foreach (var g in glyphs.Where(g => st.ShapedGlyphs.Add(g))) added = true;
                    if (added) Grew(st);
                }
            }
        }
    }

    /// <summary>The glyph run for <paramref name="text"/> once the caller's own OpenType
    /// features have been applied, each glyph with the cluster it came from; null when
    /// none of them changed anything.</summary>
    private static (ushort Glyph, int Cluster)[]? ShapeFeatureRun(
        FontState st, string text, IReadOnlyList<string> features)
    {
        try
        {
            return OpenType.TextShaper.ShapeFeatures(st.Ttf, text, features: features,
                glyphOf: cp => (ushort)st.Parser.GlyphIdOrLookAlike(cp));
        }
        catch
        {
            // A malformed layout table must never cost the caller its text.
            return null;
        }
    }

    /// <summary>The shaped glyph run for <paramref name="text"/>, or null when the run
    /// needs no shaping (every script but the Indic ones) or the font carries no layout
    /// rules for it. Both the writer and the measurer go through here so a line is
    /// measured as the glyphs it will actually be written as.</summary>
    private static ushort[]? ShapeRun(FontState st, string text)
    {
        try
        {
            return OpenType.TextShaper.Shape(st.Ttf, text,
                cp => (ushort)st.Parser.GlyphIdOrLookAlike(cp));
        }
        catch
        {
            // A malformed layout table must never cost the caller its text.
            return null;
        }
    }

    /// <summary>
    /// A glyph advance in PDF's 1000-unit space, kept at the precision the face
    /// actually has.
    ///
    /// A face is rarely drawn at 1000 units per em -- 2000 and 2048 are the
    /// common ones -- so scaling an advance into /W usually lands between
    /// integers. Rounding it there moves every following glyph on the line by up
    /// to half a unit, and the error accumulates: the drift is far below a point
    /// but it crosses pixel boundaries, so a line of text disagrees with the
    /// same line laid out from the unrounded advance in a handful of places.
    /// A whole number is still written as an integer, so nothing changes for a
    /// face that is drawn at 1000.
    /// </summary>
    private static PdfObject PdfWidth(double width) =>
#pragma warning disable S1244 // integrality test - exact by definition
        width == Math.Floor(width)
#pragma warning restore S1244
            ? new PdfInteger((int)width)
            : new PdfReal(width);

    private static void RefreshWidthsAndToUnicode(FontState st)
    {
        var wArray = new PdfArray();
        var widthsWritten = new HashSet<int>();
        void AddWidth(int gid)
        {
            if (!widthsWritten.Add(gid)) return;
            var widthArr = new PdfArray();
            widthArr.Add(PdfWidth(WholeUnitAdvances.PerMille(st.Parser.GetAdvanceWidth(gid), st.Upm, st.WholeUnits)));
            wArray.Add(new PdfInteger(gid));
            wArray.Add(widthArr);
        }
        foreach (var (_, gid) in st.UsedGlyphs) AddWidth(gid);
        // A shaped run's conjuncts and rephs are in the stream too and need their own
        // advances — no character maps to them, so UsedGlyphs never sees them.
        foreach (var gid in st.ShapedGlyphs) AddWidth(gid);
        if (wArray.Count > 0) st.CidFont.Set("W", wArray);
        FontMetrics.Forget(st.Type0Font);

        var toUnicode = BuildToUnicodeCMap(st.UsedGlyphs, st.GlyphText);
        st.Type0Font.Set("ToUnicode", new PdfStream(new PdfDictionary(),
            System.Text.Encoding.ASCII.GetBytes(toUnicode)));
    }

    private static string BuildToUnicodeCMap(
        Dictionary<int, int> usedGlyphs, Dictionary<ushort, string>? glyphText = null)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("/CIDInit /ProcSet findresource begin");
        sb.AppendLine("12 dict begin");
        sb.AppendLine("begincmap");
        sb.AppendLine("/CIDSystemInfo");
        sb.AppendLine("<< /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def");
        sb.AppendLine("/CMapName /Adobe-Identity-UCS def");
        sb.AppendLine("/CMapType 2 def");
        sb.AppendLine("1 begincodespacerange");
        sb.AppendLine("<0000> <FFFF>");
        sb.AppendLine("endcodespacerange");
        // One entry per glyph: when two characters share a glyph (the space and the
        // no-break space in every Latin face) the lower codepoint names it, so extracted
        // text reads its plain spaces back as spaces.
        var byGid = new SortedDictionary<int, int>();
        foreach (var (charCode, gid) in usedGlyphs)
            if (!byGid.TryGetValue(gid, out var cur) || charCode < cur) byGid[gid] = charCode;

        // A glyph standing for SEVERAL characters is written as the whole string it
        // replaced -- "<0043> <00660069>" for an fi ligature -- which a bfchar entry
        // allows and which is the only way the text under it can be read back. It wins
        // over any single character that happens to share the glyph.
        var multi = new SortedDictionary<int, string>();
        if (glyphText is not null)
            foreach (var (gid, stands) in glyphText)
                if (stands.Length > 0) { multi[gid] = stands; byGid.Remove(gid); }

        sb.AppendLine($"{byGid.Count + multi.Count} beginbfchar");
        foreach (var (gid, stands) in multi)
        {
            var utf16 = new System.Text.StringBuilder();
            foreach (var ch in stands) utf16.Append(((int)ch).ToString("X4"));
            sb.AppendLine($"<{gid:X4}> <{utf16}>");
        }
        foreach (var (gid, charCode) in byGid)
        {
            // Supplementary-plane codepoints are written as their UTF-16BE surrogate
            // pair (PDF 32000 §9.10.3) — "<gid> <D83DDC4D>" — not as 5-digit hex.
            if (charCode > 0xFFFF)
            {
                var s = char.ConvertFromUtf32(charCode);
                sb.AppendLine($"<{gid:X4}> <{(int)s[0]:X4}{(int)s[1]:X4}>");
            }
            else
            {
                sb.AppendLine($"<{gid:X4}> <{charCode:X4}>");
            }
        }
        sb.AppendLine("endbfchar");
        sb.AppendLine("endcmap");
        sb.AppendLine("CMapName currentdict /CMap defineresource pop");
        sb.AppendLine("end");
        sb.AppendLine("end");
        return sb.ToString();
    }

    internal static string GenerateSubsetTag()
    {
#pragma warning disable S2245 // a font subset tag (ISO 32000-1 9.6.4) needs uniqueness, not unpredictability
        var random = new Random();
#pragma warning restore S2245
        var chars = new char[6];
        for (int i = 0; i < 6; i++)
            chars[i] = (char)('A' + random.Next(26));
        return new string(chars);
    }
}
