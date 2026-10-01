using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Assemble the pages: each page's shapes and text runs become its content stream with the fonts registered on it.</summary>
    private static void AssembleClinicalPages(ClinicalFormState cf)
    {
        cf.doc = Document.Create();
        for (var pi = 0; pi < cf.pageShapes.Count; pi++)
        {
            var page = cf.doc.Pages.Add(CfPageW, CfPageH);
            var head = new StringBuilder();
            head.AppendLine(Compat.Format(cf.inv,
                $"q 1 1 1 rg {CfBodyX:F0} {CfPageH - CfContentBottom:F0} {CfBodyW:F0} {CfContentBottom - CfContentTop:F0} re f Q"));
            // container side borders span the page's flow; the top border only
            // opens the box on page 1, the bottom border closes it on the last
            var yTopB = CfContentTop;
            var yBotB = pi < cf.pageShapes.Count - 1 && pi > 0 ? CfContentBottom : cf.pageFlowBot[pi];
            if (pi == 0)
                head.AppendLine(Compat.Format(cf.inv,
                    $"q 0.267 0.267 0.267 RG 0.75 w {CfContainerX0:F3} {CfPageH - (CfContentTop + 0.375):F3} m {CfContainerX0 + CfContainerW:F3} {CfPageH - (CfContentTop + 0.375):F3} l S Q"));
            if (pi == cf.pageShapes.Count - 1)
                head.AppendLine(Compat.Format(cf.inv,
                    $"q 0.267 0.267 0.267 RG 0.75 w {CfContainerX0:F3} {CfPageH - (yBotB - 0.375):F3} m {CfContainerX0 + CfContainerW:F3} {CfPageH - (yBotB - 0.375):F3} l S Q"));
            head.AppendLine(Compat.Format(cf.inv,
                $"q 0.267 0.267 0.267 RG 0.75 w {CfContainerX0 + 0.375:F3} {CfPageH - yTopB:F3} m {CfContainerX0 + 0.375:F3} {CfPageH - yBotB:F3} l S Q"));
            head.AppendLine(Compat.Format(cf.inv,
                $"q 0.267 0.267 0.267 RG 0.75 w {CfContainerX0 + CfContainerW - 0.375:F3} {CfPageH - yTopB:F3} m {CfContainerX0 + CfContainerW - 0.375:F3} {CfPageH - yBotB:F3} l S Q"));
            page.AddContentStream(Encoding.ASCII.GetBytes(head.ToString() + cf.pageShapes[pi]));
            // deferred text runs embed the Segoe faces per page
            EnsureFonts(page);
            if (page.Dict.Get("Resources") is not Core.PdfDictionary res
                || res.Get("Font") is not Core.PdfDictionary fd)
                continue;
            var sbText = new StringBuilder();
            foreach (var (style, fs, x, baseTd, text, col) in cf.pageRuns[pi])
            {
                var (ttf, face) = style switch
                {
                    1 => (cf.segoeBold!, "SegoeUIBold"),
                    2 => (cf.segoeItalic!, "SegoeUIItalic"),
                    _ => (cf.segoe!, "SegoeUI"),
                };
                var (rn, hex) = Text.Type0FontEmbedder.Embed(fd, ttf, face, text,
                    stripSpacesInBaseFont: true);
                sbText.AppendLine(Compat.Format(cf.inv,
                    $"BT {col} rg /{rn} {fs:0.##} Tf 1 0 0 1 {x:F3} {CfPageH - baseTd:F3} Tm ")
                    + "<" + Compat.ToHexString(hex) + "> Tj ET");
            }
            page.AddContentStream(Encoding.ASCII.GetBytes(sbText.ToString()));
        }
    }

    /// <summary>The sections: each title band and its items laid out down the sheet, breaking pages as needed.</summary>
    private static void DrawClinicalSections(ClinicalFormState cf)
    {
        foreach (var (secTitle, items) in cf.sections)
        {
            var bandTop = cf.bot - (cf.prevWasBox ? CfJunction : 0) + CfSectionGap;
            if (bandTop + CfBandH > CfContentBottom) { BreakPage(cf); bandTop = CfContentTop; }
            Fill(cf, CfInnerX0, bandTop, CfInnerX1 - CfInnerX0, CfBandH, "0.867 0.867 0.867");
            var b0 = bandTop + 0.375; var b1 = bandTop + CfBandH - 0.375;
            Line(cf, CfInnerX0, b0, CfInnerX1, b0, 0.75, "0.667 0.667 0.667");
            Line(cf, CfInnerX0, b1, CfInnerX1, b1, 0.75, "0.667 0.667 0.667");
            Line(cf, CfInnerX0 + 0.375, bandTop, CfInnerX0 + 0.375, bandTop + CfBandH, 0.75, "0.667 0.667 0.667");
            Line(cf, CfInnerX1 - 0.375, bandTop, CfInnerX1 - 0.375, bandTop + CfBandH, 0.75, "0.667 0.667 0.667");
            Run(cf, 0, CfFs, CfInnerX0 + CfBandPadX, bandTop + CfBandBaseOff, secTitle);
            cf.bot = bandTop + CfBandH + CfH3Mb;
            cf.prevWasBox = false;

            var itemNo = 0;
            foreach (var atoms in items)
            {
                itemNo++;
                LayoutCfItem(cf, atoms, itemNo);
            }
            // the ol's 1rem margin-bottom collapses under the next section's
            // larger 20px margin-top — nothing to carry
        }
        cf.pageFlowBot[^1] = cf.bot;

    }

    /// <summary>The header: logo, title band, the four-cell head table, the notice paragraph and the rule under it.</summary>
    private static void DrawClinicalHeader(ClinicalFormState cf)
    {
        cf.measureDict = new Core.PdfDictionary();
        cf.pageShapes = new List<StringBuilder>();
        cf.pageRuns = new List<List<(int Style, double Fs, double X, double BaseTd, string Text, string Col)>>();
        cf.pageFlowBot = new List<double>();
        NewPage(cf);
        cf.bot = 0.0;
        cf.prevWasBox = false;

        // ── page 1 header (measured constants; texts from the fixture) ───────
        Run(cf, 0, CfFs, CfInnerX0, CfLogoBase, cf.logoAlt);
        cf.bandX = CfInnerX1 - 0.75 * (CfInnerX1 - CfInnerX0);
        cf.bandW = CfInnerX1 - cf.bandX;
        Fill(cf, cf.bandX, CfHeaderTop, cf.bandW, CfTitleBandH, "0.973 0.976 0.98");
        Run(cf, 0, 15, cf.bandX + CfCellPad, CfHeaderTop + CfTitleBaseOff, cf.title, CfBlack);
        cf.boldParts = new string[4]; cf.restParts = new string[4];
        for (var c = 0; c < 4; c++)
        {
            var mSplit = Regex.Match(cf.headCells[c], @"^(\S+\s?\S*?:)\s*(.*)$");
            cf.boldParts[c] = mSplit.Success ? mSplit.Groups[1].Value : cf.headCells[c];
            cf.restParts[c] = mSplit.Success ? mSplit.Groups[2].Value : "";
        }
        cf.c1 = Math.Max(CellW(cf, 0), CellW(cf, 2)) + 2 * CfCellPad;
        cf.c2 = Math.Max(CellW(cf, 1), CellW(cf, 3)) + 2 * CfCellPad;
        cf.slack = (cf.bandW - cf.c1 - cf.c2) / 2;
        cf.col1W = cf.c1 + cf.slack;
        cf.rowY = CfHeaderTop + CfTitleBandH;
        for (var r = 0; r < 2; r++)
        {
            Fill(cf, cf.bandX, cf.rowY, cf.col1W, CfCellH, "1 1 1");
            Fill(cf, cf.bandX + cf.col1W, cf.rowY, cf.bandW - cf.col1W, CfCellH, "1 1 1");
            for (var c = 0; c < 2; c++)
            {
                var idx = r * 2 + c;
                var cx = cf.bandX + (c == 0 ? 0 : cf.col1W) + CfCellPad;
                Run(cf, 1, CfFs, cx, cf.rowY + CfCellBaseOff, cf.boldParts[idx], CfBlack);
                if (cf.restParts[idx].Length > 0)
                    Run(cf, 0, CfFs, cx + M(cf, cf.boldParts[idx], 1, CfFs), cf.rowY + CfCellBaseOff,
                        " " + cf.restParts[idx], CfBlack);
            }
            cf.rowY += CfCellH;
        }
        cf.noticeTop = cf.rowY + CfNoticeGap;
        cf.noticeLines = WrapCfWords(cf.mainText, s => M(cf, s, 0, CfFs), CfInnerX1 - CfInnerX0);
        foreach (var ln in cf.noticeLines)
        {
            var w = M(cf, ln, 0, CfFs);
            Run(cf, 0, CfFs, CfInnerX0 + (CfInnerX1 - CfInnerX0 - w) / 2, cf.noticeTop + CfBaseOff, ln);
            cf.noticeTop += CfLineH;
        }
        cf.ruleCenter = cf.noticeTop - CfLineH + CfBaseOff + CfLineDesc + 0.75;
        Line(cf, CfInnerX0, cf.ruleCenter, CfInnerX1, cf.ruleCenter, 1.5, CfBlack);
        cf.bot = cf.ruleCenter + 0.75;
        cf.prevWasBox = false;

        // ── sections ─────────────────────────────────────────────────────────
    }

    /// <summary>Parse the faces, body, logo, title, head table, notice text and sections; a page that lacks them is not this dialect.</summary>
    private static bool TryParseClinicalForm(ClinicalFormState cf, string html)
    {
        cf.segoe = Text.SystemFontResolver.Resolve("Segoe UI");
        cf.segoeBold = Text.SystemFontResolver.Resolve("SegoeUI-Bold")
            ?? Text.SystemFontResolver.Resolve("Segoe UI Bold");
        cf.segoeItalic = Text.SystemFontResolver.Resolve("SegoeUI-Italic")
            ?? Text.SystemFontResolver.Resolve("Segoe UI Italic");
        if (cf.segoe is null || cf.segoeBold is null || cf.segoeItalic is null) return false;

        cf.bodyM = Regex.Match(html, @"<body[^>]*>([\s\S]*)</body>", RegexOptions.IgnoreCase);
        cf.body = cf.bodyM.Success ? cf.bodyM.Groups[1].Value : html;

        cf.logoAlt = Regex.Match(cf.body, @"<img\b[^>]*\balt\s*=\s*""([^""]*)""[^>]*>",
            RegexOptions.IgnoreCase) is { Success: true } lm ? lm.Groups[1].Value : "";
        cf.titleM = Regex.Match(cf.body, @"<h5[^>]*>([\s\S]*?)</h5>", RegexOptions.IgnoreCase);
        if (!cf.titleM.Success) return false;
        cf.title = Flat(cf, cf.titleM.Groups[1].Value);
        cf.headTableM = Regex.Match(cf.body,
            @"<table\b[^>]*id=""header-table""[^>]*>([\s\S]*?)</table>", RegexOptions.IgnoreCase);
        if (!cf.headTableM.Success) return false;
        cf.headCells = new List<string>();
        foreach (Match cm in Regex.Matches(cf.headTableM.Groups[1].Value,
            @"<td[^>]*>([\s\S]*?)</td>", RegexOptions.IgnoreCase))
            cf.headCells.Add(Flat(cf, cm.Groups[1].Value));
        if (cf.headCells.Count < 4) return false;
        cf.mainTextM = Regex.Match(cf.body,
            @"<div\b[^>]*id=""header-maintext""[^>]*>([\s\S]*?)</div>", RegexOptions.IgnoreCase);
        cf.mainText = cf.mainTextM.Success ? Flat(cf, cf.mainTextM.Groups[1].Value) : "";

        cf.sections = new List<(string Title, List<List<CfAtom>> Items)>();
        foreach (Match sm in Regex.Matches(cf.body,
            @"<section\s+class=""section""[^>]*>([\s\S]*?)</section>", RegexOptions.IgnoreCase))
        {
            var sec = sm.Groups[1].Value;
            var h3 = Regex.Match(sec, @"<h3[^>]*>([\s\S]*?)</h3>", RegexOptions.IgnoreCase);
            var items = new List<List<CfAtom>>();
            foreach (Match li in Regex.Matches(sec, @"<li\b[^>]*>([\s\S]*?)</li>",
                RegexOptions.IgnoreCase))
                items.Add(ParseCfAtoms(li.Groups[1].Value));
            cf.sections.Add((h3.Success ? Flat(cf, h3.Groups[1].Value) : "", items));
        }
        if (cf.sections.Count == 0) return false;

        return true;
    }
}
