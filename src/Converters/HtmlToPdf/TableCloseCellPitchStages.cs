using System.Text;
using System.Text.RegularExpressions;
using CellLineSpec = (string Text, double FontPt, string? Family, bool Keep, bool JoinNext, System.Collections.Generic.List<(string Text, string Url)>? Anchors, bool Bold, double MarginTopPt, double MarginLeftPt, Aspose.Pdf.Color? Color, bool Italic);

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The line pitch dialects the styled face and the cell's own line height leave undecided: the quirks grid's per-line box, the cell's declared height, the over-declared grid, the pt fragment and the redline cells.</summary>
    private static void ResolveCellLinePitchTail(CloseCellState cc, CellLineSpec spec)
    {
        // A quirks grid pitches every line on the normal line box of the size THAT LINE draws at, in the
        // face the sheet names: Arial 13 px steps 15 px, 16 px steps 18. One document-wide pitch spaces a
        // 9.75 pt label like body text, and leaving it unset spaces it on the grid's ambient size.
        if (cc.ps.sheetFace is { } qFace && WinMetricsFor(qFace) is { } qm
            && (spec.FontPt > 0 ? spec.FontPt
                : cc.ps.cellClassPt > 0 ? cc.ps.cellClassPt
                : cc.tf.TextState.FontSize) is > 0 and var qSize)
            cc.tf.CssLineHeightPt = MetricLineHeight(qSize, qm.sum);
        else if (cc.ps.cellOwnLineHPt > 0)
        {
            cc.tf.CssLineHeightPt = cc.ps.cellOwnLineHPt;
            // The cell's pitch becomes the LINE'S OWN BOX only for lines that
            // did not style their own font: a run carrying its own size usually
            // carries its own line-height context too (an embedded document's
            // `p { line-height:1.2 }` overriding the host cell's 19px), and we
            // do not model that cascade — the row-level pitch still applies.
            cc.tf.CssLineHeightFromCell = spec.FontPt <= 0 && !cc.ps.cellOwnHeightDecl;
        }
        // A grid whose face came from the DOCUMENT stacks on that face's normal
        // line box (a DECLARED cell line-height above wins, as in CSS). The
        // pitch is a property of the face — Arial 12 steps 13.50 whether or not
        // the table happens to mix sizes — so it does not ride the run-styling
        // gate above, which asks a different question.
        else if (cc.uaDocGrid && cc.defaultCellFace is { } dclf
                 && WinMetricsFor(dclf) is { } dcm)
            cc.tf.CssLineHeightPt = MetricLineHeight(cc.tf.TextState.FontSize, dcm.sum);
        // DataWorks form cells pace on the browser line box of their own
        // size (1.125 em: 13.5 at 12 pt); the h1 title row measures a
        // taller box (its row spans 33.8 with pads).
        else if (cc.dwFormCells && cc.tf.TextState.FontSize > 0)
        {
            PitchFormCellLine(cc, spec);
        }
        else if (cc.cellLineHeightPt > 0) cc.tf.CssLineHeightPt = cc.cellLineHeightPt;
        // Over-declared grid document: lines pitch on the CSS line box of
        // their own size — .875rem (10.5 pt) steps 12, and the sheet's
        // 1rem class declares line-height 16px = the same 12. The bare
        // font-size stack under-paces every row (measured on the spacing
        // ladder: 12 per line through the address block and title rows).
        else if (cc.overDeclaredDraw)
            cc.tf.CssLineHeightPt = Table.CssLineBoxPt(cc.tf.TextState.FontSize);
        // pt-styled fragment: lines pitch on the plain 1.2em box
        // (measured: the wrapped header's inner lines step 12.0 at
        // 10 pt; the 8 pt card rows fall under their declared 10.25 tr
        // height instead). The COLLAPSED border's share of the row pitch
        // is billed once per row in the layout (see the halved borderV),
        // not off the line box.
        else if (cc.ptCellWidths && cc.tf.TextState.FontSize > 0)
            cc.tf.CssLineHeightPt = PtFragmentLineFactor * cc.tf.TextState.FontSize;
        // Redline cells pace on the flow's 1.125 em LINE box (probed:
        // the address rows step 11.25 at 10 pt).
        else if (cc.redlineCells && cc.tf.TextState.FontSize > 0)
            cc.tf.CssLineHeightPt = RedlineLineFactor * cc.tf.TextState.FontSize;
    }
}
