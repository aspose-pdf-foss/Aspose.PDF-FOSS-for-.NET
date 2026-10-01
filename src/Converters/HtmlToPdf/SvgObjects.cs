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
    /// onto <c>page</c>. <c>placement</c> maps the SVG's
    /// viewBox onto the sheet. Raster images resolve against
    /// <c>svgDir</c>. Anything outside the subset is skipped silently —
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
                    var content = Encoding.ASCII.GetBytes(Compat.Format(inv,
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
