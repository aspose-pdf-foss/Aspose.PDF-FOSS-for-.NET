using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>Quote schedule: a table block - its columns solved and its rows drawn.</summary>
    private void RenderQuoteTable(QuoteScheduleState qs, QsBlock b)
    {
        var qt = new QuoteTableState();
        qt.qs = qs;
        qt.b = b;
        qt.declared = new List<double>();
        foreach (var row in qt.b.Rows)
        {
            if (row.Count <= qt.declared.Count) continue;
            var ok = true;
            foreach (var c in row) if (c.ColSpan > 1 || c.WidthPx <= 0) { ok = false; break; }
            if (!ok) continue;
            qt.declared.Clear();
            foreach (var c in row) qt.declared.Add(c.WidthPx);
        }
        if (qt.declared.Count == 0) qt.declared.Add(qt.qs.bodyPx);
        qt.n = qt.declared.Count;
        qt.declaredSum = 0.0;
        foreach (var d in qt.declared) qt.declaredSum += d * QsPxToPt;
        if (qt.declaredSum <= 0) return;
        qt.scale = (qt.qs.bodyW - 2 * QsCellInset - (qt.n - 1) * QsColStep) / qt.declaredSum;
        qt.colX = new double[qt.n];
        qt.colW = new double[qt.n];
        qt.cx = qt.qs.left + QsCellInset;
        for (var i = 0; i < qt.n; i++)
        {
            qt.colX[i] = qt.cx;
            qt.colW[i] = qt.declared[i] * QsPxToPt * qt.scale;
            qt.cx += qt.colW[i] + QsColStep;
        }
        qt.wrapped = new List<List<List<string>>>();
        qt.rowH = new List<double>();
        qt.rowFont = new List<double>();
        foreach (var row in qt.b.Rows)
        {
            var cellLines = new List<List<string>>();
            var maxLines = 1;
            var fontPx = QsDefaultFontPx;
            for (var ci = 0; ci < row.Count; ci++)
            {
                var c = row[ci];
                fontPx = Math.Max(fontPx, c.FontPx);
                var ls = c.Text.Length > 0
                    ? Wrap(qt.qs, c.Text, c.FontPx, SpanW(qt, ci, c.ColSpan), c.Bold, c.Italic)
                    : new List<string> { "" };
                cellLines.Add(ls);
                if (ls.Count > maxLines) maxLines = ls.Count;
            }
            qt.wrapped.Add(cellLines);
            qt.rowFont.Add(fontPx);
            qt.rowH.Add(fontPx * QsPxToPt * QsCellPadTopEm
                     + maxLines * QsLineBox(fontPx) + QsCellPadBottom);
        }
        qt.tableH = QsRowSpacing;
        foreach (var h in qt.rowH) qt.tableH += h + QsRowSpacing;
        if (qt.qs.y - qt.tableH < qt.qs.marginBottom && qt.qs.y < qt.qs.contentTop - 0.5) Break(qt.qs);

        qt.rowTop = qt.qs.y - QsRowSpacing;
        for (var ri = 0; ri < qt.b.Rows.Count; ri++)
        {
            RenderQuoteRow(qt, ri);
        }
        qt.qs.y = qt.rowTop;
    }
}
