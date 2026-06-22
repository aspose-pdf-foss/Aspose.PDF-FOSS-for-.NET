using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Optimization;

/// <summary>
/// Removes unused glyphs from embedded font programs to reduce file size.
/// </summary>
internal static class FontSubsetter
{
    /// <summary>
    /// Scan all pages for font usage and remove unused glyph data from embedded fonts.
    /// When SubsetEmbeddedFonts is true in options, performs actual TrueType subsetting.
    /// Otherwise, implements a simplified approach: removes font programs for
    /// Standard 14 fonts (which are always available in PDF viewers).
    /// </summary>
    public static void SubsetFonts(PdfReader reader, bool subsetEmbedded = false)
    {
        // Collect all font dictionaries referenced by pages
        foreach (var entry in reader.XRefTable.Entries.Values)
        {
            if (!entry.InUse || entry.ObjectNumber == 0) continue;

            var obj = reader.Resolve(new PdfIndirectRef(entry.ObjectNumber, entry.Generation));
            if (obj is not PdfDictionary dict) continue;

            // Check if this is a font dictionary
            var type = dict.GetName("Type");
            if (type != "Font") continue;

            var baseFont = dict.GetName("BaseFont");
            if (baseFont is null) continue;

            // Check if this is a Standard 14 font — we can remove embedded data
            if (IsStandard14(baseFont))
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
            SubsetEmbeddedFonts(reader);
        }
    }

    /// <summary>
    /// Collect character codes used by each font across all pages.
    /// Returns a mapping from font dictionary reference (by object number) to the set of used character codes.
    /// </summary>
    public static Dictionary<int, HashSet<int>> CollectUsedGlyphs(PdfReader reader)
    {
        // Map: font object number → used character codes
        var usedCodes = new Dictionary<int, HashSet<int>>();

        // Iterate all pages
        var catalog = reader.Catalog;
        var pagesRef = catalog.Get("Pages");
        var pagesDict = reader.ResolveDict(pagesRef);
        if (pagesDict is null) return usedCodes;

        var kids = reader.Resolve(pagesDict.Get("Kids")) as PdfArray;
        if (kids is null) return usedCodes;

        foreach (var kidRef in kids)
        {
            var pageDict = reader.ResolveDict(kidRef);
            if (pageDict is null) continue;

            // Get font resources for this page
            var resources = reader.ResolveDict(pageDict.Get("Resources"));
            var fontResources = resources is not null ? reader.ResolveDict(resources.Get("Font")) : null;
            if (fontResources is null) continue;

            // Build a mapping from font resource name to (font dict, object number)
            var fontMap = new Dictionary<string, (PdfDictionary dict, int objNum)>();
            foreach (var fontKey in fontResources.Keys)
            {
                var fontRef = fontResources.Get(fontKey);
                int fontObjNum = fontRef is PdfIndirectRef iref ? iref.ObjectNumber : 0;
                var fontDict = reader.ResolveDict(fontRef);
                if (fontDict is not null && fontObjNum > 0)
                    fontMap[fontKey] = (fontDict, fontObjNum);
            }

            // Parse content streams
            var contentStreams = GetContentStreams(pageDict, reader);
            foreach (var streamBytes in contentStreams)
            {
                CollectUsedCodesFromStream(streamBytes, fontMap, usedCodes);
            }
        }

        return usedCodes;
    }

