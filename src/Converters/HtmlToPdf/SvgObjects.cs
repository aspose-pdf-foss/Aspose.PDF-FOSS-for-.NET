using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Row-vector 2×3 matrix product: apply <paramref name="a"/> first, then
    /// <paramref name="b"/> — the composition SVG nesting and PDF cm both use.</summary>
    private static double[] MulM(double[] a, double[] b) => new[]
    {
        a[0] * b[0] + a[1] * b[2],
        a[0] * b[1] + a[1] * b[3],
        a[2] * b[0] + a[3] * b[2],
        a[2] * b[1] + a[3] * b[3],
        a[4] * b[0] + a[5] * b[2] + b[4],
        a[4] * b[1] + a[5] * b[3] + b[5],
    };

    /// <summary>Replay this library's own page-SVG (the bounded subset its PDF→HTML
    /// converter emits: nested <c>g[transform=matrix]</c>, absolute <c>M/L/C/Z</c>
    /// paths with rgb fills/strokes, and <c>image</c> references) as vector content
    /// onto <paramref name="page"/>. <paramref name="placement"/> maps the SVG's
    /// viewBox onto the sheet. Raster images resolve against
    /// <paramref name="svgDir"/>. Anything outside the subset is skipped silently —
    /// a partial page graphic still beats none.</summary>
    /// <summary>The rightmost drawn x of a page SVG's geometry as a FRACTION of its
    /// viewBox width (0..1+), or null when the SVG is page furniture whose whole box
    /// counts — anything carrying FILLED shapes or images — or when it holds
    /// constructs this scan does not model (arcs, unknown transforms, no viewBox).
    /// Only a stroke-only decoration (a header rule ending mid-page) narrows the
    /// sheet to its ink.</summary>
    private static double? TrySvgInkRightFraction(string svg)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        double Num(string v) => double.Parse(v, System.Globalization.NumberStyles.Float, inv);
        if (Regex.IsMatch(svg, @"<(?:image|rect)\b")
            || Regex.IsMatch(svg, @"fill=""(?!none"")"))
            return null;
        var vb = Regex.Match(svg, @"viewBox=""(?<a>-?[\d.]+)\s+(?<b>-?[\d.]+)\s+(?<c>[\d.]+)\s+(?<d>[\d.]+)""");
        if (!vb.Success) return null;
        var vbX = Num(vb.Groups["a"].Value);
        var vbW = Num(vb.Groups["c"].Value);
        if (vbW <= 0) return null;
        var total = new[] { 1.0, 0, 0, 1.0, 0, 0 };
        var stack = new Stack<double[]>();
        double? right = null;
        foreach (Match tag in Regex.Matches(svg, @"<(?<close>/)?(?<tag>g|path|image|rect|line)\b(?<attrs>[^>]*)>"))
        {
            var attrs = tag.Groups["attrs"].Value;
            if (tag.Groups["close"].Success)
            {
                if (tag.Groups["tag"].Value == "g" && stack.Count > 0) total = stack.Pop();
                continue;
            }
            void Point(double x, double y)
            {
                var tx = x * total[0] + y * total[2] + total[4];
                right = Math.Max(right ?? double.MinValue, tx);
            }
            switch (tag.Groups["tag"].Value)
            {
                case "g":
                {
                    stack.Push(total);
                    var tm = Regex.Match(attrs, @"transform=""matrix\((?<m>[-\d.eE ]+)\)""");
                    if (tm.Success)
                    {
                        var parts = tm.Groups["m"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 6)
                        {
                            var g = new double[6];
                            for (var k = 0; k < 6; k++) g[k] = Num(parts[k]);
                            total = MulM(g, total);
                        }
                    }
                    else if (attrs.Contains("transform=", StringComparison.Ordinal))
                        return null;    // translate()/scale() forms not modelled here
                    if (attrs.TrimEnd().EndsWith("/", StringComparison.Ordinal)) total = stack.Pop();
                    break;
                }
                case "path":
                {
                    var d = Regex.Match(attrs, @"d=""(?<d>[^""]*)""").Groups["d"].Value;
                    if (Regex.IsMatch(d, @"[AaHhVv]")) return null;   // axis/arc shorthands: bail
                    var nums = Regex.Matches(d, @"-?\d*\.?\d+(?:[eE][-+]?\d+)?");
                    for (var k = 0; k + 1 < nums.Count; k += 2)
                        Point(Num(nums[k].Value), Num(nums[k + 1].Value));
                    break;
                }
                case "rect" or "image":
                {
                    var xm = Regex.Match(attrs, @"\bx=""(?<v>-?[\d.]+)""");
                    var wm = Regex.Match(attrs, @"\bwidth=""(?<v>[\d.]+)""");
                    var ym = Regex.Match(attrs, @"\by=""(?<v>-?[\d.]+)""");
                    var x1 = (xm.Success ? Num(xm.Groups["v"].Value) : 0)
                        + (wm.Success ? Num(wm.Groups["v"].Value) : 0);
                    Point(x1, ym.Success ? Num(ym.Groups["v"].Value) : 0);
                    break;
                }
                case "line":
                {
                    foreach (var a in new[] { "x1", "x2" })
                    {
                        var m2 = Regex.Match(attrs, @"\b" + a + @"=""(?<v>-?[\d.]+)""");
                        var ym2 = Regex.Match(attrs, @"\by" + a[1] + @"=""(?<v>-?[\d.]+)""");
                        if (m2.Success)
                            Point(Num(m2.Groups["v"].Value), ym2.Success ? Num(ym2.Groups["v"].Value) : 0);
                    }
                    break;
                }
            }
        }
        return right is { } r ? (r - vbX) / vbW : null;
    }

    private static void ReplaySvgObject(Page page, string svg, double[] placement,
        string svgDir, HtmlLoadOptions? options)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        double Num(string v) => double.Parse(v, System.Globalization.NumberStyles.Float, inv);

        // viewBox → prepend its origin/scale into the placement.
        var vb = Regex.Match(svg, @"viewBox=""(?<a>-?[\d.]+)\s+(?<b>-?[\d.]+)\s+(?<c>[\d.]+)\s+(?<d>[\d.]+)""");
        var total = placement;
        if (vb.Success)
        {
            var vx = Num(vb.Groups["a"].Value);
            var vy = Num(vb.Groups["b"].Value);
            var vw = Num(vb.Groups["c"].Value);
            var vh = Num(vb.Groups["d"].Value);
            _ = vw; _ = vh; // placement is already scaled to the viewBox size by the caller
            total = MulM(new[] { 1.0, 0, 0, 1.0, -vx, -vy }, placement);
        }
        // Root svg→page matrix: <mask> defs are declared in userSpaceOnUse (root svg)
        // coordinates, independent of the group transforms active at their USE site.
        var rootTotal = total;

        // Luminosity masks (<mask id> holding one raster): id → the def's user-space
        // rect and its grayscale png. Their spans are excluded from the content scan.
        var maskDefs = new Dictionary<string, (double X, double Y, double W, double H, byte[] Png)>(StringComparer.Ordinal);
        var maskSpans = new List<(int Start, int End)>();
        foreach (Match mm in Regex.Matches(svg, @"<mask id=""(?<id>[^""]+)""(?<hattrs>[^>]*)>(?<body>[\s\S]*?)</mask\s*>"))
        {
            maskSpans.Add((mm.Index, mm.Index + mm.Length));
            var ha = mm.Groups["hattrs"].Value;
            var mx = Regex.Match(ha, @"(?<![\w-])x=""(?<v>-?[\d.]+)""");
            var my = Regex.Match(ha, @"(?<![\w-])y=""(?<v>-?[\d.]+)""");
            var mw = Regex.Match(ha, @"width=""(?<v>[\d.]+)""");
            var mh = Regex.Match(ha, @"height=""(?<v>[\d.]+)""");
            var mi = Regex.Match(mm.Groups["body"].Value, @"xlink:href=""(?<v>data:[^""]+)""");
            if (!(mx.Success && my.Success && mw.Success && mh.Success && mi.Success)) continue;
            var png = LoadConverterImage(DecodeEntities(mi.Groups["v"].Value), options);
            if (png is null) continue;
            maskDefs[mm.Groups["id"].Value] =
                (Num(mx.Groups["v"].Value), Num(my.Groups["v"].Value),
                 Num(mw.Groups["v"].Value), Num(mh.Groups["v"].Value), png);
        }
        bool InsideMaskDef(int idx)
        {
            foreach (var (s0, e0) in maskSpans)
                if (idx >= s0 && idx < e0) return true;
            return false;
        }

        var stack = new Stack<(double[] M, double Alpha, string? MaskId)>();
        double alpha = 1.0;
        string? maskId = null;
        var gsNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var sb = new StringBuilder();

        void FlushPaths()
        {
            if (sb.Length == 0) return;
            page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
            sb.Clear();
        }

        // ExtGState for the current alpha/mask, registered once per distinct pair.
        string? CurrentGs()
        {
            if (alpha >= 1.0 - 1e-9 && maskId is null) return null;
            var key = $"{alpha:F6}|{maskId}";
            if (gsNames.TryGetValue(key, out var cached)) return cached;
            var name = RegisterSvgEffectGState(page, alpha,
                maskId is not null && maskDefs.TryGetValue(maskId, out var def) ? def : null, rootTotal);
            if (name is null) return null;
            gsNames[key] = name;
            return name;
        }

        foreach (Match tag in Regex.Matches(svg, @"<(?<close>/)?(?<tag>g|path|image)\b(?<attrs>[^>]*)>"))
        {
            if (InsideMaskDef(tag.Index)) continue;
            var attrs = tag.Groups["attrs"].Value;
            if (tag.Groups["close"].Success)
            {
                if (tag.Groups["tag"].Value == "g" && stack.Count > 0) (total, alpha, maskId) = stack.Pop();
                continue;
            }
            switch (tag.Groups["tag"].Value)
            {
                case "g":
                {
                    stack.Push((total, alpha, maskId));
                    var tm = Regex.Match(attrs,
                        @"transform=""matrix\((?<m>[-\d.eE ]+)\)""");
                    if (tm.Success)
                    {
                        var parts = tm.Groups["m"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 6)
                        {
                            var g = new double[6];
                            for (var k = 0; k < 6; k++) g[k] = Num(parts[k]);
                            total = MulM(g, total);
                        }
                    }
                    var om = Regex.Match(attrs, @"opacity=""(?<v>[\d.]+)""");
                    if (om.Success) alpha *= Num(om.Groups["v"].Value);
                    var km = Regex.Match(attrs, @"mask=""url\(#(?<v>[^)]+)\)""");
                    if (km.Success) maskId = km.Groups["v"].Value;
                    if (attrs.TrimEnd().EndsWith("/", StringComparison.Ordinal)) (total, alpha, maskId) = stack.Pop();
                    break;
                }
                case "path":
                {
                    // The boundary keeps this from matching the tail of `id="…"`
                    // (the inline dialect's paths carry an id attribute).
                    var d = Regex.Match(attrs, @"(?<![\w-])d=""(?<d>[^""]*)""");
                    if (!d.Success) break;
                    var fillM = Regex.Match(attrs, @"fill=""(?<v>[^""]*)""");
                    var strokeM = Regex.Match(attrs, @"stroke=""(?<v>[^""]*)""");
                    var widthM = Regex.Match(attrs, @"stroke-width=""(?<v>[-\d.]+)""");
                    var fill = ParseSvgRgb(fillM.Success ? fillM.Groups["v"].Value : "rgb(0,0,0)");
                    var stroke = ParseSvgRgb(strokeM.Success ? strokeM.Groups["v"].Value : "none");
                    if (fill is null && stroke is null) break;
                    var ops = SvgPathToPdfOps(d.Groups["d"].Value);
                    if (ops is null) break;

                    // The inline dialect leaves the y flip to each path's own
                    // transform matrix rather than the wrapper's.
                    var pathTotal = total;
                    var ptm = Regex.Match(attrs, @"transform=""matrix\((?<m>[-\d.eE ]+)\)""");
                    if (ptm.Success)
                    {
                        var pparts = ptm.Groups["m"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (pparts.Length == 6)
                        {
                            var pm = new double[6];
                            for (var k = 0; k < 6; k++) pm[k] = Num(pparts[k]);
                            pathTotal = MulM(pm, total);
                        }
                    }

                    sb.Append("q ");
                    if (CurrentGs() is { } pathGs) sb.Append($"/{pathGs} gs ");
                    sb.Append(string.Join(' ', pathTotal.Select(v => v.ToString("F5", inv))));
                    sb.Append(" cm ");
                    if (fill is not null)
                        sb.Append($"{fill.Value.r.ToString("F3", inv)} {fill.Value.g.ToString("F3", inv)} {fill.Value.b.ToString("F3", inv)} rg ");
                    if (stroke is not null)
                    {
                        sb.Append($"{stroke.Value.r.ToString("F3", inv)} {stroke.Value.g.ToString("F3", inv)} {stroke.Value.b.ToString("F3", inv)} RG ");
                        var wRaw = widthM.Success ? Num(widthM.Groups["v"].Value) : 1.0;
                        sb.Append($"{Math.Abs(wRaw).ToString("F3", inv)} w ");
                    }
                    sb.Append(ops);
                    sb.AppendLine(fill is not null && stroke is not null ? "B" : fill is not null ? "f" : "S");
                    sb.AppendLine("Q");
                    break;
                }
                case "image":
                {
                    var href = Regex.Match(attrs, @"(?:xlink:)?href=""(?<v>[^""]+)""");
                    if (!href.Success) break;
                    double ix = 0, iy = 0, iw = 0, ih = 0;
                    var xm = Regex.Match(attrs, @"(?<![\w-])x=""(?<v>-?[\d.]+)""");
                    var ym = Regex.Match(attrs, @"(?<![\w-])y=""(?<v>-?[\d.]+)""");
                    var wm = Regex.Match(attrs, @"width=""(?<v>[\d.]+)""");
                    var hm = Regex.Match(attrs, @"height=""(?<v>[\d.]+)""");
                    if (xm.Success) ix = Num(xm.Groups["v"].Value);
                    if (ym.Success) iy = Num(ym.Groups["v"].Value);
                    if (wm.Success) iw = Num(wm.Groups["v"].Value);
                    if (hm.Success) ih = Num(hm.Groups["v"].Value);
                    if (iw <= 0 || ih <= 0) break;
                    var url = DecodeEntities(href.Groups["v"].Value);
                    var bytes = LoadConverterImage(url, options)
                                ?? (svgDir.Length > 0 ? LoadConverterImage(svgDir + url, options) : null);
                    if (bytes is null) break;
                    // The image box in local coords maps through the total matrix; only
                    // axis-aligned results can go through AddImage — rotation is rare in
                    // this generator's output and is skipped rather than mis-drawn.
                    if (Math.Abs(total[1]) > 1e-6 || Math.Abs(total[2]) > 1e-6) break;
                    var x0 = ix * total[0] + total[4];
                    var y0 = iy * total[3] + total[5];
                    var x1 = (ix + iw) * total[0] + total[4];
                    var y1 = (iy + ih) * total[3] + total[5];
                    FlushPaths();
                    try
                    {
                        // The active <g opacity>/<g mask> rides an ExtGState around
                        // the image draw (AddImage's own q…Q pairs inside it).
                        var imgGs = CurrentGs();
                        if (imgGs is not null)
                            page.AddContentStream(Encoding.ASCII.GetBytes($"q /{imgGs} gs\n"));
                        page.AddImage(bytes, new Aspose.Pdf.Rectangle(
                            Math.Min(x0, x1), Math.Min(y0, y1), Math.Max(x0, x1), Math.Max(y0, y1)));
                        if (imgGs is not null)
                            page.AddContentStream(Encoding.ASCII.GetBytes("Q\n"));
                    }
                    catch { /* undecodable image — skip */ }
                    break;
                }
            }
        }
        FlushPaths();
    }

    /// <summary>Register an ExtGState carrying a constant alpha and/or a luminosity
    /// soft mask (from an SVG &lt;mask&gt; raster) on the page's resources; returns its
    /// name, or null when there is no effect to register. The mask def's user-space
    /// rect maps to page space through <paramref name="rootTotal"/> (the root svg→page
    /// matrix — masks are declared in userSpaceOnUse coordinates).</summary>
    private static string? RegisterSvgEffectGState(Page page, double alpha,
        (double X, double Y, double W, double H, byte[] Png)? maskDef, double[] rootTotal)
    {
        var gs = new Core.PdfDictionary();
        gs.Set("Type", new Core.PdfName("ExtGState"));
        var hasEffect = false;
        if (alpha < 1.0 - 1e-9)
        {
            gs.Set("ca", new Core.PdfReal(alpha));
            gs.Set("CA", new Core.PdfReal(alpha));
            hasEffect = true;
        }
        if (maskDef is { } md)
        {
            try
            {
                var imgStream = ImageStamp.FromPngData(md.Png).BuildImageXObject();
                // Map the def's user-space rect to page space (axis-aligned by
                // construction of the exporter's placement matrices).
                var x0 = md.X * rootTotal[0] + rootTotal[4];
                var y0 = md.Y * rootTotal[3] + rootTotal[5];
                var x1 = (md.X + md.W) * rootTotal[0] + rootTotal[4];
                var y1 = (md.Y + md.H) * rootTotal[3] + rootTotal[5];
                var llx = Math.Min(x0, x1); var lly = Math.Min(y0, y1);
                var w = Math.Abs(x1 - x0); var h = Math.Abs(y1 - y0);
                if (w > 0.01 && h > 0.01)
                {
                    var inv = System.Globalization.CultureInfo.InvariantCulture;
                    var formDict = new Core.PdfDictionary();
                    formDict.Set("Type", new Core.PdfName("XObject"));
                    formDict.Set("Subtype", new Core.PdfName("Form"));
                    var bbox = new Core.PdfArray();
                    bbox.Add(new Core.PdfReal(llx));
                    bbox.Add(new Core.PdfReal(lly));
                    bbox.Add(new Core.PdfReal(llx + w));
                    bbox.Add(new Core.PdfReal(lly + h));
                    formDict.Set("BBox", bbox);
                    var xobjs = new Core.PdfDictionary();
                    xobjs.Set("M0", imgStream);
                    var res = new Core.PdfDictionary();
                    res.Set("XObject", xobjs);
                    formDict.Set("Resources", res);
                    var content = Encoding.ASCII.GetBytes(string.Create(inv,
                        $"q {w:F4} 0 0 {h:F4} {llx:F4} {lly:F4} cm /M0 Do Q\n"));
                    formDict.Set("Length", new Core.PdfInteger(content.Length));
                    var form = new Core.PdfStream(formDict, content);
                    var smask = new Core.PdfDictionary();
                    smask.Set("S", new Core.PdfName("Luminosity"));
                    smask.Set("G", form);
                    gs.Set("SMask", smask);
                    hasEffect = true;
                }
            }
            catch { /* an undecodable mask raster degrades to the bare alpha */ }
        }
        if (!hasEffect) return null;

        var resources = page.Reader.ResolveDict(page.Dict.Get("Resources"));
        if (resources is null)
        {
            resources = new Core.PdfDictionary();
            page.Dict.Set("Resources", resources);
        }
        var egs = page.Reader.ResolveDict(resources.Get("ExtGState"));
        if (egs is null)
        {
            egs = new Core.PdfDictionary();
            resources.Set("ExtGState", egs);
        }
        var name = "GSsvg0";
        var counter = 0;
        while (egs.ContainsKey(name)) name = $"GSsvg{++counter}";
        egs.Set(name, gs);
        return name;
    }

    /// <summary>Translate an absolute-command SVG path (<c>M/L/C/Z</c>, the only
    /// commands the converter emits) into PDF path operators. Null when the data
    /// contains anything else.</summary>
    private static string? SvgPathToPdfOps(string d)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        var tokens = Regex.Matches(d, @"[MLCZmlcz]|-?[\d.]+(?:[eE][-+]?\d+)?");
        var nums = new List<double>();
        var cmd = '\0';
        var i = 0;
        while (i < tokens.Count)
        {
            var t = tokens[i].Value;
            if (t.Length == 1 && char.IsLetter(t[0]))
            {
                cmd = t[0];
                i++;
                if (cmd is 'Z' or 'z') { sb.Append("h "); continue; }
                if (cmd is not ('M' or 'L' or 'C')) return null;
            }
            var need = cmd == 'C' ? 6 : 2;
            nums.Clear();
            while (nums.Count < need && i < tokens.Count && tokens[i].Value is { } nv
                   && (char.IsDigit(nv[0]) || nv[0] is '-' or '.'))
            {
                nums.Add(double.Parse(nv, System.Globalization.NumberStyles.Float, inv));
                i++;
            }
            if (nums.Count < need) return sb.Length > 0 ? sb.ToString() : null;
            foreach (var n in nums) sb.Append(n.ToString("F3", inv)).Append(' ');
            sb.Append(cmd switch { 'M' => "m ", 'C' => "c ", _ => "l " });
            // Successive coordinate pairs after an M continue as line-tos.
            if (cmd == 'M') cmd = 'L';
        }
        return sb.ToString();
    }
}
