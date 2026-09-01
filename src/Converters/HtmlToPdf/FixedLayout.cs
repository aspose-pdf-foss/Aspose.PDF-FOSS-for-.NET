using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>EOT container → raw sfnt. The header is little-endian with
    /// FontDataSize at offset 4 and magic 0x504C at offset 34; the font program is
    /// the trailing FontDataSize bytes (plain data — compressed/XOR-obfuscated
    /// variants are rejected via the sfnt signature check). Null when the bytes are
    /// not a plain EOT.</summary>
    internal static byte[]? TryUnwrapEot(byte[] eot)
    {
        try
        {
            if (eot.Length < 40) return null;
            static uint LE32(byte[] b, int o) => (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
            var eotSize = LE32(eot, 0);
            var fontSize = LE32(eot, 4);
            var version = LE32(eot, 8);
            var magic = (ushort)(eot[34] | (eot[35] << 8));
            if (magic != 0x504C || eotSize != (uint)eot.Length) return null;
            if (version is not (0x00010000 or 0x00020001 or 0x00020002)) return null;
            if (fontSize == 0 || fontSize > eot.Length - 40) return null;
            var start = eot.Length - (int)fontSize;
            // Plain sfnt data only (TrueType 0x00010000 or 'OTTO'); anything else is
            // a compressed/obfuscated payload this unwrapper doesn't handle.
            if (!((eot[start] == 0x00 && eot[start + 1] == 0x01) ||
                  (eot[start] == (byte)'O' && eot[start + 1] == (byte)'T')))
                return null;
            var ttf = new byte[fontSize];
            System.Array.Copy(eot, start, ttf, 0, (int)fontSize);
            return ttf;
        }
        catch { return null; }
    }

    /// <summary>WOFF 1.0 → raw sfnt: rebuild the table directory and inflate each
    /// zlib-compressed table (stored tables copy through). Null on malformed data.</summary>
    internal static byte[]? TryUnwrapWoff(byte[] woff)
    {
        try
        {
            static uint U32(byte[] b, int o) => (uint)((b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]);
            static ushort U16(byte[] b, int o) => (ushort)((b[o] << 8) | b[o + 1]);
            var flavor = U32(woff, 4);
            int numTables = U16(woff, 12);
            var tables = new List<(uint tag, byte[] data)>(numTables);
            for (var i = 0; i < numTables; i++)
            {
                var e = 44 + i * 20;
                var off = (int)U32(woff, e + 4);
                var comp = (int)U32(woff, e + 8);
                var orig = (int)U32(woff, e + 12);
                byte[] data;
                if (comp == orig)
                {
                    data = new byte[orig];
                    System.Array.Copy(woff, off, data, 0, orig);
                }
                else
                {
                    using var zs = new System.IO.Compression.ZLibStream(
                        new System.IO.MemoryStream(woff, off, comp),
                        System.IO.Compression.CompressionMode.Decompress);
                    using var outMs = new System.IO.MemoryStream(orig);
                    zs.CopyTo(outMs);
                    data = outMs.ToArray();
                }
                tables.Add((U32(woff, e), data));
            }

            var n = tables.Count;
            var entrySel = n > 0 ? (int)Math.Floor(Math.Log2(n)) : 0;
            var searchRange = (1 << entrySel) * 16;
            using var sf = new System.IO.MemoryStream();
            void W16(int v) { sf.WriteByte((byte)(v >> 8)); sf.WriteByte((byte)v); }
            void W32(uint v)
            {
                sf.WriteByte((byte)(v >> 24)); sf.WriteByte((byte)(v >> 16));
                sf.WriteByte((byte)(v >> 8)); sf.WriteByte((byte)v);
            }
            static uint Checksum(byte[] d)
            {
                uint sum = 0;
                for (var i = 0; i < d.Length; i += 4)
                {
                    uint v = 0;
                    for (var k = 0; k < 4; k++) v = (v << 8) | (i + k < d.Length ? d[i + k] : 0u);
                    unchecked { sum += v; }
                }
                return sum;
            }
            W32(flavor); W16(n); W16(searchRange); W16(entrySel); W16(n * 16 - searchRange);
            var cur = 12 + n * 16;
            foreach (var t in tables)
            {
                W32(t.tag);
                W32(Checksum(t.data));
                W32((uint)cur);
                W32((uint)t.data.Length);
                cur += (t.data.Length + 3) & ~3;
            }
            foreach (var t in tables)
            {
                sf.Write(t.data, 0, t.data.Length);
                for (var p = t.data.Length; (p & 3) != 0; p++) sf.WriteByte(0);
            }
            return sf.ToArray();
        }
        catch { return null; }
    }

    private sealed class FixedSpan
    {
        public double Left, Top, FontSize, LetterSpacing, WordSpacing;
        public string Text = "";
        public string Face = "Times New Roman";
        public List<OwnFace>? Own;                       // the document's own @font-face programs
        public (double r, double g, double b)? Color;   // null = transparent (selection-only text)

        // white-space:pre spans keep literal newlines: each line lays out separately.
        public string[] Lines => Text.Replace("\r", "").Split('\n');

        /// <summary>All of the document's own font programs, tried when the span's own
        /// family list resolves none — the exporter's class families are display
        /// substitutes ("Courier New"), while the glyphs live in the @font-face
        /// programs under the ORIGINAL family names.</summary>
        public Dictionary<string, List<OwnFace>>? AllOwn;

        /// <summary>The face for one codepoint: the document's own programs when one has
        /// the glyph (custom encodings only THEY can resolve, and they carry the real
        /// advances), else the mapped system face, else the script-fallback list.</summary>
        public (OwnFace? own, string sys) FaceFor(int cp)
        {
            if (Own is not null)
                foreach (var of in Own)
                    if (of.Parser.CMap.TryGetValue(cp, out var g) && g != 0)
                        return (of, Face);
            if (AllOwn is not null)
                foreach (var list in AllOwn.Values)
                    foreach (var of in list)
                        if (of.Parser.CMap.TryGetValue(cp, out var g) && g != 0)
                            return (of, Face);
            var f = PosFace(Face);
            if (f.parser is not null && f.parser.CMap.TryGetValue(cp, out var g2) && g2 != 0)
                return (null, Face);
            return (null, PosFaceNameFor(cp));
        }
    }

    /// <summary>Advance of one line of <paramref name="s"/>, resolving every codepoint
    /// through <see cref="FixedSpan.FaceFor"/>. <paramref name="includeLs"/> false
    /// measures bare glyph advances; <paramref name="includeWs"/> false leaves the
    /// word-spacing out — the sheet-width scan measures advances + letter-spacing
    /// only, while layout keeps both in.</summary>
    private static double MeasureSpanLine(FixedSpan s, string line, bool includeLs = true,
        bool includeWs = true)
    {
        double w = 0;
        for (var i = 0; i < line.Length; i++)
        {
            int cp = line[i];
            if (char.IsHighSurrogate(line[i]) && i + 1 < line.Length && char.IsLowSurrogate(line[i + 1]))
            {
                cp = char.ConvertToUtf32(line[i], line[i + 1]);
                i++;
            }
            var (own, sys) = s.FaceFor(cp);
            if (own is not null)
            {
                var gid = own.Parser.CMap.TryGetValue(cp, out var g) ? g : 0;
                var upm = own.Parser.UnitsPerEm > 0 ? own.Parser.UnitsPerEm : 1000;
                w += gid == 0 ? 0.5 * s.FontSize
                    : Math.Round(own.Parser.GetAdvanceWidth(gid) * 1000.0 / upm) * s.FontSize / 1000.0;
            }
            else
            {
                var f = PosFace(sys);
                var gid = f.parser is not null && f.parser.CMap.TryGetValue(cp, out var g) ? g : 0;
                w += f.parser is null || gid == 0 ? 0.5 * s.FontSize
                    : Math.Round(f.parser.GetAdvanceWidth(gid) * 1000.0 / f.upm) * s.FontSize / 1000.0;
            }
            if (includeLs) w += s.LetterSpacing;
            if (includeWs && s.WordSpacing != 0 && cp == ' ') w += s.WordSpacing;
        }
        return w;
    }

    private sealed class FixedPageDiv
    {
        public double SrcW, SrcH;
        public byte[]? Background;      // full-page raster background, if any
        public bool HasObjectGraphic;   // <object> page SVG: contributes to the sheet width
        public double? ObjectInkRight;  // the SVG's drawn ink extent (pt); box fallback when null
        public string? ObjectUrl;       // the page SVG's URL, for vector replay
        public string? InlineSvgText;   // the page SVG's own markup (inline dialect)
        public List<FixedSpan> Spans = new();
    }

    /// <summary>Resolve a CSS font-family list to a face the PosFace cache can load.</summary>
    private static string ResolveFixedFace(string familyList)
    {
        foreach (var raw in familyList.Split(','))
        {
            var t = raw.Trim().Trim('"', '\'').Trim();
            if (t.Length == 0) continue;
            switch (t.ToLowerInvariant())
            {
                case "sans-serif": return "Arial";
                case "serif": return "Times New Roman";
                case "monospace": return "Courier New";
            }
            t = Regex.Replace(t, @"^[A-Z]{6}\+", "");   // subset prefix
            // Family aliases the converter emits for the standard-14 fold.
            var baseName = t.Split('-')[0];
            var isBold = t.Contains("Bold", StringComparison.OrdinalIgnoreCase);
            var isItalic = t.Contains("Italic", StringComparison.OrdinalIgnoreCase)
                           || t.Contains("Oblique", StringComparison.OrdinalIgnoreCase);
            var mapped = baseName.ToLowerInvariant() switch
            {
                "helvetica" or "arialmt" or "arial" => "Arial",
                "times" or "timesnewroman" or "timesnewromanpsmt" => "Times New Roman",
                "courier" or "couriernew" or "couriernewpsmt" or "couriernewps" => "Courier New",
                _ => baseName,
            };
            var styled = mapped
                + (isBold && isItalic ? " Bold Italic" : isBold ? " Bold" : isItalic ? " Italic" : "");
            foreach (var candidate in new[] { t, t.Replace('-', ' '), styled, mapped })
                if (PosFace(candidate).parser is not null)
                    return candidate;
        }
        return "Times New Roman";
    }

    /// <summary>Advance of <paramref name="text"/> at <paramref name="fontSize"/>, walking
    /// codepoints in <paramref name="cssFace"/> where it has the glyph and the script
    /// fallback list where it does not — the same segmentation the drawing pass uses.</summary>
    private static double MeasureFixedText(string cssFace, string text, double fontSize, double letterSpacing)
    {
        var face = PosFace(cssFace);
        double w = 0;
        for (var i = 0; i < text.Length; i++)
        {
            int cp = text[i];
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                cp = char.ConvertToUtf32(text[i], text[i + 1]);
                i++;
            }
            var f = face;
            if (f.parser is null || !f.parser.CMap.TryGetValue(cp, out var gid) || gid == 0)
            {
                f = PosFace(PosFaceNameFor(cp));
                gid = f.parser is not null && f.parser.CMap.TryGetValue(cp, out var g) ? g : 0;
            }
            w += f.parser is null || gid == 0
                ? 0.5 * fontSize
                : Math.Round(f.parser.GetAdvanceWidth(gid) * 1000.0 / f.upm) * fontSize / 1000.0;
            w += letterSpacing;
        }
        return w;
    }

    /// <summary>A font program shipped with the document itself (an @font-face WOFF/TTF
    /// sidecar or data URI). Its cmap is keyed by the SAME character codes the HTML text
    /// carries, so it measures and renders custom-encoded runs that no system face can.</summary>
    private sealed class OwnFace
    {
        public byte[] Ttf = System.Array.Empty<byte>();
        public Text.GlyphOutlineParser Parser = null!;
        public int Id;   // unique per document import; keys the embedded BaseFont name
    }

    /// <summary>Decode a WOFF1 container back to its sfnt (inverse of the exporter's
    /// wrapper: per-table zlib). Returns null when the bytes are not WOFF1.</summary>
    private static byte[]? TryReadWoff(byte[] w)
    {
        if (w.Length < 44) return null;
        uint U32(int o) => (uint)((w[o] << 24) | (w[o + 1] << 16) | (w[o + 2] << 8) | w[o + 3]);
        ushort U16(int o) => (ushort)((w[o] << 8) | w[o + 1]);
        if (U32(0) != 0x774F4646) return null;   // 'wOFF'
        var flavor = U32(4);
        int num = U16(12);
        if (num <= 0 || 44 + num * 20 > w.Length) return null;

        try
        {
            var tables = new List<(uint tag, uint checksum, byte[] data)>();
            for (var i = 0; i < num; i++)
            {
                var p = 44 + i * 20;
                var tag = U32(p);
                var off = (int)U32(p + 4);
                var compLen = (int)U32(p + 8);
                var origLen = (int)U32(p + 12);
                var checksum = U32(p + 16);
                if (off < 0 || off + compLen > w.Length) return null;
                byte[] data;
                if (compLen < origLen)
                {
                    using var src = new System.IO.MemoryStream(w, off, compLen);
                    using var z = new System.IO.Compression.ZLibStream(src, System.IO.Compression.CompressionMode.Decompress);
                    using var dst = new System.IO.MemoryStream(origLen);
                    z.CopyTo(dst);
                    data = dst.ToArray();
                }
                else
                {
                    data = new byte[origLen];
                    System.Array.Copy(w, off, data, 0, origLen);
                }
                tables.Add((tag, checksum, data));
            }

            var ms = new System.IO.MemoryStream();
            void W16(ushort v) { ms.WriteByte((byte)(v >> 8)); ms.WriteByte((byte)v); }
            void W32(uint v)
            {
                ms.WriteByte((byte)(v >> 24)); ms.WriteByte((byte)(v >> 16));
                ms.WriteByte((byte)(v >> 8)); ms.WriteByte((byte)v);
            }
            var pow2 = 1;
            var log2 = 0;
            while (pow2 * 2 <= num) { pow2 *= 2; log2++; }
            W32(flavor);
            W16((ushort)num);
            W16((ushort)(pow2 * 16));
            W16((ushort)log2);
            W16((ushort)(num * 16 - pow2 * 16));
            var offset = 12 + num * 16;
            foreach (var (tag, checksum, data) in tables)
            {
                W32(tag); W32(checksum); W32((uint)offset); W32((uint)data.Length);
                offset += (data.Length + 3) & ~3;
            }
            foreach (var (_, _, data) in tables)
            {
                ms.Write(data, 0, data.Length);
                while (ms.Length % 4 != 0) ms.WriteByte(0);
            }
            return ms.ToArray();
        }
        catch { return null; }
    }

    /// <summary>Measured advance of <paramref name="text"/> in <paramref name="run"/>'s
    /// face and size in the stl_ model (<see cref="MeasureStlExactText"/>):
    /// exact glyph advances at the floor-quantized size, letter-spacing (raw size)
    /// after every character including the nbsp sentinel, word-spacing (raw size)
    /// after every U+0020 — the sentinel nbsp is NOT a word-spacing slot. The
    /// document's own @font-face programs take precedence over installed faces.</summary>
    /// <summary>True when the parsed program's cmap maps (nearly) all the non-space
    /// codepoints of <paramref name="s"/> — a subset program with a stripped or
    /// custom-encoded cmap must NOT be measured against, or every glyph collapses to
    /// the half-em fallback.</summary>
    private static bool ParserCovers(Text.GlyphOutlineParser parser, string s)
    {
        int total = 0, hit = 0;
        for (var i = 0; i < s.Length; i++)
        {
            int cp = s[i];
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                cp = char.ConvertToUtf32(s[i], s[i + 1]);
                i++;
            }
            if (cp == ' ' || cp == 0x00A0) continue;
            total++;
            if (parser.CMap.TryGetValue(cp, out var g) && g != 0) hit++;
        }
        return total == 0 || hit * 10 >= total * 9;
    }

    /// <summary>Map @font-face families to the document's own font programs, resolving
    /// each src URL as-is (data URIs, html-relative paths) and then under the linked
    /// stylesheet's directory (a sidecar style.css references its neighbours bare).</summary>
    private static Dictionary<string, List<OwnFace>> ParseFontFaces(string css, string html, HtmlLoadOptions? options)
    {
        var result = new Dictionary<string, List<OwnFace>>(StringComparer.OrdinalIgnoreCase);
        var linkDir = "";
        var link = Regex.Match(html, @"<link(?=[^>]*rel=""stylesheet"")[^>]*href=""(?<h>[^""]+)""", RegexOptions.IgnoreCase);
        if (link.Success)
        {
            var slash = link.Groups["h"].Value.LastIndexOf('/');
            if (slash > 0) linkDir = link.Groups["h"].Value[..(slash + 1)];
        }

        foreach (Match m in Regex.Matches(css, @"@font-face\s*\{(?<body>[^}]*)\}", RegexOptions.Singleline))
        {
            var body = m.Groups["body"].Value;
            var fam = Regex.Match(body, @"font-family:\s*""?(?<v>[^;""}]+)");
            if (!fam.Success) continue;
            foreach (Match u in Regex.Matches(body, @"url\(""?(?<u>[^)""]+?)""?\)"))
            {
                var url = u.Groups["u"].Value;
                if (url.EndsWith(".eot", StringComparison.OrdinalIgnoreCase)
                    || url.Contains(".eot?", StringComparison.OrdinalIgnoreCase)
                    || url.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) continue;
                var bytes = LoadConverterImage(url, options)
                            ?? (linkDir.Length > 0 ? LoadConverterImage(linkDir + url, options) : null);
                if (bytes is null || bytes.Length < 4) continue;
                var sfnt = TryReadWoff(bytes) ?? (bytes[0] == 0x00 && bytes[1] == 0x01 ? bytes : null);
                if (sfnt is null) continue;
                try
                {
                    var parser = new Text.GlyphOutlineParser(sfnt);
                    if (!result.TryGetValue(fam.Groups["v"].Value.Trim(), out var list))
                        result[fam.Groups["v"].Value.Trim()] = list = new List<OwnFace>();
                    list.Add(new OwnFace { Ttf = sfnt, Parser = parser, Id = ++_ownFaceIds });
                    break;   // one program per @font-face block
                }
                catch { /* unparseable program — try the next url */ }
            }
        }
        return result;
    }

    private static Document? TryConvertPositionedFixedLayout(string html, HtmlLoadOptions? options)
    {
        const double MarginLeft = 96.0;      // content x shift on the sheet
        const double ContentTop = 78.0;      // content y shift on the first band's sheet (104px @96dpi)
        const double ContentBottom = 69.0;   // sheet content box bottom margin (92px @96dpi); the
                                             // band pitch solves to 694.89 on an A4 sheet
                                             // from cross-page seam anchors
        const double RightPad = 89.76;       // sheet width − (96 + widest element)
        const double StlEmPt = 12.0;

        var divs = new List<FixedPageDiv>();
        // The em-compensation grid dialect (every letter-spacing on the 0.01 em
        // grid): its width budget is exact and its raster background carries
        // IMAGES ONLY, so the raster ink edge must not cap the text width.
        var emGridMarkup = false;
        var stl = IsStlPositionedHtml(html);
        if (stl)
        {
            // The stl_ dialect keeps all appearance in the stylesheet — without it
            // the bare markup flows (the caller's reflow path). Only CALLER
            // context counts here: inline <style> blocks, or a linked stylesheet reached
            // through an explicitly supplied BasePath. The auto base derived from the
            // file's own directory resolves resources, but does not flip this route —
            // a sidecar-styled page loaded without a base path reflows.
            var css = GatherStlCss(html, options?.BasePathAutoDerived == true ? null : options);
            if (string.IsNullOrWhiteSpace(css)) return null;
            {
                var sawLs = false; var onGrid = true;
                foreach (Match lm in Regex.Matches(css, @"letter-spacing:\s*(-?[\d.]+)em"))
                {
                    var le = double.Parse(lm.Groups[1].Value,
                        System.Globalization.CultureInfo.InvariantCulture);
                    if (le == 0) continue;
                    sawLs = true;
                    var cents = le * 100.0;
                    if (System.Math.Abs(cents - System.Math.Round(cents)) > 1e-6) { onGrid = false; break; }
                }
                emGridMarkup = sawLs && onGrid;
            }

            // Class → declarations we honor (font-size em, font-family, color,
            // letter-spacing em on the root 12 pt em). Class names are arbitrary
            // (CssClassNamesPrefix renames the stl_ scheme).
            var clsFont = new Dictionary<string, (double? fs, string? fam, string? col, double? ls)>(StringComparer.Ordinal);
            foreach (Match m in Regex.Matches(css, @"\.(?<cls>[\w-]+)\s*\{(?<body>[^}]*)\}", RegexOptions.Singleline))
            {
                var body = m.Groups["body"].Value;
                double? fs = null, ls = null;
                var fm = Regex.Match(body, @"font-size:\s*(?<v>[\d.]+)em");
                if (fm.Success) fs = double.Parse(fm.Groups["v"].Value, System.Globalization.CultureInfo.InvariantCulture);
                var lm = Regex.Match(body, @"letter-spacing:\s*(?<v>-?[\d.]+)em");
                if (lm.Success) ls = double.Parse(lm.Groups["v"].Value, System.Globalization.CultureInfo.InvariantCulture);
                var am = Regex.Match(body, @"font-family:\s*""?(?<v>[^;""}]+)");
                var cm = Regex.Match(body, @"(?<!-)color:\s*(?<v>[^;}]+)");
                var key = m.Groups["cls"].Value;
                // Later rules override earlier ones per property, like a cascade.
                clsFont.TryGetValue(key, out var prev);
                clsFont[key] = (fs ?? prev.fs,
                    am.Success ? am.Groups["v"].Value.Trim() : prev.fam,
                    cm.Success ? cm.Groups["v"].Value.Trim() : prev.col,
                    ls ?? prev.ls);
            }

            var ownFaces = ParseFontFaces(css, html, options);
            var pageDivs = Regex.Matches(html, @"<div id=""page_\d+""[^>]*>");
            if (pageDivs.Count == 0) return null;
            for (var p = 0; p < pageDivs.Count; p++)
            {
                var segStart = pageDivs[p].Index;
                var segEnd = p + 1 < pageDivs.Count ? pageDivs[p + 1].Index : html.Length;
                var seg = html[segStart..segEnd];
                var div = new FixedPageDiv { SrcW = 612.0, SrcH = 842.0 };

                // Page box from the container's stylesheet class (width/height em).
                var clsAttr = Regex.Match(pageDivs[p].Value, @"class=""(?<c>[^""]+)""");
                var boxResolved = false;
                if (clsAttr.Success)
                {
                    foreach (var cls in clsAttr.Groups["c"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var box = Regex.Match(css,
                            @"\." + Regex.Escape(cls) + @"\s*\{[^}]*width:\s*(?<w>[\d.]+)em[^}]*height:\s*(?<h>[\d.]+)em",
                            RegexOptions.Singleline);
                        if (box.Success)
                        {
                            div.SrcW = double.Parse(box.Groups["w"].Value, System.Globalization.CultureInfo.InvariantCulture) * StlEmPt;
                            div.SrcH = double.Parse(box.Groups["h"].Value, System.Globalization.CultureInfo.InvariantCulture) * StlEmPt;
                            boxResolved = true;
                            break;
                        }
                    }
                }
                if (!boxResolved) return null;   // container box not in the stylesheet → reflow

                var bg = Regex.Match(seg, @"<img\s+(?=[^>]*class=""stl_04"")[^>]*src=""(?<src>[^""]*)""|<img\s+(?=[^>]*src=""(?<src2>[^""]*)"")[^>]*class=""stl_04""");
                if (bg.Success)
                {
                    var src = bg.Groups["src"].Success ? bg.Groups["src"].Value : bg.Groups["src2"].Value;
                    div.Background = LoadConverterImage(DecodeEntities(src), options);
                }
                var objM = Regex.Match(seg, @"<object\s[^>]*data=""(?<u>[^""]*)""");
                if (objM.Success)
                {
                    div.HasObjectGraphic = true;
                    div.ObjectUrl = DecodeEntities(objM.Groups["u"].Value);
                    // A page SVG referenced as a SIDECAR contributes its BOX to the
                    // sheet width, not its drawn ink: a page whose
                    // only vector is a header rule ending mid-page widens all the way to
                    // the page box. (On a rule ending at 546 pt of a 612 pt
                    // box the correct sheet is 798 pt, which is the box;
                    // 736.91 pt, which is the ink, renders 127 px too
                    // narrow.) Only the INLINE dialect below, where the
                    // markup we emit IS the page's whole vector art, measures its ink.
                }
                else
                {
                    // The self-contained dialect carries the page SVG as INLINE
                    // markup instead of an <object> sidecar reference; the markup
                    // itself is the replay source (its rasters are data: URIs).
                    var inlineSvgM = Regex.Match(seg, @"<svg\b[\s\S]*?</svg\s*>");
                    if (inlineSvgM.Success)
                    {
                        div.HasObjectGraphic = true;
                        div.InlineSvgText = inlineSvgM.Value;
                        try
                        {
                            var frac = TrySvgInkRightFraction(div.InlineSvgText);
                            if (frac is { } fr) div.ObjectInkRight = Math.Min(1.0, Math.Max(0, fr)) * div.SrcW;
                        }
                        catch { /* unscannable SVG: box fallback */ }
                    }
                }

                foreach (Match dm in Regex.Matches(seg,
                    @"<div class=""[^""]*"" style=""left:(?<l>-?[\d.]+)em;\s*top:(?<t>-?[\d.]+)em;?[^""]*"">(?<body>.*?)</div>",
                    RegexOptions.Singleline))
                {
                    double Num(string s) => double.Parse(s,
                        System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);
                    // A line div holds ONE OR MORE spans (a pinned line is split into
                    // a span per word-anchored segment, each with its own spacing
                    // classes and optionally its own <a> wrapper). Segments flow one
                    // after another from the div's left edge: each becomes its own
                    // FixedSpan at the accumulated x, advanced by the segment's
                    // measured width with its pinned letter/word-spacing applied.
                    var x = Num(dm.Groups["l"].Value) * StlEmPt;
                    var top = Num(dm.Groups["t"].Value) * StlEmPt;
                    foreach (Match m in Regex.Matches(dm.Groups["body"].Value,
                        @"<span class=""(?<cls>[^""]*)""(?:\s+style=""(?<sst>[^""]*)"")?[^>]*>(?<stext>.*?)</span>",
                        RegexOptions.Singleline))
                    {
                        var text = DecodeEntities(Regex.Replace(m.Groups["stext"].Value, "<[^>]+>", ""));
                        if (text.Length == 0) continue;
                        double fsEm = 1.0, lsEm = 0.0;
                        string fam = "sans-serif";
                        string? col = null;
                        foreach (var cls in m.Groups["cls"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                        {
                            if (!clsFont.TryGetValue(cls, out var e)) continue;
                            if (e.fs is not null && e.fam is not null) { fsEm = e.fs.Value; fam = e.fam; col ??= e.col; }
                            else if (e.fs is not null) fsEm = e.fs.Value;
                            if (e.ls is not null) lsEm = e.ls.Value;
                            if (e.col is not null && e.fs is not null) col = e.col;
                        }
                        var wsM = Regex.Match(m.Groups["sst"].Value ?? "", @"word-spacing:\s*(-?[\d.]+)em");
                        var wsEm = wsM.Success ? Num(wsM.Groups[1].Value) : 0.0;
                        var famKey = fam.Split(',')[0].Trim().Trim('"', '\'');
                        var span = new FixedSpan
                        {
                            Left = x,
                            Top = top,
                            FontSize = fsEm * StlEmPt,
                            LetterSpacing = lsEm * StlEmPt,
                            WordSpacing = wsEm * fsEm * StlEmPt,
                            Text = text,
                            Face = ResolveFixedFace(fam),
                            Own = ownFaces.TryGetValue(famKey, out var ofl) ? ofl : null,
                            AllOwn = ownFaces.Count > 0 ? ownFaces : null,
                            Color = ParseCssColorRgb(col),
                        };
                        div.Spans.Add(span);
                        // The next segment starts where this one's TEXT ends: a span's
                        // FINAL space carries no word-spacing. The exporter solved each
                        // segment's spacing to place its own glyphs, so charging the
                        // trailing space again here would push every following segment
                        // out by one word-spacing (a 0.2514em span left a 14 px word gap
                        // where 7 px is correct).
                        x += MeasureSpanLine(span, text)
                             - (span.WordSpacing != 0 && text.EndsWith(' ') ? span.WordSpacing : 0);
                    }
                }
                divs.Add(div);
            }
        }
        else
        {
            // pdf-page dialect: self-contained (inline pt styles, data-URI background).
            var pageDivs = Regex.Matches(html, @"<div class=""pdf-page""[^>]*style=""(?<st>[^""]*)""[^>]*>");
            if (pageDivs.Count == 0) return null;
            for (var p = 0; p < pageDivs.Count; p++)
            {
                var segStart = pageDivs[p].Index;
                var segEnd = p + 1 < pageDivs.Count ? pageDivs[p + 1].Index : html.Length;
                var seg = html[segStart..segEnd];
                var div = new FixedPageDiv
                {
                    SrcW = StylePt(pageDivs[p].Groups["st"].Value, "width") ?? 612.0,
                    SrcH = StylePt(pageDivs[p].Groups["st"].Value, "height") ?? 792.0,
                };
                var bg = Regex.Match(seg, @"<img\s+(?=[^>]*class=""pdf-page-bg"")[^>]*src=""(?<src>[^""]*)""");
                if (bg.Success)
                    div.Background = LoadConverterImage(bg.Groups["src"].Value, options);

                foreach (Match m in Regex.Matches(seg,
                    @"<span class=""pdf-text"" style=""(?<st>[^""]*)"">(?<body>.*?)</span>",
                    RegexOptions.Singleline))
                {
                    var st = m.Groups["st"].Value;
                    var text = DecodeEntities(Regex.Replace(m.Groups["body"].Value, "<[^>]+>", ""));
                    if (text.Length == 0) continue;
                    var famM = Regex.Match(st, @"font-family:\s*(?<v>[^;""]+)");
                    var colM = Regex.Match(st, @"(?<!-)color:\s*(?<v>[^;]+)");
                    div.Spans.Add(new FixedSpan
                    {
                        Left = StylePt(st, "left") ?? 0,
                        Top = StylePt(st, "top") ?? 0,
                        FontSize = StylePt(st, "font-size") ?? 12,
                        Text = text,
                        Face = ResolveFixedFace(famM.Success ? famM.Groups["v"].Value : "serif"),
                        Color = ParseCssColorRgb(colM.Success ? colM.Groups["v"].Value : null),
                    });
                }
                divs.Add(div);
            }
        }
        if (divs.Count == 0) return null;

        var pageInfo = options?.PageInfo;
        var pageH = pageInfo?.Height is > 0 ? pageInfo.Height : 841.89;
        if (pageInfo?.LandscapeRequested == true && pageInfo.Width > pageH)
            pageH = pageInfo.Width;
        // The BAND pitch derives from exact A4 (841.89); PageInfo's rounded 842
        // default gives a pitch 0.11pt long, which walks the content ~12pt
        // off by sheet 95 of a long document. The sheet's PAGE BOX, however, is
        // the rounded 842 — the rasterized page is 842pt tall while the bands
        // step at the exact-A4 pitch.
        var bandBaseH = Math.Abs(pageH - 841.89) < 0.5 ? 841.89 : pageH;
        if (Math.Abs(pageH - 841.89) < 0.5) pageH = 842.0;
        var bandH = bandBaseH - ContentTop - ContentBottom;
        if (bandH <= 0) return null;

        // Sheet width: 96 + the widest laid-out element + 89.76, over the whole document.
        double maxRight = 0;
        foreach (var div in divs)
        {
            if (div.HasObjectGraphic) maxRight = Math.Max(maxRight, div.ObjectInkRight ?? div.SrcW);

            // Selection-layer spans (transparent text over a full-page raster) can carry
            // synthetic padding and unshaped RTL runs whose naive advance far exceeds
            // what any layout engine would produce for the visible line. The raster IS
            // the visible content, so its rightmost inked column (plus up to an em of
            // trailing whitespace) caps their contribution.
            double? bgInkRight = null;
            if (div.Background is not null)
                try
                {
                    var (px, pw, ph, hasAlpha) = Facades.PdfFileMend.DecodePng(div.Background);
                    var bpp = hasAlpha ? 4 : 3;
                    var right = -1;
                    for (var yy = 0; yy < ph; yy++)
                    {
                        var row = yy * pw;
                        for (var xx = pw - 1; xx > right; xx--)
                        {
                            var o = (row + xx) * bpp;
                            if (px[o] < 220 || px[o + 1] < 220 || px[o + 2] < 220) { right = xx; break; }
                        }
                    }
                    if (right >= 0 && pw > 0) bgInkRight = (right + 1) / (double)pw * div.SrcW;
                }
                catch { /* not a PNG / undecodable — no cap */ }

            // A bidi page's selection layer measures unreliably as a whole: RTL runs
            // are stored unshaped (ligatures collapse in any real layout) and even its
            // LTR fragments carry mirrored ordering with positioning spaces. Cap ALL
            // of that div's transparent lines at the raster's ink edge plus up to an
            // em of trailing whitespace; pure-LTR pages measure reliably and stay as-is.
            var divHasRtl = false;
            foreach (var s in div.Spans)
                if (HasRtlChar(s.Text)) { divHasRtl = true; break; }

            foreach (var s in div.Spans)
                foreach (var line in s.Lines)
                {
                    var r = s.Left + MeasureSpanLine(s, line);
                    if (Environment.GetEnvironmentVariable("STL_DEBUG_WIDTH2") is not null && r > 400)
                    {
                        var trimmed = line.TrimEnd();
                        var spacesN = 0; foreach (var chW in line) if (chW == ' ') spacesN++;
                        var trailAdvDbg = MeasureSpanLine(s, line[trimmed.Length..]);
                        Console.Error.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                            $"WLINE r={r:F2} left={s.Left:F2} fs={s.FontSize:F4} ls={s.LetterSpacing:F4} ws={s.WordSpacing:F4} nsp={spacesN} nch={line.Length} trail={trailAdvDbg:F2} txt='{(trimmed.Length > 34 ? trimmed[^34..] : trimmed)}'"));
                    }
                    if (s.Color is null && bgInkRight is not null && !emGridMarkup)
                    {
                        var trailingWs = line[line.TrimEnd().Length..];
                        var trailAdv = MeasureSpanLine(s, trailingWs);
                        // RTL layers measure so unreliably that even their trailing-space
                        // credit is clamped; LTR layers keep the real trailing advance
                        // with a floor of a few ems (selection spans
                        // run a little past the ink even on lines showing none).
                        r = Math.Min(r, bgInkRight.Value
                            + (divHasRtl ? Math.Min(trailAdv, s.FontSize)
                                         : Math.Max(trailAdv, 2.5 * s.FontSize)));
                    }
                    // Text overflowing the fixed page container never widens the sheet:
                    // the container's box is the layout surface (a 493 pt cover title
                    // whose naive advance runs metres past an A4 box still yields
                    // the 96+box+89.76 sheet).
                    maxRight = Math.Max(maxRight, Math.Min(r, div.SrcW));
                }
        }
        if (maxRight <= 0)
            foreach (var div in divs) maxRight = Math.Max(maxRight, div.SrcW);
        var pageW = MarginLeft + maxRight + RightPad;

        var doc = Document.Create();
        var docFontDict = new Core.PdfDictionary();
        var inv = System.Globalization.CultureInfo.InvariantCulture;

        // ALL source pages stack into one continuous flow that is sliced
        // into sheet-height bands ACROSS page boundaries (a sheet can show
        // the seam: one page's footer and the next page's heading mid-sheet). Sheets
        // are created on demand in flow order; a page box shorter than the band
        // shares its sheet with the next page's top.
        var cum = 0.0;          // this div's top edge in continuous flow coordinates
        var pagesMade = 0;
        foreach (var div in divs)
        {
            var k0 = (int)Math.Floor((cum + 0.01) / bandH);
            var k1 = Math.Max(k0, (int)Math.Floor((cum + div.SrcH - 0.01) / bandH));
            for (var band = k0; band <= k1; band++)
            {
                while (pagesMade <= band)
                {
                    var np = doc.Pages.Add(pageW, pageH);
                    EnsureFonts(np, docFontDict);
                    pagesMade++;
                }
                var page = doc.Pages[band + 1];
                var yOff = ContentTop - (band * bandH - cum);   // top-down source y → sheet y

                // Clip everything on this sheet to the content band; the q stays open
                // across the content streams below and is closed at the end.
                page.AddContentStream(Encoding.ASCII.GetBytes(
                    $"q 0 {ContentBottom.ToString("F2", inv)} {pageW.ToString("F2", inv)} {bandH.ToString("F2", inv)} re W n\n"));

                if (div.Background is not null)
                {
                    try
                    {
                        page.AddImage(div.Background, new Aspose.Pdf.Rectangle(
                            MarginLeft, pageH - yOff - div.SrcH, MarginLeft + div.SrcW, pageH - yOff));
                    }
                    catch { /* undecodable background — text still imports */ }
                }

                // The page SVG replays from its sidecar (ObjectUrl) or, in the
                // self-contained dialect, from the inline markup itself.
                string? replaySvgText = null;
                var replaySvgDir = "";
                if (div.ObjectUrl is not null)
                {
                    var svgBytes = LoadConverterImage(div.ObjectUrl, options);
                    if (svgBytes is not null)
                    {
                        var slash = div.ObjectUrl.LastIndexOf('/');
                        if (slash > 0) replaySvgDir = div.ObjectUrl[..(slash + 1)];
                        replaySvgText = Encoding.UTF8.GetString(svgBytes);
                    }
                }
                else if (div.InlineSvgText is not null)
                {
                    replaySvgText = div.InlineSvgText;
                }
                if (replaySvgText is not null)
                {
                    // Map the SVG's viewBox onto the page box: x right, y DOWN from
                    // the sheet's content origin.
                    var vb2 = Regex.Match(replaySvgText,
                        @"viewBox=""(?<a>-?[\d.]+)\s+(?<b>-?[\d.]+)\s+(?<c>[\d.]+)\s+(?<d>[\d.]+)""");
                    double vw = 0, vh = 0;
                    if (vb2.Success)
                    {
                        vw = double.Parse(vb2.Groups["c"].Value, System.Globalization.CultureInfo.InvariantCulture);
                        vh = double.Parse(vb2.Groups["d"].Value, System.Globalization.CultureInfo.InvariantCulture);
                    }
                    if (vw > 0 && vh > 0)
                    {
                        var placement = new[]
                        {
                            div.SrcW / vw, 0, 0, -div.SrcH / vh,
                            MarginLeft, pageH - yOff,
                        };
                        try { ReplaySvgObject(page, replaySvgText, placement, replaySvgDir, options); }
                        catch { /* a partial page graphic still beats none */ }
                    }
                }

                var sb = new StringBuilder();
                foreach (var s in div.Spans)
                {
                    if (s.Color is null) continue;   // selection-only text; the raster carries the pixels
                    // Skip spans clearly outside this band (the clip still guards stragglers).
                    // s.Top is div-local; bands live in continuous-flow coordinates.
                    var spanTop = cum + s.Top;
                    if (spanTop + s.FontSize * 1.4 < band * bandH || spanTop > (band + 1) * bandH)
                        continue;
                    var res = page.Dict.Get("Resources") as Core.PdfDictionary;
                    var fontDict = res?.Get("Font") as Core.PdfDictionary ?? docFontDict;

                    sb.Append("BT ");
                    sb.Append($"{s.Color.Value.r.ToString("F3", inv)} {s.Color.Value.g.ToString("F3", inv)} {s.Color.Value.b.ToString("F3", inv)} rg ");
                    if (s.LetterSpacing != 0)
                        sb.Append($"{s.LetterSpacing.ToString("F3", inv)} Tc ");

                    var spanLines = s.Lines;
                    for (var li = 0; li < spanLines.Length; li++)
                    {
                        var lineText = spanLines[li];
                        if (lineText.Trim().Length == 0) continue;
                        var y = pageH - (yOff + s.Top + li * 1.2 * s.FontSize + s.FontSize);
                        var runX = MarginLeft + s.Left;

                        // Split into runs by resolved face: the document's own @font-face
                        // program where it has the glyph, else the mapped system face,
                        // else the script fallback — per codepoint.
                        var i = 0;
                        while (i < lineText.Length)
                        {
                            int cp0 = lineText[i];
                            if (char.IsHighSurrogate(lineText[i]) && i + 1 < lineText.Length && char.IsLowSurrogate(lineText[i + 1]))
                                cp0 = char.ConvertToUtf32(lineText[i], lineText[i + 1]);
                            var (runOwn, runSys) = s.FaceFor(cp0);
                            var runSb = new StringBuilder();
                            double runW = 0;
                            while (i < lineText.Length)
                            {
                                int cp = lineText[i];
                                var cpLen = 1;
                                if (char.IsHighSurrogate(lineText[i]) && i + 1 < lineText.Length && char.IsLowSurrogate(lineText[i + 1]))
                                {
                                    cp = char.ConvertToUtf32(lineText[i], lineText[i + 1]);
                                    cpLen = 2;
                                }
                                var (own, sys) = s.FaceFor(cp);
                                if ((!ReferenceEquals(own, runOwn)
                                     || !sys.Equals(runSys, StringComparison.OrdinalIgnoreCase))
                                    && lineText[i] != ' ') break;
                                var piece = lineText.Substring(i, cpLen);
                                runSb.Append(piece);
                                runW += MeasureSpanLine(s, piece);
                                i += cpLen;
                                // Word-spacing cannot ride the content stream: Tw applies
                                // only to single-byte code 32, and these runs are shown
                                // through composite (Type0) fonts, so a space inside a run
                                // advances by its bare glyph width however the span is
                                // styled. The run therefore ENDS at the space, and the next
                                // one is positioned at the accumulated x - which carries the
                                // word-spacing, because runW is measured with it. Without
                                // this the drawn line is wider than the measured one by the
                                // whole word-spacing budget (a 24-space line at
                                // -0.024em ran 5.7 pt long, and the sheet with it).
                                if (s.WordSpacing != 0 && piece == " ") break;
                            }
                            var runText = runSb.ToString();
                            var runTtf = runOwn?.Ttf ?? PosFace(runSys).ttf;
                            var runBase = runOwn is not null ? "DocFace" + runOwn.Id : runSys;
                            if (runTtf is not null)
                            {
                                var (rn, hex) = Text.Type0FontEmbedder.Embed(fontDict, runTtf,
                                    runBase, runText, stripSpacesInBaseFont: true);
                                sb.Append($"/{rn} {s.FontSize.ToString("F2", inv)} Tf ");
                                sb.Append($"1 0 0 1 {runX.ToString("F2", inv)} {y.ToString("F2", inv)} Tm ");
                                sb.Append('<').Append(System.Convert.ToHexString(hex)).Append("> Tj ");
                            }
                            else
                            {
                                sb.Append($"/F1 {s.FontSize.ToString("F2", inv)} Tf ");
                                sb.Append($"1 0 0 1 {runX.ToString("F2", inv)} {y.ToString("F2", inv)} Tm ");
                                sb.Append($"({EscapePdfString(runText)}) Tj ");
                            }
                            runX += runW;
                        }
                    }
                    if (s.LetterSpacing != 0) sb.Append("0 Tc ");
                    sb.AppendLine("ET");
                }
                if (sb.Length > 0)
                    page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));

                // Close the band clip's q.
                page.AddContentStream(Encoding.ASCII.GetBytes("Q\n"));
            }
            cum += div.SrcH;
        }

        PruneUnusedFonts(doc);
        return doc;
    }
}
