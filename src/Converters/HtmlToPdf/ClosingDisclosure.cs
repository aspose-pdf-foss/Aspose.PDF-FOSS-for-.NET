using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// The Closing Disclosure ADDENDUM export. Its print stylesheet fixes the sheet:
// `@page { height: 1170px }` sets the body box's height outright, the page keeps
// the converter's default 90/72 margins, and `#maincontent { width: 99%;
// margin: auto }` centres everything inside, which is why the flow opens 0.5%
// of the sheet in from the left margin rather than on it.
//
// Everything below that is table work. The two cost grids lay out on
// `table-layout: fixed` over a 50% description column and five 10% money
// columns; a row paints its own background across its cells and hangs its
// bottom rule (black where the row declares `border-bottom`, #c7c8c8 where it
// declares `border-bottom-light`), while each cell carrying `rightbordercol`
// drops a black rule down its right edge and `border-right-light` a grey one.
// The summaries pair two half-width column stacks, and the payoff and contact
// grids follow the same row/rule model on their own column sets.
internal static partial class HtmlToPdfConverter
{
    private const double CdPxPt = 0.75;             // 96 dpi: one CSS pixel
    private const double CdMarginXPt = 90.0;        // the sheet's own default margins…
    private const double CdMarginYPt = 72.0;        // …which this export never overrides
    private const double CdSheetHeightPt = 877.5;   // @page { height: 1170px }
    private const double CdGutterFrac = 0.005;      // #maincontent { width: 99% }, centred
    private const double CdSheetPadPt = 1.5;        // .pageContent's 2px side padding
    private const double CdCellPadLeftPt = 7.5;     // .padding-left12 { padding-left: 10px }
    private const double CdSummaryPadPt = 4.5;      // the summaries frame's 6px padding
    private const double CdSummaryGapPt = 9.0;      // the gap between its two halves
    private const double CdRulePt = 0.75;           // every rule is a 1px border
    private const double CdDescFrac = 0.5;          // the cost grids' description column
    private const double CdMoneyFrac = 0.1;         // …over five 10% money columns

    // Calibri and Arial place their baseline at three quarters of the em, which
    // is what every measured baseline below reduces to.
    private const double CdAscEm = 0.75;
    private const double CdArialAscEm = 0.9043;     // Arial's hhea ascent / upem

    // The form's own ladder, measured. This export is a fixed
    // blank form: the label rows pace at the 14px line box plus their 4px
    // paddings, the property row carries a second (empty) address line, and the
    // section headings open a block of their own.
    private const double CdTitleBasePt = 35.58;     // 20px bold "Addendum"
    private const double CdTitlePt = 15.0;
    private const double CdHeadingPt = 11.4;        // font-size: larger over 9.5pt
    private const double CdBodyPt = 9.5;            // body { font-size: 9.5pt }
    private const double CdBannerPt = 10.5;         // the dark section banners
    private const double CdGridPt = 9.0;            // .sec-header { font-size: 12px }
    private const double CdLabelRowPt = 16.5;       // a 14px line box in its 4px pads
    private const double CdWrapRowPt = 18.5;        // …and one carrying a wrapped value
    private const double CdClosingBasePt = 67.73;   // "Closing Information:" baseline
    private const double CdClosingRow0Pt = 81.23;   // its first label row
    private const double CdTransBasePt = 144.88;    // "Transaction Information:" baseline
    private const double CdTransRow0Pt = 158.38;    // "Borrower:"
    private const double CdPartyGapPt = 29.01;      // borrower block to seller block

    // Section origins on the sheet (the form is fixed, so these are its own).
    private const double CdLoanTopPt = 272.77;
    private const double CdOtherTopPt = 397.02;
    private const double CdSummaryHeadBasePt = 559.27;
    private const double CdSummaryTopPt = 562.81;
    private const double CdPayoffBannerPt = 759.10;
    private const double CdPayoffTopPt = 778.97;
    private const double CdContactBannerPt = 834.76;
    private const double CdContactTopPt = 854.64;

    // Cost-grid row heights: the 12px line box, plus the head row's 2px top pad
    // and the section rows' 2px cell padding.
    private const double CdHeadRowPt = 12.38;
    private const double CdSubRowPt = 10.88;
    private const double CdSectionRowPt = 12.75;
    private const double CdBlankRowPt = 12.29;
    private const double CdHeadTextDropPt = 8.74;   // baseline inside a section row
    private const double CdHeadCentreDropPt = 9.49; // …a centred head-row caption
    private const double CdSubCentreDropPt = 8.37;  // …and a centred sub-row caption

    // The banners: a 234px dark plate with its caption, and the note beside it.
    private const double CdBannerWidthPt = 175.5;
    private const double CdBannerHeightPt = 16.5;
    private const double CdBannerInsetPt = 1.5;
    private const double CdBannerTextXPt = 10.5;
    private const double CdBannerDropPt = 3.96;
    private const double CdBannerNoteXPt = 178.5;
    private const double CdBannerNoteDropPt = 3.24;
    private const double CdBannerRulePt = 17.25;

