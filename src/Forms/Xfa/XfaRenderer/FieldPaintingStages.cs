using System.Globalization;
using System.Text;
using System.Xml;

namespace Aspose.Pdf.Forms.Xfa;

internal static partial class XfaRenderer
{
    /// <summary>Paints the value text (a middle-aligned value under a top caption centres in the whole field box) and the caption label beside, above or below the edit region.</summary>
    private static void PaintFieldText(PaintFieldState pf)
    {
        if (!string.IsNullOrWhiteSpace(pf.val))
        {
            // A middle-aligned value under a TOP caption centres in the whole field box
            // (the caption strip merely floors it) — centring in the reduced edit region
            // sits a caption-height too low against a viewer's output.
            var textH = ValueBlockHeight(pf);
            double vy = pf.plc == "top"
                ? pf.y + Math.Max(pf.capH, (pf.h - textH) / 2)
                : pf.ey + Math.Max(0, (pf.eh - textH) / 2);
            // A master page field's value is a widget to a viewer, over the whole page: it is painted last.
            // A turned field keeps its value with its box: the turn is applied to what the box painted.
            var into = pf.ctx.Items;
            if (pf.ctx.ValueItems is { } last && !pf.ctx.Turning) pf.ctx.Items = last;
            int first = pf.ctx.Items.Count;
            AddText(pf.ctx, pf.e, pf.ex + 1.5, vy, pf.ew - 3, pf.val.Trim(), singleLine: SingleLineValue(pf.e));
            pf.valueItems = pf.ctx.Items.GetRange(first, pf.ctx.Items.Count - first);
            pf.ctx.Items = into;
        }

        // caption label (vertically centred beside the edit region for left/right)
        if (!string.IsNullOrWhiteSpace(pf.cap))
        {
            double capX = pf.plc == "right" ? pf.x + pf.w - pf.capW : pf.x;
            // A side caption is centred beside the edit region as the block of its lines.
            double capY = pf.plc switch
            {
                "bottom" => pf.y + pf.h - pf.capH,
                _ => pf.y,
            };
            var (_, _, ml, mr) = pf.widgetMargins;
            bool side = pf.plc is not ("top" or "bottom");
            if (side && pf.capW > 0 ? PaintRichCaption(pf, capX + ml, capX + pf.capW, pf.y, pf.h)
                : PaintRichCaption(pf, pf.x + ml, pf.x + pf.w - mr, capY, side ? pf.h : pf.capH)) return;
            AddText(pf.ctx, pf.e, capX, capY, pf.capW > 0 ? pf.capW : pf.w, pf.cap.Trim(),
                fsOverride: pf.capFs, boldOverride: pf.capBold, alignSource: FirstChild(pf.e, "caption"),
                ownRegion: pf.capW > 0, middleOf: pf.plc is "top" or "bottom" ? 0 : pf.h);
        }
    }

    /// <summary>A caption given as rich text is laid out as a rich text draw is - its spans' sizes and weights, its
    /// line breaks - in the strip from <paramref name="left"/> to <paramref name="right"/> of the box from
    /// <paramref name="top"/> down <paramref name="height"/>; false when the caption is plain text.</summary>
    private static bool PaintRichCaption(PaintFieldState pf, double left, double right, double top, double height)
    {
        var caption = FirstChild(pf.e, "caption");
        var body = caption is not null && FirstChild(caption, "value") is { } value ? Descendants(value, "exData").FirstOrDefault() : null;
        if (body is null || body.SelectNodes(".//*[local-name()='p']")!.Count == 0 || !StylesItsRuns(body)) return false;
        var (_, _, ml, mr) = Margins(pf.e);
        AddRichText(pf.ctx, pf.e, left - ml, top, right - left + ml + mr, height, body, caption);
        return true;
    }

    /// <summary>True when rich text carries what its plain text loses: a line break, an emphasis element, or a
    /// style that sizes, weights, slants or names the face of its runs. Rich text that only wraps plain words in
    /// paragraphs keeps the caption's plain layout.</summary>
    private static bool StylesItsRuns(XmlElement body) =>
        body.SelectNodes(".//*")!.OfType<XmlElement>().Any(el =>
            el.LocalName is "br" or "b" or "strong" or "i" or "em"
            || el.GetAttribute("style") is { Length: > 0 } style
               && (style.Contains("font-size", StringComparison.Ordinal) || style.Contains("font-weight", StringComparison.Ordinal)
                   || style.Contains("font-style", StringComparison.Ordinal) || style.Contains("font-family", StringComparison.Ordinal)));

