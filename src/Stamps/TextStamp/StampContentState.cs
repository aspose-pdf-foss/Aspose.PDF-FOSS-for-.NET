using System.Linq;
using System.Text;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Facades;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public partial class TextStamp
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class StampContentState
{
    // Pull effective font/size/colour from TextState first (setting
    // TextState.* on a stamp wins over the
    // bare TextStamp.FontSize/Color), falling back to the stamp's own
    // properties for callers that don't touch TextState.
    public string baseFontName = null!;
    public double fontSize;
    public Color color = null!;
    // Encode Text into single-byte PDF string bytes against WinAnsi, and
    // collect any code-point / glyph-name pairs the resulting font must
    // declare via /Differences so non-WinAnsi chars (Polish ę/ą/ś/ł/ń/ź/ż/ć,
    // Czech č, etc.) render instead of falling back to '?'.
    public byte[] encoded = null!;
    // When the primary font can't represent some glyphs (e.g. CJK/Unicode collapses to
    // '?') and a replacement font program is configured, embed it as a Type0/CIDFontType2
    // font and draw the whole stamp with it so the text renders and round-trips through
    // extraction (the recurring non-Latin1 stamp-text path).
    public (byte[] ttf, string name)? replacement;
    public string fontResName = null!;
    // Wrapping is enabled by the WordWrap bool OR a non-NoWrap WordWrapMode
    // (both are exposed; this ctor path sets only the bool).
    public bool wrapping;
    // Rotated stamp with an explicit Width×Height box: the text scales
    // non-uniformly to fill the box (sx=Width/textW, sy=Height/textH), the box is
    // centred per Horizontal/VerticalAlignment, then rotated about the box centre.
    // The plain path below only applies the horizontal scale and
    // rotates about the block anchor, which mis-sizes/positions the result.
    public double rot;
    // Word-wrapped stamp with a background box and Scale=false: wrap the text to the
    // inner width (Width minus L/R margins), grow the box to the widest wrapped line,
    // and emit the box as the leading `q / x y w h re / rg / RG / f*`
    // block — the text follows inside it.
    public Color? bgEarly;
    // Break the text into display rows: wrap to the stamp width when wrapping
    // is on, otherwise split on the explicit '\n' line breaks that a
    // FormattedText (AddNewLineText) or a multi-line Value carries. The old
    // no-wrap path emitted the raw '\n' byte inside a single Tj string, which
    // is not a line break in PDF — every line collapsed onto one row.
    // When wrapping is requested but no explicit stamp width is set, wrap to the
    // page's available extent along the text's advance axis so a WordWrap stamp lays
    // out as multiple on-page lines instead of one line running off the edge — which
    // page-bounds text extraction would then crop. The extent is the UNROTATED
    // MediaBox dimension: the stamp's own Rotate turns the advance vertical at 90/270
    // (Rotation values are degrees, %180==90 catches both), while page /Rotate is a
    // view transform only — extraction bounds are the unrotated MediaBox, so page.Width
    // /Height (which swap for a rotated page) must not choose the wrap axis.
    public int stampDeg;
    public bool wrapVertical;
    public Rectangle mb = null!;
    public double advanceDim;
    public double wrapLead;
    public double wrapTrail;
    public double wrapWidth;
    // Natural (un-scaled) row widths and the block width (the widest row).
    public List<double> rowWidths = null!;
    public double blockWidth;
    // A stamp with an explicit Width stretches/condenses its text horizontally
    // to fill that width (the whole stamp form scales
    // by Width / naturalWidth). No Width ⇒ draw at natural size.
    public double scaleX;
    public double scaledBlockWidth;
    // Leading of one em (stamp lines are spaced by exactly the font size).
    public double lineHeight;
    public Content.ContentStreamBuilder builder = null!;
    // Place + scale the block with a single cm: rotate about the block anchor
    // when requested, then apply the horizontal fill-scale. Drawing happens in
    // block-local coordinates (top line baseline at y=0, growing downward).
    public double rotateDeg;
    public double cos;
    public double sin;
    public Color? bgColor;
    public bool hasBg;
    // A multi-line block without a background box anchors
    // at its BOTTOM — the last row's baseline sits one font-descent above
    // the block origin and each row's Tm carries the absolute in-block Y
    // ((N-1-li)·lineHeight + descent). The cm translation is lowered by the same
    // amount, so the net page placement is unchanged; only the Tm/cm split moves.
    public double bottomAnchor;
    public double cmY;
    // A 90/270-rotated stamp advances along page Y; the corner-anchored matrix below
    // would swing the block off the page (advance one way off the baseline, rows into
    // −X). Re-anchor so the block's rotated page-space bounding box honours
    // Horizontal/VerticalAlignment inside the unrotated MediaBox, keeping every glyph
    // on-page. Upright 0/180 stamps keep their existing anchor.
    public int rotQuarter;
    // An OFF-AXIS rotated stamp (e.g. 45°) is anchored by its ROTATED BOUNDING BOX,
    // not by the baseline start: the stamp's content box rotates about
    // the box origin and translates so the rotated box's min corner lands at
    // (XIndent, YIndent) (the matrix composes size·scale·rotation·shift(point);
    // the anchor is offset by the rotated box extents). Pinning the baseline start
    // (originX, cmY) leaves the stamp shifted by the rotated box's overhang. Applied
    // only to the SIMPLE case it is derived for — a single-line, XIndent/YIndent-placed
    // stamp with no alignment override, background box, wrap or width; quarter rotations
    // (90/180/270) use the alignment re-anchor above instead.
    public bool rotAnchorSimple;
    // Optional background box: when TextState.BackgroundColor is set, fill a
    // rectangle behind the text in the block-local (already rotated/placed)
    // space. The box spans the block width and one 1.1-em line box per row;
    // the text baseline is raised by the descent so the glyphs sit inside it.
    public double bgYOffset;
    public Page page = null!;
    public List<byte[]> rows = null!;
    public double topBaseline;
    public List<(byte code, string glyph)> diffMap = null!;
    public double originX;
}
}
