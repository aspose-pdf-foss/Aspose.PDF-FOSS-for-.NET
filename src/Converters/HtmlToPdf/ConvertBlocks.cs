using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The block builders, lifted out of ConvertFromHtml: each takes the
// conversion state and the pre-scan flags it reads. Bodies are verbatim.
    private static         double PxOf(string tag, string prop)
            {
                // CSS style wins; a bare width/height attribute is the fallback.
                var m = Regex.Match(tag, prop + @"\s*:\s*([\d.]+)px", RegexOptions.IgnoreCase);
                if (!m.Success)
                    m = Regex.Match(tag, "\\b" + prop + @"\s*=\s*[""']?([\d.]+)", RegexOptions.IgnoreCase);
                return m.Success && double.TryParse(m.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
            }

    private static bool HasVisibleFormControl(string markup)
    {
        foreach (Match fim in Regex.Matches(markup, @"<\s*(input|select|textarea)\b[^>]*>",
                     RegexOptions.IgnoreCase))
            if (!HiddenInlineRx.IsMatch(fim.Value)) return true;
        return false;
    }

    private static bool IsSingleColumnWrapperTable(bool sheetChromesCells, string seg)
    {
        // Cells the SHEET borders or fills are a visible grid, not layout chrome.
        if (sheetChromesCells) return false;
        var open = Regex.Match(seg, @"<table\b[^>]*>", RegexOptions.IgnoreCase);
        if (!open.Success) return false;
        if (Regex.IsMatch(open.Value, @"\bborder\s*=\s*['""]?[1-9]", RegexOptions.IgnoreCase)
            || Regex.IsMatch(open.Value, @"\b(cellspacing|cellpadding)\s*=", RegexOptions.IgnoreCase))
            return false;
        if (Regex.IsMatch(seg, @"\bbgcolor\s*=|background(-color)?\s*:|border\s*:\s*(?!none|0)",
                RegexOptions.IgnoreCase))
            return false;
        // A class on the structural tags can carry stylesheet chrome (the
        // width-class table skins) — only structurally BARE tables unwrap.
        foreach (Match st in Regex.Matches(seg, @"<(table|tr|td|tbody|thead|tfoot)\b[^>]*>",
                     RegexOptions.IgnoreCase))
            if (Regex.IsMatch(st.Value, @"\bclass\s*=", RegexOptions.IgnoreCase))
                return false;
        if (Regex.Matches(seg, @"<table\b", RegexOptions.IgnoreCase).Count > 1
            || Regex.IsMatch(seg, @"<th\b", RegexOptions.IgnoreCase))
            return false;
        var anyTd = false;
        foreach (Match tr in Regex.Matches(seg, @"<tr\b[^>]*>([\s\S]*?)</tr\s*>",
                     RegexOptions.IgnoreCase))
        {
            if (Regex.Matches(tr.Groups[1].Value, @"<td\b", RegexOptions.IgnoreCase).Count != 1)
                return false;
            anyTd = true;
        }
        return anyTd;
    }

    private static List<Block> BuildFlowBlocks(ConvertState cv, bool absSpanLedger, List<BeforeMarker> beforeMarkers, string? elementGridFace, bool htmlHasFormInput, bool inlineBlockColRules, bool perTableFormGate, List<Block> rowBlocks, bool sheetChromesCells, string frag)
    {
        var list = new List<Block>();
        if (ContainsTable(frag)
            && (cv.profile.formDialectTables || !htmlHasFormInput || perTableFormGate || cv.profile.escapedAttrDoc))
        {
            var segs = SegmentHtmlTables(frag);
            // A PAGE-BREAK-AFTER div wrapping tables: its close tag parses in
            // a LATER segment where the pending-break state cannot reach, so
            // the break attaches to the div's LAST table segment instead.
            HashSet<int>? breakAfterSegs = null;
            if (cv.profile.uaStdSerif && Regex.IsMatch(frag, @"page-break-after\s*:\s*always",
                    RegexOptions.IgnoreCase))
            {
                var spans = new (int start, int end)[segs.Count];
                var segPos = 0;
                for (var si = 0; si < segs.Count; si++)
                {
                    spans[si] = (segPos, segPos + segs[si].html.Length);
                    segPos += segs[si].html.Length;
                }
                var dRx = new Regex(@"<(/?)div\b[^>]*>", RegexOptions.IgnoreCase);
                foreach (Match bm in Regex.Matches(frag,
                    @"<div\b[^>]*page-break-after\s*:\s*always[^>]*>", RegexOptions.IgnoreCase))
                {
                    var depth = 1;
                    var endPos = -1;
                    for (var dm = dRx.Match(frag, bm.Index + bm.Length); dm.Success;
                         dm = dRx.Match(frag, dm.Index + dm.Length))
                    {
                        depth += dm.Groups[1].Value.Length > 0 ? -1 : 1;
                        if (depth == 0) { endPos = dm.Index; break; }
                    }
                    if (endPos < 0) continue;
                    var lastTableSeg = -1;
                    for (var si = 0; si < segs.Count; si++)
                        if (segs[si].isTable && spans[si].start >= bm.Index
                            && spans[si].end <= endPos)
                            lastTableSeg = si;
                    if (lastTableSeg >= 0)
                        (breakAfterSegs ??= new HashSet<int>()).Add(lastTableSeg);
                }
            }
            for (var segIdx = 0; segIdx < segs.Count; segIdx++)
            {
                var (isTable, seg) = segs[segIdx];
                // Single-column wrapper table in the UA flow: unwrap — its cell
                // content parses as ordinary flow blocks, every block inset by
                // the cell chrome and the first padded down by it (chrome is
                // box space, it never margin-collapses).
                if (isTable && cv.profile.uaStdSerif && !cv.profile.escapedAttrDoc
                    && IsSingleColumnWrapperTable(sheetChromesCells, seg))
                {
                    // Per ROW: the cell content flows with its own block margins
                    // (a p pairs only with an in-cell sibling — a lone p in a
                    // row carries none, probed on the licensing letter's grid),
                    // rows advance one row chrome apart (2 x cellpadding + the
                    // UA 2px cellspacing), and a td align=center centres each
                    // wrapped line over the table's attribute-width band.
                    var openTag = Regex.Match(seg, @"<table\b[^>]*>", RegexOptions.IgnoreCase);
                    double wrapBandW = 0;
                    var wAttrM = Regex.Match(openTag.Value, @"\bwidth\s*=\s*[""']?(\d+(?:\.\d+)?)",
                        RegexOptions.IgnoreCase);
                    if (wAttrM.Success && double.TryParse(wAttrM.Groups[1].Value,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var wAttrPx))
                        wrapBandW = wAttrPx * 0.75 - 2 * UaCellChromePt;
                    var firstUnwrapRow = true;
                    foreach (Match trM in Regex.Matches(seg, @"<tr\b[^>]*>([\s\S]*?)</tr\s*>",
                                 RegexOptions.IgnoreCase))
                    {
                        var tdM = Regex.Match(trM.Groups[1].Value,
                            @"<td\b([^>]*)>([\s\S]*?)</td\s*>", RegexOptions.IgnoreCase);
                        if (!tdM.Success) continue;
                        var cellCentered = Regex.IsMatch(tdM.Groups[1].Value,
                            @"\balign\s*=\s*[""']?center", RegexOptions.IgnoreCase);
                        var beforeUnwrap = list.Count;
                        list.AddRange(ParseBlocks("<div>" + tdM.Groups[2].Value + "</div>",
                            cv.css, beforeMarkers, rowBlocks, cv.profile.metricFlow,
                            cv.uaFlow || cv.profile.printGrid, cv.profile.uaStdSerif || cv.profile.printGrid,
                            cv.profile.printGrid ? cv.profile.printGridBase : cv.articleFlow ? CssRootFontPt
                                : cv.profile.redlineDiffDoc ? RedlineBaseFontPt
                                : cv.profile.bodyBoxGridDoc || cv.profile.scaleToPageWidth ? DefaultBodyFontPt : cv.profile.formBodyFontPt,
                            bandDialect: cv.profile.floatBandDoc, formDialect: cv.profile.formHorizontalDoc,
                            brBlankLines: cv.profile.formDialectTables || cv.profile.elementGridDoc, uaBlockRhythm: cv.profile.sectionedReport,
                            controlBoxes: cv.profile.escapedAttrDoc, articleRhythm: cv.articleFlow,
                            bodyBoxRhythm: cv.profile.bodyBoxGridDoc,
                            containerBoxIndents: cv.profile.chartCardDoc, coverStyles: cv.printCoverDoc,
                            inlineBlockCols: inlineBlockColRules,
                            absSpanLedger: absSpanLedger,
                            spanClassTypography: cv.profile.ptReportDoc,
                            fieldsetBoxes: cv.fieldsetDoc,
                            uaPMargins: cv.profile.emailNewsletterDoc,
                            msoParagraphs: cv.profile.msoFilteredDoc,
                            spanPtTypography: cv.profile.ptStyledFragment || cv.profile.redlineDiffDoc,
                            dwFlow: cv.profile.dwFormDoc,
                            floatFlow: cv.profile.floatBothSidesDoc,
                            inlineEmphasisRuns: cv.profile.redlineDiffDoc,
                            divBandBg: cv.profile.bodyPinnedW > 0));
                        if (list.Count == beforeUnwrap) continue;
                        for (var ub = beforeUnwrap; ub < list.Count; ub++)
                        {
                            list[ub].LeftIndent += UaCellChromePt;
                            if (cellCentered && wrapBandW > 0)
                                list[ub].CenterBandW = wrapBandW;
                            // The table's attribute width is the cell's WRAP box
                            // too (the letter's body wraps at 98.25 + ~333, not
                            // the page content width).
                            if (wrapBandW > 0 && list[ub].MaxWidthPt <= 0)
                                list[ub].MaxWidthPt = wrapBandW;
                        }
                        // The row boundary replaces the leading blocks' own top
                        // margins up to and including the first CONTENT block
                        // (probed: the letter's rows pitch 16.5 = one 13.5 line
                        // + this chrome, single-p cells included; an in-cell
                        // second paragraph keeps its pairwise margin).
                        for (var ub = beforeUnwrap; ub < list.Count; ub++)
                        {
                            list[ub].MarginTop = 0;
                            if (!string.IsNullOrWhiteSpace(list[ub].Text)) break;
                        }
                        // …and the closing content block's bottom margin the same
                        // way (a lone in-cell paragraph carries neither margin —
                        // the row chrome is the whole boundary).
                        for (var ub = list.Count - 1; ub >= beforeUnwrap; ub--)
                        {
                            list[ub].MarginBottom = 0;
                            if (!string.IsNullOrWhiteSpace(list[ub].Text)) break;
                        }
                        list[beforeUnwrap].PadTop +=
                            firstUnwrapRow ? UaCellChromePt : UaWrapperRowChromePt;
                        firstUnwrapRow = false;
                    }
                    continue;
                }
                // The escaped-attr dialect grids EVERY table — form controls
                // draw INSIDE grid cells rather than flattening the table.
                // A radio-only table grids too: its options ride the cells inline.
                if (isTable && (cv.profile.escapedAttrDoc || cv.profile.dwFormDoc
                    || !(perTableFormGate && HasVisibleFormControl(seg)
                         && !RadioGridableControls(seg)))) list.Add(new Block
                {
                    IsTable = true,
                    TableHtml = seg,
                    FloatFirst = Regex.IsMatch(seg, @"^<table\b[^>]*\balign\s*=\s*[""']?left",
                        RegexOptions.IgnoreCase),
                    PageBreakAfterTable = breakAfterSegs?.Contains(segIdx) ?? false,
                });
                else
                {
                    var before = list.Count;
                    // UA flow: a table splits its wrapping <p> across segments, so the
                    // stretch between two tables reaches here as "</p> <p><br/></p> <p>" -
                    // the dangling close/open tags make the p-with-br parse as an inert
                    // hard break instead of the full paragraph pitch the plain flow gives
                    // it (probed: <p><br/></p> costs one whole paragraph pitch, exactly
                    // like a text paragraph). Trim the orphan edges - ONLY on a
                    // text-free spacer stretch, so a segment that carries real
                    // paragraph text keeps its calibrated continuation parse.
                    var uaSpacerSeg = false;
                    if (cv.profile.uaBareDoc
                        && HtmlFragment.StripHtmlTags(seg).Trim().Length == 0
                        && Regex.IsMatch(seg, @"<p\b[^>]*>\s*<br\b", RegexOptions.IgnoreCase))
                    {
                        var segTrim = Regex.Replace(seg, @"^\s*</p\s*>", "", RegexOptions.IgnoreCase);
                        segTrim = Regex.Replace(segTrim, @"<p\b[^>]*>\s*$", "", RegexOptions.IgnoreCase);
                        seg = segTrim;
                        uaSpacerSeg = true;
                    }
                    list.AddRange(ParseBlocks(seg, cv.css, beforeMarkers, rowBlocks, cv.profile.metricFlow,
                        cv.uaFlow || cv.profile.printGrid, cv.profile.uaStdSerif || cv.profile.printGrid,
                        cv.profile.printGrid ? cv.profile.printGridBase : cv.articleFlow ? CssRootFontPt
                            : cv.profile.redlineDiffDoc ? RedlineBaseFontPt
                            : cv.profile.bodyBoxGridDoc || cv.profile.scaleToPageWidth ? DefaultBodyFontPt : cv.profile.formBodyFontPt,
                        bandDialect: cv.profile.floatBandDoc, formDialect: cv.profile.formHorizontalDoc,
                        brBlankLines: cv.profile.formDialectTables || cv.profile.elementGridDoc, uaBlockRhythm: cv.profile.sectionedReport,
                        controlBoxes: cv.profile.escapedAttrDoc, articleRhythm: cv.articleFlow,
                        bodyBoxRhythm: cv.profile.bodyBoxGridDoc,
                        containerBoxIndents: cv.profile.chartCardDoc, coverStyles: cv.printCoverDoc,
                        inlineBlockCols: inlineBlockColRules,
                        absSpanLedger: absSpanLedger,
                        spanClassTypography: cv.profile.ptReportDoc,
                        fieldsetBoxes: cv.fieldsetDoc,
                        uaPMargins: cv.profile.emailNewsletterDoc,
                        msoParagraphs: cv.profile.msoFilteredDoc,
                        spanPtTypography: cv.profile.ptStyledFragment || cv.profile.redlineDiffDoc,
                        dwFlow: cv.profile.dwFormDoc,
                        floatFlow: cv.profile.floatBothSidesDoc,
                        inlineEmphasisRuns: cv.profile.redlineDiffDoc,
                        divBandBg: cv.profile.bodyPinnedW > 0));
                    // A stretch between tables that is nothing but <br>s carries no text,
                    // so the block parser yields nothing for it — yet each of those
                    // breaks is a line box the next table starts below. The element-grid
                    // dialect's body rule names a face but no size: its breaks ride the
                    // UA base size in that face.
                    if (uaSpacerSeg)
                        for (var sb2 = before; sb2 < list.Count; sb2++)
                            if (list[sb2].IsHardBreak) list[sb2].UaSpacerPara = true;
                    // A text-carrying segment that FOLLOWS a table still opens
                    // with the table's break tail (`</table><br/></p><p><br/></p>
                    // <p>text`): its LEADING real line breaks are the same spacer
                    // paragraphs (empty-<p> breaks are not - they cost nothing).
                    if (cv.profile.uaBareDoc && segIdx > 0 && segs[segIdx - 1].isTable)
                        for (var sb3 = before; sb3 < list.Count; sb3++)
                        {
                            if (!list[sb3].IsHardBreak) break;
                            if (list[sb3].IsLineBreak) list[sb3].UaSpacerPara = true;
                        }
                    if (list.Count == before && (cv.bodyCssFace ?? elementGridFace
                            ?? (cv.profile.uaBareDoc ? "Times New Roman" : null)) is { } segBrFace
                        && WinMetricsFor(segBrFace) is { } segBr)
                        foreach (Match _ in Regex.Matches(seg, @"<br\b[^>]*>", RegexOptions.IgnoreCase))
                            list.Add(new Block
                            {
                                Text = "", IsHardBreak = true, IsLineBreak = true,
                                ExplicitHeight = MetricLineHeight(
                                    cv.profile.bodyCssFontPt > 0 ? cv.profile.bodyCssFontPt : DefaultBodyFontPt, segBr.sum),
                            });
                }
            }
        }
        else list.AddRange(ParseBlocks(frag, cv.css, beforeMarkers, rowBlocks, cv.profile.metricFlow,
            cv.uaFlow || cv.profile.printGrid, cv.profile.uaStdSerif || cv.profile.printGrid,
            cv.profile.printGrid ? cv.profile.printGridBase : cv.articleFlow ? CssRootFontPt
                : cv.profile.redlineDiffDoc ? RedlineBaseFontPt
                : cv.profile.bodyBoxGridDoc || cv.profile.scaleToPageWidth ? DefaultBodyFontPt : cv.profile.formBodyFontPt,
            bandDialect: cv.profile.floatBandDoc, formDialect: cv.profile.formHorizontalDoc,
            brBlankLines: cv.profile.formDialectTables || cv.profile.elementGridDoc, uaBlockRhythm: cv.profile.sectionedReport,
            controlBoxes: cv.profile.escapedAttrDoc, articleRhythm: cv.articleFlow,
            bodyBoxRhythm: cv.profile.bodyBoxGridDoc,
            containerBoxIndents: cv.profile.chartCardDoc, coverStyles: cv.printCoverDoc,
            inlineBlockCols: inlineBlockColRules,
                        absSpanLedger: absSpanLedger,
                        spanClassTypography: cv.profile.ptReportDoc,
                        fieldsetBoxes: cv.fieldsetDoc,
                        uaPMargins: cv.profile.emailNewsletterDoc,
                        msoParagraphs: cv.profile.msoFilteredDoc,
                        spanPtTypography: cv.profile.ptStyledFragment || cv.profile.redlineDiffDoc,
                        dwFlow: cv.profile.dwFormDoc,
                        floatFlow: cv.profile.floatBothSidesDoc,
                        inlineEmphasisRuns: cv.profile.redlineDiffDoc,
                        html5UaHeadings: cv.html5BareUa,
                        divBandBg: cv.profile.bodyPinnedW > 0));
        return list;
    }

    private static List<Block> BuildStructuredBlocks(ConvertState cv, bool absSpanLedger, List<BeforeMarker> beforeMarkers, string? elementGridFace, bool htmlHasFormInput, bool inlineBlockColRules, bool perTableFormGate, List<Block> rowBlocks, bool sheetChromesCells, string frag, int depth, double availPt = 0)
    {
        var structured = Regex.IsMatch(frag, @"float\s*:\s*left|border\s*:\s*solid", RegexOptions.IgnoreCase)
            || (cv.profile.printGrid && Regex.IsMatch(frag, @"col-xs-|infobox", RegexOptions.IgnoreCase));
        // A pinned-body report is a single column by AUTHORING (margin:auto
        // on a fixed body) — the floats its cells carry are in-cell layout,
        // and the div segmenter would cut its tables apart.
        if (depth > 6 || !structured || cv.profile.bodyPinnedW > 0)
            return BuildFlowBlocks(cv, absSpanLedger, beforeMarkers, elementGridFace, htmlHasFormInput, inlineBlockColRules, perTableFormGate, rowBlocks, sheetChromesCells, frag);
        // px-width float columns convert to fractions of the box they live IN —
        // the page content at the top level, the enclosing column when nested.
        var segs = SegmentDivStructures(frag, cv.profile.printGrid ? cv.css : null,
            availPt > 0 ? availPt : cv.pageWidth - cv.marginLeft - cv.marginRight,
            allowPxCols: cv.profile.formHorizontalDoc);
        if (segs.Count == 0) return BuildFlowBlocks(cv, absSpanLedger, beforeMarkers, elementGridFace, htmlHasFormInput, inlineBlockColRules, perTableFormGate, rowBlocks, sheetChromesCells, frag);
        var list = new List<Block>();
        foreach (var seg in segs)
        {
            switch (seg.Kind)
            {
                case DivSeg.Col:
                    list.Add(new Block { ColScopeStart = true, FloatWidthFrac = seg.WidthFrac, ColPadPt = seg.ColPadPt });
                    list.AddRange(BuildStructuredBlocks(cv, absSpanLedger, beforeMarkers, elementGridFace, htmlHasFormInput, inlineBlockColRules, perTableFormGate, rowBlocks, sheetChromesCells, seg.Html, depth + 1));
                    list.Add(new Block { ColScopeEnd = true });
                    break;
                case DivSeg.Band when seg.Cols is { Count: > 0 }:
                    list.Add(new Block { FloatBandStart = true });
                    foreach (var (inner, startFrac, widthFrac, padTopPt) in seg.Cols)
                    {
                        list.Add(new Block
                        {
                            FloatColStart = true,
                            FloatStartFrac = startFrac,
                            FloatWidthFrac = widthFrac,
                            FloatPadTopPt = padTopPt,
                        });
                        list.AddRange(BuildStructuredBlocks(cv, absSpanLedger, beforeMarkers, elementGridFace, htmlHasFormInput, inlineBlockColRules, perTableFormGate, rowBlocks, sheetChromesCells, inner, depth + 1,
                            widthFrac * (availPt > 0 ? availPt : cv.pageWidth - cv.marginLeft - cv.marginRight)));
                    }
                    list.Add(new Block { FloatBandEnd = true });
                    break;
                case DivSeg.Box:
                {
                    var innerBlocks = BuildStructuredBlocks(cv, absSpanLedger, beforeMarkers, elementGridFace, htmlHasFormInput, inlineBlockColRules, perTableFormGate, rowBlocks, sheetChromesCells, seg.Html, depth + 1, availPt);
                    var firstTextSize = 0.0;
                    foreach (var ib in innerBlocks)
                    {
                        if (ib.IsTable || ib.IsImage) break;
                        if (!string.IsNullOrEmpty(ib.Text)) { firstTextSize = ib.FontSize; break; }
                    }
                    list.Add(new Block
                    {
                        BoxStart = true, BoxBorderPt = seg.BorderPt, BoxPadTopPt = seg.PadTopPt,
                        BoxAscentPt = cv.profile.printGrid ? 0 : firstTextSize * 0.9,
                        BoxPadSidePt = cv.profile.printGrid ? seg.PadSidePt : 0,
                        BoxBorderGray = seg.BorderGray,
                    });
                    list.AddRange(innerBlocks);
                    list.Add(new Block { BoxEnd = true, BoxPadBottomPt = seg.PadBottomPt, BoxMarginBottomPt = seg.MarginBottomPt });
                    break;
                }
                default:
                    list.AddRange(BuildFlowBlocks(cv, absSpanLedger, beforeMarkers, elementGridFace, htmlHasFormInput, inlineBlockColRules, perTableFormGate, rowBlocks, sheetChromesCells, seg.Html));
                    break;
            }
        }
        return list;
    }

    private static string BandMeasureFace(Block b)
    {
        var fam = string.IsNullOrEmpty(b.FontFamily) ? "Times New Roman" : b.FontFamily!;
        if ((b.FontRes == "F2" || b.EmBold) && PosFace(fam + " Bold").ttf is not null)
            return fam + " Bold";
        return PosFace(fam).ttf is not null ? fam : "Arial";
    }

    private static bool IsBlankFlowBlock(ConvertState cv, Block b) =>
        string.IsNullOrWhiteSpace(b.Text) && string.IsNullOrEmpty(b.ImageSrc) && !b.IsTable;
}
