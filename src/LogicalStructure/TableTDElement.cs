using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

public sealed class TableTDElement : StructureElement
{
    internal TableTDElement() : base("TD") { }
    internal TableTDElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }

    // Cell-level style properties (stored only).
    public Aspose.Pdf.Color? BackgroundColor { get; set; }
    public Aspose.Pdf.BorderInfo? Border { get; set; }
    public bool IsNoBorder { get; set; }
    public Aspose.Pdf.MarginInfo? Margin { get; set; }
    public Aspose.Pdf.HorizontalAlignment Alignment { get; set; }
    public Aspose.Pdf.VerticalAlignment VerticalAlignment { get; set; }
    public Aspose.Pdf.Text.TextState DefaultCellTextState { get; set; } = new Aspose.Pdf.Text.TextState();
    public bool IsWordWrapped { get; set; }
    public int ColSpan { get; set; }
    public int RowSpan { get; set; }
}
