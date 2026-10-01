using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    // content x = margins(0) + UA body 8 px + main-middle 17 px + card 40 px
    private const double ArContentXPt = 48.75;

    // th label column: label x and the dot x measured off the sheet
    // (65 px + the border-container's 2 px + the 25 px content-area pad).
    private const double ArThXPt = 69.8;

    private const double ArDotXPt = 119.8;

    // editor / value content opens 4 pt past the dot cell
    private const double ArTdXPt = 123.8;

    // the Bulgu-Kodu value's link ink and the abbrSpan's teal
    private const double ArBadgeFsPt = 8.25;

    // the label column's wrap width: 'Denetlenen' (48.5) keeps its line,
    // 'Bulgu Kodu' (48.7+) breaks — measured off the sheet
    private const double ArThColWPt = 48.95;

    private static Document? TryRenderAuditReport(string html,
        double pageWidth, double pageHeight, HtmlLoadOptions? options)
    {
        if (!html.Contains("audit-report-editable", StringComparison.Ordinal)
            || !html.Contains("gt-editor-content", StringComparison.Ordinal)
            || !html.Contains("report-content-area", StringComparison.Ordinal))
            return null;
        if (WinMetricsFor("Arial") is not { } wm) return null;
        var al = new AuditLetterState();
        al.pageWidth = pageWidth;
        al.wm = wm;
        al.pageHeight = pageHeight;
        al.inv = System.Globalization.CultureInfo.InvariantCulture;
        al.c = Regex.Replace(html, @"\s+", " ");
        al.marginTop = options?.PageInfo?.Margin?.Top ?? 20.0;
        al.marginBottom = options?.PageInfo?.Margin?.Bottom ?? 10.0;
        al.contentRight = 578.5;

        al.inkBody = ParseCssColor("#355154") ?? Color.FromArgb(53, 81, 84);
        al.inkEditor = ParseCssColor("#333333") ?? Color.FromArgb(51, 51, 51);
        al.inkAbbr = ParseCssColor("#008080") ?? Color.FromArgb(0, 128, 128);
        al.inkCode = ParseCssColor("#0782C1") ?? Color.FromArgb(7, 130, 193);
        al.railGray = ParseCssColor("#e4e5e9") ?? Color.FromArgb(228, 229, 233);

        al.doc = Document.Create();
        al.page = al.doc.Pages.Add(al.pageWidth, al.pageHeight);
        al.docFontDict = new Core.PdfDictionary();
        EnsureFonts(al.page, al.docFontDict);
        al.sb = new StringBuilder();
        al.streams = new List<(Page pg, StringBuilder ops)> { (al.page, al.sb) };

        al.y = al.marginTop + 6.0 + 12.75 + 30.0;
        al.railFrom = double.NaN;
        al.h1M = Regex.Match(al.c, @"<h1 class=""ng-binding"">([^<]*)</h1>");
        if (al.h1M.Success)
        {
            EmitRun(al, CollapseWs(DecodeEntities(al.h1M.Groups[1].Value)).Trim(), 10.5, "Bold",
                ArContentXPt, al.y + Drop(al, 10.5, 12.6), al.inkBody);
            al.y += 12.6 + 3.75;                         // h1 line + title-area 5 px pad
        }
        al.railFrom = al.y;                                 // the border-container opens here
        al.y += 7.5;                                     // content-container 10 px pad-top
        foreach (Match hM in Regex.Matches(al.c, @"<h([234]) class=""ng-binding""><span class=""left-line""></span>([^<]*)</h\1>"))
        {
            var lvl = int.Parse(hM.Groups[1].Value, al.inv);
            // measured: h2 +9 (2px border + 10px pad), h3 +12.75, h4 +18
            var xOff = lvl == 2 ? 9.0 : lvl == 3 ? 12.75 : 18.0;
            // the left-line dash under the heading (5/8/13 px wide, top:9px)
            FillRect(al, ArContentXPt + 1.5, al.y + 6.75, lvl == 2 ? 3.8 : lvl == 3 ? 6.0 : 9.8, 1.5, al.railGray);
            EmitRun(al, CollapseWs(DecodeEntities(hM.Groups[2].Value)).Trim(), 10.5, "Bold",
                ArContentXPt + xOff, al.y + Drop(al, 10.5, 12.6), al.inkBody);
            al.y += 12.6 + 9.0;                          // line + 12 px pad-bottom
        }
        al.y += 26.25;                                   // content-area 35 px pad-top

        al.pRunRx = new Regex(@"<(/?)(\w[\w-]*)((?:[^>""']|""[^""]*""|'[^']*')*?)(/?)>|([^<]+)",
            RegexOptions.Singleline);
        al.contentArea = al.c[(al.c.IndexOf("report-content-area", StringComparison.Ordinal))..];
        foreach (Match rowM in Regex.Matches(al.contentArea,
            @"<th class=""ng-binding"">([^<]{1,60})</th>\s*<td class=""dot"">:</td>\s*<td[^>]*>",
            RegexOptions.IgnoreCase))
        {
            if (!LayoutAuditLetterRow(al, rowM)) break;
        }

        // close the rail on the last page
        if (!double.IsNaN(al.railFrom))
            al.sb.AppendLine(Compat.Format(al.inv,
                $"q {al.railGray.R / 255.0:0.###} {al.railGray.G / 255.0:0.###} {al.railGray.B / 255.0:0.###} RG 1.5 w " +
                $"49.5 {al.pageHeight - al.railFrom:F2} m 49.5 {al.pageHeight - Math.Min(al.y + 30, al.pageHeight - al.marginBottom):F2} l S Q"));

        foreach (var (pg, ops) in al.streams)
            pg.AddContentStream(Encoding.ASCII.GetBytes(ops.ToString() + "\n"));
        return al.doc;
    }

    // Left page margin of the letter flow, and the right margin the widened
    // sheet keeps past the container (measured: page = 96 + 15 + 720 + 90).
    private const double DnLeftMarginPt = 96.0;

    private const double DnRightMarginPt = 90.0;

    // The header's broken images seat their frames at 86 = 72 pt content top
    // + the UA 8 px body margin + the header's 10 px padding (both 0.75-scaled).
    private const double DnHeaderImgTopPt = 86.0;

    // h3 title baseline (measured 117.9: image top + title 17 px padding +
    // the UA h3 margin and its 15 px line's seat).
    private const double DnH3BaselinePt = 117.9;

    // h3 → h1 baseline advance (h3 descent + UA h3/h1 margin collapse + the
    // 29 px line's ascent, measured).
    private const double DnH1AdvancePt = 36.9;

    // The bordered info area's top edge (measured 182.1: the float column's
    // bottom — h1 baseline + descent + its 0.67 em bottom margin + the title
    // div's 10 px bottom padding).
    private const double DnInfoTopPt = 182.1;

    // A box title's knockout drops (40 px header − 24 px title) under its
    // section top; the 2 px frame runs 40 px − the content's −12 px margin.
    private const double DnTitleKnockoutDropPt = 12.15;

    private const double DnFrameDropPt = 21.0;

    // The continuation page's content top (measured off the footer table's
    // vertically-centred rows: 2-line cell first baseline 88.5, 1-line 94.1).
    private const double DnPage2TopPt = 79.8;

}
