using Aspose.Pdf.Core;

namespace Aspose.Pdf.Text;

public sealed partial class FontEmbedder
{
    private const string DingbatsFontName = "ZapfDingbats";
    private const string SymbolFontName = "Symbol";
    private const int FirstSymbolicCode = 32;
    private const int LastSymbolicCode = 254;
    // FontDescriptor /Flags: bit 3, the font's glyphs are outside the standard Latin set.
    private const int SymbolicFontFlag = 1 << 2;
    // The cmap subtables a symbolic face keys its glyphs by its codes in: Windows symbol, Macintosh.
    private static readonly (int Platform, int Encoding) WindowsSymbolSubtable = (3, 0);
    private static readonly (int Platform, int Encoding) MacintoshSubtable = (1, 0);

    /// <summary>
    /// Embed ZapfDingbats from the library's own dingbat face, whatever the host has. Its codes
    /// name dingbats, not letters, so no text face can stand in (a check box's code "4" would
    /// print a digit) - nor a host's own ZapfDingbats face, which macOS ships keyed by Unicode
    /// and which embedded as text did exactly that. The library's face keys the dingbats by
    /// their characters. Returns false (the dictionary untouched) when the face is unavailable.
    /// </summary>
    internal static bool EmbedDingbatsIntoFontDict(Document document, PdfDictionary fontDict)
    {
        var program = FontRepository.DjVuDingbatsProgram;
        if (program is null or { Length: 0 }) return false;
        var parser = new TrueTypeParser(program);
        parser.Parse();
        return EmbedSymbolicFace(document, fontDict, DingbatsFontName, program, parser);
    }

    /// <summary>
    /// Embed Symbol from the host's Symbol face as the symbolic font it is. Its codes name Greek
    /// letters and signs, not the letters a text encoding reads them as: embedded as text, its
    /// alpha, beta, gamma read "abg" - and on macOS, whose face keys its glyphs by Unicode, lost
    /// their glyphs too. Its glyphs are found as a reader finds a symbolic face's, by code in its
    /// (3,0) table at U+F000 + code or else in its (1,0) table. Returns false (the dictionary
    /// untouched) when the face has neither.
    /// </summary>
    internal static bool EmbedSymbolIntoFontDict(Document document, PdfDictionary fontDict, byte[] hostFace)
    {
        var subtables = TrueTypeAdvances.CmapSubtables(hostFace);
        var parser = new TrueTypeParser(hostFace);
        if (subtables.Contains(WindowsSymbolSubtable)) parser.PreferSubtable = WindowsSymbolSubtable;
        else if (subtables.Contains(MacintoshSubtable)) parser.PreferSubtable = MacintoshSubtable;
        else return false;
        parser.Parse();
        return EmbedSymbolicFace(document, fontDict, SymbolFontName, hostFace, parser);
    }

