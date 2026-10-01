using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Bootstrap rows: a panel emitted - its heading row over a body of paragraphs.</summary>
    private static void EmitBootstrapPanel(BootstrapRowsState bs)
    {
        // a panel: heading (one row of label/value columns, or plain
        // bold text) over a body of paragraphs
        var end = FindDivClose(bs.html, bs.pos);
        var panel = bs.html[bs.pos..end];
        bs.pos = end;
        Advance(bs, 0);
        var panelTop = bs.yTd;
        var headM = Regex.Match(panel,
            @"<div\b[^>]*class\s*=\s*['""]panel-heading['""][^>]*>", RegexOptions.IgnoreCase);
        var headH = 0.0;
        string headBody = "";
        if (headM.Success)
        {
            var hEnd = FindDivClose(panel, headM.Index + headM.Length);
            headBody = panel[(headM.Index + headM.Length)..hEnd];
        }
        var bodM = Regex.Match(panel,
            @"<div\b[^>]*class\s*=\s*['""]panel-body['""][^>]*>", RegexOptions.IgnoreCase);
        string bodBody = "";
        if (bodM.Success)
        {
            var bEnd = FindDivClose(panel, bodM.Index + bodM.Length);
            bodBody = panel[(bodM.Index + bodM.Length)..bEnd];
        }
        // heading: measure first (the fill draws before the text)
        var headRow = Regex.Match(headBody,
            @"<div\b[^>]*class\s*=\s*['""]row['""][^>]*>", RegexOptions.IgnoreCase);
        // draw the heading band behind, then its content
        var headTop = panelTop + 0.75;
        var headContentTop = headTop + BrHeadPadY;
        if (headRow.Success)
        {
            var hrEnd = FindDivClose(headBody, headRow.Index + headRow.Length);
            var hRowBody = headBody[(headRow.Index + headRow.Length)..hrEnd];
            var hBoxL = bs.contentL + 0.75 + BrColPad;
            var hBoxR = bs.contentR - 0.75 - BrColPad;
            var rowH = RenderRow(bs, hRowBody, headContentTop, hBoxL, hBoxR,
                draw: false, headFg: true);
            headH = 2 * BrHeadPadY + rowH;
            Fill(bs, BrHeadBg, bs.contentL + 0.75, headTop, bs.contentR - bs.contentL - 1.5, headH);
            StrokeRect(bs, BrPanelBorder, bs.contentL + 1.1, headTop + 0.35,
                bs.contentR - bs.contentL - 2.2, headH - 0.7, 0.75);
            RenderRow(bs, hRowBody, headContentTop, hBoxL, hBoxR, headFg: true);
        }
        else if (Flat(bs, headBody).Length > 0)
        {
            headH = 2 * BrHeadPadY + BrLineH;
            Fill(bs, BrHeadBg, bs.contentL + 0.75, headTop, bs.contentR - bs.contentL - 1.5, headH);
            EmitRun(bs, "FB", BrFontPt, bs.contentL + BrColPad, headContentTop + bs.drop,
                Flat(bs, headBody), BrHeadFg);
        }
        // body: its paragraphs (an empty .multiline keeps only its margin)
        var byTd = headTop + headH + BrBodyPad;
        foreach (Match pm in Regex.Matches(bodBody, @"<p\b[^>]*>(?<t>[\s\S]*?)</p>",
            RegexOptions.IgnoreCase))
        {
            var pt2 = Flat(bs, pm.Groups["t"].Value);
            foreach (var ln in MeasuredWordWrap(pt2, bs.contentR - bs.contentL - 2 * BrBodyPad, bs.face, BrFontPt))
            {
                if (ln.Length == 0) continue;
                EmitRun(bs, "FA", BrFontPt, bs.contentL + BrBodyPad, byTd + bs.drop, ln, BrText);
                byTd += BrLineH;
            }
            byTd += BrPMb;
        }
        var panelBot = byTd + BrBodyPad;
        // the panel frame: white body box + the success border
        StrokeRect(bs, BrPanelBorder, bs.contentL + 0.35, panelTop + 0.35,
            bs.contentR - bs.contentL - 0.7, panelBot - panelTop - 0.7, 0.75);
        bs.yTd = panelBot;
        bs.pendingMb = BrPanelMb;
    }
}
