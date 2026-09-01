using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Converters;

internal static partial class SvgToPdfConverter
{
    private static bool IsGradient(XmlElement el) =>
        el.LocalName is "linearGradient" or "radialGradient";

    /// <summary>Resolve an attribute through the gradient's href inheritance chain.</summary>
    private static string GradAttr(XmlElement grad, Ctx ctx, string name, int depth = 0)
    {
        var v = grad.GetAttribute(name);
        if (!string.IsNullOrEmpty(v) || depth > 4) return v;
        var href = Href(grad);
        if (!string.IsNullOrEmpty(href) && href.StartsWith('#')
            && ctx.Defs.TryGetValue(href[1..], out var parent) && IsGradient(parent))
            return GradAttr(parent, ctx, name, depth + 1);
        return "";
    }

    /// <summary>The most opaque stop of the gradient (1 when no stop declares an
    /// opacity). A gradient whose every stop is nearly transparent is a sheen
    /// overlay — painting it opaque would white out the artwork beneath (the
    /// pie's radial highlight: stop-opacity 0.06/0.2/0).</summary>
    private static double MaxStopOpacity(XmlElement grad, Ctx ctx, int depth = 0)
    {
        double max = -1;
        foreach (XmlNode child in grad.ChildNodes)
        {
            if (child is not XmlElement el || el.LocalName != "stop") continue;
            var op = el.GetAttribute("stop-opacity");
            var styleAttr = el.GetAttribute("style");
            if (!string.IsNullOrEmpty(styleAttr))
            {
                var m = Regex.Match(styleAttr, @"stop-opacity\s*:\s*([^;]+)");
                if (m.Success) op = m.Groups[1].Value.Trim();
            }
            max = Math.Max(max, string.IsNullOrEmpty(op) ? 1.0 : ParseOpacity(op));
        }
        if (max < 0 && depth <= 4)
        {
            var href = Href(grad);
            if (!string.IsNullOrEmpty(href) && href.StartsWith('#')
                && ctx.Defs.TryGetValue(href[1..], out var parent) && IsGradient(parent))
                return MaxStopOpacity(parent, ctx, depth + 1);
        }
        return max < 0 ? 1.0 : max;
    }

    /// <summary>Build a PDF shading pattern for the gradient and register it under
    /// the surface's /Pattern resources; returns the resource name (or null).</summary>
    private static string? RegisterGradientPattern(XmlElement grad, Ctx ctx, double[] ctm, double[] bbox)
    {
        var stops = GradStops(grad, ctx);
        if (stops.Count == 0) return null;
        if (stops.Count == 1)
            stops.Add(stops[0] with { Offset = 1 });

        // Function: single segment (Type 2) or stitching (Type 3).
        PdfObject BuildFn()
        {
            PdfDictionary Exp((double, double, double, double) s0, (double, double, double, double) s1)
            {
                var fn = new PdfDictionary();
                fn.Set("FunctionType", new PdfInteger(2));
                var domain = new PdfArray();
                domain.Add(new PdfInteger(0)); domain.Add(new PdfInteger(1));
                fn.Set("Domain", domain);
                var c0 = new PdfArray();
                c0.Add(new PdfReal(s0.Item2)); c0.Add(new PdfReal(s0.Item3)); c0.Add(new PdfReal(s0.Item4));
                fn.Set("C0", c0);
                var c1 = new PdfArray();
                c1.Add(new PdfReal(s1.Item2)); c1.Add(new PdfReal(s1.Item3)); c1.Add(new PdfReal(s1.Item4));
                fn.Set("C1", c1);
                fn.Set("N", new PdfInteger(1));
                return fn;
            }

            var norm = stops.Select(s => (s.Offset, s.R, s.G, s.B)).ToList();
            if (norm[0].Offset > 0) norm.Insert(0, norm[0] with { Offset = 0 });
            if (norm[^1].Offset < 1) norm.Add(norm[^1] with { Offset = 1 });
            if (norm.Count == 2)
                return Exp(norm[0], norm[1]);

            var stitch = new PdfDictionary();
            stitch.Set("FunctionType", new PdfInteger(3));
            var dom = new PdfArray();
            dom.Add(new PdfInteger(0)); dom.Add(new PdfInteger(1));
            stitch.Set("Domain", dom);
            var fns = new PdfArray();
            var bounds = new PdfArray();
            var encode = new PdfArray();
            for (var i = 0; i + 1 < norm.Count; i++)
            {
                fns.Add(Exp(norm[i], norm[i + 1]));
                if (i + 1 < norm.Count - 1)
                    bounds.Add(new PdfReal(norm[i + 1].Offset));
                encode.Add(new PdfInteger(0)); encode.Add(new PdfInteger(1));
            }
            stitch.Set("Functions", fns);
            stitch.Set("Bounds", bounds);
            stitch.Set("Encode", encode);
            return stitch;
        }

        var (shading, m) = BuildGradientShading(grad, ctx, ctm, bbox, BuildFn(), "DeviceRGB");

        var pattern = new PdfDictionary();
        pattern.Set("Type", new PdfName("Pattern"));
        pattern.Set("PatternType", new PdfInteger(2));
        pattern.Set("Shading", shading);
        var matArr = new PdfArray();
        foreach (var v in m) matArr.Add(new PdfReal(v));
        pattern.Set("Matrix", matArr);

        var patRes = GetOrCreate(ctx.Surface.Resources, "Pattern");
        var name = $"P{ctx.PatCounter++}";
        while (patRes.ContainsKey(name)) name = $"P{ctx.PatCounter++}";
        patRes.Set(name, pattern);
        return name;
    }

