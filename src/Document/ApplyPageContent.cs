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

public sealed partial class Document : IDisposable
{
    /// <summary>Provisional pages the flow prepared at its page breaks (keyed by
    /// overflow slot): the OnBeforePageGenerate handler ran on them before the
    /// slot's content was laid; the materialised page adopts their state.</summary>
    private readonly Dictionary<int, Page> _preparedOverflowPages = new();

    /// <summary>Content waiting for a page number that does not exist yet.</summary>
    private readonly List<(int PageNumber, Text.TextFragment Fragment)> _finalPagePlacements = new();

    /// <summary>Place an explicitly positioned fragment on the page that will bear
    /// <paramref name="pageNumber"/> ONCE THE DOCUMENT HAS PAGINATED, rather than on
    /// whatever page holds that number right now.
    ///
    /// The two differ whenever the flow is still going to grow. A caller that adds a
    /// page today to put something on "page 3" has picked a page that a continuation
    /// inserted ahead of it will renumber -- its content ends up on page 5 of the
    /// finished document, and two empty pages appear where it reserved them. Saying
    /// "page 3 of whatever this becomes" is a different request, and until now there
    /// was no way to make it.
    ///
    /// The fragment carries its own <see cref="Text.TextFragment.Position"/>, as any
    /// explicitly placed one does; only WHICH page it lands on is deferred.</summary>
    public void PlaceOnFinalPage(int pageNumber, Text.TextFragment fragment)
    {
        if (pageNumber < 1 || fragment is null) return;
        _finalPagePlacements.Add((pageNumber, fragment));
    }

    /// <summary>Resolve everything <see cref="PlaceOnFinalPage"/> deferred, now that
    /// pagination has settled and a page number means what it will mean in the file.
    /// A number past the end still extends the document -- the caller asked for that
    /// page -- but only after the flow has taken every page it needs.</summary>
    private void ApplyFinalPagePlacements()
    {
        if (_finalPagePlacements.Count == 0) return;
        // Grouped by page and PREPENDED, not appended. The caller asked for this
        // page before it existed, so everything the flow later put there came
        // after -- the page was made to hold this, and the flow continued onto
        // it. Appending would read the other way round in the extracted text.
        // One prepend per page, over the fragments concatenated in the order they
        // were asked for, since prepending each in turn would reverse them.
        foreach (var group in _finalPagePlacements.GroupBy(p => p.PageNumber))
        {
            while (Pages.Count < group.Key) Pages.Add();
            var page = Pages[group.Key];
            var blocks = new List<byte[]>();
            var builder = new Text.TextBuilder(page) { ContentSink = blocks.Add };
            foreach (var (_, fragment) in group) builder.AppendTextInline(fragment);
            if (blocks.Count == 0) continue;
            var total = blocks.Sum(b => b.Length);
            var joined = new byte[total];
            var at = 0;
            foreach (var b in blocks) { Buffer.BlockCopy(b, 0, joined, at, b.Length); at += b.Length; }
            page.PrependContentStream(joined);
        }
        _finalPagePlacements.Clear();
    }

