using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextReplacer
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class ReflowState
{
    public IO.PdfReader reader = null!;
    public List<byte[]> contentStreams = null!;
    public byte[] streamBytes = null!;
    public Dictionary<string, Aspose.Pdf.Core.PdfDictionary> fonts = null!;
    public List<Aspose.Pdf.Text.TextReplacer.CrossTextOp> textOps = null!;
    public Dictionary<Aspose.Pdf.Core.PdfDictionary, Aspose.Pdf.Text.FontMetrics?> metricsCache = null!;
    // Assign ops to the paragraph lines. `lines[].y` is each line's TRUE baseline, so an
    // operator belonging to the line sits ON it — the tolerance only has to cover
    // superscript/subscript shifts and rounding, never a descent. Keeping it tight is
    // what stops a neighbouring line's operator, a few points away and horizontally
    // inside this line's span, from being swept into the reflow and repositioned.
    public double yTol;
    public List<(Aspose.Pdf.Text.TextReplacer.CrossTextOp op, int li, double px)> affected = null!;
    // Every op mapped to a paragraph line (including pre-match line-0 runs that
    // never move) — the underline-regeneration pass below re-emits a bar per
    // covered op whether or not the op itself was repositioned.
    public List<(Aspose.Pdf.Text.TextReplacer.CrossTextOp op, int li, double px)> mapped = null!;
    public int headIdx;
    public List<(Aspose.Pdf.Text.TextReplacer.CrossTextOp op, int li, double px)> consumed = null!;
    public string? seqHeadText;
    // Where the match starts INSIDE the head run when the occurrence crossed a line
    // break; -1 otherwise. The head then keeps its own bytes up to that point and only
    // the replacement is re-encoded — re-encoding the whole run would re-code text that
    // never changed, and a subset font answers the wrong widths for it.
    public int crossHeadMatchAt;
    public Aspose.Pdf.Text.TextReplacer.CrossTextOp head = null!;
    public string newHeadText = null!;
    // Head font choice: keep the original face iff its OWN
    // width data covers every replacement character (a Word-emitted system-font
    // reference zeroes /Widths for glyphs the doc never drew — digits in a
    // prose-only bold face). Otherwise substitute a fresh system face of the
    // same family+style, embedded as a subsetted Type0/Identity-H, measured by
    // its raw TTF advances (the emitted /W integers are the same
    // fractional hmtx values).
    public byte[]? newHeadBytes;
    public FontData? switchedFace;
    public string switchedFamily = null!;
    public double headAdvPad;
    // Baseline page-Y per line, from each line's first affected op; missing lines
    // (fully emptied by the shift) interpolate from the previous baseline.
    public double?[] lineBaseY = null!;
    // Lines the wrap CREATES (beyond the paragraph's existing baselines) advance by
    // TextReplaceOptions.AdjustmentNewLineSpacing × the match run's page font size
    // when the caller set it; otherwise by the MEAN of the paragraph's pitches below
    // the edited line (a single-line paragraph keeps the caller's
    // 1.2-em fallback). Existing baselines never move.
    public double newPitch;
    public List<(Aspose.Pdf.Text.TextReplacer.CrossTextOp op, double x, int line, byte[] bytes, string? sw, int off)> pieces = null!;
    public double cursor;
    public double prevOrigEnd;
    // ── Reflow notifications ────────────────────────────────────────────────
    // One line is logged per piece of text the adjustment MOVED, and the
    // test pins two of them word for word. Across both scenarios, the anchor
    // is where the triggering condition was observed, and the two directions
    // read it from different layouts:
    //   • pushed DOWN — the moved text's ORIGINAL page X, i.e. where it sat in the
    //     SOURCE document before the replacement shifted the line, with that line's
    //     baseline. Confirmed exactly: the run `and supersede any prior ` is
    //     authored at x=396.67, `and supersede ` (82.67 wide) stays put and the
    //     move is reported at 396.67 + 82.67 = 479.3 — not the shifted 520.1
    //     the same split sits at after the replacement widened the line.
    //   • pulled UP — where the piece LANDS on its destination line, with that
    //     line's baseline ("it has free space").
    // The unit is the piece, not the word: a lone space, a bracket, and a run
    // starting mid-word ('utually beneficial.') are reported as moves of
    // their own, because this producer draws every run in its own BT/ET.
    public bool logNotes;
    public List<string>? notes;
    // Set when text is pushed to the next line: the ORIGINAL x of the split point
    // and the line it overflowed. Consumed by the piece that lands on the new line.
    public (double x, int line)? pendingPush;
    public Dictionary<Aspose.Pdf.Text.TextReplacer.CrossTextOp, List<(double x, int line, byte[] bytes, string? sw, int off)>> byOp = null!;
    public List<Aspose.Pdf.Text.TextReplacer.CrossTextOp> opOrder = null!;
    // Byte-level edits OUTSIDE the rewritten op spans: consumed multi-op match
    // show-ops are deleted (their BT..ET shells
    // survive), and underline bars are deleted + regenerated (below).
    public List<(int s, int e)> deleteSpans = null!;
    public List<(int pos, byte[] bytes)> inserts = null!;
    public System.IO.MemoryStream result = null!;
    public int lastWritePos;
    public int delIdx;
    public int insIdx;
    public int maxLi;
    // The render inputs, captured from the method parameters.
    public Page page = null!;
    public string search = null!;
    public string replacement = null!;
    public double matchX;
    public IReadOnlyList<(double y, double lx, double rx)> lines = null!;
    public double leftX;
    public double rightMargin;
    public double pitch;
    public double newLineSpacingFactor;
    public int curLi;
    public int prevOrigLine;
    public string prevOrigText = null!;
    public bool isHead;
    public double wOrig;
    public double gap;
    public double startX;
    // The head run is emitted as up to THREE runs: the source bytes that survive
    // BEFORE the match, the replacement, and the source bytes that survive AFTER it.
    // The replacement is written as its own run, and that boundary is what
    // lets a later restyle name it: a replacement glued into one show with the text
    // around it can only be resized by resizing that text too. Needs a 1:1 byte-char
    // run (guaranteed non-CID here) and a single-operator match.
    public List<(byte[] bytes, int off)> headParts = null!;
    public bool partFirst;
    // Where this run starts in the SHIFTED layout, so a split point can be
    // mapped back to the source document's coordinates for the push-down note.
    public double runStartX;
    public int guard;
    // The placement loop's own cursor: the head-part bytes still to place, their
    // byte offset inside the run (so each piece measures with the kerns that
    // belong to it), and whether those bytes are still the operation's FULL
    // original run - true only until the first split, since a split piece can no
    // longer be measured with the op's own kerning.
    public byte[] rest = null!;
    public int restOff;
    public bool wholeOriginal;
    // Only the REPLACEMENT changes face. The bytes of the run that survive on
    // either side of the match are source text the caller never touched, so they
    // keep their original font resource — the expected output splits the run exactly
    // this way (a replacement lands in a fresh "CalibriBold" while the
    // trailing "'s Mental Golf DISC Style" stays in the document's own
    // Calibri-Bold, and its rectangle keeps that descriptor's deeper descent).
    // Re-dressing the survivors as well moved them by the descent difference.
    // Needs a 1:1 byte↔char run (guaranteed non-CID here) and a single-op match.
    public int matchAt;
    public byte[] headSuffix = null!;
    // Flow the replacement TEXT, measured with the substitute face's raw TTF
    // advances; split greedily at spaces.
    public string swRest = null!;
    // A MONOSPACED source keeps its cell grid: where the substituted word is
    // WIDER than the cells it replaces, the writer squeezes it back with a
    // Tz and re-emits the separator that follows it at the head of the next
    // line, so the break carries a space on BOTH sides. Measured on two source
    // documents: one face is DejaVuSansMono ("leap" 24.000 wide against
    // Times "LEAP" 24.90, so it squeezes) and the spill reads " INTO
    // ELECTRONIC", seating the E one space further in - 130.211 from a 101.420
    // left, where a consumed separator gives 127.696. doc2's OpenSans-Light is
    // proportional ("leap" 18.9937) and never squeezes, and its spill reads
    // "INTO ELECTRONIC" with no such space. A replacement whose word is
    // NARROWER than the original is not stretched, and does not double either.
    public bool swDoubleSeparator;
    public int guardSw;
    public System.Text.StringBuilder cat = null!;
    public List<int> starts = null!;
    public int l0Count;
    public int mi;
    public int mLen;
    public int opCount;
    public bool crossedLines;
    public int firstOp;
    public int lastOp;
    public int tailStart;
    public string headPrefix = null!;
    public string lastTail = null!;
}
}
