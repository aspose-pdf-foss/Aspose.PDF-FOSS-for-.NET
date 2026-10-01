using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// The D3 VERTICAL-BAR-CHART export (the `#vertical-bar-chart` id namespace): a
// title/subtitle band over a flex row of three inline SVGs — the rotated-label
// axis svg, the y-axis svg, and the bars svg — followed by the centred axis
// caption. The expected render draws the svg content as REAL vector fills and
// Times text: each svg is CLIPPED to its own viewport box anchored at its
// containing div's x (a div narrower than its svg advances the flex row by the
// DIV width while the svg overflows into the neighbour), `<line>`/zero-area
// paths stroke hairlines, area paths fill, `<rect>` fills, and `<text>` honours
// x/y/dx/dy (em against the inherited 12px), text-anchor:end and rotate(-45).
// All constants are measured values (titles centred on the page,
// .lastN-container at its 75% share centred in the 608 pt content box, the
// chart row opening at content-top + one UA text line + the two 40px bands).
internal static partial class HtmlToPdfConverter
{
    private sealed class SvgTextEl
    {
        public double X, Y, DxEm, DyEm;    // svg px / em offsets
        public bool AnchorEnd;
        public bool Rotate45;              // rotate(-45) about the (x,y) pivot
        public string Text = "";
        public double FontPx = 12.0;
    }

