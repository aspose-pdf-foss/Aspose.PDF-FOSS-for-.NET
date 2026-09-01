using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

/// <summary>
/// Common base for nodes in the logical-structure tree. Exposes the
/// child collection and a textual representation used when dumping the
/// tree for diagnostics.
/// </summary>
public abstract class Element
{
    /// <summary>Direct children of this element.</summary>
    public abstract ElementList ChildElements { get; }
}
