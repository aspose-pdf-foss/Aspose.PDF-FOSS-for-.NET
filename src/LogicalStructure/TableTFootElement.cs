using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

/// <summary>A table-footer structure element (tag <c>/TFoot</c>) that groups footer rows.</summary>
public sealed class TableTFootElement : StructureElement
{
    internal TableTFootElement() : base("TFoot") { }
    internal TableTFootElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
    /// <summary>Creates a table row element and appends it as the last child of this footer.</summary>
    public TableTRElement CreateTR() { var el = new TableTRElement(); AppendChild(el); return el; }
}