    private const double CdSummaryRowPt = 12.75;
    private const double CdSummaryAmountPt = 60.7;  // the amount column
    private const double CdSummaryTextDropPt = 8.87;
    private const double CdSumFirstHeadPt = 12.75;   // the stack's opening lettered head
    private const double CdSumLetterHeadPt = 12.38;  // a later one, under its own rule
    private const double CdSumPlainHeadPt = 13.87;   // an Adjustments head, unbanded
    private const double CdSumBlankPt = 12.29;       // the empty value row under a head
    private const double CdSumSectionGapPt = 3.0;    // a new lettered section's 4px margin
    private const double CdSumTightBlankPt = 0.75;   // the amount-less head's collapsed row
    private const double CdFloatRightPadPt = 9.63;   // the File No float's own trailing pads
    private const double CdPayoffAmountPt = 225.0;  // 300px
    private const double CdPayoffWidthPt = 721.88;
    private const double CdPayoffRowPt = 12.75;
    private const double CdContactLabelPt = 178.5;
    private const double CdContactColPt = 131.25;   // min-width: 175px
    private const int CdContactCols = 5;
    private const double CdContactHeadPt = 2.25;
    private const double CdContactRowPt = 11.25;

    private const int CdLayerCanvas = 0;
    private const int CdLayerFill = 1;
    private const int CdLayerRule = 2;
    private const int CdLayerText = 3;

    private static readonly Color CdWhite = Color.FromRgbBytes(0xFF, 0xFF, 0xFF);
    private static readonly Color CdBand = Color.FromRgbBytes(0xE8, 0xEB, 0xEC);
    private static readonly Color CdDark = Color.FromRgbBytes(0x23, 0x23, 0x23);
    private static readonly Color CdLight = Color.FromRgbBytes(0xC7, 0xC8, 0xC8);
    private static readonly Color CdBlack = Color.FromArgb(0, 0, 0);

    /// <summary>Render the Closing Disclosure addendum, or null when the
    /// document is not one.</summary>
    private static Document? TryRenderClosingDisclosure(string html)
    {
        var cd = new ClosingDisclosureState();
        cd.html = html;
        if (!cd.html.Contains("closingDisclosureFrm", StringComparison.OrdinalIgnoreCase)
            || !cd.html.Contains("AddendumTitle", StringComparison.OrdinalIgnoreCase)
            || !cd.html.Contains("tbl_LoanCostSection", StringComparison.OrdinalIgnoreCase))
            return null;
        cd.calibri = Text.SystemFontResolver.Resolve("Calibri")!;
        cd.calibriB = Text.SystemFontResolver.Resolve("Calibri-Bold")
            ?? Text.SystemFontResolver.Resolve("Calibri Bold")!;
        cd.arial = Text.SystemFontResolver.Resolve("Arial")!;
        cd.arialB = Text.SystemFontResolver.Resolve("Arial-Bold")
            ?? Text.SystemFontResolver.Resolve("Arial Bold")!;
        if (cd.calibri is null || cd.calibriB is null || cd.arial is null || cd.arialB is null) return null;

        cd.marginLeft = CdMarginXPt;
        cd.marginTop = CdMarginYPt;
        cd.marginBottom = CdMarginYPt;
        cd.contentW = CdMeasureSheetWidth();
        cd.pageWidth = cd.marginLeft + cd.contentW + CdMarginXPt;
        cd.pageHeight = cd.marginTop + CdSheetHeightPt + cd.marginBottom;
        cd.flowLeft = cd.marginLeft + cd.contentW * CdGutterFrac;
        cd.tableLeft = cd.flowLeft + CdSheetPadPt;
        cd.tableW = cd.contentW * (1 - 2 * CdGutterFrac) - 2 * CdSheetPadPt;

        RenderHeaderSections(cd);

        RenderCostAndSummary(cd);

        RenderPayoffAndContacts(cd);

        foreach (var g in cd.ops.GroupBy(o => o.Sheet))
        {
            var sb = new StringBuilder();
            foreach (var o in g.OrderBy(o => o.Layer).ThenBy(o => o.Seq))
                sb.Append(o.Text).Append('\n');
            cd.pages[g.Key].AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
        }
        return cd.doc;
    }

    /// <summary>The sheet the print stylesheet resolves to. The contact grid is
    /// the only block that cannot shrink - five columns at `min-width: 175px`
    /// beside their label column - and the sheet grows until it very nearly
    /// holds them, the residual being the grid's own overflow.</summary>
    private static double CdMeasureSheetWidth()
        => CdContactLabelPt + CdContactCols * CdContactColPt + 2 * CdSheetPadPt
           + CdSheetOverflowPt;

    // the contact grid overruns the resolved sheet by this much (measured)
    private const double CdSheetOverflowPt = 0.95;

    private static string CdFlat(string frag)
        => Regex.Replace(DecodeEntities(Regex.Replace(frag, "<[^>]+>", " ")), @"\s+", " ").Trim();

    /// <summary>The text of the element carrying <paramref name="id"/>.</summary>
    private static string CdText(string html, string id)
    {
        var m = Regex.Match(html, @"<div\b[^>]*id\s*=\s*[""']" + Regex.Escape(id)
            + @"[""'][^>]*>([\s\S]*?)</div\s*>", RegexOptions.IgnoreCase);
        return m.Success ? CdFlat(m.Groups[1].Value) : "";
    }

