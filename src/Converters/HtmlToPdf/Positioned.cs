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

    // Reflow constants for the re-import of converter HTML:
    // uniform 12pt serif text on a constant 13.5pt baseline pitch, 96pt left
    // margin, first baseline 88.8pt from the page top. The output page keeps the
    // load-options height (A4 default); its width depends on the dialect: the
    // pdf-page shape widens to source-page width + 62.01, the stl_ shape to the
    // longest unbreakable word + both side margins (see the three stl_
    // constants below).
    private const string FaceName = "Times New Roman";
    private const double FontSizePt = 12.0;
    private const double PitchPt = 13.5;
    private const double MarginSide = 96.0;
    private const double FirstBaselinePt = 88.80;   // from the page top
    private const double BottomMarginPt = 72.0;
    private const double PageWidthPad = 62.01;      // pdf-page: output page width − source div width
    private const double StlContinuationBaselinePt = 82.80; // stl_: first baseline of every output page after the first
    private const double StlRightMarginPt = 90.0;   // stl_: sheet width − left margin − longest unit
    private const double StlPageFloorPt = 595.0;    // stl_: A4 width floor when no unit forces widening
    private const double StlSupFontSizePt = 10.0;   // stl_: a <sup> run renders at HTML "smaller" of the 12pt flow
    private const double StlSupRisePt = 4.2;        // stl_: sup baseline raise (its −0.42em inline top × 10pt)
    private const double StlSupLineExtraPt = 2.4;   // stl_: a line carrying a sup run takes 15.9pt of lead, not 13.5
    private const double IconOffsetX = 91.0;        // graphical-link icon offset from the annot rect
    private const double IconOffsetY = 73.0;
    private const double IconSizePt = 32.0;
    private const double StlEmPt = 12.0; // the stl_ scheme's em unit (font-size:10em/scale(0.1) trick)

    internal static bool IsPositionedSpanHtml(string html) =>
        html.Contains("class=\"pdf-page\"", StringComparison.Ordinal)
        && html.Contains("class=\"pdf-text\"", StringComparison.Ordinal)
        && html.Contains("position:absolute", StringComparison.Ordinal);

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

    private static int _ownFaceIds;

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

    private static Document ConvertPositionedSpans(string html, HtmlLoadOptions? options)
    {

        var pos = new PositionedSpansState();
        pos.pageDivs = Regex.Matches(html, @"<div class=""pdf-page""[^>]*style=""(?<st>[^""]*)""[^>]*>");
        pos.stlDialect = pos.pageDivs.Count == 0;
        if (pos.stlDialect)
        {
            pos.pageDivs = Regex.Matches(html, @"<div id=""page_\d+""[^>]*style=""(?<st>[^""]*)""[^>]*>");
            // Newer stl_ exports carry no inline style on the page container —
            // its box lives in a stylesheet class (<div id="page_0" class="stl_02">,
            // .stl_02 { width: 51em; height: 66em; }). Match the bare container
            // and resolve the box from the class below.
            if (pos.pageDivs.Count == 0)
                pos.pageDivs = Regex.Matches(html, @"<div id=""page_\d+""[^>]*>");
        }
        pos.stlFontSizes = pos.stlDialect ? ParseStlFontSizes(html, options) : null;
        pos.stlImgBg = pos.stlDialect && Regex.IsMatch(html, @"<img[^>]*class=""stl_04""");

        pos.pageInfo = options?.PageInfo;
        pos.pageH = pos.pageInfo?.Height is > 0 ? pos.pageInfo.Height : 842.0;
        // Same IsLandscape no-op as ConvertFromHtml: a flag-swapped A4 keeps its long side.
        if (pos.pageInfo?.LandscapeSwapApplied == true && pos.pageInfo.Width > pos.pageH)
            pos.pageH = pos.pageInfo.Width;
        pos.srcDivW = 612.0;
        pos.srcDivH = 0;
        MeasurePositionedSourcePage(pos, html, options);
        SolvePositionedPageWidth(pos, html, options);
        pos.contentW = pos.pageW - MarginSide - (pos.stlImgBg ? StlRightMarginPt : MarginSide);
        pos.doc = Document.Create();
        pos.docFontDict = new Core.PdfDictionary();
        pos.fontFileCache = new Dictionary<string, (int objNum, string embedName)>(StringComparer.Ordinal);
        pos.serifFontRef = null;

        pos.pendingLinks = new List<(Page page, Aspose.Pdf.Rectangle rect, string url)>();
        pos.page = null;
        pos.placeholderIconRef = null;
        pos.baselineY = 0;


        for (var p = 0; p < pos.pageDivs.Count; p++)
            if (!RenderPositionedPage(pos, html, p)) break;

        if (pos.doc.Pages.Count == 0) StartPositionedPage(pos);

        foreach (var (lp, rect, url) in pos.pendingLinks)
            if (!url.StartsWith("#", StringComparison.Ordinal))
                lp.Annotations.AddLinkAnnotation(rect, url);

        PruneUnusedFonts(pos.doc);
        return pos.doc;
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
    /// <returns>The resource name the icon draws as on this page, and the icon object - registered on the first
    /// call, passed back unchanged after.</returns>
    private static (string resName, Core.PdfIndirectRef iconRef) RegisterPlaceholderIcon(Document doc, Page page, Core.PdfIndirectRef? iconRef,
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
        return (resName, iconRef);
    }

    // Cache of parsed face metrics for the positioned-span reflow. Concurrent:
    // fixtures convert in parallel and a plain Dictionary corrupts under
    // simultaneous writers.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (byte[]? ttf, Text.GlyphOutlineParser? parser, double upm)>
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
            // a width variant that is not installed draws as its base family
            if (ttf is null && BaseFamilyOf(name) is { } pfBase)
                ttf = Text.FontRepository.GetTtfData(pfBase);
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
    private static void EmitStyledRuns(Document doc, Page page, double x, double y, string lineText, List<string?> urls, double fontSize, List<(Page page, Aspose.Pdf.Rectangle rect, string url)> pendingLinks, Core.PdfDictionary docFontDict, ArraySegment<double> extraAdv = default, ArraySegment<bool> supFlags = default, double supFontSize = 0, double supRise = 0)
    {
        var er = new StyledRunsState();
        er.doc = doc;
        er.page = page;
        er.x = x;
        er.y = y;
        er.lineText = lineText;
        er.urls = urls;
        er.fontSize = fontSize;
        er.pendingLinks = pendingLinks;
        er.docFontDict = docFontDict;
        er.extraAdv = extraAdv;
        er.supFlags = supFlags;
        er.supFontSize = supFontSize;
        er.supRise = supRise;
        er.inv = System.Globalization.CultureInfo.InvariantCulture;
        er.sb = new StringBuilder();
        er.underlines = new List<(double x0, double w, string url)>();
        er.sb.AppendLine("BT");

        er.i = 0;
        er.runX = er.x;
        while (er.i < er.lineText.Length)
        {
            EmitStyledRun(er);
        }
        er.sb.AppendLine();
        er.sb.AppendLine("ET");

        // Link underlines: 1.2pt-wide blue strokes 1.2pt below the baseline,
        // drawn as per-segment hairlines.
        foreach (var (x0, w, _) in er.underlines)
        {
            if (w <= 0) continue;
            var uy = (er.y - 1.2).ToString("F2", er.inv);
            er.sb.Append("q 1.2 w 0 0 1 RG ");
            er.sb.Append($"{x0.ToString("F2", er.inv)} {uy} m {(x0 + w).ToString("F2", er.inv)} {uy} l S Q");
            er.sb.AppendLine();
        }

        er.page.AddContentStream(Encoding.ASCII.GetBytes(er.sb.ToString()));
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
