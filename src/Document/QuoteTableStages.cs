using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf;

public sealed partial class Document
{
// The stages of the quote table render: the span width and one row.
    private static double SpanW(QuoteTableState qt, int ci, int span)
    {
        var w = 0.0;
        for (var s = 0; s < span && ci + s < qt.n; s++)
            w += qt.colW[ci + s] + (s > 0 ? QsColStep : 0);
        return w;
    }

    /// <summary>x</summary>
    private void RenderQuoteRow(QuoteTableState qt, int ri)
    {
        // a table taller than a whole page still has to break somewhere
        if (qt.rowTop - qt.rowH[ri] < qt.qs.marginBottom)
        {
            Break(qt.qs);
            qt.rowTop = qt.qs.y - QsRowSpacing;
        }
        var row = qt.b.Rows[ri];
        var padTop = qt.rowFont[ri] * QsPxToPt * QsCellPadTopEm;
        var innerH = qt.rowH[ri] - padTop - QsCellPadBottom;
        for (var ci = 0; ci < row.Count && ci < qt.n; ci++)
        {
            var c = row[ci];
            var ls = qt.wrapped[ri][ci];
            var box = QsLineBox(c.FontPx);
            // middle alignment inside the row's content box
            var top = qt.rowTop - padTop - (innerH - ls.Count * box) / 2;
            var cellW = SpanW(qt, ci, c.ColSpan);
            for (var li = 0; li < ls.Count; li++)
            {
                if (ls[li].Length == 0) continue;
                var w = Measure(qt.qs, ls[li], c.FontPx, c.Bold, c.Italic);
                var x = c.Centre ? qt.colX[ci] + (cellW - w) / 2 : qt.colX[ci];
                qt.qs.flow.WriteAbsoluteText(x,
                    top - li * box - QsBaselineInLine(c.FontPx)
                        - QsDescentEm * c.FontPx * QsPxToPt,
                    ls[li], c.FontPx * QsPxToPt, Face(qt.qs, c.Bold, c.Italic));
            }
        }
        qt.rowTop -= qt.rowH[ri] + QsRowSpacing;
    }
}
