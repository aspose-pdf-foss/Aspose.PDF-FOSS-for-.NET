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

public sealed partial class Document : IDisposable
{
    private const double psFs = 12.0, psPitch = 13.5, psAscent = 10.85;

    private void LayoutProcedureStepRows(List<Converters.HtmlToPdfConverter.StepRow> psRows, HtmlFragment html, FlowLayout flow, Page page, double marginLeft, double marginRight, double marginTop, double marginBottom)
    {
        // Procedure-step form rows: each bullet column numbers a
        // content column of widget lines — text runs, underlined
        // fill-in blanks at their CSS widths, stroked radio and
        // checkbox glyphs, data-entry tables — on the form's
        // 13.5 pt rhythm. Tables lay out fixed at their declared
        // width and clip at the wrap box's right edge; a data-
        // entry caption travels with its table across pages; the
        // acknowledge widgets stack blanks and small labels at
        // the sheet's right end, and a clog row draws its full-
        // height right border.
        var psWrapRight = page.Width - 34.8;



        // word-first wrap; an overlong word char-breaks (break-all)
        // chars of s that fit the first display line at maxW:
        // whole words first, char-break only an overlong word
        // the first word of s (leading spaces included in its span)
        for (var prowIdx = 0; prowIdx < psRows.Count; prowIdx++)
            LayoutStepRow(prowIdx, psRows, html, flow, page, marginLeft, marginRight, marginTop, marginBottom, psWrapRight);
    }

    // height consumed instead of moving the flow cursor, so a nested
    // layout table reports upward and every row advances exactly once.
    private static IEnumerable<Table> LeafTables(Table t)
    {
        foreach (var r in t.Rows)
            foreach (var c in r.Cells)
                foreach (var p in c.Paragraphs)
                    if (p is Table inner)
                    {
                        if (HasNestedTables(inner))
                            foreach (var deeper in LeafTables(inner)) yield return deeper;
                        else yield return inner;
                    }
    }

    private static bool HasNestedTables(Table t)
    {
        foreach (var r in t.Rows)
            foreach (var c in r.Cells)
                foreach (var p in c.Paragraphs)
                    if (p is Table) return true;
        return false;
    }

    /// <summary>The embedded face a caller set on the page's or the document's
    /// <see cref="PageInfo.DefaultTextState"/>, if any — the face HTML content
    /// inherits when neither the fragment nor its markup names one.</summary>
    private Text.Font? DefaultTextStateFace(Page page)
    {
        var face = page.PageInfo?.AssignedDefaultTextState?.Font
                   ?? PageInfo?.AssignedDefaultTextState?.Font;
        return face?.SourceFontData?.TtfData is { Length: > 0 } ? face : null;
    }

    // ── UA-serif wide-box fragment ─────────────────────────
    // A full <HTML> document fragment with no font of its own and
    // tables declaring absolute pixel widths: the text sets in the
    // UA serif at the browser's 680 css px wrap, the tables in the
    // widest declared box, and everything clips at the sheet edge.
    // All distances below are empirical constants.
    private const double UaSerifPt = 12.0;          // UA body em

    private const double UaSerifPitchPt = 13.5;     // 12 pt on the 1.125 line

    private const double UaSerifSeatPt = 10.8;      // cursor -> first baseline

    private const double UaSerifH2Pt = 18.0;        // h2: 1.5 em bold

    private const double UaSerifH2BeforePt = 33.47; // prev baseline -> h2 baseline

    private const double UaSerifH2AfterPt = 28.73;  // h2 baseline -> next baseline

    private const double UaSerifMixedPitchPt = 14.3; // a line carrying a styled span

    // ── UA-serif tables: the paste's own spreadsheet grids ──
    // A cell styles itself (face, size, banded fill, 0.5 pt
    // edges) or falls to UA defaults (the serif at 12 pt with
    // the 1 css px padding). Line geometry is the css line-box
    // model computed from the face's own metrics: box = hhea
    // line height rounded to whole css px, baseline seats at
    // winAscent plus half the surplus leading. A cell centers
    // its stack of line boxes in the row (vertical-align:
    // middle); baselines step by the primary face's box while
    // a taller fallback box adds its extra half-leading to the
    // first seat. Declared pt row heights grow by the two
    // 0.5 pt edges. The row-top edge strokes in
    // the declared border colour and the bottom/left edges in
    // black.
    private const double UaTdPadPt = 0.75;      // UA default 1 css px td padding

    private const double UaTableEdgePt = 0.5;   // declared border width
}
