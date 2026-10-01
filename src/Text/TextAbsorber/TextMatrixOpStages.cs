using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextAbsorber
{
    /// <summary>The stages of a text-matrix operator: the same-row filter against the last rendered line, and seating the text origin for upright or rotated text.</summary>
    private void SeatTextMatrixOrigin(TextMatrixOpState mo)
    {
        if (mo.xs.tmRotated)
        {
            // Advance axis for sideways text is the origin projected
            // on the composed direction vector (a,b) — so the
            // word-gap / column-grid logic sees real line offsets.
            var n2 = Math.Sqrt(mo.cEa * mo.cEa + mo.cEb * mo.cEb);
            if (n2 < 0.001) n2 = 1.0;
            mo.xs.tlmX = RotatedReadX(mo.cEa, mo.cEb, mo.cEe, mo.cEf);
            // Reading-axis scale: |a| is ~0 for sideways text, which
            // would freeze runPageX at the block origin (every run in
            // the BT block reporting the same grid X). The advance
            // axis norm |(a,b)| is the true per-unit X scale.
            mo.xs.tmA = n2;
        }
        else
        {
            mo.xs.tlmX = GetNumber(mo.xs.operands[4]);
        }
    }

    /// <summary></summary>
    private void FilterTextMatrixRow(TextMatrixOpState mo)
    {
        mo.refY = !double.IsNaN(mo.xs.lastRenderedY) ? mo.xs.lastRenderedY
                 : !double.IsNaN(mo.xs.prevTmY) ? mo.xs.prevTmY
                 : _currentLineY;
        // After a ' or " operator, the actual rendered Y is tmY - leading,
        // but a subsequent Tm's newTmY is compared with the refY directly.
        // For same-row column layouts the Tm targets Y ≈ previous Tm's Y
        // (before its '), so the above refY==lastRenderedY path would
        // fire a newline incorrectly. Fall back to prevTmY when the
        // difference to lastRenderedY is exactly ~leading.
        if (!double.IsNaN(mo.xs.prevTmY) && !double.IsNaN(mo.xs.lastRenderedY)
            && Math.Abs(Math.Abs(mo.newTmY - mo.xs.lastRenderedY) - mo.xs.leading) < mo.tmYThreshold)
        {
            mo.refY = mo.xs.prevTmY;
        }
        mo.tmSameRow = (mo.xs.tmD > 0 || mo.xs.tmRotated) && !double.IsNaN(mo.refY)
                         && Math.Abs(mo.newTmY - mo.refY) <= mo.tmYThreshold;
        // A page that steps rows with q/cm translations keeps an
        // IDENTICAL Tm in every BT block — the row change lives only
        // in the CTM Y-translation. Compare it against the CTM in
        // effect when the last text rendered; a moved translation is
        // a row change even though the Tm Y matches.
        if (mo.tmSameRow && !mo.xs.tmRotated && !double.IsNaN(mo.xs.lastRenderedCmTy)
            && Math.Abs(mo.xs.localCmTy - mo.xs.lastRenderedCmTy) > mo.tmYThreshold)
        {
            mo.tmSameRow = false;
        }
        if (GridDebug)
            Console.Error.WriteLine($"[tm] newY={mo.newTmY:F1} refY={mo.refY:F1} same={mo.tmSameRow} rot={mo.xs.tmRotated} lastRendY={mo.xs.lastRenderedY:F1} prevTmY={mo.xs.prevTmY:F1}");
        mo.tmFiltered = !mo.xs.tmRotated
            && (mo.tmSameRow ? mo.xs.openLineSkip : LineFiltered(mo.xs, mo.newTmY));
        if ((mo.xs.tmD > 0 || mo.xs.tmRotated) && !double.IsNaN(mo.refY) && !mo.tmSameRow &&
            !mo.tmFiltered && _text.Length > 0 && _text[^1] != '\n')
        {
            RecordLineY();
            AppendStreamBreak();
        }
        // Track absolute page-space Y for line sorting: keep
        // _currentLineY in text space, but snapshot the CTM Y offset
        // in effect now so RecordLineY can emit page-space Y. Only inside
        // a Form XObject (depth > 0) — page-content Y tracking is left
        // byte-identical to avoid disturbing the common extraction path.
        // A FILTERED line leaves the tracking state (the open line's
        // baseline, the gap-detection anchors) untouched.
        if (!mo.tmFiltered)
        {
            _currentLineY = mo.newTmY;
            _currentLineCmTy = mo.xs.tmRotated ? 0 : LineCmAdjust(mo.xs.depth, mo.xs.localCmD, mo.xs.localCmTy, _currentLineY);
            mo.xs.prevTmY = mo.newTmY;
            mo.xs.openLineSkip = false;
        }
    }
}
