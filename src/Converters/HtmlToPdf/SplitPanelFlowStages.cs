using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The stages of the split-panel flow parse: the run flush, the block close and one token.
    private static void FlushSpRun(SplitPanelFlowState pf)
    {
        if (pf.text.Length == 0) return;
        var runText = CollapseWs(pf.text.ToString());
        if (runText.Length == 0 || runText == " ")
        {
            // pure whitespace between markup: keep a single separating space
            if (runText == " " && pf.cur.Runs.Count > 0
                && !pf.cur.Runs[^1].Text.EndsWith(' '))
                pf.cur.Runs[^1].Text += " ";
            pf.text.Clear();
            return;
        }
        var (face, fs, col) = pf.faceStack[^1];
        // Arial at the letter sizes draws bold through <strong>; the metric
        // face carries the weight.
        pf.cur.Runs.Add(new SpRun
        {
            Text = runText, Face = face, Fs = fs,
            Bold = pf.boldDepth > 0, Col = col,
        });
        pf.text.Clear();
    }

    private static void CloseSpBlock(SplitPanelFlowState pf, bool pMargin = false)
    {
        FlushSpRun(pf);
        if (pf.cur.Runs.Count == 0 && !pf.cur.Hr)
        {
            // an empty close: a <br> is a HARD blank line; a paragraph
            // boundary is a MARGIN — adjacent margins collapse to one and
            // vanish entirely at the flow start
            if (pMargin)
            {
                if (pf.blocks.Count > 0) pf.pendingPMargin = true;
                pf.cur = new SpBlock { Indent = pf.indent, RightIndent = pf.indent };
                return;
            }
            pf.cur.BlankFs = pf.faceStack[^1].Fs;
            pf.blocks.Add(pf.cur);
            pf.cur = new SpBlock { Indent = pf.indent, RightIndent = pf.indent };
            return;
        }
        // content arrives: a pending collapsed margin becomes one blank line
        if (pf.pendingPMargin)
        {
            pf.blocks.Add(new SpBlock { BlankFs = pf.faceStack[^1].Fs });
            pf.pendingPMargin = false;
        }
        pf.blocks.Add(pf.cur);
        // a paragraph boundary AFTER content leaves its margin pending too
        if (pMargin) pf.pendingPMargin = true;
        pf.cur = new SpBlock { Indent = pf.indent, RightIndent = pf.indent };
    }
}
