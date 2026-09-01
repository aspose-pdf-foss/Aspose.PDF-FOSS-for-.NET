using System.Globalization;
using System.Text;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Devices;

public sealed partial class SvgDevice
{
    private void RenderToSvg(byte[] streamBytes, PdfDictionary? resources,
        PdfReader reader, StringBuilder sb, int depth, GState gs, ISet<string> usedBlendModes,
        List<LinkRect>? links)
    {
        var fonts = ResolveFonts(resources, reader);
        var extGStates = ResolveExtGStates(resources, reader);
        var lexer = new PdfLexer(streamBytes);
        var operands = new List<PdfObject>();
        // tm is the text matrix, tlm the text line matrix (PDF row-vector convention).
        double[] tm = { 1, 0, 0, 1, 0, 0 };
        double[] tlm = { 1, 0, 0, 1, 0, 0 };
        var gsStack = new Stack<GState>();
        var pathData = new StringBuilder();
        // Path current point in USER coordinates (needed by the v operator).
        double curX = 0, curY = 0;

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
                            break;

                        // --- CTM ---
                        case "cm":
                            if (operands.Count >= 6)
                            {
                                var m = new[]
                                {
                                    Num(operands[0]), Num(operands[1]), Num(operands[2]),
                                    Num(operands[3]), Num(operands[4]), Num(operands[5]),
                                };
                                gs.Ctm = MulAffine(m, gs.Ctm);
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
                                    if (bmObj is not null)
                                    {
                                        gs.BlendMode = bmObj;
                                        if (bmObj != "Normal") usedBlendModes.Add(bmObj);
                                    }
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
                                        try { gs.Metrics = Text.FontMetrics.FromFontDict(fd, reader); }
                                        catch { gs.Metrics = null; }
                                    }
                                    else
                                    {
                                        gs.FontDict = null;
                                        gs.ToUnicode = null;
                                        gs.Metrics = null;
                                        gs.FontName = fn.Value;
                                    }
                                }
                                gs.FontSize = Num(operands[1]);
                            }
                            break;

                        // --- Text state ---
                        case "Tc":
                            if (operands.Count >= 1) gs.CharSpacing = Num(operands[0]);
                            break;
                        case "Tw":
                            if (operands.Count >= 1) gs.WordSpacing = Num(operands[0]);
                            break;
                        case "Tz":
                            if (operands.Count >= 1) gs.HorizScale = Num(operands[0]) / 100.0;
                            break;
                        case "Ts":
                            if (operands.Count >= 1) gs.TextRise = Num(operands[0]);
                            break;
                        case "Tr":
                            if (operands.Count >= 1) gs.RenderMode = (int)Num(operands[0]);
                            break;

                        // --- Text object begin: reset text + line matrices ---
                        case "BT":
                            Array.Copy(Identity, tm, 6);
                            Array.Copy(Identity, tlm, 6);
                            break;

                        // --- Text positioning (operate on the text line matrix) ---
                        case "Td":
                            if (operands.Count >= 2)
                            {
                                tlm = MulAffine(new[] { 1.0, 0, 0, 1, Num(operands[0]), Num(operands[1]) }, tlm);
                                Array.Copy(tlm, tm, 6);
                            }
                            break;
                        case "TD":
                            if (operands.Count >= 2)
                            {
                                gs.TextLeading = -Num(operands[1]);
                                tlm = MulAffine(new[] { 1.0, 0, 0, 1, Num(operands[0]), Num(operands[1]) }, tlm);
                                Array.Copy(tlm, tm, 6);
                            }
                            break;
                        case "Tm":
                            if (operands.Count >= 6)
                            {
                                var m = new[] { Num(operands[0]), Num(operands[1]), Num(operands[2]),
                                    Num(operands[3]), Num(operands[4]), Num(operands[5]) };
                                Array.Copy(m, tm, 6); Array.Copy(m, tlm, 6);
                            }
                            break;
                        case "TL":
                            if (operands.Count >= 1)
                                gs.TextLeading = Num(operands[0]);
                            break;
                        case "T*":
                            tlm = MulAffine(new[] { 1.0, 0, 0, 1, 0, -gs.TextLeading }, tlm);
                            Array.Copy(tlm, tm, 6);
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
                        case "cs":
                        case "CS":
                            if (operands.Count >= 1 && operands[0] is PdfName csn)
                            {
                                var resolved = ResolveNamedColorSpace(csn.Value, resources, reader);
                                if (op == "cs") gs.FillCs = resolved; else gs.StrokeCs = resolved;
                            }
                            break;
                        case "sc":
                        case "scn":
                            if (!(gs.FillCs is { TintTransform: not null } fcs
                                  && TintToRgb(fcs, operands, ref gs.FillR, ref gs.FillG, ref gs.FillB)))
                                SetColorFromComponents(operands, ref gs.FillR, ref gs.FillG, ref gs.FillB);
                            break;
                        case "SC":
                        case "SCN":
                            if (!(gs.StrokeCs is { TintTransform: not null } scs
                                  && TintToRgb(scs, operands, ref gs.StrokeR, ref gs.StrokeG, ref gs.StrokeB)))
                                SetColorFromComponents(operands, ref gs.StrokeR, ref gs.StrokeG, ref gs.StrokeB);
                            break;

                        // --- Text show: ' (move to next line then show) ---
                        case "'":
                            tlm = MulAffine(new[] { 1.0, 0, 0, 1, 0, -gs.TextLeading }, tlm);
                            Array.Copy(tlm, tm, 6);
                            if (operands.Count >= 1 && operands[0] is PdfString qs)
                                ShowText(sb, gs, tm, new PdfObject[] { qs }, reader, links);
                            break;

                        // --- Text show: " (set word+char spacing, next line, show) ---
                        case "\"":
                            if (operands.Count >= 3 && operands[2] is PdfString dqs)
                            {
                                gs.WordSpacing = Num(operands[0]);
                                gs.CharSpacing = Num(operands[1]);
                                tlm = MulAffine(new[] { 1.0, 0, 0, 1, 0, -gs.TextLeading }, tlm);
                                Array.Copy(tlm, tm, 6);
                                ShowText(sb, gs, tm, new PdfObject[] { dqs }, reader, links);
                            }
                            break;

                        // --- Text show ---
                        case "Tj":
                            if (operands.Count >= 1 && operands[0] is PdfString s)
                                ShowText(sb, gs, tm, new PdfObject[] { s }, reader, links);
                            break;
                        case "TJ":
                            if (operands.Count >= 1 && operands[0] is PdfArray tja)
                                ShowText(sb, gs, tm, tja.ToArray(), reader, links);
                            break;

                        // --- Path construction (coordinates transformed by the CTM) ---
                        case "m": // moveto
                            if (operands.Count >= 2)
                            {
                                curX = Num(operands[0]); curY = Num(operands[1]);
                                var (px, py) = Apply(gs.Ctm, curX, curY);
                                pathData.Append($"M{F(px)} {F(py)} ");
                            }
                            break;
                        case "l": // lineto
                            if (operands.Count >= 2)
                            {
                                curX = Num(operands[0]); curY = Num(operands[1]);
                                var (px, py) = Apply(gs.Ctm, curX, curY);
                                // Polylines are emitted as edge
                                // pairs, so every line vertex appears twice —
                                // geometrically a no-op, kept so the path matches the expected output.
                                pathData.Append($"L{F(px)} {F(py)} L{F(px)} {F(py)} ");
                            }
                            break;
                        case "c": // curveto
                            if (operands.Count >= 6)
                            {
                                var (x1, y1) = Apply(gs.Ctm, Num(operands[0]), Num(operands[1]));
                                var (x2, y2) = Apply(gs.Ctm, Num(operands[2]), Num(operands[3]));
                                curX = Num(operands[4]); curY = Num(operands[5]);
                                var (x3, y3) = Apply(gs.Ctm, curX, curY);
                                pathData.Append($"C{F(x1)} {F(y1)} {F(x2)} {F(y2)} {F(x3)} {F(y3)} ");
                            }
                            break;
                        case "v": // curveto (initial point replicated)
                            if (operands.Count >= 4)
                            {
                                var (x1, y1) = Apply(gs.Ctm, curX, curY);
                                var (x2, y2) = Apply(gs.Ctm, Num(operands[0]), Num(operands[1]));
                                curX = Num(operands[2]); curY = Num(operands[3]);
                                var (x3, y3) = Apply(gs.Ctm, curX, curY);
                                pathData.Append($"C{F(x1)} {F(y1)} {F(x2)} {F(y2)} {F(x3)} {F(y3)} ");
                            }
                            break;
                        case "y": // curveto (final point replicated)
                            if (operands.Count >= 4)
                            {
                                var (x1, y1) = Apply(gs.Ctm, Num(operands[0]), Num(operands[1]));
                                curX = Num(operands[2]); curY = Num(operands[3]);
                                var (x3, y3) = Apply(gs.Ctm, curX, curY);
                                pathData.Append($"C{F(x1)} {F(y1)} {F(x3)} {F(y3)} {F(x3)} {F(y3)} ");
                            }
                            break;
                        case "h": // closepath
                            pathData.Append("Z ");
                            break;
                        case "re":
                            if (operands.Count >= 4)
                            {
                                var rx = Num(operands[0]); var ry = Num(operands[1]);
                                var rw = Num(operands[2]); var rh = Num(operands[3]);
                                var (p1x, p1y) = Apply(gs.Ctm, rx, ry);
                                var (p2x, p2y) = Apply(gs.Ctm, rx + rw, ry);
                                var (p3x, p3y) = Apply(gs.Ctm, rx + rw, ry + rh);
                                var (p4x, p4y) = Apply(gs.Ctm, rx, ry + rh);
                                pathData.Append($"M{F(p1x)} {F(p1y)} L{F(p2x)} {F(p2y)} L{F(p2x)} {F(p2y)} " +
                                    $"L{F(p3x)} {F(p3y)} L{F(p3x)} {F(p3y)} L{F(p4x)} {F(p4y)} L{F(p4x)} {F(p4y)} " +
                                    $"L{F(p1x)} {F(p1y)} Z ");
                                curX = rx; curY = ry;
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
                                RenderXObject(xn.Value, resources, reader, sb, depth, gs, usedBlendModes, links);
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
    }

    /// <summary>
    /// Show one run of text (a Tj/'/"-string or a full TJ array). Decodes the bytes
    /// glyph-by-glyph through the font, advances the text matrix per glyph (width,
    /// char/word spacing, TJ adjustments), and emits a single &lt;text&gt; element.
    /// Horizontal upright runs get per-glyph absolute x positions; rotated runs get a
    /// matrix() transform (y-column negated so glyphs render upright; the importer
    /// negates it back).
    /// </summary>
    private static void ShowText(StringBuilder sb, GState gs, double[] tm,
        PdfObject[] items, PdfReader reader, List<LinkRect>? links)
    {
        var fs = gs.FontSize;
        var th = gs.HorizScale;
        var isCid = gs.Metrics?.IsCid ?? (gs.FontDict?.GetName("Subtype") == "Type0");
        var step = isCid ? 2 : 1;

        // One <text> element is emitted per TJ string segment — kerning
        // adjustments between segments only move the pen.
        foreach (var item in items)
        {
            if (item is PdfInteger or PdfReal)
            {
                var adj = -Num(item) / 1000.0 * fs * th;
                ApplyPen(tm, adj);
                continue;
            }
            if (item is not PdfString ps) continue;
            var bytes = ps.Value;

            var text = new StringBuilder();
            var xs = new List<double>();
            double[]? firstDevice = null;

            for (var i = 0; i + step - 1 < bytes.Length; i += step)
            {
                var code = isCid ? ((bytes[i] << 8) | bytes[i + 1]) : bytes[i];
                var seg = isCid ? new[] { bytes[i], bytes[i + 1] } : new[] { bytes[i] };
                var glyph = gs.FontDict is not null
                    ? Text.TextAbsorber.DecodeStringPublic(seg, gs.ToUnicode, gs.FontDict, reader)
                    : Encoding.Latin1.GetString(seg);
                // A glyph whose decode is empty or XML-invalid (unmapped codes often
                // carry U+FFFF from a bfrange to FFFF) still occupies a slot on the
                // line. Substitute the PUA char U+A880 — the "SVG space" — so the
                // character count stays aligned with the x list.
                glyph = EscapeXml(glyph);
                if (glyph.Length == 0 && gs.FontDict is not null)
                    glyph = "ꢀ";

                // Device matrix for this glyph: [fs*th 0 0 fs 0 rise] × tm × ctm
                var trm = MulAffine(new[] { fs * th, 0, 0, fs, 0, gs.TextRise },
                    MulAffine(tm, gs.Ctm));
                firstDevice ??= trm;
                if (glyph.Length > 0)
                {
                    // Multi-char decodes (ligature expansions) get one position for the
                    // first char; SVG flows the rest after it with natural advances.
                    xs.Add(trm[4]);
                    text.Append(glyph);
                }

                var w = gs.Metrics is not null
                    ? gs.Metrics.GetWidth(code) / 1000.0 * fs
                    : fs * 0.5;
                var adv = (w + gs.CharSpacing + (step == 1 && code == 32 ? gs.WordSpacing : 0)) * th;
                ApplyPen(tm, adv);
            }

            if (text.Length == 0 || firstDevice is null) continue;
            if (gs.RenderMode == 3) continue; // invisible text: pen already advanced

            EmitTextRun(sb, gs, tm, firstDevice, xs, text.ToString(), links);
        }
    }

    /// <summary>Emit one decoded, positioned text run.</summary>
    private static void EmitTextRun(StringBuilder sb, GState gs, double[] tm,
        double[] firstDevice, List<double> xs, string text, List<LinkRect>? links)
    {
        var fs = gs.FontSize;
        var th = gs.HorizScale;
        var fill = FormatHex(gs.FillR, gs.FillG, gs.FillB);
        var style = new StringBuilder();
        style.Append($"fill:{fill};font-family:{gs.FontName};");
        if (gs.FillAlpha < 1.0)
            style.Append($"fill-opacity:{F(gs.FillAlpha)};");
        if (gs.BlendMode != "Normal")
            style.Append($"mix-blend-mode:{MapBlendMode(gs.BlendMode)};");

        var d = firstDevice;
        const double eps = 1e-6;
        if (Math.Abs(d[1]) < eps && Math.Abs(d[2]) < eps && d[0] > 0 && d[3] < 0)
        {
            // Horizontal upright text: absolute per-glyph x positions, single y.
            var size = Math.Abs(d[3]);
            // The run's approximate centre (half a glyph beyond the last x, half a
            // font-size above the baseline) decides link-anchor coverage.
            // Whitespace-only runs draw nothing clickable and stay unwrapped.
            var link = IsBlankRun(text) ? null : LinkAt(links,
                (xs.Min() + xs.Max() + size * 0.5) / 2.0, d[5] - size / 2.0);
            style.Append($"font-size:{FD(size)}px;");
            var xList = string.Join(" ", xs.Select(FD));
            if (link is not null) sb.AppendLine($"<a xlink:href=\"{EscapeXml(link.Uri)}\" target=\"_blank\" >");
            sb.AppendLine($"<text x=\"{xList}\" y=\"{FD(d[5])}\" style=\"{style}\">{text}</text>");
            if (link is not null) sb.AppendLine("</a>");
        }
        else
        {
            // Rotated/sheared: place by matrix (tm×ctm without the font-size factor),
            // y-column negated so the glyphs render upright under the flipped page.
            var t2 = MulAffine(new[] { th, 0, 0, 1.0, 0, gs.TextRise }, MulAffine(tm, gs.Ctm));
            // Anchor the matrix at the start of the run: translation from firstDevice.
            t2[4] = d[4]; t2[5] = d[5];
            var link = IsBlankRun(text) ? null : LinkAt(links, t2[4], t2[5]);
            style.Append($"font-size:{FD(fs)}px;");
            var transform = $"matrix({F(t2[0])},{F(t2[1])},{F(Neg(t2[2]))},{F(Neg(t2[3]))},{F(t2[4])},{F(t2[5])})";
            if (link is not null) sb.AppendLine($"<a xlink:href=\"{EscapeXml(link.Uri)}\" target=\"_blank\" >");
            sb.AppendLine($"<text transform=\"{transform}\" style=\"{style}\">{text}</text>");
            if (link is not null) sb.AppendLine("</a>");
        }
    }

    /// <summary>True when a decoded run has no visible glyphs (whitespace and the
    /// U+A880 blank-glyph placeholder only).</summary>
    private static bool IsBlankRun(string text)
    {
        foreach (var ch in text)
            if (!char.IsWhiteSpace(ch) && ch != 'ꢀ') return false;
        return true;
    }

    /// <summary>Advance the text matrix pen: tm := [1 0 0 1 adv 0] × tm.</summary>
    private static void ApplyPen(double[] tm, double adv)
    {
        tm[4] += adv * tm[0];
        tm[5] += adv * tm[1];
    }

    /// <summary>Negate, flushing -0 to 0 for tidy output.</summary>
    private static double Neg(double v) => v == 0.0 ? 0.0 : -v;

    /// <summary>
    /// Emit a path element with current graphics state. Path data is already in
    /// top-down page coordinates; stroke widths/dashes are scaled by the CTM.
    /// </summary>
    private static void EmitPath(StringBuilder sb, GState gs, string d, bool stroke, bool fill, bool evenOdd)
    {
        var attrs = new StringBuilder();
        attrs.Append($"d=\"{d}\"");

        if (fill)
        {
            var fillColor = FormatHex(gs.FillR, gs.FillG, gs.FillB);
            attrs.Append($" fill=\"{fillColor}\"");
            if (gs.FillAlpha < 1.0)
            {
                attrs.Append($" fill-opacity=\"{F(gs.FillAlpha)}\"");
                // A fully transparent fill is invisible content; keep it from
                // intercepting clicks meant for link anchors beneath it.
                if (gs.FillAlpha == 0.0)
                    attrs.Append(" pointer-events=\"none\"");
            }
            if (evenOdd)
                attrs.Append(" fill-rule=\"evenodd\"");
        }
        else
        {
            attrs.Append(" fill=\"none\"");
        }

        if (stroke)
        {
            var scale = gs.CtmScale;
            var strokeColor = FormatHex(gs.StrokeR, gs.StrokeG, gs.StrokeB);
            attrs.Append($" stroke=\"{strokeColor}\"");
            if (gs.StrokeAlpha < 1.0)
                attrs.Append($" stroke-opacity=\"{F(gs.StrokeAlpha)}\"");
            var sw = gs.LineWidth * scale;
            if (sw != 1.0)
                attrs.Append($" stroke-width=\"{F(sw)}\"");
            if (gs.LineCap != 0)
                attrs.Append($" stroke-linecap=\"{MapLineCap(gs.LineCap)}\"");
            if (gs.LineJoin != 0)
                attrs.Append($" stroke-linejoin=\"{MapLineJoin(gs.LineJoin)}\"");
            if (gs.DashArray.Length > 0)
            {
                attrs.Append($" stroke-dasharray=\"{string.Join(",", gs.DashArray.Select(v => F(v * scale)))}\"");
                if (gs.DashPhase != 0)
                    attrs.Append($" stroke-dashoffset=\"{F(gs.DashPhase * scale)}\"");
            }
        }
        else
        {
            attrs.Append(" stroke=\"none\"");
        }

        if (gs.BlendMode != "Normal")
            attrs.Append($" style=\"mix-blend-mode:{MapBlendMode(gs.BlendMode)}\"");

        // Every path opens with an empty id, an explicit
        // clip-rule and an identity transform; consumers diffing outputs see
        // the same markup shape.
        sb.AppendLine($"<path id=\"\"  clip-rule=\"evenodd\" transform=\"matrix(1 0 0 1 0 0)\" {attrs} />");
    }
}
