using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Stamps;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public sealed partial class HeaderFooter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class StampTextState
{
    public string? text;
    public string fn = null!;
    public float fs;
    public Text.Font? embedFont;
    // Left offset this paragraph's own CSS puts on its outermost block
    // container (an HtmlFragment styled `#header { margin-left: 200px }`
    // renders indented by that box offset, not at the header band's left edge).
    public double cssLeftIndent;
    // Size and alignment the fragment's outermost block declares inline
    // (a footer div styled `font-size: 12px; text-align: Center` sets its
    // flattened band line that way).
    public HorizontalAlignment cssAlign;
    // Inline paragraph: continue on the previous text line right after its
    // last glyph instead of starting a new line.
    public bool inline;
    public double drawX;
    public double drawY;
    public HorizontalAlignment alignment;
    public Content.ContentStreamBuilder builder = null!;
    public double textW;
    public string hc = null!;
    // ── report-band header ─────────────────────────────────────────
    // An <h5> of inline-block percentage spans over centred <h3>
    // headings: the band's LEFT is the band default (90) plus the
    // sheet's own @page margin, its WIDTH the header body's declared
    // physical width — the anchor holds
    // constant across every page and body width, moves 1:1 with the
    // @page margin, and each span aligns inside its percentage box.
    // The rest of the fragment keeps the ordinary block band, shifted
    // below the headings.
    public double hfBandOffset;
    public double hfBandShift;
    public bool hfBandSmall;
    public double hfBandW;
    // A block-STRUCTURED fragment (several <p>/<div> paragraphs) renders one
    // line per block — the flat tag-stripped join would run all paragraphs
    // together on a single overflowing line. With IsClipExtraContent the
    // band clips: header lines stop where the body's content begins, and a
    // footer sits just below the body's bottom content margin.
    // Inline text-transform resolves before block parsing so the banded
    // lines keep the authored casing. An IsEmbedFonts fragment stays on
    // the legacy path — its declared face must embed as a Type0 subset,
    // which the Standard-14 band writer cannot do.
    public List<Aspose.Pdf.Converters.HtmlToPdfConverter.Block> hfBlocks = null!;
    public List<Aspose.Pdf.Converters.HtmlToPdfConverter.Block> hfTextBlocks = null!;
    public System.Text.RegularExpressions.Match blkStyle = null!;
}
}
