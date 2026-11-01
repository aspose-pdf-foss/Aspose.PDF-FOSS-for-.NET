namespace Aspose.Pdf.Forms.Xfa;

internal static partial class XfaRenderer
{
    /// <summary>Installed faces tried, in order, for a line the default face has no glyphs for - the Chinese,
    /// Japanese and Korean system faces, then the wide-covering one.</summary>
    private static readonly string[] CoveringFaces =
        { "SimSun", "Microsoft YaHei", "SimHei", "MS Gothic", "Malgun Gothic", "Arial Unicode MS" };

    /// <summary>The advance of a full-width (ideographic) character, in thousandths of the type size.</summary>
    private const int FullWidthAdvance = 1000;

    /// <summary>True for a character of the CJK blocks set full width: ideographs, kana, hangul and their
    /// punctuation and full-width forms.</summary>
    private static bool IsFullWidth(char ch) =>
        ch is (>= '⺀' and <= '鿿') or (>= '가' and <= '힯') or (>= '豈' and <= '﫿')
            or (>= '＀' and <= '｠') or (>= '￠' and <= '￦');

    /// <summary>True when the text holds a full-width character, which only a CJK face paints.</summary>
    private static bool HoldsFullWidth(string text) => text.Any(IsFullWidth);

    /// <summary>The face a line WinAnsi cannot encode is shown in: Arial when it has a glyph for every character
    /// of the line, else the first installed face that does; Arial again when none does.</summary>
    private static (byte[] ttf, string name)? CoveringUnicodeFace(string text, bool bold)
    {
        var arialName = bold ? "Arial-Bold" : "Arial";
        var arial = FamilyFont("Arial", bold, false) ?? FamilyFont("Arial", false, false);
        if (arial is { } a && Covers(a.parser, text)) return (a.ttf, arialName);
        foreach (var name in CoveringFaces)
        {
            if (FamilyFont(name, false, false) is { } face && Covers(face.parser, text))
                return (face.ttf, name.Replace(" ", ""));
        }
        return arial is { } fallback ? (fallback.ttf, arialName) : null;
    }

    /// <summary>True when the face maps every character of the text that draws something.</summary>
    private static bool Covers(Text.GlyphOutlineParser parser, string text)
    {
        var map = parser.CMap;
        if (map is null) return false;
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch)) continue;
            if (!map.TryGetValue(ch, out var gid) || gid == 0) return false;
        }
        return true;
    }
}
