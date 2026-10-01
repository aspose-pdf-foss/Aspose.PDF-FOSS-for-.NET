using System.Text;

namespace Aspose.Pdf.Text;

public sealed partial class ParagraphAbsorber
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class GroupLinesState
{
    public List<Aspose.Pdf.Text.ParagraphAbsorber.TextLine> lines = null!;
    public List<Aspose.Pdf.Text.TextFragment> sorted = null!;
    // Split lines with large horizontal gaps into separate lines.
    // This handles multi-column layouts where fragments at the same Y are in different columns.
    public List<Aspose.Pdf.Text.ParagraphAbsorber.TextLine> splitLines = null!;
    public List<TextFragment> fragments = default!;
}
}
