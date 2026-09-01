using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

/// <summary>
/// Represents a row in an absorbed table.
/// </summary>
public sealed class AbsorbedRow : IComparable<AbsorbedRow>
{
    public IReadOnlyList<AbsorbedCell> Cells { get; init; } = [];

    /// <summary>Mutable cell list.</summary>
    public IList<AbsorbedCell> CellList
    {
        get
        {
            if (Cells is IList<AbsorbedCell> list) return list;
            return new List<AbsorbedCell>(Cells);
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
            foreach (var c in Cells)
            {
                if (c.Rect is null) continue;
                if (c.Rect.LLX < minX) minX = c.Rect.LLX;
                if (c.Rect.LLY < minY) minY = c.Rect.LLY;
                if (c.Rect.URX > maxX) maxX = c.Rect.URX;
                if (c.Rect.URY > maxY) maxY = c.Rect.URY;
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
