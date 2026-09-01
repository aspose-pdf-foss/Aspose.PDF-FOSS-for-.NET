using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// The eValidator sequence-validation report. Its print stylesheet drives the
// pagination outright: `.header_details` breaks BEFORE itself, so the Details
// half always opens a fresh sheet, and every `.details_group_frame` breaks
// AFTER itself, so each numbered section owns its own sheet too. Between those
// breaks the boxes simply spill - a rule frame that runs out of page carries
// its side borders onto the next sheet and closes there, which is why a frame
// draws its top and bottom rules only on the sheets those edges land on.
// Every measurement below is the stylesheet's own length converted at 96 dpi,
// and every line box is Segoe UI's ascent+descent rounded to a whole device
// pixel - which is what puts the whole report on a 0.75 pt grid.
internal static partial class HtmlToPdfConverter
{
    private const double VrPxPt = 0.75;            // 96 dpi: one CSS pixel
    private const double VrAscEm = 1.0791;         // Segoe UI hhea ascent / upem
    private const double VrLineEm = 1.33008;       // its ascent + descent
    private const double VrBorderPt = 0.75;        // the frames' 1px borders
    private const double VrFramePadPt = 10.5;      // group/rule frame padding: 14px
    private const double VrRuleGapPt = 3.75;       // .details_rule_frame margin-top: 5px
    private const double VrGroupGapPt = 3.0;       // .details_group_frame margin-right: 4px
    private const double VrBarHeightPt = 27.0;     // a section bar: 26px + 4px pads + borders
    private const double VrBarGapPt = 3.75;        // its margin-bottom: 5px
    private const double VrBarPadPt = 3.0;         // its padding: 4px 0
    private const double VrBarInsetPt = 7.5;       // its items' margin-left: 10px
    private const double VrBarItemDropPt = 1.5;    // ...and their margin-top: 2px
    private const double VrHeaderPadPt = 7.5;      // .rule_header padding: 10px
    private const double VrHeaderGapPt = 7.5;      // its margin-bottom: 10px
    private const double VrBubblePt = 9.0;         // .bubble: a 12px square...
    private const double VrBubbleGapPt = 3.0;      // ...with a 4px margin-right
    private const double VrBubbleDropPt = 2.25;    // its 4px margin-top over top:-1px
    private const double VrHelpPadPt = 7.5;        // .details_rule_help_more padding: 10px 0
    private const double VrBr2Pt = 15.0;           // an empty .br2 is its 10px padding, twice
    private const double VrLinePadXPt = 9.75;      // .single-line padding: 6px 13px
    private const double VrLinePadYPt = 4.5;
    private const double VrXmlLinePt = 16.5;       // .error-xml line-height: 22px
    private const double VrXmlTopPt = 3.75;        // its margin: 5px 0 10px 0
    private const double VrXmlBottomPt = 7.5;
    private const double VrNamePadPt = 7.5;        // .details_group_name padding-bottom: 10px
    private const double VrSideLeftPt = 18.75;     // .m-lr-25 margin-left: 25px
    private const double VrSideRightPt = 30.0;     // ...and its margin-right: 40px
    private const double VrTitlePt = 13.0;         // .details_rule_name font-size
    private const double VrCommentPt = 8.0;        // .details_rule_help_more font-size
    private const double VrFindingPt = 10.0;       // .details_rule_finding font-size
    private const double VrGroupNamePt = 12.0;     // the group names inherit the 12pt frame
    private const double VrBarTextPt = 11.0;       // the section bars' font-size
    private const double VrBannerPadPt = 4.5;      // .header padding: 6px
    private const double VrBannerGapPt = 3.0;      // its margin-bottom: 4px
    private const double VrBannerTitlePt = 14.0;   // .header_report_item font-size
    private const double VrPanelPadPt = 7.5;       // .header_results padding: 10px 0 10px 10px
    private const double VrPanelMarginPt = 3.75;   // its margin: 4px 4px 5px 5px
    private const double VrInfoMarginPt = 7.5;     // .report_text_info margin: 10px 10px
    private const double VrInfoPadXPt = 7.5;       // ...and its padding: 15px 10px
    private const double VrInfoPadYPt = 11.25;
    private const double VrInfoWidthFrac = 0.95;   // .report_text_info width: 95%
    private const double VrColWidthFrac = 0.245;   // its .width-25 columns: 24.5%
    private const double VrColPadPt = 7.5;         // their span/label padding: 0 10px
    private const double VrColTextPt = 12.0;       // ...at 12pt
    private const double VrGeneralsPt = 10.0;      // .generals_frame font-size (print): 10pt
    private const double VrGeneralsLabelEm = 23.0; // .generals_label width: 23em
    private const double VrGeneralsPadPt = 3.0;    // its padding: 4px 0
    private const double VrEnvMarginXPt = 15.0;    // .m-lr-20 margin-left/right: 20px
    private const double VrEnvTableMarginPt = 4.5; // table.tbl-envelope margin: 0 6px 20px
    private const double VrEnvTableBottomPt = 15.0;
    private const double VrEnvWidthFrac = 0.98;    // ...and its width: 98%
    private const double VrEnvPadPt = 7.5;         // its padding: 10px
    private const double VrEnvCellPadPt = 3.75;    // td padding: 5px 5px
    private const double VrEnvRowPt = 24.0;        // the 14px cells' rule-to-rule pitch
    private const double VrEnvTextPt = 10.5;       // td font-size: 14px
    private const double VrEnvLabelFrac = 0.30;    // the rows' 30%/70% split
    private const double VrFileTablePt = 12.0;     // .admin_frame font-size
    private const double VrFileHeadPt = 20.25;     // .table_header_frame height: 27px
    private const double VrFileCellPadPt = 2.25;   // .table_item padding: 3px 0 0 12px
    private const double VrFileCellInsetPt = 9.0;
    private const double VrFileSplitPt = 187.5;    // its first column: width: 250px

