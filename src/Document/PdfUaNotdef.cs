using Aspose.Pdf.Core;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>
    /// PDF/UA-1 clause 7.21.8: no text may show the .notdef glyph. A code whose glyph the
    /// embedded program lacks shows it - a composite font's CID its CIDToGIDMap sends to no
    /// glyph (or past the program's last), a Type 1 font's code whose glyph name the program
    /// does not define. The conversion cannot supply the missing glyph without changing what
    /// the page shows, so each such font is logged, once, as not convertible.
    /// </summary>
    private void ReportNotdefReferencesForUa(PdfFormatConversionOptions options)
    {
        var tests = new Dictionary<PdfDictionary, Func<int, bool>?>(ReferenceEqualityComparer.Instance);
        var reported = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        var unicodeTests = new Dictionary<PdfDictionary, Func<int, bool>?>(ReferenceEqualityComparer.Instance);
        var unicodeReported = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        var usedCodes = new Dictionary<PdfDictionary, HashSet<int>>(ReferenceEqualityComparer.Instance);
        foreach (var page in Pages)
        {
            var shows = ShownStrings(page);
            foreach (var (font, raw) in shows)
            {
                var show = (Text: raw, Font: font);
                var twoBytes = font.GetName("Subtype") == "Type0";
                if (!usedCodes.TryGetValue(font, out var used)) usedCodes[font] = used = new HashSet<int>();
                used.UnionWith(ShownCodes(show.Text ?? string.Empty, twoBytes));
                if (!unicodeReported.Contains(font))
                {
                    if (!unicodeTests.TryGetValue(font, out var lacksUnicode))
                    {
                        try { lacksUnicode = UnicodeMissingTest(font); }
                        catch { lacksUnicode = null; }
                        unicodeTests[font] = lacksUnicode;
                    }
                    if (lacksUnicode is not null && ShownCodes(show.Text ?? string.Empty, twoBytes).Any(lacksUnicode))
                    {
                        unicodeReported.Add(font);
                        options.ConversionLog.Add(new Optimization.PdfAViolation
                        {
                            Rule = "FontUnicode",
                            Clause = "7.21.7",
                            Section = "Fonts",
                            PageNumber = page.Number,
                            Description = $"Text in font '{font.GetName("BaseFont") ?? "(unnamed)"}' shows codes the document "
                                + "nowhere maps to characters (no ToUnicode entry, no standard glyph name, no Unicode in the "
                                + "font program), so the author has to state them",
                            Convertable = false,
                        });
                    }
                }
                if (reported.Contains(font)) continue;
                if (!tests.TryGetValue(font, out var isNotdef))
                {
                    try { isNotdef = NotdefTest(font); }
                    catch { isNotdef = null; }
                    tests[font] = isNotdef;
                }
                if (isNotdef is null || !ShownCodes(show.Text ?? string.Empty, twoBytes).Any(isNotdef)) continue;
                reported.Add(font);
                options.ConversionLog.Add(new Optimization.PdfAViolation
                {
                    Rule = "FontNotdefGlyph",
                    Clause = "7.21.8",
                    Section = "Fonts",
                    PageNumber = page.Number,
                    Description = $"Text in font '{font.GetName("BaseFont") ?? "(unnamed)"}' shows glyphs its program does not define (.notdef); "
                        + "supplying them would change what the page shows, so the author has to fix the font",
                    Convertable = false,
                });
            }
        }
        ReportUnrepairedWidthsForUa(options, usedCodes);
    }

    /// <summary>
    /// PDF/UA-1 clause 7.21.5, for the programs the conversion cannot rewrite (Type 1 and CFF
    /// charstrings carry their advances inside encrypted glyph programs): a font whose stated
    /// /Widths differ from its program's advances is logged, once, as not convertible.
    /// </summary>
    private void ReportUnrepairedWidthsForUa(PdfFormatConversionOptions options,
        Dictionary<PdfDictionary, HashSet<int>> usedCodes)
    {
        foreach (var (_, font, pageNumber) in DocumentFonts())
        {
            if (font.GetName("Subtype") is not ("Type1" or "MMType1") || !usedCodes.TryGetValue(font, out var used)) continue;
            try
            {
                var descriptor = _reader.ResolveDict(font.Get("FontDescriptor"));
                Text.IGlyphOutlineSource? source = null;
                Dictionary<string, int>? names = null;
                if (ProgramStream(descriptor?.Get("FontFile3")) is { } cff && Text.CffGlyphSource.TryLoad(_reader.DecodeStream(cff)) is { } c)
                    (source, names) = (c, c.NameToGid);
                else if (ProgramStream(descriptor?.Get("FontFile")) is { } type1)
                {
                    var length1 = (int)((_reader.Resolve(type1.Dict.Get("Length1")) as PdfInteger)?.Value ?? 0);
                    var length2 = (int)((_reader.Resolve(type1.Dict.Get("Length2")) as PdfInteger)?.Value ?? 0);
                    if (Text.Type1GlyphSource.TryLoad(_reader.DecodeStream(type1), length1, length2) is { } t)
                        (source, names) = (t, t.NameToGid);
                }
                if (source is null || names is null || _reader.Resolve(font.Get("Widths")) is not PdfArray widths) continue;
                var first = (int)((_reader.Resolve(font.Get("FirstChar")) as PdfInteger)?.Value ?? 0);
                var codeNames = EncodingGlyphNames(font);
                var disagrees = false;
                for (var i = 0; i < widths.Count && !disagrees; i++)
                {
                    var code = first + i;
                    if (code is < 0 or > 255 || !used.Contains(code) || StatedWidth(widths[i]) is not { } width
                        || codeNames[code] is not { } name || !names.TryGetValue(name, out var gid)) continue;
                    var advance = source.GetAdvanceWidth(gid) * 1000.0 / source.UnitsPerEm;
                    disagrees = Math.Abs(advance - width) > 1;
                }
                if (!disagrees) continue;
                options.ConversionLog.Add(new Optimization.PdfAViolation
                {
                    Rule = "FontWidths",
                    Clause = "7.21.5",
                    Section = "Fonts",
                    PageNumber = pageNumber,
                    Description = $"The widths font '{font.GetName("BaseFont") ?? "(unnamed)"}' states differ from its program's "
                        + "advances, and its Type 1 program cannot be rewritten",
                    Convertable = false,
                });
            }
            catch { /* a program this library cannot read is not judged */ }
        }
    }

    // Form XObjects nest; deeper than this is a cycle or a pathology.
    private const int MaxFormDepth = 16;

    /// <summary>Every string the page shows, with its font: in the page's content, in the
    /// form XObjects it draws (however deep), and in its annotations' appearances. Read from
    /// the content bytes, which leaves the page's operators as they are (loading them would
    /// put them back on save).</summary>
    private List<(PdfDictionary Font, string Raw)> ShownStrings(Page page)
    {
        var shows = new List<(PdfDictionary Font, string Raw)>();
        var seen = new HashSet<PdfStream>(ReferenceEqualityComparer.Instance);

        void Scan(byte[] bytes, PdfDictionary? resources, int depth)
        {
            if (depth > MaxFormDepth) return;
            var fonts = _reader.ResolveDict(resources?.Get("Font"));
            var xobjects = _reader.ResolveDict(resources?.Get("XObject"));
            var forms = new List<PdfStream>();
            try
            {
                var parser = new Content.ContentStreamParser(_reader);
                parser.OnTextShown += (_, raw, state) =>
                {
                    if (state.FontName is { } name && _reader.ResolveDict(fonts?.Get(name)) is { } f)
                        shows.Add((f, Compat.Latin1.GetString(raw)));
                };
                parser.OnImageDrawn += (name, _) =>
                {
                    if (_reader.ResolveStream(xobjects?.Get(name)) is { } form && form.Dict.GetName("Subtype") == "Form"
                        && seen.Add(form)) forms.Add(form);
                };
                parser.Parse(bytes);
            }
            catch { return; }
            foreach (var form in forms)
            {
                byte[] data;
                try { data = _reader.DecodeStream(form); }
                catch { continue; }
                Scan(data, _reader.ResolveDict(form.Dict.Get("Resources")) ?? resources, depth + 1);
            }
        }

        Scan(page.GetContentStreamBytes() ?? [], ResolveInheritedResources(page.Dict), 0);
        if (_reader.Resolve(page.Dict.Get("Annots")) is PdfArray annots)
            foreach (var entry in annots)
            {
                if (_reader.ResolveDict(_reader.ResolveDict(entry)?.Get("AP")) is not { } ap) continue;
                var normal = _reader.Resolve(ap.Get("N"));
                var streams = normal is PdfStream single ? [single]
                    : normal is PdfDictionary states ? states.Keys.Select(k => _reader.ResolveStream(states.Get(k))).OfType<PdfStream>().ToList()
                    : new List<PdfStream>();
                foreach (var stream in streams)
                {
                    if (!seen.Add(stream)) continue;
                    try { Scan(_reader.DecodeStream(stream), _reader.ResolveDict(stream.Dict.Get("Resources")), 1); }
                    catch { /* an unreadable appearance says nothing */ }
                }
            }
        return shows;
    }

    /// <summary>The character codes of a shown string: bytes, or byte pairs for a composite font.</summary>
    private static IEnumerable<int> ShownCodes(string raw, bool twoBytes)
    {
        if (!twoBytes)
            foreach (var c in raw) yield return c & 0xFF;
        else
            for (var i = 0; i + 1 < raw.Length; i += 2) yield return ((raw[i] & 0xFF) << 8) | (raw[i + 1] & 0xFF);
    }

    /// <summary>A test telling which of the font's codes map to no character: none in its
    /// /ToUnicode, and (a simple font) no standard glyph name in its encoding. Null for fonts
    /// whose codes a predefined CMap or a Type 3 procedure accounts for otherwise.</summary>
    private Func<int, bool>? UnicodeMissingTest(PdfDictionary font)
    {
        var subtype = font.GetName("Subtype");
        var map = ReadToUnicode(font);
        if (subtype == "Type0")
            return font.GetName("Encoding") is "Identity-H" or "Identity-V" ? code => !map.ContainsKey(code) : null;
        if (subtype is not ("TrueType" or "Type1" or "MMType1" or "Type3")) return null;
        // A symbolic TrueType font with no /Encoding reads its codes through the program's
        // symbol cmap: no glyph name says which character a code is.
        if (subtype == "TrueType" && font.Get("Encoding") is null && IsSymbolicSimpleFont(font))
            return code => !map.ContainsKey(code);
        var names = EncodingGlyphNames(font);
        return code => !map.ContainsKey(code)
            && (code is < 0 or > 255 || names[code] is not { } name || !Text.TextAbsorber.GlyphNameToUnicode.ContainsKey(name));
    }

    /// <summary>A test telling which of the font's codes show .notdef; null when the font is
    /// of a kind this does not read (then nothing is reported for it).</summary>
    private Func<int, bool>? NotdefTest(PdfDictionary font)
    {
        switch (font.GetName("Subtype"))
        {
            case "Type0":
            {
                if (font.GetName("Encoding") is not ("Identity-H" or "Identity-V")
                    || _reader.Resolve(font.Get("DescendantFonts")) is not PdfArray { Count: > 0 } d
                    || _reader.ResolveDict(d[0]) is not { } cidFont) return null;
                var descriptor = _reader.ResolveDict(cidFont.Get("FontDescriptor"));
                if (SfntProgram(descriptor) is { } sfnt)
                {
                    var glyphCount = Optimization.PdfAValidator.EmbeddedGlyphCount(_reader.DecodeStream(sfnt));
                    if (glyphCount <= 0) return null;
                    byte[]? map = _reader.Resolve(cidFont.Get("CIDToGIDMap")) is PdfStream m ? _reader.DecodeStream(m) : null;
                    return cid =>
                    {
                        var gid = map is null ? cid : cid * 2 + 1 < map.Length ? (map[cid * 2] << 8) | map[cid * 2 + 1] : 0;
                        return gid == 0 || gid >= glyphCount;
                    };
                }
                if (ProgramStream(descriptor?.Get("FontFile3")) is { } cff
                    && Text.CffGlyphSource.TryLoad(_reader.DecodeStream(cff)) is { IsCidKeyed: true } source)
                    return cid => source.CidToGid(cid) == 0; // CID 0 is .notdef itself
                return null;
            }
            case "Type1" or "MMType1":
            {
                var descriptor = _reader.ResolveDict(font.Get("FontDescriptor"));
                Dictionary<string, int>? names = null;
                if (ProgramStream(descriptor?.Get("FontFile3")) is { } cff)
                    names = Text.CffGlyphSource.TryLoad(_reader.DecodeStream(cff))?.NameToGid;
                else if (ProgramStream(descriptor?.Get("FontFile")) is { } type1)
                {
                    var length1 = (int)((_reader.Resolve(type1.Dict.Get("Length1")) as PdfInteger)?.Value ?? 0);
                    var length2 = (int)((_reader.Resolve(type1.Dict.Get("Length2")) as PdfInteger)?.Value ?? 0);
                    names = Text.Type1GlyphSource.TryLoad(_reader.DecodeStream(type1), length1, length2)?.NameToGid;
                }
                if (names is null || names.Count == 0 || font.Get("Encoding") is null) return null;
                var codeNames = EncodingGlyphNames(font);
                // A code the encoding names no glyph for shows .notdef as surely as one whose
                // glyph the program lacks.
                return code => code is >= 0 and < 256
                    && (codeNames[code] is not { } name || name == ".notdef" || !names.ContainsKey(name));
            }
            case "TrueType":
            {
                var descriptor = _reader.ResolveDict(font.Get("FontDescriptor"));
                if (SfntProgram(descriptor) is not { } stream) return null;
                var program = _reader.DecodeStream(stream);
                var subtables = Text.TrueTypeAdvances.CmapSubtables(program);
                var parser = new Text.TrueTypeParser(program);
                if (IsSymbolicSimpleFont(font))
                {
                    // Codes read through the symbol cmap: (3,0) keys them at 0xF000 + code (or
                    // at the code), (1,0) at the code.
                    if (subtables.Contains((3, 0))) parser.PreferSubtable = (3, 0);
                    else if (subtables.Contains((1, 0))) parser.PreferSubtable = (1, 0);
                    else return null;
                    parser.Parse();
                    return code => !(parser.CMap.TryGetValue(0xF000 + code, out var g) && g > 0)
                        && !(parser.CMap.TryGetValue(code, out var g2) && g2 > 0);
                }
                if (!subtables.Contains((3, 1))) return null;
                parser.PreferSubtable = (3, 1);
                parser.Parse();
                var codeNames = EncodingGlyphNames(font);
                return code => code is >= 0 and < 256
                    && (codeNames[code] is not { } name
                        || !Text.TextAbsorber.GlyphNameToUnicode.TryGetValue(name, out var text) || text.Length == 0
                        || !(parser.CMap.TryGetValue(char.ConvertToUtf32(text, 0), out var gid) && gid > 0));
            }
            default:
                return null;
        }
    }

    private bool IsSymbolicSimpleFont(PdfDictionary font) =>
        (((_reader.Resolve(_reader.ResolveDict(font.Get("FontDescriptor"))?.Get("Flags")) as PdfInteger)?.Value ?? 0)
            & SymbolicFlag) != 0;
}
