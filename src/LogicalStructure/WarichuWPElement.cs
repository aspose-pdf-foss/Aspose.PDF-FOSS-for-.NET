using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

public sealed class WarichuWPElement : WarichuChildElement
{
    internal WarichuWPElement() : base("WP") { }
    internal WarichuWPElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