    /// <summary>
    /// Apply page-level Paragraphs, Headers, and Footers to each page's content stream.
    /// Called automatically before save.
    /// </summary>
    private void ApplyPageContent()
    {        // Form fields (combo/list/check/text boxes, radio groups) added to the
        // generator paragraph tree must be registered in the AcroForm before the
        // pages are written, so they round-trip as real fields.
        RegisterGeneratedFormFields();

        // An authored tagged document is rendered onto its pages before any page is laid
        // out: a table-of-contents page draws the entries the rendered headers ask for, and a
        // page laid out once resumes below what it drew rather than starting over.
        EnsureTaggedPdfMetadata();

        var pc = new PageContentState();
        pc.preLayoutPageCount = Pages.Count;

        pc.overflowPages = new List<(byte[] content, double width, double height)>();
        pc.overflowImages = new Dictionary<int, List<(byte[] data, Rectangle rect)>>();
        _overflowCheckboxes.Clear();
        pc.pendingFlows = new List<(FlowLayout flow, int slotStart, int slotEnd)>();
        pc.pendingTocEmits = new List<(Page tocPage, string fontName, List<Page> contPages,
            System.Collections.Generic.List<(int slot, byte[] preLeader, double textEnd, double lastY,
                double entrySize, string entryFace, Text.TabLeaderType leader, double rightStop,
                bool showNumbers, bool underline, string prefix, double x0, Page? destPage, int fallbackIdx,
                Rectangle linkRect, Heading heading, string lastLine, double lastX,
                System.Func<string, double>? measure)> entries)>();
        pc.pagesSnapshot = Pages.ToList();
        pc.headingAutoCounters = new Dictionary<int, int>();
        foreach (var page in pc.pagesSnapshot)
            LayoutPage(page, pc);


        pc.overflowPageRefs = AddOverflowPages(pc.overflowPages, pc.overflowImages, pc.pendingFlows,
            pc.preLayoutPageCount);
        FinaliseDeferredFlows(pc.pendingFlows, pc.overflowPageRefs);
        // Page numbers mean what they will mean in the file only from here on.
        ApplyFinalPagePlacements();
        // The page-count bands deferred above render now that every page exists.
        for (var pi = 1; pi <= Pages.Count; pi++)
        {
            var pg = Pages[pi];
            if (pg.HeaderFooterApplied || (pg.Header is null && pg.Footer is null)) continue;
            pg.HeaderFooterApplied = true;
            pg.Header?.RenderToPage(pg, isHeader: true, pg.Number, this);
            pg.Footer?.RenderToPage(pg, isHeader: false, pg.Number, this,
                !_deferredNumberBands.TryGetValue(pg, out var drawsTables) || drawsTables);
        }
        EmitDeferredTocLeaders(pc.pendingTocEmits, pc.pendingFlows, pc.overflowPageRefs);
    }

    // Checkbox widgets a multi-page table laid out on its spill pages, keyed by
    // overflow slot; bound to the page when the slot materialises.
    private readonly Dictionary<int, List<(Forms.CheckboxField cbf, Rectangle rect)>> _overflowCheckboxes = new();

    private List<Page> AddOverflowPages(
        List<(byte[] content, double width, double height)> overflowPages,
        Dictionary<int, List<(byte[] data, Rectangle rect)>> overflowImages,
        List<(FlowLayout flow, int slotStart, int slotEnd)> pendingFlows,
        int preLayoutPageCount)
    {
        // Which page each overflow slot CONTINUES. A continuation belongs immediately after
        // the page whose content ran out of room — appending it to the end of the document
        // puts the rest of a page-1 paragraph after every page that was already there.
        var slotOwner = new Page?[overflowPages.Count];
        foreach (var (flow, slotStart, slotEnd) in pendingFlows)
            for (var i = slotStart; i < slotEnd && i < slotOwner.Length; i++)
                slotOwner[i] = flow.CurrentPage;
        // A slot no flow claims — an image queued onto a page of its own, say — still belongs
        // where its NEIGHBOURS do. Left ownerless it would be appended while the slots around
        // it were inserted, which puts it after pages it precedes.
        for (var i = 1; i < slotOwner.Length; i++)
            slotOwner[i] ??= slotOwner[i - 1];
        // Keyed by the owner's PAGE NUMBER, not the Page object: two flows can carry
        // different Page instances for the same page, and keying on the object then counts
        // each one's insertions separately — both land at the same slot and the later one
        // pushes the earlier down, reversing them.
        var insertedFor = new Dictionary<int, int>();
        // Every insertion above an owner pushes that owner's live index up; the
        // owner is still one of the pages that existed before layout, and the
        // count of pages inserted at or before its index says by how much.
        var insertedAt = new List<int>();
        // Add overflow pages (from multi-page table layout) after iteration.
        // Track the Page created for each slot so deferred link annotations
        // (per-segment hyperlinks queued by FlowLayout) can resolve to the
        // page they actually landed on.
        var overflowPageRefs = new List<Page>(overflowPages.Count);
        for (var slot = 0; slot < overflowPages.Count; slot++)
        {
            var (content, width, height) = overflowPages[slot];
            Page newPage;
            var owner = slotOwner[slot];
            var ownerIdx = owner is null ? -1 : Pages.IndexOf(owner);
            var already = ownerIdx >= 1 && insertedFor.TryGetValue(ownerIdx, out var c) ? c : 0;
            var shiftedBy = insertedAt.Count(at => at <= ownerIdx);
            if (ownerIdx >= 1 && ownerIdx - shiftedBy < preLayoutPageCount && ownerIdx + already < Pages.Count)
            {
                newPage = Pages.Insert(ownerIdx + already + 1, width, height);
                insertedFor[ownerIdx] = already + 1;
                insertedAt.Add(ownerIdx + already + 1);
            }
            else
            {
                newPage = Pages.Add();
                if (ownerIdx >= 1) insertedFor[ownerIdx] = already + 1;
            }
            newPage.MediaBox = new Rectangle(0, 0, width, height);
            // The page continues its owner: it takes the owner's font names first, so
            // content written against those names (a Times paragraph's F1) reaches the
            // same faces here, and only then registers a default of its own.
            if (owner is not null) MergePageFontResources(owner, newPage);
            Table.RegisterFont(newPage);
            if (owner is not null)
            {
                Text.TextParagraph.CarryExtGStates(owner, newPage);
                Text.TextParagraph.CarryXObjects(owner, newPage, content);
            }
            newPage.AddContentStream(content);
            if (overflowImages.TryGetValue(slot, out var imgs))
                foreach (var (data, rect) in imgs)
                    newPage.AddImage(data, rect);
            if (_overflowCheckboxes.TryGetValue(slot, out var cbs))
                foreach (var (cbf, rect) in cbs)
                    cbf.PlaceWidget(newPage, rect);
            overflowPageRefs.Add(newPage);
        }
        return overflowPageRefs;
    }

