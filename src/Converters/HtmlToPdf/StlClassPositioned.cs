using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// The class-positioned stl_ dialect: an older PDF→HTML export where EVERY div's
// geometry lives in the stylesheet as `.stl_NN { left: Xpt; top: Ypt;
// position: absolute }` (no page_N container, no inline styles), text style in
// sibling `display: inline` classes (font-size/font-family in pt), and the page's
// vector ink (rules, underlines) in an svg <object> background. The source
// renderer re-imports each div as ONE line at its class position on the default
// sheet: x = left + the 90 pt content origin, baseline = top + the 72 pt top
// margin + the CSS line-box baseline drop (win-metric model, strut = the UA
// 12 pt serif body — every seat probed on a face×size ladder).
internal static partial class HtmlToPdfConverter
{
    /// <summary>Content-box origin of the import sheet (probed: every div lands at
    /// class left + 90, class top + 72).</summary>
    private const double StlClsOriginXPt = 90.0;
    private const double StlClsOriginYPt = 72.0;

    /// <summary>The dialect's strut: the UA default body (12 pt serif) sets the
    /// minimum baseline drop of every line box.</summary>
    private const double StlClsBodyFsPt = 12.0;
    private const string StlClsBodyFace = "Times New Roman";

    /// <summary>SVG background path units: css px at 0.75 pt each.</summary>
    private const double StlClsSvgPxPt = 0.75;

    private sealed class StlClsStyle
    {
        public double FontSize = StlClsBodyFsPt;
        public string Family = StlClsBodyFace;
        public bool Bold;
        public double R, G, B;
    }

