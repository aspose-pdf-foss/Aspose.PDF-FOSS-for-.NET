using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Optimization;

/// <summary>
/// Removes unused glyphs from embedded font programs to reduce file size.
/// </summary>
internal static partial class FontSubsetter
{
    /// <summary>
    /// Scan all pages for font usage and remove unused glyph data from embedded fonts.
    /// When SubsetEmbeddedFonts is true in options, performs actual TrueType subsetting.
    /// Otherwise, implements a simplified approach: removes font programs for
    /// Standard 14 fonts (which are always available in PDF viewers).
    /// </summary>
    public static void SubsetFonts(PdfReader reader, bool subsetEmbedded = false,
        Func<int, PdfStream?>? resolveNewStream = null, bool stripStandard14 = true)
    {
        // Collect all font dictionaries referenced by pages
        foreach (var entry in reader.XRefTable.Entries.Values)
        {
            if (!stripStandard14) break; // PDF/A conversion: every used font must STAY embedded

            if (!entry.InUse || entry.ObjectNumber == 0) continue;

            var obj = reader.Resolve(new PdfIndirectRef(entry.ObjectNumber, entry.Generation));
            if (obj is not PdfDictionary dict) continue;

            // Check if this is a font dictionary
            var type = dict.GetName("Type");
            if (type != "Font") continue;

            var baseFont = dict.GetName("BaseFont");
            if (baseFont is null) continue;

            // A subset-prefixed name ("ABCDEF+Helvetica") is a REAL embedded subset whose
            // encoding/widths need its own program, and a composite (CID) font merely
            // NAMED like a standard face (a /Symbol CIDFontType2 under Identity-H) has
            // glyph-id-addressed content no viewer built-in can honour — neither may
            // lose its program to the standard-14 optimization.
            var isSubsetPrefixed = baseFont.Length > 7 && baseFont[6] == '+';
            var subtypeName = dict.GetName("Subtype");
            var isComposite = subtypeName is "Type0" or "CIDFontType0" or "CIDFontType2";

            // Check if this is a Standard 14 font — we can remove embedded data
            if (IsStandard14(baseFont) && !isSubsetPrefixed && !isComposite)
            {
                // Remove the font descriptor's embedded stream reference
                var descriptorRef = dict.Get("FontDescriptor");
                if (descriptorRef is not null)
                {
                    var descriptor = reader.ResolveDict(descriptorRef);
                    if (descriptor is not null)
                    {
                        // Remove embedded font file references — viewer uses built-in metrics
                        descriptor.Remove("FontFile");
                        descriptor.Remove("FontFile2");
                        descriptor.Remove("FontFile3");
                    }
                }
            }
        }

        // Perform actual TrueType subsetting if requested
        if (subsetEmbedded)
        {
            SubsetEmbeddedFonts(reader, resolveNewStream);
        }
    }

