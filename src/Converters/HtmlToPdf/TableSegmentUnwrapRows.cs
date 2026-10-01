using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Each row of the single-column wrapper flows its cell content as ordinary blocks, inset by the cell chrome and padded down by it on the first row.</summary>
    private static void UnwrapWrapperTableRows(FlowBlocksState fbk, string seg, double wrapBandW)
    {
        var fitBandW = wrapBandW <= 0 && fbk.cv.profile.rtlDoc ? ShrinkToFitWrapperWidth(fbk, seg) : 0.0;
        var firstUnwrapRow = true;
        // Two wrapper tables in a row: the first closes with its own cell chrome
        // (padding + border-spacing) before the second opens with its own, so
        // their lines stand a double chrome apart (probed on the RTL letter:
        // 4.5 between the last line of one and the leading line of the next).
        var stacked = fbk.lastUnwrapEnd == fbk.list.Count && fbk.list.Count > 0;
        foreach (Match trM in Regex.Matches(seg, @"<tr\b[^>]*>([\s\S]*?)</tr\s*>",
                     RegexOptions.IgnoreCase))
        {
            var tdM = Regex.Match(trM.Groups[1].Value,
                @"<td\b([^>]*)>([\s\S]*?)</td\s*>", RegexOptions.IgnoreCase);
            if (!tdM.Success) continue;
            var cellCentered = Regex.IsMatch(tdM.Groups[1].Value,
                @"\balign\s*=\s*[""']?center", RegexOptions.IgnoreCase)
                // …or the row's / cell's own text-align (the RTL letter's subject row)
                || (fbk.cv.profile.rtlDoc && Regex.IsMatch(
                    Regex.Match(trM.Value, @"^<tr\b[^>]*>", RegexOptions.IgnoreCase).Value + tdM.Groups[1].Value,
                    @"text-align\s*:\s*center", RegexOptions.IgnoreCase));
            // the cell's own inline style (its font-size) styles the unwrapped
            // blocks as the cell styled its text
            var tdStyle = Regex.Match(tdM.Groups[1].Value,
                @"\bstyle\s*=\s*(?:""([^""]*)""|'([^']*)')", RegexOptions.IgnoreCase);
            var cellOpen = tdStyle.Success
                ? "<div style=\"" + (tdStyle.Groups[1].Success ? tdStyle.Groups[1].Value : tdStyle.Groups[2].Value) + "\">"
                : "<div>";
            if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_BLOCKS") == "1")
                Console.WriteLine($"[unwrap] rtl={fbk.cv.profile.rtlDoc} stacked={stacked} wrapBandW={wrapBandW:0.##} fitBandW={fitBandW:0.##} centered={cellCentered} face='{fbk.cv.profile.metricFace}'");
            var beforeUnwrap = fbk.list.Count;
            fbk.list.AddRange(ParseBlocks(cellOpen + tdM.Groups[2].Value + "</div>",
                fbk.cv.css, fbk.beforeMarkers, fbk.rowBlocks, fbk.cv.profile.metricFlow,
                fbk.cv.uaFlow || fbk.cv.profile.printGrid, fbk.cv.profile.uaStdSerif || fbk.cv.profile.printGrid,
                FlowRootFontPt(fbk.cv, sheetBodyRule: false),
                bandDialect: fbk.cv.profile.floatBandDoc, formDialect: fbk.cv.profile.formHorizontalDoc,
                brBlankLines: fbk.cv.profile.formDialectTables || fbk.cv.profile.elementGridDoc || (fbk.cv.profile.uaGridSheet), uaGridBlocks: fbk.cv.profile.uaGridSheet, uaBlockRhythm: fbk.cv.profile.sectionedReport, html5UaHeadings: fbk.cv.html5BareUa || (fbk.cv.html5Doctype && fbk.cv.profile.uaStdSerif),
                controlBoxes: fbk.cv.profile.escapedAttrDoc, articleRhythm: fbk.cv.articleFlow,
                bodyBoxRhythm: fbk.cv.profile.bodyBoxGridDoc,
                containerBoxIndents: fbk.cv.profile.chartCardDoc, coverStyles: fbk.cv.printCoverDoc,
                inlineBlockCols: fbk.inlineBlockColRules,
                cv: fbk.cv,
                absSpanLedger: fbk.absSpanLedger,
                spanClassTypography: fbk.cv.profile.ptReportDoc,
                fieldsetBoxes: fbk.cv.fieldsetDoc || fbk.cv.uaFieldsetBoxes, uaFieldset: fbk.cv.uaFieldsetBoxes,
                sheetElementTypography: fbk.cv.profile.sheetTypographyDoc,
                uaPMargins: fbk.cv.profile.emailNewsletterDoc,
                msoParagraphs: fbk.cv.profile.msoFilteredDoc,
                wordMail: fbk.cv.profile.wordMailDoc, wordExport: fbk.cv.profile.wordExportDoc,
                // (the UA closing gaps of the whole-document parse - a list's or heading's box margin
                // before a margin-less follower - reach the segments of a BARE UA document that carries
                // tables; a styled document keeps the calibrated segments its greens were measured on)
                uaClosingGaps: fbk.cv.profile.uaBareDoc && !fbk.cv.profile.sectionedReport && !fbk.cv.articleFlow
                    && !fbk.cv.profile.bodyBoxGridDoc && !fbk.cv.profile.redlineDiffDoc && !fbk.cv.profile.msoFilteredDoc,
                spanPtTypography: fbk.cv.profile.ptStyledFragment || fbk.cv.profile.redlineDiffDoc || fbk.cv.profile.wordMailDoc,
                dwFlow: fbk.cv.profile.dwFormDoc,
                floatFlow: fbk.cv.profile.floatBothSidesDoc,
                inlineEmphasisRuns: fbk.cv.profile.redlineDiffDoc,
                divBandBg: fbk.cv.profile.bodyPinnedW > 0));
            if (fbk.list.Count == beforeUnwrap) continue;
            for (var ub = beforeUnwrap; ub < fbk.list.Count; ub++)
            {
                fbk.list[ub].LeftIndent += UaCellChromePt;
                if (cellCentered && wrapBandW > 0)
                    fbk.list[ub].CenterBandW = wrapBandW;
                else if (cellCentered && fitBandW > 0)
                    fbk.list[ub].CenterBandW = fitBandW;
                // The table's attribute width is the cell's WRAP box
                // too (the letter's body wraps at 98.25 + ~333, not
                // the page content width).
                if (wrapBandW > 0 && fbk.list[ub].MaxWidthPt <= 0)
                    fbk.list[ub].MaxWidthPt = wrapBandW;
            }
            // The row boundary replaces the leading blocks' own top
            // margins up to and including the first CONTENT block
            // (probed: the letter's rows pitch 16.5 = one 13.5 line
            // + this chrome, single-p cells included; an in-cell
            // second paragraph keeps its pairwise margin).
            for (var ub = beforeUnwrap; ub < fbk.list.Count; ub++)
            {
                fbk.list[ub].MarginTop = 0;
                if (!string.IsNullOrWhiteSpace(fbk.list[ub].Text)) break;
            }
            // …and the closing content block's bottom margin the same
            // way (a lone in-cell paragraph carries neither margin —
            // the row chrome is the whole boundary).
            for (var ub = fbk.list.Count - 1; ub >= beforeUnwrap; ub--)
            {
                fbk.list[ub].MarginBottom = 0;
                if (!string.IsNullOrWhiteSpace(fbk.list[ub].Text)) break;
            }
            fbk.list[beforeUnwrap].PadTop +=
                firstUnwrapRow ? (stacked ? 2 * UaCellChromePt : UaCellChromePt) : UaWrapperRowChromePt;
            firstUnwrapRow = false;
        }
    }
}
