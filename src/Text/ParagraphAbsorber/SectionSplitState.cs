using System.Text;

namespace Aspose.Pdf.Text;

public sealed partial class ParagraphAbsorber
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class SectionSplitState
{
    // Section model: every fragment
    // contributes a line box [baseline, baseline + 1.1·fontSize] rasterized on a
    // 1-pt row grid anchored at integer user-space Y; sections split at any run
    // of at least round(pageH·override) + 2 consecutive EMPTY rows (default
    // override 0.005 — "unset" is not zero). Columns split analogously on 1-pt
    // X columns with a font-size floor: max(round(pageW·hOverride) + 2,
    // round(0.8·(F + 2))).
    public double vOv;
    public double hOv;
    public int vRun;
    public double avgFontSize;
    public double pageBodyRight;
    public List<Aspose.Pdf.Text.MarkupSection> sections = null!;
    public List<TextFragment> fragments = default!;
    public double pageW = 0;
    public double pageH = 0;
}
}
