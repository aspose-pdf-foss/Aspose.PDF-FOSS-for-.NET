using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-block working state of <see cref="RenderBlock"/>: the entry cursor, the
/// spacing and adjacency flags, the box and float geometry and the measured metrics.
/// One instance per block; never shared.</summary>
private sealed class RenderBlockState
{
    public HtmlBlockMetrics metrics = null!;
    // Absolute-bar blocks (DataWorks stayTop) give back the flow y they
    // held at BLOCK ENTRY — before the pre-line drop — so the bar's two
    // spans and the flow after them all share the content top.
    public double yAtBlockEntry;
    public bool tableAfterText;
    public bool breakAfterTable;
    public bool tableAfterSpacer;
    // Margin-collapse partner: the previous FLOW block's bottom margin. Any
    // non-flow block (table, image, input, spacer) between two flow blocks
    // breaks the adjacency, so the pair no longer collapses.
    public double uaPrevMB;
    public bool wasRow;
    public double prevRowBottomPx;
    // An INLINE unitless line-height on a legacy-flow block is a CSS line
    // box: the baseline seats winAscent + half-leading below the box top
    // (the legacy flow's line-height-less drop is a full box less the
    // descent), the block ends at its box bottom plus the element's 1-em
    // default bottom margin, and at the content top the UA body margin
    // (8px) is real space above it — all measured on the
    // 9px/line-height:2 Verdana lead paragraph.
    public bool inlineCssBox;
    public double icbHalfLead;
    // CSS page-break-before:always — start this block on a fresh page (unless we're
    // already at the top of one, so a break as the very first content doesn't add a
    // blank leading page).
    public bool brokeForRule;
    // Cover blocks (a class LineFactor set) lay out on the CSS box model:
    // the block enters at its line-box TOP, and each baseline seats one
    // descent above its line-box bottom (measured within 2pt across the
    // cover fixture's 1x and 3x line factors). The drop is repaid after
    // the lines so the next box starts at this box's bottom.
    public double coverDrop;
    public double availWidth;
    // UA-serif flow: an element's inline border draws around its line
    // boxes, edges OUTSIDE them — the first line box opens one border
    // width below the border top and the box closes one width under
    // the last line (measured: border 90.75, line box 91.5, box bottom
    // 144.75 around a 52.5 line).
    // A painted box draws its own border chrome with its fill,
    // and a border-only DECLARED box strokes its own frame below.
    public bool uaBorderBox;
    // Border-only declared box: the border strokes the declared width ×
    // ExplicitHeight box (rounded by border-radius) hanging at the flow
    // position; the content flows INSIDE it — first line one border
    // width down, text inset one border width right, wrap clamped to
    // the declared content width.
    public bool uaDeclBox;
    // Beside a left-floated image the block still starts at the flow cursor, but
    // its first lines are shortened to the space left of the float's bottom edge.
    public int floatLines;
    public bool besideRightFloat;
    // The width a line level with a float gets. Both edges are absolute: a float
    // clips the BLOCK's own box, and the block's box is not the content box — the
    // certificate's paragraphs are already inset 67.5 each side, so subtracting a
    // float inset measured from the CONTENT edge took the overlap off twice and
    // left half a column. In a DECLARED float box the block's box is that box.
    public double floatBlockL;
    public double floatBlockR;
    public double floatNarrowW;
    // Pad the block's rendered area up to ExplicitHeight so styled
    // fixed-height elements keep their reserved vertical space even
    // when the text inside wraps to fewer lines.
    public double textHeight;
    public double paddingBelow;
}
}
