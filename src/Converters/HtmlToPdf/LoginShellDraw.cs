using System;
using System.Collections.Generic;
using System.Text;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Per-call working state of the login-shell render. One instance per
    /// invocation; never shared.</summary>
    private sealed class LoginShellState
    {
        public System.Globalization.CultureInfo invc = null!;
        public string html = null!;
        public HtmlLoadOptions? options;
        public LetterPreviewState lp = null!;     // the Tahoma text engine: runs, metric lines, the wrap
        public Document doc = null!;
        public StringBuilder fills = new();       // painted before the images, the text and rules after
        public List<(LsPicture im, double x, double top)> images = new();
        public double pageW, pageH;
        public double contentL, contentR, top;   // the body's content box
        public LsPicture logo = null!, appLogo = null!;
        public double navTableH;                 // the user-menu table's height attribute
        public double boxW, boxMarginPct;        // the box table's width and margin-bottom
        public List<LsPicture> topCorners = new(), bottomCorners = new();
        public double gridPct;                   // the login grid's width
        public List<List<LpRun>> welcome = new(), notice = new(), links = new();
        public List<LpRun> userLabel = new(), passLabel = new();
        public string footer = "";
    }

    private sealed class LsPicture
    {
        public byte[] Bytes = null!;
        public double W, H;
    }

    /// <summary>A text input's box: the metric line plus the UA border and padding, top and bottom.</summary>
    private static double LsInputBoxH(LoginShellState ls) => LpLineH(ls.lp, LsFs) + 2 * (LsInputBorderPx + LsInputPadPx) * LsPxPt;

    private static Document LsRender(LoginShellState ls)
    {
        var lp = ls.lp;
        ls.doc = new Document();
        lp.doc = ls.doc;
        lp.page = ls.doc.Pages.Add(ls.pageW, ls.pageH);
        LpRegisterFonts(lp.page);
        var contentW = ls.contentR - ls.contentL;
        var y = LsNav(ls, ls.top);
        // the title band between its white rules
        LsFill(ls, ls.contentL, y, contentW, LsTitleH, LsTitleRgb);
        LpHLine(lp, ls.contentL, ls.contentR, y + LsRule / 2, LsTitleRuleRgb);
        LpHLine(lp, ls.contentL, ls.contentR, y + LsTitleH - LsRule / 2, LsTitleRuleRgb);
        y += LsTitleH;
        // the content row: the box's percent margin is a share of the row's width, and the
        // box centres in the row that margin tallens
        var bodyH = LsBodyHeight(ls, out var gridCols, out var gridRowH);
        var boxH = ls.topCorners[0].H + bodyH + ls.bottomCorners[0].H;
        var rowH = boxH + ls.boxMarginPct * contentW;
        LsBox(ls, ls.contentL + (contentW - ls.boxW) / 2, y + (rowH - boxH) / 2, bodyH, gridCols, gridRowH);
        y += rowH;
        LsFooter(ls, y);
        lp.page.AddContentStream(Encoding.ASCII.GetBytes(ls.fills.ToString()));
        foreach (var (im, x, topTd) in ls.images)
        {
            try
            {
                var stamp = ImageStamp.FromEncodedBytes(im.Bytes);
                stamp.XIndent = x;
                stamp.YIndent = ls.pageH - topTd - im.H;
                stamp.DisplayWidth = im.W;
                stamp.DisplayHeight = im.H;
                stamp.ApplyTo(lp.page);
            }
            catch { /* an undecodable image draws nothing */ }
        }
        LpFlush(lp);
        return ls.doc;
    }

    /// <summary>The nav row: the logo cell is as wide as its image and the row as tall as
    /// the user-menu table; the logo (with its bottom margin) and the application logo
    /// centre vertically, the latter at the right edge. Returns the row's bottom.</summary>
    private static double LsNav(LoginShellState ls, double top)
    {
        var logoBox = ls.logo.H + LsLogoMarginBottom;
        var h = Math.Max(ls.navTableH, logoBox);
        LsFill(ls, ls.contentL, top, ls.logo.W, h, LsNavFillRgb);
        ls.images.Add((ls.logo, ls.contentL, top + (h - logoBox) / 2));
        ls.images.Add((ls.appLogo, ls.contentR - ls.appLogo.W, top + (h - ls.appLogo.H) / 2));
        return top + h;
    }

    private static double LsBodyContentW(LoginShellState ls) => ls.boxW - 2 * (LsRule + LsBodyPadSide);

    /// <summary>The body cell's height: its paddings, the welcome and notice lines and the
    /// login grid's row; solves the grid's columns on the way.</summary>
    private static double LsBodyHeight(LoginShellState ls, out double[] cols, out double gridRowH)
    {
        var lp = ls.lp;
        var cw = LsBodyContentW(ls);
        cols = LsGridColumns(ls, ls.gridPct * cw);
        gridRowH = Math.Max(ls.links.Count * LsLinkLine, LpLineH(lp, LsFs) + LsInputBoxH(ls));
        var h = 2 * LsBodyPadV + gridRowH;
        foreach (var seg in ls.welcome) h += LpLinesH(LpWrap(lp, seg, cw, LsFs, false));
        foreach (var seg in ls.notice) h += LpLinesH(LpWrap(lp, seg, cw, LsFs, false));
        return h;
    }

    /// <summary>The login grid's columns: the nowrap link column at its content, the two
    /// percent columns at their label minimum plus the remainder in proportion to their
    /// shortfall against the percent target.</summary>
    private static double[] LsGridColumns(LoginShellState ls, double w)
    {
        double linkW = 0;
        foreach (var seg in ls.links) linkW = Math.Max(linkW, LpWidth(seg));
        var c3 = LsLinkPadLeft + linkW + LsLinkPadRight;
        var m1 = LpWidth(ls.userLabel);
        var m2 = LsPassPadLeft + LpWidth(ls.passLabel);
        var spare = w - m1 - m2 - c3;
        var s1 = Math.Max(0, LsGridColPct * w - m1);
        var s2 = Math.Max(0, LsGridColPct * w - m2);
        var share = s1 + s2 > 0 ? spare / (s1 + s2) : 0;
        return new[] { m1 + share * s1, m2 + share * s2, c3 };
    }

    /// <summary>The box: the corner rows, the body cell's fill and side rules, its text and
    /// the login grid; the box's own columns widen its corner columns by the body's
    /// minimum in proportion to their own.</summary>
    private static void LsBox(LoginShellState ls, double x, double top, double bodyH, double[] gridCols, double gridRowH)
    {
        var lp = ls.lp;
        var cw = LsBodyContentW(ls);
        var cornerH = ls.topCorners[0].H;
        // the corner columns: the minimum of the three-column row is the corner images and the
        // 1px spacer; the body cell's minimum (the grid's label and link columns plus its own
        // inset) is spread over them in proportion, and the 100% middle column takes the rest
        var spacerW = LsPxPt;
        var mins = new[] { ls.topCorners[0].W, spacerW, ls.topCorners[1].W };
        var minSum = mins[0] + mins[1] + mins[2];
        double gridMin = 0;
        foreach (var seg in ls.links) gridMin = Math.Max(gridMin, LpWidth(seg));
        gridMin += LsLinkPadLeft + LsLinkPadRight + LpWidth(ls.userLabel) + LsPassPadLeft + LpWidth(ls.passLabel);
        var excess = Math.Max(0, gridMin + 2 * (LsRule + LsBodyPadSide) - minSum);
        var c1 = mins[0] + excess * mins[0] / minSum;
        var c3 = mins[2] + excess * mins[2] / minSum;
        // the top row
        LsFill(ls, x + c1, top, ls.boxW - c1 - c3, cornerH, LsBoxFillRgb);
        LpHLine(lp, x + c1, x + ls.boxW - c3, top + LsRule / 2, LsBoxRuleRgb);
        ls.images.Add((ls.topCorners[0], x, top));
        ls.images.Add((ls.topCorners[1], x + ls.boxW - c3, top));
        // the body cell
        var bodyTop = top + cornerH;
        LsFill(ls, x, bodyTop, ls.boxW, bodyH, LsBoxFillRgb);
        LpVLine(lp, x + LsRule / 2, bodyTop, bodyTop + bodyH, LsBoxRuleRgb);
        LpVLine(lp, x + ls.boxW - LsRule / 2, bodyTop, bodyTop + bodyH, LsBoxRuleRgb);
        var tx = x + LsRule + LsBodyPadSide;
        var y = bodyTop + LsBodyPadV;
        foreach (var seg in ls.welcome) y = LsDrawLines(ls, LpWrap(lp, seg, cw, LsFs, false), tx, y, LsTextRgb);
        y = LsGrid(ls, tx + (cw - ls.gridPct * cw) / 2, y, gridCols, gridRowH);
        foreach (var seg in ls.notice) y = LsDrawLines(ls, LpWrap(lp, seg, cw, LsFs, false), tx, y, LsTextRgb);
        // the bottom row
        var botTop = bodyTop + bodyH;
        var botH = ls.bottomCorners[0].H;
        LsFill(ls, x + c1, botTop, ls.boxW - c1 - c3, botH, LsBoxFillRgb);
        LpHLine(lp, x + c1, x + ls.boxW - c3, botTop + botH - LsRule / 2, LsBoxRuleRgb);
        ls.images.Add((ls.bottomCorners[0], x, botTop));
        ls.images.Add((ls.bottomCorners[1], x + ls.boxW - c3, botTop));
    }

    /// <summary>The login grid's one row: a label line over a text input in the first two
    /// cells, the link lines on their 16px line in the third. Returns the row's bottom.</summary>
    private static double LsGrid(LoginShellState ls, double x, double top, double[] cols, double rowH)
    {
        var lp = ls.lp;
        var lineH = LpLineH(lp, LsFs);
        LsDrawLines(ls, LpWrap(lp, ls.userLabel, cols[0], LsFs, false), x, top, LsTextRgb);
        LsInput(ls, x, top + lineH, cols[0]);
        var px = x + cols[0] + LsPassPadLeft;
        LsDrawLines(ls, LpWrap(lp, ls.passLabel, cols[1] - LsPassPadLeft, LsFs, false), px, top, LsTextRgb);
        LsInput(ls, px, top + lineH, cols[1] - LsPassPadLeft);
        var lx = x + cols[0] + cols[1] + LsLinkPadLeft;
        var ly = top;
        foreach (var seg in ls.links)
        {
            var line = LpCloseLine(lp, seg, LsFs);
            line.Height = LsLinkLine;
            line.Above = MetricBaselineDrop(LsFs, LsLinkLine, lp.tm);
            LsDrawLine(ls, line, lx, ly, LsTextRgb);
            ly += LsLinkLine;
        }
        return top + rowH;
    }

    /// <summary>A text input: the metric line plus the UA chrome, its widget rect a px
    /// taller each way, framed by the 1pt black rect stroked inset 0.5.</summary>
    private static void LsInput(LoginShellState ls, double x, double lineTop, double w)
    {
        var top = lineTop - LsWidgetOverhang;
        var h = LsInputBoxH(ls) + 2 * LsWidgetOverhang;
        ls.lp.sb.Append(Compat.Format(ls.invc,
            $"q 0 0 0 RG {LsWidgetStroke:0.##} w {x + LsWidgetInset:0.###} {ls.pageH - top - h + LsWidgetInset:0.###} {w - 2 * LsWidgetInset:0.###} {h - 2 * LsWidgetInset:0.###} re S Q\n"));
    }

    /// <summary>The footer band: the copyright wrapped inside its paddings.</summary>
    private static void LsFooter(LoginShellState ls, double top)
    {
        if (ls.footer.Length == 0) return;
        var lp = ls.lp;
        var contentW = ls.contentR - ls.contentL;
        var lines = LpWrap(lp, new List<LpRun> { LpMakeRun(ls.footer, false, LsFs) }, contentW - LsFooterPadL - LsFooterPadR, LsFs, false);
        LsFill(ls, ls.contentL, top, contentW, LsFooterPadT + LpLinesH(lines) + LsFooterPadB, LsFooterFillRgb);
        LsDrawLines(ls, lines, ls.contentL + LsFooterPadL, top + LsFooterPadT, LsFooterRgb);
    }

    private static double LsDrawLines(LoginShellState ls, List<LpLine> lines, double x, double top, string rgb)
    {
        var y = top;
        foreach (var ln in lines)
        {
            LsDrawLine(ls, ln, x, y, rgb);
            y += ln.Height;
        }
        return y;
    }

    private static void LsDrawLine(LoginShellState ls, LpLine ln, double x, double top, string rgb)
    {
        foreach (var r in ln.Runs)
        {
            LpEmit(ls.lp, r, x, top + ln.Above, rgb);
            if (r.Underline) LsUnderline(ls, r, x, top + ln.Above, rgb);
            x += r.Width;
        }
    }

    /// <summary>An underline 0.1em below the baseline and 0.1em thick, skipping the ink
    /// of every descender by the measured margins around its advance box.</summary>
    private static void LsUnderline(LoginShellState ls, LpRun r, double x, double baseTd, string rgb)
    {
        var yPdf = ls.pageH - baseTd - LsUnderlineEm * r.Fs;
        var sb = ls.lp.sb;
        sb.Append(Compat.Format(ls.invc, $"q {rgb} RG {LsUnderlineEm * r.Fs:0.###} w\n"));
        var segStart = x;
        for (var i = 0; i < r.Text.Length; i++)
        {
            if (LsDescenders.IndexOf(r.Text[i]) < 0) continue;
            var gx = x + LpMeasure(r.Face, r.Text[..i], r.Fs);
            var gEnd = x + LpMeasure(r.Face, r.Text[..(i + 1)], r.Fs);
            var gapL = gx - LsDescGapLeftEm * r.Fs;
            if (gapL > segStart) sb.Append(Compat.Format(ls.invc, $"{segStart:0.###} {yPdf:0.###} m {gapL:0.###} {yPdf:0.###} l S\n"));
            segStart = gEnd + LsDescGapRightEm * r.Fs;
        }
        var end = x + r.Width;
        if (end > segStart) sb.Append(Compat.Format(ls.invc, $"{segStart:0.###} {yPdf:0.###} m {end:0.###} {yPdf:0.###} l S\n"));
        sb.Append("Q\n");
    }

    private static void LsFill(LoginShellState ls, double x, double top, double w, double h, string rgb)
        => ls.fills.Append(Compat.Format(ls.invc, $"q {rgb} rg {x:0.###} {ls.pageH - top - h:0.###} {w:0.###} {h:0.###} re f Q\n"));
}
