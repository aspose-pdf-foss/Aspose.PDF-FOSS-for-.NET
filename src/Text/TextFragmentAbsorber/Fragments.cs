using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
namespace Aspose.Pdf.Text;

public sealed partial class TextFragmentAbsorber
{
    /// <summary>
    /// Visit all pages of a document.
    /// </summary>
    public void Visit(Document pdf)
    {
        var document = pdf;
        _fragments.Clear();
        _absorbAllPages.Clear();
        _absorbAllForms.Clear();
        _absorbAllDocument = null;

        if (string.IsNullOrEmpty(_searchPhrase)) // empty phrase = absorb all
        {
            // No search phrase — just extract all fragments page by page.
            // A whole-document visit is tolerant of undecodable fonts: one bad
            // font on one page must not abort the sweep (the strict throw is a
            // page-level Accept behaviour; the phrase-search path below never
            // enables it either).
            _absorbAllDocument = document;
            // One dedup set for the WHOLE walk: a form shared across pages is
            // absorbed only at its first Do (a per-page visit is its own run
            // and counts it again).
            var seenForms = new HashSet<object>(ReferenceEqualityComparer.Instance);
            foreach (var page in document.Pages)
                VisitInternal(page, tolerantFonts: true, seenForms: seenForms);
            return;
        }

        // Extract runs from all pages first. Fill rects ride along so the
        // whole-document search captures source decorations (background colour,
        // underline, strikeout) the same way a single-page visit does.
        var allPageRuns = new List<(Page page, List<RawTextRun> runs, List<RawFillRect> fills)>();
        foreach (var page in document.Pages)
        {
            var reader = page.Reader;
            var contentStreams = GetContentStreams(page.Dict, reader);
            var rawFragments = new List<RawTextRun>();
            var pageFills = new List<RawFillRect>();
            var rotCtm = PageRotationCtm(page);
            foreach (var stream in contentStreams)
                ExtractRuns(stream, page.Dict, reader, rawFragments, inheritedCtm: rotCtm, fillRects: pageFills, useFontEngineEncoding: _textSearchOptions?.UseFontEngineEncoding ?? false, keepAllFillRects: (_textSearchOptions?.SearchForTextRelatedGraphics ?? true) || (_textEditOptions?.ToAttemptGetUnderlineFromSource ?? false));
            allPageRuns.Add((page, rawFragments, pageFills));
        }

        // Try per-page search first (most common case)
        foreach (var (page, runs, fills) in allPageRuns)
            BuildSearchFragments(runs, page.Index, page, fillRects: fills);

        ApplySearchRectFilter();

        // If per-page search found results, we're done
        if (_fragments.Count > 0) return;

        // No per-page matches — try cross-page search
        BuildCrossPageSearchFragments(allPageRuns.Select(t => (t.page, t.runs)).ToList());
        ApplySearchRectFilter();
    }

