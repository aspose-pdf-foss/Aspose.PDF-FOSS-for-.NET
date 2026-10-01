using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>One cell of a metric-flow table (see <see cref="RenderMetricTable"/>).</summary>
    private sealed partial class MetricCell
    {
        /// <summary>A field-for-field copy: the continuation of a cell split across pages.</summary>
        public MetricCell CloneShallow() => (MetricCell)MemberwiseClone();

        public string Text = "";
        public bool Bold;
        public HorizontalAlignment Align = HorizontalAlignment.Left;
        // Widest `WIDTH:Npx; DISPLAY:inline-table` span in the cell (pt); such a span
        // fixes its column's content width and grows the first line box by 3 pt.
        public double SpanW;
        public bool HasSpan;
        public int ColSpan = 1;          // colspan attribute
        public double WidthPct;          // width="40%" attribute (0 = none)
        public double WidthPx;           // width="300" / "300px" attribute, in pt (0 = none)
        public string? Face;             // <font face=…> / inline font-family (null = flow default)
        public bool WidthPxStyle;        // WidthPx came from a CSS style (content box), not an attribute
        public bool FontTagSized;        // FontSize came from a <font size=N> attribute
        public bool InlineSizedLead;     // a span sized the cell before its first ink (the text stands inside a sized inline)
        public bool TextTrailsGrids;     // the cell's own text all lies AFTER its nested grids (a trailing paragraph)
        // hard breaks after the cell's last nested grid with no ink after them: each is an empty
        // line under the grid (measured on the lab report: `</table><br/></td>` seats the next
        // row one line under the grid)
        public int TrailingBreakLines;
        public int ParaBlocks;           // <p> blocks closed inside the cell (the UA grid seats their block margins)
        public bool Italic;              // inline font-style: italic
        // An <hr> in the cell: the browser's 3-D groove, drawn across the cell's
        // content box and occupying one line box of its own.
        public bool HrRule;
        /// <summary>The rule's own box when it states a height (its height plus a border a side), 0 for the UA groove.</summary>
        public double HrBoxPt;
        /// <summary>What a `width:100%` rule overflows its box by: both borders.</summary>
        public double HrOutsetPt;
        /// <summary>The cell OPENS with an <c>&lt;input type=checkbox&gt;</c>: a 13 px inline box on the
        /// first line's baseline, its widget one point inside it, and the cell's own text set
        /// after the box's margin box. Null = no checkbox; the value is the widget's field name.</summary>
        public string? LeadCheckboxName;
        /// <summary>…and whether that checkbox carries the `checked` attribute.</summary>
        public bool LeadCheckboxChecked;
        /// <summary>…and its own inline `margin-left`, pt (null = the UA 3 pt lead).</summary>
        public double? LeadCheckboxMarginLeftPt;
        /// <summary>Text-like form controls the cell holds, in source order (UA form cells).</summary>
        public List<MetricInputBox>? InputBoxes;
        /// <summary>Absolutely positioned text the cell holds out of its flow, drawn at page offsets.</summary>
        public List<MetricAbsText>? AbsTexts;
        /// <summary>The checkbox took the first line alone: its margin box left the cell's first
        /// word no room beside it, so the text sets from the line below (measured on the
        /// reference: `Anonymous` needs 84.34 in an 83.40 box and drops to its own line).</summary>
        public bool LeadCheckboxOwnLine;
        /// <summary>The cell holds a &lt;fieldset&gt;: the UA frames it with a 0.75 pt #808080 box
        /// around the cell, with or without a legend (probed: fs_bare and fs_legend emit the
        /// identical four strokes).</summary>
        public bool HasFieldset;
        /// <summary>The fieldset's legend text (UA form cells): drawn on the frame's top line,
        /// centred when the sheet centres the fieldset, else at the UA content-left seat.</summary>
        public string? LegendText;
        /// <summary>The cell's text is a remote image's alt shown in its own bordered inline box
        /// (UA form cells): the line grows by the 1 px border above and below.</summary>
        public bool AltBoxed;

        // `<b><p>…</p></b>`: an emphasis inline cannot contain a block, so the
        // parser closes it before the block and reopens it after — leaving an
        // empty inline on EACH side, each with a line box of its own.
        public bool OrphanInlineBoxes;
        public Color? Fore;              // <font color=…> ink
        public Color? Bg;                // bgcolor attribute / background-color style
        // An <a href> wrapping the cell's content: the cell text draws as the
        // link (UA blue + underline when no sheet styles anchors) and emits a
        // link annotation over its line box.
        public string? LinkUrl;
        // An inline `border-bottom: … double` (the financial statement's sum
        // rules) draws the pair of thin lines instead of one stroke.
        public bool BorderBottomDouble;
        public double? FontSize;         // tr/td inline or class font-size (pt)
        // Mixed-size inline spans on one cell line ('23 May' 12pt + '(this
        // Thursday)' 9pt): the per-size text segments, drawn sequentially on
        // the shared baseline. Null = uniform size (the normal case).
        public List<(string Text, double Size)>? SizedRuns;
        /// <summary>UA cells: the cell's runs (position in the collapsed text, style) when its ink is
        /// in more than one style - a cell wholly in one style keeps its flags instead.</summary>
        public List<(int pos, MetricRunStyle st)>? Runs;
        public bool VAlignTop;           // valign='top' attribute
        public bool NoWrap;              // nowrap attribute / white-space:nowrap
        // white-space: pre-wrap (class or inline): the source line breaks are hard breaks and
        // runs of spaces are kept; lines still wrap at the column (probed on the test report:
        // a blank source line is a blank line box, `---->    <----` keeps its spaces)
        public bool PreWrap;
        public List<string>? SubTables;  // nested tables rendered as grids in this cell
        public List<string[]?>? SubTableHostClasses;  // per sub-table, the classes of the divs open round it
        // the cell's own typography as it stood when the cell closed, kept for its nested grids
        // when the cell then takes its first ink's (a span's 14 pt over a 12 pt cell dresses the
        // cell's text, not the grid after it - measured on the lab report's result grids)
        public double? GridFontSize;
        public string? GridFace;
        public bool GridTypoSaved;
        // Interleaved cell content, kept in SOURCE order when a nested grid
        // precedes text ink: text runs (bold per run) and grids draw as one
        // flow. Null = the calibrated stacked draw (text, then grids).
        public List<(string? TableHtml, string Text, bool Bold)>? Flow;
        public double BorderRightW;      // style border-right width, in pt (0 = none)
        public Color BorderRightCol = Color.FromArgb(0, 0, 0);
        public bool VAlignBottom;        // vertical-align: bottom (class skin)
        public double PadLeft = -1;      // padding-left override, pt (-1 = table default)
        public double MinWidthPt;        // a class `min-width` floor on the cell's content box, pt (0 = none)
        public double PadRight = -1;     // padding-right override, pt (-1 = table default)
        public double BorderLeftW;       // class border-left width, pt (0 = none)
        public double BorderBottomW;     // class border-bottom width, pt (0 = none)
        public double BorderTopW;        // class border-top width, pt (0 = none)
        public Color BorderTopCol = Color.FromArgb(0, 0, 0);
        public Color BorderBottomCol = Color.FromArgb(0, 0, 0);
        public Color BorderLeftCol = Color.FromArgb(0, 0, 0);
        public bool BorderTopDashed;     // border-top: dashed (the tear-off rule)
        public double HeightPt;          // class height, pt (0 = auto) — paces the row exactly
        // td style line-height, pt (0 = the face box) — fixes every line
        // box the cell holds, wrapped continuations included
        public double LineHeightPt;
        public double HeightStylePt;     // an INLINE style height, which bands the row for centring
        public bool VAlignMiddle;        // the cell ASKED for middle, rather than defaulting to it
        public bool CellInlineTypo;      // the CELL stated its own size or face inline
        public bool FontInline;          // FontSize came from the cell's own style attribute
        public bool RowInlineTypo;       // the ROW declared this cell's typography inline
        public bool FontFromClass;       // FontSize came from a CLASS skin (row is content-paced)
        public List<string>? ClassNames; // td class attribute values
        public string Tag = "td";        // the cell's own element name (td/th)
        // Div-stacked cell content (the boleto's .t/.c ladders): each div is one
        // styled line whose class height paces its band
        public List<MetricDivSeg>? DivSegs;
        public double ImgHPt;            // declared image box height in the cell, pt
        public double ImgWPt;            // declared image box width in the cell, pt
        // A data-URI PNG inside an absolutely positioned div (left:N%): drawn
        // at natural size, offset from the cell content left by the fraction.
        public byte[]? AbsPng;
        public double AbsPngLeftFrac;
        public bool InkInFloat;          // some text of the cell sits in a float:left/right span
        public bool InkOutsideFloat;     // some text of the cell sits in the cell's own line
        public bool AltTextOnly;         // cell text is a broken image's alt — wraps in ImgWPt
        public bool ImgPlaceholder;      // a missing image without alt: the 34 pt broken-image frame
        public bool ImgRemoteBroken;     // …a REMOTE one: its bevel keeps a 6 px gutter on either side
        public bool ImgAfterText;        // …a sized one that follows the line's text: drawn inline after it
        public double PadTopPt;          // td style padding-top (newsletter and UA cells)
        public double PadBottomPt;       // td style padding-bottom (UA cells)
        public string[] Lines = [];      // wrapped at layout time
        public double ContentH;          // Σ line boxes
        public bool Phantom;             // colspan filler / RTL pad slot — never draws
        public int RowSpan = 1;          // rowspan attr — content overlays rows below
        public double SpanBandPt;        // the band the rows it spans make (UA grids): its content seats in it
        public bool RowSpanCovered;      // phantom slot under a row-spanning cell: no top rule, no ink
        public double ClassWidthPct;     // class width % — pins only when over-full
        public byte[]? ImgBytes;         // the cell's raster (data URI or a loaded file) — draws ABOVE its segments
        public bool WidthSetterCell;     // inline WIDTH+MIN-WIDTH pair (a report grid's sizing row)
        public bool WidthZero;           // an inline `width: 0mm` (a report grid's zero-width spacer column)
    }

    /// <summary>One stacked div inside a metric cell (see MetricCell.DivSegs).</summary>
    private sealed partial class MetricDivSeg
    {
        public string Text = "";
        public double? FontSize;
        public string? Face;
        public bool Bold;
        public Color? Fore;
        public double LineBoxPt;         // class height (min band height, 0 = auto)
        public double PadLeft;           // class padding-left
        public bool BorderBottom;        // .BB underline band
        public double BorderBottomPt;    // its rule width: the band's box grows by it (content-box)
        // Paragraph segments (the newsletter cells): the UA 1.12 em block
        // margins, collapsed max-wise between adjacent segments.
        public double MarginTopPt;
        public double MarginBottomPt;
        // the paragraph's class authored its margins (`margin: 0pt …`) — the
        // UA block margins yield to them at the segment close
        public bool MarginsExplicit;
        // …or its inline style stated one side (`margin-top: 0px`): that side replaces the UA
        // margin at the close, the other keeps it
        public double? MarginTopStatedPt;
        public double? MarginBottomStatedPt;
        // a block with neither ink nor a line break (`<p></p>`): it holds no line box, and its
        // margins still collapse with its neighbours' (probed: one 1.12 em gap, not two)
        public bool EmptyBlock;
        // the block closed a <p> or a heading: a quirks cell drops such a FIRST block's top margin
        // and such a LAST block's bottom margin (a div keeps both - probed on the invoice email:
        // the h1 at the cell top, the 1em-margined div 12 under it, the h2 after a float 10 under)
        public bool IsParagraph;
        public bool IsHeading;
        // the block is the cell's DIRECT child (no inline wrapper open round it): only such a
        // paragraph's margins are the quirks cell's to drop - a span-wrapped one keeps them
        public bool DirectChild;
        // A div stating its own box inside a UA block cell: the band OPENS the box (its outer
        // width, its paddings, its background) and every band up to the closing one stands inside
        // it, left-aligned; a right-aligned cell seats the box at its right edge (probed on the
        // invoice email: a `width: 260px; padding: 1.5em 20px` div in a 260 px right-aligned cell
        // is a 225 pt box at the cell's right edge, its heading 27 pt under the box top).
        public bool BoxOpen;
        public bool BoxClose;
        public double BoxWidthPt;        // outer: width + horizontal paddings
        public double BoxPadTopPt, BoxPadRightPt, BoxPadBottomPt, BoxPadLeftPt;
        // class background-color: the band fills the cell's content width
        // (the green bar — measured 97.5..497.5 × its class height)
        public Color? Bg;
        // UA block cells: the block's own alignment, a right float (drawn at the band top,
        // taking no flow space), and each hard line's own bold and size
        public bool AlignRight;
        public bool AlignCenter;         // the block's own `text-align: center` (or a `<p align=center>`)
        public double PadRight;          // the block's own right inset (margin-right / padding-right), the enclosing blocks' included
        public bool Italic;              // the block's ink is italic (an open <i>, or `font-style: italic`)
        public List<(int pos, MetricRunStyle st)>? Runs;   // the block's runs when its ink is in more than one style
        public bool FloatRight;
        public List<(bool Bold, double? Fs)> LineTypo = new();
        /// <summary>The classes the block's own tag carries (a div's class attribute), for the sheet's descendant rules on the blocks it holds.</summary>
        public string[]? Classes;
        /// <summary>The class line box is the block's EXACT content height (a `height:` on the block): its lines overflow it.</summary>
        public bool LineBoxExact;
        /// <summary>The part of the top margin that is the block's own PADDING: kept where a quirks cell drops the margin.</summary>
        public double PadTopPt;
        /// <summary>A nested grid standing among the block's lines (its index in the table's
        /// nested-table list), drawn in source order; -1 = a text band.</summary>
        public int NestedTable = -1;
        public MetricDivSeg CloneShallow() => (MetricDivSeg)MemberwiseClone();
    }
}
