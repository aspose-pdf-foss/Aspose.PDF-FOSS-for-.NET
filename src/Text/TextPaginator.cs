using System.Text;

namespace Aspose.Pdf.Text;

/// <summary>
/// Wraps a TextFragment's text to a page's content width and splits it into
/// per-page chunks so that page.Paragraphs.Add(TextFragment) with long text
/// overflows into additional Pages instead of clipping at the first page.
/// </summary>
internal static class TextPaginator
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
    /// <param name="lineHeight">Out: the line height used (1.2 × fontSize).</param>
    /// <returns>One list of lines per page. Size ≥ 1.</returns>
    public static List<List<string>> SplitIntoPages(string text, string fontName, double fontSize,
        double contentWidth, double contentHeight, out double lineHeight)
    {
        lineHeight = fontSize * 1.2;
        var linesPerPage = Math.Max(1, (int)(contentHeight / lineHeight));

        var wrapped = WrapToWidth(text, fontName, fontSize, contentWidth);

        var pages = new List<List<string>>();
        for (var i = 0; i < wrapped.Count; i += linesPerPage)
        {
            var chunk = wrapped.GetRange(i, Math.Min(linesPerPage, wrapped.Count - i));
            pages.Add(chunk);
        }
        if (pages.Count == 0) pages.Add(new List<string>());
        return pages;
    }

    /// <summary>Greedy word-wrap fallback using Standard-14 metrics only.
    /// Use the <see cref="FontData"/> overload when an embedded font is
    /// available -- Standard-14 Helvetica widths are far narrower than most
    /// non-Latin TTFs, so the fallback under-counts line widths and produces
    /// different break-points than the font's real metrics would.</summary>
    public static List<string> WrapToWidth(string text, string fontName, double fontSize, double maxWidth)
        => WrapToWidth(text, fontName, fontSize, maxWidth, fontData: null);

    /// <summary>
    /// Greedy word-wrap. When <paramref name="fontData"/> has TTF data, per-glyph
    /// advance widths come from the font's own hmtx so wrap break-points follow
    /// the font's true advances. Without it,
    /// falls back to Standard-14 widths keyed by <paramref name="fontName"/>.
    /// </summary>
    public static List<string> WrapToWidth(string text, string fontName, double fontSize,
        double maxWidth, FontData? fontData, double firstLineIndent = 0, double charSpacing = 0)
    {
        var baseMeasurer = BuildMeasurer(fontName, fontSize, fontData);
        // Character spacing (Tc) adds `charSpacing` after every glyph, so a run of
        // N characters is that much wider — fold it into the measurer so wrap
        // break-points account for it.
        Func<string, double> measurer = charSpacing == 0
            ? baseMeasurer
            : s => baseMeasurer(s) + s.Length * charSpacing;
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
                    // the end of the finished line (the
                    // wrapped line keeps its trailing space before the break).
                    lines.Add(sep ? current.ToString() + " " : current.ToString());
                    current.Clear();
                    currentWidth = 0;
                    current.Append(word);
                    currentWidth = wordWidth;
                }
                else
                {
                    if (sep) { current.Append(' '); currentWidth += spaceWidth; }
                    current.Append(word);
                    currentWidth += wordWidth;
                }
            }
            if (current.Length > 0) lines.Add(current.ToString());
        }
        return lines;
    }

    /// <summary>
    /// Mirror the greedy word-wrap of <see cref="WrapToWidth(string,string,double,double,FontData?,double)"/>
    /// but, for each output line, also report the line's rendered width (in points,
    /// including the single trailing space that separates it from the next line)
    /// and why the line ended: 'M' = reached the right margin (wrapped), 'N' = an
    /// explicit new-line marker, 'E' = the end of the text. The produced lines align
    /// one-to-one with WrapToWidth's, so callers can index both in lock-step. Used to
    /// build the line-break notification log.
    /// </summary>
    public static List<(string content, double width, char reason)> TraceLines(
        string text, string fontName, double fontSize, double maxWidth,
        FontData? fontData, double firstLineIndent = 0)
    {
        var measurer = BuildMeasurer(fontName, fontSize, fontData);
        var spaceWidth = measurer(" ");
        var normalised = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var paragraphs = normalised.Split('\n');
        var lastParagraph = paragraphs.Length - 1;

        // (content, width-without-trailing-space, lastLineOfParagraph, paragraphIndex)
        var raw = new List<(string content, double width, bool lastInPara, int para)>();
        var globalLineCount = 0;
        for (var pi = 0; pi < paragraphs.Length; pi++)
        {
            var paragraph = paragraphs[pi];
            if (paragraph.Length == 0)
            {
                raw.Add((string.Empty, 0, true, pi));
                globalLineCount++;
                continue;
            }
            var words = paragraph.Split(' ');
            var current = new StringBuilder();
            double currentWidth = 0;
            var paraLines = new List<(string, double)>();
            for (var wi = 0; wi < words.Length; wi++)
            {
                var word = words[wi];
                // Preserve delimiter/extra spaces (kept in lock-step with
                // WrapToWidth above).
                if (word.Length == 0)
                {
                    if (wi > 0) { current.Append(' '); currentWidth += spaceWidth; }
                    continue;
                }
                var wordWidth = measurer(word);
                var sep = wi > 0 && current.Length > 0;
                var needed = wordWidth + (sep ? spaceWidth : 0);
                var effectiveMax = globalLineCount == 0 && paraLines.Count == 0
                    ? maxWidth - firstLineIndent : maxWidth;
                if (currentWidth + needed > effectiveMax && current.Length > 0)
                {
                    paraLines.Add((current.ToString(), currentWidth));
                    current.Clear();
                    current.Append(word);
                    currentWidth = wordWidth;
                }
                else
                {
                    if (sep) { current.Append(' '); currentWidth += spaceWidth; }
                    current.Append(word);
                    currentWidth += wordWidth;
                }
            }
            if (current.Length > 0) paraLines.Add((current.ToString(), currentWidth));
            for (var li = 0; li < paraLines.Count; li++)
            {
                raw.Add((paraLines[li].Item1, paraLines[li].Item2, li == paraLines.Count - 1, pi));
                globalLineCount++;
            }
        }

        var result = new List<(string, double, char)>(raw.Count);
        for (var i = 0; i < raw.Count; i++)
        {
            var r = raw[i];
            char reason = !r.lastInPara ? 'M' : (r.para == lastParagraph ? 'E' : 'N');
            // Only a MARGIN break contributes a delimiter space: the wrap consumed
            // the space that separated this line from the overflowing word, and
            // WrapToWidth keeps it on the finished line. A line that ends because its
            // paragraph did ('N') or because the text did ('E') ends where its own
            // text ends -- a trailing space there is part of the paragraph and the
            // loop above already preserved it, so adding one here would report the
            // line one space too wide and break lock-step with WrapToWidth.
            if (reason == 'M')
                result.Add((r.content + " ", r.width + spaceWidth, reason));
            else
                result.Add((r.content, r.width, reason));
        }
        return result;
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
            return s =>
            {
                double w = 0;
                foreach (var c in s)
                {
                    if (!parser.CMap.TryGetValue(c, out var gid)) gid = 0;
                    var advance = parser.GetAdvanceWidth(gid);
                    // GetAdvanceWidth returns raw font units; scale to points.
                    if (advance <= 0) advance = (int)(upm * 0.5); // fallback ~ 0.5em
                    w += advance * fontSize / upm;
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
