using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

public sealed class PrivateElement : StructureElement
{
    internal PrivateElement() : base("Private") { }
    internal PrivateElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
