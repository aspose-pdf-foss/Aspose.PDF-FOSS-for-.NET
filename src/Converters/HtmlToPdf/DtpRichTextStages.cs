using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The stages of the DTP rich-text parse: the line flush, the current line, one token step and its text, block and span arms.
    private static void FlushDtpLine(DtpRichTextState rt)
    {
        if (rt.cur is null) return;
        // Drop a line that is pure collapsible whitespace.
        var any = false;
        foreach (var r in rt.cur.Runs) if (r.Text.Trim(' ').Length > 0) any = true;
        if (any || rt.cur.Runs.Count > 0 && rt.cur.Runs[0].Text.Length > 0)
            rt.lines.Add(rt.cur);
        rt.cur = null;
    }

    private static DtpLogicalLine CurDtpLine(DtpRichTextState rt)
    {
        if (rt.cur is null)
            rt.cur = new DtpLogicalLine
            {
                Align = rt.alignStack.Peek(),
                Mso = rt.inMso,
                FirstIndent = rt.inMso ? rt.msoFirstIndent : 0,
                HangIndent = rt.inMso ? rt.msoHangIndent : 0,
            };
        return rt.cur;
    }

    /// <summary></summary>
    private static bool DtpRichTextStep(DtpRichTextState rt)
    {
        rt.tm = rt.tokRx.Match(rt.inner, rt.idx);
        rt.textEnd = rt.tm.Success ? rt.tm.Index : rt.inner.Length;
        if (rt.textEnd > rt.idx)
        {
            AppendDtpText(rt);
            return true;
        }
        if (!rt.tm.Success) return false;
        rt.close = rt.tm.Groups[1].Value.Length > 0;
        rt.name = rt.tm.Groups[2].Value.ToLowerInvariant();
        rt.attrs = rt.tm.Groups[3].Value;
        switch (rt.name)
        {
            case "br":
                // A <br> on an empty line is a deliberate blank line (the
                // corpus separates paragraphs with <br><br>).
                if (rt.cur is null) rt.lines.Add(new DtpLogicalLine { Align = rt.alignStack.Peek() });
                else FlushDtpLine(rt);
                break;
            case "div":
            case "p":
                ApplyDtpBlockTag(rt);
                break;
            case "strong":
            case "b":
                rt.bold += rt.close ? -1 : 1;
                if (rt.bold < 0) rt.bold = 0;
                break;
            case "u":
                rt.under += rt.close ? -1 : 1;
                if (rt.under < 0) rt.under = 0;
                break;
            case "i":
            case "em":
                rt.ital += rt.close ? -1 : 1;
                if (rt.ital < 0) rt.ital = 0;
                break;
            case "a":
                rt.link += rt.close ? -1 : 1;
                if (rt.link < 0) rt.link = 0;
                break;
            case "span":
                ApplyDtpSpanTag(rt);
                break;
        }
        rt.idx = rt.tm.Index + rt.tm.Length;
        return true;
    }
}
