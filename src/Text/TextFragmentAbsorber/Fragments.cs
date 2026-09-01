using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
namespace Aspose.Pdf.Text;

public sealed partial class TextFragmentAbsorber
{
    private void BuildAllFragmentsFromRuns(List<RawTextRun> rawFragments, Rectangle? searchRect,
        Page? sourcePage, XForm? sourceForm, int pageIndex, List<RawFillRect>? fillRects = null,
        List<RawCoverRect>? coverRects = null)
    {
        SplitRunsAtCharGaps(rawFragments);
        if (_textSearchOptions?.ExcludeRectangles is { Length: > 0 } excludeRects)
            SplitRunsByExcludeRects(rawFragments, excludeRects);
        // Blank lines BELOW the last painted glyph leave no fragment behind — only
        // trailing newline sentinels (textless line advances). Count them so the
        // Text getter can reproduce the document's trailing blank
        // lines; the last visited page's tail is the document's tail.
        _trailingLineBreaks = 0;
        for (var t = rawFragments.Count - 1; t >= 0 && rawFragments[t].Text == "\r\n"; t--)
            _trailingLineBreaks++;

        // Index into rawFragments — cover rects record how many runs painted before
        // them, so run i is occluded only by covers with RunsBefore > i.
        var (laterInk, _, _) = ComputeLaterInkOcclusion(rawFragments);
        // Index the fill rects once by vertical midpoint so the per-run decoration probes
        // below query a small baseline-local slice instead of rescanning the whole list.
        var fillIndex = fillRects is { Count: > 0 } ? new FillRectIndex(fillRects) : null;
        var runIndex = -1;
        foreach (var run in rawFragments)
        {
            runIndex++;
            if (run.Text == "\r\n") continue;
            var occludedByLaterText = laterInk[runIndex];
            var upX_ = run.TmC * run.Ctm.A + run.TmD * run.Ctm.C;
            var upY_ = run.TmC * run.Ctm.B + run.TmD * run.Ctm.D;
            var tmScale = Math.Sqrt(upX_ * upX_ + upY_ * upY_);
            var effectiveFontSize = tmScale > 0.001 && Math.Abs(tmScale - 1.0) > 0.001
                ? run.FontSize * tmScale
                : run.FontSize;
            var textState = new TextState
            {
                FontSize = (float)effectiveFontSize,
                FontName = run.FontName,
                RenderingMode = (Aspose.Pdf.Text.TextRenderingMode)run.RenderingMode,
                LineWidth = run.LineWidth,
                IsBold = run.IsBold,
                IsItalic = run.IsItalic,
                Font = run.FontInfoObj ?? FontInfo.DefaultHelvetica,
                TextRise = run.TextRise,
                IsSuperscript = run.TextRise > 0,
                IsSubscript = run.TextRise < 0,
            };
            textState.SetCapturedForegroundColor(ForegroundColorOf(run));
            textState.StrokingColor = run.StrokingColor;
            // Tz-scaled advances really are wider on the page (a column-fit TOC
            // leader's rect ends where its stretched glyphs end).
            var width = (run.Width > 0 ? run.Width : EstimateWidth(run.Text, run.FontSize)) * run.HScaling;
            // The box ends at the last glyph's advance: the character spacing that
            // follows it - and the word spacing too when that glyph is a space - is
            // pen movement, not text. The expected box for "CHANCEN ERGREIFEN!"
            // at Tc -0.02 is 0.32 pt WIDER than the pen advance, and a justified
            // line's trailing space contributes no Tw to its box.
            if (run.Width > 0 && run.Text.Length > 0)
                width -= (run.CharSpacing + (run.Text[^1] == ' ' ? run.WordSpacing : 0)) * run.HScaling;

            // The fragment box is the same canonical line box the phrase search
            // reports: bottom at baseline + descent, 1.1 x FontSize tall (the
            // reference returns that box for every font on every page probed -
            // embedded CFF, non-embedded TrueType, CJK, rotated text alike); the
            // font's own ascent only wins for an EXTREME metric box.
            var (descentOffset, ascentHeight) = ComputeDescentAscent(run, coreFaceDescent: false);

            var rectStartX = run.X + run.TmC * descentOffset;
            var rectStartY = run.Y + run.TmD * descentOffset;
            var (rx1, ry1) = ApplyCtm(rectStartX, rectStartY, run.Ctm);
            var endX = run.X + run.TmA * width + run.TmC * ascentHeight;
            var endY = run.Y + run.TmB * width + run.TmD * ascentHeight;
            var (rx2, ry2) = ApplyCtm(endX, endY, run.Ctm);

            var llx = Math.Min(rx1, rx2);
            var lly = Math.Min(ry1, ry2);
            var urx = Math.Max(rx1, rx2);
            var ury = Math.Max(ry1, ry2);

            var rect = new Rectangle(llx, lly, urx, ury);
            var px = rx1;
            var py = ry1;

            if (searchRect is not null && !searchRect.IsEmpty)
            {
                // Horizontal gate: any X-span overlap between the run and the rectangle lets
                // the run through — ClipRunToRect then keeps only the glyphs that fall inside
                // (a run may START well left of the rect yet contribute its tail glyphs).
                // Vertical gate: at least half of the run's glyph band (descent..ascent) must
                // lie inside the rectangle. A strict baseline-in-rect test drops a line whose
                // baseline dips a fraction below the rect bottom even though the glyph bodies
                // are substantially inside, and such lines belong in the result.
                var (_, baseY) = ApplyCtm(run.X, run.Y, run.Ctm);
                var bandH = ury - lly;
                var overlapV = Math.Min(ury, searchRect.URY) - Math.Max(lly, searchRect.LLY);
                // Half a point of slack on the half-band rule: a 5 pt space glyph whose
                // canonical box straddles the rectangle's bottom edge by 49.6 % is
                // still reported inside.
                const double HalfBandSlack = 0.5;
                bool vOk = bandH > 1e-6
                    ? overlapV * 2 >= bandH - HalfBandSlack
                    : baseY >= searchRect.LLY && baseY <= searchRect.URY;
                if (!(urx >= searchRect.LLX && llx <= searchRect.URX && vOk))
                    continue;
            }

            var clipText = run.Text;
            var clipX = run.X;
            var clipWidth = width;
            if (searchRect is not null && run.Metrics is not null)
            {
                ClipRunToRect(run, searchRect, ref clipText, ref clipX, ref clipWidth);
                if (clipText.Length == 0) continue;
                var cRectStartX = clipX + run.TmC * descentOffset;
                var cRectStartY = run.Y + run.TmD * descentOffset;
                var (crx1, cry1) = ApplyCtm(cRectStartX, cRectStartY, run.Ctm);
                var cEndX = clipX + run.TmA * clipWidth + run.TmC * ascentHeight;
                var cEndY = run.Y + run.TmB * clipWidth + run.TmD * ascentHeight;
                var (crx2, cry2) = ApplyCtm(cEndX, cEndY, run.Ctm);
                llx = Math.Min(crx1, crx2);
                lly = Math.Min(cry1, cry2);
                urx = Math.Max(crx1, crx2);
                ury = Math.Max(cry1, cry2);
                rect = new Rectangle(llx, lly, urx, ury);
                px = crx1;
                py = cry1;
            }

            var tdx = run.Ctm.A * run.TmA + run.Ctm.C * run.TmB;
            var tdy = run.Ctm.B * run.TmA + run.Ctm.D * run.TmB;

            var rotDeg = RotationFromDirection(tdx, tdy);
            if (rotDeg.HasValue) textState.Rotation = rotDeg.Value;

            // SearchForTextRelatedGraphics: when a fill rect collected from the content stream
            // contains the fragment's text origin, copy its color to the TextState as the
            // background. Search the most recently emitted rect first — later draw order wins
            // for overlapping rects, matching the visible z-order on the page.
            // Assigning to the TextState's backing field directly avoids triggering the
            // save-time rect-injection registration that the public setter performs.
            RawFillRect? capturedUl = null;
            RawFillRect? capturedBg = null;
            {
                var (_, baselineY) = ApplyCtm(run.X, run.Y, run.Ctm);

                // Background colour + underline capture only when the caller asked for
                // graphics-related results (or underline-from-source). When no search options
                // were supplied, honour TextSearchOptions' own default (SearchForTextRelatedGraphics
                // = true) for BOTH captures: a plain `new TextFragmentAbsorber()` recovers
                // TextState.BackgroundColor AND reports Underline for a rule the geometry
                // detector accepts (an HtmlFragment round trip's <u> reads back true
                // with default options), and strikeout
                // already detects by default.
                bool wantGraphics = _textSearchOptions?.SearchForTextRelatedGraphics ?? true;
                bool wantSourceDecorations = _textEditOptions?.ToAttemptGetUnderlineFromSource ?? false;
                bool wantUnderline = wantGraphics || wantSourceDecorations;
                if (fillIndex is not null)
                {
                    if (wantGraphics)
                    {
                        var bg = fillIndex.FindTopMatch(py - FillRectIndex.Margin, py + FillRectIndex.Margin,
                            fr => px >= fr.Llx && px <= fr.Urx && py >= fr.Lly && py <= fr.Ury);
                        if (bg is { } bgHit) textState.SetCapturedBackgroundColor(bgHit.FillColor);
                    }
                    if (wantUnderline)
                    {
                        capturedUl = DetectUnderlineRect(rect, baselineY, effectiveFontSize, fillIndex);
                        if (capturedUl is not null) textState.SetCapturedUnderline(true);
                    }
                    // Source-highlight capture: lets a later text replacement splice the
                    // old background rect out and re-draw it at the replacement's width.
                    if (wantSourceDecorations)
                        capturedBg = DetectBackgroundRect(rect, baselineY, effectiveFontSize, fillIndex);

                    // Strikeout is detected by default (no option required).
                    if (DetectStrikeoutRect(rect, baselineY, effectiveFontSize, fillIndex) is not null)
                        textState.SetCapturedStrikeOut(true);
                }
            }

            // Hidden-by-clipping: the clip region in effect when the run was shown
            // cuts more of its box away than is tolerated - a tenth of
            // the box in each direction, see ClipSlackFraction for the measured law.
            // Same reporting rule as occlusion: Invisible, RenderingMode untouched.
            if (run.ClipRect is { } runClip)
            {
                // The verdict is the FACE's line box's, not the reported rectangle's:
                // the two differ only for a descriptor-less core face, whose rectangle
                // still seats on its baseline here (see ComputeDescentAscent).
                var lineBox = RunLineBox(run);
                if (IsHiddenByClip(run, run.Text, lineBox.Llx, lineBox.Lly,
                        lineBox.Urx, lineBox.Ury, runClip))
                    textState.SetCapturedOccluded(true);
            }

            // Hidden-by-later-text: a stacked duplicate draw (or any later text ink
            // covering the glyph box) hides this run — every copy
            // but the last reports as Invisible.
            if (occludedByLaterText)
                textState.SetCapturedOccluded(true);

            // Hidden-by-occlusion: a body-sized opaque fill painted AFTER this run that
            // fully covers its box hides it (redaction-style). Surface it through
            // TextState.Invisible — the run's
            // RenderingMode stays FillText.
            if (coverRects is not null && coverRects.Count > 0)
            {
                // Vertical slack scales with the glyph size: the nominal ascent box can
                // poke a point or two past a cover that visually swallows the line
                // (e.g. a 12pt line row under a box aligned to the row grid).
                var tol = Math.Max(0.8, effectiveFontSize * 0.25);
                foreach (var cover in coverRects)
                {
                    if (cover.RunsBefore <= runIndex) continue; // painted before this run
                    if (rect.LLX >= cover.Llx - tol && rect.URX <= cover.Urx + tol
                        && rect.LLY >= cover.Lly - tol && rect.URY <= cover.Ury + tol)
                    {
                        textState.SetCapturedOccluded(true);
                        break;
                    }
                }
            }

            var frag = new TextFragment(LogicalizeRtlPresentationForms(clipText), rect, textState)
            {
                PageIndex = pageIndex,
                Position = new Position(Q(px), Q(py)),
                SourcePage = sourcePage,
                Form = sourceForm,
                SourceXObjStream = run.SourceXObj,
                TextDirX = tdx,
                TextDirY = tdy,
                ExtractionCtm = new Aspose.Pdf.Matrix(run.Ctm.A, run.Ctm.B, run.Ctm.C, run.Ctm.D, run.Ctm.E, run.Ctm.F),
                ExtractionTmTy = run.TmBaseY,
                ReplaceOptions = TextReplaceOptions,
            };
            if (frag.Segments.Count > 0)
            {
                frag.Segments[1].EndCharIndex = clipText.Length - 1;
                // The whole-run segment IS the run: it carries the fragment's box and
                // position ("fragment right border matches its last segment's
                // right border" holds for every extracted fragment).
                frag.Segments[1].Rectangle = rect;
                // The position only for UPRIGHT runs: a segment position scopes later
                // edits to that baseline, and a rotated run's box corner is not on it
                // (an invisible vertical OCR run must still be found when deleted).
                if (Math.Abs(run.TmB) <= Math.Abs(run.TmA) && IsUprightCtm(run))
                    frag.Segments[1].Position = new Position(Q(px), Q(py));
                // Per-character layout for the whole-run segment. The unclipped case
                // (the common one) maps one character per glyph exactly from the run
                // start; PopulateCharacters bounds the range to the run text length.
                PopulateCharacters(frag.Segments[1], run, 0, clipText.Length - 1);
            }
            if (capturedUl is { } ulr)
                frag.MarkCapturedUnderlineSource(ulr.RawX, ulr.RawY, ulr.RawW, ulr.RawH);
            if (capturedBg is { } bgr)
                frag.MarkCapturedBackgroundSource(bgr.RawX, bgr.RawY, bgr.RawW, bgr.RawH, bgr.FillColor);
            _fragments.Add(frag);
        }

        DetectSuperSubscript(_fragments);
    }

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
            if (ignoreShadow)
            {
                var cur = rawFragments[i];
                // A shadow copy of a space is also dropped: position-based matching (overlapping X)
                // distinguishes it from a real inter-word space, which is a full advance away.
                if (cur.Text != "\r\n" && cur.Text.Length > 0
                    && lastKeptText == cur.Text)
                {
                    double effFs = (cur.FontSize > 0 ? cur.FontSize : 1.0) * (Math.Abs(cur.TmA) > 0 ? Math.Abs(cur.TmA) : 12.0);
                    double tol = Math.Max(1.0, 0.22 * effFs);
                    if (Math.Abs(cur.X - lastKeptX) < tol && Math.Abs(cur.Y - lastKeptY) < tol)
                    {
                        // Drop the \r\n sentinel(s) sitting between the kept glyph and this shadow
                        // copy — they only separated a glyph from its own shadow (each glyph is its
                        // own BT/ET), not real content. Otherwise an orphan \r\n can survive inside a
                        // word (e.g. "Constructio\r\nn") and break the match.
                        while (fullText.Length > 0 && (fullText[^1] == '\r' || fullText[^1] == '\n'))
                        {
                            fullText.Length--;
                            charToRun.RemoveAt(charToRun.Count - 1);
                        }
                        runStartChar[i] = charToRun.Count; // shadow duplicate — emit no characters
                        continue;
                    }
                }
            }
            // Detect horizontal gaps between consecutive runs on the same line.
            // Skip \r\n sentinels to find the real previous run — BT/ET boundaries
            // inject \r\n but runs in adjacent BT blocks at the same Y are same-line text.
            if (i > 0 && rawFragments[i].Text != "\r\n")
            {
                int prevIdx = i - 1;
                while (prevIdx >= 0 && rawFragments[prevIdx].Text == "\r\n") prevIdx--;
                if (prevIdx < 0) goto skipSpaceInsert;
                var prev = rawFragments[prevIdx];
                // Compare baselines in PAGE space: producers that position each run's line
                // via a per-block cm translation keep text-space Y at 0 for every line, so a
                // raw-Y comparison would fuse the whole page into one line. For the common
                // identity-CTM case page space equals text space, so behaviour is unchanged.
                // HORIZONTAL runs only — rotated/curved glyphs drift in page-Y along the
                // line, so page-space comparison would split them; those keep the raw test.
                // Rotation anywhere (Tm OR CTM, see IsUprightCtm) keeps the raw test too.
                double deltaY;
                if (Math.Abs(rawFragments[i].TmB) <= Math.Abs(rawFragments[i].TmA)
                    && Math.Abs(prev.TmB) <= Math.Abs(prev.TmA)
                    && IsUprightCtm(rawFragments[i]) && IsUprightCtm(prev))
                {
                    var (_, curPageY) = ApplyCtm(rawFragments[i].X, rawFragments[i].Y, rawFragments[i].Ctm);
                    var (_, prevPageY) = ApplyCtm(prev.X, prev.Y, prev.Ctm);
                    deltaY = Math.Abs(curPageY - prevPageY);
                }
                else
                    deltaY = Math.Abs(rawFragments[i].Y - prev.Y);
                if (deltaY < 2.0) // same line
                {
                    // Remove \r\n sentinels between prevIdx and i on the same line —
                    // they were BT/ET boundary artifacts, not real line breaks.
                    if (prevIdx < i - 1)
                    {
                        while (fullText.Length > 0 && (fullText[^1] == '\r' || fullText[^1] == '\n'))
                        {
                            fullText.Length--;
                            charToRun.RemoveAt(charToRun.Count - 1);
                        }
                    }

                    // Back-jump PREPEND: a same-row run drawn
                    // wholly LEFT of the line's current start splices in FRONT of the
                    // line, X-ordered (a stream that draws a value before its label
                    // reads "Kundenummer: 981641205"). Junction separator: exactly one
                    // space iff the x-gap between the run's end and the line start is
                    // ≥ 0.15 em. RTL runs keep the append path — their visual pen
                    // legitimately walks right-to-left and reorders via the bidi pass.
                    {
                        var bjText = rawFragments[i].Text;
                        var bjHasRtl = false;
                        foreach (var bjc in bjText)
                            if (BidiReorderer.IsRtlChar(bjc)) { bjHasRtl = true; break; }
                        // UPRIGHT text only: rotated/vertical runs advance along Y, so
                        // an X-based "wholly left" test would reorder reading order.
                        var bjUpright = Math.Abs(rawFragments[i].TmB) <= Math.Abs(rawFragments[i].TmA) * 1e-3
                            && Math.Abs(prev.TmB) <= Math.Abs(prev.TmA) * 1e-3
                            && IsUprightCtm(rawFragments[i]) && IsUprightCtm(prev);
                        if (bjUpright && !bjHasRtl && bjText.Trim().Length > 0)
                        {
                            var lsIdx = fullText.Length;
                            while (lsIdx > 0 && fullText[lsIdx - 1] != '\n') lsIdx--;
                            var lineMinX = double.NaN;
                            for (var cc = lsIdx; cc < charToRun.Count; cc++)
                            {
                                var rIdx = charToRun[cc];
                                if (rIdx < 0 || rIdx >= rawFragments.Count) continue;
                                var rx = rawFragments[rIdx].X;
                                if (double.IsNaN(lineMinX) || rx < lineMinX) lineMinX = rx;
                            }
                            var bjW = rawFragments[i].Width > 0
                                ? rawFragments[i].Width * rawFragments[i].HScaling
                                : EstimateWidth(bjText, rawFragments[i].FontSize);
                            var bjEnd = rawFragments[i].X + bjW;
                            var bjFs = rawFragments[i].FontSize > 0 ? rawFragments[i].FontSize : 12.0;
                            if (!double.IsNaN(lineMinX) && bjEnd <= lineMinX + 0.5)
                            {
                                var junction = lineMinX - bjEnd;
                                var sep = junction >= bjFs * 0.15 ? " " : "";
                                var insertText = bjText + sep;
                                fullText.Insert(lsIdx, insertText);
                                var entries = new int[insertText.Length];
                                for (var k = 0; k < entries.Length; k++) entries[k] = i;
                                charToRun.InsertRange(lsIdx, entries);
                                for (var rj = 0; rj < i; rj++)
                                    if (runStartChar[rj] >= lsIdx) runStartChar[rj] += insertText.Length;
                                runStartChar[i] = lsIdx;
                                if (ignoreShadow)
                                {
                                    lastKeptText = rawFragments[i].Text;
                                    lastKeptX = rawFragments[i].X;
                                    lastKeptY = rawFragments[i].Y;
                                }
                                continue;
                            }
                        }
                    }

                    // Insert space if there's a word-sized or column-sized gap.
                    // Widths are TEXT-space (they scale with Tm) while X is the Tm
                    // translation — scale the width by the run's Tm X-scale so the
                    // gap is measured in one space (a `/F 1 Tf` + `7 0 0 7 … Tm`
                    // producer otherwise reads every glyph pair as a word gap).
                    var prevTmScale = Math.Abs(prev.TmA) > 0 ? Math.Abs(prev.TmA) : 1.0;
                    var prevEndX = prev.X + (prev.Width > 0
                        ? prev.Width * prev.HScaling
                        : EstimateWidth(prev.Text, prev.FontSize)) * prevTmScale;
                    var gap = rawFragments[i].X - prevEndX;
                    var fontSize = rawFragments[i].FontSize > 0 ? rawFragments[i].FontSize : 12.0;
                    var tmScaleX = Math.Abs(rawFragments[i].TmA) > 0 ? Math.Abs(rawFragments[i].TmA) : 1.0;
                    var effFontSize = fontSize * tmScaleX;
                    var lastChar = fullText.Length > 0 ? fullText[^1] : '\0';
                    var nextChar = rawFragments[i].Text.Length > 0 ? rawFragments[i].Text[0] : '\0';
                    bool noPriorSpace = fullText.Length > 0 && lastChar != ' ' && lastChar != '\n' && nextChar != ' ';
                    // Suppress the word-gap space only inside letter-spaced words, where BOTH
                    // sides are single characters. Requiring both runs >= 2 chars also dropped
                    // the space at a word -> single-char-token boundary (e.g. "level" -> "1"),
                    // so a phrase search for "Heading level 1" missed the extracted
                    // "Heading level1".
                    // Word-gap space: a positive, word-sized gap between two runs that isn't
                    // letter-tracking. The `!letterTracked[i]` guard (see ComputeLetterTrackedGaps)
                    // covers glyph-by-glyph lines whose letters are loosely tracked, so single-char
                    // runs no longer need a blanket >=2 guard — genuine word breaks in tight
                    // glyph-by-glyph text (an OCR overlay) are kept. But for a SINGLE-char pair we
                    // still require a nearly-flat baseline (small deltaY): on a curved word (a
                    // display font following a path) an isolated large X-gap between two glyphs is
                    // a curve artifact, not a word space. Multi-char runs are unaffected.
                    bool bothSingle = prev.Text.Length == 1 && rawFragments[i].Text.Length == 1;
                    // A GapSplit continuation is a column sibling the absorber cut out
                    // of one show op — always exactly one boundary space, independent
                    // of the run-gap heuristics (the ceiling would glue wide columns).
                    bool insertBySplit = rawFragments[i].GapSplit;
                    // Beyond the classic 3-em window a gap STILL separates tokens —
                    // 4.8–10.4 em same-row column gaps get spaces
                    // ('MCF'→'Energy', 'Dry'→'72-40-097') and token streams
                    // get a space at 0.23 em AND 20 em alike — but only on a
                    // near-flat baseline (table columns drift ≤ ~0.7 pt; a diagonal
                    // watermark's run pair sits many points apart in Y) and only
                    // between runs whose facing ends are ALPHANUMERIC: a symbol
                    // watermark's decorative halves 7 em apart ('…_+|' / '|+_…')
                    // are decoration, not words — no space is emitted
                    // there. A SINGLE-glyph pair counts as well: a two-character name
                    // spread evenly across a fixed width sets its glyphs 5 em apart and
                    // reads as two tokens, while a curved word's glyphs are already held
                    // together by the flat-baseline test.
                    var alnumAdjacent =
                        (prev.Text.Length > 0 && char.IsLetterOrDigit(prev.Text[^1]))
                        || (rawFragments[i].Text.Length > 0 && char.IsLetterOrDigit(rawFragments[i].Text[0]));
                    bool insertByWordGap = gap > effFontSize * 0.2
                        && (gap <= effFontSize * 3.0
                            || (deltaY < 0.75 && alnumAdjacent))
                        && !letterTracked[i]
                        && (!bothSingle || deltaY < 0.75)
                        // An INVISIBLE (Tr 3) OCR overlay keeps a sentence period glued to
                        // the next sentence's capital even at a word-sized gap (one
                        // layer: per-glyph Tz, '.'->'H' at 0.39 em reads glued). VISIBLE
                        // glyph-by-glyph text takes the plain magnitude law instead — a
                        // period is an ordinary glyph there (elsewhere '.'->'W' at 0.28 em
                        // spaces, same as its every word boundary).
                        && !(bothSingle && prev.Text == "."
                             && prev.RenderingMode == InvisibleTextRenderMode
                             && rawFragments[i].RenderingMode == InvisibleTextRenderMode);
                    // Same-line column hops (>8 em) always separate tokens — the
                    // 3-em word-gap ceiling above only guards mid-range gaps. A
                    // BACKWARDS pen jump (>1 em) is a new column/segment too (the
                    // stream returned to an earlier X on the same row).
                    // A backwards pen jump separates columns only when the new run
                    // lands entirely LEFT of the previous run's start — an OVERLAPPING
                    // redraw (drop shadows, doubled draw) keeps gluing.
                    var newEndX = rawFragments[i].X
                        + rawFragments[i].Width * rawFragments[i].HScaling * prevTmScale;
                    bool insertByBackJump = gap < -effFontSize * 1.5;
                    bool insertByColumnGap = gap > effFontSize * 16.0 || insertByBackJump;
                    // A run that starts PART-WAY THROUGH the previous run's advance —
                    // past its origin, but short of where it ends — was set squeezed,
                    // and the pieces read as separate tokens. Japanese full-width
                    // punctuation is the everyday case: '）' and '、' carry a full-em
                    // advance but are set half-width, so the next glyph lands half an em
                    // early. The pen deviates from a flush advance by the same amount a
                    // word gap does, just with the opposite sign, so it earns the same
                    // separator. Two neighbours this rule must NOT claim: a doubled draw
                    // (drop shadow) re-starts AT the previous origin, and a back-jump
                    // starts left of it — both keep the handling they already had.
                    // Only an ISOLATED squeeze counts — see ComputeSqueezedGaps.
                    bool insertBySqueeze = squeezedGap[i];
                    // Rotated text (|TmB| > |TmA|, e.g. vertical labels rotated ~90°) advances
                    // along Y, not X, so the X-based `gap` above is meaningless (often negative).
                    // For such runs the cross-axis is X: two runs sharing a baseline (deltaY≈0)
                    // but at clearly different X are distinct columns/labels (e.g. CAD grid
                    // markers "A","B","C" rotated and spread across the sheet), not one word.
                    // Insert a separator so a regex word boundary \b can form between them.
                    // Horizontal text (|TmA| >= |TmB|, incl. curved/kerned words) is unaffected.
                    bool isRotated = Math.Abs(rawFragments[i].TmB) > Math.Abs(rawFragments[i].TmA)
                        && Math.Abs(prev.TmB) > Math.Abs(prev.TmA);
                    var rotScale = Math.Sqrt(rawFragments[i].TmA * rawFragments[i].TmA
                        + rawFragments[i].TmB * rawFragments[i].TmB);
                    var effRotFont = (fontSize > 0 ? fontSize : 12.0) * (rotScale > 0 ? rotScale : 1.0);
                    bool insertByRotatedColumn = isRotated
                        && Math.Abs(rawFragments[i].X - prev.X) > effRotFont * 0.5;
                    if (noPriorSpace && (insertBySplit || insertByWordGap || insertBySqueeze
                        || insertByColumnGap || insertByRotatedColumn))
                    {
                        charToRun.Add(prevIdx);
                        fullText.Append(' ');
                    }
                }
            }
            skipSpaceInsert:

