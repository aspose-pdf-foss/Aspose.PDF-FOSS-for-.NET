using System.Text;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Text;

/// <summary>
/// Embeds TrueType fonts into PDF documents for custom text rendering.
/// Creates the font dictionary, font descriptor, and font file stream.
/// </summary>
public sealed class FontEmbedder
{
    private readonly Document _document;
    private readonly TrueTypeParser _parser;
    private readonly string _resourceName;

    private FontEmbedder(Document document, TrueTypeParser parser, string resourceName)
    {
        _document = document;
        _parser = parser;
        _resourceName = resourceName;
    }

    /// <summary>The resource name to use in content streams (e.g., "F1").</summary>
    public string ResourceName => _resourceName;

    /// <summary>The PostScript name of the embedded font.</summary>
    public string PostScriptName => _parser.PostScriptName;

    /// <summary>
    /// Embed a TrueType font from file bytes into a document.
    /// Returns a FontEmbedder with the resource name for use in content streams.
    /// </summary>
    public static FontEmbedder Embed(Document document, byte[] ttfData, string resourceName = "F1",
        string? subsetBaseName = null)
    {
        var parser = new TrueTypeParser(ttfData);
        parser.Parse();

        var embedder = new FontEmbedder(document, parser, resourceName);
        if (subsetBaseName is not null)
        {
            // Embed the full program but present it under a subset-tagged
            // BaseFont (e.g. "ABCDEF+CourierNew") so the font reads back as a
            // subset. (True glyph subsetting is handled by EmbedSubset.)
            embedder._isSubset = true;
            embedder._originalPostScriptName = subsetBaseName;
        }
        embedder.CreateFontObjects();
        return embedder;
    }

    /// <summary>
    /// Embed a TrueType font from a file path.
    /// </summary>
    public static FontEmbedder EmbedFromFile(Document document, string path, string resourceName = "F1")
    {
        var data = File.ReadAllBytes(path);
        return Embed(document, data, resourceName);
    }

    /// <summary>
    /// Embed a subset of a TrueType font containing only the glyphs needed for the given text.
    /// This produces significantly smaller PDF files compared to full embedding.
    /// The subset font name is prefixed with a 6-letter tag per PDF spec §9.6.4.
    /// </summary>
    public static FontEmbedder EmbedSubset(Document document, byte[] ttfData, string text,
        string resourceName = "F1")
    {
        var parser = new TrueTypeParser(ttfData);
        parser.Parse();

        // Collect unique character codes from the text
        var charCodes = new HashSet<int>();
        foreach (var c in text)
            charCodes.Add(c);

        var subsetter = new TrueTypeSubsetter(ttfData, parser);
        var (subsetData, _) = subsetter.Subset(charCodes);

        // Create a new parser for the subset font
        var subsetParser = new TrueTypeParser(subsetData);
        subsetParser.Parse();

        var embedder = new FontEmbedder(document, subsetParser, resourceName);
        embedder._isSubset = true;
        embedder._originalPostScriptName = parser.PostScriptName;
        embedder._charCodes = charCodes;
        embedder.CreateFontObjects();
        return embedder;
    }

    /// <summary>
    /// Embed a subset of a TrueType font from a file path.
    /// </summary>
    public static FontEmbedder EmbedSubsetFromFile(Document document, string path, string text,
        string resourceName = "F1")
    {
        var data = File.ReadAllBytes(path);
        return EmbedSubset(document, data, text, resourceName);
    }

    /// <summary>
    /// Add the font resource to a page's resource dictionary.
    /// Call this for each page that uses the font.
    /// </summary>
    public void AddToPage(Page page) => AddToResources(page.Dict, page.Reader);

