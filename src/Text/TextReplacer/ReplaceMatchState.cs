using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextReplacer
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class ReplaceMatchState
{
    // Trim synthetic gap-space chars off the match edges and locate the ops.
    public int msIdx;
    public int meIdx;
    public int firstOp;
    public int lastOp;
    public bool inTarget;
}
}
