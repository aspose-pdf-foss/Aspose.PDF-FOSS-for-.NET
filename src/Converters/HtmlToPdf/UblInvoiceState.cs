using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class UblInvoiceState
{
    // the sheet's print-media table font (14px Verdana) and 20px line
    public double cellFs;
    public double lineH;
    public double cellDrop;
    // ── parse ──
    // header table: the address cell and the logo/references cell
    public System.Text.RegularExpressions.Match custM = null!;
    public List<string> addrLines = null!;
    public System.Text.RegularExpressions.Match h2M = null!;
    public string h2Text = null!;
    public System.Text.RegularExpressions.Match logoM = null!;
    public string logoText = null!;
    public List<(string Label, string Value)> refRows = null!;
    public System.Text.RegularExpressions.Match refM = null!;
    public System.Text.RegularExpressions.Match headlineM = null!;
    public string headline = null!;
    // item table: header row + line rows
    public List<(string Text, bool Right)> itemThs = null!;
    public List<(string Text, bool Right)> lineTds = null!;
    public System.Text.RegularExpressions.Match linesM = null!;
    public System.Text.RegularExpressions.Match lineRowM = null!;
    // totals table rows: label + right-aligned value (bold on the total row)
    public List<(string Label, string Value, bool Bold, double PadTop)> totRows = null!;
    public System.Text.RegularExpressions.Match totM = null!;
    // supplier footer lines (the <br> splits them)
    public List<string> supLines = null!;
    public System.Text.RegularExpressions.Match supM = null!;
    // ── layout ──
    public Document doc = null!;
    public Page page = null!;
    public System.Globalization.CultureInfo invc = null!;
    public double contentW;
    public double rightEdge;
    // ── header table: white box, address column, logo + references ──
    public double headerH;
    public double yAddr;
    // the inline h2: 1.5em type on the inherited 20px line (negative leading)
    public double h2Fs;
    public double h2Drop;
    // logo: 36px bold uppercase, right-aligned, wrapped per word on the 36px line
    public double logoFs;
    public double logoDrop;
    public double yLogo;
    // two <br> lines, then the floated references table: both columns share
    // fixed edges — values right-aligned two borders inside the frame, labels
    // right-aligned one 20px padding left of the widest value
    public double refDrop;
    public double yRef;
    public double refValueW;
    public double refValueRight;
    public double refLabelRight;
    // ── the VARER headline: the frame's Arial on its hhea 16px line ──
    public double yTd;
    public double headlineFs;
    public double headlineLineH;
    public double headlineDrop;
    // ── the item table: top/bottom hairlines, headings at their widths ──
    public double tableTop;
    // columns: each heading's width plus the following column's 10px padding;
    // the description column takes its declared 40%
    public double[] colW = null!;
    public double[] colX = null!;
    public double rowH;
    public double lineRowH;
    public double tableH;
    public double thBase;
    public double rowBase;
    public double tableBottom;
    // ── the totals table, floated right ──
    public double labelW;
    public double valueW;
    public double totW;
    public double totX;
    public double totRowH;
    public double yTot;
    // ── the empty spacer tables, then the supplier footer ──
    // each empty .invoice-viewer is a 1.5 pt white sliver with its 20px
    // bottom margin; the one carrying two <br>s holds their line boxes
    public double[] emptyHeights = null!;
    public double ySp;
    public double supH;
    public double ySup;
    public string html = default!;
    public double pageWidth = 0;
    public double pageHeight = 0;
    public (double asc, double sum) vm;
    public (double asc, double sum) am;
}
}
