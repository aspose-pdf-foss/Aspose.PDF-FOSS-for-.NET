using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>A closing span gives back the typography, background and emphasis run it opened.</summary>
    private static void CloseSpanTypography(ParseBlocksState pb)
    {
        // A sized span's close hands the replaced size to the next flush: the
        // span's own line keeps its size, the lines after it return to the base.
        RestoreEmptyWordMailSpan(pb);
        if (pb.uaSpanSizeSaves.Count > 0 && pb.uaSpanSizeSaves.Peek().Depth == pb.spanDepth)
            pb.uaSpanSizeRestorePt = pb.uaSpanSizeSaves.Pop().Fs;
        CloseSpanEmphasisRuns(pb);
        if (pb.isolateSpanOpen.Count > 0 && pb.isolateSpanOpen.Peek().Depth == pb.spanDepth)
            pb.isolateRanges.Add((pb.isolateSpanOpen.Pop().Start, pb.currentText.Length));
        if (pb.blockSpanDepths.Count > 0 && pb.blockSpanDepths.Peek() == pb.spanDepth)
        {
            pb.blockSpanDepths.Pop();
            if (pb.floatRightSpanDepths.Count > 0 && pb.floatRightSpanDepths.Peek() == pb.spanDepth)
            {
                pb.floatRightSpanDepths.Pop();
                pb.floatRightFlush = true;
            }
            Flush(pb, pb.controlBoxes, pb.uaBlockRhythm, pb.articleRhythm, pb.spanPtTypography, false, pb.styleStack.Peek());
            pb.floatRightFlush = false;
        }
        // The ledger's absolute column closes: its text flushes as its
        // own block at the column x. position:absolute anchors at the
        // page margin box — one UA body margin OUTSIDE the flow's
        // content origin (probed: label 96 + margin-left, value
        // 90 + left, on one line).
        if (pb.absSpanLeftPt >= 0)
        {
            var lgTop = pb.styleStack.Peek();
            var lgSavLi = lgTop.LeftIndent;
            var lgSavTi = lgTop.TextInsetPt;
            var lgBefore = pb.blocks.Count;
            lgTop.LeftIndent = Math.Max(0, pb.absSpanLeftPt - UaBodyMarginPt);
            lgTop.TextInsetPt = 0;
            Flush(pb, pb.controlBoxes, pb.uaBlockRhythm, pb.articleRhythm, pb.spanPtTypography, false, lgTop);
            lgTop.LeftIndent = lgSavLi;
            lgTop.TextInsetPt = lgSavTi;
            // An EMPTY column emitted nothing — the label must advance
            // the row itself, or the next row overprints it.
            if (pb.blocks.Count == lgBefore && pb.absSpanLabelIdx >= 0
                && pb.absSpanLabelIdx < pb.blocks.Count)
                pb.blocks[pb.absSpanLabelIdx].NoAdvanceY = false;
            pb.absSpanLeftPt = -1;
            pb.absSpanLabelIdx = -1;
        }
        // The title column closes: its text is its own run that gives the
        // row back — the value that follows seats at the column's edge.
        if (pb.titleColSpanDepth == pb.spanDepth && (pb.openTitleColW > 0 || pb.openTitleColFrac > 0))
        {
            // (a field label ends in its `:after` suffix)
            if (pb.fieldLabelOpen && pb.cv?.profile.fieldLabelSuffix is { Length: > 0 } labelSuffix)
                pb.currentText.Append(labelSuffix);
            Flush(pb, pb.controlBoxes, pb.uaBlockRhythm, pb.articleRhythm, pb.spanPtTypography, false, pb.styleStack.Peek());
            if (pb.blocks.Count > 0 && !pb.blocks[^1].IsHardBreak
                && pb.blocks[^1].Text.Length > 0)
            {
                pb.blocks[^1].NoAdvanceY = true;
                pb.pendingColIndent = pb.openTitleColW;
                pb.pendingColIndentFrac = pb.openTitleColFrac;
                if (pb.fieldLabelOpen)
                {
                    pb.blocks[^1].FieldLabel = true;
                    pb.blocks[^1].FieldColumnFrac = pb.openTitleColFrac;
                    pb.blocks[^1].FontRes = "F2";
                }
            }
            pb.openTitleColW = 0;
            pb.openTitleColFrac = 0;
            pb.titleColSpanDepth = -1;
            pb.fieldLabelOpen = false;
        }
        // Close this span's colour run and restore the frame's ink.
        while (pb.openDecorRuns.Count > 0 && pb.openDecorRuns.Peek().depth == pb.spanDepth)
        {
            var (_, drs, drk, drc) = pb.openDecorRuns.Pop();
            if (pb.currentText.Length > drs)
                pb.rawDecorRuns.Add((drs, pb.currentText.Length, drk, drc));
        }
        if (pb.openColorRuns.Count > 0 && pb.openColorRuns.Peek().depth == pb.spanDepth)
        {
            var (_, crs, crc, crPrev) = pb.openColorRuns.Pop();
            if (pb.currentText.Length > crs)
                pb.rawColorRuns.Add((crs, pb.currentText.Length, crc));
            pb.styleStack.Peek().ForeColor = crPrev;
        }
        if (pb.openSizeRuns.Count > 0 && pb.openSizeRuns.Peek().depth == pb.spanDepth)
        {
            var (_, szs, szPt) = pb.openSizeRuns.Pop();
            if (pb.currentText.Length > szs)
                pb.rawSizeRuns.Add((szs, pb.currentText.Length, szPt));
        }
        if (pb.openFamilyRuns.Count > 0 && pb.openFamilyRuns.Peek().depth == pb.spanDepth)
        {
            var (_, fms, fmFam) = pb.openFamilyRuns.Pop();
            if (pb.currentText.Length > fms)
                pb.rawFamilyRuns.Add((fms, pb.currentText.Length, fmFam));
        }
        if (pb.spanDepth > 0) pb.spanDepth--;
    }
}