    /// <summary>Render the vertical-bar-chart export, or null when the page does
    /// not carry the dialect's id.</summary>
    private static Document? TryRenderBarChart(string html, double pageWidth, double pageHeight)
    {
        var bh = new BarChartRenderState();
        bh.html = html;
        bh.pageWidth = pageWidth;
        bh.pageHeight = pageHeight;
        bh.marginLeft = 90.0;
        bh.marginTop = 72.0;
        if (!Regex.IsMatch(bh.html, @"id\s*=\s*['""]vertical-bar-chart['""]",
                RegexOptions.IgnoreCase)
            || !Regex.IsMatch(bh.html, @"<svg\b", RegexOptions.IgnoreCase))
            return null;
        if (WinMetricsFor("Times New Roman") is null) return null;

        bh.invc = System.Globalization.CultureInfo.InvariantCulture;
        bh.doc = new Document();
        bh.page = bh.doc.Pages.Add(bh.pageWidth, bh.pageHeight);
        EnsureFonts(bh.page);

        bh.contentL = bh.marginLeft + UaBodyMarginPt;
        bh.contentR = bh.pageWidth - bh.marginLeft - UaBodyMarginPt;
        bh.contentW = bh.contentR - bh.contentL;
        bh.chartGray = Color.FromArgb(136, 136, 136);      // #888888

        bh.sb = new StringBuilder();
        bh.yTd = bh.marginTop + UaBodyMarginPt;
        bh.chartOpen = Regex.Match(bh.html, @"<div\b[^>]*id\s*=\s*['""]vertical-bar-chart['""]",
            RegexOptions.IgnoreCase);
        bh.lead = Regex.Replace(bh.html[..bh.chartOpen.Index], @"<[^>]+>", " ");
        bh.lead = Regex.Replace(DecodeEntities(bh.lead), @"\s+", " ").Trim();
        if (bh.lead.Length > 0)
        {
            BcText(bh, bh.lead, bh.contentL, bh.yTd + UaSerifBaselineDropPt, 12, Color.FromArgb(0, 0, 0));
            bh.yTd += PpLineBoxPt;
        }

        // title / subTitle bands: declared heights, centred on the page
        BcBand(bh, "title", 18);
        BcBand(bh, "subTitle", 12);

        bh.container = Regex.Match(bh.html,
            @"<div\b[^>]*class\s*=\s*['""]lastN-container['""](?<tag>[^>]*)>", RegexOptions.IgnoreCase);
        bh.contPct = bh.container.Success
            ? BcPx(bh, BcStyleProp("<x " + bh.container.Groups["tag"].Value + ">", "width"), 75) / 100.0 : 0.75;
        bh.contW = bh.contentW * bh.contPct;
        bh.contX = bh.contentL + (bh.contentW - bh.contW) / 2;
        bh.rowTopTd = bh.yTd;
        bh.rowM = Regex.Match(bh.html,
            @"<div\b[^>]*class\s*=\s*['""]xAxis-and-chart['""](?<tag>[^>]*)>",
            RegexOptions.IgnoreCase);
        bh.rowHPt = bh.rowM.Success
            ? BcPx(bh, BcStyleProp("<x " + bh.rowM.Groups["tag"].Value + ">", "height"), 500) * 0.75 : 375;

        bh.penX = bh.contX;
        bh.rowEnd = bh.html.Length;
        if (bh.rowM.Success)
        {
            // child divs of the flex row, in order
            foreach (Match dm in Regex.Matches(bh.html[bh.rowM.Index..],
                @"<div\b(?<tag>[^>]*)>", RegexOptions.IgnoreCase))
            {
                if (dm.Index == 0) continue;
                var dTag = dm.Groups["tag"].Value;
                var dW = BcPx(bh, BcStyleProp("<x " + dTag + ">", "width"), -1);
                var dPct = BcStyleProp("<x " + dTag + ">", "width")?.Trim().EndsWith("%") == true;
                var abs = bh.rowM.Index + dm.Index;
                // the svg (if any) opening before the next div child
                var svgM = Regex.Match(bh.html[abs..], @"<svg\b(?<tag>[^>]*)>(?<body>[\s\S]*?)</svg>",
                    RegexOptions.IgnoreCase);
                var divWPt = dPct ? (bh.contX + bh.contW - bh.penX)
                    : dW > 0 ? dW * 0.75 : 0;
                if (svgM.Success && svgM.Index < 2000)
                    DrawSvg(bh.sb, bh.html[(abs + svgM.Index)..(abs + svgM.Index + svgM.Length)],
                        bh.penX, bh.rowTopTd, bh.pageHeight, bh.chartGray, bh.invc);
                bh.penX += divWPt;
                if (bh.penX >= bh.contX + bh.contW - 1) break;
                if (abs > bh.rowEnd) break;
            }
        }
        bh.yTd = bh.rowTopTd + bh.rowHPt;

        bh.capM = Regex.Match(bh.html,
            @"<div\b[^>]*class\s*=\s*['""]xAxisLabel['""][^>]*\bwidth:\s*100px[^>]*>(?<body>[^<]*)</div>",
            RegexOptions.IgnoreCase);
        if (!bh.capM.Success)
            bh.capM = Regex.Match(bh.html,
                @"<div\b[^>]*class\s*=\s*['""]xAxisLabel['""][^>]*>(?<body>[^<]*)</div>",
                RegexOptions.IgnoreCase);
        if (bh.capM.Success && bh.capM.Groups["body"].Value.Trim().Length > 0)
            BcText(bh, bh.capM.Groups["body"].Value.Trim(), bh.contX + (bh.contW - 75) / 2,
                bh.yTd + 9 * (UaSerifBaselineDropPt / 12.0), 9, bh.chartGray);

        bh.page.AddContentStream(Encoding.ASCII.GetBytes(bh.sb.ToString()));
        return bh.doc;
    }

    // The UA serif baseline seat inside its 13.5 line box at 12 pt (measured:
    // the 12 pt body line's baseline sits 10.8 under the line-box top).
    private const double UaSerifBaselineDropPt = 10.8;

