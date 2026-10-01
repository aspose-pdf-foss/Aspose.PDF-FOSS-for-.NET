using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Print ad tags: one tag of the ad fragment walked and emitted.</summary>
    private static bool WalkAdTag(PrintAdState pa, AdWalkState wa, string frag)
    {
        var at = new AdTagState();
        at.t = pa.tagRx.Match(frag[wa.p2..]);
        at.lead = at.t.Success ? frag[wa.p2..(wa.p2 + at.t.Index)] : frag[wa.p2..];
        if (at.lead.Trim().Length > 0)
        {
            Spend(pa);
            var box0 = pa.baseFs * pa.lineFactor;
            EmitWrapped(pa, PlainText(pa, at.lead), pa.baseFs, false, box0, pa.xText, pa.contentW);
        }
        if (!at.t.Success) return false;
        at.abs = wa.p2 + at.t.Index;
        wa.p2 = at.abs + at.t.Length;
        if (at.t.Value.StartsWith("<!--", StringComparison.Ordinal)) return true;
        if (at.t.Groups[1].Value == "/" || at.t.Groups[4].Value == "/") return true;
        at.tag = at.t.Groups[2].Value.ToLowerInvariant();
        at.cls = AttrValue("<x " + at.t.Groups[3].Value + ">", "class") ?? "";
        at.hidden = false;
        foreach (var c in at.cls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (HiddenCls(pa, c)) { at.hidden = true; break; }
        switch (at.tag)
        {
            case "style": case "script": case "img":
                SkipOrImage(pa, wa, at, frag, at.tag);
                break;
            case "h1": case "h6":
                Heading(pa, wa, at, frag, at.tag);
                break;
            case "p":
                Paragraph(pa, wa, at, frag, at.tag);
                break;
            case "ul":
                ListBlock(pa, wa, at, frag, at.tag);
                break;
            case "div": case "a":
                DivOrAnchor(pa, wa, at, frag, at.tag);
                break;
            case "h2": case "h3": case "h4": case "h5": case "span": case "strong": case "b":
                InlineRun(pa, wa, at, frag, at.tag);
                break;
        }
        return true;
    }
}
