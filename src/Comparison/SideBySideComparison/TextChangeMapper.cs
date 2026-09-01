using System;
using System.Collections.Generic;
using Aspose.Pdf.Comparison.Diff;

namespace Aspose.Pdf.Comparison.SideBySideComparison
{
    /// <summary>Maps diff edits back onto page geometry: walks the operation list with a
    /// cursor over one side's text and turns each edit belonging to that side into an
    /// <see cref="EditContainer"/> holding per-line page rectangles.</summary>
    internal class TextChangeMapper
    {
        private readonly SideBySideComparisonOptions _options;

        public TextChangeMapper(SideBySideComparisonOptions options)
        {
            _options = options;
        }

        /// <summary>Find the page rectangles for every diff edit of kind
        /// <paramref name="operation"/> (Delete when mapping the first page's fragments,
        /// Insert for the second). When AdditionalChangeMarks is on, edits of the opposite
        /// kind produce a thin positional caret instead. <paramref name="diffsIDs"/> maps a
        /// diff-operation index to its change id so paired edits correlate across pages.</summary>
        internal List<EditContainer> FindEditedFragments(List<Fragment> fragments,
            List<DiffOperation> diffOperations, Operation operation, Dictionary<int, int> diffsIDs)
        {
            var containers = new List<EditContainer>();
            var starts = BuildFragmentOffsets(fragments, out var totalLength);

            var position = 0;
            for (var i = 0; i < diffOperations.Count; i++)
            {
                var diff = diffOperations[i];
                var length = diff.Text?.Length ?? 0;
                if (diff.Operation == Operation.Equal)
                {
                    position += length;
                    continue;
                }

                var id = diffsIDs.TryGetValue(i, out var known) ? known : i;
                if (diff.Operation == operation)
                {
                    var container = new EditContainer(id, diff);
                    CollectRects(fragments, starts, totalLength, position, length, container.Rects);
                    containers.Add(container);
                    position += length;
                }
                else if (_options.AdditionalChangeMarks)
                {
                    // The other document changed here: mark the position with a caret sliver.
                    var container = new EditContainer(id, diff, isAdditionalMark: true);
                    var caret = CharRectAt(fragments, starts, totalLength, position);
                    if (caret is not null)
                        container.Rects.Add(new Rectangle(
                            caret.LLX, caret.LLY, caret.LLX + 1.5, caret.URY));
                    containers.Add(container);
                }
            }
            return containers;
        }

        private static int[] BuildFragmentOffsets(List<Fragment> fragments, out int totalLength)
        {
            var starts = new int[fragments.Count];
            var offset = 0;
            for (var i = 0; i < fragments.Count; i++)
            {
                starts[i] = offset;
                offset += fragments[i].Text.Length;
            }
            totalLength = offset;
            return starts;
        }

        /// <summary>Union the character rectangles of text span [start, start+length) into
        /// one rectangle per visual line, so a multi-line edit gets one highlight per line.</summary>
        private static void CollectRects(List<Fragment> fragments, int[] starts, int totalLength,
            int start, int length, List<Rectangle> rects)
        {
            if (length <= 0 || totalLength == 0 || start >= totalLength) return;
            var end = Math.Min(start + length, totalLength);

            Rectangle? current = null;
            var fragmentIndex = FindFragment(starts, start);
            for (var pos = start; pos < end; pos++)
            {
                while (fragmentIndex + 1 < starts.Length && starts[fragmentIndex + 1] <= pos)
                    fragmentIndex++;
                var fragment = fragments[fragmentIndex];
                var inFragment = pos - starts[fragmentIndex];
                if (inFragment >= fragment.Text.Length) continue;
                var ch = fragment.Text[inFragment];
                if (ch == '\n' || ch == '\r') { Flush(ref current, rects); continue; }

                var rect = fragment.FindCharRect(inFragment);
                if (rect.IsTrivial) continue;
                if (current is null)
                {
                    current = (Rectangle)rect.Clone();
                    continue;
                }

                var overlap = Math.Min(current.URY, rect.URY) - Math.Max(current.LLY, rect.LLY);
                var minHeight = Math.Min(current.Height, rect.Height);
                if (overlap > minHeight * 0.5)
                {
                    // Same line: extend the union.
                    current.LLX = Math.Min(current.LLX, rect.LLX);
                    current.URX = Math.Max(current.URX, rect.URX);
                    current.LLY = Math.Min(current.LLY, rect.LLY);
                    current.URY = Math.Max(current.URY, rect.URY);
                }
                else
                {
                    Flush(ref current, rects);
                    current = (Rectangle)rect.Clone();
                }
            }
            Flush(ref current, rects);
        }

        private static void Flush(ref Rectangle? current, List<Rectangle> rects)
        {
            if (current is not null) rects.Add(current);
            current = null;
        }

        private static Rectangle? CharRectAt(List<Fragment> fragments, int[] starts,
            int totalLength, int position)
        {
            if (fragments.Count == 0 || totalLength == 0) return null;
            if (position >= totalLength) position = totalLength - 1;
            if (position < 0) position = 0;
            var index = FindFragment(starts, position);
            var fragment = fragments[index];
            var inFragment = Math.Min(position - starts[index], fragment.Text.Length - 1);
            if (inFragment < 0) return null;
            var rect = fragment.FindCharRect(inFragment);
            return rect.IsTrivial ? null : rect;
        }

        private static int FindFragment(int[] starts, int position)
        {
            var lo = 0;
            var hi = starts.Length - 1;
            while (lo < hi)
            {
                var mid = (lo + hi + 1) / 2;
                if (starts[mid] <= position) lo = mid;
                else hi = mid - 1;
            }
            return lo;
        }
    }
}