    /// <summary>Render the class-positioned stl_ dialect, or null when the page
    /// is not it (needs the resolvable stylesheet — its geometry lives there).</summary>
    private static Document? TryRenderStlClassPositioned(string html, HtmlLoadOptions? options)
    {
        // The geometry is ENTIRELY in the stylesheet; an auto-derived base path
        // does not resolve it (mirrors the page_N stl_ dialect's rule).
        var css = GatherStlCss(html, options?.BasePathAutoDerived == true ? null : options);
        if (string.IsNullOrWhiteSpace(css)) return null;

        // .name { left: Xpt; top: Ypt; position: absolute }  — pt units only (the
        // page_N flavour positions in em via inline styles and never matches here).
        var pos = new Dictionary<string, (double Left, double Top)>(StringComparer.Ordinal);
        var styles = new Dictionary<string, StlClsStyle>(StringComparer.Ordinal);
        foreach (Match rm in Regex.Matches(css, @"\.(?<name>[\w-]+)\s*\{(?<body>[^}]*)\}",
            RegexOptions.Singleline))
        {
            var body = rm.Groups["body"].Value;
            var name = rm.Groups["name"].Value;
            if (Regex.IsMatch(body, @"position\s*:\s*absolute", RegexOptions.IgnoreCase))
            {
                var lm = Regex.Match(body, @"left\s*:\s*(-?[\d.]+)pt", RegexOptions.IgnoreCase);
                var tm = Regex.Match(body, @"top\s*:\s*(-?[\d.]+)pt", RegexOptions.IgnoreCase);
                if (lm.Success && tm.Success)
                    pos[name] = (
                        double.Parse(lm.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                        double.Parse(tm.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
                continue;
            }
            if (!Regex.IsMatch(body, @"display\s*:\s*inline", RegexOptions.IgnoreCase)
                && !Regex.IsMatch(body, @"font-weight\s*:\s*bold", RegexOptions.IgnoreCase))
                continue;
            var st = new StlClsStyle();
            var fs = Regex.Match(body, @"font-size\s*:\s*([\d.]+)pt", RegexOptions.IgnoreCase);
            if (fs.Success)
                st.FontSize = double.Parse(fs.Groups[1].Value,
                    System.Globalization.CultureInfo.InvariantCulture);
            var ff = Regex.Match(body, @"font-family\s*:\s*(?<v>[^;}]+)", RegexOptions.IgnoreCase);
            if (ff.Success)
                st.Family = ff.Groups["v"].Value.Split(',')[0].Trim().Trim('\'', '"');
            st.Bold = Regex.IsMatch(body, @"font-weight\s*:\s*bold", RegexOptions.IgnoreCase);
            var col = Regex.Match(body, @"color\s*:\s*#(?<h>[0-9a-fA-F]{6})");
            if (col.Success)
            {
                var h = col.Groups["h"].Value;
                st.R = System.Convert.ToInt32(h[..2], 16) / 255.0;
                st.G = System.Convert.ToInt32(h[2..4], 16) / 255.0;
                st.B = System.Convert.ToInt32(h[4..], 16) / 255.0;
            }
            styles[name] = st;
        }
        if (pos.Count < 3 || styles.Count == 0) return null;

        // The markup: class-only divs whose classes the stylesheet positions, with
        // pure inline content (spans / text / the svg object). Any inline style=
        // positioning or table structure means a different dialect.
        var divs = new List<(double Left, double Top, string Inner)>();
        var divMatches = Regex.Matches(html,
            @"<div\s+class=""(?<cls>[\w-]+)""\s*>(?<inner>(?:(?!</?div\b).)*?)</div>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        foreach (Match dm in divMatches)
        {
            if (!pos.TryGetValue(dm.Groups["cls"].Value, out var p)) continue;
            divs.Add((p.Left, p.Top, dm.Groups["inner"].Value));
        }
        if (divs.Count < 3) return null;
        // Positioned text divs must dominate the body — a page merely CONTAINING
        // a few absolute classes keeps its own flow.
        var totalDivs = Regex.Matches(html, @"<div\b", RegexOptions.IgnoreCase).Count;
        if (divs.Count * 2 < totalDivs) return null;
        if (Regex.IsMatch(html, @"<(table|p|h[1-6]|ul|ol|input|form)\b", RegexOptions.IgnoreCase))
            return null;

        var pageW = options?.PageInfo?.Width > 0 ? options.PageInfo.Width : 595.0;
        var pageH = options?.PageInfo?.Height > 0 ? options.PageInfo.Height : 842.0;

        var doc = new Document();
        var page = doc.Pages.Add(pageW, pageH);
        EnsureFonts(page);

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string N(double v) => v.ToString("0.###", inv);

        // A face draws through Standard-14 where one matches (serif output that
        // embeds nothing — the UA flow's rule); any other resolvable installed
        // face rides a named Type1 dict the rasterizer resolves. An unresolvable
        // family substitutes the UA serif (probed: Modern No. 20 → Times).
        var extraFaces = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        (string Res, string Measure) FaceFor(string family, bool bold)
        {
            if (WinMetricsFor(family) is null) family = StlClsBodyFace;
            if (family.Equals("Times New Roman", StringComparison.OrdinalIgnoreCase)
                || family.Equals("Times", StringComparison.OrdinalIgnoreCase))
                return (bold ? "F6" : "F5", "Times New Roman");
            if (family.Equals("Arial", StringComparison.OrdinalIgnoreCase)
                || family.Equals("Helvetica", StringComparison.OrdinalIgnoreCase))
                return (bold ? "F2" : "F1", "Arial");
            if (family.Equals("Courier New", StringComparison.OrdinalIgnoreCase)
                || family.Equals("Courier", StringComparison.OrdinalIgnoreCase))
                return ("F4", "Courier New");
            var key = family + (bold ? "|b" : "");
            if (!extraFaces.TryGetValue(key, out var res))
            {
                res = "FS" + (extraFaces.Count + 1).ToString(inv);
                extraFaces[key] = res;
                EnsureFont(page, family.Replace(' ', '-') + (bold ? "-Bold" : ""), res);
            }
            return (res, family);
        }

        // Baseline drop of one line box under the CSS win-metric model (the
        // MetricLineHeight/MetricBaselineDrop pair, probed exact on the ladder).
        double DropOf(string family, double fs)
        {
            var m = WinMetricsFor(family) ?? WinMetricsFor(StlClsBodyFace);
            if (m is null) return 0.9 * fs;
            var sum = HheaLineSumFor(family) ?? m.Value.sum;
            var lh = MetricLineHeight(fs, sum);
            return MetricBaselineDrop(fs, lh, m.Value);
        }
        var strutDrop = DropOf(StlClsBodyFace, StlClsBodyFsPt);

        var runs = new StringBuilder();
        var svgPaths = new StringBuilder();
        foreach (var (left, top, inner) in divs)
        {
            // the svg background object: its stroked paths are the page's rules
            var objM = Regex.Match(inner,
                @"<(object|embed)\b[^>]*(?:data|src)\s*=\s*""(?<h>[^""]+\.svg)""",
                RegexOptions.IgnoreCase);
            if (objM.Success)
            {
                AppendStlSvgStrokes(svgPaths, objM.Groups["h"].Value, options,
                    left + StlClsOriginXPt, top + StlClsOriginYPt, pageH);
                continue;
            }

            // split the inline content into styled runs
            var lineRuns = new List<(StlClsStyle St, string Text)>();
            var idx = 0;
            foreach (Match sm in Regex.Matches(inner,
                @"<span\s+class=""(?<cls>[\w-]+)""\s*>(?<t>.*?)</span>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                var before = inner[idx..sm.Index];
                if (Regex.Replace(before, "<[^>]+>", "").Trim().Length > 0)
                    lineRuns.Add((new StlClsStyle(),
                        DecodeEntities(Regex.Replace(before, "<[^>]+>", ""))));
                var st = styles.TryGetValue(sm.Groups["cls"].Value, out var s0) ? s0 : new StlClsStyle();
                lineRuns.Add((st, DecodeEntities(
                    Regex.Replace(sm.Groups["t"].Value, "<[^>]+>", ""))));
                idx = sm.Index + sm.Length;
            }
            var tail = inner[idx..];
            if (Regex.Replace(tail, "<[^>]+>", "").Trim().Length > 0)
                lineRuns.Add((new StlClsStyle(),
                    DecodeEntities(Regex.Replace(tail, "<[^>]+>", ""))));
            if (lineRuns.Count == 0) continue;

            var drop = strutDrop;
            foreach (var (st, _) in lineRuns)
                drop = Math.Max(drop, DropOf(st.Family, st.FontSize));

            var x = left + StlClsOriginXPt;
            var yPdf = pageH - (top + StlClsOriginYPt + drop);
            foreach (var (st, text) in lineRuns)
            {
                if (text.Length == 0) continue;
                var (res, measure) = FaceFor(st.Family, st.Bold);
                runs.AppendLine($"BT {N(st.R)} {N(st.G)} {N(st.B)} rg");
                runs.Append($"/{res} {st.FontSize.ToString("F2", inv)} Tf ");
                runs.Append($"1 0 0 1 {N(x)} {N(yPdf)} Tm ");
                runs.AppendLine($"({EscapePdfString(text)}) Tj ET");
                x += MeasureFaceText(measure, text, st.FontSize);
            }
        }

        if (runs.Length == 0) return null;
        if (svgPaths.Length > 0)
            page.AddContentStream(Encoding.ASCII.GetBytes(svgPaths.ToString()));
        page.AddContentStream(Encoding.ASCII.GetBytes(runs.ToString()));
        return doc;
    }

    /// <summary>Stroke the svg background's line paths onto the sheet. The export's
    /// svg draws in css px under a matrix chain (the 4/3 root scale × the y-flip);
    /// px × 0.75 lands them 1:1 in pt at the object's page position.</summary>
    private static void AppendStlSvgStrokes(StringBuilder sb, string href,
        HtmlLoadOptions? options, double originX, double originY, double pageH)
    {
        byte[]? bytes = null;
        try { bytes = LoadConverterImage(href, options); }
        catch { /* unreadable background: the text still draws */ }
        if (bytes is null || bytes.Length == 0) return;
        var svg = Encoding.UTF8.GetString(bytes);
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string N(double v) => v.ToString("0.###", inv);

        // Compose the root scale with each path's own matrix. Only the matrix()
        // form appears in this writer's output.
        var rootScale = 1.0;
        var rootM = Regex.Match(svg, @"<g transform=""matrix\((?<a>[\d.\-]+) [\d.\-]+ [\d.\-]+ [\d.\-]+ [\d.\-]+ [\d.\-]+\)""");
        if (rootM.Success)
            rootScale = double.Parse(rootM.Groups["a"].Value, inv);

        foreach (Match pm in Regex.Matches(svg, @"<path\b(?<attrs>[^>]*)/>", RegexOptions.IgnoreCase))
        {
            var attrs = pm.Groups["attrs"].Value;
            var dM = Regex.Match(attrs, @"\bd=""(?<d>[^""]+)""");
            var strokeM = Regex.Match(attrs, @"stroke=""#(?<h>[0-9a-fA-F]{6})""");
            if (!dM.Success || !strokeM.Success) continue;
            var wM = Regex.Match(attrs, @"stroke-width=""(?<w>[\d.]+)""");
            var flipM = Regex.Match(attrs, @"transform=""matrix\(1 0 0 -1 0 (?<h>[\d.]+)\)""");
            var flipH = flipM.Success ? double.Parse(flipM.Groups["h"].Value, inv) : 0;

            var h = strokeM.Groups["h"].Value;
            var r = System.Convert.ToInt32(h[..2], 16) / 255.0;
            var g = System.Convert.ToInt32(h[2..4], 16) / 255.0;
            var b = System.Convert.ToInt32(h[4..], 16) / 255.0;
            var w = (wM.Success ? double.Parse(wM.Groups["w"].Value, inv) : 1.0)
                    * rootScale * StlClsSvgPxPt;
            sb.AppendLine($"{N(r)} {N(g)} {N(b)} RG {N(w)} w");

            var started = false;
            foreach (Match seg in Regex.Matches(dM.Groups["d"].Value,
                @"(?<op>[ML])\s*(?<x>-?[\d.]+)[ ,](?<y>-?[\d.]+)"))
            {
                var px = double.Parse(seg.Groups["x"].Value, inv);
                var py = double.Parse(seg.Groups["y"].Value, inv);
                if (flipH > 0) py = flipH - py;
                var xPt = originX + px * rootScale * StlClsSvgPxPt;
                var yPt = pageH - (originY + py * rootScale * StlClsSvgPxPt);
                sb.AppendLine($"{N(xPt)} {N(yPt)} {(seg.Groups["op"].Value == "M" || !started ? "m" : "l")}");
                started = true;
            }
            if (started) sb.AppendLine("S");
        }
    }
}
