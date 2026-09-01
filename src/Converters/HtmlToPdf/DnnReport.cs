using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// The DNN sectioned-report dialect: a DotNetNuke portal page (skinmaster box,
// ModuleContainer with a header band, ModuleSubHeader section bands and
// float-label field rows). The site's skin stylesheets sit behind
// <link>+@import chains — the flow engine never sees them — so the dialect
// draws the skin's box model directly. Every constant below is either derived
// from the skin CSS (the sheet and rule are named) or measured off the
// expected render of the enrollment-summary fixture where the rule lives in
// the portal's module stylesheet (a WebResource.axd bundle that no longer
// resolves).
internal static partial class HtmlToPdfConverter
{
    // skin.css DNN.css: .skinmaster { width: 984px; border: 1px #7994cb } —
    // the bordered box is 986 px = 739.5 pt; the sheet widens to hold it
    // between the 90 pt margins.
    private const double DnnSkinBoxPt = 986 * 0.75;
    // Base.css: td, th { font-size: 8pt; font-family: Verdana } — the module
    // content all sits in skin table cells.
    private const double DnnCellFontPt = 8.0;
    // DNN.css: body { font-size: 11px } — the breadcrumb date line.
    private const double DnnBodyFontPt = 11 * 0.75;
    // Module header/subheader bands are 24 px deep: min-height 18px +
    // 2px padding top/bottom + 1px borders (DNN.css .ModuleHeaderContainer).
    private const double DnnBandPt = 24 * 0.75;
    // .ModuleHeaderContainer / .ModuleBodyContainer pad content 10 px left.
    private const double DnnBodyPadPt = 10 * 0.75;
    // Verdana metrics (asc 2059/desc 430/upm 2048): the baseline seat below a
    // text line's bbox top, and the bbox height, at 8 pt.
    private const double DnnAscEm = 2059.0 / 2048.0;
    private const double DnnDescEm = 430.0 / 2048.0;
    // Module box left/right inset from the skinmaster content edge: the module
    // stylesheet (dead axd) sizes the centred pane to 953 px — measured
    // 103.1 / 817.9 on the expected render.
    private const double DnnModuleInsetPt = 13.5;
    // Header zone (measured; the skin composes it from skinheader padding 3px,
    // the logo's inline padding 25px/10px and the breadcrumb rows):
    private const double DnnLogoTopPt = 15.0;          // content top -> logo top
    private const double DnnLogoLeftPt = 21.75;        // border+3px+25px pads
    private const double DnnDateTopPt = 52.7;          // content top -> date bbox top
    // .Breadcrumb { padding: 0 15px } + 1px skinmaster border = right inset.
    private const double DnnDateRightPt = 12.0;
    // Content top -> module box top (skin rows between ruler and pane table).
    private const double DnnModuleTopPt = 117.4;
    // A field row's line box (13 px at 8pt Verdana) and the measured seat of
    // the text bbox below the line-box top (float/baseline slack, 2.4 px).
    private const double DnnRowLinePt = 13 * 0.75;
    private const double DnnRowSeatPt = 2.25;
    // Band text bbox tops below the band fill top (measured: the header
    // centres its 2px-padded 18px min-height; the subheader seats deeper).
    private const double DnnHeaderTextSeatPt = 4.1;
    private const double DnnSubTextSeatPt = 5.6;

    private abstract class DnnBlock { }

    private sealed class DnnBand : DnnBlock          // header or subheader
    {
        public string Text = "";
        public bool Header;                          // teal header vs DFEEF7 sub
    }

    private sealed class DnnFieldRow : DnnBlock
    {
        // (label text, label border-box width pt, value text, x offset of the
        //  pair from the module body left). Label text right-aligns 7.5 pt
        //  inside the box right edge; the value sits AT the box right edge.
        public List<(string Label, double LabelW, string Value, double PairX)> Pairs = new();
        public double IndentPx;                      // ModuleBodyContainer pad-left
        public bool Bare;                            // no RowPad wrapper: taller line
    }

    private sealed class DnnParaLine : DnnBlock      // stray plain-text line
    {
        public string Text = "";
        public double Pitch = 23 * 0.75;
    }

    private sealed class DnnGap : DnnBlock
    {
        public double H;
    }

    // A DataList grid: the header row of th cells, a record's summary row of
    // td cells (both at the scaled column grid), and a record's background
    // fill emitted BEFORE its content blocks with the pre-measured height.
    private sealed class DnnGridHeader : DnnBlock
    {
        public List<(string Text, double Wpx)> Cells = new();
    }

    private sealed class DnnGridRow : DnnBlock
    {
        public List<(string Text, double Wpx)> Cells = new();
    }

    private sealed class DnnGridFill : DnnBlock
    {
        public double H;
        public bool White;                           // GridRowOdd records
        public bool FullWidth;                       // header-less list grids
    }

