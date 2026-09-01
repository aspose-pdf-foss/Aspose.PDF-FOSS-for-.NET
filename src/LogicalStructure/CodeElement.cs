using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

public sealed class CodeElement : StructureElement
{
    internal CodeElement() : base("Code") { }
    internal CodeElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
