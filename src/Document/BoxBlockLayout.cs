using System.Linq;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>Lays a <see cref="BoxBlock"/>: the flow opens the box (its top margin, band
    /// and padding, the region narrowed to its content box), its paragraphs are laid exactly
    /// as the page lays its own - the next paragraph a keep looks at is the box's next, not
    /// the page's - and the flow closes it under them, painting each page's part.</summary>
    private void LayoutBoxBlockParagraph(PageContentLayoutState lc, BoxBlock box)
    {
        var flow = lc.pl.flow;
        // A kept box that would split moves whole to the next page, when it fits an
        // empty one at all; one taller than a page is laid where it is and splits.
        if (box.IsKeptTogether && flow.WholeHeightOf(box) is { } whole
            && flow.CurrentY - whole < flow.BottomMargin
            && whole <= flow.ContentTop - flow.BottomMargin + 0.5)
            flow.ForceNewPage();
        flow.OpenBox(box);
        var outer = lc.pl.paraList;
        lc.pl.paraList = box.Paragraphs.ToList();
        var inner = new PageContentLayoutState { pl = lc.pl, page = lc.page, pc = lc.pc };
        try
        {
            for (inner.paraIdx = 0; inner.paraIdx < lc.pl.paraList.Count; inner.paraIdx++)
                if (!LayoutParagraphAt(inner)) break;
        }
        finally
        {
            lc.pl.paraList = outer;
        }
        flow.CloseBox();
    }

    /// <summary>The margins a block laid from the page's own margins stands between: inside
    /// a box, those of the region the box and the blocks around it leave.</summary>
    private static (double Left, double Right) RegionMargins(PageContentLayoutState lc)
    {
        var flow = lc.pl.flow;
        return flow.InsideBox
            ? (lc.pl.marginLeft + flow.LeftIndent, lc.pl.marginRight + flow.RightIndent)
            : (lc.pl.marginLeft, lc.pl.marginRight);
    }
}
