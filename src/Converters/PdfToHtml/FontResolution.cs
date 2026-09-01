using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Converters;

public sealed partial class PdfToHtmlConverter
{
    private sealed class HtmlFontRecord
    {
        public string Family { get; init; } = "sans-serif";
        /// <summary>Human-readable single family for fixed-layout CSS rules
        /// ("Century Gothic", "Calibri") — the BaseFont with subset prefix and
        /// style suffix removed and camel-case words re-spaced. The flow-layout
        /// path keeps <see cref="Family"/>'s generic fallback stack instead.</summary>
        public string CssFamily { get; init; } = "sans-serif";
        public string Weight { get; init; } = "normal";
        public string Style { get; init; } = "normal";
        public Func<byte[], string>? ToUnicode { get; init; }
        /// <summary>Base-encoding decode for fonts without a ToUnicode CMap
        /// (named encodings, /Differences glyph names, embedded cmap/post).</summary>
        public Func<byte[], string>? BaseDecode { get; init; }
        /// <summary>Whether the embedded program's cmap covers a codepoint;
        /// null when no embedded program (full coverage assumed).</summary>
        public Func<int, bool>? SubsetHas { get; init; }
        /// <summary>Whether a shown CHARACTER CODE resolves to a glyph the
        /// embedded program's own cmap can address (a GID-only variant glyph
        /// renders in the CSS fallback face instead). Null = always mapped.</summary>
        public Func<int, bool>? GlyphMapped { get; init; }
        /// <summary>The embedded program's advance for a shown CHARACTER CODE,
        /// milli-em — the browser-model metric when the subset itself is the
        /// served face. Null func or null result = measure by the resolved
        /// installed face instead.</summary>
        public Func<int, double?>? EmbeddedAdvMilli { get; init; }
        /// <summary>The embedded program's own hmtx advance for a shown CHARACTER
        /// CODE (cid → gid → hmtx/upm), milli-em, unquantized. The em-compensation
        /// dialect solves its spacing against exactly this basis: the line is
        /// measured with the glyph advances of the program being re-served,
        /// so a ligature code weighs its LIGATURE advance and every /W-vs-face
        /// rounding residue stays in the word-spacing numerator. Null when the
        /// font embeds no parsable TrueType program.</summary>
        public Func<int, double?>? ProgramAdvMilli { get; init; }
        /// <summary>Embedded program advance by CHARACTER (reverse ToUnicode →
        /// code → gid → hmtx), for ligature-component measuring.</summary>
        public Func<int, double?>? ProgramCharAdvMilli { get; init; }
        public bool IsCidFont { get; init; }
        /// <summary>A Type3 face: its glyphs are content-stream procedures, not a
        /// program a browser can be handed, so the text drawn with it is only ever a
        /// best-effort transcription.</summary>
        public bool IsType3 { get; init; }
        /// <summary>OS/2 usWinAscent / unitsPerEm of the embedded font program —
        /// the ascent fraction the fixed-layout `top` subtracts (not the
        /// FontDescriptor /Ascent). 1.0 when no embedded sfnt provides it.</summary>
        public double AscentFactor { get; init; } = 1.0;
        /// <summary>hhea (asc+|desc|)/upm — the stl_ line-height class value; 0 = no program.</summary>
        public double LineHeightEm { get; init; }
        /// <summary>Advance of one character code in em fractions (1000-unit widths
        /// / 1000), from /Widths (simple) or /W + /DW (CID); null = no width data.</summary>
        public Func<int, double>? AdvanceOf { get; init; }
        /// <summary>The font serves a SUBSTITUTE face's subset (SimSun standing in
        /// for a non-embedded, non-installed CJK font).</summary>
        public bool SubstituteFace { get; init; }
    }

