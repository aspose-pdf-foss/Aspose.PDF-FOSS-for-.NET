
namespace Aspose.Pdf.Text;

public partial class TextFragment
{
    /// <summary>The body line pitch around <paramref name="idx"/>: the MEDIAN of the
    /// plausible gaps among the lines within a few line heights of it. Blank lines between
    /// blocks and heading gaps are wider than this, which is what makes it usable as the
    /// "same paragraph" yardstick.
    /// ⚠ The median, not the minimum: a page that has already been reflowed once can carry
    /// a near-zero gap between two lines, and taking the smallest would collapse the
    /// paragraph to nothing on the next pass. Degenerate gaps (under half the font size)
    /// are dropped for the same reason. Falls back to 1.2 em when no neighbour is close.
    /// </summary>
    private static double ParagraphPitch(
        System.Collections.Generic.List<(TextFragment f, double y, double lx, double rx)> lines,
        int idx, double fs)
    {
        var gaps = new System.Collections.Generic.List<double>();
        for (int i = System.Math.Max(1, idx - 6); i < System.Math.Min(lines.Count, idx + 7); i++)
        {
            double gap = lines[i - 1].y - lines[i].y;
            if (gap > fs * 0.5 && gap < 3 * fs) gaps.Add(gap);
        }
        if (gaps.Count == 0) return fs * 1.2;
        gaps.Sort();
        return gaps[gaps.Count / 2];
    }

    /// <summary>
    /// Re-wrap the whole paragraph that contains this fragment after a replacement, so
    /// following words flow up to close the gap a shorter replacement leaves — matching
    /// the WholeWordsHyphenation re-flow. Groups the contiguous, same-left-margin
    /// lines around this fragment into a paragraph, applies the replacement to EVERY
    /// occurrence in it, greedy-wraps the result to the paragraph's width, and re-emits the
    /// lines at the original baseline grid (in the paragraph's dominant font). Returns false
    /// when the search text isn't in the detected paragraph (so sibling fragments no-op).
    /// </summary>
    /// <summary>The advance the replacement actually occupies on the page, read back from
    /// the written content rather than measured in the source font. A replacement whose
    /// glyphs the source subset lacks is re-dressed in a substitute face, so the source
    /// font's width table describes a run that was never drawn; only the page itself
    /// knows. Returns null when the run cannot be located again.</summary>
    private static double? MeasureWrittenAdvance(Page page, string text, double atX, double atY)
    {
        try
        {
            var probe = new TextFragmentAbsorber(text);
            probe.Visit(page);
            double? bestWidth = null;
            var bestGap = double.MaxValue;
            foreach (TextFragment f in probe.TextFragments)
            {
                var pos = f.PositionOrNull;
                if (pos is null || f.Rectangle is not { } rect) continue;
                var baseY = (f.BaselinePosition ?? pos).YIndent;
                if (Math.Abs(baseY - atY) > 0.5) continue;
                var gap = Math.Abs(pos.XIndent - atX);
                if (gap < bestGap) { bestGap = gap; bestWidth = rect.Width; }
            }
            // The run keeps its left edge through an in-place swap, so a candidate that
            // moved is a different occurrence of the same text, not this one.
            return bestGap <= 1.0 ? bestWidth : null;
        }
        catch { return null; }
    }

    /// <summary>The paragraph text with a line-STRADDLING occurrence of
    /// <paramref name="oldText"/> replaced, or null when the paragraph does not carry one.
    /// Every run of whitespace on either side counts as one break, which is what the
    /// paragraph's own line ending became when the lines were joined.</summary>
    private static string? StraddlingReplace(string paragraph, string oldText, string newText)
    {
        var words = System.Text.RegularExpressions.Regex.Split(oldText.Trim(), @"\s+");
        var sb = new System.Text.StringBuilder();
        foreach (var w in words)
        {
            if (w.Length == 0) continue;
            if (sb.Length > 0) sb.Append(@"\s+");
            sb.Append(System.Text.RegularExpressions.Regex.Escape(w));
        }
        var pattern = sb.ToString();
        if (pattern.Length == 0) return null;
        var rx = new System.Text.RegularExpressions.Regex(pattern);
        if (!rx.IsMatch(paragraph)) return null;
        return rx.Replace(paragraph, newText.Replace("$", "$$"), 1);
    }

