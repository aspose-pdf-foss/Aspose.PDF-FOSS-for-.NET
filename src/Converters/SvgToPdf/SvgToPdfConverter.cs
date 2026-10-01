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
        /// <summary>The alpha ExtGStates of this page by their 255-level value: the reference keeps
        /// one per VALUE, whichever paint asked first (see AlphaGsFor).</summary>
        public Dictionary<int, string> AlphaGsByLevel = new();
        /// <summary>Font programs the document itself declares through @font-face
        /// (inline, in a linked stylesheet, or behind an @import), keyed by family.</summary>
        public Dictionary<string, byte[]> DeclaredFaces = new(StringComparer.OrdinalIgnoreCase);
    }

    private static Document ConvertFromXml(XmlDocument xml, SvgLoadOptions? options, string? baseDir, bool imageMode = false)
    {
        var sv = new SvgConvertState();
        sv.xml = xml;
        sv.options = options;
        sv.baseDir = baseDir;
        sv.imageMode = imageMode;
        sv.svgRoot = sv.xml.DocumentElement;
        if (sv.svgRoot is null)
            throw new InvalidOperationException("SVG document has no root element");

        sv.width = sv.imageMode
            ? ParseLength(sv.svgRoot.GetAttribute("width"))
            : ParseRootLength(sv.svgRoot.GetAttribute("width"));
        sv.height = sv.imageMode
            ? ParseLength(sv.svgRoot.GetAttribute("height"))
            : ParseRootLength(sv.svgRoot.GetAttribute("height"));

        sv.viewBox = sv.svgRoot.GetAttribute("viewBox");
        sv.vbMinX = 0;
        sv.vbMinY = 0;
        sv.vbW = 0;
        sv.vbH = 0;
        sv.hasViewBox = false;
        ReadSvgViewBox(sv);

        sv.pageW = sv.width;
        sv.pageH = sv.height;
        sv.offX = 0;
        sv.offY = 0;
        sv.pi = sv.imageMode ? null : sv.options?.PageInfo;
        ApplySvgPageInfo(sv);

        sv.doc = Document.Create();
        sv.page = sv.doc.Pages.Add(sv.pageW, sv.pageH);

        sv.ctx = new Ctx
        {
            Page = sv.page,
            Surface = new Surface(),
            VpW = sv.hasViewBox ? sv.vbW : sv.width,
            VpH = sv.hasViewBox ? sv.vbH : sv.height,
            BaseDir = sv.baseDir,
        };

        CollectDefsAndCss(sv.svgRoot, sv.ctx);

        sv.sb = sv.ctx.Surface.Sb;
        // PDF coordinate system: origin at bottom-left, Y up
        // SVG coordinate system: origin at top-left, Y down
        // Transform: translate(0, height) then scale(1, -1)
        sv.sb.Append($"q 1 0 0 -1 {F(sv.offX)} {F(sv.pageH - sv.offY)} cm\n");
        sv.ctm = new[] { 1.0, 0, 0, -1, sv.offX, sv.pageH - sv.offY };

        // Map the viewBox user-space onto the page: scale (page/viewBox) and offset the
        // viewBox origin to (0,0). Without this, content authored in viewBox coordinates
        // (e.g. a 2291x1666 window shown on an 1100x800 page) is drawn unscaled/off-page.
        ApplySvgViewBoxTransform(sv);

        sv.rootStyle = new Dictionary<string, string>(InitialStyle, StringComparer.Ordinal);
        RenderChildren(sv.svgRoot, sv.ctx, sv.rootStyle, sv.ctm);

        sv.sb.Append("Q\n");

        // Merge accumulated resources (fonts/patterns/ExtGStates/XObjects) into the page.
        MergeResources(sv.page, sv.ctx.Surface.Resources);

        // Set the content stream. Latin1 keeps escaped string bytes 1:1 with the
        // WinAnsi-ish characters EmitRun produced (UTF-8 would double-encode >0x7F).
        sv.page.SetContentStream(Compat.Latin1.GetBytes(sv.sb.ToString()));

        return sv.doc;
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
        AppendShapeGeometry(ctx, elem, sb, bb, vertices);
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
                var op = Compat.Clamp(ParseOpacity(opStr), 0, 1);
                r = r * op + (1 - op);
                g = g * op + (1 - op);
                b = b * op + (1 - op);
            }
            stops.Add((Compat.Clamp(off, 0, 1), r, g, b));
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
        return NamedColors.TryGetValue(color.ToLowerInvariant(), out var named) ? named : (0, 0, 0); // default black
    }

}
