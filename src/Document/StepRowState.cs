using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;

namespace Aspose.Pdf;

public sealed partial class Document
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class StepRowState
{
    public Converters.HtmlToPdfConverter.StepRow prow = null!;
    public double psContentX;
    public double psBulletX;
    // the column the form declares for this row, which may
    // reach past the sheet and be clipped there
    // the acknowledge-table generation squares its column off
    // 1.2 pt further right than the flex one (its box rules
    // stroke at sheet − 33.6), and its
    // LANDSCAPE col-full rows keep the 729 css px landscape column
    public double psRowRight;
    public double psLimit;
    // whether this sheet already carries a step, read BEFORE the
    // row's own margin is spent - otherwise a step that has just
    // opened a fresh sheet looks like it is following something
    // and breaks again, for ever
    public bool psSheetEmpty;
    // the framed note box currently open: where it started, the
    // rule it is drawn in, and the inset its content takes
    public double psBoxTop;
    public double psBoxBorder;
    public bool psBoxDouble;
    public double psLineInset;
    public double clogTop;
    public double psRowTop;
    public Page psRowStartPage = null!;
    public bool psBulletPending;
    public bool warnFirstSeg;
    public int pidx;
    public bool psSeenTable;
}
}
