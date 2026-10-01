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
            foreach (var streamBytes in GetContentStreams(node, reader))
                CollectFromContext(streamBytes, resources, reader, usedCodes, directCodes, visitedForms);

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

        // Font resource name → (dict, objNum, CID-keyed?). A composite (Type0)
        // font's show-string bytes are 2-byte codes. A direct (inline) font dict
        // keeps objNum 0 and its codes go to directCodes.
        var fontMap = new Dictionary<string, (PdfDictionary dict, int objNum, bool isCid)>();
        if (fontResources is not null)
            foreach (var fontKey in fontResources.Keys)
            {
                var fontRef = fontResources.Get(fontKey);
                int fontObjNum = fontRef is PdfIndirectRef iref ? iref.ObjectNumber : 0;
                var fontDict = reader.ResolveDict(fontRef);
                if (fontDict is not null && (fontObjNum > 0 || directCodes is not null))
                    fontMap[fontKey] = (fontDict, fontObjNum, fontDict.GetName("Subtype") == "Type0");
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

        // Composite (Type0/CID) fonts first: group by FontFile2 stream so a program
        // shared between several font dictionaries is subset ONCE with the union of
        // their used CIDs (subsetting per-dict would drop the other dict's glyphs).
        SubsetCidFonts(su.reader, su.usedCodes, su.directCodes, su.resolveNewStream, su.newlyEmbeddedOnly);

        su.byProgram = new Dictionary<PdfStream,
            (HashSet<int> subsetCodes, List<(PdfDictionary fontDict, PdfDictionary descriptor, string baseFont, HashSet<int> ownCodes)> fonts, bool isPending)>();

        foreach (var (fontObjNum, charCodes) in su.usedCodes)
        {
            if (!CollectFontSubsetCodes(su, fontObjNum, charCodes)) break;
        }

        foreach (var (fontFileStream, entry) in su.byProgram)
        {
            if (!SubsetFontProgram(su, fontFileStream, entry)) break;
        }
    }

    /// <summary>
    /// Sparse-subset the embedded programs of composite (Type0/CID) fonts to their
    /// used CIDs. Glyph numbering is preserved (the content stream's CIDs must stay
    /// valid), so unused glyphs just lose their outlines. Programs shared between
    /// several font dictionaries are subset once with the union of their used CIDs.
    /// </summary>
    private static void SubsetCidFonts(PdfReader reader, Dictionary<int, HashSet<int>> usedCodes, Dictionary<PdfDictionary, HashSet<int>> directCodes, Func<int, PdfStream?>? resolveNewStream, bool newlyEmbeddedOnly = false)
    {
        var sc = new CidSubsetState();
        sc.reader = reader;
        sc.usedCodes = usedCodes;
        sc.directCodes = directCodes;
        sc.resolveNewStream = resolveNewStream;
        sc.newlyEmbeddedOnly = newlyEmbeddedOnly;
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
            if (!SubsetCidProgram(sc, stream, entry)) break;
        }

        // A dense CIDToGIDMap barely compresses (tens of KB of distinct GIDs). The
        // sparse subset keeps outlines only for the used CIDs, so entries for every
        // other CID can go to 0 (.notdef) — the long zero runs then deflate to
        // almost nothing on save.
        foreach (var (mapStream, (cids, data)) in sc.byMap)
        {
            if (!WriteCidSubsetMap(mapStream, cids, data)) break;
        }
    }

    /// <summary>
    /// Update the /Widths array and /FirstChar, /LastChar to cover only used characters.
    /// </summary>
    private static void UpdateWidths(PdfDictionary fontDict, HashSet<int> usedCodes,
        TrueTypeParser parser, PdfReader reader)
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
        var widths = new PdfArray();
        for (var i = 0; i < widthCount; i++)
        {
            var charCode = minCode + i;
            var width = parser.GetCharWidth(charCode);
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
        var random = new Random();
        var chars = new char[6];
        for (var i = 0; i < 6; i++)
            chars[i] = (char)('A' + random.Next(26));
        return new string(chars);
    }

    /// <summary>
    /// Parse content streams from a page dictionary and collect character codes used by each font.
    /// </summary>
    private static void CollectUsedCodesFromStream(byte[] streamBytes,
        Dictionary<string, (PdfDictionary dict, int objNum, bool isCid)> fontMap,
        Dictionary<int, HashSet<int>> usedCodes,
        Dictionary<PdfDictionary, HashSet<int>>? directCodes = null,
        Action<string>? onFormXObject = null)
    {
        var lexer = new PdfLexer(streamBytes);
        var operands = new List<PdfObject>();
        string? currentFontKey = null;

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
        Dictionary<string, (PdfDictionary dict, int objNum, bool isCid)> fontMap,
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

        if (fontInfo.isCid)
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
    /// Get all content streams from a page dictionary.
    /// </summary>
    private static List<byte[]> GetContentStreams(PdfDictionary pageDict, PdfReader reader)
    {
        var result = new List<byte[]>();
        var contents = reader.Resolve(pageDict.Get("Contents"));

        // Best-effort: glyph collection must never abort the whole pass because one
        // stream's filter chain fails to decode (e.g. an exotic LZW variant) — an
        // uncollected stream just means its fonts keep their full glyph sets.
        switch (contents)
        {
            case PdfStream stream:
                try { result.Add(reader.DecodeStream(stream)); } catch { }
                break;
            case PdfArray arr:
                foreach (var item in arr)
                {
                    var s = reader.ResolveStream(item);
                    if (s is null) continue;
                    try { result.Add(reader.DecodeStream(s)); } catch { }
                }
                break;
        }

        return result;
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
    /// Embedded subset fonts whose family isn't available are kept intact. The orphaned font
    /// program streams are removed from the file by the reachability pass that runs afterwards.
    /// </summary>
    public static void UnembedFonts(PdfReader reader)
    {
        foreach (var entry in reader.XRefTable.Entries.Values)
        {
            if (!entry.InUse || entry.ObjectNumber == 0) continue;

            PdfObject? obj;
            try { obj = reader.Resolve(new PdfIndirectRef(entry.ObjectNumber, entry.Generation)); }
            catch { continue; }
            if (obj is not PdfDictionary dict || dict.GetName("Type") != "Font") continue;

            var baseFont = dict.GetName("BaseFont");
            if (baseFont is null) continue;
            if (!IsStandard14(baseFont) && SystemFontResolver.Resolve(baseFont) is null)
                continue;

            // Simple fonts carry the descriptor directly; Type0 carries it on the descendant
            // CIDFont. Strip the program from whichever descriptor(s) apply.
            StripFontFile(reader.ResolveDict(dict.Get("FontDescriptor")));
            if (reader.Resolve(dict.Get("DescendantFonts")) is PdfArray descendants)
                foreach (var d in descendants)
                    if (reader.ResolveDict(d) is { } cid)
                        StripFontFile(reader.ResolveDict(cid.Get("FontDescriptor")));
        }
    }

    private static void StripFontFile(PdfDictionary? descriptor)
    {
        if (descriptor is null) return;
        descriptor.Remove("FontFile");
        descriptor.Remove("FontFile2");
        descriptor.Remove("FontFile3");
    }

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
