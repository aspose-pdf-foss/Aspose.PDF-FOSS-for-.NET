using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using System.Globalization;

namespace Aspose.Pdf.Text;

public sealed partial class TextParagraph
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class VisualLinesState
{
    public double maxWidth;
    public TextFormattingOptions.WordWrapMode wrapMode;
    public bool wrap;
    public List<List<(string, Aspose.Pdf.Text.TextState)>> result = null!;
    public TextFragment? current;
}
}