    /// <summary>
    /// Collect character codes used by each font across all pages.
    /// Returns a mapping from font dictionary reference (by object number) to the set of used character codes.
    /// Walks the page tree recursively and follows text into Form XObjects and
    /// annotation appearance streams — a font used ONLY inside a form/appearance
    /// (or a code used only there) must not lose its glyphs to the subset.
    /// A font written as a DIRECT dictionary in its resources (the form /DA embed
    /// nests the whole Type0 graph inline; the writer hoists it only at save time)
    /// has no object number — its codes are recorded in <paramref name="directCodes"/>
    /// keyed by dictionary instance.
    /// </summary>
    public static Dictionary<int, HashSet<int>> CollectUsedGlyphs(PdfReader reader,
        Dictionary<PdfDictionary, HashSet<int>>? directCodes = null)
    {
        // Map: font object number → used character codes
        var usedCodes = new Dictionary<int, HashSet<int>>();
        var visitedForms = new HashSet<PdfDictionary>();

        void WalkPageTree(PdfDictionary? node)
        {
            if (node is null) return;
            if (reader.Resolve(node.Get("Kids")) is PdfArray kids)
            {
                foreach (var kidRef in kids)
                    WalkPageTree(reader.ResolveDict(kidRef));
                return;
            }

            var resources = reader.ResolveDict(node.Get("Resources"));
            CollectFromContext(GetPageContent(node, reader), resources, reader, usedCodes, directCodes, visitedForms);

            // Annotation appearance streams draw with their own resources.
            if (reader.Resolve(node.Get("Annots")) is PdfArray annots)
                foreach (var annotRef in annots)
                {
                    var annot = reader.ResolveDict(annotRef);
                    var ap = annot is null ? null : reader.ResolveDict(annot.Get("AP"));
                    var n = ap is null ? null : reader.Resolve(ap.Get("N"));
                    if (n is not PdfStream apStream) continue;
                    byte[] data;
                    try { data = reader.DecodeStream(apStream); } catch { continue; }
                    var apRes = reader.ResolveDict(apStream.Dict.Get("Resources")) ?? resources;
                    CollectFromContext(data, apRes, reader, usedCodes, directCodes, visitedForms);
                }
        }

        WalkPageTree(reader.ResolveDict(reader.Catalog.Get("Pages")));
        return usedCodes;
    }

    /// <summary>Scan one content stream for text-show codes against its resource
    /// context, recursing through Form XObjects invoked with Do.</summary>
    private static void CollectFromContext(byte[] streamBytes, PdfDictionary? resources,
        PdfReader reader, Dictionary<int, HashSet<int>> usedCodes,
        Dictionary<PdfDictionary, HashSet<int>>? directCodes, HashSet<PdfDictionary> visitedForms)
    {
        var fontResources = resources is not null ? reader.ResolveDict(resources.Get("Font")) : null;

        // Font resource name → (dict, objNum, CID-keyed?, predefined CMap). A composite
        // (Type0) font's show-string bytes are 2-byte codes, unless a predefined CMap
        // declares its own code widths. A direct (inline) font dict keeps objNum 0 and
        // its codes go to directCodes.
        var fontMap = new Dictionary<string, (PdfDictionary dict, int objNum, bool isCid, PredefinedCMap? cmap)>();
        if (fontResources is not null)
            foreach (var fontKey in fontResources.Keys)
            {
                var fontRef = fontResources.Get(fontKey);
                int fontObjNum = fontRef is PdfIndirectRef iref ? iref.ObjectNumber : 0;
                var fontDict = reader.ResolveDict(fontRef);
                if (fontDict is not null && (fontObjNum > 0 || directCodes is not null))
                    fontMap[fontKey] = (fontDict, fontObjNum, fontDict.GetName("Subtype") == "Type0", CodeMapOf(fontDict));
            }

        var xobjRes = resources is not null ? reader.ResolveDict(resources.Get("XObject")) : null;

        CollectUsedCodesFromStream(streamBytes, fontMap, usedCodes, directCodes, xobjName =>
        {
            if (xobjRes is null) return;
            if (reader.Resolve(xobjRes.Get(xobjName)) is not PdfStream formStream) return;
            if (formStream.Dict.GetName("Subtype") != "Form") return;
            if (!visitedForms.Add(formStream.Dict)) return;
            byte[] data;
            try { data = reader.DecodeStream(formStream); } catch { return; }
            var subRes = reader.ResolveDict(formStream.Dict.Get("Resources")) ?? resources;
            CollectFromContext(data, subRes, reader, usedCodes, directCodes, visitedForms);
        });
    }

    /// <summary>The predefined CMap a composite font's show strings are coded in, when the
    /// library carries its table; null under Identity, an embedded CMap stream or a CMap
    /// without a table (those read as 2-byte codes).</summary>
    private static PredefinedCMap? CodeMapOf(PdfDictionary fontDict) =>
        fontDict.GetName("Encoding") is { } name && name is not ("Identity-H" or "Identity-V")
            ? PredefinedCMap.TryGet(name)
            : null;

