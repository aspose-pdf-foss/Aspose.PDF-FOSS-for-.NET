using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

public sealed class ListLIElement : StructureElement
{
    internal ListLIElement() : base("LI") { }
    internal ListLIElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
