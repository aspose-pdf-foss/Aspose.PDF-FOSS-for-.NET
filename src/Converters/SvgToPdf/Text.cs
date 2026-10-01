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
        var sx = new SvgTextRenderState();
        sx.elem = elem;
        sx.ctx = ctx;
        sx.style = style;
        sx.ctm = ctm;
        sx.sb = sx.ctx.Surface.Sb;
        sx.sb.Append("q\n");
        sx.transform = sx.elem.GetAttribute("transform");
        sx.tmMatrix = ParseMatrixOnly(sx.transform);
        // The round-trip shortcut below only applies to THIS library's own PDF-to-SVG
        // export, which never pairs a text transform with x/y (position lives entirely
        // in the matrix's e/f - see SvgDevice.EmitTextRun's rotated-text branch). A
        // third-party SVG (Raphael, D3, ...) commonly carries BOTH an ordinary x/y and
        // an (often identity) transform="matrix(...)" on the same <text> - for that
        // shape the matrix is just another CTM term, not the position, and treating it
        // as the round-trip case drops x/y entirely and stacks every run at the origin.
        if (sx.tmMatrix is not null && (sx.elem.HasAttribute("x") || sx.elem.HasAttribute("y")))
            sx.tmMatrix = null;
        sx.newCtm = sx.tmMatrix is null ? ApplyTransform(sx.elem, sx.sb, sx.ctm) : sx.ctm;
        ApplyClipPath(sx.style, sx.ctx, sx.newCtm);
        ApplyMask(sx.style, sx.ctx, sx.newCtm);

        sx.curX = GetFirstLen(sx.elem, "x", sx.ctx.VpW);
        sx.curY = GetFirstLen(sx.elem, "y", sx.ctx.VpH);

        WalkSvgTspans(sx, sx.elem, sx.style);
        sx.sb.Append("Q\n");
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
            if (ch > 0x7F && Text.Cp1252.TryGetByte(ch) is null) return true;
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
                if (ch <= 0x7F || Text.Cp1252.TryGetByte(ch) is not null) continue;
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