    /// <summary>The CIDs a composite font's used codes select: the codes themselves under
    /// Identity or an embedded CMap stream, their table entries under a predefined CMap —
    /// and null under a predefined CMap the library has no table for, since its used
    /// glyphs cannot be told from the rest.</summary>
    private static HashSet<int>? UsedCids(PdfDictionary fontDict, HashSet<int> charCodes)
    {
        if (fontDict.GetName("Encoding") is not { } name || name is "Identity-H" or "Identity-V") return charCodes;
        var cmap = PredefinedCMap.TryGet(name);
        if (cmap is null) return null;
        var cids = new HashSet<int>();
        foreach (var code in charCodes) cids.Add(cmap.Cid(code));
        return cids;
    }

    /// <summary>Sparse-subset the programs the save's embed pass just put into composite
    /// fonts (a CJK system face runs to many MB) to the glyphs the pages show; a program
    /// the source carried is left whole.</summary>
    public static void SubsetNewlyEmbeddedCidFonts(PdfReader reader, Func<int, PdfStream?> resolveNewStream)
    {
        var directCodes = new Dictionary<PdfDictionary, HashSet<int>>();
        var usedCodes = CollectUsedGlyphs(reader, directCodes);
        if (usedCodes.Count == 0 && directCodes.Count == 0) return;
        SubsetCidFonts(reader, usedCodes, directCodes, resolveNewStream, newlyEmbeddedOnly: true);
    }

    /// <summary>
    /// Subset embedded TrueType fonts, keeping only glyphs used in the document.
    /// </summary>
    public static void SubsetEmbeddedFonts(PdfReader reader, Func<int, PdfStream?>? resolveNewStream = null, bool newlyEmbeddedOnly = false)
    {
        var su = new FontSubsetState();
        su.reader = reader;
        su.resolveNewStream = resolveNewStream;
        su.newlyEmbeddedOnly = newlyEmbeddedOnly;
        su.directCodes = new Dictionary<PdfDictionary, HashSet<int>>();
        su.usedCodes = CollectUsedGlyphs(su.reader, su.directCodes);
        if (su.usedCodes.Count == 0 && su.directCodes.Count == 0) return;

        su.byProgram = new Dictionary<PdfStream,
            (HashSet<int> subsetCodes, List<(PdfDictionary fontDict, PdfDictionary descriptor, string baseFont, HashSet<int> ownCodes)> fonts, bool isPending)>();

        // Simple fonts, indirect and direct alike, grouped by program: a program shared
        // by several font dictionaries is subset once, to the union of their codes.
        foreach (var (fontObjNum, charCodes) in su.usedCodes)
            if (su.reader.Resolve(new PdfIndirectRef(fontObjNum, 0)) is PdfDictionary fontDict)
                CollectFontSubsetCodes(su, fontDict, charCodes);
        foreach (var (fontDict, charCodes) in su.directCodes)
            CollectFontSubsetCodes(su, fontDict, charCodes);

        // Composite (Type0/CID) fonts: group by FontFile2 stream so a program
        // shared between several font dictionaries is subset ONCE with the union of
        // their used CIDs (subsetting per-dict would drop the other dict's glyphs). A
        // program a simple font shares keeps that font's glyphs and the cmap it reads them
        // through.
        var cidPrograms = SubsetCidFonts(su.reader, su.usedCodes, su.directCodes, su.resolveNewStream,
            su.newlyEmbeddedOnly, su.byProgram);

        // A program a composite font draws keeps its glyph numbering: the CIDs address it.
        foreach (var (fontFileStream, entry) in su.byProgram)
            if (!cidPrograms.Contains(fontFileStream))
                SubsetFontProgram(su, fontFileStream, entry);
    }

