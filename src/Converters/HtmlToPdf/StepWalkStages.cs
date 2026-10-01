using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The step-content walk's line flush and text emitter.
    private static void FlushStepLine(StepWalkState ws)
    {
        while (ws.line.Segs.Count > 0 && ws.line.Segs[^1].Text is { } t && t.Trim().Length == 0)
            ws.line.Segs.RemoveAt(ws.line.Segs.Count - 1);
        ws.line.FontPt = ws.headingPt;
        if (ws.headingLinePt > 0) ws.line.LinePt = ws.headingLinePt;
        else if (ws.inChoice && ws.line.LinePt <= 0) ws.line.LinePt = SwmOptionPitch;
        // a paragraph sets on the line box the document declares for it, grown to the
        // blank's own box where the line carries one
        else if (ws.inPara && ws.line.LinePt <= 0 && _stepParaLinePt > 0)
        {
            ws.line.LinePt = _stepParaLinePt;
            // a blank is seated ON the baseline, so the box it needs is added under
            // the line rather than shared around it
            if (ws.line.Segs.Exists(sg => sg.BlankPt > 0) && SwElementLinePt > _stepParaLinePt)
            {
                ws.line.AscentLinePt = _stepParaLinePt;
                ws.line.LinePt = SwElementLinePt;
            }
        }
        // …and a full-width row's paragraph takes the LINKED sheet's p line box
        // when the document's own styles declare none (see _stepLinkedParaLinePt)
        else if (ws.inPara && ws.line.LinePt <= 0 && _stepRowColFull && _stepLinkedParaLinePt > 0)
        {
            ws.line.LinePt = _stepLinkedParaLinePt;
            if (ws.line.Segs.Exists(sg => sg.BlankPt > 0) && SwElementLinePt > _stepLinkedParaLinePt)
            {
                ws.line.AscentLinePt = _stepLinkedParaLinePt;
                ws.line.LinePt = SwElementLinePt;
            }
        }
        if (ws.line.Segs.Count > 0)
        {
            // a label that ends the line with nothing in it draws nothing and still
            // asks for its margin
            ws.line.TrailPadPt = ws.pendingPad;
            ws.items.Add(new StepItem { Line = ws.line, GapBefore = ws.gapNext, KeepWithNext = ws.inDetable });
            ws.gapNext = 0;
        }
        ws.line = new StepLine();
    }

    private static void EmitStepText(StepWalkState ws, string raw)
    {
        var text = Regex.Replace(DecodeEntities(raw).Replace(' ', ' '), @"\s+", " ");
        foreach (var piece in Regex.Split(text, "([⃝☐◯⬤])"))
        {
            if (piece.Length == 0) continue;
            if (piece is "⃝" or "◯")
            { ws.line.Segs.Add(new StepSeg { Radio = true }); ws.pendingPad = 0; continue; }
            // the form face has no BLACK LARGE CIRCLE: the selected option's
            // glyph renders as the missing-glyph box, same ink as the checkbox
            if (piece is "☐" or "⬤")
            { ws.line.Segs.Add(new StepSeg { Checkbox = true }); ws.pendingPad = 0; continue; }
            if (piece.Trim().Length == 0)
            {
                ws.pSawText = true;
                if (ws.line.Segs.Count > 0 && !(ws.line.Segs[^1].Text is { } pt && pt.EndsWith(' ')))
                    ws.line.Segs.Add(new StepSeg { Text = " " });
                continue;
            }
            ws.line.Segs.Add(new StepSeg { Text = piece, Bold = ws.boldDepth > 0, PadLeftPt = ws.pendingPad });
            ws.pendingPad = 0;
            ws.pHadContent = true;
        }
    }

    /// <summary>Consume one token of the step HTML at the cursor: a text run, a break, or an opening/closing tag that opens, styles or closes the current step line.</summary>
    private static bool WalkStepToken(StepWalkState ws, string html)
    {
        if (html[ws.i] != '<')
        {
            var j = html.IndexOf('<', ws.i);
            if (j < 0) j = ws.n;
            EmitStepText(ws, html[ws.i..j]);
            ws.i = j;
            return true;
        }
        ws.end = html.IndexOf('>', ws.i);
        if (ws.end < 0) return false;
        ws.tagStr = html[ws.i..(ws.end + 1)];
        ws.nm = Regex.Match(ws.tagStr, @"^</?\s*([A-Za-z][A-Za-z0-9]*)");
        if (!ws.nm.Success) { ws.i = ws.end + 1; return true; }
        ws.tag = ws.nm.Groups[1].Value.ToLowerInvariant();
        ws.isClose = ws.tagStr[1] == '/';
        ws.selfClosed = ws.tagStr.EndsWith("/>", StringComparison.Ordinal);
        ws.cls = Regex.Match(ws.tagStr, @"class\s*=\s*(['""])([^'""]*)\1", RegexOptions.IgnoreCase).Groups[2].Value;
        ws.style = Regex.Match(ws.tagStr, @"style\s*=\s*(['""])([^'""]*)\1", RegexOptions.IgnoreCase).Groups[2].Value;

        // An author's own table in the step content — no widget wrapper around it —
        // still lays out as a grid rather than dissolving into the line stream.
        if (WalkStepInlineTags(ws, html)) return true;
        if (WalkStepStructureTags(ws, html)) return true;

        if (WalkStepClassArms(ws, html)) return true;
        // a heading is a block of its own, like a paragraph: it ends the line the
        // content before it was on, then sets at its own size with its own margins
        if (WalkStepWidgetTags(ws, html)) return true;
        ws.i = ws.end + 1;
        return true;
    }
}
