// The block-dispatch loop in ConvertFromHtml carries state from one block to the
// next: where the flow has reached, which page it is on, and what the previous block
// left pending. Held together here so a block-layout method can declare that it moves
// the flow, rather than reaching for three dozen ambient locals.

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The mutable flow state the block-dispatch loop carries between blocks.</summary>
    private sealed class HtmlFlowCursor
    {
        /// <summary>The page being written to; a page break replaces it.</summary>
        public Page page = null!;
        /// <summary>The page a content band belongs to, when it is not the page being written.</summary>
        public Page? contentPage;
        /// <summary>The flow cursor: the baseline the next block starts from, in points from the page bottom.</summary>
        public double y;
        /// <summary>Width available to the flow between the left and right margins.</summary>
        public double contentWidth;
        public bool afterEscapedRule;
        public bool afterFhTable;
        public bool afterRuleDrop;
        public bool bandColClipped;
        public double certElementTopY;
        public double floatBottomY;
        public double floatIndentPt;
        public double floatRightBottomY;
        public double floatRightInsetPt;
        public double floatRightTopY;
        public double fsIndentLive;
        public int gridRadioAnon;
        public bool lastBreakWasUaSpacer;
        public bool lastWasHardBreak;
        public bool lastWasMetricTable;
        public bool lastWasRow;
        public int msoBrokenImgCount;
        public double pendingFloatLabelPt;
        public double pendingFloatLabelY;
        public bool pendingTableDrop;
        public bool pendingTableDropBordered;
        public bool pendingTopDrop;
        public bool prevBlockWasText;
        public double prevFlowFontSize;
        public double prevFlowLineHeight;
        public double prevFlowMarginBottom;
        public double prevRowMarginBottomPx;
        public double uaPrevMarginBottom;
        public bool uaTopMarginPending;
        public bool wikiAfterButtons;
        public bool wikiPrevListItem;
        /// <summary>The shared broken-image placeholder icon, registered on first use
        /// and reused for every later placeholder in the document.</summary>
        public Core.PdfIndirectRef? flowIconRef;
        /// <summary>A custom font was embedded, so unused faces are pruned at the end.</summary>
        public bool usedCustomFont;
    }
}