    // ── the EMBER/jsPlumb ORG CHART (the `ember-view chart` id namespace) ──
    // Absolutely positioned `.node` cards — a white box behind a DOUBLE border
    // (two 1/3 pt hairlines per side at the measured 0.2/0.8 insets), 10px
    // Times text on the 11px line clipped by overflow:hidden — joined by the
    // jsPlumb connector svgs, whose paths paint by standard SVG rules (the
    // polyline strokes its #567567 1px pen, the arrowhead fills, the endpoint
    // dot svgs are degenerate and leave no ink). The sheet is the UA content
    // origin (96, 78) plus px×0.75, widened to one page margin past the
    // right-most card's border box.
    private const double EmCardBorderPt = 1.0;     // the double border's total side
    private const double EmCardFsPt = 7.5;         // 10px
    private const double EmCardLineHPt = 8.25;     // 11px
    private const double EmCardPadPt = 1.05;       // text inset off the border box
    private const double EmInnerWidthFrac = 0.9;   // div.new width:90%

    private static Document? TryRenderEmberChart(string html, double pageWidth, double pageHeight)
    {
        var ec = new EmberChartRenderState();
        ec.html = html;
        ec.pageWidth = pageWidth;
        ec.pageHeight = pageHeight;
        if (!Regex.IsMatch(ec.html, @"class\s*=\s*[""']ember-view chart[""']", RegexOptions.IgnoreCase)
            || !ec.html.Contains("_jsPlumb_connector", StringComparison.OrdinalIgnoreCase))
            return null;
        if (WinMetricsFor("Times New Roman") is not { } fm) return null;
        ec.invc = System.Globalization.CultureInfo.InvariantCulture;
        ec.originX = 96.0;               // 90 + the UA body margin
        ec.originY = 78.0;               // 72 + the UA body margin


        ec.cards = new List<(double x, double y, double w, double h, List<string> fields)>();
        ec.maxRight = 0.0;
        CollectEmberCards(ec);
        if (ec.cards.Count == 0) return null;

        ec.doc = new Document();
        ec.sheetW = Math.Max(ec.pageWidth, ec.maxRight + 90.0);
        ec.page = ec.doc.Pages.Add(ec.sheetW, ec.pageHeight);
        EnsureFonts(ec.page);
        ec.sb = new StringBuilder();
        ec.drop = MetricBaselineDrop(EmCardFsPt, EmCardLineHPt, fm);

        foreach (var (cx, cy, cw, ch, fields) in ec.cards)
        {
            if (!RenderEmberCard(ec, cx, cy, cw, ch, fields)) break;
        }

        // ── the connectors: per svg, the polyline strokes and the arrow fills ──
        foreach (Match sm in Regex.Matches(ec.html,
            @"<svg(?<attrs>[^>]*_jsPlumb_connector[^>]*)>(?<body>[\s\S]*?)</svg>",
            RegexOptions.IgnoreCase))
        {
            var attrs = sm.Groups["attrs"].Value;
            var ox = ec.originX + EmberPx(ec, attrs, "left") * 0.75;
            var oy = ec.originY + EmberPx(ec, attrs, "top") * 0.75;
            foreach (Match pmM in Regex.Matches(sm.Groups["body"].Value,
                @"<path\b(?<pa>[^>]*)>", RegexOptions.IgnoreCase))
            {
                var pa = pmM.Groups["pa"].Value;
                var dM = Regex.Match(pa, @"\bd\s*=\s*[""']([^""']*)", RegexOptions.IgnoreCase);
                if (!dM.Success) continue;
                double tx = 0, ty = 0;
                var trM = Regex.Match(pa, @"translate\(\s*(-?[\d.eE+-]+)\s*[, ]\s*(-?[\d.eE+-]+)",
                    RegexOptions.IgnoreCase);
                if (trM.Success)
                {
                    tx = double.Parse(trM.Groups[1].Value, ec.invc);
                    ty = double.Parse(trM.Groups[2].Value, ec.invc);
                }
                var strokeM = Regex.Match(pa, @"stroke\s*=\s*[""']\s*(#[0-9a-fA-F]{3,6}|none)",
                    RegexOptions.IgnoreCase);
                var fillM = Regex.Match(pa, @"fill\s*=\s*[""']\s*(#[0-9a-fA-F]{3,6}|none)",
                    RegexOptions.IgnoreCase);
                var stroke = strokeM.Success && strokeM.Groups[1].Value != "none"
                    ? ParseCssColor(strokeM.Groups[1].Value) : null;
                var fill = fillM.Success && fillM.Groups[1].Value != "none"
                    ? ParseCssColor(fillM.Groups[1].Value) : null;
                if (stroke is null && fill is null) continue;
                // path: M/L pairs only (the jsPlumb flowchart segments + arrows)
                var ops = new StringBuilder();
                foreach (Match cmd in Regex.Matches(dM.Groups[1].Value,
                    @"([ML])\s*(-?[\d.eE+]+)[,\s]+(-?[\d.eE+]+)", RegexOptions.IgnoreCase))
                {
                    var px2 = ox + (tx + double.Parse(cmd.Groups[2].Value, ec.invc)) * 0.75;
                    var py2 = oy + (ty + double.Parse(cmd.Groups[3].Value, ec.invc)) * 0.75;
                    ops.Append(Compat.Format(ec.invc,
                        $"{px2:0.##} {ec.pageHeight - py2:0.##} {(cmd.Groups[1].Value.ToUpperInvariant() == "M" ? "m" : "l")} "));
                }
                if (ops.Length == 0) continue;
                if (fill is { } fc)
                    ec.sb.Append(Compat.Format(ec.invc,
                        $"q {fc.R / 255.0:0.###} {fc.G / 255.0:0.###} {fc.B / 255.0:0.###} rg "))
                        .Append(ops).Append("f Q\n");
                if (stroke is { } sc)
                    ec.sb.Append(Compat.Format(ec.invc,
                        $"q {sc.R / 255.0:0.###} {sc.G / 255.0:0.###} {sc.B / 255.0:0.###} RG 0.75 w "))
                        .Append(ops).Append("S Q\n");
            }
        }
        ec.page.AddContentStream(Encoding.ASCII.GetBytes(ec.sb.ToString()));
        return ec.doc;
    }