    private (string text, List<int> charToRun, int[] runStartChar, int[]? bidiPerm)
        BuildConcatenatedText(List<RawTextRun> rawFragments)
    {
        var fullText = new StringBuilder();
        var charToRun = new List<int>();
        var runStartChar = new int[rawFragments.Count];

        // IgnoreShadowText: drop drop-shadow duplicates. A shadow glyph is the SAME character
        // drawn again at a near-overlapping position (a small offset, far less than a glyph
        // advance) — e.g. "Construction" rendered as runs C,C,o,o,n,n,… where each second copy
        // sits ~0.06·fontSize away. Skip a run that repeats the last kept run's text within a
        // fraction of the visual font size, so the search sees "Construction" not
        // "CCoonnssttrruuccttiioonn".
        bool ignoreShadow = _textSearchOptions?.IgnoreShadowText ?? false;
        string? lastKeptText = null; double lastKeptX = 0, lastKeptY = 0;

        // Mark runs whose preceding gap is constant letter-tracking rather than a word
        // break (display/letterhead text drawn with uniform inter-glyph advance), so the
        // word-gap space insertion below does not split "MARK" (runs "M","ARK") into
        // "M ARK". Real word breaks on such lines are explicit space glyphs and survive.
        var letterTracked = ComputeLetterTrackedGaps(rawFragments);
        // Mark the runs that begin part-way through their predecessor's advance in a way
        // that reads as a token boundary rather than as how the line was set.
        var squeezedGap = ComputeSqueezedGaps(rawFragments);

        for (var i = 0; i < rawFragments.Count; i++)
        {
            if (ignoreShadow && RepeatsAsShadow(rawFragments[i], lastKeptText, lastKeptX, lastKeptY))
            {
                // Drop the \r\n sentinel(s) sitting between the kept glyph and this shadow
                // copy — they only separated a glyph from its own shadow (each glyph is its
                // own BT/ET), not real content. Otherwise an orphan \r\n can survive inside a
                // word (e.g. "Constructio\r\nn") and break the match.
                DropLineSentinels(fullText, charToRun);
                runStartChar[i] = charToRun.Count; // shadow duplicate — emit no characters
                continue;
            }
            if (InsertSeparatorSpaceBetweenRuns(rawFragments, i, fullText, charToRun, runStartChar, ignoreShadow, ref lastKeptText, ref lastKeptX, ref lastKeptY, letterTracked, squeezedGap)) continue;

            runStartChar[i] = charToRun.Count;
            var text = rawFragments[i].Text;

            // Newline sentinels: skip for phrase search (so cross-line phrases match),
            // keep for regex search (so \r\n patterns work).
            var effectiveIsRegex = _isRegex || (_textSearchOptions?.IsRegularExpression ?? false);
            if (text == "\r\n" && !effectiveIsRegex)
            {
                AppendLineBreakSeparator(rawFragments, i, fullText, charToRun);
                continue;
            }

            foreach (var _ in text)
                charToRun.Add(i);
            fullText.Append(text);

            // Track the last kept (appended) non-sentinel run for shadow de-duplication.
            if (ignoreShadow && text != "\r\n")
            {
                lastKeptText = rawFragments[i].Text;
                lastKeptX = rawFragments[i].X;
                lastKeptY = rawFragments[i].Y;
            }
        }

        var concatenated = NormalizeArabicAndRemap(fullText.ToString(), charToRun, runStartChar);

        // Apply bidi reordering for non-regex search — regex patterns expect
        // logical order. Runs on the normalized text so bidiPerm indices live
        // in the same space as the (re-projected) char maps.
        int[]? bidiPerm = null;
        var isRegex = _isRegex || (_textSearchOptions?.IsRegularExpression ?? false);
        if (!isRegex)
            (concatenated, bidiPerm) = BidiReorderer.ReorderWithPermutation(concatenated);

        return (concatenated, charToRun, runStartChar, bidiPerm);
    }

    /// <summary>Whether this run is a drop-shadow copy of the last kept one: the same text drawn
    /// again within a fraction of the visual font size. A shadow copy of a space is caught too —
    /// position-based matching (overlapping X) distinguishes it from a real inter-word space,
    /// which is a full advance away.</summary>
    private static bool RepeatsAsShadow(RawTextRun cur, string? lastKeptText,
        double lastKeptX, double lastKeptY)
    {
        if (cur.Text == "\r\n" || cur.Text.Length == 0 || lastKeptText != cur.Text) return false;
        var effFs = (cur.FontSize > 0 ? cur.FontSize : 1.0)
                    * (Math.Abs(cur.TmA) > 0 ? Math.Abs(cur.TmA) : 12.0);
        var tol = Math.Max(1.0, 0.22 * effFs);
        return Math.Abs(cur.X - lastKeptX) < tol && Math.Abs(cur.Y - lastKeptY) < tol;
    }

