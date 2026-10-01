using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Converters;

public sealed partial class PdfToHtmlConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class StlDivState
{
    // 1. Trim line-edge space glyphs.
    public int lo;
    public int hi;
    // 2. Build the item stream: chars with advance errors, and space slots
    //    (kept, dropped or synthesized around 0.6×m).
    public List<Aspose.Pdf.Converters.PdfToHtmlConverter.StlItem> items = null!;
    // The items of the PART being emitted: a number-column or tab split hands
    // EmitStlPart one slice of the line, and the spans it builds are that slice's.
    public List<Aspose.Pdf.Converters.PdfToHtmlConverter.StlItem> partItems = null!;
    // Facts the number-column split below turns on: whether any REAL space
    // glyph is drawn on the line (a synthesized gap-space advances the pen
    // but carries no drawn advance of its own), and the raw gap behind a
    // single leading character (captured when the first slot lands at
    // items[1]).
    public bool lineHasSpaceGlyph;
    // A uniformly letter-spread line (every inter-char pen gap carries the
    // same tracking) is LETTER-SPACING, not word gaps: such a heading is
    // emitted as plain words ("Journal of Xiangfan University"), not
    // atomized per gap into 'J o u r n a l …'. The em-compensation
    // synthesis therefore measures each gap against the line's TYPICAL
    // inter-char gap (median over positive gaps, 4+ samples) instead of
    // against zero — word boundaries still exceed it by a space width.
    public double lineSpreadPt;
    public double headGapPt;
    public double headFs;
    public int i;
    // 3. Span boundaries: style changes and gap atomization.
    public bool[] cut = null!;
    // A slot rides the style of the char it follows, so it stays inside the
    // span it trails; the boundary lands on the next RENDERED char whose
    // style differs from the last rendered one — a word gap between two
    // differently-sized runs must still cut.
    public int lastRendered;
    public int runStart;
    // 4. Assemble spans left-to-right, folding/externalizing slots.
    public List<(List<Aspose.Pdf.Converters.PdfToHtmlConverter.StlItem> Items, int Style, bool IsNbsp, double? InheritWs)> spans = null!;
    public List<StlItem>? cur;
    public int curStyle;
    public List<double> foldedSlots = null!;
    public double? pendingInheritWs;
    // 5. Emit. Div geometry: left from the first rendered item, top from the
    //    first span's font ascent.
    public (List<Aspose.Pdf.Converters.PdfToHtmlConverter.StlItem> Items, int Style, bool IsNbsp, double? InheritWs) first;
    public Converters.PdfToHtmlConverter.StlRunStyle st0 = null!;
    public double left;
    public double top;
    public int popupBoxNum;
    public int renderedChars;
    public int lastFontNum;
    public int lastLhNum;
    public int lastLsNum;
    // The extraction inputs, captured from the method parameters.
    public StringBuilder sb = null!;
    public List<StlLineGlyph> glyphs = null!;
    public List<StlRunStyle> styles = null!;
    public StyleRegistry styleReg = null!;
    public ClassNamer classNamer;
    public string divCls = null!;
    public string zStyle = null!;
    public double pageLLX;
    public double yTop;
    public double baselineY;
    public Func<double, double, LinkTarget?>? linkFor;
    public List<(string Label, string Href)>? popupItems;
    public double turnedOverShiftLeftEm;
    public double turnedOverShiftTopEm;
    public bool emGrid;
    public Converters.PdfToHtmlConverter.StlLineGlyph g;
    public Converters.PdfToHtmlConverter.StlRunStyle st = null!;
    public double fs;
    public double fsEff;
    // A ligature code expanded to several chars renders as its COMPONENT
    // glyphs in the browser model, so the head and its expansion tails
    // fuse into ONE item whose natural width is the face's component
    // advances — the pair's whole advance error is the (small) ligature-
    // vs-components width difference, not two large opposite errors that
    // would atomize the span.
    public int itemEnd;
    public double wSum;
    public double tailW;
    public string? itemText;
    public bool fuseByFace;
    public double ttfMilliItem;
    // The components-vs-lig face delta charged to the ws numerator but
    // NOT to the ls mean (LsE): the ls classes ignore it.
    public double lsAdjMilli;
    public double ttfPt;
    // The em-compensation PEN basis drops the /W-vs-program rounding
    // residue: such a /W is authored as round(program float),
    // so δ = round(float) − float per item — the solve sees each char's
    // error as exactly the kern/gap residue. Other dialects keep the
    // physical /W pen. (The drawn advance wSum itself carries the TJ
    // kern, which must stay.)
    public double penPt;
    // Locate the next rendered char and whether real space glyphs sit between.
    public int j;
    public bool sawSpace;
    public int spaceStyle;
    public double spaceGlyphMilli;
    // The width the drawn space actually contributes, taken from the source's
    // own /Widths rather than from a face measurement: when the run's face is
    // not installed, an unmappable space measures as the half-em guess, which
    // is nearly twice a real space and silently swallows the word break.
    public double spaceDrawnPt;
    // The slot metric m: a space glyph of the LINE's own font measures by
    // the font's space advance; a foreign-font word gap (and a
    // synthesized slot) measures by the line font's space advance at the
    // line font's size.
    public double mMilliSlot;
    public double mPt;
    public double gapPt;
    // Slot decision. For a synthesized/kern gap (no space glyph drawn) the gap
    // must reach 0.6 of the line font's nominal space advance. When a space glyph
    // WAS drawn, measure against the width that glyph actually contributes: a real
    // word space leaves a gap close to its drawn advance, whereas a space drawn for
    // justification/letter-spacing is pulled back by a following negative kern, so
    // its gap falls well short of the drawn width and must not open a word break.
    // (Against the nominal advance the two are indistinguishable — both ~0.45·m.)
    public bool slotFires;
    // The em-compensation dialect emits the css font-size ROUNDED to the
    // 0.01-em grid (drawn 11 → 0.92em, 15 → 1.25em, 40 → 3.33em); the ws
    // solve above uses the TRUNCATED size — the dialect's own
    // deliberate inconsistency, not to be reconciled.
    public int fontNum;
    public int lhNum;
    public double lsMilli;
    public string text = null!;
    public double? wsEm;
    public double emVal;
    public double pxVal;
    public int lsNum;
    // A bold/italic run carries its weight inline: the emitted font class
    // names the FAMILY only, so a viewer falling back to a system face
    // would otherwise render the run regular.
    public string weightCss = null!;
    public string wsCss = null!;
    public string wsAttr = null!;
    // A link annotation covers a RECTANGLE, not a line: each span resolves
    // its OWN target from its glyph extent, so a row of per-word hotspots
    // gives each word its own href instead of putting the whole line inside
    // the first rect's anchor. A span inside a line-wide rect still binds to
    // that rect (it is the first match), which is how a per-word hotspot
    // nested in a row-spanning link ends up with no anchor of its own.
    public LinkTarget? spanLink;
}
}
