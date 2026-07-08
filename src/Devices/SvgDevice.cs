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
        var resources = reader.ResolveDict(page.Dict.Get("Resources"));

        foreach (var stream in contentStreams)
        {
            RenderToSvg(stream, resources, reader, sb, 0);
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
        // Font descriptor + ToUnicode CMap for the current font, so show-text
        // byte strings (which for embedded/subset fonts are glyph codes, not
        // Latin1 text) are decoded to real Unicode instead of garbage.
        public PdfDictionary? FontDict;
        public Dictionary<int, string>? ToUnicode;
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
                FontDict = FontDict, ToUnicode = ToUnicode,
                LineWidth = LineWidth,
                LineCap = LineCap, LineJoin = LineJoin,
                DashArray = (double[])DashArray.Clone(),
                DashPhase = DashPhase,
                TextLeading = TextLeading,
                Ctm = (double[])Ctm.Clone(),
            };
        }
    }

    /// <summary>Guard against pathological or self-referential Form XObject nesting.</summary>
    private const int MaxXObjectDepth = 12;

    private static void RenderToSvg(byte[] streamBytes, PdfDictionary? resources,
        PdfReader reader, StringBuilder sb, int depth)
    {
        var fonts = ResolveFonts(resources, reader);
        var extGStates = ResolveExtGStates(resources, reader);
        var lexer = new PdfLexer(streamBytes);
        var operands = new List<PdfObject>();
        // Text position + matrices. tx/ty mirror the text-matrix translation
        // (tm[4],tm[5]); tm is the text matrix, tlm the text line matrix. The
        // effective glyph size is the Tf size scaled by the text matrix, so a
        // "1 Tf" with an "N 0 0 N .. Tm" renders at N, not 1.
        double tx = 0, ty = 0;
        double[] tm = { 1, 0, 0, 1, 0, 0 };
        double[] tlm = { 1, 0, 0, 1, 0, 0 };
        var gs = new GState();
        var gsStack = new Stack<GState>();
        var pathData = new StringBuilder();
        // Track open <g> elements from cm transforms so we can close them. qGroupStack
        // records how many were open at each `q`, so the matching `Q` closes only the
        // groups opened within that q/Q pair — otherwise a cm issued before/around a
        // q/Q block (e.g. a page-level flip) gets closed early and later content is no
        // longer nested under it.
        int cmGroupDepth = 0;
        var qGroupStack = new Stack<int>();

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
                            qGroupStack.Push(cmGroupDepth);
                            break;
                        case "Q":
                            if (gsStack.Count > 0)
                                gs = gsStack.Pop();
                            // Close only the cm groups opened since the matching q.
                            var target = qGroupStack.Count > 0 ? qGroupStack.Pop() : 0;
                            while (cmGroupDepth > target)
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
                                    if (fonts.TryGetValue(fn.Value, out var fd))
                                    {
                                        gs.FontDict = fd;
                                        gs.ToUnicode = Text.TextAbsorber.ParseToUnicodeFromDict(fd, reader);
                                        var baseFont = fd.GetName("BaseFont") ?? "sans-serif";
                                        gs.FontName = MapFontName(baseFont);
                                    }
                                    else
                                    {
                                        gs.FontDict = null;
                                        gs.ToUnicode = null;
                                        gs.FontName = fn.Value;
                                    }
                                }
                                gs.FontSize = Num(operands[1]);
                            }
                            break;

                        // --- Text object begin: reset text + line matrices ---
                        case "BT":
                            Array.Copy(Identity, tm, 6);
                            Array.Copy(Identity, tlm, 6);
                            tx = 0; ty = 0;
                            break;

                        // --- Text positioning (operate on the text line matrix) ---
                        case "Td":
                            if (operands.Count >= 2)
                            {
                                tlm = MulAffine(new[] { 1.0, 0, 0, 1, Num(operands[0]), Num(operands[1]) }, tlm);
                                Array.Copy(tlm, tm, 6); tx = tm[4]; ty = tm[5];
                            }
                            break;
                        case "TD":
                            if (operands.Count >= 2)
                            {
                                gs.TextLeading = -Num(operands[1]);
                                tlm = MulAffine(new[] { 1.0, 0, 0, 1, Num(operands[0]), Num(operands[1]) }, tlm);
                                Array.Copy(tlm, tm, 6); tx = tm[4]; ty = tm[5];
                            }
                            break;
                        case "Tm":
                            if (operands.Count >= 6)
                            {
                                var m = new[] { Num(operands[0]), Num(operands[1]), Num(operands[2]),
                                    Num(operands[3]), Num(operands[4]), Num(operands[5]) };
                                Array.Copy(m, tm, 6); Array.Copy(m, tlm, 6); tx = tm[4]; ty = tm[5];
                            }
                            break;
                        case "TL":
                            if (operands.Count >= 1)
                                gs.TextLeading = Num(operands[0]);
                            break;
                        case "T*":
                            tlm = MulAffine(new[] { 1.0, 0, 0, 1, 0, -gs.TextLeading }, tlm);
                            Array.Copy(tlm, tm, 6); tx = tm[4]; ty = tm[5];
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

                        // --- Colorspace-relative fill/stroke colour (sc/scn, SC/SCN) ---
                        // The colorspace is set via cs/CS to a named space; rather than
                        // resolve it, infer from the numeric operand count (1=gray,
                        // 3=rgb, 4=cmyk). Without this, sc-coloured content (e.g. white
                        // 1 1 1 sc interiors) all defaulted to black.
                        case "sc":
                        case "scn":
                            SetColorFromComponents(operands, ref gs.FillR, ref gs.FillG, ref gs.FillB);
                            break;
                        case "SC":
                        case "SCN":
                            SetColorFromComponents(operands, ref gs.StrokeR, ref gs.StrokeG, ref gs.StrokeB);
                            break;

                        // --- Text show: ' (move to next line then show) ---
                        case "'":
                            // Equivalent to T* then Tj
                            tlm = MulAffine(new[] { 1.0, 0, 0, 1, 0, -gs.TextLeading }, tlm);
                            Array.Copy(tlm, tm, 6); tx = tm[4]; ty = tm[5];
                            if (operands.Count >= 1 && operands[0] is PdfString qs)
                            {
                                EmitTextRun(sb, gs, tm, EscapeXml(DecodeShow(qs.Value, gs, reader)));
                            }
                            break;

                        // --- Text show ---
                        case "Tj":
                            if (operands.Count >= 1 && operands[0] is PdfString s)
                            {
                                EmitTextRun(sb, gs, tm, EscapeXml(DecodeShow(s.Value, gs, reader)));
                            }
                            break;
                        case "TJ":
                            if (operands.Count >= 1 && operands[0] is PdfArray tja)
                            {
                                var tjText = new StringBuilder();
                                foreach (var item in tja)
                                {
                                    if (item is PdfString ts)
                                        tjText.Append(DecodeShow(ts.Value, gs, reader));
                                    else if (item is PdfInteger ti && ti.Value < -200)
                                        tjText.Append(' ');
                                    else if (item is PdfReal tr2 && tr2.Value < -200)
                                        tjText.Append(' ');
                                }
                                EmitTextRun(sb, gs, tm, EscapeXml(tjText.ToString()));
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
                        // --- XObject invocation (Form recursion) ---
                        case "Do":
                            if (operands.Count >= 1 && operands[0] is PdfName xn)
                                RenderXObject(xn.Value, resources, reader, sb, depth);
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
    /// Emit one text run. The run is placed by its text matrix with the matrix's
    /// y-column negated (<c>matrix(a,b,-c,-d,e,f)</c> == tm composed with a glyph
    /// y-flip), which cancels the page's <c>scale(1,-1)</c> flip so the glyphs render
    /// upright in a plain SVG viewer while still landing at the right place. Font size
    /// stays the raw Tf size; the matrix carries the text-matrix scale/rotation/shear.
    /// SvgToPdfConverter recovers the PDF text matrix by negating the y-column back.
    /// </summary>
    private static void EmitTextRun(StringBuilder sb, GState gs, double[] tm, string text)
    {
        var fillColor = FormatRgb(gs.FillR, gs.FillG, gs.FillB);
        var style = new StringBuilder();
        style.Append($"font-family=\"{gs.FontName}\" font-size=\"{F(gs.FontSize)}\" ");
        style.Append($"fill=\"{fillColor}\"");
        if (gs.FillAlpha < 1.0)
            style.Append($" fill-opacity=\"{F(gs.FillAlpha)}\"");
        if (gs.BlendMode != "Normal")
            style.Append($" style=\"mix-blend-mode:{MapBlendMode(gs.BlendMode)}\"");
        var transform = $"matrix({F(tm[0])},{F(tm[1])},{F(Neg(tm[2]))},{F(Neg(tm[3]))},{F(tm[4])},{F(tm[5])})";
        sb.AppendLine($"<text transform=\"{transform}\" {style}>{text}</text>");
    }

    /// <summary>Negate, flushing -0 to 0 for tidy output.</summary>
    private static double Neg(double v) => v == 0.0 ? 0.0 : -v;

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

    private static readonly double[] Identity = { 1, 0, 0, 1, 0, 0 };

    /// <summary>Compose two affine matrices (PDF row-vector convention): m1 × m2.</summary>
    private static double[] MulAffine(double[] m1, double[] m2) => new[]
    {
        m1[0] * m2[0] + m1[1] * m2[2],
        m1[0] * m2[1] + m1[1] * m2[3],
        m1[2] * m2[0] + m1[3] * m2[2],
        m1[2] * m2[1] + m1[3] * m2[3],
        m1[4] * m2[0] + m1[5] * m2[2] + m2[4],
        m1[4] * m2[1] + m1[5] * m2[3] + m2[5],
    };

    private static string FormatRgb(double r, double g, double b) =>
        $"rgb({(int)(r * 255)},{(int)(g * 255)},{(int)(b * 255)})";

    /// <summary>
    /// Set an RGB colour from the numeric operands of an <c>sc</c>/<c>scn</c>
    /// operator, inferring the model from the component count. A trailing pattern
    /// name (scn) or zero numeric operands leaves the colour unchanged.
    /// </summary>
    private static void SetColorFromComponents(List<PdfObject> operands, ref double r, ref double g, ref double b)
    {
        var nums = operands.Where(o => o is PdfInteger or PdfReal).Select(Num).ToList();
        switch (nums.Count)
        {
            case 1:
                r = g = b = nums[0];
                break;
            case 3:
                r = nums[0]; g = nums[1]; b = nums[2];
                break;
            case 4:
                CmykToRgb(nums[0], nums[1], nums[2], nums[3], out r, out g, out b);
                break;
        }
    }

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

    /// <summary>
    /// Decode a show-text byte string through the current font. For embedded/subset
    /// fonts the raw bytes are glyph codes, so <c>Latin1.GetString</c> yields invalid
    /// XML; route through the font's ToUnicode/encoding instead. Only when no font
    /// dictionary is bound do we fall back to Latin1.
    /// </summary>
    private static string DecodeShow(byte[] bytes, GState gs, PdfReader reader)
        => gs.FontDict is not null
            ? Text.TextAbsorber.DecodeStringPublic(bytes, gs.ToUnicode, gs.FontDict, reader)
            : Encoding.Latin1.GetString(bytes);

    /// <summary>
    /// Render a named XObject. Form XObjects are decoded and recursed into with their
    /// own /Resources (falling back to the parent's) and optional /Matrix. Image
    /// XObjects are not yet emitted.
    /// </summary>
    private static void RenderXObject(string name, PdfDictionary? resources, PdfReader reader,
        StringBuilder sb, int depth)
    {
        if (depth >= MaxXObjectDepth || resources is null) return;

        var xobjDict = reader.ResolveDict(resources.Get("XObject"));
        if (xobjDict is null) return;

        var xobj = reader.ResolveStream(xobjDict.Get(name));
        if (xobj is null) return;

        var subtype = xobj.Dict.GetName("Subtype");
        if (subtype != "Form") return; // Image XObjects unsupported for now

        byte[] formBytes;
        try { formBytes = reader.DecodeStream(xobj); }
        catch { return; }

        var formResources = reader.ResolveDict(xobj.Dict.Get("Resources")) ?? resources;

        // Optional /Matrix maps form space into the current user space.
        var matrix = reader.Resolve(xobj.Dict.Get("Matrix")) as PdfArray;
        var wrapped = false;
        if (matrix is not null && matrix.Count >= 6)
        {
            sb.AppendLine($"<g transform=\"matrix({F(Num(matrix[0]))},{F(Num(matrix[1]))}," +
                $"{F(Num(matrix[2]))},{F(Num(matrix[3]))},{F(Num(matrix[4]))},{F(Num(matrix[5]))})\">");
            wrapped = true;
        }

        RenderToSvg(formBytes, formResources, reader, sb, depth + 1);

        if (wrapped) sb.AppendLine("</g>");
    }

    private static string EscapeXml(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            // Drop characters that are illegal in XML 1.0: control chars below 0x20
            // (except tab/LF/CR) and the non-characters U+FFFE/U+FFFF.
            if (ch < 0x20 && ch is not ('\t' or '\n' or '\r')) continue;
            if (ch is '￾' or '￿') continue;
            switch (ch)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                default: sb.Append(ch); break;
            }
        }
        return sb.ToString();
    }

    private static Dictionary<string, PdfDictionary> ResolveFonts(PdfDictionary? resources, PdfReader reader)
    {
        var result = new Dictionary<string, PdfDictionary>(StringComparer.Ordinal);
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

    private static Dictionary<string, PdfDictionary> ResolveExtGStates(PdfDictionary? resources, PdfReader reader)
    {
        var result = new Dictionary<string, PdfDictionary>(StringComparer.Ordinal);
        if (resources is null) return result;
        var gsDict = reader.ResolveDict(resources.Get("ExtGState"));
        if (gsDict is null) return result;
        foreach (var key in gsDict.Keys)
        {
            var entryDict = reader.ResolveDict(gsDict.Get(key));
            if (entryDict is not null) result[key] = entryDict;
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
