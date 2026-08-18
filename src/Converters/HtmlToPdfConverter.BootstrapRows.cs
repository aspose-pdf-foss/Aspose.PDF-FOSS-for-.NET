using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// The container-less Bootstrap ROWS page (the routing-slip shape): body-level
// .row grids of col-xs-N label/value columns, panel-success cards whose heading
// rows carry bold labels over values, hr rules and centred type — laid out on
// the reference's box model: 90/72 pt margins, the Site.css 20px body side
// padding, .row negative margins expanding 15px back into the padding, columns
// at their percent of the expanded row plus the 15px column padding, adjacent
// block margins MAX-collapsing, and the Bootstrap panel chrome (10px/15px
// heading padding, 15px body padding, the success palette). All constants
// measured on the reference; the grid positions reproduce it exactly
// (columns at 117 / 264.1 / 411.2 / 558.4 on the 800 pt sheet).
internal static partial class HtmlToPdfConverter
{
    private const double BrMarginX = 90.0;
    private const double BrMarginY = 72.0;
    private const double BrBodyPadX = 15.0;        // Site.css body padding 20px
    private const double BrLineH = 15.0;           // 14px × 1.42857 = 20px
    private const double BrFontPt = 10.5;          // 14px
    private const double BrH3FontPt = 18.0;        // 24px
    private const double BrH3LineH = 19.8;         // 24px × 1.1 = 26.4px
    private const double BrH3MarginTop = 15.0;     // 20px
    private const double BrH3MarginBottom = 7.5;   // 10px
    private const double BrHrMargin = 15.0;        // hr margin 20px 0
    private const double BrRowExpand = 11.25;      // .row margin -15px
    private const double BrColPad = 11.25;         // column padding 15px
    private const double BrPanelMb = 15.0;         // .panel margin-bottom 20px
    private const double BrHeadPadY = 7.5;         // panel-heading padding 10px
    private const double BrBodyPad = 11.25;        // panel-body padding 15px
    private const double BrPMb = 7.5;              // p margin-bottom 10px
    // the Bootstrap success palette + chrome inks (theme constants)
    private static readonly Color BrText = Color.FromRgb(0x33, 0x33, 0x33);
    private static readonly Color BrLink = Color.FromRgb(0x33, 0x7a, 0xb7);
    private static readonly Color BrHrInk = Color.FromRgb(0xee, 0xee, 0xee);
    private static readonly Color BrPanelBorder = Color.FromRgb(0xd6, 0xe9, 0xc6);
    private static readonly Color BrHeadBg = Color.FromRgb(0xdf, 0xf0, 0xd8);
    private static readonly Color BrHeadFg = Color.FromRgb(0x3c, 0x76, 0x3d);

