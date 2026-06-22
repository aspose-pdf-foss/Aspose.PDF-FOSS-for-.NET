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
/// Supports basic SVG elements: rect, circle, ellipse, line, polyline, polygon, path, text, image, g.
/// </summary>
internal static class SvgToPdfConverter
{
    public static Document Convert(byte[] svgData, SvgLoadOptions? options = null)
    {
        var xml = LoadSvgXml(Encoding.UTF8.GetString(svgData));
        return ConvertFromXml(xml, options);
    }

    /// <summary>
    /// Load SVG content as XML, handling malformed SVG gracefully.
    /// </summary>
    private static XmlDocument LoadSvgXml(string svgText)
    {
        var xml = new XmlDocument();

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

        // Second attempt: strip unrecognized entities
        var cleaned = Regex.Replace(svgText, @"&(?!amp;|lt;|gt;|quot;|apos;|#)\w+;", "");
        try
        {
            xml.LoadXml(cleaned);
            return xml;
        }
        catch (XmlException) { }

        // Third attempt: strip self-closing HTML tags (link, meta, br, hr, img, input)
        cleaned = Regex.Replace(cleaned, @"<(link|meta|br|hr|img|input)\b[^>]*/?>", "", RegexOptions.IgnoreCase);
        // Remove mismatched close tags
        cleaned = Regex.Replace(cleaned, @"</(?:link|meta|br|hr|img|input)\s*>", "", RegexOptions.IgnoreCase);
        try
        {
            xml.LoadXml(cleaned);
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

    public static Document Convert(string svgPath, SvgLoadOptions? options = null)
    {
        var xml = LoadSvgXml(File.ReadAllText(svgPath));
        return ConvertFromXml(xml, options);
    }

    private static Document ConvertFromXml(XmlDocument xml, SvgLoadOptions? options)
    {
        var svgRoot = xml.DocumentElement;
        if (svgRoot is null)
            throw new InvalidOperationException("SVG document has no root element");

        // Parse SVG dimensions
        double width = 612, height = 792; // default US Letter
        var widthAttr = svgRoot.GetAttribute("width");
        var heightAttr = svgRoot.GetAttribute("height");
        var viewBox = svgRoot.GetAttribute("viewBox");

        if (!string.IsNullOrEmpty(viewBox))
        {
            var parts = viewBox.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 4)
            {
                width = ParseLength(parts[2]);
                height = ParseLength(parts[3]);
            }
        }

        if (!string.IsNullOrEmpty(widthAttr)) width = ParseLength(widthAttr);
        if (!string.IsNullOrEmpty(heightAttr)) height = ParseLength(heightAttr);

        // Clamp to reasonable page size
        if (width < 1) width = 612;
        if (height < 1) height = 792;

        // Create PDF document with one page matching SVG dimensions
        var doc = Document.Create();
        var page = doc.Pages.Add(width, height);

        // Build content stream from SVG elements
        var sb = new StringBuilder();
        // PDF coordinate system: origin at bottom-left, Y up
        // SVG coordinate system: origin at top-left, Y down
        // Transform: translate(0, height) then scale(1, -1)
        sb.Append($"q 1 0 0 -1 0 {F(height)} cm\n");

        RenderNode(svgRoot, sb, page);

        sb.Append("Q\n");

        // Set the content stream
        page.SetContentStream(Encoding.ASCII.GetBytes(sb.ToString()));

        return doc;
    }

    private static void RenderNode(XmlNode node, StringBuilder sb, Page page)
    {
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            var elem = (XmlElement)child;

            switch (child.LocalName)
            {
                case "g":
                    sb.Append("q\n");
                    ApplyTransform(elem, sb);
                    ApplyStyle(elem, sb);
                    RenderNode(child, sb, page);
                    sb.Append("Q\n");
                    break;
                case "rect":
                    RenderRect(elem, sb);
                    break;
                case "circle":
                    RenderCircle(elem, sb);
                    break;
                case "ellipse":
                    RenderEllipse(elem, sb);
                    break;
                case "line":
                    RenderLine(elem, sb);
                    break;
                case "polyline":
                    RenderPolyline(elem, sb, close: false);
                    break;
                case "polygon":
                    RenderPolyline(elem, sb, close: true);
                    break;
                case "path":
                    RenderPath(elem, sb);
                    break;
                case "text":
                    RenderText(elem, sb, page);
                    break;
                case "use":
                    // Basic <use> support — render referenced element
                    RenderNode(child, sb, page);
                    break;
                case "svg":
                    // Nested SVG
                    RenderNode(child, sb, page);
                    break;
                default:
                    // Try to render children for unknown containers
                    if (child.HasChildNodes)
                        RenderNode(child, sb, page);
                    break;
            }
        }
    }