    /// <summary>Build a PDF tiling pattern (PatternType 1) for an SVG
    /// &lt;pattern&gt; and register it under the surface's /Pattern
    /// resources; null when the tile has no area. The tile cell is
    /// [x, y, x+w, y+h] in pattern space with the content drawn in raw user
    /// coordinates (matching SVG's user-space pattern content model), and
    /// the pattern matrix bakes patternTransform and the referencing
    /// element's CTM, so PDF's cell replication reproduces SVG's tiling
    /// phase.</summary>
    private static string? RegisterTilingPattern(XmlElement pat, Ctx ctx, double[] ctm, double[] bbox)
    {
        if (ctx.PatternDepth > 3) return null;

        var isBbox = pat.GetAttribute("patternUnits") != "userSpaceOnUse";
        double TileLen(string attr, double bboxLen, double vpLen)
        {
            var v = pat.GetAttribute(attr);
            if (string.IsNullOrEmpty(v)) return 0;
            if (v.EndsWith("%")) return ParseLength(v[..^1]) / 100.0 * (isBbox ? bboxLen : vpLen);
            var n = ParseLength(v);
            return isBbox ? n * bboxLen : n;
        }
        var tx = (isBbox ? bbox[0] : 0) + TileLen("x", bbox[2], ctx.VpW);
        var ty = (isBbox ? bbox[1] : 0) + TileLen("y", bbox[3], ctx.VpH);
        var tw = TileLen("width", bbox[2], ctx.VpW);
        var th = TileLen("height", bbox[3], ctx.VpH);
        if (tw <= 0 || th <= 0) return null;

        // Pattern content into its own stream (fresh style, identity CTM —
        // the pattern matrix carries all placement).
        var patSurface = new Surface();
        var saved = ctx.Surface;
        ctx.Surface = patSurface;
        ctx.PatternDepth++;
        var patStyle = new Dictionary<string, string>(InitialStyle, StringComparer.Ordinal);
        RenderChildren(pat, ctx, patStyle, Identity6());
        ctx.PatternDepth--;
        ctx.Surface = saved;

        // patternContentUnits=objectBoundingBox scales the content by the
        // target bounding box (about the bbox origin). User-space content
        // anchors at the TILE origin (the tile establishes the content
        // coordinate system, as browsers place it).
        var contentPrefix = pat.GetAttribute("patternContentUnits") == "objectBoundingBox"
            ? $"{F(bbox[2])} 0 0 {F(bbox[3])} {F(bbox[0])} {F(bbox[1])} cm\n"
            : tx != 0 || ty != 0
                ? $"1 0 0 1 {F(tx)} {F(ty)} cm\n"
                : "";

        var patDict = new PdfDictionary();
        patDict.Set("Type", new PdfName("Pattern"));
        patDict.Set("PatternType", new PdfInteger(1));
        patDict.Set("PaintType", new PdfInteger(1));
        patDict.Set("TilingType", new PdfInteger(1));
        // Pattern content draws UNCLIPPED by the tile, as browsers render it
        // (a circle seated on the tile corner paints whole, not quartered), so
        // the cell box extends one period beyond the tile on every side while
        // XStep/YStep keep the SVG tiling phase.
        var cell = new PdfArray();
        cell.Add(new PdfReal(tx - tw)); cell.Add(new PdfReal(ty - th));
        cell.Add(new PdfReal(tx + 2 * tw)); cell.Add(new PdfReal(ty + 2 * th));
        patDict.Set("BBox", cell);
        patDict.Set("XStep", new PdfReal(tw));
        patDict.Set("YStep", new PdfReal(th));
        patDict.Set("Resources", patSurface.Resources);

        var patTf = ParseTransformMatrix(pat.GetAttribute("patternTransform")) ?? Identity6();
        var m = Mul(patTf, ctm);
        var matArr = new PdfArray();
        foreach (var v in m) matArr.Add(new PdfReal(v));
        patDict.Set("Matrix", matArr);

        var contentBytes = Encoding.Latin1.GetBytes(contentPrefix + patSurface.Sb);
        patDict.Set("Length", new PdfInteger(contentBytes.Length));
        var stream = new PdfStream(patDict, contentBytes);

        var patRes = GetOrCreate(ctx.Surface.Resources, "Pattern");
        var name = $"P{ctx.PatCounter++}";
        while (patRes.ContainsKey(name)) name = $"P{ctx.PatCounter++}";
        patRes.Set(name, stream);
        return name;
    }

