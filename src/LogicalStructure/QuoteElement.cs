using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

public sealed class QuoteElement : StructureElement
{
    internal QuoteElement() : base("Quote") { }
    internal QuoteElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
