using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextFragmentAbsorber
{
    /// <summary>Drop fragments outside <c>TextSearchOptions.Rectangle</c> (whole-document
    /// visits collect from every page, so the filter runs over the full set).</summary>
    private void ApplySearchRectFilter()
    {
        var searchRect = _textSearchOptions?.Rectangle;
        if (searchRect is null || searchRect.IsEmpty) return;
        for (var i = _fragments.Count - 1; i >= 0; i--)
            if (!FragmentInSearchRect(searchRect, _fragments.GetInternal(i)))
                _fragments.RemoveAt(i);
    }

    /// <summary>
    /// Search for text across page boundaries by concatenating text from all pages.
    /// </summary>
    private void BuildCrossPageSearchFragments(List<(Page page, List<RawTextRun> runs)> allPageRuns)
    {
        var xp = new CrossPageSearchState();
        xp.allPageRuns = allPageRuns;
        xp.fullText = new StringBuilder();
        xp.charMap = new List<(int pageIdx, int runIdx)>();
        xp.pageRunStartChars = new List<List<int>>(); // per page, per run: start char index

        for (int pi = 0; pi < xp.allPageRuns.Count; pi++)
        {
            if (!CollectCrossPageRuns(xp, pi)) break;
        }

        xp.concatenated = xp.fullText.ToString();
        (xp.concatenated, var xNewToOld) = NormalizeArabicPresentationFormsWithMap(xp.concatenated);
        if (xNewToOld is not null)
        {
            var expanded = new List<(int pageIdx, int runIdx)>(xNewToOld.Length);
            foreach (var o in xNewToOld) expanded.Add(xp.charMap[o]);
            var oldToNew = new int[xp.charMap.Count + 1];
            var jj = 0;
            for (var o = 0; o <= xp.charMap.Count; o++)
            {
                while (jj < xNewToOld.Length && xNewToOld[jj] < o) jj++;
                oldToNew[o] = jj;
            }
            foreach (var starts in xp.pageRunStartChars)
                for (var r = 0; r < starts.Count; r++)
                    starts[r] = oldToNew[Math.Min(starts[r], xp.charMap.Count)];
            xp.charMap = expanded;
        }

        xp.matches = BuildMatches(xp.concatenated);

        foreach (Match match in xp.matches)
        {
            if (!EmitCrossPageMatch(xp, match)) break;
        }
    }

    private void BuildSearchFragments(List<RawTextRun> rawFragments, int pageIndex,
        Page? sourcePage = null, XForm? sourceForm = null, List<RawFillRect>? fillRects = null)
    {
        var sf = new SearchFragmentState();
        sf.rawFragments = rawFragments;
        sf.pageIndex = pageIndex;
        sf.sourcePage = sourcePage;
        sf.sourceForm = sourceForm;
        sf.fillRects = fillRects;
        SplitRunsAtCharGaps(sf.rawFragments);
        // Flatten formatting mode orders the SEARCH TEXT by reading position, not
        // stream order — a pattern spanning lines (a bracketed block whose closing
        // half is drawn earlier in the stream) only pairs up in reading order.
        if (ExtractionOptions?.FormattingMode == TextExtractionOptions.TextFormattingMode.Flatten)
            sf.rawFragments = ReorderRunsForFlatten(sf.rawFragments);
        sf.preCountAll = _fragments.Count;
        // Later-text occlusion + clipped-away detection (stacked duplicate draws,
        // strip-clipped multi-pass pages): search matches report Invisible when
        // every spanned run is hidden, same as full extraction.
        (sf.laterInk, sf.clippedAway, sf.runBoxArea) = ComputeLaterInkOcclusion(sf.rawFragments);
        // Phase 1: Build the concatenated text and character-to-run mapping
        (sf.concatenated, sf.charToRun, sf.runStartChar, sf.bidiPerm) = BuildConcatenatedText(sf.rawFragments);
        if (SearchDebug)
            Console.Error.WriteLine($"[searchtext:page{sf.pageIndex}]<<<{sf.concatenated}>>>");

        sf.fillIndex = sf.fillRects is { Count: > 0 } ? new FillRectIndex(sf.fillRects) : null;

        // Phases 2+3 run once per search pattern. The Regex[] ctor shares the
        // extracted text across ALL its regexes (extraction is the expensive
        // phase - sharing it is the point of the multi-regex API) and buckets
        // each regex's fragments into RegexResults; a regex's bucket holds
        // exactly what a sequential single-regex absorber would find.
        if (_regexes is { Length: > 0 } multiRx)
        {
            foreach (var rx in multiRx)
            {
                if (!RegexResults.TryGetValue(rx, out var bucket))
                    RegexResults[rx] = bucket = new TextFragmentCollection();
                var rxPre = _fragments.Count;
                EmitMatches(sf, BuildMatchesFor(rx, sf.concatenated), rxPre);
                for (var fi = rxPre; fi < _fragments.Count; fi++)
                    bucket.Add(_fragments.GetInternal(fi));
            }
            return;
        }
        EmitMatches(sf, BuildMatches(sf.concatenated), sf.preCountAll);
        return;

    }

    /// <summary>
    /// Drops the leading/trailing spaces a match picked up from junction synthesis while
    /// keeping the ones its glyph runs drew. <paramref name="drawn"/> is the segment join,
    /// which carries only real glyphs, so the spaces it opens and closes with are exactly
    /// the ones the matched text is entitled to keep.
    /// </summary>
    private static string TrimSynthesizedEdges(string matched, string drawn)
    {
        var lead = CountEdgeSpaces(matched, fromStart: true) - CountEdgeSpaces(drawn, fromStart: true);
        var trail = CountEdgeSpaces(matched, fromStart: false) - CountEdgeSpaces(drawn, fromStart: false);
        var start = Math.Max(0, lead);
        var end = Math.Max(0, trail);
        return start + end >= matched.Length ? matched.Trim(' ') : matched.Substring(start, matched.Length - start - end);
    }

    private static int CountEdgeSpaces(string s, bool fromStart)
    {
        var n = 0;
        while (n < s.Length && s[fromStart ? n : s.Length - 1 - n] == ' ') n++;
        return n;
    }

    /// <summary>
    /// Computes the bounding rectangle for a search match spanning runs [firstRunIdx.lastRunIdx].
    /// Handles within-run offsets for partial first/last runs, descent/ascent, and text matrix.
    /// </summary>
    private static Rectangle ComputeMatchBounds(List<RawTextRun> rawFragments, int[] runStartChar,
        int firstRunIdx, int lastRunIdx, int startCharIdx, int endCharIdx)
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        // Line-break sentinels are junction markers, not glyphs: they sit at the Y of
        // the line being LEFT with a synthetic 1-em width. One whose band lies OUTSIDE
        // the matched glyphs' own line band (a reflowed stream bouncing its text matrix
        // between lines) must not balloon the rect onto a neighbouring line; sentinels
        // INSIDE the band (back-jump splices within one visual line) keep contributing
        // as they always have. Union the real runs first, then band-test the sentinels.
        List<(double x1, double y1, double x2, double y2)>? sentinels = null;

        for (var ri = firstRunIdx; ri <= lastRunIdx; ri++)
        {
            var run = rawFragments[ri];
            var w = run.Width > 0 ? run.Width : EstimateWidth(run.Text, run.FontSize);

            // Compute descent/ascent offsets for rectangle corners.
            // Standard-14 fonts may omit FontDescriptor; fall back to AFM reference values
            // so the rectangle LLY isn't effectively zero.
            var (descentOff, ascentH) = ComputeDescentAscent(run);

            // For the first run, advance past the prefix to the match start position
            double runStartX = run.X, runStartY = run.Y;
            if (ri == firstRunIdx)
            {
                var offsetInRun = startCharIdx - runStartChar[ri];
                if (offsetInRun > 0 && offsetInRun < run.Text.Length)
                {
                    var prefixWidth = MeasureRunPrefix(run, offsetInRun);
                    runStartX = run.X + run.TmA * prefixWidth * run.HScaling;
                    runStartY = run.Y + run.TmB * prefixWidth * run.HScaling;
                    w -= prefixWidth;
                }
            }

            // For the last run, trim width to end of match
            if (ri == lastRunIdx)
                w = MeasureMatchWidthInRun(run, runStartChar[ri], startCharIdx, endCharIdx, ri == firstRunIdx);

            // Map to page space through text matrix + CTM
            var scaledW = w * run.HScaling;
            var (px, py) = ApplyCtm(runStartX + run.TmC * descentOff,
                                     runStartY + run.TmD * descentOff, run.Ctm);
            var (px2, py2) = ApplyCtm(runStartX + run.TmA * scaledW + run.TmC * ascentH,
                                       runStartY + run.TmB * scaledW + run.TmD * ascentH, run.Ctm);
            if (run.Text == "\r\n")
            {
                (sentinels ??= new()).Add((Math.Min(px, px2), Math.Min(py, py2),
                    Math.Max(px, px2), Math.Max(py, py2)));
                continue;
            }
            minX = Math.Min(minX, Math.Min(px, px2));
            minY = Math.Min(minY, Math.Min(py, py2));
            maxX = Math.Max(maxX, Math.Max(px, px2));
            maxY = Math.Max(maxY, Math.Max(py, py2));
        }

        if (sentinels is not null && minY <= maxY)
            foreach (var s in sentinels)
                if (s.y1 <= maxY + 0.5 && s.y2 >= minY - 0.5)
                {
                    minX = Math.Min(minX, s.x1);
                    minY = Math.Min(minY, s.y1);
                    maxX = Math.Max(maxX, s.x2);
                    maxY = Math.Max(maxY, s.y2);
                }

        return new Rectangle(minX, minY, maxX, maxY);
    }

    /// <summary>
    /// Measures the width of a prefix (first N characters) within a run.
    /// Uses CharCumWidths when available (exact TJ advances), then font metrics, then proportional.
    /// </summary>
    private static double MeasureRunPrefix(RawTextRun run, int offsetInRun)
    {
        if (run.CharCumWidths is not null && offsetInRun < run.CharCumWidths.Length)
            return run.CharCumWidths[offsetInRun];
        if (run.Metrics is not null)
            return run.Metrics.MeasureString(run.Text[..offsetInRun], run.FontSize);
        var totalW = run.Width > 0 ? run.Width : EstimateWidth(run.Text, run.FontSize);
        return (offsetInRun / (double)run.Text.Length) * totalW;
    }

    /// <summary>
    /// Measures the width of the matched portion within the last run of a match.
    /// Uses CharCumWidths/CharEndPositions for accuracy, falls back to proportional.
    /// CharEndPositions are preferred because they exclude compensation kerning
    /// between the matched region and post-match characters.
    /// </summary>
    private static double MeasureMatchWidthInRun(RawTextRun run, int runStart,
        int startCharIdx, int endCharIdx, bool isAlsoFirstRun)
    {
        var matchEnd = endCharIdx - runStart + 1;
        var offsetStart = isAlsoFirstRun ? startCharIdx - runStart : 0;
        if (matchEnd > run.Text.Length)
            return run.Width > 0 ? run.Width : EstimateWidth(run.Text, run.FontSize);

        var totalRunW = run.Width > 0 ? run.Width : EstimateWidth(run.Text, run.FontSize);
        if (run.CharCumWidths is not null && offsetStart < run.CharCumWidths.Length)
        {
            var startW = run.CharCumWidths[offsetStart];
            double endW;
            if (matchEnd - 1 >= 0 && run.CharEndPositions is not null
                && matchEnd - 1 < run.CharEndPositions.Length)
                endW = run.CharEndPositions[matchEnd - 1];
            else if (matchEnd < run.CharCumWidths.Length)
            {
                // The box ends where the last matched glyph's advance or the pen ends,
                // whichever is further: a NEGATIVE character spacing pulls the pen back
                // inside the glyph and the box keeps the glyph ("Solutions" at Tc -0.0333
                // in an 11.25-scaled matrix measures 0.37 pt WIDER than its pen advance),
                // a positive one moves the pen on and the box follows it (a phrase closing
                // a Tc 5 run keeps that 5 in its width).
                var trailing = run.CharSpacing + (run.Text[matchEnd - 1] == ' ' ? run.WordSpacing : 0);
                endW = run.CharCumWidths[matchEnd] - Math.Min(0, trailing);
            }
            else
                endW = totalRunW;
            return endW - startW;
        }
        // Proportional fallback — avoids MeasureString(string) encoding issues
        return ((matchEnd - offsetStart) / (double)run.Text.Length) * totalRunW;
    }

    private MatchCollection BuildMatches(string text)
    {
        // Check TextSearchOptions at search time (may have been set after construction)
        var isRegex = _isRegex || (_textSearchOptions?.IsRegularExpression ?? false);
        var caseSensitive = _textSearchOptions is not null ? _textSearchOptions.CaseSensitive : _caseSensitive;
        // A Regex ctor's IgnoreCase is not undone by search options that merely
        // carry their CaseSensitive default (see _regexIgnoreCase).
        if (_regexIgnoreCase) caseSensitive = false;
        var wholeWord = _wholeWord || (_textSearchOptions?.WholeWord ?? false);
        return BuildMatchesCore(text, _searchPhrase!, isRegex, caseSensitive, wholeWord);
    }

    /// <summary>Matches one regex of the <c>Regex[]</c> ctor over the extracted
    /// text, exactly the way a single-Regex absorber would run it (pattern from
    /// <c>ToString()</c>, case sensitivity from its <c>IgnoreCase</c> option) —
    /// the per-regex results must equal what six sequential absorbers find.</summary>
    private MatchCollection BuildMatchesFor(System.Text.RegularExpressions.Regex rx, string text)
    {
        var caseSensitive = (rx.Options & RegexOptions.IgnoreCase) == 0;
        var wholeWord = _wholeWord || (_textSearchOptions?.WholeWord ?? false);
        return BuildMatchesCore(text, rx.ToString(), isRegex: true, caseSensitive, wholeWord);
    }

    private static MatchCollection BuildMatchesCore(string text, string searchPhrase,
        bool isRegex, bool caseSensitive, bool wholeWord)
    {
        var phrase = NormalizeArabicPresentationForms(searchPhrase);
        // For non-regex search, strip trailing \r that may come from splitting \r\n text by \n.
        // Newline sentinels are excluded from concatenated text in phrase mode, so trailing
        // \r would cause a false mismatch.
        if (!isRegex)
            phrase = phrase.TrimEnd('\r');
        var pattern = isRegex ? phrase : Regex.Escape(phrase);
        if (!isRegex)
        {
            // A literal space in the phrase matches the extraction's word-gap forms:
            // subset fonts with a ToUnicode that omits the space glyph decode it as
            // NBSP, and the run concatenation can add a synthetic gap space beside it —
            // so the extracted gap between "The" and "Offer" may be " ", " ", or
            // "  ". A needle gap of n spaces matches n space/NBSP
            // chars plus at most ONE trailing NBSP (the "synthetic space + NBSP
            // glyph" pair) - never an extra plain space, so genuine multi-space
            // column gaps don't fuse phrases that were separate before.
            // A needle whitespace run that CONTAINS a line break (a phrase quoted
            // from wrapped text, "with red \r\ncolor") matches ANY whitespace run
            // in the haystack: depending on the extraction path the line boundary
            // surfaces as a bare "\r\n" sentinel, a single joining space, or a
            // trailing-space + break combination.
            pattern = Regex.Replace(pattern, @"(?:\\ |\u00A0|\\r|\\n)+", m =>
            {
                var raw = m.Value.Replace("\\ ", " ").Replace("\\r", "\r").Replace("\\n", "\n");
                if (raw.IndexOf('\r') < 0 && raw.IndexOf('\n') < 0)
                {
                    int n = raw.Length;
                    return "[ \u00A0]{" + n + "}\u00A0?";
                }
                return "[ \u00A0\r\n]+";
            });
        }
        if (wholeWord)
            pattern = @"\b" + pattern + @"\b";
        var options = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
        // Enable multiline so ^ and $ match at line boundaries, not just string start/end.
        // This matches the .NET the public API behavior for regex text search.
        if (isRegex)
            options |= RegexOptions.Multiline;
        // Apply the global RegexManager settings: NonBacktracking guarantees linear-time
        // matching, and MatchTimeout bounds runaway (catastrophic-backtracking) patterns.
        if (RegexManager.NonBacktracking)
            options |= Compat.NonBacktracking;
        return new Regex(pattern, options, RegexManager.MatchTimeout).Matches(text);
    }

    /// <summary>Keep a fragment under a <c>TextSearchOptions.Rectangle</c> filter when its
    /// TOP-LEFT corner (start X, ascent line) lies inside the search rect, edges inclusive
    /// (a 715-vs-720 box top selects/deselects a run whose
    /// rect top is 719.8 while its baseline start is well inside both; and an overlay
    /// run far wider than the box still matches when its start corner is inside).
    /// Falls back to start-position containment without a bbox.</summary>
    private static bool FragmentInSearchRect(Rectangle searchRect, TextFragment frag)
    {
        var r = frag.Rectangle;
        if (r is not null)
        {
            const double eps = 0.01;
            // The RIGHT edge is strict: a run starting on (or past) the search
            // box's right edge paints entirely outside it — a column-fit leader
            // whose dots begin a fraction of a point past the box must not count.
            return searchRect.LLX - eps <= r.LLX && searchRect.URX > r.LLX
                && searchRect.LLY - eps <= r.URY && searchRect.URY + eps >= r.URY;
        }
        var pos = frag.PositionOrNull;
        return pos is not null && RectangleContainsPoint(searchRect, pos.XIndent, pos.YIndent);
    }

    /// <summary>
    /// Clip a text run to fit within a search rectangle (horizontal text only).
    /// Trims characters from left/right whose page-space X falls outside the rect.
    /// Uses CharCumWidths (which include Tc/Tw) for accurate character positions.
    /// </summary>
    /// <returns>The run text that survives the rectangle, where it starts (both axes: the
    /// surviving prefix advances along the text matrix, so a rotated run moves in Y) and how
    /// wide it is - the inputs unchanged when nothing is clipped, an empty text when nothing
    /// survives.</returns>
    private static (string text, double startX, double startY, double width) ClipRunToRect(RawTextRun run,
        Rectangle searchRect, string text, double startX, double startY, double width)
    {
        if (text.Length == 0) return (text, startX, startY, width);

        var (charPageX, charPageEnd) = RunCharPageSpans(run, text);

        // Use tight tolerance for left clip (include chars AT or after rect.LLX)
        // and loose tolerance for right clip.
        var rightTol = 0.5;

        // Find first character that starts within or near the rect left edge.
        // Include characters whose midpoint is within the rect (more than half
        // of the glyph is visible).
        int clipStart = 0;
        for (int i = 0; i < text.Length; i++)
        {
            var charMid = (charPageX[i] + charPageEnd[i]) * 0.5;
            if (charMid >= searchRect.LLX)
            {
                clipStart = i;
                break;
            }
            clipStart = i + 1;
        }

        // Find last character whose END position is within the rect right edge.
        // When even the first candidate character ends past the right edge, nothing
        // fits — clipEnd must collapse to clipStart (empty), not keep the whole tail.
        int clipEnd = clipStart;
        for (int i = text.Length - 1; i >= clipStart; i--)
        {
            if (charPageEnd[i] <= searchRect.URX + rightTol)
            {
                clipEnd = i + 1;
                break;
            }
        }

        if (clipStart >= clipEnd) return ("", startX, startY, width);
        if (clipStart == 0 && clipEnd == text.Length)
            return (text, startX, startY, width); // no clipping needed

        var (prefAdv, clipAdv) = ClippedAdvances(run, text, clipStart, clipEnd);
        return (text[clipStart..clipEnd], run.X + run.TmA * prefAdv * run.HScaling,
            run.Y + run.TmB * prefAdv * run.HScaling, clipAdv);
    }

    /// <summary>Page-space X where each character of a run starts (text.Length + 1 entries, the
    /// last being the run's end) and where its INK ends. A TJ kern after a glyph pushes the
    /// NEXT character's start far beyond this glyph's end (a token followed by a 20-em column
    /// hop), so the next start cannot stand in for the end: the closing bracket of a token
    /// would then look as if it ran to the next column and be clipped off, and the replace
    /// that follows would rewrite the wrong span. Without per-character advances the run's
    /// width is spread evenly.</summary>
    private static (double[] starts, double[] ends) RunCharPageSpans(RawTextRun run, string text)
    {
        var charPageX = new double[text.Length + 1];
        var charPageEnd = new double[text.Length];
        double PageX(double advance)
        {
            var (px, _) = ApplyCtm(run.X + run.TmA * advance * run.HScaling,
                run.Y + run.TmB * advance * run.HScaling, run.Ctm);
            return px;
        }
        if (run.CharCumWidths is not null && run.CharCumWidths.Length > text.Length)
        {
            for (int i = 0; i <= text.Length; i++) charPageX[i] = PageX(run.CharCumWidths[i]);
            var ends = run.CharEndPositions is not null && run.CharEndPositions.Length >= text.Length
                ? run.CharEndPositions : null;
            for (int i = 0; i < text.Length; i++)
                charPageEnd[i] = ends is null ? charPageX[i + 1] : PageX(ends[i]);
        }
        else
        {
            // MeasureString(string) can return wrong widths for custom-encoded fonts,
            // but run.Width (computed from MeasureString(bytes)) is accurate.
            var totalW = run.Width > 0 ? run.Width : EstimateWidth(text, run.FontSize);
            for (int i = 0; i <= text.Length; i++) charPageX[i] = PageX(totalW * i / text.Length);
            for (int i = 0; i < text.Length; i++) charPageEnd[i] = charPageX[i + 1];
        }
        return (charPageX, charPageEnd);
    }

    /// <summary>The advance before the first kept character and the width of the kept stretch.
    /// The width ends at the last kept character's INK end, not at the next character's start:
    /// a kern after the kept token would otherwise stretch the fragment over the column hop,
    /// and the rectangle recorded for a later rectangle-scoped search would take in the
    /// neighbouring token as well.</summary>
    private static (double prefix, double width) ClippedAdvances(RawTextRun run, string text, int clipStart, int clipEnd)
    {
        if (run.CharCumWidths is not null && run.CharCumWidths.Length > text.Length)
        {
            var keptEnd = run.CharEndPositions is not null && run.CharEndPositions.Length >= clipEnd
                ? run.CharEndPositions[clipEnd - 1] : run.CharCumWidths[clipEnd];
            return (run.CharCumWidths[clipStart], keptEnd - run.CharCumWidths[clipStart]);
        }
        // Proportional distribution from total run width.
        var totalW = run.Width > 0 ? run.Width : EstimateWidth(run.Text, run.FontSize);
        return (totalW * clipStart / run.Text.Length, totalW * (clipEnd - clipStart) / run.Text.Length);
    }

    private static double EstimateWidth(string text, double fontSize)
    {
        return text.Length * fontSize * 0.5;
    }
}
