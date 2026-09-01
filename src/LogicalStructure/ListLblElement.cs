using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

public sealed class ListLblElement : StructureElement
{
    internal ListLblElement() : base("Lbl") { }
    internal ListLblElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
