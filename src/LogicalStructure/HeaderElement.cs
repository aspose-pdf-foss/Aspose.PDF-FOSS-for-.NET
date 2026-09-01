using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

public sealed class HeaderElement : StructureElement
{
    internal HeaderElement() : base("H") { }
    internal HeaderElement(int level) : base(level <= 0 ? "H" : $"H{level}") { }
    internal HeaderElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
