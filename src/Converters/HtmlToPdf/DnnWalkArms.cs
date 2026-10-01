using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The class arms of the DNN wrapper walk: a row pad, a field label, a field control, a module header and a module body.</summary>
    private static void DnnWalkRowPad(DnnWalkState dw)
    {
        FlushDnnRow(dw);
        var body = DnnInnerDiv(dw.inner, dw.openIdx);
        if (body is null) { dw.pos = dw.openIdx + dw.open.Length; return; }
        dw.pos = dw.openIdx + dw.open.Length + body.Length + "</div>".Length;
        if (!body.Contains("ContainerField", StringComparison.OrdinalIgnoreCase)
            && DnnPlainText(body).Length == 0)
        {
            // no visible content: bare padding — but an &nbsp; keeps
            // its full line box between the pads (23 px, like a row)
            var nbsp = body.Contains("&nbsp;", StringComparison.OrdinalIgnoreCase)
                || body.IndexOf(' ') >= 0;
            dw.blocks.Add(new DnnGap { H = (nbsp ? 23 : 10) * 0.75 });
            dw.lastRowPad = true;
            return;
        }
        // Nested RowPads keep their own pads, but a wrapper whose
        // content ENDS with a RowPad drops its bottom pad — the
        // measured 17.25 pt row pitch holds across ContactNamePanel's
        // double wrap where naive padding would add 3.75.
        dw.blocks.Add(new DnnGap { H = 5 * 0.75 });        // padding-top
        var endsWithRowPad = DnnWalk(body, dw.indentPx, dw.blocks, true);
        if (!endsWithRowPad)
            dw.blocks.Add(new DnnGap { H = 5 * 0.75 });    // padding-bottom
        dw.lastRowPad = true;
    }

    /// <summary></summary>
    private static void DnnWalkFieldControl(DnnWalkState dw)
    {
        var body = DnnInnerDiv(dw.inner, dw.openIdx);
        if (body is null) { dw.pos = dw.openIdx + dw.open.Length; return; }
        var wm = Regex.Match(dw.st, @"width\s*:\s*([\d.]+)px", RegexOptions.IgnoreCase);
        if (wm.Success) dw.runX += DtpNum(wm.Groups[1].Value);
        if (dw.row is { Pairs.Count: > 0 } && dw.row.Pairs[^1].Value.Length == 0)
        {
            var last = dw.row.Pairs[^1];
            dw.row.Pairs[^1] = (last.Label, last.LabelW, DnnControlValue(body), last.PairX);
        }
        dw.pos = dw.openIdx + dw.open.Length + body.Length + "</div>".Length;
    }

    /// <summary></summary>
    private static void DnnWalkFieldLabel(DnnWalkState dw)
    {
        var body = DnnInnerDiv(dw.inner, dw.openIdx);
        if (body is null) { dw.pos = dw.openIdx + dw.open.Length; return; }
        var wm = Regex.Match(dw.st, @"width\s*:\s*([\d.]+)px", RegexOptions.IgnoreCase);
        // Controls.css .ContainerFieldLabelHoriz { width: 170px } —
        // inline widths override; border-box, text pads 10px inside.
        var wpx = wm.Success ? DtpNum(wm.Groups[1].Value) : 170.0;
        dw.row ??= new DnnFieldRow { IndentPx = dw.indentPx, Bare = !dw.inRowPad };
        if (dw.row.Pairs.Count > 0) dw.runX += 10;          // CellLeft gap (measured)
        dw.row.Pairs.Add((DnnPlainText(body), wpx * 0.75, "", dw.runX * 0.75));
        dw.runX += wpx;
        dw.pos = dw.openIdx + dw.open.Length + body.Length + "</div>".Length;
    }

    /// <summary></summary>
    private static void DnnWalkModuleHeader(DnnWalkState dw)
    {
        FlushDnnRow(dw);
        var body = DnnInnerDiv(dw.inner, dw.openIdx);
        if (body is null) { dw.pos = dw.openIdx + dw.open.Length; return; }
        var isHeader = dw.cls.Contains("ModuleHeaderContainer", StringComparison.OrdinalIgnoreCase);
        dw.blocks.Add(new DnnBand
        {
            Text = DnnPlainText(DnnStripHidden(body)),
            Header = isHeader,
        });
        dw.pos = dw.openIdx + dw.open.Length + body.Length + "</div>".Length;
    }

    /// <summary></summary>
    private static void DnnWalkModuleBody(DnnWalkState dw)
    {
        FlushDnnRow(dw);
        var body = DnnInnerDiv(dw.inner, dw.openIdx);
        if (body is null) { dw.pos = dw.openIdx + dw.open.Length; return; }
        dw.blocks.Add(new DnnGap { H = 5 * 0.75 });      // padding-top 5px
        DnnWalk(body, dw.indentPx + 10, dw.blocks);         // padding-left 10px
        dw.blocks.Add(new DnnGap { H = 10 * 0.75 });     // padding-bottom 10px
        dw.pos = dw.openIdx + dw.open.Length + body.Length + "</div>".Length;
    }
}
