using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The first half of the styled dialect ladder - each arm renders its own document shape, and null hands the rest of the ladder back to the caller.</summary>
    private static Document? RenderStyledDialectsBHead(ConvertState cv, HtmlLoadOptions? options)
    {
        // Pre-wrap label documents (see PreWrapLabel.cs): a rem-scaled sheet whose
        // single div>label body declares `white-space: pre-wrap` — the label lays
        // out PREFORMATTED on the measured line ladder.
        if (!cv.marginsExplicit
            && TryRenderPreWrapLabel(cv.html, cv.css, cv.pageWidth, cv.pageHeight) is { } pwDoc)
            return pwDoc;

        // Styled XML-dump viewer sheets (see MetricsCard.cs): nested per-element
        // divs under one root class whose `.root *` rule blocks-and-pads every
        // descendant at the root's keyword font size — the em padding/margin
        // chain positions every line.
        if (!cv.marginsExplicit
            && TryRenderXmlViewer(cv.html, cv.pageWidth, cv.pageHeight) is { } xvDoc)
            return xvDoc;

        // Print-media job ads (see MetricsCard.cs): a zero-margin conversion of
        // an @media-print document whose container class sizes the sheet.
        if (cv.marginsExplicit
            && TryRenderPrintAd(cv.html, cv.css, cv.pageHeight, options) is { } paDoc)
            return paDoc;

        // Angular audit-report exports (see MetricsCard.cs): the finding sheet
        // under its authored margins, on the kept A4.
        if (cv.marginsExplicit
            && TryRenderAuditReport(cv.html, cv.pageWidth, cv.pageHeight, options) is { } arDoc)
            return arDoc;

        // Resume-builder document sheets (see MetricsCard.cs): the div#document
        // export whose dynamic stylesheet resolves the whole layout.
        if (cv.marginsExplicit
            && TryRenderResumeDoc(cv.html, cv.pageWidth, cv.pageHeight, options) is { } rdDoc)
            return rdDoc;

        // Contract-invoice sheets on remote faces (see MetricsCard.cs): the
        // Google-Fonts Lato invoice with its fetched font programs.
        if (!cv.marginsExplicit
            && TryRenderContractInvoice(cv.html, cv.pageHeight) is { } ciDoc)
            return ciDoc;

        // Decision-notification letters (see MetricsCard.cs): the all-inline
        // TCI template — basis container, float header, boxSection panels.
        if (!cv.marginsExplicit
            && TryRenderDecisionLetter(cv.html, cv.pageHeight) is { } dnDoc)
            return dnDoc;

        // Word-filtered FORM-GRID pages (see MsoForm.cs): one MsoNormalTable
        // whose columns solve to a landscape-wide single page.
        if (!cv.marginsExplicit
            && TryRenderMsoWordForm(cv.html) is { } msoFormDoc)
            return msoFormDoc;

        if (!cv.marginsExplicit
            && TryRenderPositionedForm(cv.html, options, cv.pageHeight) is { } pfDoc)
            return pfDoc;
        return null;
    }
}
