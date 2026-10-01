using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    // ── The centred-wrapper POSITIONED FORM ─────────────────────────────────────
    // A form-builder export: an auto-centred px-width white wrapper, a padded
    // relative canvas, and absolutely-positioned "field" boxes (style border,
    // fill, 5px padding) holding an inline-block question div beside a response
    // (underline divs, radio/checkbox tables, textarea rules). The engine sizes
    // the page 96 + wrapper + 90 wide, keeps the caller's page height, IGNORES
    // the sheet's page-break <br>s (measured: section 2 flows on and its box
    // splits at the content bottom), and draws Arial throughout.
    //
    // All geometry measured: content x = 96 + padding; the first section
    // canvas opens 19.05 below the h1 baseline; question text seats
    // halfLeading + ascent below its content top (13px Arial in a 16px line);
    // an inline-block neighbour follows one space advance after the question.

    private const double PfSecAfterH1Pt = 19.05;      // h1 baseline -> section canvas top
    private const double PfSecGapPt = 12.8;           // section border bottom -> next border top
    private const double PfFieldPadPt = 3.75;         // the 5px field content padding
    private const double PfH1BaselinePt = 124.8;      // measured: content top 78 + pad + h1 seat

    private static Document? TryRenderPositionedForm(string html,
        HtmlLoadOptions? options, double pageHeight)
    {
        var pf = new PositionedFormState();
        pf.html = html;
        pf.options = options;
        pf.pageHeight = pageHeight;
        pf.wrapM = Regex.Match(pf.html,
            @"<div\b[^>]*style\s*=\s*(['""])[^'""]*margin:\s*0\s+auto;[^'""]*width:\s*(\d+(?:\.\d+)?)px[^'""]*\1",
            RegexOptions.IgnoreCase);
        if (!pf.wrapM.Success) return null;
        if (Regex.Matches(pf.html, @"position\s*:\s*absolute\s*;\s*left", RegexOptions.IgnoreCase).Count < 3)
            return null;
        if (!pf.html.Contains("-bounding", StringComparison.OrdinalIgnoreCase)) return null;

        pf.wrapPx = double.Parse(pf.wrapM.Groups[2].Value,
            System.Globalization.CultureInfo.InvariantCulture);
        pf.pageWidth = 96.0 + pf.wrapPx * PxPt + 90.0;
        pf.doc = Document.Create();
        pf.docFontDict = new Core.PdfDictionary();
        pf.page = pf.doc.Pages.Add(pf.pageWidth, pf.pageHeight);
        EnsureFonts(pf.page, pf.docFontDict);
        EnsureFont(pf.page, "Arial", "F8");
        EnsureFont(pf.page, "ArialBold", "F9");

        pf.contentBottom = pf.pageHeight - 72.0;
        pf.wrapX = 96.0;
        pf.wrapW = pf.wrapPx * PxPt;
        pf.arial = WinMetricsFor("Arial") ?? (0.905, 1.117);
        pf.fs = 9.75;
        pf.lineBox = 12.0;
        pf.drop = (pf.lineBox - pf.fs * pf.arial.sum) / 2 + pf.fs * pf.arial.asc;
        pf.spaceAdv = MeasureFaceText("Arial", " ", pf.fs);

        pf.inv = System.Globalization.CultureInfo.InvariantCulture;
        pf.sb1 = new StringBuilder();
        pf.sb2 = new StringBuilder();
        pf.contentX = pf.wrapX + 15.0;
        pf.h1M = Regex.Match(pf.html, @"<h1\b[^>]*>([\s\S]*?)</h1>", RegexOptions.IgnoreCase);
        if (pf.h1M.Success)
            PfText(pf, pf.contentX, PfH1BaselinePt,
                CollapseWs(DecodeEntities(Regex.Replace(pf.h1M.Groups[1].Value, "<[^>]+>", " "))),
                bold: true, size: 19.5);

        pf.secY = PfH1BaselinePt + PfSecAfterH1Pt;
        pf.secX = pf.wrapX + 15.0 + 0.75;
        pf.lastBottom = pf.secY;
        foreach (Match secM in Regex.Matches(pf.html,
            @"<div\b[^>]*style\s*=\s*(['""])[^'""]*position:\s*relative;[^'""]*height:\s*(\d+(?:\.\d+)?)px[^'""]*\1\s*>",
            RegexOptions.IgnoreCase))
        {
            if (!RenderFormSection(pf, secM)) break;
        }

        pf.h4M = Regex.Match(pf.html, @"<h4\b[^>]*>([\s\S]*?)</h4>", RegexOptions.IgnoreCase);
        if (pf.h4M.Success)
            PfText(pf, pf.contentX, pf.contentBottom + (251.2 - 72.0),
                CollapseWs(DecodeEntities(Regex.Replace(pf.h4M.Groups[1].Value, "<[^>]+>", " "))),
                bold: true);

        pf.p1Band = Compat.Format(pf.inv,
            $"q 1 1 1 rg {pf.wrapX:F2} {pf.pageHeight - pf.contentBottom:F2} {pf.wrapW:F2} {pf.contentBottom - 78.0:F2} re f Q\n");
        pf.page.AddContentStream(Encoding.ASCII.GetBytes(pf.p1Band + pf.sb1));
        if (pf.sb2.Length > 0)
        {
            var page2 = pf.doc.Pages.Add(pf.pageWidth, pf.pageHeight);
            EnsureFonts(page2, pf.docFontDict);
            EnsureFont(page2, "Arial", "F8");
            EnsureFont(page2, "ArialBold", "F9");
            var p2Band = Compat.Format(pf.inv,
                $"q 1 1 1 rg {pf.wrapX:F2} {pf.pageHeight - 280.0:F2} {pf.wrapW:F2} {280.0 - 72.0:F2} re f Q\n");
            page2.AddContentStream(Encoding.ASCII.GetBytes(p2Band + pf.sb2));
        }
        return pf.doc;
    }

    /// <summary>The inner HTML of the div OPENING at <paramref name="afterOpen"/>
    /// (the index just past its open tag) — up to its matching close.</summary>
    private static string DivBodyAt(string html, int afterOpen)
    {
        var depth = 1;
        foreach (Match t in Regex.Matches(html[afterOpen..], @"<(/?)div\b[^>]*>", RegexOptions.IgnoreCase))
        {
            if (t.Groups[1].Value.Length == 0) depth++;
            else if (--depth == 0) return html[afterOpen..(afterOpen + t.Index)];
        }
        return html[afterOpen..];
    }
}
