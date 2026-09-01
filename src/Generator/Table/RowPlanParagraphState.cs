using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public partial class Table
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class RowPlanParagraphState
{
    public string? text;
    public double fragFontSize;
    public Color? color;
    public Hyperlink? fragLink;
    public List<(string Text, string Url)>? fragAnchors;
    // A fragment-level embedded font (e.g. a CJK Unicode fallback assigned when the
    // cell text has chars outside WinAnsi) — drawn as Type0/CID by the render pass.
    public byte[]? fragEmbeddedTtf;
    public bool fragUnderline;
    public string? fragEmbeddedName;
    // CSS line-box metrics from the HTML styled-cell path (zero = legacy).
    public double fragCssAsc;
    public double fragCssDesc;
    public bool fragKeepBlank;
    public bool fragCssForce;
    // Cell text / TextFragments follow the cell's resolved alignment; a fragment
    // that sets its own non-default alignment wins. An HtmlFragment keeps its own
    // block alignment (left unless its style centres/right-aligns).
    public HorizontalAlignment lineAlign;
    public double htmlCssBoxPx;
    public double htmlBoxedDivLineH;
    public bool fragBold;
    public bool fragItalic;
    public List<(string Text, bool Bold)>? fragGridRuns;
    // Cell text lines pitch at exactly the font size (K = 1.0): a
    // multi-paragraph 8 pt header cell stacks 8 pt per
    // line. The old 1.2× leading only showed on multi-line cells
    // (single-line rows already used the tight height).
    // A fragment carrying an explicit CSS line-height pitches at that
    // instead (the source page's `font: 1em/1.4em …` box model).
    public double fragLineH;
    // A fragment with inline boxes keeps its box height OUT of the row's
    // uniform LineHeight (which would inflate a sibling cell's nested-table
    // reserve lines); its height rides the css-box stack via BoxH instead.
    public bool boxedFrag;
    public double thisLineHeight;
    // A caller-declared LineSpacing is per-line leading in cells: the
    // pitch is fontSize + leading and every baseline sits that much
    // deeper, the leading lying ABOVE the glyphs. Under the XML dialect
    // the DOCUMENT's spacing supplies it (a 12 pt + 4 rows pitch at
    // 16); otherwise the paragraph's own TextState does — a synthetic
    // value assigned by an internal layout path is not the caller's.
    public double fragLeading;
    // CSS run boxes: the fragment's own `line-height: normal` box IS its line
    // box, so a cell stacks each run on its own size's pitch instead of the
    // flat 1.2 em the mixed-size path assumes.
    public double runBoxH;
    // Wrap when the text overflows the column AND the cell permits it:
    // IsWordWrapped is on by default and turning it off keeps the text whole
    // for the cell's clip to crop. Also split on embedded newlines
    // (from HtmlFragment block-element boundaries) so each HTML block starts
    // on its own line.
    // Each inline <a> run annotates the first laid-out line containing its
    // text — one Link annotation per anchor, over just the anchor's run,
    // pre-measured with the SAME metrics that lay the line out.
    public List<(string Text, string Url)>? pendingAnchors;
    public int linesBeforeFrag;
    // The HTML layout pass measured this cell in its own face at exact
    // Standard-14 advances; wrapping against anything else would put the
    // break somewhere other than where the column width came from.
    // CSS run boxes wrap on the same exact advances too: the coarse estimate
    // runs ~5 % wide, which breaks a token the draw then fits comfortably
    // (a 24 pt "content." measured 89.7 against an 86.9 box, drawn at 85.4).
    // A fragment set in a resolved TrueType face (FindFont) wraps on that
    // face's real advances: the generator breaks "...serviceProtocolTab" at
    // the 30th Arial 7 pt glyph (101.2 of a 102 pt column), which the
    // inflated Helvetica estimate below would cut two glyphs early.
    public Func<string, double>? htmlMeas;
    // The CELL'S OWN `line-height` is each line's BOX height in the lifted
    // dialect (the css-box stack advance) — the 1.2-em default otherwise
    // over-pitches a cell the author paced tighter. Document-level pitches
    // stay row-level; the calibrated dialects depend on that.
    public double ownBoxH;
    // The GENERATOR wraps on the bare Helvetica AFM advance — probed by
    // bracketing the threshold from both sides ("MMMM MMMM" is 69.42 pt at
    // 10 pt: one line in a 70 pt column, two in a 69 pt one). That ruler is
    // only the truth when Helvetica IS the face: a cell drawing in a named
    // installed face (or asking for bold/italic) is merely APPROXIMATED by
    // those widths, and there the calibrated ~5 % inflation still stands in
    // for the difference. The HTML dialects keep it throughout — their
    // columns were sized against it.
    public bool genDefaultFace;
    public System.Func<string, double>? genMeas;
}
}
