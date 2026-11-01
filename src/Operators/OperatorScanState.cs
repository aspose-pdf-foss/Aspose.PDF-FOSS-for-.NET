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
internal sealed class OperatorScanState
{
    // The lines, one per operator - or, scanning for a sink, the sink and no lines.
    public List<string>? result;
    public Action<ScannedOperator>? sink;
    public string text = null!;
    public int pos;
    public int len;
    // The operands since the last operator, as ranges of the text; one line per operator is built in `line`.
    public List<(int start, int end)> operands = null!;
    public StringBuilder line = null!;
    public byte[] data = default!;
}
}