    /// <summary>Build a code → advance (em fraction) lookup for <paramref name="font"/>:
    /// simple fonts from /FirstChar + /Widths (+ /MissingWidth), Type0 from the
    /// descendant's /W ranges with /DW as the default. Falls back to the embedded
    /// program's hmtx (through its cmap for simple fonts, CID→GID for composites)
    /// when the dictionary carries no widths; null when nothing is available.</summary>
    private static Func<int, double>? BuildAdvanceMap(PdfDictionary font, PdfReader reader, bool isCid)
    {
        try
        {
            if (!isCid)
            {
                var widths = reader.Resolve(font.Get("Widths")) as PdfArray;
                if (widths is { Count: > 0 })
                {
                    var first = (reader.Resolve(font.Get("FirstChar")) as PdfInteger)?.Value ?? 0;
                    var desc = reader.ResolveDict(font.Get("FontDescriptor"));
                    var missing = desc is not null
                        && reader.Resolve(desc.Get("MissingWidth")) is PdfInteger mw ? mw.Value : 0;
                    var arr = new double[widths.Count];
                    for (var i = 0; i < widths.Count; i++)
                        arr[i] = widths[i] is PdfInteger wi ? wi.Value
                            : widths[i] is PdfReal wr ? wr.Value : 0;
                    return code => code >= first && code - first < arr.Length
                        ? arr[code - first] / 1000.0 : missing / 1000.0;
                }
            }
            else
            {
                var descArr = reader.Resolve(font.Get("DescendantFonts")) as PdfArray;
                var descFont = descArr is { Count: > 0 } ? reader.ResolveDict(descArr[0]) : null;
                if (descFont is not null)
                {
                    double dw = reader.Resolve(descFont.Get("DW")) is PdfInteger d ? d.Value : 1000;
                    var map = new Dictionary<int, double>();
                    if (reader.Resolve(descFont.Get("W")) is PdfArray w)
                    {
                        var i = 0;
                        double NumAt(PdfObject? o) => o is PdfInteger pi ? pi.Value
                            : o is PdfReal pr ? pr.Value : 0;
                        while (i < w.Count)
                        {
                            if (i + 1 < w.Count && reader.Resolve(w[i]) is PdfInteger c0
                                && reader.Resolve(w[i + 1]) is PdfArray ws)
                            {
                                for (var k = 0; k < ws.Count; k++) map[(int)c0.Value + k] = NumAt(ws[k]);
                                i += 2;
                            }
                            else if (i + 2 < w.Count && reader.Resolve(w[i]) is PdfInteger ca
                                && reader.Resolve(w[i + 1]) is PdfInteger cb)
                            {
                                var val = NumAt(reader.Resolve(w[i + 2]));
                                for (var c = (int)ca.Value; c <= cb.Value && c - ca.Value < 65536; c++) map[c] = val;
                                i += 3;
                            }
                            else i++;
                        }
                    }
                    if (map.Count > 0 || dw != 1000)
                        return code => map.TryGetValue(code, out var v) ? v / 1000.0 : dw / 1000.0;
                }
            }

            // No dictionary widths: try the embedded program's own advances.
            var ttf = GetEmbeddedTtf(font, reader);
            if (ttf is not null)
            {
                var parser = new Text.GlyphOutlineParser(ttf);
                var upm = parser.UnitsPerEm > 0 ? parser.UnitsPerEm : 1000.0;
                if (!isCid)
                    return code => parser.CMap.TryGetValue(code, out var gid) && gid != 0
                        ? parser.GetAdvanceWidth(gid) / upm : 0.5;
                return code => // Identity CID: the code is (usually) the glyph id
                    parser.GetAdvanceWidth(code) is var adv && adv > 0 ? adv / upm : 0.5;
            }
        }
        catch { /* fall through to null: extent pinning simply stays off */ }
        return null;
    }

    /// <summary>
    /// Per-font output-character registry for ligature and unmapped-code handling.
    /// A char code whose ToUnicode sequence cannot be rendered from component glyphs
    /// (the embedded font has no cmap entries for them) is emitted as ONE character:
    /// the standard Unicode ligature char when the sequence has one, else the
    /// sequence's first character. When that character is already owned by a
    /// different char code of the same font, a fresh code is minted from U+A880
    /// upward instead. Identity-encoded CID codes with no unicode mapping at all
    /// mint directly — their char code is a glyph id, not text.
    /// </summary>
    private sealed class LigatureSubstitutor
    {
        private readonly Dictionary<int, string> _codeToText = new();
        private readonly HashSet<char> _owned = new();
        private char _mint = '\uA880';

        /// <summary>Register a collapsed ligature code with its preferred character.</summary>
        public string Register(int code, char desired)
        {
            if (_codeToText.TryGetValue(code, out var existing)) return existing;
            var ch = desired;
            while (_owned.Contains(ch)) ch = _mint++;
            _owned.Add(ch);
            var text = ch.ToString();
            _codeToText[code] = text;
            return text;
        }

        /// <summary>Register a code that has no derivable unicode at all.</summary>
        public string Mint(int code)
        {
            if (_codeToText.TryGetValue(code, out var existing)) return existing;
            var ch = _mint++;
            while (_owned.Contains(ch)) ch = _mint++;
            _owned.Add(ch);
            var text = ch.ToString();
            _codeToText[code] = text;
            return text;
        }
    }

