using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

public sealed class BlockQuoteElement : StructureElement
{
    internal BlockQuoteElement() : base("BlockQuote") { }
    internal BlockQuoteElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