    private static void RenderRect(XmlElement elem, StringBuilder sb)
    {
        var x = GetNum(elem, "x");
        var y = GetNum(elem, "y");
        var w = GetNum(elem, "width");
        var h = GetNum(elem, "height");
        if (w <= 0 || h <= 0) return;

        sb.Append("q\n");
        ApplyStyle(elem, sb);
        sb.Append($"{F(x)} {F(y)} {F(w)} {F(h)} re ");
        PaintOp(elem, sb);
        sb.Append("Q\n");
    }

    private static void RenderCircle(XmlElement elem, StringBuilder sb)
    {
        var cx = GetNum(elem, "cx");
        var cy = GetNum(elem, "cy");
        var r = GetNum(elem, "r");
        if (r <= 0) return;

        sb.Append("q\n");
        ApplyStyle(elem, sb);
        AppendEllipsePath(sb, cx, cy, r, r);
        PaintOp(elem, sb);
        sb.Append("Q\n");
    }

    private static void RenderEllipse(XmlElement elem, StringBuilder sb)
    {
        var cx = GetNum(elem, "cx");
        var cy = GetNum(elem, "cy");
        var rx = GetNum(elem, "rx");
        var ry = GetNum(elem, "ry");
        if (rx <= 0 || ry <= 0) return;

        sb.Append("q\n");
        ApplyStyle(elem, sb);
        AppendEllipsePath(sb, cx, cy, rx, ry);
        PaintOp(elem, sb);
        sb.Append("Q\n");
    }

    private static void RenderLine(XmlElement elem, StringBuilder sb)
    {
        var x1 = GetNum(elem, "x1");
        var y1 = GetNum(elem, "y1");
        var x2 = GetNum(elem, "x2");
        var y2 = GetNum(elem, "y2");

        sb.Append("q\n");
        ApplyStyle(elem, sb);
        sb.Append($"{F(x1)} {F(y1)} m {F(x2)} {F(y2)} l S\n");
        sb.Append("Q\n");
    }

