
namespace Aspose.Pdf.Text;

public partial class TextFragment
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class BlockTranslateState
{
    // The caller may have handed the fragment's own Rectangle instance back
    // as the target (mutated in place) — recover the PRE-shift geometry from
    // the absorbed-box snapshot (exact), falling back to the segment union.
    public double obLLX;
    public double obLLY;
    public double obURX;
    public double obURY;
    public bool dbg;
    public double dx;
    public double dy;
    // Region gate for positioning ops: the block's own coordinates, padded
    // for the baseline-vs-rect-bottom offset. Anything else on the page
    // stays untouched.
    public double padX;
    public double padY;
    public int shifted;
    public bool afterBt;
    public List<Aspose.Pdf.Operators.SetTextMatrix> toReplace = null!;
    public Page page = default!;
    public string oldText = default!;
    public string newText = default!;
    public Rectangle rect = default!;
    public double baseFs = 0;
}
}
