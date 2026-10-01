using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextAbsorber
{
    /// <summary>Page-grid scan: the show-string and show-array operator arms.</summary>
    private static void ScanShowString(GridScanState gs, PageGridState pg, string op)
    {
        if (op != "Tj") gs.tlmY -= gs.preTL; // ' and " imply T*
        var s = gs.operands.LastOrDefault(o => o is Core.PdfString) as Core.PdfString;
        if (s is not null && gs.metrics is not null && !ShowOutOfBounds(gs))
        {
            SeeShowX(gs, pg);
            // Grid buckets CEIL the effective size BEFORE aggregation
            // (9.2pt and 9.7pt text pools into one 10pt
            // bucket; 11.01pt grids as 12pt). Advances still measure at
            // the true size.
            var fsTrue = gs.fontSize * gs.fsScale;
            // Round before ceiling: matrix-composition float dirt
            // (12.0000001) must not bump a whole bucket.
            var fsDev = Math.Ceiling(Math.Round(fsTrue, 3));
            if (!pg.rawBySize.TryGetValue(fsDev, out var rw) || fsTrue < rw) pg.rawBySize[fsDev] = fsTrue;
            // Advances scale by the ADVANCE-axis norm — on rotated pages a
            // run's horizontal stretch is independent of its font size.
            var fsAdv = gs.fontSize * (gs.preRot ? gs.tmScaleX : gs.fsScale);
            var w1 = gs.metrics.MeasureString(s.Value, fsAdv) * gs.preHorizScale;
            pg.sumW += w1;
            var g = GlyphCount(s.Value.Length, gs.metrics);
            pg.cnt += g;
            if (gs.preRot) pg.rotChars += g; else pg.uprightChars += g;
            pg.charsPerSize[fsDev] = pg.charsPerSize.GetValueOrDefault(fsDev) + g;
            pg.widthPerSize[fsDev] = pg.widthPerSize.GetValueOrDefault(fsDev)
                + Math.Min(w1, 0.6 * fsTrue * g);
            pg.pureCharsPerSize[fsDev] = pg.pureCharsPerSize.GetValueOrDefault(fsDev) + g;
            pg.pureWidthPerSize[fsDev] = pg.pureWidthPerSize.GetValueOrDefault(fsDev)
                + Math.Min(w1, 0.6 * fsTrue * g);
            var (nsp, wsp) = DrawnSpaces(s.Value, gs.metrics, fsAdv, gs.preHorizScale);
            var tcW = (gs.preTc * g + gs.preTw * nsp) * (gs.preRot ? gs.tmScaleX : gs.fsScale) * gs.preHorizScale;
            pg.avgCharsPerSize[fsDev] = pg.avgCharsPerSize.GetValueOrDefault(fsDev) + g;
            pg.avgWidthPerSize[fsDev] = pg.avgWidthPerSize.GetValueOrDefault(fsDev)
                + Math.Min(Math.Max(w1 + tcW, 0), 0.6 * fsTrue * g);
        }
    }

    /// <summary></summary>
    private static void ScanShowArray(GridScanState gs, PageGridState pg, Core.PdfArray arr)
    {
        var sawString = false;
        // The estimate averages NET run widths — glyph advances plus
        // the array's kern adjustments — over the run's PHYSICAL text
        // length, which includes the word spaces its kern rule will
        // synthesize (an adjustment at word depth becomes a char).
        double arrW = 0; var arrG = 0; double arrFsTrue = 0, arrFsDev = 0;
        double arrWPure = 0; var arrSp = 0; double arrSpW = 0;
        // Mean-advance numerator: glyph widths plus kern adjustments,
        // EXCLUDING large positive ones — a positive adjustment past
        // ~0.1 em is a backward pen JUMP (RTL layout), not kerning, and
        // would cancel real ink out of the average, collapsing the cell.
        // Small positive tightening kerns stay in the sum.
        double arrWAvg = 0;
        var arrMulti = false; var arrDeep = 0; var arrAdjCnt = 0; var arrSynth = 0;
        // Word-space synthesis chars for the MEAN-ADVANCE population:
        // a deep kern only reads as a word space when it does NOT
        // adjoin a drawn space glyph (justified text kerns beside its
        // real spaces; those gaps are already counted by the glyph).
        var arrSynthAvg = 0; var pendingSynth = 0; var prevEndsSpace = false;
        foreach (var it in arr)
        {
            if (it is Core.PdfString ps && gs.metrics is not null)
            {
                sawString = true;
                arrFsTrue = gs.fontSize * gs.fsScale;
                arrFsDev = Math.Ceiling(Math.Round(arrFsTrue, 3));
                if (!pg.rawBySize.TryGetValue(arrFsDev, out var rw2) || arrFsTrue < rw2) pg.rawBySize[arrFsDev] = arrFsTrue;
                // Advance-axis norm (see the Tj note).
                var arrFsAdv = gs.fontSize * (gs.preRot ? gs.tmScaleX : gs.fsScale);
                var wPiece = gs.metrics.MeasureString(ps.Value, arrFsAdv) * gs.preHorizScale;
                arrW += wPiece;
                arrWPure += wPiece;
                arrWAvg += wPiece;
                var g2 = GlyphCount(ps.Value.Length, gs.metrics);
                arrG += g2;
                var (nsp2, wsp2) = DrawnSpaces(ps.Value, gs.metrics, arrFsAdv, gs.preHorizScale);
                arrSp += nsp2; arrSpW += wsp2;
                var simple = !gs.metrics.IsCid && ps.Value.Length > 0;
                if (pendingSynth > 0 && !(simple && ps.Value[0] == 0x20))
                    arrSynthAvg += pendingSynth;
                pendingSynth = 0;
                prevEndsSpace = simple && ps.Value[^1] == 0x20;
                if (g2 >= 2) arrMulti = true;
            }
            else if (it is not Core.PdfString && gs.metrics is not null)
            {
                var adj = GetNumber(it);
                arrW -= adj * gs.fontSize * (gs.preRot ? gs.tmScaleX : gs.fsScale) * gs.preHorizScale / 1000.0;
                if (adj < 100)
                    arrWAvg -= adj * gs.fontSize * (gs.preRot ? gs.tmScaleX : gs.fsScale) * gs.preHorizScale / 1000.0;
                arrAdjCnt++;
                if (adj <= -130) arrDeep++;
                if (adj < -190 || (arrMulti && adj <= -130))
                {
                    arrSynth++;
                    if (!prevEndsSpace) pendingSynth++;
                }
            }
        }
        if (sawString)
        {
            // Positioning arrays (word-depth kerns are the norm) don't
            // synthesize; mirror the runtime rule's shape.
            if (!arrMulti && arrAdjCnt >= 3 && arrDeep * 2 >= arrAdjCnt) { arrSynth = 0; arrSynthAvg = 0; }
            if (arrW < 0) arrW = 0;
            var chars2 = arrG + arrSynth;
            pg.sumW += arrW;
            pg.cnt += chars2;
            if (gs.preRot) pg.rotChars += arrG; else pg.uprightChars += arrG;
            pg.charsPerSize[arrFsDev] = pg.charsPerSize.GetValueOrDefault(arrFsDev) + chars2;
            pg.widthPerSize[arrFsDev] = pg.widthPerSize.GetValueOrDefault(arrFsDev)
                + Math.Min(arrW, 0.6 * arrFsTrue * chars2);
            pg.pureCharsPerSize[arrFsDev] = pg.pureCharsPerSize.GetValueOrDefault(arrFsDev) + arrG;
            pg.pureWidthPerSize[arrFsDev] = pg.pureWidthPerSize.GetValueOrDefault(arrFsDev)
                + Math.Min(arrWPure, 0.6 * arrFsTrue * arrG);
            var avgChars = arrG + arrSynthAvg;
            var arrTcW = (gs.preTc * arrG + gs.preTw * arrSp) * (gs.preRot ? gs.tmScaleX : gs.fsScale) * gs.preHorizScale;
            pg.avgCharsPerSize[arrFsDev] = pg.avgCharsPerSize.GetValueOrDefault(arrFsDev) + avgChars;
            pg.avgWidthPerSize[arrFsDev] = pg.avgWidthPerSize.GetValueOrDefault(arrFsDev)
                + Math.Min(Math.Max(arrWAvg + arrTcW, 0), 0.6 * arrFsTrue * avgChars);
            SeeShowX(gs, pg);
        }
    }
}
