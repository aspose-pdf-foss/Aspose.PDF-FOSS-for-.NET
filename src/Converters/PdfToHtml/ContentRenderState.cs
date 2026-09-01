using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Converters;

public sealed partial class PdfToHtmlConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class ContentRenderState
{
    public IO.PdfLexer lexer = null!;
    public List<Aspose.Pdf.Core.PdfObject> operands = null!;
    // Applied text spacing beyond the font's natural advances — this is exactly what
    // becomes CSS letter-spacing: Tc per character, plus the TJ kerning residual
    // (positive TJ numbers move left, so subtract). Accumulated per group.
    public double charSpacing;
    public double wordSpacing;
    public double pendingTjNum;
    public double groupTjNum;
    public int groupChars;
    // Text state. The text matrix (Tm) and text line matrix (Tlm) are tracked in
    // full: a PDF commonly carries the visible glyph scale in Tm (e.g. `Tf /F 1`
    // then `Tm [12 0 0 12 …]`), so reading the size from Tf alone yields 1pt text
    // and mis-placed runs. `tx`/`ty` mirror the text position for the `v` path op.
    public double tx;
    public double ty;
    public Converters.PdfToHtmlConverter.CtmState tlm = null!;
    public Converters.PdfToHtmlConverter.CtmState tm = null!;
    public double leading;
    public double fontSize;
    public double rise;
    // The current face is a Type3 (glyph procedures, no servable program).
    public bool fontIsType3;
    public string fontFamily = null!;
    public string fontCssFamily = null!;
    public string fontWeight = null!;
    // Text rendering mode (Tr). Fill-then-stroke (2) and its clipping variant (6)
    // are the faux-bold idiom: the same face is painted with an outline stroke to
    // thicken it, so a run drawn that way reads as bold even though the font
    // carries no weight of its own. Only THAT weight rides the span inline —
    // a face that declares its own weight is described by the emitted font class.
    public int textRenderMode;
    public string fontStyle = null!;
    public double fontAscent;
    public double fontLineHeight;
    public double r;
    public double g;
    public double b;
    public string? currentFontKey;
    // CTM state (a Form XObject invocation seeds the child call with the
    // composed matrix of its call site; cloned so the caller's isn't mutated).
    public Converters.PdfToHtmlConverter.CtmState ctm = null!;
    public Stack<Aspose.Pdf.Converters.PdfToHtmlConverter.CtmState> ctmStack = null!;
    public Stack<(double r, double g, double b, double fr, double fg, double fb, double sr, double sg, double sb)> colorStack = null!;
    public Stack<(double cs, double ws)> textSpacingStack = null!;
    // Path state
    public Converters.PdfToHtmlConverter.PathState pathState = null!;
    public System.Text.StringBuilder svgPaths = null!;
    // Operator ordinal, and the index of the operator that opened the path
    // currently under construction (-1 = no open path).
    public int opCounter;
    public int pathOpenIndex;
    // Named colour spaces (cs/CS): a Separation/DeviceN space maps its tint
    // operand through the tint transform into the alternate space; null while a
    // plain component space is selected (component-count mapping applies).
    public Func<double, (double r, double g, double b)>? fillTintMap;
    public Func<double, (double r, double g, double b)>? strokeTintMap;
    public double cpx;
    public double cpy;
    // The clip a `sh` paints inside: `W`/`W*` mark the path just built as the new
    // clip, the following path-painting op commits it, and q/Q save and restore it
    // with the rest of the graphics state.
    public string? clipD;
    public string? pendingClipD;
    public Stack<string?> clipStack = null!;
    public int shadingSeq;
    // An open SVG soft-mask group: everything painted between the gs that set a
    // luminosity /SMask and the gs (or Q) that clears it renders inside a
    // <g mask="…">. The depth records the q-nesting at open time so a Q that
    // pops past it closes the group too.
    public bool maskGroupOpen;
    public int maskOpenClipDepth;
    // Constant-alpha group (/ca < 1 via gs): emitted as <g opacity="v"> around the
    // painted content, scoped to the q…Q that set it — like the soft-mask group.
    public bool opacityGroupOpen;
    public int opacityOpenClipDepth;
    public double opacityGroupValue;
    // TrySaveTextUnderliningAndStrikeoutingInCss: horizontal hairlines (stroked
    // lines / thin filled rects) are collected in DEVICE space as decoration
    // candidates; FlushGroup matches them against each text run (same colour,
    // under/through the baseline, horizontally covering the run start).
    // (Y = rule centre line, Thick in device units.)
    public List<(double Y, double X0, double X1, double Thick, double R, double G, double B)>? rules;
    public List<(double X0, double Y0, double X1, double Y1)> pathSegs = null!;
    public double curDevX;
    public double curDevY;
    public List<(double X0, double Y0, double X1, double Y1)> pendingRects = null!;
    // Line-grouping buffer (text-only overlay): consecutive text shows on the
    // same baseline are accumulated into a single span — separately-positioned
    // words (one Tj each) would otherwise become one span per word, so a phrase
    // spanning several shows could never be matched as contiguous text. Matches
    // the expected glyph/word grouping.
    //
    // Each show is kept as its own (x, text) segment and the group is assembled
    // in LEFT-TO-RIGHT positional order at flush time: an RTL writer emits the
    // rightmost word first, so concatenating in content-stream order would
    // reverse the visual line (Hebrew/Arabic text and their embedded LTR digit
    // clusters). For LTR content the stream order is already x-ascending, so
    // the sort is the identity there.
    public List<(double X, System.Text.StringBuilder Text, double PenEnd, double GlyphEnd)> groupSegs = null!;
    // Per-line glyph records feeding the stl_ line solver; lineOk drops to
    // false (legacy emission) when a show lacks aligned per-char advances.
    public List<Aspose.Pdf.Converters.PdfToHtmlConverter.StlLineGlyph>? lineGlyphs;
    public List<Aspose.Pdf.Converters.PdfToHtmlConverter.StlRunStyle>? lineStyles;
    public bool lineOk;
    public int lineStyleIdx;
    public bool groupActive;
    // Extent pinning: the group's device-space right edge accumulated from each
    // run's PDF advances (per /Widths). False when any run's advance is unknown.
    public bool groupPinned;
    public double groupEndX;
    // groupPenX tracks the group's accumulated pen edge (Tc/Tw included) for
    // backward-draw detection in the overlay dialect. groupTextPenX is the
    // pen edge after the last NON-whitespace show — the datum the
    // forward-gap div-split measures from (whitespace shows are transparent).
    public double groupPenX;
    public double groupTextPenX;
    public double groupX;
    public double groupY;
    public double groupFontSize;
    public double groupRise;
    public double groupAngle;
    // The UNSCALED text rise (Ts) of the group. RiseThreshold is defined in text-space
    // units (see its doc), so the sup/sub decision must test the raw rise, not the
    // device-scaled groupRise used for positioning — otherwise a down-scaled text
    // matrix (scale < 1) shrinks the rise below the threshold and drops the tag.
    public double groupRawRise;
    public bool groupIsType3;
    public string groupFamily = null!;
    public string groupCssFamily = null!;
    public string groupWeight = null!;
    public string groupStyle = null!;
    // Whether the group's runs are drawn in the faux-bold rendering mode.
    public bool groupFauxBold;
    // The slant the group's faux-bold declaration states (face italic or shear).
    public string groupDeclStyle = null!;
    public double groupR;
    public double groupG;
    public double groupB;
    // The group was opened by a run drawn in an invisible rendering mode.
    public bool groupTransparent;
    // Ascent fraction of the group's font (usWinAscent/upm) — the fixed-layout
    // top subtracts ascent×size, not a full em. groupZ is the paint-order
    // z-counter value of the group's last non-whitespace glyph (UseZOrder).
    public double groupAscent;
    public double groupLineHeight;
    public int groupZ;
    // Marked-content sequence: advanced at the boundaries of /MCID-carrying
    // BDC…EMC items. groupMcSeq tracks the item of the group's LAST merged
    // show, so line continuation across structure-item boundaries demands a
    // near-identical baseline.
    public int mcSeq;
    public int groupMcSeq;
    public Stack<bool> mcStack = null!;
    // How many layer boxes each open marked-content region opened, so EMC closes
    // exactly its own.
    public Stack<int> ocDepth = null!;
    // Text of the line's last merged show, for the overstrike drop. Line state:
    // parked and resumed with the rest.
    public string groupLastShowText = null!;
    // Lines the producer has moved away from but may come back to, in FIRST-USE
    // order — that order is what they are emitted in, so a page whose lines are
    // drawn one after another (every ordinary page) emits exactly as before.
    public List<Aspose.Pdf.Converters.PdfToHtmlConverter.StlLinePark> parkedLines = null!;
    // The slot the line currently being built came from, if it was resumed: a
    // line writes back to its OWN slot, never to whatever else shares its
    // baseline, so a second line started at the same height cannot displace it.
    public StlLinePark? activePark;
    // The render inputs, captured from the method parameters.
    public byte[] streamBytes = null!;
    public Dictionary<string, HtmlFontRecord> fonts = null!;
    public Dictionary<string, ImageXObject> imageXObjects = null!;
    public PdfReader reader = null!;
    public StringBuilder sb = null!;
    public double pageHeight;
    public double pageWidth;
    public bool saveTransparentTexts;
    public bool emCompensation;
    public bool textOnly;
    public StringBuilder? externalSvgPaths;
    public ExternalImageSink? imageSink;
    public StyleRegistry? styleReg;
    public ClassNamer classNamer;
    public List<LinkTarget>? linkTargets;
    public PdfDictionary? resources;
    public bool preferFontCmap;
    public Dictionary<int, LigatureSubstitutor>? substitutors;
    public CtmState? initialCtm;
    public HashSet<PdfStream>? visitedForms;
    public RotationRegistry? rotReg;
    public bool cssTextDecorations;
    public double pageLLX;
    public double yTopRef;
    public ZCounter? zCounter;
    public string? defaultFontName;
    public bool authoredPathShape;
    public Dictionary<string, (string Name, string? GroupTitle)>? ocLayers;
    public bool pageTurnedOver;
    public bool hasLeading;
}
}
