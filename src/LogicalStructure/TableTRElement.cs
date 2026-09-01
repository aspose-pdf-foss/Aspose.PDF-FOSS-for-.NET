using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

public sealed class TableTRElement : StructureElement
{
    internal TableTRElement() : base("TR") { }
    internal TableTRElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }

    // Row-level style properties (stored only).
    public Aspose.Pdf.Color? BackgroundColor { get; set; }
    public Aspose.Pdf.BorderInfo? Border { get; set; }
    public Aspose.Pdf.BorderInfo? DefaultCellBorder { get; set; }
    public double MinRowHeight { get; set; }
    public double FixedRowHeight { get; set; }
    public bool IsInNewPage { get; set; }
    public bool IsRowBroken { get; set; }
    public Aspose.Pdf.Text.TextState DefaultCellTextState { get; set; } = new Aspose.Pdf.Text.TextState();
    public Aspose.Pdf.MarginInfo? DefaultCellPadding { get; set; }
    public Aspose.Pdf.VerticalAlignment VerticalAlignment { get; set; }

    /// <summary>Create + append a TD child. FOSS-extra.</summary>
    public TableTDElement CreateTD() { var el = new TableTDElement(); AppendChild(el); return el; }
    /// <summary>Create + append a TH child. FOSS-extra.</summary>
    public TableTHElement CreateTH() { var el = new TableTHElement(); AppendChild(el); return el; }
}