    /// <summary>
    /// Sparse-subset the embedded programs of composite (Type0/CID) fonts to their
    /// used CIDs. Glyph numbering is preserved (the content stream's CIDs must stay
    /// valid), so unused glyphs just lose their outlines. Programs shared between
    /// several font dictionaries are subset once with the union of their used CIDs, and with
    /// the glyphs of the simple fonts in <paramref name="simpleUsers"/> that share them.
    /// Returns every program a composite font draws.
    /// </summary>
    private static HashSet<PdfStream> SubsetCidFonts(PdfReader reader, Dictionary<int, HashSet<int>> usedCodes,
        Dictionary<PdfDictionary, HashSet<int>> directCodes, Func<int, PdfStream?>? resolveNewStream,
        bool newlyEmbeddedOnly = false, Dictionary<PdfStream, (HashSet<int> subsetCodes,
            List<(PdfDictionary fontDict, PdfDictionary descriptor, string baseFont, HashSet<int> ownCodes)> fonts, bool isPending)>? simpleUsers = null)
    {
        var sc = new CidSubsetState();
        sc.reader = reader;
        sc.usedCodes = usedCodes;
        sc.directCodes = directCodes;
        sc.resolveNewStream = resolveNewStream;
        sc.newlyEmbeddedOnly = newlyEmbeddedOnly;
        sc.simpleUsers = simpleUsers;
        sc.byProgram = new Dictionary<PdfStream, (HashSet<int> gids, List<(PdfDictionary type0, PdfDictionary descriptor)> fonts)>();
        sc.byMap = new Dictionary<PdfStream, (HashSet<int> cids, byte[] data)>();

        sc.candidates = new List<(PdfDictionary fontDict, HashSet<int> charCodes)>();
        foreach (var (fontObjNum, charCodes) in sc.usedCodes)
        {
            if (sc.reader.Resolve(new PdfIndirectRef(fontObjNum, 0)) is not PdfDictionary fontDict) continue;
            sc.candidates.Add((fontDict, charCodes));
        }
        foreach (var (fontDict, charCodes) in sc.directCodes)
            sc.candidates.Add((fontDict, charCodes));

        foreach (var (fontDict, charCodes) in sc.candidates)
        {
            if (!CollectCidCandidate(sc, fontDict, charCodes)) break;
        }

        foreach (var (stream, entry) in sc.byProgram)
        {
            if (sc.unknownUse.Contains(stream)) continue;
            if (!SubsetCidProgram(sc, stream, entry)) break;
        }

        // A dense CIDToGIDMap barely compresses (tens of KB of distinct GIDs). The
        // sparse subset keeps outlines only for the used CIDs, so entries for every
        // other CID can go to 0 (.notdef) — the long zero runs then deflate to
        // almost nothing on save.
        foreach (var (mapStream, (cids, data)) in sc.byMap)
        {
            if (sc.unknownUse.Contains(mapStream)) continue;
            if (!WriteCidSubsetMap(mapStream, cids, data)) break;
        }
        return sc.drawnPrograms;
    }

