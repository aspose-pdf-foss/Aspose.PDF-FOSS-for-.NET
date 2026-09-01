using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

public sealed class RubyRTElement : RubyChildElement
{
    internal RubyRTElement() : base("RT") { }
    internal RubyRTElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