    // The stacking order the report paints in, independent of the order the
    // flow places things: a frame's background is only measurable once its
    // contents have been laid out, so it has to sink under them at paint time.
    private const int VrLayerCanvas = 0;
    private const int VrLayerContainer = 1;   // the page-wide white panels
    private const int VrLayerFrame = 2;       // a rule frame's own white
    private const int VrLayerBand = 3;        // the grey rule headers, the brand fills
    private const int VrLayerBubble = 4;      // the status squares
    private const int VrLayerStroke = 5;
    private const int VrLayerText = 6;

    private static readonly Color VrPageBg = Color.FromRgbBytes(0xF2, 0xF5, 0xF7);
    private static readonly Color VrWhite = Color.FromRgbBytes(0xFF, 0xFF, 0xFF);
    private static readonly Color VrFrameBorder = Color.FromRgbBytes(0xE2, 0xE2, 0xE2);
    private static readonly Color VrRuleBorder = Color.FromRgbBytes(0xCE, 0xD2, 0xD3);
    private static readonly Color VrBarBorder = Color.FromRgbBytes(0xEC, 0xEC, 0xEC);
    private static readonly Color VrBand = Color.FromRgbBytes(0xEE, 0xEE, 0xEE);
    private static readonly Color VrBrand = Color.FromRgbBytes(0x23, 0x67, 0xAB);
    private static readonly Color VrInk = Color.FromRgbBytes(0x66, 0x66, 0x66);
    private static readonly Color VrDarkInk = Color.FromRgbBytes(0x33, 0x33, 0x33);
    private static readonly Color VrLinkInk = Color.FromRgbBytes(0x2A, 0x7F, 0xDA);
    private static readonly Color VrErrorInk = Color.FromRgbBytes(0xDE, 0x29, 0x29);
    private static readonly Color VrBannerInk = Color.FromRgbBytes(0xF6, 0xF5, 0xF4);

    /// <summary>A browser line box: the face's own ascent+descent, rounded to a
    /// whole device pixel.</summary>
    private static double VrLineH(double sizePt)
        => Math.Round(sizePt / VrPxPt * VrLineEm, MidpointRounding.AwayFromZero) * VrPxPt;

    private sealed class VrFinding
    {
        public string Text = "";
        public string Xml = "";
    }

    private sealed class VrRule
    {
        public Color Bubble = Color.FromRgbBytes(0x99, 0xCC, 0x33);
        public string Title = "";
        public string Comment = "";
        public string Path = "";
        public List<VrFinding> Findings = new();
    }

    private sealed class VrGroup
    {
        public string Name = "";
        public Color Bubble = Color.FromRgbBytes(0xDE, 0x29, 0x29);
        public List<VrRule> Rules = new();
    }

