using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Ishares fact sheet: one label/value row of a column drawn.</summary>
    private static void RenderIfsRow(IsharesFactSheetState ix, int ri)
    {
        var r = ix.col.Rows[ri];
        // greedy label wrap at the label cell width
        var labelLines = new List<string>();
        var cur = new StringBuilder();
        foreach (var word in r.Label.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var trial = cur.Length == 0 ? word : cur + " " + word;
            if (cur.Length == 0 || MW(ix, trial, 8) <= ix.labelW) { cur.Clear(); cur.Append(trial); }
            else { labelLines.Add(cur.ToString()); cur.Clear(); cur.Append(word); }
        }
        if (cur.Length > 0) labelLines.Add(cur.ToString());
        if (labelLines.Count == 0) labelLines.Add("");
        // a wrapping row leads its first line 11.6 under the previous
        // baseline instead of the plain 12.14 row pitch
        if (ri > 0 && labelLines.Count > 1) ix.yRow -= IfsRowPitch - IfsWrapRowLead;

        var yLast = ix.yRow;
        for (var li = 0; li < labelLines.Count; li++)
        {
            // a wrapped continuation indents 6 pt (measured:
            // "depreciation" sits at 132 = 126 + 6)
            var lx = ix.x0 + (li > 0 ? 6.0 : 0);
            yLast = ix.yRow + li * IfsLabelWrapPitch;
            Run(ix, "F8", 8, lx, yLast, labelLines[li]);
        }
        // dot leaders fill the label cell remainder on the LAST line,
        // 2.01 after the text, while a dot's end stays within half a
        // dot of the cell edge
        var lastW = (labelLines.Count > 1 ? 6.0 : 0) + MW(ix, labelLines[^1], 8);
        var dotsStart = ix.x0 + lastW + 2.01;
        var nDots = (int)Math.Floor((ix.labelRight + ix.dotW / 2 - dotsStart) / ix.dotW);
        if (nDots > 3)
            Run(ix, "F8", 8, dotsStart, yLast, new string('.', nDots));
        // value right-aligned at the value edge, on the last label line —
        // an <ins> value seats half a point lower (measured 176.46 against
        // the row's 175.96)
        var yVal = r.ValueIns ? yLast + IfsInsSeat : yLast;
        if (r.Value.Length > 0)
        {
            var vw = MW(ix, r.Value, 8);
            Run(ix, "F8", 8, ix.valueRight - vw, yVal, r.Value);
            if (r.ValueDelta) Delta(ix, ix.valueRight - vw, yVal);
            if (r.ValueIns)
                HRule(ix, ix.valueRight - vw, ix.valueRight, yVal + IfsInsRuleDrop, 0.8);
        }
        // hanging suffixes run on from the value edge; a percent span
        // outside the first row is invisible but advances its width
        // less the -7 pt margin
        var hx = ix.valueRight;
        foreach (var (t, sup, pct) in r.Hang)
        {
            if (pct && ri > 0) { hx += MW(ix, t, 8) - IfsHiddenPctMargin; continue; }
            Run(ix, "F8", sup ? IfsSupFs : 8, hx, sup ? yVal - IfsSupRise : yVal, t);
            hx += MW(ix, t, sup ? IfsSupFs : 8);
        }
        ix.yRow = yLast + IfsRowPitch;
    }
}
