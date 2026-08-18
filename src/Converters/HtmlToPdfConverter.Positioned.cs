using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    // ── Positioned-span round-trip re-import ────────────────────────────────
    // Recognises the HTML shape emitted by PdfToHtmlConverter (a fixed-size
    // <div class="pdf-page"> per source page holding absolutely-positioned
    // <span class="pdf-text"> runs plus <a class="pdf-link"> overlay rectangles)
    // and re-imports it geometrically: spans sharing a baseline are joined into
    // one source line (direct concatenation — inter-word spacing is already in
    // the span texts), each source line becomes one flow paragraph, and the
    // paragraphs are reflowed at a uniform size with real font metrics. Link
    // overlays map back onto the text they covered: those runs render blue,
    // underlined, and carry a URI link annotation.

    internal static bool IsPositionedSpanHtml(string html) =>
        html.Contains("class=\"pdf-page\"", StringComparison.Ordinal)
        && html.Contains("class=\"pdf-text\"", StringComparison.Ordinal)
        && html.Contains("position:absolute", StringComparison.Ordinal);

    /// <summary>The stl_ class-scheme dialect of this library's own PDF→HTML output
    /// (one <c>page_N</c> container per page holding absolutely-positioned
    /// <c>stl_01</c> text divs; appearance classes live in the stylesheet). It gets the
    /// same geometric reflow re-import as the older inline-styled pdf-page dialect —
    /// without it the page container and svg objects flow as stacked blocks and the
    /// text lands pages down (such a round-trip renders a blank page 1).</summary>
    internal static bool IsStlPositionedHtml(string html) =>
        Regex.IsMatch(html, @"<div id=""page_\d+""")
        // Line divs are "<prefix>01" positioned in em; the bare name is stl_01 and a
        // CssClassNamesPrefix save emits e.g. "p1-… p1-…-01" (base class + suffixed).
        && Regex.IsMatch(html, @"<div class=""[^""]*01"" style=""left:-?[\d.]+em");

    /// <summary>True when the stl_ document's pages carry a RASTER page background
    /// (the PNG-page-background writer: a full-page &lt;img&gt; inside the "03"
    /// background wrapper div) as opposed to the SVG-text dialect's &lt;object&gt;
    /// vector background. Content images (img_NN.png at their own sizes) don't count.</summary>
    internal static bool HasStlRasterBackground(string html) =>
        Regex.IsMatch(html,
            @"<div class=""[^""]*03""><img [^>]*style=""width:100%;height:100%;""");

    /// <summary>Map stl_ class → font-size in em (1 em = 12 pt in the stl_ scheme),
    /// harvested from inline <c>&lt;style&gt;</c> blocks and linked stylesheets
    /// (resolved against <see cref="HtmlLoadOptions.BasePath"/>). Only font-size is
    /// needed: the reflow renders uniformly, but each span's own size fixes its
    /// baseline (top already encodes −fontSize) so line grouping stays exact.</summary>
    /// <summary>All CSS visible to the document: inline &lt;style&gt; blocks plus linked
    /// stylesheets resolved against <see cref="HtmlLoadOptions.BasePath"/>.</summary>
    private static string GatherStlCss(string html, HtmlLoadOptions? options)
    {
        var css = new StringBuilder();
        foreach (Match m in Regex.Matches(html, @"<style[^>]*>(?<c>.*?)</style>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase))
            css.Append(m.Groups["c"].Value).Append('\n');
        foreach (Match m in Regex.Matches(html, @"<link(?=[^>]*rel=""stylesheet"")[^>]*href=""(?<h>[^""]+)""",
            RegexOptions.IgnoreCase))
        {
            var basePath = options?.BasePath;
            if (string.IsNullOrEmpty(basePath)) continue;
            try
            {
                var p = System.IO.Path.Combine(basePath,
                    m.Groups["h"].Value.Replace('/', System.IO.Path.DirectorySeparatorChar));
                if (File.Exists(p)) css.Append(File.ReadAllText(p)).Append('\n');
            }
            catch { /* unreadable stylesheet — sizes default to 1 em */ }
        }
        return css.ToString();
    }

    private static Dictionary<string, double> ParseStlFontSizes(string html, HtmlLoadOptions? options)
    {
        var css = new StringBuilder(GatherStlCss(html, options));
        var map = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(css.ToString(), @"\.(?<cls>[\w-]+)\s*\{(?<body>[^}]*)\}",
            RegexOptions.Singleline))
        {
            var fm = Regex.Match(m.Groups["body"].Value, @"font-size:\s*(?<v>[\d.]+)em");
            if (fm.Success && double.TryParse(fm.Groups["v"].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v))
                map[m.Groups["cls"].Value] = v;
        }
        return map;
    }

    private sealed class PosSpan
    {
        public double Left, Top, FontSize;
        public string Text = "";
        // Per-character link targets for the legacy stl_ shape, aligned to Text:
        // its linked runs are ANCHOR-WRAPPED spans inside the line div, so the URL
        // rides with the parse.
        public string?[]? Urls;
        public double Baseline => Top + FontSize;
    }

    /// <summary>One stl_ line div parsed for the reflow: its concatenated span text
    /// with, per character, the link target, the extra pen advance a span's
    /// word-spacing puts after a space, and whether the character belongs to a
    /// raised (sup) run.</summary>
    private sealed class StlPara
    {
        public string Text = "";
        public string?[] Urls = System.Array.Empty<string?>();
        public double[] Extra = System.Array.Empty<double>();
        public bool[] Sup = System.Array.Empty<bool>();
    }

    private sealed class PosLink
    {
        public double Left, Top, Width, Height;
        public string Url = "";
    }

    private static double? StylePt(string style, string prop)
    {
        var m = Regex.Match(style, prop + @"\s*:\s*(-?[\d.]+)pt", RegexOptions.IgnoreCase);
        return m.Success && double.TryParse(m.Groups[1].Value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    // ── stl_ dialect positioned re-import ────────────────────────────────────
    // Re-import of stl_ fixed-layout HTML keeps geometry: content is
    // laid out at 1em = 12pt with its CSS left/top offset from the page margins
    // (defaults L=R=90, T=B=72 on a 595x842 page), the page WIDTH grows to
    // max(default, ML + widest-line right edge + MR), the page HEIGHT stays the
    // default, and content taller than the usable band (H − MT − MB) paginates onto
    // further pages. Each span renders at its stylesheet class's font-size /
    // font-family / color with the class letter-spacing and any inline word-spacing.

    private sealed class StlClassProps
    {
        public double? FontSizeEm;
        public string? Family;
        public string? Color;
        public double? LetterSpacingEm;
        public double? WidthEm;
        public double? HeightEm;
    }

    private static Dictionary<string, StlClassProps> ParseStlClassProps(string css)
    {
        var map = new Dictionary<string, StlClassProps>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(css, @"\.(?<cls>[-\w]+)\s*\{(?<body>[^}]*)\}",
                     RegexOptions.Singleline))
        {
            var name = m.Groups["cls"].Value;
            var body = m.Groups["body"].Value;
            if (!map.TryGetValue(name, out var p)) map[name] = p = new StlClassProps();

            static double? Em(string body, string prop)
            {
                // Property-name anchored: a bare "height" must not match "line-height".
                var em = Regex.Match(body, @"(?<![-\w])" + prop + @":\s*(-?[\d.]+)em");
                return em.Success && double.TryParse(em.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
            }

            p.FontSizeEm ??= Em(body, "font-size");
            p.LetterSpacingEm ??= Em(body, "letter-spacing");
            p.WidthEm ??= Em(body, "width");
            p.HeightEm ??= Em(body, "height");
            if (p.Color is null)
            {
                var cm = Regex.Match(body, @"color:\s*(#[0-9a-fA-F]{6})");
                if (cm.Success) p.Color = cm.Groups[1].Value;
            }
            if (p.Family is null)
            {
                var fm = Regex.Match(body, @"font-family:\s*(?<v>[^;}]+)");
                if (fm.Success)
                {
                    // First family of the stack, unquoted; a subset tag ("ABCDEF+Name",
                    // the DefaultFontName shape) resolves to the bare name.
                    var fam = fm.Groups["v"].Value.Split(',')[0].Trim().Trim('"', '\'').Trim();
                    fam = Regex.Replace(fam, @"^[A-Z]{6}\+", "");
                    if (fam.Length > 0) p.Family = fam;
                }
            }
        }
        return map;
    }

    private sealed class StlRun
    {
        public double LeftPt, TopPt, FontSizePt, LetterSpacingPt, WordSpacingPt;
        public string Family = "Times New Roman";
        public string Color = "#000000";
        public string Text = "";       // drawn text (trailing sentinel space kept out)
        public double WidthPt;         // measured, spacing included (sentinel included)
        public double Baseline => TopPt + FontSizePt;
    }

    /// <summary>An @font-face family the stl_ document itself declares: the embedded
    /// font PROGRAM (data-URI or sidecar file, WOFF unwrapped to raw sfnt) used for
    /// measurement and re-embedding in preference to any installed face — subset
    /// PostScript names ("ArialMT", "Calibri-Bold") rarely resolve locally, and
    /// measurement must use the program the HTML itself carries.</summary>
    private sealed class StlFontFace
    {
        public byte[] Ttf = System.Array.Empty<byte>();
        public Text.GlyphOutlineParser? Parser;
        public double Upm = 1000;
        // Further programs sharing this face's bare family name: a subset-per-page
        // export ships many "XXXXXX+Family" programs, and a span styled with the
        // bare family can need any one of them (each carries its own glyph slice).
        public List<StlFontFace>? Alternates;
    }

    /// <summary>The primary declared face for <paramref name="family"/>. Beyond the
    /// exact key, tolerates the exporter's family-name munging: spacing differences
    /// ("Sim Hei" ↔ "SimHei") and a dropped style-looking token ("EU" ← "EU BZ").</summary>
    private static StlFontFace? StlFaceForFamily(
        Dictionary<string, StlFontFace>? htmlFaces, string family)
    {
        if (htmlFaces is null) return null;
        if (htmlFaces.TryGetValue(family, out var f)) return f;
        var squished = family.Replace(" ", "");
        foreach (var kv in htmlFaces)
            if (kv.Key.Replace(" ", "").Equals(squished, StringComparison.OrdinalIgnoreCase))
                return kv.Value;
        foreach (var kv in htmlFaces)
            if (kv.Key.StartsWith(family + " ", StringComparison.OrdinalIgnoreCase))
                return kv.Value;
        return null;
    }

    /// <summary>The declared face for <paramref name="family"/> whose program covers
    /// <paramref name="text"/>: the primary @font-face when it does, else the first
    /// covering alternate program registered under the same bare family. Null when
    /// none covers (callers fall back to installed faces).</summary>
    private static StlFontFace? CoveringStlFace(
        Dictionary<string, StlFontFace>? htmlFaces, string family, string text)
    {
        if (StlFaceForFamily(htmlFaces, family) is not { Parser: not null } f)
            return null;
        if (ParserCovers(f.Parser, text)) return f;
        if (f.Alternates is { } alts)
            foreach (var a in alts)
                if (a.Parser is not null && ParserCovers(a.Parser, text)) return a;
        return null;
    }

    /// <summary>Split <paramref name="text"/> into segments each mapped by ONE of the
    /// document's programs, when no single program of its family covers it alone — a
    /// line assembled from several shows carries each glyph in exactly one subset
    /// program, usually a family sibling ("AAAAAA+Family" … "AAAAAF+Family") but,
    /// for a fully merged line, possibly another family altogether. Family programs
    /// are preferred; spaces ride the current segment. Null when the family declares
    /// no face or even the document-wide union leaves more than the
    /// <see cref="ParserCovers"/> miss budget unmapped.</summary>
    private static List<(StlFontFace face, string text)>? UnionStlSegments(
        Dictionary<string, StlFontFace>? htmlFaces, string family, string text)
    {
        if (StlFaceForFamily(htmlFaces, family) is not { Parser: not null } primary)
            return null;
        var faces = new List<StlFontFace> { primary };
        if (primary.Alternates is { } palts)
            foreach (var a in palts)
                if (a.Parser is not null) faces.Add(a);
        // Document-wide pool, family faces first: a char absent from every family
        // sibling was drawn by another face the html also ships.
        var all = new List<StlFontFace>(faces);
        foreach (var other in htmlFaces!.Values)
        {
            if (other.Parser is not null && !all.Contains(other)) all.Add(other);
            if (other.Alternates is { } oalts)
                foreach (var a in oalts)
                    if (a.Parser is not null && !all.Contains(a)) all.Add(a);
        }

        var segs = new List<(StlFontFace face, string text)>();
        var cur = primary;
        var sb = new StringBuilder();
        int total = 0, hit = 0;
        void Flush()
        {
            if (sb.Length > 0) { segs.Add((cur, sb.ToString())); sb.Clear(); }
        }
        for (var i = 0; i < text.Length; i++)
        {
            int cp = text[i];
            var pair = char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]);
            if (pair) cp = char.ConvertToUtf32(text[i], text[i + 1]);
            if (cp is ' ' or 0x00A0)
            {
                sb.Append(text[i]);
                continue;
            }
            total++;
            var owner = cur.Parser!.CMap.TryGetValue(cp, out var g0) && g0 != 0 ? cur : null;
            if (owner is null)
                foreach (var f in faces)
                    if (f.Parser!.CMap.TryGetValue(cp, out var g) && g != 0) { owner = f; break; }
            if (owner is null)
                foreach (var f in all)
                    if (f.Parser!.CMap.TryGetValue(cp, out var g) && g != 0) { owner = f; break; }
            if (owner is not null)
            {
                hit++;
                if (!ReferenceEquals(owner, cur)) { Flush(); cur = owner; }
            }
            sb.Append(text[i]);
            if (pair) { sb.Append(text[i + 1]); i++; }
        }
        Flush();
        return total > 0 && hit * 10 >= total * 9 ? segs : null;
    }

    private static Dictionary<string, StlFontFace> ParseStlFontFaces(string css, HtmlLoadOptions? options)
    {
        var map = new Dictionary<string, StlFontFace>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(css, @"@font-face\s*\{(?<body>[^}]*)\}", RegexOptions.Singleline))
        {
            var body = m.Groups["body"].Value;
            var fam = Regex.Match(body, @"font-family:\s*""?(?<f>[^"";}]+)");
            if (!fam.Success) continue;
            var famName = fam.Groups["f"].Value.Trim();
            if (famName.Length == 0 || map.ContainsKey(famName)) continue;

            // A face can list several sources ("bulletproof" @font-face: EOT first
            // for old IE, then WOFF/TTF) — walk them all and keep the first program
            // that actually parses, unwrapping WOFF and EOT containers on the way.
            foreach (Match src in Regex.Matches(body, @"url\(\s*[""']?(?<u>[^)""']+?)[""']?\s*\)"))
            {
                byte[]? bytes = null;
                var url = src.Groups["u"].Value.Trim();
                try
                {
                    var dm = Regex.Match(url, @"^data:[^,]*;base64,(?<b>.+)$", RegexOptions.Singleline);
                    if (dm.Success) bytes = System.Convert.FromBase64String(dm.Groups["b"].Value);
                    else if (options?.BasePath is { Length: > 0 } bp)
                    {
                        var p = System.IO.Path.Combine(bp, url.Replace('/', System.IO.Path.DirectorySeparatorChar));
                        if (File.Exists(p)) bytes = File.ReadAllBytes(p);
                    }
                }
                catch { /* malformed src: try the next source */ }
                if (bytes is not { Length: > 4 }) continue;
                if (bytes[0] == (byte)'w' && bytes[1] == (byte)'O' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F')
                    bytes = TryUnwrapWoff(bytes);
                else if (bytes[0] != 0x00 || bytes[1] != 0x01)
                    bytes = TryUnwrapEot(bytes) ?? bytes;
                if (bytes is null) continue;
                try
                {
                    var parser = new Text.GlyphOutlineParser(bytes);
                    var face = new StlFontFace
                    {
                        Ttf = bytes,
                        Parser = parser,
                        Upm = parser.UnitsPerEm > 0 ? parser.UnitsPerEm : 1000,
                    };
                    map[famName] = face;
                    // A subset face ("AAAAAC+DroidSansFallback") is also reachable by
                    // its bare family — class rules and spans routinely drop the
                    // six-letter subset tag. Sibling subsets of the same family
                    // chain as alternates: each program carries a different slice
                    // of the document's glyphs, and a bare-family span can need
                    // any of them.
                    var plus = famName.IndexOf('+');
                    if (plus is > 0 and < 8 && famName.Length > plus + 1)
                    {
                        var bare = famName[(plus + 1)..];
                        if (!map.TryAdd(bare, face))
                            (map[bare].Alternates ??= new List<StlFontFace>()).Add(face);
                    }
                    break;
                }
                catch { /* unparsable program: try the next source */ }
            }
        }
        return map;
    }

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

    // ── Fixed-layout re-import of this library's own converter HTML ────────────────
    // When the page's stylesheet is resolvable, converter
    // output re-imports like a print engine: content keeps its source positions (scale 1) and is
    // placed onto A4-height sheets with a 96 pt left margin and a 78 pt content top;
    // each source page box is cut into 691.4 pt bands (the sheet's content height,
    // bottom edge 769.4 pt from the sheet top) that continue on following sheets. The
    // sheet width grows to the content: 96 + the widest laid-out element + 89.76,
    // where an <object> page graphic contributes its box but an <img> does not
    // (it has no intrinsic box at layout time). The constants cover
    // round-trips of the four raster/vector saving modes.

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

    /// <summary>Row-vector 2×3 matrix product: apply <paramref name="a"/> first, then
    /// <paramref name="b"/> — the composition SVG nesting and PDF cm both use.</summary>
    private static double[] MulM(double[] a, double[] b) => new[]
    {
        a[0] * b[0] + a[1] * b[2],
        a[0] * b[1] + a[1] * b[3],
        a[2] * b[0] + a[3] * b[2],
        a[2] * b[1] + a[3] * b[3],
        a[4] * b[0] + a[5] * b[2] + b[4],
        a[4] * b[1] + a[5] * b[3] + b[5],
    };

    /// <summary>Replay this library's own page-SVG (the bounded subset its PDF→HTML
    /// converter emits: nested <c>g[transform=matrix]</c>, absolute <c>M/L/C/Z</c>
    /// paths with rgb fills/strokes, and <c>image</c> references) as vector content
    /// onto <paramref name="page"/>. <paramref name="placement"/> maps the SVG's
    /// viewBox onto the sheet. Raster images resolve against
    /// <paramref name="svgDir"/>. Anything outside the subset is skipped silently —
    /// a partial page graphic still beats none.</summary>
    /// <summary>The rightmost drawn x of a page SVG's geometry as a FRACTION of its
    /// viewBox width (0..1+), or null when the SVG is page furniture whose whole box
    /// counts — anything carrying FILLED shapes or images — or when it holds
    /// constructs this scan does not model (arcs, unknown transforms, no viewBox).
    /// Only a stroke-only decoration (a header rule ending mid-page) narrows the
    /// sheet to its ink.</summary>
    private static double? TrySvgInkRightFraction(string svg)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        double Num(string v) => double.Parse(v, System.Globalization.NumberStyles.Float, inv);
        if (Regex.IsMatch(svg, @"<(?:image|rect)\b")
            || Regex.IsMatch(svg, @"fill=""(?!none"")"))
            return null;
        var vb = Regex.Match(svg, @"viewBox=""(?<a>-?[\d.]+)\s+(?<b>-?[\d.]+)\s+(?<c>[\d.]+)\s+(?<d>[\d.]+)""");
        if (!vb.Success) return null;
        var vbX = Num(vb.Groups["a"].Value);
        var vbW = Num(vb.Groups["c"].Value);
        if (vbW <= 0) return null;
        var total = new[] { 1.0, 0, 0, 1.0, 0, 0 };
        var stack = new Stack<double[]>();
        double? right = null;
        foreach (Match tag in Regex.Matches(svg, @"<(?<close>/)?(?<tag>g|path|image|rect|line)\b(?<attrs>[^>]*)>"))
        {
            var attrs = tag.Groups["attrs"].Value;
            if (tag.Groups["close"].Success)
            {
                if (tag.Groups["tag"].Value == "g" && stack.Count > 0) total = stack.Pop();
                continue;
            }
            void Point(double x, double y)
            {
                var tx = x * total[0] + y * total[2] + total[4];
                right = Math.Max(right ?? double.MinValue, tx);
            }
            switch (tag.Groups["tag"].Value)
            {
                case "g":
                {
                    stack.Push(total);
                    var tm = Regex.Match(attrs, @"transform=""matrix\((?<m>[-\d.eE ]+)\)""");
                    if (tm.Success)
                    {
                        var parts = tm.Groups["m"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 6)
                        {
                            var g = new double[6];
                            for (var k = 0; k < 6; k++) g[k] = Num(parts[k]);
                            total = MulM(g, total);
                        }
                    }
                    else if (attrs.Contains("transform=", StringComparison.Ordinal))
                        return null;    // translate()/scale() forms not modelled here
                    if (attrs.TrimEnd().EndsWith("/", StringComparison.Ordinal)) total = stack.Pop();
                    break;
                }
                case "path":
                {
                    var d = Regex.Match(attrs, @"d=""(?<d>[^""]*)""").Groups["d"].Value;
                    if (Regex.IsMatch(d, @"[AaHhVv]")) return null;   // axis/arc shorthands: bail
                    var nums = Regex.Matches(d, @"-?\d*\.?\d+(?:[eE][-+]?\d+)?");
                    for (var k = 0; k + 1 < nums.Count; k += 2)
                        Point(Num(nums[k].Value), Num(nums[k + 1].Value));
                    break;
                }
                case "rect" or "image":
                {
                    var xm = Regex.Match(attrs, @"\bx=""(?<v>-?[\d.]+)""");
                    var wm = Regex.Match(attrs, @"\bwidth=""(?<v>[\d.]+)""");
                    var ym = Regex.Match(attrs, @"\by=""(?<v>-?[\d.]+)""");
                    var x1 = (xm.Success ? Num(xm.Groups["v"].Value) : 0)
                        + (wm.Success ? Num(wm.Groups["v"].Value) : 0);
                    Point(x1, ym.Success ? Num(ym.Groups["v"].Value) : 0);
                    break;
                }
                case "line":
                {
                    foreach (var a in new[] { "x1", "x2" })
                    {
                        var m2 = Regex.Match(attrs, @"\b" + a + @"=""(?<v>-?[\d.]+)""");
                        var ym2 = Regex.Match(attrs, @"\by" + a[1] + @"=""(?<v>-?[\d.]+)""");
                        if (m2.Success)
                            Point(Num(m2.Groups["v"].Value), ym2.Success ? Num(ym2.Groups["v"].Value) : 0);
                    }
                    break;
                }
            }
        }
        return right is { } r ? (r - vbX) / vbW : null;
    }

    private static void ReplaySvgObject(Page page, string svg, double[] placement,
        string svgDir, HtmlLoadOptions? options)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        double Num(string v) => double.Parse(v, System.Globalization.NumberStyles.Float, inv);

        // viewBox → prepend its origin/scale into the placement.
        var vb = Regex.Match(svg, @"viewBox=""(?<a>-?[\d.]+)\s+(?<b>-?[\d.]+)\s+(?<c>[\d.]+)\s+(?<d>[\d.]+)""");
        var total = placement;
        if (vb.Success)
        {
            var vx = Num(vb.Groups["a"].Value);
            var vy = Num(vb.Groups["b"].Value);
            var vw = Num(vb.Groups["c"].Value);
            var vh = Num(vb.Groups["d"].Value);
            _ = vw; _ = vh; // placement is already scaled to the viewBox size by the caller
            total = MulM(new[] { 1.0, 0, 0, 1.0, -vx, -vy }, placement);
        }

        var stack = new Stack<double[]>();
        var sb = new StringBuilder();

        void FlushPaths()
        {
            if (sb.Length == 0) return;
            page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
            sb.Clear();
        }

        foreach (Match tag in Regex.Matches(svg, @"<(?<close>/)?(?<tag>g|path|image)\b(?<attrs>[^>]*)>"))
        {
            var attrs = tag.Groups["attrs"].Value;
            if (tag.Groups["close"].Success)
            {
                if (tag.Groups["tag"].Value == "g" && stack.Count > 0) total = stack.Pop();
                continue;
            }
            switch (tag.Groups["tag"].Value)
            {
                case "g":
                {
                    stack.Push(total);
                    var tm = Regex.Match(attrs,
                        @"transform=""matrix\((?<m>[-\d.eE ]+)\)""");
                    if (tm.Success)
                    {
                        var parts = tm.Groups["m"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 6)
                        {
                            var g = new double[6];
                            for (var k = 0; k < 6; k++) g[k] = Num(parts[k]);
                            total = MulM(g, total);
                        }
                    }
                    if (attrs.TrimEnd().EndsWith("/", StringComparison.Ordinal)) total = stack.Pop();
                    break;
                }
                case "path":
                {
                    // The boundary keeps this from matching the tail of `id="…"`
                    // (the inline dialect's paths carry an id attribute).
                    var d = Regex.Match(attrs, @"(?<![\w-])d=""(?<d>[^""]*)""");
                    if (!d.Success) break;
                    var fillM = Regex.Match(attrs, @"fill=""(?<v>[^""]*)""");
                    var strokeM = Regex.Match(attrs, @"stroke=""(?<v>[^""]*)""");
                    var widthM = Regex.Match(attrs, @"stroke-width=""(?<v>[-\d.]+)""");
                    var fill = ParseSvgRgb(fillM.Success ? fillM.Groups["v"].Value : "rgb(0,0,0)");
                    var stroke = ParseSvgRgb(strokeM.Success ? strokeM.Groups["v"].Value : "none");
                    if (fill is null && stroke is null) break;
                    var ops = SvgPathToPdfOps(d.Groups["d"].Value);
                    if (ops is null) break;

                    // The inline dialect leaves the y flip to each path's own
                    // transform matrix rather than the wrapper's.
                    var pathTotal = total;
                    var ptm = Regex.Match(attrs, @"transform=""matrix\((?<m>[-\d.eE ]+)\)""");
                    if (ptm.Success)
                    {
                        var pparts = ptm.Groups["m"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (pparts.Length == 6)
                        {
                            var pm = new double[6];
                            for (var k = 0; k < 6; k++) pm[k] = Num(pparts[k]);
                            pathTotal = MulM(pm, total);
                        }
                    }

                    sb.Append("q ");
                    sb.Append(string.Join(' ', pathTotal.Select(v => v.ToString("F5", inv))));
                    sb.Append(" cm ");
                    if (fill is not null)
                        sb.Append($"{fill.Value.r.ToString("F3", inv)} {fill.Value.g.ToString("F3", inv)} {fill.Value.b.ToString("F3", inv)} rg ");
                    if (stroke is not null)
                    {
                        sb.Append($"{stroke.Value.r.ToString("F3", inv)} {stroke.Value.g.ToString("F3", inv)} {stroke.Value.b.ToString("F3", inv)} RG ");
                        var wRaw = widthM.Success ? Num(widthM.Groups["v"].Value) : 1.0;
                        sb.Append($"{Math.Abs(wRaw).ToString("F3", inv)} w ");
                    }
                    sb.Append(ops);
                    sb.AppendLine(fill is not null && stroke is not null ? "B" : fill is not null ? "f" : "S");
                    sb.AppendLine("Q");
                    break;
                }
                case "image":
                {
                    var href = Regex.Match(attrs, @"(?:xlink:)?href=""(?<v>[^""]+)""");
                    if (!href.Success) break;
                    double ix = 0, iy = 0, iw = 0, ih = 0;
                    var xm = Regex.Match(attrs, @"(?<![\w-])x=""(?<v>-?[\d.]+)""");
                    var ym = Regex.Match(attrs, @"(?<![\w-])y=""(?<v>-?[\d.]+)""");
                    var wm = Regex.Match(attrs, @"width=""(?<v>[\d.]+)""");
                    var hm = Regex.Match(attrs, @"height=""(?<v>[\d.]+)""");
                    if (xm.Success) ix = Num(xm.Groups["v"].Value);
                    if (ym.Success) iy = Num(ym.Groups["v"].Value);
                    if (wm.Success) iw = Num(wm.Groups["v"].Value);
                    if (hm.Success) ih = Num(hm.Groups["v"].Value);
                    if (iw <= 0 || ih <= 0) break;
                    var url = DecodeEntities(href.Groups["v"].Value);
                    var bytes = LoadConverterImage(url, options)
                                ?? (svgDir.Length > 0 ? LoadConverterImage(svgDir + url, options) : null);
                    if (bytes is null) break;
                    // The image box in local coords maps through the total matrix; only
                    // axis-aligned results can go through AddImage — rotation is rare in
                    // this generator's output and is skipped rather than mis-drawn.
                    if (Math.Abs(total[1]) > 1e-6 || Math.Abs(total[2]) > 1e-6) break;
                    var x0 = ix * total[0] + total[4];
                    var y0 = iy * total[3] + total[5];
                    var x1 = (ix + iw) * total[0] + total[4];
                    var y1 = (iy + ih) * total[3] + total[5];
                    FlushPaths();
                    try
                    {
                        page.AddImage(bytes, new Aspose.Pdf.Rectangle(
                            Math.Min(x0, x1), Math.Min(y0, y1), Math.Max(x0, x1), Math.Max(y0, y1)));
                    }
                    catch { /* undecodable image — skip */ }
                    break;
                }
            }
        }
        FlushPaths();
    }

    private static (double r, double g, double b)? ParseSvgRgb(string v)
    {
        v = v.Trim();
        if (v.Length == 0 || v.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
        var m = Regex.Match(v, @"rgb\(\s*(?<r>\d+)\s*,\s*(?<g>\d+)\s*,\s*(?<b>\d+)\s*\)");
        if (m.Success)
            return (int.Parse(m.Groups["r"].Value) / 255.0,
                    int.Parse(m.Groups["g"].Value) / 255.0,
                    int.Parse(m.Groups["b"].Value) / 255.0);
        var h = Regex.Match(v, @"^#(?<h>[0-9a-fA-F]{6})$");
        if (h.Success)
        {
            var s = h.Groups["h"].Value;
            return (System.Convert.ToInt32(s[..2], 16) / 255.0,
                    System.Convert.ToInt32(s[2..4], 16) / 255.0,
                    System.Convert.ToInt32(s[4..6], 16) / 255.0);
        }
        return (0, 0, 0);
    }

    /// <summary>Translate an absolute-command SVG path (<c>M/L/C/Z</c>, the only
    /// commands the converter emits) into PDF path operators. Null when the data
    /// contains anything else.</summary>
    private static string? SvgPathToPdfOps(string d)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        var tokens = Regex.Matches(d, @"[MLCZmlcz]|-?[\d.]+(?:[eE][-+]?\d+)?");
        var nums = new List<double>();
        var cmd = '\0';
        var i = 0;
        while (i < tokens.Count)
        {
            var t = tokens[i].Value;
            if (t.Length == 1 && char.IsLetter(t[0]))
            {
                cmd = t[0];
                i++;
                if (cmd is 'Z' or 'z') { sb.Append("h "); continue; }
                if (cmd is not ('M' or 'L' or 'C')) return null;
            }
            var need = cmd == 'C' ? 6 : 2;
            nums.Clear();
            while (nums.Count < need && i < tokens.Count && tokens[i].Value is { } nv
                   && (char.IsDigit(nv[0]) || nv[0] is '-' or '.'))
            {
                nums.Add(double.Parse(nv, System.Globalization.NumberStyles.Float, inv));
                i++;
            }
            if (nums.Count < need) return sb.Length > 0 ? sb.ToString() : null;
            foreach (var n in nums) sb.Append(n.ToString("F3", inv)).Append(' ');
            sb.Append(cmd switch { 'M' => "m ", 'C' => "c ", _ => "l " });
            // Successive coordinate pairs after an M continue as line-tos.
            if (cmd == 'M') cmd = 'L';
        }
        return sb.ToString();
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

    /// <summary>The bare glyph advance of <paramref name="text"/> in the run's face
    /// and size — no letter-spacing, no word-spacing. The em-compensation sheet
    /// budget adds those two itself, over term counts of its own.</summary>
    private static double MeasureStlAdvOnly(StlRun run, string text,
        Dictionary<string, StlFontFace>? htmlFaces = null)
    {
        if (CoveringStlFace(htmlFaces, run.Family, text) is { Parser: not null } hf)
            return MeasureParsedExact(hf.Parser, hf.Upm, text, run.FontSizePt);
        if (UnionStlSegments(htmlFaces, run.Family, text) is { } segs)
        {
            double u = 0;
            foreach (var (face, seg) in segs)
                u += MeasureParsedExact(face.Parser, face.Upm, seg, run.FontSizePt);
            return u;
        }
        return PosFace(run.Family).parser is not null
            ? MeasureStlExactText(run.Family, text, run.FontSizePt)
            : MeasureStlExactText("Times New Roman", text, run.FontSizePt);
    }

    private static double MeasureStlRun(StlRun run, string text,
        Dictionary<string, StlFontFace>? htmlFaces = null)
    {
        double w;
        if (CoveringStlFace(htmlFaces, run.Family, text) is { Parser: not null } hf)
            w = MeasureParsedExact(hf.Parser, hf.Upm, text, run.FontSizePt);
        else if (UnionStlSegments(htmlFaces, run.Family, text) is { } segs)
        {
            w = 0;
            foreach (var (face, seg) in segs)
                w += MeasureParsedExact(face.Parser, face.Upm, seg, run.FontSizePt);
        }
        else
        {
            var face = PosFace(run.Family);
            w = face.parser is not null
                ? MeasureStlExactText(run.Family, text, run.FontSizePt)
                : MeasureStlExactText("Times New Roman", text, run.FontSizePt);
        }
        w += run.LetterSpacingPt * text.Length;
        if (run.WordSpacingPt != 0)
        {
            foreach (var ch in text)
                if (ch == ' ') w += run.WordSpacingPt;
            // IE-model CJK: adjacent full-em characters take word-spacing too.
            for (var ci = 1; ci < text.Length; ci++)
                if (StlIdeograph(text[ci - 1]) && StlIdeograph(text[ci]))
                    w += run.WordSpacingPt;
        }
        return w;
    }

    private static Document ConvertStlPositioned(string html, HtmlLoadOptions? options)
    {
        const double StlEmPt = 12.0;
        var stlCss = GatherStlCss(html, options);
        var classProps = ParseStlClassProps(stlCss);
        // The em-compensation dialect keeps every letter-spacing on a 0.01 em grid
        // (the word-spacing absorbs the rounding residue), and its WIDTH BUDGET
        // drops letter-spacing entirely: in such a
        // round trip, two adjacent justified lines carrying word-spacings of
        // opposite SIGN (+0.05 and -0.01 em) both solve to one right edge under
        // glyph advances + word-spacing alone, and the derived sheet runs 13 pt
        // past the page's drawn ink - consistent only with the letter-spacing
        // excluded. The grid is the dialect's signature: the default dialect
        // solves letter-spacings at four decimals.
        var emCompensationGrid = false;
        {
            var sawNonZeroLs = false;
            var allOnGrid = true;
            foreach (var cp in classProps.Values)
            {
                if (cp.LetterSpacingEm is not { } le || le == 0) continue;
                sawNonZeroLs = true;
                var cents = le * 100.0;
                if (Math.Abs(cents - Math.Round(cents)) > 1e-6) { allOnGrid = false; break; }
            }
            emCompensationGrid = sawNonZeroLs && allOnGrid;
        }
        var htmlFaces = ParseStlFontFaces(stlCss, options);
        // Constant content inset inside the margins (0.5em of the 12pt root):
        // every line and the background raster render 6pt right and 6pt
        // down of (ML, MT), and the same 6pt is each line box's width-budget tail.
        const double stlContentPad = 6.0;

        var pageInfo = options?.PageInfo;
        var pageW0 = pageInfo?.Width is > 0 ? pageInfo.Width : 595.0;
        var pageH = pageInfo?.Height is > 0 ? pageInfo.Height : 842.0;
        var pageMargin = pageInfo?.Margin;
        var marginsExplicit = pageMargin?.IsTouched ?? false;
        var ml = marginsExplicit ? pageMargin!.Left : 90.0;
        var mr = marginsExplicit ? pageMargin!.Right : 90.0;
        var mt = marginsExplicit ? pageMargin!.Top : 72.0;
        var mb = marginsExplicit ? pageMargin!.Bottom : 72.0;
        var band = Math.Max(1.0, pageH - mt - mb);

        double Num(string s) => double.Parse(s,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture);

        var pageDivs = Regex.Matches(html, @"<div id=""page_\d+""[^>]*>");

        // ── Harvest every page's runs and background image first: the page WIDTH is
        // document-wide (the widest line anywhere), so layout needs the full sweep. ──
        var pagesRuns = new List<List<StlRun>>();
        var pagesImage = new List<(byte[] bytes, double wPt, double hPt)?>();
        double maxRight = 0;

        for (var p = 0; p < pageDivs.Count; p++)
        {
            var segStart = pageDivs[p].Index;
            var segEnd = p + 1 < pageDivs.Count ? pageDivs[p + 1].Index : html.Length;
            var seg = html[segStart..segEnd];
            var runs = new List<StlRun>();

            // The page container's own box (width em × 12): text overflowing the
            // fixed box never widens the sheet — a custom-encoded run whose measured
            // advance overshoots still yields the box-bound width.
            double boxW = 0;
            var pdCls = Regex.Match(pageDivs[p].Value, @"class=""(?<c>[^""]+)""");
            if (pdCls.Success)
                foreach (var c in pdCls.Groups["c"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    if (classProps.TryGetValue(c, out var cpBox) && cpBox.WidthEm is { } weBox)
                    {
                        boxW = weBox * StlEmPt;
                        break;
                    }

            foreach (Match dm in Regex.Matches(seg,
                @"<div class=""[^""]*"" style=""left:(?<l>-?[\d.]+)em;\s*top:(?<t>-?[\d.]+)em;?""[^>]*>(?<body>.*?)</div>",
                RegexOptions.Singleline))
            {
                var leftPt = Num(dm.Groups["l"].Value) * StlEmPt;
                var topPt = Num(dm.Groups["t"].Value) * StlEmPt;
                var x = leftPt;
                var sentinelAdv = 0.0;
                var lineHasBox = false;
                var lsBudget = 0.0;
                // The em-compensation dialect's sheet budget is a rule of its own
                // (see gridBudget below).
                var gridBudget = leftPt;

                var spanMatches = Regex.Matches(dm.Groups["body"].Value,
                    @"<span class=""(?<scls>[^""]*)""(?:\s+style=""(?<sst>[^""]*)"")?[^>]*>(?<stext>.*?)</span>",
                    RegexOptions.Singleline);
                for (var spanIdx = 0; spanIdx < spanMatches.Count; spanIdx++)
                {
                    var sm = spanMatches[spanIdx];
                    var isLastSpan = spanIdx == spanMatches.Count - 1;
                    var raw = DecodeEntities(Regex.Replace(sm.Groups["stext"].Value, "<[^>]+>", ""));
                    if (raw.Length == 0) continue;
                    lineHasBox = true;

                    var run = new StlRun { LeftPt = x, TopPt = topPt };
                    double fsEm = 1.0; var fsSet = false;
                    string? family = null, color = null;
                    double? lsEm = null;
                    foreach (var c in sm.Groups["scls"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (!classProps.TryGetValue(c, out var cp)) continue;
                        if (!fsSet && cp.FontSizeEm is { } fe) { fsEm = fe; fsSet = true; }
                        family ??= cp.Family;
                        color ??= cp.Color;
                        lsEm ??= cp.LetterSpacingEm;
                    }
                    run.FontSizePt = fsEm * StlEmPt;
                    if (family is not null) run.Family = family;
                    if (color is not null) run.Color = color;
                    // letter-spacing / word-spacing em are relative to the span's own font size
                    if (lsEm is { } l0) run.LetterSpacingPt = l0 * run.FontSizePt;
                    var ws = Regex.Match(sm.Groups["sst"].Value ?? "", @"word-spacing:\s*(-?[\d.]+)em");
                    if (ws.Success) run.WordSpacingPt = Num(ws.Groups[1].Value) * run.FontSizePt;

                    // The raw text (nbsp sentinel included) measures in the stl_
                    // model — nbsp advances as the space glyph and takes a
                    // letter-spacing slot but no word-spacing slot; the drawn text
                    // drops the trailing sentinel, keeps interior spacing verbatim.
                    var measureText = raw.Replace(' ', ' ');
                    run.WidthPt = MeasureStlRun(run, raw, htmlFaces);
                    run.Text = measureText.TrimEnd();
                    if (run.Text.Length > 0) runs.Add(run);
                    x += run.WidthPt;
                    lsBudget += run.LetterSpacingPt * raw.Length;
                    // ── The em-compensation dialect's own sheet budget ──
                    // The budget sums, per span:
                    //   · glyph advances of the visible text INCLUDING its trailing
                    //     space, plus a space advance for the nbsp sentinel;
                    //   · letter-spacing after every character EXCEPT the trailing space
                    //     and the sentinel;
                    //   · word-spacing on every space AND every HYPHEN — except that a
                    //     span-final space with a further span behind it on the same
                    //     line takes none (the sheet width only comes out right
                    //     with that one space uncredited).
                    // U+00A0 - the line-final sentinel the exporter appends.
                    const char Nbsp = ' ';
                    var visible = raw.TrimEnd(Nbsp);
                    var sentinelChars = raw.Length - visible.Length;
                    var gridAdv = MeasureStlAdvOnly(run, visible, htmlFaces);
                    if (sentinelChars > 0)
                        gridAdv += MeasureStlAdvOnly(run, new string(' ', sentinelChars), htmlFaces);
                    var lsCarriers = visible.TrimEnd(' ').Length;
                    var wsSlots = visible.Count(ch => ch == ' ' || ch == '-');
                    if (!isLastSpan && visible.EndsWith(' ')) wsSlots--;
                    // The IE-model layout charges word-spacing at every
                    // boundary between two adjacent full-em CJK characters, exactly
                    // as at a drawn space (a spread heading of 8 ideographs
                    // and 2 spaces takes ws on all 8 slots — 6 ideograph pairs +
                    // the 2 spaces).
                    for (var ci2 = 1; ci2 < visible.Length; ci2++)
                        if (StlIdeograph(visible[ci2 - 1]) && StlIdeograph(visible[ci2]))
                            wsSlots++;
                    gridBudget += gridAdv + run.LetterSpacingPt * lsCarriers
                        + run.WordSpacingPt * wsSlots;
                    // The line-final sentinel &nbsp; dangles beyond the sheet's
                    // width budget; the trailing space and its word-spacing stay in.
                    // The sentinel advances as the space glyph plus letter-spacing and
                    // takes no word-spacing slot (which MeasureStlRun adds for ' ').
                    sentinelAdv = raw.EndsWith('\u00A0')
                        ? MeasureStlRun(run, " ", htmlFaces) - run.WordSpacingPt
                        : 0.0;
                }
                // All page CONTENT (lines and the background
                // raster alike) is offset by a constant 6pt (0.5em of the 12pt root) right and
                // down inside the margins; the page width then grows to
                // max(default, ML + 6 + line end + MR). The line ends after its trailing
                // space and word-spacing — only the sentinel nbsp hangs outside the
                // budget. (Stripping the whole trailing run sits 15 pt under the
                // correct 741/747 pt for the same file; keeping the
                // sentinel overshoots the sheet the other way.)
                if (lineHasBox)
                {
                    var lineEnd = emCompensationGrid ? gridBudget : x - sentinelAdv;
                    maxRight = Math.Max(maxRight,
                        (boxW > 0 ? Math.Min(lineEnd, boxW) : lineEnd) + stlContentPad);
                }
            }
            pagesRuns.Add(runs);

            // Background raster (PNG page background / embedded data URI): sized by the
            // page-box class (width/height em × 12).
            (byte[], double, double)? bg = null;
            var img = Regex.Match(seg, @"<img src=""(?<src>[^""]+)""");
            if (img.Success)
            {
                var bytes = LoadConverterImage(DecodeEntities(img.Groups["src"].Value), options);
                if (bytes is not null && !IsSvgBytes(bytes))
                {
                    double bw = 0, bh = 0;
                    var pd = Regex.Match(pageDivs[p].Value, @"class=""(?<c>[^""]+)""");
                    if (pd.Success)
                        foreach (var c in pd.Groups["c"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                            if (classProps.TryGetValue(c, out var cp) && cp.WidthEm is { } we && cp.HeightEm is { } he)
                            {
                                bw = we * StlEmPt; bh = he * StlEmPt;
                                break;
                            }
                    if (bw > 0 && bh > 0) bg = (bytes, bw, bh);
                }
            }
            pagesImage.Add(bg);
        }

        var pageW = Math.Max(pageW0, ml + maxRight + mr);

        // ── Emit ──
        var doc = Document.Create();
        var docFontDict = new Core.PdfDictionary();
        var inv = System.Globalization.CultureInfo.InvariantCulture;

        // The em-compensation dialect breaks a line to the next page when its
        // TOP plus a fixed line-box reserve overruns the content band:
        // a line at rel-top
        // 678.24 pt stays on a 698 pt band and 678.48 breaks, bracketing the
        // reserve in [19.52, 19.76) pt. The default dialect keeps its
        // baseline rule.
        const double EmGridPageBreakReservePt = 19.6;
        int PageOf(StlRun r) => (int)Math.Floor(Math.Max(0,
            (emCompensationGrid ? r.TopPt + EmGridPageBreakReservePt : r.Baseline)
            - 1e-6) / band);

        for (var p = 0; p < pagesRuns.Count; p++)
        {
            var runs = pagesRuns[p];
            var kMax = 0;
            foreach (var r in runs)
                kMax = Math.Max(kMax, PageOf(r));

            var outPages = new Page[kMax + 1];
            for (var k = 0; k <= kMax; k++)
            {
                var pg = doc.Pages.Add(pageW, pageH);
                EnsureFonts(pg, docFontDict);
                outPages[k] = pg;
                if (pagesImage[p] is { } bg)
                {
                    // The page background keeps its box size at the 6pt content
                    // inset; each output page shows its band slice of it (clipped to
                    // the content band, so the raster pages along
                    // with the text).
                    var bgLeft = ml + stlContentPad;
                    var top = pageH - mt - stlContentPad + k * band;
                    var clip = string.Create(System.Globalization.CultureInfo.InvariantCulture,
                        $"q {bgLeft:F2} {pageH - mt - stlContentPad - band:F2} {bg.wPt:F2} {band:F2} re W n\n");
                    outPages[k].AddContentStream(Encoding.ASCII.GetBytes(clip));
                    try { outPages[k].AddImage(bg.bytes, new Rectangle(bgLeft, top - bg.hPt, bgLeft + bg.wPt, top)); }
                    catch { /* undecodable background: text-only re-import */ }
                    outPages[k].AddContentStream(Encoding.ASCII.GetBytes("Q\n"));
                }
            }

            foreach (var r in runs)
            {
                var k = PageOf(r);
                var pg = outPages[k];
                var x = ml + stlContentPad + r.LeftPt;
                var y = pageH - mt - stlContentPad - (r.Baseline - k * band);

                // Face resolution: the document's own @font-face program first, then
                // an installed face by that family name, then the serif fallback.
                byte[]? faceTtf;
                Text.GlyphOutlineParser? faceParser;
                double faceUpm;
                var faceName = r.Family;
                List<(StlFontFace face, string text)>? runUnion = null;
                if (CoveringStlFace(htmlFaces, r.Family, r.Text) is { Parser: not null } hface)
                {
                    faceTtf = hface.Ttf; faceParser = hface.Parser; faceUpm = hface.Upm;
                }
                else if ((runUnion = UnionStlSegments(htmlFaces, r.Family, r.Text)) is not null)
                {
                    // Sibling subsets jointly cover the line; each piece below embeds
                    // its own program. The primary face carries the space advance.
                    faceTtf = runUnion[0].face.Ttf;
                    faceParser = runUnion[0].face.Parser;
                    faceUpm = runUnion[0].face.Upm;
                }
                else
                {
                    var face = PosFace(r.Family);
                    if (face.ttf is null) { faceName = "Times New Roman"; face = PosFace(faceName); }
                    faceTtf = face.ttf; faceParser = face.parser; faceUpm = face.upm;
                }
                if (faceTtf is null) continue;

                var res = pg.Dict.Get("Resources") as Core.PdfDictionary;
                var fontDict = res?.Get("Font") as Core.PdfDictionary ?? docFontDict;

                var sb = new StringBuilder();
                sb.Append("BT ");
                var cr = System.Convert.ToInt32(r.Color.Substring(1, 2), 16) / 255.0;
                var cg = System.Convert.ToInt32(r.Color.Substring(3, 2), 16) / 255.0;
                var cb = System.Convert.ToInt32(r.Color.Substring(5, 2), 16) / 255.0;
                sb.Append($"{cr.ToString("0.###", inv)} {cg.ToString("0.###", inv)} {cb.ToString("0.###", inv)} rg ");

                // Word-spacing applies per space glyph — and, in the IE model the
                // em-compensation dialect follows, between two adjacent full-em
                // CJK characters. PDF Tw does not act on the Type0 2-byte
                // encoding, so segments between the boundaries are placed at
                // their computed x offsets instead.
                var segments = new List<(string Text, char Sep)>();
                if (r.WordSpacingPt != 0)
                {
                    var segB = new StringBuilder();
                    foreach (var ch in r.Text)
                    {
                        if (ch == ' ') { segments.Add((segB.ToString(), ' ')); segB.Clear(); continue; }
                        if (segB.Length > 0 && StlIdeograph(segB[^1]) && StlIdeograph(ch))
                        { segments.Add((segB.ToString(), 'c')); segB.Clear(); }
                        segB.Append(ch);
                    }
                    segments.Add((segB.ToString(), '\0'));
                }
                else
                    segments.Add((r.Text, '\0'));
                var segX = x;
                for (var si = 0; si < segments.Count; si++)
                {
                    var segText = segments[si].Text;
                    if (segText.Length > 0)
                    {
                        var pieces = runUnion is null
                            ? null
                            : UnionStlSegments(htmlFaces, r.Family, segText);
                        foreach (var (pieceTtf, pieceParser, pieceUpm, pieceText) in
                            pieces is null
                                ? new[] { (faceTtf, faceParser, faceUpm, segText) }
                                : pieces.Select(p => (p.face.Ttf, p.face.Parser, p.face.Upm, p.text)))
                        {
                            if (pieceText.Length == 0) continue;
                            var (rn, hex) = Text.Type0FontEmbedder.Embed(fontDict, pieceTtf,
                                faceName, pieceText, stripSpacesInBaseFont: true);
                            sb.Append($"/{rn} {r.FontSizePt.ToString("F1", inv)} Tf ");
                            if (r.LetterSpacingPt != 0)
                                sb.Append($"{r.LetterSpacingPt.ToString("F3", inv)} Tc ");
                            sb.Append($"1 0 0 1 {segX.ToString("F2", inv)} {y.ToString("F2", inv)} Tm ");
                            sb.Append('<').Append(System.Convert.ToHexString(hex)).Append("> Tj ");
                            if (r.LetterSpacingPt != 0) sb.Append("0 Tc ");
                            segX += MeasureParsedExact(pieceParser, pieceUpm, pieceText, r.FontSizePt)
                                  + r.LetterSpacingPt * pieceText.Length;
                        }
                    }
                    if (segments[si].Sep == ' ')
                        segX += MeasureParsedExact(faceParser, faceUpm, " ", r.FontSizePt)
                              + r.LetterSpacingPt + r.WordSpacingPt;
                    else if (segments[si].Sep == 'c')
                        segX += r.WordSpacingPt;
                }
                sb.AppendLine("ET");
                pg.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
            }
        }

        if (doc.Pages.Count == 0)
        {
            var pg = doc.Pages.Add(pageW, pageH);
            EnsureFonts(pg, docFontDict);
        }

        PruneUnusedFonts(doc);
        return doc;
    }

    private static int _ownFaceIds;

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

    private static bool HasRtlChar(string s)
    {
        foreach (var ch in s)
            if ((ch >= 0x0590 && ch <= 0x08FF) || (ch >= 0xFB1D && ch <= 0xFEFC))
                return true;
        return false;
    }

    private static (double r, double g, double b)? ParseCssColorRgb(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return (0, 0, 0);
        v = v.Trim();
        if (v.Equals("transparent", StringComparison.OrdinalIgnoreCase)) return null;
        var m = Regex.Match(v, @"^#(?<h>[0-9a-fA-F]{6})$");
        if (m.Success)
        {
            var h = m.Groups["h"].Value;
            return (System.Convert.ToInt32(h[..2], 16) / 255.0,
                    System.Convert.ToInt32(h[2..4], 16) / 255.0,
                    System.Convert.ToInt32(h[4..6], 16) / 255.0);
        }
        return (0, 0, 0);
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

    private static Document ConvertPositionedSpans(string html, HtmlLoadOptions? options)
    {
        // Reflow constants for the re-import of converter HTML:
        // uniform 12pt serif text on a constant 13.5pt baseline pitch, 96pt left
        // margin, first baseline 88.8pt from the page top. The output page keeps the
        // load-options height (A4 default); its width depends on the dialect: the
        // pdf-page shape widens to source-page width + 62.01, the stl_ shape to the
        // longest unbreakable word + both side margins (see the three stl_
        // constants below).
        const string FaceName = "Times New Roman";
        const double FontSizePt = 12.0;
        const double PitchPt = 13.5;
        const double MarginSide = 96.0;
        const double FirstBaselinePt = 88.80;   // from the page top
        const double BottomMarginPt = 72.0;
        const double PageWidthPad = 62.01;      // pdf-page: output page width − source div width
        const double StlContinuationBaselinePt = 82.80; // stl_: first baseline of every output page after the first
        const double StlRightMarginPt = 90.0;   // stl_: sheet width − left margin − longest unit
        const double StlPageFloorPt = 595.0;    // stl_: A4 width floor when no unit forces widening
        const double StlSupFontSizePt = 10.0;   // stl_: a <sup> run renders at HTML "smaller" of the 12pt flow
        const double StlSupRisePt = 4.2;        // stl_: sup baseline raise (its −0.42em inline top × 10pt)
        const double StlSupLineExtraPt = 2.4;   // stl_: a line carrying a sup run takes 15.9pt of lead, not 13.5
        const double IconOffsetX = 91.0;        // graphical-link icon offset from the annot rect
        const double IconOffsetY = 73.0;
        const double IconSizePt = 32.0;

        // ── Parse the page divs, their spans and link overlays ──
        // Two source dialects: the older pdf-page shape (inline pt styles) and the
        // stl_ class scheme (page_N containers, left/top in em, 1 em = 12 pt,
        // appearance via stylesheet classes). Both reflow identically from here on.
        var pageDivs = Regex.Matches(html, @"<div class=""pdf-page""[^>]*style=""(?<st>[^""]*)""[^>]*>");
        var stlDialect = pageDivs.Count == 0;
        if (stlDialect)
        {
            pageDivs = Regex.Matches(html, @"<div id=""page_\d+""[^>]*style=""(?<st>[^""]*)""[^>]*>");
            // Newer stl_ exports carry no inline style on the page container —
            // its box lives in a stylesheet class (<div id="page_0" class="stl_02">,
            // .stl_02 { width: 51em; height: 66em; }). Match the bare container
            // and resolve the box from the class below.
            if (pageDivs.Count == 0)
                pageDivs = Regex.Matches(html, @"<div id=""page_\d+""[^>]*>");
        }
        var stlFontSizes = stlDialect ? ParseStlFontSizes(html, options) : null;
        // Two stl_ shapes with distinct behaviour: a raster-background
        // page (<img class="stl_04">) follows the reflow below; an
        // object/svg-background page keeps the legacy flow (the two differ
        // structurally - a raster img occupies a flow slot, an object
        // does not).
        var stlImgBg = stlDialect && Regex.IsMatch(html, @"<img[^>]*class=""stl_04""");
        const double StlEmPt = 12.0; // the stl_ scheme's em unit (font-size:10em/scale(0.1) trick)

        var pageInfo = options?.PageInfo;
        var pageH = pageInfo?.Height is > 0 ? pageInfo.Height : 842.0;
        // Same IsLandscape no-op as ConvertFromHtml: a flag-swapped A4 keeps its long side.
        if (pageInfo?.LandscapeSwapApplied == true && pageInfo.Width > pageH)
            pageH = pageInfo.Width;
        double srcDivW = 612.0;
        if (pageDivs.Count > 0)
        {
            var inlineW = StylePt(pageDivs[0].Groups["st"].Value, "width");
            if (inlineW is null && stlDialect)
            {
                // Style-less stl_ page container: read the box width (em) from the
                // container's stylesheet class; 1 em = 12 pt in the stl_ scheme.
                var clsAttr = Regex.Match(pageDivs[0].Value, @"class=""(?<c>[^""]+)""");
                if (clsAttr.Success)
                {
                    var css = GatherStlCss(html, options);
                    foreach (var cls in clsAttr.Groups["c"].Value.Split(' ',
                                 StringSplitOptions.RemoveEmptyEntries))
                    {
                        var box = Regex.Match(css,
                            @"\." + Regex.Escape(cls) + @"\s*\{[^}]*width:\s*(?<w>[\d.]+)em",
                            RegexOptions.Singleline);
                        if (box.Success && double.TryParse(box.Groups["w"].Value,
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var wEm))
                        {
                            inlineW = wEm * StlEmPt;
                            break;
                        }
                    }
                }
            }
            srcDivW = inlineW ?? 612.0;
        }
        // Parse every stl_ page's line divs once: the flow consumes them in document
        // order and the sheet width is measured from them.
        var stlPages = new List<List<StlPara>>();
        List<StlPara> ParseStlParas(string seg)
        {
            var paras = new List<StlPara>();
            foreach (Match dm in Regex.Matches(seg,
                @"<div class=""[^""]*"" style=""left:(?<l>-?[\d.]+)em;\s*top:(?<t>-?[\d.]+)em;?"">(?<body>.*?)</div>",
                RegexOptions.Singleline))
            {
                var body = dm.Groups["body"].Value;
                // Linked runs are spans wrapped in <a href="…"> inside the div; the
                // wrapped characters keep the URL so the reflow paints them as links.
                var anchors = new List<(int Start, int End, string Url)>();
                foreach (Match am in Regex.Matches(body,
                    @"<a\s+[^>]*href=""(?<href>[^""]*)""[^>]*>(?<ab>.*?)</a>",
                    RegexOptions.Singleline))
                    anchors.Add((am.Index, am.Index + am.Length,
                        DecodeEntities(am.Groups["href"].Value)));
                var sups = new List<(int Start, int End)>();
                foreach (Match um in Regex.Matches(body, @"<sup[^>]*>.*?</sup>",
                    RegexOptions.Singleline))
                    sups.Add((um.Index, um.Index + um.Length));
                var sbLine = new StringBuilder();
                var urls = new List<string?>();
                var extra = new List<double>();
                var supFlags = new List<bool>();
                foreach (Match sm in Regex.Matches(body,
                    @"<span class=""(?<cls>[^""]*)""(?<attrs>[^>]*)>(?<stext>.*?)</span>",
                    RegexOptions.Singleline))
                {
                    string? url = null;
                    foreach (var a in anchors)
                        if (sm.Index >= a.Start && sm.Index < a.End) { url = a.Url; break; }
                    var isSup = false;
                    foreach (var su in sups)
                        if (sm.Index >= su.Start && sm.Index < su.End) { isSup = true; break; }
                    // Every real space in a span advances the pen by the
                    // span's word-spacing × the reflow em, on top of the glyph —
                    // negative values included.
                    double wsEm = 0;
                    var wsm = Regex.Match(sm.Groups["attrs"].Value, @"word-spacing:\s*(-?[\d.]+)em");
                    if (wsm.Success)
                        wsEm = double.Parse(wsm.Groups[1].Value,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture);
                    var stext = DecodeEntities(Regex.Replace(sm.Groups["stext"].Value, "<[^>]+>", ""));
                    foreach (var ch in stext)
                    {
                        sbLine.Append(ch);
                        urls.Add(url);
                        extra.Add(ch == ' ' ? wsEm * StlEmPt : 0);
                        supFlags.Add(isSup);
                    }
                }
                var text = sbLine.ToString();
                if (text.Trim(' ', ' ').Length == 0) continue;
                // The space gluing a word to its leader run keeps its nominal
                // width - the span's word-spacing does not apply there (a
                // TOC line seats its dots at plain-space distance even while the
                // same span's word-spacing is negative).
                for (var gi = 0; gi + 1 < text.Length; gi++)
                    if (text[gi] == ' ' && text[gi + 1] == '.') extra[gi] = 0;
                paras.Add(new StlPara
                {
                    Text = text,
                    Urls = urls.ToArray(),
                    Extra = extra.ToArray(),
                    Sup = supFlags.ToArray(),
                });
            }
            return paras;
        }

        // stl_ sheet width rides the CONTENT: the longest unbreakable unit — words
        // glued by a nbsp, or a word glued to the leader run after it — measured in
        // raw font units at the reflow size, plus both side margins, when that
        // outgrows the A4 sheet. The pdf-page dialect keeps its source-box-plus-pad
        // rule.
        double pageW;
        if (stlImgBg)
        {
            for (var p = 0; p < pageDivs.Count; p++)
            {
                var segStart = pageDivs[p].Index;
                var segEnd = p + 1 < pageDivs.Count ? pageDivs[p + 1].Index : html.Length;
                stlPages.Add(ParseStlParas(html[segStart..segEnd]));
            }
            double maxUnitW = 0;
            foreach (var pageParas in stlPages)
                foreach (var para in pageParas)
                {
                    double unitW = 0;
                    for (var ci = 0; ci < para.Text.Length; ci++)
                    {
                        if (IsStlBreakSpace(para.Text, ci))
                        {
                            maxUnitW = Math.Max(maxUnitW, unitW);
                            unitW = 0;
                            continue;
                        }
                        var fs = para.Sup[ci] ? StlSupFontSizePt : FontSizePt;
                        unitW += MeasureSerifRawChar(para.Text, ref ci, fs) + para.Extra[ci];
                    }
                    maxUnitW = Math.Max(maxUnitW, unitW);
                }
            pageW = Math.Max(StlPageFloorPt, maxUnitW + MarginSide + StlRightMarginPt);
        }
        else
        {
            pageW = srcDivW + PageWidthPad;
        }
        var contentW = pageW - MarginSide - (stlImgBg ? StlRightMarginPt : MarginSide);
        var doc = Document.Create();
        var docFontDict = new Core.PdfDictionary();
        var fontFileCache = new Dictionary<string, (int objNum, string embedName)>(StringComparer.Ordinal);
        Core.PdfIndirectRef? serifFontRef = null;

        var pendingLinks = new List<(Page page, Aspose.Pdf.Rectangle rect, string url)>();
        Page? page = null;
        Core.PdfIndirectRef? placeholderIconRef = null;
        double baselineY = 0;    // CSS-style: distance from the page TOP to the current baseline

        void NewPage()
        {
            page = doc.Pages.Add(pageW, pageH);
            EnsureFonts(page, docFontDict);
            if (serifFontRef is null)
            {
                var ttf = Text.FontRepository.GetTtfData(FaceName);
                if (ttf is not null)
                {
                    var fd = new Core.PdfDictionary();
                    Text.FontEmbedder.EmbedIntoFontDict(doc, ttf, fd, FaceName.Replace(" ", ""), fontFileCache);
                    var objNum = doc.AllocateObjectNumber();
                    doc.AddNewObject(objNum, fd, registerOverlay: true);
                    serifFontRef = new Core.PdfIndirectRef(objNum, 0);
                }
            }
            if (serifFontRef is not null) RegisterPageFont(page, "FS1", serifFontRef);
            baselineY = stlImgBg && doc.Pages.Count > 1 ? StlContinuationBaselinePt : FirstBaselinePt;
        }

        for (var p = 0; p < pageDivs.Count; p++)
        {
            var segStart = pageDivs[p].Index;
            var segEnd = p + 1 < pageDivs.Count ? pageDivs[p + 1].Index : html.Length;
            var seg = html[segStart..segEnd];

            var spans = new List<PosSpan>();
            foreach (Match m in Regex.Matches(seg,
                @"<span class=""pdf-text"" style=""(?<st>[^""]*)"">(?<body>.*?)</span>",
                RegexOptions.Singleline))
            {
                var st = m.Groups["st"].Value;
                var text = DecodeEntities(Regex.Replace(m.Groups["body"].Value, "<[^>]+>", ""));
                if (text.Length == 0) continue;
                spans.Add(new PosSpan
                {
                    Left = StylePt(st, "left") ?? 0,
                    Top = StylePt(st, "top") ?? 0,
                    FontSize = StylePt(st, "font-size") ?? 12,
                    Text = text,
                });
            }
            // Reflow stl_ shape (img background): line divs were parsed up front
            // into stlPages (document order, with per-character link, word-spacing
            // and sup data). Legacy stl_ shape (object background): parse into
            // baseline-merged PosSpans as before.
            if (stlDialect && !stlImgBg)
            {
                // stl_ text runs: <div class="stl_01" style="left:Xem;top:Yem;">
                //   <span class="stl_NN …">word </span><span …>word </span>…</div>
                // left/top are em (× 12 = pt). Each line div wraps one or more
                // word-anchored spans; the first span's font-size class fixes the
                // run's size.
                double Num(string s) => double.Parse(s,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture);
                foreach (Match dm in Regex.Matches(seg,
                    @"<div class=""[^""]*"" style=""left:(?<l>-?[\d.]+)em;\s*top:(?<t>-?[\d.]+)em;?"">(?<body>.*?)</div>",
                    RegexOptions.Singleline))
                {
                    // A line div wraps ONE OR MORE word-anchored spans (a justified or
                    // positioned line is split into a span per word); concatenate them
                    // all \u2014 the earlier single-span parse dropped every span past the
                    // first, so such a line re-imported as just its first word. The
                    // first span's font-size class fixes the run's size.
                    // Linked runs are spans wrapped in <a href="\u2026"> INSIDE the div
                    // (there is no positioned overlay rectangle in this dialect);
                    // the wrapped characters keep the URL so the reflow paints them
                    // as links.
                    var body = dm.Groups["body"].Value;
                    var anchors = new List<(int Start, int End, string Url)>();
                    foreach (Match am in Regex.Matches(body,
                        @"<a\s+[^>]*href=""(?<href>[^""]*)""[^>]*>(?<ab>.*?)</a>",
                        RegexOptions.Singleline))
                        anchors.Add((am.Index, am.Index + am.Length,
                            DecodeEntities(am.Groups["href"].Value)));
                    var sbLine = new StringBuilder();
                    var lineUrls = new List<string?>();
                    double fsEm = 1.0; var fsSet = false;
                    foreach (Match sm in Regex.Matches(body,
                        @"<span class=""(?<cls>[^""]*)""[^>]*>(?<stext>.*?)</span>",
                        RegexOptions.Singleline))
                    {
                        string? url = null;
                        foreach (var a in anchors)
                            if (sm.Index >= a.Start && sm.Index < a.End) { url = a.Url; break; }
                        var stext = DecodeEntities(Regex.Replace(sm.Groups["stext"].Value, "<[^>]+>", ""));
                        sbLine.Append(stext);
                        for (var k = 0; k < stext.Length; k++) lineUrls.Add(url);
                        if (!fsSet)
                            foreach (var cls in sm.Groups["cls"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                                if (stlFontSizes!.TryGetValue(cls, out var v)) { fsEm = v; fsSet = true; break; }
                    }
                    // Each line div ends with a sentinel &nbsp; (occasionally " &nbsp;").
                    // Keep it as a single trailing space: when two divs share a baseline
                    // and merge into one reflow line the gap becomes a word space
                    // ("\u2026to" + "meet" \u2192 "to meet"); a line-final div's trailing space is
                    // trimmed downstream.
                    var raw = sbLine.ToString();
                    var keep = raw.Length;
                    while (keep > 0 && (raw[keep - 1] == '\u00A0' || raw[keep - 1] == ' ')) keep--;
                    if (keep == 0) continue;
                    var text = raw[..keep] + " ";
                    lineUrls.RemoveRange(keep, lineUrls.Count - keep);
                    lineUrls.Add(null);
                    var hasUrl = false;
                    foreach (var u in lineUrls) if (u is not null) { hasUrl = true; break; }
                    spans.Add(new PosSpan
                    {
                        Left = Num(dm.Groups["l"].Value) * StlEmPt,
                        Top = Num(dm.Groups["t"].Value) * StlEmPt,
                        FontSize = fsEm * StlEmPt,
                        Text = text,
                        Urls = hasUrl ? lineUrls.ToArray() : null,
                    });
                }
            }

            var links = new List<PosLink>();
            foreach (Match m in Regex.Matches(seg,
                @"<a\s+(?=[^>]*class=""pdf-link"")(?=[^>]*href=""(?<href>[^""]*)"")(?=[^>]*style=""(?<st>[^""]*)"")[^>]*>",
                RegexOptions.Singleline))
            {
                var st = m.Groups["st"].Value;
                links.Add(new PosLink
                {
                    Left = StylePt(st, "left") ?? 0,
                    Top = StylePt(st, "top") ?? 0,
                    Width = StylePt(st, "width") ?? 0,
                    Height = StylePt(st, "height") ?? 0,
                    Url = DecodeEntities(m.Groups["href"].Value),
                });
            }
            if (stlDialect)
            {
                // The stl_ dialect's link-annotation rectangles are invisible
                // stl_grlink overlays (a positioned div > a > transparent img), in
                // em units. They feed the same graphical-link placeholder pass as
                // the old dialect's pdf-link overlays - broken-image icons are
                // drawn for hotspots whose raster fell out of the
                // reflow.
                double NumL(string s) => double.Parse(s,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture);
                foreach (Match m in Regex.Matches(seg,
                    @"<div style=""position:absolute;left:(?<l>-?[\d.]+)em;top:(?<t>-?[\d.]+)em;width:(?<w>[\d.]+)em;height:(?<h>[\d.]+)em;"">\s*<a\s+[^>]*href=""(?<href>[^""]*)""[^>]*>\s*<img[^>]*class=""stl_grlink""",
                    RegexOptions.Singleline))
                {
                    links.Add(new PosLink
                    {
                        Left = NumL(m.Groups["l"].Value) * StlEmPt,
                        Top = NumL(m.Groups["t"].Value) * StlEmPt,
                        Width = NumL(m.Groups["w"].Value) * StlEmPt,
                        Height = NumL(m.Groups["h"].Value) * StlEmPt,
                        Url = DecodeEntities(m.Groups["href"].Value),
                    });
                }
            }

            // ── Group spans into source lines by baseline (pdf-page dialect) ──
            var lines = new List<List<PosSpan>>(spans.Count);
            if (!stlImgBg)
            {
                spans.Sort((a, b) => a.Baseline != b.Baseline
                    ? a.Baseline.CompareTo(b.Baseline) : a.Left.CompareTo(b.Left));
                foreach (var s in spans)
                {
                    if (lines.Count > 0 && Math.Abs(lines[^1][0].Baseline - s.Baseline) <= 2.0)
                        lines[^1].Add(s);
                    else
                        lines.Add(new List<PosSpan> { s });
                }
            }

            // The reflow is CONTINUOUS across source page divs — a
            // multi-page document's text flows as one stream, filling each output page
            // before starting the next (page 1 can carry several source pages'
            // text). Only the first div opens a page; later divs keep the cursor.
            if (page is null) NewPage();
            if (stlImgBg)
            {
                // One blank slot precedes every source page's text — empty source
                // pages included; a slot that crosses the bottom margin carries onto
                // the next output page.
                if (baselineY > pageH - BottomMarginPt) NewPage();
                baselineY += PitchPt;
            }
            var iconPage = page!;

            if (stlImgBg)
            {
                // stl_: one paragraph per line div, in document order — same-baseline
                // divs never merge (the number column of a tabbed TOC stacks before
                // its titles, in the order the exporter wrote them).
                foreach (var para in stlPages[p])
                    EmitStlParagraph(doc, ref page!, ref baselineY, para, pendingLinks,
                        FontSizePt, StlSupFontSizePt, StlSupRisePt, StlSupLineExtraPt,
                        PitchPt, MarginSide, contentW, pageH, BottomMarginPt,
                        docFontDict, NewPage);
            }
            else foreach (var line in lines)
            {
                line.Sort((a, b) => a.Left.CompareTo(b.Left));

                // Direct concatenation: PdfToHtml span texts carry their own spacing
                // (runs of whitespace collapse to a single space). Link coverage is
                // resolved per CHARACTER against the overlay rectangles, estimating
                // each character's source x from the span origin plus measured
                // advances, so an overlay covering part of a span (an inline "here"
                // link) doesn't paint the whole span blue.
                var sb = new StringBuilder();
                var urls = new List<string?>();
                foreach (var s in line)
                {
                    var rowLinks = links.FindAll(l =>
                        s.Baseline >= l.Top && s.Baseline <= l.Top + l.Height + 2);
                    var cx = s.Left;
                    for (var ci = 0; ci < s.Text.Length; ci++)
                    {
                        var ch = s.Text[ci];
                        var cpEnd = ci;
                        var adv = MeasureSerifChar(s.Text, ref cpEnd, s.FontSize);
                        var mid = cx + adv / 2;
                        cx += adv;
                        if (char.IsWhiteSpace(ch))
                        {
                            if (sb.Length > 0 && sb[^1] != ' ')   // collapse runs
                            {
                                sb.Append(' ');
                                urls.Add(null);
                            }
                            continue;
                        }
                        var covering = rowLinks.Find(l => mid >= l.Left - 0.5 && mid <= l.Left + l.Width + 0.5);
                        for (var u = ci; u <= cpEnd; u++)
                        {
                            sb.Append(s.Text[u]);
                            urls.Add(s.Urls is not null ? s.Urls[u] : covering?.Url);
                        }
                        ci = cpEnd;
                    }
                }
                // Trim trailing whitespace (trailing space spans widen nothing).
                var text = sb.ToString();
                var end = text.Length;
                while (end > 0 && char.IsWhiteSpace(text[end - 1])) end--;
                if (end == 0) continue;   // whitespace-only source line
                text = text[..end];
                urls.RemoveRange(end, urls.Count - end);

                EmitReflowedLine(doc, ref page!, ref baselineY, text, urls, pendingLinks,
                    FontSizePt, PitchPt, MarginSide, contentW, pageW, pageH,
                    BottomMarginPt, FirstBaselinePt, docFontDict, NewPage);
            }

            // ── Graphical links → placeholder icons at absolute positions ──
            // A stl_ overlay is a stretched raster carrying the click surface, and it
            // does not survive the reflow: every one of them draws the 32×32
            // broken-image placeholder at its position + a fixed offset. The old
            // dialect has no such raster, so there only a hotspot that the reflow lost
            // gets an icon: (1) an annot rect nested inside a larger annot (a per-word
            // hotspot within a row-spanning link) or (2) an annot covering no extracted
            // text (image/flag hotspots), the latter deduplicated by URL in document
            // order.
            var graphical = new List<PosLink>();
            if (stlDialect)
            {
                graphical.AddRange(links);
            }
            else
            {
                foreach (var l in links)
                {
                    var contained = links.Exists(m => !ReferenceEquals(m, l)
                        && m.Left <= l.Left + 0.5 && m.Top <= l.Top + 0.5
                        && m.Left + m.Width >= l.Left + l.Width - 0.5
                        && m.Top + m.Height >= l.Top + l.Height - 0.5
                        && m.Width * m.Height > l.Width * l.Height + 1);
                    if (contained) graphical.Add(l);
                }
                // "Covers text": the old dialect emits per-hotspot spans, so a span
                // STARTING inside the link is the tuned rule.
                var noTextSeen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var l in links)
                {
                    if (graphical.Contains(l)) continue;
                    var coversText = spans.Exists(s =>
                        s.Baseline >= l.Top - 1 && s.Baseline <= l.Top + l.Height + s.FontSize
                        && s.Left >= l.Left - 1.5 && s.Left <= l.Left + l.Width + 1.5
                        && s.Text.Trim().Length > 0);
                    if (!coversText && noTextSeen.Add(l.Url)) graphical.Add(l);
                }
            }
            if (graphical.Count > 0)
            {
                var iconName = RegisterPlaceholderIcon(doc, iconPage, ref placeholderIconRef);
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                var isb = new StringBuilder();
                foreach (var g in graphical)
                {
                    var ix = g.Left + IconOffsetX;
                    var iyTop = g.Top + IconOffsetY;
                    var iy = pageH - iyTop - IconSizePt;
                    isb.Append($"q {IconSizePt.ToString("F0", inv)} 0 0 {IconSizePt.ToString("F0", inv)} ");
                    isb.AppendLine($"{ix.ToString("F2", inv)} {iy.ToString("F2", inv)} cm /{iconName} Do Q");
                    // Each graphical link gets a line-box-shaped annot.
                    pendingLinks.Add((iconPage, new Aspose.Pdf.Rectangle(
                        ix, pageH - iyTop - 13.29, ix + 34, pageH - iyTop), g.Url));
                }
                iconPage.AddContentStream(Encoding.ASCII.GetBytes(isb.ToString()));
            }
        }

        if (doc.Pages.Count == 0) NewPage();

        foreach (var (lp, rect, url) in pendingLinks)
            if (!url.StartsWith("#", StringComparison.Ordinal))
                lp.Annotations.AddLinkAnnotation(rect, url);

        PruneUnusedFonts(doc);
        return doc;
    }

    // 32×32 DeviceRGB broken-image placeholder pixels (zlib-compressed), drawn for
    // graphical links whose target image is unavailable.
    private const string PlaceholderIconDeflateB64 =
        "eNrtlV1vgjAUhvfDBkWg5bMimC1Lxn7nnBfKhf9o243QuheqRGVOVj+yLXvTmELDc05P31NXqyOSqytJSvnryH1inSv6BXYh" +
        "dufijGiLGGtmnbXAYz1MolZN0ySE3GypX5I7fEaderg0DLwhj0YJT/gwSRLf9y3LIo20+YQYvkeFKLeKI9ojQAjOOSZeIy0+" +
        "4TzaOlPR9poQIoqiqqoQpQf/c+ETlOKQkSilZVkiRBiGqlAafMZYnuey2t8g8k/TVDRaLBbYix4fvw+NuqvDjbIsQ32Kovju" +
        "/QW+2jisAsKhRgNZjw9/Bj5zbBjRsAckSxMpq64ZTuGP4ihkFC61TSOgLqLItV13+HDRUX92gtQNy+Pw6TGHVVTzus5gnI0+" +
        "zV/DnwB6zH1/fcMJDixThUA7791yPfgH+su8RfFFWdVNSlkcBSqi4zjd89XLHyVXbkELYI43pBlB4CnzY0U//+b+aR/v78a4" +
        "5RTf8+hyuQT/lPwVZzabFY1enieUOrAK3ruuPZ1O5vO5WtLlE9wP3kbtXPHxGMdxu6rB/7Ke4pr/+P/6WxKX54ufw/8ADP2J" +
        "dQ==";

    // The same 32×32 icon COMPOSITED with its soft mask over white — the drawing a
    // viewer actually shows (the raw base above is mostly hidden by the mask: only
    // 199 of its 1024 pixels are opaque). The escaped-attr dialect draws this one.
    private const string PlaceholderIconMaskedDeflateB64 =
        "eNrtlctugzAQRf+sBYxtwDg8olaVSr+zaRYJi/xRQSDA0BvcoIioG5MqG668GNvijOdhMwyrVv27+oum6VKgUoyS83Bp4PON" +
        "FHEkI7mJosjzPMuylrvwOFWqHU2lx8SECynlQr6U4uqQ6hyUtpQSQnRdBy9t2xrzkYq/akEpBRkugiCAOzM+YyzLsr6bfw5g" +
        "kiRq1Ol0QizGLt5G3a5vLkrTlHO+pAqIYka47pw8z834VVX5HkOS0aiO/ZwmUd93usrXMuYDG4ciYBR2Xdc+deGlV+0tH11k" +
        "2J9h8PGeoVVQPgTiEnubxvc6P4CcueV3gQra1hNWyrLEdZ5dW2M+RBxLtcj5wCkLhY9uh0dCyF3qi/Mj5b8vW6dg11UF03Ud" +
        "3+e6+TFdwsf7M01fX7Z45WAURcE5bZpG36kl+QHncDjko74+d5QStArICGG/3x2PR71lzNc3S2uysQ4+pmEYTrvr/3HVqofo" +
        "Bw7yLG8=";

    /// <summary>Register the placeholder-icon image XObject on <paramref name="page"/>
    /// (building the shared image object once per document) and return its resource name.
    /// <paramref name="masked"/> selects the mask-composited drawing (escaped-attr
    /// dialect) over the raw base the graphical-links path draws.</summary>
    private static string RegisterPlaceholderIcon(Document doc, Page page, ref Core.PdfIndirectRef? iconRef,
        bool masked = false)
    {
        var resName = masked ? "Iph2" : "Iph1";
        if (iconRef is null)
        {
            var data = System.Convert.FromBase64String(
                masked ? PlaceholderIconMaskedDeflateB64 : PlaceholderIconDeflateB64);
            var imgDict = new Core.PdfDictionary();
            imgDict.Set("Type", new Core.PdfName("XObject"));
            imgDict.Set("Subtype", new Core.PdfName("Image"));
            imgDict.Set("Width", new Core.PdfInteger(32));
            imgDict.Set("Height", new Core.PdfInteger(32));
            imgDict.Set("BitsPerComponent", new Core.PdfInteger(8));
            imgDict.Set("ColorSpace", new Core.PdfName("DeviceRGB"));
            imgDict.Set("Filter", new Core.PdfName("FlateDecode"));
            imgDict.Set("Length", new Core.PdfInteger(data.Length));
            var objNum = doc.AllocateObjectNumber();
            doc.AddNewObject(objNum, new Core.PdfStream(imgDict, data), registerOverlay: true);
            iconRef = new Core.PdfIndirectRef(objNum, 0);
        }
        var reader = page.Reader;
        var resources = page.Dict.Get("Resources") as Core.PdfDictionary
            ?? reader.ResolveDict(page.Dict.Get("Resources"));
        if (resources is null)
        {
            resources = new Core.PdfDictionary();
            page.Dict.Set("Resources", resources);
        }
        var xobjDict = resources.Get("XObject") as Core.PdfDictionary
            ?? reader.ResolveDict(resources.Get("XObject"));
        if (xobjDict is null)
        {
            xobjDict = new Core.PdfDictionary();
            resources.Set("XObject", xobjDict);
        }
        if (!xobjDict.ContainsKey(resName)) xobjDict.Set(resName, iconRef);
        return resName;
    }

    /// <summary>Wrap one source line (a paragraph in the reflow) to the content width with
    /// real font metrics and emit each wrapped line: black plain text, blue underlined
    /// runs (with a link annotation) where a pdf-link overlay covered the characters.</summary>
    private static void EmitReflowedLine(Document doc, ref Page page, ref double baselineY,
        string text, List<string?> urls, List<(Page page, Aspose.Pdf.Rectangle rect, string url)> pendingLinks,
        double fontSize, double pitch, double marginLeft, double contentW,
        double pageW, double pageH, double bottomMargin, double firstBaseline,
        Core.PdfDictionary docFontDict, Action newPage)
    {
        // No bidi transformation: the PdfToHtml span texts already carry RTL content
        // as shaped presentation forms in visual order.

        // ── Greedy wrap with measured advances; char-level fallback for long words ──
        var wrapped = new List<(int start, int len)>();
        int lineStart = 0;
        while (lineStart < text.Length)
        {
            int lastFit = -1, lastSpace = -1;
            double w = 0;
            int i = lineStart;
            for (; i < text.Length; i++)
            {
                w += MeasureSerifChar(text, ref i, fontSize);
                if (w > contentW + 0.01) break;
                lastFit = i;
                if (text[i] == ' ') lastSpace = i;
            }
            if (i >= text.Length) { wrapped.Add((lineStart, text.Length - lineStart)); break; }
            int breakAt = lastSpace > lineStart ? lastSpace : (lastFit >= lineStart ? lastFit + 1 : lineStart + 1);
            wrapped.Add((lineStart, breakAt - lineStart));
            lineStart = breakAt;
            while (lineStart < text.Length && text[lineStart] == ' ') lineStart++;
        }

        foreach (var (ws, wl) in wrapped)
        {
            if (baselineY > pageH - bottomMargin) { newPage(); }
            var lineText = text.Substring(ws, wl).TrimEnd();
            if (lineText.Length > 0)
                EmitStyledRuns(doc, page, marginLeft, pageH - baselineY, lineText,
                    ws < urls.Count ? urls.GetRange(ws, Math.Min(lineText.Length, urls.Count - ws)) : new List<string?>(),
                    fontSize, pendingLinks, docFontDict);
            baselineY += pitch;
        }
    }

    /// <summary>Wrap and draw one stl_ paragraph (line div) with the stl_ reflow
    /// rules: greedy breaks at plain spaces except before a leader run, per-space
    /// word-spacing pen advances, sup runs at their smaller size and raise (such a
    /// line takes extra lead), and units longer than the budget kept whole.</summary>
    private static void EmitStlParagraph(Document doc, ref Page page, ref double baselineY,
        StlPara para, List<(Page page, Aspose.Pdf.Rectangle rect, string url)> pendingLinks,
        double fontSize, double supFontSize, double supRise, double supLineExtra,
        double pitch, double marginLeft, double contentW,
        double pageH, double bottomMargin, Core.PdfDictionary docFontDict, Action newPage)
    {
        var text = para.Text;
        var wrapped = new List<(int start, int len)>();
        int lineStart = 0;
        while (lineStart < text.Length)
        {
            int lastSpace = -1;
            double w = 0;
            int i = lineStart;
            while (i < text.Length)
            {
                var cpEnd = i;
                w += MeasureSerifChar(text, ref cpEnd, para.Sup[i] ? supFontSize : fontSize)
                     + para.Extra[i];
                if (w > contentW + 0.01) break;
                if (IsStlBreakSpace(text, i)) lastSpace = i;
                i = cpEnd + 1;
            }
            if (i >= text.Length) { wrapped.Add((lineStart, text.Length - lineStart)); break; }
            int breakAt;
            if (lastSpace > lineStart)
            {
                breakAt = lastSpace;
            }
            else
            {
                // A unit longer than the budget stays whole — the sheet was sized
                // off the longest unit, so at most rounding hangs past the margin.
                breakAt = i;
                while (breakAt < text.Length && !IsStlBreakSpace(text, breakAt)) breakAt++;
            }
            wrapped.Add((lineStart, breakAt - lineStart));
            lineStart = breakAt;
            while (lineStart < text.Length && text[lineStart] == ' ') lineStart++;
        }

        foreach (var (ws, wl) in wrapped)
        {
            // A line carrying a raised run takes extra lead before it seats.
            var hasSup = false;
            for (var k = ws; k < ws + wl; k++)
                if (para.Sup[k]) { hasSup = true; break; }
            if (hasSup) baselineY += supLineExtra;
            if (baselineY > pageH - bottomMargin) { newPage(); }
            var lineText = text.Substring(ws, wl).TrimEnd();
            if (lineText.Length > 0)
                EmitStyledRuns(doc, page, marginLeft, pageH - baselineY, lineText,
                    new List<string?>(new ArraySegment<string?>(para.Urls, ws, lineText.Length)),
                    fontSize, pendingLinks, docFontDict,
                    new ArraySegment<double>(para.Extra, ws, lineText.Length),
                    new ArraySegment<bool>(para.Sup, ws, lineText.Length),
                    supFontSize, supRise);
            baselineY += pitch;
        }
    }

    // Cache of parsed face metrics for the positioned-span reflow.
    private static readonly Dictionary<string, (byte[]? ttf, Text.GlyphOutlineParser? parser, double upm)>
        _posFaceCache = new(StringComparer.OrdinalIgnoreCase);

    private static (byte[]? ttf, Text.GlyphOutlineParser? parser, double upm) PosFace(string name)
    {
        if (_posFaceCache.TryGetValue(name, out var e)) return e;
        byte[]? ttf = null; Text.GlyphOutlineParser? parser = null; double upm = 1000;
        try
        {
            ttf = Text.FontRepository.GetTtfData(name);
            // The pdf2html exporter's family mapping can insert a space into a
            // camel-cased name ("NSim Sun" for NSimSun); retry without spaces.
            if (ttf is null && name.Contains(' '))
                ttf = Text.FontRepository.GetTtfData(name.Replace(" ", ""));
            if (ttf is not null)
            {
                parser = new Text.GlyphOutlineParser(ttf);
                upm = parser.UnitsPerEm > 0 ? parser.UnitsPerEm : 1000;
            }
        }
        catch { ttf = null; parser = null; }
        e = (ttf, parser, upm);
        _posFaceCache[name] = e;
        return e;
    }

    /// <summary>Advance width of the codepoint at <paramref name="i"/> (surrogate-aware;
    /// advances <paramref name="i"/> past a pair) in the serif reflow face, using the same
    /// rounded 1000-unit advances the embedded font declares.</summary>
    private static double MeasureSerifChar(string s, ref int i, double fontSize)
    {
        int cp = s[i];
        if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
        {
            cp = char.ConvertToUtf32(s[i], s[i + 1]);
            i++;
        }
        var face = PosFace(PosFaceNameFor(cp));
        if (face.parser is null) return 0.5 * fontSize;
        var gid = face.parser.CMap.TryGetValue(cp, out var g) ? g : 0;
        if (gid == 0) return 0.5 * fontSize;
        return Math.Round(face.parser.GetAdvanceWidth(gid) * 1000.0 / face.upm) * fontSize / 1000.0;
    }

    /// <summary>Unrounded advance of the codepoint at <paramref name="i"/> (surrogate
    /// aware; advances <paramref name="i"/> past a pair) in the serif reflow face.
    /// The stl_ sheet-width rule measures the longest unit in raw font units,
    /// while wrapping and drawing use the rounded 1000-unit widths of
    /// <see cref="MeasureSerifChar"/> — deliberately so, and the longest
    /// unit may hang a fraction of a point past its own budget.</summary>
    private static double MeasureSerifRawChar(string s, ref int i, double fontSize)
    {
        int cp = s[i];
        if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
        {
            cp = char.ConvertToUtf32(s[i], s[i + 1]);
            i++;
        }
        var face = PosFace(PosFaceNameFor(cp));
        var gid = face.parser is not null && face.parser.CMap.TryGetValue(cp, out var g) ? g : 0;
        return face.parser is null || gid == 0
            ? 0.5 * fontSize
            : face.parser.GetAdvanceWidth(gid) * fontSize / face.upm;
    }

    /// <summary>An stl_ reflow break opportunity: a plain space, except one that
    /// precedes a leader run (a token starting with '.') — a TOC title stays glued
    /// to its dot leader, and that glued pair is what sizes the sheet.</summary>
    private static bool IsStlBreakSpace(string s, int i) =>
        s[i] == ' ' && (i + 1 >= s.Length || s[i + 1] != '.');

    // The serif face first, then the script fallbacks (Sylfaen for
    // Georgian/Armenian, Tahoma for Thai), then the broad-Unicode list.
    private static readonly string[] PosFallbackFonts = { "Times New Roman", "Sylfaen", "Tahoma" };

    private static string PosFaceNameFor(int cp)
    {
        foreach (var name in PosFallbackFonts)
        {
            var f = PosFace(name);
            if (f.parser is not null && f.parser.CMap.TryGetValue(cp, out var g) && g != 0)
                return name;
        }
        foreach (var name in UnicodeFallbackFonts)
        {
            var f = PosFace(name);
            if (f.parser is not null && f.parser.CMap.TryGetValue(cp, out var g) && g != 0)
                return name;
        }
        return "Times New Roman";
    }

    /// <summary>Emit one laid-out output line as consecutive runs split on link
    /// coverage and font face; blue + underline + annotation rect for link runs.</summary>
    private static void EmitStyledRuns(Document doc, Page page, double x, double y,
        string lineText, List<string?> urls,
        double fontSize, List<(Page page, Aspose.Pdf.Rectangle rect, string url)> pendingLinks,
        Core.PdfDictionary docFontDict,
        ArraySegment<double> extraAdv = default, ArraySegment<bool> supFlags = default,
        double supFontSize = 0, double supRise = 0)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        var underlines = new List<(double x0, double w, string url)>();
        sb.AppendLine("BT");

        int i = 0;
        double runX = x;
        while (i < lineText.Length)
        {
            var url = i < urls.Count ? urls[i] : null;
            // A run: same link coverage, same face, and same raised state for every
            // codepoint. A char that carries an extra pen advance (a word-spacing
            // stretched space) ends its run so the next one repositions.
            int j = i;
            var faceName = PosFaceNameFor(char.IsHighSurrogate(lineText[i]) && i + 1 < lineText.Length
                ? char.ConvertToUtf32(lineText[i], lineText[i + 1]) : lineText[i]);
            var isSupRun = supFlags.Count > i && supFlags[i];
            var runFs = isSupRun ? supFontSize : fontSize;
            var runY = y + (isSupRun ? supRise : 0);
            double runW = 0;
            var runSb = new StringBuilder();
            while (j < lineText.Length)
            {
                var u2 = j < urls.Count ? urls[j] : null;
                if (!string.Equals(u2, url, StringComparison.Ordinal)) break;
                if ((supFlags.Count > j && supFlags[j]) != isSupRun) break;
                int cp = lineText[j];
                int cpLen = 1;
                if (char.IsHighSurrogate(lineText[j]) && j + 1 < lineText.Length && char.IsLowSurrogate(lineText[j + 1]))
                {
                    cp = char.ConvertToUtf32(lineText[j], lineText[j + 1]);
                    cpLen = 2;
                }
                var fn = PosFaceNameFor(cp);
                // A space glued between two same-face chars stays in the run.
                if (!fn.Equals(faceName, StringComparison.OrdinalIgnoreCase) && lineText[j] != ' ') break;
                int k = j;
                runW += MeasureSerifChar(lineText, ref k, runFs);
                runSb.Append(lineText, j, cpLen);
                var ext = extraAdv.Count > j ? extraAdv[j] : 0;
                j += cpLen;
                if (ext != 0) { runW += ext; break; }
            }

            var runText = runSb.ToString();
            var isLink = !string.IsNullOrEmpty(url);
            sb.Append(isLink ? "0 0 1 rg " : "0 0 0 rg ");

            var allAnsi = true;
            foreach (var ch in runText)
                if (ch > 0x7F && !Text.Cp1252.TryGetByte(ch, out _)) { allAnsi = false; break; }

            var face = PosFace(faceName);
            if (allAnsi && faceName.Equals("Times New Roman", StringComparison.OrdinalIgnoreCase) && face.ttf is not null)
            {
                sb.Append($"/FS1 {runFs.ToString("F1", inv)} Tf ");
                sb.Append($"1 0 0 1 {runX.ToString("F2", inv)} {runY.ToString("F2", inv)} Tm ");
                sb.Append($"({EscapePdfString(runText)}) Tj ");
            }
            else if (face.ttf is not null
                && page.Dict.Get("Resources") as Core.PdfDictionary is { } res
                && (res.Get("Font") as Core.PdfDictionary ?? docFontDict) is { } fontDict)
            {
                var (rn, hex) = Text.Type0FontEmbedder.Embed(fontDict, face.ttf,
                    faceName, runText, stripSpacesInBaseFont: true);
                sb.Append($"/{rn} {runFs.ToString("F1", inv)} Tf ");
                sb.Append($"1 0 0 1 {runX.ToString("F2", inv)} {runY.ToString("F2", inv)} Tm ");
                sb.Append('<').Append(System.Convert.ToHexString(hex)).Append("> Tj ");
            }
            else
            {
                sb.Append($"/F1 {runFs.ToString("F1", inv)} Tf ");
                sb.Append($"1 0 0 1 {runX.ToString("F2", inv)} {runY.ToString("F2", inv)} Tm ");
                sb.Append($"({EscapePdfString(runText)}) Tj ");
            }

            if (isLink)
            {
                underlines.Add((runX, runW, url!));
                pendingLinks.Add((page, new Aspose.Pdf.Rectangle(runX, runY - 2, runX + runW, runY + runFs), url!));
            }
            runX += runW;
            i = j;
        }
        sb.AppendLine();
        sb.AppendLine("ET");

        // Link underlines: 1.2pt-wide blue strokes 1.2pt below the baseline,
        // drawn as per-segment hairlines.
        foreach (var (x0, w, _) in underlines)
        {
            if (w <= 0) continue;
            var uy = (y - 1.2).ToString("F2", inv);
            sb.Append("q 1.2 w 0 0 1 RG ");
            sb.Append($"{x0.ToString("F2", inv)} {uy} m {(x0 + w).ToString("F2", inv)} {uy} l S Q");
            sb.AppendLine();
        }

        page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
    }

    /// <summary>Author a /StructTreeRoot for the converted document by walking the HTML
    /// element tree, so the tag hierarchy mirrors the markup: a <c>&lt;div&gt;</c> becomes a
    /// Div, headings become H1–H6, paragraphs P, an <c>&lt;img&gt;</c> a Figure, a list
    /// <c>&lt;ul&gt;/&lt;ol&gt;</c> an L whose items expand to LI → {Lbl, [Link], LBody}.
    /// Inline runs (span, b, i, a-in-flow) fold into their block's marked content and do not
    /// produce their own structure element. Enabled by
    /// <see cref="HtmlLoadOptions.CreateLogicalStructure"/>.</summary>
    private static void BuildLogicalStructure(Document doc, string html)
    {
        // Drop head/script/style and HTML comments before parsing so their markup — most
        // importantly commented-out sections such as a page footer — never becomes a tag.
        var cleaned = Regex.Replace(html, @"<!--[\s\S]*?-->", "");
        // Strip raw-text containers. A tempered body — the content may not cross another
        // opening tag of the same element — keeps an *unclosed* <style>/<script> (real-world
        // HTML has them) from greedily pairing with a much later close and swallowing the
        // markup (e.g. form <input>s) in between.
        cleaned = Regex.Replace(cleaned, @"<(script|style|head)\b[^>]*>(?:(?!<\1\b)[\s\S])*?</\1\s*>", "",
            RegexOptions.IgnoreCase);

        var dom = ParseDom(cleaned);
        Tagged.ITaggedContent tc = doc.TaggedContent;
        var root = tc.RootElement;

        HtmlNode? body = null;
        foreach (var d in dom.Descendants())
            if (d.Tag == "body") { body = d; break; }
        var start = body ?? dom;
        foreach (var child in start.Children)
            EmitStructureElement(child, root, tc);
    }
}
