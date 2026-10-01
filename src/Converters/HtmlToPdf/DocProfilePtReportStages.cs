using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The pt-report family and its inline-styled newsletter arm: a body sized in points with a named face, and the UA cells the two share.</summary>
    private static void DetectPtReportAndNewsletterFlow(ConvertState cv, HtmlDocProfile profile, Dictionary<string, Dictionary<string, string>> css, string html, bool marginsExplicit)
    {
        profile.ptReportDoc = false;
        // the pt-report family's NEWSLETTER arm (inline-body-styled email):
        // in-cell paragraph segments, UA p margins and the quirks body margin
        // are ITS dialect — the NHS/boleto report greens keep the whole-cell model.
        profile.emailNewsletterDoc = false;
        profile.ptTableFontPt = 0.0;
        if (!profile.metricFlow && !marginsExplicit
            && css.TryGetValue("body", out var ptBody)
            && ptBody.TryGetValue("font-family", out var ptFam0)
            && FirstFontFamily(ptFam0) is { } ptFam && WinMetricsFor(ptFam) is not null
            && ptBody.TryGetValue("font-size", out var ptFs0)
            && Regex.IsMatch(ptFs0.Trim(), @"^[\d.]+\s*pt$", RegexOptions.IgnoreCase)
            && ((css.TryGetValue("table", out var ptTbl) && ptTbl.ContainsKey("font-family"))
                // …or the FORM shape: the cells' class rules carry the body's face and the grid
                // seats text inputs in its cells (probed on the helpdesk request form: Tahoma
                // 8 pt labels beside size=30 inputs, six 98 % tables gridded in that face).
                || SheetCellClassCarriesBodyFace(css, html, ptFam))
            && Regex.Matches(html, @"<table\b", RegexOptions.IgnoreCase).Count >= 5)
        {
            profile.ptReportDoc = true;
            profile.ptFormDoc = !(css.TryGetValue("table", out var ptTblRule) && ptTblRule.ContainsKey("font-family"));
            profile.metricFlow = true;
            profile.metricFace = ptFam;
            profile.metricLineSum = HheaLineSumFor(ptFam) ?? 0;
            // The metric report opens at the raw 72 pt content top (the legacy
            // calibrated 89 belongs to the flow this document left).
            cv.marginTop = 72.0;
            // The body rule authors MARGIN-TOP: 0cm — content opens at the page
            // margin with no UA body inset. (TryParseLength rejects an explicit
            // zero by design, so the zero idiom is matched first.)
            cv.bodyMarT = ptBody.TryGetValue("margin-top", out var ptMt)
                ? Regex.IsMatch(ptMt.Trim(), @"^0(\.0+)?\s*(cm|mm|px|pt|em|in)?$")
                    ? 0.0
                    : TryParseLength(ptMt.Trim()) is { } ptMtPt ? ptMtPt : 6.0
                : 6.0;
            profile.formBodyFontPt = double.Parse(Regex.Match(ptFs0, @"[\d.]+").Value,
                System.Globalization.CultureInfo.InvariantCulture);
            if (css.TryGetValue("table", out var ptTblFs) && ptTblFs.TryGetValue("font-size", out var ptTfs)
                && TryParseCssFontSize(ptTfs.Trim()) is { } ptTfsPt)
                profile.ptTableFontPt = ptTfsPt;
        }
        // …or the same declaration INLINE on the body tag: the NEWSLETTER shape
        // (an Arial px email with zero body margins whose whole layout is
        // table-built) renders through the same metric route.
        if (!profile.ptReportDoc && !profile.metricFlow && !marginsExplicit
            && Regex.Match(html, @"<body\b[^>]*style\s*=\s*[""']([^""']*)[""']",
                RegexOptions.IgnoreCase) is { Success: true } ebM
            && Regex.Match(ebM.Groups[1].Value, @"font-family\s*:\s*([^;]+)",
                RegexOptions.IgnoreCase) is { Success: true } ebFam0
            && FirstFontFamily(ebFam0.Groups[1].Value) is { } ebFam
            && WinMetricsFor(ebFam) is not null
            && Regex.Match(ebM.Groups[1].Value, @"font-size\s*:\s*([\d.]+)\s*px",
                RegexOptions.IgnoreCase) is { Success: true } ebFs
            && Regex.Matches(html, @"<table\b", RegexOptions.IgnoreCase).Count >= 5)
        {
            profile.ptReportDoc = true;
            profile.emailNewsletterDoc = true;
            profile.metricFlow = true;
            profile.metricFace = ebFam;
            profile.metricLineSum = HheaLineSumFor(ebFam) ?? 0;
            // page margin + the quirks body's default 8px margin (the inline
            // style declares no margins of its own)
            cv.marginTop = 72.0 + UaBodyMarginPt;
            profile.formBodyFontPt = double.Parse(ebFs.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture) * 0.75;
        }

        // UA cells with BLOCK children of their own typography or box (the invoice email's `<p
        // style="font-size:12px">`, headings inside cells, the decision list's `<p style="margin-left:
        // 0.5in">` ladder): the paragraph-segment model draws each block at its size and indent, its
        // lines bold where their ink is, a right float on the right, its UA block margins collapsed
        // (measured on the invoice email: 9 pt paragraphs under a 12 pt heading in a 12 pt grid;
        // probed on the decision list: 13.44 + 13.5 + 13.44 between margin-less paragraphs that hold
        // a `<p><br></p>` between them).
        profile.uaBlockCells = profile.uaStdSerif && !profile.ptReportDoc
            // (the second shape: a BARE paragraph opening with a line break and a sized span - the
            // label idiom `<span id><P><BR><SPAN style="FONT-SIZE: 10pt">` - not a styled paragraph
            // whose span sizes it, which the calibrated whole-cell model still draws)
            && (Regex.IsMatch(html, @"<t[dh]\b[^>]*>(?:(?!</t[dh]\b).)*?(?:<(?:p|h[1-6])\b[^>]*font-size|<p>\s*<br\s*/?>\s*<span\b[^>]*font-size)",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline)
                // (the margin-left paragraph must be the cell's DIRECT child - only text, paragraphs
                // and breaks before it; a span-wrapped one keeps the calibrated whole-cell model)
                || Regex.IsMatch(html, @"<t[dh]\b[^>]*>(?:[^<]|<p\b[^>]*>|</p\s*>|<br\s*/?>)*<p\b[^>]*margin-left",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline));
    }
}
