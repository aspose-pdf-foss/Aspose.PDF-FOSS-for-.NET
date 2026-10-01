using System.Text;

namespace Aspose.Pdf.Text;

public sealed partial class ParagraphAbsorber
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class ParagraphGroupState
{
    public List<Aspose.Pdf.Text.MarkupParagraph> paragraphs = null!;
    public List<Aspose.Pdf.Text.ParagraphAbsorber.TextLine> currentLines = null!;
    public double paraLeft;
    // The section's own right edge: a line is judged "short" against the
    // column it sits in, not the page body (a centred heading in a middle
    // column is not short by two columns' worth).
    public double sectionRight;
    // The T-space right edge is the PAGE body's right margin (a section may be
    // narrower than the column it sits in).
    public double bodyRight;
    public List<TextLine> lines = default!;
    public double pageBodyRight = 0;
    public Aspose.Pdf.Text.ParagraphAbsorber.TextLine prev = null!;
    public Aspose.Pdf.Text.ParagraphAbsorber.TextLine curr = null!;
    public double f;
    public double prevF;
    public string text = null!;
    public string prevRaw = null!;
    public string prevText = null!;
    public bool capital;
    // T-indent presumes left-aligned flow. A block that is NOT left-aligned
    // but IS right- or centre-aligned (a right-ragged-left header)
    // scatters its left edges by design, so an "indent" there carries no
    // meaning. A single-line paragraph classifies as left-aligned.
    // The paragraph's left-edge SCATTER separates flow text from
    // right-/centre-placed header blocks: a first-line indent leaves a
    // small (≤ ~4 em) scatter, while a right-anchored header
    // block scatters most of its width.
    public double leftScatter;
    public bool suppressIndent;
    // Inside a bullet item the continuation edge IS an indent relative to
    // the bullet column, so T-indent stays quiet there ("■English…" item's
    // capital-led "United Nations…" continuation).
    public bool paraBullet;
    public bool currBullet;
    public bool tIndent;
    // T-outdent: a hanging-indent paragraph (first line outdented, continuation
    // lines indented — bullet/definition lists) starts anew when a line begins
    // LEFT of the continuation edge (consecutive "■ …" list items).
    public bool tOutdent;
    public bool tNumeric;
    // T-font: a pronounced font-size change is a boundary of its own — a
    // 13.3-pt heading line ("Officer") against the 8-pt list body below it.
    // The sizes compared are the CURRENT line's first fragment against the
    // PREVIOUS line's last fragment (the font-size rule) — a line
    // AVERAGE dragged down by superscript citation runs must not read as a
    // size change (the ［11-18］ citations).
    public double currLeadF;
    public double prevTailF;
    public bool tFont;
    // T-bullet: a bullet line always OPENS a list item, and a non-bullet
    // line returning to the bullet column CLOSES one ("■Place: …" followed
    // by "Closing Date: …" at the same left edge).
    public bool tBullet;
    // The literal whitespace that separates the paragraphs may sit at the
    // START of this line or (with our line assembly) as the TRAILING space
    // run of the previous one; the right-edge gap is measured to the
    // previous line's ink (trailing spaces excluded).
    public int trailingSpaces;
    public double prevInkRight;
    // T-shift: after a SHORT line (ink ending more than ShortLineGapEm of its
    // size before the section's right edge) a capital-led line that starts
    // anywhere but on the previous line's left edge opens a paragraph - the
    // reference splits "Senior" / "Macro-Economist" (outdent 35) and
    // "Finance" / "Officer" (indent 3.7) but keeps "Senior Specialist in" /
    // "International Labour Standards" (gap 2.9 em) together. Probed on
    // equal-pitch Courier lines: a 4 em gap splits at a 1 pt shift either
    // way, a 3 em gap does not, and a same-edge line never splits this way.
    public bool prevShort;
    // A lead that is not a capital (a digit, a bracketed number) needs a
    // shift of whole ems: a page number "6" half an em under a short last
    // line joins the paragraph (it is kept), a "(100) HAMBURG"
    // row outdented by 290 pt under "DEPARTURE PAGE :" opens one.
    public double shift;
    public bool tShift;
    // T-mark: a line that OPENS with a mark - a quotation mark, bracket,
    // guillemet, apostrophe - and then a capital letter starts a paragraph
    // on its own, with no indent and no gap (a newspaper quote paragraph
    // '"As a general rule ...' right under 'can be used. '). Probed on the
    // reference with equal-width lines: “As / "Pi / «Gamma / (Kappa / 'Alpha
    // all split; “1984, ‘theta and a bare capital (Theta) do not.
    public bool tMark;
}
}