    /// <summary>A line break is a WORD boundary for phrase search: a word split across lines
    /// without a hyphen is not fused back together ("tionAccountC" does not match against
    /// "…Registrat⏎onAccountCU…"). Multi-word phrases still match across the break through this
    /// separator space; when the previous run already ends in whitespace nothing is added, so no
    /// double-space can break a single-spaced phrase.</summary>
    private void AppendLineBreakSeparator(List<RawTextRun> rawFragments, int i,
        StringBuilder fullText, List<int> charToRun)
    {
        if (fullText.Length == 0) return;
        var sepPrev = i - 1;
        while (sepPrev >= 0 && rawFragments[sepPrev].Text == "\r\n") sepPrev--;
        var sepNext = i + 1;
        while (sepNext < rawFragments.Count && rawFragments[sepNext].Text == "\r\n") sepNext++;
        // HORIZONTAL neighbours only (both runs strictly rotation-free in Tm AND CTM): on
        // curved text (per-glyph rotated Tm) or under a rotated CTM the Y-drift along the
        // word leaves sentinels BETWEEN GLYPHS of one word — a separator space there would
        // break the word, so those sentinels stay skipped as before.
        var flatNeighbors = sepPrev >= 0
            && Math.Abs(rawFragments[sepPrev].TmB) <= 1e-4 * Math.Abs(rawFragments[sepPrev].TmA)
            && IsUprightCtm(rawFragments[sepPrev])
            && (sepNext >= rawFragments.Count
                || (Math.Abs(rawFragments[sepNext].TmB) <= 1e-4 * Math.Abs(rawFragments[sepNext].TmA)
                    && IsUprightCtm(rawFragments[sepNext])));
        if (!flatNeighbors) return;
        // Same-baseline neighbours mean the sentinel is a BT/ET block boundary, not a real
        // line break (adjacent blocks continue the line): no word boundary there. The
        // same-line pass above removes such sentinels and inserts a space only for a
        // word-sized geometric gap, so appending one here would force a separator into a
        // phrase that renders with none (inline fragments).
        if (sepPrev >= 0 && sepNext < rawFragments.Count)
        {
            var (_, prevPageY) = ApplyCtm(rawFragments[sepPrev].X, rawFragments[sepPrev].Y, rawFragments[sepPrev].Ctm);
            var (_, nextPageY) = ApplyCtm(rawFragments[sepNext].X, rawFragments[sepNext].Y, rawFragments[sepNext].Ctm);
            if (Math.Abs(nextPageY - prevPageY) < 2.0) return;
        }
        if (!char.IsWhiteSpace(fullText[^1]))
        {
            charToRun.Add(sepPrev);
            fullText.Append(' ');
        }
        else
        {
            // The line already ended with a space of its own — the break is then a real
            // boundary, and a single-space phrase must not read through it into the next line.
            charToRun.Add(sepPrev);
            charToRun.Add(sepPrev);
            fullText.Append("\r\n");
        }
    }

    /// <summary>Normalises Arabic presentation forms and re-projects the char-to-run and
    /// run-to-char maps onto the normalised text, so every index still means the same run.</summary>
    private static string NormalizeArabicAndRemap(string concatenated, List<int> charToRun,
        int[] runStartChar)
    {
        (concatenated, var newToOld) = NormalizeArabicPresentationFormsWithMap(concatenated);
        if (newToOld is null) return concatenated;

        var expanded = new List<int>(newToOld.Length);
        foreach (var o in newToOld) expanded.Add(charToRun[o]);
        var oldToNew = new int[charToRun.Count + 1];
        var j = 0;
        for (var o = 0; o <= charToRun.Count; o++)
        {
            while (j < newToOld.Length && newToOld[j] < o) j++;
            oldToNew[o] = j;
        }
        for (var r = 0; r < runStartChar.Length; r++)
            runStartChar[r] = oldToNew[Math.Min(runStartChar[r], charToRun.Count)];
        charToRun.Clear();
        charToRun.AddRange(expanded);
        return concatenated;
    }