    /// <summary>
    /// Update the /Widths array and /FirstChar, /LastChar to cover only used characters. A
    /// code takes the program's advance for the character its /Encoding names (a byte code is
    /// no character: 0x92 under WinAnsi is a right quote, not U+0092). Where
    /// <paramref name="keepStated"/> is set - a program the source embedded, whose widths the
    /// page was laid out against - a code keeps the width the font states for it.
    /// </summary>
    private static void UpdateWidths(PdfDictionary fontDict, HashSet<int> usedCodes,
        TrueTypeParser parser, PdfReader reader, bool keepStated)
    {
        if (usedCodes.Count == 0) return;

        var minCode = int.MaxValue;
        var maxCode = int.MinValue;
        foreach (var code in usedCodes)
        {
            if (code < minCode) minCode = code;
            if (code > maxCode) maxCode = code;
        }

        // Clamp to valid range
        if (minCode < 0) minCode = 0;
        if (maxCode > 255) maxCode = 255;
        if (minCode > maxCode) return;

        // Build new widths array
        var unitsPerEm = parser.UnitsPerEm > 0 ? parser.UnitsPerEm : 1000;
        var widthCount = maxCode - minCode + 1;
        var stated = reader.Resolve(fontDict.Get("Widths")) as PdfArray;
        var firstStated = (int)fontDict.GetInt("FirstChar", 0);
        string?[]? names = null;
        var widths = new PdfArray();
        for (var i = 0; i < widthCount; i++)
        {
            var charCode = minCode + i;
            var index = charCode - firstStated;
            if (keepStated && stated is not null && index >= 0 && index < stated.Count
                && reader.Resolve(stated[index]) is { } value and (PdfInteger { Value: > 0 } or PdfReal { Value: > 0 }))
            {
                widths.Add(value);
                continue;
            }
            names ??= EncodingGlyphNames(fontDict, reader);
            var width = parser.GetCharWidth(ProgramKey(parser, names, charCode));
            // Convert to PDF units (1/1000 of text space)
            var pdfWidth = (int)Math.Round((double)width * 1000.0 / unitsPerEm);
            widths.Add(new PdfInteger(pdfWidth));
        }

        fontDict.Set("FirstChar", new PdfInteger(minCode));
        fontDict.Set("LastChar", new PdfInteger(maxCode));
        fontDict.Set("Widths", widths);
    }

    /// <summary>
    /// Add a 6-character random prefix to the BaseFont name per PDF spec convention
    /// (e.g., "ABCDEF+Helvetica").
    /// </summary>
    private static void AddSubsetPrefix(PdfDictionary fontDict, PdfDictionary descriptor, string baseFont)
    {
        // Strip existing prefix if present
        var name = baseFont;
        if (name.Length > 7 && name[6] == '+')
            name = name[7..];

        // Generate random 6-letter prefix
        var prefix = GenerateSubsetPrefix();
        var newName = $"{prefix}+{name}";

        fontDict.Set("BaseFont", new PdfName(newName));

        // Also update FontName in the descriptor if present
        if (descriptor.GetName("FontName") is not null)
            descriptor.Set("FontName", new PdfName(newName));
    }

    /// <summary>
    /// Generate a random 6-character uppercase letter prefix for font subsetting.
    /// </summary>
    private static string GenerateSubsetPrefix()
    {
#pragma warning disable S2245 // a font subset tag (ISO 32000-1 9.6.4) needs uniqueness, not unpredictability
        var random = new Random();
#pragma warning restore S2245
        var chars = new char[6];
        for (var i = 0; i < 6; i++)
            chars[i] = (char)('A' + random.Next(26));
        return new string(chars);
    }

