using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Forms;

public sealed partial class RichTextBoxField
{
    /// <summary>A word of a line with the style it is drawn in and its measured advance.</summary>
    private sealed record RichWord(string Text, RichStyle Style, double Width);

    /// <summary>The state of one appearance being written: the fonts it names and the glyph widths it measured.</summary>
    private sealed class RichEmitState
    {
        public readonly Dictionary<string, (string Res, PdfDictionary Font)> Fonts = new(StringComparer.Ordinal);
        public readonly Dictionary<string, int[]?> Widths = new(StringComparer.Ordinal);
        public readonly StringBuilder Content = new();
        public string? CurrentRes;
        public double CurrentSize;
        public string CurrentColor = string.Empty;
        public double X;
    }

    /// <summary>Builds the /AP /N stream of the hard lines: each is wrapped to the box at its spaces, every word
    /// is placed with its own text move so its position is explicit, the font changes with the style and the
    /// colour with the run. Lines step down by a pitch of the largest type size on the line.</summary>
    private void RegenerateMarkupAppearance(List<RichLine> lines)
    {
        double w = 100, h = 20;
        if (Reader.Resolve(Dict.Get("Rect")) is PdfArray rectArr && rectArr.Count >= 4)
        {
            var r = Rectangle.FromPdfArray(rectArr, Reader);
            w = r.URX - r.LLX; h = r.URY - r.LLY;
        }
        var st = new RichEmitState { CurrentColor = ExtractDaColor(ResolveInheritedDa() ?? "", "0 0 0 rg") };
        st.Content.Append("/Tx BMC\nq\nBT\n").Append(st.CurrentColor).Append('\n');
        var avail = Math.Max(1, w - 2 * RichSideInset);
        double y = 0;
        var first = true;
        foreach (var line in lines)
        {
            foreach (var visual in WrapRichLine(line, avail, st))
            {
                var size = visual.Count == 0 ? line.Pieces.FirstOrDefault()?.Style.Size ?? DefaultRichSize : visual.Max(word => word.Style.Size);
                var pitch = size * RichLinePitchEm;
                var lineWidth = visual.Sum(word => word.Width);
                var x = line.Align switch
                {
                    "center" => RichSideInset + (avail - lineWidth) / 2,
                    "right" => RichSideInset + avail - lineWidth,
                    _ => RichSideInset,
                };
                if (first) { y = h - pitch + RichDescentEm * size; st.Content.Append($"{Format(x)} {Format(y)} Td\n"); first = false; }
                else st.Content.Append($"{Format(x - st.X)} {Format(-pitch)} Td\n");
                st.X = x;
                EmitRichWords(visual, st);
            }
        }
        st.Content.Append("ET\nQ\nEMC\n");
        WriteMarkupAppearance(st, w, h);
    }

    /// <summary>Cuts a hard line into the lines that fit the width: words go on the line while they fit, a word's
    /// trailing spaces may hang past the edge, and a word wider than the box stands alone.</summary>
    private static List<List<RichWord>> WrapRichLine(RichLine line, double avail, RichEmitState st)
    {
        var result = new List<List<RichWord>>();
        var current = new List<RichWord>();
        double used = 0;
        foreach (var piece in line.Pieces)
        {
            foreach (Match m in SafeRegex.Matches(piece.Text, @"\S+\s*|\s+"))
            {
                var word = new RichWord(m.Value, piece.Style, RichTextWidth(m.Value, piece.Style, st));
                var fit = RichTextWidth(m.Value.TrimEnd(), piece.Style, st);
                if (current.Count > 0 && used + fit > avail)
                {
                    result.Add(current);
                    current = new List<RichWord>();
                    used = 0;
                }
                current.Add(word);
                used += word.Width;
            }
        }
        result.Add(current);
        return result;
    }

    private void EmitRichWords(List<RichWord> words, RichEmitState st)
    {
        var sb = st.Content;
        for (var i = 0; i < words.Count; i++)
        {
            var word = words[i];
            if (i > 0) { sb.Append($"{Format(words[i - 1].Width)} 0 Td\n"); st.X += words[i - 1].Width; }
            var color = word.Style.Color ?? ExtractDaColor(ResolveInheritedDa() ?? "", "0 0 0 rg");
            if (color != st.CurrentColor) { sb.Append(color).Append('\n'); st.CurrentColor = color; }
            var res = RichFontResource(word.Style, st);
            if (res != st.CurrentRes || Math.Abs(word.Style.Size - st.CurrentSize) > 0.001)
            {
                sb.Append($"/{res} {Format(word.Style.Size)} Tf\n");
                (st.CurrentRes, st.CurrentSize) = (res, word.Style.Size);
            }
            var esc = word.Text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
            sb.Append($"({esc}) Tj\n");
        }
    }

