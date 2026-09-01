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
    private const double Mm = 72.0 / 25.4;

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
        var root = FirstChild(template, "subform");
        if (root is null) return;

        // Data-driven occurrence expansion: clone the template and duplicate repeatable
        // subforms once per bound data group, so layout and value binding see the real
        // instance list (e.g. an order subform with occur max=3 and three data rows).
        var dataRoot = LoadDataRoot(doc);
        var groups = new List<XmlElement>();
        var formRoot = LoadFormRoot(doc);
        var origRoot = root;
        root = ExpandOccurrences(root, dataRoot, groups, formRoot);
        // The form packet is the RUNTIME state a viewer saved: its elements carry
        // the presence, values and captions the form's scripts resolved (e.g. only
        // the peril section the claim concerns stays visible, labels in the chosen
        // language). Overlay those onto the template clone so the flow renders the
        // runtime state — the template alone shows every conditional variant at
        // once. The ORIGINAL root gets the same overlay: the master pages
        // (pageSet/pageArea) are read from the original template, not the clone.
        if (formRoot is not null)
        {
            OverlayFormPresence(root, formRoot);
            OverlayFormPresence(origRoot, formRoot);
        }

        // Body content flows through the master page's content areas. Every visible
        // top-level box under the form root is a body — subforms AND bare fields/draws
        // (Designer emits e.g. captioned text fields directly under the root, and they
        // consume flow height like any subform row); Designer metadata subforms
        // ("designer__stylesheet" etc.) are not content.
        var bodies = Boxes(root)
            .Where(s => s.LocalName is "subform" or "field" or "draw" or "exclGroup"
                        && !s.GetAttribute("name").StartsWith("designer__", StringComparison.Ordinal))
            .ToList();
        var pageAreas = Descendants(template, "pageArea").ToList();
        if (bodies.Count == 0 || pageAreas.Count == 0) return;

        // The current master pageArea: a body's <breakBefore target="…"> switches to the
        // named pageArea; startNew keeps the master and starts a fresh page.
        var master = BreakTarget(bodies[0], pageAreas)
                     ?? pageAreas.FirstOrDefault(p => p.GetAttribute("name") == bodies[0].GetAttribute("name"))
                     ?? pageAreas.FirstOrDefault();
        if (master is null) return;

        // Ordered pageSet progression: the pageAreas are consumed front to
        // back, one area per page up to its occur max (default 1, -1 unbounded), and
        // the LAST area repeats for every remaining page - a first-page master with a
        // big header hands over to the taller continuation master from page 2 on.
        // An explicit breakBefore target overrides the cursor (handled at the break).
        var masterIdx = Math.Max(0, pageAreas.IndexOf(master));
        var pagesOnMaster = 0;
        static int OccurMax(XmlElement pa)
        {
            var oc = pa.ChildNodes.OfType<XmlElement>().FirstOrDefault(c => c.LocalName == "occur");
            var maxS = oc?.GetAttribute("max");
            if (string.IsNullOrEmpty(maxS)) return 1;
            if (maxS == "-1") return int.MaxValue;
            return int.TryParse(maxS, out var m) && m > 0 ? m : 1;
        }
        void AdvanceMaster()
        {
            if (pagesOnMaster >= OccurMax(master!) && masterIdx + 1 < pageAreas.Count)
            {
                masterIdx++;
                master = pageAreas[masterIdx];
                pagesOnMaster = 0;
            }
        }

        var xfaImages = LoadXfaImages(doc);
        // id="…" elements (floatingFields etc.) referenced by rich-text <span xfa:embed="#id"/>.
        var idElements = new Dictionary<string, XmlElement>(StringComparer.Ordinal);
        foreach (var el in template.SelectNodes(".//*")!.OfType<XmlElement>())
        {
            var id = el.GetAttribute("id");
            if (id.Length > 0 && !idElements.ContainsKey(id)) idElements[id] = el;
        }
        var newPages = new List<(double w, double h, List<Item> items)>();
        Ctx ctx = null!;
        double pw = 612, ph = 792;
        var areas = new List<(double x, double y, double w, double h, string name)>();
        int ai = 0; double used = 0;
        bool pageFresh = false;
        void NewPage()
        {
            (pw, ph) = MasterMedium(master!);
            areas = MasterContentAreas(master!, pw, ph);
            var items = new List<Item>();
            ctx = new Ctx
            {
                PageH = ph, RawValue = rawValue, Items = items, Groups = groups, Images = xfaImages,
                IdElements = idElements, DataRoot = dataRoot, PageNum = newPages.Count + 1,
                StrictBinding = formRoot is null,
            };
            // Master content (cover art, headers, footers) is positioned in page coordinates.
            foreach (var c in Boxes(master!))
                Place(ctx, c, Len(c.GetAttribute("x"), 0), Len(c.GetAttribute("y"), 0), "", pw - Len(c.GetAttribute("x"), 0));
            newPages.Add((pw, ph, items));
            if (System.Environment.GetEnvironmentVariable("XFA_PAGES") is not null)
                System.Console.Error.WriteLine($"NEWPAGE	page={newPages.Count}	master={master!.GetAttribute("name")}	idx={masterIdx}	onMaster={pagesOnMaster}");
            pagesOnMaster++;
            ai = 0; used = 0;
            pageFresh = true;
        }
        NewPage();
        var rootPath = root.GetAttribute("name") + "[0]";
        // A flow-container that cannot fit the remaining space SPLITS between its own
        // rows rather than forcing a fresh page (XFA's default keep=none): a viewer
        // fills the page bottom with the rows that fit and continues the rest on the
        // next page. Containers carrying <keep intact> stay whole.
        static bool KeepIntact(XmlElement c) => c.ChildNodes.OfType<XmlElement>()
            .Any(k => k.LocalName == "keep" && k.GetAttribute("intact") is "contentArea" or "pageArea");
        bool Splittable(XmlElement c) =>
            c.LocalName == "subform"
            && c.GetAttribute("layout") is "tb" or "table" or "lr-tb"
            && !KeepIntact(c)
            && Boxes(c).Count() > 1;
        void FlowRows(XmlElement c, double indentL, double indentR)
        {
            var (mt2, _, ml2, _) = Margins(c);
            var cname = c.GetAttribute("name");
            var p2 = cname.Length > 0 ? $"{rootPath}.{cname}[{SiblingIndex(c)}]" : rootPath;
            double cw = BoxW(c);
            if (cw <= 0) cw = areas[ai].w - indentL - indentR;
            used += mt2;
            // Group children into visual rows: tb/table advance per child; lr-tb
            // accumulates children left-to-right until the width wraps.
            var rows = new List<List<XmlElement>>();
            if (c.GetAttribute("layout") == "lr-tb")
            {
                double xx = 0; List<XmlElement> row = new();
                foreach (var k in Boxes(c))
                {
                    double kw = FlowWidth(ctx, k);
                    if (row.Count > 0 && xx + kw > cw + 0.5) { rows.Add(row); row = new(); xx = 0; }
                    row.Add(k); xx += kw;
                }
                if (row.Count > 0) rows.Add(row);
            }
            else
                rows.AddRange(Boxes(c).Select(k => new List<XmlElement> { k }));
            foreach (var row in rows)
            {
                double rh = row.Max(k => Height(ctx, k, cw));
                if (System.Environment.GetEnvironmentVariable("XFA_PAGES") is not null)
                    System.Console.Error.WriteLine($"ROW\t{string.Join('+', row.Select(k => k.GetAttribute("name")))}\trh={rh:F1}\tpage={newPages.Count}\tused={used:F1}\tareaH={areas[ai].h:F1}");
                // A lone splittable subform row that overflows the remaining space
                // while its own first row still fits splits at the page bottom
                // (same fills-bottom rule as the top-level flow) instead of moving
                // whole to the next area.
                if (row.Count == 1 && rh > areas[ai].h - used + 0.1 && Splittable(row[0])
                    && Boxes(row[0]).FirstOrDefault(k => !IsFloating(k)) is { } firstNested
                    && Height(ctx, firstNested, cw) <= areas[ai].h - used + 0.1)
                {
                    FlowRows(row[0], indentL + ml2, indentR);
                    continue;
                }
                while (rh > areas[ai].h - used + 0.1 && !(used == 0 && rh > areas[ai].h))
                {
                    ai++;
                    if (ai >= areas.Count) { AdvanceMaster(); NewPage(); }
                    else used = 0;
                }
                double xx2 = areas[ai].x + indentL + ml2;
                foreach (var rc in row)
                {
                    double rcw = FlowWidth(ctx, rc);
                    Place(ctx, rc, xx2, areas[ai].y + used, p2, rcw > 0 ? rcw : cw);
                    xx2 += rcw;
                }
                used += rh;
            }
        }
        foreach (var body in bodies)
        {
            double h = Height(ctx, body, areas[ai].w);
            // An empty trailing body (Designer end-of-form marker) renders nothing and
            // must not force a page even when it carries a breakBefore.
            if (h <= 0.5) continue;
            // A y-positioned decorative draw overlays the current position without
            // consuming flow height (same rule as inside container flows).
            if (IsFloating(body))
            {
                Place(ctx, body, areas[ai].x + Len(body.GetAttribute("x"), 0),
                      areas[ai].y + used + Len(body.GetAttribute("y"), 0), rootPath,
                      areas[ai].w - Len(body.GetAttribute("x"), 0));
                continue;
            }
            // A breakBefore acts only when it names a pageArea or requests a new page;
            // Designer also emits EMPTY <breakBefore/> placeholders that are no-ops.
            var brk = body.ChildNodes.OfType<XmlElement>().FirstOrDefault(c => c.LocalName == "breakBefore");
            if (brk is not null)
            {
                var target = BreakTarget(body, pageAreas);
                var startNew = brk.GetAttribute("startNew") == "1";
                if (target is not null || startNew)
                {
                    var switched = target is not null && !ReferenceEquals(target, master);
                    if (target is not null)
                    {
                        master = target;
                        masterIdx = Math.Max(0, pageAreas.IndexOf(target));
                        // A break to the master already in effect (Designer stamps one
                        // on the entry body) must not restart its occur count - that
                        // would pin the ordered progression to it forever.
                        if (switched) pagesOnMaster = 0;
                    }
                    // A CONTENT-AREA-targeted break on the SAME master moves the flow
                    // into that area - on the CURRENT page while the area is unused
                    // there (a multi-area page's bodies lay into their
                    // own areas of ONE page, startNew notwithstanding); a fresh page
                    // only when the area is already consumed.
                    var caName = brk is null ? null : BreakContentAreaName(brk);
                    var caIdx = caName is null ? -1 : areas.FindIndex(a => a.name == caName);
                    if (!switched && caIdx >= 0)
                    {
                        var consumed = caIdx < ai || (caIdx == ai && used > 0);
                        if (consumed && !pageFresh) NewPage();
                        var landIdx = areas.FindIndex(a => a.name == caName);
                        if (landIdx >= 0) { ai = landIdx; used = 0; }
                    }
                    else
                    {
                        if (!pageFresh) NewPage();
                        else if (switched) { newPages.RemoveAt(newPages.Count - 1); NewPage(); }
                        if (caName is not null)
                        {
                            var landIdx = areas.FindIndex(a => a.name == caName);
                            if (landIdx >= 0) { ai = landIdx; used = 0; }
                        }
                    }
                }
            }
            // A flow body splits between its rows mid-page (keep=none fills the page
            // bottom) in two cases: it is substantially taller than a WHOLE content
            // area (it could never fit any single area), or it fits a whole area but
            // not the remaining space AND its first row does — then the rows that fit
            // stay at the page bottom and the rest continues on the next page. A body
            // in the small-overflow band just above a whole area (≤20pt over) advances
            // instead — a viewer places it whole, tolerating the overflow, rather than
            // breaking it up.
            double maxAreaH = areas.Max(a => a.h);
            bool fillsBottom = h > areas[ai].h - used + 0.1 && h <= maxAreaH + 0.1
                && Boxes(body).FirstOrDefault(c => !IsFloating(c)) is { } firstRow
                && Height(ctx, firstRow, areas[ai].w) <= areas[ai].h - used + 0.1;
            if ((h > maxAreaH + 20 || fillsBottom) && used > 0 && Splittable(body)
                && body.GetAttribute("layout") is "tb" or "table")
            {
                if (System.Environment.GetEnvironmentVariable("XFA_PAGES") is not null)
                    System.Console.Error.WriteLine($"BODYSPLIT\t{body.GetAttribute("name")}\th={h:F1}\tpage={newPages.Count}\tused={used:F1}");
                FlowRows(body, 0, 0);
                pageFresh = false;
                continue;
            }
            while (h > areas[ai].h - used + 0.1 && !(used == 0 && h > areas[ai].h))
            {
                ai++;
                if (ai >= areas.Count) { AdvanceMaster(); NewPage(); }
                else used = 0;
            }
            if (System.Environment.GetEnvironmentVariable("XFA_PAGES") is not null)
                System.Console.Error.WriteLine($"BODY\t{body.GetAttribute("name")}\th={h:F1}\tpage={newPages.Count}\tarea={ai}\tused={used:F1}\tareaH={areas[ai].h:F1}");
            if (h > areas[ai].h + 20 && used == 0 && body.GetAttribute("layout") == "lr-tb"
                && Splittable(body))
            {
                // An over-tall lr-tb body flows its wrapped rows across content
                // areas / continuation pages, same as a tb/table body below —
                // placing it whole would clip everything past the first area.
                FlowRows(body, 0, 0);
                pageFresh = false;
                continue;
            }
            if (h > areas[ai].h + 20 && used == 0 && body.GetAttribute("layout") is "tb" or "table")
            {
                // A flow body substantially taller than a whole content area SPLITS: its
                // top-level children flow across content areas / continuation pages
                // (over-tall subforms paginate inside rather than clipping).
                // A small overshoot (≤20pt) stays on one page — coarse Height() rounding
                // must not force a page for content that belongs together.
                var (mtB, _, mlB, mrB) = Margins(body);
                used += mtB;
                foreach (var c in Boxes(body).ToList())
                {
                    if (IsFloating(c))
                    {
                        Place(ctx, c, areas[ai].x + mlB + Len(c.GetAttribute("x"), 0),
                              areas[ai].y + used + Len(c.GetAttribute("y"), 0), rootPath,
                              areas[ai].w - mlB - Len(c.GetAttribute("x"), 0));
                        continue;
                    }
                    double ch = Height(ctx, c, areas[ai].w - mlB - mrB);
                    if (System.Environment.GetEnvironmentVariable("XFA_PAGES") is not null)
                        System.Console.Error.WriteLine($"CHILD\t{c.GetAttribute("name")}\tch={ch:F1}\tpage={newPages.Count}\tused={used:F1}");
                    if (ch > areas[ai].h - used + 0.1 && !(used == 0 && ch > areas[ai].h) && Splittable(c))
                    {
                        FlowRows(c, mlB, mrB);
                        continue;
                    }
                    while (ch > areas[ai].h - used + 0.1 && !(used == 0 && ch > areas[ai].h))
                    {
                        ai++;
                        if (ai >= areas.Count) { AdvanceMaster(); NewPage(); }
                        else used = 0;
                    }
                    Place(ctx, c, areas[ai].x + mlB, areas[ai].y + used, rootPath, areas[ai].w - mlB - mrB);
                    used += ch;
                }
            }
            else
            {
                Place(ctx, body, areas[ai].x, areas[ai].y + used, rootPath, areas[ai].w);
                used += h;
            }
            pageFresh = false;
        }
        if (newPages.Count == 0) return;

        // The total page count is only known now: substitute it into any
        // xfa.layout.pageCount() placeholders emitted while painting masters.
        var total = newPages.Count.ToString(CultureInfo.InvariantCulture);
        foreach (var (_, _, items) in newPages)
            foreach (var it in items)
                if (it.Kind == "text" && it.Text.Contains(PageCountSentinel, StringComparison.Ordinal))
                    it.Text = it.Text.Replace(PageCountSentinel, total, StringComparison.Ordinal);

        // Replace all existing pages with the rendered ones (bounded to avoid any delete-loop hang).
        for (int guard = doc.Pages.Count + 8; doc.Pages.Count > 0 && guard > 0; guard--)
            doc.Pages.Delete(1);
        foreach (var (w, h, items) in newPages)
        {
            var page = doc.Pages.Add(w, h);
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
