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
        var sp = new StlClassPositionedState();
        sp.html = html;
        sp.options = options;
        sp.css = GatherStlCss(sp.html, sp.options?.BasePathAutoDerived == true ? null : sp.options);
        if (string.IsNullOrWhiteSpace(sp.css)) return null;

        sp.pos = new Dictionary<string, (double Left, double Top)>(StringComparer.Ordinal);
        sp.styles = new Dictionary<string, StlClsStyle>(StringComparer.Ordinal);
        ParseStlClassStyles(sp);
        if (sp.pos.Count < 3 || sp.styles.Count == 0) return null;

        sp.divs = new List<(double Left, double Top, string Inner)>();
        sp.divMatches = Regex.Matches(sp.html,
            @"<div\s+class=""(?<cls>[\w-]+)""\s*>(?<inner>(?:(?!</?div\b).)*?)</div>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        foreach (Match dm in sp.divMatches)
        {
            if (!sp.pos.TryGetValue(dm.Groups["cls"].Value, out var p)) continue;
            sp.divs.Add((p.Left, p.Top, dm.Groups["inner"].Value));
        }
        if (sp.divs.Count < 3) return null;
        sp.totalDivs = Regex.Matches(sp.html, @"<div\b", RegexOptions.IgnoreCase).Count;
        if (sp.divs.Count * 2 < sp.totalDivs) return null;
        if (Regex.IsMatch(sp.html, @"<(table|p|h[1-6]|ul|ol|input|form)\b", RegexOptions.IgnoreCase))
            return null;

        sp.pageW = sp.options?.PageInfo?.Width > 0 ? sp.options.PageInfo.Width : 595.0;
        sp.pageH = sp.options?.PageInfo?.Height > 0 ? sp.options.PageInfo.Height : 842.0;

        sp.doc = new Document();
        sp.page = sp.doc.Pages.Add(sp.pageW, sp.pageH);
        EnsureFonts(sp.page);

        sp.inv = System.Globalization.CultureInfo.InvariantCulture;

        sp.extraFaces = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        sp.strutDrop = StlDropOf(StlClsBodyFace, StlClsBodyFsPt);

        sp.runs = new StringBuilder();
        sp.svgPaths = new StringBuilder();
        RenderStlPositionedDivs(sp);

        if (sp.runs.Length == 0) return null;
        if (sp.svgPaths.Length > 0)
            sp.page.AddContentStream(Encoding.ASCII.GetBytes(sp.svgPaths.ToString()));
        sp.page.AddContentStream(Encoding.ASCII.GetBytes(sp.runs.ToString()));
        return sp.doc;
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
