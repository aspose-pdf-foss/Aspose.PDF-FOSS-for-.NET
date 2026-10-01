using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Converters;

public sealed partial class PdfToHtmlConverter
{
    /// <summary>The advance map, the substitute-face verdict and the record itself, added under the resource's key.</summary>
    private static void FinishFontRecord(FontRecordState fr, string key, PdfReader reader, string family, string weight, string style, Dictionary<string, HtmlFontRecord> result)
    {
        fr.advanceMap = BuildAdvanceMap(fr.font, reader, fr.isCid);
        if (fr.embeddedAdvMilli is null && fr.ascSfnt is null && fr.advanceMap is not null
            && (GetEmbeddedBareCff(fr.font, reader) is not null
                || GetEmbeddedType1(fr.font, reader) is not null))
        {
            var advForCff = fr.advanceMap;
            fr.embeddedAdvMilli = code =>
            {
                var w = advForCff(code) * 1000.0;   // em fraction -> milli-em
                return w > 0 ? w : null;
            };
        }

        // By-char program advance for ligature-component measuring: reverse
        // the single-char ToUnicode onto whichever per-code advance source
        // this font serves through.
        if (fr.programCharAdvMilli is null && (fr.programAdvMilli ?? fr.embeddedAdvMilli) is { } advAny
            && SingleCharToUnicode(fr.font, reader) is { } uniAll)
        {
            var u2codeAll = new Dictionary<int, int>();
            foreach (var (c3, u3) in uniAll) u2codeAll.TryAdd(u3, c3);
            fr.programCharAdvMilli = ch =>
                u2codeAll.TryGetValue(ch, out var cc2) ? advAny(cc2) : null;
        }

        fr.substituteFace = false;
        if (fr.embeddedAdvMilli is null && fr.subsetSfnt is null
            && CjkSubstituteFamily(fr.font, reader) is { } subFam3
            && ResolveSubstituteParser(subFam3) is { } subParser
            && SubstituteCodeToUnicode(fr.font, reader) is { } subUniMap)
        {
            fr.substituteFace = true;
            double subUpm = subParser.UnitsPerEm <= 0 ? 1000 : subParser.UnitsPerEm;
            double? AdvOfUni(int u) => subParser.CMap.TryGetValue(u, out var g5) && g5 > 0
                ? subParser.GetAdvanceWidth(g5) * 1000.0 / subUpm
                : null;
            var subCode2Uni = subUniMap;
            fr.embeddedAdvMilli = code => subCode2Uni.TryGetValue(code, out var u5) ? AdvOfUni(u5) : null;
            fr.programCharAdvMilli = ch => AdvOfUni(ch);
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
            CssFamily = family != "sans-serif" ? family : FriendlyFontFamily(fr.baseFont),
            Weight = weight,
            Style = style,
            ToUnicode = fr.toUnicodeFunc,
            BaseDecode = fr.baseDecode,
            IsCidFont = fr.isCid,
            IsType3 = fr.fontSubtype == "Type3",
            AscentFactor = fr.ascentFactor,
            LineHeightEm = fr.lineHeightEm,
            AdvanceOf = fr.advanceMap,
            SubsetHas = fr.subsetHas,
            GlyphMapped = fr.glyphMapped,
            EmbeddedAdvMilli = fr.embeddedAdvMilli,
            ProgramAdvMilli = fr.programAdvMilli,
            ProgramCharAdvMilli = fr.programCharAdvMilli,
            SubstituteFace = fr.substituteFace,
        };
    }

