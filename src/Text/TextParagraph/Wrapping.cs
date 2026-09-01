using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Stamps;
using System.Globalization;

namespace Aspose.Pdf.Text;

public sealed partial class TextParagraph
{
    private static double MeasureText(string text, FontInfo? font, double fontSize)
    {
        if (font is not null)
        {
            try { return font.MeasureString(text, fontSize); }
            catch { /* fall through */ }
        }
        return text.Length * fontSize * 0.5;
    }

    /// <summary>
    /// Measure line width using FontData TTF metrics, Standard14 metrics, or fallback.
    /// </summary>
    private double MeasureLineWidth(string text, TextState ts)
    {
        var fontSize = ts.FontSize;

        // Non-Standard-14 embedded fonts (e.g. MS Gothic): measure with their real
        // glyph advances so wrap points match what the CID embedder actually draws.
        if (UsesRealFont(ts))
        {
            var gp = GetGlyphParser(ts)!;
            double rw = 0;
            foreach (var ch in text) rw += RealGlyphWidth(ch, fontSize, gp);
            return rw;
        }

        // Try FontData real metrics first (e.g. system TrueType font).
        var fontData = ts.FontData ?? ts.Font?.SourceFontData;
        if (fontData is { TtfData: not null })
            return fontData.MeasureString(text, fontSize);

        // Try Standard14 metrics via font name.
        var fontName = ts.FontName ?? "Helvetica";
        if (Standard14Fonts.IsStandard14(fontName))
        {
            double w = 0;
            foreach (var ch in text)
            {
                var cw = Standard14Fonts.GetWidth(fontName, ch < 256 ? ch : '?');
                w += (cw >= 0 ? cw : 500) * fontSize / 1000.0;
            }
            return w;
        }

        // Proportional fallback
        return text.Length * fontSize * 0.5;
    }

