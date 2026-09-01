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

    /// <summary>
    /// Apply page-level Paragraphs, Headers, and Footers to each page's content stream.
    /// Called automatically before save.
    /// </summary>
    private void ApplyPageContent()
    {        // Form fields (combo/list/check/text boxes, radio groups) added to the
        // generator paragraph tree must be registered in the AcroForm before the
        // pages are written, so they round-trip as real fields.
        RegisterGeneratedFormFields();

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
        // The page-count bands deferred above render now that every page exists.
        for (var pi = 1; pi <= Pages.Count; pi++)
        {
            var pg = Pages[pi];
            if (pg.HeaderFooterApplied || (pg.Header is null && pg.Footer is null)) continue;
            pg.HeaderFooterApplied = true;
            pg.Header?.RenderToPage(pg, isHeader: true, pg.Number, this);
            pg.Footer?.RenderToPage(pg, isHeader: false, pg.Number, this, true);
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
            if (ownerIdx >= 1 && ownerIdx < preLayoutPageCount && ownerIdx + already < Pages.Count)
            {
                newPage = Pages.Insert(ownerIdx + already + 1, width, height);
                insertedFor[ownerIdx] = already + 1;
            }
            else
            {
                newPage = Pages.Add();
                if (ownerIdx >= 1) insertedFor[ownerIdx] = already + 1;
            }
            newPage.MediaBox = new Rectangle(0, 0, width, height);
            Table.RegisterFont(newPage);
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
        foreach (var (tocPage, tocFontName, tocContPages, tocEntriesPending) in pendingTocEmits)
        {
            var tocPageIdxFinal = Pages.IndexOf(tocPage);

            // The page a heading FINALLY landed on: the flow that laid it out
            // recorded its slot; map the slot through that flow's overflow range.
            int FinalHeadingIdx(Heading h)
            {
                foreach (var (flow, slotStart, slotEnd) in pendingFlows)
                {
                    if (!flow.TryGetParagraphPosition(h, out var pos)) continue;
                    var hp = pos.slot < 0 ? flow.CurrentPage
                        : slotStart + pos.slot < slotEnd ? overflowPageRefs[slotStart + pos.slot] : null;
                    return hp is not null ? Pages.IndexOf(hp) : 0;
                }
                return 0;
            }

            foreach (var rec in tocEntriesPending)
            {
                var target = rec.slot == 0 ? tocPage : tocContPages[rec.slot - 1];
                if (rec.preLeader.Length > 0) target.AddContentStream(rec.preLeader);
                var destIdx = rec.destPage is not null ? Pages.IndexOf(rec.destPage) : 0;
                if (destIdx <= 0) destIdx = FinalHeadingIdx(rec.heading);
                if (destIdx <= 0)
                    destIdx = rec.fallbackIdx > tocPageIdxFinal
                        ? rec.fallbackIdx + tocContPages.Count
                        : rec.fallbackIdx;
                if (destIdx <= 0) continue;

                // IsCountTocPages=false: the printed numbers skip the TOC chain
                // itself (TOC page + its continuations) — the first content page
                // after a one-page TOC prints as "1" — while the GoTo link keeps
                // the physical index.
                var displayIdx = destIdx;
                if (tocPage.TocInfo?.IsCountTocPages == false)
                {
                    var chainEnd = tocPageIdxFinal + tocContPages.Count;
                    var sub = destIdx > chainEnd ? tocContPages.Count + 1
                        : destIdx >= tocPageIdxFinal ? destIdx - tocPageIdxFinal + 1 : 0;
                    if (destIdx - sub >= 1) displayIdx = destIdx - sub;
                }
                var pageNumStr = displayIdx.ToString(System.Globalization.CultureInfo.InvariantCulture);
                // Entries whose segment declared its own font measure through that
                // font's real advances (see the layout-side comment).
                double M(string s) => rec.measure?.Invoke(s)
                    ?? MeasureEntry(s, rec.entrySize, rec.entryFace);
                var pageNumWidth = M(pageNumStr);
                // The leader fill glyph: '.' for a Dot leader, '_' for a Solid one
                // (underscore advances abut, so the run reads as a continuous rule).
                var leaderChar = rec.leader == Text.TabLeaderType.Solid ? '_' : '.';
                var dotW = M(leaderChar.ToString());
                var leaderFontRes = rec.entryFace == "Helvetica"
                    ? tocFontName : Table.RegisterFont(target, rec.entryFace);
                var lb = new Content.ContentStreamBuilder();
                // The entry's underlined variant draws filled 0.5 pt rectangles
                // under each run (prefix at natural width, text to its end).
                if (rec.underline)
                {
                    var uy = rec.lastY - 0.207 * rec.entrySize + 0.207;
                    var prefixNat = MeasureEntry(rec.prefix, rec.entrySize, rec.entryFace);
                    if (prefixNat > 0)
                        lb.SaveState().SetFillColor(0, 0, 0)
                            .Rectangle(rec.x0, uy, prefixNat, 0.5).FillEvenOdd().RestoreState();
                    lb.SaveState().SetFillColor(0, 0, 0)
                        .Rectangle(rec.x0 + prefixNat, uy, rec.textEnd - rec.x0 - prefixNat, 0.5)
                        .FillEvenOdd().RestoreState();
                }

                // One text show at (x, lastY), horizontally scaled by tz percent.
                // CJK runs (no glyphs in the Standard-14 set) embed a script-matched
                // face and show as CID hex, exactly like the entry's earlier lines.
                void LeaderShow(double x, string s, double tz)
                {
                    if (s.Length == 0) return;
                    var scaled = System.Math.Abs(tz - 100) > 1e-9;
                    if (s.Length > 0 && ContainsCjkText(s)
                        && Text.CjkFallbackFont.ResolveEmbeddableBytes(s) is { Length: > 0 } cjkTtf)
                    {
                        var cjkDict = Table.ResolvePageFontDict(target);
                        var (cjkRes, cjkHex) = Text.Type0FontEmbedder.Embed(
                            cjkDict, cjkTtf, "CJK", s.Replace('\t', ' '),
                            stripSpacesInBaseFont: true);
                        lb.BeginText().SetFont(cjkRes, rec.entrySize).SetFillColor(0, 0, 0)
                            .SetTextMatrix(1, 0, 0, 1, x, rec.lastY);
                        if (scaled) lb.SetHorizontalScaling(tz);
                        lb.ShowTextHex(cjkHex);
                        // Tz is graphics state — reset before closing the block.
                        if (scaled) lb.SetHorizontalScaling(100);
                        lb.EndText();
                        return;
                    }
                    lb.BeginText().SetFont(leaderFontRes, rec.entrySize).SetFillColor(0, 0, 0)
                        .SetTextMatrix(1, 0, 0, 1, x, rec.lastY);
                    if (scaled) lb.SetHorizontalScaling(tz);
                    lb.ShowText(s);
                    if (scaled) lb.SetHorizontalScaling(100);
                    lb.EndText();
                }

                if (rec.lastLine.Length > 0 && dotW > 0)
                {
                    // The entry's final line is drawn HERE, horizontally scaled to fit
                    // the column: the numbering prefix paints unscaled at the line
                    // start, while the text, the leader dots and the page number
                    // carry one shared Tz. With
                    //   q = (colW − prefixW − textW − numW) / dotW,
                    //   D = round(q) − 1,
                    //   Tz = 100·colW / (prefixW + textW + D·dotW + numW),
                    // the dots+number show starts flush at the SCALED text end — so
                    // RAW extraction reads "…Heading 1....2" with no space, and a
                    // CJK entry's dots begin just right of where its natural width
                    // ends. The prefix sits in the denominator but paints unscaled
                    // (numbered lines end a hair short of the right stop).
                    var linePrefix = rec.prefix.Length > 0
                        && rec.lastLine.StartsWith(rec.prefix, StringComparison.Ordinal)
                        ? rec.prefix : string.Empty;
                    var lineText = rec.lastLine.Substring(linePrefix.Length);
                    var prefixW = M(linePrefix);
                    var textW = M(lineText);
                    var colW = rec.rightStop - rec.lastX;
                    var q = (colW - prefixW - textW - pageNumWidth) / dotW;
                    var dotCount = (int)System.Math.Round(q, System.MidpointRounding.AwayFromZero) - 1;
                    if (dotCount < 0) dotCount = 0;
                    var natural = prefixW + textW + dotCount * dotW + pageNumWidth;
                    var tz = natural > 0 ? 100.0 * colW / natural : 100.0;
                    LeaderShow(rec.lastX + prefixW, lineText, tz);
                    LeaderShow(rec.lastX + prefixW + textW * tz / 100.0,
                        new string(leaderChar, dotCount) + pageNumStr, tz);
                }
                else
                {
                    // Legacy right-aligned model for the paths the scaled draw does
                    // not cover: TabLeaderType.None keeps the page number alone on
                    // the column stop; IsShowPageNumbers=false closes the entry with
                    // an empty show at the text end (as the fragment path does); an underlined
                    // entry keeps its natural-width text (drawn with the entry) and
                    // right-aligns its leader.
                    var remainder = rec.rightStop - rec.textEnd - pageNumWidth;
                    var dotCount = dotW > 0 && remainder > 0
                        ? (int)System.Math.Round(remainder / dotW, System.MidpointRounding.AwayFromZero) - 1
                        : 0;
                    if (dotCount < 0) dotCount = 0;
                    if (rec.leader == Text.TabLeaderType.None) dotCount = 0;
                    var leaderText = rec.showNumbers
                        ? new string(leaderChar, dotCount) + pageNumStr : string.Empty;
                    var drawStart = rec.showNumbers
                        ? rec.rightStop - pageNumWidth - dotCount * dotW : rec.textEnd;
                    lb.BeginText().SetFont(leaderFontRes, rec.entrySize).SetFillColor(0, 0, 0)
                        .SetTextMatrix(1, 0, 0, 1, drawStart, rec.lastY)
                        .ShowText(leaderText)
                        .EndText();
                }
                target.AddContentStream(lb.Build());

                // The GoTo destination is the top-left of the target page
                // (0, page-height) mapped back through its rotation —
                // matching the heading-branch links, so validators that
                // expect the corner destination accept the TOC link too.
                var destPage = Pages.At(destIdx);
                var destRect = destPage.GetPageRect(true);
                var (destLeft, destTop) = destPage.RotationMatrix
                    .InverseTransformPoint(0, destRect.Height);
                target.Annotations.AddLinkAnnotation(rec.linkRect,
                    new Aspose.Pdf.Annotations.GoToAction(
                        new Aspose.Pdf.Annotations.XYZExplicitDestination(destIdx, destLeft, destTop, 0)));
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
        if (page.ExplicitBackground is { } pageBg && !page.BackgroundApplied)
        {
            page.BackgroundApplied = true;
            page.RemoveTaggedBackground();
            var isWhite = pageBg.R == 255 && pageBg.G == 255 && pageBg.B == 255;
            if (!isWhite)
            {
                var box = page.MediaBox;
                var bgBuilder = new Content.ContentStreamBuilder();
                bgBuilder.BeginMarkedContent(Page.BackgroundMarkerTag);
                bgBuilder.SaveState();
                bgBuilder.SetFillColor(pageBg.R / 255.0, pageBg.G / 255.0, pageBg.B / 255.0);
                bgBuilder.Rectangle(box.LLX, box.LLY, box.Width, box.Height);
                bgBuilder.Fill();
                bgBuilder.RestoreState();
                bgBuilder.EndMarkedContent();
                page.PrependContentStream(bgBuilder.Build());
            }
        }

        // Materialise /AP appearances for annotations that lack one so they render
        // (the renderer draws Line/Polygon/Polyline only from their /AP) and expose
        // NormalAppearance after save.
        foreach (var annot in page.Annotations)
        {
            if (annot is Annotations.FreeTextAnnotation freeText)
                freeText.GenerateAppearance();
            else if (annot is Annotations.LineAnnotation or Annotations.PolygonAnnotation
                            or Annotations.PolylineAnnotation or Annotations.SquareAnnotation
                            or Annotations.CircleAnnotation or Annotations.TextAnnotation
                     && annot.NormalAppearance is null)
                annot.UpdateAppearances();
        }

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
            page.HeaderFooterApplied = true;
            // Header paragraphs — text, HTML and Table alike — render on every
            // page that references them, whether laid out by the generator or
            // imported with its own content (their cells stay text-extractable
            // and any widgets/links bind to the page they land on).
            //
            // A FOOTER table is the one case that is gated: one HeaderFooter
            // instance shared across the already-inked static pages of an
            // imported document draws no footer table (a footer table stamped
            // onto every page of a loaded document is dropped). It still draws
            // on a generator-laid-out page, and a footer owned by a single page
            // draws normally; text/HTML footer fragments always render.
            bool FooterDrawsTables()
            {
                if (page.Paragraphs.Count > 0 || page.TocInfo is not null
                    || (page.GetContentStreamBytes()?.Length ?? 0) == 0)
                    return true;
                var footer = page.Footer;
                var refs = 0;
                for (var pi2 = 1; pi2 <= Pages.Count; pi2++)
                {
                    if (ReferenceEquals(Pages[pi2].Footer, footer)) refs++;
                    if (refs > 1) return false;
                }
                return true;
            }
            page.Header?.RenderToPage(page, isHeader: true, page.Number, this);
            page.Footer?.RenderToPage(page, isHeader: false, page.Number, this,
                FooterDrawsTables());
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
