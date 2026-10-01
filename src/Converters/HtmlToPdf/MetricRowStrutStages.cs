using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>An ink-paced or class-sized row takes its declared height as a strut, and an inline statement grid bands its own.</summary>
    private static void ApplyMetricRowStrutAndBands(MetricRowsState mr, int ri, bool inkPacedGrid)
    {
        if (inkPacedGrid || (_quirksRowStrut && MetricRowTextAllClassSized(mr.r) && MetricRowDeclaresHeight(mr, ri)))
        {
            var declared = 0.0;
            // (a row-spanning cell's declared height spans its rows: it bands none of them alone)
            foreach (var mc in mr.r) if (mc.RowSpan <= 1 || !mr.stdSerif) declared = Math.Max(declared, mc.HeightStylePt);
            if (ri < mr.mps.rowHeights.Count) declared = Math.Max(declared, mr.mps.rowHeights[ri] - 2 * mr.p);
            mr.rowContentH = Math.Max(mr.rowContentH, declared);
        }
        // Statement idiom: a row with real text pitches on that text's own
        // line boxes — the table's 12 pt strut floor does not apply. A
        // blank nbsp spacer row pitches on the 1.2 normal line box of its
        // cells' size instead (probed: the 12 pt spacers band 14.25 = 19px).
        if (mr.mps.inlineStatementGrid && mr.rowHasRealText && mr.rowRealTextH > 0
            && mr.rowRealTextH < mr.rowContentH)
            mr.rowContentH = mr.rowRealTextH;
        else if (mr.mps.inlineStatementGrid && !mr.rowHasRealText && mr.rowContentH > 0)
        {
            double blankFs = 0;
            foreach (var mc in mr.r)
                if (mc.Text.Length > 0)
                    blankFs = Math.Max(blankFs, mc.FontSize ?? mr.mps.fontSize);
            if (blankFs > 0)
                mr.rowContentH = MetricLineHeight(blankFs, Table.CssNormalLineHeight);
        }
        // …and a rule-carrying row is taller by its border width (probed:
        // the 1pt single rules add 1, the 2.8pt double adds 2.84).
        if (mr.mps.inlineStatementGrid)
        {
            double rowRuleW = 0;
            foreach (var mc in mr.r)
                rowRuleW = Math.Max(rowRuleW, mc.BorderBottomW);
            mr.rowContentH += rowRuleW;
        }
    }
}
