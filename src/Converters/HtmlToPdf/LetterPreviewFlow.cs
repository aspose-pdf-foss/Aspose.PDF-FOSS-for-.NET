using System;
using System.Collections.Generic;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    // The letter's page flow: div#content's children in order, a block that cannot end
    // above the sheet's bottom edge leaving its top margin on the page it left (the footer
    // seats under that margin) and opening the next page under the repeated thead with the
    // margin again; a block taller than a fresh page lays its own children out one by one.
    private const double LpBoundary = 1e-6;

    private static void LpRenderPages(LetterPreviewState lp)
    {
        lp.firstPage = true;
        LpOpenPage(lp);
        var pending = 0.0;                          // the margin the previous block left below itself
        foreach (var item in lp.items)
            pending = LpPlaceItem(lp, item, pending);
        // the last block's margin, div#content's and td.main's bottom paddings, then the tfoot
        LpFooter(lp, lp.y + pending + LpDivPad + LpMainPadBottom);
        LpFlush(lp);
    }

    private static void LpOpenPage(LetterPreviewState lp)
    {
        lp.page = lp.doc.Pages.Add(lp.pageWidth, lp.pageHeight);
        LpRegisterFonts(lp.page);
        LpThead(lp);
        lp.y = lp.marginT + LpTheadLine + 2 * LpTheadPad + (lp.firstPage ? LpDivPad : 0);
    }

    /// <summary>The print thead: five bold cells on the 14px line, each at its percent column.</summary>
    private static void LpThead(LetterPreviewState lp)
    {
        var fs = LpFsAt(1);
        var x = lp.contentL;
        var width = lp.contentR - lp.contentL;
        var baseTd = lp.marginT + LpTheadPad + MetricBaselineDrop(fs, LpTheadLine, lp.tm);
        foreach (var (text, pct) in lp.thead)
        {
            LpEmit(lp, LpMakeRun(text, true, fs), x + LpTheadPad, baseTd);
            x += pct * width;
        }
    }

    /// <summary>The tfoot reference, right-aligned on its own row under the fragment.</summary>
    private static void LpFooter(LetterPreviewState lp, double rowTop)
    {
        if (lp.footer.Length == 0) return;
        var run = LpMakeRun(lp.footer, false, LpFsAt(2));
        run.Width = LpMeasure(run.Face, run.Text, run.Fs);
        LpEmit(lp, run, lp.contentR - LpTheadPad - run.Width, rowTop + LpTheadPad + LpDrop(lp, run.Fs));
    }

    private static void LpBreak(LetterPreviewState lp)
    {
        LpFooter(lp, lp.y);
        LpFlush(lp);
        lp.firstPage = false;
        LpOpenPage(lp);
    }

    /// <summary>Whether a block of <paramref name="h"/> after a gap of <paramref name="gap"/> ends on this page.</summary>
    private static bool LpFits(LetterPreviewState lp, double gap, double h) => lp.y + gap + h <= lp.limit + LpBoundary;

    private static bool LpFitsFresh(LetterPreviewState lp, double gap, double h)
        => lp.marginT + LpTheadLine + 2 * LpTheadPad + gap + h <= lp.limit + LpBoundary;

    /// <summary>Place one child; returns the margin it leaves below itself.</summary>
    private static double LpPlaceItem(LetterPreviewState lp, LpItem it, double pending)
    {
        switch (it.Kind)
        {
            case LpKind.Br:
                lp.y += LpLineH(lp, LpFsAt(LpContentDepth));
                return 0;
            case LpKind.Hr:
            {
                var margin = LpHrMarginEm * LpFsAt(LpContentDepth);
                lp.y += Math.Max(pending, margin);
                LpHr(lp, lp.innerL, lp.innerR, lp.y);
                lp.y += LpHrHeight;
                return margin;
            }
            case LpKind.HeaderFloat:
                LpPlaceHeader(lp, it, Math.Max(pending, LpDivMargin));
                return 0;
            case LpKind.CenterDiv:
                LpPlaceCentered(lp, it, Math.Max(pending, it.MarginTop));
                return 0;
            case LpKind.Paragraph:
                LpPlaceParagraph(lp, it, Math.Max(pending, LpDivMargin));
                return 0;
            case LpKind.Table:
                LpPlaceTable(lp, it.Table!, pending, lp.innerL, lp.innerR - lp.innerL);
                return 0;
            default:
                LpPlaceTto(lp, it, pending);
                return 0;
        }
    }

    private static void LpPlaceHeader(LetterPreviewState lp, LpItem it, double gap)
    {
        var run = LpMakeRun(it.Text, false, it.Fs);
        var lines = LpWrap(lp, new List<LpRun> { run }, lp.innerR - lp.innerL - it.RightInset, it.Fs, false);
        lp.y += gap + LpDivPad;
        LpDrawLines(lp, lines, lp.innerL, lp.innerR - it.RightInset - lp.innerL, lp.y, false, true);
        lp.y += LpLinesH(lines) + LpDivPad;
    }

    private static void LpPlaceCentered(LetterPreviewState lp, LpItem it, double gap)
    {
        var lines = LpWrap(lp, new List<LpRun> { LpMakeRun(it.Text, it.Bold, it.Fs) }, lp.innerR - lp.innerL - 2 * it.Pad, it.Fs, false);
        var h = 2 * it.Pad + LpLinesH(lines);
        if (!LpFits(lp, gap, h) && LpFitsFresh(lp, gap, h)) { lp.y += gap; LpBreak(lp); }
        lp.y += gap + it.Pad;
        LpDrawLines(lp, lines, lp.innerL + it.Pad, lp.innerR - lp.innerL - 2 * it.Pad, lp.y, true, false);
        lp.y += LpLinesH(lines) + it.Pad;
    }

    /// <summary>div.paragraph (margin, padding, padding-bottom 8px) > div.content (margin,
    /// padding): the bold title's line, then each br-separated segment wrapped.</summary>
    private static void LpPlaceParagraph(LetterPreviewState lp, LpItem it, double gap)
    {
        var textFs = LpFsAt(LpContentDepth + 2);
        var titleFs = LpFsAt(LpContentDepth + 3);
        var width = lp.innerR - lp.innerL - 4 * LpDivPad;
        var lines = new List<LpLine>();
        var first = new List<LpRun> { LpMakeRun(it.Text, true, titleFs) };
        if (!it.TitleAlone && it.Segments.Count > 0) first.Add(LpMakeRun(it.Segments[0], false, textFs));
        lines.AddRange(LpWrap(lp, first, width, textFs, false));
        for (var i = it.TitleAlone ? 0 : 1; i < it.Segments.Count; i++)
            lines.AddRange(it.Segments[i].Length == 0
                ? new List<LpLine> { LpCloseLine(lp, new List<LpRun>(), textFs) }
                : LpWrap(lp, new List<LpRun> { LpMakeRun(it.Segments[i], false, textFs) }, width, textFs, false));
        var inset = LpDivPad + LpDivMargin + LpDivPad;
        var h = inset + LpLinesH(lines) + LpDivPad + LpParaPadBottom;
        if (!LpFits(lp, gap, h) && LpFitsFresh(lp, gap, h)) { lp.y += gap; LpBreak(lp); }
        lp.y += gap + inset;
        LpDrawLines(lp, lines, lp.innerL + 2 * LpDivPad, width, lp.y, false, false);
        lp.y += LpLinesH(lines) + LpDivPad + LpParaPadBottom;
    }

    private static void LpPlaceTable(LetterPreviewState lp, LpTable t, double gap, double x, double availW)
    {
        LpLayoutTable(lp, t, x, availW);
        if (!LpFits(lp, gap, t.Height) && LpFitsFresh(lp, gap, t.Height)) { lp.y += gap; LpBreak(lp); }
        lp.y += gap;
        LpDrawTable(lp, t, lp.y);
        lp.y += t.Height;
    }

    /// <summary>div#divTTO (margin 0, padding): the blue h2 with its em margins, div#divTTOTable
    /// (margin 0, padding) holding the grid, div#authoriseTTO (margin 0, padding) — kept whole
    /// when a page can hold it, else laid out part by part.</summary>
    private static void LpPlaceTto(LetterPreviewState lp, LpItem it, double gap)
    {
        var h2Fs = LpFsAt(LpContentDepth + 1) * LpH2Scale;
        var h2Margin = LpH2MarginEm * h2Fs;
        var noteFs = LpFsAt(LpContentDepth + 2);
        var boxW = lp.innerR - lp.innerL - 2 * LpDivPad;
        var t = it.Table;
        if (t is not null) LpLayoutTable(lp, t, lp.innerL + 2 * LpDivPad, boxW - 2 * LpDivPad);
        var headH = LpDivPad + h2Margin + LpLineH(lp, h2Fs) + h2Margin;
        var gridH = t is null ? 0 : 2 * LpDivPad + t.Height;
        var noteLines = it.Authorise.Length == 0 ? null
            : LpWrap(lp, new List<LpRun> { LpMakeRun(it.Authorise, false, noteFs) }, boxW - 2 * LpDivPad, noteFs, false);
        var noteH = noteLines is null ? 0 : 2 * LpDivPad + LpLinesH(noteLines);
        var whole = headH + gridH + noteH + LpDivPad;
        if (!LpFits(lp, gap, whole))
        {
            lp.y += gap;
            gap = 0;
            LpBreak(lp);
        }
        lp.y += gap + LpDivPad + h2Margin;
        LpDrawLines(lp, LpWrap(lp, new List<LpRun> { LpMakeRun(it.H2, true, h2Fs) }, boxW, h2Fs, false),
            lp.innerL + LpDivPad, boxW, lp.y, false, false, LpH2Rgb);
        lp.y += LpLineH(lp, h2Fs) + h2Margin;
        if (t is not null)
        {
            if (!LpFits(lp, 0, gridH)) LpBreak(lp);
            lp.y += LpDivPad;
            LpDrawTable(lp, t, lp.y);
            lp.y += t.Height + LpDivPad;
        }
        if (noteLines is not null)
        {
            if (!LpFits(lp, 0, noteH)) LpBreak(lp);
            lp.y += LpDivPad;
            LpDrawLines(lp, noteLines, lp.innerL + 2 * LpDivPad, boxW - 2 * LpDivPad, lp.y, false, false);
            lp.y += LpLinesH(noteLines) + LpDivPad;
        }
        lp.y += LpDivPad;
    }
}
