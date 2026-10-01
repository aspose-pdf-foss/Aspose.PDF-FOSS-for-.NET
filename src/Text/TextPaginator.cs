using System.Text;

namespace Aspose.Pdf.Text;

/// <summary>
/// Wraps a TextFragment's text to a page's content width and splits it into
/// per-page chunks so that page.Paragraphs.Add(TextFragment) with long text
/// overflows into additional Pages instead of clipping at the first page.
/// </summary>
internal static partial class TextPaginator
{
    /// <summary>
    /// Split `text` into per-page wrapped-line groups. Honours explicit '\n'
    /// in the input by forcing a line break; everything else is greedy-wrapped
    /// by word so each line fits within <paramref name="contentWidth"/>.
    /// </summary>
    /// <param name="text">Full paragraph text.</param>
    /// <param name="fontName">Standard-14 font name (used for glyph widths).</param>
    /// <param name="fontSize">Font size in points.</param>
    /// <param name="contentWidth">Available text width in points.</param>
    /// <param name="contentHeight">Available text height on the first page.</param>
    /// <returns>One list of lines per page (size >= 1), with the line height used
    /// (1.2 x fontSize).</returns>
    public static (List<List<string>> pages, double lineHeight) SplitIntoPages(string text, string fontName, double fontSize,
        double contentWidth, double contentHeight)
    {
        var lineHeight = fontSize * 1.2;
        var linesPerPage = Math.Max(1, (int)(contentHeight / lineHeight));

        var wrapped = WrapToWidth(text, fontName, fontSize, contentWidth);

        var pages = new List<List<string>>();
        for (var i = 0; i < wrapped.Count; i += linesPerPage)
        {
            var chunk = wrapped.GetRange(i, Math.Min(linesPerPage, wrapped.Count - i));
            pages.Add(chunk);
        }
        if (pages.Count == 0) pages.Add(new List<string>());
        return (pages, lineHeight);
    }

    /// <summary>Greedy word-wrap fallback using Standard-14 metrics only.
    /// Use the <see cref="FontData"/> overload when an embedded font is
    /// available -- Standard-14 Helvetica widths are far narrower than most
    /// non-Latin TTFs, so the fallback under-counts line widths and produces
    /// different break-points than the font's real metrics would.</summary>
    public static List<string> WrapToWidth(string text, string fontName, double fontSize, double maxWidth)
        => WrapToWidth(text, fontName, fontSize, maxWidth, fontData: null);

    private static int CountSpaces(string s)
    {
        var n = 0;
        foreach (var c in s) if (c == ' ') n++;
        return n;
    }

