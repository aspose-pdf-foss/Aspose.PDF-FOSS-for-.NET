using System.Globalization;
using System.Text;
using System.Xml;

namespace Aspose.Pdf.Forms.Xfa;

/// <summary>
/// Renders a dynamic-XFA form's content onto real PDF pages when it is flattened to a static
/// document (so a subsequent raster render shows the form, not the XFA "requires Adobe Reader"
/// fallback page). It runs a coarse XFA flow layout (positions each draw and field box) and paints
/// text / fill boxes / lines with the standard Helvetica family (Arial-compatible). It targets a
/// close visual match (within a few pixels), not pixel-perfection. Fully tolerant: any failure
/// leaves the flatten untouched.
/// </summary>
internal static partial class XfaRenderer
{
    /// <summary>Points per millimetre as the reference's XFA layout converts them: the
    /// rounded 2.835, not 72/25.4 (probed: an A4 medium of 210mm x 297mm renders a
    /// 595.35 x 841.995 pt page).</summary>
    private const double Mm = 2.835;

    // A positioned, ready-to-paint primitive (coordinates already in PDF points, bottom-left origin).
    private sealed class Item
    {
        public string Kind = "";                  // text / fill / line / box / circle / dot / image
        public double X, Y, W, H;                 // rect/line geometry (PDF pts)
        public string Text = "";
        public double FontSize = 8;
        public bool Bold;
        public double[] Color = { 0, 0, 0 };       // rgb 0..1
        public byte[]? ImageData;                 // embedded image bytes (PNG/JPEG) for Kind=image
        public bool Italic;                       // rich-text run style
        public bool Serif;                        // Times family instead of Helvetica
        public double CharSpacing;                // Tc — per-glyph letter spacing (pt)
        public bool Rich;                         // rich-text run — render with the real (embedded) system font
        public string? Family;                    // resolvable non-default template face (e.g. Verdana) — embed it
        public double HScale = 1.0;               // Tz/100 — advance stretch for wide faces (Arial Black)
        public bool Stretch;                      // image fills its box (XFA aspect="none"); default = aspect-fit
    }

    /// <summary>Paint the flattened dynamic-XFA form onto fresh pages of <paramref name="doc"/>,
    /// replacing the existing (fallback) pages. No-op on any failure.</summary>
    internal static void Render(Document doc, XmlElement template, Func<string, string?>? rawValue)
    {
        try
        {
            _docFaces = CollectDocFaces(doc);
            RenderInternal(doc, template, rawValue);
        }
        catch { /* never break flatten */ }
        finally { _docFaces = null; }
    }

    /// <summary>Faces embedded in the document's AcroForm /DR /Font resources, keyed by
    /// BaseFont name (subset prefix stripped). A template typeface with no system face
    /// (e.g. the USPS IMB barcode font) resolves through these so the barcode field
    /// paints its real glyphs instead of Helvetica letters.</summary>
    [ThreadStatic]
    private static Dictionary<string, (byte[] ttf, Text.GlyphOutlineParser parser)>? _docFaces;

    private static Dictionary<string, (byte[] ttf, Text.GlyphOutlineParser parser)>? CollectDocFaces(Document doc)
    {
        try
        {
            var reader = doc.Reader;
            var acroForm = reader.ResolveDict(reader.Catalog.Get("AcroForm"));
            var dr = acroForm is null ? null : reader.ResolveDict(acroForm.Get("DR"));
            var fonts = dr is null ? null : reader.ResolveDict(dr.Get("Font"));
            if (fonts is null) return null;
            Dictionary<string, (byte[], Text.GlyphOutlineParser)>? map = null;
            foreach (var key in fonts.Keys)
            {
                var f = reader.ResolveDict(fonts.Get(key));
                if (f is null) continue;
                var desc = reader.ResolveDict(f.Get("FontDescriptor"));
                if (desc is null && reader.Resolve(f.Get("DescendantFonts")) is Core.PdfArray da && da.Count > 0)
                    desc = reader.ResolveDict(reader.ResolveDict(da[0])?.Get("FontDescriptor"));
                var program = desc is null ? null : reader.ResolveStream(desc.Get("FontFile2"));
                if (program is null) continue;
                byte[] ttf;
                try { ttf = reader.DecodeStream(program); } catch { continue; }
                Text.GlyphOutlineParser parser;
                try { parser = new Text.GlyphOutlineParser(ttf); } catch { continue; }
                var baseName = f.GetName("BaseFont") ?? key;
                if (baseName.Length > 7 && baseName[6] == '+') baseName = baseName[7..];
                map ??= new Dictionary<string, (byte[], Text.GlyphOutlineParser)>(StringComparer.OrdinalIgnoreCase);
                map[baseName] = (ttf, parser);
                if (!map.ContainsKey(key)) map[key] = (ttf, parser);
            }
            return map;
        }
        catch { return null; }
    }

