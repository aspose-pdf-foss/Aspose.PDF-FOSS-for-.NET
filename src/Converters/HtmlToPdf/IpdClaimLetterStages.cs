using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Render one parsed item of the claim letter: title, headings, paragraphs, tables, sub-boxes and signature lines, each opening its block at the pending margin and breaking the page when it does not fit.</summary>
    private static bool RenderIpdItem(IpdClaimLetterState ic, IpdItem it)
    {
        switch (it.Kind)
        {
            case "title": case "sectitle":
                RenderIpdHeadings(ic, it, it.Kind);
                break;
            case "grid":
                RenderIpdGrid(ic, it, it.Kind);
                break;
            case "subbegin": case "subend":
                RenderIpdSubBox(ic, it, it.Kind);
                break;
            case "form": case "coverage": case "text":
                RenderIpdFormBlocks(ic, it, it.Kind);
                break;
        }
        return true;
    }

    /// <summary>Consume one tag of the letter body: opening and closing divs, tables and textareas become typed items with their sub-box depth.</summary>
    private static bool ParseIpdToken(IpdClaimLetterState ic)
    {
        var m = ic.tagRx.Match(ic.seg, ic.pos);
        if (!m.Success) return false;
        ic.pos = m.Index + m.Length;
        var closing = m.Groups[1].Value.Length > 0;
        var tag = m.Groups[2].Value.ToLowerInvariant();
        var attrs = m.Groups[3].Value;
        var clsM = Regex.Match(attrs, @"class\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase);
        var cls = clsM.Success ? clsM.Groups[1].Value : "";
        if (tag == "div")
        {
            if (ParseIpdDivTag(ic, closing, cls)) return true;
        }
        if (tag == "textarea")
        {
            var end = ic.seg.IndexOf("</textarea>", ic.pos, StringComparison.OrdinalIgnoreCase);
            if (end < 0) end = ic.seg.Length;
            ic.items.Add(new IpdItem { Kind = "text", Text = Flat(ic, ic.seg[ic.pos..end]), Fs = 9 });
            ic.pos = end;
            return true;
        }
        if (tag == "table" && !closing)
        {
            var end = ic.seg.IndexOf("</table>", ic.pos, StringComparison.OrdinalIgnoreCase);
            if (end < 0) end = ic.seg.Length;
            var body = ic.seg[ic.pos..end];
            ic.pos = end;
            var it = new IpdItem();
            if (cls.Contains("ipdPropertyGrid")) it.Kind = "grid";
            else
            {
                it.Kind = "form";
                it.Framed = cls.Contains("slfFramed");
                it.Cols = cls.Contains("slfFormLayoutCol2")
                    ? (cls.Contains("slfGeneralPayment") ? IpdCol2Pay : IpdCol2)
                    : IpdCol4;
                if (cls.Contains("slfCoverage")) { it.Kind = "coverage"; }
            }
            foreach (Match rm in Regex.Matches(body, @"<tr\b[^>]*>([\s\S]*?)</tr>",
                RegexOptions.IgnoreCase))
            {
                var cells = new List<string>();
                foreach (Match cm in Regex.Matches(rm.Groups[1].Value,
                    @"<td\b([^>]*)>([\s\S]*?)</td>", RegexOptions.IgnoreCase))
                {
                    cells.Add(Flat(ic, cm.Groups[2].Value));
                    var spanM = Regex.Match(cm.Groups[1].Value, @"colspan\s*=\s*""?(\d+)",
                        RegexOptions.IgnoreCase);
                    if (spanM.Success && int.TryParse(spanM.Groups[1].Value, out var sp))
                        for (var k = 1; k < sp; k++) cells.Add("");
                }
                if (cells.Count > 0) it.Rows.Add(cells);
            }
            if (it.Rows.Count > 0) ic.items.Add(it);
        }
        return true;
    }

    /// <summary>A div tag: the title, section, grid, sub-box, form, coverage and text classes open or close their items.</summary>
    private static bool ParseIpdDivTag(IpdClaimLetterState ic, bool closing, string cls)
    {
        if (closing)
        {
            ic.depthNow--;
            if (ic.subDepth.Count > 0 && ic.depthNow < ic.subDepth.Peek())
            {
                ic.subDepth.Pop();
                ic.items.Add(new IpdItem { Kind = "subend" });
            }
            return true;
        }
        if (cls.Contains("ipdPageTitle"))
        {
            (var titleInner, ic.pos) = DivInner(ic, ic.seg, ic.pos);
            ic.items.Add(new IpdItem { Kind = "title", Text = Flat(ic, titleInner) });
            return true;
        }
        if (cls.Contains("ipdSectionTitle"))
        {
            (var sectionInner, ic.pos) = DivInner(ic, ic.seg, ic.pos);
            ic.items.Add(new IpdItem { Kind = "sectitle", Text = Flat(ic, sectionInner) });
            return true;
        }
        if (cls.Contains("ipdSubSectionTitle"))
        {
            (var inner, ic.pos) = DivInner(ic, ic.seg, ic.pos);
            var t = Flat(ic, inner);
            if (ic.items.Count > 0 && ic.items[^1].Kind == "subbegin") ic.items[^1].Text = t;
            return true;
        }
        if (cls.Contains("ipdSubSection") && !cls.Contains("Contents"))
        {
            ic.items.Add(new IpdItem { Kind = "subbegin" });
            ic.depthNow++;
            ic.subDepth.Push(ic.depthNow);
            return true;
        }
        // a plain inner div carrying bare text (the "nodata" messages)
        ic.depthNow++;
        var tail = ic.seg[ic.pos..];
        var nextTag = ic.tagRx.Match(tail);
        var lead = nextTag.Success ? tail[..nextTag.Index] : tail;
        if (Flat(ic, lead) is { Length: > 0 } bare
            && !cls.Contains("ipdSection") && !cls.Contains("Contents")
            && !cls.Contains("slfGeneralPayment") && !cls.Contains("slfOverPay"))
            ic.items.Add(new IpdItem { Kind = "text", Text = bare, Fs = 9 });
        return true;
    }
}
