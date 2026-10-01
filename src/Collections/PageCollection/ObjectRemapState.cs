using System.Collections;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

public sealed partial class PageCollection
{
/// <summary>Per-call working state of the object remap. One instance per invocation; never shared.</summary>
private sealed class ObjectRemapState
{
    public PdfReader sourceReader = null!;
    public Dictionary<int, PdfObject> remap = null!;
    public Stack<(PdfObject source, Action<PdfObject> setter)> stack = null!;
    public HashSet<object> visitedIdentity = null!;
    public int nextObjNum;
    public PdfObject? root;
}
}