    /// <summary>
    /// Greedy word-wrap. When <paramref name="fontData"/> has TTF data, per-glyph
    /// advance widths come from the font's own hmtx so wrap break-points follow
    /// the font's true advances. Without it,
    /// falls back to Standard-14 widths keyed by <paramref name="fontName"/>.
    /// </summary>
    public static List<string> WrapToWidth(string text, string fontName, double fontSize,
        double maxWidth, FontData? fontData, double firstLineIndent = 0, double charSpacing = 0,
        bool hangingBreakSpace = false, double wordSpacing = 0)
    {
        var baseMeasurer = BuildMeasurer(fontName, fontSize, fontData);
        // Character spacing (Tc) adds `charSpacing` after every glyph, so a run of
        // N characters is that much wider — fold it into the measurer so wrap
        // break-points account for it.
        // Word spacing (Tw) adds `wordSpacing` after every space glyph the same way.
        Func<string, double> measurer = charSpacing == 0 && wordSpacing == 0
            ? baseMeasurer
            : s => baseMeasurer(s) + s.Length * charSpacing + (wordSpacing == 0 ? 0 : CountSpaces(s) * wordSpacing);
        var lines = new List<string>();
        // Normalise line endings so a \r\n file doesn't leave dangling \r in the output.
        var normalised = text.Replace("\r\n", "\n").Replace('\r', '\n');
        foreach (var paragraph in normalised.Split('\n'))
        {
            if (paragraph.Length == 0)
            {
                lines.Add(string.Empty); // preserve blank lines between paragraphs
                continue;
            }
            var words = paragraph.Split(' ');
            var current = new StringBuilder();
            double currentWidth = 0;
            var spaceWidth = measurer(" ");
            for (var wi = 0; wi < words.Length; wi++)
            {
                var word = words[wi];
                // Every token after the first is preceded by one delimiter
                // space; empty tokens are the EXTRA spaces of a run. Both are
                // preserved as literal glyphs —
                // "sentence.␣␣Next" stays double-spaced and a paragraph's leading
                // spaces indent its first line; collapsing them shifts every
                // following line-break in the paragraph.
                if (word.Length == 0)
                {
                    if (wi > 0) { current.Append(' '); currentWidth += spaceWidth; }
                    continue;
                }
                var wordWidth = measurer(word);
                var sep = wi > 0 && current.Length > 0;
                var needed = wordWidth + (sep ? spaceWidth : 0);
                // The very first output line is narrowed by a first-line indent so
                // it holds fewer words (the indent shifts its start to the right).
                var effectiveMax = lines.Count == 0 ? maxWidth - firstLineIndent : maxWidth;
                if (currentWidth + needed > effectiveMax && current.Length > 0)
                {
                    // The inter-word space that precedes the overflowing word stays at
                    // the end of the finished line when it still fits the width; a
                    // space that would itself overflow is dropped with the break
                    // (the generator's own output: "…elit," 243.42 wide on a 246 band
                    // ends without its space, "…dolore " 235.67 keeps it).
                    // A hanging break space keeps the separator whether or not it
                    // fits -- it overhangs the measure, which is the point.
                    lines.Add(sep && (hangingBreakSpace || currentWidth + spaceWidth <= effectiveMax)
                        ? current.ToString() + " " : current.ToString());
                    current.Clear();
                    currentWidth = 0;
                }
                else if (sep) { current.Append(' '); currentWidth += spaceWidth; }
                if (current.Length == 0 && wordWidth > effectiveMax && CharacterFillApplies(fontData))
                {
                    // A single token wider than the whole line is cut character by
                    // character: each line takes the longest prefix that fits and
                    // the remainder starts the next (an unbroken 3000-glyph segment
                    // chain fills 69 full lines per page; a Chinese paragraph in
                    // Arial Unicode MS breaks at the margin).
                    var rest = word;
                    while (rest.Length > 0)
                    {
                        effectiveMax = lines.Count == 0 ? maxWidth - firstLineIndent : maxWidth;
                        (var take, var pieceWidth) = LongestFittingPrefix(rest, measurer, effectiveMax);
                        if (take >= rest.Length)
                        {
                            current.Append(rest);
                            currentWidth = pieceWidth;
                            break;
                        }
                        lines.Add(rest.Substring(0, take));
                        rest = rest.Substring(take);
                    }
                }
                else
                {
                    current.Append(word);
                    currentWidth += wordWidth;
                }
            }
            if (current.Length > 0) lines.Add(current.ToString());
        }
        return lines;
    }

    /// <summary>Whether an over-wide token is cut character by character for this
    /// face: always in Standard-14 metric space and for a TrueType (glyf) outline
    /// face; never for a CFF-outline (OTTO) face, whose over-wide lines the
    /// generator leaves whole (a Source Han Serif paragraph extracts line for line
    /// with its source, while an Arial Unicode MS one wraps at the margin).</summary>
    private static bool CharacterFillApplies(FontData? fontData)
    {
        var d = fontData?.TtfData;
        if (d is null) return true;
        if (d.Length < 16) return false;
        static uint Tag(byte[] b, int at) =>
            (uint)(b[at] << 24 | b[at + 1] << 16 | b[at + 2] << 8 | b[at + 3]);
        var tag = Tag(d, 0);
        if (tag == 0x74746366) // 'ttcf': the first member face decides
        {
            var off = (int)Tag(d, 12);
            if (off < 0 || off + 4 > d.Length) return false;
            tag = Tag(d, off);
        }
        return tag == 0x00010000 || tag == 0x74727565; // sfnt 1.0 / 'true'
    }

    /// <summary>Number of leading characters of <paramref name="word"/> whose
    /// summed advances stay within <paramref name="maxWidth"/> (at least one, so
    /// an over-wide glyph still makes progress); <c>width</c> is the
    /// prefix's width.</summary>
    private static (int result, double width) LongestFittingPrefix(string word, Func<string, double> measurer, double maxWidth)
    {
        double width = default;
        width = 0;
        var take = 0;
        while (take < word.Length)
        {
            // A surrogate pair is one glyph: it is measured and cut as a unit.
            var len = char.IsHighSurrogate(word[take]) && take + 1 < word.Length
                      && char.IsLowSurrogate(word[take + 1]) ? 2 : 1;
            var cw = measurer(word.Substring(take, len));
            if (take > 0 && width + cw > maxWidth) break;
            width += cw;
            take += len;
        }
        return (take, width);
    }

