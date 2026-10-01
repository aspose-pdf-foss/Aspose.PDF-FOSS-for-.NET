// The block-dispatch loop in ConvertFromHtml carries state from one block to the
// next: where the flow has reached, which page it is on, and what the previous block
// left pending. Held together here so a block-layout method can declare that it moves
// the flow, rather than reaching for three dozen ambient locals.

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The mutable flow state the block-dispatch loop carries between blocks: the
    /// flow position itself, and everything the previous block left pending.</summary>
    private sealed class HtmlFlowCursor : FlowPosition
    {
        /// <summary>The page a content band belongs to, when it is not the page being written.</summary>
        public Page? contentPage;
        /// <summary>Width available to the flow between the left and right margins.</summary>
        public double contentWidth;
        public bool afterEscapedRule;
        public bool afterFhTable;
        public bool afterRuleDrop;
        public bool bandColClipped;
        /// <summary>How many boxes drawn at a DECLARED width the flow is currently inside.
        /// Their content box is stated, so the page body's own insets no longer apply to
        /// the text in them.</summary>
        public int declaredBoxDepth;
        public double certElementTopY;
        public double floatBottomY;
        public double floatIndentPt;
        /// <summary>The page the left float was placed on: its band ends with that page
        /// (the sheet-typography flow; the legacy flow carries the band over).</summary>
        public Page? floatPage;
        public double floatRightBottomY;
        public double floatRightInsetPt;
        public double floatRightTopY;
        public double fsIndentLive;
        /// <summary>A framed wrapper div's chrome while its tables lay out: the border the content
        /// stands inside, the content width, and whether the div centres its tables.</summary>
        public double frameInset;
        public double frameContentW;
        public bool frameCentred;
        /// <summary>The open frame's border colour (its close marker carries none).</summary>
        public Color? frameCol;
        public int gridRadioAnon;
        public bool lastBreakWasUaSpacer;
        // A table's break tail has stood the UA paragraph margin the paragraph after it owns.
        public bool uaTailMarginSpent;
        public bool lastWasHardBreak;
        public bool lastWasMetricTable;
        public bool lastWasRow;
        public int msoBrokenImgCount;
        public double pendingFloatLabelPt;
        // A right-floated inline run's box: the width the next block's lines leave free at
        // the right edge (UA-serif flow).
        public double pendingFloatRightW;
        public double pendingFloatLabelY;
        public bool pendingTableDrop;
        public bool pendingTableDropBordered;
        public bool pendingTopDrop;
        public bool prevBlockWasText;
        // the previous text block held no ink (a blank spacer paragraph)
        public bool prevBlockWasBlank;
        public double prevFlowFontSize;
        public string? prevFlowFace;
        public double prevFlowLineHeight;
        public double prevFlowMarginBottom;
        public double prevRowMarginBottomPx;
        public double uaPrevMarginBottom;
        public double uaClosingGap;
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
