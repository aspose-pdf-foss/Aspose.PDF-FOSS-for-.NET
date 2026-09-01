using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

public sealed class TOCIElement : StructureElement
{
    internal TOCIElement() : base("TOCI") { }
    internal TOCIElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