    /// <summary>
    /// Parse content streams from a page dictionary and collect character codes used by each font.
    /// </summary>
    private static void CollectUsedCodesFromStream(byte[] streamBytes,
        Dictionary<string, (PdfDictionary dict, int objNum, bool isCid, PredefinedCMap? cmap)> fontMap,
        Dictionary<int, HashSet<int>> usedCodes,
        Dictionary<PdfDictionary, HashSet<int>>? directCodes = null,
        Action<string>? onFormXObject = null)
    {
        var lexer = new PdfLexer(streamBytes);
        var operands = new List<PdfObject>();
        string? currentFontKey = null;
        // The font is part of the graphics state: Q restores the one in force at its q.
        var savedFontKeys = new Stack<string?>();

        while (true)
        {
            var token = lexer.NextToken();
            if (token.Kind == TokenKind.Eof) break;

            switch (token.Kind)
            {
                case TokenKind.Integer:
                    operands.Add(new PdfInteger(token.IntValue));
                    break;
                case TokenKind.Real:
                    operands.Add(new PdfReal(token.RealValue));
                    break;
                case TokenKind.LiteralString:
                    operands.Add(new PdfString(token.BytesValue!));
                    break;
                case TokenKind.HexString:
                    operands.Add(new PdfString(token.BytesValue!, isHex: true));
                    break;
                case TokenKind.Name:
                    operands.Add(new PdfName(token.StringValue!));
                    break;
                case TokenKind.Boolean:
                    operands.Add(token.BoolValue ? PdfBoolean.True : PdfBoolean.False);
                    break;
                case TokenKind.ArrayStart:
                    operands.Add(ParseArray(lexer));
                    break;
                case TokenKind.Keyword:
                {
                    var op = token.StringValue!;
                    switch (op)
                    {
                        case "Tf" when operands.Count >= 2 && operands[0] is PdfName fontName:
                            currentFontKey = fontName.Value;
                            break;

                        case "q":
                            savedFontKeys.Push(currentFontKey);
                            break;

                        case "Q" when savedFontKeys.Count > 0:
                            currentFontKey = savedFontKeys.Pop();
                            break;

                        case "Tj" when operands.Count >= 1 && operands[0] is PdfString s:
                            CollectCodesFromString(s.Value, currentFontKey, fontMap, usedCodes, directCodes);
                            break;

                        case "TJ" when operands.Count >= 1 && operands[0] is PdfArray arr:
                            foreach (var item in arr)
                            {
                                if (item is PdfString ts)
                                    CollectCodesFromString(ts.Value, currentFontKey, fontMap, usedCodes, directCodes);
                            }
                            break;

                        case "'" when operands.Count >= 1 && operands[0] is PdfString qs:
                            CollectCodesFromString(qs.Value, currentFontKey, fontMap, usedCodes, directCodes);
                            break;

                        case "\"" when operands.Count >= 3 && operands[2] is PdfString dqs:
                            CollectCodesFromString(dqs.Value, currentFontKey, fontMap, usedCodes, directCodes);
                            break;

                        case "Do" when operands.Count >= 1 && operands[0] is PdfName xobjName:
                            onFormXObject?.Invoke(xobjName.Value);
                            break;
                    }

                    operands.Clear();
                    break;
                }
                default:
                    operands.Clear();
                    break;
            }
        }
    }

    /// <summary>
    /// Collect character codes from a text string into the usedCodes map.
    /// </summary>
    private static void CollectCodesFromString(byte[] bytes, string? currentFontKey,
        Dictionary<string, (PdfDictionary dict, int objNum, bool isCid, PredefinedCMap? cmap)> fontMap,
        Dictionary<int, HashSet<int>> usedCodes,
        Dictionary<PdfDictionary, HashSet<int>>? directCodes)
    {
        if (currentFontKey is null) return;
        if (!fontMap.TryGetValue(currentFontKey, out var fontInfo)) return;

        HashSet<int>? codes;
        if (fontInfo.objNum == 0)
        {
            // Direct (inline) font dictionary — keyed by instance.
            if (directCodes is null) return;
            if (!directCodes.TryGetValue(fontInfo.dict, out codes))
            {
                codes = new HashSet<int>();
                directCodes[fontInfo.dict] = codes;
            }
        }
        else if (!usedCodes.TryGetValue(fontInfo.objNum, out codes))
        {
            codes = new HashSet<int>();
            usedCodes[fontInfo.objNum] = codes;
        }

        if (fontInfo.cmap is not null)
        {
            // A predefined CMap: codes as wide as its codespace ranges declare (a byte
            // for ASCII under GBK-EUC-H, two for the rest).
            codes.UnionWith(fontInfo.cmap.Codes(bytes));
        }
        else if (fontInfo.isCid)
        {
            // Composite font: 2-byte big-endian codes (CIDs).
            for (var i = 0; i + 1 < bytes.Length; i += 2)
                codes.Add((bytes[i] << 8) | bytes[i + 1]);
        }
        else
        {
            foreach (var b in bytes)
                codes.Add(b);
        }
    }

