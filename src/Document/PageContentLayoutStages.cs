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
    /// <summary>Page content layout stages: layout preparation, one paragraph placed, the TOC entries placed.</summary>
    private void PlaceTocEntries(PageContentLayoutState lc)
    {
        foreach (var (h, dIdx) in lc.pl.tocEntries)
        {
            if (lc.pl.tocRendered.Contains(h)) continue;
            var yAfter = RenderTocEntry(lc.pl, h, dIdx, lc.pl.flow.CurrentY);
            lc.pl.flow.AdvanceY(lc.pl.flow.CurrentY - yAfter);
        }
        // Materialise the TOC: insert the continuation pages the overflow
        // asked for right AFTER the TOC page (an overlong TOC
        // splits across pages inserted in place, shifting the
        // content pages down), then emit every buffered entry — its
        // pre-leader shows first, then the leader built against the FINAL
        // page numbering (dots + destination number as one show flush at
        // the text end, right edge on the column stop, floor dot count,
        // slack spread as character spacing), then its link annotation.
        if (lc.pl.tocPending.Count > 0)
        {
            var tocContPages = new List<Page>();
            if (lc.pl.tocSlot > 0)
            {
                var tocPageIdx = Pages.IndexOf(lc.page);
                for (var ci = 1; ci <= lc.pl.tocSlot; ci++)
                {
                    var cont = Pages.Insert(tocPageIdx + ci, lc.page.Width, lc.page.Height);
                    // The buffered entry bytes reference the TOC page's font
                    // resource name — register Helvetica on the continuation
                    // page and alias it under that exact name if the fresh
                    // page happened to assign a different one.
                    var contFontName = Table.RegisterFont(cont);
                    if (lc.pl.fontName is not null && contFontName != lc.pl.fontName)
                    {
                        var contRes = cont.Reader.ResolveDict(cont.Dict.Get("Resources"))!;
                        var contFonts = cont.Reader.ResolveDict(contRes.Get("Font"))!;
                        contFonts.Set(lc.pl.fontName, contFonts.Get(contFontName)!);
                    }
                    cont.LayoutApplied = true;
                    tocContPages.Add(cont);
                }
            }
            lc.pc.pendingTocEmits.Add((lc.page, lc.pl.fontName!, tocContPages, lc.pl.tocPending));
        }
    }

    /// <summary></summary>
    private bool LayoutParagraphAt(PageContentLayoutState lc)
    {
        var para = lc.pl.paraList[lc.paraIdx];
        // Close an open inline image line before any paragraph that isn't
        // itself an inline image — the cursor drops by the tallest inline
        // image so this paragraph starts on the next line.
        if (lc.pl.pendingInlineLineHeight > 0
            && !(para is Image inlineImg && inlineImg.IsInLineParagraph))
        {
            lc.pl.flow.AdvanceY(lc.pl.pendingInlineLineHeight);
            lc.pl.pendingInlineLineHeight = 0;
        }

        // IsInNewPage forces this paragraph to start on a fresh overflow
        // page, regardless of remaining room on the current one. Honors
        // the BaseParagraph.IsInNewPage flag set on headings, paragraphs,
        // and tables when the caller wants explicit pagination.
        // ForceNewPage (eager) is required for renderers that bypass the
        // Y cursor (Heading.Build draws at the supplied Y verbatim, so
        // just resetting the cursor would leave the heading on the
        // current page).
        if (ParagraphIsInNewPage(para) && para != lc.page.Paragraphs[0])
            lc.pl.flow.ForceNewPage();
        // A kept-with-next fragment whose follower opens a new page cannot stay
        // with it; the reference engine moves the fragment onto a fresh page of its
        // own first (probed: a kept note before a new-page table takes a page alone).
        if (para is HtmlFragment { IsKeptWithNext: true } && lc.paraIdx + 1 < lc.pl.paraList.Count
            && ParagraphIsInNewPage(lc.pl.paraList[lc.paraIdx + 1]) && para != lc.page.Paragraphs[0])
            lc.pl.flow.ForceNewPage();

        // Record this paragraph's starting position so a later
        // LocalHyperlink that targets it (e.g. LocalHyperlink(head))
        // can resolve to the right page + y after overflow pages
        // have been added to the document.
        lc.pl.flow.RecordPosition(para);

        if (TryLayoutInlineChain(lc, para)) return true;
        if (TryLayoutInlineJoinedRun(lc, para)) return true;

        if (para is Text.TextFragment tf)
        {
            LayoutTextFragmentAt(lc, tf);
            // measured end position).
        }
        else if (para is HtmlFragment html)
        {
            LayoutHtmlFragmentParagraph(html, lc.pl.flow, lc.page, lc.pl.tb, lc.pl.renderedTables, lc.pc.overflowPages, lc.pc.overflowImages, lc.pl.marginLeft, lc.pl.marginRight, lc.pl.marginTop, lc.pl.marginBottom);
        }
        else if (para is Table table)
        {
            LayoutTableParagraph(table, lc.pl.flow, lc.page, lc.pl.renderedTables, lc.pc.overflowPages, lc.pc.overflowImages, lc.pl.marginLeft, lc.pl.marginTop);
        }
        else if (para is FloatingBox fbox)
        {
            LayoutFloatingBoxParagraph(fbox, lc.pl.flow, lc.page, lc.pl.tocEntries, lc.pl, lc.pc.headingAutoCounters, lc.pc.overflowPages, lc.pl.marginLeft, lc.pl.marginRight, lc.pl.marginBottom, lc.pl.marginTop);
        }
        else if (para is Heading heading)
        {
            LayoutHeadingParagraph(heading, lc.pl.flow, lc.page, lc.pl.tocEntries, lc.pl, lc.pc.headingAutoCounters, lc.pl.marginLeft, lc.pl.marginRight);
        }
        else if (para is ListBlock list)
        {
            LayoutListBlockParagraph(lc, list);
        }
        else if (para is BoxBlock box)
        {
            LayoutBoxBlockParagraph(lc, box);
        }
        else if (para is ReservedBlock reserved)
        {
            LayoutReservedBlockParagraph(lc, reserved);
        }
        else if (para is Image img)
        {
            var (imgLeft, imgRight) = RegionMargins(lc);
            LayoutImageParagraph(img, lc.pl.flow, lc.page, lc.pl, imgLeft, imgRight, lc.pl.marginTop, lc.pl.marginBottom);
        }
        else if (para is Aspose.Pdf.Drawing.Graph graph)
        {
            LayoutGraphParagraph(graph, lc.pl.flow, lc.page, lc.pl.marginLeft, lc.pl.marginTop, lc.pl.marginBottom);
        }
        else if (para is Forms.Field fieldPara)
        {
            // A form field added through Paragraphs is a block of its
            // layout size at the flow cursor; the widget is bound to
            // the page its block landed on (and registered on the
            // AcroForm by RegisterGeneratedFormFields).
            var (fw, fh) = fieldPara.GeneratorBlockSize();
            lc.pl.flow.PlaceFieldBlock(fieldPara, fw, fh);
        }
        else if (para is Annotations.Annotation annPara)
        {
            // An annotation added through Paragraphs.Add flows like a block
            // paragraph: it reserves its Width×Height at the left content
            // edge with no inter-paragraph gap (Line/Ink rectangles derive
            // from their authored geometry via the ctor), and binds to the
            // page its block landed on once overflow slots materialise.
            lc.pl.flow.PlaceAnnotationBlock(annPara, annPara.Width, annPara.Height);
        }
        return true;
    }

    /// <summary></summary>
    private void PrepareLayout(PageContentLayoutState lc)
    {
        if (lc.page.RotateDegrees % 360 == 0)
        {
            var cropForLayout = lc.page.CropBox;
            var mediaForLayout = lc.page.MediaBox;
            if (cropForLayout.URY < mediaForLayout.URY - 0.01
                || cropForLayout.LLY > mediaForLayout.LLY + 0.01)
                lc.pl.layoutTopY = cropForLayout.URY;
        }
        lc.pl.curY = lc.page.LayoutCursorY ?? (lc.pl.layoutTopY - lc.pl.marginTop);
        // A reopened page with IsAddParagraphsAfterLast has no persisted
        // layout cursor; resume below the lowest existing text so the new
        // paragraphs stack under the earlier content instead of over it.
        if (lc.page.LayoutCursorY is null && lc.page.IsAddParagraphsAfterLast)
        {
            try
            {
                var resumeAbs = new Text.TextFragmentAbsorber();
                resumeAbs.Visit(lc.page);
                double lowest = double.MaxValue;
                foreach (Text.TextFragment fr in resumeAbs.TextFragments)
                    if (fr.Rectangle is { } fRect && fRect.LLY < lowest)
                        lowest = fRect.LLY;
                if (lowest < double.MaxValue && lowest < lc.pl.curY)
                    lc.pl.curY = lowest;
            }
            catch { /* unreadable content: keep the top-margin cursor */ }
        }

        lc.pl.tocEntries = lc.page.TocInfo is not null
            ? CollectTocHeadings(lc.page)
            : new System.Collections.Generic.List<(Heading h, int pageIdx)>();
        lc.pl.tocRendered = new System.Collections.Generic.HashSet<Heading>(ReferenceEqualityComparer.Instance);
        lc.pl.tocCol = 0;
        lc.pl.tocSlot = 0;
        lc.pl.tocPending = new System.Collections.Generic.List<(int slot, byte[] preLeader,
            double textEnd, double lastY, double entrySize, string entryFace,
            Text.TabLeaderType leader, double rightStop,
            bool showNumbers, bool underline, string prefix, double x0,
            Page? destPage, int fallbackIdx, Rectangle linkRect, Heading heading,
            string lastLine, double lastX, System.Func<string, double>? measure)>();
    }

    /// <summary></summary>
    private void LayoutTextFragmentAt(PageContentLayoutState lc, Text.TextFragment tf)
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
                .Replace("$p", lc.page.Number.ToString())
                .Replace("$P", PageCount.ToString());
        }
        // IsKeptWithNext + a following Table: when the pair no longer
        // fits the space left on this page (but would fit a fresh one)
        // both move together — the new page starts with
        // the fragment at the content top (its top margin is consumed
        // by the break).
        if (tf.IsKeptWithNext && lc.paraIdx + 1 < lc.pl.paraList.Count
            && lc.pl.paraList[lc.paraIdx + 1] is Table keptTable)
        {
            var keptH = keptTable.GetHeight();
            var tfH = (tf.TextState.FontSize > 0 ? tf.TextState.FontSize : 12)
                + (tf.Margin?.Top ?? 0) + (tf.Margin?.Bottom ?? 0);
            if (lc.pl.flow.CurrentY - tfH - keptH < lc.pl.flow.BottomMargin
                && tfH + keptH <= lc.pl.flow.ContentTop - lc.pl.flow.BottomMargin + 0.5)
            {
                lc.pl.flow.ForceNewPage();
                // The fragment keeps its top margin on the new page
                // (the kept title sits one Margin.Top below
                // the content top, not flush against it).
            }
        }
        // IsKeptWithNext + a following paragraph: the next one must START on the
        // page this one ends on. When this one's lines and the next one's first
        // line no longer fit the room (but would fit a fresh page), this one moves.
        // Judged pairwise, at this point: where the next one goes later is its own.
        else if (tf.IsKeptWithNext && lc.paraIdx + 1 < lc.pl.paraList.Count
            && lc.pl.paraList[lc.paraIdx + 1] is Text.TextFragment keptNext)
        {
            var keptH = (keptNext.Margin?.Top ?? 0) + FlowLayout.FirstLineNeedOf(keptNext);
            var tfH = lc.pl.flow.LinesHeightOf(tf) + (tf.Margin?.Top ?? 0) + (tf.Margin?.Bottom ?? 0);
            if (lc.pl.flow.CurrentY - tfH - keptH < lc.pl.flow.BottomMargin
                && tfH + keptH <= lc.pl.flow.ContentTop - lc.pl.flow.BottomMargin + 0.5)
                lc.pl.flow.ForceNewPage();
        }
        // IsKeptTogether: lines that would split at the page bottom start on the
        // next page instead, when they fit an empty one at all.
        if (tf.IsKeptTogether)
        {
            var wholeH = lc.pl.flow.WholeNeedOf(tf);
            if (lc.pl.flow.CurrentY - wholeH < lc.pl.flow.BottomMargin
                && wholeH <= lc.pl.flow.ContentTop - lc.pl.flow.BottomMargin + 0.5)
                lc.pl.flow.ForceNewPage();
        }
        // A fragment's own top margin is vertical space reserved above it;
        // dropping the cursor by it (which paginates when it overflows the
        // page) is what places a fragment with a large Margin.Top onto a
        // later page instead of pinning it to the current one.
        //
        // The margin and the first line are ONE UNIT: reserving them together
        // means a paragraph that cannot fit both breaks FIRST and opens the
        // next page with its margin, instead of spending the margin at the
        // bottom of the page it is leaving and arriving flush against the
        // content top. That is the rule ReserveTopMargin already states and
        // the floating-box path already follows; this is the same rule on the
        // plain paragraph path, which had only the bare AdvanceY.
        //
        // For a paragraph that fits, this is identical to what came before:
        // the reservation is a no-op and only the AdvanceY runs. It differs
        // only for a paragraph landing ON a page boundary.
        var tfTopMargin = tf.Margin?.Top ?? 0;
        var heldReserve = lc.pl.flow.BottomReserve;
        if (tf.TextState.FormattingOptions is { BottomMarginInsideRegion: true })
            lc.pl.flow.BottomReserve = Math.Max(0, tf.Margin?.Bottom ?? 0);
        if (tfTopMargin > 0) lc.pl.flow.ReserveTopMargin(tfTopMargin, tf);
        var written = lc.pl.flow.WriteTextFragment(tf);
        lc.pl.flow.BottomReserve = heldReserve;
        if (!written)
        {
            // Flow layout declined (e.g. explicit Position or embedded font) —
            // fall back to the legacy fixed-position writer. Assign a default
            // layout position only when the caller didn't set one (the Position
            // getter is never null now, so test HasExplicitPosition, not `??=`).
            if (!tf.HasExplicitPosition)
                tf.Position = new Text.Position(
                    lc.pl.marginLeft, lc.page.Height - lc.pl.marginTop - (tf.TextState.FontSize > 0 ? tf.TextState.FontSize : 12));
            lc.pl.tb.AppendTextInline(tf);
        }
        var tfBottomMargin = tf.Margin?.Bottom ?? 0;
        if (tfBottomMargin > 0) lc.pl.flow.AdvanceY(tfBottomMargin);
        // FootNote marker + page-bottom band are emitted inside
        // WriteTextFragment (the marker needs the last line's
    }
}