    private static void RenderInternal(Document doc, XmlElement template, Func<string, string?>? rawValue)
    {
        var xr = new XfaRenderState();
        xr.doc = doc;
        xr.template = template;
        xr.rawValue = rawValue;
        xr.root = FirstChild(xr.template, "subform");
        if (xr.root is null) return;

        xr.dataRoot = LoadDataRoot(xr.doc);
        xr.groups = new List<XmlElement>();
        xr.formRoot = LoadFormRoot(xr.doc);
        xr.origRoot = xr.root;
        xr.root = ExpandOccurrences(xr.root, xr.dataRoot, xr.groups, xr.formRoot);
        // The form packet is the RUNTIME state a viewer saved: its elements carry
        // the presence, values and captions the form's scripts resolved (e.g. only
        // the peril section the claim concerns stays visible, labels in the chosen
        // language). Overlay those onto the template clone so the flow renders the
        // runtime state — the template alone shows every conditional variant at
        // once. The ORIGINAL root gets the same overlay: the master pages
        // (pageSet/pageArea) are read from the original template, not the clone.
        if (xr.formRoot is not null)
        {
            OverlayFormPresence(xr.root, xr.formRoot);
            OverlayFormPresence(xr.origRoot, xr.formRoot);
        }

        // Body content flows through the master page's content areas. Every visible
        // top-level box under the form root is a body — subforms AND bare fields/draws
        // (Designer emits e.g. captioned text fields directly under the root, and they
        // consume flow height like any subform row); Designer metadata subforms
        // ("designer__stylesheet" etc.) are not content.
        var bodies = Boxes(xr.root)
            .Where(s => s.LocalName is "subform" or "field" or "draw" or "exclGroup"
                        && !s.GetAttribute("name").StartsWith("designer__", StringComparison.Ordinal))
            .ToList();
        xr.pageAreas = Descendants(xr.template, "pageArea").ToList();
        if (bodies.Count == 0 || xr.pageAreas.Count == 0) return;

        // The current master pageArea: a body's <breakBefore target="…"> switches to the
        // named pageArea; startNew keeps the master and starts a fresh page.
        xr.master = BreakTarget(bodies[0], xr.pageAreas)
                     ?? xr.pageAreas.FirstOrDefault(p => p.GetAttribute("name") == bodies[0].GetAttribute("name"))
                     ?? xr.pageAreas.FirstOrDefault()!;
        if (xr.master is null) return;

        xr.masterIdx = Math.Max(0, xr.pageAreas.IndexOf(xr.master));
        xr.pagesOnMaster = 0;
        xr.xfaImages = LoadXfaImages(xr.doc);
        xr.idElements = new Dictionary<string, XmlElement>(StringComparer.Ordinal);
        foreach (var el in xr.template.SelectNodes(".//*")!.OfType<XmlElement>())
        {
            var id = el.GetAttribute("id");
            if (id.Length > 0 && !xr.idElements.ContainsKey(id)) xr.idElements[id] = el;
        }
        xr.newPages = new List<(double w, double h, List<Item> items)>();
        xr.ctx = null!;
        xr.pw = 612;
        xr.ph = 792;
        xr.areas = new List<(double x, double y, double w, double h, string name)>();
        xr.ai = 0; xr.used = 0;
        xr.pageFresh = false;
        NewPage(xr);
        xr.rootPath = xr.root.GetAttribute("name") + "[0]";
        foreach (var body in bodies)
        {
            if (!RenderBody(xr, body, bodies)) break;
        }
        if (xr.newPages.Count == 0) return;

        xr.total = xr.newPages.Count.ToString(CultureInfo.InvariantCulture);
        foreach (var (_, _, items) in xr.newPages)
            foreach (var it in items)
                if (it.Kind == "text" && it.Text.Contains(PageCountSentinel, StringComparison.Ordinal))
                    it.Text = it.Text.Replace(PageCountSentinel, xr.total, StringComparison.Ordinal);

        // Replace all existing pages with the rendered ones (bounded to avoid any delete-loop hang).
        for (int guard = xr.doc.Pages.Count + 8; xr.doc.Pages.Count > 0 && guard > 0; guard--)
            xr.doc.Pages.Delete(1);
        foreach (var (w, h, items) in xr.newPages)
        {
            var page = xr.doc.Pages.Add(w, h);
            EnsureFonts(page);
            page.AddContentStream(Emit(items, page));
        }
    }

