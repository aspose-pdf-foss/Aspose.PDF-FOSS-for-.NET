using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Converters;

public sealed partial class PdfToHtmlConverter
{
    /// <summary>The overflow faces of a font that draws one character through several glyphs
    /// (a heading set in small capitals through remapped codes beside the body's lowercase, or a
    /// recognised scan's face holding several traced shapes of one letter): a served face holds
    /// one glyph per character, so a code whose glyph differs from the one its character got from
    /// the first code naming it is served from the next face. Each such code's level (1 = the
    /// first overflow face), by the order of the encoding's or the CID font's mapping entries,
    /// or null when every character is drawn by one glyph.</summary>
    private static Dictionary<int, int>? OverflowLevels(PdfDictionary font, PdfReader reader)
    {
        if (font.GetName("Subtype") == "Type0") return CidOverflowLevels(font, reader);
        var names = RawDifferencesNames(font, reader);
        var unicodes = SingleCharToUnicode(font, reader);
        if (names is null || unicodes is null) return null;
        Dictionary<int, int>? levels = null;
        var glyphsOf = new Dictionary<int, List<string>>();   // character -> the glyph names drawing it, in order
        foreach (var (code, name) in names)
        {
            if (!unicodes.TryGetValue(code, out var uni)) continue;
            if (!glyphsOf.TryGetValue(uni, out var glyphs)) glyphsOf[uni] = glyphs = new List<string>();
            var level = glyphs.IndexOf(name);
            if (level < 0) { level = glyphs.Count; glyphs.Add(name); }
            if (level > 0) (levels ??= new Dictionary<int, int>())[code] = level;
        }
        return levels;
    }

    /// <summary>The overflow levels of a CID font shown through an Identity encoding: each code
    /// is its own CID and so its own glyph, and the primary face's cmap keeps the first code
    /// mapping a character, so every later code mapping the same character is the next level.
    /// Blank characters draw nothing and need no face.</summary>
    private static Dictionary<int, int>? CidOverflowLevels(PdfDictionary font, PdfReader reader)
    {
        if (font.GetName("Encoding") is not ("Identity-H" or "Identity-V")) return null;
        var unicodes = SingleCharToUnicode(font, reader);
        if (unicodes is null) return null;
        Dictionary<int, int>? levels = null;
        var codesOf = new Dictionary<int, int>();   // character -> how many codes mapped it so far
        foreach (var (code, uni) in unicodes)
        {
            if (uni <= char.MaxValue && char.IsWhiteSpace((char)uni)) continue;
            codesOf.TryGetValue(uni, out var level);
            codesOf[uni] = level + 1;
            if (level > 0) (levels ??= new Dictionary<int, int>())[code] = level;
        }
        return levels;
    }

    /// <summary>The CSS family of an overflow face: the font's family and the face's number.</summary>
    private static string OverflowFamilyName(string family, int level) => family + " " + (level + 1);

    /// <summary>Ship the overflow faces of a bare charstring program beside its primary face, each
    /// mapping its level's characters to the glyphs those codes draw.</summary>
    private static void AddOverflowFaces(PdfDictionary font, PdfReader reader, byte[] cff, string family, List<EmbeddedFont> result)
    {
        var levels = OverflowLevels(font, reader);
        var unicodes = SingleCharToUnicode(font, reader);
        if (levels is null || unicodes is null) return;
        var cidFont = font.GetName("Subtype") == "Type0";
        var names = cidFont ? null : RawDifferencesNames(font, reader);
        var program = cidFont ? Text.CffGlyphSource.TryLoad(cff) : null;
        if (cidFont ? program is null : names is null) return;
        var top = levels.Values.Max();
        for (var level = 1; level <= top; level++)
        {
            byte[]? ttf;
            if (cidFont)
            {
                var uniToGid = new Dictionary<int, int>();
                foreach (var (code, l) in levels)
                {
                    var gid = program!.IsCidKeyed ? program.CidToGid(code) : code;
                    if (l == level && gid > 0) uniToGid[unicodes[code]] = gid;
                }
                // A face of only its level's glyphs: a scan's face can hold dozens of shapes per letter.
                try { ttf = Text.CffToTrueType.BuildSubset(program!, uniToGid); }
                catch { return; /* a program that does not convert ships no overflow face either */ }
            }
            else
            {
                var levelNames = new Dictionary<int, string>();
                foreach (var (code, name) in names!)
                    if (levels.TryGetValue(code, out var l) && l == level) levelNames[code] = name;
                try { ttf = Text.CffToTrueType.Convert(cff, levelNames, unicodes); }
                catch { return; /* a program that does not convert ships no overflow face either */ }
            }
            if (ttf is null) return;
            ttf = Text.CffToTrueType.MakeShippable(ttf);
            byte[] woff;
            try { woff = TtfToWoff(ttf); }
            catch { return; }
            var face = new EmbeddedFont { Family = OverflowFamilyName(family, level), Ttf = ttf, Woff = woff };
            face.Objects.Add(font);
            result.Add(face);
        }
    }
}
