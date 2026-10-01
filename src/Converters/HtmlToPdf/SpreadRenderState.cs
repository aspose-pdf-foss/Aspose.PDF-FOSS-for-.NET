using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class SpreadRenderState
{
    public string body = null!;
    public Page page = null!;
    // 2. The padded content column, walked child by child with a
    // float-aware cursor. y runs TOP-DOWN in pt.
    public System.Text.RegularExpressions.Match contentM = null!;
    public string inner = null!;
    public double y;
    public double floatBottom;
    public double pendingRightTop;
}
}
