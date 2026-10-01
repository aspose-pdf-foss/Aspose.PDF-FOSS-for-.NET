namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>The face a run sets in and its size: the run's family (else the base)
    /// at the run's weight and slant, falling back to the base face.</summary>
    private static (UaFace Face, double Size) InlineRunFace(HtmlInlineFlowState st, HtmlInlineItem item) =>
        (InlineFaceFor(st, item.Family, item.Bold, item.Italic), item.SizePt > 0 ? item.SizePt : st.basePt);

    private static UaFace InlineFaceFor(HtmlInlineFlowState st, string? family, bool bold, bool italic)
    {
        var fam = string.IsNullOrEmpty(family) ? st.baseFamily : family!;
        var key = fam.ToLowerInvariant() + (bold ? "|b" : "") + (italic ? "|i" : "");
        if (st.faces.TryGetValue(key, out var cached) && cached is not null) return cached;
        var face = TryLoadInlineFace(st, fam, bold, italic)
                   ?? TryLoadInlineFace(st, fam, false, false)
                   ?? TryLoadInlineFace(st, st.baseFamily, bold, italic)
                   ?? st.strut;
        st.faces[key] = face;
        return face;
    }

    /// <summary>Resolve a family in a style for this flow, under a resource name of its own.</summary>
    private static UaFace? TryLoadInlineFace(HtmlInlineFlowState st, string family, bool bold, bool italic) =>
        UaFace.TryLoad(family, bold, italic, "FUa" + (st.faceCount + 1)) is { } face && ++st.faceCount > 0 ? face : null;

    /// <summary>The pair-kerned advance of <paramref name="text"/> in the run's face at its size.</summary>
    private static double InlineTextWidth(HtmlInlineFlowState st, HtmlInlineItem item, string text)
    {
        var (face, size) = InlineRunFace(st, item);
        return face.Width(text, size);
    }

    /// <summary>Seat one text cell: its background box (ascent to descent), the glyphs in
    /// the run's colour, an underline 0.1 em thick 0.1 em under the baseline, and its link.</summary>
    private void EmitInlineText(HtmlInlineFlowState st, HtmlInlineCell c, double x0, double baseline)
    {
        var item = c.Item;
        var (face, size) = InlineRunFace(st, item);
        var text = face.EncodableText(c.Text);
        var csb = st.csb;
        var fore = item.Fore ?? st.defaultColor;
        if (item.Back is { } back)
        {
            csb.SaveState().SetFillColor(back)
               .Rectangle(x0, baseline - face.Descent(size), c.W, face.Ascent(size) + face.Descent(size))
               .Fill().RestoreState();
        }
        var (res, hex) = Text.Type0FontEmbedder.Embed(st.fontDict, face.Ttf, face.Name, text,
            stripSpacesInBaseFont: true, resNameHint: face.ResName);
        csb.SaveState();
        if (fore is not null) csb.SetFillColor(fore);
        csb.BeginText().SetFont(res, size).MoveTextPosition(x0, baseline);
        if (face.KernAdjustments(text) is { } adj) csb.ShowTextHexKerned(hex, adj);
        else csb.ShowTextHex(hex);
        csb.EndText();
        if (item.Underline)
        {
            var thick = size * FlowLayout.HtmlDecoThicknessEm;
            var y = baseline - size * FlowLayout.HtmlUnderlineDropEm;
            csb.SetStrokeColor(fore ?? Color.FromArgb(0, 0, 0)).SetLineWidth(thick)
               .MoveTo(x0, y).LineTo(x0 + c.W, y).Stroke();
        }
        csb.RestoreState();
        if (item.Href is { Length: > 0 } href)
            st.flow.QueueLink(new Rectangle(x0, baseline - face.Descent(size), x0 + c.W, baseline + face.Ascent(size)),
                new WebHyperlink(InlineHrefUrl(href, st.hl.html.HtmlLoadOptions?.BasePath)));
    }

    /// <summary>A relative href becomes a file URI under the fragment's base path.</summary>
    private static string InlineHrefUrl(string href, string? basePath)
    {
        if (System.Text.RegularExpressions.Regex.IsMatch(href, @"^[a-zA-Z][a-zA-Z0-9+.-]*:") || string.IsNullOrEmpty(basePath))
            return href;
        try { return new Uri(System.IO.Path.Combine(basePath, href)).AbsoluteUri; }
        catch { return href; }
    }

    /// <summary>Place a picture standing on the baseline (or centred on the x-height);
    /// returns how far it hangs under the baseline.</summary>
    private static double EmitInlineImage(HtmlInlineFlowState st, HtmlInlineCell c, double x0, double top, double baseline)
    {
        var item = c.Item;
        var bottom = item.ImageMiddle
            ? baseline - st.strut.XHeightPt(st.basePt) / 2 - item.ImageH / 2
            : baseline;
        st.flow.QueueImageAt(item.ImageData!, new Rectangle(x0, bottom, x0 + item.ImageW, bottom + item.ImageH));
        if (item.Href is { Length: > 0 } href)
            st.flow.QueueLink(new Rectangle(x0, bottom, x0 + item.ImageW, bottom + item.ImageH),
                new WebHyperlink(InlineHrefUrl(href, st.hl.html.HtmlLoadOptions?.BasePath)));
        return System.Math.Max(0, baseline - bottom);
    }

    /// <summary>A radio control: a 9.75 pt widget 3.75 pt in from the pen, its bottom on the
    /// baseline, named "radio", "radio1", … in fragment order.</summary>
    private static void EmitInlineRadio(HtmlInlineFlowState st, HtmlInlineCell c, double x0, double baseline)
    {
        var x = x0 + RadioLeftMarginPt;
        var name = st.radioCount == 0 ? "radio" : "radio" + st.radioCount;
        st.radioCount++;
        st.flow.QueueRadioAt(new Rectangle(x, baseline, x + RadioBoxPt, baseline + RadioBoxPt), name, c.Item.Checked);
    }

    /// <summary>A checkbox control: a 7.75 pt widget 4 pt in from the pen, 3.25 pt under the line top.</summary>
    private static void EmitInlineCheckbox(HtmlInlineFlowState st, HtmlInlineCell c, double x0, double top)
    {
        var x = x0 + CheckboxLeftMarginPt;
        var y1 = top - CheckboxTopMarginPt;
        st.flow.QueueCheckboxAt(new Rectangle(x, y1 - CheckboxBoxPt, x + CheckboxBoxPt, y1), c.Item.Checked);
    }
}
