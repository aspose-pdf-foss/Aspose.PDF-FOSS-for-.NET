using System.Globalization;
using System.Text;
using System.Xml;

namespace Aspose.Pdf.Forms.Xfa;

internal static partial class XfaRenderer
{
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
                        // A field's flow advance uses its AUTO width (minW + caption
                        // reserve) — the same width Place paints it at.
                        double cw = FlowWidth(ctx, c);
                        if (xx + cw > cx + W + 0.5 && xx > cx) { yy += rowh; xx = cx; rowh = 0; }
                        double cav = W - (xx - cx);
                        Place(ctx, c, xx, yy, path, cav); xx += cw; rowh = Math.Max(rowh, Height(ctx, c, cav));
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
                {
                    // A positioned container's <para hAlign="center"> centres its content
                    // BLOCK in the container width - a radio exclGroup
                    // inside its table cell centres this way (the group's
                    // captions sit 40 pt right of the cell start; left-anchoring them is
                    // a visible column shift). The block extent is the positioned
                    // children's rightmost edge at their natural widths.
                    double alignShift = 0;
                    var hA = FirstChild(e, "para")?.GetAttribute("hAlign");
                    if (hA is "center" or "right")
                    {
                        double extent = 0;
                        foreach (var c in kids)
                            extent = Math.Max(extent, Len(c.GetAttribute("x"), 0) + NaturalW(c));
                        var slack = W - extent;
                        if (slack > 0 && extent > 0) alignShift = hA == "center" ? slack / 2 : slack;
                        if (alignShift > 0 && System.Environment.GetEnvironmentVariable("XFA_ALIGN") is not null)
                            System.Console.Error.WriteLine($"ALIGN	{e.GetAttribute("name")}	W={W:F1}	extent={extent:F1}	shift={alignShift:F1}");
                    }
                    foreach (var c in kids)
                    {
                        var xoff = alignShift + Len(c.GetAttribute("x"), 0);
                        Place(ctx, c, cx + xoff, cy + Len(c.GetAttribute("y"), 0), path, W - xoff);
                    }
                }
                break;
        }
    }

    /// <summary>Place one box at top-left (x,y); paint if it is a leaf; recurse if a container.
    /// Returns its laid-out height.</summary>
    private static double Place(Ctx ctx, XmlElement e, double x, double y, string path, double availW = 0)
    {
        if (Hidden(e)) return 0;
        // A field's auto width follows the probed minW + caption-reserve law; other
        // boxes keep the declared/derived width.
        double w = e.LocalName == "field" ? FlowWidth(ctx, e) : BoxW(e);
        // A leaf without an explicit width spans its container's remaining width (a
        // widthless master text draw otherwise wraps one word per line) — EXCEPT a
        // vector line draw, whose w=0 means a VERTICAL rule (a form's
        // banner draws its warning triangles from such lines; inflating one paints
        // a strike through the caption).
        var isLineDraw = e.LocalName == "draw" && FirstChild(e, "value") is { } lv
            && lv.ChildNodes.OfType<XmlElement>().Any(c => c.LocalName == "line");
        if (w <= 0 && !isLineDraw) w = Math.Max(4, availW);
        double h = Height(ctx, e, w);
        // anchorType: a positioned element's (x,y) names the given corner/edge of its
        // box, not the top-left (a topRight-anchored title sits to the LEFT of its x).
        // A FLOWED parent (tb/lr-tb/table) ignores its children's x/y entirely, so
        // the anchor must not shift a flowed child either (an anchored field —
        // topCenter, x=2.8125in inside a tb subform — was sliding half its width
        // off the page's left edge).
        var parentLayout = (e.ParentNode as XmlElement)?.GetAttribute("layout") ?? "";
        var parentFlows = parentLayout is "tb" or "lr-tb" or "rl-tb" or "table" or "row";
        if (!parentFlows && (e.GetAttribute("x").Length > 0 || e.GetAttribute("y").Length > 0))
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

    /// <summary>A box's laid-out width: its declared w, else the extent of its
    /// positioned children (an exclGroup with no w spans to its last option's
    /// right edge, caption reserve included).</summary>
    private static double NaturalW(XmlElement e)
    {
        // Only a DECLARED w is authoritative; BoxW's kids-max fallback loses the
        // positioned children's x offsets (an exclGroup would measure as wide as
        // its widest option instead of spanning to the last option's right edge).
        if (LenN(e.GetAttribute("w")) is { } w) return w;
        double extent = 0;
        foreach (var c in Boxes(e))
            extent = Math.Max(extent, Len(c.GetAttribute("x"), 0) + NaturalW(c));
        return extent > 0 ? extent : BoxW(e);
    }

    private static int SiblingIndex(XmlElement e)
    {
        int idx = 0;
        for (var s = e.PreviousSibling; s is not null; s = s.PreviousSibling)
            if (s is XmlElement se && se.LocalName == e.LocalName && se.GetAttribute("name") == e.GetAttribute("name")) idx++;
        return idx;
    }

    private static double Height(Ctx ctx, XmlElement e, double outerW = 0)
    {
        if (Hidden(e)) return 0;
        double? h = LenN(e.GetAttribute("h")), minH = LenN(e.GetAttribute("minH"));
        if (e.LocalName == "field") return FieldHeight(ctx, e, h, minH);
        if (e.LocalName == "draw") return DrawHeight(ctx, e, outerW, h, minH);
        if (h is not null) return h.Value;   // fixed height clamps
        // container without explicit h: ignore minH (use content height)
        return ContentHeight(ctx, e, outerW);
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

    /// <summary>The element's dotted SOM path (name[idx] per ancestor), for the
    /// measure-time SOM value resolution below.</summary>
    private static string SomPathOf(XmlElement e)
    {
        var parts = new System.Collections.Generic.List<string>();
        for (XmlElement? a = e; a is not null; a = a.ParentNode as XmlElement)
        {
            if (a.LocalName is not ("subform" or "field" or "draw" or "exclGroup" or "area")) break;
            var n = a.GetAttribute("name");
            if (n.Length > 0) parts.Add($"{n}[{SiblingIndex(a)}]");
        }
        parts.Reverse();
        return string.Join(".", parts);
    }

    private static string? FieldValueText(Ctx ctx, XmlElement e)
    {
        string? val = null;
        if (!BindsNoData(e))
        {
            val = BoundValue(ctx, e, e.GetAttribute("name"));
            // The SOM resolver reaches dataRef-bound values the name walk cannot —
            // without it a bound field MEASURES as empty and loses the value pads
            // its painted height carries (a bound row: 14.7 empty vs the
            // 23.6 with its value).
            if (val is null && ctx.RawValue is not null)
                val = ctx.RawValue(SomPathOf(e));
            if (val is null && ctx.DataRoot is not null)
            {
                // Non-repeated fields carry no data-idx scope (only occur-expanded
                // instances do) — resolve the value leaf straight from the datasets
                // tree by name, the same way rich-text embeds do. A bound-but-EMPTY
                // value must stay empty (same rule as the paint pass), so this only
                // fires when no binding resolved at all.
                var name = e.GetAttribute("name");
                if (name.Length > 0)
                {
                    var leaves = ctx.DataRoot.SelectNodes(".//*")!.OfType<XmlElement>()
                        .Where(d => d.LocalName == name && !d.ChildNodes.OfType<XmlElement>().Any())
                        .ToList();
                    if (leaves.Count == 1 || (leaves.Count > 1 && !ctx.StrictBinding))
                        val = leaves[0].InnerText;
                }
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

    private static double ContentHeight(Ctx ctx, XmlElement e, double availW = 0)
    {
        var (mt, mb, ml, mr) = Margins(e);
        var layout = e.GetAttribute("layout"); if (layout is "") layout = "position";
        var kids = Boxes(e).ToList();
        if (kids.Count == 0) return mt + mb;
        // The width the kids lay out in — the container's own, else the caller's.
        double innerW = Math.Max(0, (BoxW(e) > 0 ? BoxW(e) : availW) - ml - mr);
        switch (layout)
        {
            case "tb":
            case "table":
                {
                    // Flowed children stack; floating (x/y-positioned) children extend by their bottom.
                    double flow = kids.Where(c => !IsFloating(c)).Sum(c => Height(ctx, c, innerW));
                    double flt = kids.Where(IsFloating).Select(c => Len(c.GetAttribute("y"), 0) + Height(ctx, c, innerW)).DefaultIfEmpty(0).Max();
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
                        xx += cw; rowh = Math.Max(rowh, Height(ctx, c, innerW));
                    }
                    return mt + mb + tot + rowh;
                }
            case "row":
                return mt + mb + kids.Max(c => Height(ctx, c, innerW));
            default:
                return mt + mb + kids.Max(c => Len(c.GetAttribute("y"), 0) + Height(ctx, c, innerW));
        }
    }

    /// <summary>Effective flow width of a child: a FIELD with no explicit w sizes to
    /// max(minW, its value's one-line width + h-insets) plus a left/right caption's
    /// reserve — probed on a caption row, where a field (minW 38.942mm, reserve
    /// 34.8111mm) measures exactly minW + reserve, and an uncaptioned auto field
    /// (JudicialOfficerType) grows to hold "Clerk of the Court" un-wrapped.</summary>
    private static double FlowWidth(Ctx ctx, XmlElement e)
    {
        if (e.LocalName != "field") return BoxW(e);
        if (LenN(e.GetAttribute("w")) is not null || ColumnWidth(e) is not null) return BoxW(e);
        if (LenN(e.GetAttribute("minW")) is not { } minW) return BoxW(e);
        var (res, plc) = Caption(e);
        double reserve = 0;
        if (plc is "left" or "right" or "")
        {
            var cap = InnerText(e, "caption");
            var (cfs, cbold) = CaptionFont(e);
            reserve = res > 0 ? res
                : (string.IsNullOrWhiteSpace(cap) ? 0 : TextWidth(cap!.Trim(), cfs, cbold) + 4);
        }
        double content = 0;
        var val = FieldValueText(ctx, e);
        if (!string.IsNullOrEmpty(val))
        {
            var fs = FontSize(e);
            var (_, _, ml, mr) = Margins(e);
            foreach (var line in val!.Split('\n'))
            {
                var lw = TextWidth(line.TrimEnd(), fs, false) + ml + mr;
                if (lw > content) content = lw;
            }
        }
        return Math.Max(minW, content) + reserve;
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
}
