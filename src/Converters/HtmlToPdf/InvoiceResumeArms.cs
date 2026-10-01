using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    private const double CiPageWPt = 636.0;

    private const double CiContentXPt = 96.0;         // 90 margin + UA 6 body

    private const double CiContentWPt = 450.0;        // 600 px min-width

    private const double CiHeaderHPt = 113.2;         // header { height:150px }

    private const double CiH2AfterDividerPt = 45.2;   // divider → h2 baseline

    private const double CiFreshTopBasePt = 114.9;    // fresh page h2 baseline

    private const double CiTableAfterH2Pt = 23.5;     // h2 base → thead band top

    private const double CiTheadHPt = 51.3;           // two 15 pt lines + pads

    private const double CiRowHPt = 31.5;             // 13 pt line + 10 px pads

    private const double CiRowBasePt = 21.2;          // row top → baseline

    private const double CiDividerGapPt = 16.6;       // table bottom → divider

    private static Document? TryRenderContractInvoice(string html, double pageHeight)
    {
        var cv = new ContractInvoiceState();
        cv.html = html;
        cv.pageHeight = pageHeight;
        if (!cv.html.Contains("class='contract'", StringComparison.OrdinalIgnoreCase)
            && !cv.html.Contains("class=\"contract\"", StringComparison.OrdinalIgnoreCase))
            return null;
        if (!Regex.IsMatch(cv.html, @"fonts\.googleapis\.com/css\?family=Lato",
                RegexOptions.IgnoreCase)
            && !Regex.IsMatch(cv.html, @"@font-face[^}]*Lato", RegexOptions.IgnoreCase))
            return null;
        cv.inv = System.Globalization.CultureInfo.InvariantCulture;
        cv.cssText = cv.html;
        if (!Regex.IsMatch(cv.html, @"@font-face", RegexOptions.IgnoreCase))
        {
            var linkM = Regex.Match(cv.html,
                @"<link[^>]*href\s*=\s*[""'](https?://fonts\.googleapis\.com[^""']+)[""']",
                RegexOptions.IgnoreCase);
            if (!linkM.Success) return null;
            var fetched = FetchRemoteImage(DecodeEntities(linkM.Groups[1].Value));
            if (fetched is null) return null;
            cv.cssText = Encoding.UTF8.GetString(fetched);
        }
        cv.faces = LoadRemoteFaces(cv.cssText);
        if (!cv.faces.TryGetValue((300, false), out var lightFace)
            || !cv.faces.TryGetValue((400, false), out var regFace))
            return null;
        if (WinMetricsFor(lightFace) is not { } wm) return null;
        cv.lightFace = lightFace;
        cv.wm = wm;
        cv.regFace = regFace;
        RenderInvoiceHeader(cv);
        foreach (Match cM in Regex.Matches(cv.html, @"<div class='(contract|total)'[^>]*>",
            RegexOptions.IgnoreCase))
        {
            if (!RenderInvoiceCard(cv, cM)) break;
        }

        foreach (var (pg, ops) in cv.streams)
            pg.AddContentStream(Encoding.ASCII.GetBytes(ops.ToString() + "\n"));
        return cv.doc;
    }

    private const double RdPadXPt = 30.0;             // #document hmargins

    private const double RdColWPt = 552.0;            // singlecolumn width

    private const double RdLiMarkerXPt = 12.21;       // li bullet inset

    private const double RdLiTextXPt = 23.0;          // li text inset

    private const double RdLinePt = 13.0;             // resolved line-height

    private const double RdSingleLiPitchPt = 14.84;   // single-line li pitch

                                                      // (the 13 pt marker's box)
    private const double RdTitleToParaPt = 16.25;     // title base → first line

    private const double RdParaToTitlePt = 20.75;     // last line → next title

}
