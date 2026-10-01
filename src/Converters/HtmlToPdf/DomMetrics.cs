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
        var prevGid = 0;
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
            // …at the face's OWN advance, not the advance rounded onto the 1000-unit em grid.
            // The grid belongs to the PDF /W array the glyph is DRAWN with, never to the
            // measurement: Arial is a 2048-upm face whose letters round by up to ±0.17 units
            // each, so a 73-character line drifts ~0.04 pt and a wrap decides one word wrong.
            // Probed: a greedy wrap on the unrounded advance reproduces all 23 lines of the
            // complaint report's narrative for any box in 427.00..427.38 (its real box is
            // 427.364), while the rounded measure breaks line 11 a word late at every width.
            w += face.parser is null || gid == 0
                ? (IsFullWidthCp(cp) ? 1.0 : 0.5) * fontSizePt
                : face.parser.GetAdvanceWidth(gid) * fontSizePt / face.upm;
            // …and the face's pair kerning between this glyph and the one before it (probed:
            // every adjacent pair inside a run, the TTF kern table's unrounded units, space
            // pairs included; a line fits when its KERNED width does).
            if (face.parser is not null && gid != 0 && prevGid != 0)
                w += face.parser.GetKernAdjustment(prevGid, gid) * fontSizePt / face.upm;
            prevGid = gid;
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
            if (TryParseLength(parts[i]) is not { } len) return false;
            v[i] = len;
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
    /// <summary>A face whose cmap lives in the F0xx symbol range (Symbol, Wingdings, Webdings).</summary>
    private static bool IsSymbolEncodedFace(string family)
        => family.Equals("Symbol", StringComparison.OrdinalIgnoreCase)
            || family.Equals("Wingdings", StringComparison.OrdinalIgnoreCase)
            || family.Equals("Webdings", StringComparison.OrdinalIgnoreCase);

    /// <summary>The family a span's style draws its run in: its font-family, or the family its
    /// `font:` shorthand names after the size (`font: 7.0pt "Times New Roman"`), when that face is
    /// installed.</summary>
    private static string? SpanRunFamily(string style)
    {
        string? fam = null;
        var ff = Regex.Match(style, @"(?<![-\w])font-family\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
        if (ff.Success) fam = FirstFontFamily(ff.Groups[1].Value.Trim());
        else
        {
            var sh = Regex.Match(style, @"(?<![-\w])font\s*:\s*[\d.]+\s*(?:pt|px)\s*([^;]+)", RegexOptions.IgnoreCase);
            if (sh.Success) fam = FirstFontFamily(sh.Groups[1].Value.Trim());
        }
        return fam is { Length: > 0 } && RunFace(fam).ttf is not null ? fam : null;
    }

    private static readonly Dictionary<string, (byte[]? ttf, Text.GlyphOutlineParser? parser)> _runFaceCache
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The face a family run draws in: the positioned face, or - for a symbol-encoded family the
    /// repository answers with the Standard-14 Symbol that has no outlines - the installed symbol face
    /// from the system resolver.</summary>
    private static (byte[]? ttf, Text.GlyphOutlineParser? parser) RunFace(string family)
    {
        if (_runFaceCache.TryGetValue(family, out var cached)) return cached;
        var pos = PosFace(family);
        (byte[]? ttf, Text.GlyphOutlineParser? parser) face = (pos.ttf, pos.parser);
        if (face.ttf is null && IsSymbolEncodedFace(family))
        {
            try
            {
                var ttf = Text.SystemFontResolver.Resolve(family);
                if (ttf is not null) face = (ttf, new Text.GlyphOutlineParser(ttf));
            }
            catch { face = (null, null); }
        }
        _runFaceCache[family] = face;
        return face;
    }

    /// <summary>A run that wraps the block's whole text IS the block's typography: the block takes the
    /// run's size and face (a MsoNormal paragraph whose one span is Arial 16 wraps and pitches as Arial
    /// 16), and a mixed line's box is the tallest run's device-rounded normal line (Arial 16 under a
    /// 12 pt paragraph = 25 px = 18.75).</summary>
    private static void TakeWholeBlockRuns(Block blk)
    {
        var textLen = blk.Text.TrimEnd(' ').Length;
        if (blk.SizeRuns is [{ Start: <= 0 } oneSize] && oneSize.Length >= textLen)
        {
            blk.FontSize = oneSize.Pt;
            blk.SizeRuns = null;
        }
        if (blk.FamilyRuns is [{ Start: <= 0 } oneFam] && oneFam.Length >= textLen)
        {
            blk.FontFamily = oneFam.Fam;
            blk.FamilyRuns = null;
        }
        if (blk.SizeRuns is null && blk.FamilyRuns is null) return;
        // An inner run beats its wrapper over the same characters: the runs close inner-first,
        // so order them wrapper-first (start ascending, longer first) and the last match wins.
        blk.SizeRuns?.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : b.Length.CompareTo(a.Length));
        blk.FamilyRuns?.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : b.Length.CompareTo(a.Length));
        blk.RunNormalLines = true;
    }

    /// <summary>A face's normal line box at a size, device-rounded (Arial 16 = 25 px = 18.75).</summary>
    private static double NormalLineOf(string face, double pt)
        => MetricLineHeight(pt, HheaLineSumFor(face) ?? WinMetricsFor(face)?.sum ?? 1.15);

    /// <summary>The face drawing the character: the family run covering it, else the block's.</summary>
    private static string FaceAt(Block block, string face, int i)
    {
        var found = face;
        if (block.FamilyRuns is not null)
            foreach (var (rs, rl, rf) in block.FamilyRuns)
                if (i >= rs && i < rs + rl) found = rf;
        return found;
    }

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
