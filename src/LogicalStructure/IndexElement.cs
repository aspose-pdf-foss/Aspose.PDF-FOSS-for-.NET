using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

public sealed class IndexElement : StructureElement
{
    internal IndexElement() : base("Index") { }
    internal IndexElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
