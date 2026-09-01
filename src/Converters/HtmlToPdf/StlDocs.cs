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
}