    /// <summary>Render the eValidator sequence-validation report, or null when
    /// the document is not one.</summary>
    private static Document? TryRenderValidationReport(string html,
        double pageWidth, double pageHeight, double marginLeft, double marginRight,
        double marginTop, double marginBottom)
    {
        if (!Regex.IsMatch(html, @"class\s*=\s*[""']details_rule_frame", RegexOptions.IgnoreCase)
            || !Regex.IsMatch(html, @"class\s*=\s*[""']details_group_frame", RegexOptions.IgnoreCase)
            || !Regex.IsMatch(html, @"class\s*=\s*[""']rule_header", RegexOptions.IgnoreCase))
            return null;
        var faceReg = Text.SystemFontResolver.Resolve("Segoe UI");
        var faceIt = Text.SystemFontResolver.Resolve("SegoeUI-Italic")
            ?? Text.SystemFontResolver.Resolve("Segoe UI Italic");
        var faceSemi = Text.SystemFontResolver.Resolve("SegoeUI-Semibold")
            ?? Text.SystemFontResolver.Resolve("Segoe UI Semibold") ?? faceReg;
        if (faceReg is null || faceIt is null || faceSemi is null) return null;

        var contentH = pageHeight - marginTop - marginBottom;
        if (contentH <= VrBarHeightPt) return null;
        var bodyM = Regex.Match(html, @"<body\b[^>]*>([\s\S]*)</body", RegexOptions.IgnoreCase);
        var src = bodyM.Success ? bodyM.Groups[1].Value : html;
        var groups = VrParseGroups(src);
        if (groups.Count < 2) return null;

        var doc = new Document();
        var pages = new List<Page>();
        var invc = System.Globalization.CultureInfo.InvariantCulture;

        // The report is laid out in document order but PAINTED in stacking
        // order: a rule frame's own background is measured only once its
        // contents have been placed, so every operator is banked against the
        // layer it belongs to and the sheets are written out at the end.
        var ops = new List<(int Sheet, int Layer, int Seq, string Text)>();
        var seq = 0;

        string Rgb(Color c, string op)
            => string.Create(invc,
                $"{c.R / 255.0:0.###} {c.G / 255.0:0.###} {c.B / 255.0:0.###} {op} ");

        Page PageAt(int i)
        {
            while (pages.Count <= i)
            {
                var p = doc.Pages.Add(pageWidth, pageHeight);
                EnsureFonts(p);
                ops.Add((pages.Count, VrLayerCanvas, seq++, string.Create(invc,
                    $"q {Rgb(VrPageBg, "rg")}{marginLeft:0.##} {marginBottom:0.##} "
                    + $"{pageWidth - marginLeft - marginRight:0.##} {contentH:0.##} re f Q")));
                pages.Add(p);
            }
            return pages[i];
        }

        void Emit(int sheet, int layer, string text)
        {
            PageAt(sheet);
            ops.Add((sheet, layer, seq++, text));
        }

        // Y runs continuously through the report; the sheet it lands on and the
        // offset inside that sheet fall straight out of the content height.
        (int Sheet, double Top) Loc(double y)
        {
            var i = Math.Max(0, (int)Math.Floor(y / contentH + 1e-9));
            return (i, marginTop + (y - i * contentH));
        }

        void Fill(double y0, double y1, double x, double w, Color c, int layer)
        {
            if (y1 - y0 <= 1e-6 || w <= 0) return;
            var last = Loc(y1 - 1e-6).Sheet;
            for (var i = Loc(y0).Sheet; i <= last; i++)
            {
                var top = Math.Max(y0, i * contentH);
                var bot = Math.Min(y1, (i + 1) * contentH);
                if (bot - top <= 1e-6) continue;
                var yTop = marginTop + (top - i * contentH);
                Emit(i, layer, string.Create(invc,
                    $"q {Rgb(c, "rg")}{x:0.##} {pageHeight - yTop - (bot - top):0.##} "
                    + $"{w:0.##} {bot - top:0.##} re f Q"));
            }
        }

        void HRule(double y, double x0, double x1, Color c)
        {
            var (i, top) = Loc(y);
            Emit(i, VrLayerStroke, string.Create(invc,
                $"q {Rgb(c, "RG")}{VrBorderPt:0.##} w {x0:0.##} {pageHeight - top:0.##} m "
                + $"{x1:0.##} {pageHeight - top:0.##} l S Q"));
        }

        void VRule(double y0, double y1, double x, Color c)
        {
            if (y1 - y0 <= 1e-6) return;
            var last = Loc(y1 - 1e-6).Sheet;
            for (var i = Loc(y0).Sheet; i <= last; i++)
            {
                var top = Math.Max(y0, i * contentH);
                var bot = Math.Min(y1, (i + 1) * contentH);
                if (bot - top <= 1e-6) continue;
                var yTop = marginTop + (top - i * contentH);
                Emit(i, VrLayerStroke, string.Create(invc,
                    $"q {Rgb(c, "RG")}{VrBorderPt:0.##} w {x:0.##} {pageHeight - yTop:0.##} m "
                    + $"{x:0.##} {pageHeight - yTop - (bot - top):0.##} l S Q"));
            }
        }

        // A bordered box: the sides run the whole span, while the top and the
        // bottom rule land only on the sheets those edges fall on.
        void Box(double y0, double y1, double x0, double x1, Color? fill, Color border,
            int layer = VrLayerFrame)
        {
            if (fill is { } f) Fill(y0, y1, x0, x1 - x0, f, layer);
            VRule(y0, y1, x0 + VrBorderPt / 2, border);
            VRule(y0, y1, x1 - VrBorderPt / 2, border);
            HRule(y0 + VrBorderPt / 2, x0, x1, border);
            HRule(y1 - VrBorderPt / 2, x0, x1, border);
        }

        double Measure(byte[] ttf, string name, string s, double size)
        {
            if (PageAt(0).Dict.Get("Resources") is not Core.PdfDictionary res
                || res.Get("Font") is not Core.PdfDictionary fd) return s.Length * size * 0.5;
            return Text.Type0FontEmbedder.MeasureText(fd, ttf, name, s, size,
                stripSpacesInBaseFont: true);
        }

        void Run(double lineTop, double x, double size, byte[] ttf, string name,
            string s, Color c)
        {
            if (s.Length == 0) return;
            var (i, top) = Loc(lineTop);
            var pg = PageAt(i);
            if (pg.Dict.Get("Resources") is not Core.PdfDictionary res
                || res.Get("Font") is not Core.PdfDictionary fd) return;
            var (rn, hex) = Text.Type0FontEmbedder.Embed(fd, ttf, name, s,
                stripSpacesInBaseFont: true);
            // half-leading inside the rounded line box, then the face's ascent
            var baseline = top + (VrLineH(size) - size * VrLineEm) / 2 + size * VrAscEm;
            Emit(i, VrLayerText, string.Create(invc,
                $"BT {Rgb(c, "rg")}/{rn} {size:0.##} Tf 1 0 0 1 {x:0.##} "
                + $"{pageHeight - baseline:0.##} Tm ")
                + "<" + System.Convert.ToHexString(hex) + "> Tj ET");
        }

        List<string> Wrap(byte[] ttf, string name, string s, double size, double width)
        {
            var outp = new List<string>();
            var cur = "";
            foreach (var w in s.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var t = cur.Length == 0 ? w : cur + " " + w;
                if (cur.Length > 0 && Measure(ttf, name, t, size) > width)
                { outp.Add(cur); cur = w; }
                else cur = t;
            }
            if (cur.Length > 0) outp.Add(cur);
            if (outp.Count == 0) outp.Add("");
            return outp;
        }

        var boxL = marginLeft + VrSideLeftPt;
        var boxR = pageWidth - marginRight - VrSideRightPt;

        double Bar(string itemClass, double top)
        {
            var x = boxL + VrBorderPt + VrBarInsetPt;
            var ty = top + VrBorderPt + VrBarPadPt + VrBarItemDropPt;
            foreach (var t in VrTexts(src, itemClass))
            {
                Run(ty, x, VrBarTextPt, faceReg, "SegoeUI", t, VrInk);
                x += Measure(faceReg, "SegoeUI", t, VrBarTextPt) + VrBarInsetPt;
            }
            Box(top, top + VrBarHeightPt, boxL, boxR, VrWhite, VrBarBorder, VrLayerContainer);
            return top + VrBarHeightPt + VrBarGapPt;
        }

        var y = 0.0;

        // == the brand banner ================================================
        var bannerTop = y;
        y += VrBannerPadPt;
        var items = VrTexts(src, "header_report_item");
        for (var i = 0; i < items.Count; i++)
        {
            var size = i == 0 ? VrBannerTitlePt : VrColTextPt;
            Run(y, marginLeft + VrBannerPadPt, size, faceReg, "SegoeUI", items[i], VrBannerInk);
            y += VrLineH(size);
        }
        y += VrBannerPadPt;
        Fill(bannerTop, y, marginLeft, pageWidth - marginLeft - marginRight, VrBrand,
            VrLayerBand);
        y += VrBannerGapPt;

        // == the four-column results panel ===================================
        var panelL = marginLeft + VrPanelMarginPt;
        var panelR = pageWidth - marginRight - VrGroupGapPt;
        var panelContentW = panelR - panelL - VrPanelPadPt;
        var infoL = panelL + VrPanelPadPt + VrInfoMarginPt;
        var infoW = panelContentW * VrInfoWidthFrac + 2 * VrInfoPadXPt;
        var infoTop = y + VrPanelPadPt + VrInfoMarginPt;
        var colW = (infoW - 2 * VrInfoPadXPt) * VrColWidthFrac;
        var colTop = infoTop + VrInfoPadYPt;
        var colLines = 1;
        var cols = VrColumns(src);
        for (var i = 0; i < cols.Count; i++)
        {
            var cy = colTop;
            var cx = infoL + VrInfoPadXPt + i * colW + VrColPadPt;
            foreach (var ln in Wrap(faceReg, "SegoeUI", cols[i].Label, VrColTextPt,
                         colW - 2 * VrColPadPt))
            { Run(cy, cx, VrColTextPt, faceReg, "SegoeUI", ln, VrInk); cy += VrLineH(VrColTextPt); }
            foreach (var ln in Wrap(faceSemi, "SegoeUISemibold", cols[i].Value, VrColTextPt,
                         colW - 2 * VrColPadPt))
            {
                Run(cy, cx, VrColTextPt, faceSemi, "SegoeUISemibold", ln, VrDarkInk);
                cy += VrLineH(VrColTextPt);
            }
            colLines = Math.Max(colLines, (int)Math.Round((cy - colTop) / VrLineH(VrColTextPt)));
        }
        var infoBottom = colTop + colLines * VrLineH(VrColTextPt) + VrInfoPadYPt;
        Fill(infoTop, infoBottom, infoL, infoW, VrWhite, VrLayerContainer);
        y = infoBottom + VrInfoMarginPt + VrPanelPadPt + VrPanelMarginPt;

        // == "General Information" over its label/value grid ==================
        y = Bar("header_generals_item", y);
        var genTop = y;
        var gen = VrPairs(src, "generals_label", "generals_value");
        var genContentTop = genTop + VrBorderPt + VrPanelPadPt;
        var genLabelX = boxL + VrBorderPt + VrPanelPadPt;
        var genValueX = genLabelX + VrGeneralsLabelEm * VrGeneralsPt;
        var genRowH = 2 * VrGeneralsPadPt + VrLineH(VrGeneralsPt);
        for (var i = 0; i < gen.Count; i++)
        {
            var top = genContentTop + i * (genRowH + VrBorderPt);
            Run(top + VrGeneralsPadPt, genLabelX, VrGeneralsPt, faceReg, "SegoeUI",
                gen[i].Label, VrInk);
            Run(top + VrGeneralsPadPt, genValueX, VrGeneralsPt, faceReg, "SegoeUI",
                gen[i].Value, VrInk);
            // .last_row drops its rule
            if (i == gen.Count - 1) continue;
            HRule(top + genRowH + VrBorderPt / 2, genLabelX, genValueX, VrRuleBorder);
            HRule(top + genRowH + VrBorderPt / 2, genValueX, boxR - VrGroupGapPt - VrBorderPt,
                VrRuleBorder);
        }
        var genBottom = genContentTop + gen.Count * (genRowH + VrBorderPt) - VrBorderPt
            + VrPanelPadPt + VrBorderPt;
        Box(genTop, genBottom, boxL, boxR, VrWhite, VrFrameBorder, VrLayerContainer);
        y = genBottom + VrBarGapPt;

        // == "Envelope Information" over its two tables =======================
        y = Bar("header_admin_item", y);
        var envL = marginLeft + VrEnvMarginXPt + VrEnvTableMarginPt;
        var envW = (pageWidth - marginLeft - marginRight - 2 * VrEnvMarginXPt
            - 2 * VrEnvTableMarginPt) * VrEnvWidthFrac;
        var envTop = y;
        var envRows = VrEnvelopeRows(src);
        var envSplit = envL + VrEnvPadPt + envW * VrEnvLabelFrac;
        for (var i = 0; i < envRows.Count; i++)
        {
            var top = envTop + VrEnvPadPt + i * VrEnvRowPt + VrEnvCellPadPt;
            Run(top, envL + VrEnvPadPt + VrEnvCellPadPt, VrEnvTextPt, faceReg, "SegoeUI",
                envRows[i].Label, VrDarkInk);
            Run(top, envSplit + VrEnvCellPadPt, VrEnvTextPt, faceReg, "SegoeUI",
                envRows[i].Value, VrDarkInk);
            if (i == envRows.Count - 1) continue;
            HRule(top + VrEnvRowPt - VrEnvCellPadPt - VrBorderPt / 2, envL + VrEnvPadPt,
                envL + envW - VrEnvPadPt, VrRuleBorder);
        }
        var envBottom = envTop + 2 * VrEnvPadPt + envRows.Count * VrEnvRowPt;
        Box(envTop, envBottom, envL, envL + envW, VrWhite, VrBand, VrLayerContainer);
        y = envBottom + VrEnvTableBottomPt;

        // == the File / Path table ============================================
        var fileTop = y;
        var fileHeadTop = fileTop + VrBorderPt + VrPanelPadPt;
        var fileX = boxL + VrBorderPt + VrPanelPadPt + VrFileCellInsetPt;
        var fileSplit = fileX + VrFileSplitPt;
        Fill(fileHeadTop, fileHeadTop + VrFileHeadPt, boxL + VrBorderPt + VrPanelPadPt,
            boxR - boxL - VrBorderPt - VrPanelPadPt, VrBrand, VrLayerBand);
        Run(fileHeadTop + VrFileCellPadPt, fileX, VrFileTablePt, faceReg, "SegoeUI",
            "File", VrBannerInk);
        Run(fileHeadTop + VrFileCellPadPt, fileSplit, VrFileTablePt, faceReg, "SegoeUI",
            "Path", VrBannerInk);
        var fy = fileHeadTop + VrFileHeadPt + VrFileCellPadPt;
        foreach (var (label, path) in VrFileRows(src))
        {
            var rowTop = fy;
            Run(fy, fileX, VrFileTablePt, faceReg, "SegoeUI", label, VrInk);
            var pathY = fy;
            foreach (var ln in Wrap(faceReg, "SegoeUI", path, VrFileTablePt,
                         boxR - VrPanelPadPt - fileSplit))
            {
                Run(pathY, fileSplit, VrFileTablePt, faceReg, "SegoeUI", ln, VrLinkInk);
                pathY += VrLineH(VrFileTablePt);
            }
            fy = Math.Max(pathY, rowTop + VrLineH(VrFileTablePt)) + VrFileCellPadPt;
            VRule(rowTop - VrFileCellPadPt, fy - VrFileCellPadPt,
                fileSplit - VrFileCellInsetPt, VrRuleBorder);
        }
        Box(fileTop, fy + VrPanelPadPt, boxL, boxR, VrWhite, VrFrameBorder, VrLayerContainer);

        // == the Details half, which the print sheet opens on its own page ====
        y = Math.Ceiling((fy + VrPanelPadPt + 1e-6) / contentH) * contentH;
        y = Bar("header_details_item", y);

        var listTop = y;
        var outerR = boxR - VrGroupGapPt;
        var outerContentL = boxL + VrBorderPt + VrFramePadPt;
        var outerContentR = outerR - VrBorderPt - VrFramePadPt;
        y += VrBorderPt + VrFramePadPt;

        Fill(y + VrBubbleDropPt, y + VrBubbleDropPt + VrBubblePt, outerContentL, VrBubblePt,
            groups[0].Bubble, VrLayerBubble);
        Run(y, outerContentL + VrBubblePt + VrBubbleGapPt, VrGroupNamePt, faceReg, "SegoeUI",
            groups[0].Name, VrInk);
        y += VrLineH(VrGroupNamePt) + VrNamePadPt;

        var listBottom = y;
        for (var gi = 1; gi < groups.Count; gi++)
        {
            var g = groups[gi];
            var innerR = outerContentR - VrGroupGapPt;
            var innerTop = y + VrGroupGapPt;
            var innerContentL = outerContentL + VrBorderPt + VrFramePadPt;
            var innerContentR = innerR - VrBorderPt - VrFramePadPt;
            var iy = innerTop + VrBorderPt + VrFramePadPt;
            Fill(iy + VrBubbleDropPt, iy + VrBubbleDropPt + VrBubblePt, innerContentL,
                VrBubblePt, g.Bubble, VrLayerBubble);
            Run(iy, innerContentL + VrBubblePt + VrBubbleGapPt, VrGroupNamePt, faceReg,
                "SegoeUI", g.Name, VrInk);
            iy += VrLineH(VrGroupNamePt) + VrNamePadPt;

            foreach (var r in g.Rules)
            {
                var frameTop = iy + VrRuleGapPt;
                var frameL = innerContentL;
                var frameR = innerContentR - VrGroupGapPt;
                var cl = frameL + VrBorderPt + VrFramePadPt;
                var cr = frameR - VrBorderPt - VrFramePadPt;
                var ry = frameTop + VrBorderPt + VrFramePadPt;

                // .rule_header's -14px margin pulls its band back out over the
                // frame's padding, so the band spans the frame edge to edge
                var bandTop = ry - VrFramePadPt;
                var bandBottom = bandTop + 2 * VrHeaderPadPt + VrLineH(VrTitlePt);
                Fill(bandTop, bandBottom, frameL + VrBorderPt,
                    frameR - frameL - 2 * VrBorderPt, VrBand, VrLayerBand);
                Fill(ry + VrBubbleDropPt, ry + VrBubbleDropPt + VrBubblePt, cl, VrBubblePt,
                    r.Bubble, VrLayerBubble);
                Run(bandTop + VrHeaderPadPt, cl + VrBubblePt + VrBubbleGapPt, VrTitlePt,
                    faceReg, "SegoeUI", r.Title, VrInk);
                ry = bandBottom + VrHeaderGapPt + VrHelpPadPt;

                foreach (var ln in Wrap(faceIt, "SegoeUIItalic", r.Comment, VrCommentPt, cr - cl))
                {
                    Run(ry, cl, VrCommentPt, faceIt, "SegoeUIItalic", ln, VrInk);
                    ry += VrLineH(VrCommentPt);
                }
                ry += VrHelpPadPt;

                if (r.Path.Length > 0)
                {
                    foreach (var ln in Wrap(faceReg, "SegoeUI", r.Path, VrFindingPt, cr - cl))
                    {
                        Run(ry, cl, VrFindingPt, faceReg, "SegoeUI", ln, VrLinkInk);
                        ry += VrLineH(VrFindingPt);
                    }
                    ry += VrBr2Pt;
                }
                foreach (var f in r.Findings)
                {
                    var slTop = ry;
                    var tl = cl + VrBorderPt + VrLinePadXPt;
                    var tr = cr - VrBorderPt - VrLinePadXPt;
                    var ty = slTop + VrBorderPt + VrLinePadYPt;
                    foreach (var ln in Wrap(faceReg, "SegoeUI", f.Text, VrFindingPt, tr - tl))
                    {
                        Run(ty, tl, VrFindingPt, faceReg, "SegoeUI", ln, VrInk);
                        ty += VrLineH(VrFindingPt);
                    }
                    ty += VrBr2Pt + VrXmlTopPt;
                    foreach (var ln in Wrap(faceReg, "SegoeUI", f.Xml, VrFindingPt, tr - tl))
                    {
                        // .error-xml sets its own 22px line box
                        Run(ty + (VrXmlLinePt - VrLineH(VrFindingPt)) / 2, tl, VrFindingPt,
                            faceReg, "SegoeUI", ln, VrErrorInk);
                        ty += VrXmlLinePt;
                    }
                    ty += VrXmlBottomPt + VrLinePadYPt + VrBorderPt;
                    Box(slTop, ty, cl, cr, null, VrRuleBorder);
                    ry = ty;
                }

                var frameBottom = ry + VrFramePadPt + VrBorderPt;
                Box(frameTop, frameBottom, frameL, frameR, VrWhite, VrRuleBorder);
                iy = frameBottom;
            }

            var innerBottom = iy + VrFramePadPt + VrBorderPt;
            Box(innerTop, innerBottom, outerContentL, innerR, null, VrFrameBorder);
            listBottom = innerBottom + VrFramePadPt + VrBorderPt;
            // .details_group_frame { page-break-after: always }
            y = Math.Ceiling((innerBottom + 1e-6) / contentH) * contentH;
        }

        Fill(listTop, listBottom, boxL, boxR - boxL, VrWhite, VrLayerContainer);
        VRule(listTop, listBottom, boxL + VrBorderPt / 2, VrFrameBorder);
        VRule(listTop, listBottom, outerR - VrBorderPt / 2, VrFrameBorder);
        HRule(listBottom - VrBorderPt / 2, boxL, outerR, VrFrameBorder);

        // the banked operators, written out sheet by sheet in stacking order
        foreach (var g in ops.GroupBy(o => o.Sheet))
        {
            var sb = new StringBuilder();
            foreach (var o in g.OrderBy(o => o.Layer).ThenBy(o => o.Seq))
                sb.Append(o.Text).Append('\n');
            pages[g.Key].AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
        }
        return doc;
    }

