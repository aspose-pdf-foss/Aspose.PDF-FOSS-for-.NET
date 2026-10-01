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
            ShowActualText(xs, tjStr);
        }
        else
        {
            ShowDecodedText(xs, tjStr);
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
            ShowTextArrayRuns(xs, op, tjArr);
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
        EmitSpacedNextLineText(xs, qStr, sameRow, newY);
    }
    _currentLineY = newY;
    _currentLineCmTy = xs.tmRotated ? 0 : LineCmAdjust(xs.depth, xs.localCmD, xs.localCmTy, _currentLineY);
    }
}