    /// <summary>The height the value's laid-out lines take when centred: a single line keeps its 1.15 em box,
    /// each further line adds the text pitch, so a value that grew its field is centred as a block instead of
    /// its first line being centred and the rest running out below the field.</summary>
    private static double ValueBlockHeight(PaintFieldState pf)
    {
        var (_, _, ml, mr) = Margins(pf.e);
        var lines = ValueLineCount(pf.e, pf.val!.Trim(), Math.Max(4, pf.ew - 3 - ml - mr), pf.fs);
        double lineH = FirstChild(pf.e, "para") is { } para && LenN(para.GetAttribute("lineHeight")) is { } plh && plh > 0
            ? plh
            : pf.fs;
        return pf.fs * 1.15 + Math.Max(0, lines - 1) * lineH;
    }

    /// <summary>Paints the edit-region chrome from the ui border's per-edge presence: a fill, the legacy answer-line for an editable borderless field, a full box, or a bottom underline.</summary>
    private static void PaintEditChrome(PaintFieldState pf)
    {
        // Edit-region chrome, driven by the ui <border>'s per-edge <presence>:
        //   - no <border> element        → legacy baseline underline
        //   - <border> itself hidden      → no chrome
        //   - all four edges visible (or one visible edge that XFA applies to all
        //     sides) → a full box
        //   - only the bottom edge visible (XFA edge order top/right/bottom/left,
        //     the common Designer input-line style) → a bottom underline
        // A ui-border fill shades the edit region regardless.
        var uiBorder = FirstChild(pf.e, "ui")?.ChildNodes.OfType<XmlElement>().FirstOrDefault()
            ?.ChildNodes.OfType<XmlElement>().FirstOrDefault(c => c.LocalName == "border");
        if (uiBorder is not null && ColorOf(FirstChild(uiBorder, "fill")) is { } uiFill
            && uiBorder.GetAttribute("presence") is not ("hidden" or "invisible"))
            pf.ctx.Items.Add(new Item { Kind = "fill", X = pf.bx, Y = pf.ctx.PageH - (pf.by + pf.bh), W = pf.bw, H = pf.bh, Color = uiFill, Radius = CornerRadius(uiBorder) });
        if (uiBorder is null)
        {
            // The legacy input answer-line applies to EDITABLE fields only: a
            // borderless access="readOnly" field is display text (letter templates
            // bind these straight to data) and Designer draws no chrome for it.
            // A field whose own border already draws its box or its rule needs no second line inside it.
            var boxed = pf.fieldBorder is not null && pf.fieldBorder.GetAttribute("presence") is not ("hidden" or "invisible")
                && FirstChild(pf.fieldBorder, "edge") is not null && EdgeChrome(pf.fieldBorder) != EditChrome.None;
            if (pf.e.GetAttribute("access") != "readOnly" && !boxed)
                pf.ctx.Items.Add(new Item { Kind = "line", X = pf.bx, Y = pf.ctx.PageH - (pf.by + pf.bh), W = pf.bw, H = 0 });
        }
        else if (uiBorder.GetAttribute("presence") is not ("hidden" or "invisible"))
        {
            switch (EdgeChrome(uiBorder))
            {
                case EditChrome.Box:
                    pf.ctx.Items.Add(new Item { Kind = "box", X = pf.bx, Y = pf.ctx.PageH - (pf.by + pf.bh), W = pf.bw, H = pf.bh, Radius = CornerRadius(uiBorder), Color = EdgeColor(uiBorder) });
                    break;
                case EditChrome.Underline:
                    pf.ctx.Items.Add(new Item { Kind = "line", X = pf.bx, Y = pf.ctx.PageH - (pf.by + pf.bh), W = pf.bw, H = 0 });
                    break;
                // EditChrome.None → no chrome (all edges hidden).
            }
        }
    }

