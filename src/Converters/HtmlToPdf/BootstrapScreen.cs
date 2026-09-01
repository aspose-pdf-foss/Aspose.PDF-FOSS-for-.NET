using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// The Bootstrap-screen dialect: a themed Bootstrap page (a BODY rule carrying a
// pixel font size, a unitless line-height and a page background, plus the
// .container/.table framework classes) rendered at HtmlMediaType.Screen. The
// reference lays it out on symmetric 90 pt side margins with the body background
// painted over the content box, the container inset by its 15px padding, real
// line-height line boxes (UNROUNDED — 13px · 1.42857 = 13.928 pt, where the
// px-rounding the other metric dialects apply would land a half-pixel off), and
// win-metric half-leading baselines. Tables draw as bordered grids whose columns
// share the full width in proportion to their widest cell; buttons draw as their
// CSS boxes with the surrounding text seated by vertical-align:middle (box middle
// = text baseline − half the face's x-height); glyphicons substitute the system
// symbol faces. Every constant below is either read from the
// stylesheet or measured on the expected render.
internal static partial class HtmlToPdfConverter
{
    /// <summary>One inline piece of a Bootstrap paragraph.</summary>
    private sealed class BsRun
    {
        public string? Text;                       // plain text (null for icon/button)
        public int IconCp;                         // glyphicon code point (0 = none)
        public bool InLink;                        // inside <a> — link colour
        public BsButton? Button;
    }

    private sealed class BsButton
    {
        public int IconCp;
        public string Label = "";
        public bool Large;                         // .btn-lg
        public bool IsButtonTag;                   // <button> draws the UA outline
        public Color Fill = Color.FromArgb(236, 236, 236);
        public Color Border = Color.FromArgb(145, 150, 156);
        public Color Fg = Color.FromArgb(51, 51, 50);
    }

    // The content box: 90 pt side margins, 72 pt top/bottom (the body
    // background fill spans exactly (90,72)-(pageW-90,pageH-72)).
    private const double BsMarginX = 90.0;
    private const double BsMarginY = 72.0;
    // .glyphicon { position: relative; top: 1px } — every icon sits 0.75 pt below
    // its line's baseline.
    private const double BsIconTopPt = 0.75;
    // An unresolved private-use glyphicon (E003/E045 have no glyph in the system
    // symbol faces) still advances three quarters of an em (measured: the bold
    // button label starts 7.31 pt past the icon pen at 9.75 pt).
    private const double BsNotdefIconAdvEm = 0.75;
    // A <button> element's UA chrome: a 1 px black outline drawn OUTSIDE the CSS
    // box — 2 pt to the sides, 1.5 pt above/below (measured).
    private const double BsButtonOutlineX = 2.0;
    private const double BsButtonOutlineY = 1.5;

    // The MVC-template (navbar + jumbotron) arm — all measured values:
    // the 50px navbar band, the brand baseline 23.4 under the band top, the
    // jumbotron's 36pt (48px) top pad, its 27pt/29.7 h1 with the 15px bottom
    // margin, the 15.75/22.05 lead, the 34.5 btn-lg box, the 11.3 gap to the
    // 25.5 plain-button box, the 30px bottom pad, and the 30px jumbotron
    // margin-bottom; hr rules carry 20px margins.
    private const double BsNavbarHPt = 38.2;
    private const double BsBrandBaselinePt = 23.4;
    private const double JmbPadTopPt = 36.0;
    private const double JmbPadBotPt = 22.5;
    private const double JmbPadXPt = 11.25;
    private const double JmbH1FsPt = 27.0;
    private const double JmbH1LineHPt = 29.7;
    private const double JmbH1MarBPt = 11.25;
    private const double JmbLeadFsPt = 15.75;
    private const double JmbLeadLineHPt = 22.05;
    private const double JmbLeadMarBPt = 11.25;
    private const double JmbBtnLgHPt = 34.5;
    private const double JmbBtnHPt = 25.5;
    private const double JmbBtnGapPt = 11.3;
    private const double JmbMarBPt = 22.5;
    private const double BsHrMarginPt = 15.0;

    // Glyphicons Halflings :before content (the stylesheet's own mapping — the
    // parser drops :before rules, so the three icons this sheet uses are pinned).
    private static int BsGlyphiconCp(string classes)
        => classes.Contains("glyphicon-envelope") ? 0x2709
         : classes.Contains("glyphicon-search") ? 0xE003
         : classes.Contains("glyphicon-print") ? 0xE045
         : 0;

