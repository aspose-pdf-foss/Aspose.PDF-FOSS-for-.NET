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
            // A family the DOCUMENT itself ships (@font-face, inline or from a linked
            // stylesheet) is embedded and used as-is — that is the whole point of
            // shipping it, and a Standard-14 substitute would draw the wrong typeface.
            byte[]? uniTtf = ResolveDeclaredFace(ctx, Prop(runStyle, "font-family"));
            var declaredFaceName = uniTtf is null ? null : DeclaredFaceName(ctx, Prop(runStyle, "font-family"));
            if (uniTtf is null && NeedsUnicodeSvg(text)) uniTtf = ResolveSvgUnicodeTtf(text);
            var display = text;
            if (uniTtf is not null)
                display = IsPureRtlSvg(text) ? ToVisualRtlSvg(text)
                    : Text.BidiReorderer.ContainsRtl(text) ? VisualizeMixedRtlSvg(text) : text;

            var baseFont = MapFont(runStyle);
            var fontDict = GetOrCreate(ctx.Surface.Resources, "Font");
            var fontRes = uniTtf is null ? EnsureFontResource(ctx, baseFont) : "";

            var embedName = declaredFaceName ?? "SvgUni";
            var width = uniTtf is null
                ? MeasureText(text, baseFont, fontSize)
                : Text.Type0FontEmbedder.MeasureText(fontDict, uniTtf, embedName, display, fontSize,
                    stripSpacesInBaseFont: true, resNameHint: NextCompositeResName(fontDict));
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
                    var (rn, hex) = Text.Type0FontEmbedder.Embed(fontDict, uniTtf, embedName, display,
                        stripSpacesInBaseFont: true, resNameHint: NextCompositeResName(fontDict));
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

    /// <summary>The first family in the list the document itself declares an @font-face
    /// for, or null. The family list is honoured in order, as CSS specifies.</summary>
    private static string? DeclaredFaceName(Ctx ctx, string familyList)
    {
        if (ctx.DeclaredFaces.Count == 0) return null;
        foreach (var raw in familyList.Split(','))
        {
            var f = raw.Trim().Trim('"', '\'').Trim();
            if (f.Length > 0 && ctx.DeclaredFaces.ContainsKey(f)) return f;
        }
        return null;
    }

    /// <summary>The font program for <see cref="DeclaredFaceName"/>, or null.</summary>
    private static byte[]? ResolveDeclaredFace(Ctx ctx, string familyList) =>
        DeclaredFaceName(ctx, familyList) is { } name ? ctx.DeclaredFaces[name] : null;

    /// <summary>Resource name for the next composite (Type0) font on a surface.
    /// LAW: SVG conversion names them <c>C0_0</c>, <c>C1_0</c>, …
    /// — the name a consumer indexes the converted page's font collection by.</summary>
    private static string NextCompositeResName(PdfDictionary fontDict)
    {
        var n = 0;
        while (fontDict.ContainsKey($"C{n}_0")) n++;
        return $"C{n}_0";
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
                    ttf = Text.FontRepository.TryFindFont(name)?.SourceFontData?.TtfData;
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
}
