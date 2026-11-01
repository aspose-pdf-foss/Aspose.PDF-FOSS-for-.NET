using System.Text;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Text;

public sealed partial class FontEmbedder
{
    /// <summary>The stages of the font dictionary embed: the subset and the descriptor.</summary>
    private static void WriteFontDescriptor(FontDictEmbedState fe)
    {
        var widths = new PdfArray();
        // The array is indexed by CODE, so each code must resolve to the character its
        // encoding names (WinAnsi through CP1252, a /Differences name through the glyph
        // list) before the cmap lookup — the 0x80..0x9F block (€, curly quotes, dashes, ™ …)
        // otherwise reads control codepoints and lands on the notdef advance. A control
        // code /Differences names a glyph for is shown too, and has its width.
        var characters = EncodedCharacters(fe);
        var first = Array.FindIndex(characters, c => c is not null);
        if (first < 0 || first > FirstPrintableCode) first = FirstPrintableCode;
        for (var c = first; c <= LastCode; c++)
            widths.Add(new PdfInteger((int)(fe.parser.GetCharWidth(
                characters[c] ?? Cp1252.GetString(new[] { (byte)c })[0]) * fe.scale)));
        fe.fontDict.Set("FirstChar", new PdfInteger(first));
        fe.fontDict.Set("LastChar", new PdfInteger(LastCode));
        fe.fontDict.Set("Widths", widths);
    }

    // Two widths within this many thousandths of an em are the same width.
    private const double WidthTolerance = 1;

    // The single-byte codes a simple font's /Widths covers when it is written for the
    // face: from the space to the last code.
    private const int FirstPrintableCode = 32;
    private const int LastCode = 255;

    /// <summary>The character each single-byte code shows through the dictionary's /Encoding
    /// (WinAnsi when it states none); null where the code names no character.</summary>
    private static int?[] EncodedCharacters(FontDictEmbedState fe)
    {
        var names = Optimization.FontSubsetter.EncodingGlyphNames(fe.fontDict, fe.document.Reader);
        var characters = new int?[names.Length];
        for (var code = 0; code < names.Length; code++)
            if (names[code] is { } name && TextAbsorber.GlyphNameToUnicode.TryGetValue(name, out var text) && text.Length > 0)
                characters[code] = char.ConvertToUtf32(text, 0);
        return characters;
    }

    /// <summary>The stages of the font dictionary embed: the subset and the descriptor.</summary>
    private static void SubsetEmbeddedFont(FontDictEmbedState fe)
    {
        var winAnsiCodes = new HashSet<int>();
        for (var b = 32; b <= 255; b++)
            winAnsiCodes.Add(Cp1252.GetString(new[] { (byte)b })[0]);
        // A /Differences name may name a glyph outside the WinAnsi set (a ligature, a bullet).
        foreach (var character in EncodedCharacters(fe))
            if (character is { } named) winAnsiCodes.Add(named);
        // Widths the dictionary cannot be read for leave the program's own advances in place.
        // A dictionary that states widths says which codes it shows: the program carries the
        // glyphs of those, not the whole WinAnsi set - a face shared by fonts that state
        // different widths travels once per font, so each copy is kept to what it draws.
        Dictionary<int, int>? advances;
        var stated = new HashSet<int>();
        try { advances = fe.document.ReconcileEmbeddedWidths ? StatedAdvances(fe, stated) : null; }
        catch { advances = null; }
        var characters = advances is { Count: > 0 } && stated.Count > 0 ? stated : winAnsiCodes;
        var (subsetData, _) = new TrueTypeSubsetter(fe.ttfData, fe.parser).Subset(characters, advances);
        if (subsetData.Length > 0 && subsetData.Length < fe.parser.FontData.Length)
        {
            fe.fontProgram = subsetData;
            fe.embedName = GenerateSubsetTag() + "+" + fe.baseFontName;
        }
    }

    /// <summary>
    /// The advances the dictionary already states, per glyph of the program that will carry
    /// them. A source's /Widths is kept (the page was laid out against it), so the substitute
    /// program is made to agree with it: each code resolves the way a reader resolves it - its
    /// /Encoding names a glyph, the name a character, the program's cmap a glyph - and that
    /// glyph takes the code's stated width (font units). The characters the encoding names are
    /// added to <paramref name="characters"/>, so the subset keeps their glyphs. Null when the
    /// dictionary states no widths (the program's own are written then), or when the face's
    /// own advances already agree with them.
    /// </summary>
    private static Dictionary<int, int>? StatedAdvances(FontDictEmbedState fe, HashSet<int> characters)
    {
        var reader = fe.document.Reader;
        if (reader.Resolve(fe.fontDict.Get("Widths")) is not PdfArray widths) return null;
        var first = (reader.Resolve(fe.fontDict.Get("FirstChar")) as PdfInteger)?.Value ?? 0;

        var encoding = reader.Resolve(fe.fontDict.Get("Encoding"));
        var baseEncoding = encoding is PdfName n ? n.Value
            : (encoding as PdfDictionary)?.GetName("BaseEncoding") ?? "WinAnsiEncoding";
        var differences = new Dictionary<int, string>();
        if (encoding is PdfDictionary dict && reader.Resolve(dict.Get("Differences")) is PdfArray diffs)
        {
            var code = 0;
            foreach (var item in diffs)
                switch (reader.Resolve(item))
                {
                    case PdfInteger start: code = (int)start.Value; break;
                    case PdfName glyph: differences[code++] = glyph.Value; break;
                }
        }

        var advances = new Dictionary<int, int>();
        var reconciled = false;
        var unitsPerThousand = fe.parser.UnitsPerEm / 1000.0;
        for (var i = 0; i < widths.Count; i++)
        {
            var code = (int)first + i;
            var width = reader.Resolve(widths[i]) switch
            {
                PdfInteger w => (double)w.Value,
                PdfReal w => w.Value,
                _ => 0,
            };
            if (width <= 0) continue;
            var name = differences.TryGetValue(code, out var d) ? d
                : baseEncoding switch
                {
                    "MacRomanEncoding" => PdfEncodings.MacRomanName(code),
                    "StandardEncoding" => Type1StandardEncoding.GetName(code),
                    _ => PdfEncodings.WinAnsiName(code),
                };
            if (name is null || !TextAbsorber.GlyphNameToUnicode.TryGetValue(name, out var text) || text.Length == 0)
                continue;
            var character = char.ConvertToUtf32(text, 0);
            characters.Add(character);
            if (fe.parser.CMap.TryGetValue(character, out var gid) && gid > 0 && !advances.ContainsKey(gid))
            {
                advances[gid] = (int)Math.Round(width * unitsPerThousand);
                // The face's own advance, in thousandths: where it differs from the width the
                // file states, the embedded program had to be made to agree.
                var own = gid < fe.parser.GlyphWidths.Length ? fe.parser.GlyphWidths[gid] / unitsPerThousand : width;
                if (Math.Abs(own - width) > WidthTolerance) reconciled = true;
            }
        }
        // A face that already agrees keeps its own program, shared with every font that uses it.
        if (!reconciled) return null;
        fe.document.NoteReconciledWidths(fe.fontDict);
        return advances;
    }
}
