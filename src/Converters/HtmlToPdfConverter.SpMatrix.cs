using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// The sectioned owner's-agenda report's priority-matrix page — the asserted
// page of the dead-stylesheet UA document. The whole section renders from the
// reference's measured ladder: the section h2/h6 open the page at the shared
// section baselines, the inline SVG rasterizes at its CSS-px viewport inside
// the diagram cell, and the topics column lays out at the UA list metrics with
// SimSun ideographs and Times-Roman Latin runs. A topic row whose text carries
// Latin glyphs sits one 13.5 step below its predecessor; pure-ideograph rows
// step 13.764 (both measured on the reference).
internal static partial class HtmlToPdfConverter
{
    private const double SpH2Base = 102.47;     // section heading baseline (every section page shares it)
    private const double SpH6Base = 130.41;     // the intro h6 baseline
    private const double SpChartInset = 2.25;   // the diagram cell's chrome step off the content edge
    private const double SpChartTop = 150.44;   // the svg viewport top
    private const double SpChartW = 405.0;      // style width: 540px
    private const double SpChartH = 402.0;      // style height: 536px
    private const double SpListOff = 410.25;    // topics column off the content left (chart + cell chrome)
    private const double SpListH6Base = 231.94; // the column heading (the cell centers its content)
    private const double SpLiFirstBase = 260.25;
    private const double SpLiPitch = 13.764;    // pure-ideograph row step
    private const double SpLiPitchLatin = 13.5; // the step into a row carrying Latin glyphs
    private const double SpLiIndent = 30.0;     // UA list indentation
    private const double SpLiMarkerOff = 8.7;   // the bullet pen left of the item text
    private const double SpLiWrapW = 259.5;     // item wrap width (21 ideographs fit)

    private static bool IsSpMatrixTable(string? tableHtml) =>
        tableHtml is not null
        && tableHtml.Contains("diagram-sp-topics", StringComparison.Ordinal);

    private static string SpFlat(string s) => Regex.Replace(DecodeEntities(
        Regex.Replace(s, @"<[^>]+>", "")), @"[ \t\r\n]+", " ").Trim();

    private static bool SpIsCjk(char c) => c >= 0x2E80;

    private static double SpMeasure(string seg, bool cjk, double fs)
        => MeasureFaceText(cjk ? "SimSun" : "Times New Roman", seg, fs);

    /// <summary>Emit a mixed CJK/Latin line: ideograph runs embed through the
    /// unicode fallback (SimSun), Latin runs draw in the Standard-14 serif.</summary>
    private static void SpEmitMixed(Page page, double pageHeight, string text,
        double fs, double x, double yTd)
    {
        var start = 0;
        for (var i = 1; i <= text.Length; i++)
        {
            if (i < text.Length && SpIsCjk(text[i]) == SpIsCjk(text[start])) continue;
            var seg = text[start..i];
            EmitPositionedRun(page, "F5", fs, x, pageHeight - yTd, seg);
            x += SpMeasure(seg, SpIsCjk(text[start]), fs);
            start = i;
        }
    }

    /// <summary>Render the priority-matrix section onto its fresh page from the
    /// measured ladder.</summary>
    private static void RenderSpMatrixSection(Page page, double pageHeight, double mL,
        List<string> headings, string tableHtml, List<byte[]> inlineSvgs)
    {
        if (headings.Count > 0)
            SpEmitMixed(page, pageHeight, SpFlat(headings[0]), 18.0, mL, SpH2Base);
        if (headings.Count > 1)
            SpEmitMixed(page, pageHeight, SpFlat(headings[1]), 9.0, mL, SpH6Base);

        var svgM = Regex.Match(tableHtml, @"inline-svg:(\d+)");
        if (svgM.Success && int.TryParse(svgM.Groups[1].Value, out var si)
            && si >= 0 && si < inlineSvgs.Count
            && ImageRasterizer.RasterizeSvg(inlineSvgs[si], out _, out _) is { } png)
            page.AddImage(png, new Rectangle(
                mL + SpChartInset, pageHeight - SpChartTop - SpChartH,
                mL + SpChartInset + SpChartW, pageHeight - SpChartTop));

        var topicsM = Regex.Match(tableHtml,
            @"diagram-sp-topics[""'][^>]*>([\s\S]*?)</td>", RegexOptions.IgnoreCase);
        if (!topicsM.Success) return;
        var listX = mL + SpListOff;
        var h6M = Regex.Match(topicsM.Groups[1].Value, @"<h6[^>]*>([\s\S]*?)</h6>",
            RegexOptions.IgnoreCase);
        if (h6M.Success)
            SpEmitMixed(page, pageHeight, SpFlat(h6M.Groups[1].Value), 9.0, listX, SpListH6Base);

        var y = SpLiFirstBase;
        var first = true;
        foreach (Match li in Regex.Matches(topicsM.Groups[1].Value,
            @"<li[^>]*>([\s\S]*?)</li>", RegexOptions.IgnoreCase))
        {
            var text = SpFlat(li.Groups[1].Value);
            if (text.Length == 0) continue;
            if (!first)
            {
                var hasLatin = false;
                foreach (var c in text)
                    if (!SpIsCjk(c)) { hasLatin = true; break; }
                y += hasLatin ? SpLiPitchLatin : SpLiPitch;
            }
            first = false;
            EmitPositionedRun(page, "F5", 12.0,
                listX + SpLiIndent - SpLiMarkerOff, pageHeight - y, "•");
            var line = "";
            double w = 0;
            foreach (var ch in text)
            {
                var cw = SpMeasure(ch.ToString(), SpIsCjk(ch), 12.0);
                if (w + cw > SpLiWrapW + 1e-6 && line.Length > 0)
                {
                    SpEmitMixed(page, pageHeight, line, 12.0, listX + SpLiIndent, y);
                    y += SpLiPitch;
                    line = "";
                    w = 0;
                }
                line += ch;
                w += cw;
            }
            if (line.Length > 0)
                SpEmitMixed(page, pageHeight, line, 12.0, listX + SpLiIndent, y);
        }
    }
}
