using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Converters;

/// <summary>
/// Converts SVG files to PDF documents.
/// Supported: rect (incl. rx/ry), circle, ellipse, line, polyline, polygon, path
/// (all commands incl. arcs and smooth curves), text/tspan (text-anchor,
/// font-weight/style mapped onto the Standard-14 faces, text-decoration), g/svg
/// nesting, defs/use, CSS &lt;style&gt; class rules, linear/radial gradients (PDF
/// shading patterns), clipPath, mask (luminosity soft masks), raster &lt;image&gt;
/// (PNG/JPEG, data: URI or file reference), opacity (ExtGState alpha).
/// </summary>
internal static class SvgToPdfConverter
{
    public static Document Convert(byte[] svgData, SvgLoadOptions? options = null)
    {
        var xml = LoadSvgXml(Encoding.UTF8.GetString(svgData));
        return ConvertFromXml(xml, options, null);
    }

    public static Document Convert(string svgPath, SvgLoadOptions? options = null)
    {
        var xml = LoadSvgXml(File.ReadAllText(svgPath));
        return ConvertFromXml(xml, options, Path.GetDirectoryName(Path.GetFullPath(svgPath)));
    }

    /// <summary>Page size (in points) the DOCUMENT rule would give this SVG —
    /// width/height attrs × unit factor (px/unitless ×0.75), 500pt default per
    /// missing dimension; viewBox ignored. Used by flow layout to size an
    /// Image{FileType=Svg} paragraph.</summary>
    internal static (double W, double H) MeasureDocumentSize(byte[] svgData)
    {
        try
        {
            var root = LoadSvgXml(Encoding.UTF8.GetString(svgData)).DocumentElement;
            if (root is null) return (500, 500);
            var w = ParseRootLength(root.GetAttribute("width"));
            var h = ParseRootLength(root.GetAttribute("height"));
            return (w > 0 ? w : 500, h > 0 ? h : 500);
        }
        catch
        {
            return (500, 500);
        }
    }

    /// <summary>Convert for IMAGE EMBEDDING (Image{FileType=Svg} rasterisation):
    /// the page takes the SVG's intrinsic size — width/height attrs read 1:1
    /// (px ≡ pt), else the viewBox extent — so the raster keeps the artwork's
    /// natural aspect ratio. Document loading instead follows the document
    /// page rule (0.75 px factor, 500pt default; see ConvertFromXml).</summary>
    internal static Document ConvertForImage(byte[] svgData)
    {
        var xml = LoadSvgXml(Encoding.UTF8.GetString(svgData));
        return ConvertFromXml(xml, null, null, imageMode: true);
    }

