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
    // The col-full generation's acknowledge table stands
    // UNDER the content: widget cells right-anchored on a
    // 112.5 pt grid against the sheet's right edge, each
    // blank over the labels its own cell carries, and the
    // second label row on a baseline all widgets share.
    // Above it: a paragraph's own bottom margin, or the
    // table's 7.5 after a framed box (nothing after the
    // double-ruled one). All constants are empirical,
    // exact to 0.01.
    public double relBoxTop;
    public double gapAbove;
    public double relRuleY;
    public double relBlankBottom;
    // the shared second label row keys off the lowest
    // first-row label any widget put down
    public double tr1Base;
    public bool anyTr2;
    public double tr2Base;
    public double relBottom;
    public double topY;
    public Content.ContentStreamBuilder tb2 = null!;
    public string lres2 = null!;
    public double tableRight;
    public double cellX;
    public bool anyCheckbox;
}
}
