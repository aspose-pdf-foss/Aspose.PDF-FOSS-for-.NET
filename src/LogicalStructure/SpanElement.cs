using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

/// <summary>A generic inline structure element (tag <c>/Span</c>).</summary>
public sealed class SpanElement : StructureElement
{
    internal SpanElement() : base("Span") { }
    internal SpanElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
