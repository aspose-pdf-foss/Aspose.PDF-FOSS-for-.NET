using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

public sealed class SectElement : StructureElement
{
    internal SectElement() : base("Sect") { }
    internal SectElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
