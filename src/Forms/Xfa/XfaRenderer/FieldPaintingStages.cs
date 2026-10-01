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
            double vy = pf.plc == "top"
                ? pf.y + Math.Max(pf.capH, (pf.h - pf.fs * 1.15) / 2)
                : pf.ey + Math.Max(0, (pf.eh - pf.fs * 1.15) / 2);
            AddText(pf.ctx, pf.e, pf.ex + 1.5, vy, pf.ew - 3, pf.eh, pf.val!.Trim());
        }

        // caption label (vertically centred beside the edit region for left/right)
        if (!string.IsNullOrWhiteSpace(pf.cap))
        {
            double capX = pf.plc == "right" ? pf.x + pf.w - pf.capW : pf.x;
            double capY = pf.plc switch
            {
                "bottom" => pf.y + pf.h - pf.capH,
                "top" => pf.y,
                _ => pf.y + Math.Max(0, (pf.h - pf.capFs * 1.15) / 2),
            };
            AddText(pf.ctx, pf.e, capX, capY, pf.capW > 0 ? pf.capW : pf.w, pf.capH > 0 ? pf.capH : pf.h, pf.cap.Trim(),
                fsOverride: pf.capFs, boldOverride: pf.capBold, alignSource: FirstChild(pf.e, "caption"));
        }
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
            pf.ctx.Items.Add(new Item { Kind = "fill", X = pf.bx, Y = pf.ctx.PageH - (pf.by + pf.bh), W = pf.bw, H = pf.bh, Color = uiFill });
        if (uiBorder is null)
        {
            // The legacy input answer-line applies to EDITABLE fields only: a
            // borderless access="readOnly" field is display text (letter templates
            // bind these straight to data) and Designer draws no chrome for it.
            if (pf.e.GetAttribute("access") != "readOnly")
                pf.ctx.Items.Add(new Item { Kind = "line", X = pf.bx, Y = pf.ctx.PageH - (pf.by + pf.bh), W = pf.bw, H = 0 });
        }
        else if (uiBorder.GetAttribute("presence") is not ("hidden" or "invisible"))
        {
            switch (EdgeChrome(uiBorder))
            {
                case EditChrome.Box:
                    pf.ctx.Items.Add(new Item { Kind = "box", X = pf.bx, Y = pf.ctx.PageH - (pf.by + pf.bh), W = pf.bw, H = pf.bh });
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
        pf.fieldFill = FillColor(pf.e);
        if (pf.fieldFill is not null)
            pf.ctx.Items.Add(new Item { Kind = "fill", X = pf.x, Y = pf.ctx.PageH - (pf.y + pf.h), W = pf.w, H = pf.h, Color = pf.fieldFill });
        pf.fieldBorder = FirstChild(pf.e, "border");
        if (pf.fieldBorder is not null && pf.fieldBorder.GetAttribute("presence") is not ("hidden" or "invisible")
            && FirstChild(pf.fieldBorder, "edge") is not null)
            switch (EdgeChrome(pf.fieldBorder))
            {
                case EditChrome.Box:
                    pf.ctx.Items.Add(new Item { Kind = "box", X = pf.x, Y = pf.ctx.PageH - (pf.y + pf.h), W = pf.w, H = pf.h });
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
        pf.val = null;
        if (!BindsNoData(pf.e))
        {
            pf.val = BoundValue(pf.ctx, pf.e, pf.e.GetAttribute("name"));
            if (pf.val is null) pf.val = pf.ctx.RawValue?.Invoke(pf.path);
            if (!string.IsNullOrEmpty(pf.val)) pf.val = ApplyPicture(pf.e, pf.val!);
        }
        if (string.IsNullOrEmpty(pf.val))
        {
            pf.val = InnerText(pf.e, "value");
            if (!string.IsNullOrEmpty(pf.val)) pf.val = ApplyPicture(pf.e, pf.val);
        }
        // A choice list displays the item TEXT for the bound item value.
        if (!string.IsNullOrEmpty(pf.val) && pf.ui == "choiceList") pf.val = ChoiceDisplay(pf.e, pf.val!) ?? pf.val;
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
            else if (pf.plc is "left" or "right" or "") pf.capW = pf.res > 0 ? pf.res : TextWidth(pf.cap.Trim(), pf.capFs, pf.capBold) + 4;
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

    /// <summary>A check box or radio button: the glyph centred vertically in the field, marked when its on value matches the bound group value, with its caption beside.</summary>
    private static void PaintCheckField(PaintFieldState pf)
    {
        // Check/radio glyph vertically centred in the field, dot/check when its
        // "on" value (items) matches the bound group value.
        double bs = Math.Min(9, Math.Min(pf.w, pf.h));
        var (mt2, _, ml2, _) = Margins(pf.e);
        double gx = pf.x + ml2, gy = pf.y + Math.Max(0, (pf.h - bs) / 2);
        bool round = FirstChild(pf.e, "ui")?.ChildNodes.OfType<XmlElement>()
            .FirstOrDefault(c => c.LocalName == "checkButton")?.GetAttribute("shape") == "round";
        pf.ctx.Items.Add(new Item { Kind = round ? "circle" : "box", X = gx, Y = pf.ctx.PageH - (gy + bs), W = bs, H = bs });
        var onValue = InnerText(pf.e, "items")?.Trim();
        var isExcl = (pf.e.ParentNode as XmlElement)?.LocalName == "exclGroup";
        var groupName = isExcl
            ? ((XmlElement)pf.e.ParentNode!).GetAttribute("name")
            : pf.e.GetAttribute("name");
        var bound = BoundValue(pf.ctx, pf.e, groupName);
        // Same datasets-leaf fallback fields use: a top-level radio group (e.g. a
        // chapter selector bound to a root-level value) has no data-idx ancestor.
        var bindCarrier = isExcl ? (XmlElement)pf.e.ParentNode! : pf.e;
        if (bound is null && pf.ctx.DataRoot is not null && groupName.Length > 0 && !BindsNoData(bindCarrier))
        {
            var leaves = pf.ctx.DataRoot.SelectNodes(".//*")!.OfType<XmlElement>()
                .Where(d => d.LocalName == groupName && !d.ChildNodes.OfType<XmlElement>().Any())
                .ToList();
            if (leaves.Count == 1 || (leaves.Count > 1 && !pf.ctx.StrictBinding))
                bound = leaves[0].InnerText;
        }
        if (!string.IsNullOrEmpty(onValue) && bound?.Trim() == onValue)
            pf.ctx.Items.Add(new Item { Kind = round ? "dot" : "fill", X = gx + bs * 0.28, Y = pf.ctx.PageH - (gy + bs * 0.72), W = bs * 0.44, H = bs * 0.44, Color = new[] { 0.0, 0.0, 0.0 } });
        if (!string.IsNullOrWhiteSpace(pf.cap))
        {
            var (cfs, cbold) = CaptionFont(pf.e);
            AddText(pf.ctx, pf.e, gx + bs + 1.5, pf.y + Math.Max(0, (pf.h - cfs * 1.15) / 2), pf.w - bs - 1.5, pf.h, pf.cap.Trim(),
                fsOverride: cfs, boldOverride: cbold);
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
            return;
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
            AddText(pf.ctx, pf.e, pf.x + 1.5, pf.y + Math.Max(0, (pf.h - pf.fs * 1.15) / 2), pf.w, pf.h, pf.cap.Trim(),
                colorOverride: capColor, alignSource: FirstChild(pf.e, "caption"));
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
            try { data = Convert.FromBase64String(bound!.Trim()); } catch { }
        }
        data ??= ImageDataOf(pf.ctx, pf.e);
        if (data is not null)
            pf.ctx.Items.Add(new Item { Kind = "image", X = pf.x, Y = pf.ctx.PageH - (pf.y + pf.h), W = pf.w, H = pf.h, ImageData = data, Stretch = ImageStretches(pf.e) });
    }
}
