using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

/// <summary>A table-header structure element (tag <c>/THead</c>) that groups header rows.</summary>
public sealed class TableTHeadElement : StructureElement
{
    internal TableTHeadElement() : base("THead") { }
    internal TableTHeadElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
    /// <summary>Creates a table row element and appends it as the last child of this header.</summary>
    public TableTRElement CreateTR() { var el = new TableTRElement(); AppendChild(el); return el; }
}
