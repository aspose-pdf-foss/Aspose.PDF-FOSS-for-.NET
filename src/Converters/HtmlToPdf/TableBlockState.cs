using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class TableBlockState
{
    // Metric flow: tables render through the metric layouter (real HTML
    // geometry + win-metric line boxes), not the generator table. A
    // RADIO-bearing table is the exception: the metric layouter neither
    // grids nested tables nor draws form controls, so it takes the
    // generator grid below (whose lift + slice pass carry both).
    // Quirks-mode CSS-run documents (no <!DOCTYPE>) take the same
    // layouter: the body rule's pixel font does not inherit into table
    // cells there — cells render at the UA 16px base in the body face
    // (measured: Calibri cells at 12 pt, 18 pt row pitch).
    public bool quirksRunTable;
    public string? tableFace;
    // The band dialect's table conventions (nbsp spacer rows keep their line
    // boxes, CSS row pitches, zero empty rows) hold for a filing document's
    // top-level tables too — the ToC listing and the shaded proposals grid
    // page on the same conventions.
    // A synthesized form-horizontal row: the control-group's CSS rhythm —
    // the value span's 5px margin-top, the controls div's 1px padding-top
    // and the collapsed 3px inter-group margins — separates it from the
    // block above (9px per row is the row pitch).
    public string fhTableHtml = null!;
    public bool fhRow;
    // A metric-flow doc's radio table grids through the generator (see the
    // metric bypass above); its cells keep the metric dialect's base font
    // and pitch on the browser line box the items lay out on.
    public bool radioGridTable;
    public double radioGridFontPt;
    // The radio factory for this grid: one RadioButtonField per HTML
    // `name` (an anonymous group per unnamed input), anchored on the page
    // the table starts on. Options join their group at creation so the
    // render pass can place each widget via OwnerRadio; the groups are
    // registered on doc.Form after the flow pass.
    public Page tablePage = null!;
    // The over-declared grid document resolves EVERY table — any
    // nesting depth — against the same standard box: pageW − margins
    // − the UA body gutter − the filing shell's host chrome pair
    // (measured: pageW − 201 at every page width).
    // The certificate dialect lays a table out at the width it DECLARES and
    // lets it overflow the content box, exactly as a browser does: the
    // reference keeps its 720 px (540 pt) grid on a 595 pt sheet and clips
    // it at the page edge, where squeezing it into the 403 pt content box
    // wraps every long session title onto a second line and costs a page.
    public double certTableW;
    public double tableAvailW;
    public Table? table;
    public double renderNatW;
    public List<byte[]> slices = null!;
    public IReadOnlyList<List<byte[]>> graphs = null!;
    public IReadOnlyList<List<(byte[] data, Aspose.Pdf.Rectangle rect)>> imageDraws = null!;
    // A float-band column is an overflow:hidden box: a table reaching the
    // page bottom CLIPS there instead of paginating (a browser never splits
    // a float box), so the band's other columns stay on the band's page.
    public bool bandClipped;
}
}
