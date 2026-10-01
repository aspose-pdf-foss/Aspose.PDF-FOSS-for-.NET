using Aspose.Pdf.Core;

namespace Aspose.Pdf.Text;

public sealed partial class FontEmbedder
{
    private const string DingbatsFontName = "ZapfDingbats";
    private const int FirstDingbatCode = 32;
    private const int LastDingbatCode = 254;
    // FontDescriptor /Flags: bit 3, the font's glyphs are outside the standard Latin set.
    private const int SymbolicFontFlag = 1 << 2;

    /// <summary>
    /// Embed ZapfDingbats where the host has no face for it. Its codes name dingbats, not
    /// letters, so no text face can stand in (a check box's code "4" would print a digit).
    /// The dingbats the codes name are taken from the library's own dingbat face and embedded
    /// as a symbolic TrueType: its (3,0) cmap keys each code at 0xF000 + code, its advances
    /// are the ZapfDingbats metrics that /Widths states (so every glyph keeps the place the
    /// source gave it), and a ToUnicode map says which dingbat each code is.
    /// Returns false (the dictionary untouched) when the face is unavailable.
    /// </summary>
    internal static bool EmbedDingbatsIntoFontDict(Document document, PdfDictionary fontDict)
    {
        var program = FontRepository.DjVuDingbatsProgram;
        if (program is null or { Length: 0 }) return false;
        var parser = new TrueTypeParser(program);
        parser.Parse();
        var unitsPerThousand = parser.UnitsPerEm / 1000.0;

        var codeGlyphs = new Dictionary<int, int>();
        var advances = new Dictionary<int, int>();
        var unicodeOf = new Dictionary<int, char>();
        var widths = new PdfArray();
        for (var code = FirstDingbatCode; code <= LastDingbatCode; code++)
        {
            var width = Standard14Fonts.GetWidth(DingbatsFontName, code);
            widths.Add(new PdfInteger(width));
            var ch = TextAbsorber.DingbatCharacter((byte)code);
            if (ch == ' ' && code != FirstDingbatCode) continue; // a code ZapfDingbats leaves undefined
            unicodeOf[code] = ch;
            if (!parser.CMap.TryGetValue(ch, out var gid) || gid <= 0) continue;
            codeGlyphs[code] = gid;
            advances[gid] = (int)Math.Round(width * unitsPerThousand);
        }
        if (codeGlyphs.Count == 0) return false;

        var subset = new TrueTypeSubsetter(program, parser).SubsetSymbolic(codeGlyphs, advances);
        var fontFileObjNum = document.AllocateObjectNumber();
        var fontFileDict = new PdfDictionary();
        fontFileDict.Set("Length1", new PdfInteger(subset.Length));
        document.AddNewObject(fontFileObjNum, new PdfStream(fontFileDict, subset));

        var name = GenerateSubsetTag() + "+" + DingbatsFontName;
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

        var toUnicodeObjNum = document.AllocateObjectNumber();
        document.AddNewObject(toUnicodeObjNum, new PdfStream(new PdfDictionary(), BuildToUnicodeCMap(unicodeOf)));

        // A symbolic TrueType reads its codes through the program's cmap: no /Encoding.
        foreach (var key in new[] { "Encoding", "FontFile", "FontFile3" }) fontDict.Remove(key);
        fontDict.Set("Type", new PdfName("Font"));
        fontDict.Set("Subtype", new PdfName("TrueType"));
        fontDict.Set("BaseFont", new PdfName(name));
        fontDict.Set("FirstChar", new PdfInteger(FirstDingbatCode));
        fontDict.Set("LastChar", new PdfInteger(LastDingbatCode));
        fontDict.Set("Widths", widths);
        fontDict.Set("FontDescriptor", descriptor);
        fontDict.Set("ToUnicode", new PdfIndirectRef(toUnicodeObjNum, 0));
        return true;
    }
}