    /// <summary>
    /// The content of a page: its one stream, or the streams of its /Contents array joined in
    /// order with a line break between them. A page's streams are one content (ISO 32000-1
    /// §7.8.2): a font set in one stream shows the text of the next, and a producer may split
    /// an operator's operands from the operator across two streams.
    /// </summary>
    private static byte[] GetPageContent(PdfDictionary pageDict, PdfReader reader)
    {
        var contents = reader.Resolve(pageDict.Get("Contents"));

        // Best-effort: glyph collection must never abort the whole pass because one
        // stream's filter chain fails to decode (e.g. an exotic LZW variant) — an
        // uncollected stream just means its fonts keep their full glyph sets.
        switch (contents)
        {
            case PdfStream stream:
                try { return reader.DecodeStream(stream); } catch { return []; }
            case PdfArray arr:
                var joined = new MemoryStream();
                foreach (var item in arr)
                {
                    var s = reader.ResolveStream(item);
                    if (s is null) continue;
                    byte[] data;
                    try { data = reader.DecodeStream(s); } catch { continue; /* a stream that does not decode is skipped, as if absent */ }
                    joined.Write(data, 0, data.Length);
                    joined.WriteByte((byte)'\n');
                }
                return joined.ToArray();
            default:
                return [];
        }
    }

    private static PdfArray ParseArray(PdfLexer lexer)
    {
        var arr = new PdfArray();
        while (true)
        {
            var t = lexer.NextToken();
            if (t.Kind == TokenKind.ArrayEnd || t.Kind == TokenKind.Eof) break;
            switch (t.Kind)
            {
                case TokenKind.Integer: arr.Add(new PdfInteger(t.IntValue)); break;
                case TokenKind.Real: arr.Add(new PdfReal(t.RealValue)); break;
                case TokenKind.LiteralString: arr.Add(new PdfString(t.BytesValue!)); break;
                case TokenKind.HexString: arr.Add(new PdfString(t.BytesValue!, isHex: true)); break;
                case TokenKind.Name: arr.Add(new PdfName(t.StringValue!)); break;
            }
        }
        return arr;
    }

    /// <summary>
    /// Drop embedded font programs (/FontFile, /FontFile2, /FontFile3) for fonts a viewer can
    /// substitute from its built-ins or the host system — the <c>UnembedFonts</c>
    /// behaviour. A font is unembedded only when it is one of the Standard 14 or its family
    /// (subset tag stripped) resolves to an installed system face, so glyph shapes stay close.
    /// Embedded subset fonts whose family isn't available are kept intact. Returns the object
    /// numbers of the programs it detached that no font keeping its program still holds.
    /// </summary>
    public static List<int> UnembedFonts(PdfReader reader)
    {
        var detached = new List<int>();
        var held = new HashSet<int>();
        foreach (var entry in reader.XRefTable.Entries.Values)
        {
            if (!entry.InUse || entry.ObjectNumber == 0) continue;

            PdfObject? obj;
            try { obj = reader.Resolve(new PdfIndirectRef(entry.ObjectNumber, entry.Generation)); }
            catch { continue; }
            if (obj is not PdfDictionary dict || dict.GetName("Type") != "Font") continue;
            // A CIDFont is reached through its Type0 parent, whose encoding decides.
            if (dict.GetName("Subtype") is "CIDFontType0" or "CIDFontType2") continue;

            var baseFont = dict.GetName("BaseFont");
            if (baseFont is null) continue;
            var face = IsStandard14(baseFont) ? null : SystemFontResolver.ResolveInstalledFont(baseFont);
            var keeps = (!IsStandard14(baseFont) && face is null)
                || (face is not null && IsIdentityEncodedSubset(dict, baseFont) && !TrueTypeParser.HasUnicodeCmap(face))
                || (face is not null && IsSymbolicByProgram(reader, dict) && TrueTypeParser.HasUnicodeCmap(face));

            // Simple fonts carry the descriptor directly; Type0 carries it on the descendant
            // CIDFont. Strip the program from whichever descriptor(s) apply.
            var into = keeps ? held : null;
            StripFontFile(reader.ResolveDict(dict.Get("FontDescriptor")), detached, into);
            if (reader.Resolve(dict.Get("DescendantFonts")) is PdfArray descendants)
                foreach (var d in descendants)
                    if (reader.ResolveDict(d) is { } cid)
                        StripFontFile(reader.ResolveDict(cid.Get("FontDescriptor")), detached, into);
        }
        detached.RemoveAll(held.Contains);
        return detached;
    }