    private void FinaliseDeferredFlows(
        List<(FlowLayout flow, int slotStart, int slotEnd)> pendingFlows,
        List<Page> overflowPageRefs)
    {
        // Resolve every flow's deferred link annotations + embedded-font renders
        // against the final page sequence -- each flow owns the slice of
        // overflowPageRefs captured at its commit time. Embedded renders go
        // first so TextBuilder gets a clean page before any annotation rects
        // overlay (incidental: AddContentStream order doesn't matter for the
        // saved PDF, but it keeps the dev mental model "render then annotate").
        foreach (var (flow, slotStart, slotEnd) in pendingFlows)
        {
            var pageRange = new List<Page>(slotEnd - slotStart);
            for (var i = slotStart; i < slotEnd; i++) pageRange.Add(overflowPageRefs[i]);
            // The flow's final cursor lives on its LAST overflow page: paragraphs
            // added to that page later resume below the spilled content.
            if (flow.CurrentSlot >= 0 && pageRange.Count > 0)
                pageRange[^1].LayoutCursorY = flow.CurrentY;
            // Pre-built table slices reference fonts embedded in the START page's
            // font dictionary (e.g. Type0 serif runs of an HTML-engine cell); a
            // freshly materialised overflow page only has the standard table font.
            // Merge the start page's font entries so those resource names resolve.
            foreach (var op in pageRange)
                MergePageFontResources(flow.CurrentPage, op);
            // Inline images paint before the deferred text so a line that
            // overlaps an image keeps its glyphs on top.
            flow.FinaliseBandTables(pageRange);
            flow.FinaliseImages(pageRange);
            flow.FinaliseEmbeddedRenders(pageRange);
            flow.FinaliseNotifications(pageRange);
            flow.FinaliseAnnotations(pageRange, PageCount);
            flow.FinaliseFormFields(pageRange, this);
            flow.FinaliseReservedBlocks(pageRange);
            flow.FinaliseRules(pageRange);
            // A page watermark repeats on the overflow pages its content spilled onto.
            if (flow.CurrentPage.PendingWatermark is { Available: true, Image: { } fwmImage })
                foreach (var op in pageRange)
                    new WatermarkArtifact { SourceImage = fwmImage }.AddToPage(op);

            // A background artifact likewise repeats on every overflow page of the
            // flow (an overflowing page keeps its background image).
            foreach (var srcArt in flow.CurrentPage.Artifacts)
                if (srcArt is BackgroundArtifact bgArt)
                    foreach (var op in pageRange)
                        bgArt.RenderToPage(op);

            // Every OTHER artifact the source page carries repeats too: a page the flow
            // generated is a continuation of that page, so it carries the same
            // artifact set (page 1's parsed artifacts appear on the page its box spilled
            // onto). They are copied as the raw marked-content blocks they were read from —
            // an artifact parsed out of a document has no model this library could re-render.
            if (pageRange.Count > 0)
            {
                var srcBlocks = flow.CurrentPage.Artifacts.RawArtifactBlocks();
                foreach (var block in srcBlocks)
                    foreach (var op in pageRange)
                        op.AddContentStream(block);
            }

            // A running Header/Footer likewise repeats on every overflow page of the flow, not
            // just the originating page (which was stamped in the main loop). Freshly-materialised
            // overflow pages carry no Header/Footer of their own, so render the source page's.
            var hfSource = flow.CurrentPage;
            // A per-page OnBeforePageGenerate handler owns the header of every page the
            // flow generates, not only the one it was subscribed on: the handler runs on
            // each overflow page (which inherits the subscription) and whatever it
            // assigns there is drawn instead of the source page's header. That is how a
            // report gives page 1 a title block and every later page a running head.
            for (var opi = 0; opi < pageRange.Count; opi++)
            {
                var op = pageRange[opi];
                // A generated page carries its source page's PageInfo (margins,
                // default text state): its footer band sits on the same bottom
                // margin and its header at the same left. A page the flow prepared
                // at its break (handler already run there) adopts that state.
                if (_preparedOverflowPages.TryGetValue(slotStart + opi, out var prepared))
                    op.AdoptPreparedPage(prepared);
                else
                    op.InheritPageInfoFrom(hfSource);
                // The running header/footer is inherited FIRST, so a handler that reaches
                // through `page.Header` on a generated page finds one (one clears its
                // paragraphs on page 2); a handler that assigns its own replaces it.
                op.Header ??= hfSource.Header;
                op.Footer ??= hfSource.Footer;
                if (hfSource.HasBeforePageGenerate)
                {
                    op.CopyBeforePageGenerateFrom(hfSource);
                    op.RaiseBeforePageGenerate();
                }
                if (op.Header is null && op.Footer is null) continue;
                if (op.HeaderFooterApplied) continue;
                op.HeaderFooterApplied = true;
                op.Header?.RenderToPage(op, isHeader: true, op.Number, this);
                op.Footer?.RenderToPage(op, isHeader: false, op.Number, this);
            }
        }
    }

