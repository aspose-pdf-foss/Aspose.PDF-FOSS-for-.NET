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
private sealed class TableParagraphState
{
    // Container-table unwrap: a table whose every row is one ColSpan
    // cell holding only Tables is a transparent wrapper — its
    // inner tables lay out as consecutive blocks (with
    // their own margins, whole blocks moving to the next page when
    // they don't fit) rather than being flattened into cell text.
    public List<Table>? containerInners;
    // An unbreakable table marked to continue on the next page moves WHOLE to a
    // fresh page when what is left of this one cannot hold it. Probed against the
    // generator: NEITHER flag does this alone — IsBroken=false on its own lets the
    // table run past the page foot on the page it starts, and IsInNextPage on its own
    // splits it where it stands; only the pair moves it. Once it is on the fresh page
    // a table taller than a whole page still splits (43 twenty-point rows fill page
    // two and spill nine onto page three), and its own top margin is dropped there.
    public bool movedToOwnPage;
    public Page tablePage = null!;
    // Overflow pages inset by the margin a freshly-added page would get:
    // the document-level top margin when the caller set one (explicitly
    // "for new pages added"), otherwise this page's effective top margin.
    public double spillTopMargin;
    // The page margins of the table's n-th spill page (1-based) before any box it breaks
    // inside takes its room; null outside a box.
    public Func<int, (double top, double bottom)>? spillPageMargins;
    public List<byte[]> pageContents = null!;
    public IReadOnlyList<List<(byte[] data, Aspose.Pdf.Rectangle rect)>> tableImages = null!;
    public IReadOnlyList<List<(ReservedBlock block, ReservedPart part, Aspose.Pdf.Rectangle rect)>> tableBlocks = null!;
    public IReadOnlyList<List<byte[]>> tableGraphs = null!;
}
}
