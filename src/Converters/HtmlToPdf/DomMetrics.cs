using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The exact-model core of <see cref="MeasureStlExactText"/> against an
    /// already-parsed font program (installed face or a document @font-face).</summary>
    private static double MeasureParsedExact(Text.GlyphOutlineParser? parser, double upm,
        string s, double rawFontSizePt)
    {
        var fsEff = Math.Floor(rawFontSizePt * 1000.0) / 1000.0;
        double w = 0;
        for (var i = 0; i < s.Length; i++)
        {
            int cp = s[i];
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                cp = char.ConvertToUtf32(s[i], s[i + 1]);
                i++;
            }
            if (cp == 0x00A0) cp = 0x20; // nbsp measures as the space glyph
            var gid = parser is not null && parser.CMap.TryGetValue(cp, out var g) ? g : 0;
            w += parser is null || gid == 0
                ? UnmappedAdvance(cp, fsEff)
                : parser.GetAdvanceWidth(gid) * fsEff / upm;
        }
        return w;
    }

    /// <summary>Break one space-free token at CJK line-break opportunities: an
    /// ideograph may break on BOTH sides, so a spaceless CJK run's min-content is
    /// one ideograph. A token with no full-width codepoints yields itself whole.</summary>
    private static IEnumerable<string> CjkWordSegments(string word)
    {
        // Codepoint walk, not a char walk: plane-2 ideographs (CJK Extension B)
        // arrive as surrogate pairs, and testing the halves individually finds
        // no full-width codepoint at all — the run then neither breaks nor
        // measures at its em advances.
        var st = 0;
        var i = 0;
        var prevFw = false;
        while (i < word.Length)
        {
            var pair = char.IsHighSurrogate(word[i]) && i + 1 < word.Length && char.IsLowSurrogate(word[i + 1]);
            var fw = IsFullWidthCp(pair ? char.ConvertToUtf32(word[i], word[i + 1]) : word[i]);
            if (i > st && (fw || prevFw))
            {
                yield return word[st..i];
                st = i;
            }
            prevFw = fw;
            i += pair ? 2 : 1;
        }
        if (st < word.Length) yield return word[st..];
    }

    private static double MeasureFaceText(string faceName, string s, double fontSizePt)
    {
        var face = PosFace(faceName);
        s = ShapedAsDrawn(face, s);
        double w = 0;
        for (var i = 0; i < s.Length; i++)
        {
            int cp = s[i];
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                cp = char.ConvertToUtf32(s[i], s[i + 1]);
                i++;
            }
            // an nbsp missing from the cmap advances as the space glyph
            if (cp == 0xA0 && face.parser is not null && !face.parser.CMap.ContainsKey(cp))
                cp = ' ';
            var gid = face.parser is not null && face.parser.CMap.TryGetValue(cp, out var g) ? g : 0;
            // The unresolved-glyph estimate is 0.5 em — but an IDEOGRAPH advances
            // a FULL em (measured; see the CJK-advance
            // advance law). The half-em guess drew every CJK run overlapped and
            // measured its columns half as wide as they should be.
            w += face.parser is null || gid == 0
                ? (IsFullWidthCp(cp) ? 1.0 : 0.5) * fontSizePt
                : Math.Round(face.parser.GetAdvanceWidth(gid) * 1000.0 / face.upm) * fontSizePt / 1000.0;
        }
        return w;
    }

    /// <summary>Parse a CSS `margin: a [b [c [d]]]` shorthand into a pt box
    /// (top/right/bottom/left, px at 0.75 pt/px). False when any component fails.</summary>
    private static bool TryParseCssMarginBox(string value,
        out (double top, double right, double bottom, double left) box)
    {
        box = default;
        var parts = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 1 or > 4) return false;
        var v = new double[4];
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i] == "0") { v[i] = 0; continue; }
            if (!TryParseLength(parts[i], out v[i])) return false;
        }
        box = parts.Length switch
        {
            1 => (v[0], v[0], v[0], v[0]),
            2 => (v[0], v[1], v[0], v[1]),
            3 => (v[0], v[1], v[2], v[1]),
            _ => (v[0], v[1], v[2], v[3]),
        };
        return true;
    }

    /// <summary>OS/2 sxHeight as a fraction of em (Arial: 1062/2048 = 0.5186) — the
    /// x-height CSS vertical-align:middle centres against. Null when the face or
    /// the field is unavailable.</summary>
    private static double? XHeightFor(string family)
    {
        if (string.IsNullOrEmpty(family)) return null;
        if (_xHeightCache.TryGetValue(family, out var cached)) return cached;
        double? m = null;
        try
        {
            var ttf = Text.FontRepository.GetTtfData(family);
            if (ttf is not null)
            {
                var tp = new Text.TrueTypeParser(ttf);
                tp.Parse();
                if (tp.SxHeight > 0 && tp.UnitsPerEm > 0)
                    m = tp.SxHeight / (double)tp.UnitsPerEm;
            }
        }
        catch { /* face without usable metrics */ }
        _xHeightCache[family] = m;
        return m;
    }

    /// <summary>hhea (ascender − descender + lineGap) as a fraction of em — the
    /// browser's `line-height: normal` box for faces whose hhea metrics carry a
    /// line gap the win metrics don't (Times New Roman: 1.1499 vs 1.1074, i.e.
    /// 17px lines at 11pt where the win sum gives 16px). Null when the face or
    /// its metrics are unavailable.</summary>
    private static double? HheaLineSumFor(string family)
    {
        if (string.IsNullOrEmpty(family)) return null;
        if (_hheaLineSumCache.TryGetValue(family, out var cached)) return cached;
        double? m = null;
        try
        {
            var ttf = Text.FontRepository.GetTtfData(family);
            if (ttf is not null)
            {
                var tp = new Text.TrueTypeParser(ttf);
                tp.Parse();
                if (tp.Ascent > 0 && tp.UnitsPerEm > 0)
                    m = (tp.Ascent - tp.Descent + tp.LineGap) / (double)tp.UnitsPerEm;
            }
        }
        catch { /* face without usable metrics: stay on the win-metric model */ }
        _hheaLineSumCache[family] = m;
        return m;
    }
}
