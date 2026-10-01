using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Render a covering-letter export, or null without the fingerprint.</summary>
    private static Document? TryRenderCoveringLetter(string html,
        double pageWidth, double pageHeight)
    {
        var cl = new CoveringLetterState();
        cl.html = html;
        cl.pageWidth = pageWidth;
        cl.pageHeight = pageHeight;
        if (!cl.html.Contains("covering-letter", StringComparison.Ordinal)
            || !cl.html.Contains("data-berthr-editable", StringComparison.Ordinal)
            || !Regex.IsMatch(cl.html, @"class\s*=\s*[""']justify[""']", RegexOptions.IgnoreCase))
            return null;
        if (WinMetricsFor("Arial") is not { } am) return null;
        cl.cm = WinMetricsFor("Candara Bold") ?? am;

        cl.contentL = 96.0;
        cl.contentR = cl.pageWidth - LtRightInset;
        cl.justL = cl.contentL + LtJustPad;
        cl.justR = cl.contentR - LtJustPad;
        cl.liX = cl.justL + LtUlMargin + LtLiIndent;
        cl.liRight = cl.justR - LtUlMargin;
        cl.dropP = MetricBaselineDrop(10.5, LtLineP, am);
        cl.dropEm = MetricBaselineDrop(10.5, LtLineEm, am);

        if (!ParseCoveringLetterHeader(cl)) return null;

        BuildCoveringLetterFlow(cl);
        if (cl.flow.Count == 0) return null;

        OpenCoveringLetterPage(cl);

        EmitCoveringLetterHeading(cl);

        EmitCoveringLetterAddress(cl);

        EmitCoveringLetterFlow(cl);
        return cl.doc;
    }
}
