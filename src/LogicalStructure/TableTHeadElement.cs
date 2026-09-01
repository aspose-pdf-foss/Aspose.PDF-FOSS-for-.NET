using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

public sealed class TableTHeadElement : StructureElement
{
    internal TableTHeadElement() : base("THead") { }
    internal TableTHeadElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
    public TableTRElement CreateTR() { var el = new TableTRElement(); AppendChild(el); return el; }
}
