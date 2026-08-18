using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
namespace Aspose.Pdf.Converters;

public sealed partial class PdfToHtmlConverter
{
    // ── Link annotations ────────────────────────────────────────────────

    /// <summary>A link annotation's active rectangle and resolved href, in page
    /// (PDF user-space) coordinates. <see cref="Wrapped"/> records that some text
    /// run rendered inside the rect and carried the anchor, so no overlay is needed.</summary>
    private sealed class LinkTarget
    {
        public double Llx, Lly, Urx, Ury;
        public string Uri = "";
        public bool Wrapped;
        /// <summary>Page-menu items parsed from an <c>app.popUpMenu</c> viewer
        /// script: the covered text renders as a hover drop-up of per-page
        /// anchors instead of a plain link.</summary>
        public List<(string Label, string Href)>? PopupItems;
    }

    /// <summary>Parse an <c>app.popUpMenu("A", "B", …)</c> viewer script paired
    /// with <c>case "A": this.pageNum = N</c> dispatch into (label, "#page_N")
    /// items, in menu order. Null when the script is not that shape.</summary>
    private static List<(string Label, string Href)>? ParsePopupMenuItems(string js)
    {
        var menu = System.Text.RegularExpressions.Regex.Match(js,
            @"app\.popUpMenu\s*\(([^)]*)\)", System.Text.RegularExpressions.RegexOptions.Singleline);
        if (!menu.Success) return null;
        var labels = new List<string>();
        foreach (System.Text.RegularExpressions.Match m in
            System.Text.RegularExpressions.Regex.Matches(menu.Groups[1].Value, @"""((?:[^""\\]|\\.)*)"""))
            labels.Add(m.Groups[1].Value);
        if (labels.Count == 0) return null;
        var pageByLabel = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (System.Text.RegularExpressions.Match m in
            System.Text.RegularExpressions.Regex.Matches(js,
                @"case\s+""((?:[^""\\]|\\.)*)""\s*:\s*this\.pageNum\s*=\s*(\d+)",
                System.Text.RegularExpressions.RegexOptions.Singleline))
            pageByLabel[m.Groups[1].Value] = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        var items = new List<(string, string)>();
        foreach (var label in labels)
            if (pageByLabel.TryGetValue(label, out var p))
                items.Add((label, $"#page_{p}"));
        return items.Count > 0 ? items : null;
    }

    /// <summary>Document-wide registry of internal link destinations. Every GoTo /
    /// direct-dest link annotation resolves to a point on its target page; distinct
    /// points are indexed 0-based per page in top-down order, giving each link the
    /// fragment href <c>#&lt;1-based page&gt;_&lt;index&gt;</c>, and each target page
    /// a matching set of positioned <c>&lt;a name&gt;</c> anchors at its layer end.</summary>
    private sealed class DestAnchorRegistry
    {
        /// <summary>Per 1-based target page: distinct destination points, top-down.</summary>
        public readonly Dictionary<int, List<(double X, double Y)>> PageDests = new();

        /// <summary>Page dictionary → 1-based page number, for dest resolution.</summary>
        public readonly Dictionary<PdfDictionary, int> PageNumbers = new();

        /// <summary>The fragment href for a destination point, or null when the
        /// point is not registered.</summary>
        public string? Href(int page, double x, double y)
        {
            if (!PageDests.TryGetValue(page, out var pts)) return null;
            var idx = pts.IndexOf((x, y));
            return idx < 0 ? null : $"#{page}_{idx}";
        }

        /// <summary>The fragment href for a link annotation's internal destination,
        /// or null when it carries none.</summary>
        public string? HrefForAnnot(PdfDictionary annot, Document doc, PdfReader reader)
        {
            return TryResolveDestPoint(annot, doc, reader, PageNumbers, out var tp, out var x, out var y)
                ? Href(tp, x, y)
                : null;
        }
    }

    private DestAnchorRegistry? _destAnchors;

    private Document? _destAnchorsDoc;

    private DestAnchorRegistry DestAnchorsFor(Document doc)
    {
        if (!ReferenceEquals(_destAnchorsDoc, doc) || _destAnchors is null)
        {
            _destAnchors = BuildDestAnchors(doc);
            _destAnchorsDoc = doc;
        }
        return _destAnchors;
    }

    private static DestAnchorRegistry BuildDestAnchors(Document doc)
    {
        var reg = new DestAnchorRegistry();
        var reader = doc.Reader;
        for (var i = 1; i <= doc.PageCount; i++) reg.PageNumbers[doc.Pages[i].Dict] = i;

        var points = new List<(int Page, double X, double Y)>();
        for (var i = 1; i <= doc.PageCount; i++)
        {
            if (reader.Resolve(doc.Pages[i].Dict.Get("Annots")) is not PdfArray annots) continue;
            foreach (var it in annots)
            {
                var annot = reader.ResolveDict(it);
                if (annot is null || annot.GetName("Subtype") != "Link") continue;
                if (TryResolveDestPoint(annot, doc, reader, reg.PageNumbers, out var tp, out var x, out var y))
                    points.Add((tp, x, y));
            }
        }
        foreach (var group in points.GroupBy(p => p.Page))
        {
            reg.PageDests[group.Key] = group
                .Select(p => (p.X, p.Y))
                .Distinct()
                .OrderByDescending(p => p.Item2).ThenBy(p => p.Item1)
                .ToList();
        }
        return reg;
    }

