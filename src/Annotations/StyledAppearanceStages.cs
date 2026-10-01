using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

public partial class FreeTextAnnotation : MarkupAnnotation
{
// The stages of the styled free-text appearance: the font dictionary, metrics, variant, character width and word flush helpers, and one output line.
    private PdfDictionary FtFontDict(StyledAppearanceState ra, string v)
    { if (!ra.fontDicts.TryGetValue(v, out var d)) { d = MakeFreeTextFontDict(v); ra.fontDicts[v] = d; } return d; }

    private double FtCharW(StyledAppearanceState ra, char c, RichTextFontStyles s)
    => FtMetrics(ra, FtVarOf(ra, s)).MeasureString(c.ToString(), ra.size);

    private void FtFlushWord(StyledAppearanceState ra)
    {
        if (ra.word.Count == 0) return;
        if (ra.lineW > 0 && ra.lineW + ra.wordW > ra.avail) { ra.outLines.Add(ra.line); ra.line = new(); ra.lineW = 0; }
        foreach (var t in ra.word) { ra.line.Add(t); }
        ra.lineW += ra.wordW; ra.word.Clear(); ra.wordW = 0;
    }
    private Aspose.Pdf.Text.FontMetrics FtMetrics(StyledAppearanceState ra, string v) { if (!ra.metricsCache.TryGetValue(v, out var m)) { m = Aspose.Pdf.Text.FontMetrics.FromFontDict(FtFontDict(ra, v), InternalReader); ra.metricsCache[v] = m; } return m; }
    private string FtVarOf(StyledAppearanceState ra, RichTextFontStyles s) => VariantFontName(ra.baseFont,
        (s & RichTextFontStyles.Bold) != 0, (s & RichTextFontStyles.Italic) != 0);

    /// <summary></summary>
    private bool EmitStyledAppearanceLine(StyledAppearanceState ra, int li)
    {
        double baseY = ra.y0 - li * ra.leading;
        if (baseY < -ra.size) return false; // ran past the bottom of the box
        double x = ra.inset;
        var cells = ra.outLines[li];
        int j = 0;
        while (j < cells.Count)
        {
            var st = cells[j].st;
            var run = new System.Text.StringBuilder();
            double runW = 0;
            while (j < cells.Count && cells[j].st == st) { run.Append(cells[j].ch); runW += FtCharW(ra, cells[j].ch, st); j++; }
            string v = FtVarOf(ra, st);
            ra.sb.Append("/").Append(ResName(v)).Append(' ').Append(FtNum(ra, ra.size)).Append(" Tf\n");
            ra.sb.Append("1 0 0 1 ").Append(FtNum(ra, x)).Append(' ').Append(FtNum(ra, baseY)).Append(" Tm\n");
            ra.sb.Append('(').Append(EscapePdfString(run.ToString())).Append(") Tj\n");
            if ((st & RichTextFontStyles.Underline) != 0)
                ra.underlines.Add((x, baseY - ra.size * 0.12, runW));
            x += runW;
        }
        return true;
    }
    private static string FtNum(StyledAppearanceState ra, double v) => v.ToString("0.###", ra.ci);
}
