using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

/// <summary>A section structure element (tag <c>/Sect</c>) that groups related content.</summary>
public sealed class SectElement : StructureElement
{
    internal SectElement() : base("Sect") { }
    internal SectElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
