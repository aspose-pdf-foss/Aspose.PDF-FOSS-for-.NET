using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Render the bilingual RFQ form, or null when the page is not it.</summary>
    private static Document? TryRenderRfqForm(string html, double pageWidth, double pageHeight)
    {
        var rq = new RfqFormState();
        rq.html = html;
        rq.pageWidth = pageWidth;
        rq.pageHeight = pageHeight;
        if (!rq.html.Contains("REQUEST FOR QUOTATION", StringComparison.Ordinal)
            || !rq.html.Contains("#8d98b2", StringComparison.OrdinalIgnoreCase)
            || !rq.html.Contains("#eef7f8", StringComparison.OrdinalIgnoreCase)
            || !Regex.IsMatch(rq.html, "cellpadding=\"7\"[ ]+cellspacing=\"1\"", RegexOptions.IgnoreCase))
            return null;


        rq.tables = Regex.Matches(rq.html, "<table\\b[^>]*>((?:(?!<table)[\\s\\S])*?)</table[ ]*>",
            RegexOptions.IgnoreCase).Cast<Match>().Select(m => m.Groups[1].Value).ToList();
        if (rq.tables.Count < 5) return null;

        rq.logoM = Regex.Match(rq.html, "<img[^>]*alt[ ]*=[ ]*[\"']([^\"']*)", RegexOptions.IgnoreCase);
        rq.titleLabels = RqLabels(rq.tables[0]);           // [Arabic title, English title]
        rq.band1Cells = RqCells(rq.tables[1]);             // REQ NO cell, Date cell
        rq.band2Labels = RqLabels(rq.tables[2]);           // Closing:, ar, Delivery Date:, ar
        rq.introCells = RqCells(rq.tables[3]);             // EN labels, AR labels
        rq.grid = rq.tables[4];
        if (rq.titleLabels.Count < 2 || rq.band1Cells.Count < 2 || rq.band2Labels.Count < 4
            || rq.introCells.Count < 2) return null;

        rq.reqSpans = RqSpans(rq.band1Cells[0].Groups[2].Value);   // [REQ. NO., value, : ar]
        rq.dateSpans = RqSpans(rq.band1Cells[1].Groups[2].Value);  // [Date:, value, ar]
        rq.introEn = RqLabels(rq.introCells[0].Groups[2].Value);
        rq.introAr = RqLabels(rq.introCells[1].Groups[2].Value);
        if (rq.reqSpans.Count < 3 || rq.dateSpans.Count < 3 || rq.introEn.Count < 3 || rq.introAr.Count < 2)
            return null;

        ParseRfqGrid(rq);
        if (rq.headers.Count != 4 || rq.rows.Count == 0) return null;

        rq.doc = new Document();
        rq.page = rq.doc.Pages.Add(rq.pageWidth, rq.pageHeight);
        EnsureFonts(rq.page);
        var fontDict = (rq.page.Dict.Get("Resources") as Core.PdfDictionary)
            ?.Get("Font") as Core.PdfDictionary;
        if (fontDict is null) return null;
        rq.fontDict = fontDict;
        FillRfqBands(rq);

        EmitRfqText(rq);
        rq.page.AddContentStream(Encoding.ASCII.GetBytes(rq.runs.ToString()));
        return rq.doc;
    }
}