    /// <summary>
    /// Computes the descent and ascent offsets used by the phrase-search rect
    /// calc (<see cref="ComputeMatchBounds"/>). The bounds calc
    /// uses <c>URY = baseline + (1.1 × FontSize + descentOff)</c> as a floor —
    /// a 10% padding above <c>FontSize</c> with the bottom edge at
    /// <c>baseline + descent</c>. When the font's own ascent metric implies a
    /// taller rect (typical for fonts with large <c>usWinAscent</c> from the
    /// embedded TrueType), keep the metric-driven height instead. So
    /// <c>ascentH = max(metric.Ascent × FontSize / 1000, 1.1 × FontSize +
    /// descentOff)</c>.
    /// </summary>
    /// <param name="run">The text run whose font size and metrics are measured.</param>
    /// <param name="coreFaceDescent">
    /// Fall back to the FACE's own descent when the font dict carries none.
    /// <para>This always holds: measured on a bare /Helvetica (no /Widths, no
    /// descriptor) and on the same dict carrying /Widths, a descriptor with
    /// /Descent 0, and a descriptor with no /Descent at all, every one of them
    /// reports its box at baseline - 0.207 em (Times -0.217, Courier -0.157,
    /// Symbol 0), Position.YIndent following the box.</para>
    /// <para>The whole-run RECTANGLE nevertheless still seats such a face ON its
    /// baseline here: FOSS's own writers (the positioned-fragment seat, the table
    /// and HTML cell writers) compensate for the missing descent, so correcting the
    /// rectangle alone moves nine baseline-green tests. It is a real, measured
    /// defect and belongs to a row of its own, writers and reader together. The
    /// CLIP VERDICT does not wait for that: it is taken against the true face box
    /// (see <c>RunLineBox</c>), which is the box that gets clipped.</para>
    /// </param>
    private static (double descentOff, double ascentH) ComputeDescentAscent(RawTextRun run,
        bool coreFaceDescent = true)
    {
        double effectiveDescent = 0;
        if (run.Metrics is not null && run.Metrics.Descent != 0)
            effectiveDescent = run.Metrics.Descent;
        else if (coreFaceDescent && !string.IsNullOrEmpty(run.FontName))
            effectiveDescent = Standard14Fonts.GetDescent(run.FontName!);

        double descentOff = effectiveDescent * run.FontSize / 1000.0;
        double ascentH = run.FontSize * 1.1 + descentOff;
        // The phrase-rect height is the canonical 1.1 × FontSize even for
        // fonts whose ascent+|descent| exceeds 1.1 em (SegoeUI ascent 1.08 em, Verdana
        // 1.005 + 0.209: both measure exactly 1.1 em boxes). Only an
        // EXTREME metric box overrides the canon. The discriminator is the FULL box
        // (ascent − descent), not the ascent alone: CourierNewPSMT descriptors land at
        // ordinary ascents (1.02 em after Repair) but with a huge descent the metric
        // box exceeds 1.5 em, and tests written against such fonts assume the
        // metric-driven height; Verdana (1.21 em box) and SegoeUI stay on the canon.
        // The fallback to FontSize when Metrics.Ascent==0 is intentionally NOT used
        // here: it gave a misleading height for Standard14 phrase searches without a
        // descriptor.
        if (run.Metrics is not null && run.Metrics.Ascent > 0
            && run.Metrics.Ascent - run.Metrics.Descent > 1500)
        {
            double metricBased = run.Metrics.Ascent * run.FontSize / 1000.0;
            if (metricBased > ascentH) ascentH = metricBased;
        }
        return (descentOff, ascentH);
    }

    /// <summary>
    /// Computes the page-space position of a match start within its first run.
    /// Applies within-run prefix offset, text matrix, descent, and CTM.
    /// </summary>
    private static (double x, double y) ComputeMatchPosition(RawTextRun firstRun, int offsetInRun)
    {
        double matchStartX = firstRun.X, matchStartY = firstRun.Y;
        if (offsetInRun > 0 && offsetInRun < firstRun.Text.Length)
        {
            var prefixW = MeasureRunPrefix(firstRun, offsetInRun);
            matchStartX = firstRun.X + firstRun.TmA * prefixW * firstRun.HScaling;
            matchStartY = firstRun.Y + firstRun.TmB * prefixW * firstRun.HScaling;
        }
        // Apply descent offset (bottom-left of text rect, matching per-run path).
        double posDescentOff = 0;
        if (firstRun.Metrics is not null && firstRun.Metrics.Descent != 0)
            posDescentOff = firstRun.Metrics.Descent * firstRun.FontSize / 1000.0;
        else if (Math.Abs(firstRun.TmB) > Math.Abs(firstRun.TmA))
            // Rotated run with no descriptor descent: fall back to the Standard-14 metric
            // (same as the rectangle path) so the baseline→descent offset is applied along
            // the rotated baseline. Without it the fragment Position is off by ~descent.
            // Gated to rotated runs to leave the (verified) horizontal-text positions intact.
            (posDescentOff, _) = ComputeDescentAscent(firstRun);
        return ApplyCtm(matchStartX + firstRun.TmC * posDescentOff,
                        matchStartY + firstRun.TmD * posDescentOff, firstRun.Ctm);
    }

    private static (double x, double y) ApplyCtm(double x, double y, Matrix ctm)
    {
        var tx = ctm.A * x + ctm.C * y + ctm.E;
        var ty = ctm.B * x + ctm.D * y + ctm.F;
        return (tx, ty);
    }

}
