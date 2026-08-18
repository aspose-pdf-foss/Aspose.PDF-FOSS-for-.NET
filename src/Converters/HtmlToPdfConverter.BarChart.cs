using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// The D3 VERTICAL-BAR-CHART export (the `#vertical-bar-chart` id namespace): a
// title/subtitle band over a flex row of three inline SVGs — the rotated-label
// axis svg, the y-axis svg, and the bars svg — followed by the centred axis
// caption. The source renderer draws the svg content as REAL vector fills and
// Times text: each svg is CLIPPED to its own viewport box anchored at its
// containing div's x (a div narrower than its svg advances the flex row by the
// DIV width while the svg overflows into the neighbour), `<line>`/zero-area
// paths stroke hairlines, area paths fill, `<rect>` fills, and `<text>` honours
// x/y/dx/dy (em against the inherited 12px), text-anchor:end and rotate(-45).
// All constants measured on the reference (titles centred on the page,
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
    private static Document? TryRenderBarChart(string html,
        double pageWidth, double pageHeight)
    {
        // The reference lays this export out on the UA sheet: 90 pt side margins
        // + the 6 pt body margin, 72 pt top + the same body margin (measured:
        // the stray lead glyph at (96, 78)).
        const double marginLeft = 90.0;
        const double marginTop = 72.0;
        if (!Regex.IsMatch(html, @"id\s*=\s*['""]vertical-bar-chart['""]",
                RegexOptions.IgnoreCase)
            || !Regex.IsMatch(html, @"<svg\b", RegexOptions.IgnoreCase))
            return null;
        if (WinMetricsFor("Times New Roman") is null) return null;

        var invc = System.Globalization.CultureInfo.InvariantCulture;
        double Px(string? v, double dflt = 0)
            => v is not null && double.TryParse(v.Trim().TrimEnd('p', 'x', '%'),
                System.Globalization.NumberStyles.Float, invc, out var d) ? d : dflt;
        static string? StyleProp(string tag, string prop)
            => Regex.Match(tag, @"style\s*=\s*['""][^'""]*" + prop + @"\s*:\s*([^;'""]+)",
                RegexOptions.IgnoreCase) is { Success: true } m
                ? m.Groups[1].Value.Trim() : null;

        var doc = new Document();
        var page = doc.Pages.Add(pageWidth, pageHeight);
        EnsureFonts(page);

        var contentL = marginLeft + UaBodyMarginPt;
        var contentR = pageWidth - marginLeft - UaBodyMarginPt;
        var contentW = contentR - contentL;
        var chartGray = Color.FromRgb(136, 136, 136);      // #888888

        var sb = new StringBuilder();
        void Text(string text, double x, double yBaselineTd, double fs, Color c)
        {
            if (text.Length == 0) return;
            sb.Append(string.Create(invc,
                $"q {c.R / 255.0:0.###} {c.G / 255.0:0.###} {c.B / 255.0:0.###} rg BT /F5 {fs:0.##} Tf " +
                $"1 0 0 1 {x:0.##} {pageHeight - yBaselineTd:0.##} Tm ({EscapePdfString(text)}) Tj ET Q\n"));
        }

        // ── the document flow above the chart ──
        // Any bare text before the chart div sets one UA text line at the origin.
        var yTd = marginTop + UaBodyMarginPt;
        var chartOpen = Regex.Match(html, @"<div\b[^>]*id\s*=\s*['""]vertical-bar-chart['""]",
            RegexOptions.IgnoreCase);
        var lead = Regex.Replace(html[..chartOpen.Index], @"<[^>]+>", " ");
        lead = Regex.Replace(DecodeEntities(lead), @"\s+", " ").Trim();
        if (lead.Length > 0)
        {
            Text(lead, contentL, yTd + UaSerifBaselineDropPt, 12, Color.FromRgb(0, 0, 0));
            yTd += PpLineBoxPt;
        }

        // title / subTitle bands: declared heights, centred on the page
        void Band(string cls, double dfltFsPx)
        {
            var m = Regex.Match(html,
                @"<div\b[^>]*class\s*=\s*['""]" + cls + @"['""](?<tag>[^>]*)>(?<body>[\s\S]*?)</div>",
                RegexOptions.IgnoreCase);
            if (!m.Success) return;
            var tag = m.Groups["tag"].Value;
            var fsPt = Px(StyleProp("<x " + tag + ">", "font-size"), dfltFsPx) * 0.75;
            var hPt = Px(StyleProp("<x " + tag + ">", "height"), 40) * 0.75;
            var col = ParseCssColor(StyleProp("<x " + tag + ">", "color") ?? "") ?? chartGray;
            var txt = Regex.Replace(DecodeEntities(
                Regex.Replace(m.Groups["body"].Value, @"<[^>]+>", " ")), @"\s+", " ").Trim();
            var w = MeasureFaceText("Times New Roman", txt, fsPt);
            Text(txt, (pageWidth - w) / 2, yTd + fsPt * (UaSerifBaselineDropPt / 12.0), fsPt, col);
            yTd += hPt;
        }
        Band("title", 18);
        Band("subTitle", 12);

        // ── the chart row: three svgs in a flex row inside the centred 75% box ──
        var container = Regex.Match(html,
            @"<div\b[^>]*class\s*=\s*['""]lastN-container['""](?<tag>[^>]*)>", RegexOptions.IgnoreCase);
        var contPct = container.Success
            ? Px(StyleProp("<x " + container.Groups["tag"].Value + ">", "width"), 75) / 100.0 : 0.75;
        var contW = contentW * contPct;
        var contX = contentL + (contentW - contW) / 2;
        var rowTopTd = yTd;
        var rowM = Regex.Match(html,
            @"<div\b[^>]*class\s*=\s*['""]xAxis-and-chart['""](?<tag>[^>]*)>",
            RegexOptions.IgnoreCase);
        var rowHPt = rowM.Success
            ? Px(StyleProp("<x " + rowM.Groups["tag"].Value + ">", "height"), 500) * 0.75 : 375;

        // walk the row's immediate div children: each advances the pen by ITS
        // declared width; an svg inside draws clipped to the SVG box at the div's x
        var penX = contX;
        var rowEnd = html.Length;
        if (rowM.Success)
        {
            // child divs of the flex row, in order
            foreach (Match dm in Regex.Matches(html[rowM.Index..],
                @"<div\b(?<tag>[^>]*)>", RegexOptions.IgnoreCase))
            {
                if (dm.Index == 0) continue;
                var dTag = dm.Groups["tag"].Value;
                var dW = Px(StyleProp("<x " + dTag + ">", "width"), -1);
                var dPct = StyleProp("<x " + dTag + ">", "width")?.Trim().EndsWith("%") == true;
                var abs = rowM.Index + dm.Index;
                // the svg (if any) opening before the next div child
                var svgM = Regex.Match(html[abs..], @"<svg\b(?<tag>[^>]*)>(?<body>[\s\S]*?)</svg>",
                    RegexOptions.IgnoreCase);
                var divWPt = dPct ? (contX + contW - penX)
                    : dW > 0 ? dW * 0.75 : 0;
                if (svgM.Success && svgM.Index < 2000)
                    DrawSvg(sb, html[(abs + svgM.Index)..(abs + svgM.Index + svgM.Length)],
                        penX, rowTopTd, pageHeight, chartGray, invc);
                penX += divWPt;
                if (penX >= contX + contW - 1) break;
                if (abs > rowEnd) break;
            }
        }
        yTd = rowTopTd + rowHPt;

        // the axis caption: its 100px box centred in the container, text left in it
        var capM = Regex.Match(html,
            @"<div\b[^>]*class\s*=\s*['""]xAxisLabel['""][^>]*\bwidth:\s*100px[^>]*>(?<body>[^<]*)</div>",
            RegexOptions.IgnoreCase);
        if (!capM.Success)
            capM = Regex.Match(html,
                @"<div\b[^>]*class\s*=\s*['""]xAxisLabel['""][^>]*>(?<body>[^<]*)</div>",
                RegexOptions.IgnoreCase);
        if (capM.Success && capM.Groups["body"].Value.Trim().Length > 0)
            Text(capM.Groups["body"].Value.Trim(), contX + (contW - 75) / 2,
                yTd + 9 * (UaSerifBaselineDropPt / 12.0), 9, chartGray);

        page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
        return doc;
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

    private static Document? TryRenderEmberChart(string html,
        double pageWidth, double pageHeight)
    {
        if (!Regex.IsMatch(html, @"class\s*=\s*[""']ember-view chart[""']", RegexOptions.IgnoreCase)
            || !html.Contains("_jsPlumb_connector", StringComparison.OrdinalIgnoreCase))
            return null;
        if (WinMetricsFor("Times New Roman") is not { } fm) return null;
        var invc = System.Globalization.CultureInfo.InvariantCulture;
        const double originX = 96.0;               // 90 + the UA body margin
        const double originY = 78.0;               // 72 + the UA body margin

        double Px(string st, string key, double dflt = 0)
            => Regex.Match(st, key + @"\s*:\s*(-?[\d.]+)px", RegexOptions.IgnoreCase)
                is { Success: true } m
                ? double.Parse(m.Groups[1].Value, invc) : dflt;

        // ── the cards ──
        var cards = new List<(double x, double y, double w, double h, List<string> fields)>();
        var maxRight = 0.0;
        foreach (Match nm in Regex.Matches(html,
            @"<div\b[^>]*class\s*=\s*[""']ember-view node[^""']*[""'][^>]*style\s*=\s*[""'](?<st>[^""']*)[""'][^>]*>",
            RegexOptions.IgnoreCase))
        {
            var st = nm.Groups["st"].Value;
            var end = FindDivClose(html, nm.Index + nm.Length);
            var body = html[(nm.Index + nm.Length)..end];
            var fields = new List<string>();
            foreach (Match fmM in Regex.Matches(body,
                @"<div\b[^>]*class\s*=\s*[""']ember-view[""'][^>]*>(?<t>[\s\S]*?)</div>",
                RegexOptions.IgnoreCase))
            {
                var t = Regex.Replace(DecodeEntities(Regex.Replace(
                    fmM.Groups["t"].Value, @"<[^>]+>", " ")), @"\s+", " ").Trim();
                if (t.Length > 0) fields.Add(t);
            }
            var cx = originX + Px(st, "left") * 0.75;
            var cy = originY + Px(st, "top") * 0.75;
            var cw = Px(st, "width") * 0.75 + 2 * EmCardBorderPt;
            var ch = Px(st, "height") * 0.75 + 2 * EmCardBorderPt;
            if (cw <= 2 || ch <= 2) continue;
            cards.Add((cx, cy, cw, ch, fields));
            maxRight = Math.Max(maxRight, cx + cw);
        }
        if (cards.Count == 0) return null;

        var doc = new Document();
        var sheetW = Math.Max(pageWidth, maxRight + 90.0);
        var page = doc.Pages.Add(sheetW, pageHeight);
        EnsureFonts(page);
        var sb = new StringBuilder();
        var drop = MetricBaselineDrop(EmCardFsPt, EmCardLineHPt, fm);

        foreach (var (cx, cy, cw, ch, fields) in cards)
        {
            // white box + the double border: two hairlines per side at the
            // measured insets
            sb.Append(string.Create(invc,
                $"q 1 1 1 rg {cx:0.##} {pageHeight - cy - ch:0.##} {cw:0.##} {ch:0.##} re f Q\n"));
            foreach (var inset in new[] { 0.2, 0.8 })
            {
                var l = cx + inset; var r = cx + cw - inset;
                var t = cy + inset; var b = cy + ch - inset;
                sb.Append("q 0 0 0 RG 0.33 w ");
                sb.Append(string.Create(invc,
                    $"{cx:0.##} {pageHeight - t:0.##} m {cx + cw:0.##} {pageHeight - t:0.##} l S "));
                sb.Append(string.Create(invc,
                    $"{cx:0.##} {pageHeight - b:0.##} m {cx + cw:0.##} {pageHeight - b:0.##} l S "));
                sb.Append(string.Create(invc,
                    $"{l:0.##} {pageHeight - cy:0.##} m {l:0.##} {pageHeight - cy - ch:0.##} l S "));
                sb.Append(string.Create(invc,
                    $"{r:0.##} {pageHeight - cy:0.##} m {r:0.##} {pageHeight - cy - ch:0.##} l S Q\n"));
            }
            // the fields wrap in the 90% inner box; overflow:hidden clips at the
            // card's content height
            var wrapW = (cw - 2 * EmCardBorderPt) * EmInnerWidthFrac;
            var pen = cy + EmCardBorderPt;
            var clipBottom = cy + ch - EmCardBorderPt;
            foreach (var f in fields)
            {
                foreach (var ln in MeasuredWordWrap(f, wrapW, "Times New Roman", EmCardFsPt))
                {
                    if (pen + EmCardLineHPt > clipBottom) break;
                    sb.Append(string.Create(invc,
                        $"BT /F5 {EmCardFsPt:0.##} Tf 1 0 0 1 {cx + EmCardPadPt:0.##} {pageHeight - pen - drop:0.##} Tm ({EscapePdfString(ln)}) Tj ET\n"));
                    pen += EmCardLineHPt;
                }
                if (pen + EmCardLineHPt > clipBottom) break;
            }
        }

        // ── the connectors: per svg, the polyline strokes and the arrow fills ──
        foreach (Match sm in Regex.Matches(html,
            @"<svg(?<attrs>[^>]*_jsPlumb_connector[^>]*)>(?<body>[\s\S]*?)</svg>",
            RegexOptions.IgnoreCase))
        {
            var attrs = sm.Groups["attrs"].Value;
            var ox = originX + Px(attrs, "left") * 0.75;
            var oy = originY + Px(attrs, "top") * 0.75;
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
                    tx = double.Parse(trM.Groups[1].Value, invc);
                    ty = double.Parse(trM.Groups[2].Value, invc);
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
                    var px2 = ox + (tx + double.Parse(cmd.Groups[2].Value, invc)) * 0.75;
                    var py2 = oy + (ty + double.Parse(cmd.Groups[3].Value, invc)) * 0.75;
                    ops.Append(string.Create(invc,
                        $"{px2:0.##} {pageHeight - py2:0.##} {(cmd.Groups[1].Value.ToUpperInvariant() == "M" ? "m" : "l")} "));
                }
                if (ops.Length == 0) continue;
                if (fill is { } fc)
                    sb.Append(string.Create(invc,
                        $"q {fc.R / 255.0:0.###} {fc.G / 255.0:0.###} {fc.B / 255.0:0.###} rg "))
                        .Append(ops).Append("f Q\n");
                if (stroke is { } sc)
                    sb.Append(string.Create(invc,
                        $"q {sc.R / 255.0:0.###} {sc.G / 255.0:0.###} {sc.B / 255.0:0.###} RG 0.75 w "))
                        .Append(ops).Append("S Q\n");
            }
        }
        page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
        return doc;
    }

    /// <summary>Draw one inline svg's primitive content — g/translate chains,
    /// line, rect, area/degenerate paths, and text with anchors, em offsets and
    /// rotate(-45) — clipped to the svg viewport anchored at (xPt, yTopTd).</summary>
    private static void DrawSvg(StringBuilder sb, string svg, double xPt, double yTopTd,
        double pageHeight, Color chartGray, System.Globalization.CultureInfo invc)
    {
        var open = Regex.Match(svg, @"<svg\b[^>]*>", RegexOptions.IgnoreCase);
        double AttrF(string tag, string name, double dflt)
            => Regex.Match(tag, name + @"\s*=\s*['""]([\d.-]+)", RegexOptions.IgnoreCase)
                is { Success: true } m
                ? double.Parse(m.Groups[1].Value, invc) : dflt;
        var vw = AttrF(open.Value, "width", 0) * 0.75;
        var vh = AttrF(open.Value, "height", 0) * 0.75;
        if (vw <= 0 || vh <= 0) return;
        sb.Append(string.Create(invc,
            $"q {xPt:0.##} {pageHeight - yTopTd - vh:0.##} {vw:0.##} {vh:0.##} re W n\n"));

        // tokenizer over g/line/rect/text/path with a translate stack
        var stack = new Stack<(double tx, double ty, string fill)>();
        stack.Push((0, 0, "#888888"));
        foreach (Match t in Regex.Matches(svg,
            @"<(?<close>/)?(?<tag>g|line|rect|text|path)\b(?<attrs>[^>]*?)(?<self>/)?>(?(close)|(?<body>(?=[^<]*</text>)[^<]*)?)",
            RegexOptions.IgnoreCase))
        {
            var tag = t.Groups["tag"].Value.ToLowerInvariant();
            var attrs = t.Groups["attrs"].Value;
            var top = stack.Peek();
            if (t.Groups["close"].Success)
            {
                if (tag == "g" && stack.Count > 1) stack.Pop();
                continue;
            }
            double MapX(double px) => xPt + (top.tx + px) * 0.75;
            double MapYTd(double px) => yTopTd + (top.ty + px) * 0.75;
            switch (tag)
            {
                case "g":
                {
                    var (gx, gy) = (top.tx, top.ty);
                    var tr = Regex.Match(attrs,
                        @"translate\(\s*([\d.-]+)\s*[, ]\s*([\d.-]+)\s*\)", RegexOptions.IgnoreCase);
                    if (tr.Success)
                    {
                        gx += double.Parse(tr.Groups[1].Value, invc);
                        gy += double.Parse(tr.Groups[2].Value, invc);
                    }
                    var gFill = Regex.Match(attrs, @"fill\s*:\s*(#[0-9a-fA-F]{3,6})")
                        is { Success: true } gf ? gf.Groups[1].Value : top.fill;
                    stack.Push((gx, gy, gFill));
                    if (t.Groups["self"].Success && stack.Count > 1) stack.Pop();
                    break;
                }
                case "line":
                    // The reference FILLS svg geometry — a line has no area, so
                    // it leaves no ink (the grid/tick lines are invisible on the
                    // reference render).
                    break;
                case "rect":
                {
                    var rx = MapX(AttrF(attrs, "x", 0));
                    var ryTd = MapYTd(AttrF(attrs, "y", 0));
                    var rw = AttrF(attrs, "width", 0) * 0.75;
                    var rh = AttrF(attrs, "height", 0) * 0.75;
                    if (rw <= 0 || rh <= 0) break;
                    var fill = ParseCssColor(top.fill) ?? chartGray;
                    sb.Append(string.Create(invc,
                        $"q {fill.R / 255.0:0.###} {fill.G / 255.0:0.###} {fill.B / 255.0:0.###} rg " +
                        $"{rx:0.##} {pageHeight - ryTd - rh:0.##} {rw:0.##} {rh:0.##} re f Q\n"));
                    break;
                }
                case "path":
                {
                    // The two axis-domain shapes: an area path fills its polygon
                    // (the thick x-axis band); a degenerate zero-width one
                    // strokes the hairline the reference still shows.
                    var dAttr = Regex.Match(attrs, @"\bd\s*=\s*['""]([^'""]*)['""]",
                        RegexOptions.IgnoreCase) is { Success: true } dm
                        ? dm.Groups[1].Value : "";
                    var pts = new List<(double x, double y)>();
                    double cx = 0, cy = 0;
                    foreach (Match c in Regex.Matches(dAttr, @"([MVHmvh])\s*([\d.,\s-]*)"))
                    {
                        var vals = Regex.Matches(c.Groups[2].Value, @"-?[\d.]+");
                        switch (char.ToUpperInvariant(c.Groups[1].Value[0]))
                        {
                            case 'M':
                                if (vals.Count >= 2)
                                {
                                    cx = double.Parse(vals[0].Value, invc);
                                    cy = double.Parse(vals[1].Value, invc);
                                }
                                break;
                            case 'V': if (vals.Count >= 1) cy = double.Parse(vals[0].Value, invc); break;
                            case 'H': if (vals.Count >= 1) cx = double.Parse(vals[0].Value, invc); break;
                        }
                        pts.Add((cx, cy));
                    }
                    if (pts.Count < 2) break;
                    double minX = double.MaxValue, maxX = double.MinValue,
                        minY = double.MaxValue, maxY = double.MinValue;
                    foreach (var (px, py) in pts)
                    {
                        minX = Math.Min(minX, px); maxX = Math.Max(maxX, px);
                        minY = Math.Min(minY, py); maxY = Math.Max(maxY, py);
                    }
                    if (maxX - minX > 0 && maxY - minY > 0)
                    {
                        // area: fill the polygon's box (the domain band)
                        var bx = MapX(minX); var byTd = MapYTd(minY);
                        sb.Append(string.Create(invc,
                            $"q {chartGray.R / 255.0:0.###} {chartGray.G / 255.0:0.###} {chartGray.B / 255.0:0.###} rg " +
                            $"{bx:0.##} {pageHeight - byTd - (maxY - minY) * 0.75:0.##} {(maxX - minX) * 0.75:0.##} {(maxY - minY) * 0.75:0.##} re f Q\n"));
                    }
                    // A degenerate (zero-area) path fills nothing, like the lines.
                    break;
                }
                case "text":
                {
                    var el = new SvgTextEl
                    {
                        X = AttrF(attrs, "x", 0),
                        Y = AttrF(attrs, "y", 0),
                        Text = DecodeEntities(t.Groups["body"].Value).Trim(),
                    };
                    if (el.Text.Length == 0) break;
                    var dxm = Regex.Match(attrs, @"dx\s*=\s*['""](-?[\d.]+)em", RegexOptions.IgnoreCase);
                    if (dxm.Success) el.DxEm = double.Parse(dxm.Groups[1].Value, invc);
                    else el.DxEm = AttrF(attrs, "dx", 0) / 12.0;
                    var dym = Regex.Match(attrs, @"dy\s*=\s*['""](-?[\d.]+)em", RegexOptions.IgnoreCase);
                    if (dym.Success) el.DyEm = double.Parse(dym.Groups[1].Value, invc);
                    else el.DyEm = AttrF(attrs, "dy", 0) / 12.0;
                    el.AnchorEnd = Regex.IsMatch(attrs, @"text-anchor\s*:\s*end|text-anchor\s*=\s*['""]end",
                        RegexOptions.IgnoreCase);
                    el.Rotate45 = Regex.IsMatch(attrs, @"rotate\(\s*-45", RegexOptions.IgnoreCase);
                    var fsPt = el.FontPx * 0.75;
                    var w = MeasureFaceText("Times New Roman", el.Text, fsPt);
                    var em = el.FontPx;
                    // local svg-px offset off the (x,y) anchor
                    var lx = el.X + el.DxEm * em;
                    var ly = el.Y + el.DyEm * em;
                    if (!el.Rotate45)
                    {
                        var bx = MapX(lx) - (el.AnchorEnd ? w : 0);
                        var byTd = MapYTd(ly);
                        sb.Append(string.Create(invc,
                            $"q {chartGray.R / 255.0:0.###} {chartGray.G / 255.0:0.###} {chartGray.B / 255.0:0.###} rg BT /F5 {fsPt:0.##} Tf " +
                            $"1 0 0 1 {bx:0.##} {pageHeight - byTd:0.##} Tm ({EscapePdfString(el.Text)}) Tj ET Q\n"));
                    }
                    else
                    {
                        // rotate(-45) about the anchor: the local offset turns with
                        // the glyph run; anchor-end walks back along the rotated
                        // baseline. (top-down R(-45) = [c c; -c c], c = √2/2)
                        const double c = 0.70710678;
                        var ox = (lx * c + ly * c) * 0.75;
                        var oyTd = (-lx * c + ly * c) * 0.75;
                        var px = MapX(0) + ox - (el.AnchorEnd ? w * c : 0);
                        var pyTd = MapYTd(0) + oyTd + (el.AnchorEnd ? w * c : 0);
                        sb.Append(string.Create(invc,
                            $"q {chartGray.R / 255.0:0.###} {chartGray.G / 255.0:0.###} {chartGray.B / 255.0:0.###} rg BT /F5 {fsPt:0.##} Tf " +
                            $"{c:0.#####} {c:0.#####} {-c:0.#####} {c:0.#####} {px:0.##} {pageHeight - pyTd:0.##} Tm ({EscapePdfString(el.Text)}) Tj ET Q\n"));
                    }
                    break;
                }
            }
        }
        sb.Append("Q\n");
    }
}
