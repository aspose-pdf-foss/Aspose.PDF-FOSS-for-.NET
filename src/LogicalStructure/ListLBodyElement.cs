using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

public sealed class ListLBodyElement : StructureElement
{
    internal ListLBodyElement() : base("LBody") { }
    internal ListLBodyElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
