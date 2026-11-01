using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    private static Document? TryRenderResumeDoc(string html,
        double pageWidth, double pageHeight, HtmlLoadOptions? options)
    {
        if (!html.Contains("id=\"document\"", StringComparison.Ordinal)
            || !html.Contains("class=\"fontsize fontface hmargins", StringComparison.Ordinal)
            || !html.Contains("sectiontitle", StringComparison.Ordinal))
            return null;
        var rd = new ResumeDocState();
        rd.pageWidth = pageWidth;
        rd.pageHeight = pageHeight;
        rd.face = "Palatino Linotype";
        if (PosFace(rd.face).ttf is null || WinMetricsFor(rd.face) is not { } wm) return null;
        rd.wm = wm;
        rd.inv = System.Globalization.CultureInfo.InvariantCulture;
        rd.c = SafeRegex.Replace(html, @"\s+", " ");
        rd.marginTop = options?.PageInfo?.Margin?.Top ?? 13.0;
        rd.marginBottom = options?.PageInfo?.Margin?.Bottom ?? 13.0;

        rd.doc = Document.Create();
        rd.docFontDict = new Core.PdfDictionary();
        rd.page = rd.doc.Pages.Add(rd.pageWidth, rd.pageHeight);
        EnsureFonts(rd.page, rd.docFontDict);

        rd.frags = new List<(int grp, double x, double y, double fs, bool bold, Color col, string t)>();
        rd.black = Color.FromArgb(0, 0, 0);
        rd.y = rd.marginTop;
        rd.runRx = new Regex(@"<(/?)(\w[\w-]*)((?:[^>""']|""[^""]*""|'[^']*')*?)(/?)>|([^<]+)",
            RegexOptions.Singleline, RegexLimits.Timeout);
        rd.seq = 10;
        rd.firstSection = true;
#pragma warning disable S3267 // a step loop: each call does the work and returns false to stop - as a LINQ predicate the side effects would hide in a query
        foreach (Match secM in SafeRegex.Matches(rd.c,
#pragma warning restore S3267
            @"<div id=""SECTION_(\w{4})\d+"" class=""[^""]*""[^>]*>", RegexOptions.IgnoreCase))
        {
            if (!RenderResumeSection(rd, secM)) break;
        }
        FlushPage(rd);
        return rd.doc;
    }
}
