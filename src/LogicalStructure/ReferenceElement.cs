using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

public sealed class ReferenceElement : StructureElement
{
    internal ReferenceElement() : base("Reference") { }
    internal ReferenceElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
