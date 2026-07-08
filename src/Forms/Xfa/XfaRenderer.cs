using System.Globalization;
using System.Text;
using System.Xml;

namespace Aspose.Pdf.Forms.Xfa;

/// <summary>
/// Renders a dynamic-XFA form's content onto real PDF pages when it is flattened to a static
/// document (so a subsequent raster render shows the form, not the XFA "requires Adobe Reader"
/// fallback page). It runs a coarse XFA flow layout (positions each draw and field box) and paints
/// text / fill boxes / lines with the standard Helvetica family (Arial-compatible). It targets a
/// visual match within the test-suite's tolerant image comparison (≈3px neighbourhood), not
/// pixel-perfection. Fully tolerant: any failure leaves the flatten untouched.
/// </summary>
internal static class XfaRenderer
{
    private const double Mm = 72.0 / 25.4;

    // A positioned, ready-to-paint primitive (coordinates already in PDF points, bottom-left origin).
    private sealed class Item
    {
        public string Kind = "";                  // text / fill / line / rect
        public double X, Y, W, H;                 // rect/line geometry (PDF pts)
        public string Text = "";
        public double FontSize = 8;
        public bool Bold;
        public double[] Color = { 0, 0, 0 };       // rgb 0..1
        public string Align = "left";             // text horizontal alignment within W
    }

    /// <summary>Paint the flattened dynamic-XFA form onto fresh pages of <paramref name="doc"/>,
    /// replacing the existing (fallback) pages. No-op on any failure.</summary>
    internal static void Render(Document doc, XmlElement template, Func<string, string?>? rawValue)
    {
        try { RenderInternal(doc, template, rawValue); }
        catch { /* never break flatten */ }
    }

    private static void RenderInternal(Document doc, XmlElement template, Func<string, string?>? rawValue)
    {
        var root = FirstChild(template, "subform");
        if (root is null) return;

        // Body subforms each flow onto a master pageArea (matched by name, else the single/first
        // master). Every visible top-level content subform under the form root is a body — not just
        // the "Page*"-named ones — so arbitrarily-structured forms (an invoice's CustomerDetails /
        // Orders, a purchaseOrder, a budget subform) render rather than falling back to the Adobe
        // placeholder page.
        var bodies = Children(root, "subform").Where(s => !Hidden(s)).ToList();
        var pageAreas = Descendants(template, "pageArea").ToList();
        if (bodies.Count == 0 || pageAreas.Count == 0) return;

        var newPages = new List<(double w, double h, List<Item> items)>();
        foreach (var body in bodies)
        {
            var master = pageAreas.FirstOrDefault(p => p.GetAttribute("name") == body.GetAttribute("name"))
                         ?? pageAreas.FirstOrDefault();
            if (master is null) continue;
            var ca = FirstChild(master, "contentArea");
            double pw = 612, ph = 792;
            var medium = FirstChild(master, "medium");
            if (medium is not null) { pw = Len(medium.GetAttribute("short"), 612); ph = Len(medium.GetAttribute("long"), 792); }
            double cax = ca is null ? 0 : Len(ca.GetAttribute("x"), 0);
            double cay = ca is null ? 0 : Len(ca.GetAttribute("y"), 0);

            var items = new List<Item>();
            var ctx = new Ctx { PageH = ph, RawValue = rawValue, Items = items };
            // Master content (footer, page-number fields) is positioned in page coordinates.
            foreach (var c in Boxes(master)) Place(ctx, c, Len(c.GetAttribute("x"), 0), Len(c.GetAttribute("y"), 0), "");
            // Body content flows through the content area (SOM path rooted at the form root).
            Place(ctx, body, cax, cay, root.GetAttribute("name") + "[0]");
            newPages.Add((pw, ph, items));
        }
        if (newPages.Count == 0) return;

        // Replace all existing pages with the rendered ones (bounded to avoid any delete-loop hang).
        for (int guard = doc.Pages.Count + 8; doc.Pages.Count > 0 && guard > 0; guard--)
            doc.Pages.Delete(1);
        foreach (var (w, h, items) in newPages)
        {
            var page = doc.Pages.Add(w, h);
            EnsureFonts(page);
            page.AddContentStream(Emit(items));
        }
    }

    // ------------------------------------------------------------------ layout

    private sealed class Ctx
    {
        public double PageH;
        public Func<string, string?>? RawValue;
        public List<Item> Items = new();
    }