    private static (double pw, double ph) MasterMedium(XmlElement master)
    {
        var medium = FirstChild(master, "medium");
        return medium is null
            ? (612, 792)
            : (Len(medium.GetAttribute("short"), 612), Len(medium.GetAttribute("long"), 792));
    }

    /// <summary>The master's content areas. Designer files often carry a whole-page
    /// default area alongside the real flow regions; an area that geometrically CONTAINS
    /// another is such a container artifact and is dropped.</summary>
    private static List<(double x, double y, double w, double h, string name)> MasterContentAreas(XmlElement master, double pw, double ph)
    {
        var areas = Children(master, "contentArea")
            .Select(a => (x: Len(a.GetAttribute("x"), 0), y: Len(a.GetAttribute("y"), 0),
                          w: LenN(a.GetAttribute("w")) ?? pw, h: LenN(a.GetAttribute("h")) ?? ph,
                          name: a.GetAttribute("name")))
            .ToList();
        if (areas.Count > 1)
            areas = areas.Where(a => !areas.Any(b => (b.x != a.x || b.y != a.y || b.w != a.w || b.h != a.h)
                                                     && b.x >= a.x - 0.1 && b.y >= a.y - 0.1
                                                     && b.x + b.w <= a.x + a.w + 0.1
                                                     && b.y + b.h <= a.y + a.h + 0.1)).ToList();
        if (areas.Count == 0) areas.Add((0, 0, pw, ph, ""));
        return areas;
    }

    // ------------------------------------------------------------------ data / occurrences

    private sealed class Ctx
    {
        public double PageH;
        public Func<string, string?>? RawValue;
        // No form-packet instance record: bind only via unambiguous data paths
        // (a repeated data group's fields stay empty — probed).
        public bool StrictBinding;
        public List<Item> Items = new();
        public List<XmlElement> Groups = new();
        public Dictionary<string, byte[]> Images = new();
        public Dictionary<string, XmlElement> IdElements = new();
        public XmlElement? DataRoot;
        public int PageNum = 1;
    }

    // ------------------------------------------------------------------ layout

    // ------------------------------------------------------------------ heights / widths

    // ------------------------------------------------------------------ painting

    /// <summary>A caption's own &lt;font&gt; (size/weight) wins over the field's font;
    /// captions commonly restyle (e.g. a 9pt bold label on a size-less checkbox field).</summary>
    private static (double fs, bool bold) CaptionFont(XmlElement e)
    {
        var cf = FirstChild(e, "caption") is { } c ? FirstChild(c, "font") : null;
        var fs = cf is not null ? LenN(cf.GetAttribute("size")) : null;
        var bold = cf is not null && cf.GetAttribute("weight").Length > 0
            ? cf.GetAttribute("weight") == "bold"
            : FontBold(e);
        return (fs ?? FontSize(e), bold);
    }

    // ------------------------------------------------------------------ rich text (exData XHTML)

    /// <summary>Measure pass shared by the paint and the flow HEIGHT: wrap every exData
    /// paragraph at the given width and total the content height. Line pitch is the
    /// explicit &lt;para lineHeight&gt; when given, else the formula below. OPEN
    /// CONFLICT: lineHeight-less rich text measures 1.0 × fontSize in
    /// two measured fixtures (4506-T's 5..9pt columns step 5.00..9.00; the merged
    /// tax-form pair's 10pt paragraphs step 10.00), yet an earlier fixture measured
    /// 8.4 for 7pt — and adopting 1.0 here shifts measured heights enough to flip a
    /// page break in the merged-pair document. The 1.2 formula stays until the pitch
    /// and its pagination knock-on are probed together.</summary>
    private static (List<(RtPara para, List<List<RtWord>> lines, double lineH)>, double)
        MeasureRichParas(Ctx ctx, XmlElement e, double avail, XmlElement exData,
            double baseFs, bool baseBold, bool baseSerif, string defaultAlign)
    {
        var measured = new List<(RtPara para, List<List<RtWord>> lines, double lineH)>();
        double contentH = 0;
        foreach (var p in exData.SelectNodes(".//*[local-name()='p']")!.OfType<XmlElement>())
        {
            var para = new RtPara();
            var pStyle = ParseStyle(p.GetAttribute("style"));
            para.Align = pStyle.align ?? defaultAlign;
            para.SpaceAfter = pStyle.marginBottom ?? 0;
            var pRun = new RtRun
            {
                Size = pStyle.size ?? baseFs,
                Bold = pStyle.bold ?? baseBold,
                Italic = pStyle.italic ?? false,
                Serif = pStyle.serif ?? baseSerif,
                Family = ResolvedFamily(e, pStyle.bold ?? baseBold),
            };
            pRun.LetterSpacing = pStyle.lsPt ?? (pStyle.lsEm is { } em ? em * pRun.Size : 0);
            CollectRuns(ctx, p, pRun, para.Runs);

            var maxRun = Math.Max(para.Runs.DefaultIfEmpty(pRun).Max(r => r.Size), 1);
            double lineH = FirstChild(e, "para") is { } pel && LenN(pel.GetAttribute("lineHeight")) is { } plh2 && plh2 > 0
                ? plh2
                : maxRun < 10
                    ? maxRun * 1.2
                    : Math.Ceiling(maxRun * 1.22 * 2) / 2.0;
            var lines = WrapRuns(para.Runs, avail);
            measured.Add((para, lines, lineH));
            contentH += Math.Max(1, lines.Count) * lineH + (lines.Count == 0 ? 0 : para.SpaceAfter);
        }
        return (measured, contentH);
    }

