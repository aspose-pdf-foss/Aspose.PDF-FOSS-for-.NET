using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Under a fixed table layout whose declared column widths overshoot the available width, redraws the columns proportionally so the grid lands within a point of the browser's.</summary>
    private static void ApplyOverDeclaredColumnDraw(TableStyleConfig cfg, TableParseState ps, TableColumnModel colModel)
    {
        if (cfg.overDeclaredDraw && colModel.colWidthsPt is null && cfg.availWidthPt > 0 && colModel.maxCols > 0
            && cfg.tblStyle.TryGetValue("table-layout", out var tlFixDraw)
            && tlFixDraw.Contains("fixed", StringComparison.OrdinalIgnoreCase)
            && colModel.colPctW.Count > 0)
        {
            while (colModel.colPctW.Count < colModel.maxCols) colModel.colPctW.Add(0);
            var fixBase = ps.rowPctDeclMax > 100.0 + 1e-6
                ? cfg.availWidthPt - UaBodyMarginPt
                : cfg.availWidthPt + OverDeclaredBleedRightPt;
            var padPair = 2 * Math.Max(0, cfg.padSide);
            colModel.colWidthsPt = new List<double>(colModel.maxCols);
            double fixedSum = 0;
            var autoIdx = new List<int>();
            for (var i = 0; i < colModel.maxCols; i++)
            {
                var minC = i < colModel.colMinBrkW.Count ? colModel.colMinBrkW[i] : 0;
                if (colModel.colPctW[i] > 0)
                    colModel.colWidthsPt.Add(Math.Max(colModel.colPctW[i] / 100.0 * fixBase + padPair, minC));
                else if (i < colModel.colDeclW.Count && colModel.colDeclW[i] > 0)
                    colModel.colWidthsPt.Add(Math.Max(colModel.colDeclW[i] + padPair, minC));
                else
                {
                    colModel.colWidthsPt.Add(0);
                    autoIdx.Add(i);
                }
                fixedSum += colModel.colWidthsPt[i];
            }
            if (autoIdx.Count > 0)
            {
                var autoShare = Math.Max(0, fixBase + padPair * colModel.maxCols - fixedSum) / autoIdx.Count;
                foreach (var ai in autoIdx)
                    colModel.colWidthsPt[ai] = Math.Max(autoShare,
                        ai < colModel.colMinBrkW.Count ? colModel.colMinBrkW[ai] : 0);
            }
        }
    }

    /// <summary>Folds each spanning cell's minimum and maximum into the columns it covers, sharing the excess evenly.</summary>
    private static void ApplySpanConstraints(TableColumnModel colModel, bool uaCellBoxes = false)
    {
        foreach (var (start, span, sMin, sMax, sHdr) in colModel.spanConstraints)
        {
            if (start + span > colModel.colMinW.Count) continue;
            void Raise(List<double> arr, double target, bool proportional = false)
            {
                double sum = 0; for (var k = 0; k < span; k++) sum += arr[start + k];
                if (sum >= target || span <= 0) return;
                // (a UA-boxed grid's spanning cell scales the spanned columns' MAX-content in proportion to it, the
                //  percent-grid span law - the mailing's remittance rows keep the 75/25 split of the
                //  option and label columns under them)
                if (proportional && uaCellBoxes && sum > 0)
                {
                    for (var k = 0; k < span; k++) arr[start + k] *= target / sum;
                    return;
                }
                // …and the deficit lands on the columns that can TAKE it: a column with a
                // declared width keeps it. A spanning logo cell beside a 15 px spacer was
                // spreading its own width over both, floor-ing the spacer at a third of
                // the logo and pushing everything in the row that far right.
                var takers = 0;
                for (var k = 0; k < span; k++)
                    if (start + k >= colModel.colDeclW.Count || colModel.colDeclW[start + k] <= 0) takers++;
                var add = (target - sum) / (takers > 0 ? takers : span);
                for (var k = 0; k < span; k++)
                    if (takers <= 0 || start + k >= colModel.colDeclW.Count || colModel.colDeclW[start + k] <= 0)
                        arr[start + k] += add;
            }
            Raise(colModel.colMinW, sMin);
            Raise(colModel.colMaxW, sMax, proportional: true);
            if (sHdr > 0) Raise(colModel.colHdrW, sHdr);
            if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_CELL") == "1")
                Console.Error.WriteLine($"[span] start={start} span={span} min={sMin:0.#} max={sMax:0.#} -> min=[{string.Join(" ", colModel.colMinW.ConvertAll(v => v.ToString("0.#")))}]");
        }
    }
}