    /// <summary>Resolve the /Font entries of one resource dictionary (a page's or a
    /// Form XObject's own) into decode-ready <see cref="HtmlFontRecord"/> records.</summary>
    private static Dictionary<string, HtmlFontRecord> ResolveFontsFromResources(PdfDictionary? resources,
        PdfReader reader, bool preferFontCmap = false,
        Dictionary<int, LigatureSubstitutor>? substitutors = null,
        string? defaultFontName = null, bool friendlyFamilies = false)
    {
        var result = new Dictionary<string, HtmlFontRecord>(StringComparer.Ordinal);
        if (resources is null) return result;
        var fontDict = reader.ResolveDict(resources.Get("Font"));
        if (fontDict is null) return result;

        foreach (var key in fontDict.Keys)
        {
            var fontRef = fontDict.Get(key);
            var font = reader.ResolveDict(fontRef);
            if (font is null) continue;

            var baseFont = font.GetName("BaseFont") ?? "sans-serif";
            var (family, weight, style) = MapFont(baseFont);
            // HtmlSaveOptions.DefaultFontName substitutes the requested face for every
            // source font — embedded ones included: the emitted font classes carry it
            // as the family, so the text both displays and round-trips in that face.
            if (!string.IsNullOrEmpty(defaultFontName)) family = defaultFontName;

            // Otherwise a font that ships as an @font-face program is named by its
            // FULL BaseFont there — subset prefix kept, separator-normalized
            // ("ACMJVR+Arial,Bold" → "ACMJVR+Arial Bold"); the class must reference
            // the SAME family, or the consumer substitutes a host face whose cmap
            // disagrees with a custom-encoded subset (an invoice re-imported through
            // such classes painted every run with a sibling font's garble), and a
            // bold sibling must get its own class rather than folding into the
            // regular face's. With FontSavingMode.DontSave nothing is embedded, so
            // there is no @font-face to match and the class instead keeps the font's
            // FRIENDLY family name (CssFamily below) — the raw BaseFont's style tail
            // ("Calibri-Bold") would otherwise leak into the CSS, which should
            // show the plain family ("Calibri").
            else if (!friendlyFamilies
                     && (HasEmbeddedProgram(font, reader) || SystemResolvable(baseFont)))
            {
                // System-resolvable fonts count too: the sidecar emitter embeds the
                // resolved face as an @font-face under the same BaseFont-derived name.
                var famTag = CssFaceFamily(baseFont);
                if (famTag.Length > 0) family = famTag;
            }
            else if (!friendlyFamilies && CjkSubstituteFamily(font, reader) is { } subFam)
            {
                // A substituted CJK font's class must reference the shipped
                // subset's @font-face name, exactly like an embedded program's.
                family = SubstituteTag(baseFont) + "+" + subFam;
            }

            // Parse ToUnicode CMap
            var toUnicodeMap = Text.TextAbsorber.ParseToUnicodeFromDict(font, reader);

            // A U+FFFF/U+FFFE destination is the producer's "unicode unknown"
            // (pdfTeX writes it for ligature glyphs). The /Differences glyph
            // name resolves where the CMap could not (/f_i → U+FB01); a code
            // neither can name drops back to the base decode.
            if (toUnicodeMap is not null)
            {
                List<int>? unknown = null;
                foreach (var (code, dst) in toUnicodeMap)
                    if (Text.TextAbsorber.IsUnknownToUnicodeDst(dst))
                        (unknown ??= new List<int>()).Add(code);
                if (unknown is not null)
                {
                    var rawNames = RawDifferencesNames(font, reader);
                    foreach (var code in unknown)
                    {
                        var resolved = rawNames is not null && rawNames.TryGetValue(code, out var nm)
                            ? Text.TextAbsorber.ResolveGlyphName(nm)
                            : null;
                        if (resolved is { Length: > 0 }) toUnicodeMap[code] = resolved;
                        else toUnicodeMap.Remove(code);
                    }
                }
            }

            // Check if this is a CID font (Type0)
            var fontSubtype = font.GetName("Subtype");
            var isCid = fontSubtype == "Type0";

            // FontEncodingRules.DecreaseToUnicodePriorityLevel: the font program's own
            // cmap subtable outranks the /ToUnicode CMap. Exporters that pre-compose
            // text sometimes map a combining-mark CID to a space (or another filler)
            // in /ToUnicode while the embedded cmap still carries the real codepoint
            // (e.g. Thai NIKHAHIT U+0E4D) — copy/paste from the HTML then loses the
            // character unless the cmap wins. Identity CIDs are glyph ids, so the
            // reverse cmap (gid → unicode) applies directly.
            Dictionary<int, int>? reverseCmap = null;
            if (preferFontCmap && isCid)
            {
                var descArr = reader.Resolve(font.Get("DescendantFonts")) as PdfArray;
                var descFont = descArr is { Count: > 0 } ? reader.ResolveDict(descArr[0]) : null;
                var descriptor = descFont is not null ? reader.ResolveDict(descFont.Get("FontDescriptor")) : null;
                var fontFile = descriptor is not null ? reader.ResolveStream(descriptor.Get("FontFile2")) : null;
                if (fontFile is not null)
                {
                    try
                    {
                        var parser = new Text.GlyphOutlineParser(reader.DecodeStream(fontFile));
                        reverseCmap = new Dictionary<int, int>();
                        foreach (var (ch, gid) in parser.CMap)
                            if (!reverseCmap.ContainsKey(gid)) reverseCmap[gid] = ch;
                    }
                    catch { reverseCmap = null; }
                }
            }

            // The ligature/unmapped-code model needs the embedded font program's
            // cmap coverage: a multi-char ToUnicode sequence stays expanded only
            // when the font can actually render its component characters, and an
            // Identity-encoded code with no unicode mapping at all is a bare glyph
            // id whose text form must be minted (U+A880 upward), exactly one new
            // character per glyph, shared across the pages of one conversion.
            var isIdentity = font.GetName("Encoding") is "Identity-H" or "Identity-V";
            var hasMultiDst = false;
            if (toUnicodeMap is not null)
                foreach (var dst in toUnicodeMap.Values)
                    if (CodePointCount(dst) > 1) { hasMultiDst = true; break; }

            // With Identity ENCODING the 2-byte code is the CID, but the CID is the
            // glyph id only under an Identity /CIDToGIDMap. A CIDToGIDMap STREAM
            // (packed big-endian uint16 per CID) marks the codes as true CIDs.
            PdfStream? c2gStream = null;
            if (isCid && isIdentity)
            {
                var descArr2 = reader.Resolve(font.Get("DescendantFonts")) as PdfArray;
                var descFont2 = descArr2 is { Count: > 0 } ? reader.ResolveDict(descArr2[0]) : null;
                var c2gObj = descFont2?.Get("CIDToGIDMap");
                c2gStream = c2gObj is not null ? reader.ResolveStream(c2gObj) : null;
            }

            HashSet<int>? cmapChars = null;
            Dictionary<int, int>? gidToUnicode = null;
            Func<int, bool>? glyphMapped = null;
            Func<int, double?>? embeddedAdvMilli = null;
            Func<int, double?>? programAdvMilli = null;
            Func<int, double?>? programCharAdvMilli = null;
            if (hasMultiDst || (isCid && isIdentity))
            {
                var ttf = GetEmbeddedTtf(font, reader);
                if (ttf is not null)
                {
                    try
                    {
                        var parser = new Text.GlyphOutlineParser(ttf);
                        cmapChars = new HashSet<int>(parser.CMap.Keys);
                        if (isCid && isIdentity)
                        {
                            gidToUnicode = new Dictionary<int, int>();
                            foreach (var (ch, gid) in parser.CMap)
                                if (!gidToUnicode.ContainsKey(gid)) gidToUnicode[gid] = ch;

                            // Thread the CIDToGIDMap stream so the map is keyed by the
                            // CODE the content stream actually shows (cid → gid → unicode).
                            byte[]? cgBytes = null;
                            if (c2gStream is not null)
                            {
                                var cg = reader.DecodeStream(c2gStream);
                                var cidToUnicode = new Dictionary<int, int>();
                                for (int cid = 0; cid * 2 + 1 < cg.Length; cid++)
                                {
                                    int gid = (cg[cid * 2] << 8) | cg[cid * 2 + 1];
                                    if (gid != 0 && gidToUnicode.TryGetValue(gid, out var u))
                                        cidToUnicode[cid] = u;
                                }
                                gidToUnicode = cidToUnicode;
                                cgBytes = cg;
                            }

                            // The program's own advance per shown code, for the
                            // em-compensation basis (cid → gid via the map, else
                            // Identity).
                            var upmProg = (double)parser.UnitsPerEm;
                            if (upmProg > 0)
                            {
                                var parserProg = parser;
                                var cgProg = cgBytes;
                                programAdvMilli = code =>
                                {
                                    var gid = cgProg is null
                                        ? code
                                        : code * 2 + 1 < cgProg.Length
                                            ? (cgProg[code * 2] << 8) | cgProg[code * 2 + 1]
                                            : 0;
                                    if (gid <= 0) return null;
                                    var w = parserProg.GetAdvanceWidth(gid);
                                    return w > 0 ? w * 1000.0 / upmProg : null;
                                };
                                // By CHARACTER: reverse the font's single-char
                                // ToUnicode so a ligature's components measure by
                                // the program's own f/t/i advances.
                                if (SingleCharToUnicode(font, reader) is { } uniOf)
                                {
                                    var u2code = new Dictionary<int, int>();
                                    foreach (var (code2, uni2) in uniOf)
                                        u2code.TryAdd(uni2, code2);
                                    var pam = programAdvMilli;
                                    programCharAdvMilli = ch =>
                                        u2code.TryGetValue(ch, out var c2) ? pam(c2) : null;
                                }
                            }

                            // A subset can carry several glyph VARIANTS of one
                            // character (duplicate instances from merged runs).
                            // The re-encoded face holds one glyph per character:
                            // the FIRST variant shown claims the slot, and later
                            // occurrences through a different variant (or through
                            // a multi-char ligature glyph) render in the CSS
                            // fallback face.
                            if (toUnicodeMap is not null)
                            {
                                int GidOf(int code) => cgBytes is null
                                    ? code
                                    : code * 2 + 1 < cgBytes.Length
                                        ? (cgBytes[code * 2] << 8) | cgBytes[code * 2 + 1]
                                        : 0;
                                var touForMapped = toUnicodeMap;
                                var slotWinner = new Dictionary<int, int>();
                                var cmapUnis = new HashSet<int>(parser.CMap.Keys);
                                var cmapGidSet = new HashSet<int>(parser.CMap.Values);
                                // With a DefaultFontName substitution the substituted
                                // face serves every character — the subset's variant
                                // structure is irrelevant and the machinery stays off.
                                var anyVariant = false;
                                if (string.IsNullOrEmpty(defaultFontName))
                                {
                                    foreach (var (cid0, txt0) in touForMapped)
                                    {
                                        if (txt0.Length == 0 || CodePointCount(txt0) != 1) continue;
                                        var g0 = GidOf(cid0);
                                        if (g0 != 0 && !cmapGidSet.Contains(g0)) { anyVariant = true; break; }
                                    }
                                }
                                // The whole variant/fallback machinery only exists for
                                // subsets that actually carry GID-only variant glyphs;
                                // ordinary subsets (or substituted fonts whose
                                // ToUnicode merely exceeds the cmap) keep the plain
                                // single-face model.
                                glyphMapped = !anyVariant ? null : code =>
                                {
                                    if (!touForMapped.TryGetValue(code, out var txt) || txt.Length == 0)
                                        return true;
                                    if (CodePointCount(txt) != 1)
                                    {
                                        // A ligature glyph whose expansion the subset can
                                        // render from component cmap glyphs stays in the
                                        // main face (the text is already expanded); only
                                        // an expansion with an uncovered component falls
                                        // to the fallback face.
                                        for (var ei = 0; ei < txt.Length; )
                                        {
                                            var cpt = char.ConvertToUtf32(txt, ei);
                                            if (!cmapUnis.Contains(cpt)) return false;
                                            ei += char.IsSurrogatePair(txt, ei) ? 2 : 1;
                                        }
                                        return true;
                                    }
                                    var gid = GidOf(code);
                                    if (gid == 0) return true;
                                    var uni = char.ConvertToUtf32(txt, 0);
                                    // A character the subset's cmap does not know at
                                    // all can only render from the fallback face.
                                    if (!cmapUnis.Contains(uni)) return false;
                                    if (!slotWinner.TryGetValue(uni, out var w))
                                    {
                                        slotWinner[uni] = gid;
                                        return true;
                                    }
                                    return w == gid;
                                };

                                // A subset carrying GID-only variant glyphs can
                                // only serve as itself (re-encoded with its own
                                // metrics), so the browser model measures such a
                                // font's glyphs by the embedded program's
                                // advances. A fully cmap-addressable subset is
                                // swapped for the resolved installed face instead
                                // and keeps the face-metric model.
                                var hasVariantGlyphs = anyVariant;
                                var parserForAdv = parser;
                                var upmForAdv = (double)parser.UnitsPerEm;
                                if (hasVariantGlyphs && upmForAdv > 0)
                                    embeddedAdvMilli = code =>
                                    {
                                        var gid = GidOf(code);
                                        return gid == 0
                                            ? null
                                            : parserForAdv.GetAdvanceWidth(gid) * 1000.0 / upmForAdv;
                                    };
                            }
                        }
                    }
                    catch { cmapChars = null; gidToUnicode = null; glyphMapped = null; }
                }

                // Subset programs often carry no cmap at all; a component character
                // still counts as renderable when the font's own ToUnicode maps some
                // char code to it — that code's glyph is in the subset. (This is what
                // separates an expandable "ti" from one that must collapse: a subset
                // with no single-char 't' mapping has no component glyphs for its
                // t-side ligatures to expand into.)
                if (cmapChars is not null && toUnicodeMap is not null)
                {
                    foreach (var dst in toUnicodeMap.Values)
                        if (dst.Length > 0 && CodePointCount(dst) == 1)
                            cmapChars.Add(char.ConvertToUtf32(dst, 0));
                }
            }

            LigatureSubstitutor? substitutor = null;
            if (cmapChars is not null && (hasMultiDst || (isCid && isIdentity)))
            {
                if (substitutors is not null)
                {
                    var objNum = fontRef is Core.PdfIndirectRef ir ? ir.ObjectNumber : -1;
                    if (objNum >= 0)
                    {
                        if (!substitutors.TryGetValue(objNum, out substitutor))
                            substitutors[objNum] = substitutor = new LigatureSubstitutor();
                    }
                    else substitutor = new LigatureSubstitutor();
                }
                else substitutor = new LigatureSubstitutor();
            }

            Func<byte[], string>? toUnicodeFunc = null;
            if (toUnicodeMap is not null || reverseCmap is not null
                || (isCid && isIdentity && substitutor is not null))
            {
                var map = toUnicodeMap ?? new Dictionary<int, string>();
                var subst = substitutor;
                var cmap = cmapChars;
                var gidUni = gidToUnicode;
                var identity = isIdentity;
                var cidNotGid = c2gStream is not null;
                toUnicodeFunc = (byte[] bytes) => ApplyToUnicode(bytes,
                    map, isCid, reverseCmap, cmap, subst, identity, gidUni, cidNotGid);
            }

            var ascentFactor = 1.0;
            var lineHeightEm = 0.0;
            var ascSfnt = GetEmbeddedTtf(font, reader) ?? GetEmbeddedOpenType(font, reader);
            if (ascSfnt is null && !friendlyFamilies)
                try { ascSfnt = Text.SystemFontResolver.Resolve(baseFont); } catch { }
            if (ascSfnt is not null)
            {
                var wa = SfntWinAscentFactor(ascSfnt);
                if (wa > 0) ascentFactor = wa;
                lineHeightEm = SfntLineHeightFactor(ascSfnt);
            }
            // A bare CFF (Type1C) or Type1 subset carries no sfnt at all, so neither
            // hhea nor OS/2 is reachable; the descriptor's own ascent/descent is the
            // only face metric on hand. A font with no embedded program at all still
            // measures by whatever face the browser substitutes, not by the descriptor.
            if (lineHeightEm <= 0 && HasEmbeddedProgram(font, reader))
                lineHeightEm = DescriptorLineHeightFactor(font, reader);

            // Without a ToUnicode CMap the show bytes still decode through the
            // font's base encoding (MacRoman/WinAnsi/Standard, /Differences with
            // glyph names, embedded-program cmap/post) rather than raw Latin1 —
            // a MacRomanEncoding font's quotes (0xD2/0xD5) otherwise render as
            // Ò/Õ mojibake.
            var fontForDecode = font;
            Func<byte[], string>? baseDecode = toUnicodeFunc is not null
                ? null
                : bytes => Text.TextAbsorber.DecodeStringPublic(bytes, null, fontForDecode, reader);

            // The EMBEDDED program's character coverage: a rendered char the
            // subset cannot map falls to the CSS fallback face, which cuts a
            // span and switches the measuring metrics.
            Func<int, bool>? subsetHas = null;
            var subsetSfnt = GetEmbeddedTtf(font, reader) ?? GetEmbeddedOpenType(font, reader);
            if (subsetSfnt is not null)
            {
                try
                {
                    var subsetParser = new Text.GlyphOutlineParser(subsetSfnt);
                    if (subsetParser.CMap.Count > 0)
                        subsetHas = cp => subsetParser.CMap.TryGetValue(cp, out var gg) && gg != 0;
                }
                catch { /* unparsable program: assume full coverage */ }
            }
            else if (GetEmbeddedType1(font, reader) is { } t1Cov)
            {
                // A Type 1 program serves through the synthesized sfnt whose cmap
                // is the glyph names + the dict's Differences/ToUnicode supplement
                // — the same coverage governs which chars the face can render (a
                // TeX ligature-only subset covers ﬁ but NOT the letters f and i).
                try
                {
                    var t1Src = Text.Type1GlyphSource.TryLoad(t1Cov.Data, t1Cov.Length1, t1Cov.Length2);
                    if (t1Src is not null && t1Src.CMap.Count > 0)
                    {
                        var t1Cmap = new Dictionary<int, int>(t1Src.CMap);
                        var names = RawDifferencesNames(font, reader);
                        var unis = SingleCharToUnicode(font, reader);
                        if (unis is not null)
                            foreach (var (code, uni) in unis)
                            {
                                var gid = names is not null && names.TryGetValue(code, out var nm)
                                    ? t1Src.GidForName(nm) : 0;
                                if (gid > 0) t1Cmap.TryAdd(uni, gid);
                            }
                        subsetHas = cp => t1Cmap.TryGetValue(cp, out var gg) && gg != 0;
                    }
                }
                catch { /* unparsable program: assume full coverage */ }
            }

            // A bare CFF (Type1C) — or an Adobe Type 1 program (FontFile) — is
            // shipped as a TrueType sfnt synthesized from the charstrings, so the
            // served face's advances are the program's own — the same values the
            // font dict's /Widths carries. Handing the browser model those
            // advances leaves each glyph's error as exactly its Tc/Tw and TJ
            // kern contribution, which is what the letter-spacing solves against.
            var advanceMap = BuildAdvanceMap(font, reader, isCid);
            if (embeddedAdvMilli is null && ascSfnt is null && advanceMap is not null
                && (GetEmbeddedBareCff(font, reader) is not null
                    || GetEmbeddedType1(font, reader) is not null))
            {
                var advForCff = advanceMap;
                embeddedAdvMilli = code =>
                {
                    var w = advForCff(code) * 1000.0;   // em fraction -> milli-em
                    return w > 0 ? w : null;
                };
            }

            // By-char program advance for ligature-component measuring: reverse
            // the single-char ToUnicode onto whichever per-code advance source
            // this font serves through.
            if (programCharAdvMilli is null && (programAdvMilli ?? embeddedAdvMilli) is { } advAny
                && SingleCharToUnicode(font, reader) is { } uniAll)
            {
                var u2codeAll = new Dictionary<int, int>();
                foreach (var (c3, u3) in uniAll) u2codeAll.TryAdd(u3, c3);
                programCharAdvMilli = ch =>
                    u2codeAll.TryGetValue(ch, out var cc2) ? advAny(cc2) : null;
            }

            // A substituted CJK font serves the substitute face's subset, so the
            // browser-model metrics are that face's own advances — the same
            // basis the shipped @font-face program carries, which is what the
            // em-compensation solve and the re-import both measure against.
            var substituteFace = false;
            if (embeddedAdvMilli is null && subsetSfnt is null
                && CjkSubstituteFamily(font, reader) is { } subFam3
                && ResolveSubstituteParser(subFam3) is { } subParser
                && SubstituteCodeToUnicode(font, reader) is { } subUniMap)
            {
                substituteFace = true;
                double subUpm = subParser.UnitsPerEm <= 0 ? 1000 : subParser.UnitsPerEm;
                double? AdvOfUni(int u) => subParser.CMap.TryGetValue(u, out var g5) && g5 > 0
                    ? subParser.GetAdvanceWidth(g5) * 1000.0 / subUpm
                    : null;
                var subCode2Uni = subUniMap;
                embeddedAdvMilli = code => subCode2Uni.TryGetValue(code, out var u5) ? AdvOfUni(u5) : null;
                programCharAdvMilli = ch => AdvOfUni(ch);
                // SubsetHas stays null: the built subset covers every glyph the
                // font's own /W or ToUnicode declares, and its contract keys by
                // CODEPOINT while an Identity-H font shows CIDs — a unicode-keyed
                // coverage probe here split runs at effectively random chars.
            }

            result[key] = new HtmlFontRecord
            {
                Family = family,
                // stl_ CSS font-family: the standard-14 names keep their generic
                // fallback chain; anything else (embedded/system faces) is the bare
                // friendly family name.
                CssFamily = family != "sans-serif" ? family : FriendlyFontFamily(baseFont),
                Weight = weight,
                Style = style,
                ToUnicode = toUnicodeFunc,
                BaseDecode = baseDecode,
                IsCidFont = isCid,
                IsType3 = fontSubtype == "Type3",
                AscentFactor = ascentFactor,
                LineHeightEm = lineHeightEm,
                AdvanceOf = advanceMap,
                SubsetHas = subsetHas,
                GlyphMapped = glyphMapped,
                EmbeddedAdvMilli = embeddedAdvMilli,
                ProgramAdvMilli = programAdvMilli,
                ProgramCharAdvMilli = programCharAdvMilli,
                SubstituteFace = substituteFace,
            };
        }
        return result;
    }