    private static readonly string[] BoxTags = { "subform", "subformSet", "area", "field", "draw", "exclGroup" };
    private static IEnumerable<XmlElement> Boxes(XmlElement e) =>
        e.ChildNodes.OfType<XmlElement>().Where(c => BoxTags.Contains(c.LocalName) && !Hidden(c));

    private static bool Hidden(XmlElement e) => e.GetAttribute("presence") is "hidden" or "invisible" or "inactive";

    /// <summary>A decorative draw carrying an explicit y inside a flow container is a positioned
    /// overlay (a spacer/line) and does not consume flow height. Layout subforms flow even with an
    /// x (a horizontal column indent), so they are not treated as floating.</summary>
    private static bool IsFloating(XmlElement e) =>
        e.LocalName == "draw" && e.GetAttribute("y").Length > 0;

    /// <summary>Lay out a container's children starting at top-left (x,y) in top-down coordinates.</summary>
    private static void Layout(Ctx ctx, XmlElement e, double x, double y, string path)
    {
        var (mt, mb, ml, mr) = Margins(e);
        double cx = x + ml, cy = y + mt;
        var layout = e.GetAttribute("layout");
        if (layout is "" ) layout = "position";
        var kids = Boxes(e).ToList();
        switch (layout)
        {
            case "tb":
            case "table":
                {
                    double yy = cy;
                    foreach (var c in kids)
                    {
                        if (IsFloating(c))   // explicit x/y in a flow → positioned overlay, no flow advance
                        { Place(ctx, c, cx + Len(c.GetAttribute("x"), 0), yy + Len(c.GetAttribute("y"), 0), path); continue; }
                        yy += Place(ctx, c, cx, yy, path);
                    }
                    break;
                }
            case "lr-tb":
            case "rl-tb":
                {
                    double W = BoxW(e), xx = cx, yy = cy, rowh = 0;
                    foreach (var c in kids)
                    {
                        double cw = BoxW(c);
                        if (xx + cw > cx + W + 0.5 && xx > cx) { yy += rowh; xx = cx; rowh = 0; }
                        Place(ctx, c, xx, yy, path); xx += cw; rowh = Math.Max(rowh, Height(c));
                    }
                    break;
                }
            case "row":
                { double xx = cx; foreach (var c in kids) { Place(ctx, c, xx, cy, path); xx += BoxW(c); } break; }
            default: // positioned
                foreach (var c in kids) Place(ctx, c, cx + Len(c.GetAttribute("x"), 0), cy + Len(c.GetAttribute("y"), 0), path);
                break;
        }
    }

