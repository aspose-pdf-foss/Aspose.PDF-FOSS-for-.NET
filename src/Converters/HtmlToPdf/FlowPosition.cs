namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Where a flow has reached: the page being written to and the baseline the next
    /// block starts from. A layout method that takes one MOVES it - a table that paginates
    /// leaves the cursor on the page it ended on - so the caller reads the position back
    /// from the object it passed, and a nested grid that must not move its row's cursor
    /// is given a position of its own.</summary>
    private class FlowPosition
    {
        /// <summary>The page being written to; a page break replaces it.</summary>
        public Page page = null!;
        /// <summary>The flow cursor: the baseline the next block starts from, in points from the page bottom.</summary>
        public double y;
        /// <summary>The horizontal box of the table laid out last (NaN until one has): a wrapper frames around its children's union.</summary>
        public double lastTableX0 = double.NaN;
        public double lastTableX1 = double.NaN;
    }
}
