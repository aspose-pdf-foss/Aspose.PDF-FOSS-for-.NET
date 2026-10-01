using System;
using System.Collections.Generic;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    private enum IpKind { Text, Select, Radio, Check, Button, TextBox, DtPicker, HyperBox, Image }

    /// <summary>The inherited text state while walking inline content.</summary>
    private sealed class IpStyle
    {
        public double Fs = IpFs;
        public bool Bold;
        public bool Gothic;                                  // MS UI Gothic (the placeholders)
        public string Rgb = "0 0 0";
        public IpStyle Clone() => (IpStyle)MemberwiseClone();
    }

    /// <summary>One inline box of a line: its extent above and below the line's baseline, its
    /// advance, and what it paints.</summary>
    private sealed class IpItem
    {
        public IpKind Kind;
        public string Text = "";
        public string Face = IpFace;
        public double Fs = IpFs;
        public string Rgb = "0 0 0";
        public double Above, Below, Advance;
        public double W, H;                                  // a box's declared/derived content size
        public bool Checked, Cjk, Nowrap, Swallowed;
        public IpLine? Inner;                                // a textbox's inner line
        public IpNode? Node;
        public double X;                                     // pen offset in the line, set by the line
    }

    private sealed class IpLine
    {
        public List<IpItem> Items = new();
        public double Above, Below, Width;
        public bool HasText;
        public double Height => Above + Below;
    }

    /// <summary>Build the line of a container's inline children at a content width; a block
    /// child is not an inline item (the block walker hands those out separately).</summary>
    private static IpLine IpBuildLine(InfoPathState ip, List<(IpNode node, IpStyle style)> inline, double contentW)
    {
        var line = new IpLine();
        foreach (var (node, style) in inline)
        {
            if (node.IsText) IpAddTextItem(line, node.Text, style);
            else if (IpMakeBox(ip, node, style, contentW) is { } box) line.Items.Add(box);
        }
        IpTrimLine(line);
        var x = 0.0;
        foreach (var it in line.Items)
        {
            it.X = x;
            x += it.Advance;
            line.Above = Math.Max(line.Above, it.Above);
            line.Below = Math.Max(line.Below, it.Below);
            if (it.Kind == IpKind.Text) line.HasText = true;
        }
        line.Width = x;
        return line;
    }

    private static void IpAddTextItem(IpLine line, string text, IpStyle st)
    {
        if (text.Length == 0) return;
        // a space joining two runs belongs to one of them only
        if (text == " " && line.Items.Count > 0 && line.Items[^1].Kind == IpKind.Text && line.Items[^1].Text.EndsWith(' ')) return;
        var cjk = IpIsCjk(text);
        var face = cjk ? IpCjkFace : st.Gothic ? IpGothicFace : st.Bold ? IpBoldFace : IpFace;
        var gothic = st.Gothic || cjk;
        line.Items.Add(new IpItem
        {
            Kind = IpKind.Text, Text = text, Face = face, Fs = st.Fs, Rgb = st.Rgb, Cjk = cjk,
            Above = IpAbove(st.Fs, gothic), Below = IpBelow(st.Fs, gothic),
            Advance = cjk ? text.Length * IpCjkAdvanceEm * st.Fs : IpMeasure(face, text, st.Fs),
        });
    }

    /// <summary>Drop the whitespace a line cannot show: leading and trailing spaces, and a
    /// space run beside a box that already ends in one.</summary>
    private static void IpTrimLine(IpLine line)
    {
        var items = line.Items;
        while (items.Count > 0 && items[0].Kind == IpKind.Text && items[0].Text.Trim().Length == 0) items.RemoveAt(0);
        if (items.Count > 0 && items[0].Kind == IpKind.Text && items[0].Text.StartsWith(' '))
        {
            items[0].Text = items[0].Text.TrimStart(' ');
            items[0].Advance = IpMeasure(items[0].Face, items[0].Text, items[0].Fs);
        }
        while (items.Count > 0 && items[^1].Kind == IpKind.Text && items[^1].Text.Trim().Length == 0) items.RemoveAt(items.Count - 1);
        if (items.Count > 0 && items[^1].Kind == IpKind.Text && items[^1].Text.EndsWith(' '))
        {
            items[^1].Text = items[^1].Text.TrimEnd(' ');
            items[^1].Advance = items[^1].Cjk ? items[^1].Text.Length * items[^1].Fs : IpMeasure(items[^1].Face, items[^1].Text, items[^1].Fs);
        }
    }

    /// <summary>The inline box of a control element, or null for a transparent wrapper.</summary>
    private static IpItem? IpMakeBox(InfoPathState ip, IpNode el, IpStyle st, double contentW)
    {
        switch (el.Tag)
        {
            case "select": return IpSelectBox(el, st, contentW);
            case "input":
                return el.Attr("type").ToLowerInvariant() switch
                {
                    "radio" => IpInputBox(el, IpKind.Radio),
                    "checkbox" => IpInputBox(el, IpKind.Check),
                    "button" or "submit" => IpButtonBox(el, el.Attr("value"), st),
                    _ => null,
                };
            case "button": return IpButtonBox(el, IpFlatText(el), st);
            case "img": return IpImageBox(el);
            case "span":
                if (el.HasClass("xdTextBox") || el.HasClass("xdRichTextBox")) return IpTextBoxItem(ip, el, st, contentW);
                if (el.HasClass("xdHyperlinkBox")) return IpHyperBox(el, contentW);
                return null;
            case "div":
                if (el.HasClass("xdDTPicker")) return IpDtPickerBox(el, st);
                return null;
            default: return null;
        }
    }

    private static IpItem IpSelectBox(IpNode el, IpStyle st, double contentW)
    {
        var w = IpPx(el.Css("width"));
        if (w <= 0) w = IpPct(el.Css("width"), contentW);
        var value = "";
        var selected = el.Attr("value");
        foreach (var opt in el.Elements("option"))
        {
            var text = IpFlatText(opt).Trim();
            if (value.Length == 0 || (selected.Length > 0 && opt.Attr("value") == selected)) value = text;
            if (selected.Length == 0) break;
        }
        var cjk = IpIsCjk(value);
        return new IpItem
        {
            Kind = IpKind.Select, Text = value, Cjk = cjk, Fs = st.Fs, W = w,
            Above = IpSelectAbove, Below = IpSelectBoxH - IpSelectAbove + (cjk ? IpSelectCjkExtra : 0),
            Advance = w + 2 * IpBoxMargin, Node = el,
        };
    }

    private static IpItem IpInputBox(IpNode el, IpKind kind)
    {
        var radio = kind == IpKind.Radio;
        return new IpItem
        {
            Kind = kind, Checked = el.Attrs.ContainsKey("checked"), W = IpInputBoxSize, H = IpInputBoxSize,
            Above = IpInputMarginTop + IpInputBoxSize, Below = radio ? 0 : IpCheckMarginBottom,
            Advance = (radio ? IpRadioMarginLeft : IpCheckMarginLeft) + IpInputBoxSize + IpInputMarginRight, Node = el,
        };
    }

    /// <summary>A push button: the #f0f0f0 fill is its layout box (the black rect and the bevel
    /// paint outside it); an unsized one is the text plus its pads, a sized one its declared box
    /// with the text at the top pad.</summary>
    private static IpItem IpButtonBox(IpNode el, string text, IpStyle st)
    {
        var fs = IpFontSizeOf(el) is > 0 and var own ? own : st.Fs;
        var face = IpFace;
        var textW = IpMeasure(face, text, fs);
        var w = IpPx(el.Css("width")) is > 0 and var dw ? dw : textW + 2 * IpButtonPadX;
        var h = IpPx(el.Css("height")) is > 0 and var dh ? dh : IpLineH(fs) + 2 * IpButtonPadTop;
        var above = IpButtonPadTop + IpAbove(fs);
        return new IpItem { Kind = IpKind.Button, Text = text, Face = face, Fs = fs, W = w, H = h, Above = above, Below = h - above, Advance = w, Node = el };
    }

    /// <summary>The date picker's inline box: the xdDTText span (its text, or the 15.75 box a
    /// self-closed span that swallowed its button lays out) and the 20×17px button after it.</summary>
    private static IpItem IpDtPickerBox(IpNode el, IpStyle st)
    {
        var span = el.First("span");
        var text = span is null ? "" : IpFlatText(span).Trim();
        var swallowed = span is not null && span.First("button") is not null;
        double inner;
        if (swallowed) inner = IpDtSwallowedW;
        else inner = IpMeasure(IpFace, text, st.Fs) + IpSpaceEm * st.Fs + IpDtButtonGap + IpDtButtonW;
        return new IpItem
        {
            Kind = IpKind.DtPicker, Text = text, Fs = st.Fs, Rgb = st.Rgb, Swallowed = swallowed, W = inner,
            Above = Math.Max(IpDtButtonAbove, text.Length > 0 ? IpAbove(st.Fs) : 0),
            Below = Math.Max(IpDtButtonBelow, text.Length > 0 ? IpBelow(st.Fs) : 0),
            Advance = IpBoxMargin + inner + IpBoxMargin, Node = el,
        };
    }

    /// <summary>A textbox / rich textbox: an inline-block whose baseline is its bottom margin
    /// edge; its white fill is the pads round its inner line (or its declared height).</summary>
    private static IpItem IpTextBoxItem(InfoPathState ip, IpNode el, IpStyle st, double contentW)
    {
        var w = IpPx(el.Css("width"));
        if (w <= 0) w = IpPct(el.Css("width"), contentW);
        var innerStyle = st.Clone();
        innerStyle.Rgb = "0 0 0";                            // .xdTextBox color: windowtext
        var inner = IpBuildLine(ip, IpInlineChildren(el, innerStyle), w);
        var declaredH = IpPx(el.Css("height"));
        var h = declaredH > 0 ? declaredH : inner.Height;
        var fillH = h + 2 * IpBoxPad;
        return new IpItem
        {
            Kind = IpKind.TextBox, Inner = inner, W = w, H = h, Nowrap = !el.Css("white-space").Equals("normal", StringComparison.OrdinalIgnoreCase),
            Above = IpBoxMargin + fillH + IpBoxMargin, Below = 0,
            Advance = IpBoxMargin + w + 2 * IpBoxPad + IpBoxMargin, Node = el,
        };
    }

    private static IpItem IpHyperBox(IpNode el, double contentW)
    {
        var w = IpPx(el.Css("width"));
        if (w <= 0) w = IpPct(el.Css("width"), contentW);
        var h = IpPx(el.Css("height"));
        var boxW = w + 2 * (IpHyperPad + IpHyperBorder);
        var boxH = h + 2 * (IpHyperPad + IpHyperBorder);
        return new IpItem { Kind = IpKind.HyperBox, W = boxW, H = boxH, Above = IpBoxMargin + boxH + IpBoxMargin, Below = 0, Advance = IpBoxMargin + boxW + IpBoxMargin, Node = el };
    }

    /// <summary>A picture at its declared size; the file is not beside the page, so it draws as
    /// the broken-image frame (the bevel a pt outside the declared box) with the icon inside.</summary>
    private static IpItem IpImageBox(IpNode el)
    {
        var w = IpPx(el.Css("width"));
        var h = IpPx(el.Css("height"));
        return new IpItem { Kind = IpKind.Image, W = w, H = h, Above = h + 2 * IpBrokenFrame, Below = 0, Advance = w + 2 * IpBrokenFrame, Node = el };
    }

    /// <summary>The inline children of an element in order with the text state each inherits:
    /// font/strong/span wrappers are transparent, a nested block div is skipped here (the block
    /// walker lays it out), a control element is one item.</summary>
    private static List<(IpNode node, IpStyle style)> IpInlineChildren(IpNode el, IpStyle st)
    {
        var list = new List<(IpNode, IpStyle)>();
        IpCollectInline(el, st, list);
        return list;
    }

    private static void IpCollectInline(IpNode el, IpStyle st, List<(IpNode, IpStyle)> list)
    {
        foreach (var c in el.Children)
        {
            if (c.IsText) { list.Add((c, st)); continue; }
            if (IpHidden(c)) continue;
            switch (c.Tag)
            {
                case "select": case "input": case "button": case "img":
                    list.Add((c, st));
                    break;
                case "span":
                    if (c.HasClass("xdTextBox") || c.HasClass("xdRichTextBox") || c.HasClass("xdHyperlinkBox")) list.Add((c, st));
                    else IpCollectInline(c, IpStyleOf(c, st), list);
                    break;
                case "div":
                    if (c.HasClass("xdDTPicker")) list.Add((c, st));
                    break;                                   // a block div is laid out by the block walker
                case "table":
                    break;
                default:
                    IpCollectInline(c, IpStyleOf(c, st), list);
                    break;
            }
        }
    }

    /// <summary>The text state an element hands its children.</summary>
    private static IpStyle IpStyleOf(IpNode el, IpStyle parent)
    {
        var st = parent.Clone();
        if (el.Tag == "strong" || el.Tag == "b" || el.Css("font-weight").Equals("bold", StringComparison.OrdinalIgnoreCase)) st.Bold = true;
        if (IpFontSizeOf(el) is > 0 and var fs) st.Fs = fs;
        var color = el.Attr("color");
        if (color.Length == 0) color = el.Css("color");
        if (color.Length > 0 && IpRgb(color) is { } rgb) st.Rgb = rgb;
        if (el.HasClass("primaryVeryDark")) st.Rgb = IpVeryDarkTextRgb;
        if (el.HasClass("optionalPlaceholder")) { st.Gothic = true; st.Rgb = IpPlaceholderRgb; st.Fs = IpPlaceholderFs; }
        return st;
    }

    /// <summary>The text of an element's descendants, whitespace collapsed.</summary>
    private static string IpFlatText(IpNode el)
    {
        var sb = new System.Text.StringBuilder();
        IpAppendText(el, sb);
        var s = sb.ToString();
        while (s.Contains("  ")) s = s.Replace("  ", " ");
        return s;
    }

    private static void IpAppendText(IpNode el, System.Text.StringBuilder sb)
    {
        foreach (var c in el.Children)
        {
            if (c.IsText) sb.Append(c.Text);
            else if (c.Tag != "select" && c.Tag != "button" && c.Tag != "input") IpAppendText(c, sb);
        }
    }
}
