using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>One section of the resume: the name block, the contact block, or a titled section of paragraphs and lists.</summary>
    private static bool RenderResumeSection(ResumeDocState rd, Match secM)
    {
        rd.kind = secM.Groups[1].Value.ToUpperInvariant();
        rd.inner = BalancedInner(rd.c, secM.Index + secM.Length, "div") ?? "";
        if (rd.inner.Trim().Length == 0) return true;

        rd.titleM = Regex.Match(rd.inner, @"class=""sectiontitle"">([^<]*)</div>");
        if (rd.kind == "NAME")
        {
            if (RenderResumeName(rd)) return true;
        }
        if (rd.kind == "CNTC")
        {
            if (RenderResumeContact(rd)) return true;
        }

        // a titled body section
        rd.y += rd.firstSection ? 0 : 6.0;              // section margin-top
        rd.firstSection = false;
        if (rd.titleM.Success)
        {
            BreakIf(rd, 15.0 + RdTitleToParaPt);
            var title = CollapseWs(DecodeEntities(rd.titleM.Groups[1].Value)).Trim();
            rd.frags.Add((0, RdPadXPt, rd.y + Drop(rd, 13, 15), 13, true, rd.black, title));
            // title base → first content line base = 16.25 (measured)
            rd.y += RdTitleToParaPt - Drop(rd, 11, RdLinePt) + Drop(rd, 13, 15);
        }
        rd.seq++;
        rd.paraN = 0;
        foreach (Match paraM in Regex.Matches(rd.inner,
            @"<div id=""PARAGRAPH_[^""]*"" class=""paragraph[^""]*""[^>]*>",
            RegexOptions.IgnoreCase))
        {
            if (!RenderResumeParagraph(rd, paraM)) break;
        }
        // last content line base → next title base = 20.75 (measured):
        // the walk already advanced one line box past the base
        rd.y += RdParaToTitlePt - RdLinePt - (Drop(rd, 13, 15) - Drop(rd, 11, RdLinePt)) - 6.0;
        return true;
    }

    /// <summary>One paragraph or list of a titled section: its runs emitted at the column with page breaks as needed.</summary>
    private static bool RenderResumeParagraph(ResumeDocState rd, Match paraM)
    {
        var pInner = BalancedInner(rd.inner, paraM.Index + paraM.Length, "div") ?? "";
        if (rd.paraN++ > 0) rd.y += 6.0;            // paragraph margin-top
        if (pInner.Contains("table", StringComparison.OrdinalIgnoreCase)
            && pInner.Contains("twocol", StringComparison.Ordinal))
        {
            // the two-column skills grid: bullets in 50% columns
            var tds = Regex.Matches(pInner, @"<td[^>]*class=""field twocol_\d""[^>]*>(.*?)</td>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var colTops = rd.y;
            double maxY = rd.y;
            for (var ti = 0; ti < tds.Count; ti++)
            {
                rd.y = colTops;
                var colX = RdPadXPt + ti * (RdColWPt / 2 + 0.5);
                EmitLis(rd, tds[ti].Groups[1].Value, colX, RdColWPt / 2, rd.seq);
                maxY = Math.Max(maxY, rd.y);
            }
            rd.y = maxY;
        }
        else if (pInner.Contains("paddedline", StringComparison.Ordinal))
        {
            // a JOB paragraph: title/date line, company line, bullets
            var spl = Regex.Matches(pInner, @"<span class=""paddedline""[^>]*>(.*?)(?:<br>\s*)?</span>(?=\s*<span|\s*</div>|\s*$)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var jobRuns = new List<(string, bool, Color)>();
            foreach (Match sM in spl)
            {
                if (sM.Groups[1].Value.Contains("<ul", StringComparison.OrdinalIgnoreCase))
                    continue;
                foreach (Match innerSpan in Regex.Matches(sM.Groups[1].Value,
                    @"<span([^>]*)>(.*?)</span>", RegexOptions.Singleline))
                {
                    var txt = CollapseWs(DecodeEntities(
                        Regex.Replace(innerSpan.Groups[2].Value, "<[^>]+>", "")));
                    if (txt.Length == 0) continue;
                    var boldSpan = Regex.IsMatch(innerSpan.Groups[1].Value,
                        @"jobtitle|companyname|degree", RegexOptions.IgnoreCase);
                    jobRuns.Add((txt, boldSpan, rd.black));
                }
                var hasBr = sM.Value.Contains("<br", StringComparison.OrdinalIgnoreCase);
                if (hasBr && jobRuns.Count > 0)
                {
                    BreakIf(rd, RdLinePt);
                    var x = RdPadXPt;
                    foreach (var (t, bold, col) in jobRuns)
                    {
                        rd.frags.Add((rd.seq, x, rd.y + Drop(rd, 11, RdLinePt), 11, bold, col, t));
                        x += W(rd, t, bold, 11);
                    }
                    rd.y += RdLinePt;
                    jobRuns.Clear();
                }
            }
            if (jobRuns.Count > 0)
            {
                BreakIf(rd, RdLinePt);
                var x = RdPadXPt;
                foreach (var (t, bold, col) in jobRuns)
                {
                    rd.frags.Add((rd.seq, x, rd.y + Drop(rd, 11, RdLinePt), 11, bold, col, t));
                    x += W(rd, t, bold, 11);
                }
                rd.y += RdLinePt;
            }
            var ulM = Regex.Match(pInner, @"<ul>(.*?)</ul>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (ulM.Success) EmitLis(rd, ulM.Groups[1].Value, RdPadXPt, RdColWPt, rd.seq);
        }
        else if (pInner.Contains("<ul", StringComparison.OrdinalIgnoreCase))
        {
            var ulM = Regex.Match(pInner, @"<ul>(.*?)</ul>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (ulM.Success) EmitLis(rd, ulM.Groups[1].Value, RdPadXPt, RdColWPt, rd.seq);
        }
        else
        {
            var fieldM = Regex.Match(pInner,
                @"<div class=""field singlecolumn""[^>]*>(.*?)</div>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (fieldM.Success)
                EmitRuns(rd, ParseRuns(rd, fieldM.Groups[1].Value), RdPadXPt, RdColWPt,
                    11, RdLinePt, rd.seq);
        }
        return true;
    }

    /// <summary>The contact block: its label/value rows in two columns.</summary>
    private static bool RenderResumeContact(ResumeDocState rd)
    {
        // the inline address list: items joined by 13 pt bullet
        // separators on one CENTRED 12 pt line, wrapping items whole
        rd.y += 4.0;                             // address margin-top
        var lis = new List<List<(string t, bool bold, Color col)>>();
        foreach (Match liM in Regex.Matches(rd.inner, @"<li[^>]*>(.*?)</li>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
            lis.Add(ParseRuns(rd, liM.Groups[1].Value));
        var lineFr = new List<(double w, double fs, double dy, string t)>();
        var lineList = new List<List<(double w, double fs, double dy, string t)>> { lineFr };
        double used = 0;
        for (var i = 0; i < lis.Count; i++)
        {
            var parts = new List<(double w, double fs, double dy, string t)>();
            foreach (var (t, _, _) in lis[i])
            {
                var txt = CollapseWs(t);
                if (txt.Trim().Length == 0 && txt.Length <= 1 && parts.Count == 0) continue;
                if (txt.Length == 0) continue;
                parts.Add((W(rd, txt, false, 10), 10, 0, txt));
            }
            double itemW = 0;
            foreach (var p in parts) itemW += p.w;
            if (used > 0 && used + itemW > RdColWPt)
            { lineFr = new List<(double, double, double, string)>(); lineList.Add(lineFr); used = 0; }
            lineFr.AddRange(parts); used += itemW;
            if (i < lis.Count - 1)
            {
                // the trailing separator: a text-size space + the
                // 13 pt bullet riding 1.13 LOW
                lineFr.Add((2.5, 10, 0, " "));
                lineFr.Add((W(rd, "• ", false, 13), 13, 1.13, i == 0 ? "• " : "•"));
                used += 2.5 + 11.13;
            }
        }
        foreach (var ln in lineList)
        {
            if (ln.Count == 0) continue;
            BreakIf(rd, 12.0);
            double lw = 0;
            foreach (var p in ln) lw += p.w;
            var x = RdPadXPt + (RdColWPt - lw) / 2;
            foreach (var (w2, fs, dy, t) in ln)
            {
                rd.frags.Add((2, x, rd.y + Drop(rd, 10, 12) + dy, fs, false, rd.black, t));
                x += w2;
            }
            rd.y += 12.0;
        }
        rd.firstSection = false;
        return true;
    }

    /// <summary>The name block: the name and role lines at the head of the resume.</summary>
    private static bool RenderResumeName(ResumeDocState rd)
    {
        rd.y += 6.0;                             // section margin-top
        var nameM = Regex.Match(rd.inner, @"<div class=""name"">(.*?)</div>",
            RegexOptions.Singleline);
        if (nameM.Success)
        {
            BreakIf(rd, 31.0);
            var runs = ParseRuns(rd, nameM.Groups[1].Value);
            double total = 0;
            foreach (var (t, _, _) in runs) total += W(rd, CollapseWs(t), true, 23);
            var x = RdPadXPt + (RdColWPt - total) / 2;
            foreach (var (t, _, col) in runs)
            {
                var txt = CollapseWs(t);
                if (txt.Length == 0) continue;
                rd.frags.Add((1, x, rd.y + Drop(rd, 23, 31), 23, true, col, txt));
                x += W(rd, txt, true, 23);
            }
            rd.y += 31.0;
        }
        // the 3 pt lowerborder rule under the name
        if (rd.inner.Contains("lowerborder", StringComparison.Ordinal))
        {
            rd.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(rd.inv,
                $"q 0 0 0 rg {RdPadXPt:F2} {rd.pageHeight - rd.y - 5.0:F2} {RdColWPt:F2} 3 re f Q\n")));
            rd.y += 5.0;
        }
        rd.firstSection = false;
        return true;
    }
}
