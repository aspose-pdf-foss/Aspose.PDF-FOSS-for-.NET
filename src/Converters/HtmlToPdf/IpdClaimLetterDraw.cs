using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// IPD claim letter helpers: text flattening, div extraction, measurement, page breaks, runs, lines, fills, wrapping and block open/advance.
    private static string Flat(IpdClaimLetterState ic, string s) => Regex.Replace(DecodeEntities(
        Regex.Replace(s, @"<[^>]+>", " ")).Replace(' ', ' '), @"\s+", " ").Trim();

    // Balanced scan: from an opening <div ...> at openEnd, find the matching
    // </div> and return the inner segment.
    private static (string result, int closeEnd) DivInner(IpdClaimLetterState ic, string h, int openEnd)
    {
        int closeEnd = default;
        var depth = 1;
        var i = openEnd;
        foreach (Match m in Regex.Matches(h[openEnd..], @"<(/?)div\b[^>]*>",
            RegexOptions.IgnoreCase))
        {
            depth += m.Groups[1].Value.Length > 0 ? -1 : 1;
            if (depth == 0)
            {
                closeEnd = openEnd + m.Index + m.Length;
                return (h[openEnd..(openEnd + m.Index)], closeEnd);
            }
        }
        closeEnd = h.Length;
        return (h[openEnd..], closeEnd);
    }

    private static double MW(IpdClaimLetterState ic, string s, double fs, bool bold = false)
        => MeasureFaceText(bold ? "Arial Bold" : "Arial", s, fs);

    private static void NewPage(IpdClaimLetterState ic)
    {
        ic.sb = new StringBuilder();
        ic.pages.Add(ic.sb);
        ic.pageHasGrid.Add(false);
    }

    private static void Run(IpdClaimLetterState ic, double fs, double x, double yTd, string text, bool bold = false)
        => ic.sb.AppendLine(Compat.Format(ic.inv,
            $"BT /{(bold ? "F9" : "F8")} {fs:0.##} Tf 1 0 0 1 {x:F2} {IpdPageH - yTd:F2} Tm ({EscapePdfString(text)}) Tj ET"));

    private static void Line(IpdClaimLetterState ic, double x0, double y0, double x1, double y1, double w, string gray = "0.651 0.651 0.651")
        => ic.sb.AppendLine(Compat.Format(ic.inv,
            $"q {gray} RG {w:0.###} w {x0:F2} {IpdPageH - y0:F2} m {x1:F2} {IpdPageH - y1:F2} l S Q"));

    private static void FillRect(IpdClaimLetterState ic, double x0, double yTop, double wpt, double hpt, string rgb)
        => ic.sb.AppendLine(Compat.Format(ic.inv,
            $"q {rgb} rg {x0:F2} {IpdPageH - yTop - hpt:F2} {wpt:F2} {hpt:F2} re f Q"));

    // greedy word wrap at a pixel budget
    private static List<string> Wrap(IpdClaimLetterState ic, string text, double fs, double budget, bool bold = false)
    {
        var lines = new List<string>();
        var cur = new StringBuilder();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var trial = cur.Length == 0 ? word : cur + " " + word;
            if (cur.Length == 0 || MW(ic, trial, fs, bold) <= budget) { cur.Clear(); cur.Append(trial); }
            else { lines.Add(cur.ToString()); cur.Clear(); cur.Append(word); }
        }
        if (cur.Length > 0) lines.Add(cur.ToString());
        if (lines.Count == 0) lines.Add("");
        return lines;
    }

    private static void BreakPage(IpdClaimLetterState ic)
    {
        if (ic.inSub && ic.subTop >= 0)
        {
            Line(ic, 96.38, ic.subTop, 96.38, IpdContentBottom, 0.75);
            Line(ic, 671.62, ic.subTop, 671.62, IpdContentBottom, 0.75);
        }
        NewPage(ic);
        ic.y = IpdContentTop;
        ic.subTop = ic.inSub ? IpdContentTop : -1;
        ic.pendingMargin = 0;
    }

    private static void Advance(IpdClaimLetterState ic, double margin) => ic.pendingMargin = Math.Max(ic.pendingMargin, margin);

    private static double Open(IpdClaimLetterState ic, double ownTop)
    {
        var t = ic.y + Math.Max(ic.pendingMargin, ownTop);
        ic.pendingMargin = 0;
        return t;
    }
}