    /// <summary>True for a composite font whose codes are glyph indices of its own subset program.
    /// A viewer standing an installed face in for such a font reaches the glyphs through the
    /// characters the codes map to; when the installed face is a symbol font, which maps no
    /// characters, nothing can be drawn for them, so the font keeps its program.</summary>
    private static bool IsIdentityEncodedSubset(PdfDictionary font, string baseFont)
    {
        if (font.GetName("Subtype") != "Type0") return false;
        var encoding = font.GetName("Encoding");
        if (encoding is not ("Identity-H" or "Identity-V")) return false;
        return baseFont.Length > 7 && baseFont[6] == '+';
    }

    /// <summary>True for a simple TrueType font flagged symbolic with no /Encoding: its codes reach their
    /// glyphs only through the embedded program's own cmap. A text face installed under its name maps
    /// characters, not those codes, so a viewer standing it in draws other glyphs (the codes of a
    /// subset numbered from 0x21 come out as '!"#$...'); such a font keeps its program.</summary>
    private static bool IsSymbolicByProgram(PdfReader reader, PdfDictionary font)
    {
        if (font.GetName("Subtype") != "TrueType" || font.Get("Encoding") is not null) return false;
        var descriptor = reader.ResolveDict(font.Get("FontDescriptor"));
        return descriptor is not null && (descriptor.GetInt("Flags") & SymbolicFlag) != 0
            && descriptor.Get("FontFile2") is not null;
    }

    /// <summary>The Symbolic bit of a font descriptor's /Flags (ISO 32000-1 table 123).</summary>
    private const int SymbolicFlag = 4;

    /// <summary>Take the program off <paramref name="descriptor"/>, noting its object in <paramref name="detached"/>;
    /// with <paramref name="held"/> given, the font keeps its program and the object is noted there instead.</summary>
    private static void StripFontFile(PdfDictionary? descriptor, List<int> detached, HashSet<int>? held)
    {
        if (descriptor is null) return;
        foreach (var key in FontFileKeys)
        {
            if (descriptor.Get(key) is not { } program) continue;
            if (held is not null)
            {
                if (program is PdfIndirectRef kept) held.Add(kept.ObjectNumber);
                continue;
            }
            if (program is PdfIndirectRef reference) detached.Add(reference.ObjectNumber);
            descriptor.Remove(key);
        }
    }

    private static readonly string[] FontFileKeys = ["FontFile", "FontFile2", "FontFile3"];

    private static readonly HashSet<string> Standard14Fonts = new(StringComparer.Ordinal)
    {
        "Times-Roman", "Times-Bold", "Times-Italic", "Times-BoldItalic",
        "Helvetica", "Helvetica-Bold", "Helvetica-Oblique", "Helvetica-BoldOblique",
        "Courier", "Courier-Bold", "Courier-Oblique", "Courier-BoldOblique",
        "Symbol", "ZapfDingbats",
    };

    private static bool IsStandard14(string baseFont)
    {
        // Some PDFs append a tag like "ABCDEF+Helvetica" — strip the subset prefix
        var name = baseFont;
        if (name.Length > 7 && name[6] == '+')
            name = name[7..];

        return Standard14Fonts.Contains(name);
    }
}
