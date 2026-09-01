using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextAbsorber
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class SortLinesState
{
    public int yCount;
    // Extract only the page's text
    public string pageText = null!;
    public string[] lines = null!;
    // An RTL line assembled from several tracked runs needs the geometric
    // row rebuild below even when the page has nothing to sort: a producer
    // that lays logical Hebrew words out left-to-right (one show per word,
    // ascending X) streams them in logical order, and the extractor
    // reads the row as drawn — visual order, then bidi — so a
    // single-line page must reach the rebuild too.
    public bool rtlMultiSpan;
    // Whitespace-only lines that are ONE drawn space glyph (a word space
    // shown as its own text object: "French" + " " + ":", a trailing
    // "experience" + " "): same-row segments. Any wider blank line — a
    // justified line's padding run, a pad row of placeholder spaces — is
    // vertical spacing, exactly as before.
    public bool[] singleSpaceGlyphLine = null!;
    // Per line: the page X where its last tracked run ends (text lines) or
    // where its lone space glyph sits (blank lines) — adjacency is geometric,
    // since glyphs narrower than the grid cell run a line's text past its
    // column count.
    public double[] lineEdgeX = null!;
    // A lone space glyph seats "right after" a text line when it starts
    // within a cell and a half of where that line's last run ends.
    public double adjacencyTol;
    // Build Y/X/font-size positions for this page's lines
    public List<double> pageYs = null!;
    public List<double> pageXs = null!;
    public List<double> pageFs = null!;
    public List<bool> pageRot = null!;
    public List<double> pageDesc = null!;
    // A line whose Y could not be tracked belongs WITH its neighbours, not at
    // the page end (where NaN would sort it). Forward-fill from the previous
    // line; leading unknowns take the first known Y.
    public double firstKnown;
    // Check if lines are already in visual order (Y descending = top to bottom).
    public bool needsSort;
    public bool rawModePre;
    // Pure-mode blank rows (the LineSplitter thresholds): a new
    // segment whose bottom clears the current line's top by more than one
    // line-height opens one empty line, by more than three line-heights a
    // second — i.e. baseline gap > 2·F → 1 blank, > 4·F → 2 (cap). F = the
    // PREVIOUS line's own font size (the "current line" whose top the new
    // segment clears); the page-dominant size only backstops untracked
    // lines. An 8pt address block with 15.1pt leading stays blank-free
    // (2·8 > 15.1) even when the page's dominant text is 7.5pt.
    public double blankFs;
    // Raw mode keeps the source stream order verbatim: no re-sort, no same-row
    // column merging (a wrapped table row would interleave its cells' lines) and
    // no blank-row synthesis. Measured on a multi-article newspaper page
    // whose stream jumps up-page repeatedly: the raw output is the stream
    // order even across >200pt up-jumps.
    public bool rawMode;
    // Even if sort isn't needed, check if same-Y lines need merging
    public bool hasSameYLines;
    // Map each line back to its tracked start X (reading-axis page coordinate) so
    // the same-row merge below can pad to the right part's grid column instead of
    // a fixed separator. Lines without a tracked run keep NaN.
    public double[] lineStartXs = null!;
    // Create (y, index, line) tuples and sort by Y descending (top of page first).
    // Lines were split on '\n' but were separated upstream by "\r\n", so each carries
    // a trailing '\r'; strip it so re-joining doesn't produce a doubled "\r\r\n" between
    // lines or a stray '\r' before a same-row column separator ("…large\r      companies").
    public List<(double y, int idx, string line)> indexed = null!;
    // Replace the page portion of _text with sorted text, merging visual rows.
    // Row formation (boundaries hold to ±0.004pt):
    // each line is a 1-em box sitting on its TRUE descent line (bottom =
    // baseline − descent·fs with the line font's own descent magnitude,
    // top = bottom + fs); two lines are same-row-compatible iff their
    // boxes overlap by at least half the smaller font (inclusive). Lines
    // are walked BOTTOM-UP (ascending baseline) and a line joins the
    // forming row iff it is compatible with EVERY member (complete
    // linkage); otherwise it starts a new row. Rows then emit top-down,
    // members X-ordered. The bottom-up complete-linkage walk is what no
    // pairwise (Δ, fsA, fsB) tolerance could reproduce: an intervening
    // lower line captures its neighbour and flips a pair that would merge
    // in isolation. The per-font descent anchor is what releases a small
    // label riding above a deep-descent large-font row (the fixed 0.2
    // anchor wrongly swallowed it) — and it separates an oversized
    // underscore rule from the header above without any content test.
    public int[] groupOf = null!;
    public int gStart2;
    public bool firstGroup;
    public double prevGroupY;
    // The sort inputs, captured from the method parameters.
    public int textStartOffset;
    public int yStartIndex;
}
}
