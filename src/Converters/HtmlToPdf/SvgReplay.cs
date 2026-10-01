using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    private static void ReplaySvgObject(Page page, string svg, double[] placement,
        string svgDir, HtmlLoadOptions? options)
    {
        var sr = new SvgReplayState();
        sr.page = page;
        sr.svg = svg;
        sr.placement = placement;
        sr.svgDir = svgDir;
        sr.options = options;
        sr.inv = System.Globalization.CultureInfo.InvariantCulture;
        sr.vb = Regex.Match(sr.svg, @"viewBox=""(?<a>-?[\d.]+)\s+(?<b>-?[\d.]+)\s+(?<c>[\d.]+)\s+(?<d>[\d.]+)""");
        sr.total = sr.placement;
        if (sr.vb.Success)
        {
            var vx = Num(sr, sr.vb.Groups["a"].Value);
            var vy = Num(sr, sr.vb.Groups["b"].Value);
            var vw = Num(sr, sr.vb.Groups["c"].Value);
            var vh = Num(sr, sr.vb.Groups["d"].Value);
            _ = vw; _ = vh; // placement is already scaled to the viewBox size by the caller
            sr.total = MulM(new[] { 1.0, 0, 0, 1.0, -vx, -vy }, sr.placement);
        }
        sr.rootTotal = sr.total;

        sr.maskDefs = new Dictionary<string, (double X, double Y, double W, double H, byte[] Png)>(StringComparer.Ordinal);
        sr.maskSpans = new List<(int Start, int End)>();
        foreach (Match mm in Regex.Matches(sr.svg, @"<mask id=""(?<id>[^""]+)""(?<hattrs>[^>]*)>(?<body>[\s\S]*?)</mask\s*>"))
        {
            sr.maskSpans.Add((mm.Index, mm.Index + mm.Length));
            var ha = mm.Groups["hattrs"].Value;
            var mx = Regex.Match(ha, @"(?<![\w-])x=""(?<v>-?[\d.]+)""");
            var my = Regex.Match(ha, @"(?<![\w-])y=""(?<v>-?[\d.]+)""");
            var mw = Regex.Match(ha, @"width=""(?<v>[\d.]+)""");
            var mh = Regex.Match(ha, @"height=""(?<v>[\d.]+)""");
            var mi = Regex.Match(mm.Groups["body"].Value, @"xlink:href=""(?<v>data:[^""]+)""");
            if (!(mx.Success && my.Success && mw.Success && mh.Success && mi.Success)) continue;
            var png = LoadConverterImage(DecodeEntities(mi.Groups["v"].Value), sr.options);
            if (png is null) continue;
            sr.maskDefs[mm.Groups["id"].Value] =
                (Num(sr, mx.Groups["v"].Value), Num(sr, my.Groups["v"].Value),
                 Num(sr, mw.Groups["v"].Value), Num(sr, mh.Groups["v"].Value), png);
        }
        sr.stack = new Stack<(double[] M, double Alpha, string? MaskId)>();
        sr.alpha = 1.0;
        sr.maskId = null;
        sr.gsNames = new Dictionary<string, string>(StringComparer.Ordinal);
        sr.sb = new StringBuilder();

        foreach (Match tag in Regex.Matches(sr.svg, @"<(?<close>/)?(?<tag>g|path|image)\b(?<attrs>[^>]*)>"))
        {
            if (!ReplaySvgTag(sr, tag)) break;
        }
        FlushPaths(sr);
    }
}
