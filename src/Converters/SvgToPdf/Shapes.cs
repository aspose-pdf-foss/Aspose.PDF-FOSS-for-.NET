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
    private static void RenderChildren(XmlNode node, Ctx ctx, Dictionary<string, string> style, double[] ctm)
    {
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            RenderElement((XmlElement)child, ctx, style, ctm);
        }
    }

    private static void RenderElement(XmlElement elem, Ctx ctx, Dictionary<string, string> parentStyle, double[] ctm)
    {
        switch (elem.LocalName)
        {
            case "defs": case "style": case "title": case "desc": case "metadata":
            case "symbol": case "clipPath": case "mask": case "linearGradient":
            case "radialGradient": case "pattern": case "marker": case "filter":
                return; // definition containers — referenced, not painted
        }

        var style = ResolveStyle(elem, parentStyle, ctx);
        if (Prop(style, "display") == "none") return;

        var sb = ctx.Surface.Sb;
        switch (elem.LocalName)
        {
            case "g":
            case "a":
            case "svg":
            case "switch":
                sb.Append("q\n");
                var groupCtm = OpenGroup(elem, ctx, style, ctm);
                if (elem.LocalName == "switch")
                {
                    // Pick the first RENDERABLE alternative: skip foreignObject (Adobe
                    // Illustrator puts its private <i:pgf> payload there) and children
                    // gated on unsupported required* attributes.
                    foreach (XmlNode child in elem.ChildNodes)
                    {
                        if (child is not XmlElement cand) continue;
                        if (cand.LocalName == "foreignObject") continue;
                        if (!string.IsNullOrEmpty(cand.GetAttribute("requiredExtensions"))) continue;
                        RenderElement(cand, ctx, style, groupCtm);
                        break;
                    }
                }
                else
                {
                    RenderChildren(elem, ctx, style, groupCtm);
                }
                sb.Append("Q\n");
                break;

            case "use":
                RenderUse(elem, ctx, style, ctm);
                break;

            case "rect":
            case "circle":
            case "ellipse":
            case "line":
            case "polyline":
            case "polygon":
            case "path":
                RenderShape(elem, ctx, style, ctm);
                break;

            case "text":
                RenderText(elem, ctx, style, ctm);
                break;

            case "image":
                RenderImage(elem, ctx, style, ctm);
                break;

            default:
                // Try to render children for unknown containers
                if (elem.HasChildNodes)
                    RenderChildren(elem, ctx, style, ctm);
                break;
        }
    }

    /// <summary>Emit a group prologue (transform, clip, mask, group opacity) and return
    /// the composed CTM. Caller wraps with q/Q.</summary>
    private static double[] OpenGroup(XmlElement elem, Ctx ctx, Dictionary<string, string> style, double[] ctm)
    {
        var newCtm = ApplyTransform(elem, ctx.Surface.Sb, ctm);
        // A nested <svg x= y=> establishes its viewport at that offset — the
        // x/y translate exactly like <use>'s (the legend svg at x=510).
        if (elem.LocalName == "svg")
        {
            var sx = GetLen(elem, "x", ctx.VpW);
            var sy = GetLen(elem, "y", ctx.VpH);
            if (sx != 0 || sy != 0)
            {
                ctx.Surface.Sb.Append($"1 0 0 1 {F(sx)} {F(sy)} cm\n");
                newCtm = Mul(new[] { 1.0, 0, 0, 1, sx, sy }, newCtm);
            }
        }
        ApplyClipPath(style, ctx, newCtm);
        ApplyMask(style, ctx, newCtm);
        var opacity = ParseOpacity(style.GetValueOrDefault("opacity"));
        if (opacity < 0.999)
        {
            var gsName = RegisterAlphaGs(ctx, opacity, opacity);
            ctx.Surface.Sb.Append($"/{gsName} gs\n");
        }
        return newCtm;
    }

    private static void RenderUse(XmlElement elem, Ctx ctx, Dictionary<string, string> style, double[] ctm)
    {
        if (ctx.UseDepth > 8) return;
        var href = Href(elem);
        if (string.IsNullOrEmpty(href) || !href.StartsWith('#')) return;
        if (!ctx.Defs.TryGetValue(href[1..], out var target)) return;

        var sb = ctx.Surface.Sb;
        sb.Append("q\n");
        var newCtm = ApplyTransform(elem, sb, ctm);
        var x = GetLen(elem, "x", ctx.VpW);
        var y = GetLen(elem, "y", ctx.VpH);
        if (x != 0 || y != 0)
        {
            sb.Append($"1 0 0 1 {F(x)} {F(y)} cm\n");
            newCtm = Mul(new[] { 1.0, 0, 0, 1, x, y }, newCtm);
        }
        ctx.UseDepth++;
        RenderElement(target, ctx, style, newCtm);
        ctx.UseDepth--;
        sb.Append("Q\n");
    }

    private static void RenderShape(XmlElement elem, Ctx ctx, Dictionary<string, string> style, double[] ctm)
    {
        var (pathData, bbox, vertices) = BuildShapePath(elem, ctx);
        if (pathData.Length == 0) return;

        var sb = ctx.Surface.Sb;
        sb.Append("q\n");
        var newCtm = ApplyTransform(elem, sb, ctm);
        ApplyClipPath(style, ctx, newCtm);
        ApplyMask(style, ctx, newCtm);

        var visible = Prop(style, "visibility") != "hidden";

        var fillVal = Prop(style, "fill");
        var strokeVal = Prop(style, "stroke");
        bool hasFill = visible && !IsNoPaint(fillVal);
        // stroke-width:0 is NO stroke (a PDF `0 w` would still draw the
        // thinnest device line — the list box strokes nothing).
        bool hasStroke = visible && !IsNoPaint(strokeVal)
            && ParseLength(Prop(style, "stroke-width")) > 0;
        bool fillIsPattern = false;
        // A gradient fill whose every stop is nearly transparent is a sheen
        // overlay — skip it rather than white out what it covers.
        if (hasFill && ParseUrlRef(fillVal) is { } sheenUrl
            && ctx.Defs.TryGetValue(sheenUrl, out var sheenEl) && IsGradient(sheenEl)
            && MaxStopOpacity(sheenEl, ctx) <= 0.25)
            hasFill = false;

        // A <pattern> paint server becomes a PDF tiling pattern; a tile with
        // no area disables the fill (SVG's none-rendering rule).
        string? tilingName = null;
        if (hasFill && ParseUrlRef(fillVal) is { } tileUrl
            && ctx.Defs.TryGetValue(tileUrl, out var tileEl) && tileEl.LocalName == "pattern")
        {
            tilingName = RegisterTilingPattern(tileEl, ctx, newCtm, bbox);
            if (tilingName is null) hasFill = false;
        }

        var opacity = ParseOpacity(style.GetValueOrDefault("opacity"));
        var fillOpacity = opacity * ParseOpacity(Prop(style, "fill-opacity"));
        var strokeOpacity = opacity * ParseOpacity(Prop(style, "stroke-opacity"));
        if (fillOpacity < 0.999 || strokeOpacity < 0.999)
            sb.Append($"/{RegisterAlphaGs(ctx, fillOpacity, strokeOpacity)} gs\n");

        if (hasFill)
        {
            var url = ParseUrlRef(fillVal);
            if (tilingName is not null)
            {
                sb.Append($"/Pattern cs /{tilingName} scn\n");
                fillIsPattern = true;
            }
            else if (url is not null && ctx.Defs.TryGetValue(url, out var gradEl) && IsGradient(gradEl))
            {
                var patName = RegisterGradientPattern(gradEl, ctx, newCtm, bbox);
                if (patName is not null)
                {
                    sb.Append($"/Pattern cs /{patName} scn\n");
                    fillIsPattern = true;
                }
                else
                {
                    var (r, g, b) = AverageGradientColor(gradEl, ctx);
                    sb.Append($"{F(r)} {F(g)} {F(b)} rg\n");
                }
            }
            else if (url is not null)
            {
                // Unresolvable paint server: paint black (matches historic behaviour that
                // kept white artwork on url() backgrounds visible).
                sb.Append("0 0 0 rg\n");
            }
            else
            {
                var (r, g, b) = ParseColor(fillVal);
                sb.Append($"{F(r)} {F(g)} {F(b)} rg\n");
            }
        }
        if (hasStroke)
        {
            var url = ParseUrlRef(strokeVal);
            if (url is not null && ctx.Defs.TryGetValue(url, out var gradEl) && IsGradient(gradEl))
            {
                var (r, g, b) = AverageGradientColor(gradEl, ctx);
                sb.Append($"{F(r)} {F(g)} {F(b)} RG\n");
            }
            else if (url is null)
            {
                var (r, g, b) = ParseColor(strokeVal);
                sb.Append($"{F(r)} {F(g)} {F(b)} RG\n");
            }
            EmitStrokeState(style, sb, ctx);
        }

        sb.Append(pathData);

        bool evenOdd = Prop(style, "fill-rule") == "evenodd";
        if (hasFill && hasStroke) sb.Append(evenOdd ? "B*\n" : "B\n");
        else if (hasStroke) sb.Append("S\n");
        else if (hasFill) sb.Append(evenOdd ? "f*\n" : "f\n");
        else sb.Append("n\n");

        _ = fillIsPattern;

        if (visible && vertices.Count >= 2)
            RenderMarkers(elem, ctx, style, newCtm, vertices);

        sb.Append("Q\n");
    }

    private static void RenderMarkers(XmlElement elem, Ctx ctx,
        Dictionary<string, string> style, double[] ctm, List<(double X, double Y)> v)
    {
        var start = MarkerRef(elem, style, "marker-start");
        var mid = MarkerRef(elem, style, "marker-mid");
        var end = MarkerRef(elem, style, "marker-end");
        if (start is null && mid is null && end is null) return;

        var strokeWidth = ParseLength(Prop(style, "stroke-width"));
        if (strokeWidth <= 0) strokeWidth = 1;

        double Angle((double X, double Y) a, (double X, double Y) b) =>
            Math.Atan2(b.Y - a.Y, b.X - a.X) * 180.0 / Math.PI;

        if (start is not null)
            RenderOneMarker(start, ctx, ctm, v[0], Angle(v[0], v[1]), strokeWidth, isStart: true);
        if (mid is not null)
            for (var i = 1; i + 1 < v.Count; i++)
                RenderOneMarker(mid, ctx, ctm, v[i], Angle(v[i], v[i + 1]), strokeWidth, isStart: false);
        if (end is not null)
            RenderOneMarker(end, ctx, ctm, v[^1], Angle(v[^2], v[^1]), strokeWidth, isStart: false);
    }

    private static XmlElement? MarkerRef(XmlElement elem, Dictionary<string, string> style, string prop)
    {
        var v = elem.GetAttribute(prop);
        if (string.IsNullOrEmpty(v)) v = style.GetValueOrDefault(prop) ?? "";
        var id = ParseUrlRef(v);
        if (id is null) return null;
        return DefsLookup(elem, id);
    }

    private static XmlElement? DefsLookup(XmlElement scope, string id)
    {
        var doc = scope.OwnerDocument;
        if (doc?.DocumentElement is null) return null;
        foreach (var node in doc.DocumentElement.SelectNodes(".//*")!.Cast<XmlNode>())
            if (node is XmlElement el && el.GetAttribute("id") == id)
                return el;
        return null;
    }

    private static void RenderOneMarker(XmlElement marker, Ctx ctx, double[] ctm,
        (double X, double Y) at, double tangentDeg, double strokeWidth, bool isStart)
    {
        if (marker.LocalName != "marker") return;
        var sb = ctx.Surface.Sb;

        var orient = marker.GetAttribute("orient");
        double angle;
        if (orient is "auto" or "auto-start-reverse" or "")
            angle = tangentDeg + (isStart && orient == "auto-start-reverse" ? 180 : 0);
        else
            angle = ParseLength(orient);

        var mw = marker.HasAttribute("markerWidth") ? ParseLength(marker.GetAttribute("markerWidth")) : 3;
        var mh = marker.HasAttribute("markerHeight") ? ParseLength(marker.GetAttribute("markerHeight")) : 3;
        var refX = ParseLength(marker.GetAttribute("refX"));
        var refY = ParseLength(marker.GetAttribute("refY"));
        double vbW = mw, vbH = mh;
        var vb = marker.GetAttribute("viewBox");
        if (!string.IsNullOrEmpty(vb))
        {
            var parts = vb.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 4)
            {
                vbW = ParseLength(parts[2]);
                vbH = ParseLength(parts[3]);
            }
        }
        if (vbW <= 0) vbW = mw;
        if (vbH <= 0) vbH = mh;

        // markerUnits=strokeWidth (the default) scales the marker with the line width.
        var unitScale = marker.GetAttribute("markerUnits") == "userSpaceOnUse" ? 1.0 : strokeWidth;
        var sx = unitScale * mw / vbW;
        var sy = unitScale * mh / vbH;

        var rad = angle * Math.PI / 180.0;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);

        sb.Append("q\n");
        sb.Append($"1 0 0 1 {F(at.X)} {F(at.Y)} cm\n");
        sb.Append($"{F(cos)} {F(sin)} {F(-sin)} {F(cos)} 0 0 cm\n");
        sb.Append($"{F(sx)} 0 0 {F(sy)} 0 0 cm\n");
        sb.Append($"1 0 0 1 {F(-refX)} {F(-refY)} cm\n");

        var markerCtm = Mul(new[] { 1.0, 0, 0, 1, -refX, -refY },
            Mul(new[] { sx, 0, 0, sy, 0, 0 },
                Mul(new[] { cos, sin, -sin, cos, 0, 0 },
                    Mul(new[] { 1.0, 0, 0, 1, at.X, at.Y }, ctm))));

        var markerStyle = ResolveStyle(marker,
            new Dictionary<string, string>(InitialStyle, StringComparer.Ordinal), ctx);
        RenderChildren(marker, ctx, markerStyle, markerCtm);
        sb.Append("Q\n");
    }

    private static void EmitStrokeState(Dictionary<string, string> style, StringBuilder sb, Ctx ctx)
    {
        var sw = Prop(style, "stroke-width");
        if (!string.IsNullOrEmpty(sw))
            sb.Append($"{F(Math.Max(0.0, ParseLength(sw)))} w ");
        var cap = Prop(style, "stroke-linecap");
        if (cap == "round") sb.Append("1 J ");
        else if (cap == "square") sb.Append("2 J ");
        var join = Prop(style, "stroke-linejoin");
        if (join == "round") sb.Append("1 j ");
        else if (join == "bevel") sb.Append("2 j ");
        var dash = Prop(style, "stroke-dasharray");
        if (!string.IsNullOrEmpty(dash) && dash != "none")
        {
            var nums = Regex.Matches(dash, @"[\d.]+").Cast<Match>()
                .Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture)).ToArray();
            if (nums.Length > 0)
                sb.Append($"[{string.Join(" ", nums.Select(F))}] 0 d ");
        }
        sb.Append('\n');
    }

    private sealed class BboxAcc
    {
        public double MinX = double.MaxValue, MinY = double.MaxValue,
            MaxX = double.MinValue, MaxY = double.MinValue;
        public void Add(double x, double y)
        {
            if (x < MinX) MinX = x;
            if (y < MinY) MinY = y;
            if (x > MaxX) MaxX = x;
            if (y > MaxY) MaxY = y;
        }
        public double[] ToArray() => MinX > MaxX
            ? new[] { 0.0, 0, 1, 1 }
            : new[] { MinX, MinY, Math.Max(MaxX - MinX, 1e-6), Math.Max(MaxY - MinY, 1e-6) };
    }

    private static double Diag(Ctx ctx) =>
        Math.Sqrt(ctx.VpW * ctx.VpW + ctx.VpH * ctx.VpH) / Math.Sqrt(2);

    private static void ApplyClipPath(Dictionary<string, string> style, Ctx ctx, double[] ctm)
    {
        var refId = ParseUrlRef(style.GetValueOrDefault("clip-path"));
        if (refId is null || !ctx.Defs.TryGetValue(refId, out var clipEl) || clipEl.LocalName != "clipPath")
            return;
        var sb = ctx.Surface.Sb;
        var any = false;
        foreach (XmlNode child in clipEl.ChildNodes)
        {
            if (child is not XmlElement shape) continue;
            var (pathData, _, _) = BuildShapePath(shape, ctx);
            if (pathData.Length == 0 && shape.LocalName == "use")
            {
                var href = Href(shape);
                if (href is { Length: > 1 } && href.StartsWith('#')
                    && ctx.Defs.TryGetValue(href[1..], out var t))
                    (pathData, _, _) = BuildShapePath(t, ctx);
            }
            if (pathData.Length == 0) continue;
            // A transform on the clip shape maps its geometry; bake it into the path
            // by emitting within a temporary matrix is not possible for clips (W n
            // must be in current space), so transform the coordinates directly.
            sb.Append(pathData);
            any = true;
        }
        if (any) sb.Append("W n\n");
    }

    private static void ApplyMask(Dictionary<string, string> style, Ctx ctx, double[] ctm)
    {
        var refId = ParseUrlRef(style.GetValueOrDefault("mask"));
        if (refId is null || !ctx.Defs.TryGetValue(refId, out var maskEl) || maskEl.LocalName != "mask")
            return;
        if (ctx.MaskDepth > 3) return;

        // Render the mask content into its own Form XObject; the luminance of the
        // result becomes the alpha of everything drawn afterwards in this q…Q scope.
        var maskSurface = new Surface();
        var saved = ctx.Surface;
        ctx.Surface = maskSurface;
        ctx.MaskDepth++;
        var maskStyle = new Dictionary<string, string>(InitialStyle, StringComparer.Ordinal);
        // Mask content coordinates are user-space at reference time (default
        // maskContentUnits=userSpaceOnUse); the form inherits the ambient CTM.
        RenderChildren(maskEl, ctx, maskStyle, Identity6());
        ctx.MaskDepth--;
        ctx.Surface = saved;

        var formDict = new PdfDictionary();
        formDict.Set("Type", new PdfName("XObject"));
        formDict.Set("Subtype", new PdfName("Form"));
        formDict.Set("FormType", new PdfInteger(1));
        var bboxArr = new PdfArray();
        // Generous BBox in current user space: the whole viewport plus margin.
        bboxArr.Add(new PdfReal(-ctx.VpW)); bboxArr.Add(new PdfReal(-ctx.VpH));
        bboxArr.Add(new PdfReal(2 * ctx.VpW)); bboxArr.Add(new PdfReal(2 * ctx.VpH));
        formDict.Set("BBox", bboxArr);
        var group = new PdfDictionary();
        group.Set("S", new PdfName("Transparency"));
        group.Set("CS", new PdfName("DeviceGray"));
        formDict.Set("Group", group);
        if (maskSurface.Resources.Keys.Any())
            formDict.Set("Resources", maskSurface.Resources);
        var contentBytes = Encoding.Latin1.GetBytes(maskSurface.Sb.ToString());
        formDict.Set("Length", new PdfInteger(contentBytes.Length));
        var form = new PdfStream(formDict, contentBytes);

        var smask = new PdfDictionary();
        smask.Set("S", new PdfName("Luminosity"));
        smask.Set("G", form);
        var gs = new PdfDictionary();
        gs.Set("Type", new PdfName("ExtGState"));
        gs.Set("SMask", smask);

        var name = $"GSm{ctx.GsCounter++}";
        var gsRes = GetOrCreate(ctx.Surface.Resources, "ExtGState");
        while (gsRes.ContainsKey(name)) name = $"GSm{ctx.GsCounter++}";
        gsRes.Set(name, gs);
        ctx.Surface.Sb.Append($"/{name} gs\n");
    }
}
