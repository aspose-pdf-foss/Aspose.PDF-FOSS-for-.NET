using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public partial class Table
{
    /// <summary>The HTML column minima settle the grid: the columns take their min-content floors first and share what the box has left.</summary>
    private void FitHtmlColumnMinima(ColumnWidthState pw, double availableWidth)
    {
        if (HtmlColMinPt is { } hMins && hMins.Length == pw.widths.Length && availableWidth > 0)
        {
            var hMaxs = HtmlColMaxPt is { } hm && hm.Length == pw.widths.Length ? hm : null;
            double sumMax = 0;
            if (hMaxs is not null) foreach (var m in hMaxs) sumMax += m;
            if (hMaxs is not null && sumMax > 0 && sumMax <= availableWidth + 0.01)
            {
                var surplus = availableWidth - sumMax;
                for (var i = 0; i < pw.widths.Length; i++)
                    pw.widths[i] = hMaxs[i];
                if (HtmlSurplusCol >= 0 && HtmlSurplusCol < pw.widths.Length)
                    pw.widths[HtmlSurplusCol] += surplus;
                else
                {
                    // CSS auto layout: the surplus belongs to the columns that
                    // DECLARED a percent — up to that percent of the box — while the
                    // auto columns hug their max-content (the risks pill: only
                    // `.CategoryName {width:80%}` grows, the detail button keeps its
                    // min-content 25 pt). Whatever the declared shares cannot hold
                    // falls back to the proportional rule.
                    surplus -= GrowDeclaredPercentColumns(pw.widths, pw.declShare, surplus);
                    if (HtmlUaAutoYield) YieldAutoColumnsToDeclared(pw, hMins, hMaxs);
                    if (surplus > 0.01)
                    {
                        // …and a column with a DECLARED absolute width is FIXED: the
                        // surplus belongs to the auto columns beside it.
                        double sumAutoMax = 0;
                        for (var i = 0; i < pw.widths.Length; i++)
                            if (!HtmlColFixed(pw, i)) sumAutoMax += hMaxs[i];
                        var share = sumAutoMax > 0 ? sumAutoMax : sumMax;
                        for (var i = 0; i < pw.widths.Length; i++)
                            if (sumAutoMax <= 0 || !HtmlColFixed(pw, i))
                                pw.widths[i] += surplus * hMaxs[i] / share;
                    }
                }
            }
            else
            {
                // Floors, then surplus grows each column toward its MAX-CONTENT
                // (∝ remaining room): the wide text columns get
                // the space, not the already-satisfied pill column. The emitted
                // shares only matter when they exceed a column's min (a truly
                // DECLARED percent like the milestone 13%s).
                for (var i = 0; i < pw.widths.Length; i++)
                    pw.widths[i] = HtmlColPctDeclared ? Math.Max(pw.widths[i], hMins[i]) : hMins[i];
                double sumW = 0;
                foreach (var w in pw.widths) sumW += w;
                if (sumW > availableWidth + 0.01)
                {
                    var excess = sumW - availableWidth;
                    double slack = 0;
                    for (var i = 0; i < pw.widths.Length; i++) slack += pw.widths[i] - hMins[i];
                    if (slack > 0)
                    {
                        for (var i = 0; i < pw.widths.Length; i++)
                            pw.widths[i] -= (pw.widths[i] - hMins[i]) / slack * Math.Min(excess, slack);
                        excess -= Math.Min(excess, slack);
                    }
                    // Already on the floors and STILL over the box: the floors
                    // themselves squeeze, proportionally. A grid that keeps them
                    // overhangs its host cell's right edge instead (the milestone
                    // grid's Notes column stuck 3 pt past the report frame) —
                    // narrowing the columns keeps the grid inside.
                    if (excess > 0.01)
                    {
                        double onFloors = 0;
                        foreach (var w in pw.widths) onFloors += w;
                        if (onFloors > 0)
                            for (var i = 0; i < pw.widths.Length; i++)
                                pw.widths[i] *= availableWidth / onFloors;
                    }
                }
                else if (sumW > 0 && sumW < availableWidth - 0.01)
                {
                    var surplus = availableWidth - sumW;
                    if (HtmlSurplusCol >= 0 && HtmlSurplusCol < pw.widths.Length)
                        pw.widths[HtmlSurplusCol] += surplus;
                    else if (hMaxs is not null)
                    {
                        double sumRoom = 0;
                        for (var i = 0; i < pw.widths.Length; i++)
                            if (!HtmlColFixed(pw, i)) sumRoom += Math.Max(0, hMaxs[i] - pw.widths[i]);
                        if (sumRoom > 0)
                            for (var i = 0; i < pw.widths.Length; i++)
                                pw.widths[i] += HtmlColFixed(pw, i) ? 0
                                    : surplus * Math.Max(0, hMaxs[i] - pw.widths[i]) / sumRoom;
                        else
                            for (var i = 0; i < pw.widths.Length; i++)
                                pw.widths[i] += surplus * pw.widths[i] / sumW;
                    }
                    else
                        for (var i = 0; i < pw.widths.Length; i++)
                            pw.widths[i] += surplus * pw.widths[i] / sumW;
                }
            }
        }
    }
}
