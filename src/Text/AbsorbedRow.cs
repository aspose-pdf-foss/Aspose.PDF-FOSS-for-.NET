using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

#pragma warning disable S1210 // public API: ordering only, the published class defines no operators
/// <summary>
/// Represents a row in an absorbed table.
/// </summary>
public sealed class AbsorbedRow : IComparable<AbsorbedRow>
#pragma warning restore S1210
{
    /// <summary>Creates an empty row with no cells.</summary>
    public AbsorbedRow() { }

    public IReadOnlyList<AbsorbedCell> Cells { get; init; } = [];

    /// <summary>Mutable cell list.</summary>
    public IList<AbsorbedCell> CellList
    {
        get
        {
            if (Cells is IList<AbsorbedCell> list) return list;
#pragma warning disable S2365 // public API: the published list view
            return new List<AbsorbedCell>(Cells);
#pragma warning restore S2365
        }
    }

    /// <summary>Bounding rectangle of this row (computed from its cells).</summary>
    public Rectangle? Rectangle
    {
        get
        {
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
            double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
            bool any = false;
            foreach (var rect in Cells.Select(c => c.Rect))
            {
                if (rect is null) continue;
                if (rect.LLX < minX) minX = rect.LLX;
                if (rect.LLY < minY) minY = rect.LLY;
                if (rect.URX > maxX) maxX = rect.URX;
                if (rect.URY > maxY) maxY = rect.URY;
                any = true;
            }
            return any ? new Rectangle(minX, minY, maxX, maxY) : null;
        }
    }

    public int CompareTo(AbsorbedRow? other)
    {
        if (other is null) return 1;
        var a = Rectangle; var b = other.Rectangle;
        if (a is null || b is null) return 0;
        // Top-down in PDF coords means highest URY first.
        return b.URY.CompareTo(a.URY);
    }
}
