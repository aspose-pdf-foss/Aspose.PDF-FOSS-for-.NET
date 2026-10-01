using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{

    // The escaped-attribute grid: edge pad, cell gap and cell pad in points.
    private const double GridEdgePad = 2.25;
    private const double GridCellGap = 1.5;
    private const double GridCellPad = 1.5;
// Escaped-attribute table helpers: grid text measurement, cell widths and the cell plan.
    // Column widths: every column
    // floors at its MIN-CONTENT — the widest unbreakable piece across its
    // cells, where a hyphen IS a break opportunity, controls count whole,
    // and a BUTTON counts nothing (it overhangs its cell) — and the
    // remaining space distributes proportional to SLACK (the unwrapped
    // width still wanted over the floor). This sizes
    // all eight Employer-grid columns within a point: 'Employer Name'
    // rides one line while 'Contact Person' wraps. Widths measure in the
    // REAL TimesNewRoman metrics the cells draw with.
    private static double GridMeasure(EscapedTableState et, bool bold, string s, double pt)
        => MeasureFaceText(bold ? "Times New Roman Bold" : "Times New Roman", s, pt);

    private static (double Full, double Min) CellWidths(EscapedTableState et, List<Block> items, bool header)
    {
        double full = 0, min = 0;
        foreach (var it in items)
        {
            if (it.IsButton) continue;
            if (it.IsInputField)
            {
                var w = it.InputWidth + (it.IsSelectBox ? 2 * SelectSideBearingPt : 0);
                full += w; min = System.Math.Max(min, w);
            }
            else if (!string.IsNullOrEmpty(it.Text))
            {
                var bold = header || it.FontRes == "F2";
                var fpt = it.FontSize > 0 ? it.FontSize : EscapedBodyFontPt;
                full += GridMeasure(et, bold, it.Text, fpt);
                foreach (var word in it.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    var hy = word.IndexOf('-');
                    if (hy > 0 && hy < word.Length - 1)
                    {
                        min = System.Math.Max(min, GridMeasure(et, bold, word[..(hy + 1)], fpt));
                        min = System.Math.Max(min, GridMeasure(et, bold, word[(hy + 1)..], fpt));
                    }
                    else
                        min = System.Math.Max(min, GridMeasure(et, bold, word, fpt));
                }
            }
        }
        return (full, min);
    }

    // Assemble every cell's wrapped lines: row height comes from the
    // tallest cell (16.5 floor = one 13.5 line + the cell's 3 pt of
    // vertical padding) and a shorter cell CENTRES in its row.
    private static (List<(List<(Block? Ctl, string? Txt, double XOff, double FontPt, string Res)> Items, double H)> Lines, double ContentH) AssembleCell(EscapedTableState et, List<Block> items, bool header, double availW)
    {
        var cellLines = new List<(List<(Block? Ctl, string? Txt, double XOff, double FontPt, string Res)>, double)>();
        var cl = new List<(Block? Ctl, string? Txt, double XOff, double FontPt, string Res)>();
        double pen = 0, clH = 13.5;
        void EndCellLine()
        {
            if (cl.Count == 0) return;
            cellLines.Add((cl, clH));
            cl = new List<(Block? Ctl, string? Txt, double XOff, double FontPt, string Res)>();
            pen = 0; clH = 13.5;
        }
        foreach (var it in items)
        {
            if (it.IsInputField || it.IsButton)
            {
                var w = it.IsInputField
                    ? it.InputWidth + (it.IsSelectBox ? 2 * SelectSideBearingPt : 0)
                    : it.ButtonCaption.Length > 0
                        ? MeasureStd14("Helvetica", it.ButtonCaption, 10) + ButtonChromeWPt : EmptyButtonWPt;
                var h = it.IsInputField ? it.InputHeight
                    : it.ButtonCaption.Length > 0 ? ButtonHeightPt : EmptyButtonHPt;
                if (cl.Count > 0 && pen + w > availW + 1e-6) EndCellLine();
                cl.Add((it, null, pen, 0, ""));
                // A control FILLS its cell: a 16.17 combo
                // sits flush in a 16.4 cell (borders coincide), so the
                // control's line costs its height minus the cell's own
                // 3 pt of vertical padding.
                clH = System.Math.Max(clH, h - 3);
                pen += w;
            }
            else if (!string.IsNullOrEmpty(it.Text))
            {
                var res = header || it.FontRes == "F2" ? "F6"
                    : it.FontRes == "F3" ? "F7" : "F5";
                var bold = res == "F6";
                var fpt = it.FontSize > 0 ? it.FontSize : EscapedBodyFontPt;
                int p = 0;
                while (p < it.Text.Length)
                {
                    var sp = it.Text.IndexOf(' ', p);
                    var wordEnd = sp < 0 ? it.Text.Length : sp + 1;
                    while (wordEnd < it.Text.Length && it.Text[wordEnd] == ' ') wordEnd++;
                    var word = it.Text.Substring(p, wordEnd - p);
                    p = wordEnd;
                    // A hyphen inside a word is a break opportunity too —
                    // 'Perfetto-Tullo' wraps after the hyphen.
                    var segStart = 0;
                    while (segStart < word.Length)
                    {
                        var hy = word.IndexOf('-', segStart);
                        var segEnd = hy >= 0 && hy < word.Length - 1 ? hy + 1 : word.Length;
                        var token = word[segStart..segEnd];
                        segStart = segEnd;
                        var wTrim = GridMeasure(et, bold, token.TrimEnd(' '), fpt);
                        if (cl.Count > 0 && pen + wTrim > availW + 1e-6) EndCellLine();
                        var drawTok = cl.Count == 0 ? token.TrimStart(' ') : token;
                        if (drawTok.Length == 0) continue;
                        cl.Add((null, drawTok, pen, fpt, res));
                        pen += GridMeasure(et, bold, drawTok, fpt);
                    }
                }
            }
        }
        EndCellLine();
        double contentH = 3;
        foreach (var (_, lh) in cellLines) contentH += lh;
        if (cellLines.Count == 0) contentH = 16.5;
        return (cellLines, contentH);
    }

    /// <summary>One grid row: its band, the cell texts and controls at their column offsets, and the rules between the cells.</summary>
    private static bool DrawEscapedRow(EscapedTableState et, HtmlFlowCursor flow, Document doc, Color? dialectButtonFill, string dialectButtonTextRg, double lineHeight, int ri)
    {
        var r = et.trRows[ri];
        var rh = et.rowHs[ri];
        var cx = et.gx + GridEdgePad;
        for (int ci = 0; ci < r.Count; ci++)
        {
            var cw = et.colW[ci];
            // Cell INSET frame.
            DrawBox(flow.page, cx, et.rowTop - 0.75, cw, 0.75, null, 0, Color.Black);
            DrawBox(flow.page, cx, et.rowTop - rh, cw, 0.75, null, 0, et.gridDark);
            DrawBox(flow.page, cx, et.rowTop - rh, 0.75, rh, null, 0, Color.Black);
            DrawBox(flow.page, cx + cw - 0.75, et.rowTop - rh, 0.75, rh, null, 0, et.gridDark);
            var (cellLines, cellContentH) = et.planRows[ri][ci];
            // vertical-align: middle — a shorter cell centres in its row.
            var vaOff = System.Math.Max(0, (rh - cellContentH) / 2);
            var lineBase = et.rowTop - vaOff - 12.3;
            foreach (var (lineItems, lineH) in cellLines)
            {
                // A header cell centres each of its lines (the th default).
                double cellHOff = 0;
                if (r[ci].Header)
                {
                    double lineW = 0;
                    foreach (var (lc, lt, lx, lp, lr) in lineItems)
                        lineW = System.Math.Max(lineW, lx + (lc is null
                            ? GridMeasure(et, lr == "F6", lt ?? "", lp)
                            : lc.IsInputField
                                ? lc.InputWidth + (lc.IsSelectBox ? 2 * SelectSideBearingPt : 0)
                                : lc.ButtonCaption.Length > 0
                                    ? MeasureStd14("Helvetica", lc.ButtonCaption, 10) + ButtonChromeWPt
                                    : EmptyButtonWPt));
                    cellHOff = System.Math.Max(0, (cw - 2 * GridCellPad - lineW) / 2);
                }
                foreach (var (ctl, txt, xOff, fpt, res) in lineItems)
                {
                    var ix = cx + GridCellPad + cellHOff + xOff;
                    if (ctl is null)
                    {
                        if (!string.IsNullOrEmpty(txt)) EmitSerifRun(txt, res, fpt, ix, lineBase, flow);
                    }
                    else if (ctl.IsInputField)
                    {
                        // Centre the control box in its ROW — flush with
                        // the cell borders for a full-height control.
                        var ctlH = ctl.InputHeight > 0 ? ctl.InputHeight : 15.75;
                        var ctlAbove = ctl.IsSelectBox
                            ? SelectBoxAboveBaselinePt : InputBoxAboveBaselinePt;
                        EmitControlAt(ctl, ix, et.rowTop - (rh - ctlH) / 2 - ctlAbove, flow, doc, lineHeight);
                    }
                    else
                    {
                        var bcw = ctl.ButtonCaption.Length > 0
                            ? MeasureStd14("Helvetica", ctl.ButtonCaption, 10) + ButtonChromeWPt : EmptyButtonWPt;
                        var bch = ctl.ButtonCaption.Length > 0 ? ButtonHeightPt : EmptyButtonHPt;
                        // A cell button CENTRES vertically in its row and
                        // OVERHANGS horizontally: its
                        // left edge sits half a point outside the cell box,
                        // so its outline rides the cell's own border.
                        var bcy = et.rowTop - (rh - bch) / 2 - bch;
                        var bcx = ix - GridCellPad - 0.5;
                        DrawBox(flow.page, bcx, bcy, bcw, bch,
                            border: Color.Black, borderWidth: 1, fill: null);
                        if (bcw > 4 && bch > 3)
                            DrawBox(flow.page, bcx + 2, bcy + 1.5, bcw - 4, bch - 3,
                                null, 0, dialectButtonFill);
                        if (ctl.ButtonCaption.Length > 0)
                            flow.page.AddContentStream(Encoding.ASCII.GetBytes(FormattableString.Invariant(
                                $"q BT /F1 10 Tf {dialectButtonTextRg} 1 0 0 1 {bcx + ButtonCaptionInsetXPt:0.##} {bcy + bch - ButtonCaptionDropPt:0.##} Tm ({EscapePdfString(ctl.ButtonCaption)}) Tj ET Q\n")));
                    }
                }
                lineBase -= lineH;
            }
            cx += cw + GridCellGap;
        }
        et.rowTop -= rh + GridCellGap;
        return true;
    }

    /// <summary>Assemble every cell, sum the row heights and the table size, break the page when the grid does not fit, and frame it.</summary>
    private static void PlanEscapedRows(EscapedTableState et, HtmlFlowCursor flow, HtmlDocProfile profile, Document doc, Core.PdfDictionary docFontDict, double marginBottom, double marginLeft, double marginTop, double pageHeight, double pageWidth)
    {
        et.planRows = new List<(List<(List<(Block? Ctl, string? Txt, double XOff, double FontPt, string Res)> Items, double H)> Lines, double ContentH)[]>();
        et.rowHs = new List<double>();
        foreach (var r in et.trRows)
        {
            var plans = new (List<(List<(Block? Ctl, string? Txt, double XOff, double FontPt, string Res)> Items, double H)> Lines, double ContentH)[r.Count];
            double rh = 16.5;
            for (int ci = 0; ci < r.Count; ci++)
            {
                plans[ci] = AssembleCell(et, r[ci].Items, r[ci].Header, et.colW[ci] - 2 * GridCellPad);
                rh = System.Math.Max(rh, plans[ci].ContentH);
            }
            et.planRows.Add(plans);
            et.rowHs.Add(rh);
        }
        et.tableW = et.gridChrome;
        foreach (var w in et.colW) et.tableW += w;
        et.tableH = 2 * GridEdgePad + GridCellGap * (et.trRows.Count - 1);
        foreach (var rh in et.rowHs) et.tableH += rh;

        et.gridTop = flow.y + 0.9 * 12;
        if (et.gridTop - et.tableH < marginBottom
            && et.tableH <= FreshPageTopY(profile, pageHeight, marginTop) - marginBottom
            && flow.y < FreshPageTopY(profile, pageHeight, marginTop) - 1e-3)
        {
            flow.page = doc.Pages.Add(pageWidth, pageHeight);
            EnsureFonts(flow.page, docFontDict);
            flow.y = FreshPageTopY(profile, pageHeight, marginTop); flow.pendingTopDrop = profile.hasZeroTopMargin;
            et.gridTop = flow.y + 0.9 * 12;
        }
        et.gridDark = ParseCssColor("#555555");
        et.gx = marginLeft;
        // Outer OUTSET frame.
        DrawBox(flow.page, et.gx, et.gridTop - 0.75, et.tableW, 0.75, null, 0, et.gridDark);
        DrawBox(flow.page, et.gx, et.gridTop - et.tableH, et.tableW, 0.75, null, 0, Color.Black);
        DrawBox(flow.page, et.gx, et.gridTop - et.tableH, 0.75, et.tableH, null, 0, et.gridDark);
        DrawBox(flow.page, et.gx + et.tableW - 0.75, et.gridTop - et.tableH, 0.75, et.tableH, null, 0, Color.Black);
        et.rowTop = et.gridTop - GridEdgePad;
    }

    /// <summary>The column count and the column widths from the cells' full and minimum widths within the content width.</summary>
    private static void SizeEscapedColumns(EscapedTableState et, HtmlFlowCursor flow)
    {
        foreach (var r in et.trRows) et.nCols = System.Math.Max(et.nCols, r.Count);

        et.colFull = new double[et.nCols];
        et.colMin = new double[et.nCols];
        foreach (var r in et.trRows)
            for (int ci = 0; ci < r.Count; ci++)
            {
                var (cf, cm) = CellWidths(et, r[ci].Items, r[ci].Header);
                et.colFull[ci] = System.Math.Max(et.colFull[ci], cf + 2 * GridCellPad);
                et.colMin[ci] = System.Math.Max(et.colMin[ci], cm + 2 * GridCellPad);
            }
        et.colW = new double[et.nCols];
        et.gridChrome = 2 * GridEdgePad + GridCellGap * (et.nCols - 1);
        {
            // An empty column still keeps a sliver of a cell.
            for (int ci = 0; ci < et.nCols; ci++)
            {
                et.colMin[ci] = System.Math.Max(et.colMin[ci], 10.5);
                et.colFull[ci] = System.Math.Max(et.colFull[ci], et.colMin[ci]);
            }
            double fullSum = et.gridChrome, minSum = et.gridChrome;
            foreach (var w in et.colFull) fullSum += w;
            foreach (var w in et.colMin) minSum += w;
            if (fullSum <= flow.contentWidth)
                for (int ci = 0; ci < et.nCols; ci++) et.colW[ci] = et.colFull[ci];
            else if (minSum >= flow.contentWidth)
            {
                var scale = (flow.contentWidth - et.gridChrome) / (minSum - et.gridChrome);
                for (int ci = 0; ci < et.nCols; ci++) et.colW[ci] = et.colMin[ci] * scale;
            }
            else
            {
                var surplus = flow.contentWidth - minSum;
                double slackSum = 0;
                for (int ci = 0; ci < et.nCols; ci++)
                    slackSum += System.Math.Max(0, et.colFull[ci] - et.colMin[ci]);
                for (int ci = 0; ci < et.nCols; ci++)
                {
                    var s = System.Math.Max(0, et.colFull[ci] - et.colMin[ci]);
                    et.colW[ci] = et.colMin[ci] + (slackSum > 1e-6 ? surplus * s / slackSum : 0);
                }
            }
        }

    }

    /// <summary>Parse the table rows and their header/body cells; an empty table is done here.</summary>
    private static bool TryParseEscapedRows(EscapedTableState et, Block block, HtmlFlowCursor flow, Dictionary<string, Dictionary<string, string>> css)
    {
        et.trRows = new List<List<(bool Header, List<Block> Items)>>();
        foreach (Match trm in Regex.Matches(block.TableHtml ?? "",
            @"<tr\b[^>]*>([\s\S]*?)</tr\s*>", RegexOptions.IgnoreCase))
        {
            var cellsRow = new List<(bool, List<Block>)>();
            foreach (Match cm in Regex.Matches(trm.Groups[1].Value,
                @"<(td|th)\b[^>]*>([\s\S]*?)</\1\s*>", RegexOptions.IgnoreCase))
            {
                var isTh = cm.Groups[1].Value.Equals("th", StringComparison.OrdinalIgnoreCase);
                var items = new List<Block>();
                foreach (var cb in ParseBlocks(cm.Groups[2].Value, css,
                    bodyFontSize: 12, controlBoxes: true))
                {
                    if (cb.InlineItems is { } inner) items.AddRange(inner);
                    else if (!cb.IsHardBreak || !string.IsNullOrEmpty(cb.Text)) items.Add(cb);
                }
                cellsRow.Add((isTh, items));
            }
            if (cellsRow.Count > 0) et.trRows.Add(cellsRow);
        }
        if (et.trRows.Count == 0) { flow.lastWasHardBreak = false; return false; }
        return true;
    }
}
