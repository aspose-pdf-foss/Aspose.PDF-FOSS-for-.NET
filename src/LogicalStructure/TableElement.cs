using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

/// <summary>A table structure element (tag <c>/Table</c>) whose rows are <c>/TR</c> elements, directly or inside
/// head, body and footer groups. The tagged-content renderer draws its cells as text in a simple grid.</summary>
public sealed class TableElement : StructureElement
{
    internal TableElement() : base("Table") { }
    internal TableElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }

    // Table-level style properties (stored only — the FOSS structure
    // builder records them on the element but doesn't re-flow the table).
    /// <summary>Gets or sets how many leading rows are repeated at the top of each continuation page when the table
    /// runs onto a new page. Defaults to 0.</summary>
    public int RepeatingRowsCount { get; set; }
    /// <summary>Gets or sets the number of repeating columns. Stored only; it does not affect how the table is drawn.</summary>
    public int RepeatingColumnsCount { get; set; }
    /// <summary>Gets or sets the text style for repeated rows. Stored only; it does not affect how the table is drawn.</summary>
    public Aspose.Pdf.Text.TextState? RepeatingRowsStyle { get; set; }
    /// <summary>Gets or sets the table background colour. Stored only; it does not affect how the table is drawn.</summary>
    public Aspose.Pdf.Color? BackgroundColor { get; set; }
    /// <summary>Gets or sets the table border. Stored only; it does not affect how the table is drawn.</summary>
    public Aspose.Pdf.BorderInfo? Border { get; set; }
    /// <summary>Gets or sets the horizontal alignment of the table. Stored only; it does not affect how the table is drawn.</summary>
    public Aspose.Pdf.HorizontalAlignment Alignment { get; set; }
    /// <summary>Gets or sets the border corner style. Stored only; it does not affect how the table is drawn.</summary>
    public Aspose.Pdf.BorderCornerStyle CornerStyle { get; set; }
    /// <summary>Gets or sets how the table is split across pages. Stored only; it does not affect how the table is drawn.</summary>
    public Aspose.Pdf.TableBroken Broken { get; set; }
    /// <summary>Gets or sets how column widths are adjusted. Stored only; it does not affect how the table is drawn.</summary>
    public Aspose.Pdf.ColumnAdjustment ColumnAdjustment { get; set; }
    /// <summary>Gets or sets the column widths as a space-separated list. Stored only; it does not affect how the table is drawn.</summary>
    public string? ColumnWidths { get; set; }
    /// <summary>Gets or sets the default column width. Stored only; it does not affect how the table is drawn.</summary>
    public string? DefaultColumnWidth { get; set; }
    /// <summary>Gets or sets the default border of the cells. Stored only; it does not affect how the table is drawn.</summary>
    public Aspose.Pdf.BorderInfo? DefaultCellBorder { get; set; }
    /// <summary>Gets or sets the default padding of the cells. Stored only; it does not affect how the table is drawn.</summary>
    public Aspose.Pdf.MarginInfo? DefaultCellPadding { get; set; }
    /// <summary>Gets or sets the default text style of the cells. Stored only; it does not affect how the table is drawn.</summary>
    public Aspose.Pdf.Text.TextState DefaultCellTextState { get; set; } = new Aspose.Pdf.Text.TextState();
    /// <summary>Gets or sets whether the table may break across pages. Stored only; it does not affect how the table is drawn.</summary>
    public bool IsBroken { get; set; }
    /// <summary>Gets or sets whether borders are included in the table width. Stored only; it does not affect how the table is drawn.</summary>
    public bool IsBordersIncluded { get; set; }
    /// <summary>Gets or sets the left position of the table, in points. Stored only; it does not affect how the table is drawn.</summary>
    public float Left { get; set; }
    /// <summary>Gets or sets the top position of the table, in points. Stored only; it does not affect how the table is drawn.</summary>
    public float Top { get; set; }

    /// <summary>Create + append a TBody child. FOSS-extra authoring helper.</summary>
    public TableTBodyElement CreateTBody()
    {
        var el = new TableTBodyElement();
        AppendChild(el);
        return el;
    }

    /// <summary>Create + append a THead child. FOSS-extra authoring helper.</summary>
    public TableTHeadElement CreateTHead()
    {
        var el = new TableTHeadElement();
        AppendChild(el);
        return el;
    }

    /// <summary>Create + append a TFoot child. FOSS-extra authoring helper.</summary>
    public TableTFootElement CreateTFoot()
    {
        var el = new TableTFootElement();
        AppendChild(el);
        return el;
    }
}
