using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

/// <summary>A table-row structure element (tag <c>/TR</c>) that holds <c>/TD</c> and <c>/TH</c> cells.</summary>
public sealed class TableTRElement : StructureElement
{
    internal TableTRElement() : base("TR") { }
    internal TableTRElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }

    // Row-level style properties (stored only).
    /// <summary>Gets or sets the row background colour. Stored only; it does not affect how the row is drawn.</summary>
    public Aspose.Pdf.Color? BackgroundColor { get; set; }
    /// <summary>Gets or sets the row border. Stored only; it does not affect how the row is drawn.</summary>
    public Aspose.Pdf.BorderInfo? Border { get; set; }
    /// <summary>Gets or sets the default border of the cells in the row. Stored only; it does not affect how the row is drawn.</summary>
    public Aspose.Pdf.BorderInfo? DefaultCellBorder { get; set; }
    /// <summary>Gets or sets the minimum row height, in points. Stored only; it does not affect how the row is drawn.</summary>
    public double MinRowHeight { get; set; }
    /// <summary>Gets or sets a fixed row height, in points. Stored only; it does not affect how the row is drawn.</summary>
    public double FixedRowHeight { get; set; }
    /// <summary>Gets or sets whether the row starts on a new page. Stored only; it does not affect how the row is drawn.</summary>
    public bool IsInNewPage { get; set; }
    /// <summary>Gets or sets whether the row may break across pages. Stored only; it does not affect how the row is drawn.</summary>
    public bool IsRowBroken { get; set; }
    /// <summary>Gets or sets the default text style of the cells in the row. Stored only; it does not affect how the row is drawn.</summary>
    public Aspose.Pdf.Text.TextState DefaultCellTextState { get; set; } = new Aspose.Pdf.Text.TextState();
    /// <summary>Gets or sets the default padding of the cells in the row. Stored only; it does not affect how the row is drawn.</summary>
    public Aspose.Pdf.MarginInfo? DefaultCellPadding { get; set; }
    /// <summary>Gets or sets the vertical alignment of the cells in the row. Stored only; it does not affect how the row is drawn.</summary>
    public Aspose.Pdf.VerticalAlignment VerticalAlignment { get; set; }

    /// <summary>Create + append a TD child. FOSS-extra.</summary>
    public TableTDElement CreateTD() { var el = new TableTDElement(); AppendChild(el); return el; }
    /// <summary>Create + append a TH child. FOSS-extra.</summary>
    public TableTHElement CreateTH() { var el = new TableTHElement(); AppendChild(el); return el; }
}
