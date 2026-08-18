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
            var height = run.FontSize;

            double descentOffset = 0;
            double ascentHeight = height;
            if (run.Metrics is not null && run.Metrics.Descent != 0)
                descentOffset = run.Metrics.Descent * run.FontSize / 1000.0;
            if (run.Metrics is not null && run.Metrics.Ascent > 0)
                ascentHeight = run.Metrics.Ascent * run.FontSize / 1000.0;

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
                bool vOk = bandH > 1e-6
                    ? overlapV * 2 >= bandH
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
                // = true) for BACKGROUND capture so a plain `new TextFragmentAbsorber()` recovers
                // TextState.BackgroundColor. Underline-from-source capture stays opt-in (its
                // decoration detection is only wanted when graphics search is explicitly enabled),
                // so it keeps the pre-existing "off unless requested" behaviour.
                bool graphicsExplicit = _textSearchOptions?.SearchForTextRelatedGraphics ?? false;
                bool wantGraphics = _textSearchOptions?.SearchForTextRelatedGraphics ?? true;
                bool wantSourceDecorations = _textEditOptions?.ToAttemptGetUnderlineFromSource ?? false;
                bool wantUnderline = graphicsExplicit || wantSourceDecorations;
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

            // Hidden-by-clipping: a run whose box lies entirely outside the clip
            // region in effect when it was shown never marks a pixel (the common
            // form is a degenerate sliver clip, e.g. a 0.0001pt-tall `re W* n`).
            // Same reporting rule as occlusion: Invisible, RenderingMode untouched.
            if (run.ClipRect is { } runClip)
            {
                var ix = Math.Min(rect.URX, runClip.Urx) - Math.Max(rect.LLX, runClip.Llx);
                var iy = Math.Min(rect.URY, runClip.Ury) - Math.Max(rect.LLY, runClip.Lly);
                if (ix <= 0.05 || iy <= 0.05)
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

    /// <summary>
    /// Maps character offsets in concatenated text back to source RawTextRun entries
    /// to compute bounding rectangles for search matches.
    /// Three phases: (1) build char→run index, (2) find regex/phrase matches, (3) build fragments.
    /// </summary>
    // Detect a horizontal underline drawn as a thin filled rectangle just below the
    // fragment's baseline. Used by SearchForTextRelatedGraphics. PDF producers commonly
    // emit underlines as `x y w h re f*` after the Tj/TJ that placed the text — these
    // rects are short, just below the baseline, and span (approximately) the run's width.
    private static RawFillRect? DetectUnderlineRect(Rectangle rect, double baselineY,
        double fontSize, FillRectIndex fillRects)
    {
        var fragWidth = rect.URX - rect.LLX;
        if (fragWidth <= 0) return null;
        var maxThickness = Math.Max(1.5, 0.15 * fontSize);
        var maxGap = Math.Max(2.5, 0.4 * fontSize);
        // A match has Ury in [baselineY - maxGap, baselineY + 0.5]; a thin rect's midpoint
        // sits within Margin of that band.
        return fillRects.FindTopMatch(baselineY - maxGap - FillRectIndex.Margin, baselineY + 0.5, fr =>
        {
            var h = fr.Ury - fr.Lly;
            if (h > maxThickness) return false;
            if (fr.Ury > baselineY + 0.5) return false;
            if (fr.Ury < baselineY - maxGap) return false;
            // A rule far wider than the run AND sitting deep below the baseline (past the
            // descent band) is page graphics — a table border or column rule under the
            // whole line — not this fragment's underline. Width alone can't discriminate
            // (a phrase underline legitimately spans many word fragments), so both
            // conditions must hold.
            if (fr.Urx - fr.Llx > fragWidth * 2 + 4 && fr.Ury < baselineY - Math.Max(1.8, 0.2 * fontSize)) return false;
            var ox = Math.Max(0, Math.Min(rect.URX, fr.Urx) - Math.Max(rect.LLX, fr.Llx));
            if (ox * 2 < fragWidth) return false;
            return true;
        });
    }

    // Detect a background highlight: a filled rect tall enough to cover the glyph body
    // (not a thin underline/strikeout bar) that spans the fragment horizontally and
    // vertically encloses its baseline band. Captured under ToAttemptGetUnderlineFromSource
    // so a text replacement can splice the old highlight out and re-draw it at the new
    // advance.
    private static RawFillRect? DetectBackgroundRect(Rectangle rect, double baselineY,
        double fontSize, FillRectIndex fillRects)
    {
        var fragWidth = rect.URX - rect.LLX;
        if (fragWidth <= 0) return null;
        var minThickness = Math.Max(2.0, 0.5 * fontSize);
        // A match straddles the baseline (Lly ≤ baselineY ≤ Ury − 0.3·fontSize). Taller
        // matches are caught by the index's always-tested tall list; a thin one (rare, only
        // for tiny fonts) has its midpoint within Margin of the band.
        return fillRects.FindTopMatch(baselineY - FillRectIndex.Margin, baselineY + 0.3 * fontSize + FillRectIndex.Margin, fr =>
        {
            if (fr.Ury - fr.Lly < minThickness) return false;      // thin bar = underline/strikeout
            if (fr.Lly > baselineY || fr.Ury < baselineY + 0.3 * fontSize) return false; // must straddle the glyph band
            var ox = Math.Max(0, Math.Min(rect.URX, fr.Urx) - Math.Max(rect.LLX, fr.Llx));
            if (ox * 2 < fragWidth) return false;
            return true;
        });
    }

    // Detect a strikethrough: a thin filled rect crossing the fragment's glyph body
    // (centre roughly 0.15–0.55·fontSize above the baseline — through the x-height),
    // spanning most of the run's width. Unlike an underline this sits ON the text, not
    // below it. Used to surface TextState.StrikeOut during extraction.
    private static RawFillRect? DetectStrikeoutRect(Rectangle rect, double baselineY,
        double fontSize, FillRectIndex fillRects)
    {
        var fragWidth = rect.URX - rect.LLX;
        if (fragWidth <= 0) return null;
        var maxThickness = Math.Max(1.5, 0.15 * fontSize);
        var loY = baselineY + 0.12 * fontSize;
        var hiY = baselineY + 0.58 * fontSize;
        // The predicate keys on the rect's midpoint (cy ∈ [loY, hiY]), which is exactly the
        // index key — the slice bounds are the band itself.
        return fillRects.FindTopMatch(loY, hiY, fr =>
        {
            var h = fr.Ury - fr.Lly;
            if (h > maxThickness) return false;
            var cy = (fr.Lly + fr.Ury) / 2;
            if (cy < loY || cy > hiY) return false;
            var ox = Math.Max(0, Math.Min(rect.URX, fr.Urx) - Math.Max(rect.LLX, fr.Llx));
            if (ox * 2 < fragWidth) return false;
            return true;
        });
    }

    /// <summary>Text rotation in degrees from the baseline direction vector (the
    /// text-space x-axis mapped through the text matrix and CTM), measured CCW
    /// from the page x-axis and normalised to [0, 360). Axis-aligned text yields
    /// exactly 0/90/180/270; arbitrary text matrices report their true angle.
    /// Returns null for a degenerate (zero-length) direction.</summary>
    private static double? RotationFromDirection(double tdx, double tdy)
    {
        if (Math.Abs(tdx) <= 1e-9 && Math.Abs(tdy) <= 1e-9) return null;
        var rot = Math.Atan2(tdy, tdx) * 180.0 / Math.PI;
        if (rot < 0) rot += 360.0;
        var snapped = Math.Round(rot);
        if (Math.Abs(rot - snapped) < 1e-6) rot = snapped >= 360 ? 0 : snapped;
        return rot;
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
        var preCount = _fragments.Count;
        // Later-text occlusion + clipped-away detection (stacked duplicate draws,
        // strip-clipped multi-pass pages): search matches report Invisible when
        // every spanned run is hidden, same as full extraction.
        var (laterInk, clippedAway, runBoxArea) = ComputeLaterInkOcclusion(rawFragments);
        // Phase 1: Build the concatenated text and character-to-run mapping
        var (concatenated, charToRun, runStartChar, bidiPerm) = BuildConcatenatedText(rawFragments);
        if (SearchDebug)
            Console.Error.WriteLine($"[searchtext:page{pageIndex}]<<<{concatenated}>>>");

        // Phase 2: Find matches in the concatenated text
        var matches = BuildMatches(concatenated);

        // Index the fill rects once so the per-match decoration probes below query a
        // baseline-local slice instead of rescanning the whole (possibly huge) list.
        var fillIndex = fillRects is { Count: > 0 } ? new FillRectIndex(fillRects) : null;

        // Phase 3: For each match, build a TextFragment with position, rect, and segments
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
            var fragment = new TextFragment(LogicalizeRtlPresentationForms(match.Value), rect, textState)
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
                bool wantUnderline = (_textSearchOptions?.SearchForTextRelatedGraphics ?? false)
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
                    if (capturedUl is not null) textState.SetCapturedUnderline(true);
                }
                // Source-highlight capture: lets a later text replacement splice the old
                // background rect out and re-draw it at the replacement's width.
                if (wantSourceDecorations)
                    capturedBg = DetectBackgroundRect(rect, baselineY, textState.FontSize, fillIndex);
                if (DetectStrikeoutRect(rect, baselineY, textState.FontSize, fillIndex) is not null)
                    textState.SetCapturedStrikeOut(true);
            }

            // Build per-run segments with position and rectangle
            BuildFragmentSegments(fragment, rawFragments, runStartChar,
                firstRunIdx, lastRunIdx, startCharIdx, endCharIdx, charToRun);

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
                fragment.MarkCapturedUnderlineSource(ulr.RawX, ulr.RawY, ulr.RawW, ulr.RawH);
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

    /// <summary>Standard symbol faces (Symbol, ZapfDingbats — any subset/style
    /// variant) decode through their built-in encodings, so the strict TrueType
    /// validation must not reject them.</summary>
    private static bool IsStandardSymbolFamily(string? baseFont)
    {
        if (string.IsNullOrEmpty(baseFont)) return false;
        var name = baseFont!;
        var plus = name.IndexOf('+');
        if (plus >= 0 && plus < name.Length - 1) name = name[(plus + 1)..];
        return name.StartsWith("Symbol", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("ZapfDingbats", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when the font's embedded TrueType program offers ONLY a
    /// symbolic (3,0) cmap subtable — no Mac (1,0) or Windows (3,1) map that a
    /// text extractor could decode through. Missing/unreadable programs count as
    /// symbol-only (nothing to decode with).</summary>
    private static bool HasOnlySymbolCmap(PdfDictionary fontDict, PdfReader reader)
    {
        try
        {
            var desc = reader.ResolveDict(fontDict.Get("FontDescriptor"));
            var ff = desc is null ? null : reader.ResolveStream(desc.Get("FontFile2"));
            if (ff is null) return true;
            var ttf = reader.DecodeStream(ff);
            if (ttf.Length < 12) return true;
            int numTables = (ttf[4] << 8) | ttf[5];
            for (var i = 0; i < numTables; i++)
            {
                var off = 12 + i * 16;
                if (off + 16 > ttf.Length) break;
                if (ttf[off] != 'c' || ttf[off + 1] != 'm' || ttf[off + 2] != 'a' || ttf[off + 3] != 'p') continue;
                var toff = (ttf[off + 8] << 24) | (ttf[off + 9] << 16) | (ttf[off + 10] << 8) | ttf[off + 11];
                if (toff + 4 > ttf.Length) return true;
                int n = (ttf[toff + 2] << 8) | ttf[toff + 3];
                for (var j = 0; j < n; j++)
                {
                    var rec = toff + 4 + j * 8;
                    if (rec + 8 > ttf.Length) break;
                    int pid = (ttf[rec] << 8) | ttf[rec + 1];
                    int eid = (ttf[rec + 2] << 8) | ttf[rec + 3];
                    if (pid == 1 || (pid == 3 && eid != 0) || pid == 0)
                        return false; // a decodable subtable exists
                }
                return true; // cmap present but symbol-only
            }
            return true; // no cmap at all
        }
        catch { return true; }
    }

    /// <summary>True when consecutive content runs jump UP by more than ~3 inches —
    /// the marker of a stream whose drawing order departs from reading order.</summary>
    private static bool HasMajorUpwardJump(List<RawTextRun> runs)
    {
        var prevY = double.NaN;
        foreach (var run in runs)
        {
            if (run.Text.Length == 0 || run.Text[0] == '\r' || run.Text[0] == '\n') continue;
            var (_, y) = ApplyCtm(run.X, run.Y, run.Ctm);
            if (!double.IsNaN(prevY) && y > prevY + 200.0) return true;
            prevY = y;
        }
        return false;
    }

    /// <summary>Reading-order permutation of the run list for Flatten-mode search:
    /// rows form by viewer-space baseline Y (top first, 2 pt band), runs within a
    /// row order left-to-right, and a line-break sentinel separates rows. Every
    /// downstream consumer indexes the same reordered list, so match→run mapping
    /// and geometry are untouched.</summary>
    private List<RawTextRun> ReorderRunsForFlatten(List<RawTextRun> runs)
    {
        var items = new List<(RawTextRun run, double y, double x)>();
        foreach (var r in runs)
        {
            if (r.Text == "\r\n" || r.Text.Length == 0) continue;
            var (px, py) = ApplyCtm(r.X, r.Y, r.Ctm);
            items.Add((r, py, px));
        }
        if (items.Count == 0) return runs;
        items.Sort((a, b) => a.y != b.y ? b.y.CompareTo(a.y) : a.x.CompareTo(b.x));

        var result = new List<RawTextRun>(runs.Count);
        var i = 0;
        while (i < items.Count)
        {
            var rowY = items[i].y;
            var row = new List<(RawTextRun run, double y, double x)>();
            while (i < items.Count && rowY - items[i].y <= 2.0) { row.Add(items[i]); i++; }
            row.Sort((a, b) => a.x.CompareTo(b.x));
            if (result.Count > 0)
            {
                var f = row[0].run;
                result.Add(new RawTextRun("\r\n", f.X, f.Y, f.FontSize, f.FontName, 0, f.Ctm, f.Metrics));
            }
            foreach (var t in row) result.Add(t.run);
        }
        return result;
    }

    /// <summary>
    /// Concatenates text from raw runs into a single searchable string, inserting
    /// spaces at detected word gaps, removing false newlines at BT/ET boundaries,
    /// and applying bidi reordering + Arabic normalization for phrase search.
    /// </summary>
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
                        // Glyph-by-glyph text: no word-gap space after a sentence-terminating
                        // period between two single glyphs — the following capital starts a new
                        // run that reads as directly following (the extractor's
                        // period-boundary behaviour on such overlays).
                        && !(bothSingle && prev.Text == ".");
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
    /// Flags runs whose immediately-preceding horizontal gap is part of a uniformly
    /// letter-tracked same-line sequence (constant inter-glyph advance), as opposed to a
    /// genuine word break. Letterhead/display text is often drawn with a fixed tracking
    /// that exceeds a normal word space, so a per-gap size threshold cannot tell it from a
    /// word gap — but its hallmark is that EVERY adjacent gap on the line is (near) equal.
    /// A run is flagged only inside a window of ≥3 consecutive near-equal, positive,
    /// sub-word-sized gaps. Word boundaries on such lines are explicit space glyphs, which
    /// are still appended, so suppressing gap-spaces here keeps words intact without merging.
    /// </summary>
    /// <summary>
    /// Flags runs that start PART-WAY THROUGH the previous run's advance — past its
    /// origin, but short of where it ends — in a way that reads as a token boundary.
    /// Japanese full-width punctuation is the everyday case: '）' and '、' carry a
    /// full-em advance but are set half-width, so the next glyph lands half an em early
    /// and a space is reported there, the pen having deviated from a flush
    /// advance by as much as a word gap does.
    /// <para>
    /// ★ Only an ISOLATED squeeze counts. A line drawn glyph by glyph at a uniformly
    /// tight step overlaps at EVERY pair, and that is how the line was set, not a token
    /// boundary — spacing those turns one word into one fragment per letter. So a gap
    /// whose same-line neighbour on either side is squeezed the same way is left alone,
    /// the same reasoning <see cref="ComputeLetterTrackedGaps"/> applies to uniform
    /// positive gaps.
    /// </para>
    /// Two neighbours this never claims: a doubled draw (a drop shadow re-starts AT the
    /// previous origin) and a back-jump (it starts left of it). Rotated runs are excluded
    /// outright — they advance along Y, so an X-gap between glyphs of one word is an
    /// artifact of the rotation and reads negative.
    /// </summary>
    private static bool[] ComputeSqueezedGaps(List<RawTextRun> runs)
    {
        var raw = new bool[runs.Count];
        // Index of the previous CONTENT run, so the neighbour test walks real glyphs and
        // steps over the \r\n sentinels that BT/ET boundaries inject mid-line.
        var prevOf = new int[runs.Count];
        var lastContent = -1;
        for (var i = 0; i < runs.Count; i++)
        {
            prevOf[i] = -1;
            if (runs[i].Text == "\r\n") continue;
            prevOf[i] = lastContent;
            lastContent = i;
            var p = prevOf[i];
            if (p < 0) continue;

            var prev = runs[p];
            var cur = runs[i];
            if (Math.Abs(cur.TmB) > Math.Abs(cur.TmA) * 1e-3
                || Math.Abs(prev.TmB) > Math.Abs(prev.TmA) * 1e-3
                || !IsUprightCtm(cur) || !IsUprightCtm(prev)) continue;
            if (Math.Abs(cur.Y - prev.Y) >= 2.0) continue; // different line

            var prevTmScale = Math.Abs(prev.TmA) > 0 ? Math.Abs(prev.TmA) : 1.0;
            var prevAdvance = (prev.Width > 0
                ? prev.Width * prev.HScaling
                : EstimateWidth(prev.Text, prev.FontSize)) * prevTmScale;
            if (prevAdvance <= 0) continue;
            var overlap = prev.X + prevAdvance - cur.X;
            var fs = cur.FontSize > 0 ? cur.FontSize : 12.0;
            var effFontSize = fs * (Math.Abs(cur.TmA) > 0 ? Math.Abs(cur.TmA) : 1.0);
            raw[i] = overlap > effFontSize * 0.2 && cur.X >= prev.X + prevAdvance * 0.25;
        }

        var isolated = new bool[runs.Count];
        // The NEXT content run, so "squeezed on both sides" can be asked of a gap.
        var nextOf = new int[runs.Count];
        var nextContent = -1;
        for (var i = runs.Count - 1; i >= 0; i--)
        {
            nextOf[i] = nextContent;
            if (runs[i].Text != "\r\n") nextContent = i;
        }
        for (var i = 0; i < runs.Count; i++)
        {
            if (!raw[i]) continue;
            var before = prevOf[i] >= 0 && raw[prevOf[i]];
            var after = nextOf[i] >= 0 && raw[nextOf[i]];
            isolated[i] = !before && !after;
        }
        return isolated;
    }

    /// <summary>Ideographs, kana and the CJK punctuation/fullwidth blocks — scripts whose
    /// glyphs stand alone rather than spelling a word out of letters.</summary>
    private static bool IsCjk(char c) =>
        (c >= '　' && c <= 'ヿ')     // CJK symbols & punctuation, hiragana, katakana
        || (c >= '㐀' && c <= '䶿')  // unified ideographs extension A
        || (c >= '一' && c <= '鿿')  // unified ideographs
        || (c >= '豈' && c <= '﫿')  // compatibility ideographs
        || (c >= '＀' && c <= '￯'); // halfwidth & fullwidth forms

    private static bool[] ComputeLetterTrackedGaps(List<RawTextRun> runs)
    {
        var tracked = new bool[runs.Count];
        var i = 0;
        while (i < runs.Count)
        {
            if (runs[i].Text == "\r\n") { i++; continue; }

            // Gather one line: consecutive content runs sharing a baseline. Compare each run
            // to the PREVIOUS run's Y (not the line's first) so a slightly sloped/italic
            // glyph-by-glyph baseline (a small consistent per-glyph drift) stays one line
            // instead of fragmenting — matching the guard site's adjacent deltaY check.
            var line = new List<int>();
            var prevLineY = runs[i].Y;
            var j = i;
            while (j < runs.Count)
            {
                if (runs[j].Text == "\r\n") { j++; continue; }
                if (line.Count > 0 && Math.Abs(runs[j].Y - prevLineY) >= 2.0) break;
                line.Add(j);
                prevLineY = runs[j].Y;
                j++;
            }
            var lineY = runs[i].Y;

            // Need ≥3 gaps (≥4 runs) to call a pattern "uniform tracking".
            if (line.Count >= 4)
            {
                var gaps = new double[line.Count];
                var subWord = new double[line.Count]; // 0.6·effFont ceiling per gap
                // A gap bordered by a MULTI-WORD run (a real space glyph among other
                // characters) is a WORD boundary, never intra-word letter tracking:
                // tracking splits one word into letter runs ("M","ARK"), while a
                // justified line drawn word-per-Tm has uniform ~space-sized gaps between
                // whole phrases ("…to 24"|"MAR"|"2013. During…") that must keep their
                // word spaces. A run that IS whitespace (an explicit space-glyph run
                // between tracked letters — 'M'|'ARK'|' '|'A.') stays trackable: such
                // lines mark their word breaks with the space runs themselves.
                // ★ Tracking is a LATIN phenomenon: it spreads the letters of one word,
                // which is why suppressing its gaps keeps the word whole. A CJK glyph is
                // not a letter of a word — a name set with its ideographs spread evenly
                // across a fixed column width looks identical to tracked text, and the
                // gaps there are real: a page that draws '監 察 監 督 官' glyph by glyph
                // reads back with a space at every gap. So a gap facing an ideograph or
                // kana is never claimed as tracking.
                var cjky = new bool[line.Count];
                var wordy = new bool[line.Count];
                for (var k = 0; k < line.Count; k++)
                {
                    var t = runs[line[k]].Text;
                    wordy[k] = t.Contains(' ') && t.Trim().Length > 0;
                    cjky[k] = false;
                    foreach (var ch in t)
                        if (IsCjk(ch)) { cjky[k] = true; break; }
                }
                for (var k = 1; k < line.Count; k++)
                {
                    var prev = runs[line[k - 1]];
                    var cur = runs[line[k]];
                    var prevEndX = prev.X + (prev.Width > 0 ? prev.Width * prev.HScaling : EstimateWidth(prev.Text, prev.FontSize));
                    gaps[k] = cur.X - prevEndX;
                    var fs = cur.FontSize > 0 ? cur.FontSize : 12.0;
                    var sx = Math.Abs(cur.TmA) > 0 ? Math.Abs(cur.TmA) : 1.0;
                    subWord[k] = 0.6 * fs * sx;
                }

                // Letter-tracking splits a WORD into short pieces ("M","ARK"): a
                // window only counts when its runs are word FRAGMENTS (one side a
                // 1–2 char piece, neither side a whole 4+ char word). Justified
                // prose drawn word-per-run also has uniform sub-word gaps, but its
                // runs are whole words — suppressing those spaces glued sentences
                // ("…accessanduseServices…").
                bool PieceLike(int ka, int kb)
                {
                    var la = runs[line[ka]].Text.Trim().Length;
                    var lb = runs[line[kb]].Text.Trim().Length;
                    return (la <= 2 || lb <= 2) && la < 4 && lb < 4;
                }
                var k0 = 1;
                while (k0 < line.Count)
                {
                    // Seed a window on a positive, sub-word-sized gap between space-free runs.
                    if (!(gaps[k0] > 0 && gaps[k0] < subWord[k0] && !wordy[k0 - 1] && !wordy[k0]
                          && !cjky[k0 - 1] && !cjky[k0]
                          && PieceLike(k0 - 1, k0))) { k0++; continue; }
                    var k1 = k0;
                    while (k1 + 1 < line.Count
                        && gaps[k1 + 1] > 0
                        && gaps[k1 + 1] < subWord[k1 + 1]
                        && !wordy[k1] && !wordy[k1 + 1]
                        && !cjky[k1] && !cjky[k1 + 1]
                        && PieceLike(k1, k1 + 1)
                        && Math.Abs(gaps[k1 + 1] - gaps[k0]) <= Math.Max(0.5, 0.2 * gaps[k0]))
                    {
                        k1++;
                    }
                    if (k1 - k0 + 1 >= 3)
                        for (var k = k0; k <= k1; k++) tracked[line[k]] = true;
                    k0 = Math.Max(k1 + 1, k0 + 1);
                }

                // Continuous letter-tracking (NON-uniform): within a maximal run of consecutive
                // single-char glyphs, if a MAJORITY of inter-glyph gaps carry a word-sized gap,
                // the run is one token spelled out with loose per-glyph spacing (every letter is
                // gapped) — none of the gaps are real word breaks (cf. a loosely-tracked
                // "American" or a code "ADED1"). This is distinguished from genuinely
                // word-separated glyph-by-glyph text (an OCR overlay) where letters are packed
                // tight and only a MINORITY of gaps — the actual word spaces — exceed the
                // threshold. Applied per single-char RUN (not per line) so a glyph-by-glyph
                // token embedded among coalesced words is still handled.
                {
                    var s = 0;
                    while (s < line.Count)
                    {
                        if (runs[line[s]].Text.Length != 1) { s++; continue; }
                        var e = s;
                        while (e + 1 < line.Count && runs[line[e + 1]].Text.Length == 1) e++;
                        // [s..e] is a maximal single-char run; its gaps are at k=s+1..e.
                        var totalSs = e - s;
                        if (totalSs >= 3)
                        {
                            var overThr = 0;
                            var packed = 0;
                            var doubled = 0;
                            for (var k = s + 1; k <= e; k++)
                            {
                                var fs = runs[line[k]].FontSize > 0 ? runs[line[k]].FontSize : 12.0;
                                var sx = Math.Abs(runs[line[k]].TmA) > 0 ? Math.Abs(runs[line[k]].TmA) : 1.0;
                                if (gaps[k] > 0.2 * fs * sx) overThr++;
                                // With Tz-scaled widths a genuinely packed glyph pair CLOSES: the
                                // next glyph starts left of the previous glyph's rendered right edge
                                // (a negative gap). Word-spaced glyph text packs DIFFERENT glyphs
                                // tight like this and opens only at the sparse real word breaks.
                                if (gaps[k] < 0) packed++;
                                // Drop-shadow doubling (e.g. an IgnoreShadowText source: "CCoonn…")
                                // repeats the SAME character across each small negative overlap; its
                                // inter-letter advances read word-sized so overThr is high, but it is
                                // NOT word-spaced and must stay tracked so the de-shadowed word is not
                                // split. Char-doubling separates it from real word-spaced glyph text.
                                if (runs[line[k]].Text == runs[line[k - 1]].Text) doubled++;
                            }
                            // Loose/uniform letter-tracking ("American", "ADED1") keeps every gap a
                            // similar POSITIVE amount → overThr high, packed ~0 → tracked. Shadow
                            // doubling → overThr high, packed high, but doubled high → tracked. Only
                            // tight word-spaced glyph text (packed high, doubled low) is left alone so
                            // its real word breaks survive.
                            bool shadowLike = doubled >= totalSs * 0.3;
                            bool wordSpaced = !shadowLike && packed >= totalSs * 0.4;
                            if (overThr >= totalSs * 0.4 && !wordSpaced)
                                for (var k = s + 1; k <= e; k++)
                                    if (!cjky[k - 1] && !cjky[k]) tracked[line[k]] = true;
                        }
                        s = e + 1;
                    }
                }
            }

            i = j;
        }
        return tracked;
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
    private static (double descentOff, double ascentH) ComputeDescentAscent(RawTextRun run)
    {
        double effectiveDescent = 0;
        if (run.Metrics is not null && run.Metrics.Descent != 0)
            effectiveDescent = run.Metrics.Descent;
        else if (!string.IsNullOrEmpty(run.FontName))
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

    /// <summary>Builds a TextState from the first run's font properties.</summary>
    private static TextState BuildTextState(RawTextRun run)
    {
        // Effective (device) font size: the Tm up-axis composed with the CTM —
        // a page that scales its content via `cm` (e.g. 0.75) reports the scaled
        // size (Tf 21.33 under 0.75 cm → 16).
        var upX = run.TmC * run.Ctm.A + run.TmD * run.Ctm.C;
        var upY = run.TmC * run.Ctm.B + run.TmD * run.Ctm.D;
        var tmScale = Math.Sqrt(upX * upX + upY * upY);
        var effectiveFs = tmScale > 0.001 && Math.Abs(tmScale - 1.0) > 0.001
            ? run.FontSize * tmScale : run.FontSize;
        var ts = new TextState
        {
            FontSize = (float)effectiveFs,
            FontName = run.FontName,
            RenderingMode = (Aspose.Pdf.Text.TextRenderingMode)run.RenderingMode,
            IsBold = run.IsBold,
            IsItalic = run.IsItalic,
            Font = run.FontInfoObj ?? FontInfo.DefaultHelvetica,
            TextRise = run.TextRise,
            IsSuperscript = run.TextRise > 0,
            IsSubscript = run.TextRise < 0,
        };
        ts.SetCapturedForegroundColor(ForegroundColorOf(run));
        ts.StrokingColor = run.StrokingColor;
        // The run's spacing state (Tz is stored as a fraction; the property is a
        // percentage).
        ts.CharacterSpacing = (float)run.CharSpacing;
        ts.WordSpacing = (float)run.WordSpacing;
        ts.HorizontalScaling = (float)(run.HScaling * 100);
        ts.SourceTmScale = Math.Abs(run.TmD) > 1e-9 ? run.TmA / run.TmD : 1.0;
        return ts;
    }

    /// <summary>
    /// Computes the trailing Tc/spacing contribution at the end of the last matched run.
    /// This value is subtracted from bg rect width so it covers only visible text.
    /// </summary>
    private static double ComputeTrailingTc(List<RawTextRun> rawFragments, int[] runStartChar,
        int lastRunIdx, int endCharIdx)
    {
        var lastRun = rawFragments[lastRunIdx];
        var matchEndInRun = endCharIdx - runStartChar[lastRunIdx] + 1;
        if (matchEndInRun >= 2
            && lastRun.CharCumWidths is not null && matchEndInRun < lastRun.CharCumWidths.Length
            && lastRun.Metrics is not null)
        {
            var lastCharAdvance = lastRun.CharCumWidths[matchEndInRun] - lastRun.CharCumWidths[matchEndInRun - 1];
            var lastCharText = lastRun.Text[(matchEndInRun - 1)..matchEndInRun];
            var lastGlyphW = lastRun.Metrics.MeasureString(lastCharText, lastRun.FontSize);
            var tcUnscaled = lastCharAdvance - lastGlyphW;
            // Only the Tc/Tw SPACING part of the excess advance is trimmed off the
            // highlight. An excess from a TJ kern is layout (a tab-like gap the
            // producer drew into the line), and the highlight keeps covering it —
            // the fragment rectangle spans to where the next run starts.
            var trailingSpacing = lastRun.CharSpacing
                + (lastCharText == " " ? lastRun.WordSpacing : 0);
            tcUnscaled = Math.Min(tcUnscaled, trailingSpacing);
            if (tcUnscaled > 0.01)
                return tcUnscaled * lastRun.HScaling * Math.Abs(lastRun.TmA);
        }
        return 0;
    }

    /// <summary>
    /// Shaped Arabic/Hebrew PRESENTATION FORMS are emitted (by the generator / HTML converter)
    /// in VISUAL order — the glyphs as drawn left-to-right — so a pure run of them extracts
    /// reversed from logical reading order. Reverse it back to logical. Scoped to presentation
    /// forms so raw Hebrew/Arabic (stored visually in source PDFs and matched visually by
    /// TextReplacer) is left untouched.
    /// </summary>
    private static string LogicalizeRtlPresentationForms(string text)
    {
        if (text.Length >= 2 && text[0] >= 0xFB1D)
        if (text.Length < 2) return text;
        var hasPresForm = false;
        foreach (var c in text)
        {
            if ((c >= 0x0590 && c <= 0x05FF) || (c >= 0xFB1D && c <= 0xFDFF) || (c >= 0xFE70 && c <= 0xFEFF))
                hasPresForm = true;
            else if (c == ' ' || c == '\t' || c == '\r' || c == '\n'
                     || (c >= '!' && c <= '/') || (c >= ':' && c <= '@')
                     || (c >= '[' && c <= '`') || (c >= '{' && c <= '~'))
            { /* neutral punctuation / whitespace — allowed inside an RTL run */ }
            else
                return text; // an LTR letter or a raw (unshaped) RTL char → leave as-is
        }
        if (!hasPresForm) return text;
        var arr = text.ToCharArray();
        System.Array.Reverse(arr);
        return new string(arr);
    }

    /// <summary>
    /// Builds per-source-run TextSegments for a fragment, each with accurate
    /// position, rectangle, and text state derived from its source run.
    /// </summary>
    private static void BuildFragmentSegments(TextFragment fragment, List<RawTextRun> rawFragments,
        int[] runStartChar, int firstRunIdx, int lastRunIdx, int startCharIdx, int endCharIdx,
        List<int>? charToRun = null)
    {
        fragment.Segments.Clear();

        // With a char-to-run map, walk the match's CHAR RANGE and group consecutive
        // chars by run — correct for any run order in char space (a back-jump
        // PREPEND places a later-drawn run in front of an earlier one, so the
        // run-index range [first..last] can miss runs the match actually covers).
        if (charToRun is not null)
        {
            var cc = startCharIdx;
            while (cc <= endCharIdx && cc < charToRun.Count)
            {
                var ri = charToRun[cc];
                var gStart = cc;
                while (cc <= endCharIdx && cc < charToRun.Count && charToRun[cc] == ri) cc++;
                if (ri < 0 || ri >= rawFragments.Count) continue;
                var grun = rawFragments[ri];
                if (grun.Text == "\r\n") continue; // newline sentinels

                var gSegStart = gStart - runStartChar[ri];
                var gSegEnd = (cc - 1) - runStartChar[ri];
                if (gSegStart < 0) gSegStart = 0;
                if (gSegEnd >= grun.Text.Length) gSegEnd = grun.Text.Length - 1;
                if (gSegEnd < gSegStart) continue;

                var gText = grun.Text.Substring(gSegStart, gSegEnd - gSegStart + 1);
                var gSeg = BuildSegment(grun, gText, gSegStart, gSegEnd, ri);
                gSeg.Position = ComputeSegmentPosition(grun, gSegStart);
                gSeg.Rectangle = ComputeSegmentRectangle(grun, gText, gSegStart, gSegEnd);
                PopulateCharacters(gSeg, grun, gSegStart, gSegEnd);
                fragment.Segments.Add(gSeg);
            }
            if (fragment.Segments.Count == 0)
                fragment.Segments.Add(new TextSegment(fragment.Text));
            return;
        }

        for (var ri = firstRunIdx; ri <= lastRunIdx; ri++)
        {
            var run = rawFragments[ri];
            if (run.Text == "\r\n") continue; // skip newline sentinels

            // Determine the portion of this run that is part of the match
            var runStart = runStartChar[ri];
            var segStartInRun = (ri == firstRunIdx) ? startCharIdx - runStart : 0;
            var segEndInRun = (ri == lastRunIdx) ? endCharIdx - runStart : run.Text.Length - 1;
            if (segStartInRun < 0) segStartInRun = 0;
            if (segEndInRun >= run.Text.Length) segEndInRun = run.Text.Length - 1;
            if (segEndInRun < segStartInRun) continue;

            var segText = run.Text.Substring(segStartInRun, segEndInRun - segStartInRun + 1);
            var seg = BuildSegment(run, segText, segStartInRun, segEndInRun, ri);

            // Compute segment position with within-run offset and descent
            seg.Position = ComputeSegmentPosition(run, segStartInRun);

            // Compute segment bounding rectangle
            seg.Rectangle = ComputeSegmentRectangle(run, segText, segStartInRun, segEndInRun);

            // Populate per-character layout (position + glyph rectangle).
            PopulateCharacters(seg, run, segStartInRun, segEndInRun);

            fragment.Segments.Add(seg);
        }
        if (fragment.Segments.Count == 0)
            fragment.Segments.Add(new TextSegment(fragment.Text));
    }

    /// <summary>Creates a TextSegment from a run with text state properties.</summary>
    private static TextSegment BuildSegment(RawTextRun run, string text,
        int startInRun, int endInRun, int runIndex)
    {
        var upX_ = run.TmC * run.Ctm.A + run.TmD * run.Ctm.C;
            var upY_ = run.TmC * run.Ctm.B + run.TmD * run.Ctm.D;
            var tmScale = Math.Sqrt(upX_ * upX_ + upY_ * upY_);
        var effectiveFs = tmScale > 0.001 && Math.Abs(tmScale - 1.0) > 0.001
            ? run.FontSize * tmScale : run.FontSize;
        var seg = new TextSegment(text)
        {
            StartCharIndex = startInRun,
            EndCharIndex = endInRun,
            SourceRunIndex = runIndex,
        };
        seg.TextState.FontSize = (float)effectiveFs;
        seg.TextState.RawFontSize = (float)run.FontSize;
        seg.TextState.TmD = run.TmD;
        seg.TextState.FontName = run.FontName;
        seg.TextState.RenderingMode = (Aspose.Pdf.Text.TextRenderingMode)run.RenderingMode;
        seg.TextState.StrokingColor = run.StrokingColor;
        seg.TextState.IsBold = run.IsBold;
        seg.TextState.IsItalic = run.IsItalic;
        seg.TextState.Font = run.FontInfoObj ?? FontInfo.DefaultHelvetica;
        seg.TextState.TextRise = run.TextRise;
        seg.TextState.IsSuperscript = run.TextRise > 0;
        seg.TextState.IsSubscript = run.TextRise < 0;
        // The run's spacing state is part of what the segment reports back
        // (Tz is stored as a fraction; the property is a percentage).
        seg.TextState.CharacterSpacing = (float)run.CharSpacing;
        seg.TextState.WordSpacing = (float)run.WordSpacing;
        seg.TextState.HorizontalScaling = (float)(run.HScaling * 100);
        seg.TextState.OwnerSegment = seg;
        return seg;
    }

    /// <summary>Fills <see cref="TextSegment.Characters"/> with one entry per
    /// character in the segment, each carrying the character's page-space position
    /// and glyph bounding rectangle. Reuses the segment position/rectangle math
    /// applied to a single-character range.</summary>
    /// <summary>
    /// Some embedded/subset fonts can't measure individual glyphs — per-character
    /// advance comes back as 0 even though the run's total width is correct — which
    /// collapses the cumulative-width array to <c>[0,…,0,total]</c>. That would place
    /// every character but the last at the run origin (breaking per-char
    /// <see cref="CharInfo.Rectangle"/> and, in turn, marked-text extraction). When
    /// that degenerate shape is detected, distribute the total width evenly across
    /// the characters. No-op for well-formed arrays.
    /// </summary>
    private static void NormalizeDegenerateCumWidths(double[]? cum)
    {
        if (cum is not { Length: > 2 }) return;
        var total = cum[cum.Length - 1];
        if (total <= 0) return;
        var degenerate = false;
        for (var i = 1; i < cum.Length - 1; i++)
            if (cum[i] <= 0) { degenerate = true; break; }
        if (!degenerate) return;
        var n = cum.Length - 1;
        for (var i = 0; i <= n; i++) cum[i] = total * i / n;
    }

    private static void PopulateCharacters(TextSegment seg, RawTextRun run,
        int segStartInRun, int segEndInRun)
    {
        seg.Characters.Clear();
        for (var ci = segStartInRun; ci <= segEndInRun && ci < run.Text.Length; ci++)
        {
            var charText = run.Text.Substring(ci, 1);
            var pos = ComputeSegmentPosition(run, ci);
            var rect = ComputeSegmentRectangle(run, charText, ci, ci);
            seg.Characters.Add(new CharInfo(pos, rect));
        }
    }

    /// <summary>Computes a segment's page-space position from its run and within-run offset.</summary>
    private static Position ComputeSegmentPosition(RawTextRun run, int segStartInRun)
    {
        double segX = run.X, segY = run.Y;
        if (segStartInRun > 0 && segStartInRun < run.Text.Length)
        {
            var prefW = MeasureRunPrefix(run, segStartInRun);
            segX = run.X + run.TmA * prefW * run.HScaling;
            segY = run.Y + run.TmB * prefW * run.HScaling;
        }
        // Apply descent offset — fall back to Standard-14 AFM descent
        double segDescentOff = 0;
        double effectiveDescent = 0;
        if (run.Metrics is not null && run.Metrics.Descent != 0)
            effectiveDescent = run.Metrics.Descent;
        else if (!string.IsNullOrEmpty(run.FontName))
            effectiveDescent = Standard14Fonts.GetDescent(run.FontName!);
        if (effectiveDescent != 0)
            segDescentOff = effectiveDescent * run.FontSize / 1000.0;
        var (px, py) = ApplyCtm(segX + run.TmC * segDescentOff,
                                 segY + run.TmD * segDescentOff, run.Ctm);
        return new Position(Q(px), Q(py));
    }

    /// <summary>Computes a segment's bounding rectangle from its run, text, and character range.</summary>
    private static Rectangle ComputeSegmentRectangle(RawTextRun run, string segText,
        int segStartInRun, int segEndInRun)
    {
        double segW;
        if (run.CharCumWidths is not null)
        {
            var segEndPos = Math.Min(segEndInRun + 1, run.CharCumWidths.Length - 1);
            segW = run.CharCumWidths[segEndPos]
                 - (segStartInRun < run.CharCumWidths.Length ? run.CharCumWidths[segStartInRun] : 0);
        }
        else if (run.Metrics is not null)
            segW = run.Metrics.MeasureString(segText, run.FontSize);
        else
            segW = EstimateWidth(segText, run.FontSize);

        double segAscentH = run.FontSize;
        if (run.Metrics is not null && run.Metrics.Ascent > 0)
            segAscentH = run.Metrics.Ascent * run.FontSize / 1000.0;
        var (descentOff, _) = ComputeDescentAscent(run);

        double segX = run.X, segY = run.Y;
        if (segStartInRun > 0 && segStartInRun < run.Text.Length)
        {
            var prefW = MeasureRunPrefix(run, segStartInRun);
            segX = run.X + run.TmA * prefW * run.HScaling;
            segY = run.Y + run.TmB * prefW * run.HScaling;
        }
        var scaledSegW = segW * run.HScaling;
        var (x1, y1) = ApplyCtm(segX + run.TmC * descentOff, segY + run.TmD * descentOff, run.Ctm);
        var (x2, y2) = ApplyCtm(segX + run.TmA * scaledSegW + run.TmC * segAscentH,
                                 segY + run.TmB * scaledSegW + run.TmD * segAscentH, run.Ctm);
        return new Rectangle(Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2));
    }

    private MatchCollection BuildMatches(string text)
    {
        // Check TextSearchOptions at search time (may have been set after construction)
        var isRegex = _isRegex || (_textSearchOptions?.IsRegularExpression ?? false);
        var caseSensitive = _textSearchOptions is not null ? _textSearchOptions.CaseSensitive : _caseSensitive;
        var wholeWord = _wholeWord || (_textSearchOptions?.WholeWord ?? false);

        var phrase = NormalizeArabicPresentationForms(_searchPhrase!);
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

    /// <summary>
    /// A simple 3x2 affine matrix (a, b, c, d, e, f) for CTM tracking.
    /// Represents the transformation: [a b 0; c d 0; e f 1]
    /// </summary>
    private readonly record struct Matrix(double A, double B, double C, double D, double E, double F)
    {
        public static readonly Matrix Identity = new(1, 0, 0, 1, 0, 0);

        /// <summary>
        /// Multiply this matrix by another: this * other
        /// </summary>
        public Matrix Multiply(Matrix other)
        {
            return new Matrix(
                A * other.A + B * other.C,
                A * other.B + B * other.D,
                C * other.A + D * other.C,
                C * other.B + D * other.D,
                E * other.A + F * other.C + other.E,
                E * other.B + F * other.D + other.F
            );
        }
    }

    /// <summary>
    /// Apply a CTM matrix to a point.
    /// </summary>
    /// <summary>Quantize an extracted position coordinate through single precision.
    /// Text positions are computed in float, so the reported XIndent for e.g.
    /// text-space 355 under a 0.24 scale is 85.19999695…, a hair BELOW the
    /// decimal 85.2 — while exact double arithmetic lands a hair above. Position
    /// expectations (85.19 ± 0.01) sit right at that boundary, so extracted
    /// positions must take the same rounding.</summary>
    private static double Q(double v) => (float)v;

    private static (double x, double y) ApplyCtm(double x, double y, Matrix ctm)
    {
        var tx = ctm.A * x + ctm.C * y + ctm.E;
        var ty = ctm.B * x + ctm.D * y + ctm.F;
        return (tx, ty);
    }

    /// <summary>Whether the run's CTM carries no rotation (pure translation/scale/flip).
    /// Only such runs may be compared in PAGE space: under a rotated CTM the text-space
    /// X advance leaks into page-Y (a flat-Tm glyph-per-op producer would split into one
    /// line per glyph), and a rotated page CTM likewise turns rotated-Tm labels' raw-Y
    /// baseline into a page-Y spread. Both must keep the raw text-space comparison.</summary>
    private static bool IsUprightCtm(RawTextRun run) =>
        Math.Abs(run.Ctm.B) <= 1e-4 * Math.Abs(run.Ctm.A);

    /// <summary>
    /// Compute the page-rotation CTM for a page, matching the TypeScript
    /// <c>pageRotationCtm</c> function.  Returns null for Rotate=0/unset.
    /// </summary>
    private static Matrix? PageRotationCtm(Page page)
    {
        var rotate = ((page.RotateDegrees % 360) + 360) % 360;
        if (rotate == 0) return null;
        var mb = page.MediaBox;
        var w = mb.URX - mb.LLX;
        var h = mb.URY - mb.LLY;
        return rotate switch
        {
            90  => new Matrix( 0, -1,  1,  0,  0, w),
            180 => new Matrix(-1,  0,  0, -1,  w, h),
            270 => new Matrix( 0,  1, -1,  0,  h, 0),
            _   => null,
        };
    }

    /// <summary>
    /// Check if two rectangles overlap (share any area).
    /// </summary>
    private static bool RectanglesOverlap(Rectangle a, Rectangle b)
    {
        // A fragment is included when its vertical center falls within the search rectangle's
        // Y bounds AND it overlaps with the X bounds. This prevents counting fragments whose
        // baseline just clips the rectangle edge.
        var aCenterY = (a.LLY + a.URY) / 2.0;
        if (aCenterY < b.LLY || aCenterY > b.URY) return false;
        if (a.URX < b.LLX || a.LLX > b.URX) return false;
        return true;
    }

    /// <summary>
    /// Check if the given point is contained within (or on the boundary of) a rectangle.
    /// </summary>
    private static bool RectangleContainsPoint(Rectangle rect, double x, double y)
        => x >= rect.LLX && x <= rect.URX && y >= rect.LLY && y <= rect.URY;

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
    /// The visible foreground colour of a run. Stroke-only text rendering modes (1 and 5)
    /// paint the glyph outline in the stroking colour and never use the fill colour, so the
    /// stroking colour is the foreground there; every other mode is fill-based.
    /// </summary>
    private static Color ForegroundColorOf(RawTextRun run)
    {
        if ((run.RenderingMode == 1 || run.RenderingMode == 5) && run.StrokingColor is { } sc)
            return sc;
        return run.FillColor ?? Color.Black;
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

    /// <summary>Map each nameless Type3 font resource key in <paramref name="resourceDict"/>'s
    /// /Font to its synthesised "T3Font_&lt;n&gt;" handle, indexed by /Font enumeration order —
    /// the same assignment <see cref="FontCollection"/> makes, so the absorber's per-fragment
    /// font name agrees with the resource-collection view.</summary>
    private static Dictionary<string, string> BuildType3SynthesizedNames(
        PdfDictionary? resourceDict, PdfReader reader)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (resourceDict is null) return map;
        // resourceDict is a page/form dict (fonts under /Resources/Font, possibly inherited
        // from an ancestor page) or already a /Resources dict (fonts under /Font). Resolve
        // the effective /Font the same way the FontCollection does so the Keys enumeration
        // order — and thus the T3Font_<n> index — matches the resource-collection view.
        var fontDict = ResolveEffectiveFontDict(resourceDict, reader);
        if (fontDict is null) return map;
        var t3 = 0;
        foreach (var key in fontDict.Keys)
        {
            var fd = reader.ResolveDict(fontDict.Get(key));
            if (fd is not null && fd.GetName("Subtype") == "Type3" && fd.GetName("BaseFont") is null)
                map[key] = $"T3Font_{t3++}";
        }
        return map;
    }

    /// <summary>Resolve the effective /Font dictionary for a page/form/resource dict: its own
    /// /Font (already a resource dict), else /Resources/Font, else the nearest ancestor page's
    /// /Resources/Font via the /Parent chain (inheritable per PDF 32000 §7.7.3.4).</summary>
    private static PdfDictionary? ResolveEffectiveFontDict(PdfDictionary dict, PdfReader reader)
    {
        var direct = reader.ResolveDict(dict.Get("Font"));
        if (direct is not null) return direct;
        var res = reader.ResolveDict(dict.Get("Resources"));
        var f = res is null ? null : reader.ResolveDict(res.Get("Font"));
        if (f is not null) return f;
        var parentObj = dict.Get("Parent");
        var visited = new HashSet<int>();
        while (parentObj is not null)
        {
            if (parentObj is PdfIndirectRef iref && !visited.Add(iref.ObjectNumber)) break;
            var parent = reader.ResolveDict(parentObj);
            if (parent is null) break;
            var pres = reader.ResolveDict(parent.Get("Resources"));
            var pf = pres is null ? null : reader.ResolveDict(pres.Get("Font"));
            if (pf is not null) return pf;
            parentObj = parent.Get("Parent");
        }
        return null;
    }
}
