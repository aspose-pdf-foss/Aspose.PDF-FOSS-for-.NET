using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>One cell of a metric-flow table (see <see cref="RenderMetricTable"/>).</summary>
    private sealed partial class MetricCell
    {
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
        public bool Italic;              // inline font-style: italic
        // An <hr> in the cell: the browser's 3-D groove, drawn across the cell's
        // content box and occupying one line box of its own.
        public bool HrRule;

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
        public bool VAlignTop;           // valign='top' attribute
        public bool NoWrap;              // nowrap attribute / white-space:nowrap
        public List<string>? SubTables;  // nested tables rendered as grids in this cell
        // Interleaved cell content, kept in SOURCE order when a nested grid
        // precedes text ink: text runs (bold per run) and grids draw as one
        // flow. Null = the calibrated stacked draw (text, then grids).
        public List<(string? TableHtml, string Text, bool Bold)>? Flow;
        public double BorderRightW;      // style border-right width, in pt (0 = none)
        public Color BorderRightCol = Color.FromArgb(0, 0, 0);
        public bool VAlignBottom;        // vertical-align: bottom (class skin)
        public double PadLeft = -1;      // padding-left override, pt (-1 = table default)
        public double BorderLeftW;       // class border-left width, pt (0 = none)
        public double BorderBottomW;     // class border-bottom width, pt (0 = none)
        public double BorderTopW;        // class border-top width, pt (0 = none)
        public bool BorderTopDashed;     // border-top: dashed (the tear-off rule)
        public double HeightPt;          // class height, pt (0 = auto) — paces the row exactly
        public bool FontFromClass;       // FontSize came from a CLASS skin (row is content-paced)
        public List<string>? ClassNames; // td class attribute values
        // Div-stacked cell content (the boleto's .t/.c ladders): each div is one
        // styled line whose class height paces its band
        public List<MetricDivSeg>? DivSegs;
        public double ImgHPt;            // declared image box height in the cell, pt
        public double ImgWPt;            // declared image box width in the cell, pt
        // A data-URI PNG inside an absolutely positioned div (left:N%): drawn
        // at natural size, offset from the cell content left by the fraction.
        public byte[]? AbsPng;
        public double AbsPngLeftFrac;
        public bool AltTextOnly;         // cell text is a broken image's alt — wraps in ImgWPt
        public double PadTopPt;          // td style padding-top (newsletter cells)
        public string[] Lines = [];      // wrapped at layout time
        public double ContentH;          // Σ line boxes
        public bool Phantom;             // colspan filler / RTL pad slot — never draws
        public int RowSpan = 1;          // rowspan attr — content overlays rows below
        public double ClassWidthPct;     // class width % — pins only when over-full
        public byte[]? ImgBytes;         // the cell's raster (data URI or a loaded file) — draws ABOVE its segments
        public bool WidthSetterCell;     // inline WIDTH+MIN-WIDTH pair (a report grid's sizing row)
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
        // Paragraph segments (the newsletter cells): the UA 1.12 em block
        // margins, collapsed max-wise between adjacent segments.
        public double MarginTopPt;
        public double MarginBottomPt;
        // the paragraph's class authored its margins (`margin: 0pt …`) — the
        // UA block margins yield to them at the segment close
        public bool MarginsExplicit;
        // class background-color: the band fills the cell's content width
        // (the green bar — measured 97.5..497.5 × its class height)
        public Color? Bg;
    }
}
