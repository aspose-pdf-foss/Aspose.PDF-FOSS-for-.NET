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
private sealed class HeadingLayoutState
{
    public Page headingPage = null!;
    public double headingY;
    // Auto-sequenced headings get a formatted number prefix
    // (roman/alpha/decimal per Style), counting per level. The
    // DEFAULT style (None) still numbers in arabic — a plain
    // IsAutoSequence heading prints as "1  Heading 0"
    // — and the number is followed by
    // TWO spaces (no dot), the standard
    // prefix fragment ("1  " at the margin).
    public string headingPrefix = null!;
    // Create a link annotation for the heading
    public Page? destPage;
    public Heading heading = default!;
    public FlowLayout flow = default!;
    public Page page = default!;
    public System.Collections.Generic.List<(Heading h, int pageIdx)> tocEntries = default!;
    public PageLayoutState pl = default!;
    public Dictionary<int, int> headingAutoCounters = default!;
    public double marginLeft = 0;
    public double marginRight = 0;
}
}