    /// <summary>The inner html of the div whose opening tag starts at
    /// <paramref name="open"/>, matched over nested divs.</summary>
    private static string VrDivAt(string s, int open)
    {
        var i = s.IndexOf('>', open);
        if (i < 0) return "";
        var depth = 1;
        foreach (Match m in Regex.Matches(s[(i + 1)..], @"</?div\b", RegexOptions.IgnoreCase))
        {
            depth += m.Value[1] == '/' ? -1 : 1;
            if (depth == 0) return s.Substring(i + 1, m.Index);
        }
        return s[(i + 1)..];
    }

    private static string VrFlat(string frag)
        => Regex.Replace(DecodeEntities(Regex.Replace(frag, "<[^>]+>", " ")), @"\s+", " ").Trim();

    private static string VrOpenTag(string cls)
        => @"<div\b[^>]*class\s*=\s*[""'][^""']*\b" + Regex.Escape(cls) + @"\b[^""']*[""'][^>]*>";

    private static List<string> VrTexts(string s, string cls)
    {
        var outp = new List<string>();
        foreach (Match m in Regex.Matches(s, VrOpenTag(cls), RegexOptions.IgnoreCase))
        {
            var t = VrFlat(VrDivAt(s, m.Index));
            if (t.Length > 0) outp.Add(t);
        }
        return outp;
    }

