namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>Lays a <see cref="ListBlock"/>: its top margin, then each item's paragraphs in
    /// the list's text column, then the item's marker on the first line the item laid, then the
    /// bottom margin. The column is the widest marker of any item plus the gap, so every item's
    /// text starts on one line; a nested list starts its own column where its item's text does.</summary>
    private void LayoutListBlockParagraph(PageContentLayoutState lc, ListBlock list)
    {
        var flow = lc.pl.flow;
        var margin = list.Margin;
        if (margin.Top > 0) flow.AdvanceY(margin.Top);
        double widest = 0;
        foreach (var item in list.Items)
            if (item.Marker is { } m) widest = Math.Max(widest, flow.MeasureMarker(m));
        var column = widest + list.MarkerGap;
        var outerLeft = flow.LeftIndent;
        var outerRight = flow.RightIndent;
        var itemLeft = outerLeft + margin.Left;
        flow.RightIndent = outerRight + margin.Right;
        foreach (var item in list.Items)
        {
            flow.OpenFirstLineCapture();
            flow.LeftIndent = itemLeft + column;
            foreach (var child in item.Paragraphs) LayoutListChild(lc, child);
            flow.LeftIndent = outerLeft;
            if (flow.CloseFirstLineCapture() is not { } first || item.Marker is not { } marker) continue;
            var offset = list.MarkerAlignment == HorizontalAlignment.Left ? 0 : widest - flow.MeasureMarker(marker);
            flow.SetListMarker(marker, first.Left + itemLeft + offset, first);
        }
        flow.RightIndent = outerRight;
        if (margin.Bottom > 0) flow.AdvanceY(margin.Bottom);
    }

    /// <summary>One paragraph of a list item, laid the way the page lays it.</summary>
    private void LayoutListChild(PageContentLayoutState lc, BaseParagraph child)
    {
        switch (child)
        {
            case Text.TextFragment tf:
                LayoutTextFragmentAt(lc, tf);
                break;
            case ListBlock nested:
                LayoutListBlockParagraph(lc, nested);
                break;
            case Image img:
                var (imgLeft, imgRight) = RegionMargins(lc);
                LayoutImageParagraph(img, lc.pl.flow, lc.page, lc.pl, imgLeft, imgRight, lc.pl.marginTop, lc.pl.marginBottom);
                break;
            case Table table:
                LayoutTableParagraph(table, lc.pl.flow, lc.page, lc.pl.renderedTables, lc.pc.overflowPages, lc.pc.overflowImages, lc.pl.marginLeft, lc.pl.marginTop);
                break;
            case BoxBlock box:
                LayoutBoxBlockParagraph(lc, box);
                break;
            case ReservedBlock reserved:
                LayoutReservedBlockParagraph(lc, reserved);
                break;
        }
    }
}
