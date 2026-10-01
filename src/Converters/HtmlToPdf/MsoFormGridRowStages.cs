using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal partial class HtmlToPdfConverter
{
    /// <summary>Mso form grid: the row's cells drawn - chrome, inputs and text runs.</summary>
    private static void DrawMsoRowCells(MsoFormGridState mg, MsoRow r)
    {
        foreach (var mc in r.Cells)
        {
            double cw = 0;
            for (var k = 0; k < mc.ColSpan && mg.ci + k < mg.nCols; k++) cw += mg.colW[mg.ci + k];
            mg.ci += mc.ColSpan;
            var top = mg.y; var bot = mg.y - mg.rowH;
            if (mc.BgTeal)
                mg.sb.Append(Compat.Format(mg.invc,
                    $"q 0 0.502 0.502 rg {mg.cx:F2} {bot:F2} {cw:F2} {mg.rowH:F2} re f Q\n"));
            // borders (1px black)
            var lb = new StringBuilder("q 0 0 0 RG 1 w ");
            if (mc.BTop) lb.Append(Compat.Format(mg.invc, $"{mg.cx:F2} {top:F2} m {mg.cx + cw:F2} {top:F2} l S "));
            if (mc.BBottom) lb.Append(Compat.Format(mg.invc, $"{mg.cx:F2} {bot:F2} m {mg.cx + cw:F2} {bot:F2} l S "));
            if (mc.BLeft) lb.Append(Compat.Format(mg.invc, $"{mg.cx:F2} {top:F2} m {mg.cx:F2} {bot:F2} l S "));
            if (mc.BRight) lb.Append(Compat.Format(mg.invc, $"{mg.cx + cw:F2} {top:F2} m {mg.cx + cw:F2} {bot:F2} l S "));
            lb.Append("Q\n");
            mg.sb.Append(lb);

            // content
            var ly = top - 1;
            var lx = mg.cx + MsoCellPadPt + 1;
            var pen = lx;
            var lineOpen = false;
            double lineFs = 0;
            var lineHasCb = false;
            foreach (var run in mc.Runs)
            {
                if (run.NewLine && lineOpen)
                {
                    ly -= lineHasCb ? MsoCheckLinePt : MsoLineOf(lineFs);
                    pen = lx; lineOpen = false; lineFs = 0; lineHasCb = false;
                }
                if (run.Input is { Checkbox: true } cb)
                {
                    // a checkbox rides its line inline, box seated on the text
                    var bx = pen + 2.5;
                    var byy = ly - MsoCheckLinePt + 3.2;
                    mg.sb.Append(Compat.Format(mg.invc,
                        $"q 1 1 1 rg {bx:F2} {byy:F2} {MsoCheckboxPt:F2} {MsoCheckboxPt:F2} re f Q\n"));
                    if (cb.Checked)
                        mg.tsb.Append(Compat.Format(mg.invc,
                            $"BT /F8 7.75 Tf 0 0 0 rg {bx + 0.6:F2} {byy + 1.0:F2} Td (4) Tj ET\n"));
                    pen = bx + MsoCheckboxPt + 3.5;
                    lineOpen = true;
                    lineHasCb = true;
                    lineFs = Math.Max(lineFs, 12);
                    continue;
                }
                if (run.Input is { } inp)
                {
                    if (lineOpen)
                    { ly -= lineHasCb ? MsoCheckLinePt : MsoLineOf(lineFs); pen = lx; lineOpen = false; lineFs = 0; lineHasCb = false; }
                    var bx = pen;
                    var bw = Math.Min(inp.WPt, cw - 2 * MsoCellPadPt);
                    var bTop = ly - 1;
                    mg.sb.Append(Compat.Format(mg.invc,
                        $"q 1 1 1 rg {bx:F2} {bTop - inp.HPt:F2} {bw:F2} {inp.HPt:F2} re f Q\n"));
                    mg.sb.Append(Compat.Format(mg.invc,
                        $"q 0 0 0 RG 0.75 w {bx:F2} {bTop - inp.HPt:F2} {bw:F2} {inp.HPt:F2} re S Q\n"));
                    if (inp.Value.Length > 0 && inp.Value != "?")
                        mg.tsb.Append(Compat.Format(mg.invc,
                            $"BT /F1 10 Tf 0 0 0 rg {bx + MsoInputTextInsetPt:F2} {bTop - MsoInputBaselinePt:F2} Td ({EscapePdfText(inp.Value)}) Tj ET\n"));
                    ly = bTop - inp.HPt - 1.5;
                    pen = lx;
                    continue;
                }
                if (run.Text.Length == 0)
                {
                    // a bare <br> line box
                    ly -= MsoLineOf(run.Fs);
                    pen = lx; lineOpen = false; lineFs = 0; lineHasCb = false;
                    continue;
                }
                var res = RunRes(mg, run);
                var fsz = run.Fs;
                var clean = FilterWinAnsi(run.Text);
                if (clean.Trim().Length == 0) { lineOpen = true; lineFs = Math.Max(lineFs, fsz); continue; }
                var w = MeasureFaceText(RunFace(mg, run), clean, fsz);
                var tx = run.Center ? mg.cx + (cw - w) / 2 : pen;
                var col = run.White ? "1 1 1" : run.Teal ? "0 0.502 0.502" : "0 0 0";
                var by = ly - MsoLineOf(fsz) + MsoLineOf(fsz) * 0.18;
                mg.tsb.Append(Compat.Format(mg.invc,
                    $"BT /{res} {fsz:F2} Tf {col} rg {tx:F2} {by + 1.5:F2} Td ({EscapePdfText(clean)}) Tj ET\n"));
                pen = tx + w + MeasureFaceText(RunFace(mg, run), " ", fsz);
                lineOpen = true;
                lineFs = Math.Max(lineFs, fsz);
            }
            mg.cx += cw;
        }
    }

    /// <summary>Mso form grid: the row's height measured from its cells' lines, inputs and style height.</summary>
    private static void MeasureMsoRowHeight(MsoFormGridState mg, MsoRow r)
    {
        mg.rowH = Math.Max(r.StyleHPt + 1, 0);
        foreach (var mc in r.Cells)
        {
            double h = 0;
            var firstLine = true;
            var lineHasCheck = false;
            foreach (var run in mc.Runs)
            {
                if (run.Input is { Checkbox: true })
                { lineHasCheck = true; continue; }   // inline — its line bills below
                if (run.Input is { } inp)
                {
                    // a same-paragraph (br-broken) input keeps the deep
                    // bottom band; a separate-paragraph one closes tight
                    h += inp.HPt + (run.BrLine ? MsoRowBottomPadPt : MsoTightPadPt);
                    firstLine = false; lineHasCheck = false; continue;
                }
                if (run.NewLine || firstLine)
                    h += lineHasCheck ? MsoCheckLinePt : MsoLineOf(run.Fs);
                firstLine = false;
                lineHasCheck = false;
            }
            if (lineHasCheck) h += MsoCheckLinePt;
            if (h > mg.rowH) mg.rowH = h;
        }
        if (mg.rowH <= 2) mg.rowH = 13.5;
        mg.allTeal = r.Cells.Count > 0;
        foreach (var mc in r.Cells)
        {
            if (!mc.BgTeal) { mg.allTeal = false; break; }
            foreach (var run in mc.Runs)
                if (run.Text.Trim().Length > 0 || run.Input is not null) { mg.allTeal = false; break; }
        }
    }

    /// <summary>Mso form grid: an open group closed before this row; a side-cell-only row carries no band.</summary>
    private static bool CloseMsoGroupBeforeRow(MsoFormGridState mg, MsoRow r)
    {
        if (mg.groupOpen)
        {
            CloseGroup(mg, mg.y);
            // the stray side-cell row that followed the nested table holds
            // no band of its own
            var sideOnly = true;
            foreach (var mc in r.Cells)
                foreach (var run in mc.Runs)
                    if (run.Text.Trim().Length > 0 || run.Input is not null) { sideOnly = false; break; }
            if (sideOnly) return true;
        }
        return false;
    }

    /// <summary>Mso form grid: a nested roster row - checkbox and label pairs on the two-column rhythm.</summary>
    private static bool DrawMsoRosterRow(MsoFormGridState mg, MsoRow r)
    {
        if (r.Nested)
        {
            // roster rows: checkbox + label pairs on the measured two-column
            // rhythm inside the host box
            var ly0 = mg.y;
            var pen0 = mg.x0 + MsoRosterCol1Pt;
            var ci0 = 0;
            foreach (var mc in r.Cells)
            {
                var penN = ci0 == 0 ? mg.x0 + MsoRosterCol1Pt : mg.x0 + MsoRosterCol2Pt;
                ci0++;
                foreach (var run in mc.Runs)
                {
                    if (run.Input is { Checkbox: true } cbN)
                    {
                        var bx = penN + 2.5;
                        var byy = ly0 - MsoCheckLinePt + 3.2;
                        mg.sb.Append(Compat.Format(mg.invc,
                            $"q 1 1 1 rg {bx:F2} {byy:F2} {MsoCheckboxPt:F2} {MsoCheckboxPt:F2} re f Q\n"));
                        if (cbN.Checked)
                            mg.tsb.Append(Compat.Format(mg.invc,
                                $"BT /F8 7.75 Tf 0 0 0 rg {bx + 0.6:F2} {byy + 1.0:F2} Td (4) Tj ET\n"));
                        penN = bx + MsoCheckboxPt + 3.5;
                        continue;
                    }
                    if (run.Text.Length == 0 || run.Input is not null) continue;
                    var clean0 = FilterWinAnsi(run.Text);
                    if (clean0.Trim().Length == 0) continue;
                    var w0 = MeasureFaceText(RunFace(mg, run), clean0, run.Fs);
                    mg.tsb.Append(Compat.Format(mg.invc,
                        $"BT /{RunRes(mg, run)} {run.Fs:F2} Tf 0 0 0 rg {penN:F2} {ly0 - MsoCheckLinePt + 4.6:F2} Td ({EscapePdfText(clean0)}) Tj ET\n"));
                    penN += w0;
                }
            }
            mg.y -= MsoCheckLinePt;
            return true;
        }
        return false;
    }
}
