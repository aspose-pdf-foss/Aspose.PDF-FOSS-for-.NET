using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

public sealed class ListElement : StructureElement
{
    internal ListElement() : base("L") { }
    internal ListElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
