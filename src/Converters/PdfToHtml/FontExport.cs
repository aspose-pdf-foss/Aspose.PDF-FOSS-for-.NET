using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Converters;

public sealed partial class PdfToHtmlConverter
{
    /// <summary>Append the font sidecar files for the saved pages in the formats
    /// <paramref name="fontMode"/> selects (one GUID-named file per format per font)
    /// and return the collected fonts. Each file is first offered to the caller's
    /// <see cref="HtmlSaveOptions.CustomResourceSavingStrategy"/>; a returned URL
    /// replaces the sidecar (recorded in <see cref="EmbeddedFont.Hrefs"/> for the
    /// @font-face src). <c>DontSave</c> emits nothing.</summary>
    private static List<EmbeddedFont> EmitFontSidecars(Document doc, int[] pageList,
        List<SidecarFile> sidecars, HtmlSaveOptions.FontSavingModes fontMode,
        HtmlSaveOptions? options)
    {
        if (fontMode == HtmlSaveOptions.FontSavingModes.DontSave) return new List<EmbeddedFont>();
        var fonts = CollectEmbeddedFonts(doc, pageList, options);
        foreach (var font in fonts)
        {
            var woff = fontMode is HtmlSaveOptions.FontSavingModes.AlwaysSaveAsWOFF
                or HtmlSaveOptions.FontSavingModes.SaveInAllFormats;
            var ttf = fontMode is HtmlSaveOptions.FontSavingModes.AlwaysSaveAsTTF
                or HtmlSaveOptions.FontSavingModes.SaveInAllFormats;
            var eot = fontMode is HtmlSaveOptions.FontSavingModes.AlwaysSaveAsEOT
                or HtmlSaveOptions.FontSavingModes.SaveInAllFormats;
            if (woff) EmitFontFile(sidecars, options, font, ".woff", font.Woff);
            if (ttf) EmitFontFile(sidecars, options, font, ".ttf", font.Ttf);
            if (eot) EmitFontFile(sidecars, options, font, ".eot", Text.EotWriter.Wrap(font.Ttf, font.Family));
        }
        return fonts;
    }

    /// <summary>Offer one font file to the resource strategy, falling back to a
    /// sidecar file when there is no strategy or it cancelled.</summary>
    private static void EmitFontFile(List<SidecarFile> sidecars, HtmlSaveOptions? options,
        EmbeddedFont font, string ext, byte[] bytes)
    {
        var name = font.BaseName + ext;
        if (options?.CustomResourceSavingStrategy is { } strategy)
        {
            var info = new SaveOptions.ResourceSavingInfo
            {
                ResourceType = SaveOptions.NodeLevelResourceType.Font,
                SupposedFileName = name,
                ContentStream = new System.IO.MemoryStream(bytes),
                ContentStreamData = bytes,
            };
            string? url = null;
            try { url = strategy(info); }
            catch { /* a failing caller callback must not abort the save */ }
            if (url != null && url.IndexOfAny(ForbiddenResourcePathChars) >= 0)
                throw new System.ArgumentException(
                    "Custom resource saving method returned resource path that contains char(s) forbidden in that context (('\"' or ''' or '\n' or '\r')).");
            if (!info.CustomProcessingCancelled && !string.IsNullOrEmpty(url))
            {
                font.Hrefs[ext] = url;
                return;
            }
        }
        sidecars.Add(new SidecarFile { Name = name, Content = bytes });
    }

