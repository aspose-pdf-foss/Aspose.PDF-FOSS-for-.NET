using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The UA cell-box dialects widen their chosen columns out to the declared box, sharing the surplus by the rule each dialect states.</summary>
    private static void WidenUaCellBoxColumnsToBox(TableWidthSolveState ws)
    {
        if ((ws.uaCellBoxes || ws.overDeclaredDraw || ws.ptCellWidths || ws.colModel.ancestorScopedGrid)
            && ReferenceEquals(ws.chosen, ws.colModel.colMaxW))
        {
            List<double>? floored = null;
            for (var i = 0; i < ws.chosen.Count && i < ws.colModel.colDeclW.Count; i++)
                if (ws.colModel.colDeclW[i] > ws.chosen[i])
                {
                    floored ??= new List<double>(ws.chosen);
                    floored[i] = ws.colModel.colDeclW[i];
                }
            if (floored is not null) ws.chosen = floored;
            // A UA-boxed grid whose declared columns overflow its declared box shrinks them into it in
            // proportion, each floored at its min-content; the auto columns keep theirs (measured on
            // the royalty statement's masthead: 5.5in + 1.5in beside a 42.67 logo column in a 403
            // box centre the masthead at 280 = 96 + 42.67 + 283.1 / 2).
            if (ws.uaCellBoxes && ws.colModel.tableWidthDeclared && ws.availWidthPt > 0)
            {
                var declBoxW = ws.colModel.tableWidthFrac * ws.availWidthPt;
                double sumAll = 0; foreach (var w in ws.chosen) sumAll += w;
                var excess = sumAll - declBoxW;
                // (each declared column sheds in proportion to its SLACK above min-content - measured on the
                //  quotation's status grid: 60/90/90/93.75/93.75/60 + pads in a 487.5 box land 66.4/87.4/
                //  86.9/90.7/91.5/61, the Action column with 5 pt of slack keeping nearly all of it)
                double shrinkable = 0;
                for (var i = 0; i < ws.chosen.Count && i < ws.colModel.colDeclW.Count; i++)
                    if (ws.colModel.colDeclW[i] > 0 && ws.chosen[i] > ws.colModel.colMinW[i] + 0.01) shrinkable += ws.chosen[i] - ws.colModel.colMinW[i];
                if (excess > 0.01 && shrinkable > 0)
                {
                    var k = Math.Max(0, (shrinkable - excess) / shrinkable);
                    var shrunk = new List<double>(ws.chosen);
                    for (var i = 0; i < shrunk.Count && i < ws.colModel.colDeclW.Count; i++)
                        if (ws.colModel.colDeclW[i] > 0 && shrunk[i] > ws.colModel.colMinW[i] + 0.01)
                        {
                            shrunk[i] = ws.colModel.colMinW[i] + (shrunk[i] - ws.colModel.colMinW[i]) * k;
                            // (the draw-time re-solve reads the per-column model: pin it to the shrunk box)
                            ws.colModel.colMinW[i] = shrunk[i];
                            if (i < ws.colModel.colMaxW.Count) ws.colModel.colMaxW[i] = shrunk[i];
                        }
                    ws.chosen = shrunk;
                }
            }
            // A fitting table that DECLARES its width fills that box: the declared
            // columns keep exactly what they asked for and the AUTO columns absorb
            // all the leftover (an auto label column beside fixed date columns
            // stretches to the full container).
            if (ws.colModel.tableWidthDeclared && ws.availWidthPt > 0)
            {
                var boxW = ws.colModel.tableWidthFrac * ws.availWidthPt;
                double sumSel = 0; foreach (var w in ws.chosen) sumSel += w;
                double autoW = 0;
                var autoCount = 0;
                for (var i = 0; i < ws.chosen.Count; i++)
                    if (i >= ws.colModel.colDeclW.Count || ws.colModel.colDeclW[i] <= 0) { autoW += ws.chosen[i]; autoCount++; }
                if (boxW > sumSel + 0.01 && autoCount > 0)
                {
                    var grown = new List<double>(ws.chosen);
                    var leftover = boxW - sumSel;
                    for (var i = 0; i < grown.Count; i++)
                        if (i >= ws.colModel.colDeclW.Count || ws.colModel.colDeclW[i] <= 0)
                            grown[i] += autoW > 0 ? leftover * ws.chosen[i] / autoW : leftover / autoCount;
                    ws.chosen = grown;
                }
            }
            // pt-styled fragment: an over-declared grid SQUEEZES into its
            // box instead of widening the sheet (probed: the address card's
            // content-box columns sum 493 into the 485.4 content width),
            // each column giving up width in proportion to its slack above
            // MIN-CONTENT (probed: the report grid's five columns shed
            // 18/6.7/0.6/6/4.3 of a 35.5 deficit).
            if (ws.ptCellWidths)
            {
                var boxCap = (ws.colModel.tableWidthDeclAbsPt > 0
                    ? Math.Min(ws.colModel.tableWidthDeclAbsPt, ws.availWidthPt) : ws.availWidthPt)
                    - ws.colModel.ptMaxCellBorderW;
                ws.chosen = SqueezeBySlack(ws.chosen, boxCap, ws.colModel.colMinW);
            }
        }
    }
}
