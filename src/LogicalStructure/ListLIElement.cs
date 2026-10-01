using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

/// <summary>A list-item structure element (tag <c>/LI</c>), a child of a list element.</summary>
public sealed class ListLIElement : StructureElement
{
    internal ListLIElement() : base("LI") { }
    internal ListLIElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
