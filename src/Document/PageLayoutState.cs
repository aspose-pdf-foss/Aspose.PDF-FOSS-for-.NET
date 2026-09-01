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
private sealed class PageLayoutState
{
    /// <summary>The page being laid out - the table of contents draws on it even while a
    /// dissolved floating box is measured on a dry-run page.</summary>
    public Page page = null!;
    // (The landscape request was already resolved at the top of this loop.)
    public string? fontName;
    // page.PageInfo.Margin is always non-null (auto-initialised to a
    // zeroed MarginInfo) so ?? never fires. Treat zeros as "use
    // default 72 pt" — otherwise a fresh page with no explicit
    // margins lays content out with no top/bottom/left/right
    // breathing room at all.
    public MarginInfo? pageMargin;
    // Respect user-set margins verbatim (including explicit zeros); fall
    // back per side FIRST to the document-level PageInfo margin
    // (`doc.PageInfo.Margin.Left = 40` is honoured even when it is
    // set AFTER the pages were added — margins resolve at layout time,
    // not at Pages.Add time), THEN to the Generator defaults
    // (90 pt L/R, 72 pt T/B). With the matching default page size A4
    // (595x842), GoTo destinations land at x=90 y=770 = 842-72.
    public MarginInfo? docMargin;
    public double marginTop;
    public double marginBottom;
    public double marginLeft;
    public double marginRight;
    // Shared Y cursor so consecutive paragraphs flow down the page instead
    // of piling on top of each other at the top margin. When cursor drops
    // below the bottom margin, FlushToNewPage() starts a fresh overflow page.
    // A page laid out in an earlier pass resumes below its existing content.
    // A CropBox set strictly inside the MediaBox re-anchors the flow:
    // paragraphs lay out in the VISIBLE page (a square CropBox
    // at the media bottom receives a full-bleed image at crop top, not
    // 284 pt below the A4 top edge).
    // The layout frame is the MEDIA box: a /Rotate page's paragraphs seat
    // against the media edges and paint upright in them
    // (see Page.LayoutFrameHeight; Page.Height answers the DISPLAY frame).
    public double layoutTopY;
    public double curY;
    // TOC entry rendering: every heading whose TocPage is this page renders
    // as "<auto-number> <text> .... <destination page>" — laid out across
    // the configured columns, indented by heading level, with the heading
    // text wrapped to the column width and the page number right-aligned to
    // the column edge with a dot leader on the final line.
    //
    // Entries are drawn FROM THE PARAGRAPH FLOW below (RenderTocEntry runs
    // when the flow reaches each TOC-page heading), not in a separate
    // pre-pass: page content authored around the headings (e.g. spacer
    // fragments before the first entry) keeps its authored order in the
    // content stream and in extraction. Headings that
    // live on OTHER pages get their entries appended after this page's own
    // paragraphs, chaining the same cursor.
    public List<(Heading h, int pageIdx)> tocEntries = null!;
    public HashSet<Heading> tocRendered = null!;
    public int tocCol;
    // TOC continuation-page overflow: entries that no longer fit on the
    // current TOC page (their line boxes PLUS the entry's bottom margin
    // would cross the page's bottom margin) continue on pages INSERTED
    // right after the TOC page — this splits a 4×(10+300 pt)
    // TOC into two pages and shifts the content pages down. Because
    // that insertion changes the PAGE NUMBERS the leaders print, every
    // entry's rendering is buffered as (slot, pre-leader bytes, leader
    // parameters) and emitted only after the whole TOC laid out and
    // the continuation pages exist — leaders then resolve their
    // destination indices against the FINAL page sequence, and each
    // entry's text+leader blocks stay adjacent in stream order (the
    // raw-extraction line shape depends on that adjacency).
    public int tocSlot;
    public List<(int slot, byte[] preLeader, double textEnd, double lastY, double entrySize, string entryFace, Text.TabLeaderType leader, double rightStop, bool showNumbers, bool underline, string prefix, double x0, Page? destPage, int fallbackIdx, Rectangle linkRect, Heading heading, string lastLine, double lastX, System.Func<string, double>? measure)> tocPending = null!;
    public double? tocTopY;
    // Hierarchical section counters for IsAutoSequence headings:
    // a level-N heading bumps counter[N] and resets the deeper ones,
    // printing "c1.c2.….cN " (e.g. 1, 1.1, 1.2, 2).
    public int[] tocCounters = null!;
    // Script-matched CJK face for entries whose titles need one,
    // resolved once per TOC.
    public byte[]? tocCjkTtf;
    public int tocColCount;
    public double[] tocColLefts = null!;
    public double[] tocColWidths = null!;
    public FlowLayout flow = null!;
    public int flowSlotStart;
    public Text.TextBuilder tb = null!;
    // Height of the current run of inline images (IsInLineParagraph). Inline
    // images share one line and the cursor only drops by the tallest of them
    // once the line ends (a block image or a flush at end-of-flow).
    public double pendingInlineLineHeight;
    // The same Table INSTANCE added to Paragraphs more than once lays out
    // a single time (re-adding is a no-op, not a
    // second copy of the table).
    public HashSet<Table> renderedTables = null!;
    public List<BaseParagraph> paraList = null!;
}
}
