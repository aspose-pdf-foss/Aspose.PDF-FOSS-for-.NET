using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

public sealed class WarichuElement : StructureElement
{
    internal WarichuElement() : base("Warichu") { }
    internal WarichuElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
