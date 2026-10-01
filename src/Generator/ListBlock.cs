using Aspose.Pdf.Text;

namespace Aspose.Pdf;

/// <summary>A list: items laid one under another, each item's paragraphs set in a column
/// the list indents past its markers, and each item's marker set on the item's first line.
///
/// The column is shared by the whole list: it starts at the list's left edge (its own left
/// margin in from the region), runs as wide as the WIDEST marker of any item, then the
/// <see cref="MarkerGap"/>, and every item's text starts there - so markers of different
/// widths ("9." and "10.") still leave the items' text on one line. A marker stands on the
/// baseline of the first line its item lays, whichever paragraph that is (an item that opens
/// with a nested list takes the nested list's first line), and is drawn after the item's
/// content on the page that line landed on. A picture marker stands on that baseline too.
///
/// An item's paragraphs are laid as the flow lays any paragraph - margins, wrapping, page
/// breaks - only indented to the column; a nested list is one of those paragraphs, and its
/// own column starts where its item's text does.</summary>
public sealed class ListBlock : BaseParagraph
{
    /// <summary>The items, in order.</summary>
    public List<ListBlockItem> Items { get; } = new();

    /// <summary>The space between the widest marker and the items' text, in points.</summary>
    public double MarkerGap { get; set; }

    /// <summary>Where a marker stands in the column: <see cref="HorizontalAlignment.Right"/>
    /// (the default) ends it at the gap before the text, so markers of different widths line
    /// up on their right edges; <see cref="HorizontalAlignment.Left"/> starts it at the list's
    /// left edge.</summary>
    public HorizontalAlignment MarkerAlignment { get; set; } = HorizontalAlignment.Right;
}

/// <summary>One item of a <see cref="ListBlock"/>: its marker and its paragraphs.</summary>
public sealed class ListBlockItem
{
    /// <summary>The marker, laid as a run of text in its own face, size and colour; a segment
    /// carrying an <see cref="TextSegment.InlineImage"/> with a fixed box is a picture marker.
    /// Null for an item that shows none.</summary>
    public TextFragment? Marker { get; set; }

    /// <summary>What the item holds: text fragments, and lists nested in it.</summary>
    public Paragraphs Paragraphs { get; } = new();
}