    /// <summary>Resolve a link annotation's internal destination — a direct /Dest
    /// or a GoTo action's /D, explicit array or named — to its target page number
    /// and point (defaults: page's top-left for view fits without coordinates).</summary>
    private static bool TryResolveDestPoint(PdfDictionary annot, Document doc, PdfReader reader,
        Dictionary<PdfDictionary, int> pageNum, out int targetPage, out double x, out double y)
    {
        targetPage = 0; x = 0; y = 0;
        var destObj = reader.Resolve(annot.Get("Dest"));
        if (destObj is null)
        {
            var action = reader.ResolveDict(annot.Get("A"));
            if (action is not null && action.GetName("S") == "GoTo")
                destObj = reader.Resolve(action.Get("D"));
        }
        if (destObj is PdfString ds)
            destObj = ResolveNamedDest(doc, reader, ds.ToText());
        else if (destObj is PdfName dn)
            destObj = ResolveNamedDest(doc, reader, dn.Value);
        if (destObj is not PdfArray dest || dest.Count < 1) return false;
        var targetDict = reader.ResolveDict(dest[0]);
        if (targetDict is null || !pageNum.TryGetValue(targetDict, out targetPage)) return false;

        var mb = doc.Pages[targetPage].MediaBox;
        x = mb.LLX; y = mb.URY;
        var fit = dest.Count >= 2 ? (reader.Resolve(dest[1]) as PdfName)?.Value : null;
        switch (fit)
        {
            case "XYZ":
                if (dest.Count >= 3 && NumOrNull(reader.Resolve(dest[2])) is { } xv) x = xv;
                if (dest.Count >= 4 && NumOrNull(reader.Resolve(dest[3])) is { } yv) y = yv;
                break;
            case "FitH" or "FitBH":
                if (dest.Count >= 3 && NumOrNull(reader.Resolve(dest[2])) is { } yh) y = yh;
                break;
            case "FitV" or "FitBV":
                if (dest.Count >= 3 && NumOrNull(reader.Resolve(dest[2])) is { } xv2) x = xv2;
                break;
            case "FitR":
                if (dest.Count >= 3 && NumOrNull(reader.Resolve(dest[2])) is { } xr) x = xr;
                if (dest.Count >= 6 && NumOrNull(reader.Resolve(dest[5])) is { } yr) y = yr;
                break;
        }
        return true;
    }