    /// <summary>One @font-face rule for <paramref name="font"/> in the given saving
    /// mode. <paramref name="fontUrlPrefix"/> rebases the src URLs when the CSS does
    /// not live next to the font files (e.g. embedded into the HTML).</summary>
    private static string FontFaceCss(EmbeddedFont font, string fontUrlPrefix,
        HtmlSaveOptions.FontSavingModes fontMode)
    {
        // Strategy-supplied URLs (absolute) win over the default prefix+name form.
        string U(string ext) => font.Hrefs.TryGetValue(ext, out var u)
            ? u : fontUrlPrefix + font.BaseName + ext;
        var src = fontMode switch
        {
            HtmlSaveOptions.FontSavingModes.AlwaysSaveAsTTF =>
                $"\tsrc:url(\"{U(".ttf")}\") format(\"truetype\");\n",
            HtmlSaveOptions.FontSavingModes.AlwaysSaveAsEOT =>
                $"\tsrc:url(\"{U(".eot")}\");\n",
             // the "bulletproof" shape: plain EOT for old IE, then the format list
            HtmlSaveOptions.FontSavingModes.SaveInAllFormats =>
                $"\tsrc:url(\"{U(".eot")}\");\n" +
                $"\tsrc:url(\"{U(".eot")}?#iefix\") format(\"embedded-opentype\"),\n" +
                $"\turl(\"{U(".woff")}\") format(\"woff\"),\n" +
                $"\turl(\"{U(".ttf")}\") format(\"truetype\");\n",
            _ => $"\tsrc:url(\"{U(".woff")}\") format(\"woff\");\n",
        };
        return $"@font-face {{\n\tfont-family:\"{font.Family}\";\n{src}}}\n";
    }

    /// <summary>An embedded TrueType font of the document, ready to emit as a sidecar.
    /// Sidecar files take GUID-shaped names (<see cref="BaseName"/> + a format
    /// extension); <see cref="Objects"/> lists the
    /// PDF font object(s) this file serves, for per-page CSS splitting.</summary>
    private sealed class EmbeddedFont
    {
        public string Family = "";
        public string BaseName = System.Guid.NewGuid().ToString();
        public byte[] Ttf = System.Array.Empty<byte>();
        public byte[] Woff = System.Array.Empty<byte>();
        public readonly List<PdfObject> Objects = new();
        public string? DedupKey;
        /// <summary>Per-format (".woff"/".ttf"/".eot") URLs supplied by the caller's
        /// resource strategy; a format with no entry uses the default sidecar name.</summary>
        public readonly Dictionary<string, string> Hrefs = new();
    }