    private List<string> WrapText(string text, FontInfo? font, double fontSize,
        double maxWidth, TextFormattingOptions.WordWrapMode wrapMode)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(text) || maxWidth <= 0)
        {
            result.Add(text);
            return result;
        }
        if (wrapMode == TextFormattingOptions.WordWrapMode.ByWords)
            WrapByWords(text, font, fontSize, maxWidth, result, HyphenSymbol);
        else if (wrapMode == TextFormattingOptions.WordWrapMode.DiscretionaryHyphenation)
            WrapWithHyphenation(text, font, fontSize, maxWidth, result, HyphenSymbol);
        else
            result.Add(text);
        return result;
    }

    private static void WrapByWords(string text, FontInfo? font, double fontSize,
        double maxWidth, List<string> result, string hyphen)
    {
        var words = text.Split(' ');
        var hyphenWidth = MeasureText(hyphen, font, fontSize);
        var currentLine = "";
        foreach (var word in words)
        {
            var candidate = currentLine.Length == 0 ? word : currentLine + " " + word;
            if (MeasureText(candidate, font, fontSize) <= maxWidth)
            {
                currentLine = candidate;
                continue;
            }
            // The word that triggered the break carries its preceding space onto
            // the completed line's end — when that space still fits; a line already
            // filled to the edge drops it. The final line gets none.
            if (currentLine.Length > 0)
                result.Add(MeasureText(currentLine + " ", font, fontSize) <= maxWidth
                    ? currentLine + " " : currentLine);
            currentLine = word;
            // A word wider than the line on its own is broken the way discretionary
            // hyphenation breaks it: the longest prefix that fits with a hyphen, else
            // one bare character per line; the remainder starts the next line.
            if (MeasureText(currentLine, font, fontSize) > maxWidth)
                HyphenateWord(currentLine, font, fontSize, maxWidth, hyphenWidth, result,
                    hyphen, out currentLine, out _);
        }
        if (currentLine.Length > 0) result.Add(currentLine);
    }

    private static void WrapWithHyphenation(string text, FontInfo? font, double fontSize,
        double maxWidth, List<string> result, string hyphen)
    {
        var hyphenWidth = MeasureText(hyphen, font, fontSize);
        var spaceWidth = MeasureText(" ", font, fontSize);
        var words = text.Split(' ');
        var currentLine = "";
        var currentWidth = 0.0;

        foreach (var word in words)
        {
            var wordWidth = MeasureText(word, font, fontSize);
            var separatorWidth = currentLine.Length == 0 ? 0 : spaceWidth;

            if (currentWidth + separatorWidth + wordWidth <= maxWidth)
            {
                if (currentLine.Length > 0) { currentLine += " "; currentWidth += spaceWidth; }
                currentLine += word;
                currentWidth += wordWidth;
            }
            else if (currentLine.Length == 0)
            {
                HyphenateWord(word, font, fontSize, maxWidth, hyphenWidth, result, hyphen, out currentLine, out currentWidth);
            }
            else
            {
                var remainingWidth = maxWidth - currentWidth - spaceWidth;
                int fitChars = remainingWidth >= hyphenWidth
                    ? FindHyphenBreak(word, font, fontSize, remainingWidth, hyphenWidth) : 0;
                if (fitChars > 0)
                {
                    currentLine += " " + word[..fitChars] + hyphen;
                    result.Add(currentLine);
                    var remainder = word[fitChars..];
                    if (MeasureText(remainder, font, fontSize) <= maxWidth)
                    {
                        currentLine = remainder;
                        currentWidth = MeasureText(remainder, font, fontSize);
                    }
                    else
                    {
                        currentLine = "";
                        currentWidth = 0;
                        HyphenateWord(remainder, font, fontSize, maxWidth, hyphenWidth, result, hyphen, out currentLine, out currentWidth);
                    }
                }
                else
                {
                    result.Add(currentLine);
                    if (wordWidth <= maxWidth) { currentLine = word; currentWidth = wordWidth; }
                    else HyphenateWord(word, font, fontSize, maxWidth, hyphenWidth, result, hyphen, out currentLine, out currentWidth);
                }
            }
        }
        if (currentLine.Length > 0) result.Add(currentLine);
    }

    private static int FindHyphenBreak(string word, FontInfo? font, double fontSize,
        double availableWidth, double hyphenWidth)
    {
        int best = 0;
        for (int i = 1; i < word.Length; i++)
        {
            if (MeasureText(word[..i], font, fontSize) + hyphenWidth <= availableWidth) best = i;
            else break;
        }
        return best;
    }

    private static void HyphenateWord(string word, FontInfo? font, double fontSize,
        double maxWidth, double hyphenWidth, List<string> result, string hyphen,
        out string remainingLine, out double remainingWidth)
    {
        var pos = 0;
        while (pos < word.Length)
        {
            var remaining = word[pos..];
            var remainW = MeasureText(remaining, font, fontSize);
            if (remainW <= maxWidth) { remainingLine = remaining; remainingWidth = remainW; return; }
            int fitChars = FindHyphenBreak(remaining, font, fontSize, maxWidth, hyphenWidth);
            // When not even one character plus a hyphen fits, the line takes one
            // bare character — no hyphen (a 6 pt column of 30 pt text shows
            // t / h / e, one letter per line).
            if (fitChars <= 0) { result.Add(remaining[..1]); pos += 1; continue; }
            result.Add(remaining[..fitChars] + hyphen);
            pos += fitChars;
        }
        remainingLine = "";
        remainingWidth = 0;
    }

    /// <summary>
    /// Greedy word-wrap of the char range [<paramref name="start"/>, <paramref name="end"/>)
    /// of <paramref name="logical"/> into <paramref name="ranges"/>, measuring each
    /// candidate with per-character widths (so mixed-font runs wrap correctly).
    /// The first word of a line is never broken even if it overflows.
    /// </summary>
    private void WrapRange(string logical, List<TextState> charTs,
        int start, int end, double maxWidth, List<(int, int)> ranges)
    {
        if (end <= start) { ranges.Add((start, 0)); return; }

        // Words = maximal runs of non-space characters within the range.
        var words = new List<(int s, int e)>();
        int j = start;
        while (j < end)
        {
            while (j < end && logical[j] == ' ') j++;
            if (j >= end) break;
            int s = j;
            while (j < end && logical[j] != ' ') j++;
            words.Add((s, j));
        }
        if (words.Count == 0) { ranges.Add((start, end - start)); return; }

        int curStart = words[0].s, curEnd = words[0].e;
        for (int k = 1; k < words.Count; k++)
        {
            // Measure from the line start to this word's end (includes the
            // inter-word spaces; excludes any trailing space after the word).
            if (RangeWidth(logical, charTs, curStart, words[k].e) <= maxWidth)
                curEnd = words[k].e;
            else
            {
                ranges.Add((curStart, curEnd - curStart));
                curStart = words[k].s;
                curEnd = words[k].e;
            }
        }
        ranges.Add((curStart, curEnd - curStart));
    }

    /// <summary>Sum of per-character advance widths over [start, end).</summary>
    private double RangeWidth(string logical, List<TextState> charTs, int start, int end)
    {
        double w = 0;
        for (int i = start; i < end && i < charTs.Count; i++)
            w += CharWidth(logical[i], charTs[i]);
        return w;
    }

    /// <summary>Advance width of a single character in the given text state,
    /// using TTF metrics, Standard-14 metrics, or a proportional fallback —
    /// mirroring <see cref="MeasureLineWidth"/>.</summary>
    private double CharWidth(char c, TextState ts)
    {
        if (c == '\n') return 0;
        // Non-Standard-14 embedded fonts (e.g. MS Gothic) are drawn with their real
        // glyphs, so measure them the same way. Latin core families (Arial/Times/
        // Courier) and fonts whose glyph data can't be read are substituted by the
        // Standard-14 font MapToStandard14 resolves — measure that instead.
        if (UsesRealFont(ts))
            return RealGlyphWidth(c, ts.FontSize, GetGlyphParser(ts)!);
        var std14 = TextBuilder.MapToStandard14Public(ts);
        var cw = Standard14Fonts.GetWidth(std14, c < 256 ? c : '?');
        return (cw >= 0 ? cw : 500) * ts.FontSize / 1000.0;
    }

    /// <summary>Largest font size among a visual line's chunks (clip-height/baseline sizing).</summary>
    private static double LineFontSize(List<(string text, TextState ts)> line)
    {
        double m = 0;
        foreach (var (_, ts) in line) if (ts.FontSize > m) m = ts.FontSize;
        return m > 0 ? m : 12;
    }
}
