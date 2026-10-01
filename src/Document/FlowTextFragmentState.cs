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
private sealed class FlowTextFragmentState
{
    public bool useEmbeddedFont;
            /// <summary>Whether the paragraph asked for its lines to stay unclipped.</summary>
            public bool unclippedLines;
    // The last line has already moved once to find room for the bottom margin.
    public bool movedForBottomMargin;
    /// <summary>The paragraph's line controls moved it whole to the next region once already.</summary>
    public bool movedByLineControls;
    public string baseFont = null!;
    public float fontSize;
    // In column mode this is the current column's width; otherwise the
    // full page content width. Wrapping uses the entry column's width;
    // the test columns are equal-width so a fragment that flows into the
    // next column keeps the same break points.
    public double contentWidth;
    // WordWrapMode.NoWrap → each \n-delimited input line becomes one
    // output line, regardless of width: a
    // long line stays on one rendered line that overflows the page
    // horizontally; only vertical pagination still applies. Default
    // (ByWords / Undefined / null) flows through the width-aware wrap.
    public bool noWrap;
    // First-line indent (paragraph indentation set via FormattingOptions):
    // the first wrapped line starts indented and is correspondingly narrower.
    public double firstLineIndent;
    // Subsequent-lines indent: every wrapped line after the paragraph's first
    // starts indented by this amount. Applies across page
    // breaks too — a chunk that does not start the paragraph indents all its
    // lines.
    public double subsequentLinesIndent;
    public string rawText = null!;
    public float charSpacing;
    public List<string> allLines = null!;
    // When notification logging is on, trace each wrapped line's width and
    // break reason (aligned 1:1 with allLines) so the loop below can record
    // where every line finished.
    public List<(string content, double width, char reason)>? lineTrace;
    // lineHeight resolution order:
    //   1. fragment.TextState.LineSpacing -- explicit float override
    //      in points, set by callers like
    //      `seg.TextState.LineSpacing = fontSize + 3f`.
    //   2. FormattingOptions.LineSpacing == FullSize -- use the font's
    //      full vertical extent from the TTF (ascent - descent).
    //   3. fontSize * 1.2 -- default for FontSize / Undefined modes.
    public byte[]? fontTtf;
    public bool fullSize;
    public double lineHeight;
    // FullSize takes a line's height from the FONT — so a line with no glyphs
    // has no font to measure and falls back to the DEFAULT face's own full
    // extent (Helvetica, 0.925 em), which is shorter than a text line in a
    // tall face. Measured on a multi-script text dump: the
    // blank-line gaps are exactly lineHeight + 0.925 em per empty line.
    // ⚠ and that default is only in effect on the START page: once the flow has
    // spilled, an empty line measures as one em (its bare font
    // size) instead — probed on a two-page blank-line ladder, where the same
    // double blank is 2 x 9.245 on page 1 and 2 x 10.0 on the overflow page.
    public bool variableLineHeights;
    // A line the requested face cannot COVER is not drawn in it at all: the
    // reference hands that line to a face that has the glyphs, and the line then
    // takes THAT face's full extent. (Arial Unicode MS has no Romanian
    // comma-below letters, so its lines drop to Times New Roman's 1.1074 em.)
    public Dictionary<int, Aspose.Pdf.Text.Font?> fallbackFace = null!;
    public HashSet<int> shapingLines = null!;
    // The covering face a shaping hand-off started from: its extent stays the
    // line's pitch (an Arial paragraph's Telugu line lays out on Arial Unicode
    // MS's pitch and is drawn in Gautami).
    public Dictionary<int, Aspose.Pdf.Text.Font?> coveringFace = null!;
    // The covering-face hand-off applies to EVERY embedded-face fragment, not
    // only FullSize ones - a default-spacing Arial Unicode MS line with
    // Romanian comma-below letters is still handed to Times New Roman by the
    // fallback (the line is drawn, not blanked). Only the LINE-HEIGHT
    // treatment stays FullSize-gated (HeightOfLine below).
    public bool lineFallbackActive;
    // A fragment-level hyperlink applies to the fragment's first line.
    // Capture the slot + top-of-line before the write loop advances _curY.
    public Hyperlink? fragHyperlink;
    public int fragSlot;
    public double fragTop;
    // The baseline the first line actually landed on - a segment link is
    // boxed on the baselines, not on the line box (see LinkBoxExtent).
    public double? fragFirstBaseline;
    // Per-segment hyperlinks: each TextSegment with a Hyperlink emits a
    // LinkAnnotation sized to the segment's run. Char offsets are into the
    // fragment's full text; the emission below maps them onto each wrapped
    // line (a hyperlink that wraps gets one rect per line it covers).
    public List<(int charStart, int charEnd, Aspose.Pdf.Hyperlink hyperlink)>? segHyperlinks;
    // FullJustify: each wrapped line — including the paragraph's last —
    // is stretched so its final word ends exactly at the region's right
    // edge. Every word AND every interior space goes out as
    // its own absolutely-positioned show (the line-break trailing space
    // is dropped), distributing the slack equally across the interior
    // spaces as gaps between a space glyph and the next word; space
    // glyphs keep their natural width. That show structure
    // matters beyond geometry: the absorber yields one fragment per
    // show, and justified-output tests index into that fragment list.
    public bool fullJustify;
    /// <summary>The paragraph is justified (<see cref="HorizontalAlignment.Justify"/>): every
    /// line but its last is stretched to the measure the way a full-justified one is.</summary>
    public bool justify;
    /// <summary>The paragraph is justified by character and word spacing
    /// (<see cref="Text.TextFormattingOptions.JustifySpacingRatio"/>).</summary>
    public bool spacingJustify;
    public Func<string, double>? justifyMeasurer;
    // Center / Right alignment: every wrapped line is offset by its own
    // slack against the write region (half of it for Center), measured
    // on the line without its break space -- a right-aligned line ends
    // on the region's right edge and its trailing space hangs past it
    // (reference column output, probed 2026-08-23).
    public HorizontalAlignment alignMode;
    public Func<string, double>? alignMeasurer;
    public bool alignsLines;
    public int idx;
    public double? nonEmbeddedLastBaseline;
}
}
