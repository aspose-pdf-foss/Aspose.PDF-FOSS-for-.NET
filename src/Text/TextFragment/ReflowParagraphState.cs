
namespace Aspose.Pdf.Text;

public partial class TextFragment
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class ReflowParagraphState
{
    public double fs;
    // Precompute geometry so Position (non-null after this filter) isn't re-dereferenced.
    public List<(Aspose.Pdf.Text.TextFragment f, double y, double lx, double rx)> lines0 = null!;
    // Full left span per line (min of the rect edge and the leftmost visible
    // segment) — a back-jump line's rect can START RIGHT of its own earlier-drawn
    // segments, and the match-containment test below must still accept a match
    // inside those segments.
    public Dictionary<Aspose.Pdf.Text.TextFragment, double> spanLx = null!;
    // The page's lines. The `.+` regex sweep is the primary reading, but it is a REGEX
    // over the page's assembled text: a producer that draws each line in its own
    // BT/ET with no separator between them leaves nothing for `.+` to stop at, so the
    // whole page comes back as ONE fragment and the paragraph can never be located.
    // The plain absorber reads the same page as one fragment per line, so fall back to
    // it whenever the regex collapses the page.
    public List<Aspose.Pdf.Text.TextFragmentAbsorber> absorbers = null!;
    public Aspose.Pdf.Text.TextFragmentAbsorber abs = null!;
    // The same lines WITHOUT the blank-only filter, used only to read the column's
    // geometry. A blank line is a baseline of the block like any other; dropping it
    // turns one line pitch into two and the block breaks there, so a paragraph that
    // merely has an empty line in it reads as two short blocks and the reflow wraps
    // against one line's extent instead of the column's.
    public List<(Aspose.Pdf.Text.TextFragment f, double y, double lx, double rx)> bandSource = null!;
    // Find the re-absorbed line that CONTAINS this fragment. The fragment's own
    // LLX is the X of the matched token, which may sit mid-line (e.g. "{{Name}}"
    // embedded in flowing text), so match by Y proximity plus X-within-[lx,rx]
    // rather than assuming the fragment starts at the line's left margin.
    public double myLLX;
    public int myIdx;
    public double leftX;
    // Grow the paragraph up/down over contiguous same-left-margin lines (one line pitch apart).
    // A line is only merged if it shares the left margin AND is close in font SIZE: a bigger
    // heading (e.g. a 24pt bold title above 12pt body, same left margin) is a SEPARATE
    // paragraph, so merging it would collapse it to body size on reflow. Same-size paragraphs
    // (the common case) are unaffected.
    public double xtol;
    // IgnoreParagraphs = continuous-flow reflow: the replacement flows through the WHOLE text
    // block, ignoring paragraph boundaries. Grow across all contiguous same-size lines
    // regardless of left-margin changes so the entire block reflows as one unit and cascades
    // down naturally (no separate push-down of trailing paragraphs needed). Default mode keeps
    // the strict same-left-margin grow.
    public bool ignorePara;
    public double paraFs;
    // The page's lines interleave by Y once it has more than one COLUMN, so the entry
    // directly below the match in reading order can belong to the column beside it. Such
    // a line is neither part of this paragraph nor a break in it
    // next line simply sits further down the list
    // horizontal span MEETS the match line's and steps over the rest.
    public List<(Aspose.Pdf.Text.TextFragment f, double y, double lx, double rx)> colLines = null!;
    public int myCol;
    public int lo;
    public int hi;
    // Hanging-indent lists (numbered/bulleted items): the item's FIRST line sits at a
    // dedented margin and its continuation lines share a deeper indent. The whole
    // item reflows as one paragraph, so grouping accepts one indent step
    // down from the match line (establishing the continuation indent) and, growing up
    // from a continuation line, the single dedented head line (then stops). An indent
    // step going UP is the tail of the PREVIOUS item — never merged. Any step must
    // keep the paragraph's OWN line pitch (≤1.35×): a dedented line a line-and-a-half
    // away (a salutation above an indented body, a heading) is a separate paragraph.
    public double maxHang;
    public double stepPitchTol;
    // The paragraph's OWN line pitch, measured from the contiguous run of lines
    // around the match. A gap materially wider than it is a PARAGRAPH BREAK (the
    // blank line between two blocks) — merging across it would let the reflow pull
    // the next paragraph's opening words up onto this paragraph's last line, which
    // must never happen. Continuous-flow mode deliberately ignores breaks.
    public double paraPitch;
    public double maxMergeGap;
    public double downLx;
    public bool hangStepped;
    public double upLx;
    public List<(Aspose.Pdf.Text.TextFragment f, double y, double lx, double rx)> paraLines = null!;
    // Replace PER LINE (mirroring the per-fragment absorber), then reunite — an occurrence
    // split across a line break isn't a single-line match and is left intact (a
    // per-fragment replace also misses line-straddling occurrences).
    public List<string> origParts = null!;
    public List<string> newParts = null!;
    public string origText = null!;
    public string replaced = null!;
    // Whole-paragraph replacement: when the matched fragment IS the entire paragraph
    // (oldText spans every line, e.g. a paragraph->paragraph+paragraph replace), no
    // single-line Replace fires, so replaced==origText. Detect that by comparing the
    // paragraph body to oldText ignoring all whitespace (robust to reconstruction
    // spacing differences) and re-wrap the replacement directly. Otherwise there is no
    // within-line occurrence in this paragraph and sibling fragments must no-op.
    public bool wholePara;
    // Mid-token replacement (default flow): cascade from the MATCH
    // position — the paragraph lines above the match and the match line's prefix
    // stay untouched; text from the match onward re-packs onto the EXISTING baselines.
    // When the cascade can't handle the page's structure (CID font, cross-run match,
    // glyphs missing from the subset…) FALL THROUGH to the whole-paragraph re-wrap
    // below — bailing out entirely would leave the plain in-place replace to grow the
    // line past the page edge.
    // A LONE line has no following baselines to re-pack onto, so a replacement
    // that overflows it cannot cascade — it needs the free-space re-wrap below,
    // which takes its column from the page rather than from the line.
    // ...and only when the match IS that line: a token embedded in a longer line
    // still has its line-mates to re-pack against, so it cascades as before.
    public bool lonelyOverflow;
    public double paraLeftX;
    public double paraRightX;
    public double pageCol;
    // The same paragraph, grown over MERGED lines rather than over absorbed fragments —
    // this is what the mover wraps against, and the only view in which a line drawn as
    // several operators has one left edge and one right edge.
    public List<(double y, double lx, double rx)> bands = null!;
    public List<(double y, double lx, double rx)> bandPara = null!;
    public double bandColumnRight;
    public double rightX;
    // Continuous-flow (IgnoreParagraphs): page-bound the wrap width. A previous longer
    // replacement can leave an over-wide unbreakable-token line, and re-absorbing that inflated
    // max-URX would compound the overflow. Cap the right border at the page's usable right edge
    // (mirror the left inset) so the flow wraps within the page instead of running off it.
    public Rectangle pageRect = null!;
    // A one-line "paragraph" carries no column width of its own: a lone token
    // sitting in free space would re-wrap to its own token width. Such a flow
    // takes the page as its column — the left inset mirrored on the right —
    // and stops short of the nearest text to its right on the same line, whose
    // own size sets the gap. (The MOVER reads the page's own text column instead;
    // this path re-emits into free space, where there is no column to read.)
    // A LONE line wraps at ITS OWN right edge when it HAS one. Probed on a synthetic
    // 400 pt sheet whose single line of text ends at 161: every replacement length wraps
    // the tail at 161 - a 5-character one pushes the last word over, a 20-character one
    // puts the replacement itself on a fresh line - and none of them reaches the sheet.
    // The page column is for the case this path was written for: a match that IS the
    // whole line, sitting in free space with no text of its own to the right and so no
    // edge to read. Wrapping THAT to its own extent would re-wrap a token to its own
    // width. So the page stands in only when the line carries nothing past the match.
    public bool lineHasTail;
    // RightAdjustment extends the wrap border to the right so a longer replacement
    // re-flows into more lines against the widened margin. It applies only to the
    // mid-line-token reflow; a whole-paragraph replace re-wraps to the paragraph's own
    // width and ignores RightAdjustment.
    public double rightAdjust;
    public double width;
    // Re-flow in the paragraph's dominant font (the fragment carrying the most text),
    // so a lone bold word doesn't bold the whole paragraph and vice-versa.
    public Aspose.Pdf.Text.TextFragment domLine = null!;
    public Aspose.Pdf.Text.Font domFont = null!;
    public string? domName;
    // A source font that cannot encode the replacement is substituted, and the
    // whole re-flow — wrap, widths and the written lines — runs in the stand-in.
    public string? reflowFace;
    public System.Func<string, double, double>? reflowMeasure;
    // Per source line: the seat of its LAST run (left-relative), that run's own width,
    // and the size it was drawn at — the three numbers the line-budget law needs.
    public List<(double seat, double runW, double srcFs)> lineCaps = null!;
    public System.Collections.Generic.List<string> wrapped = null!;
    public List<double> baselines = null!;
    public double pitch;
    public Aspose.Pdf.Text.TextBuilder tb = null!;
    public List<(string text, double baseline, double width)> laidOut = null!;
    public double maxLineW;
    public float domSize;
}
}
