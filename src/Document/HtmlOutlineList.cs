using System.Linq;
using Aspose.Pdf.Core;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>Per-call working state of the outline-list renderer.</summary>
    private sealed class HtmlOutlineListState
    {
        public HtmlStepListState hs = null!;
        public Converters.HtmlToPdfConverter.OutlineListDoc doc = null!;
        public double pitch;
        public double asc;
        /// <summary>The right edge lines wrap at (the page's content box).</summary>
        public double right;
        public double liLeft;
        public double spaceW;
        /// <summary>The margin the next line box opens under (collapsed: the larger of
        /// the closing and opening margins).</summary>
        public double pendingMargin;
    }

    /// <summary>The serif's resource name on the flow's pages: the start page's fonts are
    /// merged onto its overflow pages by name, where F1 is already the Helvetica every
    /// generated page registers.</summary>
    private const string OutlineFontResourceName = "FOutlineSerif";

    /// <summary>UA padding-left of a <c>ul</c> (40 px).</summary>
    private const double OutlineUlIndentPt = 30.0;

    /// <summary>The block margin the list opens and closes with, in em of the body size
    /// (the reference seats the first item 1.12 em under the title's line box, the
    /// item's own .5 em margin collapsed into it).</summary>
    private const double OutlineUlMarginEm = 1.12;

    /// <summary>The list dialects laid out on the UA serif line model: a class-styled
    /// outline list (paginated) or a step list of heading blocks (one page).</summary>
    private static bool TryRenderListDialect(HtmlFragmentLayoutState hl) =>
        (Converters.HtmlToPdfConverter.TryParseHtmlOutlineList(hl.htmlContent) is { } outline
         && RenderHtmlOutlineList(outline, hl.flow, hl.marginLeft, hl.marginRight, hl.htmlColor))
        || (Converters.HtmlToPdfConverter.TryParseHtmlStepList(hl.htmlContent) is { } stepItems
            && RenderHtmlStepList(stepItems, hl.flow, hl.marginLeft, hl.marginRight, hl.htmlColor));

    /// <summary>Lay a class-styled outline list out through the flow, page by page.
    /// LAW (measured 2026-09-07 on the reference, a 41-item outline on 800 x 650 pages,
    /// every seat exact to the printed digits): the list is set in the UA serif at the
    /// 12 pt body size on a pixel-rounded 13.5 pt line box; an item's text seats at the
    /// list's 30 pt indent plus its own padding-left; its number span is followed by
    /// its padding-right and, when the number has text, the collapsed space; the
    /// inline-block description stays on that line when its widest line fits the
    /// content box, else opens the next line at the item's text edge and wraps on the
    /// content box; the collapsible space between the description and the item's
    /// <c>br</c> takes a line of its own when it does not fit after the block's box (a
    /// wrapped block fills the box, so it never does); items collapse their .5 em
    /// margins; a line box that does not fit above the bottom margin opens the next
    /// page, an item's margin re-applied at the page top; the wrap edge is the page's
    /// right content edge with the flow's half-point slack (a 523.908 pt line fits a
    /// 523.75 pt box, a 1.46 pt overrun does not).</summary>
    private static bool RenderHtmlOutlineList(Converters.HtmlToPdfConverter.OutlineListDoc doc,
        FlowLayout flow, double marginLeft, double marginRight, Color? htmlColor)
    {
        var hs = new HtmlStepListState();
        hs.flow = flow;
        hs.marginLeft = marginLeft;
        hs.marginRight = marginRight;
        hs.htmlColor = htmlColor;
        if (!LoadStepListFaces(hs)) return false;
        hs.pageW = hs.flow.CurrentPage.Width;
        var ol = new HtmlOutlineListState();
        ol.hs = hs;
        ol.doc = doc;
        ol.pitch = Pitch(hs, hs.em);
        ol.asc = Asc(hs, hs.em);
        ol.right = hs.pageW - hs.marginRight;
        ol.liLeft = hs.marginLeft + OutlineUlIndentPt;
        ol.spaceW = GlyphW(hs, Gid(hs, ' ', false), false, hs.em);
        hs.fontDict = Table.ResolvePageFontDict(hs.flow.CurrentPage);
        hs.resNameHint = OutlineFontResourceName;
        OpenOutlinePage(ol);

        if (doc.Title.Length > 0)
            foreach (var line in WrapOutline(ol, doc.Title, ol.right - hs.marginLeft))
                PlaceOutlineLine(ol, new List<(double x, string text)> { (hs.marginLeft, line) });
        ol.pendingMargin = OutlineUlMarginEm * hs.em;
        foreach (var item in doc.Items)
            LayoutOutlineItem(ol, item);
        FlushOutlinePage(ol);
        hs.flow.AdvanceY(System.Math.Max(ol.pendingMargin, OutlineUlMarginEm * hs.em));
        return true;
    }

    private static void LayoutOutlineItem(HtmlOutlineListState ol, Converters.HtmlToPdfConverter.OutlineListItem item)
    {
        ol.pendingMargin = System.Math.Max(ol.pendingMargin, ol.doc.ItemMarginTopPt);
        var textLeft = ol.liLeft + item.PadLeftPt;
        var runs = new List<(double x, string text)>();
        var pen = textLeft;
        var hasContent = item.Number.Length > 0;
        if (hasContent)
        {
            runs.Add((pen, item.Number));
            pen += OutlineWidth(ol.hs, item.Number);
        }
        pen += item.NumberPadRightPt;
        // A collapsed space opens no line: it stays only after content.
        if (hasContent && item.SpaceBeforeDescription) pen += ol.spaceW;

        var descW = item.DescriptionLines.Count > 0 ? item.DescriptionLines.Max(l => OutlineWidth(ol.hs, l)) : 0.0;
        var hasBlock = item.DescriptionLines.Any(l => l.Length > 0);
        var blockX = pen;
        if (hasBlock && hasContent && pen + descW > ol.right + FlowLayout.WrapWidthSlackPt)
        {
            PlaceOutlineLine(ol, runs);
            runs = new List<(double x, string text)>();
            blockX = textLeft;
        }
        var blockW = System.Math.Min(descW, ol.right - blockX);
        if (hasBlock)
        {
            foreach (var segment in item.DescriptionLines)
                foreach (var line in WrapOutline(ol, segment, blockW))
                {
                    runs.Add((blockX, line));
                    PlaceOutlineLine(ol, runs);
                    runs = new List<(double x, string text)>();
                }
        }
        if (runs.Count > 0) PlaceOutlineLine(ol, runs);
        var penAfter = hasBlock ? blockX + blockW : pen;
        if (item.TrailingBreak && item.SpaceAfterDescription && penAfter + ol.spaceW > ol.right + FlowLayout.WrapWidthSlackPt)
            PlaceOutlineLine(ol, new List<(double x, string text)>());
        ol.pendingMargin = ol.doc.ItemMarginBottomPt;
    }

    /// <summary>Open one line box under the pending margin - on the next page when the
    /// box does not fit above the bottom margin - and seat its runs on the baseline.</summary>
    private static void PlaceOutlineLine(HtmlOutlineListState ol, List<(double x, string text)> runs)
    {
        var flow = ol.hs.flow;
        if (flow.CurrentY - ol.pendingMargin - ol.pitch < flow.BottomMargin && flow.CurrentY < flow.ContentTop)
        {
            FlushOutlinePage(ol);
            flow.ForceNewPage();
            OpenOutlinePage(ol);
        }
        flow.AdvanceY(ol.pendingMargin);
        ol.pendingMargin = 0;
        var baseline = flow.CurrentY - ol.asc;
        foreach (var (x, text) in runs)
            if (text.Length > 0) EmitRun(ol.hs, text, false, ol.hs.em, x, baseline);
        flow.AdvanceY(ol.pitch);
    }

    private static void OpenOutlinePage(HtmlOutlineListState ol)
    {
        ol.hs.csb = new Content.ContentStreamBuilder();
        if (ol.hs.htmlColor is not null) ol.hs.csb.SetFillColor(ol.hs.htmlColor);
    }

    private static void FlushOutlinePage(HtmlOutlineListState ol) =>
        ol.hs.flow.InjectContentAtCursor(ol.hs.csb.Build());

    /// <summary>The pair-kerned advance of <paramref name="text"/> in the regular face.</summary>
    private static double OutlineWidth(HtmlStepListState hs, string text)
    {
        var w = 0.0;
        var prev = -1;
        foreach (var ch in text)
        {
            var gid = Gid(hs, ch, false);
            if (prev >= 0) w += KernW(hs, prev, gid, false, hs.em);
            w += GlyphW(hs, gid, false, hs.em);
            prev = gid;
        }
        return w;
    }

    /// <summary>Greedy space-break wrap of one segment on <paramref name="maxW"/> plus the
    /// flow's slack; an empty segment is one empty line box.</summary>
    private static List<string> WrapOutline(HtmlOutlineListState ol, string segment, double maxW)
    {
        var stream = segment.Select(c => (c, false)).ToList();
        var lines = Wrap(ol.hs, stream, ol.hs.em, maxW + FlowLayout.WrapWidthSlackPt)
            .Select(l => new string(l.Select(t => t.c).ToArray())).ToList();
        if (lines.Count == 0) lines.Add("");
        return lines;
    }
}
