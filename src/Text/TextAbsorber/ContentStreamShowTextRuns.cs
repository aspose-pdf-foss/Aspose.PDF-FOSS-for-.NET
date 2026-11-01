using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextAbsorber
{
    /// <summary>The array's own runs: each string decoded, measured and shown, each number kerned, with the operator's RTL pass over the whole.</summary>
    /// <summary>A rotated run's row X: its device X when the page's rotation dominates, else its matrix X.</summary>
    private double RotatedRunX(ExtractState xs) =>
        _pageRotDominant ? xs.tmOriginX + (xs.tx - xs.tmOriginX) * xs.tmA + xs.localCmTx : xs.tmE;

    private void ShowTextArrayRuns(ExtractState xs, PdfArray tjArr)
    {
        var tj = new ShowTextArrayState();
        tj.tjWidth = 0;
        tj.tjBuf = new StringBuilder();
        tj.clipRot = xs.clipRect is not null && xs.tmRotated && xs.currentMetrics is not null;
        tj.clipping = tj.clipRot || (xs.clipRect is not null && xs.tmD > 0 && xs.currentMetrics is not null);
        tj.clipBuf = tj.clipping ? new StringBuilder() : null;
        tj.clipPen = xs.tx;
        tj.hadString = false;
        tj.tjRunPageX = 0;
        // Before the line tracking: the array that opens a new logical line anchors that line, not this one.
        BreakAtBackwardJump(xs, xs.tmE + (xs.tx - xs.tlmX) * xs.tmAr);
        MarkShowTextArrayLineCells(xs, tj, tjArr);
        TrackRowX(xs.tmRotated
            ? RotatedRunX(xs)
            : (xs.tmOriginX + (xs.tx - xs.tmOriginX) * xs.tmA) * xs.cmLa + xs.cmLe);
        tj.leadingSpaces = 0;
        tj.tjRunDevX = xs.tmRotated ? xs.tmOriginX + (xs.tx - xs.tmOriginX) * xs.tmA + xs.localCmTx : 0;
        tj.tjUseDev = xs.tmRotated && !double.IsNaN(xs.lastRunEndDevX);
        tj.tjUsePage = !xs.tmRotated && !double.IsNaN(xs.lastRunEndPageX);
        tj.tjStartPageX = xs.tmE + (xs.tx - xs.tlmX) * xs.tmAr;
        if (double.IsNaN(xs.lastRunEndX)) tj.tjGapPre = 0;
        else if (tj.tjUseDev) tj.tjGapPre = tj.tjRunDevX - xs.lastRunEndDevX;
        else if (tj.tjUsePage) tj.tjGapPre = tj.tjStartPageX - xs.lastRunEndPageX;
        else tj.tjGapPre = (xs.tx - xs.lastRunEndX) * (xs.tmRotated ? xs.tmA : xs.tmAr);
        OpenShowTextArrayGap(xs, tj);
        tj.tjRel = new List<double>();
        ArmShowTextArraySynth(xs, tj, tjArr);
        tj.tjDbg = GridDebug ? new StringBuilder() : null;
#pragma warning disable S3267 // a step loop: each call does the work and returns false to stop - as a LINQ predicate the side effects would hide in a query
        foreach (var item in tjArr)
#pragma warning restore S3267
        {
            if (!ShowTextArrayItem(xs, tj, item)) break;
        }
        if (xs.currentFontNonAgl && tj.hadString)
            RecordAglError(xs.currentFontName, tj.tjBuf.ToString(),
                xs.tmE + (xs.tx - xs.tlmX) * xs.tmAr, xs.tmY + xs.localCmTy);
        SolveShowTextArraySynthGaps(xs, tj);
        // A minority-rotated run flattens left-to-right in logical
        // glyph order on one row: each INTERNAL
        // drawn-space group emits
        // max(n+1, floor(|cumAdvance|/cell) − chars)
        // spaces — the gap target is quantised from the advance
        // RELATIVE to the run start (not a difference of absolute
        // grid floors), and at least one synthesized pad joins
        // every drawn gap.
        MarkShowTextArrayCells(xs, tj);
        AppendShowTextArrayRun(xs, tj);
        xs.lastRunEndDevX = xs.tmRotated ? xs.tmOriginX + (xs.tx - xs.tmOriginX) * xs.tmA + xs.localCmTx + tj.tjWidth * xs.tmA : double.NaN;
        // The next run's gap is measured from the furthest ink: a trailing kern that pulls the pen
        // back over the last glyph does not open a gap after it.
        var tjEndWidth = xs.tmRotated || tj.tjInkWidth <= tj.tjWidth || NearRightToLeftText() ? tj.tjWidth : tj.tjInkWidth;
        TrackRunExtent(xs, xs.tmE + (xs.tx - xs.tlmX) * xs.tmAr, xs.tmE + (xs.tx + tjEndWidth - xs.tlmX) * xs.tmAr,
            xs.tx + tjEndWidth * (xs.tmRotated ? xs.tmA : 1.0)); // rotated: advance projects through the axis norm
        xs.tx += tj.tjWidth;
        // Track rendered Y for subsequent line-break suppression logic
        xs.lastRenderedY = xs.tmY; xs.lastRenderedCmTy = xs.localCmTy;
    }
}
