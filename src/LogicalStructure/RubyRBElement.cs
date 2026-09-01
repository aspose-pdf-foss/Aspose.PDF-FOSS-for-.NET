using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

public sealed class RubyRBElement : RubyChildElement
{
    internal RubyRBElement() : base("RB") { }
    internal RubyRBElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