    /// <summary>Add the font resource to any container dictionary that carries a
    /// /Resources entry (a page dictionary OR a Form XObject stream dictionary).
    /// Resolves indirect /Resources and /Font so the originals aren't replaced.</summary>
    internal void AddToResources(PdfDictionary containerDict, Aspose.Pdf.IO.PdfReader reader)
    {
        var resources = containerDict.Get("Resources") as PdfDictionary
            ?? reader.ResolveDict(containerDict.Get("Resources"));
        if (resources is null)
        {
            resources = new PdfDictionary();
            containerDict.Set("Resources", resources);
        }

        var fontDict = resources.Get("Font") as PdfDictionary
            ?? reader.ResolveDict(resources.Get("Font"));
        if (fontDict is null)
        {
            fontDict = new PdfDictionary();
            resources.Set("Font", fontDict);
        }

        fontDict.Set(_resourceName, new PdfIndirectRef(_fontObjNum, 0));
    }

    private int _fontObjNum;

    /// <summary>The font dictionary this embedder created, and the object numbers of the
    /// descriptor and font-program streams hanging off it. Exposed so a caller that later
    /// cancels the embedding can rewrite the dictionary and free the stranded objects.</summary>
    internal PdfDictionary? FontDict { get; private set; }
    internal int DescriptorObjNum { get; private set; }
    internal int FontFileObjNum { get; private set; }

    private bool _isSubset;
    private string? _originalPostScriptName;
    private HashSet<int>? _charCodes;

