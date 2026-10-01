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
private sealed class EscNlFooterState
{
    public string src = null!;
    public System.Text.RegularExpressions.Match tblOpen = null!;
    public System.Text.RegularExpressions.Match tblClose = null!;
    public string preMk = null!;
    public string tblMk = null!;
    public string postMk = null!;
    public (double Box, double Drop) rootLine;
    public double rootBox;
    public double baseDrop;
    public double desc;
    public double bandW;
    // Pre-table text splits at the <center> boundary: the part outside sets at
    // the band's left edge, the part inside centres — and the fostered text
    // (between the table's structural tags, outside every cell) glues onto the
    // inside-centre part, both being the table container's inline content.
    public System.Text.RegularExpressions.Match centerOpen = null!;
    public string preOutside = null!;
    public string preCenter = null!;
    // The structural tags sit BACK TO BACK in this markup (the "\n"s between
    // them are text), so removing the cells must not inject separators — the
    // reference glues the fostered "\n"s into one unspaced run.
    public string fostered = null!;
    public bool tableCentred;
    public string preCentreRun = null!;
    // Post-table text before </center> centres; anything after it is outside.
    public System.Text.RegularExpressions.Match centerClose = null!;
    public string postCenter = null!;
    public string postOutside = null!;
    // ── parse the table: bare <th>s before the first <tr> form their own row ──
    public List<List<(List<string> lines, bool bold)>> rows = null!;
    public System.Text.RegularExpressions.Match firstTr = null!;
    public string headSpan = null!;
    // ── column widths: HTML default chrome over the full band ──
    public int nCols;
    public double[] minBox = null!;
    public double[] maxBox = null!;
    public double avail;
    public double sumMin;
    public double sumSlack;
    public double[] colBox = null!;
    // ── emit top-down; the first line box hangs one win-descent below the band
    // top (measured: the first baseline = bandTop − desc − baseDrop) ──
    public Aspose.Pdf.Core.PdfDictionary fontDict = null!;
    public Content.ContentStreamBuilder b = null!;
    public double topCursor;
    // The table: spacing above, rows of padded 13.5 pt line stacks, spacing
    // between and below. Cells centre vertically in their row (the td/th
    // default); th centres horizontally, td sets flush left. A table narrower
    // than the band centres over the band SHRUNK by a 60 pt side margin each
    // side, never left of the band edge (measured: table left 23.34 on the
    // margin-0 A4 band and 38.34 at margin 30 — both exactly
    // bandLeft + (bandW − 120 − tableW)/2).
    public double tableW;
    public double tableLeft;
    public Page page = null!;
    public string html = null!;
    public double bandLeft = 0;
    public double bandRight = 0;
    public double bandTop = 0;
    public double consumedH;
}
}
