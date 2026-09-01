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
internal static partial class SvgToPdfConverter
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
        public int PatternDepth;     // nested-tiling-pattern guard
        public int FontCounter, GsCounter, PatCounter;
        /// <summary>Font programs the document itself declares through @font-face
        /// (inline, in a linked stylesheet, or behind an @import), keyed by family.</summary>
        public Dictionary<string, byte[]> DeclaredFaces = new(StringComparer.OrdinalIgnoreCase);
    }

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
            double tx0 = 0, ty0 = 0;
            // preserveAspectRatio (default "xMidYMid meet"): a uniform scale with
            // the leftover space distributed by the alignment; "none" keeps the
            // independent per-axis stretch.
            var par = svgRoot.GetAttribute("preserveAspectRatio").Trim();
            if (par != "none")
            {
                var slice = par.EndsWith("slice", StringComparison.Ordinal);
                var s = slice ? Math.Max(sx, sy) : Math.Min(sx, sy);
                var align = par.Length > 0 ? par.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0] : "xMidYMid";
                double fx = align.StartsWith("xMid", StringComparison.OrdinalIgnoreCase) ? 0.5
                    : align.StartsWith("xMax", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                double fy = align.Contains("YMid", StringComparison.OrdinalIgnoreCase) ? 0.5
                    : align.Contains("YMax", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                tx0 = (width - vbW * s) * fx;
                ty0 = (height - vbH * s) * fy;
                sx = sy = s;
            }
            sb.Append($"{F(sx)} 0 0 {F(sy)} {F(tx0 - sx * vbMinX)} {F(ty0 - sy * vbMinY)} cm\n");
            ctm = Mul(new[] { sx, 0, 0, sy, tx0 - sx * vbMinX, ty0 - sy * vbMinY }, ctm);
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

    // ── Style resolution ────────────────────────────────────────────

    // ── Rendering ───────────────────────────────────────────────────

    // ── Shapes ──────────────────────────────────────────────────────

    // ── Markers ─────────────────────────────────────────────────────

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

    // ── Clip & mask ─────────────────────────────────────────────────

    // ── Gradients ───────────────────────────────────────────────────

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
            var opStr = el.GetAttribute("stop-opacity");
            var styleAttr = el.GetAttribute("style");
            if (!string.IsNullOrEmpty(styleAttr))
            {
                var m = Regex.Match(styleAttr, @"stop-color\s*:\s*([^;]+)");
                if (m.Success) colorStr = m.Groups[1].Value.Trim();
                var mo = Regex.Match(styleAttr, @"stop-opacity\s*:\s*([^;]+)");
                if (mo.Success) opStr = mo.Groups[1].Value.Trim();
            }
            if (string.IsNullOrEmpty(colorStr)) colorStr = "black";
            var (r, g, b) = ParseColor(colorStr);
            // stop-opacity composites against the page backdrop; the corpus
            // draws gradients over white, so a partially transparent stop is
            // the colour blended toward white by (1 − opacity). This keeps the
            // whole ramp in one opaque shading (no soft-mask form, which the
            // renderers place differently for linear geometries).
            if (!string.IsNullOrEmpty(opStr))
            {
                var op = Math.Clamp(ParseOpacity(opStr), 0, 1);
                r = r * op + (1 - op);
                g = g * op + (1 - op);
                b = b * op + (1 - op);
            }
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

    private static (double, double, double) AverageGradientColor(XmlElement grad, Ctx ctx)
    {
        var stops = GradStops(grad, ctx);
        if (stops.Count == 0) return (0, 0, 0);
        return (stops.Average(s => s.R), stops.Average(s => s.G), stops.Average(s => s.B));
    }

    /// <summary>The gradient's shading dictionary (geometry + the given
    /// function/colour space) and the pattern matrix mapping gradient space
    /// to the page default space: gradientTransform, then (for
    /// objectBoundingBox units) the bbox mapping, then the current CTM.</summary>
    private static (PdfDictionary Shading, double[] Matrix) BuildGradientShading(
        XmlElement grad, Ctx ctx, double[] ctm, double[] bbox, PdfObject function, string colorSpace)
    {
        var units = GradAttr(grad, ctx, "gradientUnits");
        var isBbox = units != "userSpaceOnUse";
        var gradTf = ParseTransformMatrix(GradAttr(grad, ctx, "gradientTransform"));

        var shading = new PdfDictionary();
        shading.Set("ColorSpace", new PdfName(colorSpace));
        shading.Set("Function", function);
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
        if (grad.LocalName != "radialGradient")
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
            // /fr: the focal circle's own radius (SVG 2), 0 when absent.
            var fr = RefLen("fr", 0, Diag(ctx));
            coords.Add(new PdfReal(fx)); coords.Add(new PdfReal(fy)); coords.Add(new PdfReal(fr));
            coords.Add(new PdfReal(cx)); coords.Add(new PdfReal(cy)); coords.Add(new PdfReal(r));
        }
        shading.Set("Coords", coords);

        var m = gradTf ?? Identity6();
        if (isBbox)
            m = Mul(m, new[] { bbox[2], 0, 0, bbox[3], bbox[0], bbox[1] });
        m = Mul(m, ctm);
        return (shading, m);
    }

    // ── Tiling patterns ─────────────────────────────────────────────

    // ── Images ──────────────────────────────────────────────────────

    // ── Text ────────────────────────────────────────────────────────

    private static readonly Dictionary<string, (byte[]? ttf, Dictionary<int, int>? cmap)>
        _svgUniFontCache = new(StringComparer.OrdinalIgnoreCase);

    // ── SVG path → PDF path conversion ──────────────────────────────

    // ── Transforms ──────────────────────────────────────────────────

    // ── Resources ───────────────────────────────────────────────────

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
            // Remainder of the CSS3 / X11 extended keyword set.
            "antiquewhite" => (0.980, 0.922, 0.843),
            "aquamarine" => (0.498, 1, 0.831),
            "bisque" => (1, 0.894, 0.769),
            "blanchedalmond" => (1, 0.922, 0.804),
            "blueviolet" => (0.541, 0.169, 0.886),
            "burlywood" => (0.871, 0.722, 0.529),
            "cadetblue" => (0.373, 0.620, 0.627),
            "chartreuse" => (0.498, 1, 0),
            "cornflowerblue" => (0.392, 0.584, 0.929),
            "cornsilk" => (1, 0.973, 0.863),
            "darkcyan" => (0, 0.545, 0.545),
            "darkgoldenrod" => (0.722, 0.525, 0.043),
            "darkkhaki" => (0.741, 0.718, 0.420),
            "darkmagenta" => (0.545, 0, 0.545),
            "darkolivegreen" => (0.333, 0.420, 0.184),
            "darkorange" => (1, 0.549, 0),
            "darkorchid" => (0.600, 0.196, 0.800),
            "darksalmon" => (0.914, 0.588, 0.478),
            "darkseagreen" => (0.561, 0.737, 0.561),
            "darkslateblue" => (0.282, 0.239, 0.545),
            "darkslategray" or "darkslategrey" => (0.184, 0.310, 0.310),
            "darkturquoise" => (0, 0.808, 0.820),
            "darkviolet" => (0.580, 0, 0.827),
            "deeppink" => (1, 0.078, 0.576),
            "deepskyblue" => (0, 0.749, 1),
            "dodgerblue" => (0.118, 0.565, 1),
            "firebrick" => (0.698, 0.133, 0.133),
            "floralwhite" => (1, 0.980, 0.941),
            "greenyellow" => (0.678, 1, 0.184),
            "hotpink" => (1, 0.412, 0.706),
            "indianred" => (0.804, 0.361, 0.361),
            "lavenderblush" => (1, 0.941, 0.961),
            "lawngreen" => (0.486, 0.988, 0),
            "lemonchiffon" => (1, 0.980, 0.804),
            "lightcoral" => (0.941, 0.502, 0.502),
            "lightcyan" => (0.878, 1, 1),
            "lightgoldenrodyellow" => (0.980, 0.980, 0.824),
            "lightgreen" => (0.565, 0.933, 0.565),
            "lightpink" => (1, 0.714, 0.757),
            "lightsalmon" => (1, 0.627, 0.478),
            "lightseagreen" => (0.125, 0.698, 0.667),
            "lightskyblue" => (0.529, 0.808, 0.980),
            "lightsteelblue" => (0.690, 0.769, 0.871),
            "lightyellow" => (1, 1, 0.878),
            "mediumaquamarine" => (0.400, 0.804, 0.667),
            "mediumblue" => (0, 0, 0.804),
            "mediumorchid" => (0.729, 0.333, 0.827),
            "mediumpurple" => (0.576, 0.439, 0.859),
            "mediumseagreen" => (0.235, 0.702, 0.443),
            "mediumslateblue" => (0.482, 0.408, 0.933),
            "mediumspringgreen" => (0, 0.980, 0.604),
            "mediumturquoise" => (0.282, 0.820, 0.800),
            "mediumvioletred" => (0.780, 0.082, 0.522),
            "mistyrose" => (1, 0.894, 0.882),
            "moccasin" => (1, 0.894, 0.710),
            "navajowhite" => (1, 0.871, 0.678),
            "olivedrab" => (0.420, 0.557, 0.137),
            "palegoldenrod" => (0.933, 0.910, 0.667),
            "palegreen" => (0.596, 0.984, 0.596),
            "paleturquoise" => (0.686, 0.933, 0.933),
            "palevioletred" => (0.859, 0.439, 0.576),
            "papayawhip" => (1, 0.937, 0.835),
            "peachpuff" => (1, 0.855, 0.725),
            "powderblue" => (0.690, 0.878, 0.902),
            "rosybrown" => (0.737, 0.561, 0.561),
            "saddlebrown" => (0.545, 0.271, 0.075),
            "sandybrown" => (0.957, 0.643, 0.376),
            "sienna" => (0.627, 0.322, 0.176),
            "slateblue" => (0.416, 0.353, 0.804),
            "springgreen" => (0, 1, 0.498),
            "thistle" => (0.847, 0.749, 0.847),
            "turquoise" => (0.251, 0.878, 0.816),
            _ => (0, 0, 0), // default black
        };
    }

}
