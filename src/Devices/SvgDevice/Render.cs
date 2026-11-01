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
        var sv = new SvgRenderState();
        sv.fonts = ResolveFonts(resources, reader);
        sv.extGStates = ResolveExtGStates(resources, reader);
        sv.faces = _faces;
        sv.lexer = new PdfLexer(streamBytes);
        sv.operands = new List<PdfObject>();
        sv.tm = new double[] { 1, 0, 0, 1, 0, 0 };
        sv.tlm = new double[] { 1, 0, 0, 1, 0, 0 };
        sv.gsStack = new Stack<GState>();
        // The caller hands in the inherited graphics state; from here it lives on the
        // render state, because `Q` replaces it wholesale from the stack above.
        sv.gs = gs;
        sv.pathData = new StringBuilder();
        sv.curX = 0;
        sv.curY = 0;

        while (true)
        {
            var token = sv.lexer.NextToken();
            if (token.Kind == TokenKind.Eof) break;

            switch (token.Kind)
            {
                case TokenKind.Integer: sv.operands.Add(new PdfInteger(token.IntValue)); break;
                case TokenKind.Real: sv.operands.Add(new PdfReal(token.RealValue)); break;
                case TokenKind.LiteralString: sv.operands.Add(new PdfString(token.BytesValue!)); break;
                case TokenKind.HexString: sv.operands.Add(new PdfString(token.BytesValue!, isHex: true)); break;
                case TokenKind.Name: sv.operands.Add(new PdfName(token.StringValue!)); break;
                case TokenKind.ArrayStart:
                    sv.operands.Add(ParseArray(sv.lexer));
                    break;
                case TokenKind.Keyword:
                {
                    var op = token.StringValue!;
                    switch (op)
                    {
                        case "q": case "Q": case "cm": case "gs": case "w": case "J": case "j": case "d":
                            if (RenderSvgStateOperator(sv, usedBlendModes, op)) return;
                            break;
                        case "Tf": case "Tc": case "Tw": case "Tz": case "Ts": case "Tr": case "BT": case "Td": case "TD": case "Tm": case "TL": case "T*": case "'": case "Tj": case "TJ":
                            if (RenderSvgTextOperator(sv, sb, reader, links, op)) return;
                            break;
                        case "rg": case "RG": case "g": case "G": case "k": case "K": case "cs": case "CS": case "sc": case "scn": case "SC": case "SCN":
                            if (RenderSvgColorOperator(sv, resources, reader, op)) return;
                            break;
                        // --- Text show: " (set word+char spacing, next line, show) ---
                        case "\"":
                            if (sv.operands.Count >= 3 && sv.operands[2] is PdfString dqs)
                            {
                                sv.gs.WordSpacing = Num(sv.operands[0]);
                                sv.gs.CharSpacing = Num(sv.operands[1]);
                                sv.tlm = MulAffine(new[] { 1.0, 0, 0, 1, 0, -sv.gs.TextLeading }, sv.tlm);
                                Array.Copy(sv.tlm, sv.tm, 6);
                                ShowText(sb, sv.gs, sv.tm, new PdfObject[] { dqs }, reader, links);
                            }
                            break;

                        case "m": case "l": case "c": case "v": case "y": case "h": case "re": case "S": case "s": case "f": case "F": case "f*": case "B": case "B*": case "b": case "b*": case "n": case "W": case "W*": case "Do":
                            if (RenderSvgPathOperator(sv, sb, resources, reader, depth, usedBlendModes, links, op)) return;
                            break;
                        case "sh":
                            RenderShadingOperator(sv, sb, resources, reader);
                            break;
                        case "BI":
                            SkipInlineImage(sv.lexer);
                            sv.operands.Clear();
                            continue;
                    }
                    sv.operands.Clear();
                    break;
                }
                default:
                    sv.operands.Clear();
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
                    : Compat.Latin1.GetString(seg);
                // A glyph whose decode is empty or XML-invalid (unmapped codes often
                // carry U+FFFF from a bfrange to FFFF) still occupies a slot on the
                // line. Substitute the PUA char U+A880 — the "SVG space" — so the
                // character count stays aligned with the x list.
                if (gs.Face is not null) glyph = gs.Face.TextFor(code, glyph);
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
            // Invisible and clip-only text: the pen has advanced, nothing is painted.
            if (!TextModePaintsFill(gs.RenderMode) && !TextModePaintsStroke(gs.RenderMode)) continue;

            EmitTextRun(sb, gs, tm, firstDevice, xs, text.ToString(), links);
        }
    }

    // Text rendering modes (PDF 32000-1 table 106): 0 fill, 1 stroke, 2 fill and stroke,
    // 3 invisible; 4 to 7 the same four, each adding the glyphs to the clip.
    private const int TextModeStroke = 1;
    private const int TextModeFillStroke = 2;
    private const int TextModeInvisible = 3;
    private const int TextModeClipOffset = 4;

    private static bool TextModePaintsFill(int mode)
    {
        var painting = mode >= TextModeClipOffset ? mode - TextModeClipOffset : mode;
        return painting is 0 or TextModeFillStroke;
    }

    private static bool TextModePaintsStroke(int mode)
    {
        var painting = mode >= TextModeClipOffset ? mode - TextModeClipOffset : mode;
        return painting is TextModeStroke or TextModeFillStroke;
    }

    /// <summary>Emit one decoded, positioned text run.</summary>
    private static void EmitTextRun(StringBuilder sb, GState gs, double[] tm,
        double[] firstDevice, List<double> xs, string text, List<LinkRect>? links)
    {
        var fs = gs.FontSize;
        var th = gs.HorizScale;
        var style = new StringBuilder();
        // The render mode decides what paints: an outlined run (stroke only) must not be filled
        // with the fill colour, which hides it when that colour is the background's.
        if (TextModePaintsFill(gs.RenderMode))
        {
            var fill = gs.FillPaint ?? FormatHex(gs.FillR, gs.FillG, gs.FillB, gs.FillAlpha);
            style.Append($"fill:{fill};font-family:{FamilyList(gs)};{FaceStyle(gs)}");
        }
        else
        {
            style.Append($"fill:none;font-family:{FamilyList(gs)};{FaceStyle(gs)}");
        }
        if (TextModePaintsStroke(gs.RenderMode))
        {
            var stroke = FormatHex(gs.StrokeR, gs.StrokeG, gs.StrokeB, gs.StrokeAlpha);
            // The pen width is in user space: the CTM scales it, the text matrix does not.
            style.Append($"stroke:{stroke};stroke-width:{F(gs.LineWidth * gs.CtmScale)};");
        }
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
            sb.AppendLine($"<text{ClipAttribute(gs)} x=\"{xList}\" y=\"{FD(d[5])}\"{SpaceAttribute(text)} style=\"{style}\">{text}</text>");
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
            sb.AppendLine($"<text{ClipAttribute(gs)} transform=\"{transform}\"{SpaceAttribute(text)} style=\"{style}\">{text}</text>");
            if (link is not null) sb.AppendLine("</a>");
        }
    }

    /// <summary>The run's font-family list: the page's web font first when the font has one, the
    /// generic family after it for any character the face does not carry.</summary>
    private static string FamilyList(GState gs) =>
        gs.Face is null ? gs.FontName : $"'{gs.Face.Family}',{gs.FontName}";

    /// <summary>The slant and weight a run asks for when its face is an installed stand-in of that
    /// style, so the browser matches the face instead of synthesising the style over it.</summary>
    private static string FaceStyle(GState gs) =>
        (gs.Face is { Italic: true } ? "font-style:italic;" : "") + (gs.Face is { Bold: true } ? "font-weight:bold;" : "");

    /// <summary>The xml:space attribute a run needs: each glyph has its own x, so a tab, a line
    /// break, a leading, trailing or doubled space must stay one character, not be collapsed
    /// by the viewer's default whitespace handling, or every glyph after it shifts.</summary>
    private static string SpaceAttribute(string text)
    {
        var collapsible = text.Contains('\t') || text.Contains('\n') || text.Contains('\r')
            || text.Contains("  ", StringComparison.Ordinal)
            || (text.Length > 0 && (text[0] == ' ' || text[^1] == ' '));
        return collapsible ? " xml:space=\"preserve\"" : "";
    }

    /// <summary>True when a decoded run has no visible glyphs (whitespace and the
    /// U+A880 blank-glyph placeholder only).</summary>
    private static bool IsBlankRun(string text)
    {
        return !text.Any(ch => !char.IsWhiteSpace(ch) && ch != 'ꢀ');
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
            // A shading pattern set as the fill paints its gradient in place of the flat colour.
            var fillColor = gs.FillPaint ?? FormatOpaqueHex(gs.FillR, gs.FillG, gs.FillB);
            attrs.Append($" fill=\"{fillColor}\"");
            // The colour is opaque: the alpha is the companion attribute alone (a corpus assertion
            // counts 16 of `fill-opacity="0" pointer-events="none"`).
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
            var strokeColor = FormatOpaqueHex(gs.StrokeR, gs.StrokeG, gs.StrokeB);
            attrs.Append($" stroke=\"{strokeColor}\"");
            if (gs.StrokeAlpha < 1.0)
                attrs.Append($" stroke-opacity=\"{F(gs.StrokeAlpha)}\"");
            var sw = gs.LineWidth * scale;
#pragma warning disable S1244 // only the exact default value is skipped - any other value is written as is
            if (sw != 1.0)
#pragma warning restore S1244
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

        // Every path opens with an explicit clip-rule and an identity transform;
        // consumers diffing outputs see the same markup shape.
        sb.AppendLine($"<path{ClipAttribute(gs)}  clip-rule=\"evenodd\" transform=\"matrix(1 0 0 1 0 0)\" {attrs} />");
    }
}
