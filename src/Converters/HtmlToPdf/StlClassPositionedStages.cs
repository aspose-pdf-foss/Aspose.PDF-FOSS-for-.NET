using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>A number in the content stream: three decimals, invariant.</summary>
    private static string StlNum(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A face draws through Standard-14 where one matches (serif output that
    /// embeds nothing — the UA flow's rule); any other resolvable installed face rides a
    /// named Type1 dict the rasterizer resolves. An unresolvable family substitutes the UA
    /// serif (probed: Modern No. 20 → Times).</summary>
    private static (string Res, string Measure) StlFaceFor(StlClassPositionedState sp, string family, bool bold)
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
        if (!sp.extraFaces.TryGetValue(key, out var res))
        {
            res = "FS" + (sp.extraFaces.Count + 1).ToString(sp.inv);
            sp.extraFaces[key] = res;
            EnsureFont(sp.page, family.Replace(' ', '-') + (bold ? "-Bold" : ""), res);
        }
        return (res, family);
    }

    /// <summary>Baseline drop of one line box under the CSS win-metric model (the
    /// MetricLineHeight/MetricBaselineDrop pair, probed exact on the ladder).</summary>
    private static double StlDropOf(string family, double fs)
    {
        var m = WinMetricsFor(family) ?? WinMetricsFor(StlClsBodyFace);
        if (m is null) return 0.9 * fs;
        var sum = HheaLineSumFor(family) ?? m.Value.sum;
        var lh = MetricLineHeight(fs, sum);
        return MetricBaselineDrop(fs, lh, m.Value);
    }

    /// <summary>Render every positioned div: the svg background object's strokes, else its styled inline runs at the class position.</summary>
    private static void RenderStlPositionedDivs(StlClassPositionedState sp)
    {
        foreach (var (left, top, inner) in sp.divs)
        {
            // the svg background object: its stroked paths are the page's rules
            var objM = Regex.Match(inner,
                @"<(object|embed)\b[^>]*(?:data|src)\s*=\s*""(?<h>[^""]+\.svg)""",
                RegexOptions.IgnoreCase);
            if (objM.Success)
            {
                AppendStlSvgStrokes(sp.svgPaths, objM.Groups["h"].Value, sp.options,
                    left + StlClsOriginXPt, top + StlClsOriginYPt, sp.pageH);
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
                var st = sp.styles.TryGetValue(sm.Groups["cls"].Value, out var s0) ? s0 : new StlClsStyle();
                lineRuns.Add((st, DecodeEntities(
                    Regex.Replace(sm.Groups["t"].Value, "<[^>]+>", ""))));
                idx = sm.Index + sm.Length;
            }
            var tail = inner[idx..];
            if (Regex.Replace(tail, "<[^>]+>", "").Trim().Length > 0)
                lineRuns.Add((new StlClsStyle(),
                    DecodeEntities(Regex.Replace(tail, "<[^>]+>", ""))));
            if (lineRuns.Count == 0) continue;

            var drop = sp.strutDrop;
            foreach (var (st, _) in lineRuns)
                drop = Math.Max(drop, StlDropOf(st.Family, st.FontSize));

            var x = left + StlClsOriginXPt;
            var yPdf = sp.pageH - (top + StlClsOriginYPt + drop);
            foreach (var (st, text) in lineRuns)
            {
                if (text.Length == 0) continue;
                var (res, measure) = StlFaceFor(sp, st.Family, st.Bold);
                sp.runs.AppendLine($"BT {StlNum(st.R)} {StlNum(st.G)} {StlNum(st.B)} rg");
                sp.runs.Append($"/{res} {st.FontSize.ToString("F2", sp.inv)} Tf ");
                sp.runs.Append($"1 0 0 1 {StlNum(x)} {StlNum(yPdf)} Tm ");
                sp.runs.AppendLine($"({EscapePdfString(text)}) Tj ET");
                x += MeasureFaceText(measure, text, st.FontSize);
            }
        }
    }

    /// <summary>Read the stylesheet's class rules into the position and style tables.</summary>
    private static void ParseStlClassStyles(StlClassPositionedState sp)
    {
        foreach (Match rm in Regex.Matches(sp.css, @"\.(?<name>[\w-]+)\s*\{(?<body>[^}]*)\}",
            RegexOptions.Singleline))
        {
            var body = rm.Groups["body"].Value;
            var name = rm.Groups["name"].Value;
            if (Regex.IsMatch(body, @"position\s*:\s*absolute", RegexOptions.IgnoreCase))
            {
                var lm = Regex.Match(body, @"left\s*:\s*(-?[\d.]+)pt", RegexOptions.IgnoreCase);
                var tm = Regex.Match(body, @"top\s*:\s*(-?[\d.]+)pt", RegexOptions.IgnoreCase);
                if (lm.Success && tm.Success)
                    sp.pos[name] = (
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
            sp.styles[name] = st;
        }
    }
}