    /// <summary>
    /// Embed a standard symbol face's program as a symbolic TrueType whose (3,0) cmap keys each
    /// code at 0xF000 + code, every glyph advancing by the width the font states for its code, so
    /// it keeps the place the source gave it, and with a ToUnicode map naming each code's character.
    /// ZapfDingbats, the library's face, finds its glyphs by the dingbats' characters and states the
    /// standard widths and the face's map, whatever the source states. Symbol, a host's face, finds
    /// them by the codes themselves in the face's symbol table and keeps what the source states: its
    /// /Widths, /FirstChar and /LastChar (the page was laid out against them; the standard widths
    /// stand for the codes it states no width for, and are stated where it gives none), and the
    /// entries of its ToUnicode map (a formula's minus, a brace's pieces), laid over the face's map,
    /// which names the codes the source's map leaves out. Returns false (the dictionary untouched)
    /// when the program carries none of the glyphs.
    /// </summary>
    private static bool EmbedSymbolicFace(Document document, PdfDictionary fontDict, string faceName,
        byte[] program, TrueTypeParser parser)
    {
        var symbol = faceName == SymbolFontName;
        var stated = symbol ? StatedWidths(document, fontDict) : null;
        var unitsPerThousand = parser.UnitsPerEm / 1000.0;

        var codeGlyphs = new Dictionary<int, int>();
        var advances = new Dictionary<int, int>();
        var unicodeOf = new Dictionary<int, string>();
        var widths = stated is null ? new PdfArray() : null;
        var reconciled = false;
        for (var code = FirstSymbolicCode; code <= LastSymbolicCode; code++)
        {
            var width = (double)Standard14Fonts.GetWidth(faceName, code);
            widths?.Add(new PdfInteger((int)width));
            var ch = symbol ? TextAbsorber.SymbolCharacter((byte)code) : TextAbsorber.DingbatCharacter((byte)code);
            // A code the face's encoding leaves undefined names no character, so the map leaves it out rather
            // than naming U+0000; the face may still draw it (macOS's Symbol face draws 0xF0).
            if (ch != TextAbsorber.UndefinedCharacter) unicodeOf[code] = ch.ToString();
            int gid;
            var found = symbol
                ? parser.CMap.TryGetValue(GlyphOutlineParser.SymbolCodeBase + code, out gid) || parser.CMap.TryGetValue(code, out gid)
                : parser.CMap.TryGetValue(ch, out gid);
            if (!found || gid <= 0) continue;
            codeGlyphs[code] = gid;
            if (stated is not null && stated.TryGetValue(code, out var own))
            {
                // A face whose own advance differs from the stated width is made to agree, and the
                // disagreement noted, as the text path notes it.
                if (gid < parser.GlyphWidths.Length && Math.Abs(parser.GlyphWidths[gid] / unitsPerThousand - own) > WidthTolerance)
                    reconciled = true;
                width = own;
            }
            advances[gid] = (int)Math.Round(width * unitsPerThousand);
        }
        if (codeGlyphs.Count == 0) return false;
        // A source map naming some codes only would leave the others mapped nowhere: its entries win
        // over the face's, which name the rest. A simple font shows single-byte codes only.
        if (symbol && TextAbsorber.ParseToUnicodeFromDict(fontDict, document.Reader) is { } sourceMap)
            foreach (var (code, text) in sourceMap.Where(p => p.Key <= byte.MaxValue)) unicodeOf[code] = text;
        if (reconciled) document.NoteReconciledWidths(fontDict);

        var subset = new TrueTypeSubsetter(program, parser).SubsetSymbolic(codeGlyphs, advances);
        var fontFileObjNum = document.AllocateObjectNumber();
        var fontFileDict = new PdfDictionary();
        fontFileDict.Set("Length1", new PdfInteger(subset.Length));
        document.AddNewObject(fontFileObjNum, new PdfStream(fontFileDict, subset));

        var name = GenerateSubsetTag() + "+" + faceName;
        var scale = 1000.0 / parser.UnitsPerEm;
        var bbox = new PdfArray();
        foreach (var v in parser.BBox) bbox.Add(new PdfInteger((int)(v * scale)));
        var descriptor = new PdfDictionary();
        descriptor.Set("Type", new PdfName("FontDescriptor"));
        descriptor.Set("FontName", new PdfName(name));
        descriptor.Set("Flags", new PdfInteger(SymbolicFontFlag));
        descriptor.Set("ItalicAngle", new PdfInteger(0));
        descriptor.Set("FontBBox", bbox);
        descriptor.Set("Ascent", new PdfInteger((int)(parser.Ascent * scale)));
        descriptor.Set("Descent", new PdfInteger((int)(parser.Descent * scale)));
        descriptor.Set("CapHeight", new PdfInteger((int)(parser.CapHeight * scale)));
        descriptor.Set("StemV", new PdfInteger(85));
        descriptor.Set("FontFile2", new PdfIndirectRef(fontFileObjNum, 0));

        // Readable before the save: a conversion reads the font's text after embedding it (its
        // auto-tagger, its PDF/UA check), and a symbolic font's codes name their characters here alone.
        var toUnicodeObjNum = document.AllocateObjectNumber();
        document.AddNewObject(toUnicodeObjNum, new PdfStream(new PdfDictionary(), Document.ToUnicodeCMap(unicodeOf, byteWidth: 1)),
            registerOverlay: true);
        // The source's own map, replaced here, is written only while something still names it (a level A
        // companion entry, another font): the save then leaves out what nothing reaches.
        if (fontDict.Get("ToUnicode") is PdfIndirectRef) document.Reader.MayHaveOrphansOnSave = true;
        fontDict.Set("ToUnicode", new PdfIndirectRef(toUnicodeObjNum, 0));

        // A symbolic TrueType reads its codes through the program's cmap: no /Encoding.
        foreach (var key in new[] { "Encoding", "FontFile", "FontFile3" }) fontDict.Remove(key);
        fontDict.Set("Type", new PdfName("Font"));
        fontDict.Set("Subtype", new PdfName("TrueType"));
        fontDict.Set("BaseFont", new PdfName(name));
        if (widths is not null)
        {
            fontDict.Set("FirstChar", new PdfInteger(FirstSymbolicCode));
            fontDict.Set("LastChar", new PdfInteger(LastSymbolicCode));
            fontDict.Set("Widths", widths);
        }
        fontDict.Set("FontDescriptor", descriptor);
        return true;
    }

    /// <summary>The widths a font's /Widths states, by code: a positive one only, as a reader takes
    /// them - a code stated as zero takes the standard width. Null when the font has no /Widths.</summary>
    private static Dictionary<int, double>? StatedWidths(Document document, PdfDictionary fontDict)
    {
        var reader = document.Reader;
        if (reader.Resolve(fontDict.Get("Widths")) is not PdfArray array) return null;
        var first = (int)((reader.Resolve(fontDict.Get("FirstChar")) as PdfInteger)?.Value ?? 0);
        var stated = new Dictionary<int, double>();
        for (var i = 0; i < array.Count; i++)
        {
            var width = reader.Resolve(array[i]) switch
            {
                PdfInteger w => w.Value,
                PdfReal w => w.Value,
                _ => 0.0,
            };
            if (width > 0) stated[first + i] = width;
        }
        return stated;
    }
}