    private static void RenderPolyline(XmlElement elem, StringBuilder sb, bool close)
    {
        var points = elem.GetAttribute("points");
        if (string.IsNullOrEmpty(points)) return;

        var nums = Regex.Matches(points, @"-?[\d.]+").Cast<Match>()
            .Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture)).ToArray();
        if (nums.Length < 4) return;

        sb.Append("q\n");
        ApplyStyle(elem, sb);
        sb.Append($"{F(nums[0])} {F(nums[1])} m ");
        for (int i = 2; i < nums.Length - 1; i += 2)
            sb.Append($"{F(nums[i])} {F(nums[i + 1])} l ");
        if (close) sb.Append("h ");
        PaintOp(elem, sb);
        sb.Append("Q\n");
    }

    private static void RenderPath(XmlElement elem, StringBuilder sb)
    {
        var d = elem.GetAttribute("d");
        if (string.IsNullOrEmpty(d)) return;

        sb.Append("q\n");
        ApplyStyle(elem, sb);
        ConvertSvgPathToPdf(d, sb);
        PaintOp(elem, sb);
        sb.Append("Q\n");
    }

    private static void RenderText(XmlElement elem, StringBuilder sb, Page page)
    {
        var x = GetNum(elem, "x");
        var y = GetNum(elem, "y");
        var text = elem.InnerText.Trim();
        if (string.IsNullOrEmpty(text)) return;

        var fontSize = 12.0;
        var style = elem.GetAttribute("style");
        var fontSizeAttr = elem.GetAttribute("font-size");
        if (!string.IsNullOrEmpty(fontSizeAttr))
            fontSize = ParseLength(fontSizeAttr);
        if (!string.IsNullOrEmpty(style))
        {
            var m = Regex.Match(style, @"font-size:\s*([\d.]+)");
            if (m.Success) fontSize = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        // Ensure font resource exists
        var fontResName = EnsureFontResource(page, "Helvetica");

        sb.Append("q\n");
        ApplyFillColor(elem, sb);
        sb.Append($"BT /{fontResName} {F(fontSize)} Tf {F(x)} {F(y)} Td ");

        // Escape text for PDF string
        var escaped = text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        sb.Append($"({escaped}) Tj ET\n");
        sb.Append("Q\n");
    }

    // ── SVG path → PDF path conversion ──────────────────────────────

    private static void ConvertSvgPathToPdf(string d, StringBuilder sb)
    {
        // SVG and PDF path commands are very similar
        // SVG: M/m, L/l, H/h, V/v, C/c, S/s, Q/q, T/t, A/a, Z/z
        // PDF: m, l, c, h (close), re
        // We need to convert SVG commands to PDF equivalents

        var tokens = Regex.Matches(d, @"[MmLlHhVvCcSsQqTtAaZz]|-?[\d.]+(?:[eE][+-]?\d+)?");
        double cx = 0, cy = 0; // current point
        double sx = 0, sy = 0; // subpath start

        var nums = new List<double>();
        char cmd = 'M';

        foreach (Match token in tokens)
        {
            var val = token.Value;
            if (val.Length == 1 && char.IsLetter(val[0]))
            {
                cmd = val[0];
                nums.Clear();
                if (cmd is 'Z' or 'z')
                {
                    sb.Append("h ");
                    cx = sx; cy = sy;
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
                    nums.Clear(); cmd = 'L'; // subsequent coords are lineto
                    break;
                case 'm' when nums.Count >= 2:
                    cx += nums[0]; cy += nums[1];
                    sx = cx; sy = cy;
                    sb.Append($"{F(cx)} {F(cy)} m ");
                    nums.Clear(); cmd = 'l';
                    break;
                case 'L' when nums.Count >= 2:
                    cx = nums[0]; cy = nums[1];
                    sb.Append($"{F(cx)} {F(cy)} l ");
                    nums.Clear();
                    break;
                case 'l' when nums.Count >= 2:
                    cx += nums[0]; cy += nums[1];
                    sb.Append($"{F(cx)} {F(cy)} l ");
                    nums.Clear();
                    break;
                case 'H' when nums.Count >= 1:
                    cx = nums[0];
                    sb.Append($"{F(cx)} {F(cy)} l ");
                    nums.Clear();
                    break;
                case 'h' when nums.Count >= 1:
                    cx += nums[0];
                    sb.Append($"{F(cx)} {F(cy)} l ");
                    nums.Clear();
                    break;
                case 'V' when nums.Count >= 1:
                    cy = nums[0];
                    sb.Append($"{F(cx)} {F(cy)} l ");
                    nums.Clear();
                    break;
                case 'v' when nums.Count >= 1:
                    cy += nums[0];
                    sb.Append($"{F(cx)} {F(cy)} l ");
                    nums.Clear();
                    break;
                case 'C' when nums.Count >= 6:
                    sb.Append($"{F(nums[0])} {F(nums[1])} {F(nums[2])} {F(nums[3])} {F(nums[4])} {F(nums[5])} c ");
                    cx = nums[4]; cy = nums[5];
                    nums.Clear();
                    break;
                case 'c' when nums.Count >= 6:
                    sb.Append($"{F(cx + nums[0])} {F(cy + nums[1])} {F(cx + nums[2])} {F(cy + nums[3])} {F(cx + nums[4])} {F(cy + nums[5])} c ");
                    cx += nums[4]; cy += nums[5];
                    nums.Clear();
                    break;
                case 'Q' when nums.Count >= 4:
                    // Convert quadratic to cubic
                    var qx1 = cx + 2.0 / 3.0 * (nums[0] - cx);
                    var qy1 = cy + 2.0 / 3.0 * (nums[1] - cy);
                    var qx2 = nums[2] + 2.0 / 3.0 * (nums[0] - nums[2]);
                    var qy2 = nums[3] + 2.0 / 3.0 * (nums[1] - nums[3]);
                    sb.Append($"{F(qx1)} {F(qy1)} {F(qx2)} {F(qy2)} {F(nums[2])} {F(nums[3])} c ");
                    cx = nums[2]; cy = nums[3];
                    nums.Clear();
                    break;
                case 'q' when nums.Count >= 4:
                    var rqx1 = cx + 2.0 / 3.0 * nums[0];
                    var rqy1 = cy + 2.0 / 3.0 * nums[1];
                    var endX = cx + nums[2]; var endY = cy + nums[3];
                    var rqx2 = endX + 2.0 / 3.0 * (cx + nums[0] - endX);
                    var rqy2 = endY + 2.0 / 3.0 * (cy + nums[1] - endY);
                    sb.Append($"{F(rqx1)} {F(rqy1)} {F(rqx2)} {F(rqy2)} {F(endX)} {F(endY)} c ");
                    cx = endX; cy = endY;
                    nums.Clear();
                    break;
                case 'A' or 'a':
                    // Arc — approximate with line for now (complex conversion)
                    if (nums.Count >= 7)
                    {
                        var ax = cmd == 'A' ? nums[5] : cx + nums[5];
                        var ay = cmd == 'A' ? nums[6] : cy + nums[6];
                        sb.Append($"{F(ax)} {F(ay)} l ");
                        cx = ax; cy = ay;
                        nums.Clear();
                    }
                    break;
            }
        }
    }

    // ── Style handling ──────────────────────────────────────────────

    private static void ApplyStyle(XmlElement elem, StringBuilder sb)
    {
        ApplyFillColor(elem, sb);
        ApplyStrokeColor(elem, sb);

        var strokeWidth = elem.GetAttribute("stroke-width");
        if (!string.IsNullOrEmpty(strokeWidth))
            sb.Append($"{F(ParseLength(strokeWidth))} w ");
    }

    private static void ApplyFillColor(XmlElement elem, StringBuilder sb)
    {
        var fill = GetStyleProp(elem, "fill");
        if (!string.IsNullOrEmpty(fill) && fill != "none")
        {
            var (r, g, b) = ParseColor(fill);
            sb.Append($"{F(r)} {F(g)} {F(b)} rg ");
        }
    }

    private static void ApplyStrokeColor(XmlElement elem, StringBuilder sb)
    {
        var stroke = GetStyleProp(elem, "stroke");
        if (!string.IsNullOrEmpty(stroke) && stroke != "none")
        {
            var (r, g, b) = ParseColor(stroke);
            sb.Append($"{F(r)} {F(g)} {F(b)} RG ");
        }
    }

    private static void ApplyTransform(XmlElement elem, StringBuilder sb)
    {
        var transform = elem.GetAttribute("transform");
        if (string.IsNullOrEmpty(transform)) return;

        // Handle matrix(a,b,c,d,e,f)
        var m = Regex.Match(transform, @"matrix\s*\(\s*([\d.e+-]+)[\s,]+([\d.e+-]+)[\s,]+([\d.e+-]+)[\s,]+([\d.e+-]+)[\s,]+([\d.e+-]+)[\s,]+([\d.e+-]+)\s*\)");
        if (m.Success)
        {
            sb.Append($"{m.Groups[1].Value} {m.Groups[2].Value} {m.Groups[3].Value} {m.Groups[4].Value} {m.Groups[5].Value} {m.Groups[6].Value} cm\n");
            return;
        }

        // Handle translate(x,y)
        m = Regex.Match(transform, @"translate\s*\(\s*([\d.e+-]+)[\s,]*([\d.e+-]*)\s*\)");
        if (m.Success)
        {
            var tx = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var ty = m.Groups[2].Length > 0 ? double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
            sb.Append($"1 0 0 1 {F(tx)} {F(ty)} cm\n");
            return;
        }

        // Handle scale(sx,sy)
        m = Regex.Match(transform, @"scale\s*\(\s*([\d.e+-]+)[\s,]*([\d.e+-]*)\s*\)");
        if (m.Success)
        {
            var sxx = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var syy = m.Groups[2].Length > 0 ? double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : sxx;
            sb.Append($"{F(sxx)} 0 0 {F(syy)} 0 0 cm\n");
        }
    }

    private static void PaintOp(XmlElement elem, StringBuilder sb)
    {
        var fill = GetStyleProp(elem, "fill");
        var stroke = GetStyleProp(elem, "stroke");

        bool hasFill = fill != "none" && !string.IsNullOrEmpty(fill);
        bool hasStroke = !string.IsNullOrEmpty(stroke) && stroke != "none";

        // Default: SVG elements are filled (black) unless fill="none"
        if (string.IsNullOrEmpty(fill)) hasFill = true;

        if (hasFill && hasStroke) sb.Append("B\n");
        else if (hasStroke) sb.Append("S\n");
        else if (hasFill) sb.Append("f\n");
        else sb.Append("n\n");
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
        sb.Append($"{F(cx - kx)} {F(cy + ry)} {F(cx - rx)} {F(cy + ky)} {F(cx - rx)} {F(cy)} c ");
    }

    private static string GetStyleProp(XmlElement elem, string prop)
    {
        // Check attribute first
        var val = elem.GetAttribute(prop);
        if (!string.IsNullOrEmpty(val)) return val;

        // Check inline style
        var style = elem.GetAttribute("style");
        if (string.IsNullOrEmpty(style)) return "";

        var m = Regex.Match(style, prop + @"\s*:\s*([^;]+)");
        return m.Success ? m.Groups[1].Value.Trim() : "";
    }

    private static (double r, double g, double b) ParseColor(string color)
    {
        if (color.StartsWith('#'))
        {
            if (color.Length == 7)
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

        var rgbMatch = Regex.Match(color, @"rgb\s*\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)");
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
            "orange" => (1, 0.647, 0),
            "purple" => (0.502, 0, 0.502),
            "navy" => (0, 0, 0.502),
            _ => (0, 0, 0), // default black
        };
    }

    private static double GetNum(XmlElement elem, string attr)
    {
        var val = elem.GetAttribute(attr);
        if (string.IsNullOrEmpty(val)) return 0;
        return ParseLength(val);
    }

    private static double ParseLength(string val)
    {
        val = val.Trim();
        // Strip units
        val = Regex.Replace(val, @"(px|pt|em|ex|cm|mm|in|%)$", "");
        if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
            return result;
        return 0;
    }

    private static string F(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

    private static string EnsureFontResource(Page page, string baseFontName)
    {
        var resources = page.Dict.Get("Resources") as PdfDictionary;
        if (resources is null)
        {
            resources = new PdfDictionary();
            page.Dict.Set("Resources", resources);
        }
        var fontDict = resources.Get("Font") as PdfDictionary;
        if (fontDict is null)
        {
            fontDict = new PdfDictionary();
            resources.Set("Font", fontDict);
        }
        var name = "F1";
        if (!fontDict.ContainsKey(name))
        {
            var font = new PdfDictionary();
            font.Set("Type", new PdfName("Font"));
            font.Set("Subtype", new PdfName("Type1"));
            font.Set("BaseFont", new PdfName(baseFontName));
            font.Set("Encoding", new PdfName("WinAnsiEncoding"));
            fontDict.Set(name, font);
        }
        return name;
    }
}
