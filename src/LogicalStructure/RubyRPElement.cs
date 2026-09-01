using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

public sealed class RubyRPElement : RubyChildElement
{
    internal RubyRPElement() : base("RP") { }
    internal RubyRPElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