    /// <summary>Rewrite an existing simple-font dictionary in place so it becomes an
    /// embedded WinAnsi TrueType backed by <paramref name="ttfData"/>, presented under
    /// <paramref name="baseFontName"/>. Used by PDF/A conversion to embed a font that was
    /// referenced but not embedded, without changing the resource reference that points at
    /// this dictionary.</summary>
    internal static void EmbedIntoFontDict(Document document, byte[] ttfData,
        PdfDictionary fontDict, string baseFontName,
        Dictionary<string, (int objNum, string embedName)>? fontFileCache = null,
        bool subset = true)
    {
        var parser = new TrueTypeParser(ttfData);
        parser.Parse();
        var scale = 1000.0 / parser.UnitsPerEm;

        // Embedding the whole system TTF (often ~1 MB) for every referenced font bloats the
        // output enormously — a PDF/A conversion of a small file can balloon to tens of MB.
        // Reduce the font program to just the glyphs reachable through this dictionary's
        // WinAnsi 32..255 range, which is all a simple TrueType font can address. When the
        // subset is a proper reduction it is presented under a 6-letter subset tag per
        // PDF 32000-1 §9.6.4. Falls back to the full program if subsetting can't apply
        // (e.g. CFF-based or loca-less fonts).
        var fontProgram = parser.FontData;
        var embedName = baseFontName;
        // A caller that explicitly cleared IsSubset wants the full program embedded under
        // the bare name (no subset tag). Skip the WinAnsi reduction then.
        if (subset)
        try
        {
            var winAnsiCodes = new HashSet<int>();
            for (var b = 32; b <= 255; b++)
                winAnsiCodes.Add(Cp1252.GetString(new[] { (byte)b })[0]);
            var (subsetData, _) = new TrueTypeSubsetter(ttfData, parser).Subset(winAnsiCodes);
            if (subsetData.Length > 0 && subsetData.Length < parser.FontData.Length)
            {
                fontProgram = subsetData;
                embedName = GenerateSubsetTag() + "+" + baseFontName;
            }
        }
        catch { /* keep the full program if subsetting throws */ }

        // The same system face is typically referenced by many font dictionaries
        // (e.g. dozens of "Arial"/"ArialMT" entries across a converted document). Embedding
        // an identical program once and sharing the FontFile2 object keeps the output small.
        int fontFileObjNum;
        var cacheKey = fontFileCache is null
            ? null
            : Convert.ToHexString(Security.ShaDigest.Sha256(fontProgram));
        if (cacheKey is not null && fontFileCache!.TryGetValue(cacheKey, out var cached))
        {
            fontFileObjNum = cached.objNum;
            embedName = cached.embedName; // keep the subset tag consistent with the shared program
        }
        else
        {
            fontFileObjNum = document.AllocateObjectNumber();
            var fontFileDict = new PdfDictionary();
            fontFileDict.Set("Length1", new PdfInteger(fontProgram.Length));
            document.AddNewObject(fontFileObjNum, new PdfStream(fontFileDict, fontProgram));
            if (cacheKey is not null)
                fontFileCache![cacheKey] = (fontFileObjNum, embedName);
        }

        var descriptor = new PdfDictionary();
        descriptor.Set("Type", new PdfName("FontDescriptor"));
        descriptor.Set("FontName", new PdfName(embedName));
        descriptor.Set("Flags", new PdfInteger(parser.GetPdfFlags()));
        descriptor.Set("ItalicAngle", new PdfReal(parser.ItalicAngle));
        var bbox = parser.BBox;
        var bboxArray = new PdfArray();
        for (var i = 0; i < 4; i++) bboxArray.Add(new PdfInteger((int)(bbox[i] * scale)));
        descriptor.Set("FontBBox", bboxArray);
        descriptor.Set("Ascent", new PdfInteger((int)(parser.Ascent * scale)));
        descriptor.Set("Descent", new PdfInteger((int)(parser.Descent * scale)));
        descriptor.Set("CapHeight", new PdfInteger((int)(parser.CapHeight * scale)));
        descriptor.Set("StemV", new PdfInteger(85));
        descriptor.Set("FontFile2", new PdfIndirectRef(fontFileObjNum, 0));

        // Drop any prior simple-font entries that no longer apply, then write the
        // embedded-TrueType shape over the existing dictionary. The descriptor is held
        // inline (direct) so an in-memory re-read of the font sees the FontFile2 entry
        // without resolving a not-yet-written indirect object; the font program stream
        // itself is indirect (it is serialised at save time).
        fontDict.Remove("FontFile");
        fontDict.Remove("FontFile3");
        fontDict.Set("Type", new PdfName("Font"));
        fontDict.Set("Subtype", new PdfName("TrueType"));
        fontDict.Set("BaseFont", new PdfName(embedName));
        // Preserve the source's /Widths (and the /FirstChar-/LastChar range and /Encoding)
        // when the dictionary already carries them: the page content was laid out against
        // those advances, so replacing them with the substitute face's own metrics shifts
        // every glyph on a text-showing run by a small, accumulating amount — the same text
        // then renders a fraction of a point off where the un-embedded source drew it. Only
        // synthesise a WinAnsi 32..255 width array (and encoding) when the source had none.
        if (fontDict.Get("Widths") is null)
        {
            var widths = new PdfArray();
            // The array is indexed by WinAnsi CODE, so each code must resolve
            // through CP1252 to its character before the cmap lookup — the
            // 0x80..0x9F block (€, curly quotes, dashes, ™ …) otherwise reads
            // control codepoints and lands on the notdef advance.
            for (var c = 32; c <= 255; c++)
                widths.Add(new PdfInteger((int)(
                    parser.GetCharWidth(Cp1252.GetString(new[] { (byte)c })[0]) * scale)));
            fontDict.Set("FirstChar", new PdfInteger(32));
            fontDict.Set("LastChar", new PdfInteger(255));
            fontDict.Set("Widths", widths);
        }
        if (fontDict.Get("Encoding") is null)
            fontDict.Set("Encoding", new PdfName("WinAnsiEncoding"));
        fontDict.Set("FontDescriptor", descriptor);
    }

