using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The 9 pt footer notice's 14 px line box.</summary>
    private const double SrFooterLinePt = 10.5;
    /// <summary>A preamble line's glyph top under its line-box top (measured: the title at 78.05 on the 78 content top, the
    /// notice at 314.70 on its 314.53 box).</summary>
    private const double SrPreambleSeatPt = 1.83;
    private const double SrFooterSeatPt = 1.56;

    /// <summary>The worksheet's PAGE PREAMBLE, probed on the procedure page: every line centred on the
    /// 96..W−96 box - the `.header-center` title on the content top; the `.top-page-align` block a
    /// percent margin below, the percent of the box WIDTH (30 % of 525.75 = 157.7; its first child's
    /// own margin collapses into it); its `.header-margin` lines their 5 px margin apart, bold under
    /// a `strong`, ruled under a `u`; the `.footer-dv` notices at their inline 12 px; the first step
    /// row one row margin under the last notice. Nothing when the page carries no title.</summary>
    private static void RenderStepRowPreamble(StepRowsState sw)
    {
        var titleInner = ExtractBalancedDivInner(sw.html, "header-center");
        if (titleInner is null) return;
        double x0 = 96.0, x1 = sw.pageWidth - 96.0, boxW = x1 - x0;
        var y = sw.contentTop;
        void Centred(string text, bool bold, bool under, double fs, double top)
        {
            var w = MeasureFaceText(bold ? "Arial-Bold" : "Arial", text, fs);
            var x = x0 + (boxW - w) / 2;
            Run(sw, bold ? "FB" : "FA", fs, x, top, text);
            if (under) HLine(sw, x, x + w, top + fs * (ArialAscentEm + 0.11));
        }
        var title = Flat(sw, titleInner);
        if (title.Length > 0)
        {
            Centred(title, Regex.IsMatch(titleInner, @"<strong\b", RegexOptions.IgnoreCase), false, PpFontPt, y + SrPreambleSeatPt);
            y += SrLinePt;
        }
        var blockInner = ExtractBalancedDivInner(sw.html, "top-page-align");
        if (blockInner is not null)
        {
            y += boxW * CssPercentOrZero(sw.css, ".top-page-align.vertical-center", "margin-top");
            var linePad = CssLengthOrZero(sw.css, ".header-margin", "margin-top");
            var first = true;
            foreach (Match hm in Regex.Matches(blockInner, @"<div\b[^>]*class\s*=\s*['""]header-margin['""][^>]*>([\s\S]*?)</div>", RegexOptions.IgnoreCase))
            {
                var text = Flat(sw, hm.Groups[1].Value);
                if (text.Length == 0) continue;
                if (!first) y += linePad;
                first = false;
                var before = blockInner[..hm.Index];
                var bold = Regex.Matches(before, @"<strong\b", RegexOptions.IgnoreCase).Count > Regex.Matches(before, @"</strong\s*>", RegexOptions.IgnoreCase).Count;
                Centred(text, bold, Regex.IsMatch(hm.Groups[1].Value, @"<u\b", RegexOptions.IgnoreCase), PpFontPt, y + SrPreambleSeatPt);
                y += SrLinePt;
            }
        }
        foreach (Match fm in Regex.Matches(sw.html, @"<div\b[^>]*class\s*=\s*['""]footer-dv['""]([^>]*)>([\s\S]*?)</div>", RegexOptions.IgnoreCase))
        {
            var text = Flat(sw, fm.Groups[2].Value);
            if (text.Length == 0) continue;
            var fsM = Regex.Match(fm.Groups[1].Value, @"font-size\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
            var fs = fsM.Success ? double.Parse(fsM.Groups[1].Value, sw.invc) * 0.75 : PpFontPt;
            Centred(text, false, false, fs, y + SrFooterSeatPt);
            y += SrFooterLinePt;
        }
        // (the first step row opens on the last notice's line-box bottom: its row margin collapses - measured 346.78)
        sw.yTd = y + SrProcSeatPt;
    }

    /// <summary>A sheet rule's percent value as a fraction (`30%` → 0.3); 0 without one.</summary>
    private static double CssPercentOrZero(IReadOnlyDictionary<string, Dictionary<string, string>> css, string selector, string prop)
    {
        if (!css.TryGetValue(selector, out var rule) || !rule.TryGetValue(prop, out var v)) return 0;
        var m = Regex.Match(v.Trim(), @"^([\d.]+)\s*%$");
        return m.Success ? double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) / 100.0 : 0;
    }

    /// <summary>A sheet rule's length in points; 0 without one.</summary>
    private static double CssLengthOrZero(IReadOnlyDictionary<string, Dictionary<string, string>> css, string selector, string prop)
        => css.TryGetValue(selector, out var rule) && rule.TryGetValue(prop, out var v) && TryParseLength(v) is { } pt ? pt : 0;
}