    private static List<(string Label, string Value)> VrColumns(string s)
    {
        var outp = new List<(string, string)>();
        foreach (Match m in Regex.Matches(s, VrOpenTag("width-25"), RegexOptions.IgnoreCase))
        {
            var inner = VrDivAt(s, m.Index);
            var sp = Regex.Match(inner, "<span[^>]*>([^<]*)</span", RegexOptions.IgnoreCase);
            var lb = Regex.Match(inner, "<label[^>]*>([^<]*)</label", RegexOptions.IgnoreCase);
            outp.Add((VrFlat(sp.Groups[1].Value), VrFlat(lb.Groups[1].Value)));
        }
        return outp;
    }

    private static List<(string Label, string Value)> VrPairs(string s, string a, string b)
    {
        var labels = VrTexts(s, a);
        var values = VrTexts(s, b);
        var outp = new List<(string, string)>();
        for (var i = 0; i < labels.Count; i++)
            outp.Add((labels[i], i < values.Count ? values[i] : ""));
        return outp;
    }

    private static List<(string Label, string Value)> VrEnvelopeRows(string s)
    {
        var outp = new List<(string, string)>();
        var t = Regex.Match(s, @"<table\b[^>]*tbl-envelope[^>]*>([\s\S]*?)</table",
            RegexOptions.IgnoreCase);
        if (!t.Success) return outp;
        foreach (Match r in Regex.Matches(t.Groups[1].Value, @"<tr\b[^>]*>([\s\S]*?)</tr",
                     RegexOptions.IgnoreCase))
        {
            var tds = Regex.Matches(r.Groups[1].Value, @"<td\b[^>]*>([\s\S]*?)</td",
                RegexOptions.IgnoreCase);
            if (tds.Count >= 2)
                outp.Add((VrFlat(tds[0].Groups[1].Value), VrFlat(tds[1].Groups[1].Value)));
        }
        return outp;
    }

