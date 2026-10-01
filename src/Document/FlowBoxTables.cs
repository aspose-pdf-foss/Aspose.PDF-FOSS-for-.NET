namespace Aspose.Pdf;

public sealed partial class Document
{
    private sealed partial class FlowLayout
    {
        /// <summary>The page's own bottom margin, without the room open boxes keep.</summary>
        internal double PageBottomMargin => _marginBottom;

        /// <summary>The margins a table breaking inside open boxes has on a page it spills
        /// onto: the page's own, with the top grown by what every box opens with again
        /// (its top margin when it takes it again, its top band and padding) and the bottom
        /// by the room every box keeps under what it holds.</summary>
        internal (double top, double bottom) SpillMarginsInsideBoxes((double top, double bottom) page)
        {
            var top = page.top;
            foreach (var open in _openBoxes) top += ReopeningDepth(open);
            return (top, page.bottom + _boxFloor);
        }

        /// <summary>A table's first page ends at <paramref name="endY"/> with boxes open:
        /// each box's part closes under it there, innermost first.</summary>
        internal void CloseBoxPartsUnderTable(double endY)
        {
            if (!InsideBox) return;
            _partBottom = endY;
            ClosePartsAtBreak();
        }

        /// <summary>The paint of every open box's part around a whole page of a table that
        /// breaks inside them, to go ahead of that page's slice: each part opens under the
        /// page top <paramref name="pageTopY"/> as the box opens again after a break, and
        /// closes under the slice ending at <paramref name="endY"/>; outermost first.</summary>
        internal byte[] BoxPartsAroundSpill(double pageTopY, double endY)
        {
            if (!InsideBox || IsDryRun) return Array.Empty<byte>();
            var tops = PartTopsFrom(pageTopY);
            var parts = new byte[_openBoxes.Count][];
            var closeAt = endY;
            for (var i = _openBoxes.Count - 1; i >= 0; i--)
            {
                var open = _openBoxes[i];
                var bottom = closeAt - open.Padding.Bottom - open.Bands.Bottom;
                parts[i] = PartPaint(open, bottom, tops[i]);
                closeAt = bottom - open.Box.Margin.Bottom;
            }
            return ConcatBlocks(parts.ToList());
        }

        /// <summary>The flow goes on from a table's last page: every open box opens its part
        /// there again, under the page top <paramref name="pageTopY"/>, its paint to go
        /// ahead of the slice already on the page.</summary>
        internal void ReopenBoxPartsOnSpill(double pageTopY)
        {
            if (!InsideBox) return;
            var tops = PartTopsFrom(pageTopY);
            for (var i = 0; i < _openBoxes.Count; i++)
            {
                var open = _openBoxes[i];
                open.StartY = i == 0 ? pageTopY : tops[i - 1] - _openBoxes[i - 1].Bands.Top - _openBoxes[i - 1].Padding.Top;
                open.Top = tops[i];
                open.Slot = _currentSlot;
                open.Opened = true;
                open.InsertAt = 0;
            }
        }

        /// <summary>Where each open box's part starts on a fresh page whose content top is
        /// <paramref name="pageTopY"/>, outermost first.</summary>
        private double[] PartTopsFrom(double pageTopY)
        {
            var tops = new double[_openBoxes.Count];
            var y = pageTopY;
            for (var i = 0; i < _openBoxes.Count; i++)
            {
                var open = _openBoxes[i];
                if (open.Box.TopMarginAfterBreak) y -= open.Box.Margin.Top;
                tops[i] = y;
                y -= open.Bands.Top + open.Padding.Top;
            }
            return tops;
        }

        /// <summary>How far under a fresh page's top a box's content starts again.</summary>
        private static double ReopeningDepth(BoxInFlow open) =>
            (open.Box.TopMarginAfterBreak ? open.Box.Margin.Top : 0) + open.Bands.Top + open.Padding.Top;
    }
}