    /// <summary>Paints a field-level border: its fill, and with a visible edge a box or a bottom rule across the field's whole box.</summary>
    private static void PaintFieldBorder(PaintFieldState pf)
    {
        pf.fieldBorder = FirstChild(pf.e, "border");
        var radius = CornerRadius(pf.fieldBorder);
        pf.fieldFill = FillColor(pf.e);
        if (pf.fieldFill is not null)
            pf.ctx.Items.Add(new Item { Kind = "fill", X = pf.x, Y = pf.ctx.PageH - (pf.y + pf.h), W = pf.w, H = pf.h, Color = pf.fieldFill, Radius = radius });
        if (pf.fieldBorder is not null && pf.fieldBorder.GetAttribute("presence") is not ("hidden" or "invisible")
            && FirstChild(pf.fieldBorder, "edge") is not null)
            switch (EdgeChrome(pf.fieldBorder))
            {
                case EditChrome.Box:
                    pf.ctx.Items.Add(new Item { Kind = "box", X = pf.x, Y = pf.ctx.PageH - (pf.y + pf.h), W = pf.w, H = pf.h, Radius = radius, Color = EdgeColor(pf.fieldBorder) });
                    break;
                // Only the bottom edge visible (Designer's answer-line style on the
                // FIELD box): a rule across the field's full width at its bottom.
                case EditChrome.Underline:
                    pf.ctx.Items.Add(new Item { Kind = "line", X = pf.x, Y = pf.ctx.PageH - (pf.y + pf.h), W = pf.w, H = 0 });
                    break;
                // EditChrome.None → nothing.
            }
    }

    /// <summary>Resolves the displayed value: the datasets-bound value, else the SOM-resolved one, else the template default, picture-formatted; a choice list shows the item text.</summary>
    private static void ResolveFieldValue(PaintFieldState pf)
    {
        // A field whose layout-ready script writes the page number shows what that script gives it on this page.
        pf.val = pf.ctx.PageScripts?.ValueOn(pf.e, pf.ctx.PageNum);
        if (pf.val is not null) return;
        if (CalculatedValue(pf.e) is { } calculated) pf.val = ApplyPicture(pf.e, calculated);
        else if (!BindsNoData(pf.e))
        {
            pf.val = BoundValue(pf.ctx, pf.e, pf.e.GetAttribute("name"));
            pf.bound = pf.val is not null;
            // A field without a name has no path of its own: its container's names that container's data.
            if (pf.val is null && PathSegment(pf.e) is not null) pf.val = pf.ctx.RawValue?.Invoke(pf.path);
            pf.val ??= ChainedReferenceValue(pf.ctx, pf.e);
            if (!string.IsNullOrEmpty(pf.val)) pf.val = ApplyPicture(pf.e, pf.val);
        }
        if (string.IsNullOrEmpty(pf.val))
        {
            pf.val = DefaultOrScriptValue(pf.ctx, pf.e);
            if (!string.IsNullOrEmpty(pf.val)) pf.val = ApplyPicture(pf.e, pf.val);
        }
        // A choice list displays the item TEXT for the bound item value.
        if (!string.IsNullOrEmpty(pf.val) && pf.ui == "choiceList") pf.val = ChoiceDisplay(pf.e, pf.val) ?? pf.val;
    }

    /// <summary>Lays the caption strip out (left/right a horizontal reserve, top/bottom a vertical one), the edit region that remains, and the widget box inside the margin insets.</summary>
    private static void ResolveFieldRegions(PaintFieldState pf)
    {
        pf.capW = 0;
        pf.capH = 0;
        pf.captionFont = CaptionFont(pf.e);
        pf.capFs = pf.captionFont.fs;
        pf.capBold = pf.captionFont.bold;
        if (!string.IsNullOrWhiteSpace(pf.cap))
        {
            if (pf.plc is "top" or "bottom") pf.capH = pf.res > 0 ? pf.res : pf.capFs * 1.2;
            else if (pf.plc is "left" or "right" or "") pf.capW = pf.res > 0 ? pf.res : TextWidth(pf.cap.Trim(), pf.capFs, pf.capBold, FontSerif(CaptionFontOwner(pf.e))) * CaptionWideFactor(pf.e) + 4;
        }
        else if (pf.res > 0)
        {
            // An explicit caption reserve holds its strip even with no caption text.
            if (pf.plc is "top" or "bottom") pf.capH = pf.res; else pf.capW = pf.res;
        }
        pf.ex = pf.x + (pf.plc is "right" ? 0 : pf.capW);
        pf.ew = Math.Max(2, pf.w - pf.capW);
        pf.ey = pf.y + (pf.plc == "top" ? pf.capH : 0);
        pf.eh = Math.Max(2, pf.h - pf.capH);
        pf.widgetMargins = Margins(pf.e);
        pf.wmT = pf.widgetMargins.Item1;
        pf.wmB = pf.widgetMargins.Item2;
        pf.wmL = pf.widgetMargins.Item3;
        pf.wmR = pf.widgetMargins.Item4;
        pf.bx = pf.ex + pf.wmL;
        pf.by = pf.ey + pf.wmT;
        pf.bw = Math.Max(2, pf.ew - pf.wmL - pf.wmR);
        pf.bh = Math.Max(2, pf.eh - pf.wmT - pf.wmB);
    }

