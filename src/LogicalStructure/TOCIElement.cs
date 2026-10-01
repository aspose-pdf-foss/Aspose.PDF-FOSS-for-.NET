using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

/// <summary>A table-of-contents item structure element (tag <c>/TOCI</c>), a child of a TOC element.</summary>
public sealed class TOCIElement : StructureElement
{
    internal TOCIElement() : base("TOCI") { }
    internal TOCIElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
