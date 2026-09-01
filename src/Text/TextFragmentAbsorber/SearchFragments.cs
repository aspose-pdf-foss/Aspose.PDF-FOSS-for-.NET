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
        // Concatenate text from all pages with \r\n between pages
        var fullText = new StringBuilder();
        // Track: for each char position, which page and which run within that page
        var charMap = new List<(int pageIdx, int runIdx)>();
        var pageRunStartChars = new List<List<int>>(); // per page, per run: start char index

        for (int pi = 0; pi < allPageRuns.Count; pi++)
        {
            var (page, runs) = allPageRuns[pi];
            var runStarts = new List<int>();
            pageRunStartChars.Add(runStarts);

            // Insert page separator (except before first page)
            if (pi > 0 && fullText.Length > 0)
            {
                fullText.Append("\r\n");
                charMap.Add((-1, -1)); // \r
                charMap.Add((-1, -1)); // \n
            }

            for (int ri = 0; ri < runs.Count; ri++)
            {
                // Space insertion between runs on the same line
                if (ri > 0 && runs[ri].Text != "\r\n" && runs[ri - 1].Text != "\r\n")
                {
                    var prev = runs[ri - 1];
                    var deltaY = Math.Abs(runs[ri].Y - prev.Y);
                    if (deltaY < 2.0)
                    {
                        var prevEndX = prev.X + (prev.Width > 0 ? prev.Width * prev.HScaling : EstimateWidth(prev.Text, prev.FontSize));
                        var gap = runs[ri].X - prevEndX;
                        var fontSize = runs[ri].FontSize > 0 ? runs[ri].FontSize : 12.0;
                        var spaceThreshold = fontSize * 0.2;
                        var maxGap = fontSize * 3.0;
                        var lastChar = fullText.Length > 0 ? fullText[^1] : '\0';
                        var nextChar = runs[ri].Text.Length > 0 ? runs[ri].Text[0] : '\0';
                        // Require a real gap and avoid spacing inside letter-spaced words,
                        // where EVERY run is a single character. The earlier `both runs >= 2
                        // chars` rule was too strict: it also dropped the space at a word↔
                        // single-char-token boundary (e.g. "level" -> "1"), so a phrase search
                        // for "Heading level 1" failed to match the extracted "Heading level1".
                        // Suppress the space only when BOTH sides are single
                        // characters (the genuine letter-spacing case).
                        if (gap > spaceThreshold && gap <= maxGap && fullText.Length > 0
                            && lastChar != ' ' && lastChar != '\n' && nextChar != ' '
                            && (prev.Text.Length >= 2 || runs[ri].Text.Length >= 2))
                        {
                            charMap.Add((pi, ri - 1));
                            fullText.Append(' ');
                        }
                    }
                }

                runStarts.Add(charMap.Count);
                var text = runs[ri].Text;
                // Keep newlines for regex
                foreach (var _ in text)
                    charMap.Add((pi, ri));
                fullText.Append(text);
            }
        }

        var concatenated = fullText.ToString();
        // Normalize with map re-projection — see BuildConcatenatedText for why
        // matching normalized text against the original maps is unsound.
        concatenated = NormalizeArabicPresentationFormsWithMap(concatenated, out var xNewToOld);
        if (xNewToOld is not null)
        {
            var expanded = new List<(int pageIdx, int runIdx)>(xNewToOld.Length);
            foreach (var o in xNewToOld) expanded.Add(charMap[o]);
            var oldToNew = new int[charMap.Count + 1];
            var jj = 0;
            for (var o = 0; o <= charMap.Count; o++)
            {
                while (jj < xNewToOld.Length && xNewToOld[jj] < o) jj++;
                oldToNew[o] = jj;
            }
            foreach (var starts in pageRunStartChars)
                for (var r = 0; r < starts.Count; r++)
                    starts[r] = oldToNew[Math.Min(starts[r], charMap.Count)];
            charMap = expanded;
        }

        var matches = BuildMatches(concatenated);

        foreach (Match match in matches)
        {
            if (match.Length == 0) continue;

            var startIdx = match.Index;
            var endIdx = match.Index + match.Length - 1;
            if (startIdx >= charMap.Count || endIdx >= charMap.Count) continue;

            // Find the first valid page/run for the match start
            var (startPageIdx, startRunIdx) = charMap[startIdx];
            // Skip separators
            while (startPageIdx < 0 && startIdx <= endIdx)
            {
                startIdx++;
                if (startIdx < charMap.Count) (startPageIdx, startRunIdx) = charMap[startIdx];
            }
            if (startPageIdx < 0) continue;

            var startPage = allPageRuns[startPageIdx].page;
            var startRuns = allPageRuns[startPageIdx].runs;
            if (startRunIdx < 0 || startRunIdx >= startRuns.Count) continue;
            var firstRun = startRuns[startRunIdx];

            // Position from first run
            var (posX, posY) = ApplyCtm(firstRun.X, firstRun.Y, firstRun.Ctm);

            // Effective font size
            var upX_ = firstRun.TmC * firstRun.Ctm.A + firstRun.TmD * firstRun.Ctm.C;
            var upY_ = firstRun.TmC * firstRun.Ctm.B + firstRun.TmD * firstRun.Ctm.D;
            var tmScale = Math.Sqrt(upX_ * upX_ + upY_ * upY_);
            var effectiveFs = tmScale > 0.001 && Math.Abs(tmScale - 1.0) > 0.001
                ? firstRun.FontSize * tmScale : firstRun.FontSize;

            var textState = new TextState
            {
                FontSize = (float)effectiveFs,
                FontName = firstRun.FontName,
                RenderingMode = (Aspose.Pdf.Text.TextRenderingMode)firstRun.RenderingMode,
                LineWidth = firstRun.LineWidth,
                IsBold = firstRun.IsBold,
                IsItalic = firstRun.IsItalic,
                Font = firstRun.FontInfoObj ?? FontInfo.DefaultHelvetica,
                TextRise = firstRun.TextRise,
                IsSuperscript = firstRun.TextRise > 0,
                IsSubscript = firstRun.TextRise < 0,
            };
            textState.SetCapturedForegroundColor(ForegroundColorOf(firstRun));
            textState.StrokingColor = firstRun.StrokingColor;

            // Simple bounding rect from first run
            var w = firstRun.Width > 0 ? firstRun.Width : EstimateWidth(firstRun.Text, firstRun.FontSize);
            var h = firstRun.FontSize;
            var (px2, py2) = ApplyCtm(firstRun.X + w, firstRun.Y + h, firstRun.Ctm);
            var rect = new Rectangle(
                Math.Min(posX, px2), Math.Min(posY, py2),
                Math.Max(posX, px2), Math.Max(posY, py2));

            // Only the ANISOTROPIC part of the matrix is a horizontal scale. A matrix
            // that scales both axes alike carries the font size (a "1 Tf" run sized
            // by "7 0 0 7 Tm"), and that size is already in TextState.FontSize.
            textState.SourceTmScale = Math.Abs(firstRun.TmD) > 1e-9
                ? firstRun.TmA / firstRun.TmD
                : 1.0;
            var fragment = new TextFragment(LogicalizeRtlPresentationForms(match.Value), rect, textState)
            {
                PageIndex = startPage.Index,
                Position = new Position(Q(posX), Q(posY)),
                SourcePage = startPage,
                SourceXObjStream = firstRun.SourceXObj,
                ExtractionCtm = new Aspose.Pdf.Matrix(firstRun.Ctm.A, firstRun.Ctm.B, firstRun.Ctm.C, firstRun.Ctm.D, firstRun.Ctm.E, firstRun.Ctm.F),
                ExtractionTmTy = firstRun.TmBaseY,
            };

            _fragments.Add(fragment);
        }
    }

    private void BuildSearchFragments(List<RawTextRun> rawFragments, int pageIndex,
        Page? sourcePage = null, XForm? sourceForm = null, List<RawFillRect>? fillRects = null)
    {
        SplitRunsAtCharGaps(rawFragments);
        // Flatten formatting mode orders the SEARCH TEXT by reading position, not
        // stream order — a pattern spanning lines (a bracketed block whose closing
        // half is drawn earlier in the stream) only pairs up in reading order.
        if (ExtractionOptions?.FormattingMode == TextExtractionOptions.TextFormattingMode.Flatten)
            rawFragments = ReorderRunsForFlatten(rawFragments);
        var preCountAll = _fragments.Count;
        // Later-text occlusion + clipped-away detection (stacked duplicate draws,
        // strip-clipped multi-pass pages): search matches report Invisible when
        // every spanned run is hidden, same as full extraction.
        var (laterInk, clippedAway, runBoxArea) = ComputeLaterInkOcclusion(rawFragments);
        // Phase 1: Build the concatenated text and character-to-run mapping
        var (concatenated, charToRun, runStartChar, bidiPerm) = BuildConcatenatedText(rawFragments);
        if (SearchDebug)
            Console.Error.WriteLine($"[searchtext:page{pageIndex}]<<<{concatenated}>>>");

        // Index the fill rects once so the per-match decoration probes below query a
        // baseline-local slice instead of rescanning the whole (possibly huge) list.
        var fillIndex = fillRects is { Count: > 0 } ? new FillRectIndex(fillRects) : null;

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
                EmitMatches(BuildMatchesFor(rx, concatenated), rxPre);
                for (var fi = rxPre; fi < _fragments.Count; fi++)
                    bucket.Add(_fragments.GetInternal(fi));
            }
            return;
        }
        EmitMatches(BuildMatches(concatenated), preCountAll);
        return;

        // Phase 3: for each match build a TextFragment with position, rect and
        // segments; ends with the same-phrase RTL reorder over [preCount..).
        void EmitMatches(MatchCollection matches, int preCount)
        {
        foreach (Match match in matches)
        {
            if (match.Length == 0)
            {
                // A zero-length regex match (lookarounds, optional groups) is still a
                // result: an empty fragment positioned at the match
                // point.
                var anchorIdx = bidiPerm is not null && match.Index < bidiPerm.Length
                    ? bidiPerm[match.Index] : match.Index;
                var empty = new TextFragment(string.Empty)
                {
                    PageIndex = pageIndex,
                    SourcePage = sourcePage,
                    Form = sourceForm,
                };
                if (anchorIdx < charToRun.Count)
                {
                    var runIdx = charToRun[anchorIdx];
                    var (ex, ey) = ComputeMatchPosition(rawFragments[runIdx], anchorIdx - runStartChar[runIdx]);
                    empty.Position = new Position(Q(ex), Q(ey));
                }
                _fragments.Add(empty);
                continue;
            }

            // Map match indices back through bidi permutation if reordering was applied
            var startCharIdx = bidiPerm is not null ? bidiPerm[match.Index] : match.Index;
            var endCharIdx = bidiPerm is not null
                ? bidiPerm[match.Index + match.Length - 1]
                : match.Index + match.Length - 1;
            if (startCharIdx > endCharIdx)
                (startCharIdx, endCharIdx) = (endCharIdx, startCharIdx);

            if (startCharIdx >= charToRun.Count || endCharIdx >= charToRun.Count)
            {
                _fragments.Add(new TextFragment(LogicalizeRtlPresentationForms(match.Value)) { PageIndex = pageIndex, SourcePage = sourcePage, Form = sourceForm });
                continue;
            }

            var firstRunIdx = charToRun[startCharIdx];
            var lastRunIdx = charToRun[endCharIdx];
            // A back-jump PREPEND (see BuildConcatenatedText) makes run indexes
            // non-monotonic in char space: the match can START in a later-drawn run
            // and END in an earlier one. Segment/bounds builders walk an ordered
            // range, so normalise to [min, max].
            if (firstRunIdx > lastRunIdx)
                (firstRunIdx, lastRunIdx) = (lastRunIdx, firstRunIdx);

            // Compute bounding rectangle spanning all involved runs
            var rect = ComputeMatchBounds(rawFragments, runStartChar,
                firstRunIdx, lastRunIdx, startCharIdx, endCharIdx);

            // Compute position, text state, and trailing Tc for the fragment
            var (posX, posY) = ComputeMatchPosition(rawFragments[firstRunIdx],
                startCharIdx - runStartChar[firstRunIdx]);
            var firstRun = rawFragments[firstRunIdx];
            var textState = BuildTextState(firstRun);
            // A match is hidden when the HIDDEN AREA of its spanned runs — covered
            // by later ink or clipped away — carries the majority of the glyph
            // area. Area-weighted, not all-runs: a word straddling two clip
            // strips is hidden in the pass that shows only its short tail, but
            // visible in the pass that shows most of it.
            double hiddenArea = 0, totalArea = 0;
            for (var ri = firstRunIdx; ri <= lastRunIdx && ri < laterInk.Length; ri++)
            {
                if (rawFragments[ri].Text == "\r\n") continue;
                var a = runBoxArea[ri];
                totalArea += a;
                if (laterInk[ri] || clippedAway[ri]) hiddenArea += a;
            }
            if (totalArea > 0 && hiddenArea > totalArea * 0.5)
                textState.SetCapturedOccluded(true);
            var trailingTc = ComputeTrailingTc(rawFragments, runStartChar, lastRunIdx, endCharIdx);

            // Text direction in page space
            var sTdx = firstRun.Ctm.A * firstRun.TmA + firstRun.Ctm.C * firstRun.TmB;
            var sTdy = firstRun.Ctm.B * firstRun.TmA + firstRun.Ctm.D * firstRun.TmB;
            var sRot = RotationFromDirection(sTdx, sTdy);
            if (sRot.HasValue) textState.Rotation = sRot.Value;

            // Only the ANISOTROPIC part of the matrix is a horizontal scale. A matrix
            // that scales both axes alike carries the font size (a "1 Tf" run sized
            // by "7 0 0 7 Tm"), and that size is already in TextState.FontSize.
            textState.SourceTmScale = Math.Abs(firstRun.TmD) > 1e-9
                ? firstRun.TmA / firstRun.TmD
                : 1.0;
            // The text a match REPORTS is in logical (reading) order. Which conversion
            // gets it there depends on the frame `match.Value` came from: when the page
            // carried RTL and the concatenation was bidi-reordered (bidiPerm non-null)
            // the value is already logical; otherwise it is still in DRAWN order — the
            // regex path deliberately searches drawn order — and the run reverses.
            var absorbedText = bidiPerm is not null
                ? match.Value
                : LogicalizeRtlPresentationForms(match.Value);
            var fragment = new TextFragment(absorbedText, rect, textState)
            {
                PageIndex = pageIndex,
                Position = new Position(Q(posX), Q(posY)),
                SourcePage = sourcePage,
                Form = sourceForm,
                SourceXObjStream = firstRun.SourceXObj,
                TextDirX = sTdx, TextDirY = sTdy,
                ExtractionCtm = new Aspose.Pdf.Matrix(firstRun.Ctm.A, firstRun.Ctm.B,
                    firstRun.Ctm.C, firstRun.Ctm.D, firstRun.Ctm.E, firstRun.Ctm.F),
                ExtractionTmTy = firstRun.TmBaseY,
                TrailingTcPageSpace = trailingTc,
                ReplaceOptions = TextReplaceOptions,
            };

            RawFillRect? capturedUl = null;
            RawFillRect? capturedBg = null;
            if (fillIndex is not null)
            {
                var (_, baselineY) = ApplyCtm(firstRun.X, firstRun.Y, firstRun.Ctm);
                bool wantSourceDecorations = _textEditOptions?.ToAttemptGetUnderlineFromSource ?? false;
                // Same default as the absorb-all path above: underline capture follows
                // TextSearchOptions' own default (on) when no options were supplied.
                bool wantUnderline = (_textSearchOptions?.SearchForTextRelatedGraphics ?? true)
                    || wantSourceDecorations;
                // Same rule as the absorb-all path: a fill rect containing the match's
                // baseline midpoint supplies TextState.BackgroundColor (later draw
                // order wins). The midpoint — not the start edge — probes: a source
                // highlight is often drawn a hair inside the first glyph's origin.
                if (_textSearchOptions?.SearchForTextRelatedGraphics ?? true)
                {
                    var midX = rect.LLX + rect.Width / 2;
                    var bgHit = fillIndex.FindTopMatch(baselineY - FillRectIndex.Margin, baselineY + FillRectIndex.Margin,
                        fr => midX >= fr.Llx && midX <= fr.Urx && baselineY >= fr.Lly && baselineY <= fr.Ury);
                    // The fragment snapshot-copied the built TextState at construction —
                    // the capture must land on the fragment's own state object.
                    if (bgHit is { } bgh) fragment.TextState.SetCapturedBackgroundColor(bgh.FillColor);
                }
                if (wantUnderline)
                {
                    capturedUl = DetectUnderlineRect(rect, baselineY, textState.FontSize, fillIndex);
                    // Like the background above: the fragment snapshot-copied the built
                    // TextState, so the capture must land on the fragment's own state.
                    if (capturedUl is not null) fragment.TextState.SetCapturedUnderline(true);
                }
                // Source-highlight capture: lets a later text replacement splice the old
                // background rect out and re-draw it at the replacement's width. Gated with
                // the RULE, not with ToAttemptGetUnderlineFromSource alone: text-related
                // graphics already hand the caller the highlight's COLOUR, and a caller that
                // then sets BackgroundColor is replacing that highlight - painting the new
                // one while the old rect still stands leaves the old one on top.
                if (wantUnderline)
                    capturedBg = DetectBackgroundRect(rect, baselineY, textState.FontSize, fillIndex);
                if (DetectStrikeoutRect(rect, baselineY, textState.FontSize, fillIndex) is not null)
                    fragment.TextState.SetCapturedStrikeOut(true);
            }

            // Build per-run segments with position and rectangle
            BuildFragmentSegments(fragment, rawFragments, runStartChar,
                firstRunIdx, lastRunIdx, startCharIdx, endCharIdx, charToRun);

            // ★ Segments are per-GLYPH-RUN, so they are necessarily in DRAWN order, and
            // adding them re-joins the fragment's text from them — which for an RTL run
            // silently hands back the reading order REVERSED. The segments keep drawn
            // order (their positions describe where the glyphs sit); the fragment's Text
            // is the reported reading order, so restore it.
            if (BidiReorderer.ContainsRtl(absorbedText))
                fragment.SetAbsorbedText(absorbedText);

            // A regex match can span a line break: the matched text carries the
            // \r\n sentinel, but segments cover only glyph runs, so the segment
            // join (which each Segments.Add refreshed _text to) loses it. Keep
            // the matched text — the break belongs in Text — but
            // only for an INTERIOR break: a match that merely ends (or starts)
            // on the sentinel (e.g. pattern "RTF\s[\r\n]") reads back without it.
            var matchTrimmed = match.Value.Trim('\r', '\n');
            if (matchTrimmed.IndexOf('\r') >= 0 || matchTrimmed.IndexOf('\n') >= 0)
                fragment.SetAbsorbedText(LogicalizeRtlPresentationForms(matchTrimmed));
            // INTERIOR junction spaces synthesised during line assembly (word gaps,
            // back-jump splices) belong to no glyph run, so the segment join drops
            // them. The full matched text is reported — restore it when the
            // join lost characters. BOUNDARY spaces stay off (a match that merely
            // starts/ends on a junction space reads back without it, same as the
            // sentinel rule above).
            else
            {
                // Only the SYNTHETIC boundary spaces come off. A space the match starts
                // or ends on that a glyph run actually drew is part of the text and must
                // survive — trimming every one of them silently shortened lines that
                // open and close on real space glyphs the moment any interior junction
                // space sent them down this path.
                var matchInner = TrimSynthesizedEdges(matchTrimmed, fragment.Text);
                if (fragment.Segments.Count > 0
                    && matchInner.Length > fragment.Text.Length
                    && string.Equals(fragment.Text.Replace(" ", ""),
                           matchInner.Replace(" ", ""), StringComparison.Ordinal))
                    fragment.SetAbsorbedText(LogicalizeRtlPresentationForms(matchInner));
            }

            if (capturedUl is { } ulr)
            {
                fragment.MarkCapturedUnderlineSource(ulr.RawX, ulr.RawY, ulr.RawW, ulr.RawH);
                fragment.CapturedUnderlinePageRect = (ulr.Llx, ulr.Lly, ulr.Urx, ulr.Ury);
                // What the source rule covers BEYOND the match: the tail of the last
                // spanned run, and where that run ends. A replacement re-seats the tail
                // at its own advance; switching the underline off leaves it underlined.
                var tailRun = rawFragments[lastRunIdx];
                var tailFrom = endCharIdx - runStartChar[lastRunIdx] + 1;
                fragment.SourceUnderlineTrailingText = tailFrom >= 0 && tailFrom < tailRun.Text.Length
                    ? tailRun.Text.Substring(tailFrom)
                    : string.Empty;
                // run.X/Width live in TEXT space; the rule's extent is page space.
                var (tailEndX, _) = ApplyCtm(
                    tailRun.X + tailRun.TmA * tailRun.Width * tailRun.HScaling,
                    tailRun.Y + tailRun.TmB * tailRun.Width * tailRun.HScaling, tailRun.Ctm);
                fragment.SourceUnderlineRunEndX = tailEndX;

                // The rules the LINE carries besides this one. A replacement re-lays the
                // line's decoration in the library's own band, so a rule under a
                // neighbouring run has to come with it - left where the source put it, it
                // sits a fraction off the band the re-laid rules share and keeps a thickness
                // none of them has.
                var (_, myBaseline) = ApplyCtm(firstRun.X, firstRun.Y, firstRun.Ctm);
                for (var ri = 0; fillIndex is not null && ri < rawFragments.Count; ri++)
                {
                    if (ri >= firstRunIdx && ri <= lastRunIdx) continue;
                    var compRun = rawFragments[ri];
                    if (string.IsNullOrWhiteSpace(compRun.Text)) continue;
                    var (_, compBaseline) = ApplyCtm(compRun.X, compRun.Y, compRun.Ctm);
                    if (Math.Abs(compBaseline - myBaseline) > 0.5) continue;
                    var compRect = ComputeMatchBounds(rawFragments, runStartChar, ri, ri,
                        runStartChar[ri], runStartChar[ri] + compRun.Text.Length - 1);
                    if (compRect.URX - compRect.LLX <= 0) continue;
                    if (DetectUnderlineRect(compRect, compBaseline, textState.FontSize, fillIndex)
                        is not { } compUl) continue;
                    if (Math.Abs(compUl.RawX - ulr.RawX) < 0.01 && Math.Abs(compUl.RawY - ulr.RawY) < 0.01
                        && Math.Abs(compUl.RawW - ulr.RawW) < 0.01) continue;
                    if (fragment.CompanionRuleSources is { } seenComp
                        && seenComp.Exists(t => Math.Abs(t.X - compUl.RawX) < 0.01
                            && Math.Abs(t.Y - compUl.RawY) < 0.01
                            && Math.Abs(t.W - compUl.RawW) < 0.01)) continue;
                    fragment.MarkCompanionRule(compUl.RawX, compUl.RawY, compUl.RawW, compUl.RawH,
                        compRect.LLX, compRect.URX - compRect.LLX, compUl.FillColor);
                }
            }
            if (capturedBg is { } bgr)
                fragment.MarkCapturedBackgroundSource(bgr.RawX, bgr.RawY, bgr.RawW, bgr.RawH, bgr.FillColor);
            // A match spanning several lines can cover more than one source
            // underline (short rules under phrases on different lines). The
            // whole-fragment detection above sees only the first baseline, so
            // re-detect per segment and capture every rule found — toggling
            // Underline off must splice out all of them.
            if ((_textEditOptions?.ToAttemptGetUnderlineFromSource ?? false)
                && fillIndex is not null && fragment.Segments.Count > 1)
            {
                foreach (TextSegment seg in fragment.Segments)
                {
                    if (seg.Rectangle is not { } segRect || seg.Position is not { } segPos) continue;
                    // The segment position anchors at the rect bottom (baseline − descent);
                    // lift it back to the true baseline so a rule hugging the baseline
                    // stays inside the detector's window.
                    var segBaseline = Math.Max(segPos.YIndent, segRect.LLY + 0.22 * textState.FontSize);
                    if (DetectUnderlineRect(segRect, segBaseline, textState.FontSize, fillIndex) is not { } segUl) continue;
                    // Raw coords repeat across cm-translated blocks — the width is
                    // part of the identity.
                    if (fragment.CapturedUnderlineSources is { } have
                        && have.Exists(t => Math.Abs(t.X - segUl.RawX) < 0.01 && Math.Abs(t.Y - segUl.RawY) < 0.01
                            && Math.Abs(t.W - segUl.RawW) < 0.01))
                        continue;
                    fragment.MarkCapturedUnderlineSource(segUl.RawX, segUl.RawY, segUl.RawW, segUl.RawH);
                }
            }
            _fragments.Add(fragment);
        }

        // Ordering: a page's matches are yielded in the order of its
        // LINE-ORDERED concatenated search text. For almost every document that
        // equals content order — an unconditional position sort misorders far
        // more documents. Only when the page's stream order is majorly scrambled
        // (a >200 pt upward jump between consecutive runs — the same cue the
        // plain-text line sort keys on: rotated column layouts, bottom-up
        // writers) do the matches get ordered top-to-bottom.
        // Scope: only same-phrase match sets reorder (a repeated label found top
        // and bottom); distinct-content matches keep content order — reported
        // match positions and plain-text dumps both preserve it.
        var newMatches = _fragments.Count - preCount;
        var samePhrase = newMatches > 1;
        var anyRtl = false;
        for (var i = preCount; samePhrase && i < _fragments.Count; i++)
        {
            var t = _fragments.GetInternal(i).Text;
            if (i > preCount && !string.Equals(t, _fragments.GetInternal(preCount).Text, StringComparison.Ordinal))
                samePhrase = false;
            foreach (var ch in t)
                if (BidiReorderer.IsRtlChar(ch)) { anyRtl = true; break; }
        }
        if (newMatches > 1 && samePhrase && anyRtl && HasMajorUpwardJump(rawFragments))
        {
            var inner = _fragments.Inner;
            var slice = inner.GetRange(preCount, inner.Count - preCount);
            slice.Sort((a, b) =>
            {
                var ya = a.Position?.YIndent ?? 0;
                var yb = b.Position?.YIndent ?? 0;
                if (Math.Abs(ya - yb) > 0.5) return yb.CompareTo(ya); // top first
                return (a.Position?.XIndent ?? 0).CompareTo(b.Position?.XIndent ?? 0);
            });
            for (var i = 0; i < slice.Count; i++) inner[preCount + i] = slice[i];
        }
        }
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
            else
                endW = matchEnd < run.CharCumWidths.Length ? run.CharCumWidths[matchEnd] : totalRunW;
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
            options |= RegexOptions.NonBacktracking;
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
    private static void ClipRunToRect(RawTextRun run, Rectangle searchRect,
        ref string text, ref double startX, ref double width)
    {
        if (text.Length == 0) return;

        // Build per-character page-space X positions using CharCumWidths (includes Tc/Tw).
        // Fall back to glyph-only widths when CumWidths not available.
        var charPageX = new double[text.Length + 1];
        if (run.CharCumWidths is not null && run.CharCumWidths.Length > text.Length)
        {
            for (int i = 0; i <= text.Length; i++)
            {
                var cumW = run.CharCumWidths[i];
                var (px, _) = ApplyCtm(run.X + run.TmA * cumW * run.HScaling,
                    run.Y + run.TmB * cumW * run.HScaling, run.Ctm);
                charPageX[i] = px;
            }
        }
        else
        {
            // No per-char cumulative widths: distribute total run width proportionally.
            // MeasureString(string) can return wrong widths for custom-encoded fonts,
            // but run.Width (computed from MeasureString(bytes)) is accurate.
            var totalW = run.Width > 0 ? run.Width : EstimateWidth(text, run.FontSize);
            for (int i = 0; i <= text.Length; i++)
            {
                var cumW = totalW * i / text.Length;
                var (px, _) = ApplyCtm(run.X + run.TmA * cumW * run.HScaling,
                    run.Y + run.TmB * cumW * run.HScaling, run.Ctm);
                charPageX[i] = px;
            }
        }

        // Use tight tolerance for left clip (include chars AT or after rect.LLX)
        // and loose tolerance for right clip.
        var rightTol = 0.5;

        // Find first character that starts within or near the rect left edge.
        // Include characters whose midpoint is within the rect (more than half
        // of the glyph is visible).
        int clipStart = 0;
        for (int i = 0; i < text.Length; i++)
        {
            var charMid = (charPageX[i] + charPageX[i + 1]) * 0.5;
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
            if (charPageX[i + 1] <= searchRect.URX + rightTol)
            {
                clipEnd = i + 1;
                break;
            }
        }


        if (clipStart >= clipEnd)
        {
            text = "";
            return;
        }
        if (clipStart == 0 && clipEnd == text.Length)
            return; // no clipping needed

        // Use CumWidths for the prefix offset and clipped width
        double prefAdv, clipAdv;
        if (run.CharCumWidths is not null && run.CharCumWidths.Length > text.Length)
        {
            prefAdv = run.CharCumWidths[clipStart];
            clipAdv = run.CharCumWidths[clipEnd] - run.CharCumWidths[clipStart];
        }
        else
        {
            // Proportional distribution from total run width.
            // text is already clipped; run.Text has the original full text.
            var totalW = run.Width > 0 ? run.Width : EstimateWidth(run.Text, run.FontSize);
            prefAdv = totalW * clipStart / run.Text.Length;
            clipAdv = totalW * text.Length / run.Text.Length;
        }

        text = text[clipStart..clipEnd];
        startX = run.X + run.TmA * prefAdv * run.HScaling;
        width = clipAdv;
    }

    private static double EstimateWidth(string text, double fontSize)
    {
        return text.Length * fontSize * 0.5;
    }
}
