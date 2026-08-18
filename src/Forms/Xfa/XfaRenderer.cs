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
internal static class XfaRenderer
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
        var areas = new List<(double x, double y, double w, double h)>();
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
            };
            // Master content (cover art, headers, footers) is positioned in page coordinates.
            foreach (var c in Boxes(master!))
                Place(ctx, c, Len(c.GetAttribute("x"), 0), Len(c.GetAttribute("y"), 0), "", pw - Len(c.GetAttribute("x"), 0));
            newPages.Add((pw, ph, items));
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
                    double kw = BoxW(k);
                    if (row.Count > 0 && xx + kw > cw + 0.5) { rows.Add(row); row = new(); xx = 0; }
                    row.Add(k); xx += kw;
                }
                if (row.Count > 0) rows.Add(row);
            }
            else
                rows.AddRange(Boxes(c).Select(k => new List<XmlElement> { k }));
            foreach (var row in rows)
            {
                double rh = row.Max(k => Height(ctx, k));
                if (System.Environment.GetEnvironmentVariable("XFA_PAGES") is not null)
                    System.Console.Error.WriteLine($"ROW\t{string.Join('+', row.Select(k => k.GetAttribute("name")))}\trh={rh:F1}\tpage={newPages.Count}\tused={used:F1}\tareaH={areas[ai].h:F1}");
                // A lone splittable subform row that overflows the remaining space
                // while its own first row still fits splits at the page bottom
                // (same fills-bottom rule as the top-level flow) instead of moving
                // whole to the next area.
                if (row.Count == 1 && rh > areas[ai].h - used + 0.1 && Splittable(row[0])
                    && Boxes(row[0]).FirstOrDefault(k => !IsFloating(k)) is { } firstNested
                    && Height(ctx, firstNested) <= areas[ai].h - used + 0.1)
                {
                    FlowRows(row[0], indentL + ml2, indentR);
                    continue;
                }
                while (rh > areas[ai].h - used + 0.1 && !(used == 0 && rh > areas[ai].h))
                {
                    ai++;
                    if (ai >= areas.Count) NewPage();
                    else used = 0;
                }
                double xx2 = areas[ai].x + indentL + ml2;
                foreach (var rc in row)
                {
                    double rcw = BoxW(rc);
                    Place(ctx, rc, xx2, areas[ai].y + used, p2, rcw > 0 ? rcw : cw);
                    xx2 += rcw;
                }
                used += rh;
            }
        }
        foreach (var body in bodies)
        {
            double h = Height(ctx, body);
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
                    if (target is not null) master = target;
                    if (!pageFresh) NewPage();
                    else if (switched) { newPages.RemoveAt(newPages.Count - 1); NewPage(); }
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
                && Height(ctx, firstRow) <= areas[ai].h - used + 0.1;
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
                if (ai >= areas.Count) NewPage();
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
                    double ch = Height(ctx, c);
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
                        if (ai >= areas.Count) NewPage();
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

    /// <summary>The pageArea named by a body subform's &lt;breakBefore&gt;, or null.</summary>
    private static XmlElement? BreakTarget(XmlElement body, List<XmlElement> pageAreas)
    {
        var brk = body.ChildNodes.OfType<XmlElement>().FirstOrDefault(c => c.LocalName == "breakBefore");
        if (brk is null || brk.GetAttribute("targetType") != "pageArea") return null;
        var name = brk.GetAttribute("target").TrimStart('#');
        if (name.Length == 0) return null;
        return pageAreas.FirstOrDefault(p => p.GetAttribute("name") == name);
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
    private static List<(double x, double y, double w, double h)> MasterContentAreas(XmlElement master, double pw, double ph)
    {
        var areas = Children(master, "contentArea")
            .Select(a => (x: Len(a.GetAttribute("x"), 0), y: Len(a.GetAttribute("y"), 0),
                          w: LenN(a.GetAttribute("w")) ?? pw, h: LenN(a.GetAttribute("h")) ?? ph))
            .ToList();
        if (areas.Count > 1)
            areas = areas.Where(a => !areas.Any(b => (b.x != a.x || b.y != a.y || b.w != a.w || b.h != a.h)
                                                     && b.x >= a.x - 0.1 && b.y >= a.y - 0.1
                                                     && b.x + b.w <= a.x + a.w + 0.1
                                                     && b.y + b.h <= a.y + a.h + 0.1)).ToList();
        if (areas.Count == 0) areas.Add((0, 0, pw, ph));
        return areas;
    }

    // ------------------------------------------------------------------ data / occurrences

    private sealed class Ctx
    {
        public double PageH;
        public Func<string, string?>? RawValue;
        public List<Item> Items = new();
        public List<XmlElement> Groups = new();
        public Dictionary<string, byte[]> Images = new();
        public Dictionary<string, XmlElement> IdElements = new();
        public XmlElement? DataRoot;
        public int PageNum = 1;
    }

    // Placeholder for xfa.layout.pageCount() in emitted text — the total is known only
    // after pagination, so it is substituted into the finished items in a post-pass.
    private const string PageCountSentinel = "\uE0C7";

    /// <summary>The datasets data root (the first element under &lt;xfa:data&gt;), or null.</summary>
    /// <summary>The XFA "form" packet's root subform — the runtime instance DOM a viewer
    /// recorded on save (instance managers + per-instance subform entries). Null when the
    /// document has no form packet.</summary>
    private static XmlElement? LoadFormRoot(Document doc)
    {
        try
        {
            var xml = doc.Form.GetXfaFormXml();
            if (string.IsNullOrEmpty(xml)) return null;
            var d = new XmlDocument();
            d.LoadXml(xml);
            return d.DocumentElement?.ChildNodes.OfType<XmlElement>()
                .FirstOrDefault(e => e.LocalName == "subform");
        }
        catch { return null; }
    }

    /// <summary>Copy each form-packet element's resolved <c>presence</c> onto its
    /// template counterpart. Counterparts pair by (tag, name) in sibling order —
    /// the expanded template's instance list and the packet's recorded instances
    /// run parallel. An element the packet gives no presence keeps the template's.</summary>
    private static void OverlayFormPresence(XmlElement tmpl, XmlElement form)
    {
        static bool IsBox(XmlElement e) =>
            e.LocalName is "subform" or "field" or "draw" or "exclGroup"
                or "pageSet" or "pageArea";
        var formKids = new Dictionary<string, List<XmlElement>>(StringComparer.Ordinal);
        foreach (var f in form.ChildNodes.OfType<XmlElement>().Where(IsBox))
        {
            var key = f.LocalName + "\0" + f.GetAttribute("name");
            if (!formKids.TryGetValue(key, out var list)) formKids[key] = list = new List<XmlElement>();
            list.Add(f);
        }
        if (formKids.Count == 0) return;
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var t in tmpl.ChildNodes.OfType<XmlElement>().Where(IsBox).ToList())
        {
            var key = t.LocalName + "\0" + t.GetAttribute("name");
            var idx = seen.TryGetValue(key, out var n) ? n : 0;
            seen[key] = idx + 1;
            if (!formKids.TryGetValue(key, out var list) || idx >= list.Count) continue;
            var f = list[idx];
            var pres = f.GetAttribute("presence");
            if (pres.Length > 0) t.SetAttribute("presence", pres);
            // The packet also records the resolved <value> (script-computed titles,
            // language-resolved labels): it replaces the template default. Empty
            // packet values (cleared placeholders) don't erase template text.
            var fVal = f.ChildNodes.OfType<XmlElement>().FirstOrDefault(c => c.LocalName == "value");
            if (fVal is not null && !string.IsNullOrWhiteSpace(fVal.InnerText))
            {
                var tVal = t.ChildNodes.OfType<XmlElement>().FirstOrDefault(c => c.LocalName == "value");
                var imported = (XmlElement)t.OwnerDocument!.ImportNode(fVal, true);
                if (tVal is not null) t.ReplaceChild(imported, tVal);
                else t.AppendChild(imported);
            }
            // Captions resolve at runtime too (language-selected field labels).
            // Only the caption's <value> is overlaid — the template caption keeps
            // its layout (reserve width, placement, font).
            var fCapVal = f.ChildNodes.OfType<XmlElement>().FirstOrDefault(c => c.LocalName == "caption")
                ?.ChildNodes.OfType<XmlElement>().FirstOrDefault(c => c.LocalName == "value");
            if (fCapVal is not null && !string.IsNullOrWhiteSpace(fCapVal.InnerText))
            {
                var tCap = t.ChildNodes.OfType<XmlElement>().FirstOrDefault(c => c.LocalName == "caption");
                var imported = (XmlElement)t.OwnerDocument!.ImportNode(fCapVal, true);
                if (tCap is null)
                {
                    tCap = t.OwnerDocument!.CreateElement("caption", t.NamespaceURI);
                    t.AppendChild(tCap);
                }
                var tCapVal = tCap.ChildNodes.OfType<XmlElement>().FirstOrDefault(c => c.LocalName == "value");
                if (tCapVal is not null) tCap.ReplaceChild(imported, tCapVal);
                else tCap.AppendChild(imported);
            }
            OverlayFormPresence(t, f);
        }
    }

    private static XmlElement? LoadDataRoot(Document doc)
    {
        try
        {
            var xml = doc.Form.GetXfaDatasetsXml();
            if (string.IsNullOrEmpty(xml)) return null;
            var d = new XmlDocument();
            d.LoadXml(xml);
            var data = d.DocumentElement?.ChildNodes.OfType<XmlElement>()
                .FirstOrDefault(e => e.LocalName == "data");
            return data?.ChildNodes.OfType<XmlElement>().FirstOrDefault();
        }
        catch { return null; }
    }

    /// <summary>Images embedded for external hrefs under /Catalog /Names /XFAImages
    /// (the Designer convention for template &lt;image href="…"&gt; artwork).</summary>
    private static Dictionary<string, byte[]> LoadXfaImages(Document doc)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var reader = doc.Reader;
            if (reader is null) return result;
            var names = reader.ResolveDict(reader.Catalog.Get("Names"));
            var tree = names is null ? null : reader.ResolveDict(names.Get("XFAImages"));
            var arr = tree is null ? null : reader.Resolve(tree.Get("Names")) as Core.PdfArray;
            if (arr is null) return result;
            for (int i = 0; i + 1 < arr.Count; i += 2)
            {
                if (arr[i] is Core.PdfString s && reader.Resolve(arr[i + 1]) is Core.PdfStream st)
                    result[System.Text.Encoding.UTF8.GetString(s.Value)] = reader.DecodeStream(st);
            }
        }
        catch { }
        return result;
    }

    /// <summary>Clone the form root and duplicate each repeatable subform once per bound
    /// data group. Binding is SCOPE-AWARE: a subform's candidate groups are the direct
    /// same-name children of its parent's bound data group (document-order consumption
    /// within that scope — sibling sections of the same name split the scope's groups
    /// between them, but a repeat in one table can never steal groups nested inside a
    /// DIFFERENT container's data). A template subform with no matching group passes the
    /// current scope through to its children. Bound instances carry a <c>data-idx</c>
    /// attribute indexing <paramref name="groups"/>; a data-less subform with occur
    /// min=0 is removed. When the document carries an XFA "form" packet (the runtime
    /// instance DOM a viewer saved), a subform whose form-DOM scope holds its
    /// <c>instanceManager</c> gets at least as many instances as the packet records —
    /// a user may have added instances beyond the bound data ("Add another" buttons),
    /// and those extra instances render with template defaults.</summary>
    private static XmlElement ExpandOccurrences(XmlElement root, XmlElement? dataRoot, List<XmlElement> groups, XmlElement? formRoot = null)
    {
        var owner = new XmlDocument();
        var clone = (XmlElement)owner.ImportNode(root, true);
        owner.AppendChild(clone);
        if (dataRoot is null && formRoot is null) return clone;

        var used = new HashSet<XmlElement>();
        static bool IsGroup(XmlElement el) =>
            el.ChildNodes.OfType<XmlElement>().Any()
            || el.GetAttribute("dataNode", "http://www.xfa.org/schema/xfa-data/1.0/") == "dataGroup";

        // A NON-TRIVIAL form packet records the instance set a viewer last saved —
        // under it, a data-less min-0 subform absent from the packet stays removed.
        // Without such a record the merge runs from scratch and every subform gets
        // its <occur initial> instances (the spec default is 1 even when min is 0 —
        // Designer sections like optional bordered tables render once, empty).
        var packetRecords = formRoot is not null
            && formRoot.SelectNodes(".//*")!.OfType<XmlElement>().Any(c => c.LocalName == "subform");

        void Walk(XmlElement e, XmlElement? scope, XmlElement? fScope)
        {
            foreach (var sub in e.ChildNodes.OfType<XmlElement>().Where(c => c.LocalName == "subform").ToList())
            {
                var name = sub.GetAttribute("name");
                var occ = sub.ChildNodes.OfType<XmlElement>().FirstOrDefault(c => c.LocalName == "occur");
                int max = 1, min = 1;
                if (occ is not null)
                {
                    var maxs = occ.GetAttribute("max"); var mins = occ.GetAttribute("min");
                    if (maxs.Length > 0 && int.TryParse(maxs, out var m)) max = m;
                    if (mins.Length > 0 && int.TryParse(mins, out var mi)) min = mi;
                    // occur with only a min (e.g. min="5"): the subform still repeats
                    // at least min times — an absent max never caps below it.
                    if (max >= 0 && max < min) max = min;
                }
                var avail = name.Length > 0 && scope is not null
                    ? scope.ChildNodes.OfType<XmlElement>()
                        .Where(c => c.LocalName == name && IsGroup(c) && !used.Contains(c)).ToList()
                    : new List<XmlElement>();
                var deepMatched = false;
                if (avail.Count == 0 && name.Length > 0 && scope is not null)
                {
                    // No direct child of the scope matches: fall back to scope DESCENDANTS
                    // (XFA's lenient matching for data nested deeper than the template).
                    avail = scope.SelectNodes(".//*")!.OfType<XmlElement>()
                        .Where(c => c.LocalName == name && IsGroup(c) && !used.Contains(c)).ToList();
                    // Same-name groups under DIFFERENT parents belong to different
                    // template sections (Debtor1's aliases vs Debtor2's): each
                    // repeating subform consumes only the first unconsumed parent's
                    // run, and the next section's search starts after it.
                    if (avail.Count > 1)
                    {
                        var firstParent = avail[0].ParentNode;
                        avail = avail.Where(c => ReferenceEquals(c.ParentNode, firstParent)).ToList();
                    }
                    deepMatched = avail.Count > 0;
                }
                if (avail.Count == 0 && name.Length > 0 && scope is not null && occ is not null)
                {
                    // Still nothing: a repeating subform whose own scope group is empty
                    // matches data one scope UP (a container bound to an empty marker
                    // group — e.g. <SubsequentSF/> — with the real repeat groups
                    // recorded as its siblings). Data-scope matching ascends.
                    for (var up = scope.ParentNode as XmlElement; up is not null && avail.Count == 0;
                         up = up.ParentNode as XmlElement)
                        avail = up.ChildNodes.OfType<XmlElement>()
                            .Where(c => c.LocalName == name && IsGroup(c) && !used.Contains(c)).ToList();
                }
                // Explicit <occur initial> (clamped up to min); the spec default is 1.
                int initial = 1;
                if (occ?.GetAttribute("initial") is { Length: > 0 } inis && int.TryParse(inis, out var ii))
                    initial = ii;
                if (initial < min) initial = min;
                int n = avail.Count == 0
                    // The data-less min=0 removal only applies when the document both
                    // carries data to bind against AND a form packet recording the saved
                    // instance set (the subform's absence from it means it was removed).
                    // A first-time merge instead creates the occur INITIAL instances.
                    // A data-less min>1 still renders its min instances (empty).
                    ? (occ is not null && dataRoot is not null && min == 0
                        ? (packetRecords ? 0 : initial)
                        : Math.Max(1, occ is null ? 1 : min))
                    : Math.Min(avail.Count, max < 0 ? avail.Count : Math.Max(max, 1));
                // Data present but fewer groups than the occur minimum: the template
                // minimum still governs the rendered instance count (trailing
                // instances stay empty).
                if (avail.Count > 0 && occ is not null && n < min) n = min;
                // Form-packet instances: when this subform's instanceManager appears in the
                // form DOM, honour the recorded instance count (never shrinking below the
                // data-driven count — stale packets must not drop bound data). Only an
                // OCCUR-LESS subform takes the boost: without <occur> the template alone
                // clamps to one instance and the packet is the only record of user-added
                // repeats, while a subform with an explicit <occur> already resolves its
                // count from the data (a packet layered on top double-counts).
                var fInst = new List<XmlElement>();
                if (fScope is not null && name.Length > 0)
                {
                    fInst = fScope.ChildNodes.OfType<XmlElement>()
                        .Where(c => c.LocalName == "subform" && c.GetAttribute("name") == name).ToList();
                    bool managed = fScope.ChildNodes.OfType<XmlElement>()
                        .Any(c => c.LocalName == "instanceManager" && c.GetAttribute("name") == "_" + name);
                    bool containerRepeats = e.ChildNodes.OfType<XmlElement>().Any(c => c.LocalName == "occur");
                    if (managed && occ is null && !containerRepeats && fInst.Count > n) n = fInst.Count;
                }
                if (n == 0) { e.RemoveChild(sub); continue; }
                var instances = new List<XmlElement> { sub };
                for (int k = 1; k < n; k++)
                {
                    var copy = (XmlElement)sub.CloneNode(true);
                    e.InsertAfter(copy, instances[k - 1]);
                    instances.Add(copy);
                }
                for (int k = 0; k < instances.Count; k++)
                {
                    var fk = k < fInst.Count ? fInst[k] : null;
                    if (k < avail.Count)
                    {
                        used.Add(avail[k]);
                        instances[k].SetAttribute("data-idx", groups.Count.ToString());
                        groups.Add(avail[k]);
                        Walk(instances[k], avail[k], fk);
                    }
                    else
                    {
                        // A deep-matched repeat that ran out of groups is explicitly
                        // UNBOUND: its fields must stay empty rather than scavenge
                        // same-name leaves from another section's data.
                        if (deepMatched) instances[k].SetAttribute("data-idx", "-1");
                        Walk(instances[k], scope, fk);
                    }
                }
            }
        }
        Walk(clone, dataRoot, formRoot);
        return clone;
    }

    /// <summary>Resolve a bound value for <paramref name="name"/>: the nearest expanded
    /// ancestor's data group child of that name, else null. When the data group carries
    /// SEVERAL same-name value nodes (repeated fields, e.g. a TOC's page-number column),
    /// the k-th same-name template field binds to the k-th data node in document order.</summary>
    private static string? BoundValue(Ctx ctx, XmlElement e, string name)
    {
        for (XmlElement? a = e; a is not null; a = a.ParentNode as XmlElement)
        {
            var idx = a.GetAttribute("data-idx");
            if (idx.Length == 0) continue;
            // Sentinel: an explicitly UNBOUND repeat instance — its fields stay
            // empty (the empty string blocks the flat datasets fallback too).
            if (idx == "-1") return string.Empty;
            if (!int.TryParse(idx, out var i) || i < 0 || i >= ctx.Groups.Count) continue;
            var matches = ctx.Groups[i].ChildNodes.OfType<XmlElement>().Where(c => c.LocalName == name).ToList();
            if (matches.Count == 0) continue;
            if (matches.Count == 1) return matches[0].InnerText;
            int ord = 0;
            foreach (var f in a.SelectNodes(".//*")!.OfType<XmlElement>())
            {
                if (f.LocalName != e.LocalName || f.GetAttribute("name") != name) continue;
                if (ReferenceEquals(f, e)) break;
                ord++;
            }
            return matches[Math.Min(ord, matches.Count - 1)].InnerText;
        }
        return null;
    }

    // ------------------------------------------------------------------ layout

    private static readonly string[] BoxTags = { "subform", "subformSet", "area", "field", "draw", "exclGroup" };
    private static IEnumerable<XmlElement> Boxes(XmlElement e) =>
        e.ChildNodes.OfType<XmlElement>().Where(c => BoxTags.Contains(c.LocalName) && !Hidden(c));

    private enum EditChrome { None, Underline, Box }

    /// <summary>Resolve an XFA ui &lt;border&gt; to the edit-region chrome it draws, from its
    /// per-&lt;edge&gt; &lt;presence&gt;. XFA edge order is top, right, bottom, left; a border with a
    /// SINGLE &lt;edge&gt; applies it to all four sides. All edges hidden → None; only the bottom
    /// edge visible → Underline (Designer's input-line style); otherwise → Box.</summary>
    private static EditChrome EdgeChrome(XmlElement border)
    {
        var edges = border.ChildNodes.OfType<XmlElement>().Where(c => c.LocalName == "edge").ToList();
        if (edges.Count == 0) return EditChrome.Box;      // border present, no edges → default box
        bool Vis(XmlElement edge) => edge.GetAttribute("presence") is not ("hidden" or "invisible");
        if (edges.Count == 1) return Vis(edges[0]) ? EditChrome.Box : EditChrome.None;
        int visible = edges.Count(Vis);
        if (visible == 0) return EditChrome.None;
        // Bottom is index 2 (top, right, bottom, left). Only-bottom-visible → underline.
        if (visible == 1 && edges.Count >= 3 && Vis(edges[2])) return EditChrome.Underline;
        return EditChrome.Box;
    }

    private static bool Hidden(XmlElement e) =>
        e.GetAttribute("presence") is "hidden" or "invisible" or "inactive"
        // A `relevant="-print"` SUBFORM (e.g. a designer cover sheet meant only for
        // on-screen viewing) is excluded from the static render.
        // Fields with -print (Save/Print/Reset toolbar buttons) are
        // still painted — they stay in the converted document.
        || (e.LocalName == "subform"
            && e.GetAttribute("relevant").Contains("-print", StringComparison.Ordinal));

    /// <summary>A decorative draw carrying an explicit y inside a flow container is a positioned
    /// overlay (a spacer/line) and does not consume flow height. Layout subforms flow even with an
    /// x (a horizontal column indent), so they are not treated as floating.</summary>
    private static bool IsFloating(XmlElement e) =>
        e.LocalName == "draw" && e.GetAttribute("y").Length > 0;

    /// <summary>Lay out a container's children starting at top-left (x,y) in top-down coordinates.</summary>
    private static void Layout(Ctx ctx, XmlElement e, double x, double y, string path, double availW = 0)
    {
        var (mt, mb, ml, mr) = Margins(e);
        double cx = x + ml, cy = y + mt;
        var layout = e.GetAttribute("layout");
        if (layout is "" ) layout = "position";
        var kids = Boxes(e).ToList();
        double W = BoxW(e);
        if (W <= 0) W = Math.Max(0, availW - ml - mr);
        switch (layout)
        {
            case "tb":
            case "table":
                {
                    double yy = cy;
                    foreach (var c in kids)
                    {
                        if (IsFloating(c))   // explicit x/y in a flow → positioned overlay, no flow advance
                        { Place(ctx, c, cx + Len(c.GetAttribute("x"), 0), yy + Len(c.GetAttribute("y"), 0), path, W - Len(c.GetAttribute("x"), 0)); continue; }
                        yy += Place(ctx, c, cx, yy, path, W);
                    }
                    break;
                }
            case "lr-tb":
            case "rl-tb":
                {
                    double xx = cx, yy = cy, rowh = 0;
                    foreach (var c in kids)
                    {
                        double cw = BoxW(c);
                        if (xx + cw > cx + W + 0.5 && xx > cx) { yy += rowh; xx = cx; rowh = 0; }
                        Place(ctx, c, xx, yy, path, W - (xx - cx)); xx += cw; rowh = Math.Max(rowh, Height(ctx, c));
                    }
                    break;
                }
            case "row":
                {
                    // A table's columnWidths override the cells' own widths: cell k sits at
                    // the cumulative column x and spans colSpan columns (Designer cells often
                    // carry stale w attributes narrower/wider than their column).
                    var colWidths = (e.ParentNode as XmlElement)?.GetAttribute("columnWidths")
                        ?.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => Len(s, 0)).Where(v => v > 0).ToList();
                    double xx = cx; int col = 0;
                    foreach (var c in kids)
                    {
                        double cw;
                        if (colWidths is { Count: > 0 } && col < colWidths.Count)
                        {
                            // colSpan="-1" spans all remaining columns (XFA spec).
                            int span = int.TryParse(c.GetAttribute("colSpan"), out var sp) && sp != 0
                                ? (sp < 0 ? Math.Max(1, colWidths.Count - col) : sp)
                                : 1;
                            cw = 0;
                            for (int s2 = 0; s2 < span && col + s2 < colWidths.Count; s2++) cw += colWidths[col + s2];
                            col += span;
                            c.SetAttribute("w", cw.ToString("0.###", CultureInfo.InvariantCulture) + "pt");
                        }
                        else cw = BoxW(c);
                        Place(ctx, c, xx, cy, path, cw);
                        xx += cw;
                    }
                    break;
                }
            default: // positioned
                foreach (var c in kids)
                {
                    var xoff = Len(c.GetAttribute("x"), 0);
                    Place(ctx, c, cx + xoff, cy + Len(c.GetAttribute("y"), 0), path, W - xoff);
                }
                break;
        }
    }

    /// <summary>Place one box at top-left (x,y); paint if it is a leaf; recurse if a container.
    /// Returns its laid-out height.</summary>
    private static double Place(Ctx ctx, XmlElement e, double x, double y, string path, double availW = 0)
    {
        if (Hidden(e)) return 0;
        double h = Height(ctx, e), w = BoxW(e);
        // A leaf without an explicit width spans its container's remaining width (a
        // widthless master text draw otherwise wraps one word per line).
        if (w <= 0) w = Math.Max(4, availW);
        // anchorType: a positioned element's (x,y) names the given corner/edge of its
        // box, not the top-left (a topRight-anchored title sits to the LEFT of its x).
        if (e.GetAttribute("x").Length > 0 || e.GetAttribute("y").Length > 0)
            switch (e.GetAttribute("anchorType"))
            {
                case "topCenter": x -= w / 2; break;
                case "topRight": x -= w; break;
                case "middleLeft": y -= h / 2; break;
                case "middleCenter": x -= w / 2; y -= h / 2; break;
                case "middleRight": x -= w; y -= h / 2; break;
                case "bottomLeft": y -= h; break;
                case "bottomCenter": x -= w / 2; y -= h; break;
                case "bottomRight": x -= w; y -= h; break;
            }
        var name = e.GetAttribute("name");
        string p2 = name.Length > 0 ? (path.Length > 0 ? $"{path}.{name}[{SiblingIndex(e)}]" : $"{name}[{SiblingIndex(e)}]") : path;
        switch (e.LocalName)
        {
            case "field":
                PaintField(ctx, e, x, y, w, h, p2); return h;
            case "draw":
                PaintDraw(ctx, e, x, y, w, h); return h;
            default:   // subform / area / exclGroup (a radio group lays out its option fields)
                if (DumpPos && name.Length > 0) System.Console.Error.WriteLine($"BLOCKPOS\t{name}\t{ctx.PageH - y:F1}\t{h:F1}");
                PaintContainerFill(ctx, e, x, y, w, h);
                Layout(ctx, e, x, y, p2, availW); return h;
        }
    }

    private static int SiblingIndex(XmlElement e)
    {
        int idx = 0;
        for (var s = e.PreviousSibling; s is not null; s = s.PreviousSibling)
            if (s is XmlElement se && se.LocalName == e.LocalName && se.GetAttribute("name") == e.GetAttribute("name")) idx++;
        return idx;
    }

    // ------------------------------------------------------------------ heights / widths

    private static double Height(Ctx ctx, XmlElement e)
    {
        if (Hidden(e)) return 0;
        double? h = LenN(e.GetAttribute("h")), minH = LenN(e.GetAttribute("minH"));
        if (e.LocalName == "field")
        {
            if (h is not null) return h.Value;   // fixed h clamps; content clips
            var (mt, mb, ml, mr) = Margins(e);
            double sv = FontSize(e);
            var (res, plc) = Caption(e);
            var capEl = FirstChild(e, "caption");
            var (capFs, capBold) = CaptionFont(e);
            // Laid-out caption lines: at least one whenever a caption element exists
            // (even with empty text); wraps count. Captions wrap within their reserve
            // (same 10% slack as the paint pass — the width model overestimates the
            // narrow Designer faces).
            int nC = 0;
            if (capEl is not null)
            {
                var capText = InnerText(capEl, "value") ?? "";
                double capAvail = (plc is "left" or "right") && res > 0 ? res * 1.10 : Math.Max(4, BoxW(e) - ml - mr);
                nC = Math.Max(1, string.IsNullOrWhiteSpace(capText) ? 1 : LineCount(capText.Trim(), capAvail, capFs, capBold));
            }
            // Buttons size from their caption alone.
            if (Ui(e) == "button")
            {
                double bhh = Math.Max(minH ?? 0, nC * capFs - 1) + 0.2 * capFs + 1;
                if (System.Environment.GetEnvironmentVariable("XFA_HEIGHTS") is not null)
                    System.Console.Error.WriteLine($"FH-BTN\t{e.GetAttribute("name")}\tnC={nC}\tcapFs={capFs}\tH={bhh:F2}");
                return bhh;
            }
            // Laid-out value lines (hard breaks and soft wraps both count).
            var val = FieldValueText(ctx, e);
            double availW = Math.Max(4, BoxW(e) - ml - mr - (plc is "left" or "right" ? res : 0) - 3);
            int nV = string.IsNullOrWhiteSpace(val) ? 0 : LineCount(val!.Trim(), availW, sv, FontBold(e));
            bool capHasText = capEl is not null && !string.IsNullOrWhiteSpace(InnerText(capEl, "value"));
            double borderPad = TextEditBorderPad(e);
            // Top-caption reserve holds its strip and never grows for the caption; the
            // 0.2-caption-size pad rides on top of the minH floor once a value exists.
            double fh;
            if (plc == "top" && capEl is not null)
                fh = mt + mb + res + Math.Max(nV * sv, (minH ?? 0) - mt - mb - res)
                       + (nV >= 1 ? 0.2 * capFs : 0);
            else if (capEl is not null && res <= 0.01)
            {
                // Side caption with a zero reserve: floor-then-pad — the value region
                // floors at minH, and the pads (caption lead, caption lines when it
                // has text, edit-border thickness) ride on top once a value exists.
                fh = mt + mb + Math.Max(nV * sv, (minH ?? 0) - mt - mb)
                     + (nV >= 1 ? 0.2 * capFs + (capHasText ? nC * capFs : 0) + borderPad : 0);
            }
            else
            {
                // Side caption with a reserve: pad-then-floor. The caption block is its
                // lines (text captions only) plus a 0.2-line lead and the edit-border
                // edge thickness; an empty value drops the insets.
                double capBlock = capEl is not null
                    ? (capHasText ? nC * capFs : 0) + 0.2 * capFs + borderPad
                    : 0;
                fh = nV > 0
                    ? Math.Max(minH ?? 0, mt + mb + nV * sv + capBlock)
                    : Math.Max(minH ?? 0, capBlock);
            }
            if (System.Environment.GetEnvironmentVariable("XFA_HEIGHTS") is not null)
            {
                double oldH = Math.Max(minH ?? 0, sv * 1.15 + mt + mb + (plc is "top" or "bottom" ? res : 0));
                if (Math.Abs(oldH - fh) > 0.05)
                    System.Console.Error.WriteLine($"FH\t{e.GetAttribute("name")}\tplc={plc}\tnC={nC}\tnV={nV}\tH={fh:F2}\toldH={oldH:F2}\tdelta={fh - oldH:+0.00;-0.00}");
            }
            return fh;
        }
        if (e.LocalName == "draw")
        {
            if (h is not null) return h.Value;
            var (mt, mb, _, _) = Margins(e);
            double line = FontSize(e) * 1.15;
            var (res, plc) = Caption(e);
            double nat = line + mt + mb + (plc is "top" or "bottom" ? res : 0);
            return Math.Max(minH ?? 0, nat);
        }
        if (h is not null) return h.Value;   // fixed height clamps
        // container without explicit h: ignore minH (use content height)
        return ContentHeight(ctx, e);
    }

    /// <summary>Top+bottom edit-border edge thickness of a field's ui widget (textEdit
    /// etc.) — it participates in the field's rendered height. Edge slots follow the
    /// XFA order top/right/bottom/left with the last given edge filling the remaining
    /// slots; a hidden edge contributes nothing. The border element's own presence
    /// attribute does not participate — only the edges decide.</summary>
    private static double TextEditBorderPad(XmlElement e)
    {
        var ui = FirstChild(e, "ui")?.ChildNodes.OfType<XmlElement>().FirstOrDefault();
        var border = ui?.ChildNodes.OfType<XmlElement>().FirstOrDefault(c => c.LocalName == "border");
        if (border is null) return 0;
        var edges = border.ChildNodes.OfType<XmlElement>().Where(c => c.LocalName == "edge").ToList();
        if (edges.Count == 0) return 0;
        double EdgeT(int slot)
        {
            var edge = edges[Math.Min(slot, edges.Count - 1)];
            if (edge.GetAttribute("presence") is "hidden" or "invisible") return 0;
            return LenN(edge.GetAttribute("thickness")) ?? 0.5;
        }
        return EdgeT(0) + EdgeT(2);
    }

    /// <summary>The value text a field lays out: datasets-bound if present, else the
    /// template default (picture-formatted) — the same resolution order as the paint
    /// pass, minus the SOM raw-value hook (not available during measurement).</summary>
    /// <summary>True when the element's &lt;bind&gt; declares match="none" — the
    /// field/subform takes NO data node; only its template default (or a script,
    /// which the renderer does not execute) can supply a value.</summary>
    private static bool BindsNoData(XmlElement e) =>
        FirstChild(e, "bind")?.GetAttribute("match") == "none";

    private static string? FieldValueText(Ctx ctx, XmlElement e)
    {
        string? val = null;
        if (!BindsNoData(e))
        {
            val = BoundValue(ctx, e, e.GetAttribute("name"));
            if (val is null && ctx.DataRoot is not null)
            {
                // Non-repeated fields carry no data-idx scope (only occur-expanded
                // instances do) — resolve the value leaf straight from the datasets
                // tree by name, the same way rich-text embeds do. A bound-but-EMPTY
                // value must stay empty (same rule as the paint pass), so this only
                // fires when no binding resolved at all.
                var name = e.GetAttribute("name");
                if (name.Length > 0)
                    val = ctx.DataRoot.SelectNodes(".//*")!.OfType<XmlElement>()
                        .FirstOrDefault(d => d.LocalName == name && !d.ChildNodes.OfType<XmlElement>().Any())
                        ?.InnerText;
            }
            if (!string.IsNullOrEmpty(val)) val = ApplyPicture(e, val!);
        }
        if (string.IsNullOrEmpty(val))
        {
            val = InnerText(e, "value");
            if (!string.IsNullOrEmpty(val)) val = ApplyPicture(e, val!);
        }
        return val;
    }

    /// <summary>Laid-out line count of plain text in a given width: hard line breaks
    /// (LF/CR/U+2028/U+2029) and greedy word wraps both count.</summary>
    private static int LineCount(string text, double maxWidth, double fs, bool bold)
    {
        text = text.Replace((char)0x2028, (char)0x0A).Replace((char)0x2029, (char)0x0A)
                   .Replace((char)0x0D, (char)0x0A);
        int n = 0;
        foreach (var para in text.Split('\n'))
            n += WrapLine(para, maxWidth, fs, bold).Count();
        return n;
    }

    private static double ContentHeight(Ctx ctx, XmlElement e)
    {
        var (mt, mb, _, _) = Margins(e);
        var layout = e.GetAttribute("layout"); if (layout is "") layout = "position";
        var kids = Boxes(e).ToList();
        if (kids.Count == 0) return mt + mb;
        switch (layout)
        {
            case "tb":
            case "table":
                {
                    // Flowed children stack; floating (x/y-positioned) children extend by their bottom.
                    double flow = kids.Where(c => !IsFloating(c)).Sum(c => Height(ctx, c));
                    double flt = kids.Where(IsFloating).Select(c => Len(c.GetAttribute("y"), 0) + Height(ctx, c)).DefaultIfEmpty(0).Max();
                    return mt + mb + Math.Max(flow, flt);
                }
            case "lr-tb":
            case "rl-tb":
                {
                    double W = BoxW(e), xx = 0, rowh = 0, tot = 0;
                    foreach (var c in kids)
                    {
                        double cw = BoxW(c);
                        if (xx + cw > W + 0.5 && xx > 0) { tot += rowh; rowh = 0; xx = 0; }
                        xx += cw; rowh = Math.Max(rowh, Height(ctx, c));
                    }
                    return mt + mb + tot + rowh;
                }
            case "row":
                return mt + mb + kids.Max(c => Height(ctx, c));
            default:
                return mt + mb + kids.Max(c => Len(c.GetAttribute("y"), 0) + Height(ctx, c));
        }
    }

    private static double BoxW(XmlElement e)
    {
        double? w = LenN(e.GetAttribute("w"));
        if (w is not null) return w.Value;
        // A table cell with no explicit width takes its column's width from the table's columnWidths.
        var col = ColumnWidth(e);
        if (col is not null) return col.Value;
        // A leaf sized only by its minimum (Designer buttons carry minW/minH, no w/h)
        // occupies at least that width — 0 would stack lr-tb siblings onto one spot.
        if (e.LocalName is "field" or "draw" && LenN(e.GetAttribute("minW")) is { } minW)
            return minW;
        var layout = e.GetAttribute("layout"); if (layout is "") layout = "position";
        var kids = Boxes(e).ToList();
        if (kids.Count == 0) return 0;
        if (layout is "lr-tb" or "row") return kids.Sum(BoxW);
        return kids.Max(BoxW);
    }

    /// <summary>If <paramref name="e"/> is a cell of a <c>row</c> whose parent table declares
    /// <c>columnWidths</c>, the width of its column; else null.</summary>
    private static double? ColumnWidth(XmlElement e)
    {
        if (e.ParentNode is not XmlElement row || row.GetAttribute("layout") != "row") return null;
        if (row.ParentNode is not XmlElement table) return null;
        var cw = table.GetAttribute("columnWidths");
        if (string.IsNullOrWhiteSpace(cw)) return null;
        var cols = cw.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        int idx = 0;
        for (var s = e.PreviousSibling; s is not null; s = s.PreviousSibling)
            if (s is XmlElement se && BoxTags.Contains(se.LocalName)) idx++;
        return idx < cols.Length ? LenN(cols[idx]) : null;
    }

    // ------------------------------------------------------------------ painting

    private static void PaintContainerFill(Ctx ctx, XmlElement e, double x, double y, double w, double h)
    {
        var fill = FillColor(e);
        if (fill is not null)
            ctx.Items.Add(new Item { Kind = "fill", X = x, Y = ctx.PageH - (y + h), W = w, H = h, Color = fill });
        // A container border with a visible edge strokes the subform's box (Designer
        // section frames — e.g. a two-column decision block's cell outlines).
        var border = FirstChild(e, "border");
        if (border is not null && border.GetAttribute("presence") is not ("hidden" or "invisible")
            && FirstChild(border, "edge") is { } edge
            && edge.GetAttribute("presence") is not ("hidden" or "invisible")
            && w > 0 && h > 0)
            ctx.Items.Add(new Item { Kind = "box", X = x, Y = ctx.PageH - (y + h), W = w, H = h });
    }

    private static void PaintDraw(Ctx ctx, XmlElement e, double x, double y, double w, double h)
    {
        var fill = FillColor(e);
        if (fill is not null)
            ctx.Items.Add(new Item { Kind = "fill", X = x, Y = ctx.PageH - (y + h), W = w, H = h, Color = fill });
        // A draw-level border with a visible edge strokes the draw's box (info sections).
        var drawBorder = FirstChild(e, "border");
        if (drawBorder is not null && drawBorder.GetAttribute("presence") is not ("hidden" or "invisible")
            && FirstChild(drawBorder, "edge") is { } de && de.GetAttribute("presence") is not ("hidden" or "invisible"))
            ctx.Items.Add(new Item { Kind = "box", X = x, Y = ctx.PageH - (y + h), W = w, H = h });
        // Vector-shape draw (<value><rectangle>): a filled bar and/or stroked box.
        var shape = FirstChild(e, "value") is { } dv
            ? dv.ChildNodes.OfType<XmlElement>().FirstOrDefault(c => c.LocalName == "rectangle")
            : null;
        if (shape is not null)
        {
            var shapeFill = ColorOf(FirstChild(shape, "fill"));
            if (shapeFill is not null)
                ctx.Items.Add(new Item { Kind = "fill", X = x, Y = ctx.PageH - (y + h), W = w, H = h, Color = shapeFill });
            var edge = FirstChild(shape, "edge");
            if (edge is not null && edge.GetAttribute("presence") is not ("hidden" or "invisible"))
                ctx.Items.Add(new Item { Kind = "box", X = x, Y = ctx.PageH - (y + h), W = w, H = h });
            return;
        }
        // Vector-shape draw (<value><line>): a horizontal/vertical rule spanning the
        // draw box (Designer's section separators — e.g. the thick bar under a form
        // title). Orientation follows the box's longer axis; the <edge thickness>
        // sets the stroke weight and its <color> the ink.
        var lineShape = FirstChild(e, "value") is { } dlv
            ? dlv.ChildNodes.OfType<XmlElement>().FirstOrDefault(c => c.LocalName == "line")
            : null;
        if (lineShape is not null)
        {
            var lEdge = FirstChild(lineShape, "edge");
            if (lEdge is null || lEdge.GetAttribute("presence") is not ("hidden" or "invisible"))
            {
                var th = lEdge is not null ? Len(lEdge.GetAttribute("thickness"), 0.5) : 0.5;
                if (th <= 0) th = 0.5;
                double[] lc = { 0, 0, 0 };
                if (lEdge is not null && FirstChild(lEdge, "color") is { } lcol)
                {
                    var parts = lcol.GetAttribute("value").Split(',');
                    if (parts.Length == 3)
                        lc = new[]
                        {
                            int.TryParse(parts[0], out var lr) ? lr / 255.0 : 0,
                            int.TryParse(parts[1], out var lg) ? lg / 255.0 : 0,
                            int.TryParse(parts[2], out var lb) ? lb / 255.0 : 0,
                        };
                }
                if (w >= h)
                    ctx.Items.Add(new Item { Kind = "fill", X = x, Y = ctx.PageH - (y + h / 2 + th / 2), W = w, H = th, Color = lc });
                else
                    ctx.Items.Add(new Item { Kind = "fill", X = x + w / 2 - th / 2, Y = ctx.PageH - (y + h), W = th, H = h, Color = lc });
            }
            return;
        }
        // Image draw: resolve the href against the document's embedded XFA image table.
        if (Ui(e) == "imageEdit")
        {
            var data = ImageDataOf(ctx, e);
            if (data is not null)
                ctx.Items.Add(new Item { Kind = "image", X = x, Y = ctx.PageH - (y + h), W = w, H = h, ImageData = data, Stretch = ImageStretches(e) });
            return;
        }
        // Rich text (exData XHTML with <p> paragraphs) renders with per-run styles.
        var exBody = FirstChild(e, "value") is { } val
            ? Descendants(val, "exData").FirstOrDefault()
            : null;
        if (exBody is not null && exBody.SelectNodes(".//*[local-name()='p']")!.Count > 0)
        {
            AddRichText(ctx, e, x, y, w, h, exBody);
            return;
        }
        var text = InnerText(e, "value");
        if (!string.IsNullOrWhiteSpace(text))
            AddText(ctx, e, x, y, w, h, text.Trim());
    }

    /// <summary>The image payload of a draw/field: the &lt;value&gt;&lt;image&gt;'s href
    /// resolved against the embedded XFA image table, or its inline base64 body.</summary>
    /// <summary>Whether an image draw/field fills its whole box. XFA's
    /// <c>aspect</c> default is "fit" (preserve the ratio inside the box,
    /// anchored top-left); only an explicit <c>aspect="none"</c> stretches.</summary>
    private static bool ImageStretches(XmlElement e)
    {
        var img = FirstChild(e, "value") is { } v
            ? v.ChildNodes.OfType<XmlElement>().FirstOrDefault(c => c.LocalName == "image")
            : null;
        return img?.GetAttribute("aspect") == "none";
    }

    private static byte[]? ImageDataOf(Ctx ctx, XmlElement e)
    {
        var img = FirstChild(e, "value") is { } v ? v.ChildNodes.OfType<XmlElement>().FirstOrDefault(c => c.LocalName == "image") : null;
        var href = img?.GetAttribute("href") ?? "";
        if (href.Length > 0 && ctx.Images.TryGetValue(href, out var d)) return d;
        if (img is not null && !string.IsNullOrWhiteSpace(img.InnerText))
        {
            try { return Convert.FromBase64String(img.InnerText.Trim()); } catch { }
        }
        return null;
    }

    private static readonly bool DumpPos = System.Environment.GetEnvironmentVariable("XFA_DUMP") is not null;

    private static void PaintField(Ctx ctx, XmlElement e, double x, double y, double w, double h, string path)
    {
        if (DumpPos) System.Console.Error.WriteLine($"FIELDPOS\t{path}\t{x:F1}\t{ctx.PageH - y:F1}");
        var (res, plc) = Caption(e);
        var cap = InnerText(e, "caption");
        double fs = FontSize(e);
        bool check = IsCheckbox(e), radio = e.LocalName == "exclGroup" || Ui(e) == "radio";
        var ui = Ui(e);

        if (ui == "imageEdit")
        {
            // An image field: datasets-bound base64 first, else the template image.
            // NEVER paint image data as text.
            byte[]? data = null;
            var bound = BoundValue(ctx, e, e.GetAttribute("name"));
            if (!string.IsNullOrWhiteSpace(bound))
            {
                try { data = Convert.FromBase64String(bound!.Trim()); } catch { }
            }
            data ??= ImageDataOf(ctx, e);
            if (data is not null)
                ctx.Items.Add(new Item { Kind = "image", X = x, Y = ctx.PageH - (y + h), W = w, H = h, ImageData = data, Stretch = ImageStretches(e) });
            return;
        }

        if (ui == "button")
        {
            // A button whose field border (or its edge) is hidden is an invisible link
            // hotspot (Designer "go to" navigation fields) — nothing is painted. A hidden
            // EDGE with a declared face fill only drops the outline: the filled face
            // (and its caption) still paints.
            var fb = FirstChild(e, "border");
            if (fb is not null
                && (fb.GetAttribute("presence") is "hidden" or "invisible"
                    || (FirstChild(fb, "edge")?.GetAttribute("presence") is "hidden" or "invisible"
                        && FillColor(e) is null)))
                return;
            // Push button: its declared face colour (border/direct fill), else the
            // classic gray, with a border and its caption on the face.
            ctx.Items.Add(new Item { Kind = "fill", X = x, Y = ctx.PageH - (y + h), W = w, H = h, Color = FillColor(e) ?? new[] { 0.83, 0.83, 0.83 } });
            ctx.Items.Add(new Item { Kind = "box", X = x, Y = ctx.PageH - (y + h), W = w, H = h });
            if (!string.IsNullOrWhiteSpace(cap))
            {
                // The caption's own font fill colours a button label (Designer's
                // white-on-colour faces); the field font stays for sizing.
                var capFont = FirstChild(e, "caption") is { } ce ? FirstChild(ce, "font") : null;
                var capColor = capFont is null ? null : ColorOf(FirstChild(capFont, "fill"));
                AddText(ctx, e, x + 1.5, y + Math.Max(0, (h - fs * 1.15) / 2), w, h, cap.Trim(),
                    colorOverride: capColor, alignSource: FirstChild(e, "caption"));
            }
            return;
        }

        if (check || radio)
        {
            // Check/radio glyph vertically centred in the field, dot/check when its
            // "on" value (items) matches the bound group value.
            double bs = Math.Min(9, Math.Min(w, h));
            var (mt2, _, ml2, _) = Margins(e);
            double gx = x + ml2, gy = y + Math.Max(0, (h - bs) / 2);
            bool round = FirstChild(e, "ui")?.ChildNodes.OfType<XmlElement>()
                .FirstOrDefault(c => c.LocalName == "checkButton")?.GetAttribute("shape") == "round";
            ctx.Items.Add(new Item { Kind = round ? "circle" : "box", X = gx, Y = ctx.PageH - (gy + bs), W = bs, H = bs });
            var onValue = InnerText(e, "items")?.Trim();
            var isExcl = (e.ParentNode as XmlElement)?.LocalName == "exclGroup";
            var groupName = isExcl
                ? ((XmlElement)e.ParentNode!).GetAttribute("name")
                : e.GetAttribute("name");
            var bound = BoundValue(ctx, e, groupName);
            // Same datasets-leaf fallback fields use: a top-level radio group (e.g. a
            // chapter selector bound to a root-level value) has no data-idx ancestor.
            var bindCarrier = isExcl ? (XmlElement)e.ParentNode! : e;
            if (bound is null && ctx.DataRoot is not null && groupName.Length > 0 && !BindsNoData(bindCarrier))
                bound = ctx.DataRoot.SelectNodes(".//*")!.OfType<XmlElement>()
                    .FirstOrDefault(d => d.LocalName == groupName && !d.ChildNodes.OfType<XmlElement>().Any())
                    ?.InnerText;
            if (!string.IsNullOrEmpty(onValue) && bound?.Trim() == onValue)
                ctx.Items.Add(new Item { Kind = round ? "dot" : "fill", X = gx + bs * 0.28, Y = ctx.PageH - (gy + bs * 0.72), W = bs * 0.44, H = bs * 0.44, Color = new[] { 0.0, 0.0, 0.0 } });
            if (!string.IsNullOrWhiteSpace(cap))
            {
                var (cfs, cbold) = CaptionFont(e);
                AddText(ctx, e, gx + bs + 1.5, y + Math.Max(0, (h - cfs * 1.15) / 2), w - bs - 1.5, h, cap.Trim(),
                    fsOverride: cfs, boldOverride: cbold);
            }
            return;
        }

        // Caption region: left (the XFA default) / right reserve a horizontal strip,
        // top / bottom a vertical one; the edit region is what remains.
        double capW = 0, capH = 0;
        var (capFs, capBold) = CaptionFont(e);
        if (!string.IsNullOrWhiteSpace(cap))
        {
            if (plc is "top" or "bottom") capH = res > 0 ? res : capFs * 1.2;
            else if (plc is "left" or "right" or "") capW = res > 0 ? res : TextWidth(cap.Trim(), capFs, capBold) + 4;
        }
        else if (res > 0)
        {
            // An explicit caption reserve holds its strip even with no caption text.
            if (plc is "top" or "bottom") capH = res; else capW = res;
        }
        double ex = x + (plc is "right" ? 0 : capW), ew = Math.Max(2, w - capW);
        double ey = y + (plc == "top" ? capH : 0), eh = Math.Max(2, h - capH);
        // Widget box: the visible edit chrome (border box / underline / fill) sits
        // INSIDE the margin insets — the field box holds caption strip + insets +
        // widget, and only the widget shows a border. Text keeps the edit region
        // (AddText applies the left/right insets itself).
        var (wmT, wmB, wmL, wmR) = Margins(e);
        double bx = ex + wmL, by = ey + wmT;
        double bw = Math.Max(2, ew - wmL - wmR), bh = Math.Max(2, eh - wmT - wmB);

        // datasets-bound value if present (bound-but-empty stays empty), else the
        // SOM-resolved value, else the template default. All picture-formatted;
        // a match="none" bind blocks the data paths entirely.
        string? val = null;
        if (!BindsNoData(e))
        {
            val = BoundValue(ctx, e, e.GetAttribute("name"));
            if (val is null) val = ctx.RawValue?.Invoke(path);
            if (!string.IsNullOrEmpty(val)) val = ApplyPicture(e, val!);
        }
        if (string.IsNullOrEmpty(val))
        {
            val = InnerText(e, "value");
            if (!string.IsNullOrEmpty(val)) val = ApplyPicture(e, val);
        }
        // A choice list displays the item TEXT for the bound item value.
        if (!string.IsNullOrEmpty(val) && ui == "choiceList") val = ChoiceDisplay(e, val!) ?? val;

        // A FIELD-level border paints its fill (Designer's shaded answer cells) and,
        // with a visible edge, strokes the field's whole box (table cells outline
        // this way — the row rules of a Designer grid).
        var fieldFill = FillColor(e);
        if (fieldFill is not null)
            ctx.Items.Add(new Item { Kind = "fill", X = x, Y = ctx.PageH - (y + h), W = w, H = h, Color = fieldFill });
        var fieldBorder = FirstChild(e, "border");
        if (fieldBorder is not null && fieldBorder.GetAttribute("presence") is not ("hidden" or "invisible")
            && FirstChild(fieldBorder, "edge") is not null)
            switch (EdgeChrome(fieldBorder))
            {
                case EditChrome.Box:
                    ctx.Items.Add(new Item { Kind = "box", X = x, Y = ctx.PageH - (y + h), W = w, H = h });
                    break;
                // Only the bottom edge visible (Designer's answer-line style on the
                // FIELD box): a rule across the field's full width at its bottom.
                case EditChrome.Underline:
                    ctx.Items.Add(new Item { Kind = "line", X = x, Y = ctx.PageH - (y + h), W = w, H = 0 });
                    break;
                // EditChrome.None → nothing.
            }

        // Edit-region chrome, driven by the ui <border>'s per-edge <presence>:
        //   - no <border> element        → legacy baseline underline
        //   - <border> itself hidden      → no chrome
        //   - all four edges visible (or one visible edge that XFA applies to all
        //     sides) → a full box
        //   - only the bottom edge visible (XFA edge order top/right/bottom/left,
        //     the common Designer input-line style) → a bottom underline
        // A ui-border fill shades the edit region regardless.
        var uiBorder = FirstChild(e, "ui")?.ChildNodes.OfType<XmlElement>().FirstOrDefault()
            ?.ChildNodes.OfType<XmlElement>().FirstOrDefault(c => c.LocalName == "border");
        if (uiBorder is not null && ColorOf(FirstChild(uiBorder, "fill")) is { } uiFill
            && uiBorder.GetAttribute("presence") is not ("hidden" or "invisible"))
            ctx.Items.Add(new Item { Kind = "fill", X = bx, Y = ctx.PageH - (by + bh), W = bw, H = bh, Color = uiFill });
        if (uiBorder is null)
        {
            // The legacy input answer-line applies to EDITABLE fields only: a
            // borderless access="readOnly" field is display text (letter templates
            // bind these straight to data) and Designer draws no chrome for it.
            if (e.GetAttribute("access") != "readOnly")
                ctx.Items.Add(new Item { Kind = "line", X = bx, Y = ctx.PageH - (by + bh), W = bw, H = 0 });
        }
        else if (uiBorder.GetAttribute("presence") is not ("hidden" or "invisible"))
        {
            switch (EdgeChrome(uiBorder))
            {
                case EditChrome.Box:
                    ctx.Items.Add(new Item { Kind = "box", X = bx, Y = ctx.PageH - (by + bh), W = bw, H = bh });
                    break;
                case EditChrome.Underline:
                    ctx.Items.Add(new Item { Kind = "line", X = bx, Y = ctx.PageH - (by + bh), W = bw, H = 0 });
                    break;
                // EditChrome.None → no chrome (all edges hidden).
            }
        }

        if (!string.IsNullOrWhiteSpace(val))
        {
            // A middle-aligned value under a TOP caption centres in the whole field box
            // (the caption strip merely floors it) — centring in the reduced edit region
            // sits a caption-height too low against a viewer's output.
            double vy = plc == "top"
                ? y + Math.Max(capH, (h - fs * 1.15) / 2)
                : ey + Math.Max(0, (eh - fs * 1.15) / 2);
            AddText(ctx, e, ex + 1.5, vy, ew - 3, eh, val!.Trim());
        }

        // caption label (vertically centred beside the edit region for left/right)
        if (!string.IsNullOrWhiteSpace(cap))
        {
            double capX = plc == "right" ? x + w - capW : x;
            double capY = plc switch
            {
                "bottom" => y + h - capH,
                "top" => y,
                _ => y + Math.Max(0, (h - capFs * 1.15) / 2),
            };
            AddText(ctx, e, capX, capY, capW > 0 ? capW : w, capH > 0 ? capH : h, cap.Trim(),
                fsOverride: capFs, boldOverride: capBold, alignSource: FirstChild(e, "caption"));
        }
    }

    /// <summary>Resolve a rich-text <c>xfa:embed="#id"</c> reference to its inline text:
    /// a layout page-number script yields the CURRENT page (or the page-count sentinel,
    /// substituted after pagination), otherwise the referenced element's bound/cached
    /// value. Unresolvable embeds render as a single space (the anchor placeholder).</summary>
    private static string ResolveEmbedText(Ctx ctx, string id)
    {
        if (!ctx.IdElements.TryGetValue(id, out var el)) return " ";
        var script = Descendants(el, "script").FirstOrDefault()?.InnerText ?? "";
        if (script.Contains("xfa.layout.pageCount(", StringComparison.Ordinal))
            return PageCountSentinel;
        if (script.Contains("xfa.layout.page(", StringComparison.Ordinal))
            return ctx.PageNum.ToString(CultureInfo.InvariantCulture);
        // "this.rawValue = a.b.c.rawValue;" — resolve the referenced field's data value
        // by its last path segment against the datasets tree.
        var m = System.Text.RegularExpressions.Regex.Match(
            script, @"this\.rawValue\s*=\s*([A-Za-z0-9_.\[\]]+)\.rawValue\s*;");
        if (m.Success && ctx.DataRoot is not null)
        {
            var last = m.Groups[1].Value.Split('.').Last();
            var node = ctx.DataRoot.SelectNodes(".//*")!.OfType<XmlElement>()
                .FirstOrDefault(d => d.LocalName == last && !d.ChildNodes.OfType<XmlElement>().Any());
            if (node is not null && !string.IsNullOrWhiteSpace(node.InnerText)) return node.InnerText.Trim();
        }
        var val = InnerText(el, "value");
        return string.IsNullOrWhiteSpace(val) ? " " : val.Trim();
    }

    /// <summary>Apply the field's numeric picture clause to a default value — the common
    /// Designer currency pattern <c>num{($z,zzz,zz9.99)}</c> renders 3000 as $3,000.00.
    /// Unknown pictures leave the value unchanged.</summary>
    private static string ApplyPicture(XmlElement e, string val)
    {
        var picture = FirstChild(e, "format") is { } f ? InnerText(f, "picture") : "";
        if (picture.Length == 0 || !picture.StartsWith("num{", StringComparison.Ordinal)) return val;
        if (!double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var n)) return val;
        var body = picture[4..].TrimEnd('}');
        var prefix = body.Contains('$') ? "$" : "";
        var decimals = body.Contains(".99") ? 2 : body.Contains(".9") ? 1 : 0;
        var grouped = body.Contains(',');
        var fmt = (grouped ? "#,##0" : "0") + (decimals > 0 ? "." + new string('0', decimals) : "");
        return prefix + n.ToString(fmt, CultureInfo.InvariantCulture);
    }

    /// <summary>Map a choice list's bound item VALUE to its display TEXT: the save-items
    /// list carries the values, the plain items list the texts, index-aligned.</summary>
    private static string? ChoiceDisplay(XmlElement e, string value)
    {
        var lists = e.ChildNodes.OfType<XmlElement>().Where(c => c.LocalName == "items").ToList();
        if (lists.Count == 0) return null;
        var saveList = lists.FirstOrDefault(l => l.GetAttribute("save") == "1") ?? (lists.Count > 1 ? lists[1] : null);
        var textList = lists.FirstOrDefault(l => l.GetAttribute("save") != "1") ?? lists[0];
        var texts = textList.ChildNodes.OfType<XmlElement>().Select(c => c.InnerText).ToList();
        if (saveList is null) return texts.Contains(value) ? value : null;
        var values = saveList.ChildNodes.OfType<XmlElement>().Select(c => c.InnerText.Trim()).ToList();
        var idx = values.IndexOf(value.Trim());
        return idx >= 0 && idx < texts.Count ? texts[idx] : null;
    }

    private static string? Ui(XmlElement e)
    {
        var ui = FirstChild(e, "ui");
        return ui?.ChildNodes.OfType<XmlElement>().FirstOrDefault()?.LocalName;
    }
    private static bool IsCheckbox(XmlElement e) => Ui(e) == "checkButton";

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

    private static void AddText(Ctx ctx, XmlElement e, double x, double ytop, double w, double h, string text, bool capOverride = false,
        double? fsOverride = null, bool? boldOverride = null, double[]? colorOverride = null, XmlElement? alignSource = null)
    {
        double fs = fsOverride ?? FontSize(e);
        var (mt, mb, ml, mr) = Margins(e);
        bool bold = boldOverride ?? FontBold(e);
        var color = colorOverride ?? FontColor(e);
        double avail = Math.Max(4, w - ml - mr);
        // Plain-text line pitch: exactly the font size
        // (single-spaced), or the element's explicit <para lineHeight> when declared.
        double lineH = FirstChild(e, "para") is { } pe && LenN(pe.GetAttribute("lineHeight")) is { } plh && plh > 0
            ? plh
            : fs;
        double topY = ytop + mt;
        int li = 0;
        // The element's <para hAlign> shifts each wrapped line within the available
        // width (Designer right-aligned labels sit flush against their input box).
        // A caption aligns by its OWN <para>, not the field's — the field para
        // describes the value region (a centred value must not centre its label).
        var hAlign = FirstChild(alignSource ?? e, "para")?.GetAttribute("hAlign") ?? "";
        // Captions measure against their reserve with a slack: the Helvetica width
        // model overestimates narrow Designer faces (Myriad, Arial Narrow), and a
        // caption that fits its reserve in a viewer must not wrap here.
        if (alignSource is not null) avail *= 1.10;
        // Unicode line/paragraph separators are hard line breaks (a double U+2029 = a blank line).
        text = text.Replace((char)0x2028, (char)0x0A).Replace((char)0x2029, (char)0x0A).Replace((char)0x0D, (char)0x0A);
        var wide = FontWideFactor(e);
        // A resolvable non-default face (Verdana, Tahoma …) measures and paints
        // with its real advances; the Tz wide-factor stays 1 on that path.
        var family = ResolvedFamily(e, bold);
        double LineWidth(string l) => family is null
            ? TextWidth(l, fs, bold) * wide
            : FamTextWidth(l, fs, family, bold);
        foreach (var para in text.Split('\n'))
            foreach (var line in family is null
                ? WrapLine(para, avail / wide, fs, bold)
                : WrapLineMeasured(para, avail, l => FamTextWidth(l, fs, family, bold)))
            {
                double baseline = ctx.PageH - (topY + fs + li * lineH);
                if (line.Length > 0)
                {
                    double lx = x + ml;
                    if (hAlign is "right" or "center")
                    {
                        double slack = avail - LineWidth(line);
                        if (slack > 0) lx += hAlign == "right" ? slack : slack / 2;
                    }
                    ctx.Items.Add(new Item { Kind = "text", X = lx, Y = baseline, W = w, Text = line, FontSize = fs, Bold = bold, Color = color, HScale = family is null ? wide : 1.0, Family = family });
                }
                li++;
            }
    }

    /// <summary>Greedy word-wrap with an arbitrary line-measure callback (real
    /// font advances); same break rule as <see cref="WrapLine"/>.</summary>
    private static IEnumerable<string> WrapLineMeasured(string para, double maxWidth, Func<string, double> measure)
    {
        para = para.Trim();
        if (para.Length == 0) { yield return ""; yield break; }
        if (measure(para) <= maxWidth) { yield return para; yield break; }
        var words = para.Split(' ');
        var cur = new StringBuilder();
        foreach (var wd in words)
        {
            var trial = cur.Length == 0 ? wd : cur.ToString() + " " + wd;
            if (measure(trial) > maxWidth && cur.Length > 0)
            {
                yield return cur.ToString();
                cur.Clear(); cur.Append(wd);
            }
            else { cur.Clear(); cur.Append(trial); }
        }
        if (cur.Length > 0) yield return cur.ToString();
    }

    // ------------------------------------------------------------------ rich text (exData XHTML)

    private sealed class RtRun
    {
        public string Text = "";
        public bool Bold, Italic;
        public double Size = 10;
        public bool Serif;
        public double LetterSpacing;   // extra advance per glyph (pt)
        public string? Family;         // resolvable non-default template face
    }

    private sealed class RtPara
    {
        public List<RtRun> Runs = new();
        public string Align = "left";
        public double SpaceAfter;
    }

    /// <summary>Render an exData XHTML body: per-paragraph alignment and spacing, per-run
    /// font family / size / weight / style, greedy mixed-run word wrap, and justified
    /// line filling (all lines but a paragraph's last).</summary>
    private static void AddRichText(Ctx ctx, XmlElement e, double x, double ytop, double w, double h, XmlElement exData)
    {
        var (mt, _, ml, mr) = Margins(e);
        double avail = Math.Max(4, w - ml - mr);
        // Rich text without an explicit font size uses the XFA default of 10pt (the
        // renderer-wide 8pt default is calibrated for plain caption/value text).
        double baseFs = FontSizeN(e) ?? 10;
        bool baseBold = FontBold(e);
        var color = FontColor(e);
        double cy = ytop + mt;

        // The draw's <para hAlign> is the default paragraph alignment; its <font typeface>
        // picks the base family — a typeface outside the well-known sans set renders as
        // Times (the standard substitution for unavailable Designer families).
        var defaultAlign = FirstChild(e, "para")?.GetAttribute("hAlign") is { Length: > 0 } ha ? ha : "left";
        var typeface = FirstChild(e, "font")?.GetAttribute("typeface") ?? "";
        var baseSerif = typeface.Length > 0 && !IsSansTypeface(typeface);

        // Measure pass: wrap every paragraph so a vAlign=middle/bottom block can be
        // positioned before emission.
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

            // Rich-text line pitch: below 10pt it is exactly 1.2 × the font size
            // (a 7pt intro panel measures 8.4pt per line); at 10pt and above it is
            // ~1.22 × the size quantized UP to the half-point grid (10pt → 12.5pt,
            // giving 26px rows at 150dpi — 1.2 drifted ~0.2px per line down long
            // paragraphs there). An explicit <para lineHeight> overrides the formula.
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

        var vAlign = FirstChild(e, "para")?.GetAttribute("vAlign") ?? "";
        var (mtIgn, mb, _, _) = Margins(e);
        double inner = h - mt - mb;
        if (vAlign == "middle" && contentH < inner) cy += (inner - contentH) / 2;
        else if (vAlign == "bottom" && contentH < inner) cy += inner - contentH;

        foreach (var (para, lines, lineH) in measured)
        {
            if (lines.Count == 0)
            {
                cy += lineH; // empty paragraph = a blank line
                continue;
            }
            for (int li = 0; li < lines.Count; li++)
            {
                EmitRunLine(ctx, lines[li], x + ml, cy, avail, para.Align, li == lines.Count - 1, color);
                cy += lineH;
            }
            cy += para.SpaceAfter;
        }
    }

    /// <summary>Flatten a paragraph's inline content into styled runs (spans override the
    /// paragraph style; xfa-spacerun spans are spaces; an xfa:embed span injects the
    /// referenced element's value as an inline run).</summary>
    private static void CollectRuns(Ctx ctx, XmlNode node, RtRun style, List<RtRun> into)
    {
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child is XmlText or XmlWhitespace or XmlSignificantWhitespace or XmlCDataSection)
            {
                var text = child.Value ?? "";
                if (text.Length > 0)
                    into.Add(new RtRun { Text = text, Bold = style.Bold, Italic = style.Italic, Size = style.Size, Serif = style.Serif, LetterSpacing = style.LetterSpacing, Family = style.Family });
            }
            else if (child is XmlElement el)
            {
                var sub = new RtRun { Bold = style.Bold, Italic = style.Italic, Size = style.Size, Serif = style.Serif, LetterSpacing = style.LetterSpacing, Family = style.Family };
                if (el.LocalName is "b" or "strong") sub.Bold = true;
                if (el.LocalName is "i" or "em") sub.Italic = true;
                var st = ParseStyle(el.GetAttribute("style"));
                if (st.bold is not null) sub.Bold = st.bold.Value;
                if (st.italic is not null) sub.Italic = st.italic.Value;
                if (st.size is not null) sub.Size = st.size.Value;
                if (st.serif is not null) sub.Serif = st.serif.Value;
                if (st.lsPt is not null) sub.LetterSpacing = st.lsPt.Value;
                else if (st.lsEm is not null) sub.LetterSpacing = st.lsEm.Value * sub.Size;
                var embed = el.Attributes.OfType<XmlAttribute>()
                    .FirstOrDefault(a => a.LocalName == "embed")?.Value ?? "";
                if (embed.StartsWith("#", StringComparison.Ordinal))
                {
                    into.Add(new RtRun { Text = ResolveEmbedText(ctx, embed.TrimStart('#')), Bold = sub.Bold, Italic = sub.Italic, Size = sub.Size, Serif = sub.Serif, LetterSpacing = sub.LetterSpacing, Family = sub.Family });
                    continue;
                }
                if (el.GetAttribute("style").Contains("xfa-spacerun", StringComparison.Ordinal))
                {
                    // A spacerun's WIDTH matters: leading runs indent TOC-style rows, so
                    // keep the actual character count (NBSPs render as ordinary spaces).
                    var spaces = el.InnerText.Replace('\u00A0', ' ');
                    if (spaces.Trim().Length > 0 || spaces.Length == 0) spaces = " ";
                    into.Add(new RtRun { Text = spaces, Bold = sub.Bold, Italic = sub.Italic, Size = sub.Size, Serif = sub.Serif, LetterSpacing = sub.LetterSpacing, Family = sub.Family });
                    continue;
                }
                if (el.LocalName == "br")
                {
                    into.Add(new RtRun { Text = "\n", Bold = sub.Bold, Italic = sub.Italic, Size = sub.Size, Serif = sub.Serif, Family = sub.Family });
                    continue;
                }
                CollectRuns(ctx, el, sub, into);
            }
        }
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

    private sealed class RtWord
    {
        public string Text = "";
        public RtRun Style = null!;
        public double Width;
    }

    /// <summary>Greedy word wrap over style-mixed runs; returns lines of styled words.</summary>
    private static List<List<RtWord>> WrapRuns(List<RtRun> runs, double maxWidth)
    {
        var words = new List<RtWord>();
        bool atLineStart = true;
        double pendingIndent = 0;
        foreach (var run in runs)
        {
            var normalized = run.Text.Replace(' ', ' ');
            var pieces = normalized.Split('\n');
            for (int pi = 0; pi < pieces.Length; pi++)
            {
                var piece = pieces[pi];
                int pos = 0;
                while (pos < piece.Length)
                {
                    if (piece[pos] == ' ')
                    {
                        // Leading whitespace at a HARD line start is kept as an indent
                        // marker ("\t" word, width only) — Designer TOC rows indent
                        // sub-entries with leading space runs.
                        if (atLineStart) pendingIndent += SpaceWidth(run);
                        pos++;
                        continue;
                    }
                    int end = piece.IndexOf(' ', pos);
                    if (end < 0) end = piece.Length;
                    var word = piece[pos..end];
                    if (atLineStart && pendingIndent > 0)
                    {
                        words.Add(new RtWord { Text = "\t", Style = run, Width = pendingIndent });
                        pendingIndent = 0;
                    }
                    atLineStart = false;
                    words.Add(new RtWord { Text = word, Style = run, Width = TextWidthF(word, run) });
                    pos = end;
                }
                if (pi < pieces.Length - 1)
                {
                    words.Add(new RtWord { Text = "\n", Style = run });
                    atLineStart = true;
                    pendingIndent = 0;
                }
            }
        }
        var lines = new List<List<RtWord>>();
        var cur = new List<RtWord>();
        double curW = 0;
        foreach (var word in words)
        {
            if (word.Text == "\n")
            {
                lines.Add(cur); cur = new List<RtWord>(); curW = 0;
                continue;
            }
            double space = cur.Count == 0 ? 0 : SpaceWidth(word.Style);
            if (curW + space + word.Width > maxWidth && cur.Count > 0)
            {
                lines.Add(cur); cur = new List<RtWord>(); curW = 0;
                space = 0;
            }
            cur.Add(word);
            curW += space + word.Width;
        }
        if (cur.Count > 0) lines.Add(cur);
        return lines;
    }

    private static double SpaceWidth(RtRun r) => TextWidthF(" ", r);

    /// <summary>Emit one wrapped line: words merged into same-style segments; justified
    /// lines distribute the slack across word gaps (never the paragraph's last line).</summary>
    private static void EmitRunLine(Ctx ctx, List<RtWord> line, double x, double ytop, double avail,
        string align, bool lastLine, double[] color)
    {
        if (line.Count == 0) return;
        double indent = 0;
        if (line[0].Text == "\t")
        {
            indent = line[0].Width;
            line = line.Skip(1).ToList();
            if (line.Count == 0) return;
        }
        double natural = line.Sum(w => w.Width) + line.Skip(1).Sum(w => SpaceWidth(w.Style));
        double gap = 0, startX = x + indent;
        if (align == "justify" && !lastLine && line.Count > 1 && natural + indent < avail)
            gap = (avail - indent - natural) / (line.Count - 1);
        else if (align == "center") startX = x + Math.Max(0, (avail - natural) / 2);
        else if (align == "right") startX = x + Math.Max(0, avail - natural);

        double fs = line.Max(w => w.Style.Size);
        double baseline = ctx.PageH - (ytop + fs);
        double cx = startX;
        int i = 0;
        while (i < line.Count)
        {
            // Merge adjacent words with the identical style into one text item when no
            // justification slack is being distributed.
            var st = line[i].Style;
            if (gap == 0)
            {
                var seg = new StringBuilder(line[i].Text);
                double segW = line[i].Width;
                int j = i + 1;
                while (j < line.Count && SameStyle(line[j].Style, st))
                {
                    seg.Append(' ').Append(line[j].Text);
                    segW += SpaceWidth(st) + line[j].Width;
                    j++;
                }
                ctx.Items.Add(new Item { Kind = "text", X = cx, Y = baseline, W = segW, Text = seg.ToString(), FontSize = st.Size, Bold = st.Bold, Italic = st.Italic, Serif = st.Serif, CharSpacing = st.LetterSpacing, Rich = true, Family = st.Family, Color = color });
                cx += segW + (j < line.Count ? SpaceWidth(line[j].Style) : 0);
                i = j;
            }
            else
            {
                ctx.Items.Add(new Item { Kind = "text", X = cx, Y = baseline, W = line[i].Width, Text = line[i].Text, FontSize = st.Size, Bold = st.Bold, Italic = st.Italic, Serif = st.Serif, CharSpacing = st.LetterSpacing, Rich = true, Family = st.Family, Color = color });
                cx += line[i].Width + SpaceWidth(st) + gap;
                i++;
            }
        }
    }

    private static bool SameStyle(RtRun a, RtRun b)
        => a.Bold == b.Bold && a.Italic == b.Italic && a.Serif == b.Serif && a.Family == b.Family
           && Math.Abs(a.Size - b.Size) < 0.01 && Math.Abs(a.LetterSpacing - b.LetterSpacing) < 0.001;

    private static bool IsSansTypeface(string typeface)
        => typeface.Contains("Arial", StringComparison.OrdinalIgnoreCase)
           || typeface.Contains("Helvetica", StringComparison.OrdinalIgnoreCase)
           || typeface.Contains("Verdana", StringComparison.OrdinalIgnoreCase)
           || typeface.Contains("Tahoma", StringComparison.OrdinalIgnoreCase)
           || typeface.Contains("Segoe", StringComparison.OrdinalIgnoreCase)
           || typeface.Contains("Calibri", StringComparison.OrdinalIgnoreCase);

    /// <summary>Greedy word-wrap of a line to <paramref name="maxWidth"/> pt using an approximate
    /// Helvetica advance-width metric.</summary>
    private static IEnumerable<string> WrapLine(string para, double maxWidth, double fs, bool bold)
    {
        para = para.Trim();
        if (para.Length == 0) { yield return ""; yield break; }
        if (TextWidth(para, fs, bold) <= maxWidth) { yield return para; yield break; }
        var words = para.Split(' ');
        var cur = new StringBuilder();
        foreach (var wd in words)
        {
            var trial = cur.Length == 0 ? wd : cur.ToString() + " " + wd;
            if (TextWidth(trial, fs, bold) > maxWidth && cur.Length > 0)
            {
                yield return cur.ToString();
                cur.Clear(); cur.Append(wd);
            }
            else { cur.Clear(); cur.Append(trial); }
        }
        if (cur.Length > 0) yield return cur.ToString();
    }

    // Standard Helvetica / Helvetica-Bold AFM advance widths (per-1000 em) for ASCII 32..126, so
    // line-break decisions match the layout engine's wrapping.
    private static readonly int[] HelvW =
    {
        278,278,355,556,556,889,667,191,333,333,389,584,278,333,278,278,556,556,556,556,556,556,556,556,
        556,556,278,278,584,584,584,556,1015,667,667,722,722,667,611,778,722,278,500,667,556,833,722,778,
        667,778,722,667,611,722,667,944,667,667,611,278,278,278,469,556,333,556,556,500,556,556,278,556,
        556,222,222,500,222,833,556,556,556,556,333,500,278,556,500,722,500,500,500,334,260,334,584,
    };
    private static readonly int[] HelvBoldW =
    {
        278,333,474,556,556,889,722,238,333,333,389,584,278,333,278,278,556,556,556,556,556,556,556,556,
        556,556,333,333,584,584,584,611,975,722,722,722,722,667,611,778,722,278,556,722,611,833,722,778,
        667,778,722,667,611,722,667,944,667,667,611,333,278,333,584,556,333,556,611,556,611,556,333,611,
        611,278,278,556,278,889,611,611,611,611,389,556,333,611,556,778,556,556,500,389,280,389,584,
    };
    // Standard Times-Roman / Times-Bold AFM advance widths (per-1000 em) for ASCII 32..126.
    private static readonly int[] TimesW =
    {
        250,333,408,500,500,833,778,180,333,333,500,564,250,333,250,278,500,500,500,500,500,500,500,500,
        500,500,278,278,564,564,564,444,921,722,667,667,722,611,556,722,722,333,389,722,611,889,722,722,
        556,722,667,556,611,722,722,944,722,722,611,333,278,333,469,500,333,444,500,444,500,444,333,500,
        500,278,278,500,278,778,500,500,500,500,333,389,278,500,500,722,500,500,444,480,200,480,541,
    };
    private static readonly int[] TimesBoldW =
    {
        250,333,555,500,500,1000,833,278,333,333,500,570,250,333,250,278,500,500,500,500,500,500,500,500,
        500,500,333,333,570,570,570,500,930,722,667,722,722,667,611,778,778,389,500,778,667,944,722,778,
        611,778,722,556,667,722,722,1000,722,722,667,333,278,333,581,500,333,500,556,444,556,444,333,500,
        556,278,333,556,278,833,556,500,556,556,444,389,333,556,500,722,500,500,444,394,220,394,520,
    };

    private static double TextWidth(string s, double fs, bool bold)
    {
        var t = bold ? HelvBoldW : HelvW;
        double w = 0;
        foreach (var ch in s) w += ch is >= (char)32 and <= (char)126 ? t[ch - 32] : 556;
        return w * fs / 1000.0;
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

    private static string RtFontName(bool serif, bool bold, bool italic)
        => (serif ? "TimesNewRoman" : "Arial") + (bold && italic ? "-BoldItalic" : bold ? "-Bold" : italic ? "-Italic" : "");

    /// <summary>Advance width of a styled rich-text run, including per-glyph letter
    /// spacing — real TrueType advances when the system font resolves, else the AFM
    /// tables (Times or Helvetica family).</summary>
    private static double TextWidthF(string s, RtRun r)
    {
        if (r.Family is { } fam)
            return FamTextWidth(s, r.Size, fam, r.Bold, r.Italic) + r.LetterSpacing * s.Length;
        var font = RtFont(r.Serif, r.Bold, r.Italic);
        if (font is { } f)
        {
            double adv = 0;
            var upm = (double)f.parser.UnitsPerEm;
            foreach (var ch in s)
                adv += f.parser.CMap.TryGetValue(ch, out var gid)
                    ? Math.Round(f.parser.GetAdvanceWidth(gid) * 1000.0 / upm)
                    : 500;
            return adv * r.Size / 1000.0 + r.LetterSpacing * s.Length;
        }
        var t = r.Serif ? (r.Bold ? TimesBoldW : TimesW) : (r.Bold ? HelvBoldW : HelvW);
        double w = 0;
        foreach (var ch in s) w += ch is >= (char)32 and <= (char)126 ? t[ch - 32] : 500;
        return w * r.Size / 1000.0 + r.LetterSpacing * s.Length;
    }

    // ------------------------------------------------------------------ emit

    private static byte[] Emit(List<Item> items, Page page)
    {
        var b = new Content.ContentStreamBuilder();
        foreach (var it in items)
        {
            if (it.Kind == "fill")
            {
                b.SaveState().SetFillColor(it.Color[0], it.Color[1], it.Color[2]);
                b.Rectangle(it.X, it.Y, it.W, it.H).Fill();
                b.RestoreState();
            }
            else if (it.Kind == "line")
            {
                b.SaveState().SetStrokeColor(0, 0, 0).SetLineWidth(0.5);
                b.MoveTo(it.X, it.Y).LineTo(it.X + it.W, it.Y).Stroke();
                b.RestoreState();
            }
            else if (it.Kind == "box")
            {
                b.SaveState().SetStrokeColor(0, 0, 0).SetLineWidth(0.6);
                b.Rectangle(it.X, it.Y, it.W, it.H).Stroke();
                b.RestoreState();
            }
            else if (it.Kind is "circle" or "dot")
            {
                // Circle via four Bézier quadrants; "dot" is filled (radio selection).
                double r = it.W / 2, cx = it.X + r, cy = it.Y + r, k = r * 0.5523;
                b.SaveState();
                if (it.Kind == "dot") b.SetFillColor(0, 0, 0); else b.SetStrokeColor(0, 0, 0).SetLineWidth(0.6);
                b.MoveTo(cx + r, cy)
                 .CurveTo(cx + r, cy + k, cx + k, cy + r, cx, cy + r)
                 .CurveTo(cx - k, cy + r, cx - r, cy + k, cx - r, cy)
                 .CurveTo(cx - r, cy - k, cx - k, cy - r, cx, cy - r)
                 .CurveTo(cx + k, cy - r, cx + r, cy - k, cx + r, cy);
                if (it.Kind == "dot") b.Fill(); else b.Stroke();
                b.RestoreState();
            }
            else if (it.Kind == "image" && it.ImageData is not null)
            {
                try
                {
                    page.Resources.Images.Add(new System.IO.MemoryStream(it.ImageData));
                    var name = page.Resources.Images[page.Resources.Images.Count].Name;
                    if (name is not null)
                    {
                        // Default XFA aspect ("fit"): preserve the image's natural
                        // ratio inside the box, anchored at the box's top-left.
                        double dw = it.W, dh = it.H, dy = it.Y;
                        if (!it.Stretch
                            && Document.TryGetImageNaturalSizePt(it.ImageData, out var nw, out var nh)
                            && nw > 0 && nh > 0)
                        {
                            var s = Math.Min(it.W / nw, it.H / nh);
                            dw = nw * s; dh = nh * s;
                            dy = it.Y + it.H - dh;
                        }
                        b.SaveState().SetMatrix(dw, 0, 0, dh, it.X, dy);
                        b.DrawXObject(name);
                        b.RestoreState();
                    }
                }
                catch { /* undecodable image — skip */ }
            }
            else if (it.Kind == "text")
            {
                b.SaveState().SetFillColor(it.Color[0], it.Color[1], it.Color[2]);
                b.BeginText();
                var unicodeShown = false;
                if (NeedsUnicodeFont(it.Text))
                {
                    // Text WinAnsi can't encode (Hebrew, Cyrillic, CJK, …) is shown with
                    // an embedded Identity-H Type0 font instead of collapsing to '?'.
                    var ttf = Text.SystemFontResolver.Resolve(it.Bold ? "Arial,Bold" : "Arial")
                              ?? Text.SystemFontResolver.Resolve("Arial");
                    var fontRes = PageFontDict(page);
                    if (ttf is not null && fontRes is not null)
                    {
                        var (resName, hexGlyphs) = Text.Type0FontEmbedder.Embed(
                            fontRes, ttf, it.Bold ? "Arial-Bold" : "Arial", OrderForExtraction(it.Text));
                        b.SetFont(resName, it.FontSize);
                        b.MoveTextPosition(it.X, it.Y);
                        b.ShowTextHex(hexGlyphs);
                        unicodeShown = true;
                    }
                }
                // A resolvable non-default template face (Verdana, Tahoma …) is
                // embedded so painted advances equal the widths the wrap measured.
                if (!unicodeShown && it.Family is { } fam
                    && FamilyFont(fam, it.Bold, it.Italic) is { } famf
                    && PageFontDict(page) is { } famFd)
                {
                    var famName = fam.Replace(" ", "")
                        + (it.Bold && it.Italic ? "-BoldItalic" : it.Bold ? "-Bold" : it.Italic ? "-Italic" : "");
                    var (resName, hexGlyphs) = Text.Type0FontEmbedder.Embed(famFd, famf.ttf, famName, it.Text);
                    b.SetFont(resName, it.FontSize);
                    if (it.CharSpacing != 0) b.SetCharSpacing(it.CharSpacing);
                    b.MoveTextPosition(it.X, it.Y);
                    b.ShowTextHex(hexGlyphs);
                    unicodeShown = true;
                }
                // A rich-text run renders with the real (embedded) system font so drawn
                // glyph shapes and advances match the widths the wrap was measured with.
                if (!unicodeShown && it.Rich && RtFont(it.Serif, it.Bold, it.Italic) is { } rtf
                    && PageFontDict(page) is { } fd)
                {
                    var (resName, hexGlyphs) = Text.Type0FontEmbedder.Embed(
                        fd, rtf.ttf, RtFontName(it.Serif, it.Bold, it.Italic), it.Text);
                    b.SetFont(resName, it.FontSize);
                    if (it.CharSpacing != 0) b.SetCharSpacing(it.CharSpacing);
                    b.MoveTextPosition(it.X, it.Y);
                    b.ShowTextHex(hexGlyphs);
                    unicodeShown = true;
                }
                if (!unicodeShown)
                {
                    b.SetFont(StandardFontRes(page, it.Serif, it.Bold, it.Italic), it.FontSize);
                    if (it.CharSpacing != 0) b.SetCharSpacing(it.CharSpacing);
                    if (it.HScale != 1.0) b.SetHorizontalScaling(it.HScale * 100.0);
                    b.MoveTextPosition(it.X, it.Y);
                    b.ShowText(ToWinAnsi(it.Text));
                }
                b.EndText();
                b.RestoreState();
            }
        }
        return b.Build();
    }

    /// <summary>True when the string contains characters ToWinAnsi would collapse to '?'
    /// (anything above 0xFF that isn't one of its special punctuation mappings).</summary>
    private static bool NeedsUnicodeFont(string s)
    {
        foreach (var ch in s)
            if (ch > (char)0xFF && ch is not ('‘' or '’' or '“' or '”' or '•' or '–' or '—' or '…' or '™'))
                return true;
        return false;
    }

    /// <summary>Order a line for extraction round-trip: a PURE-RTL(+neutral) line is stored
    /// reversed (visual order) because the text extractor reverses exactly such runs back to
    /// logical order; mixed lines (digits/Latin embedded in RTL) are stored logically — the
    /// extractor leaves them unchanged. Mirrors TextAbsorber.ApplyRtlIfPureRtl.</summary>
    private static string OrderForExtraction(string s)
    {
        bool hasRtl = false;
        foreach (var c in s)
        {
            if (Text.BidiReorderer.IsRtlChar(c))
                hasRtl = true;
            else if (c == ' ' || c == '\t'
                     || (c >= '!' && c <= '/') || (c >= ':' && c <= '@')
                     || (c >= '[' && c <= '`') || (c >= '{' && c <= '~'))
                { /* neutral */ }
            else
                return s; // LTR character — logical order round-trips as-is
        }
        if (!hasRtl) return s;
        var arr = s.ToCharArray();
        Array.Reverse(arr);
        return new string(arr);
    }

    /// <summary>The page's /Resources /Font dictionary (created by EnsureFonts).</summary>
    private static Core.PdfDictionary? PageFontDict(Page page)
        => (page.Dict.Get("Resources") as Core.PdfDictionary)?.Get("Font") as Core.PdfDictionary;

    /// <summary>Map a Unicode string to a WinAnsi-encoded byte string (returned as a char string whose
    /// code units are the encoded bytes). ShowText handles the ()\ escaping itself.</summary>
    private static string ToWinAnsi(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            int c = ch switch
            {
                '‘' => 0x91, '’' => 0x92, '“' => 0x93, '”' => 0x94,
                '•' => 0x95, '–' => 0x96, '—' => 0x97, '…' => 0x85,
                ' ' => 0x20, '™' => 0x99, '®' => 0xAE, '©' => 0xA9,
                _ => ch <= 0xFF ? ch : '?',
            };
            sb.Append((char)c);
        }
        return sb.ToString();
    }

    private static void EnsureFonts(Page page)
    {
        EnsureFont(page, "Helvetica", "F1");
        EnsureFont(page, "Helvetica-Bold", "F2");
    }

    /// <summary>Resource name of the standard-14 font for a run style, registered on the
    /// page on first use (F1/F2 = the legacy Helvetica pair; the rest F3..F8).</summary>
    private static string StandardFontRes(Page page, bool serif, bool bold, bool italic)
    {
        var (baseFont, res) = (serif, bold, italic) switch
        {
            (false, false, false) => ("Helvetica", "F1"),
            (false, true, false) => ("Helvetica-Bold", "F2"),
            (false, false, true) => ("Helvetica-Oblique", "F3"),
            (false, true, true) => ("Helvetica-BoldOblique", "F4"),
            (true, false, false) => ("Times-Roman", "F5"),
            (true, true, false) => ("Times-Bold", "F6"),
            (true, false, true) => ("Times-Italic", "F7"),
            (true, true, true) => ("Times-BoldItalic", "F8"),
        };
        EnsureFont(page, baseFont, res);
        return res;
    }

    private static void EnsureFont(Page page, string baseFont, string res)
    {
        var resources = page.Dict.Get("Resources") as Core.PdfDictionary;
        if (resources is null) { resources = new Core.PdfDictionary(); page.Dict.Set("Resources", resources); }
        var fonts = resources.Get("Font") as Core.PdfDictionary;
        if (fonts is null) { fonts = new Core.PdfDictionary(); resources.Set("Font", fonts); }
        if (fonts.ContainsKey(res)) return;
        var f = new Core.PdfDictionary();
        f.Set("Type", new Core.PdfName("Font"));
        f.Set("Subtype", new Core.PdfName("Type1"));
        f.Set("BaseFont", new Core.PdfName(baseFont));
        f.Set("Encoding", new Core.PdfName("WinAnsiEncoding"));
        fonts.Set(res, f);
    }

    // ------------------------------------------------------------------ template helpers

    private static (double, double, double, double) Margins(XmlElement e)
    {
        var m = FirstChild(e, "margin");
        if (m is null) return (0, 0, 0, 0);
        return (Len(m.GetAttribute("topInset"), 0), Len(m.GetAttribute("bottomInset"), 0),
                Len(m.GetAttribute("leftInset"), 0), Len(m.GetAttribute("rightInset"), 0));
    }

    private static double FontSize(XmlElement e)
    {
        // 10pt is the XFA default type size (a Designer <font> without size means 10).
        var f = FirstChild(e, "font");
        return f is null ? 10 : Len(f.GetAttribute("size"), 10);
    }

    /// <summary>The element's explicit font size, or null when the font carries none.</summary>
    private static double? FontSizeN(XmlElement e)
    {
        var f = FirstChild(e, "font");
        return f is null ? null : LenN(f.GetAttribute("size"));
    }
    private static bool FontBold(XmlElement e) =>
        FirstChild(e, "font") is { } f
        && (f.GetAttribute("weight") == "bold"
            // "Black"-class faces (Arial Black) are inherently heavy — no weight attr.
            || f.GetAttribute("typeface").Contains("Black", StringComparison.OrdinalIgnoreCase));

    /// <summary>Advance-width factor for faces wider than the Helvetica model:
    /// Arial Black runs ~15% wider than Helvetica-Bold. The emitter mirrors the
    /// factor as Tz so painted ink matches the measured span.</summary>
    private static double FontWideFactor(XmlElement e) =>
        FirstChild(e, "font")?.GetAttribute("typeface")
            .Contains("Black", StringComparison.OrdinalIgnoreCase) == true ? 1.15 : 1.0;

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

    /// <summary>The element's font typeface when it is a NON-default family the
    /// system resolves (Verdana, Tahoma, Calibri, …). Null keeps the calibrated
    /// Helvetica/Times model: default faces, unresolvable Designer faces (Myriad),
    /// and Arial Black (which keeps its Tz wide-factor emulation).</summary>
    private static string? ResolvedFamily(XmlElement e, bool bold)
    {
        var tf = FirstChild(e, "font")?.GetAttribute("typeface") ?? "";
        if (tf.Length == 0) return null;
        if (tf.Contains("Arial", StringComparison.OrdinalIgnoreCase)
            || tf.Contains("Helvetica", StringComparison.OrdinalIgnoreCase)
            || tf.Contains("Times", StringComparison.OrdinalIgnoreCase)
            || tf.Contains("Black", StringComparison.OrdinalIgnoreCase))
            return null;
        return FamilyFont(tf, bold, false) is not null ? tf : null;
    }

    /// <summary>Advance width of <paramref name="s"/> in the resolved family face;
    /// falls back to the Helvetica model when the face is unavailable.</summary>
    private static double FamTextWidth(string s, double fs, string family, bool bold, bool italic = false)
    {
        if (FamilyFont(family, bold, italic) is not { } f) return TextWidth(s, fs, bold);
        double adv = 0;
        var upm = (double)f.parser.UnitsPerEm;
        foreach (var ch in s)
            adv += f.parser.CMap.TryGetValue(ch, out var gid)
                ? Math.Round(f.parser.GetAdvanceWidth(gid) * 1000.0 / upm)
                : 500;
        return adv * fs / 1000.0;
    }
    private static double[] FontColor(XmlElement e)
    {
        var f = FirstChild(e, "font");
        var col = f is null ? null : FirstChild(f, "fill");
        return ColorOf(col) ?? new double[] { 0, 0, 0 };
    }

    private static (double reserve, string placement) Caption(XmlElement e)
    {
        var c = FirstChild(e, "caption");
        if (c is null) return (0, "left");
        return (Len(c.GetAttribute("reserve"), 0), c.GetAttribute("placement") is { Length: > 0 } p ? p : "left");
    }

    private static double[]? FillColor(XmlElement e)
    {
        // A box's background fill is either a direct <fill> or the <border>'s <fill>.
        var direct = ColorOf(FirstChild(e, "fill"));
        if (direct is not null) return direct;
        var border = FirstChild(e, "border");
        return border is null ? null : ColorOf(FirstChild(border, "fill"));
    }
    private static double[]? ColorOf(XmlElement? fill)
    {
        if (fill is null) return null;
        if (fill.GetAttribute("presence") == "hidden") return null;
        var color = FirstChild(fill, "color");
        if (color is null) return null;                 // <fill> with no <color> defaults to white — skip
        var v = color.GetAttribute("value");
        var parts = v.Split(',');
        if (parts.Length != 3) return null;
        return new[]
        {
            int.TryParse(parts[0], out var r) ? r / 255.0 : 0,
            int.TryParse(parts[1], out var g) ? g / 255.0 : 0,
            int.TryParse(parts[2], out var bl) ? bl / 255.0 : 0,
        };
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
