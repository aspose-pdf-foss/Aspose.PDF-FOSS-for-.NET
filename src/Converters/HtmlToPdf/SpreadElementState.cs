using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class SpreadElementState
{
    public string tag = null!;
    public string attrs = null!;
    public string elBody = null!;
}
}