    private void EmitDeferredTocLeaders(
        List<(Page tocPage, string fontName, List<Page> contPages,
        System.Collections.Generic.List<(int slot, byte[] preLeader, double textEnd, double lastY,
            double entrySize, string entryFace, Text.TabLeaderType leader, double rightStop,
            bool showNumbers, bool underline, string prefix, double x0, Page? destPage, int fallbackIdx,
            Rectangle linkRect, Heading heading, string lastLine, double lastX,
            System.Func<string, double>? measure)> entries)> pendingTocEmits,
        List<(FlowLayout flow, int slotStart, int slotEnd)> pendingFlows,
        List<Page> overflowPageRefs)
    {
        // Emit the deferred TOC leaders + link annotations against the FINAL
        // page sequence: only now is it known which page each heading actually
        // rendered on (content pagination and IsInNewPage move headings onto
        // overflow pages materialised above).
        var te = new TocLeaderEmitState { pendingFlows = pendingFlows, overflowPageRefs = overflowPageRefs };
        foreach (var (tocPage, tocFontName, tocContPages, tocEntriesPending) in pendingTocEmits)
        {
            te.tocPage = tocPage;
            te.tocFontName = tocFontName;
            te.tocContPages = tocContPages;
            te.tocPageIdxFinal = Pages.IndexOf(tocPage);
            foreach (var rec in tocEntriesPending)
            {
                te.rec = rec;
                EmitTocLeaderEntry(te);
            }
        }
    }

