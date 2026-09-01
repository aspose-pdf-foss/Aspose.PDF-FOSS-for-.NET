using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The block-build stage of an HTML conversion: flow detection, block construction and the table width ledgers, verbatim. A non-null result is a finished document.</summary>
    private static Document? ConvertBlockBuild(HtmlLoadOptions? options, ConvertState cv)
    {
        cv.uaFlow = cv.uaMshtml || cv.uaNoFontDoc;
        // The full-document path draws Standard-14 serif (no embedding); MSHTML keeps its
        // embedded-Type0 serif output.
        cv.profile.uaStdSerif = cv.uaNoFontDoc && !cv.uaMshtml;
        // A BARE full UA document: a real <html> document with no stylesheet
        // markup at all. Only this shape carries the probed serif-floor and
        // break-paragraph laws - styled or fragment documents keep their
        // calibrated flows (a UTF-16 <body> fragment charges NOTHING for a
        // bare <br> between tables where the full document charges a line box).
        cv.profile.uaBareDoc = cv.profile.uaStdSerif && !cv.profile.deadExternalCss
            && !Regex.IsMatch(cv.html, @"<(style|link)\b", RegexOptions.IgnoreCase)
            && Regex.IsMatch(cv.html, @"<html\b", RegexOptions.IgnoreCase);
        cv.html5BareUa = (cv.profile.uaBareDoc || (cv.profile.uaStdSerif && cv.css.Count == 0 && Regex.IsMatch(cv.html, @"<html\b", RegexOptions.IgnoreCase))) && Regex.IsMatch(cv.html, @"<!DOCTYPE\s+html\s*>", RegexOptions.IgnoreCase);
        // A body styled width:100% — the sheet widens by the UA body inset and
        // its tables sit the measured gap below body text (both measured).
        cv.profile.bodyWidthFullDoc = false;
        // The Word-filtered text column the flow justifies to (see the
        // margin override below: sheet = 96 + column + 96), and the drop of its
        // broken-image placeholders under the 72 pt top margin (both measured).
        // The UA paragraph margin (1.12 em of the 12pt base), as the metric flow's
        // own <p> blocks carry it - probed on the plain-serif document ladder:
        // p-to-p pitch 26.94 = 13.5 line box + this margin, collapsed pairwise.
        const double MsoTextColumnWPt = 529.8;
        if (cv.uaFlow)
        {
            cv.profile.metricFlow = true;
            // The UA serif, unless the body rule pinned its own face at the UA base.
            cv.profile.metricFace = cv.profile.uaStdSerif && cv.uaBodyFace is not null ? cv.uaBodyFace : "Times New Roman";
            cv.bodyMarT = 6.0;
            // Edge-to-edge sheets: the first paragraph's UA margin-top collapses
            // with the body margin — the content opens max(6, 12) below the top
            // margin, plus the engine's measured first-line seat (baseline lands
            // at 96.2: 72 + 15.35 + the metric drop).
            if (cv.edgeToEdgeDoc) cv.bodyMarT = 15.35;
            // Per-side-touched defaults: an untouched side keeps the renderer
            // default (the caller authored only the sides they set).
            var uaPerSide = cv.marginsExplicit && (cv.pageMargin?.IsTouched ?? false)
                && cv.pageMargin!.HtmlPerSideDefaults;
            // a zero body margin keeps the bare page margin — no 6pt body inset
            if (cv.profile.bodyZeroMargin) cv.bodyMarT = 0.0;
            cv.marginLeft = (cv.marginsExplicit
                ? (uaPerSide && !cv.pageMargin!.LeftTouched ? 90.0 : cv.pageMargin!.Left) : 90.0)
                + (cv.profile.bodyZeroMargin ? 0.0 : 6.0);
            cv.marginRight = (cv.marginsExplicit
                ? (uaPerSide && !cv.pageMargin!.RightTouched ? 90.0 : cv.pageMargin!.Right) : 90.0)
                // Edge-to-edge sheets get the UA body margin on the RIGHT too — a
                // width:100% table ends exactly one body margin short of the edge.
                + (cv.edgeToEdgeDoc ? 6.0 : 0.0);
            cv.marginTop = cv.marginsExplicit
                ? (uaPerSide && !cv.pageMargin!.TopTouched ? 72.0 : cv.pageMargin!.Top) : 72.0;
            cv.marginBottom = cv.marginsExplicit
                ? (uaPerSide && !cv.pageMargin!.BottomTouched ? 72.0 : cv.pageMargin!.Bottom) : 72.0;
            // Fieldset worksheet: the body's own margin + padding ARE the content
            // offsets (no UA 6pt inset), and that padding blocks the doc-top
            // margin collapse — the first heading keeps its full margin.
            if (cv.fieldsetDoc)
            {
                cv.marginLeft = 90.0 + cv.fsBodyChromePt;
                cv.marginTop = 72.0 + cv.fsBodyChromePt;
                cv.bodyMarT = 0.0;
            }
            // Word-filtered pages lay on a SYMMETRIC 96 pt inset over the
            // measured 529.8 pt text column — the sheet is
            // 96 + 529.8 + 96 = 721.75, and the justified lines stretch to
            // exactly that column (measured on the filtered-page output).
            if (cv.profile.msoFilteredDoc && !cv.marginsExplicit && !(cv.pageInfo?.WidthAssigned ?? false))
            {
                cv.marginRight = 90.0 + UaBodyMarginPt;
                cv.pageWidth = cv.marginLeft + MsoTextColumnWPt + cv.marginRight;
            }
            // The custom-font report keeps the UA body margin on BOTH sides of
            // its explicit zero margins — its reference wraps at exactly
            // page − 2×6 (a 602 pt line breaks out of the 600 box).
            if (cv.customFontFaceDoc && cv.marginsExplicit)
                cv.marginRight += UaBodyMarginPt;
            // A width:100% BODY spans the bare page margins instead of losing
            // the 6 pt UA body inset to its width — the sheet grows by that
            // inset so the offset box still fits (measured:
            // MediaBox 601 = 595 + 6, body box 96..517 = 421 wide).
            cv.profile.bodyWidthFullDoc = !(cv.pageInfo?.WidthAssigned ?? false)
                && Regex.Match(cv.html, @"<body\b[^>]*style\s*=\s*(['""])[^'""]*?(?<![-\w])width\s*:\s*100%[^'""]*\1",
                    RegexOptions.IgnoreCase).Success;
            if (cv.profile.bodyWidthFullDoc)
            {
                cv.pageWidth += UaBodyMarginPt;
                cv.marginRight -= UaBodyMarginPt;
            }
        }

        // Official-letter flow (gated): an explicit-zero-margin CJK letter —
        // a content class carries the family, table rows pace themselves with
        // inline font-size keywords and px heights, and no body rule exists
        // for the standard metric gate. The metric flow lays it out with the
        // face's real advances; an uninstalled family substitutes to SimSun,
        // the same fallback the expected output draws it with.
        if (!cv.profile.metricFlow && cv.marginsExplicit && !cv.css.ContainsKey("body")
            && Regex.IsMatch(cv.html, @"<tr[^>]*style\s*=\s*[""'][^""']*font-size\s*:",
                RegexOptions.IgnoreCase))
        {
            string? letterFam = null;
            foreach (var (sel, props) in cv.css)
                if (sel.StartsWith('.') && props.TryGetValue("font-family", out var lff))
                {
                    letterFam = FirstFontFamily(lff);
                    break;
                }
            if (letterFam is not null)
            {
                var letterFace = WinMetricsFor(letterFam) is not null ? letterFam
                    : WinMetricsFor("SimSun") is not null ? "SimSun" : null;
                if (letterFace is not null)
                {
                    cv.profile.metricFlow = true;
                    cv.profile.metricFace = letterFace;
                    // the UA 8px body margin boxes the letter's tables
                    cv.marginLeft += 6.0;
                    cv.marginRight += 6.0;
                    cv.bodyMarT = 6.0;
                }
            }
        }

        cv.articleFlow = false;
        cv.articleLineFactor = 0.0;
        DetectArticleAndNewsletterFlow(cv.bodyCssFace, cv.css, cv.html, cv.inlineSvgs, cv.marginsExplicit, cv.profile, ref cv.articleFlow, ref cv.articleLineFactor, ref cv.bodyMarT, ref cv.marginTop);

        cv.printCoverDoc = cv.profile.bodyZeroMargin && !cv.profile.metricFlow && !cv.profile.chartCardDoc && Regex.IsMatch(cv.html, @"page-break-after\s*:\s*always", RegexOptions.IgnoreCase);

        // Styled inline rows (nav bars, centered link lines) render from prebuilt
        // run blocks; their markup is replaced by <rowmark> placeholders.
        cv.html = ExtractRowBlocks(cv.html, cv.css, out var rowBlocks);

        // Document-level RTL (dir="rtl" on <html>/<body>): a block image wider than
        // the content box keeps its NATIVE size with its right edge on the right
        // margin, overflowing (and clipping) off the left page edge — the mirror of
        // the LTR left-pinned overflow.
        cv.profile.rtlDoc = Regex.IsMatch(cv.html, @"<(?:html|body)[^>]*\bdir\s*=\s*[""']?rtl",
            RegexOptions.IgnoreCase);

        cv.runHeader = null;
        cv.runFooter = null;
        cv.hMatch = Regex.Match(cv.html, @"<header([^>]*)>(.*?)</header>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (cv.hMatch.Success && IsFixedRegion(cv.hMatch.Groups[1].Value, "header", cv.css))
        {
            cv.runHeader = DecodeEntities(HtmlFragment.StripHtmlTags(cv.hMatch.Groups[2].Value)).Trim();
            cv.html = cv.html.Remove(cv.hMatch.Index, cv.hMatch.Length);
        }
        cv.fMatch = Regex.Match(cv.html, @"<footer([^>]*)>(.*?)</footer>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (cv.fMatch.Success && IsFixedRegion(cv.fMatch.Groups[1].Value, "footer", cv.css))
        {
            cv.runFooter = DecodeEntities(HtmlFragment.StripHtmlTags(cv.fMatch.Groups[2].Value)).Trim();
            cv.html = cv.html.Remove(cv.fMatch.Index, cv.fMatch.Length);
        }
        if (!string.IsNullOrEmpty(cv.runHeader)) cv.marginTop += 24;
        if (!string.IsNullOrEmpty(cv.runFooter)) cv.marginBottom += 24;

        cv.page1FooterImgSrc = null;
        cv.page1FooterImgW = 0;
        cv.page1FooterImgH = 0;
        cv.dfMatch = Regex.Match(cv.html, @"<div[^>]*\bid\s*=\s*[""']footer[""'][^>]*>(.*?)</div>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (cv.dfMatch.Success)
        {
            var footerInner = cv.dfMatch.Groups[1].Value;
            var footImg = Regex.Match(footerInner, @"<img\b[^>]*>", RegexOptions.IgnoreCase);
            var footerText = DecodeEntities(HtmlFragment.StripHtmlTags(footerInner)).Trim();
            if (footImg.Success && footerText.Length == 0)
            {
                var tag = footImg.Value;
                var srcM = Regex.Match(tag, @"\bsrc\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
                if (srcM.Success)
                {
                    cv.page1FooterImgSrc = srcM.Groups[1].Value;
                    cv.page1FooterImgW = PxOf(tag, "width") * 0.75;  // CSS px → pt
                    cv.page1FooterImgH = PxOf(tag, "height") * 0.75;
                    cv.html = cv.html.Remove(cv.dfMatch.Index, cv.dfMatch.Length);
                }
            }
        }
        cv.beforeMarkers = ParseBeforeMarkers(cv.html);

        cv.blocks = new List<Block>();
        cv.htmlHasFormInput = HasVisibleFormControl(cv.html);


        // FORM-DOCUMENT dialect (document-level `td {font: …}` shorthand cells —
        // the application-form shape): EVERY table renders as a grid, its cells
        // laying form controls out inline. Outside the dialect the legacy gate
        // holds — a document with any form control keeps the whole flat path,
        // whose blocks emit the controls as AcroForm fields.
        cv.profile.formDialectTables =
            (CssFontShorthand(cv.css, "td") ?? CssFontShorthand(cv.css, "table")) is not null;

        cv.perTableFormGate = cv.htmlHasFormInput && !cv.profile.formDialectTables
            && SegmentHtmlTables(cv.html).Any(s => s.isTable && !HasVisibleFormControl(s.html));

        cv.sheetChromesCells = false;
        foreach (var chromeKey in new[] { "td", "table td", "th", "table th" })
        {
            if (!cv.css.TryGetValue(chromeKey, out var chromeRule)) continue;
            if (chromeRule.TryGetValue("border", out var chromeB)
                && !Regex.IsMatch(chromeB, @"^\s*(0\w*|none)\b", RegexOptions.IgnoreCase))
                cv.sheetChromesCells = true;
            if (chromeRule.ContainsKey("background-color") || chromeRule.ContainsKey("background"))
                cv.sheetChromesCells = true;
        }

        // Float-column groups and bordered divs become structural marker blocks with
        // their inner flow recursively segmented; HTML without those patterns takes
        // the flat path untouched.
        // Report label/span rows (gated with the physical-unit body width that also
        // sizes the sheet): each row is a bold right-aligned label column beside a
        // wrapped span column at the sheet's own small size; an hr divides sections.
        if (!cv.marginsExplicit && !ContainsTable(cv.html)
            && Regex.Match(cv.html, @"<body\b[^>]*style\s*=\s*(['""])[^'""]*?(?<![-\w])width\s*:\s*([\d.]+\s*(?:cm|mm|in|pt))[^'""]*\1",
                RegexOptions.IgnoreCase) is { Success: true } repBodyM
            && TryParseLength(repBodyM.Groups[2].Value.Replace(" ", ""), out var repBodyW)
            && repBodyW > 0
            && TryBuildReportLabelBlocks(cv.html, repBodyW, out var repBlocks))
            cv.blocks = repBlocks;
        else
            cv.blocks = BuildStructuredBlocks(cv, cv.absSpanLedger, cv.beforeMarkers, cv.elementGridFace, cv.htmlHasFormInput, cv.inlineBlockColRules, cv.perTableFormGate, rowBlocks, cv.sheetChromesCells, cv.html, 0);
        // A content-less document still ships ONE page at the configured size —
        // the converter never emits a zero-page PDF.
        if (cv.blocks.Count == 0)
        {
            var blankDoc = Document.Create();
            blankDoc.Pages.Add(cv.pageWidth, cv.pageHeight);
            return blankDoc;
        }

        cv.dashWrapFace = cv.bodyCssFace ?? "Times New Roman";
        cv.quirksWrapW = 0.0;
        if ((cv.profile.quirksCssRun || cv.inlineBlockColRules) && WinMetricsFor(cv.dashWrapFace) is not null)
        {
            foreach (var b in cv.blocks)
            {
                if (b.IsTable || b.IsHardBreak || b.IsImage || string.IsNullOrEmpty(b.Text)) continue;
                var bfs = b.FontSize > 0 ? b.FontSize : 11.0;
                foreach (var seg in DashSegments(b.Text))
                    if (seg.Length > 2)
                        cv.quirksWrapW = Math.Max(cv.quirksWrapW,
                            MeasureFaceText(cv.dashWrapFace, seg, bfs));
            }
        }

        // Print media reset (* { color:#000 !important; background: transparent }):
        // every block draws black on transparent; borders and the heading bands keep
        // their colours (the reset touches text and backgrounds only).
        if (cv.profile.printGrid)
            foreach (var b in cv.blocks)
            {
                b.ForeColor = null;
                b.BackgroundColor = null;
            }

        // An <img> whose source cannot be loaded surfaces its alt text as an ordinary
        // text line (the browser fallback) — the block keeps its place in the flow.
        foreach (var b in cv.blocks)
            if (b.IsImage && !string.IsNullOrWhiteSpace(b.ImageAlt)
                && !b.ImageSrc.StartsWith("inline-svg:", StringComparison.Ordinal)
                && LoadConverterImage(b.ImageSrc, options) is null)
            {
                b.IsImage = false;
                b.Text = b.ImageAlt!.Trim();
                if (b.FontSize <= 0) b.FontSize = cv.uaFlow ? 12 : 11;
            }


        // Pure-table document with explicit page margins: the UA's 8px body margin
        // (6pt) still sits INSIDE the authored margins — it offsets the
        // table on the left and the top. The default-margin path already bakes
        // this into its calibrated 96/89 defaults; flow documents keep their
        // calibrated explicit-margin geometry untouched.
        if (cv.marginsExplicit && cv.blocks.TrueForAll(b => b.IsTable))
        {
            cv.marginLeft += 6.0;
            cv.marginTop += 6.0;
        }

        cv.availContentW = cv.pageWidth - cv.marginLeft - cv.marginRight;
        cv.widestTable = 0;
        // Set when any probed segment holds an over-declared fixed-layout attribute
        // grid: those documents draw their nested grids as REAL grids (the metric
        // layouter flattens nested tables into stacked lines).
        cv.profile.overDeclaredGridDoc = false;
        cv.preGrownGridDoc = false;
        cv.widestIsPctMin = false;
        DetectOverDeclaredGrid(cv.bodyCssFace, cv.availContentW, cv.blocks, cv.css, cv.inlineSvgs, options, cv.profile, ref cv.preGrownGridDoc, ref cv.widestIsPctMin, ref cv.widestTable);
        cv.declaredTableW = 0;
        cv.collapseTableW = 0;
        foreach (Match tm in Regex.Matches(cv.html, @"<table\b[^>]*>", RegexOptions.IgnoreCase))
        {
            double w = 0;
            var attr = Regex.Match(tm.Value,
                @"\bwidth\s*=\s*[""']?\s*(\d+(?:\.\d+)?)\s*(?:px)?\s*[""'\s/>]", RegexOptions.IgnoreCase);
            if (attr.Success && double.TryParse(attr.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var attrPx))
                w = attrPx * 0.75;
            var styleW = Regex.Match(DivStyleOf(tm.Value),
                @"\bwidth\s*:\s*([\d.]+\s*(?:px|pt|in|cm|mm))", RegexOptions.IgnoreCase);
            if (styleW.Success && TryParseLength(styleW.Groups[1].Value.Replace(" ", ""), out var stylePt))
                w = stylePt;
            // a width CLASS on the table (the boleto's .w666 skin) declares the
            // same fixed box as an attribute
            if (w == 0)
            {
                var clsM = Regex.Match(tm.Value, @"class\s*=\s*[""']?([\w \-]+)",
                    RegexOptions.IgnoreCase);
                if (clsM.Success)
                    foreach (var tcl in clsM.Groups[1].Value.Split(' ',
                        StringSplitOptions.RemoveEmptyEntries))
                        if (cv.css.TryGetValue("." + tcl, out var tclR)
                            && tclR.TryGetValue("width", out var tclW)
                            && TryParseLength(tclW.Trim(), out var tclPt))
                            w = Math.Max(w, tclPt);
            }
            if (w > cv.declaredTableW) cv.declaredTableW = w;
            // a bordered COLLAPSE grid (border=N + border-collapse:collapse)
            // keeps its declared width exactly — the sheet grows to page margin
            // + declared box + page margin, with no slack and no body inset.
            // A style-collapsed table with its own declared width follows the
            // same model without the border attribute (probed: the widest
            // width:491.4pt collapse grid grows the sheet to 96 + 491.4 + 90).
            if (w > cv.collapseTableW
                && Regex.IsMatch(tm.Value, @"border-collapse\s*:\s*collapse", RegexOptions.IgnoreCase)
                && (Regex.IsMatch(tm.Value, @"\bborder\s*=\s*[""']?[1-9]", RegexOptions.IgnoreCase)
                    || styleW.Success))
                cv.collapseTableW = w;
        }
        cv.elementTableW = 0;
        if (cv.profile.elementGridDoc && cv.css.TryGetValue("table", out var tElemDecl)
            && tElemDecl.TryGetValue("width", out var tElemW)
            && !tElemW.Contains('%')
            && TryParseLength(tElemW.Trim(), out var tElemPt)
            && Regex.IsMatch(cv.html, @"<table\b", RegexOptions.IgnoreCase))
            cv.elementTableW = tElemPt;
        // The natural box for a declared-width table carries a 2.25
        // chrome step over the declared width — the probe's symmetric border-
        // spacing counts 3.0 (measured: the UA sheet lands at 96 + 936px + 2.25
        // + 90); trim the difference so the widened page sizes as expected.
        if (cv.uaFlow && cv.declaredTableW > 0
            && cv.widestTable > cv.declaredTableW + 2.25 && cv.widestTable <= cv.declaredTableW + 3.0 + 1e-6)
            cv.widestTable = cv.declaredTableW + 2.25;
        // …unless the caller AUTHORED the page width. A browser printing to a fixed
        // paper size overflows a too-wide table, it does not grow the paper, and the
        // public idiom for that is `options.PageInfo.Width = PageSize.A4.Width`.
        // Under ScaleToPageWidth an authored width no longer pins the sheet
        // during LAYOUT: the body grows to the widest table's min-content (the
        // percent tables resolve against the grown box),
        // and the finished pages shrink back onto the authored page with the
        // content pinned at the left margin and the page top.
        if (cv.profile.bodyPinnedW > 0 && !(cv.pageInfo?.WidthAssigned ?? false))
        {
            // The pinned body sizes the sheet and nothing else does: the widest
            // table lays out inside (or overflows) the authored box.
            if (90.0 + cv.profile.bodyPinnedW + 90.0 > cv.pageWidth)
            {
                cv.pageHeight = Math.Max(cv.pageWidth, cv.pageHeight);
                cv.pageWidth = 90.0 + cv.profile.bodyPinnedW + 90.0;
                cv.marginLeft = 90.0;
                cv.marginRight = 90.0;
                // The authored zero-margin body starts at the plain page margin;
                // every offset below it is a REAL margin the flow spends
                // (measured: content top = 72 + the first table's own margin).
                cv.marginTop = 72.0;
            }
        }
        // The ELEMENT-rule grid (`table { width: 650px }` sizing every table on the
        // page): the grown sheet ends one PAGE margin past the declared box
        // (measured: 96 + 487.5 + 90 = 673.5) — the flow keeps its calibrated left
        // inset but the right side is the page margin, not the A4 flow's right
        // inset. The declared width pins the natural width too, so the probe's
        // widest table cannot exceed it.
        else if (cv.elementTableW > 0 && cv.widestTable <= cv.elementTableW + 1e-6
            && cv.elementTableW > cv.pageWidth - cv.marginLeft - cv.marginRight
            && cv.elementTableW > cv.declaredTableW && !cv.profile.escapedAttrDoc
            && !(cv.pageInfo?.WidthAssigned ?? false))
        {
            cv.pageHeight = Math.Max(cv.pageWidth, cv.pageHeight);
            cv.pageWidth = cv.marginLeft + cv.elementTableW + 90.0;
            // The grown sheet keeps the DEFAULT top margin plus the UA body
            // margin (measured: the first grid's border at 72 + 6 = 78), not
            // the A4 flow's calibrated 89.
            cv.marginTop = 72.0 + UaBodyMarginPt;
        }
        else if (cv.profile.dwFormDoc && cv.widestTable > cv.pageWidth - cv.marginLeft - 90.0)
        {
            // DataWorks form page: the sheet grows to hold the widest form row
            // plus the UA right margin (measured: the page is 617.28 =
            // 96 + the widest row + 90).
            cv.pageHeight = Math.Max(cv.pageWidth, cv.pageHeight);
            cv.pageWidth = cv.marginLeft + cv.widestTable + 90.0;
            cv.marginTop = 72.0 + UaBodyMarginPt;
            // Page 1 fills down to the Document-links row (ink to
            // 758.9 on the 842 sheet) — the flow's break threshold sits at 68.
            cv.marginBottom = DwBottomMarginPt;
        }
        else if (cv.profile.scaleToPageWidth && cv.widestTable > cv.availContentW)
        {
            cv.scaleReqPageW = cv.pageWidth;
            cv.scaleReqPageH = cv.pageHeight;
            cv.pageWidth = cv.marginLeft + cv.widestTable + cv.marginRight;
            var pmL = cv.pageMargin?.Left ?? 0;
            var pmR = cv.pageMargin?.Right ?? 0;
            cv.scalePendingS = (cv.scaleReqPageW - pmL - pmR) / (cv.pageWidth - pmL - pmR);
        }
        else if ((cv.profile.uaStdSerif || cv.collapseTableW > cv.widestTable)
            && cv.collapseTableW > cv.availContentW
            && !(cv.pageInfo?.WidthAssigned ?? false))
        {
            // A collapse grid's DECLARED width sizes the sheet exactly (probed:
            // the widest width:491.4pt collapse table grows the page to
            // 96 + 491.4 + 90 with no slack) — on the legacy path too, whenever
            // the declaration exceeds every probed natural width.
            cv.pageHeight = Math.Max(cv.pageWidth, cv.pageHeight);
            cv.pageWidth = cv.marginLeft + cv.collapseTableW + 90.0;
        }
        // Fieldset worksheet: the page grows to the widest declared table plus
        // the whole left chrome chain and the frame's right pad (probed:
        // 90 + 39 + 9.75 + 450 + 8.25 + 90.75 = 687.75).
        else if (cv.profile.uaStdSerif && cv.fieldsetDoc && cv.declaredTableW > 0
            && 90.0 + cv.fsBodyChromePt + FsPadLeftPt + cv.declaredTableW + FsPadRightPt
                + FsWidenRightPt > cv.pageWidth
            && !(cv.pageInfo?.WidthAssigned ?? false))
        {
            cv.pageHeight = Math.Max(cv.pageWidth, cv.pageHeight);
            cv.pageWidth = 90.0 + cv.fsBodyChromePt + FsPadLeftPt + cv.declaredTableW
                + FsPadRightPt + FsWidenRightPt;
        }
        // RTL attribute-grid sheet: the page grows to the widest DECLARED table
        // between the 90 pt page margin (its left edge) and the RTL right inset
        // the grids anchor against (measured: 90 + 600 + 91.78 = 781.78).
        else if (cv.profile.uaStdSerif && cv.profile.rtlDoc && cv.declaredTableW > 0
            && 90.0 + cv.declaredTableW + RtlGridRightInsetPt > cv.pageWidth
            && !(cv.pageInfo?.WidthAssigned ?? false))
        {
            cv.pageHeight = Math.Max(cv.pageWidth, cv.pageHeight);
            cv.pageWidth = 90.0 + cv.declaredTableW + RtlGridRightInsetPt;
        }
        // A FLOAT-HEADER document keeps its sheet and CLIPS a wide table instead of
        // growing to it: the certificate page declares a 720 px grid on a 595 pt A4
        // sheet and the page is left alone, its text ending at 505 where
        // the content box does. Widening it there moved every float and heading with it.
        else if (cv.widestTable > cv.availContentW && !(cv.pageInfo?.WidthAssigned ?? false)
                 && !cv.profile.floatBothSidesDoc)
        {
            // Band documents: the widened page = min-content + left page margin (90) +
            // UA body-left (6) + right page margin (90); no measurement
            // slack and no body-right margin. A zero-body-margin or print-grid
            // document widens to exactly the declared table plus the margins (no
            // slack there either). Other documents keep the legacy slack.
            // A page that grows to its content is measured off the PAGE margin (90) —
            // not the narrower right inset the A4 flow is calibrated to, and with no
            // slack: the sheet ends exactly one page margin past the last ink, so the
            // widest table overflows the body box by the body's own right margin and
            // is clipped there. Legacy dialects that pin their own symmetric margins
            // keep both their margin and their slack.
            // Dead-stylesheet UA documents follow the same model as the ink-widen
            // dialects: the sheet is page margin + the widest table's natural box
            // + page margin, and the content box keeps the symmetric 96 inset.
            // A UA-serif document probed with the serif min floors follows the same
            // model: the sheet ends one page margin past the last ink (probed: the
            // two-table report page = 96 + the percent grid's min floors + 90); the
            // legacy +8-slack symmetric widen was calibrated against the Helvetica
            // stand-in floors this document class no longer probes with.
            var inkWiden = !cv.marginsExplicit && !cv.profile.printGrid && !cv.profile.floatBandDoc
                && (!cv.uaFlow || cv.profile.deadExternalCss || (cv.profile.uaStdSerif && !cv.profile.deadExternalCss && cv.widestIsPctMin))
                && !cv.profile.bodyZeroMargin && !cv.profile.escapedAttrDoc;
            var neededContent = cv.profile.floatBandDoc ? cv.widestTable - 6
                : cv.profile.bodyZeroMargin || cv.profile.printGrid || inkWiden ? cv.widestTable : cv.widestTable + 8;
            var widenRight = inkWiden ? 90.0 : cv.marginRight;
            // Chain-dialect documents widen to PAGE margin + content + PAGE margin
            // exactly — the content box starts at x = 90 on the grown
            // sheet, not at the A4 flow's calibrated 96 left inset.
            var chainWiden = inkWiden && cv.profile.docChainRules is not null;
            var neededPage = neededContent + (chainWiden ? 90.0 : cv.marginLeft) + widenRight
                // A UA-serif document's grown sheet keeps the symmetric body
                // margin on the RIGHT of the widest grid too (measured: the
                // register report's grid ends one body margin inside the frame).
                + (cv.profile.uaStdSerif && !cv.profile.deadExternalCss && !cv.profile.bodyZeroMargin && !inkWiden ? UaBodyMarginPt : 0);
            // The escaped-attr dialect never widens: it keeps the default
            // page and SQUEEZES its grids into the content box instead.
            if (neededPage > cv.pageWidth && !cv.profile.escapedAttrDoc)
            {
                // The layout engine keeps the page's larger (portrait-height) dimension as the
                // height and widens the width to fit the table, rather than the swapped landscape
                // short edge — so a wide table on an A4 page lands ~1129 × 842, not 1129 × 595.
                cv.pageHeight = Math.Max(cv.pageWidth, cv.pageHeight);
                cv.pageWidth = neededPage;
                // A pre-grown grid's sheet opens at the UA top (72 + the body
                // margin) — the legacy calibrated top was measured on flows
                // this dialect never rides (probed: the first header row's
                // baseline sits 91.2 from the page top).
                if (cv.preGrownGridDoc) cv.marginTop = 72.0 + UaBodyMarginPt;
                if (chainWiden)
                {
                    cv.marginLeft = 90.0 + cv.bodyMarginLeftPt;
                    cv.marginRight = widenRight;
                }
                // The body's own right margin still insets the content box, mirroring
                // the left — the page margin alone sized the sheet.
                else if (inkWiden) cv.marginRight = widenRight + 6.0;
            }
        }

        // A declared table wider than the content box widens the page too: the
        // browser grows the canvas rather than squeezing a fixed-width table.
        // A table the natural-width probe already measured (and the page grew
        // for) must not re-widen the sheet — the stale second pass would also
        // capture the widened width as the page height.
        // The escaped-attr dialect's declared widths are JSON-mangled — they are
        // all ignored and the default page is kept.
        if (cv.declaredTableW > cv.pageWidth - cv.marginLeft - cv.marginRight
            && cv.declaredTableW > cv.widestTable && !cv.profile.escapedAttrDoc && cv.profile.bodyPinnedW <= 0)
        {
            cv.pageHeight = Math.Max(cv.pageWidth, cv.pageHeight);
            cv.pageWidth = cv.marginLeft + cv.declaredTableW + cv.marginRight;
        }

        // A `body { min-width: Npx }` floors the canvas itself, so the page widens to it
        // exactly as it widens to an over-wide table — the responsive-framework print
        // rule ("@media print { body { min-width: 992px !important } }") that pins a
        // desktop layout onto paper. A document that pins its own page margins keeps
        // its authored width.
        // ⚠ Read this off the STYLE BLOCKS, not the flattened rule map: the map holds
        // every sheet's rules with no medium attached, and a sheet the page links
        // `media="screen"` still styles the flow but never reaches paper — its print
        // at-rules must not size the sheet.
        if (!cv.marginsExplicit)
        {
            double bodyMinPt = 0;
            foreach (Match styleBlock in Regex.Matches(cv.html, @"<style\b([^>]*)>([\s\S]*?)</style\s*>",
                         RegexOptions.IgnoreCase))
            {
                var mediaM = Regex.Match(styleBlock.Groups[1].Value, @"\bmedia\s*=\s*[""']?([^""'>]*)",
                    RegexOptions.IgnoreCase);
                if (mediaM.Success && !Regex.IsMatch(mediaM.Groups[1].Value, @"\b(all|print)\b",
                        RegexOptions.IgnoreCase))
                    continue;
                foreach (Match br in Regex.Matches(styleBlock.Groups[2].Value,
                             @"(?<![\w.#-])body\s*\{([^{}]*)\}", RegexOptions.IgnoreCase))
                {
                    var mw = Regex.Match(br.Groups[1].Value,
                        @"\bmin-width\s*:\s*([\d.]+\s*(?:px|pt|in|cm|mm))", RegexOptions.IgnoreCase);
                    if (mw.Success && TryParseLength(mw.Groups[1].Value.Replace(" ", ""), out var mwPt)
                        && mwPt > bodyMinPt)
                        bodyMinPt = mwPt;
                }
            }
            if (bodyMinPt > cv.pageWidth - cv.marginLeft - cv.marginRight)
            {
                cv.pageHeight = Math.Max(cv.pageWidth, cv.pageHeight);
                cv.pageWidth = cv.marginLeft + bodyMinPt + cv.marginRight;
            }
        }

        // Flex-grid widen: a positioned page wrapper above the waybill grid
        // declares the sheet's content width in physical units (width: 8in) —
        // the page grows to margins + body inset + that width (measured 762 =
        // 90 + 6 + 576 + 90 on the table-flavoured waybill).
        if (!(cv.pageInfo?.WidthAssigned ?? false))
            foreach (var b in cv.blocks)
                if (b.Flex is { PageContentPt: > 0 } fgw)
                {
                    var fgNeedW = 90.0 + CardBodyPadPt + fgw.PageContentPt + 90.0;
                    if (fgNeedW > cv.pageWidth)
                    {
                        cv.pageHeight = Math.Max(cv.pageWidth, cv.pageHeight);
                        cv.pageWidth = fgNeedW;
                        cv.marginLeft = 90.0;
                        cv.marginRight = 90.0;
                        cv.marginTop = 72.0;
                    }
                }

        // Positioned-slide widen: the page grows to the slide's CONTENT EXTENT —
        // the rightmost absolutely positioned child's edge — inside the page and
        // UA body margins. The canvas min-width does NOT drive it (measured:
        // 878.25 = 90 + 6 + (403 + 520)px·0.75 + 90 on a 960px-min-width slide).
        if (!(cv.pageInfo?.WidthAssigned ?? false))
            foreach (var b in cv.blocks)
                if (b.Slide is { } slw)
                {
                    double slExtentPx = 0;
                    foreach (var it in slw.Items)
                        slExtentPx = Math.Max(slExtentPx, it.LeftPx + it.WPx);
                    if (slExtentPx > 0)
                    {
                        var slNeedW = 90.0 + CardBodyPadPt + slExtentPx * 0.75 + 90.0;
                        if (slNeedW > cv.pageWidth)
                        {
                            cv.pageHeight = Math.Max(cv.pageWidth, cv.pageHeight);
                            cv.pageWidth = slNeedW;
                            cv.marginLeft = 90.0;
                            cv.marginRight = 90.0;
                            cv.marginTop = 72.0;
                        }
                    }
                }

        // The engine's growth allowance when a page widens to natural content:
        // 0.1 in (7.2 pt = 9.6 px). Measured: the chart report's 622.0 page is
        // its minimal content fit 614.72 + 7.2 (rounded to the quarter-point
        // grid), and the zero-margin table pair's 602.5 is 595.28 + 7.2 the
        // same way.
        const double ChartWidenSlackPt = 7.2;

        // Edge-to-edge sheets with tables get the same growth allowance on the
        // authored page itself (595.28 + 7.2 → 602.5 on the quarter-point grid).
        // The engine's A4 basis is the true 595.276 — our 595.0 default page
        // stands in for it, so the widen resolves against the real sheet.
        if (cv.edgeToEdgeDoc && cv.blocks.Exists(b => b.IsTable))
        {
            const double A4TruePt = 210.0 / 25.4 * 72.0;   // 595.276
            var widenBase = Math.Abs(cv.pageWidth - 595.0) < 0.5 ? A4TruePt : cv.pageWidth;
            cv.pageWidth = Math.Round((widenBase + ChartWidenSlackPt) * 4.0,
                MidpointRounding.AwayFromZero) / 4.0;
        }
        // Chart-card widen: the page grows so the inline-SVG chart fits at its
        // NATURAL size inside its width-billing container chrome, plus the engine's
        // growth allowance, quantized to the quarter-point grid. Both constants are
        // measured on the expected output: the chart report widens to exactly
        // round4(90 + (svg 419.72 + col pads 15) + 7.2 + 90) = 622.0, and the
        // zero-margin table pair grows by the same 7.2 (602.5 = 595.28 + 7.22
        // rounded) — a 0.1 in allowance on the content's natural width.
        if (cv.profile.chartCardDoc && !(cv.pageInfo?.WidthAssigned ?? false))
        {
            double widestSvg = 0;
            foreach (var b in cv.blocks)
                if (b.IsImage && b.ImageWidth > 0
                    && b.ImageSrc.StartsWith("inline-svg:", StringComparison.Ordinal))
                    widestSvg = Math.Max(widestSvg, b.ImageWidth * 0.75 + b.ImageWidenPadPt);
            if (widestSvg > 0)
            {
                var neededW = Math.Round(
                    (cv.marginLeft + widestSvg + ChartWidenSlackPt + cv.marginRight) * 4.0,
                    MidpointRounding.AwayFromZero) / 4.0;
                if (neededW > cv.pageWidth)
                {
                    cv.pageHeight = Math.Max(cv.pageWidth, cv.pageHeight);
                    cv.pageWidth = neededW;
                }
            }
        }
        // The SSRS report opens at the raw 72 pt content top and flows to the
        // raw 72 pt bottom (measured: the first grid baseline seats at 86 =
        // 72 + the 2.05 mm spacer row + cell chrome, and page 1 runs to 753).
        return null;
    }
}
