
namespace Aspose.Pdf.Text;

public partial class TextFragment
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class StyledCascadeState
{
    public Aspose.Pdf.Text.Font? newFont;
    public double newFs;
    public List<Aspose.Pdf.Text.TextSegment> headSegs = null!;
    public int headIdx;
    public Aspose.Pdf.Text.TextSegment headSeg = null!;
    public Aspose.Pdf.Text.Font? headFont;
    public double headFs;
    // Only a genuine restyle takes this path; a same-style replacement stays on
    // the byte-level run mover (which positions that case exactly).
    public bool restyled;
    // Char index of the match inside the head run, located by measuring prefixes
    // against the match X (the match may be any occurrence within the run).
    public double headX;
    public int occ;
    // Styled run stream: source text keeps its style; every oldText occurrence
    // becomes newText in the new style.
    public List<(string text, Aspose.Pdf.Text.Font? font, double fs, Aspose.Pdf.Color? fg)> runs = null!;
    public Color? newFg;
    // Wrap geometry: left = the match line's left margin; right mirrors the left
    // inset against the page width (never tighter than the paragraph's extent).
    // The line's leftmost RUN X (the fragment rect's LLX can degrade to 0).
    public double pLeft;
    public double maxRx;
    public double mediaW;
    public double rightMargin;
    // Tokenize into wrap units (split at spaces; units may span styles). The
    // style of the space BEFORE each unit is recorded for gap measurement.
    public List<List<(string t, int r)>> units = null!;
    public List<int> unitGap = null!;
    public System.Collections.Generic.List<(string t, int r)>? cur;
    public int pendingGap;
    // Greedy flow: first fresh baseline one new-size step below the match
    // baseline; every wrapped line steps by the new size.
    // The re-absorbed line Y is already the run's Tm baseline.
    public double matchTm;
    public double tmY;
    public double x;
    public List<(string text, int r, double x, double tmY)> pieces = null!;
    // Merge same-style neighbours on a line into single show pieces.
    public List<(string text, int r, double x, double tmY)> merged = null!;
    public Aspose.Pdf.Text.TextBuilder tb = null!;
    public Page page = null!;
    public System.Collections.Generic.List<(TextFragment f, double y, double lx, double rx)> paraLines = null!;
    public int matchLine = 0;
    public double myLLX = 0;
    public string oldText = null!;
    public string newText = null!;
}
}
