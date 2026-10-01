using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Font face sheet: one paragraph measured, wrapped and emitted in its declared face.</summary>
    private static bool LayoutFontFaceParagraph(FontFaceSheetState ff, string text, string pCls, string divCls, bool boldTag)
    {
        var famName = PropOf(ff, pCls, divCls, "font-family").Trim().Trim('"', '\'');
        if (!ff.faces.TryGetValue(famName, out var famFaces) || famFaces.Count == 0) return false;
        var face = famFaces[0];
        if (boldTag)
            foreach (var f in famFaces)
                if (f.Bold && !f.Italic) { face = f; break; }

        var size = Pt(ff, PropOf(ff, pCls, divCls, "font-size"));
        if (size <= 0) size = 12;
        var color = ParseCssColorRgb(PropOf(ff, pCls, divCls, "color")) ?? (0, 0, 0);
        var centered = PropOf(ff, pCls, divCls, "text-align").StartsWith("center", System.StringComparison.OrdinalIgnoreCase);
        var mt = Pt(ff, PropOf(ff, pCls, divCls, "margin-top"));
        var mb = Pt(ff, PropOf(ff, pCls, divCls, "margin-bottom"));
        var pb = Pt(ff, PropOf(ff, pCls, divCls, "padding-bottom"));
        var borderDecl = PropOf(ff, pCls, divCls, "border-bottom");
        var borderW = Pt(ff, borderDecl);
        var borderColor = borderDecl.Length > 0
            ? ParseCssColorRgb(Regex.Match(borderDecl, @"(rgba?\([^)]*\)|#[0-9a-fA-F]{3,6})").Value)
            : null;

        // Adjacent vertical margins collapse to the larger.
        ff.flowTop += System.Math.Max(ff.pendingMargin, mt);
        ff.pendingMargin = mb;

        var lineH = FontFaceSheetLineHeightEm * size;
        var halfLead = (FontFaceSheetLineHeightEm - (face.WinAsc + face.WinDesc)) * size / 2.0;

        // Greedy word wrap at the content width.
        var words = text.Split(' ');
        var lines = new System.Collections.Generic.List<string>();
        var cur = new StringBuilder();
        foreach (var word in words)
        {
            var candidate = cur.Length == 0 ? word : cur + " " + word;
            if (cur.Length > 0 && MeasureParsedExact(face.Parser, face.Upm, candidate, size) > ff.contentW)
            {
                lines.Add(cur.ToString());
                cur.Clear().Append(word);
            }
            else
            {
                cur.Clear().Append(candidate);
            }
        }
        if (cur.Length > 0) lines.Add(cur.ToString());

        foreach (var line in lines)
        {
            if (ff.flowTop + lineH > ff.pageH - FontFaceSheetTopMargin && ff.sb.Length > 0)
            {
                ff.pg.AddContentStream(Encoding.ASCII.GetBytes(ff.sb.ToString()));
                ff.sb.Clear();
                ff.pg = ff.doc.Pages.Add(ff.pageW, ff.pageH);
                EnsureFonts(ff.pg, ff.fontDict);
                ff.flowTop = FontFaceSheetTopMargin;
            }
            var lineWidth = MeasureParsedExact(face.Parser, face.Upm, line, size);
            var x = centered ? ff.left + (ff.contentW - lineWidth) / 2.0 : ff.left;
            var baselineTop = ff.flowTop + halfLead + face.WinAsc * size;
            var y = ff.pageH - baselineTop;
            var (rn, hex) = Text.Type0FontEmbedder.Embed(ff.fontDict, face.Ttf, famName, line, stripSpacesInBaseFont: true);
            ff.sb.Append("BT ");
            ff.sb.Append($"/{rn} {size.ToString("F1", ff.inv)} Tf ");
            ff.sb.Append($"{color.r.ToString("F3", ff.inv)} {color.g.ToString("F3", ff.inv)} {color.b.ToString("F3", ff.inv)} rg ");
            ff.sb.Append($"1 0 0 1 {x.ToString("F2", ff.inv)} {y.ToString("F2", ff.inv)} Tm ");
            ff.sb.Append('<').Append(Compat.ToHexString(hex)).Append("> Tj ");
            ff.sb.AppendLine("ET");
            ff.flowTop += lineH;
        }

        ff.flowTop += pb;
        if (borderW > 0 && borderColor is { } bc)
        {
            var yb = ff.pageH - (ff.flowTop + borderW / 2.0);
            ff.sb.Append($"q {bc.r.ToString("F3", ff.inv)} {bc.g.ToString("F3", ff.inv)} {bc.b.ToString("F3", ff.inv)} RG ");
            ff.sb.Append($"{borderW.ToString("F2", ff.inv)} w ");
            ff.sb.Append($"{ff.left.ToString("F2", ff.inv)} {yb.ToString("F2", ff.inv)} m ");
            ff.sb.Append($"{(ff.left + ff.contentW).ToString("F2", ff.inv)} {yb.ToString("F2", ff.inv)} l S Q");
            ff.sb.AppendLine();
            ff.flowTop += borderW;
        }
        return true;
    }

    /// <summary>Font face sheet: the @font-face rules read into the face table.</summary>
    private static void ParseFontFaceRules(FontFaceSheetState ff)
    {
        foreach (Match m in Regex.Matches(ff.css, @"@font-face\s*\{(?<body>[^}]*)\}", RegexOptions.Singleline))
        {
            var body = m.Groups["body"].Value;
            var fam = Regex.Match(body, @"font-family:\s*""?(?<v>[^;""}]+)");
            var src = Regex.Match(body, @"url\(""?(?<u>[^)""]+?)""?\)");
            if (!fam.Success || !src.Success) continue;
            var bytes = LoadConverterImage(src.Groups["u"].Value, ff.options);
            if (bytes is null || bytes.Length < 4) continue;
            var sfnt = TryReadWoff(bytes) ?? (bytes[0] == 0x00 && bytes[1] == 0x01 ? bytes : null);
            if (sfnt is null) continue;
            SheetFace face;
            try
            {
                var parser = new Text.GlyphOutlineParser(sfnt);
                face = new SheetFace
                {
                    Ttf = sfnt,
                    Parser = parser,
                    Upm = parser.UnitsPerEm > 0 ? parser.UnitsPerEm : 1000,
                    Bold = Regex.IsMatch(body, @"font-weight:\s*(bold|[7-9]00)", RegexOptions.IgnoreCase),
                    Italic = Regex.IsMatch(body, @"font-style:\s*italic", RegexOptions.IgnoreCase),
                };
                ReadWinMetrics(sfnt, face);
            }
            catch { continue; }
            if (!ff.faces.TryGetValue(fam.Groups["v"].Value.Trim(), out var list))
                ff.faces[fam.Groups["v"].Value.Trim()] = list = new System.Collections.Generic.List<SheetFace>();
            list.Add(face);
        }
    }
}
