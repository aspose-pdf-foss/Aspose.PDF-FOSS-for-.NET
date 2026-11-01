using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

#pragma warning disable S101 // public API: the published type name
/// <summary>A table-of-contents item structure element (tag <c>/TOCI</c>), a child of a TOC element.</summary>
public sealed class TOCIElement : StructureElement
#pragma warning restore S101
{
    internal TOCIElement() : base("TOCI") { }
    internal TOCIElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