    /// <summary>True when <see cref="HtmlSaveOptions.ExcludeFontNameList"/> names this
    /// font, so the save ships neither its program nor an <c>@font-face</c> for it and
    /// its text falls back to the configured default family. The list names the FACE,
    /// so a six-letter subset tag (<c>ABCDEF+ArialMT</c>) is matched with and without
    /// its prefix.</summary>
    private static bool IsFontExcluded(HtmlSaveOptions? options, string baseFont)
    {
        if (options?.ExcludeFontNameList is not { Length: > 0 } list) return false;
        var bare = baseFont;
        if (bare.Length > 7 && bare[6] == '+') bare = bare.Substring(7);
        foreach (var name in list)
        {
            if (string.IsNullOrEmpty(name)) continue;
            if (string.Equals(name, bare, System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, baseFont, System.StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static List<EmbeddedFont> CollectEmbeddedFonts(Document doc, int[]? pages = null,
        HtmlSaveOptions? options = null)
    {
        var result = new List<EmbeddedFont>();
        var seen = new System.Collections.Generic.HashSet<string>();
        var seenObjs = new System.Collections.Generic.HashSet<PdfObject>();

        int[] pageList;
        if (pages is { Length: > 0 })
        {
            pageList = pages;
        }
        else
        {
            pageList = new int[doc.PageCount];
            for (var k = 0; k < pageList.Length; k++) pageList[k] = k + 1;
        }

        // Pass 1: which font objects actually show visible glyphs on the saved pages.
        var used = new System.Collections.Generic.HashSet<PdfObject>();
        foreach (var i in pageList)
            ScanUsedFontObjectsOnPage(doc, i, used);

        // Pass 2: walk the resource dictionaries and emit the used fonts in
        // encounter order.
        foreach (var i in pageList)
        {
            var page = doc.Pages[i];
            var reader = page.Reader;
            if (reader is null) continue;
            var resources = reader.ResolveDict(page.Dict.Get("Resources"));
            CollectFontsFromResources(resources, reader, seen, seenObjs, used, result, new System.Collections.Generic.HashSet<PdfObject>(), options);
        }
        return result;
    }

    /// <summary>Add every font object showing a visible glyph on page
    /// <paramref name="pageNum"/> to <paramref name="used"/>.</summary>
    private static void ScanUsedFontObjectsOnPage(Document doc, int pageNum,
        System.Collections.Generic.HashSet<PdfObject> used)
    {
        var page = doc.Pages[pageNum];
        var reader = page.Reader;
        if (reader is null) return;
        var resources = reader.ResolveDict(page.Dict.Get("Resources"));
        foreach (var stream in GetContentStreams(page.Dict, reader))
            ScanVisiblyUsedFonts(stream, resources, reader, used, new System.Collections.Generic.HashSet<PdfObject>());
    }

    /// <summary>Walk a content stream marking every font object that a text-showing
    /// operator gives at least one visible (non-whitespace) glyph, recursing into
    /// Form XObjects drawn by <c>Do</c> (their text uses their own resources).</summary>
    private static void ScanVisiblyUsedFonts(byte[] streamBytes, PdfDictionary? resources,
        PdfReader reader, System.Collections.Generic.HashSet<PdfObject> used,
        System.Collections.Generic.HashSet<PdfObject> visitedForms)
    {
        if (resources is null) return;
        var fontDict = reader.ResolveDict(resources.Get("Font"));
        var xobjects = reader.ResolveDict(resources.Get("XObject"));

        static bool Visible(PdfString s)
        {
            foreach (var b in s.Value)
                if (b is not (0x00 or 0x09 or 0x0A or 0x0D or 0x20)) return true;
            return false;
        }

        var lexer = new PdfLexer(streamBytes);
        var operands = new List<PdfObject>();
        PdfDictionary? currentFont = null;
        while (true)
        {
            var token = lexer.NextToken();
            if (token.Kind == TokenKind.Eof) break;
            switch (token.Kind)
            {
                case TokenKind.Integer: operands.Add(new PdfInteger(token.IntValue)); break;
                case TokenKind.Real: operands.Add(new PdfReal(token.RealValue)); break;
                case TokenKind.LiteralString: operands.Add(new PdfString(token.BytesValue!)); break;
                case TokenKind.HexString: operands.Add(new PdfString(token.BytesValue!, isHex: true)); break;
                case TokenKind.Name: operands.Add(new PdfName(token.StringValue!)); break;
                case TokenKind.ArrayStart: operands.Add(ParseArray(lexer)); break;
                case TokenKind.Keyword:
                    switch (token.StringValue)
                    {
                        case "Tf":
                            currentFont = operands.Count >= 1 && operands[0] is PdfName fn && fontDict is not null
                                ? reader.ResolveDict(fontDict.Get(fn.Value)) : null;
                            break;
                        case "Tj" or "'" or "\"":
                            if (currentFont is not null)
                                foreach (var o in operands)
                                    if (o is PdfString ps && Visible(ps)) { used.Add(currentFont); break; }
                            break;
                        case "TJ":
                            if (currentFont is not null && operands.Count >= 1 && operands[^1] is PdfArray arr)
                                foreach (var item in arr)
                                    if (item is PdfString ts && Visible(ts)) { used.Add(currentFont); break; }
                            break;
                        case "Do":
                            if (operands.Count >= 1 && operands[0] is PdfName xn && xobjects is not null
                                && reader.ResolveStream(xobjects.Get(xn.Value)) is { } form
                                && form.Dict.GetName("Subtype") == "Form" && visitedForms.Add(form))
                            {
                                byte[]? body = null;
                                try { body = reader.DecodeStream(form); } catch { }
                                if (body is not null)
                                    ScanVisiblyUsedFonts(body, reader.ResolveDict(form.Dict.Get("Resources")),
                                        reader, used, visitedForms);
                            }
                            break;
                        case "BI":
                            SkipInlineImage(lexer);
                            break;
                    }
                    operands.Clear();
                    break;
                default:
                    operands.Clear();
                    break;
            }
            if (operands.Count > 16) operands.Clear(); // stray tokens between keywords
        }
    }

    /// <summary>Harvest fonts from a resource dictionary's /Font entries and,
    /// recursively, from the /Resources of any Form XObject it references (fonts used
    /// only inside a form live in the form's own resource dict, not the page's).
    /// Only fonts in <paramref name="used"/> (visibly shown somewhere) are emitted,
    /// each font OBJECT once. The <paramref name="visitedForms"/> set guards against
    /// resource-graph cycles.</summary>
    private static void CollectFontsFromResources(PdfDictionary? resources, PdfReader reader,
        System.Collections.Generic.HashSet<string> seen,
        System.Collections.Generic.HashSet<PdfObject> seenObjs,
        System.Collections.Generic.HashSet<PdfObject> used,
        List<EmbeddedFont> result,
        System.Collections.Generic.HashSet<PdfObject> visitedForms,
        HtmlSaveOptions? options = null)
    {
        if (resources is null) return;

        var fontDict = reader.ResolveDict(resources.Get("Font"));
        if (fontDict is not null)
        {
            foreach (var key in fontDict.Keys)
            {
                var font = reader.ResolveDict(fontDict.Get(key));
                var baseFont = font?.GetName("BaseFont");
                if (font is null || string.IsNullOrEmpty(baseFont)) continue;
                // An excluded face never claims a slot: the caller asked for its
                // program to stay out of the output entirely.
                if (IsFontExcluded(options, baseFont)) continue;
                if (!used.Contains(font) || !seenObjs.Add(font)) continue;
                var ttf = GetEmbeddedTtf(font, reader) ?? GetEmbeddedOpenType(font, reader);
                // A CID-keyed subset ships without a cmap (glyphs are addressed by GID;
                // the char mapping lives in /ToUnicode + /CIDToGIDMap), and a simple
                // TrueType subset commonly ships only a byte cmap over its re-encoded
                // content-stream codes (a (1,0) format-0 table — the CJK newspaper
                // workflow). Either face is useless to the HTML consumer — the spans
                // carry Unicode text — so synthesize a (3,1) cmap from the PDF's own
                // mapping before shipping.
                if (ttf is not null && font.GetName("Subtype") is "Type0" or "TrueType")
                    ttf = EnsureUnicodeCmap(ttf, font, reader) ?? ttf;
                if (ttf is null && GetEmbeddedBareCff(font, reader) is { } cff)
                {
                    // Bare CFF (Type1C): synthesize a TrueType sfnt so the glyphs
                    // survive into a WOFF like any other embedded program.
                    try { ttf = Text.CffToTrueType.Convert(cff); } catch { }
                }
                if (ttf is null && GetEmbeddedType1(font, reader) is { } t1)
                {
                    // Adobe Type 1 (FontFile): same treatment — without it the face
                    // never ships, and a round-trip through the HTML loses the
                    // weight, slant and ligature glyphs it carried. The dict's
                    // /Differences + /ToUnicode supplement the synthesized cmap so
                    // a glyph whose NAME has no codepoint (/equalx) is still
                    // reachable at the char the text decodes to.
                    try
                    {
                        ttf = Text.CffToTrueType.ConvertType1(t1.Data, t1.Length1, t1.Length2,
                            RawDifferencesNames(font, reader), SingleCharToUnicode(font, reader));
                    }
                    catch { }
                }
                string? dedupKey = null;
                string? substituteFamily = null;
                if (ttf is null)
                {
                    // Non-embedded font (no FontFile at all): resolve a system face for the
                    // BaseFont name and ship it like an embedded program. Deduped by NAME,
                    // not content — two BaseFonts resolving to the same host file still get
                    // separate files.
                    if (HasEmbeddedProgram(font, reader)) continue;
                    dedupKey = "name:" + baseFont;
                    if (!seen.Add(dedupKey))
                    {
                        // A later font object folded into an existing file still counts
                        // as a user of that file (per-page CSS needs to know).
                        result.Find(f => f.DedupKey == dedupKey)?.Objects.Add(font);
                        continue;
                    }
                    try { ttf = Text.SystemFontResolver.Resolve(baseFont); } catch { }
                    // A CJK font with no installed face by name serves a SUBSET
                    // of the substitute face instead (shipped as "TAG+SimSun"
                    // programs for these).
                    if (ttf is null)
                    {
                        try { ttf = BuildCjkSubstituteSubset(font, reader, out substituteFamily); }
                        catch { }
                    }
                    if (ttf is null) continue;
                }
                byte[] woff;
                try { woff = TtfToWoff(ttf); }
                catch { continue; /* an unparseable sfnt is skipped rather than aborting */ }
                var tag = substituteFamily ?? CssFaceFamily(baseFont);
                var emitted = new EmbeddedFont { Family = tag, Ttf = ttf, Woff = woff, DedupKey = dedupKey };
                emitted.Objects.Add(font);
                result.Add(emitted);
            }
        }

        var xobjects = reader.ResolveDict(resources.Get("XObject"));
        if (xobjects is null) return;
        foreach (var key in xobjects.Keys)
        {
            var xobj = reader.ResolveStream(xobjects.Get(key));
            if (xobj is null || xobj.Dict.GetName("Subtype") != "Form" || !visitedForms.Add(xobj)) continue;
            CollectFontsFromResources(reader.ResolveDict(xobj.Dict.Get("Resources")), reader, seen, seenObjs, used, result, visitedForms, options);
        }
    }

    /// <summary>Build unicode→GID from /ToUnicode (code→text) composed with the
    /// code→GID mapping — the descendant's /CIDToGIDMap (Identity when
    /// absent/named) for a Type0 font, the program's own byte cmap for a simple
    /// TrueType subset — and patch a (3,1) format-4 cmap into
    /// <paramref name="ttf"/>. Null when the program already maps Unicode or no
    /// mapping can be derived.</summary>
    private static byte[]? EnsureUnicodeCmap(byte[] ttf, PdfDictionary font, PdfReader reader)
    {
        try
        {
            var toUni = Text.TextAbsorber.ParseToUnicodeFromDict(font, reader);
            if (toUni is not { Count: > 0 }) return null;

            var isType0 = font.GetName("Subtype") == "Type0";
            byte[]? cid2gid = null;
            if (isType0
                && reader.Resolve(font.Get("DescendantFonts")) is PdfArray da2 && da2.Count > 0
                && reader.ResolveDict(da2[0]) is { } cidFont
                && cidFont.Get("CIDToGIDMap") is { } mapObj and not PdfName)
            {
                var mapStream = reader.ResolveStream(mapObj);
                if (mapStream is not null)
                    try { cid2gid = reader.DecodeStream(mapStream); } catch { }
            }
            Dictionary<int, int>? programCmap = null;
            if (!isType0)
            {
                try { programCmap = new Text.GlyphOutlineParser(ttf).CMap; }
                catch { return null; }
                if (programCmap.Count == 0) return null;
            }

            var uniToGid = new Dictionary<int, int>();
            foreach (var kv in toUni)
            {
                var (code, text) = (kv.Key, kv.Value);
                if (string.IsNullOrEmpty(text)) continue;
                int uni = text[0];
                if (char.IsHighSurrogate(text[0])) continue;   // format 4 is BMP-only
                int gid;
                if (isType0)
                {
                    gid = code;
                    if (cid2gid is not null)
                    {
                        var off = code * 2;
                        gid = off + 1 < cid2gid.Length ? (cid2gid[off] << 8) | cid2gid[off + 1] : 0;
                    }
                }
                else if (!programCmap!.TryGetValue(code, out gid))
                    continue;
                if (gid > 0 && !uniToGid.ContainsKey(uni)) uniToGid[uni] = gid;
            }
            return Text.CffToTrueType.TryAddUnicodeCmap(ttf, uniToGid);
        }
        catch { return null; }
    }

    /// <summary>Decoded FontFile3 program when it is a bare CFF (Type1C /
    /// CIDFontType0C — no sfnt wrapper), or null.</summary>
    private static byte[]? GetEmbeddedBareCff(PdfDictionary font, PdfReader reader)
    {
        try
        {
            var descriptor = reader.ResolveDict(font.Get("FontDescriptor"));
            if (descriptor is null && reader.Resolve(font.Get("DescendantFonts")) is PdfArray da && da.Count > 0)
            {
                var descFont = reader.ResolveDict(da[0]);
                descriptor = descFont is not null ? reader.ResolveDict(descFont.Get("FontDescriptor")) : null;
            }
            var fontFile = descriptor is not null ? reader.ResolveStream(descriptor.Get("FontFile3")) : null;
            if (fontFile is null) return null;
            var bytes = reader.DecodeStream(fontFile);
            if (bytes.Length < 4) return null;
            var tag = (uint)((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]);
            // An sfnt-wrapped program is handled by GetEmbeddedOpenType; bare CFF
            // starts with the 1.x header (major version 1, header size 4).
            return tag is 0x4F54544F or 0x00010000 or 0x74727565 ? null : bytes;
        }
        catch { return null; }
    }

    /// <summary>The font's /Encoding /Differences as raw code → glyph NAME (no
    /// resolution to unicode), or null when it carries none.</summary>
    private static Dictionary<int, string>? RawDifferencesNames(PdfDictionary font, PdfReader reader)
    {
        try
        {
            var enc = reader.ResolveDict(font.Get("Encoding"));
            if (reader.Resolve(enc?.Get("Differences")) is not PdfArray diffs) return null;
            var map = new Dictionary<int, string>();
            var code = 0;
            foreach (var item in diffs)
            {
                var v = reader.Resolve(item);
                if (v is Core.PdfInteger pi) code = (int)pi.Value;
                else if (v is Core.PdfReal pr) code = (int)pr.Value;
                else if (v is PdfName pn) map[code++] = pn.Value;
            }
            return map.Count > 0 ? map : null;
        }
        catch { return null; }
    }

    /// <summary>The font's /ToUnicode as code → single codepoint, skipping
    /// multi-char expansions and the U+FFFF "unknown" sentinel. Null when the
    /// font has no usable entries.</summary>
    private static Dictionary<int, int>? SingleCharToUnicode(PdfDictionary font, PdfReader reader)
    {
        try
        {
            var tou = Text.TextAbsorber.ParseToUnicodeFromDict(font, reader);
            if (tou is null) return null;
            var map = new Dictionary<int, int>();
            foreach (var (code, dst) in tou)
            {
                if (dst.Length == 0 || Text.TextAbsorber.IsUnknownToUnicodeDst(dst)) continue;
                if (CodePointCount(dst) != 1) continue;
                map[code] = char.ConvertToUtf32(dst, 0);
            }
            return map.Count > 0 ? map : null;
        }
        catch { return null; }
    }

    /// <summary>(/Ascent + |/Descent|) / 1000 from the font's descriptor — the
    /// line-height for a program that carries no sfnt of its own (a bare CFF /
    /// Type1C or Type1 subset has neither hhea nor OS/2). 0 when unreadable.</summary>
    private static double DescriptorLineHeightFactor(PdfDictionary font, PdfReader reader)
    {
        try
        {
            var descriptor = reader.ResolveDict(font.Get("FontDescriptor"));
            if (descriptor is null && reader.Resolve(font.Get("DescendantFonts")) is PdfArray da && da.Count > 0)
            {
                var descFont = reader.ResolveDict(da[0]);
                descriptor = descFont is not null ? reader.ResolveDict(descFont.Get("FontDescriptor")) : null;
            }
            if (descriptor is null) return 0;
            var ascent = DescriptorNumber(reader.Resolve(descriptor.Get("Ascent")));
            var descent = DescriptorNumber(reader.Resolve(descriptor.Get("Descent")));
            var lh = (ascent + Math.Abs(descent)) / 1000.0;
            return lh > 0 ? lh : 0;
        }
        catch { return 0; }
    }

    private static double DescriptorNumber(object? value) => value switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => 0,
    };

    /// <summary>True when the font (or its descendant CID font) carries any embedded
    /// program — FontFile (Type1), FontFile2 (TrueType) or FontFile3 (CFF/OpenType).</summary>
    private static bool HasEmbeddedProgram(PdfDictionary font, PdfReader reader)
    {
        try
        {
            var descriptor = reader.ResolveDict(font.Get("FontDescriptor"));
            if (descriptor is null && reader.Resolve(font.Get("DescendantFonts")) is PdfArray da && da.Count > 0)
            {
                var descFont = reader.ResolveDict(da[0]);
                descriptor = descFont is not null ? reader.ResolveDict(descFont.Get("FontDescriptor")) : null;
            }
            if (descriptor is null) return false;
            return descriptor.Get("FontFile") is not null
                || descriptor.Get("FontFile2") is not null
                || descriptor.Get("FontFile3") is not null;
        }
        catch { return false; }
    }

    /// <summary>Wrap an sfnt (TrueType) font program in a WOFF 1.0 container, zlib-
    /// compressing each table when that shrinks it. Structure per the W3C WOFF spec:
    /// 44-byte header, a 20-byte directory entry per table, then 4-byte-aligned
    /// (optionally compressed) table data.</summary>
    private static byte[] TtfToWoff(byte[] sfnt)
    {
        uint U32(int o) => (uint)((sfnt[o] << 24) | (sfnt[o + 1] << 16) | (sfnt[o + 2] << 8) | sfnt[o + 3]);
        ushort U16(int o) => (ushort)((sfnt[o] << 8) | sfnt[o + 1]);

        var flavor = U32(0);
        var numTables = U16(4);

        var entries = new List<byte[]>();
        var blocks = new List<byte[]>();
        var woffHeader = 44;
        var dirSize = numTables * 20;
        var offset = woffHeader + dirSize;
        uint totalSfntSize = (uint)(12 + numTables * 16);

        for (var i = 0; i < numTables; i++)
        {
            var p = 12 + i * 16;
            var tag = U32(p);
            var checksum = U32(p + 4);
            var tblOff = (int)U32(p + 8);
            var tblLen = (int)U32(p + 12);
            var orig = new byte[tblLen];
            System.Array.Copy(sfnt, tblOff, orig, 0, tblLen);
            var comp = ZlibCompress(orig);
            var data = comp.Length < orig.Length ? comp : orig;

            var e = new byte[20];
            WriteU32(e, 0, tag);
            WriteU32(e, 4, (uint)offset);
            WriteU32(e, 8, (uint)data.Length);
            WriteU32(e, 12, (uint)tblLen);
            WriteU32(e, 16, checksum);
            entries.Add(e);
            blocks.Add(data);

            offset += data.Length;
            offset = (offset + 3) & ~3;
            totalSfntSize += (uint)((tblLen + 3) & ~3);
        }

        var ms = new System.IO.MemoryStream();
        var header = new byte[44];
        WriteU32(header, 0, 0x774F4646);       // 'wOFF'
        WriteU32(header, 4, flavor);
        WriteU32(header, 8, (uint)offset);      // total WOFF length
        WriteU16(header, 12, numTables);
        WriteU32(header, 16, totalSfntSize);
        WriteU16(header, 20, 1);                // majorVersion
        ms.Write(header);
        foreach (var e in entries) ms.Write(e);
        foreach (var block in blocks)
        {
            ms.Write(block);
            while (ms.Length % 4 != 0) ms.WriteByte(0);
        }
        return ms.ToArray();
    }

    private static byte[] ZlibCompress(byte[] data)
    {
        using var ms = new System.IO.MemoryStream();
        using (var z = new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            z.Write(data, 0, data.Length);
        return ms.ToArray();
    }

    private static void WriteU32(byte[] b, int o, uint v)
    {
        b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v;
    }

    private static void WriteU16(byte[] b, int o, ushort v) { b[o] = (byte)(v >> 8); b[o + 1] = (byte)v; }
}