    /// <summary>Render a DNN portal report (see the class comment). Null when
    /// the document does not carry the skin's fingerprint.</summary>
    private static Document? TryRenderDnnReport(string html, HtmlLoadOptions? options,
        double pageHIn)
    {
        if (!Regex.IsMatch(html, @"class\s*=\s*[""']skinmaster[""']", RegexOptions.IgnoreCase)
            || html.IndexOf("ModuleHeaderContainer", StringComparison.OrdinalIgnoreCase) < 0
            || html.IndexOf("ContainerFieldLabelHoriz", StringComparison.OrdinalIgnoreCase) < 0)
            return null;

        var pageH = pageHIn;
        const double marginL = 90.0, marginR = 90.0, marginT = 72.0, marginB = 72.0;
        var pageW = marginL + DnnSkinBoxPt + marginR;
        var bandBottom = pageH - marginB;
        var contentL = marginL;                       // skinmaster box left
        var contentR = marginL + DnnSkinBoxPt;        // and right
        var moduleL = contentL + DnnModuleInsetPt;
        var moduleR = contentR - DnnModuleInsetPt + 0.9;   // measured 817.9
        var bodyL = moduleL + 0.375 + DnnBodyPadPt;   // 111.4 fills, 111.0 text

        html = Regex.Replace(html, @"<script[\s\S]*?</script>", " ", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<!--[\s\S]*?-->", " ");

        // ── harvest the header pieces ──────────────────────────────────────
        string? logoUrl = null; double logoWpx = 115, logoHpx = 48;
        var logoM = Regex.Match(html,
            @"<img[^>]*id\s*=\s*[""'][^""']*imgLogo[""'][^>]*>", RegexOptions.IgnoreCase);
        if (logoM.Success)
        {
            logoUrl = DtpAttr(logoM.Value, "src");
            var st = DtpAttr(logoM.Value, "style") ?? "";
            var wm = Regex.Match(st, @"width\s*:\s*([\d.]+)px", RegexOptions.IgnoreCase);
            var hm = Regex.Match(st, @"height\s*:\s*([\d.]+)px", RegexOptions.IgnoreCase);
            if (wm.Success) logoWpx = DtpNum(wm.Groups[1].Value);
            if (hm.Success) logoHpx = DtpNum(hm.Groups[1].Value);
        }
        var dateM = Regex.Match(html,
            @"DateTimeCmnLabel[""'][^>]*>([^<]*)<", RegexOptions.IgnoreCase);
        var dateText = dateM.Success ? EdgarHtmlRenderer.DecodeEntities(dateM.Groups[1].Value).Trim() : "";

        // ── module content: every ModuleContainer in document order (the page
        // stacks several modules; their boxes render flush as one) ───────────
        var blocks = new List<DnnBlock>();
        foreach (Match mc in Regex.Matches(html,
                     @"<div\b[^>]*class\s*=\s*[""'][^""']*\bModuleContainer\b[^""']*[""'][^>]*>",
                     RegexOptions.IgnoreCase))
        {
            var moduleHtml = DnnInnerDiv(html, mc.Index);
            if (moduleHtml is not null) DnnWalk(moduleHtml, 0, blocks);
        }
        if (blocks.Count == 0) return null;

        // ── layout + draw ──────────────────────────────────────────────────
        var doc = new Document();
        var fontDict = new Core.PdfDictionary();
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var pageOps = new List<List<string>>();
        var pageStamps = new List<List<(byte[] Bytes, double X, double YTop, double W, double H)>>();
        var pageBottom = new List<double>();          // content extent per page
        var moduleTopPerPage = new List<double>();    // module box start per page
        var moduleBotPerPage = new List<double>();    // module box end per page (0 = runs on)

        void EnsurePage(int p)
        {
            while (pageOps.Count <= p)
            {
                pageOps.Add(new List<string>());
                pageStamps.Add(new List<(byte[], double, double, double, double)>());
                pageBottom.Add(marginT);
                moduleTopPerPage.Add(pageOps.Count == 1 ? marginT + DnnModuleTopPt : marginT);
                moduleBotPerPage.Add(0);
            }
        }

        void Touch(int p, double bottom)
        {
            EnsurePage(p);
            if (bottom > pageBottom[p]) pageBottom[p] = Math.Min(bottom, bandBottom);
        }

        void DrawText(int p, double x, double baseline, string text, bool bold, double size,
            (double R, double G, double B) color)
        {
            if (text.Length == 0) return;
            var faceName = bold ? "Verdana Bold" : "Verdana";
            var face = PosFace(faceName);
            if (face.ttf is null) { faceName = bold ? "Arial Bold" : "Arial"; face = PosFace(faceName); }
            if (face.ttf is null) return;
            var (rn, hex) = Text.Type0FontEmbedder.Embed(fontDict, face.ttf, faceName, text,
                stripSpacesInBaseFont: true);
            EnsurePage(p);
            pageOps[p].Add(string.Create(inv,
                $"BT {color.R:F3} {color.G:F3} {color.B:F3} rg /{rn} {size:F2} Tf 1 0 0 1 {x:F2} {pageH - baseline:F2} Tm <{System.Convert.ToHexString(hex)}> Tj ET\n"));
        }

        void FillRect(int p, double x0, double y0, double x1, double y1,
            (double R, double G, double B) c)
        {
            EnsurePage(p);
            pageOps[p].Add(string.Create(inv,
                $"q {c.R:F3} {c.G:F3} {c.B:F3} rg {x0:F2} {pageH - y1:F2} {x1 - x0:F2} {y1 - y0:F2} re f Q\n"));
        }

        void Line(int p, double x0, double y0, double x1, double y1,
            (double R, double G, double B) c)
        {
            EnsurePage(p);
            pageOps[p].Add(string.Create(inv,
                $"q {c.R:F3} {c.G:F3} {c.B:F3} RG 0.75 w {x0:F2} {pageH - y0:F2} m {x1:F2} {pageH - y1:F2} l S Q\n"));
        }

        var teal = (0.0, 1.0 * 0x55 / 255, 1.0 * 0x7C / 255);
        var subBg = (1.0 * 0xDF / 255, 1.0 * 0xEE / 255, 1.0 * 0xF7 / 255);
        var gridHdrBg = (1.0 * 0xDF / 255, 1.0 * 0xED / 255, 1.0 * 0xF6 / 255);
        var listBg = (1.0 * 0xF1 / 255, 1.0 * 0xF5 / 255, 1.0 * 0xF8 / 255);
        var black = (0.0, 0.0, 0.0);
        var white = (1.0, 1.0, 1.0);

        var asc8 = DnnAscEm * DnnCellFontPt;

        // header zone (page 1)
        EnsurePage(0);
        if (logoUrl is not null && logoUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            var logo = FetchRemoteImage(logoUrl);
            if (logo is not null)
                pageStamps[0].Add((logo, contentL + DnnLogoLeftPt, marginT + DnnLogoTopPt,
                    logoWpx * 0.75, logoHpx * 0.75));
        }
        if (dateText.Length > 0)
        {
            var w = MeasureFaceText("Verdana", dateText, DnnBodyFontPt);
            DrawText(0, contentR - DnnDateRightPt - w,
                marginT + DnnDateTopPt + DnnAscEm * DnnBodyFontPt, dateText, false,
                DnnBodyFontPt, black);
        }
        Touch(0, marginT + DnnModuleTopPt);

        // module flow
        var page = 0;
        var y = marginT + DnnModuleTopPt;             // module box top on p1

        void BreakPage(double need)
        {
            if (y + need <= bandBottom) return;
            moduleBotPerPage[page] = 0;               // box runs off this page
            Touch(page, bandBottom);
            page++;
            EnsurePage(page);
            y = marginT;
        }

        foreach (var b in blocks)
        {
            switch (b)
            {
                case DnnBand band:
                {
                    BreakPage(DnnBandPt);
                    var top = y + 0.375;              // outer border line
                    var h = DnnBandPt - 0.375 * 2;
                    if (band.Header)
                    {
                        Line(page, moduleL - 0.375, y, moduleR + 0.375, y, teal);
                        FillRect(page, moduleL + 0.375, top, moduleR - 0.375, top + h, teal);
                        Line(page, moduleL + 0.375, top + 0.375, moduleR - 0.375, top + 0.375, teal);
                        Line(page, moduleL + 0.375, top + h - 0.375, moduleR - 0.375, top + h - 0.375, teal);
                    }
                    else
                    {
                        FillRect(page, moduleL + 0.375, top, moduleR - 0.375, top + h, subBg);
                        Line(page, moduleL + 0.375, top + 0.375, moduleR - 0.375, top + 0.375, teal);
                        Line(page, moduleL + 0.375, top + h - 0.375, moduleR - 0.375, top + h - 0.375, teal);
                    }
                    var textTop = top + (band.Header ? DnnHeaderTextSeatPt : DnnSubTextSeatPt);
                    DrawText(page, bodyL + (band.Header ? 0.75 : 0.0),
                        textTop + asc8, band.Text, true, DnnCellFontPt,
                        band.Header ? white : black);
                    y += DnnBandPt;
                    Touch(page, y);
                    break;
                }
                case DnnFieldRow row:
                {
                    var rowH = DnnRowLinePt + (row.Bare ? 5 * 0.75 : 0);
                    BreakPage(rowH);
                    var baseline = y + DnnRowSeatPt + asc8;
                    var rowL = bodyL + row.IndentPx * 0.75;
                    foreach (var (label, labelW, value, pairX) in row.Pairs)
                    {
                        var boxRight = rowL + pairX + labelW;
                        var lw = MeasureFaceText("Verdana Bold", label, DnnCellFontPt);
                        DrawText(page, boxRight - DnnBodyPadPt - lw, baseline, label, true,
                            DnnCellFontPt, black);
                        DrawText(page, boxRight, baseline, value, false, DnnCellFontPt, black);
                    }
                    y += rowH;
                    Touch(page, y);
                    break;
                }
                case DnnParaLine para:
                {
                    BreakPage(para.Pitch);
                    DrawText(page, bodyL, y + para.Pitch - 3.75 - DnnDescEm * DnnCellFontPt,
                        para.Text, false, DnnCellFontPt, black);
                    y += para.Pitch;
                    Touch(page, y);
                    break;
                }
                case DnnGap gap:
                {
                    y += gap.H;
                    if (y > bandBottom) { BreakPage(bandBottom); }
                    else Touch(page, y);
                    break;
                }
                case DnnGridHeader gh:
                {
                    BreakPage(DnnGridHeaderHPt);
                    var gl = bodyL;
                    var gr = moduleR - 0.375 - DnnBodyPadPt;
                    FillRect(page, gl, y, gr, y + DnnGridHeaderHPt, gridHdrBg);
                    Line(page, gl - 0.375, y, gr + 0.375, y, teal);
                    Line(page, gl, y, gl, y + DnnGridHeaderHPt, teal);
                    Line(page, gr, y, gr, y + DnnGridHeaderHPt, teal);
                    Line(page, gl, y + DnnGridHeaderHPt + 0.3, gr, y + DnnGridHeaderHPt + 0.3, teal);
                    double total = 0;
                    foreach (var c in gh.Cells) total += c.Wpx + DnnGridCellPadPx;
                    double cum = 0;
                    var hb = y + DnnGridTextSeatPt + asc8;
                    foreach (var (text, wpx) in gh.Cells)
                    {
                        var x0 = gl + (gr - gl) * cum / total;
                        cum += wpx + DnnGridCellPadPx;
                        var x1 = gl + (gr - gl) * cum / total;
                        if (text.Length > 0)
                            DrawText(page, x0 + DnnGridCellTextPadPt, hb, text, true, DnnCellFontPt, black);
                        if (cum < total - 0.1)
                            Line(page, x1, y + 0.3, x1, y + DnnGridHeaderHPt, teal);
                    }
                    y += DnnGridHeaderHPt;
                    Touch(page, y);
                    break;
                }
                case DnnGridRow grow:
                {
                    BreakPage(DnnGridSummaryHPt);
                    var gl = bodyL;
                    var gr = moduleR - 0.375 - DnnBodyPadPt;
                    double total = 0;
                    foreach (var c in grow.Cells) total += c.Wpx + DnnGridCellPadPx;
                    double cum = 0;
                    var sb = y + DnnGridTextSeatPt + asc8;
                    foreach (var (text, wpx) in grow.Cells)
                    {
                        var x0 = gl + (gr - gl) * cum / total;
                        cum += wpx + DnnGridCellPadPx;
                        var x1 = gl + (gr - gl) * cum / total;
                        if (text.Length > 0)
                            DrawText(page, x0 + DnnGridCellTextPadPt, sb, text, false, DnnCellFontPt, black);
                        if (cum < total - 0.1)
                            Line(page, x1, y, x1, y + DnnGridSummaryHPt, teal);
                    }
                    Line(page, gl, y + DnnGridSummaryHPt + 0.3, gr, y + DnnGridSummaryHPt + 0.3, teal);
                    y += DnnGridSummaryHPt;
                    Touch(page, y);
                    break;
                }
                case DnnGridFill gf:
                {
                    var gl = gf.FullWidth ? moduleL + 0.375 : bodyL;
                    var gr = gf.FullWidth ? moduleR - 0.375 : moduleR - 0.375 - DnnBodyPadPt;
                    var rem = gf.H;
                    var yy = y;
                    var pp = page;
                    while (rem > 0)
                    {
                        var seg = Math.Min(rem, bandBottom - yy);
                        if (seg <= 0.01) { pp++; EnsurePage(pp); yy = marginT; continue; }
                        FillRect(pp, gl, yy, gr, yy + seg, gf.White ? (1.0, 1.0, 1.0) : listBg);
                        if (!gf.FullWidth)
                        {
                            Line(pp, gl, yy, gl, yy + seg, teal);
                            Line(pp, gr, yy, gr, yy + seg, teal);
                        }
                        Touch(pp, yy + seg);
                        rem -= seg;
                        yy += seg;
                    }
                    break;              // a fill never advances the cursor
                }
            }
        }
        moduleBotPerPage[page] = y;
        Touch(page, y);

        // ── assemble pages: frame first, then content ──────────────────────
        var borderBlue = (1.0 * 0x79 / 255, 1.0 * 0x94 / 255, 1.0 * 0xCB / 255);
        var nearWhite = (1.0 * 0xFE / 255, 1.0 * 0xFE / 255, 1.0 * 0xFE / 255);
        for (var p = 0; p < pageOps.Count; p++)
        {
            var pg = doc.Pages.Add(pageW, pageH);
            EnsureFonts(pg, fontDict);
            var bot = pageBottom[p];
            var frame = new StringBuilder();
            void F(string s) => frame.Append(s);
            F(string.Create(inv,
                $"q {nearWhite.Item1:F3} {nearWhite.Item2:F3} {nearWhite.Item3:F3} rg {contentL:F2} {pageH - bot:F2} {contentR - contentL:F2} {bot - marginT:F2} re f Q\n"));
            void Stroke(double x0, double y0, double x1, double y1, (double, double, double) c)
                => F(string.Create(inv,
                    $"q {c.Item1:F3} {c.Item2:F3} {c.Item3:F3} RG 0.75 w {x0:F2} {pageH - y0:F2} m {x1:F2} {pageH - y1:F2} l S Q\n"));
            if (p == 0) Stroke(contentL, marginT + 0.375, contentR, marginT + 0.375, borderBlue);
            Stroke(contentR - 0.375, marginT, contentR - 0.375, bot, borderBlue);
            Stroke(contentL + 0.375, marginT, contentL + 0.375, bot, borderBlue);
            if (p == pageOps.Count - 1)
                Stroke(contentL, bot - 0.375, contentR, bot - 0.375, borderBlue);
            // module box verticals
            var mTop = moduleTopPerPage[p];
            var mBot = moduleBotPerPage[p] > 0 ? moduleBotPerPage[p] : bot;
            if (mBot > mTop)
            {
                if (p == 0)
                    Stroke(moduleL - 0.75, mTop + 0.375 - 0.75, moduleR + 0.675, mTop + 0.375 - 0.75, teal);
                Stroke(moduleR - 0.375 + 0.375, mTop, moduleR + 0.375 - 0.375, mBot, teal);
                Stroke(moduleL + 0.375 - 0.375, mTop, moduleL - 0.375 + 0.375, mBot, teal);
                if (moduleBotPerPage[p] > 0)
                    Stroke(moduleL - 0.75, mBot - 0.375, moduleR + 0.675, mBot - 0.375, teal);
            }
            pg.AddContentStream(Encoding.ASCII.GetBytes(frame.ToString()));
            foreach (var op in pageOps[p])
                pg.AddContentStream(Encoding.ASCII.GetBytes(op));
            foreach (var im in pageStamps[p])
            {
                try
                {
                    var stamp = ImageStamp.FromEncodedBytes(im.Bytes);
                    stamp.XIndent = im.X;
                    stamp.YIndent = pageH - im.YTop - im.H;
                    stamp.DisplayWidth = im.W;
                    stamp.DisplayHeight = im.H;
                    stamp.ApplyTo(pg);
                }
                catch { /* undecodable: skip */ }
            }
        }
        return doc;
    }

    // ── parsing ────────────────────────────────────────────────────────────

    /// <summary>Inner HTML of the div opening at <paramref name="openIdx"/>.</summary>
    private static string? DnnInnerDiv(string html, int openIdx)
    {
        var end = html.IndexOf('>', openIdx);
        if (end < 0) return null;
        var depth = 1;
        foreach (Match m in Regex.Matches(html[(end + 1)..], @"<(/?)div\b[^>]*>",
                     RegexOptions.IgnoreCase))
        {
            depth += m.Groups[1].Value.Length > 0 ? -1 : 1;
            if (depth == 0) return html.Substring(end + 1, m.Index);
        }
        return null;
    }

    /// <summary>Linear walk of a module's markup into layout blocks. Wrapper
    /// divs are entered, not skipped, so sibling CellLeft float groups merge
    /// into one field row; a RowPad, band, body-container or grid boundary
    /// closes the open row. Vertical rhythm: every RowPad opening contributes
    /// its padding pair (Controls.css .RowPad { padding: 5px 0 } — 7.5 pt
    /// between rows, verified by the measured 17.25 pt row pitch), and a
    /// ModuleBodyContainer brackets its children with its 5px/10px paddings
    /// and indents them by its 10px padding-left.</summary>
    /// <returns>True when the walk's last content element was a RowPad
    /// subtree (the caller then collapses its own bottom pad).</returns>
    private static bool DnnWalk(string inner, double indentPx, List<DnnBlock> blocks,
        bool inRowPad = false)
    {
        var lastRowPad = false;
        var pos = 0;
        DnnFieldRow? row = null;
        double runX = 0;
        var brRun = false;
        void Flush()
        {
            if (row is { Pairs.Count: > 0 }) blocks.Add(row);
            row = null;
            runX = 0;
        }

        while (true)
        {
            var m = Regex.Match(inner[pos..], @"<(div|table|ol|hr|br)\b[^>]*/?>", RegexOptions.IgnoreCase);
            if (!m.Success) break;
            var openIdx = pos + m.Index;
            var open = m.Value;
            var tag = m.Groups[1].Value.ToLowerInvariant();
            var cls = DtpAttr(open, "class") ?? "";
            var st = DtpAttr(open, "style") ?? "";
            if (tag != "br") brRun = false;
            if (!cls.Contains("RowPad", StringComparison.OrdinalIgnoreCase)) lastRowPad = false;

            if (cls.Contains("PrintHidden", StringComparison.OrdinalIgnoreCase)
                || cls.Contains("aspNetHidden", StringComparison.OrdinalIgnoreCase)
                || Regex.IsMatch(st, @"display\s*:\s*none", RegexOptions.IgnoreCase))
            {
                var hEnd = tag == "hr" ? -2 : DnnTagEnd(inner, openIdx, tag);
                pos = hEnd < 0 ? openIdx + open.Length : inner.IndexOf('>', hEnd) + 1;
                continue;
            }

            if (tag == "br")
            {
                // section spacer <br/>s between wrappers: the first of a run
                // clears the float line (half a row pad), each further one is
                // a full 23px row (both measured against the band ladder)
                Flush();
                blocks.Add(new DnnGap { H = (brRun ? 18 : 5) * 0.75 });
                brRun = true;
                pos = openIdx + open.Length;
                continue;
            }

            if (tag == "hr")
            {
                Flush();
                blocks.Add(new DnnGap { H = 10 * 0.75 });
                pos = openIdx + open.Length;
                continue;
            }

            if (tag == "ol"
                || tag == "table" && cls.Contains("RadioButtonList", StringComparison.OrdinalIgnoreCase))
            {
                // the agreement list arrives in a later increment; radio
                // tables are the hidden editable twin of a shown value. Plain
                // tables are skin chrome — fall through and descend.
                Flush();
                var tEnd = DnnTagEnd(inner, openIdx, tag);
                if (tEnd < 0) { pos = openIdx + open.Length; continue; }
                pos = inner.IndexOf('>', tEnd) + 1;
                continue;
            }

            if (tag == "table" && cls.Contains("GridContainer", StringComparison.OrdinalIgnoreCase))
            {
                Flush();
                var tEnd = DnnTagEnd(inner, openIdx, "table");
                if (tEnd < 0) { pos = openIdx + open.Length; continue; }
                DnnParseGrid(inner[(inner.IndexOf('>', openIdx) + 1)..tEnd], indentPx, blocks);
                pos = inner.IndexOf('>', tEnd) + 1;
                continue;
            }

            if (cls.Contains("ModuleHeaderContainer", StringComparison.OrdinalIgnoreCase)
                || cls.Contains("ModuleSubHeaderContainer", StringComparison.OrdinalIgnoreCase))
            {
                Flush();
                var body = DnnInnerDiv(inner, openIdx);
                if (body is null) { pos = openIdx + open.Length; continue; }
                var isHeader = cls.Contains("ModuleHeaderContainer", StringComparison.OrdinalIgnoreCase);
                blocks.Add(new DnnBand
                {
                    Text = DnnPlainText(DnnStripHidden(body)),
                    Header = isHeader,
                });
                pos = openIdx + open.Length + body.Length + "</div>".Length;
                continue;
            }

            if (cls.Contains("ModuleBodyContainer", StringComparison.OrdinalIgnoreCase))
            {
                Flush();
                var body = DnnInnerDiv(inner, openIdx);
                if (body is null) { pos = openIdx + open.Length; continue; }
                blocks.Add(new DnnGap { H = 5 * 0.75 });      // padding-top 5px
                DnnWalk(body, indentPx + 10, blocks);         // padding-left 10px
                blocks.Add(new DnnGap { H = 10 * 0.75 });     // padding-bottom 10px
                pos = openIdx + open.Length + body.Length + "</div>".Length;
                continue;
            }

            if (cls.Contains("ContainerFieldLabelHoriz", StringComparison.OrdinalIgnoreCase))
            {
                var body = DnnInnerDiv(inner, openIdx);
                if (body is null) { pos = openIdx + open.Length; continue; }
                var wm = Regex.Match(st, @"width\s*:\s*([\d.]+)px", RegexOptions.IgnoreCase);
                // Controls.css .ContainerFieldLabelHoriz { width: 170px } —
                // inline widths override; border-box, text pads 10px inside.
                var wpx = wm.Success ? DtpNum(wm.Groups[1].Value) : 170.0;
                row ??= new DnnFieldRow { IndentPx = indentPx, Bare = !inRowPad };
                if (row.Pairs.Count > 0) runX += 10;          // CellLeft gap (measured)
                row.Pairs.Add((DnnPlainText(body), wpx * 0.75, "", runX * 0.75));
                runX += wpx;
                pos = openIdx + open.Length + body.Length + "</div>".Length;
                continue;
            }

            if (cls.Contains("ContainerFieldControlHoriz", StringComparison.OrdinalIgnoreCase))
            {
                var body = DnnInnerDiv(inner, openIdx);
                if (body is null) { pos = openIdx + open.Length; continue; }
                var wm = Regex.Match(st, @"width\s*:\s*([\d.]+)px", RegexOptions.IgnoreCase);
                if (wm.Success) runX += DtpNum(wm.Groups[1].Value);
                if (row is { Pairs.Count: > 0 } && row.Pairs[^1].Value.Length == 0)
                {
                    var last = row.Pairs[^1];
                    row.Pairs[^1] = (last.Label, last.LabelW, DnnControlValue(body), last.PairX);
                }
                pos = openIdx + open.Length + body.Length + "</div>".Length;
                continue;
            }

            if (cls.Contains("RowPad", StringComparison.OrdinalIgnoreCase))
            {
                Flush();
                var body = DnnInnerDiv(inner, openIdx);
                if (body is null) { pos = openIdx + open.Length; continue; }
                pos = openIdx + open.Length + body.Length + "</div>".Length;
                if (!body.Contains("ContainerField", StringComparison.OrdinalIgnoreCase)
                    && DnnPlainText(body).Length == 0)
                {
                    // no visible content: bare padding — but an &nbsp; keeps
                    // its full line box between the pads (23 px, like a row)
                    var nbsp = body.Contains("&nbsp;", StringComparison.OrdinalIgnoreCase)
                        || body.IndexOf(' ') >= 0;
                    blocks.Add(new DnnGap { H = (nbsp ? 23 : 10) * 0.75 });
                    lastRowPad = true;
                    continue;
                }
                // Nested RowPads keep their own pads, but a wrapper whose
                // content ENDS with a RowPad drops its bottom pad — the
                // measured 17.25 pt row pitch holds across ContactNamePanel's
                // double wrap where naive padding would add 3.75.
                blocks.Add(new DnnGap { H = 5 * 0.75 });        // padding-top
                var endsWithRowPad = DnnWalk(body, indentPx, blocks, true);
                if (!endsWithRowPad)
                    blocks.Add(new DnnGap { H = 5 * 0.75 });    // padding-bottom
                lastRowPad = true;
                continue;
            }

            // any other wrapper: descend
            pos = openIdx + open.Length;
        }
        Flush();
        return lastRowPad;
    }

    // Grid metrics (all measured on the expected render's Addresses and
    // Specialties DataLists): row heights, text seats below the row top, and
    // the 4px ListCell padding that joins each declared column width.
    private const double DnnGridHeaderHPt = 19.9;
    private const double DnnGridSummaryHPt = 20.3;
    private const double DnnGridTextSeatPt = 5.2;
    private const double DnnGridCellPadPx = 8.0;
    // Header/summary cell text inset from the cell's left edge (measured
    // 145.0 for the Address Type header at column x 133).
    private const double DnnGridCellTextPadPt = 12.0;

    /// <summary>A GridContainer DataList: GridRowHeader th cells, then
    /// GridRowEven/Odd records — a summary row of ListCell tds plus a details
    /// panel of ordinary field rows. Column x's scale the declared px widths
    /// (+4px padding each side) onto the grid width.</summary>
    private static void DnnParseGrid(string grid, double indentPx, List<DnnBlock> blocks)
    {
        var hasHeader = grid.Contains("GridRowHeader", StringComparison.OrdinalIgnoreCase);
        foreach (Match td in Regex.Matches(grid,
                     @"<td\b[^>]*class\s*=\s*[""'](GridRowHeader|GridRowEven|GridRowOdd)[""'][^>]*>",
                     RegexOptions.IgnoreCase))
        {
            var kind = td.Groups[1].Value;
            var end = DnnTagEnd(grid, td.Index, "td");
            if (end < 0) continue;
            var cell = grid[(grid.IndexOf('>', td.Index) + 1)..end];

            if (kind.Equals("GridRowHeader", StringComparison.OrdinalIgnoreCase))
            {
                var header = new DnnGridHeader();
                foreach (Match th in Regex.Matches(cell, @"<th\b([^>]*)>([\s\S]*?)</th>",
                             RegexOptions.IgnoreCase))
                {
                    var wm = Regex.Match(th.Groups[1].Value, @"width\s*:\s*([\d.]+)px",
                        RegexOptions.IgnoreCase);
                    header.Cells.Add((DnnPlainText(th.Groups[2].Value),
                        wm.Success ? DtpNum(wm.Groups[1].Value) : 100));
                }
                if (header.Cells.Count > 0) blocks.Add(header);
                continue;
            }

            // record: summary ItemTable row + details panel
            var content = new List<DnnBlock>();
            var itemTr = Regex.Match(cell,
                @"<table\b[^>]*_ItemTable[^>]*>[\s\S]*?<tr\b[^>]*>([\s\S]*?)</tr>",
                RegexOptions.IgnoreCase);
            var rest = cell;
            if (itemTr.Success)
            {
                var summary = new DnnGridRow();
                foreach (Match tc in Regex.Matches(itemTr.Groups[1].Value,
                             @"<td\b([^>]*)>([\s\S]*?)</td>", RegexOptions.IgnoreCase))
                {
                    var wm = Regex.Match(tc.Groups[1].Value, @"width\s*:\s*([\d.]+)px",
                        RegexOptions.IgnoreCase);
                    summary.Cells.Add((DnnPlainText(DnnStripHidden(tc.Groups[2].Value)),
                        wm.Success ? DtpNum(wm.Groups[1].Value) : 100));
                }
                content.Add(summary);
                var tblEnd = DnnTagEnd(cell,
                    Regex.Match(cell, @"<table\b[^>]*_ItemTable[^>]*>", RegexOptions.IgnoreCase).Index,
                    "table");
                if (tblEnd >= 0) rest = cell[(cell.IndexOf('>', tblEnd) + 1)..];
            }
            DnnWalk(rest, indentPx, content);
            double h = 0;
            foreach (var b in content)
                h += b switch
                {
                    DnnGap g => g.H,
                    DnnFieldRow r => DnnRowLinePt + (r.Bare ? 5 * 0.75 : 0),
                    DnnBand => DnnBandPt,
                    DnnGridRow => DnnGridSummaryHPt,
                    DnnParaLine p => p.Pitch,
                    _ => 0.0,
                };
            blocks.Add(new DnnGridFill
            {
                H = h,
                White = kind.Equals("GridRowOdd", StringComparison.OrdinalIgnoreCase),
                FullWidth = !hasHeader,
            });
            blocks.AddRange(content);
        }
    }

    /// <summary>Remove PrintHidden / display:none subtrees.</summary>
    private static string DnnStripHidden(string html)
    {
        for (var guard = 0; guard < 32; guard++)
        {
            var m = Regex.Match(html,
                @"<(div|span|a)\b[^>]*(?:PrintHidden|display\s*:\s*none)[^>]*>",
                RegexOptions.IgnoreCase);
            if (!m.Success) break;
            var end = DnnTagEnd(html, m.Index, m.Groups[1].Value);
            if (end < 0) break;
            var close = html.IndexOf('>', end);
            html = html.Remove(m.Index, close + 1 - m.Index);
        }
        return html;
    }

    /// <summary>Index just before the matching close tag of the element opening
    /// at <paramref name="openIdx"/>.</summary>
    private static int DnnTagEnd(string html, int openIdx, string tag)
    {
        var end = html.IndexOf('>', openIdx);
        if (end < 0) return -1;
        var depth = 1;
        var rx = new Regex(@"<(/?)" + tag + @"\b[^>]*>", RegexOptions.IgnoreCase);
        var scan = end + 1;
        while (depth > 0)
        {
            var m = rx.Match(html, scan);
            if (!m.Success) return -1;
            depth += m.Groups[1].Value.Length > 0 ? -1 : 1;
            if (depth == 0) return m.Index;
            scan = m.Index + m.Length;
        }
        return -1;
    }

    /// <summary>Visible text of a control cell: selected option of a select,
    /// value of a text input, the checked radio's label, or the inline text;
    /// an empty control renders as an underscore.</summary>
    private static string DnnControlValue(string inner)
    {
        var sel = Regex.Match(inner,
            @"<option[^>]*\bselected\b[^>]*>([^<]*)</option>", RegexOptions.IgnoreCase);
        if (sel.Success) return EdgarHtmlRenderer.DecodeEntities(sel.Groups[1].Value).Trim();
        var chk = Regex.Match(inner,
            @"<input[^>]*\bchecked\b[^>]*>\s*<label[^>]*>([^<]*)</label>", RegexOptions.IgnoreCase);
        if (chk.Success) return EdgarHtmlRenderer.DecodeEntities(chk.Groups[1].Value).Trim();
        var inp = Regex.Match(inner, @"<input[^>]*>", RegexOptions.IgnoreCase);
        if (inp.Success && DtpAttr(inp.Value, "type") is not "hidden")
        {
            var v = DtpAttr(inp.Value, "value");
            if (!string.IsNullOrWhiteSpace(v)) return EdgarHtmlRenderer.DecodeEntities(v).Trim();
        }
        var t = DnnPlainText(inner);
        return t.Length > 0 ? t : "_";
    }

    /// <summary>Tag-stripped, entity-decoded, whitespace-collapsed text.</summary>
    private static string DnnPlainText(string inner)
    {
        var t = Regex.Replace(inner, @"<[^>]+>", " ");
        t = EdgarHtmlRenderer.DecodeEntities(t);
        return Regex.Replace(t, @"\s+", " ").Trim();
    }
}
