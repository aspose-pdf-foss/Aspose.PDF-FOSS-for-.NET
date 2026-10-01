using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Renders each invoice table at its column edges: the head band, the rows with their cells and rules, and the totals block.</summary>
    private static void RenderInvoiceTables(PrintInvoiceState pv)
    {
        for (var t = 0; t < pv.tables.Count; t++)
        {
            if (t > 0) pv.yTop += PrintTableOpenPt - PrintRowPitchPt;
            var isItem = t == pv.itemTableIdx;
            var trailerRow = 0;
            List<(string, bool, bool)>? lastValuesRow = null;
            foreach (var (cells, anyTh) in pv.tables[t])
            {
                var filled = new List<(string Text, bool Th, bool Italic)>();
                foreach (var cc in cells) if (cc.Item1.Length > 0) filled.Add(cc);
                // An all-empty row collapses — it holds no line band.
                if (filled.Count == 0) continue;
                var pitch = PrintRowPitchPt;
                if (isItem && filled.Count >= 4)
                {
                    // Column header / values row: right-aligned at the col edges.
                    for (var c = 0; c < filled.Count && c < pv.colEdges.Length; c++)
                    {
                        var (txt, th2, it2) = filled[c];
                        Draw(pv, txt, pv.colEdges[c] - PrintColRightInsetPt - Measure(pv, txt, th2, it2),
                            pv.yTop, th2, it2);
                    }
                    if (!anyTh) lastValuesRow = cells;
                }
                // A totals row carries an EMPHASISED label (th/bold); the
                // trailer rows (hash pairs, edition lines) are plain pairs.
                else if (isItem && filled.Count == 2 && (filled[0].Th || anyTh))
                {
                    // Close the values band with the dashed cell bottoms first.
                    if (lastValuesRow is not null)
                    {
                        var dashY = pv.yTop - PrintRowPitchPt + PrintDashDropPt;
                        var segL = pv.x0 + 1.5;
                        for (var c = 0; c < pv.colEdges.Length; c++)
                        {
                            Dash(pv, segL, pv.colEdges[c], dashY);
                            segL = pv.colEdges[c] + 1.5;
                        }
                        lastValuesRow = null;
                    }
                    var (lab, labTh, labIt) = filled[0];
                    var (val, valTh, valIt) = filled[1];
                    Draw(pv, lab, pv.colEdges[^2] - PrintColRightInsetPt - Measure(pv, lab, labTh, labIt),
                        pv.yTop, labTh, labIt);
                    Draw(pv, val, pv.colEdges[^1] - PrintColRightInsetPt - Measure(pv, val, valTh, valIt),
                        pv.yTop, valTh, valIt);
                }
                else if (filled.Count >= 2)
                {
                    var isTrailerRow = t > pv.itemTableIdx || (isItem && !filled[0].Th && !anyTh);
                    if (isTrailerRow && pv.trailerTop < 0)
                    {
                        // The trailer opens two bands below the totals.
                        pv.yTop += PrintTrailerGapPt - PrintRowPitchPt;
                        pv.trailerTop = pv.yTop;
                    }
                    var (lab, labTh, labIt) = filled[0];
                    var (val, valTh, valIt) = filled[1];
                    Draw(pv, lab, pv.x0 + PrintCellInsetPt, pv.yTop, labTh, labIt);
                    var valX = isTrailerRow
                        ? pv.x0 + PrintZoiValueOffPt
                        : pv.x0 + PrintCellInsetPt + PrintValueColFrac * PrintContainerPt;
                    Draw(pv, val, valX, pv.yTop, valTh, valIt);
                    if (isTrailerRow && trailerRow < PrintTrailerPitchPt.Length)
                        pitch = PrintTrailerPitchPt[trailerRow++];
                }
                else
                {
                    var (txt, th2, it2) = filled[0];
                    Draw(pv, txt, pv.x0 + PrintCellInsetPt, pv.yTop, th2, it2);
                }
                pv.yTop += pitch;
            }
        }
    }

    /// <summary>Collects the invoice's tables from the DOM with their declared column widths.</summary>
    private static void CollectInvoiceTables(PrintInvoiceState pv)
    {
        foreach (var el in pv.dom.Descendants())
        {
            if (el.Tag != "table") continue;
            var rows = new List<(List<(string, bool, bool)>, bool)>();
            foreach (var tr in el.Descendants())
            {
                if (tr.Tag != "tr") continue;
                var cells = new List<(string, bool, bool)>();
                var anyTh = false;
                foreach (var cd in tr.Children)
                {
                    if (cd.Tag is not ("td" or "th")) continue;
                    var italic = cd.Attrs is not null && cd.Attrs.TryGetValue("class", out var ccls)
                        && ccls.Contains("italic", StringComparison.OrdinalIgnoreCase);
                    foreach (var sp2 in cd.Descendants())
                        if (sp2.Tag == "span" && sp2.Attrs is not null
                            && sp2.Attrs.TryGetValue("class", out var scls)
                            && scls.Contains("italic", StringComparison.OrdinalIgnoreCase))
                            italic = true;
                    var txt = DomText(cd, pv.css);
                    if (cd.Tag == "th") anyTh = true;
                    cells.Add((txt, cd.Tag == "th", italic));
                }
                if (cells.Count > 0) rows.Add((cells, anyTh));
            }
            if (rows.Count > 0) pv.tables.Add(rows);
        }
    }
}
