using System.Globalization;
using System.Text;
using System.Xml;

namespace Aspose.Pdf.Forms.Xfa;

internal static partial class XfaRenderer
{
    private static void PaintContainerFill(Ctx ctx, XmlElement e, double x, double y, double w, double h)
    {
        var fill = FillColor(e);
        if (fill is not null)
            ctx.Items.Add(new Item { Kind = "fill", X = x, Y = ctx.PageH - (y + h), W = w, H = h, Color = fill });
        // A container border strokes its VISIBLE edges individually (XFA edge order
        // top, right, bottom, left; a single <edge> applies to all four sides).
        // Painting a full box off the first edge alone both drew hidden sides
        // (a bottom-hidden box gained a phantom rule at its floor)
        // and dropped borders whose FIRST edge is hidden (such a box's
        // bottom-only rule under the address never painted).
        var border = FirstChild(e, "border");
        if (border is not null && border.GetAttribute("presence") is not ("hidden" or "invisible")
            && w > 0 && h > 0)
        {
            var edges = border.ChildNodes.OfType<XmlElement>()
                .Where(c => c.LocalName == "edge").ToList();
            static bool EdgeVis(XmlElement ed) =>
                ed.GetAttribute("presence") is not ("hidden" or "invisible");
            if (edges.Count == 1)
            {
                if (EdgeVis(edges[0]))
                    ctx.Items.Add(new Item { Kind = "box", X = x, Y = ctx.PageH - (y + h), W = w, H = h });
            }
            else if (edges.Count > 1)
            {
                var vis = new bool[4];
                for (var i = 0; i < 4; i++) vis[i] = i < edges.Count && EdgeVis(edges[i]);
                if (vis[0] && vis[1] && vis[2] && vis[3])
                    ctx.Items.Add(new Item { Kind = "box", X = x, Y = ctx.PageH - (y + h), W = w, H = h });
                else
                {
                    const double th = 0.66;
                    double[] black = { 0, 0, 0 };
                    if (vis[0]) ctx.Items.Add(new Item { Kind = "fill", X = x, Y = ctx.PageH - y - th, W = w, H = th, Color = black });
                    if (vis[1]) ctx.Items.Add(new Item { Kind = "fill", X = x + w - th, Y = ctx.PageH - (y + h), W = th, H = h, Color = black });
                    if (vis[2]) ctx.Items.Add(new Item { Kind = "fill", X = x, Y = ctx.PageH - (y + h), W = w, H = th, Color = black });
                    if (vis[3]) ctx.Items.Add(new Item { Kind = "fill", X = x, Y = ctx.PageH - (y + h), W = th, H = h, Color = black });
                }
            }
        }
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
                // A line with BOTH extents is a DIAGONAL: slope "/" rises from the
                // box's bottom-left, the default falls from its top-left.
                if (w > 0.01 && h > 0.01)
                    ctx.Items.Add(new Item { Kind = "diag", X = x, Y = ctx.PageH - (y + h), W = w, H = h, Color = lc,
                                             Stretch = lineShape.GetAttribute("slope") == "/", FontSize = th });
                else if (w >= h)
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
            // An image field: datasets-bound base64 first (the SOM resolver reaches
            // dataRef-bound nodes the name walk cannot — a signature/seal image bound
            // via <bind match="dataRef">), else the template image.
            // NEVER paint image data as text.
            byte[]? data = null;
            var bound = BoundValue(ctx, e, e.GetAttribute("name"));
            if (string.IsNullOrWhiteSpace(bound)) bound = ctx.RawValue?.Invoke(path);
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
            {
                var leaves = ctx.DataRoot.SelectNodes(".//*")!.OfType<XmlElement>()
                    .Where(d => d.LocalName == groupName && !d.ChildNodes.OfType<XmlElement>().Any())
                    .ToList();
                if (leaves.Count == 1 || (leaves.Count > 1 && !ctx.StrictBinding))
                    bound = leaves[0].InnerText;
            }
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

    /// <summary>Apply the field's picture clause to a default value. Handles the
    /// Designer currency pattern <c>num{($z,zzz,zz9.99)}</c> (3000 → $3,000.00) and
    /// the COMPOUND form with quoted literal affixes —
    /// <c>null{'Federal Tax @ 0.00%'}|'Federal Tax @ 'z9.99'%'</c> renders a null
    /// value as the first alternative's literal and 5 as "Federal Tax @ 5.00%".
    /// Unknown pictures leave the value unchanged.</summary>
    private static string ApplyPicture(XmlElement e, string val)
    {
        var picture = FirstChild(e, "format") is { } f ? InnerText(f, "picture") : "";
        if (picture.Length == 0) return val;

        // Split top-level alternatives on '|' (quotes and braces protect it).
        var alternatives = SplitPictureAlternatives(picture);
        var isNull = string.IsNullOrWhiteSpace(val);
        string? numericMask = null;
        foreach (var alt in alternatives)
        {
            if (alt.StartsWith("null{", StringComparison.Ordinal))
            {
                if (isNull) return StripPictureQuotes(alt[5..].TrimEnd('}'));
                continue;
            }
            numericMask ??= alt.StartsWith("num{", StringComparison.Ordinal)
                ? alt[4..].TrimEnd('}')
                : alt;
        }
        if (numericMask is null || isNull) return val;
        if (!double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var n)) return val;

        // Walk the mask: quoted runs are literals; z/Z/9 with ',' and '.' form the
        // numeric core; '$' is a currency literal; parens are the XFA negative
        // indicator and are dropped for non-negative values.
        var prefix = new System.Text.StringBuilder();
        var suffix = new System.Text.StringBuilder();
        var coreSeen = false;
        int intDigits = 0, decDigits = 0;
        var grouped = false;
        var afterPoint = false;
        for (var i = 0; i < numericMask.Length; i++)
        {
            var c = numericMask[i];
            if (c == '\'')
            {
                var end = numericMask.IndexOf('\'', i + 1);
                if (end < 0) end = numericMask.Length;
                (coreSeen ? suffix : prefix).Append(numericMask, i + 1, end - i - 1);
                i = end;
            }
            else if (c is 'z' or 'Z' or '9' or 's' or 'S')
            {
                coreSeen = true;
                if (afterPoint) decDigits++; else intDigits++;
            }
            else if (c == '.' && coreSeen) afterPoint = true;
            else if (c == ',' && !afterPoint) grouped = true;
            else if (c == '$') (coreSeen ? suffix : prefix).Append('$');
            else if (c is '(' or ')')
            {
                if (n < 0) (coreSeen ? suffix : prefix).Append(c);
            }
            else if (c == '%') (coreSeen ? suffix : prefix).Append('%');
        }
        if (!coreSeen) return val;
        var fmt = (grouped ? "#,##0" : "0") + (decDigits > 0 ? "." + new string('0', decDigits) : "");
        return prefix.ToString() + Math.Abs(n).ToString(fmt, CultureInfo.InvariantCulture)
            .Insert(0, n < 0 && !numericMask.Contains('(') ? "-" : "") + suffix;
    }

    private static List<string> SplitPictureAlternatives(string picture)
    {
        var parts = new List<string>();
        var depth = 0; var inQuote = false; var start = 0;
        for (var i = 0; i < picture.Length; i++)
        {
            var c = picture[i];
            if (c == '\'') inQuote = !inQuote;
            else if (!inQuote && c == '{') depth++;
            else if (!inQuote && c == '}') depth--;
            else if (!inQuote && depth == 0 && c == '|')
            {
                parts.Add(picture[start..i].Trim());
                start = i + 1;
            }
        }
        parts.Add(picture[start..].Trim());
        return parts;
    }

    private static string StripPictureQuotes(string s)
    {
        var sb = new System.Text.StringBuilder();
        var inQuote = false;
        foreach (var c in s)
        {
            if (c == '\'') { inQuote = !inQuote; continue; }
            if (inQuote) sb.Append(c);
        }
        // an unquoted null-picture body is taken verbatim
        return sb.Length > 0 ? sb.ToString() : s;
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
}
