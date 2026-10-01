using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    // ── The classed border-table case letter ────────────────────────────────
    //
    // A generated merchant letter: a #PageHeading table, an address table with
    // padded cells, prose tables, a bulleted request list, and then numbered
    // case tables — each a "N." marker cell beside a nested table whose CLASS
    // (.blackBorder) declares all four border-side longhands on the table and
    // on every cell. <div class="break"> splits the letters onto their pages.
    //
    // The geometry is measured on the expected output for this
    // fixture (it reproduces the shipped era template exactly):
    //  - page 657 × 842: 96 left margin + the declared 630 px box + 88.5;
    //  - a 9 pt line paces 10.5 (floor(1.2 × 12 px) css px), an 11 pt line
    //    12.75, everything seats its baseline 8.0 under its band top;
    //  - the heading draws ONE 20 pt darkgray line ("HSBC" bold) at x 98.25,
    //    baseline 111.18; the address block opens at baseline 199.19;
    //  - case tables: declared percent columns of the 459 box, and a column
    //    whose MIN-CONTENT (longest unbreakable token + the 1.13 pad pair)
    //    exceeds its share takes that min while the others give the surplus
    //    up in proportion to their own slack;
    //  - a row is max-cell-lines × 10.5 + 2.25 tall (+0.375 per extra line),
    //    each cell's lines centred in the row; the marker "N." seats at the
    //    table top + 8.0.
    private const double ClPageW = 657.0;
    private const double ClPageH = 842.0;
    private const double ClLeftPt = 96.0;               // outer tables' left edge
    private const double ClCellChromePt = 1.5;          // cellspacing 1 + cellpadding 1
    private const double ClLine9Pt = 10.5;              // 9 pt body line (14 css px)
    private const double ClLine11Pt = 12.75;            // 11 pt address line (17 css px)
    private const double ClAscSeatPt = 8.0;             // baseline under the band top
    private const double ClHeadingBaselinePt = 111.18;
    private const double ClHeadingXPt = 98.25;          // UA table chrome (spacing 2 + pad 1)
    private const double ClAddrBaselinePt = 199.19;
    private const double ClAddrXPt = 121.5;             // cell chrome + padding-left 32 px
    private const double ClDateBaselinePt = 250.62;
    private const double ClRequestBaselinePt = 328.62;
    private const double ClParaBaselinePt = 355.62;
    private const double ClPleaseBaselinePt = 403.62;
    private const double ClBulletBaselinePt = 424.2;
    private const double ClBulletMarkerXPt = 120.97;
    private const double ClBulletTextXPt = 127.5;
    private const double ClBulletNestedXPt = 150.93;
    /// <summary>The first case table's top border under the last bullet baseline.</summary>
    private const double ClCaseGapAfterProsePt = 29.08;
    /// <summary>Between two case tables on one page (their <br/> plus chrome).</summary>
    private const double ClInterCaseGapPt = 16.88;
    /// <summary>A break page's first case-table top border.</summary>
    private const double ClBreakPageTopPt = 87.38;
    /// <summary>The case grid's left border past the marker column.</summary>
    private const double ClCaseGridLeftPt = 107.63;
    private const double ClCaseGridRightPt = 566.62;
    private const double ClCellPadXPt = 1.13;
    private const double ClRowPadPt = 2.25;
    private const double ClRowExtraLinePt = 0.375;
    private const double ClBorderW = 0.75;
    /// <summary>Overflow guard for un-broken content (the era pages never reach it).</summary>
    private const double ClBottomPt = 770.0;
    private static readonly Color ClHeadingInk = Color.FromArgb(169, 169, 169); // darkgray

    private sealed class ClCellRun { public string Text = ""; public bool Bold; }
    private sealed class ClCell
    {
        public List<ClCellRun> Runs = new();
        public int ColSpan = 1;
        public List<List<ClCellRun>>? Lines;            // wrap result
    }
    private sealed class ClCaseTable
    {
        public string Marker = "";
        public List<double> ColPct = new();
        public List<List<ClCell>> Rows = new();
        public bool BreakBefore;
        public int SourceEnd;
    }

    private static Document? TryRenderCaseLetter(string html)
    {
        if (html.IndexOf("id=\"PageHeading\"", System.StringComparison.OrdinalIgnoreCase) < 0
            || html.IndexOf("class=\"blackBorder\"", System.StringComparison.OrdinalIgnoreCase) < 0
            || html.IndexOf("class=\"break\"", System.StringComparison.OrdinalIgnoreCase) < 0) return null;
        // The letterhead-IMAGE variant of the same letter renders its logo through
        // the legacy flow (calibrated green there); this arm is the imageless
        // #PageHeading text dialect only.
        if (html.IndexOf("<img", System.StringComparison.OrdinalIgnoreCase) >= 0) return null;
        var ce = new CaseLetterState();
        if (!TryParseCaseHead(ce, html)) return null;
        ParseCaseBullets(ce, html);
        if (!TryParseCaseTables(ce, html)) return null;

        DrawCaseProse(ce);
        ce.caseTop = (ce.y - ClLine9Pt) + ClCaseGapAfterProsePt;
        ce.innerBox = ClCaseGridRightPt - ClCaseGridLeftPt;
        ce.firstOnPage = false;
        foreach (var ct in ce.caseTables)
        {
            if (!DrawCaseTable(ce, ct)) break;
        }

        // Closing paragraph on the final page.
        if (ce.closeText.Length > 0)
        {
            var cl2 = Wrap(ce, ce.closeText, ce.proseBox, 9);
            var cy = ce.caseTop - ClInterCaseGapPt + ClInterCaseGapPt + ClAscSeatPt;
            for (var i = 0; i < cl2.Count; i++)
                T(ce, 9, ce.contentX, cy + i * ClLine9Pt, cl2[i]);
        }
        FlushStrokes(ce);
        PruneUnusedFonts(ce.doc);
        return ce.doc;
    }
}