    /// <summary>Render a themed Bootstrap screen document, or null when the page
    /// does not carry the dialect's fingerprint.</summary>
    private static Document? TryRenderBootstrapScreen(string html,
        IReadOnlyDictionary<string, Dictionary<string, string>> css,
        double pageWidth, double pageHeight)
    {
        // ── fingerprint: themed Bootstrap body + framework classes ──
        if (!css.TryGetValue("body", out var body)) return null;
        if (!body.TryGetValue("background-color", out var bodyBgV)
            || ParseCssColor(bodyBgV) is not { } bodyBg) return null;
        if (!body.TryGetValue("font-size", out var bodyFsV)
            || !bodyFsV.TrimEnd().EndsWith("px", StringComparison.OrdinalIgnoreCase)
            || !TryParseLength(bodyFsV, out var bodyFs)) return null;
        if (!body.TryGetValue("line-height", out var bodyLhV)
            || !double.TryParse(bodyLhV.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var lineFactor)
            || lineFactor is <= 1.0 or >= 2.0) return null;
        if (!css.ContainsKey(".container") || !css.ContainsKey(".table")) return null;
        if (!Regex.IsMatch(html, @"class\s*=\s*[""']container[""']", RegexOptions.IgnoreCase))
            return null;

        // The face: first INSTALLED family of the body stack, else the sans
        // default substitute (Arial).
        var face = "Arial";
        if (body.TryGetValue("font-family", out var famV))
            foreach (var fam in famV.Split(','))
            {
                var f = fam.Trim().Trim('"', '\'');
                if (f.Length > 0 && !f.Equals("sans-serif", StringComparison.OrdinalIgnoreCase)
                    && WinMetricsFor(f) is not null) { face = f; break; }
            }
        if (WinMetricsFor(face) is not { } fm) return null;
        var xHalf = XHeightFor(face) is { } xh ? xh / 2 : 0.26;

        var bodyColor = body.TryGetValue("color", out var bodyColV)
            && ParseCssColor(bodyColV) is { } bc ? bc : Color.FromArgb(0, 0, 0);
        var linkColor = css.TryGetValue("a", out var aRule)
            && aRule.TryGetValue("color", out var aColV)
            && ParseCssColor(aColV) is { } ac ? ac : bodyColor;

        var lineH = bodyFs * lineFactor;
        var drop = MetricBaselineDrop(bodyFs, lineH, fm);

        // h2: the theme's size, 1.1 line box, 18px/9px margins.
        double h2Fs = 14.25, h2LineF = 1.1, h2MarT = 13.5, h2MarB = 6.75;
        if (css.TryGetValue("h2", out var h2Rule))
        {
            if (h2Rule.TryGetValue("font-size", out var h2FsV)
                && TryParseLength(h2FsV, out var h2FsPt)) h2Fs = h2FsPt;
            if (h2Rule.TryGetValue("line-height", out var h2LhV)
                && double.TryParse(h2LhV.Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var h2Lf)) h2LineF = h2Lf;
            if (h2Rule.TryGetValue("margin-top", out var h2MtV)
                && TryParseLength(h2MtV, out var h2Mt)) h2MarT = h2Mt;
            if (h2Rule.TryGetValue("margin-bottom", out var h2MbV)
                && TryParseLength(h2MbV, out var h2Mb)) h2MarB = h2Mb;
        }
        var h2LineH = h2Fs * h2LineF;
        var h2Drop = MetricBaselineDrop(h2Fs, h2LineH, fm);

        // p { margin: 0 0 9px }
        var pMarB = 6.75;
        if (css.TryGetValue("p", out var pRule)
            && pRule.TryGetValue("margin", out var pMarV))
            pMarB = ParseInlineMarginBox("margin:" + pMarV, bodyFs).bottom;

        // .container { padding-left/right: 15px }
        var containerPad = 11.25;
        if (css.TryGetValue(".container", out var contRule)
            && contRule.TryGetValue("padding-left", out var cplV)
            && TryParseLength(cplV, out var cpl)) containerPad = cpl;

        // table geometry from the sheet: cell padding, border colour, thead's
        // 2px bottom border, the table's own margin-bottom and white background.
        double cellPad = 6.0, tableMarB = 13.5;
        var borderCol = Color.FromArgb(195, 198, 201);
        var tableBg = Color.FromArgb(255, 255, 255);
        if (css.TryGetValue(".table td", out var tdRule)
            && tdRule.TryGetValue("padding", out var tdPadV)
            && TryParseLength(tdPadV, out var tdPad)) cellPad = tdPad;
        if (css.TryGetValue(".table", out var tblRule)
            && tblRule.TryGetValue("margin-bottom", out var tblMbV)
            && TryParseLength(tblMbV, out var tblMb)) tableMarB = tblMb;
        if (css.TryGetValue(".table-bordered", out var tbRule)
            && tbRule.TryGetValue("border", out var tbBorderV)
            && ParseCssColor(tbBorderV) is { } tbc) borderCol = tbc;
        if (css.TryGetValue("table", out var tblBgRule)
            && tblBgRule.TryGetValue("background-color", out var tblBgV)
            && ParseCssColor(tblBgV) is { } tbg) tableBg = tbg;

        // .btn box model + variant colours.
        double btnPadY = 4.5, btnPadX = 9.0, btnLgPadY = 7.5, btnLgPadX = 12.0;
        double btnLgFs = 12.75, btnLgLineF = 1.3333333;
        if (css.TryGetValue(".btn", out var btnRule))
        {
            var btnBox = btnRule.TryGetValue("padding", out var btnPadV)
                ? ParseInlineMarginBox("margin:" + btnPadV, bodyFs) : default;
            if (btnBox.top > 0) { btnPadY = btnBox.top; btnPadX = btnBox.right; }
        }
        if (css.TryGetValue(".btn-lg", out var btnLgRule))
        {
            var lgBox = btnLgRule.TryGetValue("padding", out var lgPadV)
                ? ParseInlineMarginBox("margin:" + lgPadV, bodyFs) : default;
            if (lgBox.top > 0) { btnLgPadY = lgBox.top; btnLgPadX = lgBox.right; }
            if (btnLgRule.TryGetValue("font-size", out var lgFsV)
                && TryParseLength(lgFsV, out var lgFs)) btnLgFs = lgFs;
            if (btnLgRule.TryGetValue("line-height", out var lgLhV)
                && double.TryParse(lgLhV.Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var lgLf)) btnLgLineF = lgLf;
        }
        (Color fill, Color border, Color fg) BtnColors(string classes)
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
                        && ParseCssColor(fgv) is { } g ? g : bodyColor;
                    return (fill, border, fg);
                }
            return (Color.FromArgb(236, 236, 236), Color.FromArgb(145, 150, 156), bodyColor);
        }

        // ── parse the body into a linear block list ──
        var bodyM = Regex.Match(html, @"<body\b[^>]*>([\s\S]*)</body>", RegexOptions.IgnoreCase);
        if (!bodyM.Success) return null;
        var bodyHtml = bodyM.Groups[1].Value;

        // The MVC-template arm: a fixed navbar over a jumbotron. Both are drawn
        // from their measured chrome below and STRIPPED here so the linear walk
        // does not double-render their content; the arm also wraps paragraphs at
        // the container width, collapses a paragraph margin into a following
        // heading's, and draws <hr> rules — all reference-measured behaviours of
        // this page shape.
        var jumboDoc = Regex.IsMatch(bodyHtml, @"class\s*=\s*[""'][^""']*jumbotron",
                RegexOptions.IgnoreCase)
            && Regex.IsMatch(bodyHtml, @"class\s*=\s*[""'][^""']*navbar-fixed-top",
                RegexOptions.IgnoreCase);
        var navBrand = "";
        string jumboH1 = "", jumboLead = "", jumboBtnLabel = "", jumboBtn2Label = "";
        Color jumboBtnFill = Color.FromRgbBytes(0x33, 0x7a, 0xb7);
        Color jumboBtnBorder = Color.FromRgbBytes(0x2e, 0x6d, 0xa4);
        if (jumboDoc)
        {
            static string Cut(ref string s, string clsRe)
            {
                var m = Regex.Match(s, @"<div\b[^>]*class\s*=\s*[""'][^""']*" + clsRe + @"[^>]*>",
                    RegexOptions.IgnoreCase);
                if (!m.Success) return "";
                var end = FindDivClose(s, m.Index + m.Length);
                var inner = s[(m.Index + m.Length)..end];
                var close = s.IndexOf('>', end);
                s = s.Remove(m.Index, (close < 0 ? end : close + 1) - m.Index);
                return inner;
            }
            var navHtml = Cut(ref bodyHtml, "navbar-fixed-top");
            var brandM = Regex.Match(navHtml,
                @"class\s*=\s*[""'][^""']*navbar-brand[^>]*>(?<t>[\s\S]*?)</a>",
                RegexOptions.IgnoreCase);
            if (brandM.Success)
                navBrand = Regex.Replace(DecodeEntities(Regex.Replace(
                    brandM.Groups["t"].Value, @"<[^>]+>", " ")), @"\s+", " ").Trim();
            var jumboHtml = Cut(ref bodyHtml, "jumbotron");
            static string Flat1(string frag)
                => Regex.Replace(DecodeEntities(Regex.Replace(frag, @"<[^>]+>", " ")),
                    @"\s+", " ").Trim();
            var h1M = Regex.Match(jumboHtml, @"<h1\b[^>]*>(?<t>[\s\S]*?)</h1>", RegexOptions.IgnoreCase);
            if (h1M.Success) jumboH1 = Flat1(h1M.Groups["t"].Value);
            var leadM = Regex.Match(jumboHtml,
                @"<p\b[^>]*class\s*=\s*[""']lead[""'][^>]*>(?<t>[\s\S]*?)</p>", RegexOptions.IgnoreCase);
            if (leadM.Success) jumboLead = Flat1(leadM.Groups["t"].Value);
            var btnAM = Regex.Match(jumboHtml,
                @"<a\b[^>]*class\s*=\s*[""'][^""']*btn-lg[^>]*>(?<t>[\s\S]*?)</a>", RegexOptions.IgnoreCase);
            if (btnAM.Success)
            {
                jumboBtnLabel = Flat1(btnAM.Groups["t"].Value);
                var (f2, b2, _) = BtnColors("btn btn-primary btn-lg");
                jumboBtnFill = f2; jumboBtnBorder = b2;
            }
            var btnBM = Regex.Match(jumboHtml,
                @"<button\b[^>]*>(?<t>[\s\S]*?)</button>", RegexOptions.IgnoreCase);
            if (btnBM.Success) jumboBtn2Label = Flat1(btnBM.Groups["t"].Value);
        }

        var blocks = ParseBootstrapBlocks(bodyHtml, BtnColors);
        if (blocks.Count == 0) return null;

        // ── layout ──
        var doc = new Document();
        var page = doc.Pages.Add(pageWidth, pageHeight);
        var boldFace = face + "-Bold";
        EnsureFont(page, face.Replace(" ", ""), "FA");
        EnsureFont(page, face.Replace(" ", "") + "-Bold", "FB");

        var contentX = BsMarginX + containerPad;
        var contentW = pageWidth - 2 * BsMarginX - 2 * containerPad;
        var invc = System.Globalization.CultureInfo.InvariantCulture;

        // body background over the content box
        page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
            $"q {bodyBg.R / 255.0:0.###} {bodyBg.G / 255.0:0.###} {bodyBg.B / 255.0:0.###} rg " +
            $"{BsMarginX:F2} {BsMarginY:F2} {pageWidth - 2 * BsMarginX:F2} {pageHeight - 2 * BsMarginY:F2} re f Q\n")));

        void EmitRun(string res, double fs, double x, double yTd, string text, Color col)
        {
            page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
                $"BT {col.R / 255.0:0.###} {col.G / 255.0:0.###} {col.B / 255.0:0.###} rg " +
                $"/{res} {fs:F2} Tf 1 0 0 1 {x:F2} {pageHeight - yTd:F2} Tm ({EscapePdfString(text)}) Tj ET\n")));
        }
        // A glyphicon: the substituted system symbol face, embedded
        // as Type0; a code point that face lacks draws nothing but still advances.
        double EmitIcon(int cp, double fs, double x, double yTd, Color col)
        {
            var faceName = cp == 0x2709 || cp == 0xE003 ? "Segoe UI Symbol" : "MS Gothic";
            var ttf = Text.SystemFontResolver.Resolve(faceName);
            var s = char.ConvertFromUtf32(cp);
            var adv = BsNotdefIconAdvEm * fs;
            if (ttf is not null
                && page.Dict.Get("Resources") as Core.PdfDictionary is { } res
                && res.Get("Font") as Core.PdfDictionary is { } fontDict)
            {
                var (rn, hex) = Text.Type0FontEmbedder.Embed(fontDict, ttf, faceName, s,
                    stripSpacesInBaseFont: true);
                page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
                    $"BT {col.R / 255.0:0.###} {col.G / 255.0:0.###} {col.B / 255.0:0.###} rg " +
                    $"/{rn} {fs:F2} Tf 1 0 0 1 {x:F2} {pageHeight - yTd:F2} Tm ")
                    + "<" + System.Convert.ToHexString(hex) + "> Tj ET\n"));
                // A private-use icon has no glyph in the substitute face and keeps
                // the measured 0.75 em advance; a real symbol advances naturally.
                if (cp < 0xE000)
                {
                    var real = Text.Type0FontEmbedder.MeasureText(fontDict, ttf, faceName, s, fs,
                        stripSpacesInBaseFont: true);
                    if (real > 0.01) adv = real;
                }
            }
            return adv;
        }
        void Box(double x, double yTd, double w, double h, Color? fill, Color? stroke, double sw)
        {
            var sb = new StringBuilder("q ");
            if (fill is { } f)
                sb.Append(string.Create(invc,
                    $"{f.R / 255.0:0.###} {f.G / 255.0:0.###} {f.B / 255.0:0.###} rg {x:F2} {pageHeight - yTd - h:F2} {w:F2} {h:F2} re f "));
            if (stroke is { } st)
                sb.Append(string.Create(invc,
                    $"{st.R / 255.0:0.###} {st.G / 255.0:0.###} {st.B / 255.0:0.###} RG {sw:0.##} w " +
                    $"{x + sw / 2:F2} {pageHeight - yTd - h + sw / 2:F2} {w - sw:F2} {h - sw:F2} re S "));
            sb.Append("Q\n");
            page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
        }

        var yTd = BsMarginY;
        var lastPMarB = 0.0;                       // for the jumbo arm's collapse
        if (jumboDoc)
        {
            // The fixed navbar: its #222 band across the content box under a
            // #080808 hairline, the brand at the container origin, and the
            // toggle button's chrome at the right margin (all measured).
            var navR = pageWidth - BsMarginX;
            page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
                $"q 0.133 0.133 0.133 rg {BsMarginX:F2} {pageHeight - BsMarginY - BsNavbarHPt:F2} {navR - BsMarginX:F2} {BsNavbarHPt:F2} re f Q\n" +
                $"q 0.031 0.031 0.031 RG 0.75 w {BsMarginX:F2} {pageHeight - BsMarginY - BsNavbarHPt + 0.35:F2} m {navR:F2} {pageHeight - BsMarginY - BsNavbarHPt + 0.35:F2} l S Q\n")));
            if (navBrand.Length > 0)
                EmitRun("FA", 13.5, contentX, BsMarginY + BsBrandBaselinePt, navBrand,
                    Color.FromRgbBytes(0x9d, 0x9d, 0x9d));
            // toggle: UA outline, #333 border box, three white bars
            var tR = navR - 9.2;
            page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
                $"q 0 0 0 RG 1 w {tR - 37:F2} {pageHeight - BsMarginY - 33:F2} 37 28.5 re S Q\n" +
                $"q 0.2 0.2 0.2 RG 0.75 w {tR - 34.7:F2} {pageHeight - BsMarginY - 31.1:F2} 32.3 24.7 re S Q\n" +
                $"q 1 1 1 rg {tR - 26.8:F2} {pageHeight - BsMarginY - 15:F2} 16.5 1.5 re f " +
                $"{tR - 26.8:F2} {pageHeight - BsMarginY - 19.5:F2} 16.5 1.5 re f " +
                $"{tR - 26.8:F2} {pageHeight - BsMarginY - 24:F2} 16.5 1.5 re f Q\n")));
            // The jumbotron: the #eee container box with its measured content
            // ladder (h1, the lead lines, the btn-lg, the plain <button>).
            var jTop = BsMarginY + BsNavbarHPt - 0.75;
            var jX = contentX;
            var jW = pageWidth - BsMarginX - containerPad - jX;
            var pen = jTop + JmbPadTopPt;
            var leadLines = jumboLead.Length > 0
                ? MeasuredWordWrap(jumboLead, jW - 2 * JmbPadXPt, face, JmbLeadFsPt)
                : Array.Empty<string>();
            var jH = JmbPadTopPt + (jumboH1.Length > 0 ? JmbH1LineHPt + JmbH1MarBPt : 0)
                + leadLines.Length * JmbLeadLineHPt + JmbLeadMarBPt
                + (jumboBtnLabel.Length > 0 ? JmbBtnLgHPt + JmbBtnGapPt : 0)
                + (jumboBtn2Label.Length > 0 ? JmbBtnHPt : 0) + JmbPadBotPt;
            page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
                $"q 0.933 0.933 0.933 rg {jX:F2} {pageHeight - jTop - jH:F2} {jW:F2} {jH:F2} re f Q\n")));
            if (jumboH1.Length > 0)
            {
                EmitRun("FA", JmbH1FsPt, jX + JmbPadXPt,
                    pen + MetricBaselineDrop(JmbH1FsPt, JmbH1LineHPt, fm), jumboH1, bodyColor);
                pen += JmbH1LineHPt + JmbH1MarBPt;
            }
            foreach (var ln in leadLines)
            {
                EmitRun("FA", JmbLeadFsPt, jX + JmbPadXPt,
                    pen + MetricBaselineDrop(JmbLeadFsPt, JmbLeadLineHPt, fm), ln, bodyColor);
                pen += JmbLeadLineHPt;
            }
            pen += JmbLeadMarBPt;
            if (jumboBtnLabel.Length > 0)
            {
                var lw = MeasureFaceText(boldFace, jumboBtnLabel, btnLgFs);
                var bw = 2 * btnLgPadX + 2 * 0.75 + lw;
                Box(jX + JmbPadXPt, pen, bw, JmbBtnLgHPt, jumboBtnFill, jumboBtnBorder, 0.75);
                EmitRun("FB", btnLgFs, jX + JmbPadXPt + 0.75 + btnLgPadX,
                    pen + 0.75 + btnLgPadY + MetricBaselineDrop(btnLgFs, btnLgFs * btnLgLineF, fm),
                    jumboBtnLabel, Color.FromArgb(255, 255, 255));
                pen += JmbBtnLgHPt + JmbBtnGapPt;
            }
            if (jumboBtn2Label.Length > 0)
            {
                var lw = MeasureFaceText(boldFace, jumboBtn2Label, bodyFs);
                var bw = 2 * btnPadX + 2 * 0.75 + lw;
                Box(jX + JmbPadXPt - BsButtonOutlineX, pen - BsButtonOutlineY,
                    bw + 2 * BsButtonOutlineX, JmbBtnHPt + 2 * BsButtonOutlineY,
                    null, Color.FromArgb(0, 0, 0), 1.0);
                Box(jX + JmbPadXPt, pen, bw, JmbBtnHPt,
                    Color.FromArgb(255, 255, 255), Color.FromRgbBytes(0xcc, 0xcc, 0xcc), 0.75);
                EmitRun("FB", bodyFs, jX + JmbPadXPt + 0.75 + btnPadX,
                    pen + 0.75 + btnPadY + drop, jumboBtn2Label, bodyColor);
                pen += JmbBtnHPt;
            }
            yTd = jTop + jH + JmbMarBPt;
        }
        foreach (var blk in blocks)
        {
            switch (blk)
            {
                case BsRule:
                {
                    // only the jumbo arm draws the hr; other themes were
                    // calibrated without it
                    if (!jumboDoc) break;
                    yTd += BsHrMarginPt;
                    page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
                        $"q 0.933 0.933 0.933 RG 0.75 w {contentX:F2} {pageHeight - yTd:F2} m {pageWidth - BsMarginX - containerPad:F2} {pageHeight - yTd:F2} l S Q\n")));
                    yTd += BsHrMarginPt;
                    lastPMarB = 0;
                    break;
                }
                case BsHeading h:
                {
                    // jumbo arm: a preceding paragraph's bottom margin collapses
                    // into the heading's larger top margin
                    yTd += h2MarT - (jumboDoc ? Math.Min(lastPMarB, h2MarT) : 0);
                    EmitRun("FB", h2Fs, contentX, yTd + h2Drop, h.Text, bodyColor);
                    yTd += h2LineH + h2MarB;
                    lastPMarB = 0;
                    break;
                }
                case BsParagraph p when p.Runs.TrueForAll(r => r.Button is null):
                {
                    // jumbo arm: a plain text paragraph wraps at the container
                    // width; other themes keep their calibrated single line
                    if (jumboDoc && p.Runs.Count == 1 && p.Runs[0].Text is { } single)
                    {
                        foreach (var ln in MeasuredWordWrap(single.Trim(),
                            pageWidth - BsMarginX - containerPad - contentX, face, bodyFs))
                        {
                            EmitRun("FA", bodyFs, contentX, yTd + drop, ln, bodyColor);
                            yTd += lineH;
                        }
                        yTd += pMarB;
                        lastPMarB = pMarB;
                        break;
                    }
                    var baseline = yTd + drop;
                    var x = contentX;
                    foreach (var r in p.Runs)
                    {
                        if (r.Text is not null)
                        {
                            EmitRun("FA", bodyFs, x, baseline, r.Text, bodyColor);
                            x += MeasureFaceText(face, r.Text, bodyFs);
                        }
                        else if (r.IconCp > 0)
                            x += EmitIcon(r.IconCp, bodyFs, x, baseline + BsIconTopPt,
                                r.InLink ? linkColor : bodyColor);
                    }
                    yTd += lineH + pMarB;
                    lastPMarB = pMarB;
                    break;
                }
                case BsParagraph p:
                {
                    // a line holding a button: the CSS box tops the line, the text
                    // seats by vertical-align:middle against it
                    var btn = p.Runs.Find(r => r.Button is not null)!.Button!;
                    var fs = btn.Large ? btnLgFs : bodyFs;
                    var padY = btn.Large ? btnLgPadY : btnPadY;
                    var padX = btn.Large ? btnLgPadX : btnPadX;
                    var btnLineH = fs * (btn.Large ? btnLgLineF : lineFactor);
                    var btnH = 2 * padY + btnLineH + 2 * 0.75;
                    var baseline = yTd + btnH / 2 + xHalf * bodyFs;
                    var x = contentX;
                    foreach (var r in p.Runs)
                    {
                        if (r.Text is not null)
                        {
                            EmitRun("FA", bodyFs, x, baseline, r.Text, bodyColor);
                            x += MeasureFaceText(face, r.Text, bodyFs);
                        }
                        else if (r.IconCp > 0)
                            x += EmitIcon(r.IconCp, bodyFs, x, baseline + BsIconTopPt,
                                r.InLink ? linkColor : bodyColor);
                        else if (r.Button is { } b)
                        {
                            var labelW = MeasureFaceText(boldFace, b.Label, fs);
                            var iconW = b.IconCp > 0 ? BsNotdefIconAdvEm * fs : 0;
                            var boxW = 2 * padX + 2 * 0.75 + iconW + labelW;
                            if (b.IsButtonTag)
                                Box(x - BsButtonOutlineX, yTd - BsButtonOutlineY,
                                    boxW + 2 * BsButtonOutlineX, btnH + 2 * BsButtonOutlineY,
                                    null, Color.FromArgb(0, 0, 0), 1.0);
                            Box(x, yTd, boxW, btnH, b.Fill, b.Border, 0.75);
                            var bDrop = MetricBaselineDrop(fs, btnLineH, fm);
                            var bBase = yTd + 0.75 + padY + bDrop;
                            var bx = x + 0.75 + padX;
                            if (b.IconCp > 0)
                                bx += EmitIcon(b.IconCp, fs, bx, bBase + BsIconTopPt, b.Fg);
                            EmitRun("FB", fs, bx, bBase, b.Label, b.Fg);
                            x += boxW;
                        }
                    }
                    yTd += btnH + pMarB;
                    lastPMarB = pMarB;
                    break;
                }
                case BsTable t:
                {
                    yTd = RenderBootstrapTable(page, t, yTd, contentX, contentW,
                        pageHeight, face, boldFace, fm, bodyFs, lineH, drop,
                        cellPad, borderCol, tableBg, bodyColor, EmitRun);
                    yTd += tableMarB;
                    break;
                }
            }
        }
        return doc;
    }

    private abstract class BsBlock { }
    private sealed class BsRule : BsBlock { }
    private sealed class BsHeading : BsBlock { public string Text = ""; }
    private sealed class BsParagraph : BsBlock { public List<BsRun> Runs = new(); }
    private sealed class BsTable : BsBlock
    {
        public List<List<(string Text, bool Th)>> Rows = new();
    }

    private static List<BsBlock> ParseBootstrapBlocks(string bodyHtml,
        Func<string, (Color fill, Color border, Color fg)> btnColors)
    {
        var blocks = new List<BsBlock>();
        BsParagraph? p = null;
        BsTable? table = null;
        List<(string Text, bool Th)>? row = null;
        var text = new StringBuilder();
        string? textTarget = null;                 // "h2" | "p" | "cell" | "btn"
        var cellTh = false;
        BsButton? btn = null;
        var linkDepth = 0;

        void FlushText()
        {
            var t = Regex.Replace(DecodeEntities(text.ToString()), @"\s+", " ");
            text.Clear();
            if (textTarget == "h2" && t.Trim().Length > 0)
                blocks.Add(new BsHeading { Text = t.Trim() });
            else if (textTarget == "p" && p is not null && t.Length > 0
                     && (t != " " || p.Runs.Count > 0))
                p.Runs.Add(new BsRun { Text = p.Runs.Count == 0 ? t.TrimStart() : t });
            else if (textTarget == "cell" && row is not null && t.Trim().Length > 0)
                row.Add((t.Trim(), cellTh));
            else if (textTarget == "btn" && btn is not null && t.TrimEnd().Length > 0)
                btn.Label += t.TrimEnd();
        }

        foreach (var tok in Tokenize(bodyHtml))
        {
            if (tok.Kind == TokenKind.Text)
            {
                if (textTarget is not null) text.Append(tok.Value);
                continue;
            }
            var tag = tok.Tag!.ToLowerInvariant();
            if (tok.IsClose)
            {
                switch (tag)
                {
                    case "h2": FlushText(); textTarget = null; break;
                    case "p": FlushText(); if (p is { Runs.Count: > 0 }) blocks.Add(p); p = null; textTarget = null; break;
                    case "td" or "th": FlushText(); textTarget = null; break;
                    case "tr": if (row is { Count: > 0 }) table?.Rows.Add(row); row = null; break;
                    case "table": if (table is { Rows.Count: > 0 }) blocks.Add(table); table = null; break;
                    case "button": FlushText(); if (p is not null && btn is not null) p.Runs.Add(new BsRun { Button = btn }); btn = null; textTarget = p is not null ? "p" : null; break;
                    case "a" when linkDepth > 0:
                        linkDepth--;
                        // a link styled as a button closes like a <button>
                        if (btn is not null && !btn.IsButtonTag)
                        {
                            FlushText();
                            p?.Runs.Add(new BsRun { Button = btn });
                            btn = null;
                            textTarget = p is not null ? "p" : null;
                        }
                        break;
                }
                continue;
            }
            switch (tag)
            {
                case "h2": FlushText(); textTarget = "h2"; break;
                case "hr": FlushText(); blocks.Add(new BsRule()); break;
                case "p": FlushText(); p = new BsParagraph(); textTarget = "p"; break;
                case "table": table = new BsTable(); break;
                case "tr": row = new List<(string, bool)>(); break;
                case "td" or "th": FlushText(); cellTh = tag == "th"; textTarget = "cell"; break;
                case "span":
                    if (tok.Attributes is { } sa && sa.TryGetValue("class", out var scls)
                        && scls.Contains("glyphicon"))
                    {
                        var cp = BsGlyphiconCp(scls);
                        if (cp > 0)
                        {
                            FlushText();
                            if (btn is not null) btn.IconCp = cp;
                            else p?.Runs.Add(new BsRun { IconCp = cp, InLink = linkDepth > 0 });
                        }
                    }
                    break;
                case "button":
                    if (p is not null && tok.Attributes is { } ba
                        && ba.TryGetValue("class", out var bcls) && bcls.Contains("btn"))
                    {
                        FlushText();
                        var (fill, border, fg) = btnColors(bcls);
                        btn = new BsButton
                        {
                            IsButtonTag = true, Large = bcls.Contains("btn-lg"),
                            Fill = fill, Border = border, Fg = fg,
                        };
                        textTarget = "btn";
                    }
                    break;
                case "a":
                    linkDepth++;
                    if (p is not null && tok.Attributes is { } aa
                        && aa.TryGetValue("class", out var acls) && acls.Contains("btn"))
                    {
                        FlushText();
                        var (fill, border, fg) = btnColors(acls);
                        btn = new BsButton
                        {
                            IsButtonTag = false, Large = acls.Contains("btn-lg"),
                            Fill = fill, Border = border, Fg = fg,
                        };
                        textTarget = "btn";
                    }
                    break;
            }
        }
        return blocks;
    }

    /// <summary>The bordered Bootstrap table: white box, #c3c6c9 collapsed grid
    /// (thead bottom 2px), columns sharing the full width in proportion to their
    /// widest cell. Returns the y (top-down) below the table box.</summary>
    private static double RenderBootstrapTable(Page page, BsTable t, double yTd,
        double contentX, double contentW, double pageHeight,
        string face, string boldFace, (double asc, double sum) fm,
        double fontSize, double lineH, double drop, double pad,
        Color borderCol, Color tableBg, Color textCol,
        Action<string, double, double, double, string, Color> emitRun)
    {
        const double bw = 0.75;                    // 1px grid border
        const double thBw = 1.5;                   // thead's 2px bottom border
        var invc = System.Globalization.CultureInfo.InvariantCulture;

        var nCols = 0;
        foreach (var r in t.Rows) nCols = Math.Max(nCols, r.Count);
        if (nCols == 0) return yTd;

        // Columns: each takes its widest cell's text plus the cell chrome, and
        // the leftover width distributes in the same proportion (the browser's
        // auto-layout fill under width:100%).
        var natural = new double[nCols];
        foreach (var r in t.Rows)
            for (var c = 0; c < r.Count; c++)
                natural[c] = Math.Max(natural[c],
                    MeasureFaceText(r[c].Th ? boldFace : face, r[c].Text, fontSize));
        double natSum = 0;
        foreach (var w in natural) natSum += w;
        var chrome = nCols * (2 * pad + bw);
        var inner = contentW - bw;                 // between the outer border centers
        var scale = natSum > 0 ? (inner - chrome) / natSum : 0;
        var edge = new double[nCols + 1];
        edge[0] = contentX + bw / 2;
        for (var c = 0; c < nCols; c++)
            edge[c + 1] = edge[c] + 2 * pad + bw + natural[c] * scale;
        edge[nCols] = contentX + contentW - bw / 2;

        // Pass 1: row boundaries (each row is one line box plus padding; the
        // thead closes with its 2px border).
        var boundaries = new List<double> { yTd + bw / 2 };
        foreach (var r in t.Rows)
        {
            var isHead = r.Count > 0 && r[0].Th;
            var halfBelow = isHead ? thBw / 2 : bw / 2;
            boundaries.Add(boundaries[^1] + bw / 2 + pad + lineH + pad + halfBelow);
        }
        var bottomEdge = boundaries[^1] + bw;      // the box closes one full border below

        // Paint order: white table box, then the grid strokes, then the text.
        page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
            $"q {tableBg.R / 255.0:0.###} {tableBg.G / 255.0:0.###} {tableBg.B / 255.0:0.###} rg " +
            $"{contentX:F2} {pageHeight - bottomEdge:F2} {contentW:F2} {bottomEdge - yTd:F2} re f Q\n")));

        var sb = new StringBuilder();
        void H(double y, double w2)
            => sb.Append(string.Create(invc,
                $"{w2:0.##} w {contentX:F2} {pageHeight - y:F2} m {contentX + contentW:F2} {pageHeight - y:F2} l S "));
        void V(double x, double y0, double y1)
            => sb.Append(string.Create(invc,
                $"{bw:0.##} w {x:F2} {pageHeight - y0:F2} m {x:F2} {pageHeight - y1:F2} l S "));
        H(boundaries[0], bw);
        for (var ri = 0; ri < t.Rows.Count; ri++)
        {
            var isHead = t.Rows[ri].Count > 0 && t.Rows[ri][0].Th;
            for (var c = 0; c <= nCols; c++)
                V(edge[c], boundaries[ri] - bw / 2, boundaries[ri + 1] + bw / 2);
            H(boundaries[ri + 1], isHead ? thBw : bw);
        }
        page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
            $"q {borderCol.R / 255.0:0.###} {borderCol.G / 255.0:0.###} {borderCol.B / 255.0:0.###} RG {sb}Q\n")));

        for (var ri = 0; ri < t.Rows.Count; ri++)
        {
            var baseline = boundaries[ri] + bw / 2 + pad + drop;
            var r = t.Rows[ri];
            for (var c = 0; c < r.Count; c++)
                emitRun(r[c].Th ? "FB" : "FA", fontSize,
                    edge[c] + bw / 2 + pad, baseline, r[c].Text, textCol);
        }
        return bottomEdge;
    }
}