    private void WriteMarkupAppearance(RichEmitState st, double w, double h)
    {
        var apStream = new PdfStream(new PdfDictionary(), Aspose.Pdf.Text.Cp1252.GetBytes(st.Content.ToString()));
        apStream.Dict.Set("Type", new PdfName("XObject"));
        apStream.Dict.Set("Subtype", new PdfName("Form"));
        var bbox = new PdfArray();
        bbox.Add(new PdfReal(0)); bbox.Add(new PdfReal(0)); bbox.Add(new PdfReal(w)); bbox.Add(new PdfReal(h));
        apStream.Dict.Set("BBox", bbox);
        var fonts = new PdfDictionary();
        foreach (var (res, font) in st.Fonts.Values) fonts.Set(res, font);
        var resources = new PdfDictionary();
        resources.Set("Font", fonts);
        apStream.Dict.Set("Resources", resources);
        var newAp = new PdfDictionary();
        newAp.Set("N", apStream);
        Dict.Set("AP", newAp);
    }

    /// <summary>The resource name of the face a style draws in, made on first use. The default family's faces
    /// are its Standard-14 siblings; another family's regular face is named verbatim and a styled one carries a
    /// style comma under a subset tag, so a reader reports the styled family exactly.</summary>
    private static string RichFontResource(RichStyle style, RichEmitState st)
    {
        var key = style.Family + "|" + (style.Bold ? "B" : "") + (style.Italic ? "I" : "");
        if (st.Fonts.TryGetValue(key, out var known)) return known.Res;
        var styled = (style.Bold, style.Italic) switch { (true, true) => "BoldItalic", (true, false) => "Bold", (false, true) => "Italic", _ => "" };
        string baseFont;
        if (IsHelveticaFamily(style.Family))
            baseFont = "Helvetica" + (style.Bold, style.Italic) switch { (true, true) => "-BoldOblique", (true, false) => "-Bold", (false, true) => "-Oblique", _ => "" };
        else baseFont = styled.Length == 0 ? style.Family : RichSubsetTag() + "+" + style.Family + "," + styled;
        var res = styled.Length == 0 && !st.Fonts.Values.Any(f => f.Res == "FR") ? "FR"
            : styled == "Bold" && !st.Fonts.Values.Any(f => f.Res == "FB") ? "FB"
            : "F" + (st.Fonts.Count + 1).ToString(CultureInfo.InvariantCulture);
        st.Fonts[key] = (res, MakeRichFont(baseFont));
        return res;
    }

    private static bool IsHelveticaFamily(string family) =>
        family.Equals("Helvetica", StringComparison.OrdinalIgnoreCase) || family.Equals("Helv", StringComparison.OrdinalIgnoreCase);

    /// <summary>The Standard-14 face whose metrics measure a family, or null for a family outside them.</summary>
    private static string? RichStandardFace(RichStyle style)
    {
        var family = style.Family.Replace(" ", "").ToLowerInvariant();
        var sans = family is "helvetica" or "helv" or "arial" or "arialmt";
        var serif = family is "times" or "timesnewroman" or "tiro" or "times-roman" or "timesnewromanpsmt";
        var mono = family is "courier" or "couriernew" or "cour" or "couriernewpsmt";
        if (!sans && !serif && !mono) return null;
        var root = sans ? "Helvetica" : serif ? "Times" : "Courier";
        return (sans || mono, style.Bold, style.Italic) switch
        {
            (true, true, true) => root + "-BoldOblique",
            (true, true, false) => root + "-Bold",
            (true, false, true) => root + "-Oblique",
            (true, false, false) => root,
            (false, true, true) => "Times-BoldItalic",
            (false, true, false) => "Times-Bold",
            (false, false, true) => "Times-Italic",
            _ => "Times-Roman",
        };
    }

    /// <summary>The advance of a text in a style: Standard-14 metrics for the families they cover, the installed
    /// face's for another family, and half an em for a glyph neither knows.</summary>
    private static double RichTextWidth(string text, RichStyle style, RichEmitState st)
    {
        const double UnknownGlyphEm = 0.5;
        var standard = RichStandardFace(style);
        int[]? widths = null;
        if (standard is null)
        {
            var key = style.Family + (style.Bold ? "-Bold" : "") + (style.Italic ? "-Italic" : "");
            if (!st.Widths.TryGetValue(key, out widths))
            {
                var data = Aspose.Pdf.Text.SystemFontResolver.Resolve(key);
                widths = data is { Length: > 0 } ? Aspose.Pdf.Text.FontRepository.ReadTtfMetrics(data).widths : null;
                st.Widths[key] = widths;
            }
        }
        double em = 0;
        foreach (var c in text)
        {
            var width = standard is not null ? Aspose.Pdf.Text.Standard14Fonts.GetWidth(standard, c)
                : widths is not null && c < widths.Length ? widths[c] : -1;
            em += width > 0 ? width / 1000.0 : UnknownGlyphEm;
        }
        return em * style.Size;
    }
}
