using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

/// <summary>An inline quotation structure element (tag <c>/Quote</c>).</summary>
public sealed class QuoteElement : StructureElement
{
    internal QuoteElement() : base("Quote") { }
    internal QuoteElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
