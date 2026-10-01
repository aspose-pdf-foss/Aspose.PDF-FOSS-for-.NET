namespace Aspose.Pdf;

public partial class Table
{
    /// <summary>Whether a picture in a cell takes EXACTLY its box -- its drawn
    /// height plus its own top and bottom margins, seated its left margin in,
    /// stacked with the cell's lines at their own heights -- instead of the whole
    /// lines of the row's pitch it reserves by default (a 26.67 pt picture then
    /// costs three 12 pt lines). The cell's height becomes its own stack. Off by
    /// default; a caller whose grid stacks content by its boxes asks for it.</summary>
    public bool CellPictureBoxesAreExact { get; set; }
}
