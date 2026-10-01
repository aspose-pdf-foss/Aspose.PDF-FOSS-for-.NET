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
        var vr = new ValidationReportState();
        vr.pageWidth = pageWidth;
        vr.pageHeight = pageHeight;
        vr.marginLeft = marginLeft;
        vr.marginRight = marginRight;
        vr.marginTop = marginTop;
        vr.marginBottom = marginBottom;
        vr.faceReg = (Text.SystemFontResolver.Resolve("Segoe UI"))!;
        vr.faceIt = (Text.SystemFontResolver.Resolve("SegoeUI-Italic")
            ?? Text.SystemFontResolver.Resolve("Segoe UI Italic"))!;
        vr.faceSemi = (Text.SystemFontResolver.Resolve("SegoeUI-Semibold")
            ?? Text.SystemFontResolver.Resolve("Segoe UI Semibold") ?? vr.faceReg)!;
        if (vr.faceReg is null || vr.faceIt is null || vr.faceSemi is null) return null;

        vr.contentH = vr.pageHeight - vr.marginTop - vr.marginBottom;
        if (vr.contentH <= VrBarHeightPt) return null;
        vr.bodyM = Regex.Match(html, @"<body\b[^>]*>([\s\S]*)</body", RegexOptions.IgnoreCase);
        vr.src = vr.bodyM.Success ? vr.bodyM.Groups[1].Value : html;
        vr.groups = VrParseGroups(vr.src);
        if (vr.groups.Count < 2) return null;

        vr.doc = new Document();
        vr.pages = new List<Page>();
        vr.invc = System.Globalization.CultureInfo.InvariantCulture;

        vr.ops = new List<(int Sheet, int Layer, int Seq, string Text)>();
        vr.seq = 0;

        vr.boxL = vr.marginLeft + VrSideLeftPt;
        vr.boxR = vr.pageWidth - vr.marginRight - VrSideRightPt;

        vr.y = 0.0;

        DrawBanner(vr);

        DrawInfoPanel(vr);

        // == "General Information" over its label/value grid ==================
        DrawGeneralsPanel(vr);

        // == "Envelope Information" over its two tables =======================
        DrawEnvelopePanel(vr);

        DrawFilesPanel(vr);
        vr.y = Bar(vr, "header_details_item", vr.y);

        vr.listTop = vr.y;
        vr.outerR = vr.boxR - VrGroupGapPt;
        vr.outerContentL = vr.boxL + VrBorderPt + VrFramePadPt;
        vr.outerContentR = vr.outerR - VrBorderPt - VrFramePadPt;
        vr.y += VrBorderPt + VrFramePadPt;

        Fill(vr, vr.y + VrBubbleDropPt, vr.y + VrBubbleDropPt + VrBubblePt, vr.outerContentL, VrBubblePt,
            vr.groups[0].Bubble, VrLayerBubble);
        Run(vr, vr.y, vr.outerContentL + VrBubblePt + VrBubbleGapPt, VrGroupNamePt, vr.faceReg, "SegoeUI",
            vr.groups[0].Name, VrInk);
        vr.y += VrLineH(VrGroupNamePt) + VrNamePadPt;

        vr.listBottom = vr.y;
        for (var gi = 1; gi < vr.groups.Count; gi++)
        {
            if (!DrawRuleGroup(vr, gi)) break;
        }

        Fill(vr, vr.listTop, vr.listBottom, vr.boxL, vr.boxR - vr.boxL, VrWhite, VrLayerContainer);
        VRule(vr, vr.listTop, vr.listBottom, vr.boxL + VrBorderPt / 2, VrFrameBorder);
        VRule(vr, vr.listTop, vr.listBottom, vr.outerR - VrBorderPt / 2, VrFrameBorder);
        HRule(vr, vr.listBottom - VrBorderPt / 2, vr.boxL, vr.outerR, VrFrameBorder);

        // the banked operators, written out sheet by sheet in stacking order
        foreach (var g in vr.ops.GroupBy(o => o.Sheet))
        {
            var sb = new StringBuilder();
            foreach (var o in g.OrderBy(o => o.Layer).ThenBy(o => o.Seq))
                sb.Append(o.Text).Append('\n');
            vr.pages[g.Key].AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
        }
        return vr.doc;
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
