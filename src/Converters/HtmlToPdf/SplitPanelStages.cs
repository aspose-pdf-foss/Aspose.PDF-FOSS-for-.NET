using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Split panel: one laid-out sidebar block emitted.</summary>
    private static bool EmitSidebarBlock(SplitPanelState sp, SpBlock b, List<List<(SpRun Run, string Text)>> lines, double pitch, (double asc, double sum) pfm)
    {
        if (b.Hr)
        {
            SpHrGroove(sp.page, sp.sbX0 + 3.0, sp.sbX1 - 3.0, sp.sbY + SpLinePitch(9.75), sp.pageHeight, sp.invc);
            sp.sbY += pitch;
            return true;
        }
        if (lines.Count == 0) { sp.sbY += pitch; return true; }
        foreach (var ln in lines)
        {
            var lx = sp.sbX0 + 3.0;
            var baseY = sp.sbY + MetricBaselineDrop(9.75, pitch, pfm);
            foreach (var (r, t) in ln)
            {
                SpEmitRun(sp.page, sp.docFontDict, r, t, lx, sp.pageHeight - baseY, sp.invc);
                lx += MeasureFaceText(r.Face + (r.Bold ? " Bold" : ""), t, r.Fs);
            }
            sp.sbY += pitch;
        }
        return true;
    }

    /// <summary>Split panel: one sidebar block wrapped and its height accumulated.</summary>
    private static bool LayoutSidebarBlock(SplitPanelState sp, SpBlock b)
    {
        if (b.Hr)
        {
            var pitchH = SpLinePitch(9.75) + 6.1;
            sp.sbLaid.Add((b, new List<List<(SpRun, string)>>(), pitchH, (0.905, 1.15)));
            sp.sbH += pitchH;
            return true;
        }
        if (b.Runs.Count == 0)
        {
            var pitchB = SpLinePitch(b.BlankFs > 0 ? b.BlankFs : 9.75);
            sp.sbLaid.Add((b, new List<List<(SpRun, string)>>(), pitchB, (0.905, 1.15)));
            sp.sbH += pitchB;
            return true;
        }
        var maxFs = 0.0;
        string maxFace = "Arial";
        foreach (var r in b.Runs)
            if (r.Fs > maxFs) { maxFs = r.Fs; maxFace = r.Face; }
        var pitch = SpLinePitch(maxFs);
        var pfm = WinMetricsFor(maxFace) ?? (0.905, 1.15);
        var lines = SpWrap(b, sp.sbW);
        sp.sbLaid.Add((b, lines, pitch, pfm));
        sp.sbH += lines.Count * pitch;
        return true;
    }

    /// <summary>Split panel: one main-column block wrapped and emitted.</summary>
    private static bool EmitMainBlock(SplitPanelState sp, SpBlock b)
    {
        if (b.Hr)
        {
            // the groove sits one blank line down, with a short seat below
            var hrW = sp.mainW - b.Indent - b.RightIndent;
            var hx0 = sp.mainX0 + sp.mainPad + b.Indent;
            var hrPitch = SpLinePitch(9.75);
            SpHrGroove(sp.pages[sp.pageIdx], hx0, hx0 + hrW, sp.mainY + hrPitch, sp.pageHeight, sp.invc);
            sp.mainY += hrPitch + 6.1;
            return true;
        }
        if (b.Runs.Count == 0)
        {
            sp.mainY += SpLinePitch(b.BlankFs > 0 ? b.BlankFs : 9.75);
            return true;
        }
        var maxFs = 0.0;
        string maxFace = "Arial";
        foreach (var r in b.Runs)
            if (r.Fs > maxFs) { maxFs = r.Fs; maxFace = r.Face; }
        var pitch = SpLinePitch(maxFs);
        var pfm = WinMetricsFor(maxFace) ?? (0.891, 1.15);
        var lines = SpWrap(b, sp.mainW - b.Indent - b.RightIndent);
        foreach (var ln in lines)
        {
            if (sp.mainY + pitch > sp.flowBottom && sp.pageIdx == 0)
            {
                sp.p1PanelBottom = sp.pageHeight - 72.8;
                var p2 = sp.doc.Pages.Add(sp.pageWidth, sp.pageHeight);
                EnsureFonts(p2, sp.docFontDict);
                sp.pages.Add(p2);
                sp.pageIdx = 1;
                sp.mainY = 72.0;
            }
            var lx = sp.mainX0 + sp.mainPad + b.Indent;
            var baseY = sp.mainY + MetricBaselineDrop(maxFs, pitch, pfm);
            foreach (var (r, t) in ln)
            {
                SpEmitRun(sp.pages[sp.pageIdx], sp.docFontDict, r, t, lx, sp.pageHeight - baseY, sp.invc);
                lx += MeasureFaceText(r.Face + (r.Bold ? " Bold" : ""), t, r.Fs);
            }
            sp.mainY += pitch;
        }
        return true;
    }

    /// <summary>Split panel: the logo image and the panel heading emitted.</summary>
    private static void EmitPanelLogoAndHeading(SplitPanelState sp)
    {
        sp.imgM = Regex.Match(sp.html[..sp.orphan.Index],
            @"<img\b[^>]*src\s*=\s*[""']?([^""'\s>]+)[^>]*width\s*=\s*[""']?(\d+)[^>]*height\s*=\s*[""']?(\d+)",
            RegexOptions.IgnoreCase);
        if (sp.imgM.Success && LoadConverterImage(sp.imgM.Groups[1].Value, sp.options) is { } logoBytes)
        {
            var iw = double.Parse(sp.imgM.Groups[2].Value, sp.invc) * 0.75;
            var ih = double.Parse(sp.imgM.Groups[3].Value, sp.invc) * 0.75;
            var ix = sp.leftCellX0 + 0.7;
            var iyTd = SpHeaderBandTopPt + 0.7;
            try { sp.page.AddImage(logoBytes, new Rectangle(ix, sp.pageHeight - iyTd - ih, ix + iw, sp.pageHeight - iyTd)); }
            catch { }
        }

        sp.thM = Regex.Match(sp.html[..sp.orphan.Index], @"<th\b[^>]*>([\s\S]*)",
            RegexOptions.IgnoreCase);
        if (sp.thM.Success)
        {
            var thBlocks = SpParseFlow("<b>" + sp.thM.Groups[1].Value); // th = UA bold
            var cy = SpHeaderBandTopPt + SpTitleLine1DropPt;
            var first = true;
            foreach (var b in thBlocks)
            {
                if (b.Hr) continue;
                var lines = SpWrap(b, first ? sp.rightCellX1 - sp.rightCellX0 : SpTitleWrapPt);
                foreach (var ln in lines)
                {
                    double lw = 0;
                    foreach (var (r, t) in ln)
                        lw += MeasureFaceText(r.Face + (r.Bold ? " Bold" : ""), t, r.Fs);
                    var lx = sp.rightCellX0 + (sp.rightCellX1 - sp.rightCellX0 - lw) / 2;
                    foreach (var (r, t) in ln)
                    {
                        SpEmitRun(sp.page, sp.docFontDict, r, t, lx, sp.pageHeight - cy, sp.invc);
                        lx += MeasureFaceText(r.Face + (r.Bold ? " Bold" : ""), t, r.Fs);
                    }
                    cy += first ? SpTitleGapPt : SpTitlePitchPt;
                    first = false;
                }
            }
        }
    }
}
