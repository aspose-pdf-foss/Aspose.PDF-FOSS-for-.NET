using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    // ── The authored-width @font-face sheet ─────────────────────────────────
    //
    // A hand-authored document that declares its OWN faces (@font-face with
    // relative url() sources), pins the body width in points, and styles its
    // paragraphs through class rules (colour, centering, border-bottom bands).
    //
    // Measured on the faux-bolding fixture:
    //  - page width = side margin + body width + side margin (90 + 612 + 90 =
    //    792), page height stays the default sheet (842); flow starts at a
    //    72 pt top margin;
    //  - a text line is a CSS line box on the face's WIN metrics: line height
    //    1.2 em, half-leading (1.2 − (winAsc+winDesc)) · size / 2, baseline at
    //    halfLead + winAsc · size from the box top (reproduces baselines
    //    117.42 / 139.92 to 0.001 with ITCFrankGoth 0.934/0.250);
    //  - adjacent block margins COLLAPSE to the larger (the div's 36 swallows
    //    the paragraph's 5); padding-bottom then border-bottom follow the line
    //    box (border stroked at its centre line, full content width);
    //  - a <b> run selects the family's own bold-declared @font-face source —
    //    the same program when the author mapped bold to the regular file, so
    //    no synthetic bolding ever happens.
    private const double FontFaceSheetSideMargin = 90.0;
    private const double FontFaceSheetTopMargin = 72.0;
    private const double FontFaceSheetLineHeightEm = 1.2;

    private sealed class SheetFace
    {
        public byte[] Ttf = System.Array.Empty<byte>();
        public Text.GlyphOutlineParser Parser = null!;
        public double Upm = 1000;
        public double WinAsc = 0.75;   // em fractions
        public double WinDesc = 0.25;
        public bool Bold;
        public bool Italic;
    }

    private static Document? TryRenderFontFaceSheet(string html, HtmlLoadOptions? options,
        double defaultPageHeight)
    {
        // ── fingerprint ──
        var styleText = new StringBuilder();
        foreach (Match sm in Regex.Matches(html, @"<style[^>]*>(?<b>[\s\S]*?)</style\s*>", RegexOptions.IgnoreCase))
            styleText.Append(sm.Groups["b"].Value).Append('\n');
        var css = styleText.ToString();
        if (!css.Contains("@font-face", System.StringComparison.OrdinalIgnoreCase)) return null;
        // data-URI faces belong to the styled-class data-font flow (StyledDoc.cs) —
        // this arm takes only the FILE-sourced variant of the shape.
        if (Regex.IsMatch(css, @"@font-face[^}]*url\(\s*[""']?data:", RegexOptions.Singleline)) return null;
        var bodyW = Regex.Match(css, @"body\s*\{[^}]*?width:\s*(?<w>[\d.]+)pt", RegexOptions.Singleline);
        if (!bodyW.Success) return null;
        if (Regex.IsMatch(html, @"<(table|img|input|ul|ol)\b", RegexOptions.IgnoreCase)) return null;

        // ── own faces, with their weight/style declarations ──
        var faces = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<SheetFace>>(
            System.StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(css, @"@font-face\s*\{(?<body>[^}]*)\}", RegexOptions.Singleline))
        {
            var body = m.Groups["body"].Value;
            var fam = Regex.Match(body, @"font-family:\s*""?(?<v>[^;""}]+)");
            var src = Regex.Match(body, @"url\(""?(?<u>[^)""]+?)""?\)");
            if (!fam.Success || !src.Success) continue;
            var bytes = LoadConverterImage(src.Groups["u"].Value, options);
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
            if (!faces.TryGetValue(fam.Groups["v"].Value.Trim(), out var list))
                faces[fam.Groups["v"].Value.Trim()] = list = new System.Collections.Generic.List<SheetFace>();
            list.Add(face);
        }
        if (faces.Count == 0) return null;

        // ── class rules (selector → declarations), attribute selectors matched
        //    against the element chain the document actually has ──
        var rules = new System.Collections.Generic.List<(string Selector, string Body)>();
        foreach (Match m in Regex.Matches(css, @"(?<sel>[^{}@]+)\{(?<body>[^}]*)\}", RegexOptions.Singleline))
            foreach (var sel in m.Groups["sel"].Value.Split(','))
                rules.Add((sel.Trim(), m.Groups["body"].Value));

        // ── body blocks: paragraphs inside optional div wrappers ──
        var bodyM = Regex.Match(html, @"<body[^>]*>(?<b>[\s\S]*?)</body\s*>", RegexOptions.IgnoreCase);
        if (!bodyM.Success) return null;
        var bodyInner = bodyM.Groups["b"].Value;

        var paras = new System.Collections.Generic.List<(string Text, string PClass, string DivClasses, bool BoldTag)>();
        foreach (Match dm in Regex.Matches(bodyInner, @"<div(?<dattrs>[^>]*)>(?<dbody>[\s\S]*?)</div\s*>", RegexOptions.IgnoreCase))
        {
            var divCls = Regex.Match(dm.Groups["dattrs"].Value, @"class=""(?<c>[^""]*)""").Groups["c"].Value;
            foreach (Match pm in Regex.Matches(dm.Groups["dbody"].Value, @"<p(?<pattrs>[^>]*)>(?<pbody>[\s\S]*?)</p\s*>", RegexOptions.IgnoreCase))
            {
                var pCls = Regex.Match(pm.Groups["pattrs"].Value, @"class=""(?<c>[^""]*)""").Groups["c"].Value;
                var inner = pm.Groups["pbody"].Value;
                var boldTag = Regex.IsMatch(inner, @"^\s*<(b|strong)\b", RegexOptions.IgnoreCase);
                var text = Regex.Replace(inner, "<[^>]+>", "");
                text = Regex.Replace(DecodeEntities(text), @"\s+", " ").Trim();
                if (text.Length == 0) continue;
                paras.Add((text, pCls, divCls, boldTag));
            }
        }
        if (paras.Count == 0) return null;

        // Resolve a paragraph's rule chain: any selector whose last simple part is
        // p.<class> and whose ancestor parts each match the wrapper's classes.
        string PropOf(string pClass, string divClasses, string prop)
        {
            string? value = null;
            foreach (var (sel, body) in rules)
            {
                var parts = sel.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;
                var last = parts[^1];
                var match = false;
                foreach (var cls in pClass.Split(' ', System.StringSplitOptions.RemoveEmptyEntries))
                    if (last == "p." + cls || last == "." + cls) match = true;
                if (!match) continue;
                var ancestorsOk = true;
                for (var i = 0; i < parts.Length - 1; i++)
                {
                    var anc = parts[i].TrimStart('.');
                    var ok = false;
                    foreach (var cls in divClasses.Split(' ', System.StringSplitOptions.RemoveEmptyEntries))
                        if (anc == cls) ok = true;
                    if (!ok) { ancestorsOk = false; break; }
                }
                if (!ancestorsOk) continue;
                var pv = Regex.Match(body, prop + @":\s*(?<v>[^;}]+)");
                if (pv.Success) value = pv.Groups["v"].Value.Trim();
            }
            return value ?? "";
        }

        double Pt(string v) => Regex.Match(v, @"(?<n>-?[\d.]+)pt") is { Success: true } pm2
            ? double.Parse(pm2.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;

        // The `body > :first-child { margin-top }` rule — a candidate margin the
        // first block collapses with.
        var firstChildMt = 0.0;
        var fcm = Regex.Match(css, @"body\s*>\s*:first-child\s*\{[^}]*margin-top:\s*(?<v>[\d.]+)pt", RegexOptions.Singleline);
        if (fcm.Success) firstChildMt = double.Parse(fcm.Groups["v"].Value, System.Globalization.CultureInfo.InvariantCulture);

        // ── layout ──
        var contentW = double.Parse(bodyW.Groups["w"].Value, System.Globalization.CultureInfo.InvariantCulture);
        var pageW = FontFaceSheetSideMargin * 2 + contentW;
        var pageH = defaultPageHeight;
        var left = FontFaceSheetSideMargin;

        var doc = new Document();
        var fontDict = new Core.PdfDictionary();
        var pg = doc.Pages.Add(pageW, pageH);
        EnsureFonts(pg, fontDict);
        var sb = new StringBuilder();
        var inv = System.Globalization.CultureInfo.InvariantCulture;

        var flowTop = FontFaceSheetTopMargin;   // top-down flow position
        var pendingMargin = firstChildMt;       // collapsed margin awaiting the next block

        foreach (var (text, pCls, divCls, boldTag) in paras)
        {
            var famName = PropOf(pCls, divCls, "font-family").Trim().Trim('"', '\'');
            if (!faces.TryGetValue(famName, out var famFaces) || famFaces.Count == 0) return null;
            var face = famFaces[0];
            if (boldTag)
                foreach (var f in famFaces)
                    if (f.Bold && !f.Italic) { face = f; break; }

            var size = Pt(PropOf(pCls, divCls, "font-size"));
            if (size <= 0) size = 12;
            var color = ParseCssColorRgb(PropOf(pCls, divCls, "color")) ?? (0, 0, 0);
            var centered = PropOf(pCls, divCls, "text-align").StartsWith("center", System.StringComparison.OrdinalIgnoreCase);
            var mt = Pt(PropOf(pCls, divCls, "margin-top"));
            var mb = Pt(PropOf(pCls, divCls, "margin-bottom"));
            var pb = Pt(PropOf(pCls, divCls, "padding-bottom"));
            var borderDecl = PropOf(pCls, divCls, "border-bottom");
            var borderW = Pt(borderDecl);
            var borderColor = borderDecl.Length > 0
                ? ParseCssColorRgb(Regex.Match(borderDecl, @"(rgba?\([^)]*\)|#[0-9a-fA-F]{3,6})").Value)
                : null;

            // Adjacent vertical margins collapse to the larger.
            flowTop += System.Math.Max(pendingMargin, mt);
            pendingMargin = mb;

            var lineH = FontFaceSheetLineHeightEm * size;
            var halfLead = (FontFaceSheetLineHeightEm - (face.WinAsc + face.WinDesc)) * size / 2.0;

            // Greedy word wrap at the content width.
            var words = text.Split(' ');
            var lines = new System.Collections.Generic.List<string>();
            var cur = new StringBuilder();
            foreach (var word in words)
            {
                var candidate = cur.Length == 0 ? word : cur + " " + word;
                if (cur.Length > 0 && MeasureParsedExact(face.Parser, face.Upm, candidate, size) > contentW)
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
                if (flowTop + lineH > pageH - FontFaceSheetTopMargin && sb.Length > 0)
                {
                    pg.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
                    sb.Clear();
                    pg = doc.Pages.Add(pageW, pageH);
                    EnsureFonts(pg, fontDict);
                    flowTop = FontFaceSheetTopMargin;
                }
                var lineWidth = MeasureParsedExact(face.Parser, face.Upm, line, size);
                var x = centered ? left + (contentW - lineWidth) / 2.0 : left;
                var baselineTop = flowTop + halfLead + face.WinAsc * size;
                var y = pageH - baselineTop;
                var (rn, hex) = Text.Type0FontEmbedder.Embed(fontDict, face.Ttf, famName, line, stripSpacesInBaseFont: true);
                sb.Append("BT ");
                sb.Append($"/{rn} {size.ToString("F1", inv)} Tf ");
                sb.Append($"{color.r.ToString("F3", inv)} {color.g.ToString("F3", inv)} {color.b.ToString("F3", inv)} rg ");
                sb.Append($"1 0 0 1 {x.ToString("F2", inv)} {y.ToString("F2", inv)} Tm ");
                sb.Append('<').Append(System.Convert.ToHexString(hex)).Append("> Tj ");
                sb.AppendLine("ET");
                flowTop += lineH;
            }

            flowTop += pb;
            if (borderW > 0 && borderColor is { } bc)
            {
                var yb = pageH - (flowTop + borderW / 2.0);
                sb.Append($"q {bc.r.ToString("F3", inv)} {bc.g.ToString("F3", inv)} {bc.b.ToString("F3", inv)} RG ");
                sb.Append($"{borderW.ToString("F2", inv)} w ");
                sb.Append($"{left.ToString("F2", inv)} {yb.ToString("F2", inv)} m ");
                sb.Append($"{(left + contentW).ToString("F2", inv)} {yb.ToString("F2", inv)} l S Q");
                sb.AppendLine();
                flowTop += borderW;
            }
        }

        if (sb.Length > 0) pg.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
        PruneUnusedFonts(doc);
        return doc;
    }

    /// <summary>OS/2 usWinAscent/usWinDescent as em fractions (falling back to
    /// hhea ascent/descent when the table is absent).</summary>
    private static void ReadWinMetrics(byte[] sfnt, SheetFace face)
    {
        try
        {
            int U16(int o) => (sfnt[o] << 8) | sfnt[o + 1];
            var num = U16(4);
            int os2 = -1, hhea = -1;
            for (var i = 0; i < num; i++)
            {
                var rec = 12 + 16 * i;
                var tag = Encoding.ASCII.GetString(sfnt, rec, 4);
                var off = (sfnt[rec + 8] << 24) | (sfnt[rec + 9] << 16) | (sfnt[rec + 10] << 8) | sfnt[rec + 11];
                if (tag == "OS/2") os2 = off;
                else if (tag == "hhea") hhea = off;
            }
            if (os2 >= 0 && os2 + 78 <= sfnt.Length)
            {
                face.WinAsc = U16(os2 + 74) / face.Upm;
                face.WinDesc = U16(os2 + 76) / face.Upm;
            }
            else if (hhea >= 0 && hhea + 10 <= sfnt.Length)
            {
                short S16(int o) => (short)U16(o);
                face.WinAsc = S16(hhea + 4) / face.Upm;
                face.WinDesc = System.Math.Abs(S16(hhea + 6)) / face.Upm;
            }
        }
        catch { /* keep the defaults */ }
    }
}
