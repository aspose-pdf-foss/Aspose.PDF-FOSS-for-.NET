using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Slip invoice: one table of the slip drawn.</summary>
    private static bool DrawSlipTable(SlipInvoiceState si, Match tm)
    {
        si.rows = new List<List<(string Text, int Span, bool Head, bool Ital, bool Right, bool Rule)>>();
        si.rowImgs = new List<string?>();
        ParseSlipRows(si, tm);
        if (si.rows.Count == 0) return true;

        si.nCols = 0;
        foreach (var r in si.rows)
        {
            var t = 0;
            foreach (var c in r) t += c.Span;
            si.nCols = Math.Max(si.nCols, t);
        }
        if (si.nCols == 0) return true;

        SolveSlipColumns(si);

        // ── draw ──────────────────────────────────────────────────────
        for (var ri = 0; ri < si.rows.Count; ri++)
        {
            DrawSlipRow(si, ri);
        }
        si.y += SlipTableGapPt;   // the spacing a following table opens with
        return true;
    }
}