    private static (double? size, bool? bold, bool? italic, bool? serif, string? align, double? marginBottom, double? lsEm, double? lsPt)
        ParseStyle(string style)
    {
        double? size = null, marginBottom = null, lsEm = null, lsPt = null;
        bool? bold = null, italic = null, serif = null;
        string? align = null;
        foreach (var part in style.Split(';'))
        {
            var kv = part.Split(':', 2);
            if (kv.Length != 2) continue;
            var k = kv[0].Trim().ToLowerInvariant();
            var v = kv[1].Trim();
            switch (k)
            {
                case "letter-spacing":
                    if (v.EndsWith("em", StringComparison.Ordinal)
                        && double.TryParse(v[..^2], NumberStyles.Any, CultureInfo.InvariantCulture, out var lse)) lsEm = lse;
                    else if (v.EndsWith("pt", StringComparison.Ordinal)
                        && double.TryParse(v[..^2], NumberStyles.Any, CultureInfo.InvariantCulture, out var lsp)) lsPt = lsp;
                    else if (v.EndsWith("in", StringComparison.Ordinal)
                        && double.TryParse(v[..^2], NumberStyles.Any, CultureInfo.InvariantCulture, out var lsi)) lsPt = lsi * 72;
                    break;
                case "font-size":
                    if (v.EndsWith("pt", StringComparison.Ordinal)
                        && double.TryParse(v[..^2], NumberStyles.Any, CultureInfo.InvariantCulture, out var fsz)) size = fsz;
                    break;
                case "font-weight": bold = v.StartsWith("bold", StringComparison.OrdinalIgnoreCase) || v == "700"; break;
                case "font-style": italic = v.Contains("italic", StringComparison.OrdinalIgnoreCase); break;
                case "font-family":
                    serif = (v.Contains("Times", StringComparison.OrdinalIgnoreCase)
                             || v.Contains("Georgia", StringComparison.OrdinalIgnoreCase)
                             || v.Contains("Garamond", StringComparison.OrdinalIgnoreCase)
                             || v.Contains("Book Antiqua", StringComparison.OrdinalIgnoreCase)
                             || (v.Contains("serif", StringComparison.OrdinalIgnoreCase)
                                 && !v.Contains("sans-serif", StringComparison.OrdinalIgnoreCase)));
                    break;
                case "text-align": align = v.ToLowerInvariant(); break;
                case "margin-bottom":
                    if (v.EndsWith("pt", StringComparison.Ordinal)
                        && double.TryParse(v[..^2], NumberStyles.Any, CultureInfo.InvariantCulture, out var mb)) marginBottom = mb;
                    break;
            }
        }
        return (size, bold, italic, serif, align, marginBottom, lsEm, lsPt);
    }

    // Real system-font metrics for rich text: wrapping uses the actual
    // TrueType advances (Times New Roman / Arial), which differ slightly from the AFM
    // tables — enough to move justified line breaks. Resolved once per style, null when
    // the system font is unavailable (AFM fallback keeps CI runners working).
    private static readonly Dictionary<(bool serif, bool bold, bool italic), (byte[] ttf, Text.GlyphOutlineParser parser)?> _rtFonts = new();

