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
        sr.vb = SafeRegex.Match(sr.svg, @"viewBox=""(?<a>-?[\d.]+)\s+(?<b>-?[\d.]+)\s+(?<c>[\d.]+)\s+(?<d>[\d.]+)""");
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
        sr.defSpans = new List<(int Start, int End)>();
        // A clip path's shape is the outline of a clip, not ink: replayed as a path it would
        // fill the clipped area, a page-sized one painting the whole page black.
        foreach (Match cm in SafeRegex.Matches(sr.svg, @"<clipPath\b[^>]*>[\s\S]*?</clipPath\s*>"))
            sr.defSpans.Add((cm.Index, cm.Index + cm.Length));
        foreach (Match mm in SafeRegex.Matches(sr.svg, @"<mask id=""(?<id>[^""]+)""(?<hattrs>[^>]*)>(?<body>[\s\S]*?)</mask\s*>"))
        {
            sr.defSpans.Add((mm.Index, mm.Index + mm.Length));
            var ha = mm.Groups["hattrs"].Value;
            var mx = SafeRegex.Match(ha, @"(?<![\w-])x=""(?<v>-?[\d.]+)""");
            var my = SafeRegex.Match(ha, @"(?<![\w-])y=""(?<v>-?[\d.]+)""");
            var mw = SafeRegex.Match(ha, @"width=""(?<v>[\d.]+)""");
            var mh = SafeRegex.Match(ha, @"height=""(?<v>[\d.]+)""");
            var mi = SafeRegex.Match(mm.Groups["body"].Value, @"xlink:href=""(?<v>data:[^""]+)""");
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

#pragma warning disable S3267 // a step loop: each call does the work and returns false to stop - as a LINQ predicate the side effects would hide in a query
        foreach (Match tag in SafeRegex.Matches(sr.svg, @"<(?<close>/)?(?<tag>g|path|image)\b(?<attrs>[^>]*)>"))
#pragma warning restore S3267
        {
            if (!ReplaySvgTag(sr, tag)) break;
        }
        FlushPaths(sr);
    }
}
