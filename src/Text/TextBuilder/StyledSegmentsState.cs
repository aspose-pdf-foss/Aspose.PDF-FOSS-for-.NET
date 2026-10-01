using Aspose.Pdf.Content;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Text;

public sealed partial class TextBuilder
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class StyledSegmentsState
{
    public double lineH;
    public double curX;
    public double curY;
    public bool lineStarted;
    public TextFragment fragment = default!;
    public ContentStreamBuilder builder = default!;
    public string fragResName = default!;
    public double fontSize = 0;
    public double fragDescentComp = 0;
    public double x = 0;
    public double y = 0;
}
}