    /// <summary>
    /// Load SVG content as XML, handling malformed SVG gracefully.
    /// </summary>
    private static XmlDocument LoadSvgXml(string svgText)
    {
        var xml = new XmlDocument();

        // HTML-inline SVGs commonly omit the xmlns declarations while still using the
        // xlink: prefix on <use>/<image> hrefs; an undeclared prefix is a hard XML parse
        // error that would drop the whole file to the blank last-resort page. Declare the
        // standard namespace on every <svg> root that uses the prefix without declaring it.
        if (Regex.IsMatch(svgText, @"\bxlink:") && svgText.IndexOf("xmlns:xlink", StringComparison.Ordinal) < 0)
            svgText = Regex.Replace(svgText, @"<svg\b",
                "<svg xmlns:xlink=\"http://www.w3.org/1999/xlink\"", RegexOptions.IgnoreCase);

        // Raphael/HTML-inline SVGs quote url() references with the SAME quote
        // that delimits the attribute (`fill='url('#id')'`) — valid to a browser's
        // HTML parser, fatal to XML. Unquote the inner reference in place.
        if (svgText.Contains("url(", StringComparison.OrdinalIgnoreCase))
        {
            svgText = Regex.Replace(svgText, @"='url\('([^')]*)'\)'", "='url($1)'");
            svgText = Regex.Replace(svgText, @"=""url\(""([^"")]*)""\)""", "=\"url($1)\"");
        }

        // First attempt: standard XML parse with DTD disabled
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
            };
            using var reader = XmlReader.Create(new StringReader(svgText), settings);
            xml.Load(reader);
            return xml;
        }
        catch (XmlException) { }

        // Second attempt: strip unrecognized entities (Adobe Illustrator SVGs reference
        // custom entities like &ns_extend; in the <svg> xmlns attrs, defined only in an
        // external DTD). Parse with the DTD IGNORED + no resolver — using LoadXml here
        // instead would still try to process the DOCTYPE/external DTD and throw, dropping
        // the file (and its viewBox) to the empty last-resort page.
        // Adobe Illustrator SVGs reference custom entities (e.g. xmlns:i="&ns_ai;") defined only
        // in an external DTD, and USE those prefixes on body elements (<i:pgf>…). Replace each
        // unknown entity with a placeholder URI (not empty) so the prefixed xmlns stays a valid,
        // non-empty declaration and the prefix remains declared for the body — dropping the decl
        // (or emptying it) would make every use of that prefix an "undeclared prefix" parse error.
        var cleaned = Regex.Replace(svgText, @"&(?!amp;|lt;|gt;|quot;|apos;|#)\w+;", "urn:svg-entity");
        var ignoreDtd = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
        try
        {
            using var reader = XmlReader.Create(new StringReader(cleaned), ignoreDtd);
            xml.Load(reader);
            return xml;
        }
        catch (XmlException) { }

        // Third attempt: strip self-closing HTML tags (link, meta, br, hr, img, input)
        cleaned = Regex.Replace(cleaned, @"<(link|meta|br|hr|img|input)\b[^>]*/?>", "", RegexOptions.IgnoreCase);
        // Remove mismatched close tags
        cleaned = Regex.Replace(cleaned, @"</(?:link|meta|br|hr|img|input)\s*>", "", RegexOptions.IgnoreCase);
        try
        {
            using var reader = XmlReader.Create(new StringReader(cleaned), ignoreDtd);
            xml.Load(reader);
            return xml;
        }
        catch (XmlException) { }

        // Fourth attempt: wrap in root and extract SVG element
        try
        {
            // Find <svg.>.</svg> substring
            var svgStart = cleaned.IndexOf("<svg", StringComparison.OrdinalIgnoreCase);
            var svgEnd = cleaned.LastIndexOf("</svg>", StringComparison.OrdinalIgnoreCase);
            if (svgStart >= 0 && svgEnd > svgStart)
            {
                var svgOnly = cleaned.Substring(svgStart, svgEnd - svgStart + 6);
                xml.LoadXml(svgOnly);
                return xml;
            }
        }
        catch (XmlException) { }

        // Last resort: create minimal SVG
        xml.LoadXml("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"612\" height=\"792\"></svg>");
        return xml;
    }

    // ── Conversion context ──────────────────────────────────────────

    /// <summary>Where content operators and resources go — the page itself, or a
    /// form XObject while rendering mask content.</summary>
    private sealed class Surface
    {
        public StringBuilder Sb = new();
        public PdfDictionary Resources = new();
    }

    private sealed class Ctx
    {
        public Page Page = null!;
        public Surface Surface = null!;
        public Dictionary<string, XmlElement> Defs = new(StringComparer.Ordinal);
        public List<(string Selector, int Specificity, int Order, Dictionary<string, string> Props)> Css = new();
        public double VpW, VpH;      // viewport (viewBox or page) size for % lengths
        public string? BaseDir;      // for relative image hrefs
        public int UseDepth;         // <use> recursion guard
        public int MaskDepth;        // nested-mask guard
        public int FontCounter, GsCounter, PatCounter;
    }

    /// <summary>Inheritable presentation properties and their initial values.</summary>
    private static readonly Dictionary<string, string> InitialStyle = new(StringComparer.Ordinal)
    {
        ["fill"] = "black",
        ["stroke"] = "none",
        ["stroke-width"] = "1",
        ["stroke-linecap"] = "butt",
        ["stroke-linejoin"] = "miter",
        ["stroke-dasharray"] = "none",
        ["fill-rule"] = "nonzero",
        ["fill-opacity"] = "1",
        ["stroke-opacity"] = "1",
        ["font-family"] = "sans-serif",
        ["font-size"] = "16",
        ["font-weight"] = "normal",
        ["font-style"] = "normal",
        ["text-anchor"] = "start",
        ["text-decoration"] = "",
        ["color"] = "black",
        ["visibility"] = "visible",
    };

    // Properties consulted from attributes/CSS that are NOT inherited.
    private static readonly string[] NonInherited = { "opacity", "display", "clip-path", "mask" };

    private static Document ConvertFromXml(XmlDocument xml, SvgLoadOptions? options, string? baseDir,
        bool imageMode = false)
    {
        var svgRoot = xml.DocumentElement;
        if (svgRoot is null)
            throw new InvalidOperationException("SVG document has no root element");

        // Page size rule: each dimension independently comes from
        // the root width/height attribute converted to points — unitless and px scale
        // by 0.75 (CSS 96dpi), pt×1, in×72, pc×12, cm×28.346, mm×2.8346, em/ex×1.
        // A missing, percentage, zero, or invalid attribute defaults that dimension
        // to 500pt. The viewBox NEVER influences the page size; it only defines the
        // user-space window that is scaled onto the page.
        var width = imageMode
            ? ParseLength(svgRoot.GetAttribute("width"))
            : ParseRootLength(svgRoot.GetAttribute("width"));
        var height = imageMode
            ? ParseLength(svgRoot.GetAttribute("height"))
            : ParseRootLength(svgRoot.GetAttribute("height"));

        var viewBox = svgRoot.GetAttribute("viewBox");
        double vbMinX = 0, vbMinY = 0, vbW = 0, vbH = 0;
        bool hasViewBox = false;
        if (!string.IsNullOrEmpty(viewBox))
        {
            var parts = viewBox.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 4)
            {
                vbMinX = ParseLength(parts[0]);
                vbMinY = ParseLength(parts[1]);
                vbW = ParseLength(parts[2]);
                vbH = ParseLength(parts[3]);
                hasViewBox = vbW > 0 && vbH > 0;
            }
        }

        // Image mode falls back to the viewBox extent (natural artwork aspect);
        // document mode falls back to the 500pt default per dimension.
        if (width <= 0) width = imageMode && hasViewBox ? vbW : 500;
        if (height <= 0) height = imageMode && hasViewBox ? vbH : 500;

        if (!hasViewBox)
        {
            // No viewBox: content is authored in CSS px user units. Document mode
            // maps 1px → 0.75pt; image mode reads them 1:1.
            vbMinX = 0; vbMinY = 0;
            var pxFactor = imageMode ? 1.0 : 0.75;
            vbW = width / pxFactor;
            vbH = height / pxFactor;
            hasViewBox = true;
        }

        // Page geometry from load-option PageInfo (all values CSS px ×0.75): an
        // explicit Width/Height replaces the content-derived dimension;
        // otherwise margins grow the page around the artwork. The artwork itself is
        // never scaled — it anchors to the left/top margin when one is set, else to
        // the right/bottom margin (which can push it off-page), else to the origin.
        double pageW = width, pageH = height, offX = 0, offY = 0;
        var pi = imageMode ? null : options?.PageInfo;
        if (pi is not null)
        {
            const double px = 0.75;
            pageW = pi.Width > 0 ? pi.Width * px : width + (pi.Margin.Left + pi.Margin.Right) * px;
            pageH = pi.Height > 0 ? pi.Height * px : height + (pi.Margin.Top + pi.Margin.Bottom) * px;
            offX = pi.Margin.Left > 0 ? pi.Margin.Left * px
                : pi.Margin.Right > 0 ? pageW - width - pi.Margin.Right * px : 0;
            offY = pi.Margin.Top > 0 ? pi.Margin.Top * px
                : pi.Margin.Bottom > 0 ? pageH - height - pi.Margin.Bottom * px : 0;
        }

        // Create PDF document with one page matching SVG dimensions
        var doc = Document.Create();
        var page = doc.Pages.Add(pageW, pageH);

        var ctx = new Ctx
        {
            Page = page,
            Surface = new Surface(),
            VpW = hasViewBox ? vbW : width,
            VpH = hasViewBox ? vbH : height,
            BaseDir = baseDir,
        };

        CollectDefsAndCss(svgRoot, ctx);

        var sb = ctx.Surface.Sb;
        // PDF coordinate system: origin at bottom-left, Y up
        // SVG coordinate system: origin at top-left, Y down
        // Transform: translate(0, height) then scale(1, -1)
        sb.Append($"q 1 0 0 -1 {F(offX)} {F(pageH - offY)} cm\n");
        var ctm = new[] { 1.0, 0, 0, -1, offX, pageH - offY };

        // Map the viewBox user-space onto the page: scale (page/viewBox) and offset the
        // viewBox origin to (0,0). Without this, content authored in viewBox coordinates
        // (e.g. a 2291x1666 window shown on an 1100x800 page) is drawn unscaled/off-page.
        if (hasViewBox)
        {
            double sx = width / vbW, sy = height / vbH;
            sb.Append($"{F(sx)} 0 0 {F(sy)} {F(-sx * vbMinX)} {F(-sy * vbMinY)} cm\n");
            ctm = Mul(new[] { sx, 0, 0, sy, -sx * vbMinX, -sy * vbMinY }, ctm);
        }

        var rootStyle = new Dictionary<string, string>(InitialStyle, StringComparer.Ordinal);
        RenderChildren(svgRoot, ctx, rootStyle, ctm);

        sb.Append("Q\n");

        // Merge accumulated resources (fonts/patterns/ExtGStates/XObjects) into the page.
        MergeResources(page, ctx.Surface.Resources);

        // Set the content stream. Latin1 keeps escaped string bytes 1:1 with the
        // WinAnsi-ish characters EmitRun produced (UTF-8 would double-encode >0x7F).
        page.SetContentStream(Encoding.Latin1.GetBytes(sb.ToString()));

        return doc;
    }

    // ── Defs + CSS collection ───────────────────────────────────────

    private static void CollectDefsAndCss(XmlElement root, Ctx ctx)
    {
        foreach (var node in root.SelectNodes(".//*")!.Cast<XmlNode>().Prepend(root))
        {
            if (node is not XmlElement el) continue;
            var id = el.GetAttribute("id");
            if (!string.IsNullOrEmpty(id) && !ctx.Defs.ContainsKey(id))
                ctx.Defs[id] = el;
            if (el.LocalName == "style")
                ParseCss(el.InnerText, ctx);
        }
    }

    private static void ParseCss(string css, Ctx ctx)
    {
        css = Regex.Replace(css, @"/\*.*?\*/", "", RegexOptions.Singleline);
        var order = ctx.Css.Count;
        foreach (Match rule in Regex.Matches(css, @"([^{}]+)\{([^}]*)\}"))
        {
            var props = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var decl in rule.Groups[2].Value.Split(';'))
            {
                var idx = decl.IndexOf(':');
                if (idx <= 0) continue;
                var name = decl[..idx].Trim().ToLowerInvariant();
                var value = decl[(idx + 1)..].Trim();
                if (name.Length > 0 && value.Length > 0) props[name] = value;
            }
            if (props.Count == 0) continue;
            foreach (var sel in rule.Groups[1].Value.Split(','))
            {
                var s = sel.Trim();
                if (s.Length == 0) continue;
                var spec = s.StartsWith('#') ? 100 : s.Contains('.') ? 10 : 1;
                ctx.Css.Add((s, spec, order++, props));
            }
        }
    }

    /// <summary>Match simple selectors: <c>tag</c>, <c>.class</c>, <c>tag.class</c>, <c>#id</c>, <c>*</c>.</summary>
    private static bool SelectorMatches(string selector, XmlElement el)
    {
        if (selector == "*") return true;
        if (selector.StartsWith('#'))
            return el.GetAttribute("id") == selector[1..];
        string tagPart, classPart = "";
        var dot = selector.IndexOf('.');
        if (dot >= 0) { tagPart = selector[..dot]; classPart = selector[(dot + 1)..]; }
        else tagPart = selector;
        if (tagPart.Length > 0 && !string.Equals(tagPart, el.LocalName, StringComparison.OrdinalIgnoreCase))
            return false;
        if (classPart.Length > 0)
        {
            var classes = el.GetAttribute("class").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // handle multi-class selectors a.b.c
            foreach (var cls in classPart.Split('.'))
                if (Array.IndexOf(classes, cls) < 0) return false;
        }
        return true;
    }

    // ── Style resolution ────────────────────────────────────────────

    private static readonly string[] PresentationAttrs =
    {
        "fill", "stroke", "stroke-width", "stroke-linecap", "stroke-linejoin",
        "stroke-dasharray", "fill-rule", "fill-opacity", "stroke-opacity", "opacity",
        "font-family", "font-size", "font-weight", "font-style", "text-anchor",
        "text-decoration", "color", "visibility", "display", "clip-path", "mask",
    };

    /// <summary>
    /// Compute the effective style for an element: inherited values, then CSS rules
    /// (by specificity), then presentation attributes, then the inline style attribute.
    /// Presentation attributes rank below CSS per the SVG spec, but above inherited.
    /// </summary>
    private static Dictionary<string, string> ResolveStyle(XmlElement el, Dictionary<string, string> parent, Ctx ctx)
    {
        var style = new Dictionary<string, string>(parent, StringComparer.Ordinal);
        foreach (var ni in NonInherited) style.Remove(ni);

        // 1. presentation attributes
        foreach (var name in PresentationAttrs)
        {
            var v = el.GetAttribute(name);
            if (!string.IsNullOrEmpty(v)) style[name] = v.Trim();
        }

        // 2. CSS rules (override presentation attributes)
        List<(int Spec, int Order, Dictionary<string, string> Props)>? matched = null;
        foreach (var (sel, spec, order, props) in ctx.Css)
        {
            if (SelectorMatches(sel, el))
                (matched ??= new()).Add((spec, order, props));
        }
        if (matched is not null)
        {
            matched.Sort((a, b) => a.Spec != b.Spec ? a.Spec - b.Spec : a.Order - b.Order);
            foreach (var (_, _, props) in matched)
                foreach (var kv in props)
                    style[kv.Key] = kv.Value;
        }

        // 3. inline style attribute (highest)
        var inline = el.GetAttribute("style");
        if (!string.IsNullOrEmpty(inline))
        {
            foreach (var decl in inline.Split(';'))
            {
                var idx = decl.IndexOf(':');
                if (idx <= 0) continue;
                var name = decl[..idx].Trim().ToLowerInvariant();
                var value = decl[(idx + 1)..].Trim();
                if (name.Length > 0 && value.Length > 0) style[name] = value;
            }
        }

        // currentColor indirection
        if (style.TryGetValue("fill", out var f) && f == "currentColor")
            style["fill"] = style.GetValueOrDefault("color", "black");
        if (style.TryGetValue("stroke", out var st) && st == "currentColor")
            style["stroke"] = style.GetValueOrDefault("color", "black");

        return style;
    }

    private static string Prop(Dictionary<string, string> style, string name) =>
        style.GetValueOrDefault(name, InitialStyle.GetValueOrDefault(name, ""));

    // ── Rendering ───────────────────────────────────────────────────

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

    // ── Shapes ──────────────────────────────────────────────────────

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

        var opacity = ParseOpacity(style.GetValueOrDefault("opacity"));
        var fillOpacity = opacity * ParseOpacity(Prop(style, "fill-opacity"));
        var strokeOpacity = opacity * ParseOpacity(Prop(style, "stroke-opacity"));
        if (fillOpacity < 0.999 || strokeOpacity < 0.999)
            sb.Append($"/{RegisterAlphaGs(ctx, fillOpacity, strokeOpacity)} gs\n");

        if (hasFill)
        {
            var url = ParseUrlRef(fillVal);
            if (url is not null && ctx.Defs.TryGetValue(url, out var gradEl) && IsGradient(gradEl))
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

    // ── Markers ─────────────────────────────────────────────────────

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

    /// <summary>Build the PDF path operators for a shape element, plus its
    /// user-space bounding box (for objectBoundingBox gradients) and vertex list
    /// (for marker placement).</summary>
    private static (string Path, double[] Bbox, List<(double X, double Y)> Vertices)
        BuildShapePath(XmlElement elem, Ctx ctx)
    {
        var sb = new StringBuilder();
        var bb = new BboxAcc();
        var vertices = new List<(double X, double Y)>();
        switch (elem.LocalName)
        {
            case "rect":
            {
                var x = GetLen(elem, "x", ctx.VpW);
                var y = GetLen(elem, "y", ctx.VpH);
                var w = GetLen(elem, "width", ctx.VpW);
                var h = GetLen(elem, "height", ctx.VpH);
                if (w <= 0 || h <= 0) break;
                var rx = GetLen(elem, "rx", ctx.VpW);
                var ry = GetLen(elem, "ry", ctx.VpH);
                if (rx <= 0 && ry > 0) rx = ry;
                if (ry <= 0 && rx > 0) ry = rx;
                rx = Math.Min(rx, w / 2); ry = Math.Min(ry, h / 2);
                bb.Add(x, y); bb.Add(x + w, y + h);
                if (rx > 0.01 && ry > 0.01)
                {
                    const double k = 0.5522847498;
                    var kx = rx * k; var ky = ry * k;
                    sb.Append($"{F(x + rx)} {F(y)} m ");
                    sb.Append($"{F(x + w - rx)} {F(y)} l ");
                    sb.Append($"{F(x + w - rx + kx)} {F(y)} {F(x + w)} {F(y + ry - ky)} {F(x + w)} {F(y + ry)} c ");
                    sb.Append($"{F(x + w)} {F(y + h - ry)} l ");
                    sb.Append($"{F(x + w)} {F(y + h - ry + ky)} {F(x + w - rx + kx)} {F(y + h)} {F(x + w - rx)} {F(y + h)} c ");
                    sb.Append($"{F(x + rx)} {F(y + h)} l ");
                    sb.Append($"{F(x + rx - kx)} {F(y + h)} {F(x)} {F(y + h - ry + ky)} {F(x)} {F(y + h - ry)} c ");
                    sb.Append($"{F(x)} {F(y + ry)} l ");
                    sb.Append($"{F(x)} {F(y + ry - ky)} {F(x + rx - kx)} {F(y)} {F(x + rx)} {F(y)} c h ");
                }
                else
                {
                    sb.Append($"{F(x)} {F(y)} {F(w)} {F(h)} re ");
                }
                break;
            }
            case "circle":
            {
                var cx = GetLen(elem, "cx", ctx.VpW);
                var cy = GetLen(elem, "cy", ctx.VpH);
                var r = GetLen(elem, "r", Diag(ctx));
                if (r <= 0) break;
                bb.Add(cx - r, cy - r); bb.Add(cx + r, cy + r);
                AppendEllipsePath(sb, cx, cy, r, r);
                break;
            }
            case "ellipse":
            {
                var cx = GetLen(elem, "cx", ctx.VpW);
                var cy = GetLen(elem, "cy", ctx.VpH);
                var rx = GetLen(elem, "rx", ctx.VpW);
                var ry = GetLen(elem, "ry", ctx.VpH);
                if (rx <= 0 || ry <= 0) break;
                bb.Add(cx - rx, cy - ry); bb.Add(cx + rx, cy + ry);
                AppendEllipsePath(sb, cx, cy, rx, ry);
                break;
            }
            case "line":
            {
                var x1 = GetLen(elem, "x1", ctx.VpW);
                var y1 = GetLen(elem, "y1", ctx.VpH);
                var x2 = GetLen(elem, "x2", ctx.VpW);
                var y2 = GetLen(elem, "y2", ctx.VpH);
                bb.Add(x1, y1); bb.Add(x2, y2);
                vertices.Add((x1, y1)); vertices.Add((x2, y2));
                sb.Append($"{F(x1)} {F(y1)} m {F(x2)} {F(y2)} l ");
                break;
            }
            case "polyline":
            case "polygon":
            {
                var points = elem.GetAttribute("points");
                if (string.IsNullOrEmpty(points)) break;
                var nums = Regex.Matches(points, @"-?[\d.]+(?:[eE][+-]?\d+)?").Cast<Match>()
                    .Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture)).ToArray();
                if (nums.Length < 4) break;
                sb.Append($"{F(nums[0])} {F(nums[1])} m ");
                bb.Add(nums[0], nums[1]);
                vertices.Add((nums[0], nums[1]));
                for (int i = 2; i + 1 < nums.Length; i += 2)
                {
                    sb.Append($"{F(nums[i])} {F(nums[i + 1])} l ");
                    bb.Add(nums[i], nums[i + 1]);
                    vertices.Add((nums[i], nums[i + 1]));
                }
                if (elem.LocalName == "polygon") sb.Append("h ");
                break;
            }
            case "path":
            {
                var d = elem.GetAttribute("d");
                if (string.IsNullOrEmpty(d)) break;
                ConvertSvgPathToPdf(d, sb, bb, vertices);
                break;
            }
        }
        return (sb.ToString(), bb.ToArray(), vertices);
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

    // ── Clip & mask ─────────────────────────────────────────────────

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

    // ── Gradients ───────────────────────────────────────────────────

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

    private static List<(double Offset, double R, double G, double B)> GradStops(XmlElement grad, Ctx ctx, int depth = 0)
    {
        var stops = new List<(double, double, double, double)>();
        foreach (XmlNode child in grad.ChildNodes)
        {
            if (child is not XmlElement el || el.LocalName != "stop") continue;
            var offStr = el.GetAttribute("offset");
            double off = 0;
            if (!string.IsNullOrEmpty(offStr))
                off = offStr.EndsWith("%")
                    ? ParseLength(offStr[..^1]) / 100.0
                    : ParseLength(offStr);
            var colorStr = el.GetAttribute("stop-color");
            var styleAttr = el.GetAttribute("style");
            if (!string.IsNullOrEmpty(styleAttr))
            {
                var m = Regex.Match(styleAttr, @"stop-color\s*:\s*([^;]+)");
                if (m.Success) colorStr = m.Groups[1].Value.Trim();
            }
            if (string.IsNullOrEmpty(colorStr)) colorStr = "black";
            var (r, g, b) = ParseColor(colorStr);
            stops.Add((Math.Clamp(off, 0, 1), r, g, b));
        }
        if (stops.Count == 0 && depth <= 4)
        {
            var href = Href(grad);
            if (!string.IsNullOrEmpty(href) && href.StartsWith('#')
                && ctx.Defs.TryGetValue(href[1..], out var parent) && IsGradient(parent))
                return GradStops(parent, ctx, depth + 1);
        }
        stops.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return stops;
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

    private static (double, double, double) AverageGradientColor(XmlElement grad, Ctx ctx)
    {
        var stops = GradStops(grad, ctx);
        if (stops.Count == 0) return (0, 0, 0);
        return (stops.Average(s => s.R), stops.Average(s => s.G), stops.Average(s => s.B));
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

        var units = GradAttr(grad, ctx, "gradientUnits");
        var isBbox = units != "userSpaceOnUse";
        var gradTf = ParseTransformMatrix(GradAttr(grad, ctx, "gradientTransform"));

        var shading = new PdfDictionary();
        shading.Set("ColorSpace", new PdfName("DeviceRGB"));
        shading.Set("Function", BuildFn());
        var extend = new PdfArray();
        extend.Add(PdfBoolean.True); extend.Add(PdfBoolean.True);
        shading.Set("Extend", extend);

        double RefLen(string attr, double dflt, double refBase)
        {
            var v = GradAttr(grad, ctx, attr);
            if (string.IsNullOrEmpty(v)) return dflt;
            if (v.EndsWith("%")) return ParseLength(v[..^1]) / 100.0 * (isBbox ? 1.0 : refBase);
            return ParseLength(v);
        }

        var coords = new PdfArray();
        if (grad.LocalName == "linearGradient"
            || (grad.LocalName != "radialGradient" && true))
        {
            shading.Set("ShadingType", new PdfInteger(2));
            var x1 = RefLen("x1", 0, ctx.VpW);
            var y1 = RefLen("y1", 0, ctx.VpH);
            var x2 = RefLen("x2", isBbox ? 1 : ctx.VpW, ctx.VpW);
            var y2 = RefLen("y2", 0, ctx.VpH);
            coords.Add(new PdfReal(x1)); coords.Add(new PdfReal(y1));
            coords.Add(new PdfReal(x2)); coords.Add(new PdfReal(y2));
        }
        else
        {
            shading.Set("ShadingType", new PdfInteger(3));
            var cx = RefLen("cx", isBbox ? 0.5 : ctx.VpW / 2, ctx.VpW);
            var cy = RefLen("cy", isBbox ? 0.5 : ctx.VpH / 2, ctx.VpH);
            var r = RefLen("r", isBbox ? 0.5 : Diag(ctx) / 2, Diag(ctx));
            var fxAttr = GradAttr(grad, ctx, "fx");
            var fyAttr = GradAttr(grad, ctx, "fy");
            var fx = string.IsNullOrEmpty(fxAttr) ? cx : RefLen("fx", cx, ctx.VpW);
            var fy = string.IsNullOrEmpty(fyAttr) ? cy : RefLen("fy", cy, ctx.VpH);
            coords.Add(new PdfReal(fx)); coords.Add(new PdfReal(fy)); coords.Add(new PdfReal(0));
            coords.Add(new PdfReal(cx)); coords.Add(new PdfReal(cy)); coords.Add(new PdfReal(r));
        }
        shading.Set("Coords", coords);

        // Pattern matrix maps gradient space to the page default space:
        // gradientTransform, then (for objectBoundingBox) the bbox mapping,
        // then the current CTM.
        var m = gradTf ?? Identity6();
        if (isBbox)
            m = Mul(m, new[] { bbox[2], 0, 0, bbox[3], bbox[0], bbox[1] });
        m = Mul(m, ctm);

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

    // ── Images ──────────────────────────────────────────────────────

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

    // ── Text ────────────────────────────────────────────────────────

    private static void RenderText(XmlElement elem, Ctx ctx, Dictionary<string, string> style, double[] ctm)
    {
        var sb = ctx.Surface.Sb;
        sb.Append("q\n");
        var transform = elem.GetAttribute("transform");
        var tmMatrix = ParseMatrixOnly(transform);
        // A pure matrix() transform is applied through the text matrix (Tm) in
        // EmitRun — emitting it as a cm too would double the translation.
        var newCtm = tmMatrix is null ? ApplyTransform(elem, sb, ctm) : ctm;
        ApplyClipPath(style, ctx, newCtm);
        ApplyMask(style, ctx, newCtm);

        // Walk the text content: direct text nodes and tspan children, tracking the
        // current text position.
        double curX = GetFirstLen(elem, "x", ctx.VpW);
        double curY = GetFirstLen(elem, "y", ctx.VpH);

        void EmitRun(string text, XmlElement source, Dictionary<string, string> runStyle)
        {
            if (text.Length == 0) return;
            // U+A880 is the exporter's PUA stand-in for a space-like glyph slot
            // (see SvgDevice.ShowText); map it back to a plain space on import.
            text = text.Replace('ꢀ', ' ');
            if (text.Trim().Length == 0) return;

            var fontSize = ParseLength(Prop(runStyle, "font-size"));
            if (fontSize <= 0) fontSize = 16;

            // Non-WinAnsi text (Arabic, Hebrew, Cyrillic, CJK, …) cannot be written with a
            // Standard-14 face — it would flatten to '?'. Route it through an embedded Type0
            // face (RTL runs shaped to visual order first). uniTtf == null => the run keeps
            // the Standard-14 path below.
            byte[]? uniTtf = NeedsUnicodeSvg(text) ? ResolveSvgUnicodeTtf(text) : null;
            var display = text;
            if (uniTtf is not null)
                display = IsPureRtlSvg(text) ? ToVisualRtlSvg(text)
                    : Text.BidiReorderer.ContainsRtl(text) ? VisualizeMixedRtlSvg(text) : text;

            var baseFont = MapFont(runStyle);
            var fontDict = GetOrCreate(ctx.Surface.Resources, "Font");
            var fontRes = uniTtf is null ? EnsureFontResource(ctx, baseFont) : "";

            var width = uniTtf is null
                ? MeasureText(text, baseFont, fontSize)
                : Text.Type0FontEmbedder.MeasureText(fontDict, uniTtf, "SvgUni", display, fontSize, stripSpacesInBaseFont: true);
            var anchor = Prop(runStyle, "text-anchor");
            var x = curX;
            if (anchor == "middle") x -= width / 2;
            else if (anchor == "end") x -= width;

            var visible = Prop(runStyle, "visibility") != "hidden";
            if (visible)
            {
                var fillVal = Prop(runStyle, "fill");
                double fr = 0, fg = 0, fb = 0;
                var noFill = IsNoPaint(fillVal);
                if (!noFill && ParseUrlRef(fillVal) is null)
                    (fr, fg, fb) = ParseColor(fillVal);

                var opacity = ParseOpacity(runStyle.GetValueOrDefault("opacity"))
                              * ParseOpacity(Prop(runStyle, "fill-opacity"));

                sb.Append("q\n");
                if (opacity < 0.999)
                    sb.Append($"/{RegisterAlphaGs(ctx, opacity, opacity)} gs\n");
                if (!noFill)
                    sb.Append($"{F(fr)} {F(fg)} {F(fb)} rg ");

                // The glyph payload: WinAnsi (escaped) string, or 2-byte Type0 hex codes.
                string glyphOp;
                string useFontRes;
                if (uniTtf is not null)
                {
                    var (rn, hex) = Text.Type0FontEmbedder.Embed(fontDict, uniTtf, "SvgUni", display, stripSpacesInBaseFont: true);
                    useFontRes = rn;
                    glyphOp = $"<{System.Convert.ToHexString(hex)}>";
                }
                else
                {
                    useFontRes = fontRes;
                    glyphOp = $"({EscapePdfString(text)})";
                }

                if (tmMatrix is not null)
                {
                    // FOSS-generated SVG (round-trip): a matrix() transform on <text> is the
                    // PDF text matrix with its y-column negated (see SvgDevice) — negate it
                    // back to recover the text matrix and place the run with Tm.
                    sb.Append($"BT /{useFontRes} {F(fontSize)} Tf " +
                        $"{F(tmMatrix[0])} {F(tmMatrix[1])} {F(-tmMatrix[2])} {F(-tmMatrix[3])} {F(tmMatrix[4])} {F(tmMatrix[5])} Tm ");
                }
                else
                {
                    // Draw with a LOCAL y-flip (1 0 0 -1) so the glyphs are upright —
                    // cancelling the page's scale(1,-1); without the flip the text
                    // renders mirrored/upside-down.
                    sb.Append($"BT /{useFontRes} {F(fontSize)} Tf 1 0 0 -1 {F(x)} {F(curY)} Tm ");
                }
                sb.Append($"{glyphOp} Tj ET\n");

                // text-decoration: draw the line as a filled rect in user space.
                var deco = Prop(runStyle, "text-decoration");
                if (deco.Contains("line-through") || deco.Contains("underline"))
                {
                    var t = Math.Max(fontSize * 0.06, 0.5);
                    if (deco.Contains("line-through"))
                        sb.Append($"{F(x)} {F(curY - fontSize * 0.30 - t / 2)} {F(width)} {F(t)} re f\n");
                    if (deco.Contains("underline"))
                        sb.Append($"{F(x)} {F(curY + fontSize * 0.11 - t / 2)} {F(width)} {F(t)} re f\n");
                }
                sb.Append("Q\n");
            }
            curX += width;
        }

        void Walk(XmlNode node, Dictionary<string, string> nodeStyle)
        {
            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.NodeType is XmlNodeType.Text or XmlNodeType.CDATA)
                {
                    var text = CollapseWs(child.Value ?? "");
                    EmitRun(text, (XmlElement)node, nodeStyle);
                }
                else if (child is XmlElement tspan && child.LocalName is "tspan" or "textPath" or "a")
                {
                    var childStyle = ResolveStyle(tspan, nodeStyle, ctx);
                    if (tspan.HasAttribute("x")) curX = GetFirstLen(tspan, "x", ctx.VpW);
                    if (tspan.HasAttribute("y")) curY = GetFirstLen(tspan, "y", ctx.VpH);
                    curX += GetFirstLen(tspan, "dx", ctx.VpW);
                    curY += GetFirstLen(tspan, "dy", ctx.VpH);
                    Walk(tspan, childStyle);
                }
            }
        }

        Walk(elem, style);
        sb.Append("Q\n");
    }

    private static string CollapseWs(string s) => Regex.Replace(s, @"\s+", " ").Trim();

    private static string EscapePdfString(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case '(': sb.Append("\\("); break;
                case ')': sb.Append("\\)"); break;
                default:
                    // Content stream strings are WinAnsi-ish single-byte; map
                    // non-Latin1 chars to '?' rather than corrupting the stream.
                    sb.Append(ch <= 0xFF ? ch : '?');
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Map font-family/weight/style onto a Standard-14 base font name.</summary>
    private static string MapFont(Dictionary<string, string> style)
    {
        var family = Prop(style, "font-family").ToLowerInvariant();
        var weight = Prop(style, "font-weight").ToLowerInvariant();
        var italicStyle = Prop(style, "font-style").ToLowerInvariant();

        var bold = weight is "bold" or "bolder" || (double.TryParse(weight, out var wNum) && wNum >= 600);
        var italic = italicStyle is "italic" or "oblique";

        // First recognizable family in the list decides the face.
        string face = "helvetica";
        foreach (var raw in family.Split(','))
        {
            var f = raw.Trim().Trim('"', '\'');
            if (f.Length == 0) continue;
            if (f.Contains("times") || f.Contains("serif") && !f.Contains("sans")
                || f.Contains("georgia") || f.Contains("cambria") || f.Contains("garamond")
                || f.Contains("book"))
            { face = "times"; break; }
            if (f.Contains("courier") || f.Contains("mono") || f.Contains("consolas"))
            { face = "courier"; break; }
            if (f.Contains("arial") || f.Contains("helvetica") || f.Contains("sans")
                || f.Contains("verdana") || f.Contains("tahoma") || f.Contains("segoe")
                || f.Contains("lucida") || f.Contains("calibri") || f.Contains("frutiger"))
            { face = "helvetica"; break; }
        }

        return face switch
        {
            "times" => bold && italic ? "Times-BoldItalic"
                : bold ? "Times-Bold"
                : italic ? "Times-Italic"
                : "Times-Roman",
            "courier" => bold && italic ? "Courier-BoldOblique"
                : bold ? "Courier-Bold"
                : italic ? "Courier-Oblique"
                : "Courier",
            _ => bold && italic ? "Helvetica-BoldOblique"
                : bold ? "Helvetica-Bold"
                : italic ? "Helvetica-Oblique"
                : "Helvetica",
        };
    }

    private static double MeasureText(string text, string baseFont, double fontSize)
    {
        double total = 0;
        foreach (var ch in text)
        {
            var w = Text.Standard14Fonts.GetWidth(baseFont, ch < 256 ? ch : '?');
            total += (w >= 0 ? w : 500) / 1000.0 * fontSize;
        }
        return total;
    }

    // Broad-Unicode faces (installed on most Windows systems) tried in order for SVG
    // <text> whose characters the Standard-14 faces cannot encode; the first whose
    // embedded program covers every non-WinAnsi character in the run is embedded.
    private static readonly string[] SvgUnicodeFallbackFonts =
        { "Arial", "Segoe UI", "Tahoma", "SimSun", "Microsoft YaHei", "MS Gothic",
          "Arial Unicode MS", "Nirmala UI", "Ebrima", "Segoe UI Historic" };
    private static readonly Dictionary<string, (byte[]? ttf, Dictionary<int, int>? cmap)>
        _svgUniFontCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True when the run has any character the WinAnsi Tf/Tj path cannot encode.</summary>
    private static bool NeedsUnicodeSvg(string s)
    {
        foreach (var ch in s)
            if (ch > 0x7F && !Text.Cp1252.TryGetByte(ch, out _)) return true;
        return false;
    }

    /// <summary>Resolve an embedded Unicode fallback face covering every non-WinAnsi
    /// character in <paramref name="text"/>, or null when none is available.</summary>
    private static byte[]? ResolveSvgUnicodeTtf(string text)
    {
        foreach (var name in SvgUnicodeFallbackFonts)
        {
            if (!_svgUniFontCache.TryGetValue(name, out var entry))
            {
                byte[]? ttf = null; Dictionary<int, int>? cmap = null;
                try
                {
                    ttf = Text.FontRepository.FindFont(name)?.SourceFontData?.TtfData;
                    if (ttf is not null) cmap = new Text.GlyphOutlineParser(ttf).CMap;
                }
                catch { ttf = null; cmap = null; }
                entry = (ttf, cmap);
                _svgUniFontCache[name] = entry;
            }
            if (entry.ttf is null || entry.cmap is null) continue;
            var covers = true;
            foreach (var ch in text)
            {
                if (ch <= 0x7F || Text.Cp1252.TryGetByte(ch, out _)) continue;
                if (!entry.cmap.TryGetValue(ch, out var gid) || gid == 0) { covers = false; break; }
            }
            if (covers) return entry.ttf;
        }
        return null;
    }

    /// <summary>True when the run is entirely RTL letters plus neutrals.</summary>
    private static bool IsPureRtlSvg(string s)
    {
        var hasRtl = false;
        foreach (var c in s)
        {
            if (Text.BidiReorderer.IsRtlChar(c)) hasRtl = true;
            else if (c == ' ' || c == '\t' || (c >= '!' && c <= '@')
                     || (c >= '[' && c <= '`') || (c >= '{' && c <= '~')) { /* neutral */ }
            else return false;
        }
        return hasRtl;
    }

    /// <summary>Pure-RTL logical string → visual order: Arabic shaped, others reversed.</summary>
    private static string ToVisualRtlSvg(string s)
    {
        if (Text.ArabicTextShaper.ContainsArabic(s)) return Text.ArabicTextShaper.Shape(s);
        var arr = s.ToCharArray();
        System.Array.Reverse(arr);
        return new string(arr);
    }

    /// <summary>Visualize the RTL segments of a mixed LTR+RTL run in place.</summary>
    private static string VisualizeMixedRtlSvg(string s)
    {
        var sb = new StringBuilder(s.Length);
        int i = 0;
        while (i < s.Length)
        {
            if (!Text.BidiReorderer.IsRtlChar(s[i])) { sb.Append(s[i]); i++; continue; }
            int end = i, j = i;
            while (j < s.Length)
            {
                if (Text.BidiReorderer.IsRtlChar(s[j])) { end = j; j++; }
                else if (s[j] == ' ' || char.IsPunctuation(s[j]) || char.IsDigit(s[j])) j++;
                else break;
            }
            sb.Append(ToVisualRtlSvg(s.Substring(i, end - i + 1)));
            i = end + 1;
        }
        return sb.ToString();
    }

    /// <summary>Parse a <c>matrix(a,b,c,d,e,f)</c> transform, or null if the transform
    /// is missing or contains any other function.</summary>
    private static double[]? ParseMatrixOnly(string transform)
    {
        if (string.IsNullOrEmpty(transform)) return null;
        var m = Regex.Match(transform, @"^\s*matrix\s*\(([^)]*)\)\s*$");
        if (!m.Success) return null;
        var nums = Regex.Matches(m.Groups[1].Value, @"-?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?")
            .Cast<Match>()
            .Select(x => double.Parse(x.Value, CultureInfo.InvariantCulture))
            .ToArray();
        return nums.Length >= 6 ? nums : null;
    }

    // ── SVG path → PDF path conversion ──────────────────────────────

    private static void ConvertSvgPathToPdf(string d, StringBuilder sb, BboxAcc bb,
        List<(double X, double Y)>? vertices = null)
    {
        var tokens = Regex.Matches(d, @"[MmLlHhVvCcSsQqTtAaZz]|[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][+-]?\d+)?");
        double cx = 0, cy = 0; // current point
        double sx = 0, sy = 0; // subpath start
        double pcx = 0, pcy = 0; // previous cubic control (for S/s)
        double pqx = 0, pqy = 0; // previous quadratic control (for T/t)
        char prevCmd = ' ';

        var nums = new List<double>();
        char cmd = 'M';

        void Cubic(double x1, double y1, double x2, double y2, double x3, double y3)
        {
            sb.Append($"{F(x1)} {F(y1)} {F(x2)} {F(y2)} {F(x3)} {F(y3)} c ");
            bb.Add(x1, y1); bb.Add(x2, y2); bb.Add(x3, y3);
            pcx = x2; pcy = y2;
            cx = x3; cy = y3;
            vertices?.Add((cx, cy));
        }

        foreach (Match token in tokens)
        {
            var val = token.Value;
            if (val.Length == 1 && char.IsLetter(val[0]) && !char.IsDigit(val[0]))
            {
                cmd = val[0];
                nums.Clear();
                if (cmd is 'Z' or 'z')
                {
                    sb.Append("h ");
                    cx = sx; cy = sy;
                    prevCmd = 'Z';
                }
                continue;
            }

            if (!double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
                continue;
            nums.Add(num);

            switch (cmd)
            {
                case 'M' when nums.Count >= 2:
                    cx = nums[0]; cy = nums[1];
                    sx = cx; sy = cy;
                    sb.Append($"{F(cx)} {F(cy)} m ");
                    bb.Add(cx, cy);
                    vertices?.Add((cx, cy));
                    nums.Clear(); cmd = 'L'; prevCmd = 'M';
                    break;
                case 'm' when nums.Count >= 2:
                    cx += nums[0]; cy += nums[1];
                    sx = cx; sy = cy;
                    sb.Append($"{F(cx)} {F(cy)} m ");
                    bb.Add(cx, cy);
                    vertices?.Add((cx, cy));
                    nums.Clear(); cmd = 'l'; prevCmd = 'M';
                    break;
                case 'L' when nums.Count >= 2:
                    cx = nums[0]; cy = nums[1];
                    sb.Append($"{F(cx)} {F(cy)} l ");
                    bb.Add(cx, cy);
                    vertices?.Add((cx, cy));
                    nums.Clear(); prevCmd = 'L';
                    break;
                case 'l' when nums.Count >= 2:
                    cx += nums[0]; cy += nums[1];
                    sb.Append($"{F(cx)} {F(cy)} l ");
                    bb.Add(cx, cy);
                    vertices?.Add((cx, cy));
                    nums.Clear(); prevCmd = 'L';
                    break;
                case 'H' when nums.Count >= 1:
                    cx = nums[0];
                    sb.Append($"{F(cx)} {F(cy)} l ");
                    bb.Add(cx, cy);
                    vertices?.Add((cx, cy));
                    nums.Clear(); prevCmd = 'L';
                    break;
                case 'h' when nums.Count >= 1:
                    cx += nums[0];
                    sb.Append($"{F(cx)} {F(cy)} l ");
                    bb.Add(cx, cy);
                    vertices?.Add((cx, cy));
                    nums.Clear(); prevCmd = 'L';
                    break;
                case 'V' when nums.Count >= 1:
                    cy = nums[0];
                    sb.Append($"{F(cx)} {F(cy)} l ");
                    bb.Add(cx, cy);
                    vertices?.Add((cx, cy));
                    nums.Clear(); prevCmd = 'L';
                    break;
                case 'v' when nums.Count >= 1:
                    cy += nums[0];
                    sb.Append($"{F(cx)} {F(cy)} l ");
                    bb.Add(cx, cy);
                    vertices?.Add((cx, cy));
                    nums.Clear(); prevCmd = 'L';
                    break;
                case 'C' when nums.Count >= 6:
                    Cubic(nums[0], nums[1], nums[2], nums[3], nums[4], nums[5]);
                    nums.Clear(); prevCmd = 'C';
                    break;
                case 'c' when nums.Count >= 6:
                    Cubic(cx + nums[0], cy + nums[1], cx + nums[2], cy + nums[3], cx + nums[4], cy + nums[5]);
                    nums.Clear(); prevCmd = 'C';
                    break;
                case 'S' when nums.Count >= 4:
                {
                    var (rx, ry) = prevCmd == 'C' ? (2 * cx - pcx, 2 * cy - pcy) : (cx, cy);
                    Cubic(rx, ry, nums[0], nums[1], nums[2], nums[3]);
                    nums.Clear(); prevCmd = 'C';
                    break;
                }
                case 's' when nums.Count >= 4:
                {
                    var (rx, ry) = prevCmd == 'C' ? (2 * cx - pcx, 2 * cy - pcy) : (cx, cy);
                    Cubic(rx, ry, cx + nums[0], cy + nums[1], cx + nums[2], cy + nums[3]);
                    nums.Clear(); prevCmd = 'C';
                    break;
                }
                case 'Q' when nums.Count >= 4:
                {
                    var qx = nums[0]; var qy = nums[1];
                    var ex = nums[2]; var ey = nums[3];
                    Cubic(cx + 2.0 / 3.0 * (qx - cx), cy + 2.0 / 3.0 * (qy - cy),
                        ex + 2.0 / 3.0 * (qx - ex), ey + 2.0 / 3.0 * (qy - ey), ex, ey);
                    pqx = qx; pqy = qy;
                    nums.Clear(); prevCmd = 'Q';
                    break;
                }
                case 'q' when nums.Count >= 4:
                {
                    var qx = cx + nums[0]; var qy = cy + nums[1];
                    var ex = cx + nums[2]; var ey = cy + nums[3];
                    Cubic(cx + 2.0 / 3.0 * (qx - cx), cy + 2.0 / 3.0 * (qy - cy),
                        ex + 2.0 / 3.0 * (qx - ex), ey + 2.0 / 3.0 * (qy - ey), ex, ey);
                    pqx = qx; pqy = qy;
                    nums.Clear(); prevCmd = 'Q';
                    break;
                }
                case 'T' or 't' when nums.Count >= 2:
                {
                    var (qx, qy) = prevCmd == 'Q' ? (2 * cx - pqx, 2 * cy - pqy) : (cx, cy);
                    var ex = cmd == 'T' ? nums[0] : cx + nums[0];
                    var ey = cmd == 'T' ? nums[1] : cy + nums[1];
                    Cubic(cx + 2.0 / 3.0 * (qx - cx), cy + 2.0 / 3.0 * (qy - cy),
                        ex + 2.0 / 3.0 * (qx - ex), ey + 2.0 / 3.0 * (qy - ey), ex, ey);
                    pqx = qx; pqy = qy;
                    nums.Clear(); prevCmd = 'Q';
                    break;
                }
                case 'A' or 'a' when nums.Count >= 7:
                {
                    var ex = cmd == 'A' ? nums[5] : cx + nums[5];
                    var ey = cmd == 'A' ? nums[6] : cy + nums[6];
                    ArcToBeziers(sb, bb, cx, cy, nums[0], nums[1], nums[2],
                        nums[3] != 0, nums[4] != 0, ex, ey, ref pcx, ref pcy);
                    cx = ex; cy = ey;
                    vertices?.Add((cx, cy));
                    nums.Clear(); prevCmd = 'A';
                    break;
                }
            }
        }
    }

    /// <summary>Convert an SVG elliptical arc to cubic Bezier segments
    /// (endpoint → center parameterization, PDF-ready).</summary>
    private static void ArcToBeziers(StringBuilder sb, BboxAcc bb, double x1, double y1,
        double rx, double ry, double rotDeg, bool largeArc, bool sweep,
        double x2, double y2, ref double pcx, ref double pcy)
    {
        if (rx == 0 || ry == 0 || (x1 == x2 && y1 == y2))
        {
            sb.Append($"{F(x2)} {F(y2)} l ");
            bb.Add(x2, y2);
            return;
        }
        rx = Math.Abs(rx); ry = Math.Abs(ry);
        var phi = rotDeg * Math.PI / 180.0;
        var cosPhi = Math.Cos(phi);
        var sinPhi = Math.Sin(phi);

        // Step 1: compute (x1', y1')
        var dx2 = (x1 - x2) / 2.0;
        var dy2 = (y1 - y2) / 2.0;
        var x1p = cosPhi * dx2 + sinPhi * dy2;
        var y1p = -sinPhi * dx2 + cosPhi * dy2;

        // Correct radii
        var lam = (x1p * x1p) / (rx * rx) + (y1p * y1p) / (ry * ry);
        if (lam > 1)
        {
            var s = Math.Sqrt(lam);
            rx *= s; ry *= s;
        }

        // Step 2: compute (cx', cy')
        var rxSq = rx * rx; var rySq = ry * ry;
        var x1pSq = x1p * x1p; var y1pSq = y1p * y1p;
        var num = rxSq * rySq - rxSq * y1pSq - rySq * x1pSq;
        if (num < 0) num = 0;
        var den = rxSq * y1pSq + rySq * x1pSq;
        var coef = den == 0 ? 0 : Math.Sqrt(num / den);
        if (largeArc == sweep) coef = -coef;
        var cxp = coef * (rx * y1p / ry);
        var cyp = coef * (-ry * x1p / rx);

        // Step 3: center
        var cxc = cosPhi * cxp - sinPhi * cyp + (x1 + x2) / 2;
        var cyc = sinPhi * cxp + cosPhi * cyp + (y1 + y2) / 2;

        // Step 4: angles
        double Angle(double ux, double uy, double vx, double vy)
        {
            var dot = ux * vx + uy * vy;
            var len = Math.Sqrt((ux * ux + uy * uy) * (vx * vx + vy * vy));
            var ang = Math.Acos(Math.Clamp(dot / len, -1, 1));
            if (ux * vy - uy * vx < 0) ang = -ang;
            return ang;
        }
        var theta1 = Angle(1, 0, (x1p - cxp) / rx, (y1p - cyp) / ry);
        var dTheta = Angle((x1p - cxp) / rx, (y1p - cyp) / ry, (-x1p - cxp) / rx, (-y1p - cyp) / ry);
        if (!sweep && dTheta > 0) dTheta -= 2 * Math.PI;
        else if (sweep && dTheta < 0) dTheta += 2 * Math.PI;

        // Split into segments of at most 90°
        var segments = (int)Math.Ceiling(Math.Abs(dTheta) / (Math.PI / 2));
        if (segments == 0) segments = 1;
        var delta = dTheta / segments;
        var t = 4.0 / 3.0 * Math.Tan(delta / 4);

        var cosT1 = Math.Cos(theta1);
        var sinT1 = Math.Sin(theta1);
        var curX = x1; var curY = y1;
        for (var i = 0; i < segments; i++)
        {
            var theta2 = theta1 + delta;
            var cosT2 = Math.Cos(theta2);
            var sinT2 = Math.Sin(theta2);

            // Endpoint of this segment
            var ex = cxc + rx * (cosPhi * cosT2) - ry * (sinPhi * sinT2);
            var ey = cyc + rx * (sinPhi * cosT2) + ry * (cosPhi * sinT2);

            // Control points
            var c1x = curX + t * (-rx * cosPhi * sinT1 - ry * sinPhi * cosT1);
            var c1y = curY + t * (-rx * sinPhi * sinT1 + ry * cosPhi * cosT1);
            var c2x = ex - t * (-rx * cosPhi * sinT2 - ry * sinPhi * cosT2);
            var c2y = ey - t * (-rx * sinPhi * sinT2 + ry * cosPhi * cosT2);

            sb.Append($"{F(c1x)} {F(c1y)} {F(c2x)} {F(c2y)} {F(ex)} {F(ey)} c ");
            bb.Add(c1x, c1y); bb.Add(c2x, c2y); bb.Add(ex, ey);
            pcx = c2x; pcy = c2y;

            theta1 = theta2;
            cosT1 = cosT2; sinT1 = sinT2;
            curX = ex; curY = ey;
        }
    }

    // ── Transforms ──────────────────────────────────────────────────

    /// <summary>Emit the element's transform functions as <c>cm</c> operators and
    /// return the CTM composed with them.</summary>
    private static double[] ApplyTransform(XmlElement elem, StringBuilder sb, double[] ctm)
    {
        var transform = elem.GetAttribute("transform");
        if (string.IsNullOrEmpty(transform)) return ctm;

        // A transform attribute may chain several functions, e.g.
        // "translate(0,540) scale(1,-1)". SVG applies them left-to-right, so emit
        // a `cm` per function in the same order.
        var result = ctm;
        foreach (var m in EnumerateTransforms(transform))
        {
            sb.Append($"{F(m[0])} {F(m[1])} {F(m[2])} {F(m[3])} {F(m[4])} {F(m[5])} cm\n");
            result = Mul(m, result);
        }
        return result;
    }

    /// <summary>Parse a full transform list into a single composed matrix (or null).</summary>
    private static double[]? ParseTransformMatrix(string transform)
    {
        if (string.IsNullOrEmpty(transform)) return null;
        double[]? total = null;
        foreach (var m in EnumerateTransforms(transform))
            total = total is null ? m : Mul(m, total);
        return total;
    }

    private static IEnumerable<double[]> EnumerateTransforms(string transform)
    {
        foreach (Match fn in Regex.Matches(transform, @"(matrix|translate|scale|rotate|skewX|skewY)\s*\(([^)]*)\)"))
        {
            var op = fn.Groups[1].Value;
            var args = Regex.Matches(fn.Groups[2].Value, @"-?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?")
                .Cast<Match>()
                .Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture))
                .ToArray();

            switch (op)
            {
                case "matrix" when args.Length >= 6:
                    yield return new[] { args[0], args[1], args[2], args[3], args[4], args[5] };
                    break;
                case "translate" when args.Length >= 1:
                    yield return new[] { 1.0, 0, 0, 1, args[0], args.Length > 1 ? args[1] : 0 };
                    break;
                case "scale" when args.Length >= 1:
                    yield return new[] { args[0], 0, 0, args.Length > 1 ? args[1] : args[0], 0, 0 };
                    break;
                case "rotate" when args.Length >= 1:
                {
                    var rad = args[0] * Math.PI / 180.0;
                    var cos = Math.Cos(rad);
                    var sin = Math.Sin(rad);
                    if (args.Length >= 3)
                    {
                        // rotate(angle, cx, cy) == translate(cx,cy) rotate translate(-cx,-cy)
                        yield return new[] { 1.0, 0, 0, 1, args[1], args[2] };
                        yield return new[] { cos, sin, -sin, cos, 0, 0 };
                        yield return new[] { 1.0, 0, 0, 1, -args[1], -args[2] };
                    }
                    else
                    {
                        yield return new[] { cos, sin, -sin, cos, 0, 0 };
                    }
                    break;
                }
                case "skewX" when args.Length >= 1:
                    yield return new[] { 1.0, 0, Math.Tan(args[0] * Math.PI / 180.0), 1, 0, 0 };
                    break;
                case "skewY" when args.Length >= 1:
                    yield return new[] { 1.0, Math.Tan(args[0] * Math.PI / 180.0), 0, 1, 0, 0 };
                    break;
            }
        }
    }

    // ── Resources ───────────────────────────────────────────────────

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

    private static string EnsureFontResource(Ctx ctx, string baseFontName)
    {
        var fontRes = GetOrCreate(ctx.Surface.Resources, "Font");
        // Reuse an existing resource for the same base font.
        foreach (var key in fontRes.Keys)
        {
            if (fontRes.Get(key) is PdfDictionary fd && fd.GetName("BaseFont") == baseFontName)
                return key;
        }
        var name = $"F{++ctx.FontCounter}";
        while (fontRes.ContainsKey(name)) name = $"F{++ctx.FontCounter}";
        var font = new PdfDictionary();
        font.Set("Type", new PdfName("Font"));
        font.Set("Subtype", new PdfName("Type1"));
        font.Set("BaseFont", new PdfName(baseFontName));
        font.Set("Encoding", new PdfName("WinAnsiEncoding"));
        fontRes.Set(name, font);
        return name;
    }

    private static PdfDictionary GetOrCreate(PdfDictionary parent, string key)
    {
        if (parent.Get(key) is PdfDictionary d) return d;
        var dict = new PdfDictionary();
        parent.Set(key, dict);
        return dict;
    }

    /// <summary>Merge converter-accumulated resources into the page's /Resources.</summary>
    private static void MergeResources(Page page, PdfDictionary accumulated)
    {
        var resources = page.Dict.Get("Resources") as PdfDictionary;
        if (resources is null)
        {
            resources = new PdfDictionary();
            page.Dict.Set("Resources", resources);
        }
        foreach (var key in accumulated.Keys)
        {
            if (accumulated.Get(key) is not PdfDictionary src) continue;
            var dst = GetOrCreate(resources, key);
            foreach (var sub in src.Keys)
                dst.Set(sub, src.Get(sub)!);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static void AppendEllipsePath(StringBuilder sb, double cx, double cy, double rx, double ry)
    {
        // Approximate ellipse with 4 cubic Bezier curves
        const double k = 0.5522847498; // 4/3 * (sqrt(2) - 1)
        var kx = rx * k;
        var ky = ry * k;

        sb.Append($"{F(cx - rx)} {F(cy)} m ");
        sb.Append($"{F(cx - rx)} {F(cy - ky)} {F(cx - kx)} {F(cy - ry)} {F(cx)} {F(cy - ry)} c ");
        sb.Append($"{F(cx + kx)} {F(cy - ry)} {F(cx + rx)} {F(cy - ky)} {F(cx + rx)} {F(cy)} c ");
        sb.Append($"{F(cx + rx)} {F(cy + ky)} {F(cx + kx)} {F(cy + ry)} {F(cx)} {F(cy + ry)} c ");
        sb.Append($"{F(cx - kx)} {F(cy + ry)} {F(cx - rx)} {F(cy + ky)} {F(cx - rx)} {F(cy)} c h ");
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

    private static (double r, double g, double b) ParseColor(string color)
    {
        color = color.Trim();
        if (color.StartsWith('#'))
        {
            if (color.Length >= 7)
            {
                var r = int.Parse(color.Substring(1, 2), NumberStyles.HexNumber) / 255.0;
                var g = int.Parse(color.Substring(3, 2), NumberStyles.HexNumber) / 255.0;
                var b = int.Parse(color.Substring(5, 2), NumberStyles.HexNumber) / 255.0;
                return (r, g, b);
            }
            if (color.Length == 4)
            {
                var r = int.Parse(color.Substring(1, 1), NumberStyles.HexNumber) / 15.0;
                var g = int.Parse(color.Substring(2, 1), NumberStyles.HexNumber) / 15.0;
                var b = int.Parse(color.Substring(3, 1), NumberStyles.HexNumber) / 15.0;
                return (r, g, b);
            }
        }

        var rgbMatch = Regex.Match(color, @"rgba?\s*\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)");
        if (rgbMatch.Success)
            return (int.Parse(rgbMatch.Groups[1].Value) / 255.0,
                    int.Parse(rgbMatch.Groups[2].Value) / 255.0,
                    int.Parse(rgbMatch.Groups[3].Value) / 255.0);

        // Named colors
        return color.ToLowerInvariant() switch
        {
            "black" => (0, 0, 0),
            "white" => (1, 1, 1),
            "red" => (1, 0, 0),
            "green" => (0, 0.502, 0),
            "blue" => (0, 0, 1),
            "yellow" => (1, 1, 0),
            "gray" or "grey" => (0.502, 0.502, 0.502),
            "lightgray" or "lightgrey" => (0.827, 0.827, 0.827),
            "darkgray" or "darkgrey" => (0.663, 0.663, 0.663),
            "silver" => (0.753, 0.753, 0.753),
            "gainsboro" => (0.863, 0.863, 0.863),
            "whitesmoke" => (0.961, 0.961, 0.961),
            "orange" => (1, 0.647, 0),
            "gold" => (1, 0.843, 0),
            "purple" => (0.502, 0, 0.502),
            "navy" => (0, 0, 0.502),
            "maroon" => (0.502, 0, 0),
            "olive" => (0.502, 0.502, 0),
            "teal" => (0, 0.502, 0.502),
            "aqua" or "cyan" => (0, 1, 1),
            "fuchsia" or "magenta" => (1, 0, 1),
            "lime" => (0, 1, 0),
            "lightblue" => (0.678, 0.847, 0.902),
            "darkblue" => (0, 0, 0.545),
            "darkgreen" => (0, 0.392, 0),
            "darkred" => (0.545, 0, 0),
            "pink" => (1, 0.753, 0.796),
            "brown" => (0.647, 0.165, 0.165),
            "beige" => (0.961, 0.961, 0.863),
            "ivory" => (1, 1, 0.941),
            "khaki" => (0.941, 0.902, 0.549),
            "lavender" => (0.902, 0.902, 0.980),
            "salmon" => (0.980, 0.502, 0.447),
            "coral" => (1, 0.498, 0.314),
            "tomato" => (1, 0.388, 0.278),
            "orangered" => (1, 0.271, 0),
            "skyblue" => (0.529, 0.808, 0.922),
            "steelblue" => (0.275, 0.510, 0.706),
            "royalblue" => (0.255, 0.412, 0.882),
            "midnightblue" => (0.098, 0.098, 0.439),
            "forestgreen" => (0.133, 0.545, 0.133),
            "seagreen" => (0.180, 0.545, 0.341),
            "limegreen" => (0.196, 0.804, 0.196),
            "yellowgreen" => (0.604, 0.804, 0.196),
            "goldenrod" => (0.855, 0.647, 0.125),
            "indigo" => (0.294, 0, 0.510),
            "violet" => (0.933, 0.510, 0.933),
            "plum" => (0.867, 0.627, 0.867),
            "orchid" => (0.855, 0.439, 0.839),
            "crimson" => (0.863, 0.078, 0.235),
            "chocolate" => (0.824, 0.412, 0.118),
            "peru" => (0.804, 0.522, 0.247),
            "tan" => (0.824, 0.706, 0.549),
            "wheat" => (0.961, 0.871, 0.702),
            "snow" => (1, 0.980, 0.980),
            "mintcream" => (0.961, 1, 0.980),
            "azure" => (0.941, 1, 1),
            "aliceblue" => (0.941, 0.973, 1),
            "ghostwhite" => (0.973, 0.973, 1),
            "honeydew" => (0.941, 1, 0.941),
            "linen" => (0.980, 0.941, 0.902),
            "oldlace" => (0.992, 0.961, 0.902),
            "seashell" => (1, 0.961, 0.933),
            "slategray" or "slategrey" => (0.439, 0.502, 0.565),
            "lightslategray" or "lightslategrey" => (0.467, 0.533, 0.600),
            "dimgray" or "dimgrey" => (0.412, 0.412, 0.412),
            _ => (0, 0, 0), // default black
        };
    }

    /// <summary>Length of an attribute; <c>%</c> resolves against <paramref name="refLen"/>.</summary>
    private static double GetLen(XmlElement elem, string attr, double refLen)
    {
        var val = elem.GetAttribute(attr);
        if (string.IsNullOrEmpty(val)) return 0;
        val = val.Trim();
        if (val.EndsWith("%"))
            return ParseLength(val[..^1]) / 100.0 * refLen;
        return ParseLength(val);
    }

    /// <summary>First value of a (possibly space/comma separated) coordinate list.</summary>
    private static double GetFirstLen(XmlElement elem, string attr, double refLen)
    {
        var val = elem.GetAttribute(attr);
        if (string.IsNullOrEmpty(val)) return 0;
        var first = val.Split(new[] { ' ', ',', '\t', '\n', '\r' },
            StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrEmpty(first)) return 0;
        if (first.EndsWith("%"))
            return ParseLength(first[..^1]) / 100.0 * refLen;
        return ParseLength(first);
    }

    /// <summary>Root width/height attribute → points:
    /// unitless/px ×0.75, pt ×1, in ×72, pc ×12, cm ×28.346, mm ×2.8346, em/ex ×1.
    /// Missing, percentage, zero, or unparsable → 0 (caller defaults to 500pt).</summary>
    private static double ParseRootLength(string? val)
    {
        if (string.IsNullOrWhiteSpace(val)) return 0;
        val = val.Trim();
        if (val.EndsWith("%")) return 0;
        var m = Regex.Match(val, @"^([-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][+-]?\d+)?)\s*(px|pt|em|ex|cm|mm|in|pc)?$");
        if (!m.Success) return 0;
        var num = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var factor = m.Groups[2].Value switch
        {
            "pt" => 1.0,
            "in" => 72.0,
            "pc" => 12.0,
            "cm" => 28.346,
            "mm" => 2.8346,
            "em" or "ex" => 1.0,
            // px or unitless: CSS 96-per-inch pixels
            _ => 0.75,
        };
        return num * factor;
    }

    /// <summary>Parse a CSS/SVG length into USER units (CSS px): px/unitless ×1,
    /// pt ×4/3, in ×96, cm ×37.795, mm ×3.7795, pc ×16, em ×16, ex ×8.</summary>
    private static double ParseLength(string val)
    {
        val = val.Trim();
        var m = Regex.Match(val, @"^([-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][+-]?\d+)?)\s*(px|pt|em|ex|cm|mm|in|pc|%)?$");
        if (!m.Success)
        {
            // Fall back to the first number in the string.
            var n = Regex.Match(val, @"[-+]?(?:\d*\.\d+|\d+\.?)");
            return n.Success ? double.Parse(n.Value, CultureInfo.InvariantCulture) : 0;
        }
        var num = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        return m.Groups[2].Value switch
        {
            "pt" => num * 4.0 / 3.0,
            "in" => num * 96.0,
            "cm" => num * 96.0 / 2.54,
            "mm" => num * 96.0 / 25.4,
            "pc" => num * 16.0,
            "em" => num * 16.0,
            "ex" => num * 8.0,
            // px, %, unitless: 1 user unit
            _ => num,
        };
    }

    private static double[] Identity6() => new[] { 1.0, 0, 0, 1, 0, 0 };

    /// <summary>Compose two affine matrices (row-vector convention): m1 × m2.</summary>
    private static double[] Mul(double[] m1, double[] m2) => new[]
    {
        m1[0] * m2[0] + m1[1] * m2[2],
        m1[0] * m2[1] + m1[1] * m2[3],
        m1[2] * m2[0] + m1[3] * m2[2],
        m1[2] * m2[1] + m1[3] * m2[3],
        m1[4] * m2[0] + m1[5] * m2[2] + m2[4],
        m1[4] * m2[1] + m1[5] * m2[3] + m2[5],
    };

    private static string F(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
}
