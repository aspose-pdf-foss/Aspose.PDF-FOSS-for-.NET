using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Text;
using System.Text;

namespace Aspose.Pdf;

internal static partial class ContentStreamOperatorParser
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class OperatorScanState
{
    public List<string> result = null!;
    public string text = null!;
    public int pos;
    public int len;
    public List<string> operands = null!;
    public byte[] data = default!;
}
}
