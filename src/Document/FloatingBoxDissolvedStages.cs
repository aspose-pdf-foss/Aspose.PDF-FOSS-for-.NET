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
    /// <summary>Lay out one child of a dissolved floating box: page and column breaks, then the heading, fragment, image, graph, HTML and table arms, inline runs collected into the styled paragraph.</summary>
    private bool LayoutDissolvedChild(DissolvedBoxState db, int innerIdx, FlowLayout flow, Page page, System.Collections.Generic.List<(Heading h, int pageIdx)> tocEntries, PageLayoutState pl, Dictionary<int, int> headingAutoCounters, List<(byte[] content, double width, double height)> overflowPages, double marginLeft, double marginRight, double marginBottom)
    {
        db.inner = db.innerList[innerIdx];
        // IsInNewPage on a child forces the rest of the box onto a
        // fresh page (the surrounding flow paginates it), mirroring the
        // page-level paragraph rule. Flush the accumulated inline
        // paragraph first so it stays on the current page.
        if (innerIdx > 0 && ParagraphIsInNewPage(db.inner))
        {
            FlushStyled(db, flow);
            flow.ForceNewPage();
        }
        // IsFirstParagraphInColumn pushes this paragraph to the
        // top of the next column. Never on the
        // very first child — column 0 is already its home.
        if (db.inColumns && innerIdx > 0 && db.inner.IsFirstParagraphInColumn)
        {
            FlushStyled(db, flow);
            flow.ForceNextColumn();
        }
        db.nextInline = innerIdx + 1 < db.innerList.Count
            && InlineOf(db.innerList[innerIdx + 1]);

        if (db.inner is Heading innerHeading)
        {
            if (CollectDissolvedHeading(db, innerIdx, innerHeading, flow, page, tocEntries, pl, headingAutoCounters, marginLeft)) return true;
        }
        if (db.inner is Text.TextFragment innerTf)
        {
            if (CollectDissolvedFragment(db, innerIdx, innerTf, flow)) return true;
        }
        if (db.inner is Image lineImg && (db.nextInline || (InlineOf(lineImg) && db.styRuns.Count > 0)))
        {
            // A picture that shares its line with text — either it is itself
            // inline-joined, or the paragraph after it is — is a RUN of the
            // styled paragraph: the text seats at the picture's right edge and
            // the line advances by the text's pitch, the picture overhanging
            // below it (probed on the era generator).
            if (!InlineOf(lineImg)) FlushStyled(db, flow);
            if (LoadFlowImage(lineImg, page.Width - marginLeft - marginRight, flow.ContentTop - marginBottom) is (var liData, var liW, var liH))
            {
                db.styLastChild = innerIdx;
                db.styRuns.Add(new FlowLayout.StyledRun
                { ImageData = liData, ImageW = liW, ImageH = liH });
            }
            if (!db.nextInline) FlushStyled(db, flow);
            return true;
        }
        FlushStyled(db, flow);
        if (db.inner is Aspose.Pdf.Drawing.Graph innerGraph)
        {
            // A flow Graph in a dissolved box (a chapter rule):
            // draw at the cursor and advance past it.
            LayoutGraphParagraph(innerGraph, flow, page, marginLeft, 0, marginBottom);
            return true;
        }
        if (db.inner is Image innerImage)
        {
            // A block Image in a dissolved box draws at the flow
            // cursor and advances the flow below it.
            if (LoadFlowImage(innerImage, page.Width - marginLeft - marginRight, flow.ContentTop - marginBottom) is (var fbImgBytes, var fbIw, var fbIh))
                flow.PlaceImageBlock(fbImgBytes, fbIw, fbIh);
            return true;
        }
        if (db.inner is HtmlFragment innerHtml)
        {
            if (LayoutDissolvedHtml(innerHtml, flow)) return true;
        }
        if (db.inner is Table innerTable)
        {
            if (LayoutDissolvedTable(innerTable, flow, overflowPages, marginLeft, marginBottom)) return true;
        }
        return true;
    }

    /// <summary>A table child: measured and placed at the box's column, spilling to the next column or page as its slices demand.</summary>
    private bool LayoutDissolvedTable(Table innerTable, FlowLayout flow, List<(byte[] content, double width, double height)> overflowPages, double marginLeft, double marginBottom)
    {
        // The box dissolves into the page flow, so its table
        // anchors at the page's left content margin (a
        // margin-less box still honours the page margins) and its
        // continuation pages resume below the page's TOP content
        // margin like any flow table (not at the bare page top).
        innerTable.FlowLeftOffset = marginLeft;
        var fbPage = flow.CurrentPage;
        var fbSpillTop = fbPage.PageInfo?.Margin is { TopTouched: true } fbPm ? fbPm.Top
            : PageInfo?.Margin is { TopTouched: true } fbDm ? fbDm.Top : 72;
        // Bottom limit is the flow's own, not a flat 36: a page declaring a
        // 72 pt bottom margin had its box's table run 36 pt past it. Taking
        // it from the flow (rather than the caller's page margin) keeps a
        // continuation slot's margins, exactly as the plain flow-table path
        // and the report-band path already do.
        var innerContents = innerTable.BuildMultiPage(fbPage, flow.CurrentY, flow.BottomMargin, fbSpillTop);
        // Inject at the flow's CURRENT page position — after a
        // page break the slice belongs to the overflow buffer,
        // not the start page.
        flow.InjectContentAtCursor(innerContents[0]);
        var innerGraphs = innerTable.LastGraphDraws;
        if (innerGraphs.Count > 0)
            foreach (var gc in innerGraphs[0])
                flow.InjectContentAtCursor(gc);
        var innerImgs = innerTable.LastImageDraws;
        if (!flow.HasOverflowed && innerImgs.Count > 0)
            foreach (var (data, rect) in innerImgs[0])
                flow.CurrentPage.AddImage(data, rect);
        // A single-slice table consumes exactly its height so
        // following children continue below it on this page.
        if (innerContents.Count == 1)
            flow.AdvanceY(innerTable.LastRenderedHeight);
        else
        {
            // Intermediate spill pages are whole body pages and stand
            // alone; the LAST one goes back to the flow so the box's
            // remaining children pack BELOW the table on it. Parking the
            // cursor under the bottom margin instead (the old reset) made
            // every following child build from an exhausted page, so each
            // returned an empty first slice plus a page of its own — the
            // reference runs seven of these blocks onto one such page.
            for (var pi = 1; pi < innerContents.Count - 1; pi++)
            {
                flow.RecordBodyOnSlot(overflowPages.Count, marginBottom);
                overflowPages.Add((innerContents[pi], flow.CurrentPage.Width, flow.CurrentPage.Height));
            }
            var innerSlot = flow.ContinueOnPrebuiltSpill(
                innerContents[innerContents.Count - 1], innerTable.LastPageEndY);
            flow.RecordBodyOnSlot(innerSlot, innerTable.LastPageEndY);
        }
        return false;
    }

    /// <summary>An HTML fragment child laid out through the flow at the box's width.</summary>
    private bool LayoutDissolvedHtml(HtmlFragment innerHtml, FlowLayout flow)
    {
        // A dissolved box renders its HTML as blocks, not as one
        // tag-stripped run: each block is its own paragraph, drawn in
        // the browser default serif face at the browser default block
        // size, and inline <a href> ranges become hyperlinked segments
        // that the flow turns into Link annotations over their glyphs.
        var innerBlocks = Converters.HtmlToPdfConverter.ParseHtmlBlocks(
            innerHtml.HtmlContent ?? "", HtmlUaBlockFontSize);
        var innerWrote = false;
        foreach (var ib in innerBlocks)
        {
            if (string.IsNullOrWhiteSpace(ib.Text)) continue;
            var innerFrag = new Text.TextFragment(ib.Text);
            innerFrag.TextState.FontName = HtmlUaSerifFontName;
            innerFrag.TextState.FontSize =
                (float)(ib.FontSize > 0 ? ib.FontSize : HtmlUaBlockFontSize);
            if (ib.Anchors is { Count: > 0 })
                ApplyHtmlAnchorSegments(innerFrag, ib.Text, ib.Anchors);
            flow.WriteTextFragment(innerFrag);
            innerWrote = true;
        }
        if (!innerWrote)
        {
            var innerPlain = HtmlFragment.StripHtmlTags(innerHtml.HtmlContent ?? "");
            if (!string.IsNullOrWhiteSpace(innerPlain))
                flow.WriteTextFragment(new Text.TextFragment(innerPlain));
        }
        return true;
    }

    /// <summary>A text fragment child: inline and styled runs join the styled paragraph, a plain one is written straight through the flow.</summary>
    private bool CollectDissolvedFragment(DissolvedBoxState db, int innerIdx, Text.TextFragment innerTf, FlowLayout flow)
    {
        var tfStyled = innerTf.IsInLineParagraph || db.nextInline
            || innerTf.FootNote is { Paragraphs.Count: > 0 }
            || SegStyled(innerTf.TextState)
            || innerTf.Segments.Any(s => SegStyled(s.TextState))
            // A fragment that MIXES a newline segment with real text needs
            // the styled-run engine: the legacy writer prices that empty
            // line at the fragment's own pitch instead of one default line,
            // and drops the whole fragment outright when the segments also
            // differ in size (its styled-line fallback carries no break).
            || HasBreakSegmentBesideText(innerTf);
        if (tfStyled)
        {
            if (!innerTf.IsInLineParagraph) FlushStyled(db, flow);
            if (db.styRuns.Count == 0)
            {
                flow.RecordPosition(innerTf);
                if (innerTf.Margin is { Top: > 0 } styTopM) flow.AdvanceY(styTopM.Top);
            }
            db.styLastChild = innerIdx;
            AppendFragmentRuns(db, flow, innerTf);
            if (!db.nextInline) FlushStyled(db, flow);
            return true;
        }
        FlushStyled(db, flow);
        flow.RecordPosition(innerTf);
        // A child's own margins are room above and below it, as the
        // page-level dispatcher reserves them (a headnote line with
        // Margin.Top 6 pitches 20, not 14).
        flow.ReserveTopMargin(innerTf.Margin?.Top ?? 0, innerTf);
        flow.WriteTextFragment(innerTf);
        var innerBottomM = innerTf.Margin?.Bottom ?? 0;
        if (innerBottomM > 0) flow.AdvanceY(innerBottomM);
        return true;
    }

    /// <summary>A heading child: inline-joined or styled headings become runs of the styled paragraph, a plain one writes itself with its TOC entry.</summary>
    private bool CollectDissolvedHeading(DissolvedBoxState db, int innerIdx, Heading innerHeading, FlowLayout flow, Page page, System.Collections.Generic.List<(Heading h, int pageIdx)> tocEntries, PageLayoutState pl, Dictionary<int, int> headingAutoCounters, double marginLeft)
    {
        // A TOC-page-authored heading inside the box is a TOC
        // entry (same rule as the page-level branch); anything
        // else renders as a content heading at the flow cursor
        // — previously these were silently dropped and the box
        // rendered blank.
        if (ReferenceEquals(innerHeading.TocPage, page) || flow.IsDryRun)
        {
            FlushStyled(db, flow);
            if (flow.IsDryRun) return true;
            var tIdx = tocEntries.FindIndex(e => ReferenceEquals(e.h, innerHeading));
            if (tIdx >= 0)
            {
                var yA = RenderTocEntry(pl, innerHeading, tocEntries[tIdx].pageIdx, flow.CurrentY);
                flow.AdvanceY(flow.CurrentY - yA);
            }
            return true;
        }
        var hStyled = innerHeading.UserLabel is not null
            || InlineOf(innerHeading) || db.nextInline
            || SegStyled(innerHeading.TextState)
            || innerHeading.Segments.Any(s => SegStyled(s.TextState));
        if (hStyled)
        {
            if (!InlineOf(innerHeading)) FlushStyled(db, flow);
            if (db.styRuns.Count == 0 && innerHeading.Margin is { Top: > 0 } hm)
            {
                if (flow.CurrentY - hm.Top - 12 < flow.BottomMargin)
                    flow.ForceNewPage();
                flow.AdvanceY(hm.Top);
            }
            db.styLastChild = innerIdx;
            AppendHeadingRuns(db, headingAutoCounters, innerHeading);
            if (!db.nextInline) FlushStyled(db, flow);
            return true;
        }
        FlushStyled(db, flow);
        // Heading top margin advances the flow; when the margin
        // plus one heading line no longer fits, the heading
        // moves to a fresh page and re-applies its margin there
        // (as in the overflowing list case).
        var hTopM = innerHeading.Margin?.Top ?? 0;
        if (hTopM > 0)
        {
            if (flow.CurrentY - hTopM - 12 < flow.BottomMargin)
                flow.ForceNewPage();
            flow.AdvanceY(hTopM);
        }
        pl.fontName ??= Table.RegisterFont(page);
        var innerPrefix = NextHeadingPrefix(headingAutoCounters, innerHeading);
        var (hContent, hHeight) = innerHeading.Build(
            flow.CurrentPage, marginLeft + (innerHeading.Margin?.Left ?? 0),
            flow.CurrentY, pl.fontName, innerPrefix);
        flow.InjectContentAtCursor(hContent);
        flow.AdvanceY(hHeight);
        return true;
    }
}