    private static List<(string Label, string Path)> VrFileRows(string s)
    {
        var outp = new List<(string, string)>();
        var f = Regex.Match(s, VrOpenTag("admin_frame"), RegexOptions.IgnoreCase);
        if (!f.Success) return outp;
        var frame = VrDivAt(s, f.Index);
        foreach (Match r in Regex.Matches(frame, @"<tr\b[^>]*>([\s\S]*?)</tr",
                     RegexOptions.IgnoreCase))
        {
            var tds = Regex.Matches(r.Groups[1].Value, @"<td\b[^>]*>([\s\S]*?)</td",
                RegexOptions.IgnoreCase);
            if (tds.Count < 2) continue;
            // the link's own href quotes markup, so the cell text is the
            // innermost bold run rather than everything outside the tags
            var b = Regex.Match(tds[1].Groups[1].Value, "<b><b>([^<]*)</b>",
                RegexOptions.IgnoreCase);
            outp.Add((VrFlat(tds[0].Groups[1].Value),
                b.Success ? VrFlat(b.Groups[1].Value) : VrFlat(tds[1].Groups[1].Value)));
        }
        return outp;
    }

    /// <summary>The status colour a bubble carries, either inline or through
    /// its region-status class.</summary>
    private static Color VrBubbleColor(string tag)
    {
        var st = Regex.Match(tag, @"background(?:-color)?\s*:\s*#([0-9a-f]{6})",
            RegexOptions.IgnoreCase);
        if (st.Success)
            return Color.FromRgbBytes(System.Convert.ToInt32(st.Groups[1].Value[..2], 16),
                System.Convert.ToInt32(st.Groups[1].Value.Substring(2, 2), 16),
                System.Convert.ToInt32(st.Groups[1].Value.Substring(4, 2), 16));
        if (tag.Contains("pass", StringComparison.OrdinalIgnoreCase))
            return Color.FromRgbBytes(0x99, 0xCC, 0x33);
        if (tag.Contains("syserror", StringComparison.OrdinalIgnoreCase))
            return Color.FromRgbBytes(0xC1, 0x3E, 0xB3);
        if (tag.Contains("info", StringComparison.OrdinalIgnoreCase))
            return Color.FromRgbBytes(0x00, 0x7F, 0xFF);
        if (tag.Contains("low", StringComparison.OrdinalIgnoreCase))
            return Color.FromRgbBytes(0xF5, 0xCC, 0x00);
        return Color.FromRgbBytes(0xDE, 0x29, 0x29);
    }

