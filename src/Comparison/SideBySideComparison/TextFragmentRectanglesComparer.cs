using System;
using System.Collections.Generic;

namespace Aspose.Pdf.Comparison.SideBySideComparison
{
    /// <summary>Orders fragment rectangles in reading order: fragments whose vertical spans
    /// overlap by more than half the smaller height count as one line and sort left-to-right;
    /// otherwise the higher fragment comes first.</summary>
    internal class TextFragmentRectanglesComparer : IComparer<Rectangle>
    {
        public int Compare(Rectangle? x, Rectangle? y)
        {
            if (x is null || y is null) return 0;
            var overlap = Math.Min(x.URY, y.URY) - Math.Max(x.LLY, y.LLY);
            var minHeight = Math.Min(x.Height, y.Height);
            if (overlap > minHeight * 0.5)
                return x.LLX.CompareTo(y.LLX);
            return y.URY.CompareTo(x.URY);
        }
    }
}
