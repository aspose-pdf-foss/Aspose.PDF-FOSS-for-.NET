using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

public sealed class TableElement : StructureElement
{
    internal TableElement() : base("Table") { }
    internal TableElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }

    // Table-level style properties (stored only — the FOSS structure
    // builder records them on the element but doesn't re-flow the table).
    public int RepeatingRowsCount { get; set; }
    public int RepeatingColumnsCount { get; set; }
    public Aspose.Pdf.Text.TextState? RepeatingRowsStyle { get; set; }
    public Aspose.Pdf.Color? BackgroundColor { get; set; }
    public Aspose.Pdf.BorderInfo? Border { get; set; }
    public Aspose.Pdf.HorizontalAlignment Alignment { get; set; }
    public Aspose.Pdf.BorderCornerStyle CornerStyle { get; set; }
    public Aspose.Pdf.TableBroken Broken { get; set; }
    public Aspose.Pdf.ColumnAdjustment ColumnAdjustment { get; set; }
    public string? ColumnWidths { get; set; }
    public string? DefaultColumnWidth { get; set; }
    public Aspose.Pdf.BorderInfo? DefaultCellBorder { get; set; }
    public Aspose.Pdf.MarginInfo? DefaultCellPadding { get; set; }
    public Aspose.Pdf.Text.TextState DefaultCellTextState { get; set; } = new Aspose.Pdf.Text.TextState();
    public bool IsBroken { get; set; }
    public bool IsBordersIncluded { get; set; }
    public float Left { get; set; }
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
