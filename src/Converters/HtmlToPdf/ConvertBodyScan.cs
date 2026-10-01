using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The body-scan stage of an HTML conversion: the body rules, the dialect flags and the font-family verdicts, verbatim. A non-null result is a finished document.</summary>
    private static Document? ConvertBodyScan(HtmlLoadOptions? options, ConvertState cv)
    {
        cv.profile.printGrid = !cv.marginsExplicit
            && cv.css.ContainsKey(".col-xs-6")
            && cv.css.TryGetValue("*", out var uniRule)
            && uniRule.TryGetValue("color", out var uniColor)
            && uniColor.Contains("#000") && uniColor.Contains("!important");
        cv.profile.printGridBase = 0;
        cv.printGridLineFactor = 1.15;
        ApplyPrintGridBase(cv.css, cv.profile, cv);

        // Styled-class data-font flow (gated): a stylesheet that embeds its faces as
        // data: URIs and styles a flat classed-paragraph body (the EDGAR TSR report
        // shape) renders through the styled HTML engine. Default page
        // setup only — explicit PageInfo/margins keep the legacy flow.
        if (RenderStyledOrEdgarDocument(cv, options) is { } renderStyledOrEdgarDocumentResult) return renderStyledOrEdgarDocumentResult;
        // body{margin:0}: the default 90pt side margins / 72pt content top
        // apply verbatim — the usual defaults (96/89) bake in the browser's 8px body
        // margin and the default first-baseline drop, which this page has switched off.
        ResolveBodyMargin(cv, options);
        ResolveBodyWidthAndMetricFace(cv, options);
        ResolveMetricFlowAndUaMshtml(cv, options);
        ScanStylesheetForLayoutFreedom(cv, options);
        // Unresolved external stylesheets (InlineLinkedStylesheets leaves the <link>
        // tags of stylesheets it could not fetch): the converter falls back to
        // pure UA defaults for such documents — tables included, they lay out through
        // the metric table renderer. Only ABSOLUTE http(s) links qualify: those are
        // unreachable at render time by design, whereas an unresolved RELATIVE
        // link is a packaging gap — the sheet was present when the document
        // was authored, so the document must keep the legacy calibrated flow.
        DetectDeadCssAndRealFamily(cv, options);
        ResolveUaBodyFace(cv, options);
        ResolveLayoutFreeAndFieldsetFlow(cv, options);
        // Word-filtered TEXT pages (meta Generator "Microsoft Word N (filtered)",
        // no tables — the tabled forms take the MsoForm dialect): their styling is
        // all inline (pt sizes, % line-heights, span faces), which the UA flow
        // renders directly, so the inline-face disqualifier below does not apply.
        DetectDocumentDialects(cv, options);
        ClassifyUaNoFontDocument(cv, options);
        return null;
    }
}
