using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public partial class Heading
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class TocBuildState
{
    public Content.ContentStreamBuilder builder = null!;
    public string totalText = null!;
    // The heading's OWN TextState wins when the caller set it (a content
    // heading with TextState.FontSize = 12 renders 12 pt even though its
    // segment was created at the 10 pt default); an explicitly-sized
    // segment comes next; the legacy segment fallback stays for untouched
    // headings so their metrics don't shift.
    public double fontSize;
    public double lineSpacing;
    // Word-wrap the text to fit page width, filling each line to the REAL
    // measured width ("…under the plan onaccount" /
    // "of each allowed" break exactly where the Helvetica advances run out).
    public double availWidth;
    public List<string> lines = null!;
    public System.Text.StringBuilder cur = null!;
    // First baseline drops by the cap-height ascent from the band top (the
    // same placement the flow's plain-fragment writer uses), so a heading
    // line chains bottoms with its neighbours by exactly its own font size
    // — stepping 758 → 748 → … → next heading at −12.
    public int capHeight;
    public double ascent;
    public double baseline;
    // The auto-sequence number is its OWN show at the margin and the
    // heading text starts at a fixed 20 pt tab stop after it
    // ("1  " at x=40, "Heading 0" at x=60 regardless of the number width).
    public double textX;
    public double height;
}
}