    /// <summary>Embed <paramref name="ttfData"/> as the /FontFile2 of an EXISTING
    /// composite (Type0/CID) font whose descendant lacks a program. The dictionary's
    /// /W widths, /Encoding CMap and /CIDSystemInfo are left untouched — the content
    /// stream was authored against them. For an Identity encoding the CIDs are the
    /// original face's glyph ids, so only the same-named real face may be embedded
    /// (the caller guarantees that); for a predefined national CMap a /CIDToGIDMap
    /// is synthesised via CID→Unicode (Adobe tables) → Unicode→GID (the face's cmap).</summary>
    internal static void EmbedIntoCidFontDict(Document document, byte[] ttfData,
        PdfDictionary type0Dict, PdfDictionary cidFontDict,
        Dictionary<string, (int objNum, string embedName)>? fontFileCache = null)
    {
        ttfData = CjkFallbackFont.NormalizeToSfnt(ttfData);
        var parser = new TrueTypeParser(ttfData);
        parser.Parse();
        var scale = 1000.0 / parser.UnitsPerEm;
        var reader = document.Reader;

        // Share one FontFile2 object per distinct program (CJK faces run to many MB).
        int fontFileObjNum;
        var cacheKey = fontFileCache is null
            ? null
            : Convert.ToHexString(Security.ShaDigest.Sha256(ttfData));
        if (cacheKey is not null && fontFileCache!.TryGetValue(cacheKey, out var cached))
            fontFileObjNum = cached.objNum;
        else
        {
            fontFileObjNum = document.AllocateObjectNumber();
            var fontFileDict = new PdfDictionary();
            fontFileDict.Set("Length1", new PdfInteger(ttfData.Length));
            document.AddNewObject(fontFileObjNum, new PdfStream(fontFileDict, ttfData));
            if (cacheKey is not null)
                fontFileCache![cacheKey] = (fontFileObjNum, parser.PostScriptName);
        }

        // Reuse the existing descriptor when present — its metrics match the /W
        // layout the page was set with; only synthesise one when absent.
        var descriptor = reader.ResolveDict(cidFontDict.Get("FontDescriptor"));
        if (descriptor is null)
        {
            descriptor = new PdfDictionary();
            descriptor.Set("Type", new PdfName("FontDescriptor"));
            descriptor.Set("FontName",
                new PdfName(cidFontDict.GetName("BaseFont") ?? parser.PostScriptName));
            descriptor.Set("Flags", new PdfInteger(parser.GetPdfFlags()));
            descriptor.Set("ItalicAngle", new PdfReal(parser.ItalicAngle));
            var bbox = parser.BBox;
            var bboxArray = new PdfArray();
            for (var i = 0; i < 4; i++) bboxArray.Add(new PdfInteger((int)(bbox[i] * scale)));
            descriptor.Set("FontBBox", bboxArray);
            descriptor.Set("Ascent", new PdfInteger((int)(parser.Ascent * scale)));
            descriptor.Set("Descent", new PdfInteger((int)(parser.Descent * scale)));
            descriptor.Set("CapHeight", new PdfInteger((int)(parser.CapHeight * scale)));
            descriptor.Set("StemV", new PdfInteger(85));
            cidFontDict.Set("FontDescriptor", descriptor);
        }
        descriptor.Remove("FontFile");
        descriptor.Remove("FontFile3");
        descriptor.Set("FontFile2", new PdfIndirectRef(fontFileObjNum, 0));

        // A TrueType program makes the descendant a CIDFontType2.
        if (cidFontDict.GetName("Subtype") == "CIDFontType0")
            cidFontDict.Set("Subtype", new PdfName("CIDFontType2"));

        // The content-stream CIDs are registry CIDs whenever CIDSystemInfo names a
        // predefined Adobe ordering (Japan1/GB1/CNS1/Korea1) — even under an Identity
        // encoding, which producers use to reference a registry-subset of a system CJK
        // face. The original face is absent (this is the substitution path), so a
        // CID→GID map through CID→Unicode→(substitute cmap) is required; an Identity
        // map would draw the substitute's glyph N for registry CID N (garbled Latin and
        // kana). Build the national map first, before the Identity fallback.
        var csi = reader.ResolveDict(cidFontDict.Get("CIDSystemInfo"));
        var orderingObj = csi?.Get("Ordering");
        var ordering = orderingObj is PdfString os ? os.ToText()
            : (orderingObj is PdfName on ? on.Value : null);
        var map = BuildCidToGidMap(ordering, parser);
        if (map is not null)
        {
            // The map depends only on (program, ordering) — share one stream across
            // every CID font that embeds the same face (a document repeating one CJK
            // family across pages would otherwise carry N identical ~50 KB maps).
            var mapKey = cacheKey is null ? null : $"cid2gid:{cacheKey}:{ordering}";
            int mapObjNum;
            if (mapKey is not null && fontFileCache!.TryGetValue(mapKey, out var cachedMap))
                mapObjNum = cachedMap.objNum;
            else
            {
                mapObjNum = document.AllocateObjectNumber();
                document.AddNewObject(mapObjNum, new PdfStream(new PdfDictionary(), map));
                if (mapKey is not null)
                    fontFileCache![mapKey] = (mapObjNum, "");
            }
            cidFontDict.Set("CIDToGIDMap", new PdfIndirectRef(mapObjNum, 0));
            return;
        }

        // Identity ordering (or an unknown one): the CIDs are the original face's glyph
        // ids, so a same-named real face embeds against an Identity CIDToGIDMap.
        var encoding = type0Dict.GetName("Encoding");
        if (encoding is null or "Identity-H" or "Identity-V")
        {
            if (cidFontDict.Get("CIDToGIDMap") is null)
                cidFontDict.Set("CIDToGIDMap", new PdfName("Identity"));
        }
    }

