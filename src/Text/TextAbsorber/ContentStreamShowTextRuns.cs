using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextAbsorber
{
    /// <summary>The array's own runs: each string decoded, measured and shown, each number kerned, with the operator's RTL pass over the whole.</summary>
    private void ShowTextArrayRuns(ExtractState xs, string op, PdfArray tjArr)
    {
        var tj = new ShowTextArrayState();
        tj.tjWidth = 0;
        tj.tjDecodedLen = 0;
        tj.tjBuf = new StringBuilder();
        tj.clipRot = xs.clipRect is not null && xs.tmRotated && xs.currentMetrics is not null;
        tj.clipping = tj.clipRot || (xs.clipRect is not null && xs.tmD > 0 && xs.currentMetrics is not null);
        tj.clipBuf = tj.clipping ? new StringBuilder() : null;
        tj.clipPen = xs.tx;
        tj.hadString = false;
        tj.tjRunPageX = 0;
        MarkShowTextArrayLineCells(xs, tj, tjArr);
        TrackRowX(xs.tmRotated
            ? (_pageRotDominant ? xs.tmOriginX + (xs.tx - xs.tmOriginX) * xs.tmA + xs.localCmTx : xs.tmE)
            : (xs.tmOriginX + (xs.tx - xs.tmOriginX) * xs.tmA) * xs.cmLa + xs.cmLe);
        tj.leadingSpaces = 0;
        tj.tjRunDevX = xs.tmRotated ? xs.tmOriginX + (xs.tx - xs.tmOriginX) * xs.tmA + xs.localCmTx : 0;
        tj.tjUseDev = xs.tmRotated && !double.IsNaN(xs.lastRunEndDevX);
        tj.tjUsePage = !xs.tmRotated && !double.IsNaN(xs.lastRunEndPageX);
        tj.tjStartPageX = xs.tmE + (xs.tx - xs.tlmX) * xs.tmAr;
        tj.tjGapPre = double.IsNaN(xs.lastRunEndX) ? 0
            : tj.tjUseDev ? tj.tjRunDevX - xs.lastRunEndDevX
            : tj.tjUsePage ? tj.tjStartPageX - xs.lastRunEndPageX
            : (xs.tx - xs.lastRunEndX) * (xs.tmRotated ? xs.tmA : xs.tmAr);
        OpenShowTextArrayGap(xs, tj, op, tjArr);
        tj.tjRel = new List<double>();
        ArmShowTextArraySynth(xs, tj, tjArr);
        tj.tjDbg = GridDebug ? new StringBuilder() : null;
        foreach (var item in tjArr)
        {
            if (!ShowTextArrayItem(xs, tj, op, item)) break;
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
        xs.lastRunEndPageX = xs.tmRotated ? double.NaN : xs.tmE + (xs.tx + tj.tjWidth - xs.tlmX) * xs.tmAr;
        xs.lastRunStartPageX = xs.tmRotated ? double.NaN : xs.tmE + (xs.tx - xs.tlmX) * xs.tmAr;
        xs.lastRunEndX = xs.tx + tj.tjWidth * (xs.tmRotated ? xs.tmA : 1.0); // rotated: advance projects through the axis norm
        xs.lastRunEstWidth = tj.tjWidth;
        xs.lastDecodedLength = tj.tjDecodedLen;
        xs.tx += tj.tjWidth;
        // Track rendered Y for subsequent line-break suppression logic
        xs.lastRenderedY = xs.tmY; xs.lastRenderedFs = xs.fontSize * (xs.tmRotated ? xs.tmN : 1.0); xs.lastRenderedCmTy = xs.localCmTy;
    }
}