    /// <summary>The face's ascent and line height, and the subset coverage its embedded program declares.</summary>
    private static void ReadFontMetrics(FontRecordState fr, PdfReader reader, bool friendlyFamilies)
    {
        fr.ascentFactor = 1.0;
        fr.lineHeightEm = 0.0;
        fr.ascSfnt = GetEmbeddedTtf(fr.font, reader) ?? GetEmbeddedOpenType(fr.font, reader);
        if (fr.ascSfnt is null && !friendlyFamilies)
            try { fr.ascSfnt = Text.SystemFontResolver.Resolve(fr.baseFont); } catch { }
        if (fr.ascSfnt is not null)
        {
            var wa = SfntWinAscentFactor(fr.ascSfnt);
            if (wa > 0) fr.ascentFactor = wa;
            fr.lineHeightEm = SfntLineHeightFactor(fr.ascSfnt);
        }
        // A bare CFF (Type1C) or Type1 subset carries no sfnt at all, so neither
        // hhea nor OS/2 is reachable; the descriptor's own ascent/descent is the
        // only face metric on hand. A font with no embedded program at all still
        // measures by whatever face the browser substitutes, not by the descriptor.
        if (fr.lineHeightEm <= 0 && HasEmbeddedProgram(fr.font, reader))
            fr.lineHeightEm = DescriptorLineHeightFactor(fr.font, reader);

        fr.fontForDecode = fr.font;
        fr.baseDecode = fr.toUnicodeFunc is not null
            ? null
            : bytes => Text.TextAbsorber.DecodeStringPublic(bytes, null, fr.fontForDecode, reader);

        fr.subsetHas = null;
        fr.subsetSfnt = GetEmbeddedTtf(fr.font, reader) ?? GetEmbeddedOpenType(fr.font, reader);
        if (fr.subsetSfnt is not null)
        {
            try
            {
                var subsetParser = new Text.GlyphOutlineParser(fr.subsetSfnt);
                if (subsetParser.CMap.Count > 0)
                    fr.subsetHas = cp => subsetParser.CMap.TryGetValue(cp, out var gg) && gg != 0;
            }
            catch { /* unparsable program: assume full coverage */ }
        }
        else if (GetEmbeddedType1(fr.font, reader) is { } t1Cov)
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
                    var names = RawDifferencesNames(fr.font, reader);
                    var unis = SingleCharToUnicode(fr.font, reader);
                    if (unis is not null)
                        foreach (var (code, uni) in unis)
                        {
                            var gid = names is not null && names.TryGetValue(code, out var nm)
                                ? t1Src.GidForName(nm) : 0;
                            if (gid > 0) t1Cmap.TryAdd(uni, gid);
                        }
                    fr.subsetHas = cp => t1Cmap.TryGetValue(cp, out var gg) && gg != 0;
                }
            }
            catch { /* unparsable program: assume full coverage */ }
        }
    }

    /// <summary>The ligature substitutor and the code-to-text function the run decoder calls.</summary>
    private static void BuildFontDecoders(FontRecordState fr, Dictionary<int, LigatureSubstitutor>? substitutors)
    {
        fr.substitutor = null;
        if (fr.cmapChars is not null && (fr.hasMultiDst || (fr.isCid && fr.isIdentity)))
        {
            if (substitutors is not null)
            {
                var objNum = fr.fontRef is Core.PdfIndirectRef ir ? ir.ObjectNumber : -1;
                if (objNum >= 0)
                {
                    if (!substitutors.TryGetValue(objNum, out fr.substitutor))
                        substitutors[objNum] = fr.substitutor = new LigatureSubstitutor();
                }
                else fr.substitutor = new LigatureSubstitutor();
            }
            else fr.substitutor = new LigatureSubstitutor();
        }

        fr.toUnicodeFunc = null;
        if (fr.toUnicodeMap is not null || fr.reverseCmap is not null
            || (fr.isCid && fr.isIdentity && fr.substitutor is not null))
        {
            var map = fr.toUnicodeMap ?? new Dictionary<int, string>();
            var subst = fr.substitutor;
            var cmap = fr.cmapChars;
            var gidUni = fr.gidToUnicode;
            var identity = fr.isIdentity;
            var cidNotGid = fr.c2gStream is not null;
            fr.toUnicodeFunc = (byte[] bytes) => ApplyToUnicode(bytes,
                map, fr.isCid, fr.reverseCmap, cmap, subst, identity, gidUni, cidNotGid);
        }
    }

    /// <summary>A CID or multi-destination font: its program cmap, the glyph coverage and the per-glyph advances read from it.</summary>
    private static void ReadFontCmaps(FontRecordState fr, PdfReader reader, string? defaultFontName)
    {
        if (!(fr.hasMultiDst || (fr.isCid && fr.isIdentity))) return;
        var ttf = GetEmbeddedTtf(fr.font, reader);
        if (ttf is not null)
        {
            try
            {
                ReadFontProgramCmap(fr, reader, ttf, defaultFontName);
            }
            catch { fr.cmapChars = null; fr.gidToUnicode = null; fr.glyphMapped = null; }
        }

        // Subset programs often carry no cmap at all; a component character
        // still counts as renderable when the font's own ToUnicode maps some
        // char code to it — that code's glyph is in the subset. (This is what
        // separates an expandable "ti" from one that must collapse: a subset
        // with no single-char 't' mapping has no component glyphs for its
        // t-side ligatures to expand into.)
        if (fr.cmapChars is not null && fr.toUnicodeMap is not null)
        {
            foreach (var dst in fr.toUnicodeMap.Values)
                if (dst.Length > 0 && CodePointCount(dst) == 1)
                    fr.cmapChars.Add(char.ConvertToUtf32(dst, 0));
        }
    }

    /// <summary>The ToUnicode map, the reverse cmap, the identity flag and the CIDToGID stream of the resource.</summary>
    private static void ReadFontEncoding(FontRecordState fr, PdfReader reader, bool preferFontCmap)
    {
        fr.toUnicodeMap = Text.TextAbsorber.ParseToUnicodeFromDict(fr.font, reader);

        // A U+FFFF/U+FFFE destination is the producer's "unicode unknown"
        // (pdfTeX writes it for ligature glyphs). The /Differences glyph
        // name resolves where the CMap could not (/f_i → U+FB01); a code
        // neither can name drops back to the base decode.
        if (fr.toUnicodeMap is not null)
        {
            List<int>? unknown = null;
            foreach (var (code, dst) in fr.toUnicodeMap)
                if (Text.TextAbsorber.IsUnknownToUnicodeDst(dst))
                    (unknown ??= new List<int>()).Add(code);
            if (unknown is not null)
            {
                var rawNames = RawDifferencesNames(fr.font, reader);
                foreach (var code in unknown)
                {
                    var resolved = rawNames is not null && rawNames.TryGetValue(code, out var nm)
                        ? Text.TextAbsorber.ResolveGlyphName(nm)
                        : null;
                    if (resolved is { Length: > 0 }) fr.toUnicodeMap[code] = resolved;
                    else fr.toUnicodeMap.Remove(code);
                }
            }
        }

        fr.fontSubtype = fr.font.GetName("Subtype");
        fr.isCid = fr.fontSubtype == "Type0";

        fr.reverseCmap = null;
        if (preferFontCmap && fr.isCid)
        {
            var descArr = reader.Resolve(fr.font.Get("DescendantFonts")) as PdfArray;
            var descFont = descArr is { Count: > 0 } ? reader.ResolveDict(descArr[0]) : null;
            var descriptor = descFont is not null ? reader.ResolveDict(descFont.Get("FontDescriptor")) : null;
            var fontFile = descriptor is not null ? reader.ResolveStream(descriptor.Get("FontFile2")) : null;
            if (fontFile is not null)
            {
                try
                {
                    var parser = new Text.GlyphOutlineParser(reader.DecodeStream(fontFile));
                    fr.reverseCmap = new Dictionary<int, int>();
                    foreach (var (ch, gid) in parser.CMap)
                        if (!fr.reverseCmap.ContainsKey(gid)) fr.reverseCmap[gid] = ch;
                }
                catch { fr.reverseCmap = null; }
            }
        }

        fr.isIdentity = fr.font.GetName("Encoding") is "Identity-H" or "Identity-V";
        fr.hasMultiDst = false;
        if (fr.toUnicodeMap is not null)
            foreach (var dst in fr.toUnicodeMap.Values)
                if (CodePointCount(dst) > 1) { fr.hasMultiDst = true; break; }

        fr.c2gStream = null;
        if (fr.isCid && fr.isIdentity)
        {
            var descArr2 = reader.Resolve(fr.font.Get("DescendantFonts")) as PdfArray;
            var descFont2 = descArr2 is { Count: > 0 } ? reader.ResolveDict(descArr2[0]) : null;
            var c2gObj = descFont2?.Get("CIDToGIDMap");
            fr.c2gStream = c2gObj is not null ? reader.ResolveStream(c2gObj) : null;
        }

        fr.cmapChars = null;
        fr.gidToUnicode = null;
        fr.glyphMapped = null;
        fr.embeddedAdvMilli = null;
        fr.programAdvMilli = null;
        fr.programCharAdvMilli = null;
    }

    /// <summary>The embedded program's own cmap: its glyph coverage, the CID-to-Unicode map and the advances measured from its outlines.</summary>
    private static void ReadFontProgramCmap(FontRecordState fr, PdfReader reader, byte[] ttf, string? defaultFontName)
    {
        var parser = new Text.GlyphOutlineParser(ttf);
        fr.cmapChars = new HashSet<int>(parser.CMap.Keys);
        if (!(fr.isCid && fr.isIdentity)) return;
        fr.gidToUnicode = new Dictionary<int, int>();
        foreach (var (ch, gid) in parser.CMap)
            if (!fr.gidToUnicode.ContainsKey(gid)) fr.gidToUnicode[gid] = ch;

        // Thread the CIDToGIDMap stream so the map is keyed by the
        // CODE the content stream actually shows (cid → gid → unicode).
        byte[]? cgBytes = null;
        if (fr.c2gStream is not null)
        {
            var cg = reader.DecodeStream(fr.c2gStream);
            var cidToUnicode = new Dictionary<int, int>();
            for (int cid = 0; cid * 2 + 1 < cg.Length; cid++)
            {
                int gid = (cg[cid * 2] << 8) | cg[cid * 2 + 1];
                if (gid != 0 && fr.gidToUnicode.TryGetValue(gid, out var u))
                    cidToUnicode[cid] = u;
            }
            fr.gidToUnicode = cidToUnicode;
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
            fr.programAdvMilli = code =>
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
            if (SingleCharToUnicode(fr.font, reader) is { } uniOf)
            {
                var u2code = new Dictionary<int, int>();
                foreach (var (code2, uni2) in uniOf)
                    u2code.TryAdd(uni2, code2);
                var pam = fr.programAdvMilli;
                fr.programCharAdvMilli = ch =>
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
        MapFontGlyphsToUnicode(fr, parser, cgBytes, defaultFontName);
    }

    /// <summary>Each mapped glyph's Unicode, and whether the program's cmap covers the components a ligature would expand into.</summary>
    private static void MapFontGlyphsToUnicode(FontRecordState fr, Text.GlyphOutlineParser parser, byte[]? cgBytes, string? defaultFontName)
    {
        if (fr.toUnicodeMap is not null)
        {
            int GidOf(int code) => cgBytes is null
                ? code
                : code * 2 + 1 < cgBytes.Length
                    ? (cgBytes[code * 2] << 8) | cgBytes[code * 2 + 1]
                    : 0;
            var touForMapped = fr.toUnicodeMap;
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
            fr.glyphMapped = !anyVariant ? null : code =>
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
                fr.embeddedAdvMilli = code =>
                {
                    var gid = GidOf(code);
                    return gid == 0
                        ? null
                        : parserForAdv.GetAdvanceWidth(gid) * 1000.0 / upmForAdv;
                };
        }
    }
}
