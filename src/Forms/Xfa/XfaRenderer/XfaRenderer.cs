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
#pragma warning disable S3218 // data-only nested type: its members are always reached through an instance
        public double FontSize = 8;
#pragma warning restore S3218
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
        public double Radius;                     // corner radius of a fill or box (0 = square corners)
        public double[]? Matrix;                  // the turn of a rotated box, applied around the item (null = upright)
        public XfaFieldPlacement? Field;          // the image field this picture stands for: its widget shows the picture too
        public XfaFieldPlacement? ValueOf;        // the field whose value this text is: drawn in its widget, not on the page
    }

    /// <summary>Paint the flattened dynamic-XFA form onto fresh pages of <paramref name="doc"/>,
    /// replacing the existing (fallback) pages. No-op on any failure. With <paramref name="placeFields"/>
    /// it returns where each field was laid out on the new pages; null when it was not asked to or the
    /// pages were not replaced.</summary>
    internal static List<XfaFieldPlacement>? Render(Document doc, XmlElement template, Func<string, string?>? rawValue, bool placeFields)
    {
        try
        {
            _docFaces = CollectDocFaces(doc);
            return RenderInternal(doc, template, rawValue, placeFields);
        }
        catch { return null; /* never break flatten */ }
        finally { _docFaces = null; }
    }

    /// <summary>Faces embedded in the document's AcroForm /DR /Font resources, keyed by
    /// BaseFont name (subset prefix stripped). A template typeface with no system face
    /// (e.g. the USPS IMB barcode font) resolves through these so the barcode field
    /// paints its real glyphs instead of Helvetica letters.</summary>
    [ThreadStatic]
    private static DocFaces? _docFaces;

    /// <summary>The faces the document's resources list, each read when the template first asks for
    /// its name: a font program is decoded and parsed only for a typeface the form is set in.</summary>
    private sealed class DocFaces
    {
        private readonly IO.PdfReader _reader;
        // In the order the resources list them: a face's own name, the key it is listed under, its program.
        private readonly List<(string baseName, string key, Core.PdfStream program)> _listed = new();
        private readonly Dictionary<Core.PdfStream, (byte[] ttf, Text.GlyphOutlineParser parser)?> _read = new();
        private readonly Dictionary<string, (byte[] ttf, Text.GlyphOutlineParser parser)?> _byFamily = new(StringComparer.OrdinalIgnoreCase);

        public DocFaces(IO.PdfReader reader) => _reader = reader;

        public void List(string baseName, string key, Core.PdfStream program) => _listed.Add((baseName, key, program));

        /// <summary>The face of that name: the last listed face whose own name it is, else the first
        /// one listed under it as a key; a program that cannot be read counts as not listed.</summary>
        public bool TryGetValue(string family, out (byte[] ttf, Text.GlyphOutlineParser parser) face)
        {
            if (!_byFamily.TryGetValue(family, out var found))
            {
                foreach (var (baseName, key, program) in _listed)
                {
                    bool named = SameName(baseName, family);
                    if (!named && (found is not null || !SameName(key, family))) continue;
                    if (Read(program) is { } read) found = read;
                }
                _byFamily[family] = found;
            }
            face = found ?? default;
            return found is not null;
        }

        /// <summary>Whether two face names are one, case and spaces aside: a face's own name drops the
        /// spaces of the family the template names ("Free 3 of 9 Extended" is listed as Free3of9Extended).</summary>
        private static bool SameName(string listed, string family) =>
            string.Equals(listed.Replace(" ", ""), family.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);

        private (byte[] ttf, Text.GlyphOutlineParser parser)? Read(Core.PdfStream program)
        {
            if (_read.TryGetValue(program, out var known)) return known;
            (byte[], Text.GlyphOutlineParser)? face = null;
            try
            {
                var ttf = _reader.DecodeStream(program);
                face = (ttf, new Text.GlyphOutlineParser(ttf));
            }
            catch { /* a program that cannot be decoded or parsed is no face */ }
            _read[program] = face;
            return face;
        }
    }

    private static DocFaces? CollectDocFaces(Document doc)
    {
        try
        {
            var reader = doc.Reader;
            var acroForm = reader.ResolveDict(reader.Catalog.Get("AcroForm"));
            var dr = acroForm is null ? null : reader.ResolveDict(acroForm.Get("DR"));
            var fonts = dr is null ? null : reader.ResolveDict(dr.Get("Font"));
            if (fonts is null) return null;
            DocFaces? faces = null;
            foreach (var key in fonts.Keys)
            {
                var f = reader.ResolveDict(fonts.Get(key));
                if (f is null) continue;
                var desc = reader.ResolveDict(f.Get("FontDescriptor"));
                if (desc is null && reader.Resolve(f.Get("DescendantFonts")) is Core.PdfArray da && da.Count > 0)
                    desc = reader.ResolveDict(reader.ResolveDict(da[0])?.Get("FontDescriptor"));
                var program = desc is null ? null : reader.ResolveStream(desc.Get("FontFile2"));
                if (program is null) continue;
                var baseName = f.GetName("BaseFont") ?? key;
                if (baseName.Length > 7 && baseName[6] == '+') baseName = baseName[7..];
                faces ??= new DocFaces(reader);
                faces.List(baseName, key, program);
            }
            return faces;
        }
        catch { return null; }
    }

    private static List<XfaFieldPlacement>? RenderInternal(Document doc, XmlElement template, Func<string, string?>? rawValue, bool placeFields)
    {
        var xr = new XfaRenderState();
        xr.doc = doc;
        xr.template = template;
        xr.rawValue = rawValue;
        xr.placeFields = placeFields;
        xr.root = FirstChild(xr.template, "subform");
        if (xr.root is null) return null;

        ResolvePrototypes(xr.template);
        xr.dataRoot = LoadDataRoot(xr.doc);
        xr.groups = new List<XmlElement>();
        xr.formRoot = LoadFormRoot(xr.doc);
        xr.origRoot = xr.root;
        xr.root = ExpandOccurrences(xr.root, xr.dataRoot, xr.groups, xr.formRoot);
        ResolveScriptState(xr);
        // The form packet is the RUNTIME state a viewer saved: its elements carry
        // the values and captions the form's scripts resolved (script-computed
        // titles, labels in the chosen language). Overlay those onto the template
        // clone so the flow shows them. Which boxes are laid out is decided by the
        // template's own presence and its scripts alone: a conversion shows every
        // variant the template shows, whatever the saved state hid - unless it was
        // asked to render the form as signed, which is the state the viewer saved:
        // then the packet's presence has the last word. The ORIGINAL root gets the
        // same overlay: the master pages (pageSet/pageArea) are read from the
        // original template, not the clone.
        if (xr.formRoot is not null)
        {
            var saved = doc.Form.SignDependentElementsRenderingModeWhenConverted == Form.SignDependentElementsRenderingModes.RenderFormAsSigned;
            OverlayFormValues(xr.root, xr.formRoot, saved);
            OverlayFormValues(xr.origRoot, xr.formRoot, saved);
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
        if (bodies.Count == 0 || xr.pageAreas.Count == 0) return null;

        // The current master pageArea: a body's <breakBefore target="…"> switches to the
        // named pageArea; startNew keeps the master and starts a fresh page.
        xr.master = BreakTarget(bodies[0], xr.pageAreas)
                     ?? xr.pageAreas.FirstOrDefault(p => p.GetAttribute("name") == bodies[0].GetAttribute("name"))
                     ?? xr.pageAreas.FirstOrDefault()!;
        if (xr.master is null) return null;

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
#pragma warning disable S3267 // a step loop: each call does the work and returns false to stop - as a LINQ predicate the side effects would hide in a query
        foreach (var body in bodies)
#pragma warning restore S3267
        {
            if (!RenderBody(xr, body)) break;
        }
        if (xr.newPages.Count == 0) return null;

        PaintMasters(xr);
        // The master page content declared after the content area follows the body, and the values of
        // the master's fields come last of all: the order a viewer paints the page in.
        foreach (var (_, _, items) in xr.newPages)
        {
            if (!xr.pageTails.TryGetValue(items, out var tail)) continue;
            items.AddRange(tail.afterBody);
            items.AddRange(tail.values);
        }
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
        return placeFields ? PlacementsInPageOrder(xr) : null;
    }

    private static (double pw, double ph) MasterMedium(XmlElement master)
    {
        var medium = FirstChild(master, "medium");
        if (medium is null) return (612, 792);
        double shortSide = Len(medium.GetAttribute("short"), 612), longSide = Len(medium.GetAttribute("long"), 792);
        // A landscape medium lies on its long side: the page is as wide as that.
        return medium.GetAttribute("orientation") == "landscape" ? (longSide, shortSide) : (shortSide, longSide);
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
#pragma warning disable S1244 // the content areas are read from the same attributes - exact identity on purpose
            areas = areas.Where(a => !areas.Any(b => (b.x != a.x || b.y != a.y || b.w != a.w || b.h != a.h)
#pragma warning restore S1244
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
        public PageScripts? PageScripts;
        // Where the fields painted on this page lie, for their widgets; null when nothing collects them.
        public double PageW;
        public List<XfaFieldPlacement>? Placements;
        // Set while a turned box is painted upright: its placement is settled once the turn is known.
        public bool Turning;
        // Set while the master page content is placed: the path its fields are named under and the page area's name.
        public string? MasterPrefix;
        public string MasterName = string.Empty;
        // Where the value text of a master page field goes while the master is painted: a viewer shows
        // such a value as a widget over the whole page, so it is painted after everything else.
        public List<Item>? ValueItems;
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
                Color = StyleColor(p.GetAttribute("style")),
                HScale = pStyle.hScale ?? FontHorizontalScale(e),
            };
            pRun.LetterSpacing = pStyle.lsPt ?? (pStyle.lsEm is { } em ? em * pRun.Size : 0);
            CollectRuns(ctx, p, pRun, para.Runs);

            var maxRun = Math.Max(para.Runs.DefaultIfEmpty(pRun).Max(r => r.Size), 1);
            double lineH;
            if (FirstChild(e, "para") is { } pel && LenN(pel.GetAttribute("lineHeight")) is { } plh2 && plh2 > 0) lineH = plh2;
            else if (maxRun < 10) lineH = maxRun * 1.2;
            else lineH = Math.Ceiling(maxRun * 1.22 * 2) / 2.0;
            var lines = WrapRuns(para.Runs, avail);
            measured.Add((para, lines, lineH));
            contentH += Math.Max(1, lines.Count) * lineH + (lines.Count == 0 ? 0 : para.SpaceAfter);
        }
        return (measured, contentH);
    }

    private static (double? size, bool? bold, bool? italic, bool? serif, string? align, double? marginBottom, double? lsEm, double? lsPt, double? hScale)
        ParseStyle(string style)
    {
        double? size = null, marginBottom = null, lsEm = null, lsPt = null, hScale = null;
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
                // A family a span names is classed as a draw's typeface is: a face outside the well-known
                // sans set is set in the serif model, in the run as in its paragraph (a column whose runs
                // named the paragraph's own face wrapped in a wider model than the column beside it).
                case "font-family": serif = !IsSansTypeface(v); break;
                case "text-align": align = v.ToLowerInvariant(); break;
                case "xfa-font-horizontal-scale": hScale = HorizontalScale(v); break;
                case "margin-bottom":
                    if (v.EndsWith("pt", StringComparison.Ordinal)
                        && double.TryParse(v[..^2], NumberStyles.Any, CultureInfo.InvariantCulture, out var mb)) marginBottom = mb;
                    break;
            }
        }
        return (size, bold, italic, serif, align, marginBottom, lsEm, lsPt, hScale);
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
                           + ((bold, italic) switch { (true, true) => ",BoldItalic", (true, _) => ",Bold", (_, true) => ",Italic", _ => "" });
                var ttf = Text.SystemFontResolver.Resolve(name);
                if (ttf is not null)
                    val = (ttf, new Text.GlyphOutlineParser(ttf));
            }
            catch { /* a font that cannot be found or read leaves the fallback in place */ }
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
        // A bold or italic run takes the document's face of that style, else the system's: the family's
        // regular face is its last resort, not a stand-in for the bold one the resources list beside it.
        var style = (bold, italic) switch { (true, true) => "-BoldItalic", (true, _) => "-Bold", (_, true) => "-Italic", _ => "" };
        if (_docFaces is { } styled && style.Length > 0 && styled.TryGetValue(family.Replace(" ", "") + style, out var styledFace)) return styledFace;
        if (_docFaces is { } df && style.Length == 0 && df.TryGetValue(family, out var docFace)) return docFace;
        var system = SystemFamilyFont(family, bold, italic);
        if (system is null && style.Length > 0 && _docFaces is { } plain && plain.TryGetValue(family, out var regular)) return regular;
        return system;
    }

    private static (byte[] ttf, Text.GlyphOutlineParser parser)? SystemFamilyFont(string family, bool bold, bool italic)
    {
        var key = (family, bold, italic);
        lock (_famFonts)
        {
            if (_famFonts.TryGetValue(key, out var hit)) return hit;
            (byte[], Text.GlyphOutlineParser)? val = null;
            try
            {
                var name = family + ((bold, italic) switch { (true, true) => ",BoldItalic", (true, _) => ",Bold", (_, true) => ",Italic", _ => "" });
                var ttf = Text.SystemFontResolver.Resolve(name);
                if (ttf is not null) val = (ttf, new Text.GlyphOutlineParser(ttf));
            }
            catch { /* a font that cannot be found or read leaves the fallback in place */ }
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
        // — but never binary or vector payloads (<image> base64 must not paint as text; shapes
        // carry no text).
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
