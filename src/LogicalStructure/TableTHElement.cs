using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

/// <summary>A table header-cell structure element (tag <c>/TH</c>), a child of a table row.</summary>
public sealed class TableTHElement : StructureElement
{
    internal TableTHElement() : base("TH") { }
    internal TableTHElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }

    // Cell-level style properties (stored only).
    /// <summary>Gets or sets the cell background colour. Stored only; it does not affect how the cell is drawn.</summary>
    public Aspose.Pdf.Color? BackgroundColor { get; set; }
    /// <summary>Gets or sets the cell border. Stored only; it does not affect how the cell is drawn.</summary>
    public Aspose.Pdf.BorderInfo? Border { get; set; }
    /// <summary>Gets or sets whether the cell has no border. Stored only; it does not affect how the cell is drawn.</summary>
    public bool IsNoBorder { get; set; }
    /// <summary>Gets or sets the cell margin. Stored only; it does not affect how the cell is drawn.</summary>
    public Aspose.Pdf.MarginInfo? Margin { get; set; }
    /// <summary>Gets or sets the horizontal alignment of the cell content. Stored only; it does not affect how the cell is drawn.</summary>
    public Aspose.Pdf.HorizontalAlignment Alignment { get; set; }
    /// <summary>Gets or sets the vertical alignment of the cell content. Stored only; it does not affect how the cell is drawn.</summary>
    public Aspose.Pdf.VerticalAlignment VerticalAlignment { get; set; }
    /// <summary>Gets or sets the default text style of the cell. Stored only; it does not affect how the cell is drawn.</summary>
    public Aspose.Pdf.Text.TextState DefaultCellTextState { get; set; } = new Aspose.Pdf.Text.TextState();
    /// <summary>Gets or sets whether text in the cell wraps. Stored only; it does not affect how the cell is drawn.</summary>
    public bool IsWordWrapped { get; set; }

    /// <summary>The columns the cell spans - the /ColSpan table attribute; one when unset.</summary>
    public int ColSpan
    {
        get => TableSpan(AttributeKey.ColSpan);
        set => SetTableSpan(AttributeKey.ColSpan, value);
    }

    /// <summary>The rows the cell spans - the /RowSpan table attribute; one when unset.</summary>
    public int RowSpan
    {
        get => TableSpan(AttributeKey.RowSpan);
        set => SetTableSpan(AttributeKey.RowSpan, value);
    }
}
