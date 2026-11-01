using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

#pragma warning disable S1694 // public API: the published abstract base of the structure-tree nodes
/// <summary>
/// Common base for nodes in the logical-structure tree. Exposes the
/// child collection and a textual representation used when dumping the
/// tree for diagnostics.
/// </summary>
public abstract class Element
#pragma warning restore S1694
{
    /// <summary>Direct children of this element.</summary>
    public abstract ElementList ChildElements { get; }
}
