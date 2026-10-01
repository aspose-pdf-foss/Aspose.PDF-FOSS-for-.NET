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
                    data = IO.Filters.ManagedInflater.InflateToEnd(woff, off, comp, zlibWrapper: true);
                }
                tables.Add((U32(woff, e), data));
            }

            var n = tables.Count;
            var entrySel = n > 0 ? (int)Math.Floor(Compat.Log2(n)) : 0;
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

        /// <summary>The line box this span sits in, and the baseline's drop from its top.
        /// Zero leaves the original calibration (a 1.2 em box, the baseline an em down).</summary>
        public double LineHeightPt, BaselineDropPt;
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
        // The background's own INTRINSIC box in points (0 = unknown, fall back to the container
        // box). A raster page export's PNG is the page: it draws at its natural size and it is
        // the widest laid-out element, so the sheet grows to hold it whole.
        public double BackgroundW, BackgroundH;
        public bool HasObjectGraphic;   // <object> page SVG: contributes to the sheet width
        public double? ObjectInkRight;  // the SVG's drawn ink extent (pt); box fallback when null
        public string? ObjectUrl;       // the page SVG's URL, for vector replay
        public string? InlineSvgText;   // the page SVG's own markup (inline dialect)
        public List<FixedSpan> Spans = new();
        /// <summary>Height of the container's IN-FLOW content: its explicit-height block
        /// children (an absolutely positioned child counts nothing). Zero = none read. A
        /// dialect with a widow rule moves the WHOLE container to the next sheet when this
        /// does not fit the band that is left - see RenderFixedLayoutPages.</summary>
        public double InFlowContentHeightPt;
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
                    data = IO.Filters.ManagedInflater.InflateToEnd(w, off, compLen, zlibWrapper: true);
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

    /// <summary>Measured advance of <c>text</c> in <c>run</c>'s
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

    private const double FixedMarginLeftPt = 96.0;      // content x shift on the sheet
    private const double FixedContentTopPt = 78.0;      // content y shift on the first band's sheet (104px @96dpi)
    private const double FixedContentBottomPt = 69.0;   // sheet content box bottom margin (92px @96dpi); the
                                                        // band pitch solves to 694.89 on an A4 sheet
                                                        // from cross-page seam anchors
    private const double FixedRightPadPt = 89.76;       // sheet width − (96 + widest element)
    /// <summary>A container whose in-flow content overshoots the band that is left by this
    /// much (one CSS pixel) or more moves whole to the next sheet (probed: 0.74 fits, 0.75 moves).</summary>
    private const double FixedPushTolerancePt = 0.75;

    /// <summary>The sheet one fixed-layout dialect imports onto: where its content band
    /// sits, how far the sheet grows past the widest element, and whether a line box that
    /// crosses the band's bottom edge moves whole to the next sheet. The inline-style and
    /// page_N dialects keep the original calibration; the em-class dialect measures its own.</summary>
    private sealed class FixedSheetModel
    {
        public double ContentTopPt = FixedContentTopPt;
        public double ContentBottomPt = FixedContentBottomPt;
        public double RightPadPt = FixedRightPadPt;

        /// <summary>Band pitch when the dialect measures its own; zero derives it from the sheet.</summary>
        public double BandPitchPt;

        /// <summary>Sheet y a line box may not cross. Zero = no widow rule: the band clip alone
        /// decides what shows, and a straddling line draws on both sheets.</summary>
        public double FitBottomPt;

        /// <summary>Where a line moved off the previous band seats on the next one.</summary>
        public double WidowTopPt;

        /// <summary>True when the sheet ends one page margin past the last INKED column rather
        /// than past the last text advance - see LastGlyphRightBearingPt in InchStyleFixed.cs.
        /// The dialects calibrated against the advance keep it false.</summary>
        public bool SheetOnInkExtent;
    }

    private static Document? TryConvertPositionedFixedLayout(string html, HtmlLoadOptions? options)
    {

        if (ReadFixedLayoutDivs(html, options) is not (var divs, var emGridMarkup, var model)) return null;
        if (divs.Count == 0) return null;

        if (SolveFixedLayoutSheet(options, divs, emGridMarkup, model) is not (var pageH, var bandH, var pageW)) return null;

        var doc = RenderFixedLayoutPages(options, divs, pageH, bandH, pageW, model);

        PruneUnusedFonts(doc);
        return doc;
    }
}