    /// <summary>The bold labels inside the element carrying <paramref name="id"/>.</summary>
    private static List<string> CdLabels(string html, string id)
    {
        var outp = new List<string>();
        var open = Regex.Match(html, @"<div\b[^>]*id\s*=\s*[""']" + Regex.Escape(id)
            + @"[""'][^>]*>", RegexOptions.IgnoreCase);
        if (!open.Success) return outp;
        var inner = VrDivAt(html, open.Index);
        foreach (Match b in Regex.Matches(inner, @"<b\b[^>]*>([^<]*)</b\s*>",
                     RegexOptions.IgnoreCase))
        {
            var t = CdFlat(b.Groups[1].Value);
            if (t.Length > 0) outp.Add(t);
        }
        return outp;
    }

    /// <summary>A table's rows as (row class, cells), each cell carrying its
    /// colspan, text and class.</summary>
    private static List<(string Cls, List<(int Span, string Text, string CellCls)> Cells)>
        CdRows(string html, string id)
    {
        var outp = new List<(string, List<(int, string, string)>)>();
        var t = Regex.Match(html, @"<table\b[^>]*id\s*=\s*[""']" + Regex.Escape(id)
            + @"[""'][^>]*>([\s\S]*?)</table\s*>", RegexOptions.IgnoreCase);
        if (!t.Success) return outp;
        foreach (Match r in Regex.Matches(t.Groups[1].Value, @"<tr\b([^>]*)>([\s\S]*?)</tr\s*>",
                     RegexOptions.IgnoreCase))
        {
            var rc = Regex.Match(r.Groups[1].Value, @"class\s*=\s*[""']([^""']*)",
                RegexOptions.IgnoreCase);
            var cells = new List<(int, string, string)>();
            foreach (Match c in Regex.Matches(r.Groups[2].Value, @"<td\b([^>]*)>([\s\S]*?)</td\s*>",
                         RegexOptions.IgnoreCase))
            {
                var span = Regex.Match(c.Groups[1].Value, @"colspan\s*=\s*[""']?(\d+)",
                    RegexOptions.IgnoreCase);
                var cc = Regex.Match(c.Groups[1].Value, @"class\s*=\s*[""']([^""']*)",
                    RegexOptions.IgnoreCase);
                cells.Add((span.Success ? int.Parse(span.Groups[1].Value) : 1,
                    CdFlat(c.Groups[2].Value), cc.Success ? cc.Groups[1].Value : ""));
            }
            outp.Add((rc.Success ? rc.Groups[1].Value : "", cells));
        }
        return outp;
    }

    /// <summary>The summaries' section headings, borrower side or seller side:
    /// each sub-table's heading row, its `sub02` class marking a lettered
    /// section and its cell count telling whether it keeps an amount column.
    /// The transaction banner opening the K and M tables is not a heading - it
    /// sits above the stack's first rule.</summary>
    private static List<(string Text, bool Lettered, bool Amount)> CdSummaryHeads(
        string html, bool borrower)
    {
        var ids = borrower
            ? new[] { "tbl_SectionK", "tbl_SectionK1", "tbl_SectionK2", "tbl_SectionL",
                      "tbl_SectionL1", "tbl_SectionL2", "tbl_SectionL3" }
            : new[] { "tbl_SectionM", "tbl_SectionM1", "tbl_SectionN", "tbl_SectionN1" };
        var outp = new List<(string, bool, bool)>();
        foreach (var id in ids)
            foreach (var (_, cells) in CdRows(html, id))
            {
                if (cells.Count == 0 || cells[0].Text.Length == 0) continue;
                if (cells[0].CellCls.Contains("font-sec-headre",
                        StringComparison.OrdinalIgnoreCase)) continue;
                outp.Add((cells[0].Text,
                    cells[0].CellCls.Contains("sub02", StringComparison.OrdinalIgnoreCase),
                    cells.Count > 1));
                break;
            }
        return outp;
    }

    /// <summary>The document's `font-size: larger` block headings, in order.</summary>
    private static List<string> CdHeadings(string html)
    {
        var outp = new List<string>();
        foreach (Match m in Regex.Matches(html,
                     @"<b\b[^>]*font-size\s*:\s*larger[^>]*>([^<]*)</b\s*>",
                     RegexOptions.IgnoreCase))
        {
            var t = CdFlat(m.Groups[1].Value);
            if (t.Length > 0) outp.Add(t);
        }
        return outp;
    }

    /// <summary>The contact grid's row labels, down its first column.</summary>
    private static List<string> CdContactLabels(string html)
    {
        var outp = new List<string>();
        foreach (var (_, cells) in CdRows(html, "tbl_ContactInformation"))
        {
            if (cells.Count == 0) continue;
            var t = cells[0].Text;
            if (t.Length == 0 || t.Contains("Contact Information", StringComparison.Ordinal))
                continue;
            outp.Add(t);
        }
        return outp;
    }
}
