using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

internal sealed class GenericStructureElement : StructureElement
{
    internal GenericStructureElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