    /// <summary>The report's groups: the wrapper first, then one per numbered
    /// section. A group the sheet hides contributes nothing at all.</summary>
    private static List<VrGroup> VrParseGroups(string html)
    {
        var outp = new List<VrGroup>();
        var list = Regex.Match(html, @"<div\b[^>]*id\s*=\s*[""']detail_list[""'][^>]*>",
            RegexOptions.IgnoreCase);
        if (!list.Success) return outp;
        var s = VrDivAt(html, list.Index);
        foreach (Match m in Regex.Matches(s, VrOpenTag("details_group_frame"),
                     RegexOptions.IgnoreCase))
        {
            if (Regex.IsMatch(m.Value, @"display\s*:\s*none", RegexOptions.IgnoreCase)) continue;
            var inner = VrDivAt(s, m.Index);
            var g = new VrGroup();
            var name = Regex.Match(inner, VrOpenTag("details_group_name"),
                RegexOptions.IgnoreCase);
            if (name.Success) g.Name = VrFlat(VrDivAt(inner, name.Index));
            var bub = Regex.Match(inner, VrOpenTag("bubble"), RegexOptions.IgnoreCase);
            if (bub.Success) g.Bubble = VrBubbleColor(bub.Value);
            foreach (Match rm in Regex.Matches(inner, VrOpenTag("details_rule_frame"),
                         RegexOptions.IgnoreCase))
                g.Rules.Add(VrParseRule(VrDivAt(inner, rm.Index), rm.Value));
            outp.Add(g);
        }
        return outp;
    }

