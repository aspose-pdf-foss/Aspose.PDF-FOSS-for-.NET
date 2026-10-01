using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The stages of the bar-chart render: the pixel and style reads, the text and band draws, and the chart sections.
    private static double BcPx(BarChartRenderState bh, string? v, double dflt = 0)
        => v is not null && double.TryParse(v.Trim().TrimEnd('p', 'x', '%'),
            System.Globalization.NumberStyles.Float, bh.invc, out var d) ? d : dflt;

    private static string? BcStyleProp(string tag, string prop)
        => Regex.Match(tag, @"style\s*=\s*['""][^'""]*" + prop + @"\s*:\s*([^;'""]+)",
            RegexOptions.IgnoreCase) is { Success: true } m
            ? m.Groups[1].Value.Trim() : null;

    private static void BcText(BarChartRenderState bh, string text, double x, double yBaselineTd, double fs, Color c)
    {
        if (text.Length == 0) return;
        bh.sb.Append(Compat.Format(bh.invc,
            $"q {c.R / 255.0:0.###} {c.G / 255.0:0.###} {c.B / 255.0:0.###} rg BT /F5 {fs:0.##} Tf " +
            $"1 0 0 1 {x:0.##} {bh.pageHeight - yBaselineTd:0.##} Tm ({EscapePdfString(text)}) Tj ET Q\n"));
    }

    private static void BcBand(BarChartRenderState bh, string cls, double dfltFsPx)
    {
        var m = Regex.Match(bh.html,
            @"<div\b[^>]*class\s*=\s*['""]" + cls + @"['""](?<tag>[^>]*)>(?<body>[\s\S]*?)</div>",
            RegexOptions.IgnoreCase);
        if (!m.Success) return;
        var tag = m.Groups["tag"].Value;
        var fsPt = BcPx(bh, BcStyleProp("<x " + tag + ">", "font-size"), dfltFsPx) * 0.75;
        var hPt = BcPx(bh, BcStyleProp("<x " + tag + ">", "height"), 40) * 0.75;
        var col = ParseCssColor(BcStyleProp("<x " + tag + ">", "color") ?? "") ?? bh.chartGray;
        var txt = Regex.Replace(DecodeEntities(
            Regex.Replace(m.Groups["body"].Value, @"<[^>]+>", " ")), @"\s+", " ").Trim();
        var w = MeasureFaceText("Times New Roman", txt, fsPt);
        BcText(bh, txt, (bh.pageWidth - w) / 2, bh.yTd + fsPt * (UaSerifBaselineDropPt / 12.0), fsPt, col);
        bh.yTd += hPt;
    }
}
