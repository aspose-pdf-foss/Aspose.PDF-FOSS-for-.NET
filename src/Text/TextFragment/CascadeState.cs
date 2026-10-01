
namespace Aspose.Pdf.Text;

public partial class TextFragment
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class CascadeState
{
    // Effective (page-space) font scale: producers that draw each run in its own
    // q/cm/BT..ET/Q block size text via Tm with the CTM shrinking it back; measuring
    // or re-emitting at the raw Tm size would be wrong by the CTM factor.
    public double ctmScale;
    // Collect the segments to re-flow, each one source run: on the match line those
    // at/after the match X, on the following paragraph lines all of them.
    public List<(Aspose.Pdf.Text.TextSegment seg, double x, double y)> moved = null!;
    // The first moved segment must carry the matched token (a match hidden mid-run
    // with a prefix inside the same run is left to the in-place replace path).
    public string head = null!;
    public int occ;
    // Combined text from the match onward. Same-line neighbours concatenate verbatim
    // (their spacing rides in the runs); a line break is a word boundary. NBSPs fold
    // to plain spaces — producers that pad word gaps with U+00A0 would otherwise glue
    // the NBSP onto the next word through the space-split below, and the re-emitted
    // line would never phrase-match a plain-space search.
    public System.Text.StringBuilder sb = null!;
    // Measure/emit in the paragraph's dominant face at the effective size.
    public Aspose.Pdf.Text.TextSegment domSeg = null!;
    public Aspose.Pdf.Text.Font? font;
    public double rawFs;
    public double effFs;
    // Prefer the SYSTEM face of the same family for measuring and re-emission. The
    // source font is typically an embedded SUBSET whose width table is keyed by its
    // custom byte codes, so measuring Unicode text against it mis-indexes the widths;
    // the system face carries the true advances (the reflow is measured
    // with these), and embedding it makes the absorber read the same metrics back, so
    // the re-emitted words land at consistent positions.
    public string faceName = null!;
    public int subsetPlus;
    public int styleComma;
    public double leftX;
    public double rightX;
    // Tokenise KEEPING each gap's space run: a double space is preserved
    // where the replacement's own trailing space meets the source's
    // (a "text.  1" tail), and a break seam's space stays on the closing
    // line when it still fits that line's extent.
    public string srcJoined = null!;
    public List<(string w, int sp)> toks = null!;
    public string[] words = null!;
    // A repository CJK face can come back with a FLAT 1-em-per-unit width table
    // (space = a full em, surrogate pairs = two) — degenerate for packing Latin
    // replacement text. Detect it by the space width and fall through to the raw
    // font program's own advances (Latin ~0.5 em, a surrogate pair one '?' pair).
    // A repository face measures through its RAW program metrics: the dict-based
    // Metrics of a system face routes standard families to the Helvetica AFM
    // (5-10% off Arial's true advances - an 82-char token measured 531
    // instead of its drawn 600.7 and never wrapped), and a repository CJK face
    // comes back with a FLAT 1-em-per-unit table.
    public bool degenerateMetrics;
    // A CJK-family face writes a LATIN replacement in the face's OWN half-width
    // Latin cells: the re-emitted subset carries a flat 500/1000 /W
    // for every ASCII glyph — space and digits included — so a 47-char
    // sentence spans exactly 235 pt at fs 10 and its tail run seats at 325
    // (measured from the expected content stream and font /W).
    // The CJK glyphs keep the face's full-width em advances.
    public string packFamily = null!;
    public int subsetPlusP;
    public bool cjkBase;
    public List<string> packed = null!;
    public System.Text.StringBuilder cur = null!;
    public double curX;
    public double curW;
    public double spaceW;
    public int pendSp;
    // Existing baselines from the match line down; extend below by the pitch if the
    // packed text needs more lines than the paragraph had.
    public List<double> baselines = null!;
    public double pitch;
    // How far down the appended runs reached; the caller reports it as its out-value.
    public double appendedBottom;
    // Re-emit the packed lines.
    public Aspose.Pdf.Text.TextBuilder tb = null!;
    public Page page = null!;
    public System.Collections.Generic.List<(TextFragment f, double y, double lx, double rx)> paraLines = null!;
    public int matchLine = 0;
    public double myLLX = 0;
    public string oldText = null!;
    public string newText = null!;
    public double pageRightMargin = 0;
    public System.Collections.Generic.List<(double y, double lx, double rx)> bandPara = null!;
}
}
