using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Render one parsed block of the screen: a heading, a paragraph, a button row, a table or a list, each advancing the flow.</summary>
    private static bool RenderBootstrapBlock(BootstrapScreenState bs, double pageWidth, double pageHeight, Aspose.Pdf.Converters.HtmlToPdfConverter.BsBlock blk)
    {
        switch (blk)
        {
            case BsRule:
            {
                // only the jumbo arm draws the hr; other themes were
                // calibrated without it
                if (!bs.jumboDoc) break;
                bs.yTd += BsHrMarginPt;
                bs.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(bs.invc,
                    $"q 0.933 0.933 0.933 RG 0.75 w {bs.contentX:F2} {pageHeight - bs.yTd:F2} m {pageWidth - BsMarginX - bs.containerPad:F2} {pageHeight - bs.yTd:F2} l S Q\n")));
                bs.yTd += BsHrMarginPt;
                bs.lastPMarB = 0;
                break;
            }
            case BsHeading h:
            {
                // jumbo arm: a preceding paragraph's bottom margin collapses
                // into the heading's larger top margin
                bs.yTd += bs.h2MarT - (bs.jumboDoc ? Math.Min(bs.lastPMarB, bs.h2MarT) : 0);
                EmitRun(bs, pageHeight, "FB", bs.h2Fs, bs.contentX, bs.yTd + bs.h2Drop, h.Text, bs.bodyColor);
                bs.yTd += bs.h2LineH + bs.h2MarB;
                bs.lastPMarB = 0;
                break;
            }
            case BsParagraph p when p.Runs.TrueForAll(r => r.Button is null):
            {
                // jumbo arm: a plain text paragraph wraps at the container
                // width; other themes keep their calibrated single line
                if (bs.jumboDoc && p.Runs.Count == 1 && p.Runs[0].Text is { } single)
                {
                    foreach (var ln in MeasuredWordWrap(single.Trim(),
                        pageWidth - BsMarginX - bs.containerPad - bs.contentX, bs.face, bs.bodyFs))
                    {
                        EmitRun(bs, pageHeight, "FA", bs.bodyFs, bs.contentX, bs.yTd + bs.drop, ln, bs.bodyColor);
                        bs.yTd += bs.lineH;
                    }
                    bs.yTd += bs.pMarB;
                    bs.lastPMarB = bs.pMarB;
                    break;
                }
                var baseline = bs.yTd + bs.drop;
                var x = bs.contentX;
                foreach (var r in p.Runs)
                {
                    if (r.Text is not null)
                    {
                        EmitRun(bs, pageHeight, "FA", bs.bodyFs, x, baseline, r.Text, bs.bodyColor);
                        x += MeasureFaceText(bs.face, r.Text, bs.bodyFs);
                    }
                    else if (r.IconCp > 0)
                        x += EmitIcon(bs, pageHeight, r.IconCp, bs.bodyFs, x, baseline + BsIconTopPt,
                            r.InLink ? bs.linkColor : bs.bodyColor);
                }
                bs.yTd += bs.lineH + bs.pMarB;
                bs.lastPMarB = bs.pMarB;
                break;
            }
            case BsParagraph p:
            {
                RenderButtonParagraph(bs, pageHeight, p);
                break;
            }
            case BsTable t:
            {
                bs.yTd = RenderBootstrapTable(bs.page, t, bs.yTd, bs.contentX, bs.contentW,
                    pageHeight, bs.face, bs.boldFace, bs.fm, bs.bodyFs, bs.lineH, bs.drop,
                    bs.cellPad, bs.borderCol, bs.tableBg, bs.bodyColor, bs);
                bs.yTd += bs.tableMarB;
                break;
            }
        }
        return true;
    }

    /// <summary>The navbar brand and the jumbotron: its heading, lead and the one or two call-to-action buttons.</summary>
    private static void RenderJumbotron(BootstrapScreenState bs, double pageWidth, double pageHeight)
    {
        // The fixed navbar: its #222 band across the content box under a
        // #080808 hairline, the brand at the container origin, and the
        // toggle button's chrome at the right margin (all measured).
        var navR = pageWidth - BsMarginX;
        bs.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(bs.invc,
            $"q 0.133 0.133 0.133 rg {BsMarginX:F2} {pageHeight - BsMarginY - BsNavbarHPt:F2} {navR - BsMarginX:F2} {BsNavbarHPt:F2} re f Q\n" +
            $"q 0.031 0.031 0.031 RG 0.75 w {BsMarginX:F2} {pageHeight - BsMarginY - BsNavbarHPt + 0.35:F2} m {navR:F2} {pageHeight - BsMarginY - BsNavbarHPt + 0.35:F2} l S Q\n")));
        if (bs.navBrand.Length > 0)
            EmitRun(bs, pageHeight, "FA", 13.5, bs.contentX, BsMarginY + BsBrandBaselinePt, bs.navBrand,
                Color.FromRgbBytes(0x9d, 0x9d, 0x9d));
        // toggle: UA outline, #333 border box, three white bars
        var tR = navR - 9.2;
        bs.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(bs.invc,
            $"q 0 0 0 RG 1 w {tR - 37:F2} {pageHeight - BsMarginY - 33:F2} 37 28.5 re S Q\n" +
            $"q 0.2 0.2 0.2 RG 0.75 w {tR - 34.7:F2} {pageHeight - BsMarginY - 31.1:F2} 32.3 24.7 re S Q\n" +
            $"q 1 1 1 rg {tR - 26.8:F2} {pageHeight - BsMarginY - 15:F2} 16.5 1.5 re f " +
            $"{tR - 26.8:F2} {pageHeight - BsMarginY - 19.5:F2} 16.5 1.5 re f " +
            $"{tR - 26.8:F2} {pageHeight - BsMarginY - 24:F2} 16.5 1.5 re f Q\n")));
        // The jumbotron: the #eee container box with its measured content
        // ladder (h1, the lead lines, the btn-lg, the plain <button>).
        var jTop = BsMarginY + BsNavbarHPt - 0.75;
        var jX = bs.contentX;
        var jW = pageWidth - BsMarginX - bs.containerPad - jX;
        var pen = jTop + JmbPadTopPt;
        var leadLines = bs.jumboLead.Length > 0
            ? MeasuredWordWrap(bs.jumboLead, jW - 2 * JmbPadXPt, bs.face, JmbLeadFsPt)
            : Array.Empty<string>();
        var jH = JmbPadTopPt + (bs.jumboH1.Length > 0 ? JmbH1LineHPt + JmbH1MarBPt : 0)
            + leadLines.Length * JmbLeadLineHPt + JmbLeadMarBPt
            + (bs.jumboBtnLabel.Length > 0 ? JmbBtnLgHPt + JmbBtnGapPt : 0)
            + (bs.jumboBtn2Label.Length > 0 ? JmbBtnHPt : 0) + JmbPadBotPt;
        bs.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(bs.invc,
            $"q 0.933 0.933 0.933 rg {jX:F2} {pageHeight - jTop - jH:F2} {jW:F2} {jH:F2} re f Q\n")));
        if (bs.jumboH1.Length > 0)
        {
            EmitRun(bs, pageHeight, "FA", JmbH1FsPt, jX + JmbPadXPt,
                pen + MetricBaselineDrop(JmbH1FsPt, JmbH1LineHPt, bs.fm), bs.jumboH1, bs.bodyColor);
            pen += JmbH1LineHPt + JmbH1MarBPt;
        }
        foreach (var ln in leadLines)
        {
            EmitRun(bs, pageHeight, "FA", JmbLeadFsPt, jX + JmbPadXPt,
                pen + MetricBaselineDrop(JmbLeadFsPt, JmbLeadLineHPt, bs.fm), ln, bs.bodyColor);
            pen += JmbLeadLineHPt;
        }
        pen += JmbLeadMarBPt;
        if (bs.jumboBtnLabel.Length > 0)
        {
            var lw = MeasureFaceText(bs.boldFace, bs.jumboBtnLabel, bs.btnLgFs);
            var bw = 2 * bs.btnLgPadX + 2 * 0.75 + lw;
            Box(bs, pageHeight, jX + JmbPadXPt, pen, bw, JmbBtnLgHPt, bs.jumboBtnFill, bs.jumboBtnBorder, 0.75);
            EmitRun(bs, pageHeight, "FB", bs.btnLgFs, jX + JmbPadXPt + 0.75 + bs.btnLgPadX,
                pen + 0.75 + bs.btnLgPadY + MetricBaselineDrop(bs.btnLgFs, bs.btnLgFs * bs.btnLgLineF, bs.fm),
                bs.jumboBtnLabel, Color.FromArgb(255, 255, 255));
            pen += JmbBtnLgHPt + JmbBtnGapPt;
        }
        if (bs.jumboBtn2Label.Length > 0)
        {
            var lw = MeasureFaceText(bs.boldFace, bs.jumboBtn2Label, bs.bodyFs);
            var bw = 2 * bs.btnPadX + 2 * 0.75 + lw;
            Box(bs, pageHeight, jX + JmbPadXPt - BsButtonOutlineX, pen - BsButtonOutlineY,
                bw + 2 * BsButtonOutlineX, JmbBtnHPt + 2 * BsButtonOutlineY,
                null, Color.FromArgb(0, 0, 0), 1.0);
            Box(bs, pageHeight, jX + JmbPadXPt, pen, bw, JmbBtnHPt,
                Color.FromArgb(255, 255, 255), Color.FromRgbBytes(0xcc, 0xcc, 0xcc), 0.75);
            EmitRun(bs, pageHeight, "FB", bs.bodyFs, jX + JmbPadXPt + 0.75 + bs.btnPadX,
                pen + 0.75 + bs.btnPadY + bs.drop, bs.jumboBtn2Label, bs.bodyColor);
            pen += JmbBtnHPt;
        }
        bs.yTd = jTop + jH + JmbMarBPt;
    }

    /// <summary>Read the body markup: the navbar brand and, for a jumbotron document, its heading, lead and button labels and colours.</summary>
    private static bool TryParseBootstrapHeader(BootstrapScreenState bs, IReadOnlyDictionary<string, Dictionary<string, string>> css, string html)
    {
        bs.bodyM = Regex.Match(html, @"<body\b[^>]*>([\s\S]*)</body>", RegexOptions.IgnoreCase);
        if (!bs.bodyM.Success) return false;
        bs.bodyHtml = bs.bodyM.Groups[1].Value;

        bs.jumboDoc = Regex.IsMatch(bs.bodyHtml, @"class\s*=\s*[""'][^""']*jumbotron",
                RegexOptions.IgnoreCase)
            && Regex.IsMatch(bs.bodyHtml, @"class\s*=\s*[""'][^""']*navbar-fixed-top",
                RegexOptions.IgnoreCase);
        bs.navBrand = "";
        bs.jumboH1 = "";
        bs.jumboLead = "";
        bs.jumboBtnLabel = "";
        bs.jumboBtn2Label = "";
        bs.jumboBtnFill = Color.FromRgbBytes(0x33, 0x7a, 0xb7);
        bs.jumboBtnBorder = Color.FromRgbBytes(0x2e, 0x6d, 0xa4);
        if (bs.jumboDoc)
        {
            static (string inner, string rest) Cut(string s, string clsRe)
            {
                var m = Regex.Match(s, @"<div\b[^>]*class\s*=\s*[""'][^""']*" + clsRe + @"[^>]*>",
                    RegexOptions.IgnoreCase);
                if (!m.Success) return ("", s);
                var end = FindDivClose(s, m.Index + m.Length);
                var inner = s[(m.Index + m.Length)..end];
                var close = s.IndexOf('>', end);
                return (inner, s.Remove(m.Index, (close < 0 ? end : close + 1) - m.Index));
            }
            (var navHtml, bs.bodyHtml) = Cut(bs.bodyHtml, "navbar-fixed-top");
            var brandM = Regex.Match(navHtml,
                @"class\s*=\s*[""'][^""']*navbar-brand[^>]*>(?<t>[\s\S]*?)</a>",
                RegexOptions.IgnoreCase);
            if (brandM.Success)
                bs.navBrand = Regex.Replace(DecodeEntities(Regex.Replace(
                    brandM.Groups["t"].Value, @"<[^>]+>", " ")), @"\s+", " ").Trim();
            (var jumboHtml, bs.bodyHtml) = Cut(bs.bodyHtml, "jumbotron");
            static string Flat1(string frag)
                => Regex.Replace(DecodeEntities(Regex.Replace(frag, @"<[^>]+>", " ")),
                    @"\s+", " ").Trim();
            var h1M = Regex.Match(jumboHtml, @"<h1\b[^>]*>(?<t>[\s\S]*?)</h1>", RegexOptions.IgnoreCase);
            if (h1M.Success) bs.jumboH1 = Flat1(h1M.Groups["t"].Value);
            var leadM = Regex.Match(jumboHtml,
                @"<p\b[^>]*class\s*=\s*[""']lead[""'][^>]*>(?<t>[\s\S]*?)</p>", RegexOptions.IgnoreCase);
            if (leadM.Success) bs.jumboLead = Flat1(leadM.Groups["t"].Value);
            var btnAM = Regex.Match(jumboHtml,
                @"<a\b[^>]*class\s*=\s*[""'][^""']*btn-lg[^>]*>(?<t>[\s\S]*?)</a>", RegexOptions.IgnoreCase);
            if (btnAM.Success)
            {
                bs.jumboBtnLabel = Flat1(btnAM.Groups["t"].Value);
                var (f2, b2, _) = BtnColors(bs, css, "btn btn-primary btn-lg");
                bs.jumboBtnFill = f2; bs.jumboBtnBorder = b2;
            }
            var btnBM = Regex.Match(jumboHtml,
                @"<button\b[^>]*>(?<t>[\s\S]*?)</button>", RegexOptions.IgnoreCase);
            if (btnBM.Success) bs.jumboBtn2Label = Flat1(btnBM.Groups["t"].Value);
        }
        return true;
    }

    /// <summary>Resolve the screen's face, colours, heading, paragraph, container, table and button rules from the stylesheet.</summary>
    private static bool TryResolveBootstrapStyles(BootstrapScreenState bs, IReadOnlyDictionary<string, Dictionary<string, string>> css)
    {
        bs.face = "Arial";
        if (bs.body.TryGetValue("font-family", out var famV))
            foreach (var fam in famV.Split(','))
            {
                var f = fam.Trim().Trim('"', '\'');
                if (f.Length > 0 && !f.Equals("sans-serif", StringComparison.OrdinalIgnoreCase)
                    && WinMetricsFor(f) is not null) { bs.face = f; break; }
            }
        if (WinMetricsFor(bs.face) is not { } fm) return false;
        bs.fm = fm;
        bs.xHalf = XHeightFor(bs.face) is { } xh ? xh / 2 : 0.26;

        bs.bodyColor = bs.body.TryGetValue("color", out var bodyColV)
            && ParseCssColor(bodyColV) is { } bc ? bc : Color.FromArgb(0, 0, 0);
        bs.linkColor = css.TryGetValue("a", out var aRule)
            && aRule.TryGetValue("color", out var aColV)
            && ParseCssColor(aColV) is { } ac ? ac : bs.bodyColor;

        bs.lineH = bs.bodyFs * bs.lineFactor;
        bs.drop = MetricBaselineDrop(bs.bodyFs, bs.lineH, bs.fm);

        bs.h2Fs = 14.25;
        bs.h2LineF = 1.1;
        bs.h2MarT = 13.5;
        bs.h2MarB = 6.75;
        if (css.TryGetValue("h2", out var h2Rule))
        {
            if (h2Rule.TryGetValue("font-size", out var h2FsV)
                && TryParseLength(h2FsV) is { } h2FsPt) bs.h2Fs = h2FsPt;
            if (h2Rule.TryGetValue("line-height", out var h2LhV)
                && double.TryParse(h2LhV.Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var h2Lf)) bs.h2LineF = h2Lf;
            if (h2Rule.TryGetValue("margin-top", out var h2MtV)
                && TryParseLength(h2MtV) is { } h2Mt) bs.h2MarT = h2Mt;
            if (h2Rule.TryGetValue("margin-bottom", out var h2MbV)
                && TryParseLength(h2MbV) is { } h2Mb) bs.h2MarB = h2Mb;
        }
        bs.h2LineH = bs.h2Fs * bs.h2LineF;
        bs.h2Drop = MetricBaselineDrop(bs.h2Fs, bs.h2LineH, bs.fm);

        bs.pMarB = 6.75;
        if (css.TryGetValue("p", out var pRule)
            && pRule.TryGetValue("margin", out var pMarV))
            bs.pMarB = ParseInlineMarginBox("margin:" + pMarV, bs.bodyFs).bottom;

        bs.containerPad = 11.25;
        if (css.TryGetValue(".container", out var contRule)
            && contRule.TryGetValue("padding-left", out var cplV)
            && TryParseLength(cplV) is { } cpl) bs.containerPad = cpl;

        bs.cellPad = 6.0;
        bs.tableMarB = 13.5;
        bs.borderCol = Color.FromArgb(195, 198, 201);
        bs.tableBg = Color.FromArgb(255, 255, 255);
        if (css.TryGetValue(".table td", out var tdRule)
            && tdRule.TryGetValue("padding", out var tdPadV)
            && TryParseLength(tdPadV) is { } tdPad) bs.cellPad = tdPad;
        if (css.TryGetValue(".table", out var tblRule)
            && tblRule.TryGetValue("margin-bottom", out var tblMbV)
            && TryParseLength(tblMbV) is { } tblMb) bs.tableMarB = tblMb;
        if (css.TryGetValue(".table-bordered", out var tbRule)
            && tbRule.TryGetValue("border", out var tbBorderV)
            && ParseCssColor(tbBorderV) is { } tbc) bs.borderCol = tbc;
        if (css.TryGetValue("table", out var tblBgRule)
            && tblBgRule.TryGetValue("background-color", out var tblBgV)
            && ParseCssColor(tblBgV) is { } tbg) bs.tableBg = tbg;

        bs.btnPadY = 4.5;
        bs.btnPadX = 9.0;
        bs.btnLgPadY = 7.5;
        bs.btnLgPadX = 12.0;
        bs.btnLgFs = 12.75;
        bs.btnLgLineF = 1.3333333;
        if (css.TryGetValue(".btn", out var btnRule))
        {
            var btnBox = btnRule.TryGetValue("padding", out var btnPadV)
                ? ParseInlineMarginBox("margin:" + btnPadV, bs.bodyFs) : default;
            if (btnBox.top > 0) { bs.btnPadY = btnBox.top; bs.btnPadX = btnBox.right; }
        }
        if (css.TryGetValue(".btn-lg", out var btnLgRule))
        {
            var lgBox = btnLgRule.TryGetValue("padding", out var lgPadV)
                ? ParseInlineMarginBox("margin:" + lgPadV, bs.bodyFs) : default;
            if (lgBox.top > 0) { bs.btnLgPadY = lgBox.top; bs.btnLgPadX = lgBox.right; }
            if (btnLgRule.TryGetValue("font-size", out var lgFsV)
                && TryParseLength(lgFsV) is { } lgFs) bs.btnLgFs = lgFs;
            if (btnLgRule.TryGetValue("line-height", out var lgLhV)
                && double.TryParse(lgLhV.Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var lgLf)) bs.btnLgLineF = lgLf;
        }
        return true;
    }

    /// <summary>The fill, border and text colours of a button from its btn-* classes, the default grey when none is styled.</summary>
    private static (Color fill, Color border, Color fg) BtnColors(BootstrapScreenState bs, IReadOnlyDictionary<string, Dictionary<string, string>> css, string classes)
    {
        foreach (var cls in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (cls.StartsWith("btn-", StringComparison.OrdinalIgnoreCase)
                && cls is not ("btn-lg" or "btn-sm" or "btn-xs")
                && css.TryGetValue("." + cls, out var vr))
            {
                var fill = vr.TryGetValue("background-color", out var bgv)
                    && ParseCssColor(bgv) is { } f ? f : Color.FromArgb(236, 236, 236);
                var border = vr.TryGetValue("border-color", out var bov)
                    && ParseCssColor(bov) is { } b ? b : fill;
                var fg = vr.TryGetValue("color", out var fgv)
                    && ParseCssColor(fgv) is { } g ? g : bs.bodyColor;
                return (fill, border, fg);
            }
        return (Color.FromArgb(236, 236, 236), Color.FromArgb(145, 150, 156), bs.bodyColor);
    }

    /// <summary>A paragraph carrying buttons: each button framed at its measured width with its icon and label, plain runs set between them.</summary>
    private static void RenderButtonParagraph(BootstrapScreenState bs, double pageHeight, BsParagraph p)
    {
        // a line holding a button: the CSS box tops the line, the text
        // seats by vertical-align:middle against it
        var btn = p.Runs.Find(r => r.Button is not null)!.Button!;
        var fs = btn.Large ? bs.btnLgFs : bs.bodyFs;
        var padY = btn.Large ? bs.btnLgPadY : bs.btnPadY;
        var padX = btn.Large ? bs.btnLgPadX : bs.btnPadX;
        var btnLineH = fs * (btn.Large ? bs.btnLgLineF : bs.lineFactor);
        var btnH = 2 * padY + btnLineH + 2 * 0.75;
        var baseline = bs.yTd + btnH / 2 + bs.xHalf * bs.bodyFs;
        var x = bs.contentX;
        foreach (var r in p.Runs)
        {
            if (r.Text is not null)
            {
                EmitRun(bs, pageHeight, "FA", bs.bodyFs, x, baseline, r.Text, bs.bodyColor);
                x += MeasureFaceText(bs.face, r.Text, bs.bodyFs);
            }
            else if (r.IconCp > 0)
                x += EmitIcon(bs, pageHeight, r.IconCp, bs.bodyFs, x, baseline + BsIconTopPt,
                    r.InLink ? bs.linkColor : bs.bodyColor);
            else if (r.Button is { } b)
            {
                var labelW = MeasureFaceText(bs.boldFace, b.Label, fs);
                var iconW = b.IconCp > 0 ? BsNotdefIconAdvEm * fs : 0;
                var boxW = 2 * padX + 2 * 0.75 + iconW + labelW;
                if (b.IsButtonTag)
                    Box(bs, pageHeight, x - BsButtonOutlineX, bs.yTd - BsButtonOutlineY,
                        boxW + 2 * BsButtonOutlineX, btnH + 2 * BsButtonOutlineY,
                        null, Color.FromArgb(0, 0, 0), 1.0);
                Box(bs, pageHeight, x, bs.yTd, boxW, btnH, b.Fill, b.Border, 0.75);
                var bDrop = MetricBaselineDrop(fs, btnLineH, bs.fm);
                var bBase = bs.yTd + 0.75 + padY + bDrop;
                var bx = x + 0.75 + padX;
                if (b.IconCp > 0)
                    bx += EmitIcon(bs, pageHeight, b.IconCp, fs, bx, bBase + BsIconTopPt, b.Fg);
                EmitRun(bs, pageHeight, "FB", fs, bx, bBase, b.Label, b.Fg);
                x += boxW;
            }
        }
        bs.yTd += btnH + bs.pMarB;
        bs.lastPMarB = bs.pMarB;
    }
}
