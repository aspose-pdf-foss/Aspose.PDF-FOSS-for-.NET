using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

public sealed class TableTFootElement : StructureElement
{
    internal TableTFootElement() : base("TFoot") { }
    internal TableTFootElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
    public TableTRElement CreateTR() { var el = new TableTRElement(); AppendChild(el); return el; }
}
