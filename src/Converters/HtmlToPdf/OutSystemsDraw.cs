using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// OutSystems export drawing helpers: resources, segments, rules, frames, header, grid, bands, signature and date.
    private static string ResFor(OutSystemsState os, string face) => face switch
    {
        "Arial Bold" => "F9",
        "Times New Roman" => "F10",
        _ => "F8",
    };

    private static void Stream(OutSystemsState os, string s) => os.page.AddContentStream(Encoding.ASCII.GetBytes(s));

    private static void EmitSegs(OutSystemsState os, List<OsRun> segs, double x, double yTd)
    {
        foreach (var r in segs)
        {
            if (r.Text.Length > 0)
                EmitPositionedRun(os.page, ResFor(os, r.Face), r.Fs, x, os.pageHeight - yTd, r.Text);
            x += MeasureFaceText(r.Face, r.Text, r.Fs);
        }
    }

    private static double SegsW(OutSystemsState os, List<OsRun> segs)
    {
        double w = 0;
        foreach (var r in segs) w += MeasureFaceText(r.Face, r.Text, r.Fs);
        return w;
    }

    // A rectangle stroked at the given line CENTERS (top-down y).
    private static void RectStroke(OutSystemsState os, double x0, double y0, double x1, double y1, double lw)
        => Stream(os, Compat.Format(os.inv,
            $"q {lw:0.##} w {x0:F2} {os.pageHeight - y1:F2} m {x1:F2} {os.pageHeight - y1:F2} l {x1:F2} {os.pageHeight - y0:F2} l {x0:F2} {os.pageHeight - y0:F2} l h S Q\n"));

    private static void HLine(OutSystemsState os, double x0, double x1, double yTd, double lw)
        => Stream(os, Compat.Format(os.inv,
            $"q {lw:0.##} w {x0:F2} {os.pageHeight - yTd:F2} m {x1:F2} {os.pageHeight - yTd:F2} l S Q\n"));

    // A missing image draws as a 1 pt frame around its declared CSS box.
    private static void ImgFrame(OutSystemsState os, double x, double yTdBottom, double w, double h)
        => RectStroke(os, x + 0.5, yTdBottom - h - 1.5, x + w + 1.5, yTdBottom - 0.5, 1.0);

    // ── the header table: the 48px red title + address, the version cell ──
    private static double OsHeader(OutSystemsState os, List<List<OsCell>> rows, double top)
    {
        if (rows.Count == 0 || rows[0].Count < 2) return top;
        var titleTop = top + OsPad1 + os.pMargin;
        var groups = OsBreakGroups(rows[0][0].Runs);
        var x0 = os.contentX + OsPad1;
        Stream(os, "q 0.906 0.298 0.235 rg\n");   // the sheet's #e74c3c
        var lineTop = titleTop;
        for (var g = 0; g < groups.Count; g++)
        {
            EmitSegs(os, groups[g], x0, lineTop + (g == 0 ? OsTitleDrop : os.drop12));
            lineTop += g == 0 ? OsTitleLineH : OsLine;
        }
        Stream(os, "0 0 0 rg\nQ\n");
        if (rows[0][0].Img is { } img)        // the inline icon seats on the title baseline
            ImgFrame(os, x0 + SegsW(os, groups[0]), titleTop + OsTitleDrop, img.W, img.H);
        var ver = OsWrap(rows[0][1].Runs, os.contentW);
        if (ver.Count > 0)
            EmitSegs(os, ver[0], os.contentX + os.contentW - OsPad1 - SegsW(os, ver[0]), titleTop + os.dropTnr);
        return lineTop + os.pMargin + OsPad1;
    }

    // ── a borderless attribute grid (the FROM and key/value tables) ──
    private static double OsGrid(OutSystemsState os, List<List<OsCell>> rows, double[] colX, double top)
    {
        foreach (var row in rows)
        {
            var n = Math.Min(row.Count, colX.Length - 1);
            var lines = new List<List<OsRun>>[n];
            var rowLines = 1;
            for (var c = 0; c < n; c++)
            {
                lines[c] = OsWrap(row[c].Runs, colX[c + 1] - colX[c]);
                rowLines = Math.Max(rowLines, lines[c].Count);
            }
            for (var c = 0; c < n; c++)
            {
                var off = lines[c].Count == rowLines || row[c].ValignTop
                    ? 0 : (rowLines - lines[c].Count) * OsLine / 2;
                for (var i = 0; i < lines[c].Count; i++)
                {
                    var x = row[c].AlignRight
                        ? colX[c + 1] - SegsW(os, lines[c][i]) : colX[c];
                    EmitSegs(os, lines[c][i], x, top + off + i * OsLine + os.drop12);
                }
            }
            top += rowLines * OsLine;
        }
        return top;
    }

    private static double[] OsEvenCols(OutSystemsState os) => new[]
        { os.contentX, os.contentX + os.q4, os.contentX + 2 * os.q4, os.contentX + 3 * os.q4, os.contentX + 4 * os.q4 };

    // The FROM grid declares 25% on its first two columns only; the
    // undeclared pair takes max-content plus the leftover in proportion to
    // it (the percent-table column model).
    private static double[] OsFromCols(OutSystemsState os, List<List<OsCell>> rows)
    {
        double c3 = 0, c4 = 0;
        foreach (var row in rows)
        {
            if (row.Count > 2) c3 = Math.Max(c3, OsMaxContent(row[2].Runs));
            if (row.Count > 3) c4 = Math.Max(c4, OsMaxContent(row[3].Runs));
        }
        var rem = os.contentW / 2;
        var c3W = c3 + c4 < 1e-9 ? rem / 2 : c3 + (rem - c3 - c4) * c3 / (c3 + c4);
        return new[] { os.contentX, os.contentX + os.q4, os.contentX + 2 * os.q4,
            os.contentX + 2 * os.q4 + c3W, os.contentX + os.contentW };
    }

    // ── a border=1 band: the description pair or the centered emergency cell ──
    private static double OsBand(OutSystemsState os, List<List<OsCell>> rows, double top, bool centered)
    {
        var cells = rows.Count > 0 ? rows[0] : new List<OsCell>();
        var innerW = centered ? os.contentW - 2 * (OsPad1 + OsBorder)
            : os.contentW / 2 - OsPad1 - OsBorder;
        var lineSets = cells.Select(c => OsWrap(c.Runs, innerW)).ToList();
        var rowLines = 1;
        foreach (var l in lineSets) rowLines = Math.Max(rowLines, l.Count);
        var bottom = top + OsPad1 + rowLines * OsLine + OsPad1;
        RectStroke(os, os.contentX + OsBorder / 2, top + OsBorder / 2,
            os.contentX + os.contentW - OsBorder / 2, bottom - OsBorder / 2, OsBorder);
        var mid = os.contentX + os.contentW / 2;
        if (centered)
            RectStroke(os, os.contentX + OsPad1 - OsBorder / 2, top + OsPad1 - OsBorder / 2,
                os.contentX + os.contentW - OsPad1 + OsBorder / 2, bottom - OsPad1 + OsBorder / 2, OsBorder);
        else
        {
            RectStroke(os, os.contentX + OsPad1 - OsBorder / 2, top + OsPad1 - OsBorder / 2,
                mid - OsBorder / 2, bottom - OsPad1 + OsBorder / 2, OsBorder);
            RectStroke(os, mid + OsBorder / 2, top + OsPad1 - OsBorder / 2,
                os.contentX + os.contentW - OsPad1 + OsBorder / 2, bottom - OsPad1 + OsBorder / 2, OsBorder);
        }
        if (lineSets.Count > 0)
            for (var i = 0; i < lineSets[0].Count; i++)
            {
                var segs = lineSets[0][i];
                var x = centered ? os.contentX + OsPad1 + (innerW - SegsW(os, segs)) / 2
                    : os.contentX + OsPad1;
                EmitSegs(os, segs, x, top + OsPad1 + i * OsLine + os.drop12);
            }
        return bottom;
    }

    // ── the signature table: min-content columns, the section-marker boxes,
    //    the measured staircase rows ──
    private static (double result, double right) OsSignature(OutSystemsState os, List<List<OsCell>> rows, double top)
    {
        double right = default;
        double c1 = 0, c2 = 0, c3 = 0, c5 = 0;
        foreach (var row in rows)
            for (var c = 0; c < row.Count && c < 4; c++)
            {
                var w = row[c].NestedText is { } nt
                    ? MeasureFaceText("Arial", nt, 12.0) + 4 * OsBorder
                    : OsMinContent(row[c].Runs);
                switch (c)
                {
                    case 0: c1 = Math.Max(c1, w); break;
                    case 1: c2 = Math.Max(c2, w); break;
                    case 2: c3 = Math.Max(c3, w); break;
                    default: c5 = Math.Max(c5, w); break;
                }
            }
        var colX = new[] { os.contentX, os.contentX + c1, os.contentX + c1 + c2,
            os.contentX + c1 + c2 + c3, os.contentX + c1 + c2 + c3 + c5 };
        right = colX[4];
        var widths = new[] { c1, c2, c3, c5 };

        var sigRow = 0;
        foreach (var row in rows)
        {
            var boxCell = row.FirstOrDefault(c => c.NestedText is not null);
            if (boxCell is not null)
            {
                // a marker-box row: the label wraps down its narrow column
                // and the box centers against those lines
                var label = row.Count > 2 ? OsWrap(row[2].Runs, c3) : new List<List<OsRun>>();
                var rowH = Math.Max(1, label.Count) * OsLine;
                for (var i = 0; i < label.Count; i++)
                    EmitSegs(os, label[i], colX[2], top + i * OsLine + os.drop12);
                var boxH = 4 * OsBorder + OsLine;
                var boxW = MeasureFaceText("Arial", boxCell.NestedText!, 12.0) + 4 * OsBorder;
                var bx = colX[3];
                var bt = top + (rowH - boxH) / 2;
                RectStroke(os, bx + OsBorder / 2, bt + OsBorder / 2,
                    bx + boxW - OsBorder / 2, bt + boxH - OsBorder / 2, OsBorder);
                RectStroke(os, bx + 1.5 * OsBorder, bt + 1.5 * OsBorder,
                    bx + boxW - 1.5 * OsBorder, bt + boxH - 1.5 * OsBorder, OsBorder);
                EmitSegs(os, new List<OsRun> { new(boxCell.NestedText!, "Arial", 12.0) },
                    bx + 2 * OsBorder, bt + 2 * OsBorder + os.drop12);
                top += rowH;
                continue;
            }
            var r = Math.Min(sigRow, OsSigRowLines.Length - 1);
            for (var c = 0; c < row.Count && c < 4; c++)
            {
                var lines = OsWrap(row[c].Runs, widths[c]);
                var off = OsSigCellOff[r][c] * OsLine;
                for (var i = 0; i < lines.Count; i++)
                {
                    var x = row[c].AlignRight
                        ? colX[c + 1] - SegsW(os, lines[i]) : colX[c];
                    EmitSegs(os, lines[i], x, top + off + i * OsLine + os.drop12);
                }
            }
            top += OsSigRowLines[r] * OsLine;
            sigRow++;
        }
        return (top, right);
    }

    // ── the print-date table: the bottom-seated logo frame, the date cell ──
    private static double OsDate(OutSystemsState os, List<List<OsCell>> rows, double top)
    {
        if (rows.Count == 0 || rows[0].Count < 2) return top;
        var cTop = top + OsPad1;
        var img = rows[0][1].Img ?? (W: 59.25, H: 37.5);
        var boxH = img.H + 2;
        ImgFrame(os, os.contentX + os.contentW - OsPad1 - img.W - 2, cTop + boxH, img.W, img.H);
        var groups = OsBreakGroups(rows[0][0].Runs);
        for (var g = 0; g < groups.Count; g++)
            EmitSegs(os, groups[g], os.contentX + OsPad1, cTop + OsDateTextOff + g * OsLine);
        return cTop + boxH + OsPad1;
    }
}
