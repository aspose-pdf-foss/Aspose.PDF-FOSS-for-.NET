using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// Closing disclosure drawing helpers: colours, page lookup, fills, rules, text runs, the cost grid, summary stack and banner.
    private static string Rgb(ClosingDisclosureState cd, Color c, string op)
        => Compat.Format(cd.invc,
            $"{c.R / 255.0:0.###} {c.G / 255.0:0.###} {c.B / 255.0:0.###} {op} ");

    private static Page PageAt(ClosingDisclosureState cd, int i)
    {
        while (cd.pages.Count <= i)
        {
            var p = cd.doc.Pages.Add(cd.pageWidth, cd.pageHeight);
            EnsureFonts(p);
            // the body plate: the @page box inside the sheet's own margins
            cd.ops.Add((cd.pages.Count, CdLayerCanvas, cd.seq++, Compat.Format(cd.invc,
                $"q {Rgb(cd, CdWhite, "rg")}{cd.marginLeft:0.##} {cd.marginBottom:0.##} "
                + $"{cd.contentW:0.##} {CdSheetHeightPt:0.##} re f Q")));
            cd.pages.Add(p);
        }
        return cd.pages[i];
    }

    private static void Fill(ClosingDisclosureState cd, int sheet, double x, double top, double w, double h, Color c)
    {
        if (w <= 0 || h <= 0) return;
        PageAt(cd, sheet);
        cd.ops.Add((sheet, CdLayerFill, cd.seq++, Compat.Format(cd.invc,
            $"q {Rgb(cd, c, "rg")}{x:0.##} {cd.pageHeight - top - h:0.##} "
            + $"{w:0.##} {h:0.##} re f Q")));
    }

    private static void HRule(ClosingDisclosureState cd, int sheet, double x0, double x1, double y, Color c)
    {
        PageAt(cd, sheet);
        cd.ops.Add((sheet, CdLayerRule, cd.seq++, Compat.Format(cd.invc,
            $"q {Rgb(cd, c, "RG")}{CdRulePt:0.##} w {x0:0.##} {cd.pageHeight - y:0.##} m "
            + $"{x1:0.##} {cd.pageHeight - y:0.##} l S Q")));
    }

    private static void VRule(ClosingDisclosureState cd, int sheet, double x, double y0, double y1, Color c)
    {
        PageAt(cd, sheet);
        cd.ops.Add((sheet, CdLayerRule, cd.seq++, Compat.Format(cd.invc,
            $"q {Rgb(cd, c, "RG")}{CdRulePt:0.##} w {x:0.##} {cd.pageHeight - y0:0.##} m "
            + $"{x:0.##} {cd.pageHeight - y1:0.##} l S Q")));
    }

    private static double Measure(ClosingDisclosureState cd, byte[] ttf, string name, string s, double size)
    {
        if (PageAt(cd, 0).Dict.Get("Resources") is not Core.PdfDictionary res
            || res.Get("Font") is not Core.PdfDictionary fd) return s.Length * size * 0.5;
        return Text.Type0FontEmbedder.MeasureText(fd, ttf, name, s, size,
            stripSpacesInBaseFont: true);
    }

    private static void Run(ClosingDisclosureState cd, int sheet, double x, double baseline, double size, byte[] ttf,
        string name, string s, Color c)
    {
        if (s.Length == 0) return;
        var pg = PageAt(cd, sheet);
        if (pg.Dict.Get("Resources") is not Core.PdfDictionary res
            || res.Get("Font") is not Core.PdfDictionary fd) return;
        var (rn, hex) = Text.Type0FontEmbedder.Embed(fd, ttf, name, s,
            stripSpacesInBaseFont: true);
        cd.ops.Add((sheet, CdLayerText, cd.seq++, Compat.Format(cd.invc,
            $"BT {Rgb(cd, c, "rg")}/{rn} {size:0.##} Tf 1 0 0 1 {x:0.##} "
            + $"{cd.pageHeight - baseline:0.##} Tm ")
            + "<" + Compat.ToHexString(hex) + "> Tj ET"));
    }

    private static void Centre(ClosingDisclosureState cd, int sheet, double x0, double x1, double baseline, double size,
        byte[] ttf, string name, string s, Color c)
        => Run(cd, cd.sheet, (x0 + x1 - Measure(cd, ttf, name, s, size)) / 2, baseline, size,
            ttf, name, s, c);

    private static void CostGrid(ClosingDisclosureState cd, string id, double top)
    {
        var rows = CdRows(cd.body, id);
        if (rows.Count == 0) return;
        HRule(cd, 0, cd.colX[0], cd.colX[6], top, CdBlack);
        var ry = top;
        for (var ri = 0; ri < rows.Count; ri++)
        {
            var (cls, cells) = rows[ri];
            var head = cls.Contains("sec-header", StringComparison.OrdinalIgnoreCase);
            var h = ri == 0 ? CdHeadRowPt
                : ri == 1 ? CdSubRowPt
                : head ? CdSectionRowPt : CdBlankRowPt;
            if (head) Fill(cd, 0, cd.colX[0], ry, cd.colX[6] - cd.colX[0], h, CdBand);

            var ci = 0;
            foreach (var (span, text, cellCls) in cells)
            {
                var x0 = cd.colX[ci];
                var x1 = cd.colX[Math.Min(6, ci + span)];
                if (text.Length > 0)
                {
                    var bold = head;
                    var drop = ci == 0 ? CdHeadTextDropPt
                        : ri == 0 ? CdHeadCentreDropPt : CdSubCentreDropPt;
                    if (ci == 0)
                        Run(cd, 0, x0 + CdCellPadLeftPt, ry + drop, CdGridPt,
                            bold ? cd.arialB : cd.arial, bold ? "ArialBold" : "Arial",
                            text, CdBlack);
                    else
                        Centre(cd, 0, x0, x1, ry + drop, CdGridPt,
                            bold ? cd.arialB : cd.arial, bold ? "ArialBold" : "Arial",
                            text, CdBlack);
                }
                if (cellCls.Contains("rightbordercol", StringComparison.OrdinalIgnoreCase))
                    VRule(cd, 0, x1, ry - CdRulePt / 2, ry + h + CdRulePt / 2, CdBlack);
                else if (cellCls.Contains("border-right-light", StringComparison.OrdinalIgnoreCase))
                    VRule(cd, 0, x1, ry - CdRulePt / 2, ry + h + CdRulePt / 2, CdLight);
                ci += span;
            }
            ry += h;
            if (cls.Contains("border-bottom-light", StringComparison.OrdinalIgnoreCase))
                HRule(cd, 0, cd.colX[0], cd.colX[6], ry, CdLight);
            else if (cls.Contains("border-bottom", StringComparison.OrdinalIgnoreCase))
                HRule(cd, 0, cd.colX[0], cd.colX[6], ry, CdBlack);
        }
    }

    // Each sub-table is one heading row over one empty value row. A heading
    // cell marked `sub02` is a lettered section: it takes the band, opens a
    // 4px margin above itself when it is not the stack's first, and keeps
    // the amount column unless it declares no amount cell at all.
    private static void SummaryStack(ClosingDisclosureState cd, double x,
        IReadOnlyList<(string Text, bool Lettered, bool Amount)> heads, double top)
    {
        var sy = top;
        var amountX = x + cd.halfW - CdSummaryAmountPt;
        var deepest = sy;
        HRule(cd, 0, x, x + cd.halfW, sy, CdBlack);
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
                    Fill(cd, 0, x, sy, cd.halfW - CdSummaryAmountPt, headH, CdBand);
                    Fill(cd, 0, amountX, sy, CdSummaryAmountPt, headH, CdBand);
                }
                else Fill(cd, 0, x, sy, cd.halfW, headH, CdBand);
            }
            Run(cd, 0, x + (lettered ? CdBannerInsetPt : 0), sy + CdSummaryTextDropPt,
                CdBodyPt, cd.calibriB, "CalibriBold", text, CdDark);
            sy += headH;
            HRule(cd, 0, x, x + cd.halfW, sy, CdLight);
            sy += lettered && !amount ? CdSumTightBlankPt : CdSumBlankPt;
            HRule(cd, 0, x, x + cd.halfW, sy, CdLight);
            deepest = sy;
        }
        VRule(cd, 0, amountX, top + CdRulePt / 2, deepest, CdBlack);
    }

    // ── the payoff and contact plates ───────────────────────────────────
    private static void Banner(ClosingDisclosureState cd, double top, string caption, string note)
    {
        Fill(cd, 0, cd.tableLeft + CdBannerInsetPt, top, CdBannerWidthPt, CdBannerHeightPt,
            CdDark);
        Run(cd, 0, cd.tableLeft + CdBannerTextXPt, top + CdBannerDropPt + CdBannerPt * CdAscEm,
            CdBannerPt, cd.calibriB, "CalibriBold", caption, CdWhite);
        Run(cd, 0, cd.tableLeft + CdBannerNoteXPt,
            top + CdBannerNoteDropPt + CdBodyPt * CdAscEm, CdBodyPt, cd.calibriB,
            "CalibriBold", note, CdDark);
        HRule(cd, 0, cd.tableLeft + CdBannerInsetPt, cd.tableLeft + CdBannerInsetPt
            + CdBannerWidthPt, top + CdBannerRulePt, CdDark);
    }
}
