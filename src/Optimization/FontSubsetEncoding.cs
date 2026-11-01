using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Optimization;

internal static partial class FontSubsetter
{
    private const int SingleByteCodes = 256;

    /// <summary>The glyph name each code of a simple font names through its /Encoding: the base
    /// encoding it states (WinAnsi when it states none) under its /Differences.</summary>
    internal static string?[] EncodingGlyphNames(PdfDictionary font, PdfReader reader)
    {
        var names = new string?[SingleByteCodes];
        var encoding = reader.Resolve(font.Get("Encoding"));
        var baseName = encoding is PdfName n ? n.Value
            : (encoding as PdfDictionary)?.GetName("BaseEncoding") ?? "WinAnsiEncoding";
        for (var code = 0; code < SingleByteCodes; code++)
            names[code] = baseName switch
            {
                "MacRomanEncoding" => PdfEncodings.MacRomanName(code),
                "StandardEncoding" => Type1StandardEncoding.GetName(code),
                _ => PdfEncodings.WinAnsiName(code),
            };
        if (encoding is PdfDictionary dict && reader.Resolve(dict.Get("Differences")) is PdfArray differences)
        {
            var code = 0;
            foreach (var item in differences)
                switch (reader.Resolve(item))
                {
                    case PdfInteger start: code = (int)start.Value; break;
                    case PdfName glyph when code is >= 0 and < SingleByteCodes: names[code++] = glyph.Value; break;
                }
        }
        return names;
    }

    // A symbol (3,0) cmap keys the glyph of byte code c at this base plus c.
    private const int SymbolCmapBase = 0xF000;

    /// <summary>The key the program's cmap holds the glyph of a code under: the character the
    /// font's encoding names for it, else the code in a symbol cmap's range, else the code.</summary>
    private static int ProgramKey(TrueTypeParser parser, string?[] names, int code)
    {
        if (EncodedCharacter(names, code) is { } character && parser.CMap.ContainsKey(character)) return character;
        return parser.CMap.ContainsKey(SymbolCmapBase | code) ? SymbolCmapBase | code : code;
    }

    /// <summary>The character a code shows through the font's encoding - the Unicode value of
    /// the glyph name it names - or null when the name means no character.</summary>
    private static int? EncodedCharacter(string?[] names, int code) =>
        code is >= 0 and < SingleByteCodes && names[code] is { } name
        && TextAbsorber.GlyphNameToUnicode.TryGetValue(name, out var text) && text.Length > 0
            ? char.ConvertToUtf32(text, 0)
            : null;
}
