using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>Writes the body rows: each laid row's cells at their pens, run by run, stepping the row top down by the row height.</summary>
    private static void RenderTableSegmentRows(TableSegmentState ts)
    {
        foreach (var (rh, cellsOut) in ts.laidRows)
        {
            var cellX = ts.tx0 + ts.gap;
            for (var c = 0; c < cellsOut.Count; c++)
            {
                if (cellX >= ts.txR - 2)
                {
                    cellX += (c < ts.declared.Length ? ts.declared[c] : 48.75) + ts.gap;
                    continue;
                }
                var (lhs, pieces) = cellsOut[c];
                var tops = new double[lhs.Count + 1];
                // a cell's content is centred in the box it was
                // given: the tallest cell fills the row, the rest
                // ride in the middle of it
                var lineSum = 0.0;
                foreach (var lh in lhs) lineSum += lh;
                var boxIn = ts.pt.FormRhythm ? 0.75 : 0.0;
                var vMid = ts.pt.FormRhythm
                    ? Math.Max(0, (rh - ts.gap - 2 * boxIn - lineSum) / 2) : 0.0;
                tops[0] = ts.rowTop - boxIn - vMid;
                for (var li = 0; li < lhs.Count; li++) tops[li + 1] = tops[li] - lhs[li];
                // how far each of the cell's lines runs, so the
                // wrap's alignment can seat it in the column
                var cellColW = c < ts.declared.Length ? ts.declared[c] : 48.75;
                var lineEnd = new double[lhs.Count + 1];
                foreach (var (li, sx, seg, txt) in pieces)
                {
                    var w = seg is { BlankPt: > 0 } ? seg.BlankPt
                        : seg is { Radio: true } ? (ts.pt.FormRhythm ? 13.5 : 11.2) * ts.kfs
                        : seg is { Checkbox: true } ? 11.2 * ts.kfs
                        : txt is not null && seg is not null
                            ? PsMeasure(txt, seg.Bold, ts.cfs) : 0.0;
                    var i2 = Math.Min(li, lhs.Count);
                    lineEnd[i2] = Math.Max(lineEnd[i2], sx + w);
                }
                foreach (var (li, sx, seg, txt) in pieces)
                {
                    var lineTop = tops[Math.Min(li, lhs.Count)];
                    var lx = cellX + sx
                        + PsCellInset(ts.pt, cellColW, lineEnd[Math.Min(li, lhs.Count)]);
                    // an author's cell seats its text on the box
                    // its own size asks for, under the rule it
                    // draws above it
                    var lBase = lineTop - (ts.pt.FormRhythm
                        ? PsAscentFor(PsCssLineBox(ts.cfs), ts.cfs)
                        : 12.5 * ts.kfs);
                    if (seg is { BlankPt: > 0 })
                    {
                        // the blank is seated on the bottom of its
                        // line box, 3 css px of margin under its rule
                        var ruleY = lineTop - (ts.pt.FormRhythm
                            ? PsCssLineBox(ts.cfs) - 2.625 * ts.kfs : 12.0 * ts.kfs);
                        ts.tb2.SetLineWidth(0.75)
                           .MoveTo(lx, ruleY)
                           .LineTo(lx + seg.BlankPt, ruleY)
                           .Stroke();
                    }
                    else if (seg is { Radio: true } or { Checkbox: true })
                    {
                        PsGlyph(ts.tb2, seg!.Checkbox, lx, lBase);
                    }
                    else if (txt is not null && seg is not null)
                    {
                        var cres = Table.RegisterFont(ts.flow.CurrentPage,
                            seg.Bold ? "Helvetica-Bold" : "Helvetica");
                        ts.tb2.BeginText().SetFont(cres, ts.cfs)
                           .MoveTextPosition(lx, lBase)
                           .ShowText(txt).EndText();
                    }
                }
                cellX += cellColW + ts.gap;
            }
            ts.rowTop -= rh;
        }
    }

    /// <summary>Writes the head lines of every column at the head band.</summary>
    private static void RenderTableSegmentHead(TableSegmentState ts)
    {
        for (var c = 0; ts.headH > 0 && c < ts.headLines.Count; c++)
        {
            if (ts.hx < ts.txR - 2)
            {
                var blockTop = ts.topY - (ts.headH - ts.headLines[c].Count * 12.24) / 2;
                var colW = c < ts.declared.Length ? ts.declared[c] : 48.75;
                for (var li = 0; li < ts.headLines[c].Count; li++)
                {
                    // a heading cell centres each of its lines in
                    // the column, the way a th is set
                    var lw = PsMeasure(ts.headLines[c][li], true, 10.5);
                    ts.tb2.BeginText().SetFont(ts.thRes, 10.5)
                       .MoveTextPosition(ts.hx + PsCellInset(ts.pt, colW, lw),
                           blockTop - 9.5 - li * 12.24)
                       .ShowText(ts.headLines[c][li]).EndText();
                }
            }
            ts.hx += (c < ts.declared.Length ? ts.declared[c] : 48.75) + ts.gap;
        }
    }

    /// <summary>Draws the table's rules: the form-rhythm shape (bands between rows) or the plain grid (outer frame and column/row lines).</summary>
    private static void DrawTableSegmentRules(TableSegmentState ts)
    {
        if (ts.pt.FormRhythm)
        {
            // separate borders: every cell keeps its own rule,
            // drawn inside its box, and the spacing shows as a
            // gap between one cell's box and the next
            var cy = ts.topY - ts.headH - ts.gap;
            foreach (var lr in ts.laidRows)
            {
                var boxH = Math.Max(0, lr.h - ts.gap);
                var cx = ts.tx0 + ts.gap;
                for (var c = 0; c < ts.declared.Length; c++)
                {
                    var cw = ts.declared[c];
                    if (cx + cw > ts.txR + 0.5) break;
                    ts.tb2.MoveTo(cx, cy - 0.375).LineTo(cx + cw, cy - 0.375).Stroke();
                    ts.tb2.MoveTo(cx, cy - boxH + 0.375).LineTo(cx + cw, cy - boxH + 0.375).Stroke();
                    ts.tb2.MoveTo(cx + 0.375, cy).LineTo(cx + 0.375, cy - boxH).Stroke();
                    ts.tb2.MoveTo(cx + cw - 0.375, cy).LineTo(cx + cw - 0.375, cy - boxH).Stroke();
                    cx += cw + ts.gap;
                }
                cy -= lr.h;
            }
        }
        else
        {
            var gy = ts.topY;
            ts.tb2.MoveTo(ts.tx0, gy).LineTo(ts.txR, gy).Stroke();
            gy -= ts.headH;
            ts.tb2.MoveTo(ts.tx0, gy).LineTo(ts.txR, gy).Stroke();
            foreach (var lr in ts.laidRows)
            {
                gy -= lr.h;
                ts.tb2.MoveTo(ts.tx0, gy).LineTo(ts.txR, gy).Stroke();
            }
            var gx2 = ts.tx0;
            ts.tb2.MoveTo(gx2, ts.topY).LineTo(gx2, gy).Stroke();
            for (var c = 0; c < ts.declared.Length; c++)
            {
                gx2 += ts.declared[c];
                if (gx2 > ts.txR - 1) break;
                ts.tb2.MoveTo(gx2, ts.topY).LineTo(gx2, gy).Stroke();
            }
            ts.tb2.MoveTo(ts.txR, ts.topY).LineTo(ts.txR, gy).Stroke();
        }
    }

    /// <summary>Fills each row's declared cell backgrounds across the columns, from the head band downward.</summary>
    private static void DrawTableSegmentRowBackgrounds(TableSegmentState ts)
    {
        for (var r = 0; r < ts.laidRows.Count; r++)
        {
            var bgRow = ts.firstRow + r < ts.pt.RowBg.Count ? ts.pt.RowBg[ts.firstRow + r] : null;
            var boxH = Math.Max(0, ts.laidRows[r].h - ts.gap);
            var bgX = ts.tx0 + ts.gap;
            for (var c = 0; bgRow is not null && c < bgRow.Count; c++)
            {
                var cw = c < ts.declared.Length ? ts.declared[c] : 48.75;
                if (bgRow[c] is { } fill && bgX < ts.txR - 1)
                {
                    ts.tb2.SetFillColor(fill);
                    ts.tb2.Rectangle(bgX, ts.bgY - boxH,
                        Math.Min(cw, ts.txR - bgX), boxH);
                    ts.tb2.Fill();
                }
                bgX += cw + ts.gap;
            }
            ts.bgY -= ts.laidRows[r].h;
        }
    }
}
