using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class StepWalkState
{
    public List<Aspose.Pdf.Converters.HtmlToPdfConverter.StepItem> items = null!;
    public Converters.HtmlToPdfConverter.StepLine line = null!;
    public int boldDepth;
    public bool inDetable;
    public bool pSawText;
    public bool pHadContent;
    public double pendingPad;
    public double gapNext;
    public double headingPt;
    public double headingLinePt;
    public bool inChoice;
    public bool inPara;
    public int i;
    public int n;
    public int end;
    public string tagStr = null!;
    public System.Text.RegularExpressions.Match nm = null!;
    public string tag = null!;
    public bool isClose;
    public bool selfClosed;
    public string cls = null!;
    public string style = null!;
    // A note, caution, ALARA or warning box: a framed block the form rules in its
    // own border width, holding a centred caption over its text. The caption sits
    // in a box of its own declared width, centred in the content column, and the
    // frame stands at least 80 css px tall.
    public System.Text.RegularExpressions.Match nbm = null!;
}
}
