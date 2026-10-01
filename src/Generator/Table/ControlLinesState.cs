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
private sealed class ControlLinesState
{
    // A multi-line control cell stacks by each line's OWN height (a 10pt
    // spacer line directly above an 8.5pt box: the box top sits exactly
    // 10pt below the cell content top) instead of the row's uniform pitch.
    public bool exactStack;
    // Serif-faced tables (the DataWorks form grid and its kin) typeset the
    // flow text INSIDE control cells in the cell's serif face while button
    // captions stay in the UI sans; std14 Times-Roman advances track the
    // expected Times New Roman.
    public bool serifText;
    public string textFont = null!;
    public double yCursor;
    // The non-exact walk still advances a nested-grid RESERVE line by its own
    // FontSize (an equal share of the grid's real height) — the uniform pitch
    // over-advances a reserve that doesn't divide evenly and pushes everything
    // after the grid down by the quantization slack.
    public double walkY;
    public Table.CellLine line = null!;
    public double lineTop;
    public double textX;
    // A control line OPENED by a hidden checkbox (the upload cell's
    // filename) seats its whole run higher — it is drawn
    // 4.3 pt up from the uniform first-line seat; the walk advance
    // is untouched so the lines below hold their measured places.
    public double dwBase;
    public double dwPen;
    public int dwBoxIdx;
    public System.Text.StringBuilder dwSb = null!;
    public int dwConsumed;
    public int dwTi;
    public double bScale;
    // DataWorks: the caption draws in the 10 pt UI sans while the box
    // chrome keeps the line's 12 pt scale, and the whole control seats
    // one point higher (both measured on the expected buttons).
    public double bCapFs;
    public double bBase;
    public double bFaceTop;
    public double bFaceH;
    public double bPen;
    public System.Text.StringBuilder bSb = null!;
    public int bConsumed;
    public int bti;
}
}