    private static Document? TryRenderBootstrapRows(string html,
        IReadOnlyDictionary<string, Dictionary<string, string>> css,
        double pageWidth, double pageHeight)
    {
        // ── fingerprint: the Bootstrap body + panel/row/col markup, no container ──
        if (!css.TryGetValue("body", out var body)
            || !body.TryGetValue("font-size", out var bodyFsV)
            || !bodyFsV.TrimEnd().EndsWith("px", StringComparison.OrdinalIgnoreCase)
            || !css.ContainsKey(".panel")
            || !Regex.IsMatch(html, @"class\s*=\s*['""]panel panel-", RegexOptions.IgnoreCase)
            || !Regex.IsMatch(html, @"class\s*=\s*['""]col-xs-\d", RegexOptions.IgnoreCase)
            || Regex.IsMatch(html, @"class\s*=\s*['""]container['""]", RegexOptions.IgnoreCase))
            return null;
        var face = "Arial";
        if (body.TryGetValue("font-family", out var famV))
            foreach (var fam in famV.Split(','))
            {
                var f = fam.Trim().Trim('"', '\'');
                if (f.Length > 0 && !f.Equals("sans-serif", StringComparison.OrdinalIgnoreCase)
                    && WinMetricsFor(f) is not null) { face = f; break; }
            }
        if (WinMetricsFor(face) is not { } fmv) return null;

        var doc = new Document();
        var page = doc.Pages.Add(pageWidth, pageHeight);
        EnsureFont(page, face.Replace(" ", ""), "FA");
        EnsureFont(page, face.Replace(" ", "") + "-Bold", "FB");
        var invc = System.Globalization.CultureInfo.InvariantCulture;

        var contentL = BrMarginX + BrBodyPadX;
        var contentR = pageWidth - BrMarginX - BrBodyPadX;
        var limit = pageHeight - BrMarginY;

        // white body canvas over the content box (the Bootstrap body background)
        page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
            $"q 1 1 1 rg {BrMarginX:F2} {BrMarginY:F2} {pageWidth - 2 * BrMarginX:F2} {pageHeight - 2 * BrMarginY:F2} re f Q\n")));

        void EmitRun(string res, double fs, double x, double yBaselineTd, string text, Color col)
            => page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
                $"BT {col.R / 255.0:0.###} {col.G / 255.0:0.###} {col.B / 255.0:0.###} rg " +
                $"/{res} {fs:F2} Tf 1 0 0 1 {x:F2} {pageHeight - yBaselineTd:F2} Tm ({EscapePdfString(text)}) Tj ET\n")));
        void Fill(Color c, double x, double yTd, double w, double h)
            => page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
                $"q {c.R / 255.0:0.###} {c.G / 255.0:0.###} {c.B / 255.0:0.###} rg {x:F2} {pageHeight - yTd - h:F2} {w:F2} {h:F2} re f Q\n")));
        void StrokeRect(Color c, double x, double yTd, double w, double h, double sw)
            => page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
                $"q {c.R / 255.0:0.###} {c.G / 255.0:0.###} {c.B / 255.0:0.###} RG {sw:0.##} w " +
                $"{x + sw / 2:F2} {pageHeight - yTd - h + sw / 2:F2} {w - sw:F2} {h - sw:F2} re S Q\n")));
        static string Flat(string frag)
            => Regex.Replace(DecodeEntities(Regex.Replace(frag, @"<[^>]+>", " ")), @"\s+", " ").Trim();
        // baseline seat inside the 20px line box (half-leading + the face ascent)
        var drop = MetricBaselineDrop(BrFontPt, BrLineH, fmv);
        var dropH3 = MetricBaselineDrop(BrH3FontPt, BrH3LineH, fmv);

        // ── the column set of one .row: [(pct, inner html)] ──
        static List<(double frac, string body)> RowCols(string rowBody)
        {
            var cols = new List<(double, string)>();
            foreach (Match cm in Regex.Matches(rowBody,
                @"<div\b[^>]*class\s*=\s*['""]col-xs-(\d+)[^'""]*['""][^>]*>(?<b>[\s\S]*?)</div>",
                RegexOptions.IgnoreCase))
                cols.Add((int.Parse(cm.Groups[1].Value) / 12.0, cm.Groups["b"].Value));
            return cols;
        }

        // Render one row's columns from rowTop; returns the row's height.
        // draw:false only measures (the panel-heading fill needs the height first).
        double RenderRow(string rowBody, double rowTop, double boxL, double boxR,
            bool draw = true, bool headFg = false)
        {
            var rowL = boxL - BrRowExpand;
            var rowW = boxR + BrRowExpand - rowL;
            var cols = RowCols(rowBody);
            var maxH = BrLineH;
            var colX = rowL;
            foreach (var (frac, colBody) in cols)
            {
                var colW = rowW * frac;
                var textX = colX + BrColPad;
                var wrapW = colW - 2 * BrColPad;
                var y = rowTop;
                // bold label segment, then the value after the <br>
                var boldM = Regex.Match(colBody, @"<b\b[^>]*>(?<t>[\s\S]*?)</b>", RegexOptions.IgnoreCase);
                var rest = boldM.Success
                    ? colBody.Remove(boldM.Index, boldM.Length) : colBody;
                var linkM = Regex.Match(rest, @"<a\b[^>]*>(?<t>[\s\S]*?)</a>", RegexOptions.IgnoreCase);
                var isLink = false;
                string valueText;
                if (linkM.Success && Flat(rest).Length == Flat(linkM.Groups["t"].Value).Length)
                {
                    valueText = Flat(linkM.Groups["t"].Value);
                    isLink = true;
                }
                else valueText = Flat(rest);
                if (boldM.Success)
                {
                    var lbl = Flat(boldM.Groups["t"].Value);
                    if (lbl.Length > 0)
                    {
                        if (draw) EmitRun("FB", BrFontPt, textX, y + drop, lbl,
                            headFg ? BrHeadFg : BrText);
                        y += BrLineH;
                    }
                }
                if (valueText.Length > 0)
                    foreach (var ln in MeasuredWordWrap(valueText, wrapW, face, BrFontPt))
                    {
                        if (ln.Length == 0) continue;
                        if (draw) EmitRun("FA", BrFontPt, textX, y + drop, ln,
                            isLink ? BrLink : headFg ? BrHeadFg : BrText);
                        y += BrLineH;
                    }
                maxH = Math.Max(maxH, y - rowTop);
                colX += colW;
            }
            return maxH;
        }

        // ── walk the body's top-level constructs in document order ──
        var bodyM = Regex.Match(html, @"<body\b[^>]*>", RegexOptions.IgnoreCase);
        var pos = bodyM.Success ? bodyM.Index + bodyM.Length : 0;
        var yTd = BrMarginY;                       // flow position (top-down)
        var pendingMb = 0.0;                       // margin awaiting MAX-collapse
        void Advance(double marginTop)
        {
            yTd += Math.Max(pendingMb, marginTop);
            pendingMb = 0;
        }
        var construct = new Regex(
            @"<h3\b[^>]*>(?<h3>[\s\S]*?)</h3>|<hr\s*/?>|<p\b[^>]*class\s*=\s*['""]text-center['""][^>]*>(?<pc>[\s\S]*?)</p>|<div\b[^>]*class\s*=\s*['""]\s*(?<cls>row|panel panel-[\w-]+)\s*['""][^>]*>",
            RegexOptions.IgnoreCase);
        while (true)
        {
            var m = construct.Match(html, pos);
            if (!m.Success || yTd > limit) break;
            pos = m.Index + m.Length;
            if (m.Groups["h3"].Success)
            {
                Advance(BrH3MarginTop);
                var txt = Flat(m.Groups["h3"].Value);
                var w = MeasureFaceText(face, txt, BrH3FontPt);
                EmitRun("FA", BrH3FontPt, (pageWidth - w) / 2, yTd + dropH3, txt, BrText);
                yTd += BrH3LineH;
                pendingMb = BrH3MarginBottom;
            }
            else if (m.Value.StartsWith("<hr", StringComparison.OrdinalIgnoreCase))
            {
                Advance(BrHrMargin);
                page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
                    $"q {BrHrInk.R / 255.0:0.###} {BrHrInk.G / 255.0:0.###} {BrHrInk.B / 255.0:0.###} RG 0.75 w " +
                    $"{contentL:F2} {pageHeight - yTd:F2} m {contentR:F2} {pageHeight - yTd:F2} l S Q\n")));
                pendingMb = BrHrMargin;
            }
            else if (m.Groups["pc"].Success)
            {
                Advance(0);
                var txt = Flat(m.Groups["pc"].Value);
                var w = MeasureFaceText(face, txt, BrFontPt);
                EmitRun("FA", BrFontPt, (pageWidth - w) / 2, yTd + drop, txt, BrText);
                yTd += BrLineH;
                pendingMb = BrPMb;
            }
            else if (m.Groups["cls"].Value.Equals("row", StringComparison.OrdinalIgnoreCase))
            {
                // a bare body-level row: consume to its balanced close
                var end = FindDivClose(html, pos);
                Advance(0);
                yTd += RenderRow(html[pos..end], yTd, contentL, contentR);
                pos = end;
            }
            else
            {
                // a panel: heading (one row of label/value columns, or plain
                // bold text) over a body of paragraphs
                var end = FindDivClose(html, pos);
                var panel = html[pos..end];
                pos = end;
                Advance(0);
                var panelTop = yTd;
                var headM = Regex.Match(panel,
                    @"<div\b[^>]*class\s*=\s*['""]panel-heading['""][^>]*>", RegexOptions.IgnoreCase);
                var headH = 0.0;
                string headBody = "";
                if (headM.Success)
                {
                    var hEnd = FindDivClose(panel, headM.Index + headM.Length);
                    headBody = panel[(headM.Index + headM.Length)..hEnd];
                }
                var bodM = Regex.Match(panel,
                    @"<div\b[^>]*class\s*=\s*['""]panel-body['""][^>]*>", RegexOptions.IgnoreCase);
                string bodBody = "";
                if (bodM.Success)
                {
                    var bEnd = FindDivClose(panel, bodM.Index + bodM.Length);
                    bodBody = panel[(bodM.Index + bodM.Length)..bEnd];
                }
                // heading: measure first (the fill draws before the text)
                var headRow = Regex.Match(headBody,
                    @"<div\b[^>]*class\s*=\s*['""]row['""][^>]*>", RegexOptions.IgnoreCase);
                // draw the heading band behind, then its content
                var headTop = panelTop + 0.75;
                var headContentTop = headTop + BrHeadPadY;
                if (headRow.Success)
                {
                    var hrEnd = FindDivClose(headBody, headRow.Index + headRow.Length);
                    var hRowBody = headBody[(headRow.Index + headRow.Length)..hrEnd];
                    var hBoxL = contentL + 0.75 + BrColPad;
                    var hBoxR = contentR - 0.75 - BrColPad;
                    var rowH = RenderRow(hRowBody, headContentTop, hBoxL, hBoxR,
                        draw: false, headFg: true);
                    headH = 2 * BrHeadPadY + rowH;
                    Fill(BrHeadBg, contentL + 0.75, headTop, contentR - contentL - 1.5, headH);
                    StrokeRect(BrPanelBorder, contentL + 1.1, headTop + 0.35,
                        contentR - contentL - 2.2, headH - 0.7, 0.75);
                    RenderRow(hRowBody, headContentTop, hBoxL, hBoxR, headFg: true);
                }
                else if (Flat(headBody).Length > 0)
                {
                    headH = 2 * BrHeadPadY + BrLineH;
                    Fill(BrHeadBg, contentL + 0.75, headTop, contentR - contentL - 1.5, headH);
                    EmitRun("FB", BrFontPt, contentL + BrColPad, headContentTop + drop,
                        Flat(headBody), BrHeadFg);
                }
                // body: its paragraphs (an empty .multiline keeps only its margin)
                var byTd = headTop + headH + BrBodyPad;
                foreach (Match pm in Regex.Matches(bodBody, @"<p\b[^>]*>(?<t>[\s\S]*?)</p>",
                    RegexOptions.IgnoreCase))
                {
                    var pt2 = Flat(pm.Groups["t"].Value);
                    foreach (var ln in MeasuredWordWrap(pt2, contentR - contentL - 2 * BrBodyPad, face, BrFontPt))
                    {
                        if (ln.Length == 0) continue;
                        EmitRun("FA", BrFontPt, contentL + BrBodyPad, byTd + drop, ln, BrText);
                        byTd += BrLineH;
                    }
                    byTd += BrPMb;
                }
                var panelBot = byTd + BrBodyPad;
                // the panel frame: white body box + the success border
                StrokeRect(BrPanelBorder, contentL + 0.35, panelTop + 0.35,
                    contentR - contentL - 0.7, panelBot - panelTop - 0.7, 0.75);
                yTd = panelBot;
                pendingMb = BrPanelMb;
            }
        }
        return doc;
    }

    // ── the EDGE-TO-EDGE SEGOE ALERT sheet (the `.top_label/.grid_header`
    // class namespace at zero margins) ──
    // A Segoe UI e-mail alert: the banner line and its UA hr, two label/value
    // panels (9pt grey right-aligned labels against 10.5pt bold values on a
    // shared baseline; the right panel's label centres on its wrapped value
    // block), the broken vehicle-image frame with the browser placeholder, and
    // the sensor grid — six measured columns, grey centred headers over their
    // 2px underline, bold centred values, the 45px red highlight cells with
    // white ink, and the red 'No Sensor' rows. Every constant measured on the
    // reference.
    private const double SaBodyX = 6.0;            // the UA body margin at zero margins
    private const double SaBannerBaselinePt = 18.85;
    private const double SaHrY1 = 28.1;            // the UA hr's black edge…
    private const double SaHrY2 = 28.9;            // …over its #555 shadow
    private const double SaLeftLabelRight = 303.0;
    private const double SaLeftValueX = 310.5;
    private const double SaRightLabelRight = 733.3;
    private const double SaRightValueX = 740.8;
    private const double SaPanelBase0 = 57.6;      // first shared baseline
    private const double SaPanelPitch = 17.45;     // left-panel row pitch
    private const double SaValuePitch = 15.95;     // wrapped right-value pitch
    private const double SaValueWrapW = 145.0;     // the value column wrap box (200px − pads)
    private const double SaGridHeadTop = 342.0;    // header glyph top
    private const double SaGridRuleY = 358.5;      // the 2px header underline
    private const double SaGridRow0Top = 370.1;    // first value glyph top
    private const double SaGridRowPitch1 = 35.3;   // rows 1-2 (highlight rows)
    private const double SaGridRowPitch2 = 33.7;   // the sensor-less rows
    private const double SaRedTop0 = 359.2;        // first highlight fill top
    private const double SaRedH = 33.8;            // 45px cell
    private const double SegoeAscEm = 1.079;       // Segoe UI hhea ascent

    private static readonly double[] SaColEdges =
        { 57.8, 133.5, 307.7, 482.0, 656.2, 830.4, 1005.4 };

    private static Document? TryRenderSegoeAlert(string html,
        IReadOnlyDictionary<string, Dictionary<string, string>> css,
        double pageWidth, double pageHeight)
    {
        if (!css.ContainsKey(".top_label") || !css.ContainsKey(".grid_header")
            || !css.ContainsKey(".grid_highlight_value")
            || !Regex.IsMatch(html, @"class\s*=\s*[""']left_panel[""']", RegexOptions.IgnoreCase))
            return null;
        var segoe = Text.SystemFontResolver.Resolve("Segoe UI");
        // the resolver maps PDF-style names — the bold face answers to the
        // hyphenated form
        var segoeBold = Text.SystemFontResolver.Resolve("SegoeUI-Bold")
            ?? Text.SystemFontResolver.Resolve("Segoe UI Bold");
        if (segoe is null || segoeBold is null) return null;
        var invc = System.Globalization.CultureInfo.InvariantCulture;
        var grey = Color.FromRgb(0x59, 0x59, 0x59);
        var red = Color.FromRgb(0xCF, 0x31, 0x35);
        var white = Color.FromRgb(255, 255, 255);
        var black = Color.FromRgb(0, 0, 0);

        var doc = new Document();
        var page = doc.Pages.Add(pageWidth, pageHeight);
        EnsureFonts(page);

        double Measure(byte[] ttf, string face, string s, double fs)
        {
            if (page.Dict.Get("Resources") is not Core.PdfDictionary res
                || res.Get("Font") is not Core.PdfDictionary fd) return s.Length * fs * 0.5;
            return Text.Type0FontEmbedder.MeasureText(fd, ttf, face, s, fs,
                stripSpacesInBaseFont: true);
        }
        void Run(bool bold, double fs, double x, double baselineTd, string text, Color col)
        {
            if (text.Length == 0) return;
            if (page.Dict.Get("Resources") is not Core.PdfDictionary res
                || res.Get("Font") is not Core.PdfDictionary fd) return;
            var (rn, hex) = Text.Type0FontEmbedder.Embed(fd,
                bold ? segoeBold! : segoe!, bold ? "SegoeUIBold" : "SegoeUI", text,
                stripSpacesInBaseFont: true);
            page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
                $"BT {col.R / 255.0:0.###} {col.G / 255.0:0.###} {col.B / 255.0:0.###} rg " +
                $"/{rn} {fs:0.##} Tf 1 0 0 1 {x:0.##} {pageHeight - baselineTd:0.##} Tm ")
                + "<" + System.Convert.ToHexString(hex) + "> Tj ET\n"));
        }
        void Line(double x0, double x1, double yTd, double w, Color c)
            => page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
                $"q {c.R / 255.0:0.###} {c.G / 255.0:0.###} {c.B / 255.0:0.###} RG {w:0.##} w " +
                $"{x0:0.##} {pageHeight - yTd:0.##} m {x1:0.##} {pageHeight - yTd:0.##} l S Q\n")));
        void Fill(Color c, double x, double yTd, double w, double h)
            => page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
                $"q {c.R / 255.0:0.###} {c.G / 255.0:0.###} {c.B / 255.0:0.###} rg " +
                $"{x:0.##} {pageHeight - yTd - h:0.##} {w:0.##} {h:0.##} re f Q\n")));
        static string Flat(string frag)
            => Regex.Replace(DecodeEntities(Regex.Replace(frag, @"<[^>]+>", " ")), @"\s+", " ").Trim();

        // ── the banner and its hr ──
        var bodyM = Regex.Match(html, @"<body\b[^>]*>\s*(?<t>[^<]+)", RegexOptions.IgnoreCase);
        if (bodyM.Success && Flat(bodyM.Groups["t"].Value).Length > 0)
            Run(false, 12, SaBodyX, SaBannerBaselinePt, Flat(bodyM.Groups["t"].Value), black);
        Line(SaBodyX, pageWidth - SaBodyX, SaHrY1, 0.75, black);
        Line(SaBodyX, pageWidth - SaBodyX, SaHrY2, 0.75, Color.FromRgb(0x55, 0x55, 0x55));

        // ── the two label/value panels ──
        void Panel(string cls, double labelRight, double valueX)
        {
            var pm = Regex.Match(html,
                @"<table\b[^>]*class\s*=\s*[""']" + cls + @"[""'][^>]*>(?<b>[\s\S]*?)</table>",
                RegexOptions.IgnoreCase);
            if (!pm.Success) return;
            var baseline = SaPanelBase0;
            foreach (Match trM in Regex.Matches(pm.Groups["b"].Value,
                @"<tr\b[^>]*>(?<r>[\s\S]*?)</tr>", RegexOptions.IgnoreCase))
            {
                var lblM = Regex.Match(trM.Groups["r"].Value,
                    @"class\s*=\s*[""']top_label[""'][^>]*>(?<t>[\s\S]*?)</span>", RegexOptions.IgnoreCase);
                var valM = Regex.Match(trM.Groups["r"].Value,
                    @"class\s*=\s*[""']top_value[""'][^>]*>(?<t>[\s\S]*?)</span>", RegexOptions.IgnoreCase);
                var lbl = lblM.Success ? Flat(lblM.Groups["t"].Value).ToUpperInvariant() : "";
                var val = valM.Success ? Flat(valM.Groups["t"].Value) : "";
                var valLines = MeasuredWordWrap(val, SaValueWrapW, "Segoe UI", 10.5);
                // the label centres on the value block's baselines
                var lblBase = baseline + (valLines.Length - 1) * SaValuePitch / 2;
                if (lbl.Length > 0)
                    Run(false, 9, labelRight - Measure(segoe!, "SegoeUI", lbl, 9), lblBase, lbl, grey);
                for (var li = 0; li < valLines.Length; li++)
                    Run(true, 10.5, valueX, baseline + li * SaValuePitch, valLines[li], black);
                baseline += Math.Max(SaPanelPitch,
                    (valLines.Length - 1) * SaValuePitch + SaPanelPitch);
            }
        }
        Panel("left_panel", SaLeftLabelRight, SaLeftValueX);
        Panel("right_panel", SaRightLabelRight, SaRightValueX);

        // The vehicle cid: image leaves NOTHING — the reference draws neither a
        // frame nor a placeholder for it (the area is bare white on the
        // template), it only holds the vertical space.

        // ── the sensor grid ──
        var gm = Regex.Match(html,
            @"<table\b[^>]*border-collapse[^>]*>(?<b>[\s\S]*?)$", RegexOptions.IgnoreCase);
        if (gm.Success)
        {
            var gbody = gm.Groups["b"].Value;
            // headers: centred grey per column over the per-column underline
            var heads = new List<string>();
            foreach (Match thM in Regex.Matches(gbody,
                @"class\s*=\s*[""']grid_header[""'][\s\S]*?<span>(?<t>[^<]*)</span>",
                RegexOptions.IgnoreCase))
                heads.Add(Flat(thM.Groups["t"].Value));
            for (var ci = 0; ci < heads.Count && ci + 1 < SaColEdges.Length; ci++)
            {
                if (heads[ci].Length == 0) continue;
                var cw = Measure(segoe!, "SegoeUI", heads[ci], 9);
                var cx = (SaColEdges[ci] + SaColEdges[ci + 1] - cw) / 2;
                Run(false, 9, cx, SaGridHeadTop + 9 * SegoeAscEm, heads[ci], grey);
            }
            var ruleInk = Color.FromRgb(0x89, 0x89, 0x89);
            for (var ci = 0; ci + 1 < SaColEdges.Length; ci++)
                Line(SaColEdges[ci] - (ci > 0 ? 0.7 : 0), SaColEdges[ci + 1] + 1.4,
                    SaGridRuleY, 1.5, ruleInk);

            // data rows: [id, computed?, temp?, target?, sensor, days?] — a
            // colspan row carries only the id and its red sensor note
            var rowTop = SaGridRow0Top;
            var redTop = SaRedTop0;
            foreach (Match tbM in Regex.Matches(gbody,
                @"<tbody\b[^>]*>(?<r>[\s\S]*?)</tbody>", RegexOptions.IgnoreCase))
            {
                var r = tbM.Groups["r"].Value;
                if (Regex.IsMatch(r, @"grid_header", RegexOptions.IgnoreCase)) continue;
                var cells = new List<(string text, bool highlight, bool redText)>();
                foreach (Match tdM in Regex.Matches(r,
                    @"<td\b(?<a>[^>]*)>(?<c>[\s\S]*?)</td>", RegexOptions.IgnoreCase))
                {
                    var c = tdM.Groups["c"].Value;
                    var hl = Regex.IsMatch(c, @"grid_highlight_value[^>]*background",
                        RegexOptions.IgnoreCase);
                    var rt = Regex.IsMatch(c, @"color:\s*#CF3135", RegexOptions.IgnoreCase);
                    var span = Regex.Match(tdM.Groups["a"].Value, @"colspan\s*=\s*[""']?(\d+)");
                    cells.Add((Flat(c), hl, rt));
                    if (span.Success)
                        for (var k = 1; k < int.Parse(span.Groups[1].Value); k++)
                            cells.Add(("", false, false));
                }
                if (cells.Count == 0) continue;
                var isFull = cells.Count >= 6 && cells[1].highlight;
                for (var ci = 0; ci < cells.Count && ci + 1 < SaColEdges.Length; ci++)
                {
                    var (txt, hl, rt) = cells[ci];
                    if (hl)
                        Fill(red, SaColEdges[1] + 0.8, redTop, SaColEdges[2] - SaColEdges[1] - 0.8, SaRedH);
                    if (txt.Length == 0) continue;
                    var cw = Measure(segoeBold!, "SegoeUIBold", txt, 9);
                    var cx = (SaColEdges[ci] + SaColEdges[ci + 1] - cw) / 2;
                    Run(true, 9, cx, rowTop + 9 * SegoeAscEm, txt,
                        hl ? white : rt ? red : black);
                }
                var pitch = isFull ? SaGridRowPitch1 : SaGridRowPitch2;
                rowTop += pitch;
                redTop += pitch;
            }
        }
        return doc;
    }

    /// <summary>Index of the matching close for the div whose open tag ends at
    /// <paramref name="afterOpen"/> (balanced by div depth).</summary>
    private static int FindDivClose(string html, int afterOpen)
    {
        var depth = 1;
        foreach (Match t in Regex.Matches(html[afterOpen..], @"<div\b|</div\s*>",
            RegexOptions.IgnoreCase))
        {
            depth += t.Value.StartsWith("</") ? -1 : 1;
            if (depth == 0) return afterOpen + t.Index;
        }
        return html.Length;
    }
}
