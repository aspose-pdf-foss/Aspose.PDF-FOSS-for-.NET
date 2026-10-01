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
    private void LayoutFloatingBoxParagraph(FloatingBox fbox, FlowLayout flow, Page page, System.Collections.Generic.List<(Heading h, int pageIdx)> tocEntries, PageLayoutState pl, Dictionary<int, int> headingAutoCounters, List<(byte[] content, double width, double height)> overflowPages, double marginLeft, double marginRight, double marginBottom, double marginTop)
    {
        var fx = new FloatingBoxParagraphState();
        fx.fbox = fbox;
        fx.flow = flow;
        fx.page = page;
        fx.tocEntries = tocEntries;
        fx.pl = pl;
        fx.headingAutoCounters = headingAutoCounters;
        fx.overflowPages = overflowPages;
        fx.marginLeft = marginLeft;
        fx.marginRight = marginRight;
        fx.marginBottom = marginBottom;
        fx.marginTop = marginTop;
        // A box holding one multi-column article: the article paints its
        // own sized, padded background and pours its paragraphs down each
        // column in turn. The declared width and height size the CONTENT,
        // so the painted box grows by the padding on every side; columns
        // split what is left after the CSS gap between them.
        if (!TryLayoutDefaultFlow(fx)) return;
        fx.fboxIsVisibleBox = fx.fbox.BackgroundColor is not null
            || fx.fbox.BackgroundImage is not null
            || (fx.fbox.Border is not null && fx.fbox.Border.HasAnySide);
        fx.fboxChromeOnFlow = fx.fboxIsVisibleBox && fx.fbox.Width <= 0 && fx.fbox.Height <= 0;
        if (!PlaceFloatingBox(fx)) return;
    }

    /// <summary>A FloatingBox's paragraph Hyperlink is one Link annotation over the
    /// painted box.</summary>
    private static void EmitFloatingBoxLink(FloatingBox fbox, FlowLayout flow, Page page)
    {
        if (fbox.Hyperlink is null || flow.IsDryRun) return;
        // The box paints on the flow's start page; queue so the link resolves
        // against the final page sequence.
        if (ReferenceEquals(page, flow.CurrentPage) && !flow.HasOverflowed)
            flow.QueueLink(fbox.LastBoxRect, fbox.Hyperlink);
        else
            flow.EmitLinkNow(page, fbox.LastBoxRect, fbox.Hyperlink);
    }

    /// <summary>A fragment that carries a line-break segment ALONGSIDE real text. Only
    /// then does the break stand one builder-default line: a whole paragraph that is
    /// nothing but <c>Environment.NewLine</c> keeps the paragraph pitch the legacy
    /// writer gives it (probed — a box stacks bare newline PARAGRAPHS and
    /// each is charged its own pitch, while newline SEGMENTS inside
    /// a text-bearing fragment cost 10 pt each).</summary>
    private static bool HasBreakSegmentBesideText(Text.TextFragment tf)
    {
        var brk = false;
        var text = false;
        foreach (var seg in tf.Segments)
        {
            if (IsBreakSegment(seg)) { brk = true; continue; }
            if (!string.IsNullOrEmpty(seg.Text)) text = true;
        }
        return brk && text;
    }

    /// <summary>A segment that is nothing but a line break: it closes the line it sits
    /// on and stands one builder-default line of its own.</summary>
    private static bool IsBreakSegment(Text.TextSegment seg)
    {
        var t = seg.Text;
        if (string.IsNullOrEmpty(t) || t.Trim().Length != 0) return false;
        foreach (var ch in t) if (ch == (char)10 || ch == (char)13) return true;
        return false;
    }

    /// <summary>Whether a dissolved box carries a note whose band the planner must
    /// fit against the body.</summary>
    private static bool DissolvedBoxHasNotes(FloatingBox fbox)
    {
        foreach (var p in fbox.Paragraphs)
            if (p is Text.TextFragment { FootNote.Paragraphs.Count: > 0 }
                || p is Text.TextFragment { EndNote.Paragraphs.Count: > 0 })
                return true;
        return false;
    }

    /// <summary>Lay a dissolved (flow-positioned, no-size) FloatingBox's children
    /// into <paramref name="flow"/>. Run once for real; the band planner runs it
    /// into dry flows first when the box carries notes.</summary>
    private void LayoutDissolvedFloatingBox(FloatingBox fbox, FlowLayout flow, Page page,
        System.Collections.Generic.List<(Heading h, int pageIdx)> tocEntries,
        PageLayoutState pl, Dictionary<int, int> headingAutoCounters,
        List<(byte[] content, double width, double height)> overflowPages,
        double marginLeft, double marginRight, double marginBottom,
        (double[] lefts, double[] widths)? columnOverride = null)
    {
        var db = new DissolvedBoxState();
        db.columnCount = fbox.ColumnInfo?.ColumnCount ?? 0;
        db.inColumns = false;
        if (columnOverride is { } co)
        {
            flow.BeginColumns(co.lefts, co.widths);
            db.inColumns = true;
        }
        else if (db.columnCount > 1)
        {
            var (lefts, widths) = BuildColumnGeometry(
                fbox.ColumnInfo!, marginLeft,
                page.Width - marginLeft - marginRight);
            if (lefts.Length > 1)
            {
                flow.BeginColumns(lefts, widths);
                db.inColumns = true;
            }
        }

        db.styRuns = new List<FlowLayout.StyledRun>();
        db.styLs = 0;
        db.styBaseSize = 0;
        db.styNotes = new List<(Note note, string marker, double size)>();
        db.styBackground = null;
        db.styAlign = HorizontalAlignment.Left;
        db.styLastChild = -1;

        db.innerList = fbox.Paragraphs.ToList();
        for (var innerIdx = 0; innerIdx < db.innerList.Count; innerIdx++)
        {
            if (!LayoutDissolvedChild(db, innerIdx, flow, page, tocEntries, pl, headingAutoCounters, overflowPages, marginLeft, marginRight, marginBottom)) break;
        }
        FlushStyled(db, flow);

        if (db.inColumns)
                flow.EndColumns();
    }
}
