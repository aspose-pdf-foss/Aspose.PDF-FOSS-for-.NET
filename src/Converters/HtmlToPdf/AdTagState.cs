using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class AdTagState
{
    public System.Text.RegularExpressions.Match t = null!;
    // trailing bare text
    public string lead = null!;
    public int abs;
    public string tag = null!;
    public string cls = null!;
    public bool hidden;
}
}
