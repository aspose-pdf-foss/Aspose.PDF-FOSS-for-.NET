using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The table parser's working set, lifted out of BuildTableFromHtml: each
// method takes the parse state, the column model and the settled dialect
// scalars it reads. Bodies are verbatim.
    private static string CellFaceName(string face, string boldFace, MetricCell mc) => mc.Face is { } cf
        ? cf + (mc.Bold ? " Bold" : mc.Italic ? " Italic" : "")
        : (mc.Bold ? boldFace : face);

    private static (double asc, double sum) CellFm((double asc, double sum) fm, MetricCell mc) => mc.Face is { } cf
        ? (WinMetricsFor(cf) ?? fm) : fm;

    private static double CellLineOf(MetricParseState mps, bool stdSerif, bool wrapperStacks, double hheaSum, string face, (double asc, double sum) fm, MetricCell mc, double cellFs)
    {
        // the collapsed class grid's LINE-HEIGHT pitches every cell line
        if (mps.collapsedLineH > 0) return mps.collapsedLineH;
        if (stdSerif && mc.FontTagSized)
            return Math.Max(MetricLineHeight(mps.fontSize, hheaSum),
                            MetricLineHeight(cellFs, hheaSum));
        var cSum0 = CellFm(fm, mc).sum;
        // pt-report cells pace on the face's hhea line (probed: 9pt Arial
        // rows pitch 10.5 = 14px, not the win-metric 13px) — and so do the
        // saved-statement grid's inline-sized cells (11pt Times rows pitch
        // 12.75 = 17px, the hhea box).
        if ((!stdSerif && wrapperStacks) || mps.inlineStatementGrid)
            cSum0 = HheaLineSumFor(mc.Face ?? face) ?? cSum0;
        return MetricLineHeight(cellFs, cSum0 <= 1.0 ? 1.2 : cSum0);
    }

    private static double CellDropOf(MetricParseState mps, bool stdSerif, (double asc, double sum) fm, MetricCell mc, double cellFs, double box)
        => stdSerif && mc.FontTagSized
            ? Math.Max(MetricBaselineDrop(mps.fontSize, box, fm),
                       MetricBaselineDrop(cellFs, box, CellFm(fm, mc)))
            : MetricBaselineDrop(cellFs, box, CellFm(fm, mc));

    private static string ResOfFlatOn(MetricParseState mps, Dictionary<string, string> flatRes, string face, string boldFace, Page pg, MetricCell mc)
    {
        var fn = CellFaceName(face, boldFace, mc);
        if (!flatRes.TryGetValue(fn, out var rn))
        {
            // Pick a name no page-level font has already claimed for
            // something else — the flow's Type0 embeds share this /Font
            // dictionary and count through the same F-numbers.
            var fd = (pg.Dict.Get("Resources") as Core.PdfDictionary)?
                .Get("Font") as Core.PdfDictionary;
            var idx = 8 + flatRes.Count;
            while (fd?.Get("F" + idx) is { } takenObj
                   && (takenObj is not Core.PdfDictionary taken
                       || taken.GetName("BaseFont") != fn.Replace(" ", "")))
                idx++;
            rn = "F" + idx;
            flatRes[fn] = rn;
        }
        EnsureFont(pg, fn.Replace(" ", ""), rn);
        return rn;
    }
}
