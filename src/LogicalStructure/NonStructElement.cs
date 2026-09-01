using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

public sealed class NonStructElement : StructureElement
{
    internal NonStructElement() : base("NonStruct") { }
    internal NonStructElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