    private static double? NumOrNull(PdfObject? obj) => obj switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => null,
    };

    /// <summary>Look a named destination up in the catalog's /Dests dictionary or
    /// the /Names → /Dests name tree. Returns the destination array (unwrapping a
    /// /D-carrying dictionary), or null.</summary>
    private static PdfObject? ResolveNamedDest(Document doc, PdfReader reader, string name)
    {
        var catalog = reader.ResolveDict(reader.Trailer?.Get("Root"));
        if (catalog is null) return null;
        var dests = reader.ResolveDict(catalog.Get("Dests"));
        var direct = dests?.Get(name);
        var resolved = direct is null ? null : reader.Resolve(direct);
        if (resolved is null)
        {
            var names = reader.ResolveDict(catalog.Get("Names"));
            var tree = names is null ? null : reader.ResolveDict(names.Get("Dests"));
            resolved = tree is null ? null : reader.Resolve(LookupNameTree(tree, name, reader));
        }
        if (resolved is PdfDictionary dd) resolved = reader.Resolve(dd.Get("D"));
        return resolved;
    }

    private static PdfObject? LookupNameTree(PdfDictionary node, string name, PdfReader reader)
    {
        if (reader.Resolve(node.Get("Names")) is PdfArray leaf)
        {
            for (var i = 0; i + 1 < leaf.Count; i += 2)
            {
                if (reader.Resolve(leaf[i]) is PdfString key && key.ToText() == name)
                    return leaf[i + 1];
            }
        }
        if (reader.Resolve(node.Get("Kids")) is PdfArray kids)
        {
            foreach (var kid in kids)
            {
                var kd = reader.ResolveDict(kid);
                if (kd is null) continue;
                var found = LookupNameTree(kd, name, reader);
                if (found is not null) return found;
            }
        }
        return null;
    }

    /// <summary>Per-page paint-order counter for UseZOrder: every atomic painted
    /// object (non-whitespace glyph, path fill/stroke, image, soft-mask content)
    /// advances it; a text div's z-index is the value at its last glyph. Mask
    /// forms memoize their object count — the SAME mask loaded by another gs op
    /// still adds its count again.</summary>
    private sealed class ZCounter
    {
        public int V;
        public readonly Dictionary<PdfStream, int> MaskMemo = new();
    }

    /// <summary>Whether a Do operand names an Image XObject — first via the page's
    /// pre-resolved image map, else through the active resource dictionary (a form's
    /// own images are not in the page map).</summary>
    private static bool IsImageXObject(string name, Dictionary<string, ImageXObject> imageXObjects,
        PdfDictionary? resources, PdfReader reader)
    {
        if (imageXObjects.ContainsKey(name)) return true;
        if (resources is null) return false;
        var xo = reader.ResolveDict(resources.Get("XObject"));
        var st = xo is not null ? reader.ResolveStream(xo.Get(name)) : null;
        return st is not null && st.Dict.GetName("Subtype") == "Image";
    }

    /// <summary>Count the atomic painted objects inside a soft-mask (or nested)
    /// form: path fills/strokes, images, shown non-whitespace text (approximated
    /// as non-whitespace string bytes), plus nested forms and soft masks,
    /// recursively. Memoized per form stream.</summary>
    private static int CountMaskPaintOps(PdfStream form, PdfReader reader,
        Dictionary<PdfStream, int> memo, int depth)
    {
        if (depth > 8) return 0;
        if (memo.TryGetValue(form, out var cached)) return cached;
        memo[form] = 0; // cycle guard while this form is being counted
        var count = 0;
        try
        {
            var bytes = reader.DecodeStream(form);
            var res = reader.ResolveDict(form.Dict.Get("Resources"));
            var lexer = new PdfLexer(bytes);
            var operands = new List<PdfObject>();
            while (true)
            {
                var token = lexer.NextToken();
                if (token.Kind == TokenKind.Eof) break;
                switch (token.Kind)
                {
                    case TokenKind.Integer: operands.Add(new PdfInteger(token.IntValue)); break;
                    case TokenKind.Real: operands.Add(new PdfReal(token.RealValue)); break;
                    case TokenKind.LiteralString: operands.Add(new PdfString(token.BytesValue!)); break;
                    case TokenKind.HexString: operands.Add(new PdfString(token.BytesValue!, isHex: true)); break;
                    case TokenKind.Name: operands.Add(new PdfName(token.StringValue!)); break;
                    case TokenKind.ArrayStart: operands.Add(ParseArray(lexer)); break;
                    case TokenKind.Keyword:
                        switch (token.StringValue)
                        {
                            case "S" or "s" or "f" or "F" or "f*" or "B" or "B*" or "b" or "b*" or "BI":
                                count++;
                                break;
                            case "Tj" or "'" or "\"" or "TJ":
                                foreach (var o in operands)
                                {
                                    if (o is PdfString ps)
                                        foreach (var by in ps.Value) { if (by is not (0x20 or 0x09 or 0x0A or 0x0D)) count++; }
                                    else if (o is PdfArray arr)
                                        for (var ai = 0; ai < arr.Count; ai++)
                                            if (arr[ai] is PdfString aps)
                                                foreach (var by in aps.Value) { if (by is not (0x20 or 0x09 or 0x0A or 0x0D)) count++; }
                                }
                                break;
                            case "Do":
                                if (res is not null && operands.Count >= 1 && operands[0] is PdfName doName)
                                {
                                    var xo = reader.ResolveDict(res.Get("XObject"));
                                    var st = xo is not null ? reader.ResolveStream(xo.Get(doName.Value)) : null;
                                    if (st is not null)
                                    {
                                        if (st.Dict.GetName("Subtype") == "Image") count++;
                                        else count += CountMaskPaintOps(st, reader, memo, depth + 1);
                                    }
                                }
                                break;
                            case "gs":
                                if (res is not null && operands.Count >= 1 && operands[0] is PdfName gsName)
                                {
                                    var egs = reader.ResolveDict(
                                        reader.ResolveDict(res.Get("ExtGState"))?.Get(gsName.Value));
                                    var sm = egs is not null ? reader.ResolveDict(egs.Get("SMask")) : null;
                                    var g = sm is not null ? reader.ResolveStream(sm.Get("G")) : null;
                                    if (g is not null) count += CountMaskPaintOps(g, reader, memo, depth + 1);
                                }
                                break;
                        }
                        operands.Clear();
                        break;
                }
            }
        }
        catch { /* an undecodable mask contributes what was counted so far */ }
        memo[form] = count;
        return count;
    }

    /// <summary>Root-em (pt/12) coordinate for inline styles: 4 decimals, rounded
    /// half away from zero, trailing zeros trimmed.</summary>
    private static string Em4(double v) =>
        Math.Round(v, 4, MidpointRounding.AwayFromZero).ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>Em4 for text-run coordinates, whose device x/y go through a long
    /// multiply chain: values a few micro-em under a
    /// 4-decimal tie round up, absorbing the chain's accumulated float noise.</summary>
    private static string Em4T(double v) =>
        Math.Round(v + (v >= 0 ? 5e-6 : -5e-6), 4, MidpointRounding.AwayFromZero)
            .ToString("0.####", CultureInfo.InvariantCulture);

    // A 1×1 fully transparent PNG: the stretched click surface inside a "great
    // link" overlay anchor (the stl_grlink structural class supplies the geometry).
    private const string TransparentPixelPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=";

    /// <summary>Emit the invisible link overlay for each link annotation: a
    /// positioned class-less div (root-em geometry, page top from the truncated
    /// page height) holding the anchor around a stretched 1×1 transparent PNG with
    /// the grlink class (z-index 1000000 via structural CSS).
    /// A link whose rect covered text is skipped: that text already renders inside
    /// an inline anchor, and a second anchor over it would make the page carry two
    /// hyperlinks where the document has one.</summary>
    private static void EmitGrlinkOverlays(List<LinkTarget>? targets, StringBuilder sb,
        double pageHeight, ClassNamer namer)
    {
        if (targets is null) return;
        foreach (var t in targets)
        {
            if (t.Wrapped) continue;
            sb.Append($"\t\t\t<div style=\"position:absolute;left:{Em4(t.Llx / 12.0)}em;" +
                $"top:{Em4((Math.Floor(pageHeight) - t.Ury) / 12.0)}em;" +
                $"width:{Em4((t.Urx - t.Llx) / 12.0)}em;height:{Em4((t.Ury - t.Lly) / 12.0)}em;\">\r\n");
            sb.Append($"\t\t\t\t<a href=\"{EscapeHtml(t.Uri)}\"" +
                (t.Uri.StartsWith('#') ? ">\r\n" : " target=\"_blank\">\r\n"));
            sb.Append($"\t\t\t\t\t<img src=\"data:image/png;base64,{TransparentPixelPng}\" " +
                $"class=\"{namer.Cls("grlink")}\" />\r\n");
            sb.Append("\t\t\t\t</a>\r\n");
            sb.Append("\t\t\t</div>\r\n");
        }
    }

    private static List<LinkTarget>? CollectLinkTargets(PdfDictionary pageDict, PdfReader reader,
        Document? doc = null, DestAnchorRegistry? destAnchors = null)
    {
        if (reader.Resolve(pageDict.Get("Annots")) is not PdfArray annots) return null;
        List<LinkTarget>? result = null;
        foreach (var annotRef in annots)
        {
            var annotDict = reader.ResolveDict(annotRef);
            if (annotDict is null) continue;
            var actionDict = reader.ResolveDict(annotDict.Get("A"));
            var uri = ResolveLinkHref(actionDict, reader);
            // An action-less internal link resolves through the document-wide
            // destination registry to its "#page_index" fragment.
            if (uri is null && destAnchors is not null && doc is not null
                && annotDict.GetName("Subtype") == "Link")
                uri = destAnchors.HrefForAnnot(annotDict, doc, reader);
            if (uri is null) continue;
            // A page-menu viewer script becomes a hover drop-up of per-page anchors.
            List<(string Label, string Href)>? popupItems = null;
            if (actionDict?.GetName("S") == "JavaScript"
                && ActionTextValue(actionDict.Get("JS"), reader) is { } rawJs
                && rawJs.Contains("app.popUpMenu"))
                popupItems = ParsePopupMenuItems(rawJs);
            if (reader.Resolve(annotDict.Get("Rect")) is not PdfArray rect || rect.Count < 4) continue;
            var x0 = NumFromObj(rect[0]); var y0 = NumFromObj(rect[1]);
            var x1 = NumFromObj(rect[2]); var y1 = NumFromObj(rect[3]);
            (result ??= new List<LinkTarget>()).Add(new LinkTarget
            {
                Llx = Math.Min(x0, x1), Lly = Math.Min(y0, y1),
                Urx = Math.Max(x0, x1), Ury = Math.Max(y0, y1),
                Uri = uri,
                PopupItems = popupItems,
            });
        }
        return result;
    }

    /// <summary>The link whose rect the run [x..xEnd] substantially overlaps on a
    /// matching baseline: the run covers most of the rect (its own hotspot) or lies
    /// mostly inside it (a word within a row-spanning link). A run with no usable
    /// extent falls back to the origin-in-rect test (small tolerance for hairline
    /// rect/baseline mismatches). Null when nothing qualifies.</summary>
    private static LinkTarget? FindLinkTarget(List<LinkTarget>? targets, double x, double xEnd, double y)
    {
        if (targets is null) return null;
        foreach (var t in targets)
        {
            if (y < t.Lly - 1.0 || y > t.Ury + 1.0) continue;
            if (xEnd > x + 0.01)
            {
                var ov = Math.Min(t.Urx, xEnd) - Math.Max(t.Llx, x);
                if (ov >= 0.5 * (t.Urx - t.Llx) || ov >= 0.5 * (xEnd - x))
                    return t;
            }
            else if (x >= t.Llx - 0.5 && x <= t.Urx + 0.5)
                return t;
        }
        return null;
    }

    private static void RenderLinkAnnotations(PdfDictionary pageDict,
        PdfReader reader, StringBuilder sb, double pageHeight)
    {
        var annotsObj = reader.Resolve(pageDict.Get("Annots"));
        if (annotsObj is not PdfArray annots) return;

        foreach (var annotRef in annots)
        {
            var annotDict = reader.ResolveDict(annotRef);
            if (annotDict is null) continue;

            // Resolve the /A action into an href: web/file URIs, JavaScript, and
            // launch-a-file actions all become real anchors so the link target
            // survives the conversion. Any annotation kind qualifies — a Link, or a
            // push-button Widget wired to e.g. window.print() — as long as it carries
            // such an action.
            var uri = ResolveLinkHref(reader.ResolveDict(annotDict.Get("A")), reader);
            if (uri is null) continue;

            // Get the Rect [llx lly urx ury]
            var rectObj = reader.Resolve(annotDict.Get("Rect"));
            if (rectObj is not PdfArray rect || rect.Count < 4) continue;

            var llx = NumFromObj(rect[0]);
            var lly = NumFromObj(rect[1]);
            var urx = NumFromObj(rect[2]);
            var ury = NumFromObj(rect[3]);

            // Convert to CSS coordinates
            var cssLeft = llx;
            var cssTop = pageHeight - ury;
            var cssWidth = urx - llx;
            var cssHeight = ury - lly;

            // href comes first so consumers scanning for `a href="…"` match.
            sb.Append($"<a href=\"{EscapeHtml(uri)}\" class=\"pdf-link\" ");
            sb.Append($"style=\"position:absolute;left:{F(cssLeft)}pt;top:{F(cssTop)}pt;");
            sb.Append($"width:{F(cssWidth)}pt;height:{F(cssHeight)}pt;display:block;\"");
            sb.AppendLine("></a>");
        }
    }

    // ── SVG path emission ───────────────────────────────────────────────

    /// <summary>Number of colour stops an exported gradient carries: one per whole
    /// percent of the axis, which reproduces a sampled or stitched colour ramp closely
    /// enough that no banding is visible at page scale.</summary>
    private const int SvgGradientStops = 101;

    /// <summary>The pixel density a luminosity soft-mask group is rasterised at —
    /// a mask sidecar's pixel size is exactly its region pt × 200/72.</summary>
    private const double SoftMaskDpi = 200.0;

    /// <summary>Rasterise a luminosity /SMask group to a grayscale PNG covering the
    /// group's BBox (through its /Matrix and the gs-time CTM). White keeps, black
    /// hides — the SVG mask reads the raster's luminance, which is exactly the
    /// per-pixel alpha the renderer derives. Returns null when the group cannot
    /// be resolved or has no area.</summary>
    private static (byte[] Png, int PxW, int PxH, double X0, double Y0, double X1, double Y1)?
        RenderLuminosityMaskPng(PdfReader reader, PdfDictionary smDict, PdfStream gForm, CtmState ctm)
    {
        if (reader.Resolve(gForm.Dict.Get("BBox")) is not PdfArray bb || bb.Count < 4)
            return null;
        double n(int k) => reader.Resolve(bb[k]) is { } bo ? NumFromObj(bo) : 0;
        var fx0 = Math.Min(n(0), n(2));
        var fx1 = Math.Max(n(0), n(2));
        var fy0 = Math.Min(n(1), n(3));
        var fy1 = Math.Max(n(1), n(3));

        // Form /Matrix then the gs-time CTM take the box to page space; the raster
        // covers the transformed corners' bounds.
        var m = ctm.Clone();
        if (reader.Resolve(gForm.Dict.Get("Matrix")) is PdfArray fm && fm.Count >= 6)
        {
            double fmn(int k) => reader.Resolve(fm[k]) is { } fo ? NumFromObj(fo) : 0;
            m.Concat(fmn(0), fmn(1), fmn(2), fmn(3), fmn(4), fmn(5));
        }
        double bx0 = double.MaxValue, by0 = double.MaxValue, bx1 = double.MinValue, by1 = double.MinValue;
        foreach (var (cx, cy) in new[] { (fx0, fy0), (fx1, fy0), (fx0, fy1), (fx1, fy1) })
        {
            var px = m.A * cx + m.C * cy + m.E;
            var py = m.B * cx + m.D * cy + m.F;
            bx0 = Math.Min(bx0, px); bx1 = Math.Max(bx1, px);
            by0 = Math.Min(by0, py); by1 = Math.Max(by1, py);
        }
        var s = SoftMaskDpi / 72.0;
        var pw = (int)Math.Round((bx1 - bx0) * s);
        var ph = (int)Math.Round((by1 - by0) * s);
        if (pw < 1 || ph < 1 || (long)pw * ph > 64_000_000) return null;

        // Device grid over the region, y down; the gs-time CTM rides under it so
        // the group renders exactly where the page would put it.
        var dev = new CtmState { A = s, B = 0, C = 0, D = -s, E = -bx0 * s, F = by1 * s };
        dev.Concat(ctm.A, ctm.B, ctm.C, ctm.D, ctm.E, ctm.F);
        var smInfo = new Content.SoftMaskInfo
        {
            Dict = smDict,
            Subtype = smDict.GetName("S") ?? "Luminosity",
            Ctm = new[] { dev.A, dev.B, dev.C, dev.D, dev.E, dev.F },
        };
        byte[]? alpha;
        try
        {
            alpha = Devices.SoftwarePageRenderer.RenderSoftMaskAlpha(
                reader, pw, ph, s, new Rectangle(bx0, by0, bx1, by1), smInfo);
        }
        catch
        {
            return null;
        }
        if (alpha is null) return null;
        return (IO.PngEncoder.Encode(alpha, pw, ph, colorType: 0), pw, ph, bx0, by0, bx1, by1);
    }

    /// <summary>Paint a shading into the page SVG: a gradient element describing the
    /// colour ramp, and a path filled with it covering the region the shading paints.
    /// The region is the current clip when there is one, otherwise the shading's own
    /// bounding box. Axis endpoints and the box go through <paramref name="dp"/> so the
    /// gradient shares the coordinate space of every other path in the buffer, which is
    /// what lets it be declared with <c>userSpaceOnUse</c> and no transform of its own.
    /// A shading whose colours cannot be evaluated is skipped rather than guessed at.</summary>
    private static void EmitSvgShading(StringBuilder svgPaths, Aspose.Pdf.Shading.ShadingBase? shading,
        string? clipD, Func<double, double, (double x, double y)> dp, int seq, double pageHeight)
    {
        if (shading is null) return;

        // Colour ramp. Both gradient kinds carry a function over the same domain.
        var (fn, domain) = shading switch
        {
            Aspose.Pdf.Shading.AxialShading a => (a.Function, a.Domain),
            Aspose.Pdf.Shading.RadialShading rr => (rr.Function, rr.Domain),
            _ => (null, null),
        };
        if (fn is null) return;
        var lo = domain is { Length: > 0 } ? domain[0] : 0;
        var hi = domain is { Length: > 1 } ? domain[1] : 1;

        var stops = new StringBuilder();
        var input = new double[1];
        for (var i = 0; i < SvgGradientStops; i++)
        {
            var t = i / (double)(SvgGradientStops - 1);
            input[0] = lo + t * (hi - lo);
            var comps = fn.Evaluate(input);
            if (comps is null) return;
            Devices.SoftwarePageRenderer.ComponentsToRgb(comps, shading.ColorSpaceName,
                out var cr, out var cg, out var cb, shading.TintTransform, shading.AltSpaceName);
            stops.Append($"<stop stop-color=\"{FormatHexRgb(cr / 255.0, cg / 255.0, cb / 255.0)}\" " +
                $"offset=\"{i * 100 / (SvgGradientStops - 1)}%\" />");
        }

        var id = $"sh{seq}";
        switch (shading)
        {
            case Aspose.Pdf.Shading.AxialShading ax:
            {
                var (x1, y1) = dp(ax.X0, ax.Y0);
                var (x2, y2) = dp(ax.X1, ax.Y1);
                svgPaths.Append($"<linearGradient id=\"{id}\" gradientUnits=\"userSpaceOnUse\" " +
                    $"x1=\"{F(x1)}\" y1=\"{F(y1)}\" x2=\"{F(x2)}\" y2=\"{F(y2)}\">{stops}</linearGradient>");
                break;
            }
            case Aspose.Pdf.Shading.RadialShading ra:
            {
                var (cx, cy) = dp(ra.X1, ra.Y1);
                var (fx, fy) = dp(ra.X0, ra.Y0);
                // The radius scales with the transform; the axis-average keeps a
                // non-uniform scale from collapsing the circle.
                var (rx, ry) = dp(ra.X1 + ra.R1, ra.Y1);
                var radius = Math.Abs(rx - cx) is var dxr && dxr > 0 ? dxr : Math.Abs(ry - cy);
                svgPaths.Append($"<radialGradient id=\"{id}\" gradientUnits=\"userSpaceOnUse\" " +
                    $"cx=\"{F(cx)}\" cy=\"{F(cy)}\" r=\"{F(radius)}\" fx=\"{F(fx)}\" fy=\"{F(fy)}\">" +
                    $"{stops}</radialGradient>");
                break;
            }
            default:
                return;
        }

        var region = clipD;
        if (string.IsNullOrEmpty(region))
        {
            var box = shading.BBox;
            if (box is not { Length: >= 4 }) return;
            var (bx0, by0) = dp(box[0], box[1]);
            var (bx1, by1) = dp(box[2], box[3]);
            region = $"M{F(bx0)} {F(by0)}L{F(bx1)} {F(by0)}L{F(bx1)} {F(by1)}L{F(bx0)} {F(by1)}Z";
        }
        svgPaths.Append($"<path d=\"{region}\" fill=\"url(#{id})\" stroke=\"none\" />");
    }

    /// <summary>Emit one collected path as an SVG &lt;path&gt; element.
    /// <paramref name="authoredShape"/> selects the INLINE dialect: each element then
    /// carries its own flip matrix (PDF's bottom-left origin to SVG's top-left, in
    /// points, against the truncated page height) and the id of the operator that
    /// opened its construction, with colours as #hex — attribute order id, transform,
    /// d, stroke, stroke-width, stroke-linejoin, fill. A sidecar document keeps the
    /// wrapper-flipped form with rgb() colours.</summary>
    private static void EmitSvgPath(StringBuilder svgPaths, PathState ps,
        bool stroke, bool fill, bool evenOdd = false,
        double pageHeight = 0, int pathId = -1, bool authoredShape = false)
    {
        var d = ps.Data.ToString().Trim();
        if (string.IsNullOrEmpty(d)) return;

        var attrs = new StringBuilder();
        if (authoredShape)
        {
            if (pathId >= 0) attrs.Append($"id=\"{pathId}\" ");
            attrs.Append($"transform=\"matrix(1 0 0 -1 0 {(int)pageHeight})\" ");
        }
        attrs.Append($"d=\"{d}\"");

        if (authoredShape)
        {
            if (stroke)
            {
                attrs.Append($" stroke=\"{FormatHexRgb(ps.StrokeR, ps.StrokeG, ps.StrokeB)}\"");
                attrs.Append($" stroke-width=\"{F(ps.LineWidth)}\"");
                attrs.Append(" stroke-linejoin=\"bevel\"");
            }
            else attrs.Append(" stroke=\"none\"");

            if (fill)
            {
                attrs.Append($" fill=\"{FormatHexRgb(ps.FillR, ps.FillG, ps.FillB)}\"");
                if (evenOdd) attrs.Append(" fill-rule=\"evenodd\"");
            }
            else attrs.Append(" fill=\"none\"");
        }
        else
        {
            if (fill)
            {
                attrs.Append($" fill=\"{FormatRgb(ps.FillR, ps.FillG, ps.FillB)}\"");
                if (evenOdd) attrs.Append(" fill-rule=\"evenodd\"");
            }
            else attrs.Append(" fill=\"none\"");

            if (stroke)
            {
                attrs.Append($" stroke=\"{FormatRgb(ps.StrokeR, ps.StrokeG, ps.StrokeB)}\"");
                if (ps.LineWidth != 1.0)
                    attrs.Append($" stroke-width=\"{F(ps.LineWidth)}\"");
            }
            else attrs.Append(" stroke=\"none\"");
        }

        svgPaths.AppendLine($"<path {attrs} />");
    }

    private static string FormatRgb(double r, double g, double b) =>
        $"rgb({(int)(r * 255)},{(int)(g * 255)},{(int)(b * 255)})";

    private static string FormatHexRgb(double r, double g, double b) =>
        $"#{(int)System.Math.Round(r * 255):X2}{(int)System.Math.Round(g * 255):X2}{(int)System.Math.Round(b * 255):X2}";

    /// <summary>sc/scn operands → RGB by component count (1 gray / 3 RGB / 4 CMYK);
    /// null for pattern names or unexpected shapes.</summary>
    private static (double r, double g, double b)? NumericColor(List<PdfObject> operands)
    {
        var nums = new List<double>(4);
        foreach (var o in operands)
        {
            if (o is PdfInteger pi) nums.Add(pi.Value);
            else if (o is PdfReal pr) nums.Add(pr.Value);
            else return null;
        }
        return nums.Count switch
        {
            1 => (nums[0], nums[0], nums[0]),
            3 => (nums[0], nums[1], nums[2]),
            4 => CmykToRgb(nums[0], nums[1], nums[2], nums[3]),
            _ => null,
        };
    }

    /// <summary>The inline appearance declaration for an stl_ span. A run is described
    /// by its font class unless it is FAUX bold — thickened by a fill-then-stroke
    /// rendering mode — which is weight the font does not carry, so no class can
    /// describe it. Such a run then states its painted appearance in full, slant
    /// included, weight first.</summary>
    private static string StlWeightStyleCss(bool fauxBold, string fontStyle) =>
        !fauxBold ? ""
        : fontStyle != "normal" ? $"font-weight:bold;font-style:{fontStyle};"
        : "font-weight:bold;";

    // ── Text rendering ──────────────────────────────────────────────────

    private static void EmitSpan(StringBuilder sb, string text,
        double x, double y, double fontSize, string fontFamily,
        string fontWeight, string fontStyle,
        double r, double g, double b, double pageHeight, double rise = 0,
        bool transparentText = false, string? rotationClass = null)
    {
        if (string.IsNullOrEmpty(text)) return;

        // Convert PDF coordinates (bottom-left origin) to CSS (top-left origin).
        // A text rise (Ts) shifts the baseline: positive raises, negative lowers.
        var cssTop = pageHeight - y - rise - fontSize;
        var cssLeft = x;

        var color = $"rgb({(int)(r * 255)},{(int)(g * 255)},{(int)(b * 255)})";
        var escaped = EscapeHtml(text);

        // A non-trivial text rise marks a superscript/subscript run — carry the
        // semantics into the markup. The rise is already baked into `top` and the
        // reduced font size is explicit, so the inline style neutralises the
        // browser's default sup/sub shrink-and-shift.
        if (rise > RiseThreshold) escaped = $"<sup style=\"{SupSubStyle}\">{escaped}</sup>";
        else if (rise < -RiseThreshold) escaped = $"<sub style=\"{SupSubStyle}\">{escaped}</sub>";

        var cls = rotationClass is null ? "pdf-text" : $"pdf-text {rotationClass}";
        sb.Append($"<span class=\"{cls}\" style=\"{TextSpanStyle}left:{F(cssLeft)}pt;top:{F(cssTop)}pt;");
        sb.Append($"font-size:{F(fontSize)}pt;font-family:{fontFamily};");
        if (fontWeight != "normal") sb.Append($"font-weight:{fontWeight};");
        if (fontStyle != "normal") sb.Append($"font-style:{fontStyle};");
        sb.Append(transparentText ? "color:transparent;" : $"color:{color};");
        sb.AppendLine($"\">{escaped}</span>");
    }

    /// <summary>True when the content stream contains any operator that would paint
    /// graphics into the SVG backdrop (path paints, shadings, images or XObject
    /// placements). Scans operator tokens with string/hex/name literals skipped so
    /// letters inside show-text strings don't count; a false positive only keeps
    /// the conservative class-numbering base, so residual over-matching is safe.</summary>
    private static bool HasVectorPaintOps(byte[] content)
    {
        static bool IsDelim(byte c) =>
            c is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' or (byte)'\f' or 0
              or (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'['
              or (byte)']' or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%';
        var i = 0;
        var n = content.Length;
        while (i < n)
        {
            var c = content[i];
            if (c == (byte)'(')
            {
                // String literal: skip with nesting and backslash escapes.
                var depth = 1;
                i++;
                while (i < n && depth > 0)
                {
                    if (content[i] == (byte)'\\') i++;
                    else if (content[i] == (byte)'(') depth++;
                    else if (content[i] == (byte)')') depth--;
                    i++;
                }
                continue;
            }
            if (c == (byte)'<')
            {
                if (i + 1 < n && content[i + 1] == (byte)'<') { i += 2; continue; }
                while (i < n && content[i] != (byte)'>') i++;
                i++;
                continue;
            }
            if (c == (byte)'/')
            {
                i++;
                while (i < n && !IsDelim(content[i])) i++;
                continue;
            }
            if (c == (byte)'%')
            {
                while (i < n && content[i] != (byte)'\n') i++;
                continue;
            }
            if (IsDelim(c)) { i++; continue; }

            // Bare token: collect it.
            var start = i;
            while (i < n && !IsDelim(content[i])) i++;
            var len = i - start;
            if (len is < 1 or > 2) continue;
            var t0 = (char)content[start];
            var t1 = len == 2 ? (char)content[start + 1] : '\0';
            switch (len)
            {
                case 1 when t0 is 'S' or 's' or 'f' or 'F' or 'B' or 'b':
                    return true;
                case 2 when (t1 == '*' && t0 is 'f' or 'B' or 'b')
                            || (t0 == 's' && t1 == 'h')
                            || (t0 == 'D' && t1 == 'o')
                            || (t0 == 'B' && t1 == 'I'):
                    return true;
            }
        }
        return false;
    }

    private static string DecodeString(PdfString s, string? fontKey,
        Dictionary<string, FontInfo> fonts)
    {
        string decoded;
        FontInfo? fRec = null;
        if (fontKey is not null) fonts.TryGetValue(fontKey, out fRec);
        if (fRec?.ToUnicode is not null)
        {
            // Use ToUnicode CMap if available
            decoded = fRec.ToUnicode(s.Value);
        }
        else if (fRec?.BaseDecode is not null)
        {
            decoded = fRec.BaseDecode(s.Value);
        }
        else
        {
            // Default: Latin1
            decoded = Encoding.Latin1.GetString(s.Value);
        }
        return NormalizeWhitespace(DecomposeAsciiLigatures(decoded, fRec?.SubsetHas));
    }

    /// <summary>
    /// Expand Latin ligature codepoints whose compatibility decomposition is plain
    /// ASCII (ﬀ ﬁ ﬂ ﬃ ﬄ ﬆ) into their component letters, so the HTML text is
    /// searchable and copy-pasteable ("find: fi" must match a ﬁ-ligature glyph).
    /// U+FB05 (ſt) is left alone — its decomposition contains the non-ASCII long s.
    /// A ligature whose COMPONENT letters the serving face cannot render (a TeX
    /// subset carrying only the ligature glyphs) keeps its codepoint — the served
    /// face renders the ligature glyph itself, decomposition would render nothing.
    /// </summary>
    private static string DecomposeAsciiLigatures(string text, Func<int, bool>? subsetHas = null)
    {
        if (string.IsNullOrEmpty(text)) return text;
        StringBuilder? sb = null;
        for (var i = 0; i < text.Length; i++)
        {
            var repl = text[i] switch
            {
                'ﬀ' => "ff",
                'ﬁ' => "fi",
                'ﬂ' => "fl",
                'ﬃ' => "ffi",
                'ﬄ' => "ffl",
                'ﬆ' => "st",
                _ => null,
            };
            if (repl is not null && subsetHas is not null && subsetHas(text[i]))
            {
                var componentsCovered = true;
                foreach (var c in repl)
                    if (!subsetHas(c)) { componentsCovered = false; break; }
                if (!componentsCovered) repl = null;
            }
            if (repl is null) { sb?.Append(text[i]); continue; }
            sb ??= new StringBuilder(text, 0, i, text.Length + 8);
            sb.Append(repl);
        }
        return sb is null ? text : sb.ToString();
    }

    /// <summary>
    /// Some PDFs map the inter-word space glyph to a C0 control character
    /// (most commonly a horizontal tab, U+0009) in their ToUnicode CMap.
    /// For displayed text these are word separators, so fold them to a normal
    /// space — matching the text content produced by the reference converter.
    /// </summary>
    private static string NormalizeWhitespace(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        char[]? buffer = null;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            // Fold C0 control characters (except line breaks) to a space.
            if (c < ' ' && c != '\n' && c != '\r')
            {
                buffer ??= text.ToCharArray();
                buffer[i] = ' ';
            }
        }
        return buffer is null ? text : new string(buffer);
    }
}
