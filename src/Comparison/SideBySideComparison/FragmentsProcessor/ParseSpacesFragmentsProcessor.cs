using System;
using System.Collections.Generic;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Comparison.SideBySideComparison
{
    /// <summary>ParseSpaces mode: reconstructs inter-word spaces and line breaks from glyph
    /// geometry so the compared texts carry explicit whitespace where the page shows a gap.</summary>
    internal class ParseSpacesFragmentsProcessor : NormalFragmentProcessor
    {
        /// <summary>When true (default) a fragment that ends its line contributes an explicit
        /// line-break character to the comparison text.</summary>
        internal bool ParseLineBreaks { get; set; } = true;

        internal ParseSpacesFragmentsProcessor(TextFragmentRectanglesComparer fragmentsComparer)
            : base(fragmentsComparer)
        {
        }

        protected override List<Fragment> ProcessSortedFragments(List<TextFragment> fragments)
        {
            var result = new List<Fragment>(fragments.Count);
            for (var i = 0; i < fragments.Count; i++)
            {
                var current = fragments[i];
                var next = i + 1 < fragments.Count ? fragments[i + 1] : null;
                var rect = current.Rectangle;
                var nextRect = next?.Rectangle;

                if (next is null || rect is null || nextRect is null)
                {
                    result.Add(new FragmentWithSpaces(current));
                    continue;
                }

                var overlap = Math.Min(rect.URY, nextRect.URY) - Math.Max(rect.LLY, nextRect.LLY);
                var minHeight = Math.Min(rect.Height, nextRect.Height);
                var sameLine = overlap > minHeight * 0.5;
                if (!sameLine)
                {
                    result.Add(new FragmentWithSpaces(current, ParseLineBreaks));
                    continue;
                }

                var gap = nextRect.LLX - rect.URX;
                var spaceWidth = EstimateSpaceWidth(current);
                if (gap > spaceWidth * 0.5)
                {
                    var count = Math.Max(1, Math.Min(100, (int)Math.Round(gap / spaceWidth)));
                    result.Add(new FragmentWithSpaces(current, count, Math.Max(gap, 0)));
                }
                else
                {
                    result.Add(new FragmentWithSpaces(current));
                }
            }
            return result;
        }

        private static double EstimateSpaceWidth(TextFragment fragment)
        {
            double fontSize = fragment.TextState?.FontSize ?? 0;
            if (fontSize <= 0 && fragment.Rectangle is { } rect) fontSize = rect.Height;
            if (fontSize <= 0) fontSize = 10;
            return fontSize * 0.28;
        }
    }
}
