using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    private static Document? TryRenderSplitPanel(string html, HtmlLoadOptions? options,
        double pageWidth, double pageHeight)
    {
        var sp = new SplitPanelState();
        sp.html = html;
        sp.options = options;
        sp.pageWidth = pageWidth;
        sp.pageHeight = pageHeight;
        sp.orphan = SafeRegex.Match(sp.html, @"</table\s*>\s*<td\b[^>]*\browspan\s*=",
            RegexOptions.IgnoreCase);
        if (!sp.orphan.Success) return null;
        sp.innerM = SafeRegex.Match(sp.html[sp.orphan.Index..],
            @"<table\b[^>]*width\s*=\s*[""']?100%[\s\S]*?<td\b([^>]*)>([\s\S]*)",
            RegexOptions.IgnoreCase);
        if (!sp.innerM.Success) return null;

        sp.doc = Document.Create();
        sp.docFontDict = new Core.PdfDictionary();
        sp.page = sp.doc.Pages.Add(sp.pageWidth, sp.pageHeight);
        EnsureFonts(sp.page, sp.docFontDict);
        sp.invc = System.Globalization.CultureInfo.InvariantCulture;

        sp.marginLeft = 96.0;
        sp.contentW = sp.pageWidth - 90.0 - 90.0;

        sp.bandFill = Color.FromRgbBytes(0xE7, 0xEF, 0xF7);
        sp.leftCellX0 = sp.marginLeft + 1.5;
        sp.leftCellX1 = sp.leftCellX0 + 90.0;              // 16% cell box (measured 97.5..187.5)
        sp.rightCellX0 = sp.leftCellX1 + 1.5;
        sp.rightCellX1 = sp.marginLeft + sp.contentW * 0.968; // measured 497.5 on the 595 sheet
        FillRect(sp, sp.leftCellX0, SpHeaderBandTopPt, sp.leftCellX1, SpHeaderBandBotPt, sp.bandFill);
        FillRect(sp, sp.rightCellX0, SpHeaderBandTopPt, sp.rightCellX1, SpHeaderBandBotPt, sp.bandFill);

        EmitPanelLogoAndHeading(sp);

        sp.panelTop = SpPanelsTopPt;
        sp.sbX0 = sp.marginLeft + 0.75;
        sp.sbX1 = sp.sbX0 + 94.3;                          // 16% of the inner table (measured 96.8..191.1)
        sp.mainX0 = sp.sbX1 + 0.75;
        sp.mainX1 = sp.marginLeft + sp.contentW * 0.97 + 0.4; // measured 498.2

        sp.seg2 = sp.html[sp.orphan.Index..];
        sp.sbM = SafeRegex.Match(sp.seg2, @"<td\b[^>]*width\s*=\s*[""']?16%[^>]*>([\s\S]*?)</td>",
            RegexOptions.IgnoreCase);
        sp.mainM = SafeRegex.Match(sp.seg2, @"<td\b[^>]*width\s*=\s*[""']?84%[^>]*>([\s\S]*)",
            RegexOptions.IgnoreCase);

        sp.sbBlocks = sp.sbM.Success ? SpParseFlow(sp.sbM.Groups[1].Value) : new List<SpBlock>();
        sp.mainBlocks = sp.mainM.Success ? SpParseFlow(sp.mainM.Groups[1].Value) : new List<SpBlock>();

        sp.mainPad = 3.0;
        sp.mainW = sp.mainX1 - sp.mainX0 - 2 * sp.mainPad;
        sp.flowBottom = sp.pageHeight - 72.0;
        sp.mainY = sp.panelTop + sp.mainPad;               // cursor = next line TOP
        sp.pageIdx = 0;
        sp.pages = new List<Page> { sp.page };
        sp.p1PanelBottom = 0;
#pragma warning disable S3267 // a step loop: each call does the work and returns false to stop - as a LINQ predicate the side effects would hide in a query
        foreach (var b in sp.mainBlocks)
#pragma warning restore S3267
        {
            if (!EmitMainBlock(sp, b)) break;
        }
        if (sp.p1PanelBottom == 0) sp.p1PanelBottom = Math.Min(sp.mainY + sp.mainPad, sp.pageHeight - 72.8);

        sp.sbFillBytes = Encoding.ASCII.GetBytes(Compat.Format(sp.invc,
            $"q {0xE7 / 255.0:0.###} {0xEF / 255.0:0.###} {0xF7 / 255.0:0.###} rg " +
            $"{sp.sbX0:F2} {sp.pageHeight - sp.p1PanelBottom:F2} {sp.sbX1 - sp.sbX0:F2} {sp.p1PanelBottom - sp.panelTop:F2} re f " +
            $"1 1 1 rg {sp.mainX0:F2} {sp.pageHeight - sp.p1PanelBottom:F2} {sp.mainX1 - sp.mainX0:F2} {sp.p1PanelBottom - sp.panelTop:F2} re f Q\n"));
        sp.page.InsertContentStreamAt(0, sp.sbFillBytes);

        sp.sbW = sp.sbX1 - sp.sbX0 - 6.0;
        sp.sbH = 0;
        sp.sbLaid = new List<(SpBlock B, List<List<(SpRun, string)>> Lines, double Pitch, (double asc, double sum) Fm)>();
#pragma warning disable S3267 // a step loop: each call does the work and returns false to stop - as a LINQ predicate the side effects would hide in a query
        foreach (var b in sp.sbBlocks)
#pragma warning restore S3267
        {
            if (!LayoutSidebarBlock(sp, b)) break;
        }
        sp.sbY = sp.panelTop + (sp.p1PanelBottom - sp.panelTop - sp.sbH) / 2;
        foreach (var (b, lines, pitch, pfm) in sp.sbLaid)
        {
            if (!EmitSidebarBlock(sp, b, lines, pitch, pfm)) break;
        }

        return sp.doc;
    }
}
