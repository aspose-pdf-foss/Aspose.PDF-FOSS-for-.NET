using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextAbsorber
{
    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private void ShowTextOp(ExtractState xs, string op)
    {
    _textShowingOpCount++;
    EnsureFontSet(xs.fontSet, op);
    if (xs.skipText) return;
    _pageHasRotatedText |= xs.tmRotated;
    _currentLineEffFs = xs.tmRotated
        ? Math.Abs(xs.fontSize * xs.tmN)  // composed projection norm already carries the CTM; the scalar d is ~0 sideways
        : Math.Abs(xs.fontSize * (xs.tmD > 0 ? xs.tmD : xs.tmN) * xs.localCmD);
    _currentLineDescent = xs.currentMetrics is not null && xs.currentMetrics.Descent < 0
        ? -xs.currentMetrics.Descent / 1000.0
        : 0.2;
    _currentLineIsRotated = xs.tmRotated && !_pageRotDominant
        && (_text.Length == 0 || _text[^1] == '\n' || _currentLineIsRotated);
    if (_currentLineIsRotated && double.IsNaN(_currentLineDevY))
    {
        _currentLineDevY = xs.tmF + (xs.tx - xs.tlmX) * xs.tmBr / (Math.Abs(xs.tmA) < 0.001 ? 1.0 : xs.tmA);
        if (GridDebug)
            Console.Error.WriteLine($"[roty] devY={_currentLineDevY:F1} tmF={xs.tmF:F1} tx={xs.tx:F1} tlmX={xs.tlmX:F1} tmBr={xs.tmBr:F2} tmA={xs.tmA:F2} tmE={xs.tmE:F1} op={op}");
    }
    // A page positioned by Td alone (no Tm) never seeds the line Y —
    // without it RecordLineY skips every line and the Y-sort/merge
    // pass gets nothing to work with. Seed from the tracked tmY.
    if (double.IsNaN(_currentLineY))
    {
        _currentLineY = xs.tmY;
        _currentLineCmTy = xs.tmRotated ? 0 : LineCmAdjust(xs.depth, xs.localCmD, xs.localCmTy, _currentLineY);
    }
    if (xs.operands.Count >= 1 && xs.operands[0] is PdfString tjStr)
    {
        // Styled single glyph: one-char /ActualText over a one-glyph
        // show falls back to the font's own decode (see the flag note).
        if (xs.actualText is not null && !xs.actualTextUsed && xs.actualTextSingleChar
            && ActualTextYieldsToDecode(xs, tjStr))
            xs.actualText = null;
        if (xs.actualText is not null)
        {
            if (!xs.actualTextUsed)
            {
                AppendShowText(xs.actualText);
                xs.actualTextUsed = true;
                // The replaced glyphs' advance differs from the ActualText's,
                // so the gap chain restarts at the next regular run.
                xs.lastRunEndX = double.NaN; xs.lastRunEndDevX = double.NaN; xs.lastRunEndPageX = double.NaN;
            }
            // The span's glyphs still advance the pen even though their
            // decode is replaced — with a stale tx the NEXT run's Td
            // reads as a huge phantom word gap ("Is," grew a space).
            var atAdvW = ((xs.currentMetrics?.MeasureString(tjStr.Value, xs.fontSize)
                   ?? xs.fontSize * 0.5 * tjStr.Value.Length) + SpacingAdvance(xs, tjStr.Value)) * xs.horizScale;
            if (Type3SpanActive(xs))
            {
                var t3Adv = Type3Advance(tjStr.Value, xs.currentFontDict!, xs.reader, xs.fontSize);
                if (t3Adv >= 0) atAdvW = t3Adv * xs.horizScale;
                CollectType3SpanRun(xs, 
                    DecodeString(tjStr.Value, xs.currentToUnicode, xs.currentFontDict, xs.reader, xs.useFontEngine).Length,
                    xs.tmOriginX + (xs.tx - xs.tmOriginX) * xs.tmA + xs.localCmTx, xs.tmY + xs.localCmTy,
                    xs.fontSize * Math.Abs(xs.tmA), atAdvW * Math.Abs(xs.tmA));
            }
            xs.tx += atAdvW;
        }
        else
        {
            var fullDecoded = ApplyRtlIfPureRtl(NormalizeDecoded(DecodeString(tjStr.Value, xs.currentToUnicode, xs.currentFontDict, xs.reader, xs.useFontEngine), foldNbsp: xs.searchRect is null));
            if (xs.currentFontNonAgl)
                RecordAglError(xs.currentFontName, fullDecoded,
                    xs.tmE + (xs.tx - xs.tlmX) * xs.tmAr, xs.tmY + xs.localCmTy);
            // When a search rectangle is active, clip the run to the
            // glyphs whose advance box falls inside it (page space).
            // Sideways text clips along its advance axis (page Y).
            var clipRot = xs.clipRect is not null && xs.tmRotated && xs.currentMetrics is not null;
            var clipping = clipRot || (xs.clipRect is not null && xs.tmD > 0 && xs.currentMetrics is not null);
            var decoded = fullDecoded;
            // A left-clipped run starts, for layout purposes, at its first
            // surviving glyph — the off-page prefix neither indents the line
            // nor widens the gap to the previous run.
            var txClip = xs.tx;
            if (clipping)
            {
                var clip = new StringBuilder();
                var pen = xs.tx;
                if (clipRot)
                    AppendClippedRunRot(xs, clip, tjStr.Value, ref pen);
                else
                {
                    AppendClippedRun(clip, tjStr.Value, xs.currentToUnicode, xs.currentFontDict,
                        xs.reader, xs.useFontEngine, xs.currentMetrics, xs.fontSize, xs.horizScale,
                        xs.clipRect!, xs.tmOriginX, xs.tmA, xs.localCmTx, xs.cmLa, ref pen, xs.charSpacing, xs.wordSpacing,
                        out var keptStart, xs.blankClip,
                        dropLeadingSpaces: xs.searchRect is not null && !xs.blankClip
                            && (_text.Length == 0 || _text[^1] == '\n'));
                    if (!double.IsNaN(keptStart)) txClip = keptStart;
                }
                decoded = clip.ToString();
            }
            var measuredWidth = xs.currentMetrics?.MeasureString(tjStr.Value, xs.fontSize);
            var width = ((measuredWidth ?? (xs.fontSize * 0.5 * fullDecoded.Length))
                + SpacingAdvance(xs, tjStr.Value)) * xs.horizScale;
            if (!clipping || decoded.Length > 0)
            {
                // In Pure mode, capture the current run's page-space X and keep the
                // per-line grid origin up to date before computing spacing.
                double runPageX = 0;
                if (_pageCellWidth > 0)
                {
                    // Upright: composed device X (identical to the raw
                    // expression under an identity CTM/Tm, correct under
                    // scaled ones). Rotated keeps its projection frame on a
                    // rotated-dominant page; a minority rotated run on an
                    // upright page grids at its DEVICE x — the horizontal
                    // position of its vertical baseline.
                    runPageX = xs.tmRotated
                        ? (_pageRotDominant
                            ? xs.tmOriginX + (xs.tx - xs.tmOriginX) * xs.tmA + xs.localCmTx
                            : xs.tmE)
                        : xs.tmE + (txClip - xs.tlmX) * xs.tmAr;
                    TrackLineStart(runPageX, string.IsNullOrWhiteSpace(decoded));
                }
                TrackRowX(xs.tmRotated
                    ? (_pageRotDominant ? xs.tmOriginX + (xs.tx - xs.tmOriginX) * xs.tmA + xs.localCmTx : xs.tmE)
                    : (xs.tmOriginX + (txClip - xs.tmOriginX) * xs.tmA) * xs.cmLa + xs.cmLe);
                // Insert space for significant inter-word gap.
                // With proper text line matrix tracking, gap = tx - lastRunEndX
                // represents the actual visual gap between text runs (in user space).
                // A word space is typically ~fontSize * 0.25; we use a lower threshold
                // to catch narrow word spaces while avoiding false positives.
                // A trailing source space suppresses WORD-gap insertion
                // (no double spaces), but a genuine COLUMN jump still pads
                // to its grid column - the emitted chars (that space
                // included) already count toward the output column.
                var runDevX = xs.tmRotated ? xs.tmOriginX + (xs.tx - xs.tmOriginX) * xs.tmA + xs.localCmTx : 0;
                var useDev = xs.tmRotated && !double.IsNaN(xs.lastRunEndDevX);
                var usePage = !xs.tmRotated && !double.IsNaN(xs.lastRunEndPageX);
                var runStartPageX = xs.tmE + (txClip - xs.tlmX) * xs.tmAr;
                var gapPre = double.IsNaN(xs.lastRunEndX) ? 0
                    : useDev ? runDevX - xs.lastRunEndDevX
                    : usePage ? runStartPageX - xs.lastRunEndPageX
                    : (txClip - xs.lastRunEndX) * (xs.tmRotated ? xs.tmA : xs.tmAr);
                // Duplicate-stack dedup: when this run re-draws the previous
                // run's text over its box, it inherits the victim's slot —
                // no gap spaces of its own (they were measured against the
                // victim's end, which the truncation just removed).
                var dedupReplaced = !xs.tmRotated && xs.searchRect is null && !xs.rawInlineScripts
                    && decoded.Trim().Length > 0
                    && ReplaceOccludedPrevRun(xs, decoded, runStartPageX, width * Math.Abs(xs.tmAr), xs.tmY);
                var synthesizedHoleSpace = false;
                if (!dedupReplaced
                    && !double.IsNaN(xs.lastRunEndX)
                    && _text.Length > 0 && _text[^1] != '\n'
                    && (_text[^1] != ' '
                        || _prevShowHadTab
                        || (_pageCellWidth > 0 && gapPre > _pageCellWidth)))
                {
                    var gap = useDev ? runDevX - xs.lastRunEndDevX
                        : usePage ? runStartPageX - xs.lastRunEndPageX
                        : txClip - xs.lastRunEndX;
                    // See the TJ note: upright keeps the page-space Tm scale,
                    // rotated runs use the projected line size.
                    var gapFs = usePage ? xs.fontSize * Math.Abs(xs.tmAr)
                        : _currentLineEffFs > 0 && !double.IsNaN(_currentLineEffFs)
                        ? _currentLineEffFs
                        : xs.tmRotated ? Math.Abs(xs.fontSize * xs.tmN)
                        : xs.fontSize;
                    // Use a threshold based on font size. Lower threshold for runs
                    // with font metrics since tlmX tracking gives accurate gaps.
                    // Cumulative font metric imprecision over long runs can narrow
                    // the apparent gap, so use 0.09 * fontSize to catch narrow spaces
                    // (6pt fine print squeezes a word space down to ~0.098 em).
                    var threshold = (xs.lastHadMetrics || xs.currentMetrics != null)
                        ? gapFs * 0.09
                        : gapFs * 0.4;
                    // A run pads to its own start column; leading drawn
                    // space glyphs then land at their columns like any
                    // character (nothing is discounted
                    // for them — pad + drawn spaces total the gap).
                    var spaces = _pageCellWidth > 0
                        ? ColumnSpaces(gap, threshold, runPageX)
                        : ComputeSpaceCount(gap, threshold, usePage ? gapFs : xs.fontSize);
                    // Sub-cell gaps keep their grid pad: the synthesized gap
                    // space lands at ITS OWN grid column (padding
                    // the cursor up to it) and the following word writes
                    // contiguously after it — so target − output is the pad
                    // even when the visual gap is narrower than one cell.
                    var devGap = useDev || usePage || xs.tmRotated ? gap : gap * xs.tmAr;
                    if (GridDebug)
                        Console.Error.WriteLine($"[gap] gap={gap:F2} thr={threshold:F2} spaces={spaces} devGap={devGap:F2} cell={_pageCellWidth:F2} rot={xs.tmRotated} tmA={xs.tmA:F3} runPageX={runPageX:F1} lineStartX={_lineStartPageX:F1} fs={xs.fontSize:F2} tx={xs.tx:F2} lastEnd={xs.lastRunEndX:F2} metrics={(xs.lastHadMetrics || xs.currentMetrics != null)} txt='{(decoded.Length > 24 ? decoded.Substring(0, 24) : decoded)}'");
                    if (spaces > 0) _sawIntraLineGapSpaces = true;
                    for (int si = 0; si < spaces; si++) _text.Append(' ');
                    synthesizedHoleSpace = spaces > 0;
                }
                // Avoid double spaces: if a space was just emitted and the decoded text
                // starts with a space, skip the leading space — UNLESS the space was
                // just synthesized for THIS boundary's inter-run hole in the
                // layout-aware (Pure) mode (the hole and a drawn space
                // glyph count separately there; Raw/MemorySaving keep the
                // single-space collapse), and NOT on RTL lines: the document's
                // own space glyphs are kept there in ADDITION to the
                // synthesized gap space ("כתובת:    שפרעם" carries three glyphs +
                // one synthesized), and the RTL row rebuild needs the full count.
                if ((!synthesizedHoleSpace
                        || ExtractionOptions?.FormattingMode
                            is TextExtractionOptions.TextFormattingMode.Raw
                            or TextExtractionOptions.TextFormattingMode.MemorySaving)
                    && _text.Length > 0 && _text[^1] == ' ' && decoded.Length > 0 && decoded[0] == ' '
                    && !RecentTextIsRtl())
                    decoded = decoded.Substring(1);
                if (decoded.Length > 0)
                {
                    var spanScale = xs.horizScale * Math.Abs(xs.tmRotated ? xs.tmA : xs.tmAr);
                    _pageRunSpans.Add(new RunSpan(_text.Length, decoded.Length,
                        xs.tmRotated ? (_pageRotDominant ? xs.tmOriginX + (xs.tx - xs.tmOriginX) * xs.tmA + xs.localCmTx : xs.tmE)
                                  : xs.tmE + (txClip - xs.tlmX) * xs.tmAr,
                        (xs.currentMetrics?.MeasureString(tjStr.Value, xs.fontSize)
                         ?? (xs.fontSize * 0.5 * fullDecoded.Length)) * spanScale,
                        !clipping && IsPureRtlRun(decoded),
                        clipping ? null : BuildCharXs(tjStr.Value, xs.currentMetrics, xs.fontSize,
                            spanScale, decoded.Length, xs.charSpacing, xs.wordSpacing)));
                }
                if (!xs.tmRotated && xs.searchRect is null && !xs.rawInlineScripts
                    && decoded.Trim().Length > 0)
                    xs.dedupPrevOffset = _text.Length;
                AppendShowText(decoded);
            }
            // Capture invisible (Tr 3) runs (with their rendered advance) for
            // hOCR-overlay reconstruction.
            if (_collectOcrRuns && xs.textRenderMode == 3 && fullDecoded.Length > 0)
                _ocrRuns.Add((fullDecoded,
                    xs.tmOriginX + (xs.tx - xs.tmOriginX) * xs.tmA + xs.localCmTx, xs.tmY, xs.fontSize, width));
            xs.lastRunEndDevX = xs.tmRotated ? xs.tmOriginX + (xs.tx - xs.tmOriginX) * xs.tmA + xs.localCmTx + width * xs.tmA : double.NaN;
            xs.lastRunEndPageX = xs.tmRotated ? double.NaN : xs.tmE + (xs.tx + width - xs.tlmX) * xs.tmAr;
            xs.lastRunStartPageX = xs.tmRotated ? double.NaN : xs.tmE + (xs.tx - xs.tlmX) * xs.tmAr;
            xs.lastRunEndX = xs.tx + width * (xs.tmRotated ? xs.tmA : 1.0); // rotated: advance projects through the axis norm
            xs.lastRunEstWidth = width;
            xs.lastHadMetrics = measuredWidth.HasValue;
            xs.lastDecodedLength = decoded.Length;
            xs.tx += width;
            // Track rendered Y so subsequent '/"/'Tm' can distinguish
            // same-row column repositioning from real line advances.
            xs.lastRenderedY = xs.tmY; xs.lastRenderedFs = xs.fontSize * (xs.tmRotated ? xs.tmN : 1.0); xs.lastRenderedCmTy = xs.localCmTy;
        }
    }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private void ShowTextArrayOp(ExtractState xs, string op)
    {
    _textShowingOpCount++;
    EnsureFontSet(xs.fontSet, op);
    if (xs.skipText) return;
    _pageHasRotatedText |= xs.tmRotated;
    _currentLineEffFs = xs.tmRotated
        ? Math.Abs(xs.fontSize * xs.tmN)  // composed projection norm already carries the CTM; the scalar d is ~0 sideways
        : Math.Abs(xs.fontSize * (xs.tmD > 0 ? xs.tmD : xs.tmN) * xs.localCmD);
    _currentLineDescent = xs.currentMetrics is not null && xs.currentMetrics.Descent < 0
        ? -xs.currentMetrics.Descent / 1000.0
        : 0.2;
    _currentLineIsRotated = xs.tmRotated && !_pageRotDominant
        && (_text.Length == 0 || _text[^1] == '\n' || _currentLineIsRotated);
    if (_currentLineIsRotated && double.IsNaN(_currentLineDevY))
    {
        _currentLineDevY = xs.tmF + (xs.tx - xs.tlmX) * xs.tmBr / (Math.Abs(xs.tmA) < 0.001 ? 1.0 : xs.tmA);
        if (GridDebug)
            Console.Error.WriteLine($"[roty] devY={_currentLineDevY:F1} tmF={xs.tmF:F1} tx={xs.tx:F1} tlmX={xs.tlmX:F1} tmBr={xs.tmBr:F2} tmA={xs.tmA:F2} tmE={xs.tmE:F1} op={op}");
    }
    // See the Tj note: seed the line Y for Td-only pages.
    if (double.IsNaN(_currentLineY))
    {
        _currentLineY = xs.tmY;
        _currentLineCmTy = xs.tmRotated ? 0 : LineCmAdjust(xs.depth, xs.localCmD, xs.localCmTy, _currentLineY);
    }
    if (xs.operands.Count >= 1 && xs.operands[0] is PdfArray tjArr)
    {
        // Styled single glyph: one-char /ActualText over a one-glyph
        // show falls back to the font's own decode (see the flag note).
        if (xs.actualText is not null && !xs.actualTextUsed && xs.actualTextSingleChar
            && ActualTextYieldsToDecode(xs, tjArr))
            xs.actualText = null;
        if (xs.actualText is not null)
        {
            if (!xs.actualTextUsed)
            {
                AppendShowText(xs.actualText);
                xs.actualTextUsed = true;
                xs.lastRunEndX = double.NaN; xs.lastRunEndDevX = double.NaN; xs.lastRunEndPageX = double.NaN;
            }
            var atStartTx = xs.tx;
            var atRawLen = 0;
            // Advance the pen over the replaced glyphs (see the Tj note).
            foreach (var atItem in tjArr)
            {
                if (atItem is PdfString atS)
                {
                    var atItemAdv = (xs.currentMetrics?.MeasureString(atS.Value, xs.fontSize)
                           ?? xs.fontSize * 0.5 * atS.Value.Length) * xs.horizScale;
                    if (Type3SpanActive(xs))
                    {
                        var t3 = Type3Advance(atS.Value, xs.currentFontDict!, xs.reader, xs.fontSize);
                        if (t3 >= 0) atItemAdv = t3 * xs.horizScale;
                        atRawLen += DecodeString(atS.Value, xs.currentToUnicode, xs.currentFontDict, xs.reader, xs.useFontEngine).Length;
                    }
                    xs.tx += atItemAdv;
                }
                else
                    xs.tx += -GetNumber(atItem) * xs.fontSize / 1000.0;
            }
            if (Type3SpanActive(xs))
                CollectType3SpanRun(xs, atRawLen,
                    xs.tmOriginX + (atStartTx - xs.tmOriginX) * xs.tmA + xs.localCmTx, xs.tmY + xs.localCmTy,
                    xs.fontSize * Math.Abs(xs.tmA), (xs.tx - atStartTx) * Math.Abs(xs.tmA));
        }
        else
        {
            double tjWidth = 0;
            int tjDecodedLen = 0;
            // Buffer the TJ text so we can apply per-operator RTL reversal
            // after collecting all sub-strings (mirrors TypeScript applyRtl on TJ).
            var tjBuf = new StringBuilder();
            // When a search rectangle is active, clip each glyph to it in
            // page space; the pen advances over the whole array (strings and
            // numeric adjustments) regardless of visibility.
            // Sideways text clips along its advance axis (page Y).
            var clipRot = xs.clipRect is not null && xs.tmRotated && xs.currentMetrics is not null;
            var clipping = clipRot || (xs.clipRect is not null && xs.tmD > 0 && xs.currentMetrics is not null);
            var clipBuf = clipping ? new StringBuilder() : null;
            var clipPen = xs.tx;
            var hadString = false;
            // Track this run for the Pure-mode grid (line-start X for
            // leading columns) — the TJ path must mirror the Tj path or
            // TJ-drawn documents get no grid anchoring at all.
            double tjRunPageX = 0;
            if (_pageCellWidth > 0)
            {
                // See the Tj-path note: device X for upright text;
                // minority rotated runs grid at their device X too.
                tjRunPageX = xs.tmRotated
                    ? (_pageRotDominant
                        ? xs.tmOriginX + (xs.tx - xs.tmOriginX) * xs.tmA + xs.localCmTx
                        : xs.tmE)
                    : xs.tmE + (xs.tx - xs.tlmX) * xs.tmAr;
                // Whitespace-only detection from raw bytes (simple fonts:
                // every code 0x20; composite codes stay "visible").
                var tjAllSpaces = false;
                if (xs.currentMetrics is not null && !xs.currentMetrics.IsCid)
                {
                    tjAllSpaces = true;
                    foreach (var pre0 in tjArr)
                    {
                        if (pre0 is not PdfString ps0) continue;
                        foreach (var b0 in ps0.Value)
                            if (b0 != 0x20) { tjAllSpaces = false; break; }
                        if (!tjAllSpaces) break;
                    }
                }
                TrackLineStart(tjRunPageX, tjAllSpaces);
            }
            TrackRowX(xs.tmRotated
                ? (_pageRotDominant ? xs.tmOriginX + (xs.tx - xs.tmOriginX) * xs.tmA + xs.localCmTx : xs.tmE)
                : (xs.tmOriginX + (xs.tx - xs.tmOriginX) * xs.tmA) * xs.cmLa + xs.cmLe);
            // The inter-word space before the run depends only on pre-run state.
            var leadingSpaces = 0;
            var tjRunDevX = xs.tmRotated ? xs.tmOriginX + (xs.tx - xs.tmOriginX) * xs.tmA + xs.localCmTx : 0;
            var tjUseDev = xs.tmRotated && !double.IsNaN(xs.lastRunEndDevX);
            var tjUsePage = !xs.tmRotated && !double.IsNaN(xs.lastRunEndPageX);
            var tjStartPageX = xs.tmE + (xs.tx - xs.tlmX) * xs.tmAr;
            var tjGapPre = double.IsNaN(xs.lastRunEndX) ? 0
                : tjUseDev ? tjRunDevX - xs.lastRunEndDevX
                : tjUsePage ? tjStartPageX - xs.lastRunEndPageX
                : (xs.tx - xs.lastRunEndX) * (xs.tmRotated ? xs.tmA : xs.tmAr);
            if (!double.IsNaN(xs.lastRunEndX)
                && _text.Length > 0 && _text[^1] != '\n'
                && (_text[^1] != ' '
                    || _prevShowHadTab
                    || (_pageCellWidth > 0 && tjGapPre > _pageCellWidth)))
            {
                var tjGap = tjUseDev ? tjRunDevX - xs.lastRunEndDevX
                    : tjUsePage ? tjStartPageX - xs.lastRunEndPageX
                    : xs.tx - xs.lastRunEndX;
                // Effective font size for the gap threshold. Upright pages
                // keep the page-space Tm scale (the calibrated rule); rotated
                // runs use the projected line size — their raw fontSize can be
                // Tm-scaled (fs 327 with tmA 0.027 is 8.85 pt on the page) and
                // an unprojected threshold swallows every real word gap.
                var tjGapFs = tjUsePage ? xs.fontSize * Math.Abs(xs.tmAr)
                    : _currentLineEffFs > 0 && !double.IsNaN(_currentLineEffFs)
                    ? _currentLineEffFs
                    : xs.tmRotated ? Math.Abs(xs.fontSize * xs.tmN)
                    : xs.fontSize;
                // A BACKWARD pen jump bigger than a grid cell means the
                // stream draws this row's columns out of X order: start a
                // new logical line and let the row merge re-order by column.
                // NOT for RTL text - Hebrew/Arabic legitimately pens
                // right-to-left and its runs assemble via the RTL row path.
                var recentRtl = false;
                for (var ri2 = _text.Length - 1; ri2 >= 0 && ri2 >= _text.Length - 8; ri2--)
                    if (BidiReorderer.IsRtlChar(_text[ri2])) { recentRtl = true; break; }
                // An overlapping backjump — the pen lands within one cell of
                // the PREVIOUS run's own start, i.e. the stream re-draws over
                // the same spot (shadow/duplicate stack) — stays inline so the
                // later-ink dedup can collapse it; only a jump to an earlier
                // column (left of the previous run's start) breaks the line.
                var tjOverlapJump = !xs.tmRotated && !double.IsNaN(xs.lastRunStartPageX)
                    && tjStartPageX >= xs.lastRunStartPageX - _pageCellWidth;
                if (_pageCellWidth > 0 && tjGap < -_pageCellWidth && !recentRtl
                    && !tjOverlapJump
                    && _text.Length > 0 && _text[^1] != '\n')
                {
                    RecordLineY();
                    AppendStreamBreak();
                    xs.lastRunEndX = double.NaN; xs.lastRunEndDevX = double.NaN; xs.lastRunEndPageX = double.NaN;
                }
                else
                {
                if (GridDebug)
                    Console.Error.WriteLine($"[tjgap] tx={xs.tx:F1} lastEnd={xs.lastRunEndX:F1} gap={tjGap:F1} rot={xs.tmRotated} runPageX={tjRunPageX:F1} gapFs={tjGapFs:F2} effFs={_currentLineEffFs:F2} useDev={tjUseDev}");
                // Leading drawn spaces of the array's first piece fill
                // their own columns and count toward the grid target
                // (see the Tj-path note).
                // See the Tj-path note: a run pads to its own start
                // column; leading drawn spaces land at their columns.
                leadingSpaces = _pageCellWidth > 0
                    ? ColumnSpaces(tjGap, tjGapFs * 0.15, tjRunPageX)
                    : ComputeSpaceCount(tjGap, tjGapFs * 0.15, tjGapFs);
                // Sub-cell gaps keep their grid pad (see the Tj-path note:
                // the gap space grid-places like any word start).
                }
            }
            // Pen start offsets (text-space, one per tjBuf char) for the run
            // span's per-character X map; invalidated when the code↔char
            // mapping is not 1:1 for some sub-string.
            var tjRel = new List<double>();
            var tjRelValid = !clipping;
            // Synthetic-space eligibility (validated over a
            // 1231-run corpus; same rule as the fragment
            // absorber): one space per adjustment ≤ −130/1000 em iff the
            // array is "armed" — any ≥2-glyph piece, or any glyph that is
            // NOT an uppercase letter or punctuation (font type is
            // irrelevant; tracked caps-only display text collapses) — and
            // is not the letter-tracking shape (>10 pieces, ALL
            // single-glyph → collapse; word-piece prose arrays keep their
            // kern-encoded word gaps).
            var tjIsType0 = xs.currentFontDict?.GetName("Subtype") == "Type0";
            var tjPieceCount = 0;
            var tjMultiGlyph = false;
            var tjAdjs = new List<double>();
            foreach (var pre in tjArr)
                if (pre is PdfString preS0)
                {
                    tjPieceCount++;
                    if (preS0.Value.Length >= (tjIsType0 ? 4 : 2)) tjMultiGlyph = true;
                }
                else
                    tjAdjs.Add(GetNumber(pre));
            var tjSynthArmed = tjMultiGlyph;
            if (!tjSynthArmed)
                foreach (var pre in tjArr)
                {
                    if (pre is not PdfString preS) continue;
                    var preDec = NormalizeDecoded(DecodeString(preS.Value, xs.currentToUnicode, xs.currentFontDict, xs.reader, xs.useFontEngine));
                    if (preDec.Length >= 2) { tjSynthArmed = true; tjMultiGlyph = true; break; }
                    var preArm = false;
                    foreach (var preC in preDec)
                        if (!char.IsUpper(preC) && !char.IsPunctuation(preC))
                        { preArm = true; break; }
                    if (preArm) { tjSynthArmed = true; break; }
                }
            tjSynthArmed = tjSynthArmed && tjPieceCount >= 2
                && (tjPieceCount <= 10 || tjMultiGlyph);
            // Letter-tracked single-glyph arrays (the disarmed shape) can still
            // encode WORD gaps — as kern OUTLIERS against the array's uniform
            // tracking baseline, not as absolute-threshold kerns: a newspaper
            // headline tracks letters at +20..+58 and words at −135..−169
            // (never reaching the classic −190). Break where the adjustment
            // falls ≥130/1000 em BELOW the array's median; a uniformly tracked
            // display run (every kern ≈ the median) still collapses.
            var tjMedian = double.NaN;
            if (tjPieceCount >= 5 && tjAdjs.Count >= 4)
            {
                tjAdjs.Sort();
                tjMedian = tjAdjs[tjAdjs.Count / 2];
            }
            var tjLtrackMedian = !tjSynthArmed && !tjMultiGlyph ? tjMedian : double.NaN;
            // Per-glyph POSITIONING arrays: in an all-single-glyph array
            // where word-depth kerns are the NORM rather than the exception
            // (half or more of the adjustments reach −130), the kerns place
            // glyphs, they don't separate words — synthesizing a space at
            // each would shred the run into single-char confetti. "Page:1/1"
            // (4 of 7 kerns at −264…−284) collapses even though lowercase
            // letters arm it; "Date : 26/05/2022 03:53:42 PM" (3 word kerns
            // among 24 small tracking values) keeps its word gaps.
            var tjPositioningArray = false;
            var tjDeepMedian = double.NaN;
            if (!tjMultiGlyph && tjAdjs.Count >= 3)
            {
                var deepList = new List<double>();
                foreach (var a2 in tjAdjs)
                    if (a2 <= -130) deepList.Add(a2);
                if (deepList.Count * 2 >= tjAdjs.Count && deepList.Count > 0)
                {
                    tjPositioningArray = true;
                    deepList.Sort();
                    // The placement baseline is the word-depth cluster's own
                    // median; only a kern well below IT separates words.
                    tjDeepMedian = deepList[deepList.Count / 2];
                }
            }
            StringBuilder? tjDbg = GridDebug ? new StringBuilder() : null;
            foreach (var item in tjArr)
            {
                if (item is PdfString tjS)
                {
                    hadString = true;
                    var tjDecoded = NormalizeDecoded(DecodeString(tjS.Value, xs.currentToUnicode, xs.currentFontDict, xs.reader, xs.useFontEngine), foldNbsp: xs.searchRect is null);
                    tjDbg?.Append('\'').Append(tjDecoded).Append('\'');
                    tjBuf.Append(tjDecoded);
                    var tjItemW = ((xs.currentMetrics?.MeasureString(tjS.Value, xs.fontSize)
                               ?? (xs.fontSize * 0.5 * tjS.Value.Length)) + SpacingAdvance(xs, tjS.Value)) * xs.horizScale;
                    if (tjRelValid)
                    {
                        var itemRel = BuildCharXs(tjS.Value, xs.currentMetrics, xs.fontSize,
                            xs.horizScale, tjDecoded.Length, xs.charSpacing, xs.wordSpacing);
                        if (itemRel is not null)
                            foreach (var r in itemRel) tjRel.Add(tjWidth + r);
                        else if (tjDecoded.Length > 0)
                        {
                            // Uniform fallback for this sub-string only.
                            var step = tjItemW / tjDecoded.Length;
                            for (var ri = 0; ri < tjDecoded.Length; ri++)
                                tjRel.Add(tjWidth + step * ri);
                        }
                    }
                    tjWidth += tjItemW;
                    tjDecodedLen += tjDecoded.Length;
                    if (clipRot)
                        AppendClippedRunRot(xs, clipBuf!, tjS.Value, ref clipPen);
                    else if (clipping)
                        AppendClippedRun(clipBuf!, tjS.Value, xs.currentToUnicode, xs.currentFontDict,
                            xs.reader, xs.useFontEngine, xs.currentMetrics, xs.fontSize, xs.horizScale,
                            xs.clipRect!, xs.tmOriginX, xs.tmA, xs.localCmTx, xs.cmLa, ref clipPen, xs.charSpacing, xs.wordSpacing, out _, xs.blankClip,
                            dropLeadingSpaces: xs.searchRect is not null && !xs.blankClip
                                && clipBuf!.Length == 0 && (_text.Length == 0 || _text[^1] == '\n'));
                }
                else
                {
                    var adj = GetNumber(item);
                    tjDbg?.Append('(').Append(adj.ToString("F0")).Append(')');
                    var advance = -adj * xs.fontSize / 1000.0;
                    tjWidth += advance;
                    var kernGapStart = tjWidth - advance;
                    // Any kern beyond the classic −190 word-break threshold
                    // separates (incl. Pure-grid column jumps in letter-tracked
                    // single-glyph arrays, e.g. an 11-piece row with a −9711
                    // column hop); the −130 rule EXTENDS the reach for
                    // armed shapes only.
                    var tjKernBreaks = tjPositioningArray
                        ? adj - tjDeepMedian <= -130
                        : adj < -190 || (tjSynthArmed && adj <= -130)
                          || (!double.IsNaN(tjLtrackMedian) && adj - tjLtrackMedian <= -130
                              && (tjLtrackMedian >= 0 || adj <= -250));
                    var kernAfterSpace = tjBuf.Length > 0 && tjBuf[^1] == ' ';
                    // Grid-pad only genuine column jumps (≥ ~1 em). A word-space
                    // kern (0.2–0.6 em) stays a single space — proportional prose
                    // output columns drift from ink columns, and padding to the
                    // grid there sprays spaces mid-sentence.
                    var gridKernPad = _pageCellWidth > 0 && !clipping && advance > xs.fontSize;
                    // A drawn space before the kern suppresses the single word
                    // space, but a GRID column jump still pads to its target
                    // column — the drawn space merely counts as one emitted
                    // char toward it ("( )-1129.6(NAME)" lands NAME at the
                    // same column as "(*)-1129.6(NAME)").
                    if (tjKernBreaks && (!kernAfterSpace || gridKernPad))
                    {
                        // Under the Pure grid a large intra-TJ kern is a column
                        // gap like any other: pad to the grid column of the pen
                        // position after the kern, not a single word space.
                        var pad = kernAfterSpace ? 0 : 1;
                        if (gridKernPad)
                        {
                            // Same absolute floor grid as ColumnSpaces — the kern pen
                            // sits at the target glyph's left edge; floor quantisation
                            // assigns boundary glyphs to the lower column by itself.
                            var penPageX = xs.tmRotated
                                               ? xs.tmOriginX + (xs.tx + tjWidth - xs.tmOriginX) * xs.tmA + xs.localCmTx
                                               : xs.tmE + (xs.tx + tjWidth - xs.tlmX) * xs.tmAr;
                            pad = ColumnSpaces(advance, 0, penPageX, leadingSpaces + tjBuf.Length,
                                kernAfterSpace ? 0 : 1);
                            if (GridDebug)
                                Console.Error.WriteLine($"[tjkern] pen={penPageX:F2} col={(penPageX - _pageMinX) / _pageCellWidth:F3} pad={pad} buf='{tjBuf}'");
                        }
                        for (var k = 0; k < pad; k++)
                        {
                            tjBuf.Append(' ');
                            if (tjRelValid) tjRel.Add(kernGapStart);
                        }
                    }
                    if (clipping)
                    {
                        // The synthesized word space sits at the current pen; emit it
                        // only when that point is inside the rectangle (advance-axis
                        // position: page X upright, page Y sideways).
                        if (tjPositioningArray
                            ? adj - tjDeepMedian <= -130
                            : adj < -190 || (tjSynthArmed && adj <= -130)
                              || (!double.IsNaN(tjLtrackMedian) && adj - tjLtrackMedian <= -130
                                  && (tjLtrackMedian >= 0 || adj <= -250)))
                        {
                            // Under LimitToPageBounds with no caller rectangle, searchRect
                            // is null while clipping is driven by the page-bounds clipRect;
                            // fall back to it so the in-window test doesn't dereference null.
                            var win = xs.searchRect ?? xs.clipRect!;
                            var inWindow = clipRot
                                ? xs.tmF + (clipPen - xs.tlmX) * xs.tmBr is var pY
                                    && pY >= win.LLY && pY <= win.URY
                                : xs.tmOriginX + (clipPen - xs.tmOriginX) * xs.tmA + xs.localCmTx is var pX
                                    && pX >= win.LLX && pX <= win.URX;
                            if (inWindow && (clipBuf!.Length == 0 || clipBuf[^1] != ' '))
                                clipBuf!.Append(' ');
                        }
                        clipPen += advance;
                    }
                }
            }
            if (xs.currentFontNonAgl && hadString)
                RecordAglError(xs.currentFontName, tjBuf.ToString(),
                    xs.tmE + (xs.tx - xs.tlmX) * xs.tmAr, xs.tmY + xs.localCmTy);
            // Apply per-operator RTL reversal: if all decoded TJ chars are RTL/neutral,
            // reverse to convert visual order to logical order (Hebrew, Arabic).
            var tjText = clipping ? clipBuf!.ToString() : ApplyRtlIfPureRtl(tjBuf.ToString());
            if (GridDebug)
            {
                Console.Error.WriteLine($"[tjrun] tx={xs.tx:F2} w={tjWidth:F2} fs={xs.fontSize:F2} tmA={xs.tmA:F3} armed={tjSynthArmed} pieces={tjPieceCount} lead={leadingSpaces} txt='{(tjText.Length > 32 ? tjText.Substring(0, 32) : tjText)}'");
                var dbgS = tjDbg!.ToString();
                Console.Error.WriteLine($"[tjarr] {(dbgS.Length > 300 ? dbgS.Substring(0, 300) : dbgS)}");
            }
            // Duplicate-stack dedup (see the Tj path): the occluder inherits
            // the victim's slot, so its own gap spaces are dropped too.
            if (!xs.tmRotated && xs.searchRect is null && !xs.rawInlineScripts
                && tjText.Trim().Length > 0
                && ReplaceOccludedPrevRun(xs, tjText, xs.tmE + (xs.tx - xs.tlmX) * xs.tmAr,
                    tjWidth * Math.Abs(xs.tmAr), xs.tmY))
                leadingSpaces = 0;
            if (clipping ? tjText.Length > 0 : hadString)
            {
                if (leadingSpaces > 0) _sawIntraLineGapSpaces = true;
                for (int si = 0; si < leadingSpaces; si++) _text.Append(' ');
            }
            // Avoid double spaces between previous run and this TJ block —
            // UNLESS the space was just synthesized for THIS boundary's
            // inter-run hole in the layout-aware (Pure) mode: a glyph-sized
            // hole and a drawn space glyph count separately
            // there (a run whose ':' was redacted reads back
            // "Date  13" — synth(gap) + the real space; Raw/MemorySaving
            // keep the single-space collapse). Also NOT on RTL lines (see
            // the Tj-path note): the document's own
            // space glyphs are kept beside the synthesized one.
            if ((leadingSpaces == 0
                    || ExtractionOptions?.FormattingMode
                        is TextExtractionOptions.TextFormattingMode.Raw
                        or TextExtractionOptions.TextFormattingMode.MemorySaving)
                && _text.Length > 0 && _text[^1] == ' ' && tjText.Length > 0 && tjText[0] == ' '
                && !RecentTextIsRtl())
            {
                tjText = tjText.Substring(1);
                if (tjRelValid && tjRel.Count > 0) tjRel.RemoveAt(0);
            }
            // A minority-rotated run flattens left-to-right in logical
            // glyph order on one row: each INTERNAL
            // drawn-space group emits
            // max(n+1, floor(|cumAdvance|/cell) − chars)
            // spaces — the gap target is quantised from the advance
            // RELATIVE to the run start (not a difference of absolute
            // grid floors), and at least one synthesized pad joins
            // every drawn gap.
            if (xs.tmRotated && !_pageRotDominant && _pageCellWidth > 0 && !clipping
                && tjRelValid && tjRel.Count == tjText.Length
                && tjText.IndexOf(' ') > 0)
            {
                var rsb = new StringBuilder(tjText.Length + 8);
                var ci2 = 0;
                while (ci2 < tjText.Length)
                {
                    if (tjText[ci2] != ' ') { rsb.Append(tjText[ci2]); ci2++; continue; }
                    var n2 = 0;
                    while (ci2 + n2 < tjText.Length && tjText[ci2 + n2] == ' ') n2++;
                    var after2 = ci2 + n2;
                    if (after2 >= tjText.Length) { rsb.Append(' ', n2); break; }
                    var target2 = (int)Math.Floor(Math.Abs(tjRel[after2] * xs.tmA) / _pageCellWidth);
                    rsb.Append(' ', Math.Min(200, Math.Max(n2 + 1, target2 - rsb.Length)));
                    ci2 = after2;
                }
                if (rsb.Length != tjText.Length)
                {
                    tjText = rsb.ToString();
                    tjRelValid = false; // char↔pen map no longer 1:1
                }
            }
            // UPRIGHT pure pages: an in-string space whose rendered advance
            // is a genuine column jump (> 1 em — e.g. a Tw-inflated table
            // gap, "( AIRCASTLE )" under Tw 1.1296) pads the following
            // glyph to its grid column, exactly as the intra-TJ kern rule
            // does; ordinary word spaces (0.2–0.6 em) stay single.
            if (!xs.tmRotated && _pageCellWidth > 0 && !clipping
                && tjRelValid && tjRel.Count == tjText.Length
                && tjText.IndexOf(' ') >= 0)
            {
                var usb = new StringBuilder(tjText.Length + 8);
                var ui = 0;
                var changed = false;
                while (ui < tjText.Length)
                {
                    if (tjText[ui] != ' ') { usb.Append(tjText[ui]); ui++; continue; }
                    var n3 = 0;
                    while (ui + n3 < tjText.Length && tjText[ui + n3] == ' ') n3++;
                    var after3 = ui + n3;
                    if (after3 >= tjText.Length) { usb.Append(' ', n3); break; }
                    var gapAdv = tjRel[after3] - tjRel[ui];
                    if (gapAdv > xs.fontSize * xs.horizScale)
                    {
                        var penAfter = xs.tmE + (xs.tx + tjRel[after3] - xs.tlmX) * xs.tmAr;
                        var pad3 = ColumnSpaces(gapAdv, 0, penAfter,
                            leadingSpaces + usb.Length, n3);
                        usb.Append(' ', Math.Min(200, pad3));
                        if (pad3 != n3) changed = true;
                    }
                    else usb.Append(' ', n3);
                    ui = after3;
                }
                if (changed)
                {
                    tjText = usb.ToString();
                    tjRelValid = false; // char↔pen map no longer 1:1
                }
            }
            bool tjIsLeadingPos = _text.Length == 0 || _text[^1] == '\n';
            bool tjAllSpace = tjText.Length > 0 && tjText.Trim().Length == 0;
            if (tjAllSpace && tjIsLeadingPos)
            {
                // A line-leading whitespace run is a grid citizen: emit it
                // (placeholder space glyphs form pure-pad lines in the
                // output). Remember its Y so a following RTL run
                // on the same line can still re-home the pad to its logical
                // end (the appended space also guards the re-home branch's
                // "text doesn't end with a space" check below).
                xs.pendingReorderSpaceY = xs.tmY;
                AppendShowText(tjText);
            }
            else
            {
                if (!double.IsNaN(xs.pendingReorderSpaceY))
                {
                    if (System.Math.Abs(xs.tmY - xs.pendingReorderSpaceY) > 1.0)
                        xs.pendingReorderSpaceY = double.NaN;              // different line: drop the orphan
                    else if (tjText.Length > 0 && Aspose.Pdf.Text.BidiReorderer.IsRtlChar(tjText[0])
                             && _text.Length > 0 && _text[^1] != ' ')
                    {
                        _text.Append(' ');                             // re-home before the first RTL run
                        xs.pendingReorderSpaceY = double.NaN;
                    }
                }
                if (tjText.Length > 0)
                {
                    var tjPageScale = Math.Abs(xs.tmRotated ? xs.tmA : xs.tmAr);
                    double[]? tjCharXs = null;
                    if (tjRelValid && tjRel.Count == tjText.Length)
                    {
                        tjCharXs = new double[tjRel.Count];
                        for (var ri = 0; ri < tjRel.Count; ri++)
                            tjCharXs[ri] = tjRel[ri] * tjPageScale;
                    }
                    _pageRunSpans.Add(new RunSpan(_text.Length, tjText.Length,
                        xs.tmRotated ? (_pageRotDominant ? xs.tmOriginX + (xs.tx - xs.tmOriginX) * xs.tmA + xs.localCmTx : xs.tmE)
                                  : xs.tmE + (xs.tx - xs.tlmX) * xs.tmAr,
                        tjWidth * tjPageScale,
                        !clipping && IsPureRtlRun(tjText),
                        tjCharXs));
                }
                if (!xs.tmRotated && xs.searchRect is null && !xs.rawInlineScripts
                    && tjText.Trim().Length > 0)
                    xs.dedupPrevOffset = _text.Length;
                AppendShowText(tjText);
            }
            xs.lastRunEndDevX = xs.tmRotated ? xs.tmOriginX + (xs.tx - xs.tmOriginX) * xs.tmA + xs.localCmTx + tjWidth * xs.tmA : double.NaN;
            xs.lastRunEndPageX = xs.tmRotated ? double.NaN : xs.tmE + (xs.tx + tjWidth - xs.tlmX) * xs.tmAr;
            xs.lastRunStartPageX = xs.tmRotated ? double.NaN : xs.tmE + (xs.tx - xs.tlmX) * xs.tmAr;
            xs.lastRunEndX = xs.tx + tjWidth * (xs.tmRotated ? xs.tmA : 1.0); // rotated: advance projects through the axis norm
            xs.lastRunEstWidth = tjWidth;
            xs.lastDecodedLength = tjDecodedLen;
            xs.tx += tjWidth;
            // Track rendered Y for subsequent line-break suppression logic
            xs.lastRenderedY = xs.tmY; xs.lastRenderedFs = xs.fontSize * (xs.tmRotated ? xs.tmN : 1.0); xs.lastRenderedCmTy = xs.localCmTy;
        }
    }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private void ShowTextSpacedNextLineOp(ExtractState xs, string op)
    {
    _textShowingOpCount++;
    EnsureFontSet(xs.fontSet, op);
    // PDF spec: ' is "move to next line and show string" — equivalent to T* then Tj.
    //          " is "set word/char spacing, move to next line, show string" —
    //          operands = aw, ac, string.
    // The operator advances the text line matrix by -leading in y.
    // Historically we unconditionally emitted \r\n, but when a preceding Tm
    // has just repositioned to a different column's Y (same visual row),
    // the post-' Y may still be on the SAME logical line. Compare with
    // lastRenderedY to decide.
    // Move text line matrix down by leading (pre-text position).
    // This happens even while the current line is filtered out —
    // ' is T* + Tj, and T* always advances the line matrix. Bailing
    // out before the advance froze tmY at the paragraph's first
    // line, so a paragraph starting above the search rectangle
    // never re-entered it and its in-window lines were dropped.
    xs.tmE += -xs.leading * xs.tmCr;
    xs.tmF += -xs.leading * xs.tmDr;
    var newY = xs.tmRotated
        ? RotatedRowY(xs.tmCr, xs.tmDr, xs.tmE, xs.tmF)
        : xs.tmY - xs.leading * (xs.tmD > 0 ? xs.tmD : xs.tmN);
    xs.tmY = newY;
    xs.tx = xs.tlmX;
    // Re-evaluate the line-level filters at the new baseline.
    xs.skipText = LineFiltered(xs, xs.tmY);
    if (xs.skipText) { return; }
    _pageHasRotatedText |= xs.tmRotated;
    _currentLineEffFs = xs.tmRotated
        ? Math.Abs(xs.fontSize * xs.tmN)  // composed projection norm already carries the CTM; the scalar d is ~0 sideways
        : Math.Abs(xs.fontSize * (xs.tmD > 0 ? xs.tmD : xs.tmN) * xs.localCmD);
    _currentLineDescent = xs.currentMetrics is not null && xs.currentMetrics.Descent < 0
        ? -xs.currentMetrics.Descent / 1000.0
        : 0.2;
    _currentLineIsRotated = xs.tmRotated && !_pageRotDominant
        && (_text.Length == 0 || _text[^1] == '\n' || _currentLineIsRotated);
    if (_currentLineIsRotated && double.IsNaN(_currentLineDevY))
    {
        _currentLineDevY = xs.tmF + (xs.tx - xs.tlmX) * xs.tmBr / (Math.Abs(xs.tmA) < 0.001 ? 1.0 : xs.tmA);
        if (GridDebug)
            Console.Error.WriteLine($"[roty] devY={_currentLineDevY:F1} tmF={xs.tmF:F1} tx={xs.tx:F1} tlmX={xs.tlmX:F1} tmBr={xs.tmBr:F2} tmA={xs.tmA:F2} tmE={xs.tmE:F1} op={op}");
    }
    PdfString? qStr = null;
    if (op == "'" && xs.operands.Count >= 1) qStr = xs.operands[0] as PdfString;
    else if (op == "\"" && xs.operands.Count >= 3) qStr = xs.operands[2] as PdfString;

    // Decide whether to emit a newline. If we have no prior rendered Y
    // or the new Y is meaningfully below the last rendered Y, we are on
    // a new logical line — emit \r\n. Otherwise (same Y ± ~fontSize*0.3)
    // we are continuing the same row from a different column.
    var yThreshold = Math.Max(1.0, xs.fontSize * 0.3 * (xs.tmRotated ? xs.tmN : 1.0));
    bool sameRow = !double.IsNaN(xs.lastRenderedY)
                   && Math.Abs(newY - xs.lastRenderedY) <= yThreshold;
    if (!sameRow)
    {
        if (_text.Length > 0 && _text[^1] != '\n')
        {
            RecordLineY();
            AppendStreamBreak();
        }
        xs.lastRunEndX = double.NaN; xs.lastRunEndDevX = double.NaN; xs.lastRunEndPageX = double.NaN; // new line, reset gap tracking
    }

    if (qStr is not null)
    {
        // Styled single glyph: one-char /ActualText over a one-glyph
        // show falls back to the font's own decode (see the flag note).
        if (xs.actualText is not null && !xs.actualTextUsed && xs.actualTextSingleChar
            && ActualTextYieldsToDecode(xs, qStr))
            xs.actualText = null;
        if (xs.actualText is not null)
        {
            if (!xs.actualTextUsed)
            {
                AppendShowText(xs.actualText);
                xs.actualTextUsed = true;
            }
            // Advance the pen over the replaced glyphs (see the Tj note).
            xs.tx += (xs.currentMetrics?.MeasureString(qStr.Value, xs.fontSize)
                   ?? xs.fontSize * 0.5 * qStr.Value.Length) * xs.horizScale;
        }
        else
        {
            var fullDecoded = ApplyRtlIfPureRtl(NormalizeDecoded(
                DecodeString(qStr.Value, xs.currentToUnicode, xs.currentFontDict, xs.reader, xs.useFontEngine), foldNbsp: xs.searchRect is null));
            // When a search rectangle is active, clip the run to the
            // glyphs whose advance box falls inside it (page space).
            var clipping = xs.clipRect is not null && xs.tmD > 0 && xs.currentMetrics is not null;
            var decoded = fullDecoded;
            if (clipping)
            {
                var clip = new StringBuilder();
                var pen = xs.tx;
                AppendClippedRun(clip, qStr.Value, xs.currentToUnicode, xs.currentFontDict,
                    xs.reader, xs.useFontEngine, xs.currentMetrics, xs.fontSize, xs.horizScale,
                    xs.clipRect!, xs.tmOriginX, xs.tmA, xs.localCmTx, xs.cmLa, ref pen, xs.charSpacing, xs.wordSpacing, out _, xs.blankClip);
                decoded = clip.ToString();
            }
            if (!clipping || decoded.Length > 0)
            {
                TrackRowX(xs.tmRotated
                    ? (_pageRotDominant ? xs.tmOriginX + (xs.tx - xs.tmOriginX) * xs.tmA + xs.localCmTx : xs.tmE)
                    : (xs.tmOriginX + (xs.tx - xs.tmOriginX) * xs.tmA) * xs.cmLa + xs.cmLe);
                // Same-row continuation: insert proportional spaces for the
                // horizontal gap (Pure mode), mirrors Tj/TJ gap logic.
                if (sameRow && !double.IsNaN(xs.lastRunEndX)
                    && _text.Length > 0 && _text[^1] != ' ' && _text[^1] != '\n')
                {
                    var gap = xs.tx - xs.lastRunEndX;
                    var threshold = xs.fontSize * 0.2;
                    var spaces = ComputeSpaceCount(gap, threshold, xs.fontSize);
                    if (spaces > 0) _sawIntraLineGapSpaces = true;
                    for (int si = 0; si < spaces; si++) _text.Append(' ');
                }
                AppendShowText(decoded);
            }
            var measuredWidth = xs.currentMetrics?.MeasureString(qStr.Value, xs.fontSize);
            var width = (measuredWidth ?? (xs.fontSize * 0.5 * fullDecoded.Length)) * xs.horizScale;
            xs.lastRunEndDevX = xs.tmRotated ? xs.tmOriginX + (xs.tx - xs.tmOriginX) * xs.tmA + xs.localCmTx + width * xs.tmA : double.NaN;
            xs.lastRunEndPageX = xs.tmRotated ? double.NaN : xs.tmE + (xs.tx + width - xs.tlmX) * xs.tmAr;
            xs.lastRunStartPageX = xs.tmRotated ? double.NaN : xs.tmE + (xs.tx - xs.tlmX) * xs.tmAr;
            xs.lastRunEndX = xs.tx + width * (xs.tmRotated ? xs.tmA : 1.0); // rotated: advance projects through the axis norm
            xs.lastRunEstWidth = width;
            xs.lastHadMetrics = measuredWidth.HasValue;
            xs.lastDecodedLength = decoded.Length;
            xs.tx += width;
            xs.lastRenderedY = newY; xs.lastRenderedFs = xs.fontSize * (xs.tmRotated ? xs.tmN : 1.0); xs.lastRenderedCmTy = xs.localCmTy;
        }
    }
    _currentLineY = newY;
    _currentLineCmTy = xs.tmRotated ? 0 : LineCmAdjust(xs.depth, xs.localCmD, xs.localCmTy, _currentLineY);
    }
}