    private static void RenderImage(XmlElement elem, Ctx ctx, Dictionary<string, string> style, double[] ctm)
    {
        var href = Href(elem);
        if (string.IsNullOrEmpty(href)) return;

        byte[]? data = null;
        var dataUri = Regex.Match(href, @"^data:image/(png|jpeg|jpg);base64,(.*)$",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (dataUri.Success)
        {
            try { data = System.Convert.FromBase64String(dataUri.Groups[2].Value.Trim()); }
            catch (FormatException) { return; }
        }
        else if (!href.StartsWith("http", StringComparison.OrdinalIgnoreCase) && ctx.BaseDir is not null)
        {
            var path = Path.Combine(ctx.BaseDir, href);
            if (File.Exists(path)) data = File.ReadAllBytes(path);
        }
        if (data is null) return;

        ImageStamp stamp;
        try
        {
            stamp = data.Length >= 2 && data[0] == 0xFF && data[1] == 0xD8
                ? ImageStamp.FromJpeg(data)
                : ImageStamp.FromPngData(data);
        }
        catch
        {
            return;
        }

        var x = GetLen(elem, "x", ctx.VpW);
        var y = GetLen(elem, "y", ctx.VpH);
        var w = GetLen(elem, "width", ctx.VpW);
        var h = GetLen(elem, "height", ctx.VpH);
        if (w <= 0) w = stamp.PixelWidth;
        if (h <= 0) h = stamp.PixelHeight;
        if (w <= 0 || h <= 0) return;

        // preserveAspectRatio: default xMidYMid meet — letterbox into the box.
        var par = elem.GetAttribute("preserveAspectRatio");
        double dx = x, dy = y, dw = w, dh = h;
        if (par != "none" && stamp.PixelWidth > 0 && stamp.PixelHeight > 0)
        {
            var scale = Math.Min(w / stamp.PixelWidth, h / stamp.PixelHeight);
            dw = stamp.PixelWidth * scale;
            dh = stamp.PixelHeight * scale;
            dx = x + (w - dw) / 2;
            dy = y + (h - dh) / 2;
        }

        var name = RegisterImageXObject(stamp, ctx);
        var sb = ctx.Surface.Sb;
        sb.Append("q\n");
        ApplyTransform(elem, sb, ctm);
        var opacity = ParseOpacity(style.GetValueOrDefault("opacity"));
        if (opacity < 0.999)
            sb.Append($"/{RegisterAlphaGs(ctx, opacity, opacity)} gs\n");
        // In the y-down user space the image box top is at dy; the unit image square
        // maps bottom-left origin, so flip locally: [w 0 0 -h x y+h].
        sb.Append($"{F(dw)} 0 0 {F(-dh)} {F(dx)} {F(dy + dh)} cm\n");
        sb.Append($"/{name} Do\n");
        sb.Append("Q\n");
    }

    private static string RegisterImageXObject(ImageStamp stamp, Ctx ctx)
    {
        var xobjRes = GetOrCreate(ctx.Surface.Resources, "XObject");
        var name = "Im0";
        var counter = 0;
        while (xobjRes.ContainsKey(name)) name = $"Im{++counter}";
        xobjRes.Set(name, stamp.BuildImageXObject());
        return name;
    }

    private static string RegisterAlphaGs(Ctx ctx, double fillAlpha, double strokeAlpha)
    {
        var gsRes = GetOrCreate(ctx.Surface.Resources, "ExtGState");
        var name = $"GSa{ctx.GsCounter++}";
        while (gsRes.ContainsKey(name)) name = $"GSa{ctx.GsCounter++}";
        var gs = new PdfDictionary();
        gs.Set("Type", new PdfName("ExtGState"));
        gs.Set("ca", new PdfReal(Math.Clamp(fillAlpha, 0, 1)));
        gs.Set("CA", new PdfReal(Math.Clamp(strokeAlpha, 0, 1)));
        gsRes.Set(name, gs);
        return name;
    }

    /// <summary>A fill/stroke value that paints nothing: <c>none</c> or <c>transparent</c>.</summary>
    private static bool IsNoPaint(string? v) =>
        !string.IsNullOrEmpty(v) && (v == "none" || v.Equals("transparent", StringComparison.OrdinalIgnoreCase));

    /// <summary>Extract the id from a <c>url(#id)</c> reference, or null.</summary>
    private static string? ParseUrlRef(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        var m = Regex.Match(value, @"url\(\s*['""]?#([^'"")\s]+)['""]?\s*\)");
        return m.Success ? m.Groups[1].Value : null;
    }

    private static string Href(XmlElement el)
    {
        var href = el.GetAttribute("href");
        if (string.IsNullOrEmpty(href))
            href = el.GetAttribute("href", "http://www.w3.org/1999/xlink");
        if (string.IsNullOrEmpty(href))
            href = el.GetAttribute("xlink:href");
        return href;
    }

    private static double ParseOpacity(string? v)
    {
        if (string.IsNullOrEmpty(v)) return 1.0;
        if (v.EndsWith("%")) v = v[..^1];
        return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var o)
            ? Math.Clamp(v.Length > 0 && o > 1 ? o / 100.0 : o, 0, 1)
            : 1.0;
    }
}
