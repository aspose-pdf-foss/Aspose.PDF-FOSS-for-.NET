using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

/// <summary>A paragraph structure element (tag <c>/P</c>).</summary>
public sealed class ParagraphElement : StructureElement
{
    internal ParagraphElement() : base("P") { }
    internal ParagraphElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
