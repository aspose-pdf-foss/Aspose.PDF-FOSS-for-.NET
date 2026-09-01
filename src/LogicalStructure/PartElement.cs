using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

public sealed class PartElement : StructureElement
{
    internal PartElement() : base("Part") { }
    internal PartElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
