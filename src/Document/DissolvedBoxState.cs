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
private sealed class DissolvedBoxState
{
    // Multi-column box: lay the children out across N columns
    // (fill column 0 top-to-bottom, then column 1, ... then a
    // fresh page). Columns start at the page's left content
    // margin; the box's own Margin doesn't inset the flow.
    // A caller that sized/offset the box passes its own geometry
    // (a single narrow column for an offset box, padded columns
    // for a painted one).
    public int columnCount;
    public bool inColumns;
    // Inline-joined styled paragraph accumulator: consecutive
    // IsInLineParagraph fragments/headings merge into ONE
    // flowing paragraph (joined with their
    // per-segment styles, footnote reference marks and heading
    // labels intact). A paragraph flushes when a
    // non-inline child starts the next one.
    public List<Aspose.Pdf.Document.FlowLayout.StyledRun> styRuns = null!;
    public double styLs;
    public double styBaseSize;
    public List<(Aspose.Pdf.Note note, string marker, double size)> styNotes = null!;
    public Color? styBackground;
    public HorizontalAlignment styAlign;
    public int styLastChild;
    public List<Aspose.Pdf.BaseParagraph> innerList = null!;
    public BaseParagraph inner = null!;
    public bool nextInline;
}
}
