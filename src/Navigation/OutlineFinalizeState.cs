using System;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

public sealed partial class OutlineCollection
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class OutlineFinalizeState
{
    // Allocate object numbers for all items (flat list)
    public List<(Aspose.Pdf.OutlineItem item, int objNum)> flatItems = null!;
    public int baseObjNum;
    public int outlinesObjNum;
    // Build item → objNum map
    public Dictionary<Aspose.Pdf.OutlineItem, int> objMap = null!;
    // Build /Outlines root dict — /Count is the total number of visible
    // items: every top-level item plus open items' visible descendants.
    public int rootCount;
    public Aspose.Pdf.Core.PdfDictionary outlinesDict = null!;
    public Document doc = default!;
}
}
