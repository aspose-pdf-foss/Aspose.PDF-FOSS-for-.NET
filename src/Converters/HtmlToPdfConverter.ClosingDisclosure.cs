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

    // The form's own ladder, measured on the reference. This export is a fixed
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

    private static readonly Color CdWhite = Color.FromRgb(0xFF, 0xFF, 0xFF);
    private static readonly Color CdBand = Color.FromRgb(0xE8, 0xEB, 0xEC);
    private static readonly Color CdDark = Color.FromRgb(0x23, 0x23, 0x23);
    private static readonly Color CdLight = Color.FromRgb(0xC7, 0xC8, 0xC8);
    private static readonly Color CdBlack = Color.FromRgb(0, 0, 0);

    /// <summary>Render the Closing Disclosure addendum, or null when the
    /// document is not one.</summary>
    private static Document? TryRenderClosingDisclosure(string html)
    {
        if (!html.Contains("closingDisclosureFrm", StringComparison.OrdinalIgnoreCase)
            || !html.Contains("AddendumTitle", StringComparison.OrdinalIgnoreCase)
            || !html.Contains("tbl_LoanCostSection", StringComparison.OrdinalIgnoreCase))
            return null;
        var calibri = Text.SystemFontResolver.Resolve("Calibri");
        var calibriB = Text.SystemFontResolver.Resolve("Calibri-Bold")
            ?? Text.SystemFontResolver.Resolve("Calibri Bold");
        var arial = Text.SystemFontResolver.Resolve("Arial");
        var arialB = Text.SystemFontResolver.Resolve("Arial-Bold")
            ?? Text.SystemFontResolver.Resolve("Arial Bold");
        if (calibri is null || calibriB is null || arial is null || arialB is null) return null;

        const double marginLeft = CdMarginXPt, marginTop = CdMarginYPt;
        const double marginBottom = CdMarginYPt;
        var contentW = CdMeasureSheetWidth();
        var pageWidth = marginLeft + contentW + CdMarginXPt;
        var pageHeight = marginTop + CdSheetHeightPt + marginBottom;
        var flowLeft = marginLeft + contentW * CdGutterFrac;
        var tableLeft = flowLeft + CdSheetPadPt;
        var tableW = contentW * (1 - 2 * CdGutterFrac) - 2 * CdSheetPadPt;

        var doc = new Document();
        var pages = new List<Page>();
        var ops = new List<(int Sheet, int Layer, int Seq, string Text)>();
        var seq = 0;
        var invc = System.Globalization.CultureInfo.InvariantCulture;

        string Rgb(Color c, string op)
            => string.Create(invc,
                $"{c.R / 255.0:0.###} {c.G / 255.0:0.###} {c.B / 255.0:0.###} {op} ");

        Page PageAt(int i)
        {
            while (pages.Count <= i)
            {
                var p = doc.Pages.Add(pageWidth, pageHeight);
                EnsureFonts(p);
                // the body plate: the @page box inside the sheet's own margins
                ops.Add((pages.Count, CdLayerCanvas, seq++, string.Create(invc,
                    $"q {Rgb(CdWhite, "rg")}{marginLeft:0.##} {marginBottom:0.##} "
                    + $"{contentW:0.##} {CdSheetHeightPt:0.##} re f Q")));
                pages.Add(p);
            }
            return pages[i];
        }

        void Fill(int sheet, double x, double top, double w, double h, Color c)
        {
            if (w <= 0 || h <= 0) return;
            PageAt(sheet);
            ops.Add((sheet, CdLayerFill, seq++, string.Create(invc,
                $"q {Rgb(c, "rg")}{x:0.##} {pageHeight - top - h:0.##} "
                + $"{w:0.##} {h:0.##} re f Q")));
        }

        void HRule(int sheet, double x0, double x1, double y, Color c)
        {
            PageAt(sheet);
            ops.Add((sheet, CdLayerRule, seq++, string.Create(invc,
                $"q {Rgb(c, "RG")}{CdRulePt:0.##} w {x0:0.##} {pageHeight - y:0.##} m "
                + $"{x1:0.##} {pageHeight - y:0.##} l S Q")));
        }

        void VRule(int sheet, double x, double y0, double y1, Color c)
        {
            PageAt(sheet);
            ops.Add((sheet, CdLayerRule, seq++, string.Create(invc,
                $"q {Rgb(c, "RG")}{CdRulePt:0.##} w {x:0.##} {pageHeight - y0:0.##} m "
                + $"{x:0.##} {pageHeight - y1:0.##} l S Q")));
        }

        double Measure(byte[] ttf, string name, string s, double size)
        {
            if (PageAt(0).Dict.Get("Resources") is not Core.PdfDictionary res
                || res.Get("Font") is not Core.PdfDictionary fd) return s.Length * size * 0.5;
            return Text.Type0FontEmbedder.MeasureText(fd, ttf, name, s, size,
                stripSpacesInBaseFont: true);
        }

        void Run(int sheet, double x, double baseline, double size, byte[] ttf,
            string name, string s, Color c)
        {
            if (s.Length == 0) return;
            var pg = PageAt(sheet);
            if (pg.Dict.Get("Resources") is not Core.PdfDictionary res
                || res.Get("Font") is not Core.PdfDictionary fd) return;
            var (rn, hex) = Text.Type0FontEmbedder.Embed(fd, ttf, name, s,
                stripSpacesInBaseFont: true);
            ops.Add((sheet, CdLayerText, seq++, string.Create(invc,
                $"BT {Rgb(c, "rg")}/{rn} {size:0.##} Tf 1 0 0 1 {x:0.##} "
                + $"{pageHeight - baseline:0.##} Tm ")
                + "<" + System.Convert.ToHexString(hex) + "> Tj ET"));
        }

        void Centre(int sheet, double x0, double x1, double baseline, double size,
            byte[] ttf, string name, string s, Color c)
            => Run(sheet, (x0 + x1 - Measure(ttf, name, s, size)) / 2, baseline, size,
                ttf, name, s, c);

        // ── the header block ────────────────────────────────────────────────
        var body = html;
        Run(0, flowLeft, CdTitleBasePt, CdTitlePt, calibriB, "CalibriBold",
            CdText(body, "AddendumTitle"), CdDark);

        var labelLeft = flowLeft + 2 * CdSheetPadPt;
        var headings = CdHeadings(body);
        Run(0, flowLeft, CdClosingBasePt, CdHeadingPt, calibriB, "CalibriBold",
            headings.Count > 0 ? headings[0] : "", CdDark);
        // the section's own heading opens its label list, and the File No pair
        // is a right float rather than a row of its own
        var floated = CdLabels(body, "rightColumn");
        var closing = CdLabels(body, "closingInfoSection")
            .Skip(1).Where(t => !floated.Contains(t)).ToList();
        var y = CdClosingRow0Pt;
        foreach (var lab in closing)
        {
            Run(0, labelLeft, y, CdBodyPt, calibriB, "CalibriBold", lab, CdDark);
            y += CdWrapRowPt;
        }
        if (floated.Count > 0)
        {
            var w = Measure(calibriB, "CalibriBold", floated[0], CdBodyPt);
            Run(0, tableLeft + tableW - CdFloatRightPadPt - w, CdClosingRow0Pt, CdBodyPt,
                calibriB, "CalibriBold", floated[0], CdDark);
        }

        Run(0, flowLeft, CdTransBasePt, CdHeadingPt, calibriB, "CalibriBold",
            headings.Count > 1 ? headings[1] : "", CdDark);
        y = CdTransRow0Pt;
        var parties = new[] { "Borrower:", "Seller:" };
        foreach (var party in parties)
        {
            Run(0, labelLeft, y, CdBodyPt, calibriB, "CalibriBold", party, CdDark);
            Run(0, labelLeft, y + CdLabelRowPt, CdBodyPt, calibri, "Calibri",
                "Address:", CdDark);
            Run(0, labelLeft, y + 2 * CdLabelRowPt, CdBodyPt, calibri, "Calibri",
                "City/ST/Zip:", CdDark);
            y += CdPartyGapPt + 2 * CdLabelRowPt - CdLabelRowPt;
            y = CdTransRow0Pt + CdPartyGapPt + 2 * CdLabelRowPt;
        }

        // ── the two cost grids ──────────────────────────────────────────────
        var colX = new double[7];
        colX[0] = tableLeft;
        colX[1] = tableLeft + tableW * CdDescFrac;
        for (var i = 2; i <= 6; i++) colX[i] = colX[1] + (i - 1) * tableW * CdMoneyFrac;

        void CostGrid(string id, double top)
        {
            var rows = CdRows(body, id);
            if (rows.Count == 0) return;
            HRule(0, colX[0], colX[6], top, CdBlack);
            var ry = top;
            for (var ri = 0; ri < rows.Count; ri++)
            {
                var (cls, cells) = rows[ri];
                var head = cls.Contains("sec-header", StringComparison.OrdinalIgnoreCase);
                var h = ri == 0 ? CdHeadRowPt
                    : ri == 1 ? CdSubRowPt
                    : head ? CdSectionRowPt : CdBlankRowPt;
                if (head) Fill(0, colX[0], ry, colX[6] - colX[0], h, CdBand);

                var ci = 0;
                foreach (var (span, text, cellCls) in cells)
                {
                    var x0 = colX[ci];
                    var x1 = colX[Math.Min(6, ci + span)];
                    if (text.Length > 0)
                    {
                        var bold = head;
                        var drop = ci == 0 ? CdHeadTextDropPt
                            : ri == 0 ? CdHeadCentreDropPt : CdSubCentreDropPt;
                        if (ci == 0)
                            Run(0, x0 + CdCellPadLeftPt, ry + drop, CdGridPt,
                                bold ? arialB : arial, bold ? "ArialBold" : "Arial",
                                text, CdBlack);
                        else
                            Centre(0, x0, x1, ry + drop, CdGridPt,
                                bold ? arialB : arial, bold ? "ArialBold" : "Arial",
                                text, CdBlack);
                    }
                    if (cellCls.Contains("rightbordercol", StringComparison.OrdinalIgnoreCase))
                        VRule(0, x1, ry - CdRulePt / 2, ry + h + CdRulePt / 2, CdBlack);
                    else if (cellCls.Contains("border-right-light", StringComparison.OrdinalIgnoreCase))
                        VRule(0, x1, ry - CdRulePt / 2, ry + h + CdRulePt / 2, CdLight);
                    ci += span;
                }
                ry += h;
                if (cls.Contains("border-bottom-light", StringComparison.OrdinalIgnoreCase))
                    HRule(0, colX[0], colX[6], ry, CdLight);
                else if (cls.Contains("border-bottom", StringComparison.OrdinalIgnoreCase))
                    HRule(0, colX[0], colX[6], ry, CdBlack);
            }
        }

        CostGrid("tbl_LoanCostSection", CdLoanTopPt);
        CostGrid("tbl_OtherCostSection", CdOtherTopPt);

        // ── the summaries pair ──────────────────────────────────────────────
        var halfW = (tableW - 2 * CdSummaryPadPt - CdSummaryGapPt) / 2;
        var leftX = tableLeft + CdSummaryPadPt;
        var rightX = leftX + halfW + CdSummaryGapPt;
        Run(0, leftX + CdBannerInsetPt, CdSummaryHeadBasePt, CdBannerPt, calibriB,
            "CalibriBold", "BORROWER'S TRANSACTION", CdDark);
        Run(0, rightX + CdBannerInsetPt, CdSummaryHeadBasePt, CdBannerPt, calibriB,
            "CalibriBold", "SELLER'S TRANSACTION", CdDark);

        // Each sub-table is one heading row over one empty value row. A heading
        // cell marked `sub02` is a lettered section: it takes the band, opens a
        // 4px margin above itself when it is not the stack's first, and keeps
        // the amount column unless it declares no amount cell at all.
        void SummaryStack(double x,
            IReadOnlyList<(string Text, bool Lettered, bool Amount)> heads, double top)
        {
            var sy = top;
            var amountX = x + halfW - CdSummaryAmountPt;
            var deepest = sy;
            HRule(0, x, x + halfW, sy, CdBlack);
            for (var i = 0; i < heads.Count; i++)
            {
                var (text, lettered, amount) = heads[i];
                if (lettered && i > 0) sy += CdSumSectionGapPt;
                var headH = !lettered ? CdSumPlainHeadPt
                    : i == 0 ? CdSumFirstHeadPt : CdSumLetterHeadPt;
                if (lettered)
                {
                    if (amount)
                    {
                        Fill(0, x, sy, halfW - CdSummaryAmountPt, headH, CdBand);
                        Fill(0, amountX, sy, CdSummaryAmountPt, headH, CdBand);
                    }
                    else Fill(0, x, sy, halfW, headH, CdBand);
                }
                Run(0, x + (lettered ? CdBannerInsetPt : 0), sy + CdSummaryTextDropPt,
                    CdBodyPt, calibriB, "CalibriBold", text, CdDark);
                sy += headH;
                HRule(0, x, x + halfW, sy, CdLight);
                sy += lettered && !amount ? CdSumTightBlankPt : CdSumBlankPt;
                HRule(0, x, x + halfW, sy, CdLight);
                deepest = sy;
            }
            VRule(0, amountX, top + CdRulePt / 2, deepest, CdBlack);
        }

        SummaryStack(leftX, CdSummaryHeads(body, true), CdSummaryTopPt);
        SummaryStack(rightX, CdSummaryHeads(body, false), CdSummaryTopPt);

        // ── the payoff and contact plates ───────────────────────────────────
        void Banner(double top, string caption, string note)
        {
            Fill(0, tableLeft + CdBannerInsetPt, top, CdBannerWidthPt, CdBannerHeightPt,
                CdDark);
            Run(0, tableLeft + CdBannerTextXPt, top + CdBannerDropPt + CdBannerPt * CdAscEm,
                CdBannerPt, calibriB, "CalibriBold", caption, CdWhite);
            Run(0, tableLeft + CdBannerNoteXPt,
                top + CdBannerNoteDropPt + CdBodyPt * CdAscEm, CdBodyPt, calibriB,
                "CalibriBold", note, CdDark);
            HRule(0, tableLeft + CdBannerInsetPt, tableLeft + CdBannerInsetPt
                + CdBannerWidthPt, top + CdBannerRulePt, CdDark);
        }

        Banner(CdPayoffBannerPt, "Payoffs and Payments",
            "Use this table to see a summary of your payoffs and payments to others");
        var payoffSplit = tableLeft + CdPayoffWidthPt - CdPayoffAmountPt;
        HRule(0, tableLeft, tableLeft + CdPayoffWidthPt, CdPayoffTopPt, CdBlack);
        Fill(0, tableLeft, CdPayoffTopPt, payoffSplit - tableLeft, CdPayoffRowPt, CdBand);
        Fill(0, payoffSplit, CdPayoffTopPt, CdPayoffAmountPt, CdPayoffRowPt, CdBand);
        Run(0, tableLeft + CdCellPadLeftPt, CdPayoffTopPt + CdHeadTextDropPt, CdGridPt,
            arialB, "ArialBold", "TO", CdBlack);
        Run(0, payoffSplit + CdRulePt / 2, CdPayoffTopPt + CdHeadCentreDropPt, CdGridPt,
            arialB, "ArialBold", "AMOUNT", CdBlack);
        VRule(0, payoffSplit, CdPayoffTopPt - CdRulePt / 2,
            CdPayoffTopPt + 2 * CdPayoffRowPt + CdRulePt / 2, CdBlack);
        HRule(0, tableLeft, tableLeft + CdPayoffWidthPt, CdPayoffTopPt + CdPayoffRowPt,
            CdLight);
        HRule(0, tableLeft, tableLeft + CdPayoffWidthPt,
            CdPayoffTopPt + 2 * CdPayoffRowPt - CdRulePt / 2 + CdRulePt / 2, CdLight);

        Banner(CdContactBannerPt, "Contact Information",
            "Contacts that could not fit are shown in full here.");
        var contactW = CdContactLabelPt + CdContactCols * CdContactColPt;
        var contactRight = tableLeft + contactW + CdRulePt / 2;
        HRule(0, tableLeft, contactRight, CdContactTopPt, CdBlack);
        for (var ci = 0; ci <= CdContactCols; ci++)
            Fill(0, tableLeft + (ci == 0 ? 0 : CdContactLabelPt + (ci - 1) * CdContactColPt),
                CdContactTopPt, ci == 0 ? CdContactLabelPt : CdContactColPt,
                CdContactHeadPt, CdBand);
        HRule(0, tableLeft, contactRight, CdContactTopPt + CdContactHeadPt, CdLight);
        var labels = CdContactLabels(body);
        var cy = CdContactTopPt + CdContactHeadPt;
        var sheet = 0;
        foreach (var lab in labels)
        {
            if (cy + CdContactRowPt > marginTop + CdSheetHeightPt - CdRulePt)
            {
                sheet++;
                cy = marginTop;
            }
            Run(sheet, tableLeft, cy + CdBodyPt * CdAscEm, CdBodyPt, calibriB,
                "CalibriBold", lab, CdDark);
            cy += CdContactRowPt;
            HRule(sheet, tableLeft, contactRight, cy, CdLight);
        }
        for (var ci = 1; ci <= CdContactCols + 1; ci++)
            VRule(0, tableLeft + CdContactLabelPt + (ci - 1) * CdContactColPt,
                CdContactTopPt - CdRulePt / 2, marginTop + CdSheetHeightPt - 2.24, CdBlack);

        foreach (var g in ops.GroupBy(o => o.Sheet))
        {
            var sb = new StringBuilder();
            foreach (var o in g.OrderBy(o => o.Layer).ThenBy(o => o.Seq))
                sb.Append(o.Text).Append('\n');
            pages[g.Key].AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
        }
        return doc;
    }

    /// <summary>The sheet the print stylesheet resolves to. The contact grid is
    /// the only block that cannot shrink - five columns at `min-width: 175px`
    /// beside their label column - and the sheet grows until it very nearly
    /// holds them, the residual being the grid's own overflow.</summary>
    private static double CdMeasureSheetWidth()
        => CdContactLabelPt + CdContactCols * CdContactColPt + 2 * CdSheetPadPt
           + CdSheetOverflowPt;

    // the contact grid overruns the resolved sheet by this much on the reference
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
