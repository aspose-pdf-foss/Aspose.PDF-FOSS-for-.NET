using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;

namespace Aspose.Pdf;

public sealed partial class Document
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class TocEntryState
{
    public LevelFormat[]? formatArray;
    public double entryY;
    public int level;
    // TocInfo.FormatArray[level-1] carries this level's formatting
    // (font size, subsequent-lines indent, margins) — every entry
    // of a level is formatted from it. Falls back to the
    // heading's own TextState / margins when no level format is set.
    public LevelFormat? fmt;
    // The governing level format's TextState, bound once: it is null
    // exactly when no level format governs this entry, so every read
    // below resolves through that single null check.
    public Aspose.Pdf.Text.TextState? fmtState;
    // The entry's size and line spacing resolve through a fixed
    // chain: the level format wins, then the first SEGMENT
    // whose value was explicitly set (a segment font size set by the
    // caller beats the heading's own TextState), then the heading.
    public double? segSize;
    public double segSpacing;
    public double entrySize;
    // The level format's FontStyle picks the Standard-14 Helvetica
    // variant the whole entry (text, leader and page number) is set in.
    public string entryFace = null!;
    // The level's leader style; the TocInfo-wide LineDash applies only
    // when no level format governs this entry.
    public Aspose.Pdf.Text.TabLeaderType entryLeader;
    // Subsequent (continuation) lines of a multi-line entry are
    // indented by this level's SubsequentLinesIndent.
    public double subIndent;
    // The line pitch is the entry size PLUS the resolved LineSpacing —
    // plain entries pack one font-size apart, and an
    // entry with TextState.LineSpacing=18 at 11 pt steps 29 pt per line
    // (both its own wrapped lines and the gap to the next entry).
    public double lineSpacing;
    public double lineH;
    // Every line box's BOTTOM sits exactly one pitch below the previous
    // line's bottom (the chain runs on rect bottoms, not baselines) —
    // startY is the previous line's bottom, so this entry's first
    // baseline is one pitch lower plus the font's descent.
    public double descFrac;
    public string prefix = null!;
    // A heading authored through SEGMENTS (Heading.Text left empty)
    // still titles its TOC entry — the entry text is the segment
    // chain's concatenation.
    public string headingText = null!;
    // The prefix is NOT part of the wrapped body: a numbering
    // style that prints no number leaves a whitespace-only
    // prefix, and the word splitter drops leading spaces. Line
    // one carries the prefix back verbatim once wrapped.
    public string entryBody = null!;
    // A segment carrying its OWN font measures the entry — and its
    // leader dots and page number — by that font's REAL advances:
    // the column-fit scale is computed in the entry font's metrics,
    // and positions are pinned to fractions of a point of those
    // advances (a Standard-14 approximation lands dots a few
    // hundredths of a point astray).
    public System.Func<string, double>? entryAdvance;
    public Text.Font? segFont0;
    public string pageNumStr = null!;
    public double pageNumWidth;
    public double colLeft;
    public double colWidth;
    // Entries are NOT indented by heading level — every level starts
    // at the column left edge (plus any explicit level-
    // format left margin).
    public double indent;
    public double rightStop;
    public double pageNumX;
    // The auto-number sits alone at the column left edge and the
    // entry TEXT hangs under it: every line after the first starts
    // at the prefix END, not back at the column edge, plus this
    // level’s SubsequentLinesIndent. A wrapped entry therefore
    // reads as one indented block beside its number.
    public double prefixWidth;
    public List<(string text, double x)> wrapped = null!;
    // Out of vertical room — the entry's line boxes plus its own
    // bottom margin must fit above the page's bottom margin (an
    // entry with Margin.Bottom=300 overflows even when its single
    // line alone would fit). Move to the next column for a
    // multi-column TOC; when the columns are exhausted, continue
    // on the next CONTINUATION page (one is inserted
    // after the TOC page — entries are never truncated).
    public double entryMarginBottom;
    // LEVEL-GROUP ORPHAN CONTROL: an entry that STARTS a run of
    // same-level entries does not open the run at the page foot
    // alone — when fewer than TWO of the run's entries fit the
    // space left (and the whole run would fit one full TOC page),
    // the run opens on the next continuation page instead (the ten
    // level-2 entries open the final TOC page together while the
    // level-3 run fills the page before them). A run whose first
    // two entries fit — or one too long for any single page —
    // breaks wherever it runs out.
    public bool forceTocGroupBreak;
    public int grpIdx;
    // All entry pieces are positioned with Tm (absolute text matrix),
    // not Td: column repositioning on one visual row goes out
    // via Tm, and RAW extraction merges same-row Tm shows into
    // one output line ("1  Heading 1.....2"), while Td-per-BT blocks
    // stay one-line-per-show (table cells). This operator
    // choice lets the extractor reassemble the entry as one
    // line without loosening any extraction heuristics.
    public Content.ContentStreamBuilder b = null!;
    public string entryFontRes = null!;
    // A leadered, numbered entry defers its FINAL line to the leader
    // emission: that line is drawn horizontally scaled to the column
    // (Tz), and the scale depends on the page-number width — which is
    // only known after the final page sequence exists.
    public bool deferFinal;
    public double lastY;
    public double lastLineW;
    // The entry's link box spans the whole entry: height is exactly
    // the rendered-line count times the line PITCH (2×29 = 58 pt
    // for a two-line 11 pt entry with 18 pt line
    // spacing), top where the entry's box started, bottom at the last
    // line's rect bottom, right edge on the column's right stop.
    public double linkTop;
    public Rectangle linkRect = null!;
}
}
