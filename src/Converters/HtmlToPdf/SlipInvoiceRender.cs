using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Render the percent-width till-slip invoice, or null.</summary>
    private static Document? TryRenderSlipInvoice(string html, HtmlLoadOptions? options,
        IReadOnlyDictionary<string, Dictionary<string, string>> css,
        double pageWidth, double pageHeight)
    {
        var si = new SlipInvoiceState();
        si.html = html;
        si.options = options;
        si.css = css;
        si.pageWidth = pageWidth;
        si.pageHeight = pageHeight;
        // the body declares a PERCENT width and lays out as a table box
        if (!si.css.TryGetValue("body", out var bodyRule)
            || !bodyRule.TryGetValue("display", out var disp)
            || !disp.Contains("table", StringComparison.OrdinalIgnoreCase)
            || !bodyRule.TryGetValue("width", out var bw) || !bw.Trim().EndsWith('%'))
            return null;
        if (!double.TryParse(bw.Trim().TrimEnd('%'), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var bodyPct)
            || bodyPct is <= 0 or > 100) return null;
        si.face = "Calibri";
        if (bodyRule.TryGetValue("font-family", out var famv))
            foreach (var cand in famv.Split(','))
            {
                var nm = cand.Trim().Trim('"', '\'');
                if (nm.Length > 0 && !nm.StartsWith('-') && WinMetricsFor(nm) is not null)
                { si.face = nm; break; }
            }
        if (WinMetricsFor(si.face) is not { } fm) return null;
        si.fs = 9.0;
        if (bodyRule.TryGetValue("font-size", out var fsv)
            && TryParseLength(fsv.Trim()) is { } fsPt && fsPt > 0) si.fs = fsPt;

        si.bodyM = Regex.Match(si.html, @"<body\b[^>]*>([\s\S]*)</body\s*>", RegexOptions.IgnoreCase);
        si.body = si.bodyM.Success ? si.bodyM.Groups[1].Value : si.html;
        if (!Regex.IsMatch(si.body, @"<table\b", RegexOptions.IgnoreCase)) return null;

        si.lineH = MetricLineHeight(si.fs, HheaLineSumFor(si.face) ?? fm.sum);
        si.drop = MetricBaselineDrop(si.fs, si.lineH, fm);
        si.boldFace = si.face + "-Bold";
        si.italFace = si.face + "-Italic";

        si.boxLeft = UaBodyMarginPt;
        si.boxW = si.pageWidth * bodyPct / 100.0;
        si.padTop = 0.0;
        if (bodyRule.TryGetValue("padding-top", out var ptv) && TryParseLength(ptv.Trim()) is { } ptPt)
            si.padTop = ptPt;

        si.doc = new Document();
        si.page = si.doc.Pages.Add(si.pageWidth, si.pageHeight);
        EnsureFonts(si.page);
        si.res = "F8";
        si.resB = "F9";
        si.resI = "F10";
        EnsureFont(si.page, si.face, si.res);
        EnsureFont(si.page, si.face + "-Bold", si.resB);
        EnsureFont(si.page, si.face + "-Italic", si.resI);
        si.invc = System.Globalization.CultureInfo.InvariantCulture;

        si.rowAdvance = si.lineH + 2 * SlipPadPt + SlipSpacingPt;
        si.y = si.padTop + UaBodyMarginPt + SlipSpacingPt + SlipPadPt;
        si.drewAny = false;
        foreach (Match tm in Regex.Matches(si.body, @"<table\b[^>]*>([\s\S]*?)</table\s*>",
                     RegexOptions.IgnoreCase))
        {
            if (!DrawSlipTable(si, tm)) break;
        }
        // the sheet's closing <hr>, drawn across the body box
        if (si.drewAny && Regex.IsMatch(si.body, @"<hr\b", RegexOptions.IgnoreCase))
        {
            var hy = si.y - SlipTableGapPt + SlipHrGapPt;
            si.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(si.invc,
                $"q 0 0 0 RG {SlipBorderPt:0.##} w [1 1] 0 d " +
                $"{si.boxLeft:F2} {si.pageHeight - hy:F2} m {si.boxLeft + si.boxW:F2} {si.pageHeight - hy:F2} l S Q\n")));
        }
        return si.drewAny ? si.doc : null;
    }
}
