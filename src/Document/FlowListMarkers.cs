namespace Aspose.Pdf;

public sealed partial class Document
{
    private sealed partial class FlowLayout
    {
        /// <summary>Open first-line captures, innermost last: each is filled by the first body
        /// line laid after it opened, with the page slot, the baseline and the region's left edge
        /// (without the flow's own indent). A list item opens one, so its marker can stand on
        /// the line its content opens with - which a nested list inside the item may also want,
        /// hence a stack: one line can fill every capture still waiting.</summary>
        private readonly List<(int Slot, double Baseline, double Left)?> _firstLineCaptures = new();

        /// <summary>Starts waiting for the next body line the flow lays.</summary>
        public void OpenFirstLineCapture() => _firstLineCaptures.Add(null);

        /// <summary>Stops waiting and hands back the line that came, or null when none did.</summary>
        public (int Slot, double Baseline, double Left)? CloseFirstLineCapture()
        {
            var last = _firstLineCaptures[^1];
            _firstLineCaptures.RemoveAt(_firstLineCaptures.Count - 1);
            return last;
        }

        /// <summary>A body line was laid on this baseline: every capture still waiting takes it.</summary>
        private void CaptureFirstLine(double baseline)
        {
            for (var i = 0; i < _firstLineCaptures.Count; i++)
                if (_firstLineCaptures[i] is null)
                    _firstLineCaptures[i] = (_currentSlot, baseline, CurLeft - LeftIndent);
        }

        /// <summary>How wide a marker is: its runs measured in their own faces and sizes, a
        /// picture by its box. Zero for a marker this writer cannot lay.</summary>
        public double MeasureMarker(Text.TextFragment marker)
        {
            if (CollectSegmentRuns(marker) is not { Count: > 0 } runs) return 0;
            double width = 0;
            foreach (var run in runs) width += MeasureRunWidth(run, run.Text) + run.Extra;
            return width;
        }

        /// <summary>Draws a marker from <paramref name="x"/> on a captured line: its runs on the
        /// line's baseline, a picture standing on it, on the page the line landed on. It goes out
        /// straight away, after the content already written there, unless the flow is deferring
        /// its text to keep the stream in paragraph order - then it joins that queue too.</summary>
        public void SetListMarker(Text.TextFragment marker, double x, (int Slot, double Baseline, double Left) line)
        {
            if (CollectSegmentRuns(marker) is not { Count: > 0 } runs) return;
            if (_forceDeferredWrites)
            {
                QueueDeferredMarker(runs, x, line);
                return;
            }
            var pieces = new List<Text.RunPiece>(runs.Count);
            for (var i = 0; i < runs.Count; i++) pieces.Add(new Text.RunPiece(i, runs[i].Text));
            var resources = new Dictionary<string, string>(StringComparer.Ordinal);
            Table.RegisterFont(_startPage);
            AddContentToSlot(line.Slot, BuildSegmentLine(runs, pieces, x, line.Baseline, resources, marker.TextState, line.Slot));
        }

        /// <summary>A marker whose text must wait with the rest of the flow's deferred text: each
        /// run queued at its own x, a picture placed on its page.</summary>
        private void QueueDeferredMarker(List<SegmentRun> runs, double x, (int Slot, double Baseline, double Left) line)
        {
            foreach (var run in runs)
            {
                if (run.Picture is not null)
                    PlacePictureOnSlot(line.Slot, run.Picture,
                        new Rectangle(x, line.Baseline, x + run.PictureWidth, line.Baseline + run.PictureHeight));
                else
                {
                    // The same state and seat the segment writer defers a run with: a
                    // standard face by its name, an embedded one lifted by its descent.
                    var (state, seat) = DeferredRun(run, line.Baseline);
                    _pendingEmbeddedRenders.Add((line.Slot, x, line.Baseline, run.Text, state, run.FontSize, seat));
                }
                x += MeasureRunWidth(run, run.Text) + run.Extra;
            }
        }

        /// <summary>Places a picture on the page a slot became: the start page at once, a later
        /// page once it exists.</summary>
        private void PlacePictureOnSlot(int slot, byte[] data, Rectangle rect)
        {
            if (slot < 0) { _startPage.AddImage(data, rect); return; }
            _pendingImages.Add((slot, data, rect, false));
        }
    }
}