    /// <summary>Big-endian ushort[] CID→GID map for a predefined Adobe ordering
    /// (Japan1, GB1, CNS1, Korea1), built through the face's Unicode cmap.
    /// Null when the ordering is unknown.</summary>
    private static byte[]? BuildCidToGidMap(string? ordering, TrueTypeParser parser)
    {
        if (string.IsNullOrEmpty(ordering)) return null;
        var maxCid = AdobeCidTables.MaxCid(ordering);
        if (maxCid <= 0) return null;
        var map = new byte[(maxCid + 1) * 2];
        for (var cid = 0; cid <= maxCid; cid++)
        {
            if (AdobeCidTables.LookupCid(ordering, cid) is not int uni) continue;
            if (!parser.CMap.TryGetValue(uni, out var gid)) continue;
            map[cid * 2] = (byte)(gid >> 8);
            map[cid * 2 + 1] = (byte)gid;
        }
        return map;
    }

    private void CreateFontObjects()
    {
        var scale = 1000.0 / _parser.UnitsPerEm;

        // Generate subset tag per PDF spec §9.6.4 (e.g., "ABCDEF+FontName")
        var fontName = _isSubset
            ? GenerateSubsetTag() + "+" + (_originalPostScriptName ?? _parser.PostScriptName)
            : _parser.PostScriptName;

        // 1. Font file stream (FontFile2 — TrueType program)
        var fontFileObjNum = _document.AllocateObjectNumber();
        var fontFileDict = new PdfDictionary();
        fontFileDict.Set("Length1", new PdfInteger(_parser.FontData.Length));
        var fontFileStream = new PdfStream(fontFileDict, _parser.FontData);
        _document.AddNewObject(fontFileObjNum, fontFileStream);

        // 2. Font descriptor
        var descriptorObjNum = _document.AllocateObjectNumber();
        var descriptor = new PdfDictionary();
        descriptor.Set("Type", new PdfName("FontDescriptor"));
        descriptor.Set("FontName", new PdfName(fontName));
        descriptor.Set("Flags", new PdfInteger(_parser.GetPdfFlags()));
        descriptor.Set("ItalicAngle", new PdfReal(_parser.ItalicAngle));

        var bbox = _parser.BBox;
        var bboxArray = new PdfArray();
        bboxArray.Add(new PdfInteger((int)(bbox[0] * scale)));
        bboxArray.Add(new PdfInteger((int)(bbox[1] * scale)));
        bboxArray.Add(new PdfInteger((int)(bbox[2] * scale)));
        bboxArray.Add(new PdfInteger((int)(bbox[3] * scale)));
        descriptor.Set("FontBBox", bboxArray);

        descriptor.Set("Ascent", new PdfInteger((int)(_parser.Ascent * scale)));
        descriptor.Set("Descent", new PdfInteger((int)(_parser.Descent * scale)));
        descriptor.Set("CapHeight", new PdfInteger((int)(_parser.CapHeight * scale)));
        descriptor.Set("StemV", new PdfInteger(EstimateStemV()));

        descriptor.Set("FontFile2", new PdfIndirectRef(fontFileObjNum, 0));
        _document.AddNewObject(descriptorObjNum, descriptor);

        // 3. Build /Widths array (character codes 32-255 for simple TrueType font)
        var firstChar = 32;
        var lastChar = 255;
        var widths = new PdfArray();
        for (var c = firstChar; c <= lastChar; c++)
        {
            var w = _parser.GetCharWidth(c);
            // The face's advance as it really is, not truncated to a whole 1000th. Verdana's
            // 'A' is 1400/2048 = 683.59375 and its 'P' 603.02734375; truncating each loses
            // up to a full 1000th, and a 35-glyph run then reads back an eighth of a point
            // short of the width it was laid out at. The fractional advances are written
            // as they are (/W carries 683.59375 verbatim).
            widths.Add(new PdfReal(Math.Round(w * scale, 4)));
        }

        // 4. Font dictionary (TrueType simple font)
        _fontObjNum = _document.AllocateObjectNumber();
        var font = new PdfDictionary();
        font.Set("Type", new PdfName("Font"));
        font.Set("Subtype", new PdfName("TrueType"));
        font.Set("BaseFont", new PdfName(fontName));
        font.Set("Encoding", new PdfName("WinAnsiEncoding"));
        font.Set("FirstChar", new PdfInteger(firstChar));
        font.Set("LastChar", new PdfInteger(lastChar));
        font.Set("Widths", widths);
        font.Set("FontDescriptor", new PdfIndirectRef(descriptorObjNum, 0));

        // 5. Add ToUnicode CMap for text extraction support
        if (_isSubset && _charCodes is not null)
        {
            var toUnicodeData = BuildToUnicodeCMap(_charCodes);
            var toUnicodeObjNum = _document.AllocateObjectNumber();
            var toUnicodeStream = new PdfStream(new PdfDictionary(), toUnicodeData);
            _document.AddNewObject(toUnicodeObjNum, toUnicodeStream, registerOverlay: true);
            font.Set("ToUnicode", new PdfIndirectRef(toUnicodeObjNum, 0));
        }

        _document.AddNewObject(_fontObjNum, font, registerOverlay: true);

        FontDict = font;
        DescriptorObjNum = descriptorObjNum;
        FontFileObjNum = fontFileObjNum;
    }

