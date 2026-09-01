using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

public sealed class WarichuWTElement : WarichuChildElement
{
    internal WarichuWTElement() : base("WT") { }
    internal WarichuWTElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