    /// <summary>
    /// Mirror the greedy word-wrap of <c>WrapToWidth</c>
    /// but, for each output line, also report the line's rendered width (in points,
    /// including the single trailing space that separates it from the next line)
    /// and why the line ended: 'M' = reached the right margin (wrapped), 'N' = an
    /// explicit new-line marker, 'E' = the end of the text. The produced lines align
    /// one-to-one with WrapToWidth's, so callers can index both in lock-step. Used to
    /// build the line-break notification log.
    /// </summary>
    public static List<(string content, double width, char reason)> TraceLines( string text, string fontName, double fontSize, double maxWidth, FontData? fontData, double firstLineIndent = 0)
    {
        var tl = new TraceLinesState();
        tl.measurer = BuildMeasurer(fontName, fontSize, fontData);
        tl.spaceWidth = tl.measurer(" ");
        tl.normalised = text.Replace("\r\n", "\n").Replace('\r', '\n');
        tl.paragraphs = tl.normalised.Split('\n');
        tl.lastParagraph = tl.paragraphs.Length - 1;

        tl.raw = new List<(string content, double width, bool lastInPara, int para, bool keptSpace)>();
        tl.globalLineCount = 0;
        for (var pi = 0; pi < tl.paragraphs.Length; pi++)
        {
            TraceParagraphLines(tl, pi, firstLineIndent, fontData, maxWidth);
        }

        tl.result = new List<(string, double, char)>(tl.raw.Count);
        for (var i = 0; i < tl.raw.Count; i++)
        {
            var r = tl.raw[i];
            char reason = !r.lastInPara ? 'M' : (r.para == tl.lastParagraph ? 'E' : 'N');
            // Only a MARGIN break contributes a delimiter space: the wrap consumed
            // the space that separated this line from the overflowing word, and
            // WrapToWidth keeps it on the finished line. A line that ends because its
            // paragraph did ('N') or because the text did ('E') ends where its own
            // text ends -- a trailing space there is part of the paragraph and the
            // loop above already preserved it, so adding one here would report the
            // line one space too wide and break lock-step with WrapToWidth.
            if (reason == 'M' && r.keptSpace)
                tl.result.Add((r.content + " ", r.width + tl.spaceWidth, reason));
            else
                tl.result.Add((r.content, r.width, reason));
        }
        return tl.result;
    }

    /// <summary>Expose the wrap's own measurer so a caller placing text it
    /// wrapped here (e.g. the flow layout's justified-line token placement)
    /// positions glyph runs with exactly the metrics the wrap decided line
    /// breaks with.</summary>
    internal static Func<string, double> CreateMeasurer(string fontName, double fontSize, FontData? fontData)
        => BuildMeasurer(fontName, fontSize, fontData);

    /// <summary>Build a single-string -> width measurer. When the supplied
    /// FontData has TtfData, instantiates a GlyphOutlineParser once and routes
    /// every character through cmap -> hmtx -> scaled-to-pt; otherwise falls
    /// back to Standard-14 widths.</summary>
    private static Func<string, double> BuildMeasurer(string fontName, double fontSize, FontData? fontData)
    {
        if (fontData is { TtfData: { Length: > 12 } ttf })
        {
            var parser = new GlyphOutlineParser(ttf);
            var upm = parser.UnitsPerEm > 0 ? parser.UnitsPerEm : 1000;
            var whole = WholeUnitAdvances.Holds(ttf);
            return s =>
            {
                double w = 0;
                foreach (var c in s)
                {
                    if (!parser.CMap.TryGetValue(c, out var gid)) gid = 0;
                    var advance = parser.GetAdvanceWidth(gid);
                    // GetAdvanceWidth returns raw font units; scale to points.
                    if (advance <= 0) advance = (int)(upm * 0.5); // fallback ~ 0.5em
                    w += whole ? WholeUnitAdvances.PerMille(advance, upm, whole) * fontSize / 1000.0 : advance * fontSize / upm;
                }
                return w;
            };
        }
        return s => Standard14MeasureWidth(s, fontName, fontSize);
    }

    private static double Standard14MeasureWidth(string s, string fontName, double fontSize)
    {
        double w = 0;
        foreach (var c in s)
        {
            var glyph = c < 256 ? c : '?';
            var cw = Standard14Fonts.GetWidth(fontName, glyph);
            if (cw < 0) cw = 500; // unknown: proportional fallback
            w += cw * fontSize / 1000.0;
        }
        return w;
    }
}
