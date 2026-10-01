using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Flow blocks: the flow stretch between tables parsed, its dangling p tags kept inert.</summary>
    private static void BuildNonTableSegment(FlowBlocksState fbk, string seg, List<(bool isTable, string html)> segs, int segIdx)
    {
        var before = fbk.list.Count;
        // UA flow: a table splits its wrapping <p> across segments, so the
        // stretch between two tables reaches here as "</p> <p><br/></p> <p>" -
        // the dangling close/open tags make the p-with-br parse as an inert
        // hard break instead of the full paragraph pitch the plain flow gives
        // it (probed: <p><br/></p> costs one whole paragraph pitch, exactly
        // like a text paragraph). Trim the orphan edges - ONLY on a
        // text-free spacer stretch, so a segment that carries real
        // paragraph text keeps its calibrated continuation parse.
        var uaSpacerSeg = false;
        if (fbk.cv.profile.uaBareDoc
            && HtmlFragment.StripHtmlTags(seg).Trim().Length == 0
            && Regex.IsMatch(seg, @"<p\b[^>]*>\s*<br\b", RegexOptions.IgnoreCase))
        {
            var segTrim = Regex.Replace(seg, @"^\s*</p\s*>", "", RegexOptions.IgnoreCase);
            segTrim = Regex.Replace(segTrim, @"<p\b[^>]*>\s*$", "", RegexOptions.IgnoreCase);
            seg = segTrim;
            uaSpacerSeg = true;
        }
        // A table splits its enclosing containers across segments: the stretch AFTER one still
        // sits inside the div/font/span boxes that were open before it and keeps their
        // typography (measured: the dunning letter's closing notice stays 10 pt past its grid).
        if (fbk.cv.profile.uaStdSerif && segIdx > 0)
            seg = ReopenSplitContainers(segs, segIdx, fbk.cv.profile.fieldsClass) + seg;
        fbk.list.AddRange(ParseBlocks(seg, fbk.cv.css, fbk.beforeMarkers, fbk.rowBlocks, fbk.cv.profile.metricFlow,
            fbk.cv.uaFlow || fbk.cv.profile.printGrid, fbk.cv.profile.uaStdSerif || fbk.cv.profile.printGrid,
            FlowRootFontPt(fbk.cv),
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
            divBandBg: fbk.cv.profile.bodyPinnedW > 0,
            sheetElementTypography: fbk.cv.profile.sheetTypographyDoc,
                        chainRules: fbk.cv.profile.docChainRules, bodyLineBoxPt: fbk.cv.profile.bodyLineBoxPt,
                        contentWidthPt: fbk.cv.pageWidth - fbk.cv.marginLeft - fbk.cv.marginRight,
                        uaBoxes: fbk.cv.uaFlow && !fbk.cv.profile.printGrid));
        // A stretch between tables that is nothing but <br>s carries no text,
        // so the block parser yields nothing for it — yet each of those
        // breaks is a line box the next table starts below. The element-grid
        // dialect's body rule names a face but no size: its breaks ride the
        // UA base size in that face.
        if (uaSpacerSeg)
            for (var sb2 = before; sb2 < fbk.list.Count; sb2++)
                if (fbk.list[sb2].IsHardBreak) fbk.list[sb2].UaSpacerPara = true;
        // A text-carrying segment that FOLLOWS a table still opens
        // with the table's break tail (`</table><br/></p><p><br/></p>
        // <p>text`): its LEADING real line breaks are the same spacer
        // paragraphs (empty-<p> breaks are not - they cost nothing).
        // …but only where those breaks REALLY are that tail, which the shape says: the
        // breaks sit between PARAGRAPH boundaries. A leading break that is plain inline flow
        // (`</table><br><span><br>text`) is an ordinary line box and costs one line, not a
        // paragraph — marking it too put a whole extra empty line under every such table.
        if (fbk.cv.profile.uaBareDoc && segIdx > 0 && segs[segIdx - 1].isTable
            && Regex.IsMatch(seg, @"^\s*(?:<br\b[^>]*>\s*)*</?p\b", RegexOptions.IgnoreCase))
            for (var sb3 = before; sb3 < fbk.list.Count; sb3++)
            {
                if (!fbk.list[sb3].IsHardBreak) break;
                if (fbk.list[sb3].IsLineBreak) fbk.list[sb3].UaSpacerPara = true;
            }
        if (fbk.list.Count == before && (fbk.cv.bodyCssFace ?? fbk.elementGridFace
                // (a UA-grid document's bare breaks between its tables are line boxes of the UA base font)
                ?? (fbk.cv.profile.uaBareDoc || (fbk.cv.profile.uaGridSheet) ? "Times New Roman" : null)) is { } segBrFace
            && WinMetricsFor(segBrFace) is { } segBr)
            foreach (Match _ in Regex.Matches(seg, @"<br\b[^>]*>", RegexOptions.IgnoreCase))
                fbk.list.Add(new Block
                {
                    Text = "", IsHardBreak = true, IsLineBreak = true,
                    ExplicitHeight = MetricLineHeight(
                        fbk.cv.profile.bodyCssFontPt > 0 ? fbk.cv.profile.bodyCssFontPt : DefaultBodyFontPt, segBr.sum),
                });
    }

    /// <summary>The inline containers still open where the flow segments before
    /// <paramref name="segIdx"/> end, as their opening tags outermost-first: the segment after a
    /// table re-enters the boxes the table split it out of. A paragraph is never re-opened - an
    /// HTML parser closes one before a table.</summary>
    private static string ReopenSplitContainers(List<(bool isTable, string html)> segs, int segIdx, string? fieldsClass = null)
    {
        var open = new List<(string tag, string text)>();
        foreach (var (isTable, html) in segs.Take(segIdx))
        {
            if (isTable) continue;
            foreach (Match m in SplitContainerTagRx.Matches(html))
            {
                var tag = m.Groups["tag"].Value.ToLowerInvariant();
                if (m.Groups["close"].Length > 0)
                {
                    for (var i = open.Count - 1; i >= 0; i--)
                        if (open[i].tag == tag) { open.RemoveRange(i, open.Count - i); break; }
                }
                else if (!m.Groups["attrs"].Value.TrimEnd().EndsWith("/", StringComparison.Ordinal))
                {
                    open.Add((tag, m.Value));
                }
            }
        }
        // Only a container that STYLES TYPOGRAPHY is worth re-opening: an unstyled wrapper
        // would add a block box the flow never had, and the tail draws the same without it.
        // (…and the field-list dialect's fields box: its inset and its rows' label columns reach the
        //  stretch after a grid only through it)
        var sb = new StringBuilder();
        foreach (var (_, text) in open)
            if (ReopenTypographyRx.IsMatch(text)
                || (fieldsClass is not null && Regex.IsMatch(text, @"class\s*=\s*[""']?(?:[^""'>]*\s)?" + Regex.Escape(fieldsClass) + @"(?:[\s""'>]|$)", RegexOptions.IgnoreCase)))
                sb.Append(text);
        return sb.ToString();
    }

    /// <summary>The typography a re-opened container must carry to be worth re-opening.</summary>
    private static readonly Regex ReopenTypographyRx = new Regex(
        @"(?<![-\w])(font-size|font-family|font-weight)\s*:", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The inline container tags a table can split: they style what follows them.</summary>
    private static readonly Regex SplitContainerTagRx = new Regex(
        @"<(?<close>/?)(?<tag>div|span|font|center|b|i|u|strong|em)\b(?<attrs>[^>]*)>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
}