    /// <summary>Lays out one page of the pass: orientation, background, bands, then the TOC and the paragraphs.</summary>
    private void LayoutPage(Page page, PageContentState pc)
    {
        // A requested landscape orientation is resolved HERE, first thing: the
        // background fill, the header/footer render and the paragraph flow below all
        // measure the page and must see the wide box, and this is also where the
        // authored dimensions stop shadowing PageInfo.Width/Height. Resolving it
        // once per page (rather than only for pages carrying paragraphs) means a
        // page that only has a background is turned too. A page whose box was only
        // INHERITED — a no-size Insert — resolves from the PageInfo A4 default
        // instead, so such a TOC page renders 842×595 even inside a US-Letter
        // document.
        if (page.PageInfo is { LandscapeRequested: true })
        {
            if (page.SizeInherited) page.MediaBox = new Rectangle(0, 0, 842, 595);
            else page.PageInfo.ApplyRequestedOrientation();
        }

        // Page.OnBeforePageGenerate fires BEFORE the page is generated, so a handler
        // that assigns Page.Header / Page.Footer is honoured by this layout pass —
        // the header then reserves its band and the rows fit around it. Firing it
        // only at save (after layout) left such headers undrawn and let the table
        // claim the whole content height.
        page.RaiseBeforePageGenerate();

        // Flush operator collection to content stream
        page.Contents.FlushToPage();

        // Page.Background paints the whole page (MediaBox) behind every other
        // operator. Prepended so it sits under existing content and any
        // paragraphs/header/footer rendered below. The fill is wrapped in a
        // /Background marked-content block so re-applying a background replaces
        // the previous one instead of stacking, and Color.White means "remove
        // the background" (the documented semantics).
        ApplyExplicitPageBackground(page);

        // Materialise /AP appearances for annotations that lack one so they render
        // (the renderer draws Line/Polygon/Polyline only from their /AP) and expose
        // NormalAppearance after save.
        PlacePageAnnotations(page);

        // Render page Header/Footer set through Page.Header / Page.Footer.
        // Independent of the paragraph layout below (a page may carry only a
        // header), so it runs before the LayoutApplied gate and guards itself.
        // A band printing the page count waits, on a page with paragraphs
        // still to lay out, until the flow has made its overflow pages (a
        // 20-page document's first page reads "Page 1 of 20", not "of 1").
        var bandWaitsForCount = page.Paragraphs.Count > 0
            && (page.Header?.UsesPageCount == true || page.Footer?.UsesPageCount == true);
        if (!page.HeaderFooterApplied && !bandWaitsForCount
            && (page.Header is not null || page.Footer is not null))
        {
            ApplyPageHeaderFooterBands(page);
        }

        // A page laid out by an earlier ProcessParagraphs() call re-enters
        // layout when NEW paragraphs were queued since: the pass resumes at
        // the persisted LayoutCursorY, so the new content stacks below the
        // earlier content on the SAME page (five
        // one-per-call tables stay on page 1 — a re-processed paragraph is the
        // page's first, so IsInNewPage never forces a break either).
        if (page.LayoutApplied && page.Paragraphs.Count == 0) return;

        // Apply TOC info + Paragraphs
        if (page.TocInfo is not null || page.Paragraphs.Count > 0)
            LayoutPageContent(page, pc);

        // Header/Footer are already rendered once per page by the self-guarding
        // RenderToPage block above (which uses page.Number for '#' substitution).
        // Re-applying them here stamped a second copy onto every freshly laid-out
        // page, so it is intentionally not repeated.

        // Apply a watermark set through Page.Watermark as an artifact. Use the
        // set-only PendingWatermark (not the Watermark getter, which now *detects*
        // an already-present watermark from the content) so re-saving a document
        // that already carries a watermark doesn't stamp a second copy.
        if (page.PendingWatermark is { Available: true, Image: { } wmImage })
            new WatermarkArtifact { SourceImage = wmImage }.AddToPage(page);

        page.LayoutApplied = true;
    }
}
