using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>A cell built from div segments measures each segment's own wrapped lines, and the widest of them sets the cell's effective width; true means the cell is fully measured.</summary>
    private static bool MeasureMetricCellDivSegments(MetricTableState mt, MetricCell mc, ref double effW)
    {
        if (mc.DivSegs is not { Count: > 0 } dsegs) return false;
        mc.Lines = [];
        // an overflowing image GROWS the cell's content box — the
        // paragraphs below it wrap at the image's width (measured:
        // the report paragraphs break at the 612 pt photo, not the
        // 504 pt column)
        if (mc.ImgBytes is not null && mc.ImgWPt > effW)
            effW = mc.ImgWPt;
        var m = new DivSegMeasure();
        foreach (var sg in dsegs)
        {
            if (mt.mps.uaBlockCells) MeasureUaBlockSegment(mt, mc, sg, effW, m);
            else MeasureDivTextSegment(mt, sg, effW, m);
        }
        // (a quirks cell drops the last paragraph's bottom margin, as it drops the first's top)
        // (...and a plain cell of a standards document keeps its last block's bottom margin too:
        //  the e-mail cards' `<td><h2>` heads stand their 0.75 em over the card's foot)
        if ((mt.mps.uaBlockCells || (mt.stdSerif && !QuirksDropsCellBlockMargins)) && !(QuirksDropsCellBlockMargins && m.LastParagraph)) m.Height += m.PrevMarginBottom;
        // an intrinsic-aspect JPEG stacks ABOVE the segments — the
        // reserved-box images centre in the band instead
        mc.ContentH = mc.ImgBytes is not null
            ? m.Height + mc.ImgHPt : Math.Max(m.Height, mc.ImgHPt);
        // …and the cell's own block paddings are box space round the bands, as round
        // a text cell's lines (measured: the report's 2 pt padded paragraph cells
        // band 64 for five 12 pt lines, its 3/2 pt padded section label 14).
        mc.ContentH += mc.PadTopPt + mc.PadBottomPt;
        return true;
    }

    /// <summary>The running measure of a div-segment cell: its stacked height, the bottom
    /// margin left open for the next block to collapse with, whether the next block is the
    /// cell's first, the open div box's content width and closing space, and whether the last
    /// block measured was a direct paragraph.</summary>
    private sealed class DivSegMeasure
    {
        public double Height;
        public double PrevMarginBottom;
        public bool FirstBlock = true;
        // the open div box: its blocks wrap inside it, its bottom padding and margin close it
        public double BoxContentW;
        public double BoxPadBottom;
        public double BoxMarginBottom;
        public bool LastParagraph;
    }

    /// <summary>One segment of a UA block cell: a box opening or closing, a nested grid, an
    /// empty block, or a block band whose lines wrap inside the open box.</summary>
    private static void MeasureUaBlockSegment(MetricTableState mt, MetricCell mc,
        MetricDivSeg sg, double effW, DivSegMeasure m)
    {
        if (sg.FloatRight) { m.FirstBlock = false; return; }
        if (sg.BoxOpen)
        {
            m.Height += (UaFirstBlockMarginDropped(m.FirstBlock, mt.p, sg) ? sg.PadTopPt : Math.Max(sg.MarginTopPt, m.PrevMarginBottom)) + sg.BoxPadTopPt;
            m.PrevMarginBottom = 0; m.FirstBlock = false;
            m.BoxContentW = sg.BoxWidthPt - sg.BoxPadLeftPt - sg.BoxPadRightPt;
            m.BoxPadBottom = sg.BoxPadBottomPt; m.BoxMarginBottom = sg.MarginBottomPt;
            if (sg.EmptyBlock) return;
    }
    else if (sg.BoxClose)
    {
        m.Height += m.PrevMarginBottom + m.BoxPadBottom;
        m.PrevMarginBottom = m.BoxMarginBottom; m.BoxContentW = 0; m.LastParagraph = false;
        return;
    }
    m.LastParagraph = (sg.IsParagraph || sg.IsHeading) && sg.DirectChild;
    if (sg.NestedTable >= 0)
    {
        // a nested grid keeps the margin of the block above it (collapsed with its
        // own top margin) and stands its own bottom margin under it
        var (gridMt, gridMb) = NestedGridMarginsPt(mt.mps.nestedTables![sg.NestedTable]);
        m.Height += Math.Max(m.PrevMarginBottom, gridMt) + MetricGridHeightPt(mt, mt.mps.nestedTables![sg.NestedTable], mc, effW);
        m.PrevMarginBottom = gridMb;
        m.FirstBlock = false;
    }
    if (sg.EmptyBlock)
    {
        var above = m.FirstBlock && mt.p > 0 ? m.PrevMarginBottom : Math.Max(m.PrevMarginBottom, sg.MarginTopPt);
        m.PrevMarginBottom = Math.Max(above, sg.MarginBottomPt);
        m.FirstBlock = false;
    }
    m.Height += (UaFirstBlockMarginDropped(m.FirstBlock, mt.p, sg) ? sg.PadTopPt : Math.Max(sg.MarginTopPt, m.PrevMarginBottom))
        + UaBlockBandHeight(mt, mc, sg, (m.BoxContentW > 0 ? m.BoxContentW : effW) - sg.PadLeft);
    m.PrevMarginBottom = sg.MarginBottomPt;
        m.FirstBlock = false;
    }

    /// <summary>One text segment of a div-segment cell: its own line height, its wrapped line
    /// count at the cell width, and the UA block margins it carries — adjacent margins
    /// collapsing to the larger one (a negative stated margin adds to the positive one, as CSS
    /// has it).</summary>
    private static void MeasureDivTextSegment(MetricTableState mt, MetricDivSeg sg,
        double effW, DivSegMeasure m)
    {
        var sgFs = sg.FontSize ?? mt.mps.fontSize;
        // newsletter segments pace on the cell line model (hhea);
        // the calibrated div-seg dialects keep their win metrics
        double sgLineH;
        if (mt.paragraphCells)
            sgLineH = CellLineOf(mt.mps, mt.stdSerif, mt.wrapperStacks, mt.hheaSum, mt.face, mt.fm, new MetricCell
            { Face = sg.Face, Bold = sg.Bold, FontSize = sg.FontSize }, sgFs);
        else
        {
            var sgFmv = sg.Face is { } sgf ? (WinMetricsFor(sgf) ?? mt.fm) : mt.fm;
            var sgSum = sgFmv.sum <= 1.0 ? 1.2 : sgFmv.sum;
            sgLineH = MetricLineHeight(sgFs, sgSum);
        }
        var sgFaceN = sg.Face is { } f2
            ? f2 + (sg.Bold ? " Bold" : "")
            : (sg.Bold ? mt.boldFace : mt.face);
        var nLines = sg.Text.Length == 0 ? 0
            : sg.Runs is not null
                ? MeasuredWordWrapStyled(sg.Text, effW - sg.PadLeft - sg.PadRight, mt.face, mt.boldFace,
                    new MetricCell { Face = sg.Face, Bold = sg.Bold, FontSize = sg.FontSize, Italic = sg.Italic }, sgFs, sg.Runs).Length
                : MeasuredWordWrap(sg.Text, effW - sg.PadLeft - sg.PadRight, sgFaceN, sgFs).Length;
        // paragraph segments carry the UA block margins, adjacent margins collapsing to
        // the larger one (a negative stated margin adds to the positive one, as CSS has it)
        m.Height += (m.FirstBlock && QuirksDropsCellBlockMargins && sg.DirectChild && (sg.IsParagraph || sg.IsHeading) ? sg.PadTopPt
                : sg.MarginTopPt < 0 ? sg.MarginTopPt + m.PrevMarginBottom : Math.Max(sg.MarginTopPt, m.PrevMarginBottom))
              + (sg.LineBoxExact ? sg.LineBoxPt : Math.Max(sg.LineBoxPt, nLines * sgLineH)) + sg.BorderBottomPt;
        m.PrevMarginBottom = sg.MarginBottomPt;
        m.FirstBlock = false;
    }
}