    /// <summary>Draw one inline svg's primitive content — g/translate chains,
    /// line, rect, area/degenerate paths, and text with anchors, em offsets and
    /// rotate(-45) — clipped to the svg viewport anchored at (xPt, yTopTd).</summary>
    private static void DrawSvg(StringBuilder sb, string svg, double xPt, double yTopTd,
        double pageHeight, Color chartGray, System.Globalization.CultureInfo invc)
    {
        var bv = new BarSvgState();
        bv.sb = sb;
        bv.svg = svg;
        bv.xPt = xPt;
        bv.yTopTd = yTopTd;
        bv.pageHeight = pageHeight;
        bv.chartGray = chartGray;
        bv.invc = invc;
        bv.open = Regex.Match(bv.svg, @"<svg\b[^>]*>", RegexOptions.IgnoreCase);
        bv.vw = AttrF(bv, bv.open.Value, "width", 0) * 0.75;
        bv.vh = AttrF(bv, bv.open.Value, "height", 0) * 0.75;
        if (bv.vw <= 0 || bv.vh <= 0) return;
        bv.sb.Append(Compat.Format(bv.invc,
            $"q {bv.xPt:0.##} {bv.pageHeight - bv.yTopTd - bv.vh:0.##} {bv.vw:0.##} {bv.vh:0.##} re W n\n"));

        bv.stack = new Stack<(double tx, double ty, string fill)>();
        bv.stack.Push((0, 0, "#888888"));
        foreach (Match t in Regex.Matches(bv.svg,
            @"<(?<close>/)?(?<tag>g|line|rect|text|path)\b(?<attrs>[^>]*?)(?<self>/)?>(?(close)|(?<body>(?=[^<]*</text>)[^<]*)?)",
            RegexOptions.IgnoreCase))
        {
            if (!ReplayBarSvgTag(bv, t)) break;
        }
        bv.sb.Append("Q\n");
    }
}