    private static string GenerateSubsetTag()
    {
        // PDF spec requires a 6-letter uppercase tag
        var chars = new char[6];
        var random = Random.Shared;
        for (var i = 0; i < 6; i++)
            chars[i] = (char)('A' + random.Next(26));
        return new string(chars);
    }

    private static byte[] BuildToUnicodeCMap(HashSet<int> charCodes)
    {
        var sb = new StringBuilder();
        sb.Append("/CIDInit /ProcSet findresource begin\n");
        sb.Append("12 dict begin\n");
        sb.Append("begincmap\n");
        sb.Append("/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n");
        sb.Append("/CMapName /Adobe-Identity-UCS def\n");
        sb.Append("/CMapType 2 def\n");
        sb.Append("1 begincodespacerange\n");
        sb.Append("<00> <FF>\n");
        sb.Append("endcodespacerange\n");

        var sorted = charCodes.Where(c => c >= 32 && c <= 255).OrderBy(c => c).ToList();
        if (sorted.Count > 0)
        {
            // Write in groups of up to 100 (CMap spec limit)
            for (var i = 0; i < sorted.Count; i += 100)
            {
                var count = Math.Min(100, sorted.Count - i);
                sb.Append($"{count} beginbfchar\n");
                for (var j = 0; j < count; j++)
                {
                    var c = sorted[i + j];
                    sb.Append($"<{c:X2}> <{c:X4}>\n");
                }
                sb.Append("endbfchar\n");
            }
        }

        sb.Append("endcmap\n");
        sb.Append("CMapName currentdict /CMap defineresource pop\n");
        sb.Append("end\n");
        sb.Append("end\n");

        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private int EstimateStemV()
    {
        // Approximate StemV from weight class (PDF spec doesn't define a formula)
        return _parser.WeightClass switch
        {
            <= 100 => 40,
            <= 200 => 50,
            <= 300 => 60,
            <= 400 => 70,  // Normal
            <= 500 => 80,  // Medium
            <= 600 => 100, // Semi-Bold
            <= 700 => 120, // Bold
            <= 800 => 140, // Extra-Bold
            _ => 160,      // Black
        };
    }
}
