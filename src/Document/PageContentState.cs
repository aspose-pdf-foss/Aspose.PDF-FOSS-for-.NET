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
private sealed class PageContentState
{
    // How many pages the document had BEFORE this layout ran. A continuation page is
    // placed after the page it continues only when there is pre-existing content it
    // would otherwise jump over; pages this same pass created are already in flow order
    // and re-ordering them puts a continuation ahead of the page it continues.
    public int preLayoutPageCount;
    // Collect overflow pages to add after iteration
    public List<(byte[] content, double width, double height)> overflowPages = null!;
    // Table images destined for an overflow page, keyed by that page's slot index in
    // overflowPages. Applied once the page is materialised (the page object doesn't
    // exist while the table is being built).
    public Dictionary<int, List<(byte[] data, Rectangle rect)>> overflowImages = null!;
    // Per-page flow layouts whose deferred annotations need resolving against
    // the final Page sequence (slot indices into overflowPages). Each entry:
    // the flow + the slot range it owns in overflowPages.
    public List<(FlowLayout flow, int slotStart, int slotEnd)> pendingFlows = null!;
    // TOC leaders + link annotations are emitted only after EVERY page has
    // laid out and the overflow pages are materialised: an entry's printed
    // page number must reflect the page its heading FINALLY landed on
    // (content pagination / IsInNewPage moves headings onto pages that do
    // not exist while the TOC page itself is being laid out).
    public List<(Page tocPage, string fontName, List<Page> contPages, List<(int slot, byte[] preLeader, double textEnd, double lastY, double entrySize, string entryFace, Text.TabLeaderType leader, double rightStop, bool showNumbers, bool underline, string prefix, double x0, Page? destPage, int fallbackIdx, Rectangle linkRect, Heading heading, string lastLine, double lastX, System.Func<string, double>? measure)> entries)> pendingTocEmits = null!;
    // Snapshot pages: if a paragraph handler (e.g. Image overflow)
    // appends a new Page mid-loop, the live collection mutates.
    public List<Page> pagesSnapshot = null!;
    // Per-level running counters for auto-sequenced headings authored
    // directly in page Paragraphs (e.g. Heading{ IsAutoSequence = true }).
    // Document-scoped so the sequence continues across pages.
    public Dictionary<int, int> headingAutoCounters = null!;
    public List<Page> overflowPageRefs = null!;
}
}
