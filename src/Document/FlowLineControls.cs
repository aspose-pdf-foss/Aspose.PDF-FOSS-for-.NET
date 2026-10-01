namespace Aspose.Pdf;

public sealed partial class Document
{
    private sealed partial class FlowLayout
    {
        /// <summary>True when the cursor stands at the top of its region, below the paragraph's
        /// own top margin at most: moving the paragraph on would gain it no room.</summary>
        private bool NearRegionTop(Text.TextFragment tf) =>
            _curY >= (_colLefts is not null ? _colBandTop : ContentTop) - (tf.Margin?.Top ?? 0) - RegionTopTolerance;

        /// <summary>How close to its region's top a paragraph counts as standing at it.</summary>
        private const double RegionTopTolerance = 1;

        /// <summary>How many lines from <paramref name="from"/> fit under the cursor, each its own
        /// box tall, the paragraph's last one leaving the room kept under it
        /// (<see cref="BottomReserve"/>).</summary>
        private int LinesFittingRegion(IReadOnlyList<double> boxes, int from)
        {
            var y = _curY;
            var fit = 0;
            for (var li = from; li < boxes.Count; li++)
            {
                if (y - boxes[li] - (li == boxes.Count - 1 ? BottomReserve : 0) < EffectiveBottom) break;
                y -= boxes[li];
                fit++;
            }
            return fit;
        }

        /// <summary>The line of a paragraph of runs before which its line controls break the
        /// region, planned from line <paramref name="from"/> where the region stands now; null
        /// when they leave the breaks to the flow.</summary>
        private int? LineControlBreak(Text.TextFragment tf, IReadOnlyList<double> boxes, int from, bool mayMoveWhole)
        {
            var options = tf.TextState.FormattingOptions;
            if (!Text.LineControls.Any(options)) return null;
            var fit = LinesFittingRegion(boxes, from);
            var kept = Text.LineControls.Kept(options, fit, boxes.Count - from, from == 0, mayMoveWhole);
            return kept == fit ? null : from + kept;
        }
    }
}
