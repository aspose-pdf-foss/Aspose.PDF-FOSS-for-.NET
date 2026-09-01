using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

public sealed class TableTBodyElement : StructureElement
{
    internal TableTBodyElement() : base("TBody") { }
    internal TableTBodyElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
    /// <summary>Create + append a TR child. FOSS-extra.</summary>
    public TableTRElement CreateTR() { var el = new TableTRElement(); AppendChild(el); return el; }
}
