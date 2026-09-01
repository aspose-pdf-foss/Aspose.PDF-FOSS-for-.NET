using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

public sealed class DivElement : StructureElement
{
    internal DivElement() : base("Div") { }
    internal DivElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
