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
    /// <summary>Point size of a table-of-contents entry whose heading carries no size of its own.</summary>
    private const double DefaultTocEntrySize = 10.0;

    /// <summary>Lays out a page's table of contents and paragraphs: margins and cursor, the TOC entries, then the paragraph flow.</summary>
    private void LayoutPageContent(Page page, PageContentState pc)
    {
        var pl = new PageLayoutState();
        pl.page = page;
        pl.fontName = null;
        pl.pageMargin = page.PageInfo?.Margin;
        pl.docMargin = PageInfo?.Margin;
        pl.marginTop = pl.pageMargin?.TopTouched    == true ? pl.pageMargin!.Top    : pl.docMargin?.TopTouched    == true ? pl.docMargin!.Top    : 72;
        pl.marginBottom = pl.pageMargin?.BottomTouched == true ? pl.pageMargin!.Bottom : pl.docMargin?.BottomTouched == true ? pl.docMargin!.Bottom : 72;
        pl.marginLeft = pl.pageMargin?.LeftTouched   == true ? pl.pageMargin!.Left   : pl.docMargin?.LeftTouched   == true ? pl.docMargin!.Left   : 90;
        pl.marginRight = pl.pageMargin?.RightTouched  == true ? pl.pageMargin!.Right  : pl.docMargin?.RightTouched  == true ? pl.docMargin!.Right  : 90;
        pl.layoutTopY = page.LayoutFrameHeight;
        if (page.RotateDegrees % 360 == 0)
        {
            var cropForLayout = page.CropBox;
            var mediaForLayout = page.MediaBox;
            if (cropForLayout.URY < mediaForLayout.URY - 0.01
                || cropForLayout.LLY > mediaForLayout.LLY + 0.01)
                pl.layoutTopY = cropForLayout.URY;
        }
        pl.curY = page.LayoutCursorY ?? (pl.layoutTopY - pl.marginTop);
        // A reopened page with IsAddParagraphsAfterLast has no persisted
        // layout cursor; resume below the lowest existing text so the new
        // paragraphs stack under the earlier content instead of over it.
        if (page.LayoutCursorY is null && page.IsAddParagraphsAfterLast)
        {
            try
            {
                var resumeAbs = new Text.TextFragmentAbsorber();
                resumeAbs.Visit(page);
                double lowest = double.MaxValue;
                foreach (Text.TextFragment fr in resumeAbs.TextFragments)
                    if (fr.Rectangle is { } fRect && fRect.LLY < lowest)
                        lowest = fRect.LLY;
                if (lowest < double.MaxValue && lowest < pl.curY)
                    pl.curY = lowest;
            }
            catch { /* unreadable content: keep the top-margin cursor */ }
        }

        pl.tocEntries = page.TocInfo is not null
            ? CollectTocHeadings(page)
            : new System.Collections.Generic.List<(Heading h, int pageIdx)>();
        pl.tocRendered = new System.Collections.Generic.HashSet<Heading>(ReferenceEqualityComparer.Instance);
        pl.tocCol = 0;
        pl.tocSlot = 0;
        pl.tocPending = new System.Collections.Generic.List<(int slot, byte[] preLeader,
            double textEnd, double lastY, double entrySize, string entryFace,
            Text.TabLeaderType leader, double rightStop,
            bool showNumbers, bool underline, string prefix, double x0,
            Page? destPage, int fallbackIdx, Rectangle linkRect, Heading heading,
            string lastLine, double lastX, System.Func<string, double>? measure)>();
        pl.tocTopY = null;
        pl.tocCounters = new int[12];
        pl.tocCjkTtf = null;
        pl.tocColCount = 1;
        pl.tocColLefts = System.Array.Empty<double>();
        pl.tocColWidths = System.Array.Empty<double>();
        LayoutTocHeader(page, pc, pl);

        pl.flow = new FlowLayout(page, pc.overflowPages, pl.marginLeft, pl.marginRight, pl.marginTop, pl.marginBottom, pl.curY, EnableNotificationLogging);
        pl.flowSlotStart = pc.overflowPages.Count;
        // A page with an OnBeforePageGenerate handler: every page the flow
        // generates from it gets the handler BEFORE its content is laid
        // (a provisional page carrying the source page's PageInfo and the
        // expected number), so a handler that re-margins or re-heads page 2
        // shapes page 2's layout. The materialised page adopts that state.
        if (page.HasBeforePageGenerate)
        {
            var sourcePage = page;
            pl.flow.OnPageBreak = slot =>
            {
                var prepared = sourcePage.CreateDetachedSibling();
                prepared.SetIndex(sourcePage.Index + (slot - pl.flowSlotStart) + 1);
                prepared.InheritPageInfoFrom(sourcePage);
                prepared.Header = sourcePage.Header;
                prepared.Footer = sourcePage.Footer;
                prepared.CopyBeforePageGenerateFrom(sourcePage);
                prepared.RaiseBeforePageGenerate();
                _preparedOverflowPages[slot] = prepared;
                var pm = prepared.PageInfo.Margin;
                return (pm.TopTouched ? pm.Top : pl.flow.ContentTopMargin,
                        pm.BottomTouched ? pm.Bottom : pl.flow.BottomMargin);
            };
        }
        pl.tb = new Text.TextBuilder(page);
        pl.pendingInlineLineHeight = 0;
        pl.renderedTables = new HashSet<Table>(ReferenceEqualityComparer.Instance);
        pl.paraList = page.Paragraphs.ToList();
        for (var paraIdx = 0; paraIdx < pl.paraList.Count; paraIdx++)
        {
            var para = pl.paraList[paraIdx];
            // Close an open inline image line before any paragraph that isn't
            // itself an inline image — the cursor drops by the tallest inline
            // image so this paragraph starts on the next line.
            if (pl.pendingInlineLineHeight > 0
                && !(para is Image inlineImg && inlineImg.IsInLineParagraph))
            {
                pl.flow.AdvanceY(pl.pendingInlineLineHeight);
                pl.pendingInlineLineHeight = 0;
            }

            // IsInNewPage forces this paragraph to start on a fresh overflow
            // page, regardless of remaining room on the current one. Honors
            // the BaseParagraph.IsInNewPage flag set on headings, paragraphs,
            // and tables when the caller wants explicit pagination.
            // ForceNewPage (eager) is required for renderers that bypass the
            // Y cursor (Heading.Build draws at the supplied Y verbatim, so
            // just resetting the cursor would leave the heading on the
            // current page).
            if (ParagraphIsInNewPage(para) && para != page.Paragraphs[0])
                pl.flow.ForceNewPage();

            // Record this paragraph's starting position so a later
            // LocalHyperlink that targets it (e.g. LocalHyperlink(head))
            // can resolve to the right page + y after overflow pages
            // have been added to the document.
            pl.flow.RecordPosition(para);

            if (TryLayoutInlineChain(pl.paraList, ref paraIdx, para, pl.flow)) continue;
            if (TryLayoutInlineJoinedRun(pl.paraList, ref paraIdx, para, pl.flow)) continue;

            if (para is Text.TextFragment tf)
            {
                // NoCharacterAction.ReplaceFonts (explicit): substitute a glyph-covering
                // face before layout when the fragment's font can't show its text —
                // registered sources first, then host Arial, then a system CJK face.
                if (tf.HasExplicitReplaceFonts &&
                    Text.FontRepository.SubstituteForMissingGlyphs(tf.Text, tf.TextState.Font) is { } replaceFace)
                    tf.TextState.Font = replaceFace;
                // Arabic/RTL text: shape into contextual presentation forms and route
                // through an Arabic-capable embedded font (the default Standard-14 font
                // has no Arabic coverage and the renderer applies no OpenType shaping).
                ShapeArabicForGenerator(tf);
                // Replace page number macros
                if (tf.Text.Contains("$p") || tf.Text.Contains("$P"))
                {
                    tf.Text = tf.Text
                        .Replace("$p", page.Number.ToString())
                        .Replace("$P", PageCount.ToString());
                }
                // IsKeptWithNext + a following Table: when the pair no longer
                // fits the space left on this page (but would fit a fresh one)
                // both move together — the new page starts with
                // the fragment at the content top (its top margin is consumed
                // by the break).
                if (tf.IsKeptWithNext && paraIdx + 1 < pl.paraList.Count
                    && pl.paraList[paraIdx + 1] is Table keptTable)
                {
                    var keptH = keptTable.GetHeight();
                    var tfH = (tf.TextState.FontSize > 0 ? tf.TextState.FontSize : 12)
                        + (tf.Margin?.Top ?? 0) + (tf.Margin?.Bottom ?? 0);
                    if (pl.flow.CurrentY - tfH - keptH < pl.flow.BottomMargin
                        && tfH + keptH <= pl.flow.ContentTop - pl.flow.BottomMargin + 0.5)
                    {
                        pl.flow.ForceNewPage();
                        // The fragment keeps its top margin on the new page
                        // (the kept title sits one Margin.Top below
                        // the content top, not flush against it).
                    }
                }
                // A fragment's own top margin is vertical space reserved above it;
                // dropping the cursor by it (which paginates when it overflows the
                // page) is what places a fragment with a large Margin.Top onto a
                // later page instead of pinning it to the current one.
                var tfTopMargin = tf.Margin?.Top ?? 0;
                if (tfTopMargin > 0) pl.flow.AdvanceY(tfTopMargin);
                if (!pl.flow.WriteTextFragment(tf))
                {
                    // Flow layout declined (e.g. explicit Position or embedded font) —
                    // fall back to the legacy fixed-position writer. Assign a default
                    // layout position only when the caller didn't set one (the Position
                    // getter is never null now, so test HasExplicitPosition, not `??=`).
                    if (!tf.HasExplicitPosition)
                        tf.Position = new Text.Position(
                            pl.marginLeft, page.Height - pl.marginTop - (tf.TextState.FontSize > 0 ? tf.TextState.FontSize : 12));
                    pl.tb.AppendTextInline(tf);
                }
                var tfBottomMargin = tf.Margin?.Bottom ?? 0;
                if (tfBottomMargin > 0) pl.flow.AdvanceY(tfBottomMargin);
                // FootNote marker + page-bottom band are emitted inside
                // WriteTextFragment (the marker needs the last line's
                // measured end position).
            }
            else if (para is HtmlFragment html)
            {
                LayoutHtmlFragmentParagraph(html, pl.flow, page, pl.tb, pl.renderedTables, pc.overflowPages, pc.overflowImages, pl.marginLeft, pl.marginRight, pl.marginTop, pl.marginBottom);
            }
            else if (para is Table table)
            {
                LayoutTableParagraph(table, pl.flow, page, pl.renderedTables, pc.overflowPages, pc.overflowImages, pl.marginLeft, pl.marginTop);
            }
            else if (para is FloatingBox fbox)
            {
                LayoutFloatingBoxParagraph(fbox, pl.flow, page, pl.tocEntries, pl, pc.headingAutoCounters, ref pl.fontName, pc.overflowPages, pl.marginLeft, pl.marginRight, pl.marginBottom, pl.marginTop);
            }
            else if (para is Heading heading)
            {
                LayoutHeadingParagraph(heading, pl.flow, page, pl.tocEntries, pl, pc.headingAutoCounters, ref pl.fontName, pl.marginLeft, pl.marginRight);
            }
            else if (para is Image img)
            {
                LayoutImageParagraph(img, pl.flow, page, ref pl.pendingInlineLineHeight, pl.marginLeft, pl.marginRight, pl.marginTop, pl.marginBottom);
            }
            else if (para is Aspose.Pdf.Drawing.Graph graph)
            {
                LayoutGraphParagraph(graph, pl.flow, page, pl.marginLeft, pl.marginTop, pl.marginBottom);
            }
            else if (para is Forms.Field fieldPara)
            {
                // A form field added through Paragraphs is a block of its
                // layout size at the flow cursor; the widget is bound to
                // the page its block landed on (and registered on the
                // AcroForm by RegisterGeneratedFormFields).
                var (fw, fh) = fieldPara.GeneratorBlockSize();
                pl.flow.PlaceFieldBlock(fieldPara, fw, fh);
            }
            else if (para is Annotations.Annotation annPara)
            {
                // An annotation added through Paragraphs.Add flows like a block
                // paragraph: it reserves its Width×Height at the left content
                // edge with no inter-paragraph gap (Line/Ink rectangles derive
                // from their authored geometry via the ctor), and binds to the
                // page its block landed on once overflow slots materialise.
                pl.flow.PlaceAnnotationBlock(annPara, annPara.Width, annPara.Height);
            }
        }
        if (pl.pendingInlineLineHeight > 0)
        {
            pl.flow.AdvanceY(pl.pendingInlineLineHeight);
            pl.pendingInlineLineHeight = 0;
        }
        // TOC entries whose headings live on OTHER pages (they are not
        // paragraphs of this page, so the flow above never reached them):
        // append them after the page's own content, chaining the cursor.
        foreach (var (h, dIdx) in pl.tocEntries)
        {
            if (pl.tocRendered.Contains(h)) continue;
            var yAfter = RenderTocEntry(pl, h, dIdx, pl.flow.CurrentY);
            pl.flow.AdvanceY(pl.flow.CurrentY - yAfter);
        }
        // Materialise the TOC: insert the continuation pages the overflow
        // asked for right AFTER the TOC page (an overlong TOC
        // splits across pages inserted in place, shifting the
        // content pages down), then emit every buffered entry — its
        // pre-leader shows first, then the leader built against the FINAL
        // page numbering (dots + destination number as one show flush at
        // the text end, right edge on the column stop, floor dot count,
        // slack spread as character spacing), then its link annotation.
        if (pl.tocPending.Count > 0)
        {
            var tocContPages = new List<Page>();
            if (pl.tocSlot > 0)
            {
                var tocPageIdx = Pages.IndexOf(page);
                for (var ci = 1; ci <= pl.tocSlot; ci++)
                {
                    var cont = Pages.Insert(tocPageIdx + ci, page.Width, page.Height);
                    // The buffered entry bytes reference the TOC page's font
                    // resource name — register Helvetica on the continuation
                    // page and alias it under that exact name if the fresh
                    // page happened to assign a different one.
                    var contFontName = Table.RegisterFont(cont);
                    if (pl.fontName is not null && contFontName != pl.fontName)
                    {
                        var contRes = cont.Reader.ResolveDict(cont.Dict.Get("Resources"))!;
                        var contFonts = cont.Reader.ResolveDict(contRes.Get("Font"))!;
                        contFonts.Set(pl.fontName, contFonts.Get(contFontName)!);
                    }
                    cont.LayoutApplied = true;
                    tocContPages.Add(cont);
                }
            }
            pc.pendingTocEmits.Add((page, pl.fontName!, tocContPages, pl.tocPending));
        }
        pl.flow.Commit();
        // FinaliseFootnotes runs before slotEnd capture so its spillover
        // pages (added via _overflowPages) extend this flow's slot range.
        pl.flow.FinaliseNoteBands();
        pc.pendingFlows.Add((pl.flow, pl.flowSlotStart, pc.overflowPages.Count));
        // Remember where this pass left the cursor: on the original page when
        // the flow never spilled, otherwise on the flow's final overflow slot
        // (persisted onto the Page object when the slot materialises below).
        if (pl.flow.CurrentSlot < 0)
            page.LayoutCursorY = pl.flow.CurrentY;
        page.Paragraphs.Clear();
    }
}
