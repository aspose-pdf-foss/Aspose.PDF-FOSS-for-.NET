using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Flow blocks: a table-free fragment parsed straight into blocks.</summary>
    private static void BuildPlainFlowBlocks(FlowBlocksState fbk)
    {
        fbk.list.AddRange(ParseBlocks(fbk.frag, fbk.cv.css, fbk.beforeMarkers, fbk.rowBlocks, fbk.cv.profile.metricFlow,
            fbk.cv.uaFlow || fbk.cv.profile.printGrid, fbk.cv.profile.uaStdSerif || fbk.cv.profile.printGrid,
            FlowRootFontPt(fbk.cv),
            bandDialect: fbk.cv.profile.floatBandDoc, formDialect: fbk.cv.profile.formHorizontalDoc,
            brBlankLines: fbk.cv.profile.formDialectTables || fbk.cv.profile.elementGridDoc || (fbk.cv.profile.uaGridSheet), uaGridBlocks: fbk.cv.profile.uaGridSheet, uaBlockRhythm: fbk.cv.profile.sectionedReport,
            controlBoxes: fbk.cv.profile.escapedAttrDoc, articleRhythm: fbk.cv.articleFlow,
            bodyBoxRhythm: fbk.cv.profile.bodyBoxGridDoc,
            containerBoxIndents: fbk.cv.profile.chartCardDoc, coverStyles: fbk.cv.printCoverDoc,
            inlineBlockCols: fbk.inlineBlockColRules,
                        absSpanLedger: fbk.absSpanLedger,
                        spanClassTypography: fbk.cv.profile.ptReportDoc,
                        fieldsetBoxes: fbk.cv.fieldsetDoc || fbk.cv.uaFieldsetBoxes, uaFieldset: fbk.cv.uaFieldsetBoxes,
                        uaPMargins: fbk.cv.profile.emailNewsletterDoc,
                        msoParagraphs: fbk.cv.profile.msoFilteredDoc,
                        wordMail: fbk.cv.profile.wordMailDoc, wordExport: fbk.cv.profile.wordExportDoc,
                        spanPtTypography: fbk.cv.profile.ptStyledFragment || fbk.cv.profile.redlineDiffDoc || fbk.cv.profile.wordMailDoc
                            || fbk.cv.profile.inlineSpanTypography,
                        dwFlow: fbk.cv.profile.dwFormDoc,
                        floatFlow: fbk.cv.profile.floatBothSidesDoc,
                        inlineEmphasisRuns: fbk.cv.profile.redlineDiffDoc,
                        html5UaHeadings: fbk.cv.html5BareUa || (fbk.cv.html5Doctype && fbk.cv.profile.uaStdSerif),
                        uaClosingGaps: fbk.cv.profile.uaStdSerif && !fbk.cv.profile.sectionedReport && !fbk.cv.articleFlow
                            && !fbk.cv.profile.bodyBoxGridDoc && !fbk.cv.profile.redlineDiffDoc
                            // (the Word-filtered mail keeps its own calibrated paragraph pitch)
                            && !fbk.cv.profile.msoFilteredDoc,
                        divBandBg: fbk.cv.profile.bodyPinnedW > 0,
                        // The chain dialect's sheet sizes and faces the flow's blocks (probed: H1 22px
                        // draws 16.5, H2/H3 14px draw 10.5 where the legacy defaults drew 18/15/13).
                        sheetElementTypography: fbk.cv.profile.sheetTypographyDoc,
                        chainRules: fbk.cv.profile.docChainRules, bodyLineBoxPt: fbk.cv.profile.bodyLineBoxPt,
                        contentWidthPt: fbk.cv.pageWidth - fbk.cv.marginLeft - fbk.cv.marginRight,
                        uaBoxes: fbk.cv.uaFlow && !fbk.cv.profile.printGrid,
                        cv: fbk.cv));
    }
}
