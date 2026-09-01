using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

/// <summary>An ordered list of structure elements, as returned by
/// <see cref="Element.ChildElements"/>.</summary>
public sealed class ElementList : List<StructureElement>
{
    internal ElementList() { }
    internal ElementList(IEnumerable<StructureElement> items) : base(items) { }
}
