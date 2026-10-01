using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextAbsorber
{
    /// <summary>The assembled run joins the extracted text: a line-leading pad is remembered, an RTL run re-homes it, a duplicate stack replaces its occluded victim.</summary>
    private void AppendShowTextArrayRun(ExtractState xs, ShowTextArrayState tj)
    {
        tj.tjIsLeadingPos = _text.Length == 0 || _text[^1] == '\n';
        tj.tjAllSpace = tj.tjText.Length > 0 && tj.tjText.Trim().Length == 0;
        if (tj.tjAllSpace && tj.tjIsLeadingPos)
        {
            // A line-leading whitespace run is a grid citizen: emit it
            // (placeholder space glyphs form pure-pad lines in the
            // output). Remember its Y so a following RTL run
            // on the same line can still re-home the pad to its logical
            // end (the appended space also guards the re-home branch's
            // "text doesn't end with a space" check below).
            xs.pendingReorderSpaceY = xs.tmY;
            AppendShowText(tj.tjText);
        }
        else
        {
            if (!double.IsNaN(xs.pendingReorderSpaceY))
            {
                if (System.Math.Abs(xs.tmY - xs.pendingReorderSpaceY) > 1.0)
                    xs.pendingReorderSpaceY = double.NaN;              // different line: drop the orphan
                else if (tj.tjText.Length > 0 && Aspose.Pdf.Text.BidiReorderer.IsRtlChar(tj.tjText[0])
                         && _text.Length > 0 && _text[^1] != ' ')
                {
                    _text.Append(' ');                             // re-home before the first RTL run
                    xs.pendingReorderSpaceY = double.NaN;
                }
            }
            if (tj.tjText.Length > 0)
            {
                var tjPageScale = Math.Abs(xs.tmRotated ? xs.tmA : xs.tmAr);
                double[]? tjCharXs = null;
                if (tj.tjRelValid && tj.tjRel.Count == tj.tjText.Length)
                {
                    tjCharXs = new double[tj.tjRel.Count];
                    for (var ri = 0; ri < tj.tjRel.Count; ri++)
                        tjCharXs[ri] = tj.tjRel[ri] * tjPageScale;
                }
                _pageRunSpans.Add(new RunSpan(_text.Length, tj.tjText.Length,
                    xs.tmRotated ? (_pageRotDominant ? xs.tmOriginX + (xs.tx - xs.tmOriginX) * xs.tmA + xs.localCmTx : xs.tmE)
                              : xs.tmE + (xs.tx - xs.tlmX) * xs.tmAr,
                    tj.tjWidth * tjPageScale,
                    !tj.clipping && IsPureRtlRun(tj.tjText),
                    tjCharXs));
            }
            if (!xs.tmRotated && xs.searchRect is null && !xs.rawInlineScripts
                && tj.tjText.Trim().Length > 0)
                xs.dedupPrevOffset = _text.Length;
            AppendShowText(tj.tjText);
        }
    }

    /// <summary>The run's glyph cells on the page grid, in the rotated and the upright axes.</summary>
    private void MarkShowTextArrayCells(ExtractState xs, ShowTextArrayState tj)
    {
        if (xs.tmRotated && !_pageRotDominant && _pageCellWidth > 0 && !tj.clipping
            && tj.tjRelValid && tj.tjRel.Count == tj.tjText.Length
            && tj.tjText.IndexOf(' ') > 0)
        {
            var rsb = new StringBuilder(tj.tjText.Length + 8);
            var ci2 = 0;
            while (ci2 < tj.tjText.Length)
            {
                if (tj.tjText[ci2] != ' ') { rsb.Append(tj.tjText[ci2]); ci2++; continue; }
                var n2 = 0;
                while (ci2 + n2 < tj.tjText.Length && tj.tjText[ci2 + n2] == ' ') n2++;
                var after2 = ci2 + n2;
                if (after2 >= tj.tjText.Length) { rsb.Append(' ', n2); break; }
                var target2 = (int)Math.Floor(Math.Abs(tj.tjRel[after2] * xs.tmA) / _pageCellWidth);
                rsb.Append(' ', Math.Min(200, Math.Max(n2 + 1, target2 - rsb.Length)));
                ci2 = after2;
            }
            if (rsb.Length != tj.tjText.Length)
            {
                tj.tjText = rsb.ToString();
                tj.tjRelValid = false; // char↔pen map no longer 1:1
            }
        }
        // UPRIGHT pure pages: an in-string space whose rendered advance
        // is a genuine column jump (> 1 em — e.g. a Tw-inflated table
        // gap, "( AIRCASTLE )" under Tw 1.1296) pads the following
        // glyph to its grid column, exactly as the intra-TJ kern rule
        // does; ordinary word spaces (0.2–0.6 em) stay single.
        if (!xs.tmRotated && _pageCellWidth > 0 && !tj.clipping
            && tj.tjRelValid && tj.tjRel.Count == tj.tjText.Length
            && tj.tjText.IndexOf(' ') >= 0)
        {
            var usb = new StringBuilder(tj.tjText.Length + 8);
            var ui = 0;
            var changed = false;
            while (ui < tj.tjText.Length)
            {
                if (tj.tjText[ui] != ' ') { usb.Append(tj.tjText[ui]); ui++; continue; }
                var n3 = 0;
                while (ui + n3 < tj.tjText.Length && tj.tjText[ui + n3] == ' ') n3++;
                var after3 = ui + n3;
                if (after3 >= tj.tjText.Length) { usb.Append(' ', n3); break; }
                var gapAdv = tj.tjRel[after3] - tj.tjRel[ui];
                if (gapAdv > xs.fontSize * xs.horizScale)
                {
                    var penAfter = xs.tmE + (xs.tx + tj.tjRel[after3] - xs.tlmX) * xs.tmAr;
                    var pad3 = ColumnSpaces(gapAdv, 0, penAfter,
                        tj.leadingSpaces + usb.Length, n3);
                    usb.Append(' ', Math.Min(200, pad3));
                    if (pad3 != n3) changed = true;
                }
                else usb.Append(' ', n3);
                ui = after3;
            }
            if (changed)
            {
                tj.tjText = usb.ToString();
                tj.tjRelValid = false; // char↔pen map no longer 1:1
            }
        }
    }

    /// <summary>One item of the array: a string is decoded, measured and buffered; a number kerns the pen.</summary>
    private bool ShowTextArrayItem(ExtractState xs, ShowTextArrayState tj, string op, PdfObject item)
    {
        if (item is PdfString tjS)
        {
            ShowTextArrayString(xs, tj, op, tjS);
        }
        else
        {
            KernShowTextArrayNumber(xs, tj, item);
        }
        return true;
    }

    /// <summary>A number between the strings kerns the pen and, when the gap is wide enough, becomes a space.</summary>
    private void KernShowTextArrayNumber(ExtractState xs, ShowTextArrayState tj, PdfObject item)
    {
        var adj = GetNumber(item);
        tj.tjDbg?.Append('(').Append(adj.ToString("F0")).Append(')');
        var advance = -adj * xs.fontSize / 1000.0;
        tj.tjWidth += advance;
        var kernGapStart = tj.tjWidth - advance;
        // Any kern beyond the classic −190 word-break threshold
        // separates (incl. Pure-grid column jumps in letter-tracked
        // single-glyph arrays, e.g. an 11-piece row with a −9711
        // column hop); the −130 rule EXTENDS the reach for
        // armed shapes only.
        var tjKernBreaks = tj.tjPositioningArray
            ? adj - tj.tjDeepMedian <= -130
            : adj < -190 || (tj.tjSynthArmed && adj <= -130)
              || (!double.IsNaN(tj.tjLtrackMedian) && adj - tj.tjLtrackMedian <= -130
                  && (tj.tjLtrackMedian >= 0 || adj <= -250));
        var kernAfterSpace = tj.tjBuf.Length > 0 && tj.tjBuf[^1] == ' ';
        // Grid-pad only genuine column jumps (≥ ~1 em). A word-space
        // kern (0.2–0.6 em) stays a single space — proportional prose
        // output columns drift from ink columns, and padding to the
        // grid there sprays spaces mid-sentence.
        var gridKernPad = _pageCellWidth > 0 && !tj.clipping && advance > xs.fontSize;
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
                                   ? xs.tmOriginX + (xs.tx + tj.tjWidth - xs.tmOriginX) * xs.tmA + xs.localCmTx
                                   : xs.tmE + (xs.tx + tj.tjWidth - xs.tlmX) * xs.tmAr;
                pad = ColumnSpaces(advance, 0, penPageX, tj.leadingSpaces + tj.tjBuf.Length,
                    kernAfterSpace ? 0 : 1);
                if (GridDebug)
                    Console.Error.WriteLine($"[tjkern] pen={penPageX:F2} col={(penPageX - _pageMinX) / _pageCellWidth:F3} pad={pad} buf='{tj.tjBuf}'");
            }
            for (var k = 0; k < pad; k++)
            {
                tj.tjBuf.Append(' ');
                if (tj.tjRelValid) tj.tjRel.Add(kernGapStart);
            }
        }
        if (tj.clipping)
        {
            // The synthesized word space sits at the current pen; emit it
            // only when that point is inside the rectangle (advance-axis
            // position: page X upright, page Y sideways).
            if (tj.tjPositioningArray
                ? adj - tj.tjDeepMedian <= -130
                : adj < -190 || (tj.tjSynthArmed && adj <= -130)
                  || (!double.IsNaN(tj.tjLtrackMedian) && adj - tj.tjLtrackMedian <= -130
                      && (tj.tjLtrackMedian >= 0 || adj <= -250)))
            {
                // Under LimitToPageBounds with no caller rectangle, searchRect
                // is null while clipping is driven by the page-bounds clipRect;
                // fall back to it so the in-window test doesn't dereference null.
                var win = xs.searchRect ?? xs.clipRect!;
                var inWindow = tj.clipRot
                    ? xs.tmF + (tj.clipPen - xs.tlmX) * xs.tmBr is var pY
                        && pY >= win.LLY && pY <= win.URY
                    : xs.tmOriginX + (tj.clipPen - xs.tmOriginX) * xs.tmA + xs.localCmTx is var pX
                        && pX >= win.LLX && pX <= win.URX;
                if (inWindow && (tj.clipBuf!.Length == 0 || tj.clipBuf[^1] != ' '))
                    tj.clipBuf!.Append(' ');
            }
            tj.clipPen += advance;
        }
    }

    /// <summary>One string of the array: decoded, measured, clipped to the search rectangle and buffered.</summary>
    private void ShowTextArrayString(ExtractState xs, ShowTextArrayState tj, string op, PdfString tjS)
    {
        tj.hadString = true;
        var tjDecoded = NormalizeDecoded(DecodeString(tjS.Value, xs.currentToUnicode, xs.currentFontDict, xs.reader, xs.useFontEngine), foldNbsp: xs.searchRect is null);
        tj.tjDbg?.Append('\'').Append(tjDecoded).Append('\'');
        tj.tjBuf.Append(tjDecoded);
        var tjItemW = ((xs.currentMetrics?.MeasureString(tjS.Value, xs.fontSize)
                   ?? (xs.fontSize * 0.5 * tjS.Value.Length)) + SpacingAdvance(xs, tjS.Value)) * xs.horizScale;
        if (tj.tjRelValid)
        {
            var itemRel = BuildCharXs(tjS.Value, xs.currentMetrics, xs.fontSize,
                xs.horizScale, tjDecoded.Length, xs.charSpacing, xs.wordSpacing);
            if (itemRel is not null)
                foreach (var r in itemRel) tj.tjRel.Add(tj.tjWidth + r);
            else if (tjDecoded.Length > 0)
            {
                // Uniform fallback for this sub-string only.
                var step = tjItemW / tjDecoded.Length;
                for (var ri = 0; ri < tjDecoded.Length; ri++)
                    tj.tjRel.Add(tj.tjWidth + step * ri);
            }
        }
        tj.tjWidth += tjItemW;
        tj.tjDecodedLen += tjDecoded.Length;
        if (tj.clipRot)
            tj.clipPen = AppendClippedRunRot(xs, tj.clipBuf!, tjS.Value, tj.clipPen);
        else if (tj.clipping)
            (tj.clipPen, _) = AppendClippedRun(tj.clipBuf!, tjS.Value, xs.currentToUnicode, xs.currentFontDict,
                xs.reader, xs.useFontEngine, xs.currentMetrics, xs.fontSize, xs.horizScale,
                xs.clipRect!, xs.tmOriginX, xs.tmA, xs.localCmTx, xs.cmLa, tj.clipPen, xs.charSpacing, xs.wordSpacing, xs.blankClip,
                dropLeadingSpaces: xs.searchRect is not null && !xs.blankClip
                    && tj.clipBuf!.Length == 0 && (_text.Length == 0 || _text[^1] == '\n'));
    }

    /// <summary>The synthesised gap spaces the array's own kerning implies, once its runs are collected.</summary>
    private void SolveShowTextArraySynthGaps(ExtractState xs, ShowTextArrayState tj)
    {
        tj.tjText = tj.clipping ? tj.clipBuf!.ToString() : ApplyRtlIfPureRtl(tj.tjBuf.ToString());
        if (GridDebug)
        {
            Console.Error.WriteLine($"[tjrun] tx={xs.tx:F2} w={tj.tjWidth:F2} fs={xs.fontSize:F2} tmA={xs.tmA:F3} armed={tj.tjSynthArmed} pieces={tj.tjPieceCount} lead={tj.leadingSpaces} txt='{(tj.tjText.Length > 32 ? tj.tjText.Substring(0, 32) : tj.tjText)}'");
            var dbgS = tj.tjDbg!.ToString();
            Console.Error.WriteLine($"[tjarr] {(dbgS.Length > 300 ? dbgS.Substring(0, 300) : dbgS)}");
        }
        // Duplicate-stack dedup (see the Tj path): the occluder inherits
        // the victim's slot, so its own gap spaces are dropped too.
        if (!xs.tmRotated && xs.searchRect is null && !xs.rawInlineScripts
            && tj.tjText.Trim().Length > 0
            && ReplaceOccludedPrevRun(xs, tj.tjText, xs.tmE + (xs.tx - xs.tlmX) * xs.tmAr,
                tj.tjWidth * Math.Abs(xs.tmAr), xs.tmY))
            tj.leadingSpaces = 0;
        if (tj.clipping ? tj.tjText.Length > 0 : tj.hadString)
        {
            if (tj.leadingSpaces > 0) _sawIntraLineGapSpaces = true;
            for (int si = 0; si < tj.leadingSpaces; si++) _text.Append(' ');
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
        if ((tj.leadingSpaces == 0
                || ExtractionOptions?.FormattingMode
                    is TextExtractionOptions.TextFormattingMode.Raw
                    or TextExtractionOptions.TextFormattingMode.MemorySaving)
            && _text.Length > 0 && _text[^1] == ' ' && tj.tjText.Length > 0 && tj.tjText[0] == ' '
            && !RecentTextIsRtl())
        {
            tj.tjText = tj.tjText.Substring(1);
            if (tj.tjRelValid && tj.tjRel.Count > 0) tj.tjRel.RemoveAt(0);
        }
    }

    /// <summary>Arm the gap-space synthesis: the array's adjustment median decides whether its numbers are letter tracking or word gaps.</summary>
    private void ArmShowTextArraySynth(ExtractState xs, ShowTextArrayState tj, PdfArray tjArr)
    {
        tj.tjRelValid = !tj.clipping;
        tj.tjIsType0 = xs.currentFontDict?.GetName("Subtype") == "Type0";
        tj.tjPieceCount = 0;
        tj.tjMultiGlyph = false;
        tj.tjAdjs = new List<double>();
        foreach (var pre in tjArr)
            if (pre is PdfString preS0)
            {
                tj.tjPieceCount++;
                if (preS0.Value.Length >= (tj.tjIsType0 ? 4 : 2)) tj.tjMultiGlyph = true;
            }
            else
                tj.tjAdjs.Add(GetNumber(pre));
        tj.tjSynthArmed = tj.tjMultiGlyph;
        if (!tj.tjSynthArmed)
            foreach (var pre in tjArr)
            {
                if (pre is not PdfString preS) continue;
                var preDec = NormalizeDecoded(DecodeString(preS.Value, xs.currentToUnicode, xs.currentFontDict, xs.reader, xs.useFontEngine));
                if (preDec.Length >= 2) { tj.tjSynthArmed = true; tj.tjMultiGlyph = true; break; }
                var preArm = false;
                foreach (var preC in preDec)
                    if (!char.IsUpper(preC) && !char.IsPunctuation(preC))
                    { preArm = true; break; }
                if (preArm) { tj.tjSynthArmed = true; break; }
            }
        tj.tjSynthArmed = tj.tjSynthArmed && tj.tjPieceCount >= 2
            && (tj.tjPieceCount <= 10 || tj.tjMultiGlyph);
        tj.tjMedian = double.NaN;
        if (tj.tjPieceCount >= 5 && tj.tjAdjs.Count >= 4)
        {
            tj.tjAdjs.Sort();
            tj.tjMedian = tj.tjAdjs[tj.tjAdjs.Count / 2];
        }
        tj.tjLtrackMedian = !tj.tjSynthArmed && !tj.tjMultiGlyph ? tj.tjMedian : double.NaN;
        tj.tjPositioningArray = false;
        tj.tjDeepMedian = double.NaN;
        if (!tj.tjMultiGlyph && tj.tjAdjs.Count >= 3)
        {
            var deepList = new List<double>();
            foreach (var a2 in tj.tjAdjs)
                if (a2 <= -130) deepList.Add(a2);
            if (deepList.Count * 2 >= tj.tjAdjs.Count && deepList.Count > 0)
            {
                tj.tjPositioningArray = true;
                deepList.Sort();
                // The placement baseline is the word-depth cluster's own
                // median; only a kern well below IT separates words.
                tj.tjDeepMedian = deepList[deepList.Count / 2];
            }
        }
    }

    /// <summary>The gap between this array and the run before it: a wide enough pen jump opens a space.</summary>
    private void OpenShowTextArrayGap(ExtractState xs, ShowTextArrayState tj, string op, PdfArray tjArr)
    {
        if (!double.IsNaN(xs.lastRunEndX)
            && _text.Length > 0 && _text[^1] != '\n'
            && (_text[^1] != ' '
                || _prevShowHadTab
                || (_pageCellWidth > 0 && tj.tjGapPre > _pageCellWidth)))
        {
            var tjGap = tj.tjUseDev ? tj.tjRunDevX - xs.lastRunEndDevX
                : tj.tjUsePage ? tj.tjStartPageX - xs.lastRunEndPageX
                : xs.tx - xs.lastRunEndX;
            // Effective font size for the gap threshold. Upright pages
            // keep the page-space Tm scale (the calibrated rule); rotated
            // runs use the projected line size — their raw fontSize can be
            // Tm-scaled (fs 327 with tmA 0.027 is 8.85 pt on the page) and
            // an unprojected threshold swallows every real word gap.
            var tjGapFs = tj.tjUsePage ? xs.fontSize * Math.Abs(xs.tmAr)
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
                && tj.tjStartPageX >= xs.lastRunStartPageX - _pageCellWidth;
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
                Console.Error.WriteLine($"[tjgap] tx={xs.tx:F1} lastEnd={xs.lastRunEndX:F1} gap={tjGap:F1} rot={xs.tmRotated} runPageX={tj.tjRunPageX:F1} gapFs={tjGapFs:F2} effFs={_currentLineEffFs:F2} useDev={tj.tjUseDev}");
            // Leading drawn spaces of the array's first piece fill
            // their own columns and count toward the grid target
            // (see the Tj-path note).
            // See the Tj-path note: a run pads to its own start
            // column; leading drawn spaces land at their columns.
            tj.leadingSpaces = _pageCellWidth > 0
                ? ColumnSpaces(tjGap, tjGapFs * 0.15, tj.tjRunPageX)
                : ComputeSpaceCount(tjGap, tjGapFs * 0.15, tjGapFs);
            // Sub-cell gaps keep their grid pad (see the Tj-path note:
            // the gap space grid-places like any word start).
            }
        }
    }

    /// <summary>The line's own cell span on the page grid, before the array's runs are read.</summary>
    private void MarkShowTextArrayLineCells(ExtractState xs, ShowTextArrayState tj, PdfArray tjArr)
    {
        if (_pageCellWidth > 0)
        {
            // See the Tj-path note: device X for upright text;
            // minority rotated runs grid at their device X too.
            tj.tjRunPageX = xs.tmRotated
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
            TrackLineStart(tj.tjRunPageX, tjAllSpaces);
        }
    }
}
