using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Assemble one page: its module frame, the collected ops and the image stamps in one content stream.</summary>
    private static bool AssembleDnnPage(DnnReportState dn, int p)
    {
        var pg = dn.doc.Pages.Add(dn.pageW, dn.pageH);
        EnsureFonts(pg, dn.fontDict);
        var bot = dn.pageBottom[p];
        var frame = new StringBuilder();
        void F(string s) => frame.Append(s);
        F(Compat.Format(dn.inv,
            $"q {dn.nearWhite.Item1:F3} {dn.nearWhite.Item2:F3} {dn.nearWhite.Item3:F3} rg {dn.contentL:F2} {dn.pageH - bot:F2} {dn.contentR - dn.contentL:F2} {bot - dn.marginT:F2} re f Q\n"));
        void Stroke(double x0, double y0, double x1, double y1, (double, double, double) c)
            => F(Compat.Format(dn.inv,
                $"q {c.Item1:F3} {c.Item2:F3} {c.Item3:F3} RG 0.75 w {x0:F2} {dn.pageH - y0:F2} m {x1:F2} {dn.pageH - y1:F2} l S Q\n"));
        if (p == 0) Stroke(dn.contentL, dn.marginT + 0.375, dn.contentR, dn.marginT + 0.375, dn.borderBlue);
        Stroke(dn.contentR - 0.375, dn.marginT, dn.contentR - 0.375, bot, dn.borderBlue);
        Stroke(dn.contentL + 0.375, dn.marginT, dn.contentL + 0.375, bot, dn.borderBlue);
        if (p == dn.pageOps.Count - 1)
            Stroke(dn.contentL, bot - 0.375, dn.contentR, bot - 0.375, dn.borderBlue);
        // module box verticals
        var mTop = dn.moduleTopPerPage[p];
        var mBot = dn.moduleBotPerPage[p] > 0 ? dn.moduleBotPerPage[p] : bot;
        if (mBot > mTop)
        {
            if (p == 0)
                Stroke(dn.moduleL - 0.75, mTop + 0.375 - 0.75, dn.moduleR + 0.675, mTop + 0.375 - 0.75, dn.teal);
            Stroke(dn.moduleR - 0.375 + 0.375, mTop, dn.moduleR + 0.375 - 0.375, mBot, dn.teal);
            Stroke(dn.moduleL + 0.375 - 0.375, mTop, dn.moduleL - 0.375 + 0.375, mBot, dn.teal);
            if (dn.moduleBotPerPage[p] > 0)
                Stroke(dn.moduleL - 0.75, mBot - 0.375, dn.moduleR + 0.675, mBot - 0.375, dn.teal);
        }
        pg.AddContentStream(Encoding.ASCII.GetBytes(frame.ToString()));
        foreach (var op in dn.pageOps[p])
            pg.AddContentStream(Encoding.ASCII.GetBytes(op));
        foreach (var im in dn.pageStamps[p])
        {
            try
            {
                var stamp = ImageStamp.FromEncodedBytes(im.Bytes);
                stamp.XIndent = im.X;
                stamp.YIndent = dn.pageH - im.YTop - im.H;
                stamp.DisplayWidth = im.W;
                stamp.DisplayHeight = im.H;
                stamp.ApplyTo(pg);
            }
            catch { /* undecodable: skip */ }
        }
        return true;
    }

    /// <summary>Render one report block by its kind: headings, paragraphs, lists, grids, images and sub-modules, each advancing the page.</summary>
    private static bool RenderDnnBlock(DnnReportState dn, DnnBlock b)
    {
        switch (b)
        {
            case DnnBand: case DnnFieldRow: case DnnParaLine: case DnnGap:
                RenderDnnTextBlocks(dn, b);
                break;
            case DnnGridHeader: case DnnGridRow:
                RenderDnnGridBlocks(dn, b);
                break;
            case DnnGridFill gf:
            {
                var gl = gf.FullWidth ? dn.moduleL + 0.375 : dn.bodyL;
                var gr = gf.FullWidth ? dn.moduleR - 0.375 : dn.moduleR - 0.375 - DnnBodyPadPt;
                var rem = gf.H;
                var yy = dn.y;
                var pp = dn.page;
                while (rem > 0)
                {
                    var seg = Math.Min(rem, dn.bandBottom - yy);
                    if (seg <= 0.01) { pp++; EnsurePage(dn, pp); yy = dn.marginT; continue; }
                    FillRect(dn, pp, gl, yy, gr, yy + seg, gf.White ? (1.0, 1.0, 1.0) : dn.listBg);
                    if (!gf.FullWidth)
                    {
                        Line(dn, pp, gl, yy, gl, yy + seg, dn.teal);
                        Line(dn, pp, gr, yy, gr, yy + seg, dn.teal);
                    }
                    Touch(dn, pp, yy + seg);
                    rem -= seg;
                    yy += seg;
                }
                break;              // a fill never advances the cursor
            }
        }
        return true;
    }

    /// <summary>Create the document, its colours and first page, then the logo and date header.</summary>
    private static void DrawDnnHeader(DnnReportState dn)
    {
        dn.doc = new Document();
        dn.fontDict = new Core.PdfDictionary();
        dn.inv = System.Globalization.CultureInfo.InvariantCulture;
        dn.pageOps = new List<List<string>>();
        dn.pageStamps = new List<List<(byte[] Bytes, double X, double YTop, double W, double H)>>();
        dn.pageBottom = new List<double>();
        dn.moduleTopPerPage = new List<double>();
        dn.moduleBotPerPage = new List<double>();

        dn.teal = (0.0, 1.0 * 0x55 / 255, 1.0 * 0x7C / 255);
        dn.subBg = (1.0 * 0xDF / 255, 1.0 * 0xEE / 255, 1.0 * 0xF7 / 255);
        dn.gridHdrBg = (1.0 * 0xDF / 255, 1.0 * 0xED / 255, 1.0 * 0xF6 / 255);
        dn.listBg = (1.0 * 0xF1 / 255, 1.0 * 0xF5 / 255, 1.0 * 0xF8 / 255);
        dn.black = (0.0, 0.0, 0.0);
        dn.white = (1.0, 1.0, 1.0);

        dn.asc8 = DnnAscEm * DnnCellFontPt;

        // header zone (page 1)
        EnsurePage(dn, 0);
        if (dn.logoUrl is not null && dn.logoUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            var logo = FetchRemoteImage(dn.logoUrl);
            if (logo is not null)
                dn.pageStamps[0].Add((logo, dn.contentL + DnnLogoLeftPt, dn.marginT + DnnLogoTopPt,
                    dn.logoWpx * 0.75, dn.logoHpx * 0.75));
        }
        if (dn.dateText.Length > 0)
        {
            var w = MeasureFaceText("Verdana", dn.dateText, DnnBodyFontPt);
            DrawText(dn, 0, dn.contentR - DnnDateRightPt - w,
                dn.marginT + DnnDateTopPt + DnnAscEm * DnnBodyFontPt, dn.dateText, false,
                DnnBodyFontPt, dn.black);
        }
        Touch(dn, 0, dn.marginT + DnnModuleTopPt);

        dn.page = 0;
        dn.y = dn.marginT + DnnModuleTopPt;

    }

    /// <summary>Strip scripts and comments, read the logo, date and module blocks; a page without blocks is not this dialect.</summary>
    private static bool TryParseDnnReport(DnnReportState dn, double pageHIn, string html)
    {
        dn.pageH = pageHIn;
        dn.marginL = 90.0;
        dn.marginR = 90.0;
        dn.marginT = 72.0;
        dn.marginB = 72.0;
        dn.pageW = dn.marginL + DnnSkinBoxPt + dn.marginR;
        dn.bandBottom = dn.pageH - dn.marginB;
        dn.contentL = dn.marginL;
        dn.contentR = dn.marginL + DnnSkinBoxPt;
        dn.moduleL = dn.contentL + DnnModuleInsetPt;
        dn.moduleR = dn.contentR - DnnModuleInsetPt + 0.9;
        dn.bodyL = dn.moduleL + 0.375 + DnnBodyPadPt;

        html = Regex.Replace(html, @"<script[\s\S]*?</script>", " ", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<!--[\s\S]*?-->", " ");

        dn.logoUrl = null; dn.logoWpx = 115;
        dn.logoHpx = 48;
        dn.logoM = Regex.Match(html,
            @"<img[^>]*id\s*=\s*[""'][^""']*imgLogo[""'][^>]*>", RegexOptions.IgnoreCase);
        if (dn.logoM.Success)
        {
            dn.logoUrl = DtpAttr(dn.logoM.Value, "src");
            var st = DtpAttr(dn.logoM.Value, "style") ?? "";
            var wm = Regex.Match(st, @"width\s*:\s*([\d.]+)px", RegexOptions.IgnoreCase);
            var hm = Regex.Match(st, @"height\s*:\s*([\d.]+)px", RegexOptions.IgnoreCase);
            if (wm.Success) dn.logoWpx = DtpNum(wm.Groups[1].Value);
            if (hm.Success) dn.logoHpx = DtpNum(hm.Groups[1].Value);
        }
        dn.dateM = Regex.Match(html,
            @"DateTimeCmnLabel[""'][^>]*>([^<]*)<", RegexOptions.IgnoreCase);
        dn.dateText = dn.dateM.Success ? EdgarHtmlRenderer.DecodeEntities(dn.dateM.Groups[1].Value).Trim() : "";

        dn.blocks = new List<DnnBlock>();
        foreach (Match mc in Regex.Matches(html,
                     @"<div\b[^>]*class\s*=\s*[""'][^""']*\bModuleContainer\b[^""']*[""'][^>]*>",
                     RegexOptions.IgnoreCase))
        {
            var moduleHtml = DnnInnerDiv(html, mc.Index);
            if (moduleHtml is not null) DnnWalk(moduleHtml, 0, dn.blocks);
        }
        if (dn.blocks.Count == 0) return false;
        return true;
    }
}
