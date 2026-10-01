using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextAbsorber
{
    /// <summary>Show text: one decoded run seated on the grid and appended.</summary>
    private void EmitDecodedRun(ExtractState xs, PdfString tjStr, string decoded, string fullDecoded, double width, double txClip, bool clipping)
    {
        var runPageX = TrackRunLineStart(xs, decoded, txClip);
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
        // A 180-degree line advances LEFTWARD in page space: its hole is measured along the line's own advance.
        var advanceSign = xs.tmAr < 0 ? -1 : 1;
        var gapPre = double.IsNaN(xs.lastRunEndX) ? 0
            : useDev ? runDevX - xs.lastRunEndDevX
            : usePage ? (runStartPageX - xs.lastRunEndPageX) * advanceSign
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
                : usePage ? (runStartPageX - xs.lastRunEndPageX) * advanceSign
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
        RecordRunSpan(xs, tjStr, decoded, fullDecoded, txClip, clipping);
        if (!xs.tmRotated && xs.searchRect is null && !xs.rawInlineScripts
            && decoded.Trim().Length > 0)
            xs.dedupPrevOffset = _text.Length;
        AppendShowText(decoded);
        xs.lastDecodedLength = decoded.Length;
    }
}