    /// <summary>OS/2 usWinAscent / head unitsPerEm from an sfnt (TrueType or OTTO),
    /// or 0 when either table is missing/short.</summary>
    private static double SfntWinAscentFactor(byte[] sfnt)
    {
        try
        {
            if (sfnt.Length < 12) return 0;
            int U16(int at) => (sfnt[at] << 8) | sfnt[at + 1];
            var numTables = U16(4);
            int os2 = 0, head = 0;
            for (var t = 0; t < numTables; t++)
            {
                var rec = 12 + t * 16;
                if (rec + 16 > sfnt.Length) return 0;
                var tag = System.Text.Encoding.ASCII.GetString(sfnt, rec, 4);
                var off = (sfnt[rec + 8] << 24) | (sfnt[rec + 9] << 16) | (sfnt[rec + 10] << 8) | sfnt[rec + 11];
                if (tag == "OS/2") os2 = off;
                else if (tag == "head") head = off;
            }
            if (os2 == 0 || head == 0) return 0;
            if (os2 + 76 > sfnt.Length || head + 20 > sfnt.Length) return 0;
            var upm = U16(head + 18);
            var winAscent = U16(os2 + 74);
            return upm > 0 ? winAscent / (double)upm : 0;
        }
        catch { return 0; }
    }

