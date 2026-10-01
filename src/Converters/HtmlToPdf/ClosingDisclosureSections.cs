using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Closing disclosure sections: header, cost grids with summaries, payoff and contacts.</summary>
    private static void RenderPayoffAndContacts(ClosingDisclosureState cd)
    {
        Banner(cd, CdPayoffBannerPt, "Payoffs and Payments",
            "Use this table to see a summary of your payoffs and payments to others");
        cd.payoffSplit = cd.tableLeft + CdPayoffWidthPt - CdPayoffAmountPt;
        HRule(cd, 0, cd.tableLeft, cd.tableLeft + CdPayoffWidthPt, CdPayoffTopPt, CdBlack);
        Fill(cd, 0, cd.tableLeft, CdPayoffTopPt, cd.payoffSplit - cd.tableLeft, CdPayoffRowPt, CdBand);
        Fill(cd, 0, cd.payoffSplit, CdPayoffTopPt, CdPayoffAmountPt, CdPayoffRowPt, CdBand);
        Run(cd, 0, cd.tableLeft + CdCellPadLeftPt, CdPayoffTopPt + CdHeadTextDropPt, CdGridPt,
            cd.arialB, "ArialBold", "TO", CdBlack);
        Run(cd, 0, cd.payoffSplit + CdRulePt / 2, CdPayoffTopPt + CdHeadCentreDropPt, CdGridPt,
            cd.arialB, "ArialBold", "AMOUNT", CdBlack);
        VRule(cd, 0, cd.payoffSplit, CdPayoffTopPt - CdRulePt / 2,
            CdPayoffTopPt + 2 * CdPayoffRowPt + CdRulePt / 2, CdBlack);
        HRule(cd, 0, cd.tableLeft, cd.tableLeft + CdPayoffWidthPt, CdPayoffTopPt + CdPayoffRowPt,
            CdLight);
        HRule(cd, 0, cd.tableLeft, cd.tableLeft + CdPayoffWidthPt,
            CdPayoffTopPt + 2 * CdPayoffRowPt - CdRulePt / 2 + CdRulePt / 2, CdLight);

        Banner(cd, CdContactBannerPt, "Contact Information",
            "Contacts that could not fit are shown in full here.");
        cd.contactW = CdContactLabelPt + CdContactCols * CdContactColPt;
        cd.contactRight = cd.tableLeft + cd.contactW + CdRulePt / 2;
        HRule(cd, 0, cd.tableLeft, cd.contactRight, CdContactTopPt, CdBlack);
        for (var ci = 0; ci <= CdContactCols; ci++)
            Fill(cd, 0, cd.tableLeft + (ci == 0 ? 0 : CdContactLabelPt + (ci - 1) * CdContactColPt),
                CdContactTopPt, ci == 0 ? CdContactLabelPt : CdContactColPt,
                CdContactHeadPt, CdBand);
        HRule(cd, 0, cd.tableLeft, cd.contactRight, CdContactTopPt + CdContactHeadPt, CdLight);
        cd.labels = CdContactLabels(cd.body);
        cd.cy = CdContactTopPt + CdContactHeadPt;
        cd.sheet = 0;
        foreach (var lab in cd.labels)
        {
            if (cd.cy + CdContactRowPt > cd.marginTop + CdSheetHeightPt - CdRulePt)
            {
                cd.sheet++;
                cd.cy = cd.marginTop;
            }
            Run(cd, cd.sheet, cd.tableLeft, cd.cy + CdBodyPt * CdAscEm, CdBodyPt, cd.calibriB,
                "CalibriBold", lab, CdDark);
            cd.cy += CdContactRowPt;
            HRule(cd, cd.sheet, cd.tableLeft, cd.contactRight, cd.cy, CdLight);
        }
        for (var ci = 1; ci <= CdContactCols + 1; ci++)
            VRule(cd, 0, cd.tableLeft + CdContactLabelPt + (ci - 1) * CdContactColPt,
                CdContactTopPt - CdRulePt / 2, cd.marginTop + CdSheetHeightPt - 2.24, CdBlack);
    }

    /// <summary></summary>
    private static void RenderCostAndSummary(ClosingDisclosureState cd)
    {
        cd.colX = new double[7];
        cd.colX[0] = cd.tableLeft;
        cd.colX[1] = cd.tableLeft + cd.tableW * CdDescFrac;
        for (var i = 2; i <= 6; i++) cd.colX[i] = cd.colX[1] + (i - 1) * cd.tableW * CdMoneyFrac;

        CostGrid(cd, "tbl_LoanCostSection", CdLoanTopPt);
        CostGrid(cd, "tbl_OtherCostSection", CdOtherTopPt);

        cd.halfW = (cd.tableW - 2 * CdSummaryPadPt - CdSummaryGapPt) / 2;
        cd.leftX = cd.tableLeft + CdSummaryPadPt;
        cd.rightX = cd.leftX + cd.halfW + CdSummaryGapPt;
        Run(cd, 0, cd.leftX + CdBannerInsetPt, CdSummaryHeadBasePt, CdBannerPt, cd.calibriB,
            "CalibriBold", "BORROWER'S TRANSACTION", CdDark);
        Run(cd, 0, cd.rightX + CdBannerInsetPt, CdSummaryHeadBasePt, CdBannerPt, cd.calibriB,
            "CalibriBold", "SELLER'S TRANSACTION", CdDark);

        SummaryStack(cd, cd.leftX, CdSummaryHeads(cd.body, true), CdSummaryTopPt);
        SummaryStack(cd, cd.rightX, CdSummaryHeads(cd.body, false), CdSummaryTopPt);
    }

    /// <summary></summary>
    private static void RenderHeaderSections(ClosingDisclosureState cd)
    {
        cd.doc = new Document();
        cd.pages = new List<Page>();
        cd.ops = new List<(int Sheet, int Layer, int Seq, string Text)>();
        cd.seq = 0;
        cd.invc = System.Globalization.CultureInfo.InvariantCulture;

        cd.body = cd.html;
        Run(cd, 0, cd.flowLeft, CdTitleBasePt, CdTitlePt, cd.calibriB, "CalibriBold",
            CdText(cd.body, "AddendumTitle"), CdDark);

        cd.labelLeft = cd.flowLeft + 2 * CdSheetPadPt;
        cd.headings = CdHeadings(cd.body);
        Run(cd, 0, cd.flowLeft, CdClosingBasePt, CdHeadingPt, cd.calibriB, "CalibriBold",
            cd.headings.Count > 0 ? cd.headings[0] : "", CdDark);
        cd.floated = CdLabels(cd.body, "rightColumn");
        var closing = CdLabels(cd.body, "closingInfoSection")
            .Skip(1).Where(t => !cd.floated.Contains(t)).ToList();
        cd.y = CdClosingRow0Pt;
        foreach (var lab in closing)
        {
            Run(cd, 0, cd.labelLeft, cd.y, CdBodyPt, cd.calibriB, "CalibriBold", lab, CdDark);
            cd.y += CdWrapRowPt;
        }
        if (cd.floated.Count > 0)
        {
            var w = Measure(cd, cd.calibriB, "CalibriBold", cd.floated[0], CdBodyPt);
            Run(cd, 0, cd.tableLeft + cd.tableW - CdFloatRightPadPt - w, CdClosingRow0Pt, CdBodyPt,
                cd.calibriB, "CalibriBold", cd.floated[0], CdDark);
        }

        Run(cd, 0, cd.flowLeft, CdTransBasePt, CdHeadingPt, cd.calibriB, "CalibriBold",
            cd.headings.Count > 1 ? cd.headings[1] : "", CdDark);
        cd.y = CdTransRow0Pt;
        cd.parties = new[] { "Borrower:", "Seller:" };
        foreach (var party in cd.parties)
        {
            Run(cd, 0, cd.labelLeft, cd.y, CdBodyPt, cd.calibriB, "CalibriBold", party, CdDark);
            Run(cd, 0, cd.labelLeft, cd.y + CdLabelRowPt, CdBodyPt, cd.calibri, "Calibri",
                "Address:", CdDark);
            Run(cd, 0, cd.labelLeft, cd.y + 2 * CdLabelRowPt, CdBodyPt, cd.calibri, "Calibri",
                "City/ST/Zip:", CdDark);
            cd.y += CdPartyGapPt + 2 * CdLabelRowPt - CdLabelRowPt;
            cd.y = CdTransRow0Pt + CdPartyGapPt + 2 * CdLabelRowPt;
        }
    }
}
