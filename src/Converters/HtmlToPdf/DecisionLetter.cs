using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    private static Document? TryRenderDecisionLetter(string html, double pageHeight)
    {
        if (!html.Contains("class=\"basis\"", StringComparison.OrdinalIgnoreCase)
            || !html.Contains("boxSection", StringComparison.OrdinalIgnoreCase)
            || !html.Contains("information-table", StringComparison.OrdinalIgnoreCase)
            || !html.Contains("box-header-title", StringComparison.OrdinalIgnoreCase))
            return null;
        var dl = new DecisionLetterState();
        dl.pageHeight = pageHeight;
        if (!TryParseLetterHead(dl, html)) return null;

        dl.doc = Document.Create();
        dl.page = dl.doc.Pages.Add(dl.pageWidth, dl.pageHeight);
        dl.docFontDict = new Core.PdfDictionary();
        EnsureFonts(dl.page, dl.docFontDict);
        dl.sb = new StringBuilder();
        dl.pageStreams = new List<(Page pg, StringBuilder ops)> { (dl.page, dl.sb) };

        dl.iconRef = (Core.PdfIndirectRef?)null;
        DrawLetterHeader(dl);

        if (!TryParseInfoTables(dl)) return null;
        DrawInfoBands(dl);

        dl.sectionTop0 = dl.infoBottom + 1.2;
        DrawColumnTable(dl);
        DrawDealerTable(dl);

        dl.rightBottom = dl.midTop;
        {
            DrawSidePanels(dl);
        }

        dl.discTop = Math.Max(dl.leftBottom, dl.rightBottom) + 11.25;
        {
            DrawDisclaimer(dl);
        }

        // ── footer paragraph + the contact table (30/20/20/30 % columns,
        //    vertically-centred rows — the table opens the SECOND page) ──
        {
            DrawFooterAndContacts(dl);
        }

        foreach (var (pg, ops) in dl.pageStreams)
            pg.AddContentStream(Encoding.ASCII.GetBytes(ops.ToString() + "\n"));
        return dl.doc;
    }
}
