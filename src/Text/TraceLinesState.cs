using System.Text;

namespace Aspose.Pdf.Text;

internal static partial class TextPaginator
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class TraceLinesState
{
    public System.Func<string, double> measurer = null!;
    public double spaceWidth;
    public string normalised = null!;
    public string[] paragraphs = null!;
    public int lastParagraph;
    // (content, width-without-trailing-space, lastLineOfParagraph, paragraphIndex)
    public List<(string content, double width, bool lastInPara, int para, bool keptSpace)> raw = null!;
    public int globalLineCount;
    public List<(string, double, char)> result = null!;
}
}
