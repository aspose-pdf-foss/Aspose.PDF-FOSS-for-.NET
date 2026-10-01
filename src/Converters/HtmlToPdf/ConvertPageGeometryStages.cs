using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>A letter-width page with no authored margins takes the dialect's own margin box.</summary>
    private static void ResolveDefaultPageMargins(ConvertState cv)
    {
        if (!cv.marginsExplicit && cv.pageWidth <= 612.0)
        {
            var rbodyM = Regex.Match(cv.html, @"<body\b[^>]*style\s*=\s*(['""])([^'""]*)\1",
                RegexOptions.IgnoreCase);
            if (rbodyM.Success)
            {
                var rbw = Regex.Match(rbodyM.Groups[2].Value,
                    @"(?<![-\w])width\s*:\s*([\d.]+\s*(?:cm|mm|in|pt))", RegexOptions.IgnoreCase);
                if (rbw.Success && TryParseLength(rbw.Groups[1].Value.Replace(" ", "")) is { } rbwPt
                    && rbwPt > 0 && cv.marginLeft + rbwPt + ReportPageRightPt > cv.pageWidth)
                {
                    cv.pageWidth = cv.marginLeft + rbwPt + ReportPageRightPt;
                    cv.marginRight = ReportPageRightPt;
                    cv.grewSheetFromBodyWidth = true;
                }
            }
            // …and the same widening for a STYLESHEET class declaring a physical
            // width, applied to a top-level wrapper div (the .ipdPortrait 8in
            // form-letter frame: the sheet fits the content measure,
            // which the wrapped-row saturation pins at the declared width).
            if (cv.pageWidth <= 612.0)
                foreach (var (wsel, wprops) in cv.css)
                {
                    if (!wsel.StartsWith('.') || wsel.Contains(' ')) continue;
                    if (!wprops.TryGetValue("width", out var wv)) continue;
                    if (!Regex.IsMatch(wv, @"^\s*[\d.]+\s*(cm|mm|in|pt)\s*$",
                            RegexOptions.IgnoreCase)) continue;
                    if (!Regex.IsMatch(cv.html,
                            @"<div\b[^>]*class\s*=\s*[""'][^""']*\b" + Regex.Escape(wsel[1..]) + @"\b",
                            RegexOptions.IgnoreCase)) continue;
                    if (TryParseLength(wv.Trim()) is not { } wclsPt || wclsPt <= 0) continue;
                    // The content origin includes the UA body inset (probed: the
                    // minimal 8in-div sheet inks from 96 on a 752 page).
                    var wclsNeed = cv.marginLeft + UaBodyMarginPt + wclsPt + ReportPageRightPt;
                    if (wclsNeed > cv.pageWidth)
                    {
                        cv.pageWidth = wclsNeed;
                        cv.marginRight = ReportPageRightPt;
                    }
                }
        }
    }
}
