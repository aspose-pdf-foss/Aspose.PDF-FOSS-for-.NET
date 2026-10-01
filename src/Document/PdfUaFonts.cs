using System.Numerics;
using Aspose.Pdf.Core;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>
    /// PDF/UA-1 clause 7.21.4.2: a CIDFont that ships a /CIDSet must account for every glyph
    /// of its embedded program. A set that names fewer is rewritten to name them all - the
    /// program is kept as it is, so every glyph it defines is one the set has to admit. The
    /// log already names the defect; this is the repair a UA conversion makes for it.
    /// </summary>
    private void RepairCidSets()
    {
        var visited = new HashSet<PdfDictionary>();
        foreach (var page in Pages)
            foreach (var (_, font) in PageFonts(page, visited))
            {
                if (font.GetName("Subtype") != "Type0") continue;
                if (_reader.Resolve(font.Get("DescendantFonts")) is not PdfArray { Count: > 0 } descendants)
                    continue;
                var cidFont = _reader.ResolveDict(descendants[0]);
                var descriptor = cidFont is null ? null : _reader.ResolveDict(cidFont.Get("FontDescriptor"));
                if (descriptor is null || _reader.ResolveStream(descriptor.Get("CIDSet")) is not { } cidSet)
                    continue;
                // A CFF program numbers its glyphs by CIDs of its own charset, not by glyph
                // index: the set cannot be restated from a glyph count. It is optional, so a
                // set that cannot be checked is dropped rather than left to misstate the font.
                if (descriptor.Get("FontFile2") is null)
                {
                    descriptor.Remove("CIDSet");
                    continue;
                }
                var program = _reader.ResolveStream(descriptor.Get("FontFile2"));
                if (program is null) continue;

                int glyphs;
                byte[] marked;
                try
                {
                    glyphs = Optimization.PdfAValidator.EmbeddedGlyphCount(_reader.DecodeStream(program));
                    marked = _reader.DecodeStream(cidSet);
                }
                catch { continue; }
                if (glyphs <= 0) continue;

                var named = 0;
                foreach (var b in marked) named += Compat.PopCount(b);
                if (named == glyphs) continue;

                var all = new byte[(glyphs + 7) / 8];
                for (var glyph = 0; glyph < glyphs; glyph++)
                    all[glyph / 8] |= (byte)(0x80 >> (glyph % 8));
                descriptor.Set("CIDSet", new PdfStream(new PdfDictionary(), all));
            }
    }

    /// <summary>
    /// PDF/UA-1 clause 7.21.4.2: a Type 1 font's /CharSet, when present, lists every glyph its
    /// program defines. The list is optional and a subsetter often leaves it naming the
    /// original face's glyphs; it is dropped rather than left to misstate the font.
    /// </summary>
    private void DropType1CharSets()
    {
        foreach (var (_, font, _) in DocumentFonts())
        {
            if (font.GetName("Subtype") is not ("Type1" or "MMType1")) continue;
            var descriptor = _reader.ResolveDict(font.Get("FontDescriptor"));
            if (descriptor?.Get("CharSet") is not null) descriptor.Remove("CharSet");
        }
    }

    /// <summary>
    /// PDF/UA-1 clause 7.21.6: a symbolic TrueType font carries no /Encoding. A font whose
    /// program maps only symbol codes (a (3,0) cmap) reads its codes through that cmap, so the
    /// /Encoding it should not carry is dropped.
    /// </summary>
    private void RepairSymbolicTrueTypeEncodings(PdfFormatConversionOptions options)
    {
        foreach (var (_, font, _) in DocumentFonts())
        {
            if (font.GetName("Subtype") != "TrueType" || font.Get("Encoding") is null) continue;
            var descriptor = _reader.ResolveDict(font.Get("FontDescriptor"));
            if (_reader.Resolve(descriptor?.Get("Flags")) is not PdfInteger flags || (flags.Value & SymbolicFlag) == 0)
                continue;
            if (SfntProgram(descriptor) is not { } stream) continue;
            HashSet<(int Platform, int Encoding)> subtables;
            try { subtables = Text.TrueTypeAdvances.CmapSubtables(_reader.DecodeStream(stream)); }
            catch { continue; }

            // Only a program that maps symbol codes alone is read the same without the /Encoding;
            // one that also maps Unicode may be read through the encoding's glyph names, and
            // is left as it is - and logged, since the file keeps what the standard forbids.
            if (!subtables.Contains((3, 0)) || subtables.Contains((3, 1)))
            {
                options.ConversionLog.Add(new Optimization.PdfAViolation
                {
                    Rule = "SymbolicEncoding",
                    Clause = "7.21.6",
                    Section = "Fonts",
                    Description = $"Symbolic TrueType font '{font.GetName("BaseFont") ?? "(unnamed)"}' carries an /Encoding "
                        + "that cannot be dropped without changing how its codes are read",
                    Convertable = false,
                });
                continue;
            }
            // The encoding's glyph names are what said which character each code shows; a
            // font with no /ToUnicode keeps saying it there.
            if (font.Get("ToUnicode") is null)
            {
                var names = EncodingGlyphNames(font);
                var unicodeOf = new Dictionary<int, string>();
                for (var code = 0; code < 256; code++)
                    if (names[code] is { } name && Text.TextAbsorber.GlyphNameToUnicode.TryGetValue(name, out var text)
                        && text.Length > 0)
                        unicodeOf[code] = text;
                if (unicodeOf.Count > 0) WriteToUnicode(font, unicodeOf, 1);
            }
            font.Remove("Encoding");
        }
    }

    /// <summary>
    /// PDF/UA-1 clause 7.21.5: an embedded font program's advances agree with the widths its
    /// dictionary states. The page is laid out against the dictionary's widths (every reader
    /// positions glyphs by them), so where an embedded TrueType program says otherwise its
    /// advances are set to the stated widths - nothing drawn moves.
    /// </summary>
    private void RepairEmbeddedTrueTypeWidths()
    {
        foreach (var (_, font, _) in DocumentFonts())
        {
            try
            {
                if (font.GetName("Subtype") == "TrueType") RepairSimpleTrueTypeWidths(font);
                else if (font.GetName("Subtype") == "Type0") RepairCidTrueTypeWidths(font);
            }
            catch { /* an unreadable program is left as it is */ }
        }
    }

    private void RepairSimpleTrueTypeWidths(PdfDictionary font)
    {
        var descriptor = _reader.ResolveDict(font.Get("FontDescriptor"));
        if (SfntProgram(descriptor) is not { } stream
            || _reader.Resolve(font.Get("Widths")) is not PdfArray widths) return;
        var program = _reader.DecodeStream(stream);
        var parser = new Text.TrueTypeParser(program);
        var symbolic = ((_reader.Resolve(descriptor!.Get("Flags")) as PdfInteger)?.Value & SymbolicFlag) != 0;
        // A symbolic font's codes are read through its symbol cmap, even beside a Unicode one.
        if (symbolic)
        {
            var subtables = Text.TrueTypeAdvances.CmapSubtables(program);
            if (subtables.Contains((3, 0))) parser.PreferSubtable = (3, 0);
            else if (subtables.Contains((1, 0))) parser.PreferSubtable = (1, 0);
        }
        parser.Parse();
        var first = (int)((_reader.Resolve(font.Get("FirstChar")) as PdfInteger)?.Value ?? 0);
        var names = symbolic ? null : EncodingGlyphNames(font);

        var advances = new Dictionary<int, int>();
        for (var i = 0; i < widths.Count; i++)
        {
            var code = first + i;
            if (StatedWidth(widths[i]) is not { } width) continue;
            int gid;
            if (symbolic)
            {
                if (!parser.CMap.TryGetValue(0xF000 + code, out gid) && !parser.CMap.TryGetValue(code, out gid)) continue;
            }
            else if (names![code] is not { } name
                     || !Text.TextAbsorber.GlyphNameToUnicode.TryGetValue(name, out var text) || text.Length == 0
                     || !parser.CMap.TryGetValue(char.ConvertToUtf32(text, 0), out gid)) continue;
            if (gid > 0 && !advances.ContainsKey(gid))
                advances[gid] = (int)Math.Round(width * parser.UnitsPerEm / 1000.0);
        }
        ReplaceProgram(stream, program, advances);
    }

    private void RepairCidTrueTypeWidths(PdfDictionary font)
    {
        if (_reader.Resolve(font.Get("DescendantFonts")) is not PdfArray { Count: > 0 } descendants
            || _reader.ResolveDict(descendants[0]) is not { } cidFont
            || SfntProgram(_reader.ResolveDict(cidFont.Get("FontDescriptor"))) is not { } stream)
            return;
        // CID -> glyph: the /CIDToGIDMap stream, or identity.
        byte[]? map = null;
        var mapObject = _reader.Resolve(cidFont.Get("CIDToGIDMap"));
        if (mapObject is PdfStream mapStream) map = _reader.DecodeStream(mapStream);
        else if (mapObject is not (null or PdfName { Value: "Identity" })) return;

        var program = _reader.DecodeStream(stream);
        var glyphCount = Optimization.PdfAValidator.EmbeddedGlyphCount(program);
        var parser = new Text.TrueTypeParser(program);
        parser.Parse();
        var stated = Text.TrueTypeAdvances.CidWidths(_reader.Resolve(cidFont.Get("W")) as PdfArray, _reader);
        // A CID /W does not list takes /DW (1000 when absent, PDF 32000-1 §9.7.4.3).
        var defaultWidth = StatedWidth(cidFont.Get("DW")) ?? 1000;
        var cidCount = map is not null ? map.Length / 2 : glyphCount;
        int GlyphOf(int cid) => map is not null ? cid * 2 + 1 < map.Length ? (map[cid * 2] << 8) | map[cid * 2 + 1] : 0 : cid;
        var advances = new Dictionary<int, int>();
        // The CIDs /W names come first: two CIDs may share a glyph (a symbol font keys it at
        // both 0x3F and 0xF03F), and the one the file states a width for is the one it shows.
        foreach (var (cid, width) in stated)
        {
            var gid = GlyphOf(cid);
            if (gid > 0 && gid < glyphCount && !advances.ContainsKey(gid))
                advances[gid] = (int)Math.Round(width * parser.UnitsPerEm / 1000.0);
        }
        for (var cid = 0; cid < cidCount; cid++)
        {
            var gid = GlyphOf(cid);
            if (gid <= 0 || gid >= glyphCount || advances.ContainsKey(gid)) continue;
            advances[gid] = (int)Math.Round(defaultWidth * parser.UnitsPerEm / 1000.0);
        }
        ReplaceProgram(stream, program, advances);
    }

    /// <summary>The descriptor's sfnt program: /FontFile2, or an OpenType /FontFile3 (whose
    /// advances live in the same hmtx table).</summary>
    private PdfStream? SfntProgram(PdfDictionary? descriptor) =>
        ProgramStream(descriptor?.Get("FontFile2"))
        ?? (ProgramStream(descriptor?.Get("FontFile3")) is { } ff3 && ff3.Dict.GetName("Subtype") == "OpenType"
            ? ff3 : null);

    /// <summary>A font program stream, including one this conversion has just embedded and
    /// not yet written.</summary>
    private PdfStream? ProgramStream(PdfObject? reference) =>
        _reader.ResolveStream(reference)
        ?? (reference is PdfIndirectRef r ? ResolvePendingStream(r.ObjectNumber) : null);

    private double? StatedWidth(PdfObject? value) => _reader.Resolve(value) switch
    {
        PdfInteger i when i.Value > 0 => i.Value,
        PdfReal r when r.Value > 0 => r.Value,
        _ => null,
    };

    /// <summary>The glyph name each code of a simple font names through its /Encoding (a
    /// base encoding, WinAnsi when none, under its /Differences).</summary>
    private string?[] EncodingGlyphNames(PdfDictionary font)
    {
        var names = new string?[256];
        var encoding = _reader.Resolve(font.Get("Encoding"));
        var baseName = encoding is PdfName n ? n.Value
            : (encoding as PdfDictionary)?.GetName("BaseEncoding") ?? "WinAnsiEncoding";
        for (var code = 0; code < 256; code++)
            names[code] = baseName switch
            {
                "MacRomanEncoding" => Text.PdfEncodings.MacRomanName(code),
                "StandardEncoding" => Text.Type1StandardEncoding.GetName(code),
                _ => Text.PdfEncodings.WinAnsiName(code),
            };
        if (encoding is PdfDictionary dict && _reader.Resolve(dict.Get("Differences")) is PdfArray differences)
        {
            var code = 0;
            foreach (var item in differences)
                switch (_reader.Resolve(item))
                {
                    case PdfInteger start: code = (int)start.Value; break;
                    case PdfName glyph when code is >= 0 and < 256: names[code++] = glyph.Value; break;
                }
        }
        return names;
    }

    /// <summary>Store the program with the advances set, when any of them changes it.</summary>
    private static void ReplaceProgram(PdfStream stream, byte[] program, Dictionary<int, int> advances)
    {
        var patched = Text.TrueTypeAdvances.Patch(program, advances);
        if (ReferenceEquals(patched, program) || patched.AsSpan().SequenceEqual(program)) return;
        stream.Dict.Remove("Filter");
        stream.Dict.Remove("DecodeParms");
        if (stream.Dict.Get("Length1") is not null) stream.Dict.Set("Length1", new PdfInteger(patched.Length));
        stream.ReplaceData(patched);
    }

    // The character collection each predefined CJK CMap addresses: ordering and the supplement
    // its CIDs come from (ISO 32000-1 §9.7.5.2, Adobe's CMap resources).
    private static readonly Dictionary<string, (string Ordering, int Supplement)> PredefinedCMapCollections =
        new(StringComparer.Ordinal)
        {
            ["KSC-EUC-H"] = ("Korea1", 0), ["KSC-EUC-V"] = ("Korea1", 0), ["KSCpc-EUC-H"] = ("Korea1", 0),
            ["KSCms-UHC-H"] = ("Korea1", 1), ["KSCms-UHC-V"] = ("Korea1", 1),
            ["KSCms-UHC-HW-H"] = ("Korea1", 1), ["KSCms-UHC-HW-V"] = ("Korea1", 1),
            ["UniKS-UCS2-H"] = ("Korea1", 1), ["UniKS-UCS2-V"] = ("Korea1", 1),
            ["UniKS-UTF16-H"] = ("Korea1", 2), ["UniKS-UTF16-V"] = ("Korea1", 2),
            ["GB-EUC-H"] = ("GB1", 0), ["GB-EUC-V"] = ("GB1", 0), ["GBpc-EUC-H"] = ("GB1", 0), ["GBpc-EUC-V"] = ("GB1", 0),
            ["GBK-EUC-H"] = ("GB1", 2), ["GBK-EUC-V"] = ("GB1", 2),
            ["UniGB-UCS2-H"] = ("GB1", 4), ["UniGB-UCS2-V"] = ("GB1", 4),
            ["UniGB-UTF16-H"] = ("GB1", 5), ["UniGB-UTF16-V"] = ("GB1", 5),
            ["B5pc-H"] = ("CNS1", 0), ["B5pc-V"] = ("CNS1", 0), ["ETen-B5-H"] = ("CNS1", 0), ["ETen-B5-V"] = ("CNS1", 0),
            ["ETenms-B5-H"] = ("CNS1", 0), ["ETenms-B5-V"] = ("CNS1", 0), ["CNS-EUC-H"] = ("CNS1", 0), ["CNS-EUC-V"] = ("CNS1", 0),
            ["HKscs-B5-H"] = ("CNS1", 3), ["HKscs-B5-V"] = ("CNS1", 3),
            ["UniCNS-UCS2-H"] = ("CNS1", 3), ["UniCNS-UCS2-V"] = ("CNS1", 3),
            ["UniCNS-UTF16-H"] = ("CNS1", 4), ["UniCNS-UTF16-V"] = ("CNS1", 4),
            ["83pv-RKSJ-H"] = ("Japan1", 1), ["90ms-RKSJ-H"] = ("Japan1", 2), ["90ms-RKSJ-V"] = ("Japan1", 2),
            ["90msp-RKSJ-H"] = ("Japan1", 2), ["90msp-RKSJ-V"] = ("Japan1", 2),
            ["EUC-H"] = ("Japan1", 1), ["EUC-V"] = ("Japan1", 1), ["H"] = ("Japan1", 1), ["V"] = ("Japan1", 1),
            ["UniJIS-UCS2-H"] = ("Japan1", 4), ["UniJIS-UCS2-V"] = ("Japan1", 4),
            ["UniJIS-UTF16-H"] = ("Japan1", 5), ["UniJIS-UTF16-V"] = ("Japan1", 5),
        };

    /// <summary>
    /// PDF/UA-1 clause 7.21.3.1: a composite font over a predefined CMap names, in its
    /// CIDFont's /CIDSystemInfo, the CMap's registry and ordering, and a supplement no later
    /// than the CMap's. A CIDFont that names the right collection with a later supplement is
    /// restated at the CMap's (the CMap's codes reach no CID past it, so nothing drawn changes);
    /// one that names another collection is logged, as not convertible.
    /// </summary>
    private void RepairCidSystemInfo(PdfFormatConversionOptions options)
    {
        foreach (var (_, font, pageNumber) in DocumentFonts())
        {
            if (font.GetName("Subtype") != "Type0" || font.GetName("Encoding") is not { } cmap
                || !PredefinedCMapCollections.TryGetValue(cmap, out var collection)
                || _reader.Resolve(font.Get("DescendantFonts")) is not PdfArray { Count: > 0 } d
                || _reader.ResolveDict(d[0]) is not { } cidFont
                || _reader.ResolveDict(cidFont.Get("CIDSystemInfo")) is not { } info) continue;
            var registry = (_reader.Resolve(info.Get("Registry")) as PdfString)?.ToText();
            var ordering = (_reader.Resolve(info.Get("Ordering")) as PdfString)?.ToText();
            var supplement = (_reader.Resolve(info.Get("Supplement")) as PdfInteger)?.Value ?? 0;
            if (registry == "Adobe" && ordering == collection.Ordering)
            {
                if (supplement > collection.Supplement) info.Set("Supplement", new PdfInteger(collection.Supplement));
                continue;
            }
            options.ConversionLog.Add(new Optimization.PdfAViolation
            {
                Rule = "CidSystemInfo",
                Clause = "7.21.3.1",
                Section = "Fonts",
                PageNumber = pageNumber,
                Description = $"Font '{font.GetName("BaseFont") ?? "(unnamed)"}' names the character collection "
                    + $"{registry}-{ordering} over the CMap {cmap} (Adobe-{collection.Ordering})",
                Convertable = false,
            });
        }
    }

    // FontDescriptor /Flags bit 3: the font uses a symbol set outside the standard Latin one.
    private const int SymbolicFlag = 1 << 2;

    /// <summary>
    /// PDF/UA-1 clause 7.21.6: a non-symbolic TrueType font states WinAnsiEncoding or
    /// MacRomanEncoding, as its /Encoding or as the /BaseEncoding under its /Differences. A
    /// font that states another base (or, with /Differences alone, the implied
    /// StandardEncoding) is restated over WinAnsiEncoding with every code the two bases name
    /// differently spelled out in /Differences - each code keeps the glyph it had.
    /// </summary>
    private void RepairTrueTypeEncodings()
    {
        foreach (var (_, font, _) in DocumentFonts())
        {
            if (font.GetName("Subtype") != "TrueType") continue;
            var flags = (_reader.Resolve(_reader.ResolveDict(font.Get("FontDescriptor"))?.Get("Flags")) as PdfInteger)?.Value ?? 0;
            if ((flags & SymbolicFlag) != 0) continue;

            var encoding = _reader.Resolve(font.Get("Encoding"));
            var dict = encoding as PdfDictionary;
            var baseName = encoding is PdfName n ? n.Value : dict?.GetName("BaseEncoding") ?? "StandardEncoding";
            if (encoding is null || baseName is "WinAnsiEncoding" or "MacRomanEncoding") continue;
            if (baseName != "StandardEncoding") continue; // no table to restate another base from

            var names = new SortedDictionary<int, string>();
            if (dict is not null && _reader.Resolve(dict.Get("Differences")) is PdfArray differences)
            {
                var code = 0;
                foreach (var item in differences)
                    switch (_reader.Resolve(item))
                    {
                        case PdfInteger start: code = (int)start.Value; break;
                        case PdfName glyph: names[code++] = glyph.Value; break;
                    }
            }
            for (var code = 0; code < 256; code++)
            {
                if (names.ContainsKey(code)) continue;
                var standard = Text.Type1StandardEncoding.GetName(code);
                if (standard is not null && standard != Text.PdfEncodings.WinAnsiName(code))
                    names[code] = standard;
            }

            var restated = new PdfArray();
            var next = -1;
            foreach (var (code, glyph) in names)
            {
                if (code != next) restated.Add(new PdfInteger(code));
                restated.Add(new PdfName(glyph));
                next = code + 1;
            }
            dict ??= new PdfDictionary();
            dict.Set("Type", new PdfName("Encoding"));
            dict.Set("BaseEncoding", new PdfName("WinAnsiEncoding"));
            dict.Set("Differences", restated);
            if (encoding is PdfName) font.Set("Encoding", dict);
        }
    }

    /// <summary>
    /// PDF/UA-1 clause 7.21.3.2: an embedded TrueType-based CIDFont names its CID-to-glyph
    /// mapping. One that names none maps by identity (the PDF default), which is written out.
    /// </summary>
    private void RepairCidToGidMaps()
    {
        foreach (var (_, font, _) in DocumentFonts())
        {
            if (font.GetName("Subtype") != "Type0"
                || _reader.Resolve(font.Get("DescendantFonts")) is not PdfArray { Count: > 0 } descendants
                || _reader.ResolveDict(descendants[0]) is not { } cidFont
                || cidFont.GetName("Subtype") != "CIDFontType2"
                || cidFont.Get("CIDToGIDMap") is not null)
                continue;
            if (_reader.ResolveDict(cidFont.Get("FontDescriptor"))?.Get("FontFile2") is null) continue;
            cidFont.Set("CIDToGIDMap", new PdfName("Identity"));
        }
    }
}
