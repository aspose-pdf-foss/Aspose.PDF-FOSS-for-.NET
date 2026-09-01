using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public partial class Table
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class RowPlanColumnState
{
    public int origIdx;
    public Cell cell = null!;
    public MarginInfo? padding;
    public double dp;
    // Vertical padding defaults to ZERO — row pitch is
    // exactly lineCount×fontSize for borderless cells; a cell border
    // adds its stroke width above and below (a 0.5 pt bordered 10 pt
    // row pitches at 11 pt). Explicit padding is
    // honoured verbatim.
    public BorderInfo? vb;
    public double borderV;
    public double padV;
    public double padLeft;
    public double padRight;
    public int span;
    public double cellWidth;
    // The cell-border pitch is box, not text box: a spanned cell keeps the
    // interior rules' share (probed: a ColSpan-2 cell on 102 pt columns
    // fills/wraps in 202).
    public double availWidth;
    public Aspose.Pdf.Text.TextState? textState;
    public double defaultFontSize;
    public HorizontalAlignment cellAlign;
    public List<Aspose.Pdf.Table.CellLine> lines = null!;
    public bool cellNeedsInline;
    public bool cellInlineExact;
    // A cell whose ONLY reason to go inline is a Graph keeps the plain cell's
    // line model: a Graph is pure overlay (it occupies just the box it
    // declares — zero, here) and must not re-price the cell's text lines at
    // their segments' own touched sizes. Probed 2026-08-26: a Graph(0,0) beside
    // a cell's text leaves that text exactly where a graph-less cell puts it.
    public bool cellInlineFromGraphOnly;
    // Generator dialect: a text paragraph's own vertical margins are silent
    // spacer boxes in the cell's stack — its Margin.Top above its first line,
    // its Margin.Bottom below its last (measured: a 12.5 pt "Weight"
    // fragment carrying (top 15, bottom 10) seats 25 pt below the cell's content
    // top and starts the following HtmlFragment 10 pt under its box).
    public double genPendingBottom;
    // Each paragraph's declared leading is stamped onto the lines it produced.
    // The stamp runs at the TOP of the next pass (and once after the loop)
    // because the body leaves through a dozen different `continue`s.
    public int paraLineStart;
    public double paraLeading;
    // CSS line-box mode: a cell whose lines carry mixed font sizes (styled HTML
    // paragraphs) stacks each line as its own box (1.2 × em) with the baseline at
    // ascent + half-leading — the uniform LineHeight grid can't express this.
    public bool cssMode;
    public bool preBox;
    // Whether this cell's lines carry DIFFERENT font sizes — a generator cell
    // that does stacks each line at its own size rather than on the row's
    // uniform grid (see cellExact below and the draw's per-line walk).
    public bool cellMixedSizes;
    // A STANDALONE BADGE cell — every line text-less and carrying only inline
    // boxes (the risks pill's traffic-light circle) — owns its box height.
    // Folding it into the row's SHARED content height and then stacking a
    // sibling cell's padding on top sizes the row at box + other-cell padding
    // (24.5 pt instead of the correct 19.2, where the circle sits in its own
    // 17.25 pt cell beside a 19.2 pt padded button). It sizes as its own stack
    // instead, and the row takes the max over cells of (padding + content).
    public bool badgeOnlyCell;
    // Generator dialect: a cell whose stack holds boxed lines (margin spacers,
    // HTML-engine boxes) is an EXACT stack — every text line occupies its
    // font size (K = 1, the generator's pitch) with the baseline at the
    // generator seat (em less the face's descent), image reserves their
    // image's height; the row is the tallest cell's padding + stack.
    public bool genExactStack;
    public double genDescEm;
    // A styled-paragraph cell (a block-aligned fragment with its own
    // stylesheet) drops leading blank spacers — the empty companion
    // fragment contributes no line to it.
    public bool hasStyledPara;
    // The cell's tight line is measured with the ROW's ruler, leading included:
    // the row padding is derived by subtracting the row's content height from
    // the tallest cell's total, and a tight line that left the leading out made
    // the subtraction swallow it -- a 9 pt line with 4.5 pt leading reported a
    // 5.7 pt row padding where 10.2 pt was declared.
    public double cellTight;
    // A multi-line control cell (text line(s) stacked above a checkbox)
    // sizes as the EXACT stack of its parts — each text line at its own
    // font size and the box at its box height (a " " + 8.5pt checkbox
    // cell is 10 + 8.5 = 18.5, not two 10pt grid lines).
    public double cellExact;
    public double cellOwnStack;
    public bool cellHasBox;
    public bool cellHasReserve;
}
}