    /// <summary>The X a reflow wraps against: the PAGE'S OWN TEXT COLUMN — the widest text
    /// extent anywhere on the page, clipped to the page itself. Measured on two documents
    /// whose columns differ by 200 pt, each reproduced to within one character
    /// width (one page: widest line 365.83, wrap 362.45–367.85; another: widest line 582.56,
    /// wrap 573.08–585.30). It is NOT the paragraph's own extent — a heading narrower than the
    /// body still reflows to the body's column — and not the mirrored left inset, which
    /// depends on where the paragraph happens to start. Falls back to that mirror when the
    /// page carries no other text to read a column from (the reflowed line IS the widest).</summary>
    private static double PageTextRightMargin(Page page,
        System.Collections.Generic.List<(TextFragment f, double y, double lx, double rx)> pageLines,
        double paraLeftX, double paraRightX)
    {
        double pageRight = page.MediaBox is { } mb ? mb.URX : 0;
        double widest = 0;
        foreach (var l in pageLines)
            if (l.rx > widest && !string.IsNullOrWhiteSpace(l.f.Text)) widest = l.rx;
        if (pageRight > 0 && widest > pageRight) widest = pageRight;
        // Nothing else on the page is as wide as the paragraph being reflowed: there is no
        // column to read, so mirror the paragraph's left inset the way a lone line does.
        if (widest <= paraRightX + 0.5)
            return System.Math.Max(pageRight - paraLeftX, paraRightX);
        return widest;
    }

    /// <summary>Greedy word-wrap of <paramref name="text"/> to <paramref name="maxWidth"/>
    /// using the font's real advance metrics at <paramref name="fs"/>. When
    /// <paramref name="trailingSpace"/> is set, each candidate line is measured WITH a
    /// trailing space (reserving one space width past each line, so lines break slightly
    /// earlier) and every completed (non-final) line keeps that trailing space — this keeps
    /// the wrapped lines re-searchable across the breaks.</summary>
    private static double MeasureOrEstimate(FontInfo font, string s, double fs, bool trailingSpace)
    {
        var m = trailingSpace ? s + " " : s;
        try { return font.MeasureString(m, fs); } catch { return m.Length * fs * 0.5; }
    }

    private static System.Collections.Generic.List<string> WrapToWidth(string text, FontInfo font, double fs, double maxWidth, bool trailingSpace = false, bool allowCharBreak = false, Func<string, double>? measure = null)
        => WrapToBudgets(text, font, fs, _ => maxWidth, trailingSpace, allowCharBreak, measure);

    /// <summary>Height the wrapped block occupies at <paramref name="fs"/>, counting
    /// one em of ascent for the last line — the lower edge of the fit window.</summary>
    private static double BlockHeight(string text, FontInfo font, double fs,
        double wrapWidth, double leadingRatio, Func<string, double, double>? measure)
    {
        var n = WrapToWidth(text, font, fs, wrapWidth,
            measure: measure is null ? null : s => measure(s, fs)).Count;
        return (leadingRatio * (n - 1) + 1.0) * fs;
    }

