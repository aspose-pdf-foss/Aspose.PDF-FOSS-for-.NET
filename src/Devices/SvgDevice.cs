using System.Globalization;
using System.Text;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Devices;

/// <summary>
/// Converts a PDF page to SVG markup.
/// </summary>
public sealed class SvgDevice
{
    /// <summary>
    /// Convert a page to SVG string.
    /// </summary>
    public string Process(Page page)
    {
        var mb = page.MediaBox;
        var sb = new StringBuilder();
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" " +
            $"width=\"{F(mb.Width)}\" height=\"{F(mb.Height)}\" " +
            $"viewBox=\"{F(mb.LLX)} {F(mb.LLY)} {F(mb.Width)} {F(mb.Height)}\">");

        // Flip Y axis (PDF origin is bottom-left, SVG is top-left)
        sb.AppendLine($"<g transform=\"translate(0,{F(mb.Height)}) scale(1,-1)\">");

        var reader = page.Reader;
        var contentStreams = GetContentStreams(page.Dict, reader);
        var fonts = ResolveFonts(page.Dict, reader);
        var extGStates = ExtGState.ResolveRawFromPage(page.Dict, reader);

        foreach (var stream in contentStreams)
        {
            RenderToSvg(stream, fonts, extGStates, reader, sb);
        }

