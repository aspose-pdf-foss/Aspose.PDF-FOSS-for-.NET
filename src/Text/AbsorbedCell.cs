using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

/// <summary>
/// Represents a cell in an absorbed table.
/// </summary>
public sealed class AbsorbedCell : IComparable<AbsorbedCell>
{
    public string Text { get; init; } = "";
    public Rectangle? Rect { get; init; }
    /// <summary>Alias for Rect.</summary>
    public Rectangle? Rectangle => Rect;
    /// <summary>Text fragments in the cell. 1-based indexer.</summary>
    public TextFragmentCollection TextFragments { get; internal init; } = new();

    /// <summary>Per-cell border info; null when the cell uses the table-default border. Stored only.</summary>
    public BorderInfo? BorderInfo { get; internal init; }

    /// <summary>How many columns this cell spans. Stored only.</summary>
    public int ColSpan { get; internal init; } = 1;

    /// <summary>Order cells top-down then left-to-right by their bounding rectangle.</summary>
    public int CompareTo(AbsorbedCell? other)
    {
        if (other is null) return 1;
        if (Rect is null || other.Rect is null) return 0;
        // PDF Y grows up — top-first means larger URY first.
        var dy = other.Rect.URY.CompareTo(Rect.URY);
        return dy != 0 ? dy : Rect.LLX.CompareTo(other.Rect.LLX);
    }

    internal static TextFragmentCollection ToCollection(IEnumerable<TextFragment> items)
    {
        var c = new TextFragmentCollection();
        foreach (var f in items) c.Add(f);
        return c;
    }
}