    /// <summary>Side of a check box or radio glyph whose button declares no size, in points.</summary>
    private const double DefaultCheckGlyphSize = 9;

    /// <summary>A check box or radio button: the glyph centred vertically in the field, marked when its on value matches the bound group value, with its caption beside.</summary>
    private static void PaintCheckField(PaintFieldState pf)
    {
        // Check/radio glyph vertically centred in the field, dot/check when its
        // "on" value (items) matches the bound group value.
        var button = FirstChild(pf.e, "ui")?.ChildNodes.OfType<XmlElement>().FirstOrDefault(c => c.LocalName == "checkButton");
        // The glyph has the size its button declares, inside the field; one that declares none keeps the usual size.
        double declared = button is null ? 0 : Len(button.GetAttribute("size"), 0);
        double bs = Math.Min(declared > 0 ? declared : DefaultCheckGlyphSize, Math.Min(pf.w, pf.h));
        var (_, _, ml2, mr2) = Margins(pf.e);
        double gx = pf.x + ml2, gy = pf.y + Math.Max(0, (pf.h - bs) / 2);
        // The field's own fill is the cell the glyph stands on.
        if (FillColor(pf.e) is { } cell)
            pf.ctx.Items.Add(new Item { Kind = "fill", X = pf.x, Y = pf.ctx.PageH - (pf.y + pf.h), W = pf.w, H = pf.h, Color = cell, Radius = CornerRadius(FirstChild(pf.e, "border")) });
        bool round = button?.GetAttribute("shape") == "round";
        var glyphBorder = button is null ? null : FirstChild(button, "border");
        pf.ctx.Items.Add(new Item { Kind = round ? "circle" : "box", X = gx, Y = pf.ctx.PageH - (gy + bs), W = bs, H = bs, Color = EdgeColor(glyphBorder) });
        // The items list holds the on value first, then the off (and neutral) values: only the first counts.
        var onValue = FirstChild(pf.e, "items")?.ChildNodes.OfType<XmlElement>().FirstOrDefault()?.InnerText.Trim();
        var isExcl = (pf.e.ParentNode as XmlElement)?.LocalName == "exclGroup";
        var groupName = isExcl
            ? ((XmlElement)pf.e.ParentNode!).GetAttribute("name")
            : pf.e.GetAttribute("name");
        var bound = BoundValue(pf.ctx, pf.e, groupName);
        var bindCarrier = isExcl ? (XmlElement)pf.e.ParentNode! : pf.e;
        // A box outside every data-bound instance reads the node its place among its namesakes binds, as a
        // text field does, before the first leaf of its name anywhere in the data is taken for it.
        if (bound is null && pf.ctx.RawValue is not null && PathSegment(bindCarrier) is not null && !BindsNoData(bindCarrier))
            bound = pf.ctx.RawValue(SomPathOf(bindCarrier));
        // Same datasets-leaf fallback fields use: a top-level radio group (e.g. a
        // chapter selector bound to a root-level value) has no data-idx ancestor.
        if (bound is null && pf.ctx.DataRoot is not null && groupName.Length > 0 && !BindsNoData(bindCarrier))
        {
            var leaves = pf.ctx.DataRoot.SelectNodes(".//*")!.OfType<XmlElement>()
                .Where(d => d.LocalName == groupName && !d.ChildNodes.OfType<XmlElement>().Any())
                .ToList();
            if (leaves.Count == 1 || (leaves.Count > 1 && !pf.ctx.StrictBinding))
                bound = leaves[0].InnerText;
        }
        // No data node at all: the field's own value decides, as for a text field - the state the saved
        // form packet overlaid, else the template default.
        bound ??= InnerText(bindCarrier, "value");
        (pf.glyphX, pf.glyphY, pf.glyphSize, pf.glyphRound) = (gx, gy, bs, round);
        pf.glyphMark = CheckMarkOf(button, round);
        (pf.glyphOnValue, pf.glyphOn) = (onValue, !string.IsNullOrEmpty(onValue) && bound?.Trim() == onValue);
        if (!string.IsNullOrEmpty(onValue) && bound?.Trim() == onValue)
            pf.ctx.Items.Add(CheckMark(pf.glyphMark, gx, gy, bs, pf.ctx.PageH));
        if (!string.IsNullOrWhiteSpace(pf.cap))
        {
            var (cfs, cbold) = CaptionFont(pf.e);
            // A caption with a reserve beside the glyph lies in its strip at the far side of the content box;
            // without one it follows the glyph. The insets are the field's, counted once.
            double capLeft = pf.res > 0 && pf.plc == "right" ? Math.Max(gx + bs, pf.x + pf.w - mr2 - pf.res) : gx + bs + 1.5;
            if (PaintRichCaption(pf, capLeft, pf.x + pf.w - mr2, pf.y, pf.h)) return;
            AddText(pf.ctx, pf.e, capLeft - ml2, pf.y, pf.x + pf.w - capLeft + ml2, pf.cap.Trim(),
                fsOverride: cfs, boldOverride: cbold, alignSource: FirstChild(pf.e, "caption"), middleOf: pf.h);
        }
    }