        sb.AppendLine("</g>");
        sb.AppendLine("</svg>");
        return sb.ToString();
    }

    /// <summary>
    /// Convert a page to SVG and write to stream.
    /// </summary>
    public void Process(Page page, Stream output)
    {
        var svg = Process(page);
        output.Write(Encoding.UTF8.GetBytes(svg));
    }

    /// <summary>
    /// Convert a page to SVG and write to a file.
    /// </summary>
    public void Process(Page page, string outputFileName)
    {
        using var fs = new FileStream(outputFileName, FileMode.Create, FileAccess.Write);
        Process(page, fs);
    }

    /// <summary>
    /// Holds the mutable graphics state for rendering.
    /// </summary>
    private sealed class GState
    {
        public double FillR, FillG, FillB;
        public double StrokeR, StrokeG, StrokeB;
        public double FillAlpha = 1.0;
        public double StrokeAlpha = 1.0;
        public string BlendMode = "Normal";
        public double FontSize = 12;
        public string FontName = "sans-serif";
        public double LineWidth = 1.0;
        public int LineCap; // 0=butt, 1=round, 2=square
        public int LineJoin; // 0=miter, 1=round, 2=bevel
        public double[] DashArray = Array.Empty<double>();
        public double DashPhase;
        public double TextLeading;

        // CTM as 6-element matrix [a b c d e f]
        public double[] Ctm = { 1, 0, 0, 1, 0, 0 };

        public GState Clone()
        {
            return new GState
            {
                FillR = FillR, FillG = FillG, FillB = FillB,
                StrokeR = StrokeR, StrokeG = StrokeG, StrokeB = StrokeB,
                FillAlpha = FillAlpha, StrokeAlpha = StrokeAlpha,
                BlendMode = BlendMode,
                FontSize = FontSize, FontName = FontName,
                LineWidth = LineWidth,
                LineCap = LineCap, LineJoin = LineJoin,
                DashArray = (double[])DashArray.Clone(),
                DashPhase = DashPhase,
                TextLeading = TextLeading,
                Ctm = (double[])Ctm.Clone(),
            };
        }
    }

    private static void RenderToSvg(byte[] streamBytes, Dictionary<string, PdfDictionary> fonts,
        Dictionary<string, PdfDictionary> extGStates, PdfReader reader, StringBuilder sb)
    {
        var lexer = new PdfLexer(streamBytes);
        var operands = new List<PdfObject>();
        double tx = 0, ty = 0;
        var gs = new GState();
        var gsStack = new Stack<GState>();
        var pathData = new StringBuilder();
        // Track open <g> elements from cm transforms so we can close them
        int cmGroupDepth = 0;

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
                        // --- Graphics state stack ---
                        case "q":
                            gsStack.Push(gs.Clone());
                            break;
                        case "Q":
                            if (gsStack.Count > 0)
                                gs = gsStack.Pop();
                            // Close any cm transform groups opened since last q
                            while (cmGroupDepth > 0)
                            {
                                sb.AppendLine("</g>");
                                cmGroupDepth--;
                            }
                            break;

                        // --- CTM ---
                        case "cm":
                            if (operands.Count >= 6)
                            {
                                var a = Num(operands[0]); var b2 = Num(operands[1]);
                                var c = Num(operands[2]); var d = Num(operands[3]);
                                var e = Num(operands[4]); var f = Num(operands[5]);
                                sb.AppendLine($"<g transform=\"matrix({F(a)},{F(b2)},{F(c)},{F(d)},{F(e)},{F(f)})\">");
                                cmGroupDepth++;

                                // Update CTM (multiply current by new)
                                var old = gs.Ctm;
                                gs.Ctm = new[]
                                {
                                    a * old[0] + b2 * old[2],
                                    a * old[1] + b2 * old[3],
                                    c * old[0] + d * old[2],
                                    c * old[1] + d * old[3],
                                    e * old[0] + f * old[2] + old[4],
                                    e * old[1] + f * old[3] + old[5],
                                };
                            }
                            break;

                        // --- ExtGState ---
                        case "gs":
                            if (operands.Count >= 1 && operands[0] is PdfName gsName)
                            {
                                if (extGStates.TryGetValue(gsName.Value, out var gsDict))
                                {
                                    var caObj = gsDict.Get("ca");
                                    if (caObj is PdfReal caR) gs.FillAlpha = caR.Value;
                                    else if (caObj is PdfInteger caI) gs.FillAlpha = caI.Value;

                                    var scaObj = gsDict.Get("CA");
                                    if (scaObj is PdfReal scaR) gs.StrokeAlpha = scaR.Value;
                                    else if (scaObj is PdfInteger scaI) gs.StrokeAlpha = scaI.Value;

                                    var bmObj = gsDict.GetName("BM");
                                    if (bmObj is not null) gs.BlendMode = bmObj;
                                }
                            }
                            break;

                        // --- Line width ---
                        case "w":
                            if (operands.Count >= 1)
                                gs.LineWidth = Num(operands[0]);
                            break;

                        // --- Line cap ---
                        case "J":
                            if (operands.Count >= 1)
                                gs.LineCap = (int)Num(operands[0]);
                            break;

                        // --- Line join ---
                        case "j":
                            if (operands.Count >= 1)
                                gs.LineJoin = (int)Num(operands[0]);
                            break;

                        // --- Dash pattern ---
                        case "d":
                            if (operands.Count >= 2 && operands[0] is PdfArray dashArr)
                            {
                                gs.DashArray = new double[dashArr.Count];
                                for (int i = 0; i < dashArr.Count; i++)
                                    gs.DashArray[i] = Num(dashArr[i]);
                                gs.DashPhase = Num(operands[1]);
                            }
                            break;

                        // --- Font ---
                        case "Tf":
                            if (operands.Count >= 2)
                            {
                                if (operands[0] is PdfName fn)
                                {
                                    gs.FontName = fn.Value;
                                    if (fonts.TryGetValue(gs.FontName, out var fd))
                                    {
                                        var baseFont = fd.GetName("BaseFont") ?? "sans-serif";
                                        gs.FontName = MapFontName(baseFont);
                                    }
                                }
                                gs.FontSize = Num(operands[1]);
                            }
                            break;

                        // --- Text positioning ---
                        case "Td":
                            if (operands.Count >= 2)
                            { tx += Num(operands[0]); ty += Num(operands[1]); }
                            break;
                        case "TD":
                            if (operands.Count >= 2)
                            {
                                var tdx = Num(operands[0]);
                                var tdy = Num(operands[1]);
                                tx += tdx;
                                ty += tdy;
                                gs.TextLeading = -tdy;
                            }
                            break;
                        case "Tm":
                            if (operands.Count >= 6)
                            { tx = Num(operands[4]); ty = Num(operands[5]); }
                            break;
                        case "TL":
                            if (operands.Count >= 1)
                                gs.TextLeading = Num(operands[0]);
                            break;
                        case "T*":
                            tx = 0;
                            ty -= gs.TextLeading;
                            break;

                        // --- Fill color (RGB) ---
                        case "rg":
                            if (operands.Count >= 3)
                            { gs.FillR = Num(operands[0]); gs.FillG = Num(operands[1]); gs.FillB = Num(operands[2]); }
                            break;

                        // --- Stroke color (RGB) ---
                        case "RG":
                            if (operands.Count >= 3)
                            { gs.StrokeR = Num(operands[0]); gs.StrokeG = Num(operands[1]); gs.StrokeB = Num(operands[2]); }
                            break;

                        // --- Grayscale fill ---
                        case "g":
                            if (operands.Count >= 1)
                            {
                                var gray = Num(operands[0]);
                                gs.FillR = gray; gs.FillG = gray; gs.FillB = gray;
                            }
                            break;

                        // --- Grayscale stroke ---
                        case "G":
                            if (operands.Count >= 1)
                            {
                                var gray = Num(operands[0]);
                                gs.StrokeR = gray; gs.StrokeG = gray; gs.StrokeB = gray;
                            }
                            break;

                        // --- CMYK fill ---
                        case "k":
                            if (operands.Count >= 4)
                            {
                                CmykToRgb(Num(operands[0]), Num(operands[1]), Num(operands[2]), Num(operands[3]),
                                    out gs.FillR, out gs.FillG, out gs.FillB);
                            }
                            break;

                        // --- CMYK stroke ---
                        case "K":
                            if (operands.Count >= 4)
                            {
                                CmykToRgb(Num(operands[0]), Num(operands[1]), Num(operands[2]), Num(operands[3]),
                                    out gs.StrokeR, out gs.StrokeG, out gs.StrokeB);
                            }
                            break;

                        // --- Text show: ' (move to next line then show) ---
                        case "'":
                            // Equivalent to T* then Tj
                            tx = 0;
                            ty -= gs.TextLeading;
                            if (operands.Count >= 1 && operands[0] is PdfString qs)
                            {
                                EmitText(sb, gs, tx, ty, EscapeXml(Encoding.Latin1.GetString(qs.Value)));
                            }
                            break;

                        // --- Text show ---
                        case "Tj":
                            if (operands.Count >= 1 && operands[0] is PdfString s)
                            {
                                EmitText(sb, gs, tx, ty, EscapeXml(Encoding.Latin1.GetString(s.Value)));
                            }
                            break;
                        case "TJ":
                            if (operands.Count >= 1 && operands[0] is PdfArray tja)
                            {
                                var tjText = new StringBuilder();
                                foreach (var item in tja)
                                {
                                    if (item is PdfString ts)
                                        tjText.Append(Encoding.Latin1.GetString(ts.Value));
                                    else if (item is PdfInteger ti && ti.Value < -200)
                                        tjText.Append(' ');
                                    else if (item is PdfReal tr2 && tr2.Value < -200)
                                        tjText.Append(' ');
                                }
                                EmitText(sb, gs, tx, ty, EscapeXml(tjText.ToString()));
                            }
                            break;

                        // --- Path construction ---
                        case "m": // moveto
                            if (operands.Count >= 2)
                                pathData.Append($"M{F(Num(operands[0]))} {F(Num(operands[1]))} ");
                            break;
                        case "l": // lineto
                            if (operands.Count >= 2)
                                pathData.Append($"L{F(Num(operands[0]))} {F(Num(operands[1]))} ");
                            break;
                        case "c": // curveto
                            if (operands.Count >= 6)
                                pathData.Append($"C{F(Num(operands[0]))} {F(Num(operands[1]))} {F(Num(operands[2]))} {F(Num(operands[3]))} {F(Num(operands[4]))} {F(Num(operands[5]))} ");
                            break;
                        case "v": // curveto (initial point replicated)
                            if (operands.Count >= 4)
                                pathData.Append($"C{F(tx)} {F(ty)} {F(Num(operands[0]))} {F(Num(operands[1]))} {F(Num(operands[2]))} {F(Num(operands[3]))} ");
                            break;
                        case "y": // curveto (final point replicated)
                            if (operands.Count >= 4)
                                pathData.Append($"C{F(Num(operands[0]))} {F(Num(operands[1]))} {F(Num(operands[2]))} {F(Num(operands[3]))} {F(Num(operands[2]))} {F(Num(operands[3]))} ");
                            break;
                        case "h": // closepath
                            pathData.Append("Z ");
                            break;
                        case "re":
                            if (operands.Count >= 4)
                            {
                                var rx = Num(operands[0]); var ry = Num(operands[1]);
                                var rw = Num(operands[2]); var rh = Num(operands[3]);
                                pathData.Append($"M{F(rx)} {F(ry)} L{F(rx + rw)} {F(ry)} L{F(rx + rw)} {F(ry + rh)} L{F(rx)} {F(ry + rh)} Z ");
                            }
                            break;

                        // --- Path painting ---
                        case "S": // stroke
                            if (pathData.Length > 0)
                            {
                                EmitPath(sb, gs, pathData.ToString().Trim(), stroke: true, fill: false, evenOdd: false);
                                pathData.Clear();
                            }
                            break;
                        case "s": // close and stroke
                            pathData.Append("Z ");
                            goto case "S";
                        case "f" or "F": // fill (nonzero)
                            if (pathData.Length > 0)
                            {
                                EmitPath(sb, gs, pathData.ToString().Trim(), stroke: false, fill: true, evenOdd: false);
                                pathData.Clear();
                            }
                            break;
                        case "f*": // fill (even-odd)
                            if (pathData.Length > 0)
                            {
                                EmitPath(sb, gs, pathData.ToString().Trim(), stroke: false, fill: true, evenOdd: true);
                                pathData.Clear();
                            }
                            break;
                        case "B": // fill and stroke (nonzero)
                            if (pathData.Length > 0)
                            {
                                EmitPath(sb, gs, pathData.ToString().Trim(), stroke: true, fill: true, evenOdd: false);
                                pathData.Clear();
                            }
                            break;
                        case "B*": // fill and stroke (even-odd)
                            if (pathData.Length > 0)
                            {
                                EmitPath(sb, gs, pathData.ToString().Trim(), stroke: true, fill: true, evenOdd: true);
                                pathData.Clear();
                            }
                            break;
                        case "b": // close, fill and stroke (nonzero)
                            pathData.Append("Z ");
                            if (pathData.Length > 0)
                            {
                                EmitPath(sb, gs, pathData.ToString().Trim(), stroke: true, fill: true, evenOdd: false);
                                pathData.Clear();
                            }
                            break;
                        case "b*": // close, fill and stroke (even-odd)
                            pathData.Append("Z ");
                            if (pathData.Length > 0)
                            {
                                EmitPath(sb, gs, pathData.ToString().Trim(), stroke: true, fill: true, evenOdd: true);
                                pathData.Clear();
                            }
                            break;
                        case "n": // end path (no fill, no stroke)
                            pathData.Clear();
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

        // Close any remaining cm groups
        while (cmGroupDepth > 0)
        {
            sb.AppendLine("</g>");
            cmGroupDepth--;
        }
    }

    /// <summary>
    /// Emit a text element with current graphics state.
    /// </summary>
    private static void EmitText(StringBuilder sb, GState gs, double x, double y, string text)
    {
        var fillColor = FormatRgb(gs.FillR, gs.FillG, gs.FillB);
        var style = new StringBuilder();
        style.Append($"font-family=\"{gs.FontName}\" font-size=\"{F(gs.FontSize)}\" ");
        style.Append($"fill=\"{fillColor}\"");
        if (gs.FillAlpha < 1.0)
            style.Append($" fill-opacity=\"{F(gs.FillAlpha)}\"");
        if (gs.BlendMode != "Normal")
            style.Append($" style=\"mix-blend-mode:{MapBlendMode(gs.BlendMode)}\"");
        sb.AppendLine($"<text x=\"{F(x)}\" y=\"{F(y)}\" {style}>{text}</text>");
    }

    /// <summary>
    /// Emit a path element with current graphics state.
    /// </summary>
    private static void EmitPath(StringBuilder sb, GState gs, string d, bool stroke, bool fill, bool evenOdd)
    {
        var attrs = new StringBuilder();
        attrs.Append($"d=\"{d}\"");

        if (fill)
        {
            var fillColor = FormatRgb(gs.FillR, gs.FillG, gs.FillB);
            attrs.Append($" fill=\"{fillColor}\"");
            if (gs.FillAlpha < 1.0)
                attrs.Append($" fill-opacity=\"{F(gs.FillAlpha)}\"");
            if (evenOdd)
                attrs.Append(" fill-rule=\"evenodd\"");
        }
        else
        {
            attrs.Append(" fill=\"none\"");
        }

        if (stroke)
        {
            var strokeColor = FormatRgb(gs.StrokeR, gs.StrokeG, gs.StrokeB);
            attrs.Append($" stroke=\"{strokeColor}\"");
            if (gs.StrokeAlpha < 1.0)
                attrs.Append($" stroke-opacity=\"{F(gs.StrokeAlpha)}\"");
            if (gs.LineWidth != 1.0)
                attrs.Append($" stroke-width=\"{F(gs.LineWidth)}\"");
            if (gs.LineCap != 0)
                attrs.Append($" stroke-linecap=\"{MapLineCap(gs.LineCap)}\"");
            if (gs.LineJoin != 0)
                attrs.Append($" stroke-linejoin=\"{MapLineJoin(gs.LineJoin)}\"");
            if (gs.DashArray.Length > 0)
            {
                attrs.Append($" stroke-dasharray=\"{string.Join(",", gs.DashArray.Select(v => F(v)))}\"");
                if (gs.DashPhase != 0)
                    attrs.Append($" stroke-dashoffset=\"{F(gs.DashPhase)}\"");
            }
        }
        else
        {
            attrs.Append(" stroke=\"none\"");
        }

        if (gs.BlendMode != "Normal")
            attrs.Append($" style=\"mix-blend-mode:{MapBlendMode(gs.BlendMode)}\"");

        sb.AppendLine($"<path {attrs} />");
    }

    private static string FormatRgb(double r, double g, double b) =>
        $"rgb({(int)(r * 255)},{(int)(g * 255)},{(int)(b * 255)})";

    private static void CmykToRgb(double c, double m, double y, double k,
        out double r, out double g, out double b)
    {
        r = (1 - c) * (1 - k);
        g = (1 - m) * (1 - k);
        b = (1 - y) * (1 - k);
    }

    private static string MapLineCap(int cap) => cap switch
    {
        1 => "round",
        2 => "square",
        _ => "butt",
    };

    private static string MapLineJoin(int join) => join switch
    {
        1 => "round",
        2 => "bevel",
        _ => "miter",
    };

    private static string MapFontName(string baseFont) => baseFont switch
    {
        var n when n.Contains("Helvetica") => "Helvetica, Arial, sans-serif",
        var n when n.Contains("Times") => "Times New Roman, serif",
        var n when n.Contains("Courier") => "Courier New, monospace",
        _ => "sans-serif",
    };

    private static string MapBlendMode(string pdfMode) => pdfMode switch
    {
        "Multiply" => "multiply",
        "Screen" => "screen",
        "Overlay" => "overlay",
        "Darken" => "darken",
        "Lighten" => "lighten",
        "ColorDodge" => "color-dodge",
        "ColorBurn" => "color-burn",
        "HardLight" => "hard-light",
        "SoftLight" => "soft-light",
        "Difference" => "difference",
        "Exclusion" => "exclusion",
        "Hue" => "hue",
        "Saturation" => "saturation",
        "Color" => "color",
        "Luminosity" => "luminosity",
        _ => "normal",
    };

    private static string EscapeXml(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static Dictionary<string, PdfDictionary> ResolveFonts(PdfDictionary pageDict, PdfReader reader)
    {
        var result = new Dictionary<string, PdfDictionary>(StringComparer.Ordinal);
        var resources = reader.ResolveDict(pageDict.Get("Resources"));
        if (resources is null) return result;
        var fontDict = reader.ResolveDict(resources.Get("Font"));
        if (fontDict is null) return result;
        foreach (var key in fontDict.Keys)
        {
            var font = reader.ResolveDict(fontDict.Get(key));
            if (font is not null) result[key] = font;
        }
        return result;
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

    private static string F(double v) => v.ToString("G", CultureInfo.InvariantCulture);
}
