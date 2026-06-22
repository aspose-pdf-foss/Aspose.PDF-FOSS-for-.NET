using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Converters;

/// <summary>
/// Converts PDF pages to HTML markup.
/// Text fragments are positioned with absolute CSS positioning.
/// Supports images (base64 data URIs), link annotations, ToUnicode CMap decoding,
/// and vector path rendering as inline SVG.
/// </summary>
public sealed class PdfToHtmlConverter
{
    /// <summary>
    /// Convert all pages to a single HTML document.
    /// </summary>
    public string SaveAsHtml(Document doc)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html><head><meta charset=\"utf-8\">");
        sb.AppendLine("<style>");
        sb.AppendLine("  .pdf-page { position: relative; margin: 10px auto; border: 1px solid #ccc; overflow: hidden; }");
        sb.AppendLine("  .pdf-text { position: absolute; white-space: pre; }");
        sb.AppendLine("  .pdf-image { position: absolute; }");
        sb.AppendLine("  .pdf-link { position: absolute; }");
        sb.AppendLine("  .pdf-svg { position: absolute; top: 0; left: 0; }");
        sb.AppendLine("</style></head><body>");

        for (var i = 1; i <= doc.PageCount; i++)
        {
            var page = doc.Pages[i];
            sb.Append(RenderPage(page, i));
        }

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    /// <summary>
    /// Convert each page to a separate HTML fragment and return as an array.
    /// </summary>
    public string[] SaveAllPagesAsHtml(Document doc)
    {
        var results = new string[doc.PageCount];
        for (var i = 1; i <= doc.PageCount; i++)
        {
            var page = doc.Pages[i];
            results[i - 1] = RenderPage(page, i);
        }
        return results;
    }

    /// <summary>
    /// Convert a single page to an HTML fragment (a &lt;div&gt; with positioned text).
    /// </summary>
    public string SavePageAsHtml(Document doc, int pageNumber)
    {
        var page = doc.Pages.At(pageNumber);
        return RenderPage(page, pageNumber);
    }

    /// <summary>
    /// Convert all pages to HTML and write to a stream.
    /// </summary>
    public void SaveAsHtml(Document doc, Stream output)
    {
        var html = SaveAsHtml(doc);
        output.Write(Encoding.UTF8.GetBytes(html));
    }

    private static string RenderPage(Page page, int pageNumber)
    {
        var mb = page.MediaBox;
        var width = page.Width;
        var height = page.Height;

        var sb = new StringBuilder();
        sb.AppendLine($"<div class=\"pdf-page\" data-page=\"{pageNumber}\" " +
            $"style=\"width:{F(width)}pt;height:{F(height)}pt;\">");

        var reader = page.Reader;
        var contentStreams = GetContentStreams(page.Dict, reader);
        var fonts = ResolveFonts(page.Dict, reader);
        var imageXObjects = ResolveImageXObjects(page.Dict, reader);

        foreach (var stream in contentStreams)
        {
            RenderContentToHtml(stream, fonts, imageXObjects, reader, sb, height, width);
        }

        // Render link annotations
        RenderLinkAnnotations(page.Dict, reader, sb, height);

        sb.AppendLine("</div>");
        return sb.ToString();
    }

    // ── CTM tracking ────────────────────────────────────────────────────

    private sealed class CtmState
    {
        public double A = 1, B, C, D = 1, E, F;

        public CtmState Clone() => new()
        {
            A = A, B = B, C = C, D = D, E = E, F = F
        };

        /// <summary>
        /// Multiply this CTM by a new matrix: this = new * this
        /// (PDF spec: the new matrix pre-multiplies the current CTM)
        /// </summary>
        public void Concat(double a, double b, double c, double d, double e, double f)
        {
            var na = a * A + b * C;
            var nb = a * B + b * D;
            var nc = c * A + d * C;
            var nd = c * B + d * D;
            var ne = e * A + f * C + E;
            var nf = e * B + f * D + F;
            A = na; B = nb; C = nc; D = nd; E = ne; F = nf;
        }
    }

    // ── Path tracking ───────────────────────────────────────────────────

    private sealed class PathState
    {
        public readonly StringBuilder Data = new();
        public double StrokeR, StrokeG, StrokeB;
        public double FillR, FillG, FillB;
        public double LineWidth = 1.0;

        public void Clear() => Data.Clear();
    }

    private static void RenderContentToHtml(byte[] streamBytes,
        Dictionary<string, FontInfo> fonts,
        Dictionary<string, ImageXObject> imageXObjects,
        PdfReader reader,
        StringBuilder sb, double pageHeight, double pageWidth)
    {
        var lexer = new PdfLexer(streamBytes);
        var operands = new List<PdfObject>();

        // Text state
        double tx = 0, ty = 0;
        double fontSize = 12;
        string fontFamily = "sans-serif";
        string fontWeight = "normal";
        string fontStyle = "normal";
        double r = 0, g = 0, b = 0;
        string? currentFontKey = null;

        // CTM state
        var ctm = new CtmState();
        var ctmStack = new Stack<CtmState>();

        // Path state
        var pathState = new PathState();
        var svgPaths = new StringBuilder();

        while (true)
        {
            var token = lexer.NextToken();
            if (token.Kind == TokenKind.Eof) break;

            switch (token.Kind)
            {
                case TokenKind.Integer: operands.Add(new PdfInteger(token.IntValue)); break;
                case TokenKind.Real: operands.Add(new PdfReal(token.RealValue)); break;
                case TokenKind.LiteralString: operands.Add(new PdfString(token.BytesValue!)); break;
                case TokenKind.HexString: operands.Add(new PdfString(token.BytesValue!, isHex: true)); break;
                case TokenKind.Name: operands.Add(new PdfName(token.StringValue!)); break;
                case TokenKind.ArrayStart:
                    operands.Add(ParseArray(lexer));
                    break;
                case TokenKind.Keyword:
                {
                    var op = token.StringValue!;
                    switch (op)
                    {
                        // ── Graphics state stack ──
                        case "q":
                            ctmStack.Push(ctm.Clone());
                            break;
                        case "Q":
                            if (ctmStack.Count > 0)
                                ctm = ctmStack.Pop();
                            break;

                        // ── CTM ──
                        case "cm":
                            if (operands.Count >= 6)
                            {
                                ctm.Concat(
                                    Num(operands[0]), Num(operands[1]),
                                    Num(operands[2]), Num(operands[3]),
                                    Num(operands[4]), Num(operands[5]));
                            }
                            break;

                        // ── Text state ──
                        case "BT":
                            tx = 0; ty = 0;
                            break;
                        case "ET":
                            break;
                        case "Tf":
                            if (operands.Count >= 2)
                            {
                                currentFontKey = (operands[0] as PdfName)?.Value;
                                fontSize = Num(operands[1]);
                                if (currentFontKey is not null && fonts.TryGetValue(currentFontKey, out var fi))
                                {
                                    fontFamily = fi.Family;
                                    fontWeight = fi.Weight;
                                    fontStyle = fi.Style;
                                }
                            }
                            break;
                        case "Td" or "TD":
                            if (operands.Count >= 2)
                            { tx += Num(operands[0]); ty += Num(operands[1]); }
                            break;
                        case "Tm":
                            if (operands.Count >= 6)
                            { tx = Num(operands[4]); ty = Num(operands[5]); }
                            break;
                        case "T*":
                            ty -= fontSize * 1.2; // Approximate leading
                            tx = 0;
                            break;

                        // ── Color ──
                        case "rg":
                            if (operands.Count >= 3)
                            {
                                r = Num(operands[0]); g = Num(operands[1]); b = Num(operands[2]);
                                pathState.FillR = r; pathState.FillG = g; pathState.FillB = b;
                            }
                            break;
                        case "RG":
                            if (operands.Count >= 3)
                            {
                                pathState.StrokeR = Num(operands[0]);
                                pathState.StrokeG = Num(operands[1]);
                                pathState.StrokeB = Num(operands[2]);
                            }
                            break;
                        case "g":
                            if (operands.Count >= 1)
                            {
                                var gray = Num(operands[0]);
                                r = g = b = gray;
                                pathState.FillR = pathState.FillG = pathState.FillB = gray;
                            }
                            break;
                        case "G":
                            if (operands.Count >= 1)
                            {
                                var gray = Num(operands[0]);
                                pathState.StrokeR = pathState.StrokeG = pathState.StrokeB = gray;
                            }
                            break;

                        // ── Line width ──
                        case "w":
                            if (operands.Count >= 1)
                                pathState.LineWidth = Num(operands[0]);
                            break;

                        // ── Text showing ──
                        case "Tj":
                            if (operands.Count >= 1 && operands[0] is PdfString s)
                            {
                                var text = DecodeString(s, currentFontKey, fonts);
                                EmitSpan(sb, text, tx, ty, fontSize, fontFamily,
                                    fontWeight, fontStyle, r, g, b, pageHeight);
                            }
                            break;
                        case "TJ":
                            if (operands.Count >= 1 && operands[0] is PdfArray arr)
                            {
                                var tjText = new StringBuilder();
                                foreach (var item in arr)
                                {
                                    if (item is PdfString ts)
                                        tjText.Append(DecodeString(ts, currentFontKey, fonts));
                                    else if (item is PdfInteger ti && ti.Value < -100)
                                        tjText.Append(' ');
                                    else if (item is PdfReal tr && tr.Value < -100)
                                        tjText.Append(' ');
                                }
                                if (tjText.Length > 0)
                                {
                                    EmitSpan(sb, tjText.ToString(), tx, ty, fontSize,
                                        fontFamily, fontWeight, fontStyle, r, g, b, pageHeight);
                                }
                            }
                            break;
                        case "'":
                            // Move to next line and show string
                            ty -= fontSize * 1.2;
                            if (operands.Count >= 1 && operands[0] is PdfString qs)
                            {
                                var text = DecodeString(qs, currentFontKey, fonts);
                                EmitSpan(sb, text, tx, ty, fontSize, fontFamily,
                                    fontWeight, fontStyle, r, g, b, pageHeight);
                            }
                            break;

                        // ── Image XObject (Do operator) ──
                        case "Do":
                            if (operands.Count >= 1 && operands[0] is PdfName xobjName)
                            {
                                if (imageXObjects.TryGetValue(xobjName.Value, out var img))
                                {
                                    EmitImage(sb, img, ctm, pageHeight);
                                }
                            }
                            break;

                        // ── Path construction ──
                        case "m": // moveto
                            if (operands.Count >= 2)
                                pathState.Data.Append($"M{F(Num(operands[0]))} {F(Num(operands[1]))} ");
                            break;
                        case "l": // lineto
                            if (operands.Count >= 2)
                                pathState.Data.Append($"L{F(Num(operands[0]))} {F(Num(operands[1]))} ");
                            break;
                        case "c": // curveto
                            if (operands.Count >= 6)
                                pathState.Data.Append($"C{F(Num(operands[0]))} {F(Num(operands[1]))} {F(Num(operands[2]))} {F(Num(operands[3]))} {F(Num(operands[4]))} {F(Num(operands[5]))} ");
                            break;
                        case "v": // curveto (initial point replicated)
                            if (operands.Count >= 4)
                                pathState.Data.Append($"C{F(tx)} {F(ty)} {F(Num(operands[0]))} {F(Num(operands[1]))} {F(Num(operands[2]))} {F(Num(operands[3]))} ");
                            break;
                        case "y": // curveto (final point replicated)
                            if (operands.Count >= 4)
                                pathState.Data.Append($"C{F(Num(operands[0]))} {F(Num(operands[1]))} {F(Num(operands[2]))} {F(Num(operands[3]))} {F(Num(operands[2]))} {F(Num(operands[3]))} ");
                            break;
                        case "h": // closepath
                            pathState.Data.Append("Z ");
                            break;
                        case "re": // rectangle
                            if (operands.Count >= 4)
                            {
                                var rx = Num(operands[0]); var ry = Num(operands[1]);
                                var rw = Num(operands[2]); var rh = Num(operands[3]);
                                pathState.Data.Append($"M{F(rx)} {F(ry)} L{F(rx + rw)} {F(ry)} L{F(rx + rw)} {F(ry + rh)} L{F(rx)} {F(ry + rh)} Z ");
                            }
                            break;

                        // ── Path painting ──
                        case "S": // stroke
                            if (pathState.Data.Length > 0)
                            {
                                EmitSvgPath(svgPaths, pathState, stroke: true, fill: false);
                                pathState.Clear();
                            }
                            break;
                        case "s": // close and stroke
                            pathState.Data.Append("Z ");
                            if (pathState.Data.Length > 0)
                            {
                                EmitSvgPath(svgPaths, pathState, stroke: true, fill: false);
                                pathState.Clear();
                            }
                            break;
                        case "f" or "F": // fill (nonzero)
                            if (pathState.Data.Length > 0)
                            {
                                EmitSvgPath(svgPaths, pathState, stroke: false, fill: true);
                                pathState.Clear();
                            }
                            break;
                        case "f*": // fill (even-odd)
                            if (pathState.Data.Length > 0)
                            {
                                EmitSvgPath(svgPaths, pathState, stroke: false, fill: true, evenOdd: true);
                                pathState.Clear();
                            }
                            break;
                        case "B": // fill and stroke (nonzero)
                            if (pathState.Data.Length > 0)
                            {
                                EmitSvgPath(svgPaths, pathState, stroke: true, fill: true);
                                pathState.Clear();
                            }
                            break;
                        case "B*": // fill and stroke (even-odd)
                            if (pathState.Data.Length > 0)
                            {
                                EmitSvgPath(svgPaths, pathState, stroke: true, fill: true, evenOdd: true);
                                pathState.Clear();
                            }
                            break;
                        case "b": // close, fill and stroke (nonzero)
                            pathState.Data.Append("Z ");
                            if (pathState.Data.Length > 0)
                            {
                                EmitSvgPath(svgPaths, pathState, stroke: true, fill: true);
                                pathState.Clear();
                            }
                            break;
                        case "b*": // close, fill and stroke (even-odd)
                            pathState.Data.Append("Z ");
                            if (pathState.Data.Length > 0)
                            {
                                EmitSvgPath(svgPaths, pathState, stroke: true, fill: true, evenOdd: true);
                                pathState.Clear();
                            }
                            break;
                        case "n": // end path (no paint)
                            pathState.Clear();
                            break;
                        case "BI":
                            SkipInlineImage(lexer);
                            operands.Clear();
                            continue;
                    }
                    operands.Clear();
                    break;
                }
                default:
                    operands.Clear();
                    break;
            }
        }

        // Emit collected SVG paths as a single overlay
        if (svgPaths.Length > 0)
        {
            sb.AppendLine($"<svg class=\"pdf-svg\" width=\"{F(pageWidth)}pt\" height=\"{F(pageHeight)}pt\" " +
                $"viewBox=\"0 0 {F(pageWidth)} {F(pageHeight)}\" xmlns=\"http://www.w3.org/2000/svg\">");
            // Flip Y: PDF origin is bottom-left, SVG is top-left
            sb.AppendLine($"<g transform=\"translate(0,{F(pageHeight)}) scale(1,-1)\">");
            sb.Append(svgPaths);
            sb.AppendLine("</g>");
            sb.AppendLine("</svg>");
        }
    }

    // ── Image rendering ─────────────────────────────────────────────────

    private static void EmitImage(StringBuilder sb, ImageXObject img,
        CtmState ctm, double pageHeight)
    {
        // The CTM after cm typically contains: [w 0 0 h x y]
        // where w=width, h=height, x=left, y=bottom
        var imgWidth = Math.Abs(ctm.A);
        var imgHeight = Math.Abs(ctm.D);
        var imgLeft = ctm.E;
        var imgBottom = ctm.F;

        // If width/height are 0 (no cm before Do), use image pixel dimensions
        if (imgWidth < 0.01) imgWidth = img.Width;
        if (imgHeight < 0.01) imgHeight = img.Height;

        // Convert PDF coords (bottom-left) to CSS (top-left)
        var cssTop = pageHeight - imgBottom - imgHeight;
        var cssLeft = imgLeft;

        // Build data URI
        string dataUri;
        if (img.IsJpeg)
        {
            var jpegBytes = img.GetRawData();
            dataUri = $"data:image/jpeg;base64,{Convert.ToBase64String(jpegBytes)}";
        }
        else
        {
            var pngBytes = img.ToPng();
            dataUri = $"data:image/png;base64,{Convert.ToBase64String(pngBytes)}";
        }

        sb.Append($"<img class=\"pdf-image\" src=\"{dataUri}\" ");
        sb.Append($"style=\"left:{F(cssLeft)}pt;top:{F(cssTop)}pt;");
        sb.Append($"width:{F(imgWidth)}pt;height:{F(imgHeight)}pt;\"");
        sb.AppendLine($" />");
    }

    // ── Link annotations ────────────────────────────────────────────────

    private static void RenderLinkAnnotations(PdfDictionary pageDict,
        PdfReader reader, StringBuilder sb, double pageHeight)
    {
        var annotsObj = reader.Resolve(pageDict.Get("Annots"));
        if (annotsObj is not PdfArray annots) return;

        foreach (var annotRef in annots)
        {
            var annotDict = reader.ResolveDict(annotRef);
            if (annotDict is null) continue;

            var subtype = annotDict.GetName("Subtype");
            if (subtype != "Link") continue;

            // Get the Rect [llx lly urx ury]
            var rectObj = reader.Resolve(annotDict.Get("Rect"));
            if (rectObj is not PdfArray rect || rect.Count < 4) continue;

            var llx = NumFromObj(rect[0]);
            var lly = NumFromObj(rect[1]);
            var urx = NumFromObj(rect[2]);
            var ury = NumFromObj(rect[3]);

            // Get the URI from the /A action dictionary
            string? uri = null;
            var actionDict = reader.ResolveDict(annotDict.Get("A"));
            if (actionDict is not null)
            {
                var sType = actionDict.GetName("S");
                if (sType == "URI")
                {
                    var uriObj = actionDict.Get("URI");
                    if (uriObj is PdfString uriStr)
                        uri = Encoding.Latin1.GetString(uriStr.Value);
                    else if (uriObj is PdfName uriName)
                        uri = uriName.Value;
                }
            }

            if (uri is null) continue;

            // Convert to CSS coordinates
            var cssLeft = llx;
            var cssTop = pageHeight - ury;
            var cssWidth = urx - llx;
            var cssHeight = ury - lly;

            sb.Append($"<a class=\"pdf-link\" href=\"{EscapeHtml(uri)}\" ");
            sb.Append($"style=\"left:{F(cssLeft)}pt;top:{F(cssTop)}pt;");
            sb.Append($"width:{F(cssWidth)}pt;height:{F(cssHeight)}pt;display:block;\"");
            sb.AppendLine("></a>");
        }
    }

    // ── SVG path emission ───────────────────────────────────────────────

    private static void EmitSvgPath(StringBuilder svgPaths, PathState ps,
        bool stroke, bool fill, bool evenOdd = false)
    {
        var d = ps.Data.ToString().Trim();
        if (string.IsNullOrEmpty(d)) return;

        var attrs = new StringBuilder();
        attrs.Append($"d=\"{d}\"");

        if (fill)
        {
            var fillColor = FormatRgb(ps.FillR, ps.FillG, ps.FillB);
            attrs.Append($" fill=\"{fillColor}\"");
            if (evenOdd) attrs.Append(" fill-rule=\"evenodd\"");
        }
        else
        {
            attrs.Append(" fill=\"none\"");
        }

        if (stroke)
        {
            var strokeColor = FormatRgb(ps.StrokeR, ps.StrokeG, ps.StrokeB);
            attrs.Append($" stroke=\"{strokeColor}\"");
            if (ps.LineWidth != 1.0)
                attrs.Append($" stroke-width=\"{F(ps.LineWidth)}\"");
        }
        else
        {
            attrs.Append(" stroke=\"none\"");
        }

        svgPaths.AppendLine($"<path {attrs} />");
    }

    private static string FormatRgb(double r, double g, double b) =>
        $"rgb({(int)(r * 255)},{(int)(g * 255)},{(int)(b * 255)})";

    // ── Text rendering ──────────────────────────────────────────────────

    private static void EmitSpan(StringBuilder sb, string text,
        double x, double y, double fontSize, string fontFamily,
        string fontWeight, string fontStyle,
        double r, double g, double b, double pageHeight)
    {
        if (string.IsNullOrEmpty(text)) return;

        // Convert PDF coordinates (bottom-left origin) to CSS (top-left origin)
        var cssTop = pageHeight - y - fontSize;
        var cssLeft = x;

        var color = $"rgb({(int)(r * 255)},{(int)(g * 255)},{(int)(b * 255)})";
        var escaped = EscapeHtml(text);

        sb.Append($"<span class=\"pdf-text\" style=\"left:{F(cssLeft)}pt;top:{F(cssTop)}pt;");
        sb.Append($"font-size:{F(fontSize)}pt;font-family:{fontFamily};");
        if (fontWeight != "normal") sb.Append($"font-weight:{fontWeight};");
        if (fontStyle != "normal") sb.Append($"font-style:{fontStyle};");
        sb.Append($"color:{color};");
        sb.AppendLine($"\">{escaped}</span>");
    }

    private static string DecodeString(PdfString s, string? fontKey,
        Dictionary<string, FontInfo> fonts)
    {
        string decoded;
        if (fontKey is not null && fonts.TryGetValue(fontKey, out var fi) && fi.ToUnicode is not null)
        {
            // Use ToUnicode CMap if available
            decoded = fi.ToUnicode(s.Value);
        }
        else
        {
            // Default: Latin1
            decoded = Encoding.Latin1.GetString(s.Value);
        }
        return NormalizeWhitespace(decoded);
    }

    /// <summary>
    /// Some PDFs map the inter-word space glyph to a C0 control character
    /// (most commonly a horizontal tab, U+0009) in their ToUnicode CMap.
    /// For displayed text these are word separators, so fold them to a normal
    /// space — matching the text content produced by the Aspose.PDF for .NET converter.
    /// </summary>
    private static string NormalizeWhitespace(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        char[]? buffer = null;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            // Fold C0 control characters (except line breaks) to a space.
            if (c < ' ' && c != '\n' && c != '\r')
            {
                buffer ??= text.ToCharArray();
                buffer[i] = ' ';
            }
        }
        return buffer is null ? text : new string(buffer);
    }

    // ── Infrastructure ──────────────────────────────────────────────────

    private sealed class FontInfo
    {
        public string Family { get; init; } = "sans-serif";
        public string Weight { get; init; } = "normal";
        public string Style { get; init; } = "normal";
        public Func<byte[], string>? ToUnicode { get; init; }
        public bool IsCidFont { get; init; }
    }

    private static Dictionary<string, FontInfo> ResolveFonts(PdfDictionary pageDict, PdfReader reader)
    {
        var result = new Dictionary<string, FontInfo>(StringComparer.Ordinal);
        var resources = reader.ResolveDict(pageDict.Get("Resources"));
        if (resources is null) return result;
        var fontDict = reader.ResolveDict(resources.Get("Font"));
        if (fontDict is null) return result;

        foreach (var key in fontDict.Keys)
        {
            var font = reader.ResolveDict(fontDict.Get(key));
            if (font is null) continue;

            var baseFont = font.GetName("BaseFont") ?? "sans-serif";
            var (family, weight, style) = MapFont(baseFont);

            // Parse ToUnicode CMap
            var toUnicodeMap = Text.TextAbsorber.ParseToUnicodeFromDict(font, reader);

            // Check if this is a CID font (Type0)
            var fontSubtype = font.GetName("Subtype");
            var isCid = fontSubtype == "Type0";

            Func<byte[], string>? toUnicodeFunc = null;
            if (toUnicodeMap is not null)
            {
                toUnicodeFunc = (byte[] bytes) => ApplyToUnicode(bytes, toUnicodeMap, isCid);
            }

            result[key] = new FontInfo
            {
                Family = family,
                Weight = weight,
                Style = style,
                ToUnicode = toUnicodeFunc,
                IsCidFont = isCid,
            };
        }
        return result;
    }

    /// <summary>
    /// Apply a ToUnicode CMap to raw string bytes.
    /// For CID fonts (Type0), character codes are 2 bytes each.
    /// For simple fonts, character codes are 1 byte each.
    /// </summary>
    private static string ApplyToUnicode(byte[] bytes, Dictionary<int, string> map, bool isCid)
    {
        var sb = new StringBuilder();

        if (isCid)
        {
            // 2-byte character codes
            for (var i = 0; i + 1 < bytes.Length; i += 2)
            {
                var code = (bytes[i] << 8) | bytes[i + 1];
                if (map.TryGetValue(code, out var unicode))
                    sb.Append(unicode);
                else
                    sb.Append('?');
            }
        }
        else
        {
            // 1-byte character codes
            foreach (var b in bytes)
            {
                if (map.TryGetValue(b, out var unicode))
                    sb.Append(unicode);
                else
                    sb.Append((char)b);
            }
        }

        return sb.ToString();
    }

    private static Dictionary<string, ImageXObject> ResolveImageXObjects(
        PdfDictionary pageDict, PdfReader reader)
    {
        var result = new Dictionary<string, ImageXObject>(StringComparer.Ordinal);
        var resources = reader.ResolveDict(pageDict.Get("Resources"));
        if (resources is null) return result;
        var xobjectDict = reader.ResolveDict(resources.Get("XObject"));
        if (xobjectDict is null) return result;

        foreach (var key in xobjectDict.Keys)
        {
            var obj = reader.ResolveStream(xobjectDict.Get(key));
            if (obj is not null && obj.Dict.GetName("Subtype") == "Image")
            {
                result[key] = new ImageXObject(key, obj, reader);
            }
        }

        return result;
    }

    private static (string family, string weight, string style) MapFont(string baseFont)
    {
        var name = baseFont;
        // Strip subset prefix (e.g., "ABCDEF+Helvetica")
        if (name.Length > 7 && name[6] == '+')
            name = name[7..];

        var family = name switch
        {
            var n when n.Contains("Helvetica") => "Helvetica, Arial, sans-serif",
            var n when n.Contains("Times") => "'Times New Roman', Times, serif",
            var n when n.Contains("Courier") => "'Courier New', Courier, monospace",
            var n when n.Contains("Symbol") => "Symbol, serif",
            var n when n.Contains("ZapfDingbats") => "ZapfDingbats, serif",
            _ => "sans-serif",
        };

        var weight = name switch
        {
            var n when n.Contains("Bold") => "bold",
            _ => "normal",
        };

        var style = name switch
        {
            var n when n.Contains("Italic") || n.Contains("Oblique") => "italic",
            _ => "normal",
        };

        return (family, weight, style);
    }

    private static List<byte[]> GetContentStreams(PdfDictionary pageDict, PdfReader reader)
    {
        var result = new List<byte[]>();
        var obj = reader.Resolve(pageDict.Get("Contents"));
        if (obj is PdfStream stream) result.Add(reader.DecodeStream(stream));
        else if (obj is PdfArray arr)
        {
            foreach (var item in arr)
            {
                var s = reader.ResolveStream(item);
                if (s is not null) result.Add(reader.DecodeStream(s));
            }
        }
        return result;
    }

    private static PdfArray ParseArray(PdfLexer lexer)
    {
        var arr = new PdfArray();
        while (true)
        {
            var t = lexer.NextToken();
            if (t.Kind == TokenKind.ArrayEnd || t.Kind == TokenKind.Eof) break;
            switch (t.Kind)
            {
                case TokenKind.Integer: arr.Add(new PdfInteger(t.IntValue)); break;
                case TokenKind.Real: arr.Add(new PdfReal(t.RealValue)); break;
                case TokenKind.LiteralString: arr.Add(new PdfString(t.BytesValue!)); break;
                case TokenKind.HexString: arr.Add(new PdfString(t.BytesValue!, isHex: true)); break;
                case TokenKind.Name: arr.Add(new PdfName(t.StringValue!)); break;
            }
        }
        return arr;
    }

    private static double Num(PdfObject obj) => obj switch
    {
        PdfInteger i => i.Value, PdfReal r => r.Value, _ => 0,
    };

    private static double NumFromObj(PdfObject obj) => obj switch
    {
        PdfInteger i => i.Value, PdfReal r => r.Value, _ => 0,
    };

    private static string F(double v) => v.ToString("G6", CultureInfo.InvariantCulture);

    private static void SkipInlineImage(PdfLexer lexer)
    {
        while (true)
        {
            var t = lexer.NextToken();
            if (t.Kind == TokenKind.Eof) return;
            if (t.Kind == TokenKind.Keyword && t.StringValue == "ID") break;
        }

        var pos = lexer.Position + 1;
        var len = lexer.Length;

        while (pos < len - 2)
        {
            var b = lexer.ByteAt(pos);
            if (b is 0x00 or 0x09 or 0x0A or 0x0C or 0x0D or 0x20 &&
                lexer.ByteAt(pos + 1) == (byte)'E' &&
                lexer.ByteAt(pos + 2) == (byte)'I')
            {
                var after = pos + 3;
                if (after >= len || lexer.ByteAt(after) is 0x00 or 0x09 or 0x0A or 0x0C or 0x0D or 0x20)
                {
                    lexer.Position = after;
                    return;
                }
            }
            pos++;
        }
        lexer.Position = len;
    }

    private static string EscapeHtml(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
            .Replace("\"", "&quot;");
}