            runStartChar[i] = charToRun.Count;
            var text = rawFragments[i].Text;

            // Newline sentinels: skip for phrase search (so cross-line phrases match),
            // keep for regex search (so \r\n patterns work).
            var effectiveIsRegex = _isRegex || (_textSearchOptions?.IsRegularExpression ?? false);
            if (text == "\r\n" && !effectiveIsRegex)
            {
                // A line break is a WORD boundary for phrase search: a word split across
                // lines without a hyphen is not fused back together ("tionAccountC" does
                // not match against "…Registrat⏎onAccountCU…"). Multi-word
                // phrases still match across the break through this separator space; when
                // the previous run already ends in whitespace nothing is added, so no
                // double-space can break a single-spaced phrase.
                // HORIZONTAL neighbours only (both runs strictly rotation-free in Tm
                // AND CTM): on curved text (per-glyph rotated Tm) or under a rotated
                // CTM the Y-drift along the word leaves sentinels BETWEEN GLYPHS of
                // one word — a separator space there would break the word, so those
                // sentinels stay skipped as before.
                if (fullText.Length > 0)
                {
                    int sepPrev = i - 1;
                    while (sepPrev >= 0 && rawFragments[sepPrev].Text == "\r\n") sepPrev--;
                    int sepNext = i + 1;
                    while (sepNext < rawFragments.Count && rawFragments[sepNext].Text == "\r\n") sepNext++;
                    bool flatNeighbors = sepPrev >= 0
                        && Math.Abs(rawFragments[sepPrev].TmB) <= 1e-4 * Math.Abs(rawFragments[sepPrev].TmA)
                        && IsUprightCtm(rawFragments[sepPrev])
                        && (sepNext >= rawFragments.Count
                            || (Math.Abs(rawFragments[sepNext].TmB) <= 1e-4 * Math.Abs(rawFragments[sepNext].TmA)
                                && IsUprightCtm(rawFragments[sepNext])));
                    // Same-baseline neighbours mean the sentinel is a BT/ET block
                    // boundary, not a real line break (adjacent blocks continue the
                    // line): no word boundary there. The same-line pass above removes
                    // such sentinels and inserts a space only for a word-sized
                    // geometric gap, so appending one here would force a separator
                    // into a phrase that renders with none (inline fragments).
                    bool realLineBreak = true;
                    if (sepPrev >= 0 && sepNext < rawFragments.Count && flatNeighbors)
                    {
                        var (_, prevPageY) = ApplyCtm(rawFragments[sepPrev].X, rawFragments[sepPrev].Y, rawFragments[sepPrev].Ctm);
                        var (_, nextPageY) = ApplyCtm(rawFragments[sepNext].X, rawFragments[sepNext].Y, rawFragments[sepNext].Ctm);
                        realLineBreak = Math.Abs(nextPageY - prevPageY) >= 2.0;
                    }
                    if (flatNeighbors && realLineBreak)
                    {
                        if (!char.IsWhiteSpace(fullText[^1]))
                        {
                            charToRun.Add(sepPrev);
                            fullText.Append(' ');
                        }
                        else
                        {
                            // The line already ended with a space of its own — the
                            // break is then a real boundary, and a single-space
                            // phrase must not read through it into the next line.
                            charToRun.Add(sepPrev);
                            charToRun.Add(sepPrev);
                            fullText.Append("\r\n");
                        }
                    }
                }
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

        var concatenated = fullText.ToString();

        // Normalize presentation forms FIRST and re-project the char maps:
        // NFKD decompositions change the string length, so matching on the
        // normalized text against maps built on the original mis-addresses
        // runs (wrong fragment text, position and even phantom matches).
        concatenated = NormalizeArabicPresentationFormsWithMap(concatenated, out var newToOld);
        if (newToOld is not null)
        {
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
        }

        // Apply bidi reordering for non-regex search — regex patterns expect
        // logical order. Runs on the normalized text so bidiPerm indices live
        // in the same space as the (re-projected) char maps.
        int[]? bidiPerm = null;
        var isRegex = _isRegex || (_textSearchOptions?.IsRegularExpression ?? false);
        if (!isRegex)
            concatenated = BidiReorderer.ReorderIfNeeded(concatenated, out bidiPerm);

        return (concatenated, charToRun, runStartChar, bidiPerm);
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
