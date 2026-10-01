using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
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
                var rel = m.Groups["h"].Value.Replace('/', System.IO.Path.DirectorySeparatorChar);
                var p = System.IO.Path.Combine(basePath, rel);
                // Callers commonly pass the page FILE as the base path — a browser
                // resolves against the page's containing directory (the same rule
                // LoadConverterImage applies to image sources).
                if (!File.Exists(p)
                    && System.IO.Path.GetDirectoryName(basePath) is { Length: > 0 } parentDir)
                    p = System.IO.Path.Combine(parentDir, rel);
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
        var sp = new StlPositionedState();
        sp.stlCss = GatherStlCss(html, options);
        sp.classProps = ParseStlClassProps(sp.stlCss);
        sp.emCompensationGrid = false;
        {
            var sawNonZeroLs = false;
            var allOnGrid = true;
            foreach (var cp in sp.classProps.Values)
            {
                if (cp.LetterSpacingEm is not { } le || le == 0) continue;
                sawNonZeroLs = true;
                var cents = le * 100.0;
                if (Math.Abs(cents - Math.Round(cents)) > 1e-6) { allOnGrid = false; break; }
            }
            sp.emCompensationGrid = sawNonZeroLs && allOnGrid;
        }
        sp.htmlFaces = ParseStlFontFaces(sp.stlCss, options);
        sp.stlContentPad = 6.0;

        sp.pageInfo = options?.PageInfo;
        sp.pageW0 = sp.pageInfo?.Width is > 0 ? sp.pageInfo.Width : 595.0;
        sp.pageH = sp.pageInfo?.Height is > 0 ? sp.pageInfo.Height : 842.0;
        sp.pageMargin = sp.pageInfo?.Margin;
        sp.marginsExplicit = sp.pageMargin?.IsTouched ?? false;
        sp.ml = sp.marginsExplicit ? sp.pageMargin!.Left : 90.0;
        sp.mr = sp.marginsExplicit ? sp.pageMargin!.Right : 90.0;
        sp.mt = sp.marginsExplicit ? sp.pageMargin!.Top : 72.0;
        sp.mb = sp.marginsExplicit ? sp.pageMargin!.Bottom : 72.0;
        sp.band = Math.Max(1.0, sp.pageH - sp.mt - sp.mb);

        sp.pageDivs = Regex.Matches(html, @"<div id=""page_\d+""[^>]*>");

        sp.pagesRuns = new List<List<StlRun>>();
        sp.pagesImage = new List<(byte[] bytes, double wPt, double hPt)?>();
        sp.maxRight = 0;

        for (var p = 0; p < sp.pageDivs.Count; p++)
        {
            if (!ParseStlPage(sp, html, options, p)) break;
        }

        sp.pageW = Math.Max(sp.pageW0, sp.ml + sp.maxRight + sp.mr);

        sp.doc = Document.Create();
        sp.docFontDict = new Core.PdfDictionary();
        sp.inv = System.Globalization.CultureInfo.InvariantCulture;

        for (var p = 0; p < sp.pagesRuns.Count; p++)
        {
            if (!RenderStlPage(sp, p)) break;
        }

        if (sp.doc.Pages.Count == 0)
        {
            var pg = sp.doc.Pages.Add(sp.pageW, sp.pageH);
            EnsureFonts(pg, sp.docFontDict);
        }

        PruneUnusedFonts(sp.doc);
        return sp.doc;
    }

    /// <summary>Wrap one source line (a paragraph in the reflow) to the content width with
    /// real font metrics and emit each wrapped line: black plain text, blue underlined
    /// runs (with a link annotation) where a pdf-link overlay covered the characters.</summary>
    private static void EmitReflowedLine(Document doc,
        string text, List<string?> urls, List<(Page page, Aspose.Pdf.Rectangle rect, string url)> pendingLinks,
        double fontSize, double pitch, double marginLeft, double contentW,
        double pageW, double pageH, double bottomMargin, double firstBaseline,
        Core.PdfDictionary docFontDict, PositionedSpansState pos)
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
                (var adv, i) = MeasureSerifChar(text, i, fontSize);
                w += adv;
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
            if (pos.baselineY > pageH - bottomMargin) { StartPositionedPage(pos); }
            var lineText = text.Substring(ws, wl).TrimEnd();
            if (lineText.Length > 0)
                EmitStyledRuns(doc, pos.page!, marginLeft, pageH - pos.baselineY, lineText,
                    ws < urls.Count ? urls.GetRange(ws, Math.Min(lineText.Length, urls.Count - ws)) : new List<string?>(),
                    fontSize, pendingLinks, docFontDict);
            pos.baselineY += pitch;
        }
    }

    /// <summary>Wrap and draw one stl_ paragraph (line div) with the stl_ reflow
    /// rules: greedy breaks at plain spaces except before a leader run, per-space
    /// word-spacing pen advances, sup runs at their smaller size and raise (such a
    /// line takes extra lead), and units longer than the budget kept whole.</summary>
    private static void EmitStlParagraph(Document doc,
        StlPara para, List<(Page page, Aspose.Pdf.Rectangle rect, string url)> pendingLinks,
        double fontSize, double supFontSize, double supRise, double supLineExtra,
        double pitch, double marginLeft, double contentW,
        double pageH, double bottomMargin, Core.PdfDictionary docFontDict, PositionedSpansState pos)
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
                (var adv, cpEnd) = MeasureSerifChar(text, cpEnd, para.Sup[i] ? supFontSize : fontSize);
                w += adv + para.Extra[i];
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
            if (hasSup) pos.baselineY += supLineExtra;
            if (pos.baselineY > pageH - bottomMargin) { StartPositionedPage(pos); }
            var lineText = text.Substring(ws, wl).TrimEnd();
            if (lineText.Length > 0)
                EmitStyledRuns(doc, pos.page!, marginLeft, pageH - pos.baselineY, lineText,
                    new List<string?>(new ArraySegment<string?>(para.Urls, ws, lineText.Length)),
                    fontSize, pendingLinks, docFontDict,
                    new ArraySegment<double>(para.Extra, ws, lineText.Length),
                    new ArraySegment<bool>(para.Sup, ws, lineText.Length),
                    supFontSize, supRise);
            pos.baselineY += pitch;
        }
    }

    /// <summary>Advance width of the codepoint at <paramref name="i"/> (surrogate-aware;
    /// advances <paramref name="i"/> past a pair) in the serif reflow face, using the same
    /// rounded 1000-unit advances the embedded font declares.</summary>
    private static (double result, int last) MeasureSerifChar(string s, int i, double fontSize)
    {
        int last = default;
        int cp = s[i];
        last = i;
        if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
        {
            cp = char.ConvertToUtf32(s[i], s[i + 1]);
            last = i + 1;
        }
        var face = PosFace(PosFaceNameFor(cp));
        if (face.parser is null) return (0.5 * fontSize, last);
        var gid = face.parser.CMap.TryGetValue(cp, out var g) ? g : 0;
        if (gid == 0) return (0.5 * fontSize, last);
        return (Math.Round(face.parser.GetAdvanceWidth(gid) * 1000.0 / face.upm) * fontSize / 1000.0, last);
    }

    /// <summary>Unrounded advance of the codepoint at <paramref name="i"/> (surrogate
    /// aware; <c>last</c> is the index of the pair's second half) in the serif reflow face.
    /// The stl_ sheet-width rule measures the longest unit in raw font units,
    /// while wrapping and drawing use the rounded 1000-unit widths of
    /// <see cref="MeasureSerifChar"/> — deliberately so, and the longest
    /// unit may hang a fraction of a point past its own budget.</summary>
    private static (double width, int last) MeasureSerifRawChar(string s, int i, double fontSize)
    {
        int cp = s[i];
        var last = i;
        if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
        {
            cp = char.ConvertToUtf32(s[i], s[i + 1]);
            last = i + 1;
        }
        var face = PosFace(PosFaceNameFor(cp));
        var gid = face.parser is not null && face.parser.CMap.TryGetValue(cp, out var g) ? g : 0;
        var width = face.parser is null || gid == 0
            ? 0.5 * fontSize
            : face.parser.GetAdvanceWidth(gid) * fontSize / face.upm;
        return (width, last);
    }

    /// <summary>An stl_ reflow break opportunity: a plain space, except one that
    /// precedes a leader run (a token starting with '.') — a TOC title stays glued
    /// to its dot leader, and that glued pair is what sizes the sheet.</summary>
    private static bool IsStlBreakSpace(string s, int i) =>
        s[i] == ' ' && (i + 1 >= s.Length || s[i + 1] != '.');
}