    /// <summary>Place one box at top-left (x,y); paint if it is a leaf; recurse if a container.
    /// Returns its laid-out height.</summary>
    private static double Place(Ctx ctx, XmlElement e, double x, double y, string path)
    {
        if (Hidden(e)) return 0;
        double h = Height(e), w = BoxW(e);
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
                Layout(ctx, e, x, y, p2); return h;
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

    private static double Height(XmlElement e)
    {
        if (Hidden(e)) return 0;
        double? h = LenN(e.GetAttribute("h")), minH = LenN(e.GetAttribute("minH"));
        if (e.LocalName is "field" or "draw")
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
        return ContentHeight(e);
    }

    private static double ContentHeight(XmlElement e)
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
                    double flow = kids.Where(c => !IsFloating(c)).Sum(Height);
                    double flt = kids.Where(IsFloating).Select(c => Len(c.GetAttribute("y"), 0) + Height(c)).DefaultIfEmpty(0).Max();
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
                        xx += cw; rowh = Math.Max(rowh, Height(c));
                    }
                    return mt + mb + tot + rowh;
                }
            case "row":
                return mt + mb + kids.Max(Height);
            default:
                return mt + mb + kids.Max(c => Len(c.GetAttribute("y"), 0) + Height(c));
        }
    }

    private static double BoxW(XmlElement e)
    {
        double? w = LenN(e.GetAttribute("w"));
        if (w is not null) return w.Value;
        // A table cell with no explicit width takes its column's width from the table's columnWidths.
        var col = ColumnWidth(e);
        if (col is not null) return col.Value;
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
    }

    private static void PaintDraw(Ctx ctx, XmlElement e, double x, double y, double w, double h)
    {
        var fill = FillColor(e);
        if (fill is not null)
            ctx.Items.Add(new Item { Kind = "fill", X = x, Y = ctx.PageH - (y + h), W = w, H = h, Color = fill });
        var text = InnerText(e, "value");
        if (!string.IsNullOrWhiteSpace(text))
            AddText(ctx, e, x, y, w, h, text.Trim());
    }

    private static readonly bool DumpPos = System.Environment.GetEnvironmentVariable("XFA_DUMP") is not null;

    private static void PaintField(Ctx ctx, XmlElement e, double x, double y, double w, double h, string path)
    {
        if (DumpPos) System.Console.Error.WriteLine($"FIELDPOS\t{path}\t{x:F1}\t{ctx.PageH - y:F1}");
        var (res, plc) = Caption(e);
        bool check = IsCheckbox(e), radio = e.LocalName == "exclGroup" || Ui(e) == "radio";

        if (check || radio)
        {
            // draw the box/circle only (a few pt square at the edit region)
            double bs = Math.Min(9, Math.Min(w, h));
            double bx = x, by = ctx.PageH - (y + bs);
            ctx.Items.Add(new Item { Kind = "box", X = bx, Y = by, W = bs, H = bs });
        }
        else
        {
            // datasets-bound value if present, else the template default value
            var val = ctx.RawValue?.Invoke(path);
            if (string.IsNullOrEmpty(val)) val = InnerText(e, "value");
            if (!string.IsNullOrWhiteSpace(val))
            {
                double editH = plc is "top" or "bottom" ? h - res : h;
                double editY = plc == "top" ? y + res : y;
                AddText(ctx, e, x, editY, w, editH, val.Trim());
            }
            // text-field baseline underline
            ctx.Items.Add(new Item { Kind = "line", X = x, Y = ctx.PageH - (y + (plc == "bottom" ? h - res : h)), W = w, H = 0 });
        }

        // caption label
        var cap = InnerText(e, "caption");
        if (!string.IsNullOrWhiteSpace(cap))
        {
            double capX = x + ((check || radio) ? Math.Min(9, Math.Min(w, h)) + 3 : 0);
            double capY = plc == "bottom" ? y + h - res : y;
            AddText(ctx, e, capX, capY, w, res > 0 ? res : h, cap.Trim());
        }
    }

    private static string? Ui(XmlElement e)
    {
        var ui = FirstChild(e, "ui");
        return ui?.ChildNodes.OfType<XmlElement>().FirstOrDefault()?.LocalName;
    }
    private static bool IsCheckbox(XmlElement e) => Ui(e) == "checkButton";

    private static void AddText(Ctx ctx, XmlElement e, double x, double ytop, double w, double h, string text, bool capOverride = false)
    {
        double fs = FontSize(e);
        var (mt, mb, ml, mr) = Margins(e);
        bool bold = FontBold(e);
        var color = FontColor(e);
        double avail = Math.Max(4, w - ml - mr);
        double lineH = fs * 1.15;
        double topY = ytop + mt;
        int li = 0;
        // Unicode line/paragraph separators are hard line breaks (a double U+2029 = a blank line).
        text = text.Replace((char)0x2028, (char)0x0A).Replace((char)0x2029, (char)0x0A).Replace((char)0x0D, (char)0x0A);
        foreach (var para in text.Split('\n'))
            foreach (var line in WrapLine(para, avail, fs, bold))
            {
                double baseline = ctx.PageH - (topY + fs + li * lineH);
                if (line.Length > 0)
                    ctx.Items.Add(new Item { Kind = "text", X = x + ml, Y = baseline, W = w, Text = line, FontSize = fs, Bold = bold, Color = color });
                li++;
            }
    }

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
    private static double TextWidth(string s, double fs, bool bold)
    {
        var t = bold ? HelvBoldW : HelvW;
        double w = 0;
        foreach (var ch in s) w += ch is >= (char)32 and <= (char)126 ? t[ch - 32] : 556;
        return w * fs / 1000.0;
    }

    // ------------------------------------------------------------------ emit

    private static byte[] Emit(List<Item> items)
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
            else if (it.Kind == "text")
            {
                b.SaveState().SetFillColor(it.Color[0], it.Color[1], it.Color[2]);
                b.BeginText();
                b.SetFont(it.Bold ? "F2" : "F1", it.FontSize);
                b.MoveTextPosition(it.X, it.Y);
                b.ShowText(ToWinAnsi(it.Text));
                b.EndText();
                b.RestoreState();
            }
        }
        return b.Build();
    }

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
        var f = FirstChild(e, "font");
        return f is null ? 8 : Len(f.GetAttribute("size"), 8);
    }
    private static bool FontBold(XmlElement e) => FirstChild(e, "font")?.GetAttribute("weight") == "bold";
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
        return string.Concat(c.SelectNodes(".//*[local-name()='text']")!.OfType<XmlNode>().Select(n => n.InnerText));
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