    /// <summary>hhea (ascender + |descender|) / unitsPerEm — the
    /// line-height class value for a font (1.117188 for Arial); 0 when unreadable.</summary>
    private static double SfntLineHeightFactor(byte[] sfnt)
    {
        try
        {
            if (sfnt.Length < 12) return 0;
            int U16(int at) => (sfnt[at] << 8) | sfnt[at + 1];
            int S16(int at) { var v = U16(at); return v >= 0x8000 ? v - 0x10000 : v; }
            var numTables = U16(4);
            int hhea = 0, head = 0, os2 = 0;
            for (var t = 0; t < numTables; t++)
            {
                var rec = 12 + t * 16;
                if (rec + 16 > sfnt.Length) return 0;
                var tag = System.Text.Encoding.ASCII.GetString(sfnt, rec, 4);
                var off = (sfnt[rec + 8] << 24) | (sfnt[rec + 9] << 16) | (sfnt[rec + 10] << 8) | sfnt[rec + 11];
                if (tag == "hhea") hhea = off;
                else if (tag == "head") head = off;
                else if (tag == "OS/2") os2 = off;
            }
            if (head == 0 || head + 20 > sfnt.Length) return 0;
            var upm = U16(head + 18);
            if (upm <= 0) return 0;
            var hheaLh = 0.0;
            if (hhea != 0 && hhea + 8 <= sfnt.Length)
            {
                var ascender = S16(hhea + 4);
                var descender = S16(hhea + 6);
                hheaLh = (ascender + Math.Abs(descender)) / (double)upm;
            }
            // A subset whose hhea is the degenerate 1-em placeholder (1536/-512
            // at 2048) still carries the real face metrics in OS/2
            // usWinAscent/usWinDescent; a live hhea stays authoritative.
            if (hheaLh != 0.0 && hheaLh != 1.0) return hheaLh;
            if (os2 > 0 && os2 + 78 <= sfnt.Length)
            {
                var winA = U16(os2 + 74);
                var winD = U16(os2 + 76);
                if (winA + winD > 0) return (winA + winD) / (double)upm;
            }
            return hheaLh;
        }
        catch { return 0; }
    }

