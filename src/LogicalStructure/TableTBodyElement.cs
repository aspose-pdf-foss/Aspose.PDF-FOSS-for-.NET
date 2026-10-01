using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

/// <summary>A table-body structure element (tag <c>/TBody</c>) that groups body rows.</summary>
public sealed class TableTBodyElement : StructureElement
{
    internal TableTBodyElement() : base("TBody") { }
    internal TableTBodyElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
    /// <summary>Create + append a TR child. FOSS-extra.</summary>
    public TableTRElement CreateTR() { var el = new TableTRElement(); AppendChild(el); return el; }
}
