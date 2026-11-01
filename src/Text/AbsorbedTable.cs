using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

#pragma warning disable S1210 // public API: ordering only, the published class defines no operators
/// <summary>
/// Represents a table detected on a PDF page.
/// </summary>
public sealed class AbsorbedTable : IComparable<AbsorbedTable>
#pragma warning restore S1210
{
    /// <summary>Creates an empty table with no rows.</summary>
    public AbsorbedTable() { }

    public IReadOnlyList<AbsorbedRow> Rows { get; init; } = [];

    /// <summary>Mutable row list.</summary>
    public IList<AbsorbedRow> RowList
    {
        get
        {
            if (Rows is IList<AbsorbedRow> list) return list;
#pragma warning disable S2365 // public API: the published list view
            return new List<AbsorbedRow>(Rows);
#pragma warning restore S2365
        }
    }

    /// <summary>Gets the bounding rectangle of the table on the page, in points; <c>null</c> when unknown.</summary>
    public Rectangle? Rect { get; init; }
    /// <summary>Alias for Rect.</summary>
    public Rectangle? Rectangle => Rect;

    /// <summary>The 1-based page number this table was detected on (0 when unset).</summary>
    public int PageNum { get; internal init; }

    public int CompareTo(AbsorbedTable? other)
    {
        if (other is null) return 1;
        if (PageNum != other.PageNum) return PageNum.CompareTo(other.PageNum);
        if (Rect is null || other.Rect is null) return 0;
        return other.Rect.URY.CompareTo(Rect.URY);
    }
}