    private static (byte[] ttf, Text.GlyphOutlineParser parser)? RtFont(bool serif, bool bold, bool italic)
    {
        var key = (serif, bold, italic);
        lock (_rtFonts)
        {
            if (_rtFonts.TryGetValue(key, out var hit)) return hit;
            (byte[], Text.GlyphOutlineParser)? val = null;
            try
            {
                var name = (serif ? "Times New Roman" : "Arial")
                           + (bold && italic ? ",BoldItalic" : bold ? ",Bold" : italic ? ",Italic" : "");
                var ttf = Text.SystemFontResolver.Resolve(name);
                if (ttf is not null)
                    val = (ttf, new Text.GlyphOutlineParser(ttf));
            }
            catch { }
            _rtFonts[key] = val;
            return val;
        }
    }

    // ------------------------------------------------------------------ emit

    // ------------------------------------------------------------------ template helpers

    private static (double, double, double, double) Margins(XmlElement e)
    {
        var m = FirstChild(e, "margin");
        if (m is null) return (0, 0, 0, 0);
        return (Len(m.GetAttribute("topInset"), 0), Len(m.GetAttribute("bottomInset"), 0),
                Len(m.GetAttribute("leftInset"), 0), Len(m.GetAttribute("rightInset"), 0));
    }

    // Real-face support for template typefaces the Helvetica/Times model
    // mis-measures (Verdana, Tahoma, …): measure AND paint with the resolved
    // system face so glyph advances match a viewer's output exactly.
    private static readonly Dictionary<(string fam, bool bold, bool italic), (byte[] ttf, Text.GlyphOutlineParser parser)?>
        _famFonts = new();

    private static (byte[] ttf, Text.GlyphOutlineParser parser)? FamilyFont(string family, bool bold, bool italic)
    {
        // Document-embedded /DR faces win (per-render table, so the static cache
        // below can't leak one document's face into another's render).
        if (_docFaces is { } df && df.TryGetValue(family, out var docFace)) return docFace;
        var key = (family, bold, italic);
        lock (_famFonts)
        {
            if (_famFonts.TryGetValue(key, out var hit)) return hit;
            (byte[], Text.GlyphOutlineParser)? val = null;
            try
            {
                var name = family + (bold && italic ? ",BoldItalic" : bold ? ",Bold" : italic ? ",Italic" : "");
                var ttf = Text.SystemFontResolver.Resolve(name);
                if (ttf is not null) val = (ttf, new Text.GlyphOutlineParser(ttf));
            }
            catch { }
            _famFonts[key] = val;
            return val;
        }
    }

    private static (double reserve, string placement) Caption(XmlElement e)
    {
        var c = FirstChild(e, "caption");
        if (c is null) return (0, "left");
        return (Len(c.GetAttribute("reserve"), 0), c.GetAttribute("placement") is { Length: > 0 } p ? p : "left");
    }

    private static string InnerText(XmlElement e, string childTag)
    {
        var c = FirstChild(e, childTag);
        if (c is null) return "";
        // Plain <text> content, or rich text (<exData> XHTML) whose <p> paragraphs are line-separated.
        var ps = c.SelectNodes(".//*[local-name()='p']")!.OfType<XmlElement>().ToList();
        if (ps.Count > 0)
            return string.Join("\n", ps.Select(p => p.InnerText.Replace(' ', ' ').Trim()));
        var texts = string.Concat(c.SelectNodes(".//*[local-name()='text']")!.OfType<XmlNode>().Select(n => n.InnerText));
        if (texts.Length > 0) return texts;
        // Plain text content, or typed scalar children (<integer>/<float>/<decimal>/<date>…)
        // — but never binary or vector payloads (<image> base64 must not paint as text;
        // shapes carry no text).
        var firstEl = c.ChildNodes.OfType<XmlElement>().FirstOrDefault();
        if (firstEl is not null && firstEl.LocalName is "image" or "rectangle" or "line" or "arc" or "exData") return "";
        return c.InnerText.Trim();
    }

    private static XmlElement? FirstChild(XmlElement e, string local) =>
        e.ChildNodes.OfType<XmlElement>().FirstOrDefault(c => c.LocalName == local);
    private static IEnumerable<XmlElement> Children(XmlElement e, string local) =>
        e.ChildNodes.OfType<XmlElement>().Where(c => c.LocalName == local);
    private static IEnumerable<XmlElement> Descendants(XmlElement e, string local) =>
        e.SelectNodes($".//*[local-name()='{local}']")!.OfType<XmlElement>();

    private static double Len(string v, double def) => LenN(v) ?? def;
    private static double? LenN(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        v = v.Trim();
        foreach (var (u, f) in new[] { ("mm", Mm), ("in", 72.0), ("cm", Mm * 10), ("pt", 1.0) })
            if (v.EndsWith(u, StringComparison.Ordinal) && double.TryParse(v[..^u.Length], NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                return d * f;
        return double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var raw) ? raw : null;
    }
}
