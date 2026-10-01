using System;
using System.Text;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    // Painting: geometry is in continuous page space from the first sheet's top; the sheets are
    // cut at the bottom margin (a later sheet starts at its top margin), and the y flip happens here.
    private const double FbBezierArc = 0.5523;               // the quarter-circle control distance

    /// <summary>The sheet index and the y on it of a continuous y.</summary>
    private static (int page, double y) FbPageOf(FbState fb, double y)
    {
        if (y < fb.bandBottom - FbEpsilon) return (0, y);
        var k = 1 + (int)Math.Floor((y - fb.bandBottom) / fb.laterPageStep);
        return (k, FbPageMarginTop + (y - fb.bandBottom) - (k - 1) * fb.laterPageStep);
    }

    private static double FbPageBottom(FbState fb, int page) => page == 0 ? fb.bandBottom : fb.bandBottom + page * fb.laterPageStep;

    private static StringBuilder FbOps(FbState fb, int page)
    {
        while (fb.pages.Count <= page) fb.pages.Add(new StringBuilder());
        return fb.pages[page];
    }

    /// <summary>A filled rect in continuous space, cut at the sheet bottoms.</summary>
    private static void FbFillRect(FbState fb, double x, double top, double w, double h, string rgb)
    {
        if (w <= 0 || h <= 0) return;
        var y = top;
        while (y < top + h - FbEpsilon)
        {
            var (page, py) = FbPageOf(fb, y);
            var cut = Math.Min(top + h, FbPageBottom(fb, page));
            var ph = cut - y;
            FbOps(fb, page).Append(Compat.Format(fb.invc, $"q {rgb} rg {x:0.###} {fb.pageH - py - ph:0.###} {w:0.###} {ph:0.###} re f Q\n"));
            y = cut;
        }
    }

    /// <summary>A stroked rect on the sheet its top falls on.</summary>
    private static void FbStrokeRect(FbState fb, double x, double top, double w, double h, string rgb, double lw)
    {
        if (w <= 0 || h <= 0) return;
        var (page, py) = FbPageOf(fb, top);
        FbOps(fb, page).Append(Compat.Format(fb.invc, $"q {rgb} RG {lw:0.###} w {x:0.###} {fb.pageH - py - h:0.###} {w:0.###} {h:0.###} re S Q\n"));
    }

    /// <summary>A horizontal stroke at a continuous y.</summary>
    private static void FbStrokeH(FbState fb, double x0, double x1, double y, string rgb, double lw)
    {
        if (x1 <= x0 || lw <= 0) return;
        var (page, py) = FbPageOf(fb, y);
        FbOps(fb, page).Append(Compat.Format(fb.invc, $"q {rgb} RG {lw:0.###} w {x0:0.###} {fb.pageH - py:0.###} m {x1:0.###} {fb.pageH - py:0.###} l S Q\n"));
    }

    /// <summary>A vertical stroke from a continuous y0 down to y1, cut at the sheet bottoms.</summary>
    private static void FbStrokeV(FbState fb, double x, double y0, double y1, string rgb, double lw)
    {
        if (y1 <= y0 || lw <= 0) return;
        var y = y0;
        while (y < y1 - FbEpsilon)
        {
            var (page, py) = FbPageOf(fb, y);
            var cut = Math.Min(y1, FbPageBottom(fb, page));
            FbOps(fb, page).Append(Compat.Format(fb.invc, $"q {rgb} RG {lw:0.###} w {x:0.###} {fb.pageH - py:0.###} m {x:0.###} {fb.pageH - py - (cut - y):0.###} l S Q\n"));
            y = cut;
        }
    }

    /// <summary>A circle of four Béziers, stroked (1 pt) or filled, centred at a continuous y.</summary>
    private static void FbCircle(FbState fb, double cx, double cyTd, double r, string rgb, bool fill)
    {
        var (page, py) = FbPageOf(fb, cyTd);
        var cy = fb.pageH - py;
        var k = FbBezierArc;
        var sb = FbOps(fb, page);
        sb.Append(fill ? Compat.Format(fb.invc, $"q {rgb} rg ") : Compat.Format(fb.invc, $"q {rgb} RG {FbWidgetFrameWidthPt:0.###} w "));
        sb.Append(Compat.Format(fb.invc, $"{cx + r:0.###} {cy:0.###} m "));
        sb.Append(Compat.Format(fb.invc, $"{cx + r:0.###} {cy + k * r:0.###} {cx + k * r:0.###} {cy + r:0.###} {cx:0.###} {cy + r:0.###} c "));
        sb.Append(Compat.Format(fb.invc, $"{cx - k * r:0.###} {cy + r:0.###} {cx - r:0.###} {cy + k * r:0.###} {cx - r:0.###} {cy:0.###} c "));
        sb.Append(Compat.Format(fb.invc, $"{cx - r:0.###} {cy - k * r:0.###} {cx - k * r:0.###} {cy - r:0.###} {cx:0.###} {cy - r:0.###} c "));
        sb.Append(Compat.Format(fb.invc, $"{cx + k * r:0.###} {cy - r:0.###} {cx + r:0.###} {cy - k * r:0.###} {cx + r:0.###} {cy:0.###} c "));
        sb.Append(fill ? "f Q\n" : "S Q\n");
    }

    /// <summary>A run in its style: one of the sheet's own faces is embedded and registered on the run's
    /// page on first use; a relatively positioned run rides its lift above the baseline.</summary>
    private static void FbText(FbState fb, FbStyle st, double x, double baseline, string text)
    {
        var y = baseline - st.RaisePt;
        var res = st.Own is { } own ? FbOwnFaceRes(fb, own, FbPageOf(fb, y).page) : st.Res;
        FbTextRes(fb, res, st.Pt, st.Rgb, x, y, text);
    }

    private static void FbTextRes(FbState fb, string res, double fs, string rgb, double x, double baseline, string text)
    {
        if (text.Length == 0) return;
        var (page, py) = FbPageOf(fb, baseline);
        var shown = text.Replace(' ', ' ');
        FbOps(fb, page).Append(Compat.Format(fb.invc, $"BT {rgb} rg /{res} {fs:0.###} Tf 1 0 0 1 {x:0.###} {fb.pageH - py:0.###} Tm ({EscapePdfString(shown)}) Tj ET\n"));
    }

    /// <summary>Paint a box: its background, its four border lines (each centred inside its band),
    /// a table's grid, a control's widget, its lines' text, then its child boxes in document order.</summary>
    private static void FbPaintTree(FbState fb, FbBox box)
    {
        var p = box.P;
        if (p.BgRgb is { } bg) FbFillRect(fb, box.X, box.Y, box.W, box.H, bg);
        if (box.Img is not { UaChrome: true })
        {
            if (p.Border.T > 0) FbStrokeH(fb, box.X, box.X + box.W, box.Y + p.Border.T / 2, p.BorderRgb, p.Border.T);
            if (p.Border.R > 0) FbStrokeV(fb, box.X + box.W - p.Border.R / 2, box.Y, box.Y + box.H, p.BorderRgb, p.Border.R);
            if (p.Border.B > 0) FbStrokeH(fb, box.X, box.X + box.W, box.Y + box.H - p.Border.B / 2, p.BorderRgb, p.Border.B);
            if (p.Border.L > 0) FbStrokeV(fb, box.X + p.Border.L / 2, box.Y, box.Y + box.H, p.BorderRgb, p.Border.L);
        }
        if (box.Img is not null) FbPaintReplaced(fb, box);
        if (box.Grid is { } grid) FbPaintGrid(fb, grid);
        if (box.Ctl is not null) FbPaintControl(fb, box);
        foreach (var line in box.Lines) FbPaintLineRuns(fb, line);
        foreach (var k in box.Kids) FbPaintTree(fb, k);
    }

    /// <summary>A line's text as one string per run of items in one style (the spaces between
    /// the words drawn in the run), from the run's first pen.</summary>
    private static void FbPaintLineRuns(FbState fb, FbLine line)
    {
        var sb = new StringBuilder();
        FbStyle? st = null;
        var startX = 0.0;
        var runAdv = 0.0;
        void Flush()
        {
            if (st is not null && sb.Length > 0)
            {
                // an inline background fills the run's content area: ascent over descent of its own face
                if (st.BgRgb is { } bg)
                {
                    var m = FbStyleMetrics(fb, st);
                    FbFillRect(fb, line.X + startX, line.Y + line.Above - m.asc * st.Pt, runAdv, (m.asc + m.desc) * st.Pt, bg);
                }
                FbText(fb, st, line.X + startX, line.Y + line.Above, sb.ToString());
            }
            sb.Clear();
            st = null;
            runAdv = 0;
        }
        foreach (var it in line.Items)
        {
            if (it.IsBreak || it.IsSpacer || it.Box is not null) { Flush(); continue; }
            if (st is null || !ReferenceEquals(st, it.St)) { Flush(); st = it.St; startX = it.X; }
            sb.Append(it.Text);
            runAdv += it.Adv;
        }
        Flush();
    }

    private static bool FbCellAboveHasBottom(FbTableGrid g, int r, int col)
    {
        if (r == 0) return false;
        foreach (var c in g.Rows[r - 1]) if (c.Col == col) return c.Bottom;
        return false;
    }

    /// <summary>The centre line of the boundary above row j (j = the row count: the bottom).</summary>
    private static double FbBoundaryLine(FbTableGrid g, int j)
    {
        var y = g.RowTop[j];
        if (j < g.Rows.Length && g.ChargedTop[j]) return y + g.B / 2;
        if (j > 0 && g.ChargedBottom[j - 1]) return y - g.B / 2;
        return y;
    }

    /// <summary>The collapsed grid's borders: one vertical per grid line where either neighbour
    /// declares the side (reaching on through a boundary border charged to the row below), and
    /// per-cell horizontals half a border past the grid lines on the charged side of each
    /// boundary, clipped to the table box.</summary>
    private static void FbPaintGrid(FbState fb, FbTableGrid g)
    {
        if (!g.Collapsed) return;
        var b = g.B;
        var nRows = g.Rows.Length;
        // the cell fills: between the boundary borders' centre lines
        for (var r = 0; r < nRows; r++)
        {
            var y0 = FbBoundaryLine(g, r);
            var y1 = FbBoundaryLine(g, r + 1);
            foreach (var c in g.Rows[r])
                if (c.P.BgRgb is { } bg) FbFillRect(fb, g.Gx[c.Col], y0, g.Gx[c.Col + c.Span] - g.Gx[c.Col], y1 - y0, bg);
        }
        if (b <= 0) return;
        for (var r = 0; r < nRows; r++)
        {
            var row = g.Rows[r];
            double top = g.RowTop[r], bottom = g.RowTop[r + 1];
            var nCols = g.Gx.Length - 1;
            var nextCharged = r + 1 < nRows && g.ChargedTop[r + 1];
            for (var k = 0; k <= nCols; k++)
            {
                FbTableCell? leftCell = null, rightCell = null;
                foreach (var c in row) { if (c.Col + c.Span == k) leftCell = c; if (c.Col == k) rightCell = c; }
                var drawn = (leftCell is not null && leftCell.Right) || (rightCell is not null && rightCell.Left);
                if (!drawn) continue;
                var extra = nextCharged && ((leftCell is not null && leftCell.Bottom) || (rightCell is not null && rightCell.Bottom)) ? b : 0;
                FbStrokeV(fb, g.Gx[k], top, bottom + extra, g.Rgb, b);
            }
            foreach (var c in row)
            {
                var x0 = Math.Max(g.X0, g.Gx[c.Col] - b / 2);
                var x1 = Math.Min(g.X1, g.Gx[c.Col + c.Span] + b / 2);
                // (a boundary both neighbours declare is one line, the upper cell's)
                if (c.Top && !FbCellAboveHasBottom(g, r, c.Col)) FbStrokeH(fb, x0, x1, g.ChargedTop[r] ? top + b / 2 : top - b / 2, g.Rgb, b);
                if (c.Bottom)
                {
                    var y = g.ChargedBottom[r] ? bottom - b / 2 : nextCharged ? bottom + b / 2 : bottom;
                    FbStrokeH(fb, x0, x1, y, g.Rgb, b);
                }
            }
        }
    }
}