    private static VrRule VrParseRule(string inner, string openTag)
    {
        var r = new VrRule();
        var bub = Regex.Match(inner, VrOpenTag("bubble"), RegexOptions.IgnoreCase);
        r.Bubble = VrBubbleColor(bub.Success ? bub.Value : openTag);
        var head = Regex.Match(inner, VrOpenTag("rule_header"), RegexOptions.IgnoreCase);
        if (head.Success) r.Title = VrFlat(VrDivAt(inner, head.Index));
        var help = Regex.Match(inner, VrOpenTag("corrective_action"), RegexOptions.IgnoreCase);
        if (help.Success) r.Comment = VrFlat(VrDivAt(inner, help.Index));
        var det = Regex.Match(inner, VrOpenTag("error_details"), RegexOptions.IgnoreCase);
        if (!det.Success) return r;
        var ed = VrDivAt(inner, det.Index);
        var link = Regex.Match(ed, @"<a\b[^>]*>([^<]*)</a>", RegexOptions.IgnoreCase);
        if (link.Success) r.Path = VrFlat(link.Groups[1].Value);
        foreach (Match sl in Regex.Matches(ed, VrOpenTag("single-line"), RegexOptions.IgnoreCase))
        {
            var body = VrDivAt(ed, sl.Index);
            var cut = body.IndexOf('<');
            var xml = Regex.Match(body, VrOpenTag("error-xml"), RegexOptions.IgnoreCase);
            r.Findings.Add(new VrFinding
            {
                Text = VrFlat(cut < 0 ? body : body[..cut]),
                Xml = xml.Success ? VrFlat(VrDivAt(body, xml.Index)) : "",
            });
        }
        return r;
    }
}
