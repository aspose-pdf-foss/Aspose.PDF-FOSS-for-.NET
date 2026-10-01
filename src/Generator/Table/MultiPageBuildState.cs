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
private sealed class MultiPageBuildState
{
    // The MEDIA frame's height (see Page.LayoutFrameHeight): a /Rotate page's
    // table seats against the media edges and paints upright in them.
    public double pageHeight;
    public double marginLeft;
    public double marginTop;
    // A declared Left PINS the table at that page x — it is an absolute
    // coordinate, not an inset from the content margin — and the pin beats the
    // table's own Alignment (a centred table pinned at 440 lands at 440).
    // With no pin the table hangs off the
    // flow's left content offset as before.
    public bool pinned;
    public double tableX;
    // The table's own box border draws OUTSIDE the column block, the same way an
    // explicitly-assigned cell border does: the stroke's outer edge sits on the
    // table's footprint edge and the columns start one border-width in. A 3 pt
    // box border therefore occupies 3 pt of page space on each side and the first
    // cell's text moves 3 pt right of the table origin.
    public double tableBorderWidth;
    // A declared Top PINS the table's own top at that page y — an absolute
    // coordinate measured from the page's TOP edge, not an inset from the content
    // margin — and the pin beats the flow cursor exactly as Left beats the content
    // offset (a pin at 50 on a 759 pt page draws its band
    // from 709 down).
    public double tableTopY;
    // Overflow pages restart the table below the page's top margin (the flow's body
    // band), not at the bare page top — matches the generator's spill layout. The
    // table's own Margin.Top still applies when no page margin is supplied.
    // …and a table PINNED by Top resumes on a spill page at the HIGHER of its pin
    // and that margin: a pin ABOVE the margin carries over (a table pinned at 10
    // and its page 2 opens its repeated header at the same 831.5 page 1 started
    // from), while a pin BELOW it does not (a table pinned at 400: its pages 2 and 3
    // both start at the ordinary 770).
    public double bandTop;
    public double fullPageTopY;
    public double pageBottom;
    // A page whose DECLARED bottom margin is tighter than the flow's default
    // 36 pt overflow inset fills its rows down to that margin (a 0.375 pt-margin
    // report sheet packs 82 rows a page); pages with ordinary margins keep the
    // legacy inset fill.
    public bool tightMarginFill;
    // A FOOT-STARTED main-flow table (pushed into the last row-slot above the
    // bottom content margin by its own top margin) keeps its rows above that
    // margin — its rows break to the next page at that bound.
    // An ordinarily-flowing table keeps the overflow inset, filling rows into
    // the margin band like the legacy layout. Header/footer and conversion
    // builds keep the caller's bound. The flag is resolved after the row plans
    // exist (the first row's height defines the foot band); see below.
    // A main-flow build is always a candidate — the flow passes the page's real
    // bottom margin, and the foot band is measured against that margin (a table
    // whose top sits exactly one row above it places that row on the page).
    public bool footStartCandidate;
    public double contentBottomMargin;
    // RowSpan grid placement — only for the plain (identity-mapped) layout; the
    // column-chunk slicing path keeps the legacy cell-index mapping.
    public bool identityMap;
    public (int[][] gridToCell, int[][] effRowSpan, int gridCols, List<Aspose.Pdf.Table.SpanBlock> blocks)? grid;
    public List<SpanBlock>? spanBlocks;
    // Bundle map for keep-together pagination: rows chained by any rowspan form a
    // bundle. link[r] == true → rows r and r+1 belong to the same bundle.
    public bool[]? bundleLink;
    // Pre-compute per-row content plans. Each plan carries the cells' wrapped
    // lines, uniform line height, vertical padding and the min (one-line) chunk
    // height — the paginator uses these to chop a row across pages when it
    // cannot fit in the remaining vertical space.
    public List<Aspose.Pdf.Table.RowPlan> rowPlans = null!;
    // Space a sizeless vector image may fill: from the table top down to the
    // page's bottom CONTENT margin (72pt default), not the flow's tighter
    // overflow inset — same boundary the row-span paginator uses. Header/footer
    // builds (negative margin) and HTML conversions keep their own bound.
    public double svgFillBottom;
    public double svgFillHeight;
    // Row.IsInNewPage is BOTH an input and an output: a caller sets it to demand
    // that the row opens a page, and the layout then overwrites it to report where
    // the row actually landed. Snapshot the demand before the report clobbers it.
    public bool[] rowOpensPage = null!;
    // A row-spanning cell distributes its height demand EVENLY across its
    // spanned rows: share = H / n where H = the cell's effective top+bottom
    // padding plus every wrapped line's font size plus each fragment's own
    // top margin. Each spanned row then independently takes
    // max(naturalHeight, share) — non-iterative, so a row whose natural
    // height exceeds the share does NOT shrink the share of the others.
    public double[]? shareFloor;
    // Foot-start resolution: the table is foot-started when its top sits at or
    // below one first-row height above the bottom content margin.
    public bool footStart;
    // Walk rows, emit slices, spill to new pages as needed.
    public List<byte[]> result = null!;
    public List<Aspose.Pdf.Table.RowSlice> slices = null!;
    // Cellspacing rides OUTSIDE each row's box: the first row starts one gap below
    // the table top and every following row one gap below the previous box, so the
    // cell content and its chrome keep the box the row plan measured.
    public double rowGap;
    public double currentY;
    public double pageStartY;
    // Cell hyperlinks are emitted as link annotations on the first page only
    // (overflow pages aren't materialised here). firstPageDone flips once the
    // first page's content is built.
    public bool firstPageDone;
    // Repeating-rows: build slices for the first N rows once, then re-emit
    // them at the top of every overflow page (Y rebased per page).
    public int repeatCount;
    // The footer band: the LAST N rows, drawn under the last row every page
    // holds rather than once at the end of the table. bodyRowCount is what the
    // page-filling loop walks, and footerReserve is the height held back from
    // every page's bottom so the band always has its room.
    public int bodyRowCount;
    public double footerReserve;
    // A carried-forward band releases its reserve on the page that ends the
    // table: once the rows left fit the page without it, that page is the last
    // one and the band neither costs it room nor draws on it.
    public bool footerReleased;
    // IsBroken = false: the table is never carried onto a second page. What does not
    // fit above the bottom content margin is DROPPED — 71
    // one-line rows produce a single page carrying rows 1..68 and a table border that
    // closes on the last one. An explicit <see cref="Broken"/> mode supersedes the
    // legacy flag: a grid declares BOTH `IsBroken = false` and
    // `TableBroken.IsInNextPage`, and its 43 rows still run over two pages.
    public bool truncatedUnbroken;
    // The build inputs, captured from the method parameters.
    public Page page = null!;
    public double startY;
    public double bottomMargin;
    public double[] colWidths = null!;
    public int[] cellMap = null!;
    public string fontName = null!;
    public double topMargin;
    public Table.RowPlan plan = null!;
    public int lineIdx;
    // RowSpan keep-together: rows chained by rowspans form a
    // bundle. When the bundle doesn't fit the space left on this page, a small
    // bundle (≤ 8 rows) moves to the next page whole; a larger one may split but
    // only if at least 4 of its rows stay on this page — else it moves too.
    public bool forceBreak;
    // A page was broken for this row and NOTHING has been placed since. The
    // loop must never break twice without progress: a fresh page whose
    // repeating header leaves no room for even one line would otherwise break
    // again, and again, appending a page's content each time.
    public bool brokeWithoutProgress;
    // True when this row already forced a page break without emitting anything:
    // the next iteration must make progress (split the row) even when a repeated
    // header sits below the page top — otherwise an image-bearing row taller than
    // the space under the header would force page breaks forever.
    public bool brokePageForRow;
    public double usable;
    public int linesFit;
    // The row stays on the page under its glyphs, its box closed at the page's bottom
    // (see Table.RowsCloseUnderTheirGlyphs).
    public bool closesAtBottom;
    // The slice cuts a row that continues overleaf and closes under the glyphs of its last line
    // (see Table.RowsCloseUnderTheirGlyphs).
    public bool closesUnderGlyphs;
    // The line controls cut the row short of what fits: its next slice opens the next page.
    public bool breakAfterSlice;
    public bool atFreshPage;
    // Keep-together rules below may only DEFER a row that could actually land
    // somewhere else. A row taller than an entire empty page has nowhere to go:
    // deferring it buys a blank page and asks the same question again on the
    // next one, so such a row must split wherever it is.
    public double rowFullH;
    public bool fitsAnEmptyPage;
    public int remaining;
    public int take;
    // A FixedRowHeight row is HARD-sized: it occupies exactly its fixed
    // height and CLIPS the wrapped lines that don't fit inside it (a
    // multi-line wrapped header key shows only the lines its fixed
    // height can hold), never splitting across pages.
    public double fixedH;
    // INNER-DRIVEN PAGE BREAK: when a nested grid crosses this row's split
    // boundary, the grid decides where the page really ends. It is built
    // NOW (once — the draw hook consumes the cached slices) against the
    // real page bounds, and this slice sizes to the height the grid
    // consumed on this page. The line-quanta allotment would otherwise run
    // to the page bottom and paint over the band strip that must stay
    // bare below the break.
    public double nestedDrivenH;
    public double sliceContentH;
    // A generator row carrying a picture splits with the picture ATOMIC: the
    // slice takes the text lines that fit, defers any image whose full height
    // does not, and is only as tall as what it actually placed: a split row
    // leaves its two 10 pt captions at the foot of page 1 (a 20 pt band) and
    // opens page 2 with the 100 pt map alone.
    public bool imageDeferred;
    // Spacer rows (content-less or whitespace-only) reserve just their line with
    // no cell padding, matching the generator; content rows keep padding.
    // Form-grid cells are CSS boxes: an &nbsp;-only row still carries its
    // borders and cellpadding (a 36pt spacer row is its 58px
    // line box PLUS the 3pt border+padding band).
    public double sliceH;
    // Honour a row's minimum height as a floor for content rows too (not just
    // empty ones), but only when the whole row fits in this slice.
    public bool wholeRowInSlice;
    // Under UA cell boxes the minimum is a CONTENT floor (the CSS box a
    // fixed-height child claims), so the cell's own padding still rides on
    // top of it; the legacy floor is the whole row height.
    public double minFloor;
}
}
