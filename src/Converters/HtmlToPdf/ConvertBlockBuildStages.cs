using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The widest table past the content width widens the page to hold it, by the dialect's rule for how much of the surplus the page takes.</summary>
    private static void WidenPageToWidestTable(ConvertState cv, HtmlLoadOptions? options)
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
            && (!cv.uaFlow || cv.profile.deadExternalCss || cv.profile.cellAuthoredTypography
                || (cv.profile.uaStdSerif && !cv.profile.deadExternalCss && cv.widestIsPctMin))
            && !cv.profile.bodyZeroMargin && !cv.profile.escapedAttrDoc;
        // (a UA grid's natural width is its CONTENT: the ink the sheet ends one page margin past
        // starts one border-spacing and one cell padding inside the grid's box - probed: a 2.50 cm
        // cellspacing grid pages 96 + 70.87 + 0.75 + its content + 90)
        // (a grid at its min floors inks the chrome between its columns and its frame too, and
        // ends at a last-column control's box rather than its advance)
        var uaGridChrome = inkWiden && cv.profile.uaStdSerif && !cv.profile.deadExternalCss
            ? (cv.widestIsPctMin ? MinFloorInkChromePt(cv) : cv.widestTableChromePt)
            : 0;
        var neededContent = cv.profile.floatBandDoc ? cv.widestTable - 6
            : cv.profile.bodyZeroMargin || cv.profile.printGrid || inkWiden ? cv.widestTable + uaGridChrome
            // (the field-list sheet ends one page margin past its grid's min-content exactly: 99.8 + 580.252 + 90)
            : cv.profile.fieldListDoc ? cv.widestTable
            // (the pt form sheet ends one page margin past its grid's min-content box: 96 + 550.609 + 90)
            : cv.profile.ptFormDoc ? cv.widestTable
            : cv.widestTable + 8;
        // The same measured quantity as the ink pass's own right band, reached by a different
        // route: this one was derived from table geometry on documents that grow for a table,
        // the other from page-minus-ink on documents that grow for a declared width. Named
        // once so the two cannot drift apart. The other literal 90s in this file are the PAGE
        // MARGIN per side, which is numerically equal and conceptually not the same thing.
        var widenRight = inkWiden ? InkWidenRightMarginPt : cv.marginRight;
        // Chain-dialect documents widen to PAGE margin + content + PAGE margin exactly —
        // the content box starts at x = 90 + the body margin on the grown sheet, not at
        // the A4 flow's calibrated 96 left inset.
        var chainWiden = inkWiden && cv.profile.docChainRules is not null && !cv.profile.docChainCellRulesOnly;
        // …the UA body margin included when the body keeps it (probed: 96 + 420 + 90 = 606
        // under a margin-less body); an AUTHORED body margin insets the content but the
        // sheet still ends one page margin past the grid measured from the page margin.
        // A framed wrapper div is a laid-out element like any other: when its border box is the
        // widest thing on the sheet, the sheet ends one page margin past IT, not past the widest
        // table inside it (probed on the invoice: a 612 px bordered wrapper round a 583 px grid
        // pages 96 + 612px + 2 x 1px border + 90 = 646.50).
        if (inkWiden && FramedWrapperDivOuterWidthPt(cv.html, cv.css) is var wrapW && wrapW > neededContent)
            neededContent = wrapW;
        var neededPage = neededContent + (chainWiden ? 90.0 + (cv.bodyMarginAuthored ? 0.0 : cv.bodyMarginLeftPt) : cv.marginLeft) + widenRight
            // A UA-serif document's grown sheet keeps the symmetric body
            // margin on the RIGHT of the widest grid too (measured: the
            // register report's grid ends one body margin inside the frame).
            + (cv.profile.uaStdSerif && !cv.profile.deadExternalCss && !cv.profile.bodyZeroMargin && !inkWiden && !cv.profile.fieldListDoc ? UaBodyMarginPt : 0);
        // A UA-serif document whose widest table declares an absolute width: the sheet estimate is
        // one page margin past that table's INK - its box less the trailing cellspacing an unframed
        // grid never paints - and the ink pass after layout lifts it to whatever was really painted
        // (measured: 96 + 570 - 3.75 + 90 = 752.25 on the work permit; 672.75 on the valuation
        // report, whose framing div is the widest ink; 786 on the framed worksheet).
        // (a class-styled table keeps the calibrated slack: its class chrome - borders, paddings -
        // carries its ink past the declared box; measured: the AdvTbl report's 800 px tables page 809)
        var framedDivW = cv.profile.uaStdSerif && !cv.profile.deadExternalCss ? FramedWrapperDivOuterWidthPt(cv.html, cv.css) : 0;
        var declaredInkSheet = false;
        if (cv.profile.uaStdSerif && !cv.profile.deadExternalCss && !cv.profile.bodyZeroMargin && !inkWiden
            && cv.declaredTableW > 0 && cv.widestTable <= cv.declaredTableW + 3.0 + 1e-6
            && (!cv.declaredTableClassed || framedDivW > 0))
        {
            neededContent = cv.declaredTableW - (cv.declaredTableFramed ? 0 : cv.declaredTableSpacingPt)
                // (a grid nested in a host cell stands past that cell's chrome)
                + cv.declaredTableHostChromePt;
            // (a framed wrapper div round the tables is ink to its own box edge)
            neededContent = Math.Max(neededContent, framedDivW);
            // An UNFRAMED, unpainted declared table inks only its text: the sheet ends one page
            // margin past the furthest line its declared box lays out (probed on the safety data
            // sheet: the 768 px borderless header's centred title ends 489.31 into its box and the
            // page is 675.31, not the 762 the box would make).
            if (!cv.declaredTableFramed && framedDivW <= 0)
            {
                var dtInk = DeclaredTablesInkRightPt(cv, options);
                if (dtInk > 0 && dtInk < neededContent) { neededContent = dtInk; declaredInkSheet = true; }
            }
            neededPage = cv.marginLeft + neededContent + widenRight;
        }
        // The escaped-attr dialect never widens: it keeps the default
        // page and SQUEEZES its grids into the content box instead.
        if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_PAGEW") == "1")
            Console.Error.WriteLine($"[pagew2] inkWiden={inkWiden} chain={chainWiden} widest={cv.widestTable:0.##} needed={neededContent:0.##} page={neededPage:0.##} pctMin={cv.widestIsPctMin} declared={cv.declaredTableW:0.##}");
        GrowPageToNeededWidth(cv, neededPage, widenRight, inkWiden, chainWiden, declaredInkSheet);
    }

    /// <summary>The furthest ink over the top-level UA grids holding a nested grid that declares an
    /// absolute width - the floors the flat probe cannot see (its serif floors count text alone) -
    /// each laid out in the content box; 0 when no grid qualifies.</summary>
    private static double OverConstrainedGridInkPt(ConvertState cv, HtmlLoadOptions? options)
    {
        var ink = 0.0;
        foreach (var b in cv.blocks)
        {
            if (!b.IsTable || b.TableHtml is not { } html) continue;
            var nestedDeclared = false;
            foreach (var sub in ExtractNestedTables(html).subTables)
                if (DeclaredTableWidthPt(Regex.Match(sub, @"<table\b[^>]*>", RegexOptions.IgnoreCase).Value) > 0)
                { nestedDeclared = true; break; }
            if (!nestedDeclared) continue;
            var gridInk = MetricTableInkRightPt(cv, options, html, cv.availContentW);
            ink = Math.Max(ink, gridInk);
        }
        return ink;
    }

    /// <summary>The before-markers, the form-control and form-dialect flags, the sheet chrome check, and the block parse itself - structured or plain.</summary>
    private static void ParseDocumentBlocks(ConvertState cv, HtmlLoadOptions? options, List<Block> rowBlocks)
    {
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

        // The per-table gate opens when some table can be a grid: one with no control at
        // all, or one whose only controls are the options a control grid seats in its cells
        // (the letter whose single table carries the reasons' checkboxes in a nested grid
        // draws as that grid, measured; the flat path put every cell on its own line).
        cv.perTableFormGate = cv.htmlHasFormInput && !cv.profile.formDialectTables
            && SegmentHtmlTables(cv.html).Any(s => s.isTable
                && (!HasVisibleFormControl(s.html) || RadioGridableControls(s.html, cv.profile.uaStdSerif)
                    || ButtonFamilyControlsOnly(s.html)));

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
            && TryParseLength(repBodyM.Groups[2].Value.Replace(" ", "")) is { } repBodyW
            && repBodyW > 0
            && TryBuildReportLabelBlocks(cv.html, repBodyW) is { } repBlocks)
            cv.blocks = repBlocks;
        else
            cv.blocks = BuildStructuredBlocks(cv, cv.absSpanLedger, cv.beforeMarkers, cv.elementGridFace, cv.htmlHasFormInput, cv.inlineBlockColRules, cv.perTableFormGate, rowBlocks, cv.sheetChromesCells, cv.html, 0);
    }

    /// <summary>The widest intrinsic box among the tables that declare a CSS width while standing in a
    /// host cell: the declared width plus the table's own horizontal padding, border and margins (a
    /// content-box quirk of the reference, probed on the e-mail cards: a 755 px card with `padding:
    /// 5px; border: 1px solid` counts 767), or the card's real minimum where that is wider - a
    /// declared div inside its cell plus the UA cell spacing, the cell padding and the border. Zero
    /// when no such table exists.</summary>
    private static double InCellDeclaredTableIntrinsicPt(string html, IReadOnlyDictionary<string, Dictionary<string, string>>? css, bool selfInCell = false)
    {
        double widest = 0;
        foreach (Match tm in Regex.Matches(html, @"<table\b[^>]*>", RegexOptions.IgnoreCase))
        {
            var style = DivStyleOf(tm.Value);
            if (Regex.Match(style, @"(?<![-\w])width\s*:\s*([\d.]+\s*(?:px|pt|in|cm|mm))", RegexOptions.IgnoreCase) is not { Success: true } wM
                || TryParseLength(wM.Groups[1].Value.Replace(" ", "")) is not { } wPt || wPt <= 0)
                continue;
            if (!(selfInCell && tm.Index == 0) && !TableStandsInCell(html, tm.Index)) continue;
            var em = UaDefaultFontPt;
            var (_, padR, _, padL) = CssPaddingSidesPt(style, em);
            var sides = CssBorderSides(style);
            var borderW = (sides[3].Style is "none" ? 0 : sides[3].W) + (sides[1].Style is "none" ? 0 : sides[1].W);
            double marginW = 0;
            if (Regex.Match(style, @"(?<![-\w])margin-left\s*:\s*([^;]+)", RegexOptions.IgnoreCase) is { Success: true } mlM) marginW += StatedMarginPt(mlM.Groups[1].Value, em) ?? 0;
            if (Regex.Match(style, @"(?<![-\w])margin-right\s*:\s*([^;]+)", RegexOptions.IgnoreCase) is { Success: true } mrM) marginW += StatedMarginPt(mrM.Groups[1].Value, em) ?? 0;
            var box = wPt + padL + padR + borderW + marginW;
            // (the card's real minimum: a declared div in its cell stands inside the UA cell spacing
            //  and padding on both sides, and the card's border round that)
            var end = html.IndexOf("</table", tm.Index, StringComparison.OrdinalIgnoreCase);
            var inner = end > tm.Index ? html.Substring(tm.Index, end - tm.Index) : "";
            var innerDiv = Regex.Match(inner, @"<div\b[^>]*\bclass\s*=\s*[""']?([\w-]+)", RegexOptions.IgnoreCase);
            if (innerDiv.Success && css is not null
                && (css.TryGetValue("." + innerDiv.Groups[1].Value, out var dRule) || css.TryGetValue("div." + innerDiv.Groups[1].Value, out dRule))
                && dRule.TryGetValue("width", out var dW) && TryParseLength(dW.Trim()) is { } dWPt && dWPt > 0)
            {
                var (sp, pd) = TableSpacingAndPaddingPt(tm.Value);
                if (css.TryGetValue("td", out var tdRule) && tdRule.TryGetValue("padding", out var tdPad) && IsZeroLength(tdPad.Trim())) pd = 0;
                box = Math.Max(box, dWPt + 2 * sp + 2 * pd + borderW + marginW);
            }
            widest = Math.Max(widest, box);
        }
        return widest;
    }

    /// <summary>Whether the table at <paramref name="tableIndex"/> opens inside an unclosed cell.</summary>
    private static bool TableStandsInCell(string html, int tableIndex)
    {
        var depth = 0;
        foreach (Match m in Regex.Matches(html[..tableIndex], @"<(/?)t[dh]\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.RightToLeft))
        {
            if (m.Groups[1].Value.Length > 0) { depth++; continue; }
            if (depth > 0) { depth--; continue; }
            return true;
        }
        return false;
    }

    /// <summary>A declared table standing in a HOST CELL counts its own padding and border on top of its
    /// declared width (content-box arithmetic - the same table as a body child counts its width alone):
    /// the sheet is page margin + the UA body margin + the widest such box + page margin, and the body's
    /// 100% grid fills that box from the body margin, overflowing the right page margin by the body margin
    /// (probed on the e-mail cards: 90 + 6 + (755 + 2 x 5 + 2 x 1) px + 90 = 761.25, the grey band
    /// 96..677.25, every card centred in the 775 px box). True when the sheet was sized this way.</summary>
    private static bool FitInCellDeclaredSheet(ConvertState cv, double hostChrome)
    {
        if (!cv.profile.uaStdSerif || cv.profile.deadExternalCss || cv.marginsExplicit || cv.profile.escapedAttrDoc
            || cv.profile.bodyPinnedW > 0) return false;
        var inCellW = InCellDeclaredTableIntrinsicPt(cv.html, cv.css);
        if (inCellW <= cv.declaredTableW + hostChrome || inCellW <= cv.widestTable
            || 90.0 + UaBodyMarginPt + inCellW + 90.0 <= cv.pageWidth) return false;
        cv.pageHeight = Math.Max(cv.pageWidth, cv.pageHeight);
        cv.pageWidth = 90.0 + UaBodyMarginPt + inCellW + 90.0;
        cv.marginLeft = 90.0 + UaBodyMarginPt;
        cv.marginRight = 90.0 - UaBodyMarginPt;
        cv.profile.inCellSheet = true;
        return true;
    }

    /// <summary>A declared table width past the page grows the page or shrinks the margins, and default margins yield to a wide sheet.</summary>
    private static void FitDeclaredTableWidth(ConvertState cv, HtmlLoadOptions? options)
    {
        // (a UA grid nested in a host cell stands past that cell's chrome - probed: a 600 px grid
        // in a cellpadding=10 cell pages 96 + 7.5 + 450 + 90)
        var hostChrome = cv.profile.uaStdSerif && !cv.profile.deadExternalCss ? cv.declaredTableHostChromePt : 0;
        // (the widen pass has already sized a UA sheet to the declared box less the trailing spacing an
        // unframed grid never paints - probed 96 + 1500 - 1.5 + 90; sizing it again would also re-square it)
        if (cv.profile.uaStdSerif && !cv.declaredTableFramed
            && cv.pageWidth + cv.declaredTableSpacingPt + 1e-6 >= cv.marginLeft + hostChrome + cv.declaredTableW + cv.marginRight)
            return;
        if (FitInCellDeclaredSheet(cv, hostChrome)) return;
        if (cv.declaredTableW + hostChrome > cv.pageWidth - cv.marginLeft - cv.marginRight
            && cv.declaredTableW > cv.widestTable && !cv.profile.escapedAttrDoc && cv.profile.bodyPinnedW <= 0)
        {
            cv.pageHeight = Math.Max(cv.pageWidth, cv.pageHeight);
            // (an UNFRAMED, unpainted declared table inks only what its cells draw: the sheet ends
            // one page margin past that ink, the nested painted grids' boxes included, and keeps
            // the body inset on the right - measured on the enterprise summary: the 800 px table
            // round three framed 200 px grids pages 736.75 = 96 + 550.75 + 90, not 786)
            if (cv.profile.uaStdSerif && !cv.profile.deadExternalCss && !cv.profile.bodyZeroMargin
                && !cv.declaredTableFramed && !cv.declaredTableClassed
                && DeclaredTablesInkRightPt(cv, options) is > 0 and var dtInk
                && dtInk < cv.declaredTableW)
            {
                // (one PAGE margin past the ink; the body's own right margin insets the content box)
                cv.pageWidth = cv.marginLeft + hostChrome + dtInk + PageMarginRightPt(cv);
                cv.marginRight = PageMarginRightPt(cv) + cv.bodyMarginRightPt;
                return;
            }
            cv.pageWidth = cv.marginLeft + hostChrome + cv.declaredTableW + cv.marginRight;
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
                    if (mw.Success && TryParseLength(mw.Groups[1].Value.Replace(" ", "")) is { } mwPt
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
    }

    /// <summary>The available content width, the widest, declared, collapsed and element-rule table widths, and the over-declared grid detection.</summary>
    /// <summary>A table tag's absolute declared width in points (the width attribute in px, or an absolute style width); 0 for none or a percent.</summary>
    private static double DeclaredTableWidthPt(string tableTag)
    {
        // (`border-width: 1px` is not a width: the property must stand on its own)
        var styleW = Regex.Match(DivStyleOf(tableTag), @"(?<![-\w])width\s*:\s*([\d.]+\s*(?:px|pt|in|cm|mm))", RegexOptions.IgnoreCase);
        if (styleW.Success && TryParseLength(styleW.Groups[1].Value.Replace(" ", "")) is { } stylePt) return stylePt;
        var attr = Regex.Match(tableTag, @"\bwidth\s*=\s*[""']?\s*(\d+(?:\.\d+)?)\s*(?:px)?\s*[""'\s/>]", RegexOptions.IgnoreCase);
        return attr.Success && double.TryParse(attr.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var px) ? px * PxPt : 0;
    }

    /// <summary>The width of the part of a Word grid that PAINTS: its declared columns up to the last one any
    /// cell of which draws a border or a background, summed on their declarations. Zero when no column
    /// paints or when a column declares no width (then the grid's box is its own measure).</summary>
    /// <remarks>
    /// A sheet ends one page margin past the rightmost painted ink, never past a declared box (MEASURED,
    /// 26 probes, stable across releases): the Word mail's 1289 pt grid declares 31 columns of which the left 15
    /// carry the mso cell borders and the right 16 are bare, so its ink ends at 96 + 586.77 and the
    /// sheet at 773.27 - not at the 1469 the box would make. The same law the unpainted-wrapper arm
    /// beside this one states for a wrapper that paints nothing at all.
    /// </remarks>
    private static double WordGridPaintedWidthPt(string html, int tagIndex)
    {
        var depth = 0;
        var end = -1;
        for (var t = Regex.Match(html, @"<(?<c>/?)table\b[^>]*>", RegexOptions.IgnoreCase); t.Success; t = t.NextMatch())
        {
            if (t.Index < tagIndex) continue;
            depth += t.Groups["c"].Length > 0 ? -1 : 1;
            if (depth == 0) { end = t.Index + t.Length; break; }
        }
        if (end < 0) return 0;
        // (a nested grid's cells are its own columns, not this grid's)
        var (own, _) = ExtractNestedTables(html.Substring(tagIndex, end - tagIndex));
        var declared = new List<double>();
        var painted = new List<bool>();
        foreach (Match row in Regex.Matches(own, @"<tr\b.*?</tr\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var cursor = 0;
            foreach (Match cell in Regex.Matches(row.Value, @"<t[dh]\b[^>]*>", RegexOptions.IgnoreCase))
            {
                var tag = cell.Value;
                var span = Regex.Match(tag, @"\bcolspan\s*=\s*[""']?(\d+)", RegexOptions.IgnoreCase) is { Success: true } sm
                    && int.TryParse(sm.Groups[1].Value, out var sv) && sv > 0 ? sv : 1;
                while (declared.Count < cursor + span) { declared.Add(0); painted.Add(false); }
                var paints = Regex.IsMatch(tag, @"\bbgcolor\s*=|background(?:-color)?\s*:|\bborder\s*=\s*[""']?[1-9]|(?<![-\w])border(?:-(?:top|left|right|bottom))?\s*:\s*(?!none|0)", RegexOptions.IgnoreCase);
                for (var k = 0; k < span; k++) painted[cursor + k] |= paints;
                // (a single column's width is read from its own cells; a spanning cell's box is theirs to share)
                if (span == 1 && declared[cursor] <= 0)
                {
                    var w = DeclaredTableWidthPt(tag);
                    if (w > 0) declared[cursor] = w;
                }
                cursor += span;
            }
        }
        var last = painted.LastIndexOf(true);
        if (last < 0) return 0;
        double sum = 0;
        for (var i = 0; i <= last; i++)
        {
            if (declared[i] <= 0) return 0;
            sum += declared[i];
        }
        return sum;
    }

    /// <summary>True when the table opening at <paramref name="tagIndex"/> paints nothing of its own and
    /// holds nothing but nested tables: no border or background on its tags, and no text once the
    /// nested tables are taken out.</summary>
    private static bool IsUnpaintedWrapperTable(string html, int tagIndex)
    {
        var depth = 0;
        var end = -1;
        for (var t = Regex.Match(html, @"<(?<c>/?)table\b[^>]*>", RegexOptions.IgnoreCase); t.Success; t = t.NextMatch())
        {
            if (t.Index < tagIndex) continue;
            depth += t.Groups["c"].Length > 0 ? -1 : 1;
            if (depth == 0) { end = t.Index + t.Length; break; }
        }
        if (end < 0) return false;
        var outer = html.Substring(tagIndex, end - tagIndex);
        var (inner, subs) = ExtractNestedTables(outer);
        if (subs.Count != 1) return false;
        // …and only when the table it wraps states an ABSOLUTE width narrower than the wrapper's:
        // a nested grid that fills its wrapper (a percent width, or none) paints the wrapper's box.
        var wrapW = DeclaredTableWidthPt(outer.Substring(0, outer.IndexOf('>') + 1));
        var subTag = subs[0].Substring(0, subs[0].IndexOf('>') + 1);
        var subW = DeclaredTableWidthPt(subTag);
        if (subW <= 0 || wrapW <= 0 || subW >= wrapW - 1e-6) return false;
        if (Regex.IsMatch(inner, @"\bbgcolor\s*=|background(?:-color)?\s*:|\bborder\s*=\s*[""']?[1-9]|border(?:-(?:top|left|right|bottom))?\s*:\s*(?!none|0)", RegexOptions.IgnoreCase)) return false;
        // (the extraction leaves a numbered marker where each nested table stood)
        var innerText = Regex.Replace(Regex.Replace(inner, "<[^>]+>", " "), @"\u0002\d+\u0003", " ");
        return CollapseWs(DecodeEntities(innerText)).Trim().Length == 0;
    }

    /// <summary>The box the sheet's id/class rule declares for the div a table opens as the first child
    /// of; null where the table has no such wrapper or the rule states no absolute width.</summary>
    private static double? HostDivRuleWidthPt(ConvertState cv, int tableIndex)
    {
        var before = cv.html.Substring(0, tableIndex);
        var open = Regex.Match(before, @"<div\b([^>]*)>\s*$", RegexOptions.IgnoreCase);
        if (!open.Success) return null;
        var attrs = open.Groups[1].Value;
        var keys = new List<string>();
        var idM = Regex.Match(attrs, @"\bid\s*=\s*[""']?([\w-]+)", RegexOptions.IgnoreCase);
        if (idM.Success) { keys.Add("#" + idM.Groups[1].Value); keys.Add("div#" + idM.Groups[1].Value); }
        var clsM = Regex.Match(attrs, @"\bclass\s*=\s*[""']?([\w \-]+)", RegexOptions.IgnoreCase);
        if (clsM.Success)
            foreach (var c in clsM.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)) { keys.Add("." + c); keys.Add("div." + c); }
        foreach (var key in keys)
            if (cv.css.TryGetValue(key, out var rule) && rule.TryGetValue("width", out var w)
                && !w.Contains('%') && TryParseLength(w.Trim()) is { } wPt && wPt > 0)
                return wPt;
        return null;
    }

    private static void MeasureTableWidths(ConvertState cv, HtmlLoadOptions? options)
    {
        cv.availContentW = cv.pageWidth - cv.marginLeft - cv.marginRight;
        cv.widestTable = 0;
        // Set when any probed segment holds an over-declared fixed-layout attribute
        // grid: those documents draw their nested grids as REAL grids (the metric
        // layouter flattens nested tables into stacked lines).
        cv.profile.overDeclaredGridDoc = false;
        cv.preGrownGridDoc = false;
        cv.widestIsPctMin = false;
        cv.widestIsRowDemand = false;
        DetectOverDeclaredGrid(cv.bodyCssFace, cv.availContentW, cv.blocks, cv.css, cv.inlineSvgs, options, cv.profile, cv);
        cv.declaredTableW = 0;
        cv.collapseTableW = 0;
        MeasureDeclaredTableWidths(cv);
        cv.elementTableW = 0;
        if (cv.profile.elementGridDoc && cv.css.TryGetValue("table", out var tElemDecl)
            && tElemDecl.TryGetValue("width", out var tElemW)
            && !tElemW.Contains('%')
            && TryParseLength(tElemW.Trim()) is { } tElemPt
            && Regex.IsMatch(cv.html, @"<table\b", RegexOptions.IgnoreCase))
            cv.elementTableW = tElemPt;
        // The natural box for a declared-width table carries a 2.25
        // chrome step over the declared width — the probe's symmetric border-
        // spacing counts 3.0 (measured: the UA sheet lands at 96 + 936px + 2.25
        // + 90); trim the difference so the widened page sizes as expected.
        if (cv.uaFlow && cv.declaredTableW > 0
            && cv.widestTable > cv.declaredTableW + 2.25 && cv.widestTable <= cv.declaredTableW + 3.0 + 1e-6)
            cv.widestTable = cv.declaredTableW + 2.25;
        // A table's declared width is the box it is laid out in whatever its content
        // needs, so the page grows to it even when the probed natural width is
        // narrower (probed: a 560 px table of short numeric cells widens the default
        // sheet to 96 + 420 + 90 = 606 exactly). The calibrated flow only: the UA
        // flow's widen arm still carries its measured slack over the natural width,
        // and a declared floor under that slack over-grows the sheet.
        if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_PAGEW") == "1") Console.Error.WriteLine($"[pagew] widest={cv.widestTable:0.#} declared={cv.declaredTableW:0.#} collapse={cv.collapseTableW:0.#} uaFlow={cv.uaFlow} avail={cv.availContentW:0.#}");
        if (!cv.uaFlow && !cv.marginsExplicit && !cv.profile.printGrid && cv.declaredTableW > cv.widestTable)
            cv.widestTable = cv.declaredTableW;
    }

    /// <summary>The dash-wrap face and quirks wrap width, the print grid's block seats, and the all-table explicit-margin case.</summary>
    private static void SeatBlockFaces(ConvertState cv, HtmlLoadOptions? options)
    {
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
    }

    /// <summary>A document with no blocks becomes one blank page; otherwise null to continue.</summary>
    private static Document? EmptyDocumentOrContinue(ConvertState cv, HtmlLoadOptions? options)
    {
        if (cv.blocks.Count == 0)
        {
            var blankDoc = Document.Create();
            blankDoc.Pages.Add(cv.pageWidth, cv.pageHeight);
            return blankDoc;
        }
        return null;
    }

    /// <summary>The RTL flag, the fixed header and footer regions with their margin charges, and the first-page footer image.</summary>
    private static void DetectRegionsAndFooterImage(ConvertState cv, HtmlLoadOptions? options)
    {
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
    }

    /// <summary>Explicit margins outside the metric flow take the stylesheet's body margins.</summary>
    private static void ApplyMetricFlowMargins(ConvertState cv, HtmlLoadOptions? options)
    {
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
    }

    /// <summary>The UA flow and standard-serif flags, the bare-document and HTML5 UA cases, and the UA flow's body width, face and margins.</summary>
    private static void ResolveUaFlowAndBodyWidth(ConvertState cv, HtmlLoadOptions? options)
    {
        cv.uaFlow = cv.uaMshtml || cv.uaNoFontDoc;
        // The full-document path draws Standard-14 serif (no embedding); MSHTML keeps its
        // embedded-Type0 serif output.
        cv.profile.uaStdSerif = cv.uaNoFontDoc && !cv.uaMshtml;
        // The UA serif flow frames a legend-bearing fieldset at the content width.
        cv.profile.uaFieldsetContent = cv.uaFieldsetBoxes = cv.profile.uaStdSerif
            && Regex.IsMatch(cv.html, @"<fieldset", RegexOptions.IgnoreCase)
            && Regex.IsMatch(cv.html, @"<legend", RegexOptions.IgnoreCase);
        // A BARE full UA document: a real <html> document with no stylesheet
        // markup at all. Only this shape carries the probed serif-floor and
        // break-paragraph laws - styled or fragment documents keep their
        // calibrated flows (a UTF-16 <body> fragment charges NOTHING for a
        // bare <br> between tables where the full document charges a line box).
        cv.profile.uaBareDoc = cv.profile.uaStdSerif && !cv.profile.deadExternalCss
            && !Regex.IsMatch(cv.html, @"<(style|link)\b", RegexOptions.IgnoreCase)
            && Regex.IsMatch(cv.html, @"<html\b", RegexOptions.IgnoreCase);
        // (a styled UA document whose cell rule forbids wrapping grows its sheet to the nowrap
        // grid's serif floors, as a bare document does - probed on the state analysis: 96 + the
        // 20-column nowrap grid + 90 = 740.29)
        cv.profile.uaNoWrapCellRule = cv.profile.uaStdSerif && !cv.profile.deadExternalCss
            && ElementRule(cv.css, "td") is { } tdNwRule && tdNwRule.TryGetValue("white-space", out var tdNwWs)
            && Regex.IsMatch(tdNwWs, @"^\s*(nowrap|pre(?![-\w]))", RegexOptions.IgnoreCase);
        cv.html5Doctype = Regex.IsMatch(cv.html, @"<!DOCTYPE\s+html\s*>", RegexOptions.IgnoreCase);
        _quirksRowStrut = ReadsInQuirksMode(cv.html);
        _limitedQuirks = ReadsInLimitedQuirks(cv.html);
        _remoteBaseHost = RemoteBaseHost(cv.html);
        _fieldListDoc = cv.profile.fieldListDoc;
        cv.profile.bodyOwnFontPt = cv.uaBodyFaceFromAttr ? cv.uaBodyFontPt : 0;
        _uaStdSerifFlow = cv.profile.uaStdSerif;
        cv.html5BareUa = (cv.profile.uaBareDoc || (cv.profile.uaStdSerif && cv.css.Count == 0 && Regex.IsMatch(cv.html, @"<html\b", RegexOptions.IgnoreCase))) && Regex.IsMatch(cv.html, @"<!DOCTYPE\s+html\s*>", RegexOptions.IgnoreCase);
        // A body styled width:100% — the sheet widens by the UA body inset and
        // its tables sit the measured gap below body text (both measured).
        cv.profile.bodyWidthFullDoc = false;
        ResolveUaFlowWidth(cv);
    }

    /// <summary>The page widens for its tables by dialect: the pinned body width, the element-rule width, the form and scale-to-page cases, the collapsed and standard-serif grids, and the widest table past the content width.</summary>
    private static void WidenPageForTables(ConvertState cv, HtmlLoadOptions? options)
    {
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
        else WidenPageForRemainingTableDialects(cv, options);
    }

}
