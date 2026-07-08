namespace Aspose.Pdf.Tagged;

/// <summary>
/// Layout/position settings for a tagged structure element, passed to
/// <see cref="Aspose.Pdf.LogicalStructure.StructureElement.AdjustPosition"/>.
/// The renderer honours <see cref="Margin"/> when laying an authored block
/// onto the page (Left/Right indent the column, Top adds space above it).
/// </summary>
public sealed class PositionSettings
{
    /// <summary>Horizontal alignment of the element within its column.</summary>
    public HorizontalAlignment HorizontalAlignment { get; set; } = HorizontalAlignment.None;

    /// <summary>Vertical alignment of the element.</summary>
    public VerticalAlignment VerticalAlignment { get; set; } = VerticalAlignment.None;

    /// <summary>Margin around the element (Left, Bottom, Right, Top).</summary>
    public MarginInfo? Margin { get; set; }

    /// <summary>Whether the element starts a new column. Stored only.</summary>
    public bool IsFirstParagraphInColumn { get; set; }

    /// <summary>Whether the element is kept with the next one. Stored only.</summary>
    public bool IsKeptWithNext { get; set; }

    /// <summary>Whether the element starts on a new page. Stored only.</summary>
    public bool IsInNewPage { get; set; }

    /// <summary>Whether the element flows inline within a paragraph. Stored only.</summary>
    public bool IsInLineParagraph { get; set; }
}
