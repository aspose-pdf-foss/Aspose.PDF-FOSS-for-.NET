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
    private void LayoutGraphParagraph(Aspose.Pdf.Drawing.Graph graph, FlowLayout flow, Page page, double marginLeft, double marginTop, double marginBottom)
    {
        // Shapes carry graph-local coordinates with the origin at the
        // graph box's bottom-left corner. Translate the rendered stream
        // so that corner lands at the correct page position.
        double originX, originY;
        if (!graph.IsChangePosition)
        {
            // A box that does not move the flow: it stands where the flow is - an
            // assigned Left re-anchors the flow at margin + Left, an assigned Top seats
            // the box top at content top - Top, an unassigned axis takes the cursor -
            // and the flow goes on from the box's TOP, taking no room for it (probed:
            // "Before" / box / "After" writes "After" on the line the box top starts;
            // a Left of -margin reaches the page corner).
            if (graph.LeftAssigned)
                flow.AnchorLeft = marginLeft + graph.Left;
            originX = flow.CurrentLeft;
            var pinnedTop = graph.TopAssigned ? flow.ContentTop - graph.Top : flow.CurrentY;
            originY = pinnedTop - graph.Height;
            flow.InjectContentAtCursor(graph.Build(flow.CurrentPage, originX, originY));
            EmitGraphLink(graph, flow, originX, originY, deferred: true);
            flow.MoveCursorTo(pinnedTop);
            return;
        }

        // Flow placement, per axis: an assigned Left anchors the box's left edge
        // at margin + Left and re-anchors the flow there for everything that
        // follows; an assigned Top seats the box top at content top - Top. An
        // unassigned axis flows: the left edge is the flow's current left edge
        // (a previous anchor included), the top is the cursor. Repeated graphs
        // with the same Left/Top therefore overlay, and the cursor continues
        // under the box either way (measured 2026-08-23).
        if (!graph.TopAssigned)
        {
            // Push to a fresh page if it doesn't fit below the cursor (but
            // never when the cursor is already at the page top — an oversized
            // graph still renders on the current page rather than looping).
            if (flow.CurrentY - graph.Height < flow.BottomMargin
                && flow.CurrentY < flow.ContentTop - 0.5)
                flow.BreakPageIfContent();
        }
        if (graph.LeftAssigned)
            flow.AnchorLeft = marginLeft + graph.Left;
        originX = flow.CurrentLeft;
        var boxTop = graph.TopAssigned ? flow.ContentTop - graph.Top : flow.CurrentY;
        originY = boxTop - graph.Height;
        flow.InjectContentAtCursor(graph.Build(flow.CurrentPage, originX, originY));
        EmitGraphLink(graph, flow, originX, originY, deferred: true);
        flow.MoveCursorTo(originY);
        // The graph's Title is a text paragraph flowed directly under the box
        // (measured: the title's line box starts at the box bottom, at the
        // flow's left edge, in the title's own text state).
        if (graph.Title is { } title && !string.IsNullOrEmpty(title.Text))
            flow.WriteTextFragment(title);
    }

    /// <summary>A Graph's paragraph Hyperlink becomes one Link annotation over the
    /// whole graph box.</summary>
    private static void EmitGraphLink(Aspose.Pdf.Drawing.Graph graph, FlowLayout flow,
        double originX, double originY, bool deferred)
    {
        if (graph.Hyperlink is null) return;
        var rect = new Rectangle(originX, originY, originX + graph.Width, originY + graph.Height);
        if (deferred) flow.QueueLink(rect, graph.Hyperlink);
        else flow.EmitLinkNow(flow.CurrentPage, rect, graph.Hyperlink);
    }

    private void LayoutHeadingParagraph(Heading heading, FlowLayout flow, Page page, System.Collections.Generic.List<(Heading h, int pageIdx)> tocEntries, PageLayoutState pl, Dictionary<int, int> headingAutoCounters, double marginLeft, double marginRight)
    {
        var hl = new HeadingLayoutState();
        hl.heading = heading;
        hl.flow = flow;
        hl.page = page;
        hl.tocEntries = tocEntries;
        hl.pl = pl;
        hl.headingAutoCounters = headingAutoCounters;
        hl.marginLeft = marginLeft;
        hl.marginRight = marginRight;
        // A heading whose TocPage is this page is a TOC entry authored
        // directly on the TOC page — render its TOC line AT the flow
        // cursor (paragraph order), not as plain content text, so page
        // content authored before it (e.g. spacer fragments) stays
        // above it. Headings on other pages still render as their
        // content heading (and also appear in the TOC list).
        if (ReferenceEquals(hl.heading.TocPage, hl.page))
        {
            var tocIdx = hl.tocEntries.FindIndex(e => ReferenceEquals(e.h, hl.heading));
            if (tocIdx >= 0)
            {
                var yAfter = RenderTocEntry(hl.pl, hl.heading, hl.tocEntries[tocIdx].pageIdx, hl.flow.CurrentY);
                hl.flow.AdvanceY(hl.flow.CurrentY - yAfter);
            }
            return;
        }

        hl.pl.fontName ??= Table.RegisterFont(hl.page);
        // The heading's own Margin.Top is leading reserved above it —
        // each heading drops by it before its line box.
        if (hl.heading.Margin?.Top > 0) hl.flow.AdvanceY(hl.heading.Margin.Top);
        hl.headingPage = hl.flow.CurrentPage;
        hl.headingY = hl.flow.CurrentY;
        hl.headingPrefix = NextHeadingPrefix(hl.headingAutoCounters, hl.heading);
        var (content, height) = hl.heading.Build(hl.headingPage, hl.marginLeft, hl.headingY, hl.pl.fontName, hl.headingPrefix);
        // A heading that no longer fits above the bottom margin moves to the
        // next page whole (Heading.Build draws at the supplied Y verbatim, so
        // the cursor alone cannot spill it) — a long run of headings paginates
        // instead of piling below the page foot. Re-record the position so a
        // TOC leader resolves to the page it really landed on.
        if (hl.headingY - height < hl.flow.BottomMargin
            && height <= hl.flow.ContentTop - hl.flow.BottomMargin + 0.5)
        {
            hl.flow.ForceNewPage();
            hl.flow.RecordPosition(hl.heading);
            hl.headingY = hl.flow.CurrentY;
            (content, height) = hl.heading.Build(hl.flow.CurrentPage, hl.marginLeft, hl.headingY, hl.pl.fontName, hl.headingPrefix);
        }
        hl.flow.InjectContentAtCursor(content);

        hl.destPage = hl.heading.DestinationPage;
        if (hl.destPage is not null)
        {
            LinkHeadingDestination(hl, height);
        }

        // Mirror the heading into the document outlines when its
        // TOC page asks for it (Heading.TocPage.TocInfo.CopyToOutlines).
        // A synthetic TOC asserts the saved PDF carries a flat
        // list of bookmarks, one per heading.
        if (hl.heading.TocPage?.TocInfo?.CopyToOutlines == true
            && hl.heading.Segments.Count > 0)
        {
            CopyHeadingToOutlines(hl);
        }

        // A heading line consumes exactly its own box (font size per
        // line) — the next paragraph chains one of ITS
        // OWN font sizes below the heading's bottom, with no extra
        // padding (758 → 748 for a 12 pt heading followed by 10 pt
        // text; the old +4 pushed every following line down) — plus
        // the heading's own Margin.Bottom when the caller set one.
        hl.flow.AdvanceY(height);
        if (hl.heading.Margin?.Bottom > 0) hl.flow.AdvanceY(hl.heading.Margin.Bottom);
    }

    /// <summary>Read a generator <see cref="Image"/>'s bytes and the size it draws
    /// at in a flow of <paramref name="availW"/> x <paramref name="availH"/> points:
    /// a Fix dimension counts on its own and the OTHER axis keeps the source's own
    /// pixel measure (probed 2026-08-26: a 240x60 picture under FixHeight 20 draws
    /// 240x20, under FixWidth 100 draws 100x60 — the unset axis is neither scaled to
    /// preserve the aspect nor stretched to the band); with neither set the pixels
    /// map 1 px = 1 pt, scaled by <see cref="Image.ImageScale"/>. A zero
    /// <paramref name="availW"/>/<paramref name="availH"/> means "no band to squash
    /// into" — a note band lets its picture overhang.</summary>
    private static (byte[] data, double w, double h)? LoadFlowImage(Image img, double availW, double availH)
    {
        byte[]? data = default;
        double w = default;
        double h = default;
        w = h = 0;
        byte[]? bytes;
        if (img.ImageStream is { } ist)
        {
            using var ms = new MemoryStream();
            if (ist.CanSeek) ist.Position = 0;
            ist.CopyTo(ms);
            bytes = ms.ToArray();
        }
        else
            bytes = img.ReadSourceBytes();
        if (bytes is null) { data = System.Array.Empty<byte>(); return null; }
        data = bytes;
        var (natW, natH) = TryGetImageNaturalSizePt(bytes, img.IsApplyResolution) ?? (0, 0);
        var haveNatural = natW > 0 && natH > 0;
        if (img.FixWidth > 0 || img.FixHeight > 0)
        {
            w = img.FixWidth > 0 ? img.FixWidth : haveNatural ? natW : availW;
            h = img.FixHeight > 0 ? img.FixHeight : haveNatural ? natH : availH;
        }
        else if (haveNatural)
        {
            var scale = img.ImageScale > 0 ? img.ImageScale : 1.0;
            w = natW * scale;
            h = natH * scale;
        }
        else { w = 100; h = 100; }
        if (availW > 0) w = Math.Min(w, availW);
        if (availH > 0) h = Math.Min(h, availH);
        return (data, w, h);
    }

    private void LayoutTableParagraph(Table table, FlowLayout flow, Page page, HashSet<Table> renderedTables, List<(byte[] content, double width, double height)> overflowPages, Dictionary<int, List<(byte[] data, Rectangle rect)>> overflowImages, double marginLeft, double marginTop)
    {
        // A table already drawn as another table's inner content is not drawn again;
        // one the caller added to the paragraphs twice draws twice (probed on the
        // reference: a repeated table instance repeats, like a repeated fragment).
        if (renderedTables.Contains(table)) return;

        var tp = new TableParagraphState();
        tp.containerInners = null;
        CollectContainerInners(tp, table);
        if (!TryLayoutContainerTable(tp, table, flow, page, renderedTables, overflowPages, overflowImages, marginLeft, marginTop)) return;
        if (!TryLayoutRepeatingTable(tp, table, flow)) return;
        SeatTableOnPage(tp, table, flow, page, marginLeft, marginTop);
        BuildTableSlices(tp, table, flow, overflowPages);
        PlaceTableSlices(tp, table, flow, overflowPages, overflowImages);
        // A table broken vertically in the same page draws once however often it is
        // added (probed on the reference: narrow, wide and six-column replicas alike),
        // while an unbroken table added again repeats.
        if (table.Broken == TableBroken.VerticalInSamePage) renderedTables.Add(table);
    }


}
