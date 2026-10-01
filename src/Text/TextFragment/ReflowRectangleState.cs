
namespace Aspose.Pdf.Text;

public partial class TextFragment
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class ReflowRectangleState
{
    public Page page = null!;
    public Aspose.Pdf.Text.Font font = null!;
    public double baseFs;
    // Left/RightAdjustment extend the wrap borders (a negative left
    // adjustment widens leftward, a positive right one rightward).
    public double leftAdj;
    public double rightAdj;
    // AdjustSpaceWidth justifies every wrapped line but the last to the
    // wrap width by widening the inter-word gaps.
    public bool justify;
    // Derive the line pitch (baseline-to-baseline) from the fragment's current
    // multi-line layout so the reflowed block keeps the same leading —
    // averaged over the whole first-to-last span, so the per-segment
    // position quantization doesn't accumulate over the reflowed lines.
    public double leadingRatio;
    public double wrapWidth;
    // The source run's text matrix scales every advance it draws, so the width a
    // line occupies on the page is its advance sum times that scale. Measuring
    // and wrapping happen in the font's own space; only the page-space results
    // carry the factor.
    public double sx;
    public double wrapWidthT;
    public Aspose.Pdf.Text.TextReplaceOptions.FontSizeAdjustment fit;
    // A font resolved without its real width table measures every glyph near
    // an em wide; the original segments carry their TRUE drawn widths, so
    // calibrate the measure against them before wrapping.
    public double measureScale;
    // An embedded font program measures through its OWN advance table (hmtx at
    // its own units-per-em) — the width table the wrap measurement works from.
    // The PDF /Widths array carries the same advances rounded to integer
    // 1000ths of an em, and that rounding is enough to move a fitted size off
    // by a tenth of a point.
    public Func<string, double, double>? hmtxMeasure;
    // A source font that cannot encode the replacement (a Japanese face asked to
    // show Latin, a subset carrying only its own glyphs) is SUBSTITUTED, and the
    // substitute is the serif default — the replacement measures and writes in
    // Times, not in the Helvetica the family map falls back to. Detected by the
    // width table itself: a face with no widths for these characters reports
    // about a full em each.
    public string? substFace;
    // A stand-in has to be able to SHOW the replacement. The serif default carries
    // Latin, Greek, Cyrillic and the common symbols, but not a dingbat like '★', a
    // circled numeral, kana or han — and ONE character it has no glyph for re-dresses
    // the WHOLE run in a face that does. That is why a line of ordinary English can
    // come back measured at half-width advances: the covering face sets its Latin at
    // half an em, so a run led by one star fits a size well below the serif one.
    public string? coveringFace;
    public Aspose.Pdf.Text.IGlyphOutlineSource? coveringGlyphs;
    // The stand-in follows the SHAPE of the font it stands in for. Replacing a CID
    // font's run produces a composite stand-in carrying the installed face's own
    // advances; standing in for a simple font keeps a simple one, whose widths are
    // the core table's. The extent the page reports back follows that choice, so
    // the measure has to make it too.
    public double[]? substWidths;
    public System.Func<string, double, double>? substMeasure;
    // Prefer measuring with the source resources' own width tables; the
    // calibrated host-face measure (wrapWidthM compensates its scale) is
    // the fallback.
    public System.Func<string, double, double>? srcMeasure;
    public double wrapWidthM;
    public double fs;
    // Wrapped lines keep the break's inter-word space (the last line ends at
    // its final word), so each re-absorbed line extent includes the trailing
    // space advance — the same extent the source lines report.
    public List<string> lines = null!;
    public double leading;
    // Anchor the block so its re-absorbed top matches rect.URY. The wrapped
    // lines are written through TextBuilder, which maps a non-embedded font
    // to a Standard-14 face; TextFragmentAbsorber then reconstructs that
    // run's box as URY = baseline + (1.1·fs + descentOff) and
    // LLY = baseline + descentOff (descentOff negative). Use the WRITTEN
    // font's descent (not the original run's ascent) so the anchor lines up.
    public string writtenFontName = null!;
    // An Arial-family source is re-fonted with the system Arial face rather
    // than the Helvetica AFM face: the written resource carries Arial's own
    // vertical metrics (hhea descender -434 in a 2048 em, truncated to -211)
    // in a FontDescriptor, and the anchor math below runs on that descent.
    public bool arialFace;
    public double descentOff;
    public double ascentH;
    public double firstBaseline;
    // Baseline grid for the SECOND and later lines. On the re-fonted path the
    // first line keeps the source baseline itself while the rest of the block
    // sits on the descent-corrected grid — the two differ by exactly the
    // source-vs-written descent delta, so the re-absorbed box top follows the
    // written face while the bottom stays on the source box model.
    public double gridFirst;
    // Write the wrapped lines as positioned fragments.
    public Aspose.Pdf.Text.TextBuilder tb = null!;
    public double maxW;
    // Update this fragment to the laid-out block (matching the absorber's
    // baseline+descentOff floor and baseline+ascentH top).
    public double lastBaseline;
    // ★ The reported box is ONE LINE deep whatever the block wraps to: the extent a
    // replaced paragraph reports back is its top edge less a single line's height,
    // not the height of the lines it actually drew. A block that fills its rectangle
    // reports the same box as one that half-fills it, which is why every case here
    // wants h = leading·fs exactly. The DRAWN lines are untouched — this is what the
    // paragraph reports, not where its text sits.
    public double reportedURY;
    // The reported line box is 1.1 em — the face-independent line height a replaced
    // paragraph reports back, NOT the flow's own leading (which is 1.2 here and is
    // what spaces the drawn lines apart).
    public double reportedLineHeightEm;
    public double singleLineLLY;
    // Keep the drawn-block floor when it is the SHALLOWER of the two: a one-line
    // block already reports its own line, and a source whose descent puts the floor
    // above that must not be pushed down by the nominal line height.
    public double reportedLLY;
}
}