    /// <summary>Fit a font size to the rectangle by bisecting between
    /// <paramref name="lo"/> and <paramref name="hi"/>, testing the wrapped block
    /// against a two-sided height window: the block must FIT the rectangle counting
    /// one em of ascent for the last line, and must FILL it counting 1.1 em. The
    /// first midpoint inside that window is the answer — which is why fitted sizes
    /// come out as exact dyadic fractions of the starting size. When no size can
    /// satisfy the window (the line count flips before it is reached) the search
    /// converges instead on the wrap threshold, the largest size whose widest line
    /// still fits the width.</summary>
    private static double FitFontSize(string text, FontInfo font, double wrapWidth,
        double targetHeight, double lo, double hi, double leadingRatio,
        Func<string, double, double>? measure = null)
    {
        (double Min, double Max) Window(double fs)
        {
            var n = WrapToWidth(text, font, fs, wrapWidth,
                measure: measure is null ? null : s => measure(s, fs)).Count;
            var lead = leadingRatio * (n - 1);
            return ((lead + 1.0) * fs, (lead + 1.1) * fs);
        }
        for (var it = 0; it < 48; it++)
        {
            var mid = (lo + hi) / 2;
            var w = Window(mid);
            if (w.Min > targetHeight) hi = mid;
            else if (w.Max < targetHeight) lo = mid;
            else return mid;
        }
        return lo;
    }

    /// <summary>When the fragment's edit options explicitly select
    /// <see cref="TextEditOptions.NoCharacterAction.ThrowException"/>, throw
    /// <see cref="InvalidOperationException"/> if the new text contains a character
    /// the fragment's font cannot represent.</summary>
    private void ThrowIfFontLacksGlyph(string newText)
    {
        if (_textEditOptions is not { NoCharacterBehaviorExplicit: true } teo
            || teo.NoCharacterBehavior != TextEditOptions.NoCharacterAction.ThrowException
            || TextState.Font is not { } font
            || string.IsNullOrEmpty(newText))
            return;

        foreach (var ch in newText)
            if (!font.CanRepresent(ch))
                throw new InvalidOperationException(
                    $"Font '{font.FontName}' does not contain a glyph for character " +
                    $"'{ch}' (U+{(int)ch:X4}).");
    }

    /// <summary>Measure the replacement text's advance for decoration sizing. A subset
    /// font carries widths only for its own glyphs, so when any replacement character
    /// lacks an explicit width the embedded metrics would degrade to default (1 em)
    /// widths — fall back to the real system face of the same family/style, which is
    /// what the replaced text renders in after the subset-glyph font switch.</summary>
    private static double MeasureReplacementAdvance(FontInfo font, string text, double fontSize)
    {
        var covered = true;
        try
        {
            var m = font.Metrics;
            foreach (var ch in text)
            {
                var code = m.IsCid ? ch : (ch < 256 ? ch : '?');
                if (!m.HasExplicitWidth(code)) { covered = false; break; }
            }
        }
        catch { covered = true; }
        if (!covered && font.FontName is { Length: > 0 } name)
        {
            try
            {
                // Measure with the system face's raw TTF metrics (FontData.MeasureString);
                // Font.MeasureString would consult the synthetic font dict, which carries
                // no widths for a repository-resolved face.
                var real = FontRepository.TryFindFont(name, ignoreCase: true);
                if (real?.SourceFontData is { } fd)
                {
                    var w = fd.MeasureString(text, fontSize);
                    if (w > 0) return w;
                }
            }
            catch { }
        }
        try { return font.MeasureString(text, fontSize); }
        catch { return -1; }
    }

    /// <summary>The (negative) page-space descent between this fragment's box
    /// bottom / Position and its baseline - the SAME rule the absorber seats the
    /// whole-run box by: the font's descriptor descent, and none for a
    /// descriptor-less core face (its box sits on the baseline).</summary>
    private double SeatDescent()
    {
        var metrics = TextState.Font?.GetMetrics();
        var fs = TextState.FontSize;
        return metrics is not null && metrics.Descent != 0 ? metrics.Descent * fs / 1000.0 : 0;
    }

    /// <summary>The same seat descent for an arbitrary face and size, as a POSITIVE
    /// box-bottom-to-baseline gap — what a re-flow needs to compare the descent it is
    /// writing with against the one the source was drawn with.</summary>
    private static double SeatDescentOf(FontInfo? font, double fs)
    {
        var metrics = font?.GetMetrics();
        return metrics is not null && metrics.Descent != 0 ? -metrics.Descent * fs / 1000.0 : 0;
    }
}
