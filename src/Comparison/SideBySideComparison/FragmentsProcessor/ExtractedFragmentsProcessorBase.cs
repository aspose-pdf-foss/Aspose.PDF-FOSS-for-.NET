using System.Collections.Generic;
using System.Text;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Comparison.SideBySideComparison
{
    /// <summary>Extracts a page's text fragments, filters them by the configured
    /// comparison/exclusion areas, sorts them into reading order and converts them into
    /// comparison <see cref="Fragment"/>s whose concatenated text is what gets diffed.</summary>
    internal abstract class ExtractedFragmentsProcessorBase
    {
        private readonly TextFragmentRectanglesComparer _fragmentsComparer;

        /// <summary>Areas whose text is dropped from the comparison (fragment centre test).</summary>
        internal Rectangle[]? ExcludeAreas { get; set; }

        /// <summary>When set, only fragments intersecting this area are compared.</summary>
        internal Rectangle? SearchArea { get; set; }

        protected ExtractedFragmentsProcessorBase(TextFragmentRectanglesComparer fragmentsComparer)
        {
            _fragmentsComparer = fragmentsComparer;
        }

        /// <summary>Extract, filter, sort and wrap the page's fragments;
        /// <paramref name="text"/> receives the concatenation of every fragment's
        /// comparable text (the string handed to the diff).</summary>
        internal List<Fragment> PrepareFragments(Page? page, out string text)
        {
            var fragments = new List<Fragment>();
            if (page is not null)
            {
                var raw = GetTextFragments(page);
                raw = Filter(raw);
                // Stable reading-order sort — OrderBy keeps document order inside a line.
                raw = new List<TextFragment>(System.Linq.Enumerable.OrderBy(
                    raw, f => f, new TextFragmentReadingOrderComparer(_fragmentsComparer)));
                fragments = ProcessSortedFragments(raw);
            }

            var sb = new StringBuilder();
            foreach (var fragment in fragments) sb.Append(fragment.Text);
            text = sb.ToString();
            return fragments;
        }

        protected abstract List<TextFragment> GetTextFragments(Page page);

        protected abstract List<Fragment> ProcessSortedFragments(List<TextFragment> fragments);

        private List<TextFragment> Filter(List<TextFragment> fragments)
        {
            if (SearchArea is null && (ExcludeAreas is null || ExcludeAreas.Length == 0))
                return fragments;

            var kept = new List<TextFragment>(fragments.Count);
            foreach (var fragment in fragments)
            {
                var rect = fragment.Rectangle;
                if (rect is null) continue;
                if (SearchArea is not null && !Intersects(SearchArea, rect)) continue;
                if (IsExcluded(rect)) continue;
                kept.Add(fragment);
            }
            return kept;
        }

        private bool IsExcluded(Rectangle rect)
        {
            if (ExcludeAreas is null) return false;
            var cx = (rect.LLX + rect.URX) / 2;
            var cy = (rect.LLY + rect.URY) / 2;
            foreach (var area in ExcludeAreas)
                if (area is not null && area.ContainsPoint(cx, cy))
                    return true;
            return false;
        }

        private static bool Intersects(Rectangle a, Rectangle b)
            => a.LLX <= b.URX && b.LLX <= a.URX && a.LLY <= b.URY && b.LLY <= a.URY;

        /// <summary>Adapts the rectangle comparer to whole fragments for the sort.</summary>
        private sealed class TextFragmentReadingOrderComparer : IComparer<TextFragment>
        {
            private readonly TextFragmentRectanglesComparer _inner;

            public TextFragmentReadingOrderComparer(TextFragmentRectanglesComparer inner)
                => _inner = inner;

            public int Compare(TextFragment? x, TextFragment? y)
            {
                if (x is null || y is null) return 0;
                return _inner.Compare(x.Rectangle ?? Rectangle.Trivial, y.Rectangle ?? Rectangle.Trivial);
            }
        }
    }
}