    /// <summary>
    /// Subset embedded TrueType fonts, keeping only glyphs used in the document.
    /// </summary>
    public static void SubsetEmbeddedFonts(PdfReader reader)
    {
        var usedCodes = CollectUsedGlyphs(reader);
        if (usedCodes.Count == 0) return;

        foreach (var (fontObjNum, charCodes) in usedCodes)
        {
            var fontObj = reader.Resolve(new PdfIndirectRef(fontObjNum, 0));
            if (fontObj is not PdfDictionary fontDict) continue;

            var baseFont = fontDict.GetName("BaseFont");
            if (baseFont is null) continue;

            // Skip Standard 14 fonts
            if (IsStandard14(baseFont)) continue;

            // Get font descriptor
            var descriptorObj = fontDict.Get("FontDescriptor");
            if (descriptorObj is null) continue;
            var descriptor = reader.ResolveDict(descriptorObj);
            if (descriptor is null) continue;

            // Only process TrueType fonts with FontFile2
            var fontFileRef = descriptor.Get("FontFile2");
            if (fontFileRef is null) continue;

            var fontFileStream = reader.ResolveStream(fontFileRef);
            if (fontFileStream is null) continue;

            // Decode the font stream
            byte[] fontData;
            try
            {
                fontData = reader.DecodeStream(fontFileStream);
            }
            catch
            {
                continue; // Skip fonts that fail to decode
            }

            if (fontData.Length < 12) continue; // Too small to be a valid TrueType font

            // Parse the font with TrueTypeParser
            TrueTypeParser parser;
            try
            {
                parser = new TrueTypeParser(fontData);
                parser.Parse();
            }
            catch
            {
                continue; // Skip fonts that fail to parse
            }

            // Map character codes to glyph IDs
            var glyphIds = new HashSet<int> { 0 }; // Always include .notdef
            foreach (var charCode in charCodes)
            {
                if (parser.CMap.TryGetValue(charCode, out var gid) && gid > 0)
                    glyphIds.Add(gid);
            }

            // Perform subsetting
            TrueTypeSubsetter subsetter;
            byte[] subsetData;
            Dictionary<int, int> glyphMap;
            try
            {
                subsetter = new TrueTypeSubsetter(fontData, parser);
                (subsetData, glyphMap) = subsetter.Subset(charCodes);
            }
            catch
            {
                continue; // Skip fonts that fail to subset
            }

            // Only replace if the subset is actually smaller
            if (subsetData.Length >= fontData.Length) continue;

            // Replace the font stream data
            fontFileStream.ReplaceData(subsetData);
            // Remove filter since we're writing raw data
            fontFileStream.Dict.Remove("Filter");
            fontFileStream.Dict.Remove("DecodeParms");
            fontFileStream.Dict.Set("Length", new PdfInteger(subsetData.Length));

            // Update Length1 in the font descriptor
            descriptor.Set("Length1", new PdfInteger(subsetData.Length));

            // Update Widths array based on used character range
            UpdateWidths(fontDict, charCodes, parser, reader);

            // Add subset prefix to BaseFont name
            AddSubsetPrefix(fontDict, descriptor, baseFont);
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
        Dictionary<string, (PdfDictionary dict, int objNum)> fontMap,
        Dictionary<int, HashSet<int>> usedCodes)
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
                            CollectCodesFromString(s.Value, currentFontKey, fontMap, usedCodes);
                            break;

                        case "TJ" when operands.Count >= 1 && operands[0] is PdfArray arr:
                            foreach (var item in arr)
                            {
                                if (item is PdfString ts)
                                    CollectCodesFromString(ts.Value, currentFontKey, fontMap, usedCodes);
                            }
                            break;

                        case "'" when operands.Count >= 1 && operands[0] is PdfString qs:
                            CollectCodesFromString(qs.Value, currentFontKey, fontMap, usedCodes);
                            break;

                        case "\"" when operands.Count >= 3 && operands[2] is PdfString dqs:
                            CollectCodesFromString(dqs.Value, currentFontKey, fontMap, usedCodes);
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
        Dictionary<string, (PdfDictionary dict, int objNum)> fontMap,
        Dictionary<int, HashSet<int>> usedCodes)
    {
        if (currentFontKey is null) return;
        if (!fontMap.TryGetValue(currentFontKey, out var fontInfo)) return;

        if (!usedCodes.TryGetValue(fontInfo.objNum, out var codes))
        {
            codes = new HashSet<int>();
            usedCodes[fontInfo.objNum] = codes;
        }

        foreach (var b in bytes)
            codes.Add(b);
    }

    /// <summary>
    /// Get all content streams from a page dictionary.
    /// </summary>
    private static List<byte[]> GetContentStreams(PdfDictionary pageDict, PdfReader reader)
    {
        var result = new List<byte[]>();
        var contents = reader.Resolve(pageDict.Get("Contents"));

        switch (contents)
        {
            case PdfStream stream:
                result.Add(reader.DecodeStream(stream));
                break;
            case PdfArray arr:
                foreach (var item in arr)
                {
                    var s = reader.ResolveStream(item);
                    if (s is not null)
                        result.Add(reader.DecodeStream(s));
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
