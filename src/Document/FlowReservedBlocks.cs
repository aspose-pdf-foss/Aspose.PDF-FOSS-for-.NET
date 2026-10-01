namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>Lays a <see cref="ReservedBlock"/>: part after part, each in the room left
    /// on its page, a page break between them.</summary>
    private static void LayoutReservedBlockParagraph(PageContentLayoutState lc, ReservedBlock block) =>
        lc.pl.flow.PlaceReservedBlock(block);

    private sealed partial class FlowLayout
    {
        /// <summary>Where the cursor stood when the current page opened, under the tops of
        /// the boxes open across the break; null on the page the flow started on.</summary>
        private double? _pageTopY;

        // Parts placed by PlaceReservedBlock, bound to their final page by
        // FinaliseReservedBlocks once overflow slots map to real Pages.
        private readonly List<(int slot, ReservedBlock block, ReservedPart part, Rectangle rect)> _pendingReservedParts = new();

        /// <summary>Whether nothing stands above the cursor on its page.</summary>
        private bool IsAtPageTop => Math.Abs(_curY - (_pageTopY ?? ContentTop)) < PageTopTolerance;

        /// <summary>How close to the page's top the cursor may stand and still be at it.</summary>
        private const double PageTopTolerance = 0.01;

        /// <summary>Place the block's parts from the cursor down: ask for each part in the
        /// room left, keep its height, break the page while the block continues. A block
        /// that fits nothing where it stands moves to the next page; one that fits nothing
        /// at a page's top is laid no further.</summary>
        public void PlaceReservedBlock(ReservedBlock block)
        {
            if (block.LayOut is null) return;
            // A block laid again replaces the parts it was given before.
            if (!IsDryRun) _pendingReservedParts.RemoveAll(placed => ReferenceEquals(placed.block, block));
            for (var index = 0; ;)
            {
                var atTop = IsAtPageTop;
                var room = new ReservedRoom(index, CurLeft, CurWidth, _curY, EffectiveBottom, atTop);
                if (block.LayOut(room) is not { } part)
                {
                    if (atTop) return;
                    StartNewPage();
                    continue;
                }
                var rect = new Rectangle(room.Left, _curY - part.Height, room.Left + room.Width, _curY);
                if (!IsDryRun)
                {
                    _pendingReservedParts.Add((_currentSlot, block, part, rect));
                    // The part's place in the page's content is marked with a comment, so the caller
                    // can paint it there, between what the flow drew before and after it; the mark
                    // is content of its page too, so an overflow page holding nothing else is made.
                    part.Marker = "reserved part " + Guid.NewGuid().ToString("N");
                    var mark = System.Text.Encoding.ASCII.GetBytes("% " + part.Marker + "\n");
                    if (_overflowBuffer is not null) _overflowBuffer.Add(mark);
                    else _startPage.AddContentStream(mark);
                }
                _curY -= part.Height;
                _lastBodyBaseline = null;
                RecordSlotBottom(_curY);
                if (!part.Continues) return;
                index++;
                StartNewPage();
            }
        }

        /// <summary>A part a table's cell placed on the current page, at the rectangle the cell gave it.</summary>
        public void PlaceReservedPart(ReservedBlock block, ReservedPart part, Rectangle rect) =>
            PlaceReservedPartOn(_currentSlot, block, part, rect);

        /// <summary>A part a table's cell placed on the page a slot becomes.</summary>
        public void PlaceReservedPartOn(int slot, ReservedBlock block, ReservedPart part, Rectangle rect)
        {
            if (!IsDryRun) _pendingReservedParts.Add((slot, block, part, rect));
        }

        /// <summary>Tell every placed part the page its slot resolved to, and its rectangle there.</summary>
        public void FinaliseReservedBlocks(IList<Page> overflowPageRefs)
        {
            foreach (var (_, block, _, _) in _pendingReservedParts) block.ClearParts();
            foreach (var (slot, block, part, rect) in _pendingReservedParts)
            {
                part.Page = SlotPage(slot, overflowPageRefs);
                part.Rect = rect;
                block.AddPart(part);
            }
        }
    }
}
