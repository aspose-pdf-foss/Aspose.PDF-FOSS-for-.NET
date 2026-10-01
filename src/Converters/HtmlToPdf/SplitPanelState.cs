using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class SplitPanelState
{
    // Gate: a closed table followed by a stray td rowspan wrapper holding a
    // width:100% two-column table — the orphan-rowspan recovery shape.
    public System.Text.RegularExpressions.Match orphan = null!;
    public System.Text.RegularExpressions.Match headM = null!;
    public System.Text.RegularExpressions.Match innerM = null!;
    public Document doc = null!;
    public Aspose.Pdf.Core.PdfDictionary docFontDict = null!;
    public Page page = null!;
    public System.Globalization.CultureInfo invc = null!;
    public double marginLeft;
    public double contentW;
    // ── Header band ──────────────────────────────────────────────────────
    public Color bandFill = null!;
    public double leftCellX0;
    public double leftCellX1;
    public double rightCellX0;
    public double rightCellX1;
    // logo at its natural pixel size (fetched like a browser; nothing drawn
    // when the host is unreachable)
    public System.Text.RegularExpressions.Match imgM = null!;
    // header th: line 1 = the mixed-run heading; then the centred title
    public System.Text.RegularExpressions.Match thM = null!;
    // ── The two-panel band ───────────────────────────────────────────────
    public double panelTop;
    public double sbX0;
    public double sbX1;
    public double mainX0;
    public double mainX1;
    // sidebar + main content flows
    public System.Text.RegularExpressions.MatchCollection tdSplit = null!;
    // sidebar cell = the inner table's first td; main = the 84% td after it
    public string seg2 = null!;
    public System.Text.RegularExpressions.Match sbM = null!;
    public System.Text.RegularExpressions.Match mainM = null!;
    public List<Aspose.Pdf.Converters.HtmlToPdfConverter.SpBlock> sbBlocks = null!;
    public List<Aspose.Pdf.Converters.HtmlToPdfConverter.SpBlock> mainBlocks = null!;
    // Lay the MAIN flow first so the panel band height is known: it flows
    // top-down from the panel top, splitting to page 2 at the margin.
    public double mainPad;
    public double mainW;
    public double flowBottom;
    // Pure line-grid flow: every line — text or blank — advances by the
    // quirks pitch of its font; blocks add nothing of their own.
    public double mainY;
    public int pageIdx;
    public List<Aspose.Pdf.Page> pages = null!;
    public double p1PanelBottom;
    // panel fills UNDER the text (insert at the head of page 1's streams
    // would reorder everything; the fills went in before the flows in the
    // reference, so draw them now on a fresh underlay stream inserted early)
    public byte[] sbFillBytes = null!;
    // sidebar mini-flow: the same line grid, centred vertically in the band
    public double sbW;
    public double sbH;
    public List<(Aspose.Pdf.Converters.HtmlToPdfConverter.SpBlock B, List<List<(Aspose.Pdf.Converters.HtmlToPdfConverter.SpRun, string)>> Lines, double Pitch, (double asc, double sum) Fm)> sbLaid = null!;
    public double sbY;
    public string html = default!;
    public HtmlLoadOptions? options = null;
    public double pageWidth = 0;
    public double pageHeight = 0;
}
}