    /// <summary>
    /// Apply a ToUnicode CMap to raw string bytes.
    /// For CID fonts (Type0), character codes are 2 bytes each.
    /// For simple fonts, character codes are 1 byte each.
    /// </summary>
    private static string ApplyToUnicode(byte[] bytes, Dictionary<int, string> map, bool isCid,
        Dictionary<int, int>? reverseCmap = null, HashSet<int>? cmapChars = null,
        LigatureSubstitutor? substitutor = null, bool isIdentity = false,
        Dictionary<int, int>? gidToUnicode = null, bool cidCodeIsNotGid = false)
    {
        var sb = new StringBuilder();

        if (isCid)
        {
            // 2-byte character codes
            for (var i = 0; i + 1 < bytes.Length; i += 2)
            {
                var code = (bytes[i] << 8) | bytes[i + 1];
                // A reverse font-cmap entry (gid → unicode) outranks /ToUnicode when
                // the caller asked for cmap priority (see ResolveFonts).
                if (reverseCmap is not null && reverseCmap.TryGetValue(code, out var cmapCh))
                    sb.Append(char.ConvertFromUtf32(cmapCh));
                else if (map.TryGetValue(code, out var unicode))
                    sb.Append(MapDst(code, unicode, cmapChars, substitutor));
                else if (isIdentity && gidToUnicode is not null
                         && gidToUnicode.TryGetValue(code, out var uniCh))
                    sb.Append(char.ConvertFromUtf32(uniCh));
                else if (isIdentity && cidCodeIsNotGid)
                    // A CIDToGIDMap STREAM marks the code as a true CID, not a bare
                    // glyph id — nothing to mint. Fall back to the CID
                    // as a raw character (producers commonly assign CID = Unicode).
                    sb.Append((char)code);
                else if (isIdentity && substitutor is not null)
                    sb.Append(substitutor.Mint(code));
                else
                    sb.Append('?');
            }
        }
        else
        {
            // 1-byte character codes
            foreach (var b in bytes)
            {
                if (map.TryGetValue(b, out var unicode))
                    sb.Append(MapDst(b, unicode, cmapChars, substitutor));
                else
                    sb.Append((char)b);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// The output text for one char code's ToUnicode sequence. Multi-char sequences
    /// stay expanded when the font's cmap can render every component character;
    /// otherwise the sequence collapses to a single stand-in registered with the
    /// font's substitutor (see <see cref="LigatureSubstitutor"/>).
    /// </summary>
    private static string MapDst(int code, string dst, HashSet<int>? cmapChars,
        LigatureSubstitutor? substitutor)
    {
        if (CodePointCount(dst) <= 1)
        {
            // A single-char dst naming an UNASSIGNED Unicode code point (category
            // Cn — e.g. a custom-encoded font whose identity ToUnicode lands raw
            // char codes in the U+FFDD / U+FFF0–FFF8 reserved gaps) is not real
            // text: each such char CODE is replaced with a
            // fresh minted character (U+A880 upward, in first-use order across the
            // conversion) so distinct glyphs keep distinct text. Assigned chars —
            // including private-use — pass through untouched.
            if (substitutor is not null && dst.Length == 1
                && System.Globalization.CharUnicodeInfo.GetUnicodeCategory(dst[0])
                    == System.Globalization.UnicodeCategory.OtherNotAssigned)
                return substitutor.Mint(code);
            return dst;
        }
        if (dst == SpaceLigature) return " ";
        if (cmapChars is null || substitutor is null || AllInCmap(dst, cmapChars)) return dst;
        return substitutor.Register(code, StandardLigatureChar(dst));
    }

    /// <summary>The human-readable CSS family for a /BaseFont name: subset prefix
    /// ("ABCDEF+") and style suffix (the first "-"/"," segment and any trailing
    /// Bold/Italic/Oblique words) stripped, then glued camel-case words re-spaced —
    /// "CenturyGothic" → "Century Gothic", "Calibri-Bold" → "Calibri",
    /// "TimesNewRomanPSMT" → "Times New Roman".</summary>
    internal static string FriendlyFontFamily(string baseFont)
    {
        var name = baseFont;
        if (name.Length > 7 && name[6] == '+') name = name[7..];
        var cut = name.IndexOfAny(new[] { '-', ',' });
        if (cut > 0) name = name[..cut];
        // PostScript naming tails that are not part of the family.
        foreach (var tail in new[] { "PSMT", "PS", "MT" })
            if (name.Length > tail.Length && name.EndsWith(tail, StringComparison.Ordinal))
            { name = name[..^tail.Length]; break; }
        foreach (var styleWord in new[] { "BoldItalic", "BoldOblique", "Bold", "Italic", "Oblique" })
            if (name.Length > styleWord.Length && name.EndsWith(styleWord, StringComparison.Ordinal))
            { name = name[..^styleWord.Length]; break; }
        if (name.Length == 0) return "sans-serif";
        var sb = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i])
                && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1])))
                sb.Append(' ');
            sb.Append(name[i]);
        }
        return sb.ToString();
    }
}