    /// <summary>A push button: nothing when its border or edge is hidden without a face fill, else the face fill, a box and the caption on the face.</summary>
    private static void PaintButtonField(PaintFieldState pf)
    {
        // A button whose field border (or its edge) is hidden is an invisible link
        // hotspot (Designer "go to" navigation fields) — nothing is painted. A hidden
        // EDGE with a declared face fill only drops the outline: the filled face
        // (and its caption) still paints.
        var fb = FirstChild(pf.e, "border");
        if (fb is not null
            && (fb.GetAttribute("presence") is "hidden" or "invisible"
                || (FirstChild(fb, "edge")?.GetAttribute("presence") is "hidden" or "invisible"
                    && FillColor(pf.e) is null)))
        {
            // Such a link still shows what it is called: its caption, in the caption's own font.
            if (!string.IsNullOrWhiteSpace(pf.cap))
            {
                var (linkFs, linkBold) = CaptionFont(pf.e);
                var linkFont = FirstChild(pf.e, "caption") is { } lc ? FirstChild(lc, "font") : null;
                AddText(pf.ctx, pf.e, pf.x, pf.y, pf.w, pf.cap.Trim(),
                    fsOverride: linkFs, boldOverride: linkBold,
                    colorOverride: linkFont is null ? null : ColorOf(FirstChild(linkFont, "fill")), alignSource: FirstChild(pf.e, "caption"),
                    middleOf: pf.h);
            }
            return;
        }
        // Push button: its declared face colour (border/direct fill), else the
        // classic gray, with a border and its caption on the face.
        pf.ctx.Items.Add(new Item { Kind = "fill", X = pf.x, Y = pf.ctx.PageH - (pf.y + pf.h), W = pf.w, H = pf.h, Color = FillColor(pf.e) ?? new[] { 0.83, 0.83, 0.83 } });
        pf.ctx.Items.Add(new Item { Kind = "box", X = pf.x, Y = pf.ctx.PageH - (pf.y + pf.h), W = pf.w, H = pf.h });
        if (!string.IsNullOrWhiteSpace(pf.cap))
        {
            // The caption's own font fill colours a button label (Designer's
            // white-on-colour faces); the field font stays for sizing.
            var capFont = FirstChild(pf.e, "caption") is { } ce ? FirstChild(ce, "font") : null;
            var capColor = capFont is null ? null : ColorOf(FirstChild(capFont, "fill"));
            AddText(pf.ctx, pf.e, pf.x + 1.5, pf.y, pf.w, pf.cap.Trim(),
                colorOverride: capColor, alignSource: FirstChild(pf.e, "caption"), middleOf: pf.h);
        }
    }

    /// <summary>An image field: the datasets-bound base64 image, else the template image, never painted as text.</summary>
    private static void PaintImageField(PaintFieldState pf)
    {
        // An image field: datasets-bound base64 first (the SOM resolver reaches
        // dataRef-bound nodes the name walk cannot — a signature/seal image bound
        // via <bind match="dataRef">), else the template image.
        // NEVER paint image data as text.
        byte[]? data = null;
        var bound = BoundValue(pf.ctx, pf.e, pf.e.GetAttribute("name"));
        if (string.IsNullOrWhiteSpace(bound)) bound = pf.ctx.RawValue?.Invoke(pf.path);
        if (!string.IsNullOrWhiteSpace(bound))
        {
            try { data = Convert.FromBase64String(bound.Trim()); } catch { /* malformed base64 - no image */ }
        }
        data ??= ImageDataOf(pf.ctx, pf.e);
        if (data is null) return;
        // The field is listed with its picture; an image field holding none is a frame the page shows nothing of.
        var picture = new Item { Kind = "image", X = pf.x, Y = pf.ctx.PageH - (pf.y + pf.h), W = pf.w, H = pf.h, ImageData = data, Stretch = ImageStretches(pf.e) };
        pf.ctx.Items.Add(picture);
        picture.Field = RecordPlacement(pf);
    }
}
