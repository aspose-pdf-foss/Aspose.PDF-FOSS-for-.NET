using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>An inline grid's collapsed columns fit the declared boxes it states, sharing what the table width leaves over.</summary>
    private static void FitInlineGridCollapsedColumns(FitBorderedState fb)
    {
        if (fb.mps.wtInlineGrid && fb.tableWpt > 0 && fb.rows.Count > 0 && fb.rows[0].Count > 0)
        {
            double DeclBox(int dc) => fb.colPx[dc]
                + (fb.colPxStyle[dc] ? 2 * fb.p + (fb.mps.wtBw > 0 ? fb.mps.wtBw : fb.bw) : 0);
            // Weigh only the cells that fit the column count — CloseRow
            // pads short rows with empty slots a spanning cell already
            // covers, and those carry no width share.
            double wSum = 0;
            var covered = 0;
            foreach (var mc0 in fb.rows[0])
            {
                if (covered >= fb.nCols) break;
                wSum += 1.0 / Math.Max(1, mc0.ColSpan);
                covered += Math.Max(1, mc0.ColSpan);
            }
            // When EVERY column declares a width, the declared boxes
            // SCALE proportionally to fill the declared table (the
            // email grid's 118.15 + 429.6 pt columns fill the 708 pt
            // box); otherwise row-1 cells split ∝ 1/colspan and the
            // last spanned column takes each cell's remainder.
            var allDecl = true;
            double declSum = 0;
            for (var dc0 = 0; dc0 < fb.nCols; dc0++)
            {
                if (fb.colPx[dc0] <= 0) { allDecl = false; break; }
                declSum += DeclBox(dc0);
            }
            if (allDecl && declSum > 0 && fb.nCols > 1
                && Math.Abs(declSum - fb.tableWpt) > 1.0)
            {
                var declScale = fb.tableWpt / declSum;
                for (var dc0 = 0; dc0 < fb.nCols; dc0++)
                    fb.colW[dc0] = Math.Max(1, DeclBox(dc0) * declScale - 2 * fb.p - 2 * fb.bw);
            }
            else
            {
                var col0 = 0;
                foreach (var mc0 in fb.rows[0])
                {
                    var span0 = Math.Max(1, mc0.ColSpan);
                    if (col0 >= fb.nCols) break;
                    var cellBox = fb.tableWpt * (1.0 / span0) / wSum;
                    var rem = cellBox;
                    for (var k = 0; k < span0 - 1 && col0 + k < fb.nCols; k++)
                    {
                        var b = fb.colPx[col0 + k] > 0 ? DeclBox(col0 + k) : 0;
                        if (b > 0)
                        {
                            fb.colW[col0 + k] = Math.Max(1, b - 2 * fb.p - 2 * fb.bw);
                            rem -= b;
                        }
                    }
                    var last0 = Math.Min(col0 + span0 - 1, fb.nCols - 1);
                    var lastB = fb.colPx[last0] > 0 ? DeclBox(last0) : Math.Max(1, rem);
                    fb.colW[last0] = Math.Max(1, lastB - 2 * fb.p - 2 * fb.bw);
                    col0 += span0;
                }
            }
        }
    }
}
