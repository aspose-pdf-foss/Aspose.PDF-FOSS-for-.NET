using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Each separated column's own band width, chrome and minimum come off the column model.</summary>
    private static void MeasureSeparatedColumnBands(FitBorderedState fb)
    {
        for (var c = 0; c < fb.nCols; c++)
        {
            // …in the cell's OWN face: a Verdana cell of a serif-flow grid is as wide as
            // Verdana sets it, not as the table face would (the review report's header
            // band measured 94 in the serif and 125 in its Verdana).
            // (…and, in the UA grid, its longest LINE: a `<br>` splits the cell's lines and each
            // wraps on its own words - the holdings grid's `CAD $10.0963<br>USD $9.7558` column
            // solves 67.39 on the reference, min + slack, not its concatenated 139)
            foreach (var r in fb.rows)
                if (c < r.Count && r[c].Text.Length > 0)
                {
                    // (an AUTO-width grid only: one that fills a declared or percent box keeps the
                    // calibrated concatenated measure - the 80 % statement grid wraps its names)
                    if (fb.stdSerif && fb.tableWpt <= 0 && fb.tablePct <= 0)
                        foreach (var brSeg in r[c].Text.Split(''))
                            fb.naturalB[c] = Math.Max(fb.naturalB[c],
                                MeasureFaceText(CellFaceName(fb.face, fb.boldFace, r[c]), brSeg.Trim(),
                                    r[c].FontSize ?? fb.mps.fontSize));
                    else
                        fb.naturalB[c] = Math.Max(fb.naturalB[c],
                            MeasureFaceText(CellFaceName(fb.face, fb.boldFace, r[c]), r[c].Text,
                                r[c].FontSize ?? fb.mps.fontSize));
                }
            // …and a cell holding a nested grid asks for that grid's max-content, capped by the box
            // (measured on the case report: the label column beside a column of nested grids drew
            // every grid in a zero-width column, at the page's right edge)
            if (fb.stdSerif && fb.host is not null)
                foreach (var r in fb.rows)
                    if (c < r.Count && r[c].ColSpan <= 1 && r[c].SubTables is { Count: > 0 } natSubs)
                        foreach (var sub in natSubs)
                            fb.naturalB[c] = Math.Max(fb.naturalB[c],
                                Math.Min(MetricGridMaxContentPt(fb.host, sub, r[c]), fb.availW - fb.chromeB));
            var share = fb.colPct[c] > 0 ? fb.colPct[c] / 100.0 * fb.innerW - 2 * fb.p - 2 * fb.bw : 0;
            // A pixel width attribute fixes the column (its text wraps at
            // that width instead of widening it) — but a larger declared
            // SHARE still wins (measured: a 50% column beats its 366px
            // content cells).
            if (fb.colPx[c] > 0)
            {
                // Excel-fragment grid: the width attribute is the BORDER
                // BOX (probed: 80/95/72 px land as 60/71.25/54 pt boxes,
                // shared borders inside) — the generic attribute grid
                // keeps its content-box reading.
                fb.colW[c] = fb.mps.wtInlineGrid
                    ? Math.Max(1, fb.colPx[c] - 2 * fb.p - 2 * fb.bw)
                    : Math.Max(fb.colPx[c] - 2 * fb.p, share);
                // …and never below the column's MIN-CONTENT: a word wider
                // than the attribute widens the column instead of breaking
                // inside itself (probed: the font-size ladder's 36 pt
                // `size="10000"` cell keeps its whole 223.3 pt run past
                // every 40…130 px width its column declares).
                fb.colW[c] = Math.Max(fb.colW[c], MetricColumnMinContent(fb, c));
                continue;
            }
            // A percent share fixes its column too: the 20 % product column
            // wraps its names inside the share (the expected render), and only
            // a word wider than the share widens it.
            fb.colW[c] = share > 0
                ? Math.Max(share, MetricColumnMinContent(fb, c))
                : fb.naturalB[c];
        }
    }
}
