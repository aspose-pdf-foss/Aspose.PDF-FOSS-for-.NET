using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

public sealed class RubyElement : StructureElement
{
    internal RubyElement() : base("Ruby") { }
    internal RubyElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
