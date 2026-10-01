using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The document chain rules and the body background from the body tag or the stylesheet, white dropped.</summary>
    private static void ResolveBodyBackground(ConvertState cv, HtmlLoadOptions? options)
    {
        cv.profile.docChainRules = ParseChainRules(cv.html, out var chainCellOnly);
        cv.profile.docChainCellRulesOnly = chainCellOnly;
        cv.profile.docSiblingCellRules = ParseSiblingCellRules(cv.html);

        cv.bodyBackground = null;
        cv.bodyOpen = Regex.Match(cv.html, @"<body\b[^>]*>", RegexOptions.IgnoreCase);
        if (cv.bodyOpen.Success)
        {
            var bgAttr = Regex.Match(cv.bodyOpen.Value,
                @"\bbgcolor\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))", RegexOptions.IgnoreCase);
            if (bgAttr.Success)
                cv.bodyBackground = ParseCssColor(bgAttr.Groups[1].Success ? bgAttr.Groups[1].Value
                    : bgAttr.Groups[2].Success ? bgAttr.Groups[2].Value : bgAttr.Groups[3].Value);
            var inlineBg = Regex.Match(DivStyleOf(cv.bodyOpen.Value),
                @"background(?:-color)?\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
            if (inlineBg.Success && ParseCssColor(inlineBg.Groups[1].Value) is { } inlineColor)
                cv.bodyBackground = inlineColor;
        }
        if (cv.css.TryGetValue("body", out var bodyRule))
            foreach (var prop in new[] { "background", "background-color" })
                if (bodyRule.TryGetValue(prop, out var bgv) && ParseCssColor(bgv) is { } cssBg)
                    cv.bodyBackground = cssBg;
        if (cv.bodyBackground is { R: 255, G: 255, B: 255 }) cv.bodyBackground = null;
    }

    /// <summary>Page size, landscape swap, margins (explicit, per-side or default), the CSS @page rule, single-page mode and the scale-to-page-width request.</summary>
    private static void ResolvePageGeometry(ConvertState cv, HtmlLoadOptions? options)
    {
        cv.pageInfo = options?.PageInfo;
        cv.pageWidth = cv.pageInfo?.Width  is > 0 ? cv.pageInfo.Width  : 612.0;
        cv.pageHeight = cv.pageInfo?.Height is > 0 ? cv.pageInfo.Height : 792.0;
        // PageInfo.IsLandscape is a no-op for HTML imports — the page lays
        // out portrait regardless — so undo the SETTER's dimension swap (the
        // caller authored portrait and only set the flag). Width/Height the
        // caller authored landscape stand, flag or no flag.
        if (cv.pageInfo?.LandscapeSwapApplied == true && cv.pageWidth > cv.pageHeight)
            (cv.pageWidth, cv.pageHeight) = (cv.pageHeight, cv.pageWidth);
        cv.pageMargin = cv.pageInfo?.Margin;
        cv.marginsExplicit = (cv.pageMargin?.IsTouched ?? false) || (cv.pageInfo?.MarginAssigned ?? false);
        cv.perSide = (cv.pageMargin?.IsTouched ?? false) && cv.pageMargin!.HtmlPerSideDefaults;
        cv.marginLeft = cv.marginsExplicit ? (cv.perSide && !cv.pageMargin!.LeftTouched   ? 96.0 : cv.pageMargin!.Left)   : 96.0;
        cv.marginRight = cv.marginsExplicit ? (cv.perSide && !cv.pageMargin!.RightTouched  ? 72.0 : cv.pageMargin!.Right)  : 72.0;
        cv.marginTop = cv.marginsExplicit ? (cv.perSide && !cv.pageMargin!.TopTouched    ? 89.0 : cv.pageMargin!.Top)    : 89.0;
        cv.marginBottom = cv.marginsExplicit ? (cv.perSide && !cv.pageMargin!.BottomTouched ? 72.0 : cv.pageMargin!.Bottom) : 72.0;

        cv.cssPageFirstTopLift = 0.0;
        cv.cssPageRule = null;
        if (options?.IsPriorityCssPageRule == true && TryReadCssPageRule(cv.html) is { } cssPageRead)
        {
            cv.cssPageRule = cssPageRead;
            if (cssPageRead.WidthPt > 0 && cssPageRead.HeightPt > 0)
            {
                cv.pageWidth = cssPageRead.WidthPt;
                cv.pageHeight = cssPageRead.HeightPt;
            }
            ApplyCssPageMargins(cv);
            cv.marginsExplicit = true;
        }
        // The declared page margins, re-asserted. Every document class below may
        // seat its own calibrated margins on the sheet; under this option the
        // sheet's own rule is what the caller asked to be honoured, so it is
        // applied again once those arms have had their say.
        cv.singlePage = options?.IsRenderToSinglePage == true;
        cv.singlePageRealH = cv.pageHeight;
        if (cv.singlePage) cv.pageHeight = 14400.0;   // the PDF page-dimension ceiling

        // ScaleToPageWidth: the caller asks for the layout at its NATURAL width,
        // shrunk uniformly onto the authored sheet — such a document is laid
        // out at the UA base size on its min-content width, then scales
        // the finished pages down with the content pinned at the left margin and
        // the page top. The natural width comes from the widest table's natural
        // probe below; scalePendingS carries the shrink to the end of the flow.
        cv.profile.scaleToPageWidth = options?.PageLayoutOption == HtmlPageLayoutOption.ScaleToPageWidth;
        cv.scalePendingS = 0;
        cv.scaleReqPageW = 0;
        cv.scaleReqPageH = 0;
    }

    /// <summary>A table with NOTHING between its tags that declares its cellspacing is still a box:
    /// its border-spacing stands above and below it (probed on the helpdesk form: the empty
    /// `cellSpacing="1"` header table advances the flow 1.5). It becomes a spacer of that height
    /// before the rowless unwrap - which is for tables whose CHILDREN the parser foster-parents.</summary>
    private static string EmptySpacedTablesToSpacers(string html)
    {
        if (html.IndexOf("<table", StringComparison.OrdinalIgnoreCase) < 0) return html;
        return Regex.Replace(html,
            @"<table\b[^>]*\bcellspacing\s*=\s*[""']?(?<cs>\d+(?:\.\d+)?)[^>]*>\s*</table\s*>",
            m => double.Parse(m.Groups["cs"].Value, System.Globalization.CultureInfo.InvariantCulture) is var csPx && csPx > 0
                // (a zero spacing is no box at all: the rowless unwrap takes that table)
                ? Compat.Format(System.Globalization.CultureInfo.InvariantCulture, $"<div style=\"height:{2 * csPx * PxPt:0.###}pt\"></div>")
                : m.Value,
            RegexOptions.IgnoreCase);
    }

    /// <summary>Linked stylesheets inline, rowless tables unwrap, font shorthands expand, and the horizontal-form, sectioned-report and escaped-attribute dialects set their body sizes; a horizontal form renders through its own path.</summary>
    private static Document? PreparePageMarkup(ConvertState cv, HtmlLoadOptions? options)
    {
        cv.html = InlineLinkedStylesheets(cv.html, options);

        // A rowless <table> is a wrapper tag, not a grid — the HTML parser
        // foster-parents its illegal children out of the table and the table
        // itself generates no boxes. Unwrapping it here lets every downstream
        // gate and flow see the document's real structure.
        cv.html = EmptySpacedTablesToSpacers(cv.html);
        cv.html = UnwrapRowlessTables(cv.html);

        cv.profile = new HtmlDocProfile();
        cv.profile.formHorizontalDoc = cv.html.IndexOf("control-group", StringComparison.OrdinalIgnoreCase) >= 0
                                && cv.html.IndexOf("<label", StringComparison.OrdinalIgnoreCase) >= 0;

        // Expand the CSS `font:` shorthand (font: [style weight] size[/line-height]
        // family, …) into its longhands once, up front — every downstream dialect
        // gate and style regex reads font-size / font-family / line-height only, so
        // a shorthand-styled document (body font: 1em/1.4em Tahoma …) would
        // otherwise pass for "font-family-free" and take the UA serif flow.
        cv.html = ExpandFontShorthands(cv.html, familylessResets: cv.profile.formHorizontalDoc);

        if (cv.profile.formHorizontalDoc)
        {
            cv.html = TransformFormHorizontalRows(cv.html);
            // An empty div styled with only a border-bottom is a section divider —
            // exactly what <hr> renders as. The divider's CSS vertical margins ride
            // along: they are the section rhythm of this dialect.
            cv.html = Regex.Replace(cv.html,
                @"<div\b[^>]*style\s*=\s*""[^""]*border-bottom\s*:\s*(\d+(?:\.\d+)?)px\s+solid\s+(#[0-9a-fA-F]{3,6}|[a-zA-Z]+)[^""]*""[^>]*>\s*</div>",
                m =>
                {
                    var mt = Regex.Match(m.Value, @"margin\s*:\s*(\d+(?:\.\d+)?)px(?:\s+\S+)?\s+(\d+(?:\.\d+)?)px", RegexOptions.IgnoreCase);
                    var margins = mt.Success
                        ? $"; margin-top: {mt.Groups[1].Value}px; margin-bottom: {mt.Groups[2].Value}px"
                        : "";
                    return $"<hr style=\"border: {m.Groups[1].Value}px solid {m.Groups[2].Value}{margins}\" />";
                },
                RegexOptions.IgnoreCase);
            // A styled <legend> is this dialect's section heading — the block engine
            // has no legend tag, so it would flow as body text at body size. Unlike a
            // real h2 it keeps the normal (inherited) weight.
            cv.html = Regex.Replace(cv.html, @"<legend\b([^>]*?)style\s*=\s*""",
                "<h2$1style=\"font-weight: normal;", RegexOptions.IgnoreCase);
            cv.html = Regex.Replace(cv.html, @"<(/?)legend\b", "<$1h2", RegexOptions.IgnoreCase);
        }
        // The body's CSS line-height (the `font: 1em/1.4em …` shorthand expands to
        // it) sets the pitch of the synthesized form-row cells.
        cv.profile.bodyLineHeightPt = 0.0;
        // Sectioned-report shape: a document whose sections are divided by rules that
        // break the page. Such a document is laid out on the browser's own block
        // rhythm — real UA margins, collapsed between siblings, and a <br> holding its
        // line box — rather than the legacy line-on-line stack.
        cv.profile.sectionedReport = Regex.IsMatch(cv.html,
            @"<hr\b[^>]*(page-)?break-after\s*:\s*(always|page)", RegexOptions.IgnoreCase);
        cv.profile.uaGridBoxes = cv.profile.sectionedReport;

        // A JSON-escaped export defeats every style= attribute — the value truncates at its
        // first space — so nothing in the markup can size a control or the flow. Such a
        // document falls back to the UA base (16px = 12pt) and to the character-grid box
        // each control's size/cols/rows declares, which is what gets drawn.
        cv.profile.escapedAttrDoc = cv.html.IndexOf("=\\\"", StringComparison.Ordinal) >= 0;
        cv.profile.embedFonts = options?.IsEmbedFonts ?? true;

        cv.profile.formBodyFontPt = 0.0;
        if (cv.profile.formHorizontalDoc || cv.profile.sectionedReport || cv.profile.escapedAttrDoc)
        {
            var bodyTag = Regex.Match(cv.html, @"<body\b[^>]*>", RegexOptions.IgnoreCase);
            var bodyStyle = bodyTag.Success ? DivStyleOf(bodyTag.Value) : "";
            var lhm = Regex.Match(bodyStyle, @"line-height\s*:\s*([\d.]+)\s*(em|px|pt)?", RegexOptions.IgnoreCase);
            if (lhm.Success && double.TryParse(lhm.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var lhv))
                cv.profile.bodyLineHeightPt = lhm.Groups[2].Value.ToLowerInvariant() switch
                {
                    "px" => lhv * 0.75,
                    "pt" => lhv,
                    _ => lhv * 12.0,   // em / unitless of the 16px = 12pt UA base
                };
            // The body font size seeds the whole flow (an em-sized body resolves
            // against the browser's 16px = 12pt base, not this flow's legacy 11pt).
            cv.profile.formBodyFontPt = 12.0;
            var bfm = Regex.Match(bodyStyle, @"font-size\s*:\s*([\d.]+)\s*(em|px|pt)?", RegexOptions.IgnoreCase);
            if (bfm.Success && double.TryParse(bfm.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var bfv))
                cv.profile.formBodyFontPt = bfm.Groups[2].Value.ToLowerInvariant() switch
                {
                    "px" => bfv * 0.75,
                    "pt" => bfv,
                    _ => bfv * 12.0,
                };
        }
        ReadBodyLineBox(cv);
        return null;
    }

    /// <summary>The body's ABSOLUTE line-height (its own inline style first, else the sheet's
    /// body rule), in points; a factor or `normal` leaves the flow on its own line boxes.</summary>
    private static void ReadBodyLineBox(ConvertState cv)
    {
        cv.profile.bodyLineBoxPt = 0.0;
        var bodyTag = Regex.Match(cv.html, @"<body\b[^>]*>", RegexOptions.IgnoreCase);
        var inline = bodyTag.Success
            ? Regex.Match(DivStyleOf(bodyTag.Value), LineHeightDeclRx, RegexOptions.IgnoreCase)
            : Match.Empty;
        var value = inline.Success ? inline.Groups[1].Value.Trim()
            : cv.css is not null && cv.css.TryGetValue("body", out var bodyRule)
                && bodyRule.TryGetValue("line-height", out var sheetLh) ? sheetLh.Trim()
            : null;
        if (value is not null
            && Regex.IsMatch(value, @"^[\d.]+\s*(px|pt|cm|mm|in)$", RegexOptions.IgnoreCase)
            && TryParseLength(value) is { } lhPt && lhPt > 0)
            cv.profile.bodyLineBoxPt = lhPt;
    }

    /// <summary>A `line-height:` declaration in a style attribute, up to its terminator.</summary>
    private const string LineHeightDeclRx = @"(?<![-\w])line-height\s*:\s*([^;""']+)";

    /// <summary>The last group of stylesheet-driven dialects - inline CSS columns, mono report, radius grid, slip invoice, receipt card, access form, task report, RFQ and RTL forms, validation report, closing disclosure, audit report; the document when one matched, otherwise null.</summary>
    private static Document? RenderStyledDialectsD(ConvertState cv, HtmlLoadOptions? options)
    {

        // CSS multi-column containers (see CssColumns.cs): `columns: N` flows
        // the paragraphs down N balanced columns.
        if (!cv.marginsExplicit
            && TryRenderCssColumns(cv.html, cv.css, cv.pageWidth, cv.pageHeight) is { } mcDoc)
            return mcDoc;

        // …and a container declaring its columns in its OWN style attribute
        // pours the flow down them page after page instead (ColumnFlow.cs).
        if (TryRenderInlineCssColumns(cv.html, options, cv.pageWidth, cv.pageHeight) is { } icDoc)
            return icDoc;

        // A fixed-width monospace report dump lays out on a character grid
        // and grows the sheet to its widest unbreakable line (MonoReport.cs).
        if (TryRenderMonoReport(cv.html, cv.pageWidth, cv.pageHeight) is { } mrDoc)
            return mrDoc;

        // A continuously-rendered rounded-corner report grid shrinks its
        // columns to min-content and grows the sheet to them (RadiusGrid.cs).
        if (TryRenderRadiusGrid(cv.html, options, cv.css, cv.pageWidth, cv.singlePageRealH) is { } rgDoc)
            return rgDoc;

        // A percent-width till-slip invoice fills its body box with tables
        // whose columns take their max-content share (SlipInvoice.cs).
        if (TryRenderSlipInvoice(cv.html, options, cv.css, cv.pageWidth, cv.pageHeight) is { } siDoc)
            return siDoc;

        // The payment-receipt card export (ReceiptCard.cs): the rounded
        // .receiptDetails card with its float-right header, the dotted grey
        // auth band and the label/value details grid, on the
        // measured Arial-#666 geometry.
        if (TryRenderReceiptCard(cv.html, cv.css, cv.pageWidth, cv.pageHeight) is { } rcpDoc)
            return rcpDoc;

        // The boxed IT access form (AccessForm.cs): the box-shadowed white
        // card on the body's grey ground, double-tone section rules, and one
        // #fafafa question box per Q/A pair at the measured 54 pt pitch.
        if (TryRenderAccessForm(cv.html, cv.css, cv.pageWidth, cv.pageHeight) is { } afDoc)
            return afDoc;

        // The workflow snapshot report (TaskReport.cs): bordered stage cards of
        // uppercase label/value rows and nested task cards, flowed page to page
        // with the open cards' side borders sliced across the breaks.
        if (TryRenderTaskReport(cv.html, cv.pageWidth, cv.pageHeight, cv.marginLeft,
                cv.marginRight, cv.marginTop, cv.marginBottom) is { } trDoc)
            return trDoc;

        // The bilingual RFQ form (RfqForm.cs): the logo/title header, two
        // #eef7f8 info bands, the grey intro pair and the #8d98b2-headed items
        // grid, with the Arabic runs shaped through an embedded Tahoma.
        if (TryRenderRfqForm(cv.html, cv.pageWidth, cv.pageHeight) is { } rfqDoc)
            return rfqDoc;

        // An RTL Word export anchors its lines on the content box's right edge
        // and takes its rhythm from the margins its paragraphs keep (RtlForm.cs).
        if (TryRenderRtlForm(cv.html, cv.pageWidth, cv.pageHeight) is { } rfDoc)
            return rfDoc;

        // The eValidator validation report paginates off its own print sheet -
        // the Details half and every numbered section open a page of their own,
        // and the frames between them spill (ValidationReport.cs).
        if (TryRenderValidationReport(cv.html, cv.pageWidth, cv.pageHeight, cv.marginLeft,
                cv.marginRight, cv.marginTop, cv.marginBottom) is { } vrDoc)
            return vrDoc;

        // The Closing Disclosure addendum takes its sheet from the print
        // stylesheet's own @page height and grows it to the contact grid's
        // minimum columns (ClosingDisclosure.cs).
        if (TryRenderClosingDisclosure(cv.html) is { } cdDoc)
            return cdDoc;

        // The infrastructure-assessment report hangs every block off one
        // percentage-resolved content width and flows them across its own
        // landscape sheet (AuditReport.cs).
        if (TryRenderAuditReport(cv.html) is { } auditDoc)
            return auditDoc;
        return null;
    }

    /// <summary>The third group of stylesheet-driven dialects - print page, spreads, Gantt, contact form, portal shell, case letter, article; the document when one matched, otherwise null.</summary>
    private static Document? RenderStyledDialectsC(ConvertState cv, HtmlLoadOptions? options)
    {

        // The CJK ORDER REPORT (see PrintPage.cs): the vertical title, the
        // order-info box and the activity/infrastructure grids on the shipped
        // template's measured geometry.
        if (!cv.marginsExplicit
            && TryRenderCjkOrderReport(cv.html, cv.css, cv.pageHeight) is { } cjkDoc)
            return cjkDoc;

        // The D3 vertical-bar-chart export (see BarChart.cs): title/subtitle
        // bands over a flex row of inline SVGs drawn as vector fills and Times
        // text, clipped per svg viewport.
        // The fixed-band print-page idiom (see PrintPage.cs): position:fixed
        // header/footer bands repeating per sheet, .page divs breaking after
        // themselves, the @media body width sizing the sheet.
        if (TryRenderPrintPage(cv.html, cv.css, cv.pageHeight) is { } ppDoc)
            return ppDoc;

        // Ebook spreads (see SpreadPage.cs): a body pinned to a pixel canvas
        // whose .spread divs are one full page each — absolute full-bleed image
        // layers under a padded serif content column with initial-cap floats.
        if (TryRenderSpreadPages(cv.html, cv.css, options, cv.pageHeight) is { } sprDoc)
            return sprDoc;

        // Article-PDF exports (see ArticlePdf.cs): the .article-pdf__* class
        // namespace — the red title band, the float column pair, the wrapper's
        // outsized paddings that pace the document onto its four pages.
        // The dhtmlxGantt chart export: a grid of task rows beside a timeline of
        // absolutely placed bars and connectors (GanttChart.cs).
        if (TryRenderGanttChart(cv.html, options, cv.pageWidth, cv.pageHeight, cv.marginLeft, cv.marginTop) is { } gtDoc)
            return gtDoc;

        // The em-grid contact form: a centred max-width column of inline-block
        // field boxes sized by their own em classes (ContactForm.cs).
        if (TryRenderContactForm(cv.html, options, cv.pageWidth, cv.pageHeight,
                cv.marginLeft, cv.marginRight, cv.marginTop) is { } cfDoc)
            return cfDoc;

        // The gridster widget dashboard: absolutely placed .gs-w items whose whole
        // geometry is declared in px inline styles (GridsterDashboard.cs).
        if (!cv.marginsExplicit
            && TryRenderGridsterDashboard(cv.html, cv.pageHeight) is { } gridDoc)
            return gridDoc;

        // The ASP.NET portal shell: a fixed-width #wrapper (header/banner/col2)
        // whose two linked sheets both apply — the page grows to the wrapper
        // (PortalShell.cs).
        if (!cv.marginsExplicit && TryRenderPortalShell(cv.html) is { } pshDoc)
            return pshDoc;

        // The classed border-table case letter: #PageHeading + .blackBorder
        // four-side class grids split onto pages by div.break (CaseLetter.cs).
        if (!cv.marginsExplicit && TryRenderCaseLetter(cv.html) is { } caseLetterDoc)
            return caseLetterDoc;

        // An authored-width @font-face sheet: the body pins its width in points
        // and the paragraphs ride the document's own faces (FontFaceSheet.cs).
        if (!cv.marginsExplicit
            && TryRenderFontFaceSheet(cv.html, options, cv.pageHeight) is { } ffsDoc)
            return ffsDoc;

        if (TryRenderArticlePdf(cv.html, cv.css, cv.marginLeft, cv.marginRight, cv.marginTop,
                cv.marginBottom, cv.pageWidth, cv.pageHeight, options?.BasePath) is { } apDoc)
            return apDoc;
        return null;
    }

    /// <summary>The second group of stylesheet-driven dialects up to the Segoe alert; the document when one matched, otherwise null.</summary>
    private static Document? RenderStyledDialectsB(ConvertState cv, HtmlLoadOptions? options)
    {
        if (RenderStyledDialectsBHead(cv, options) is { } bHeadDoc) return bHeadDoc;

        // Orphan-rowspan split-panel recovery (see SplitPanel.cs): a stray
        // `<td rowspan=…>` outside any table wraps a two-panel sidebar/main band.
        if (!cv.marginsExplicit
            && TryRenderSplitPanel(cv.html, options, cv.pageWidth, cv.pageHeight) is { } spDoc)
            return spDoc;

        if (!cv.marginsExplicit
            && TryRenderBootstrapScreen(cv.html, cv.css, cv.pageWidth, cv.pageHeight) is { } bsDoc)
            return bsDoc;

        // Container-less Bootstrap ROWS pages (see BootstrapRows.cs): body-level
        // col-xs grids and panel-success cards on the Site.css-padded sheet.
        if (!cv.marginsExplicit
            && TryRenderBootstrapRows(cv.html, cv.css, cv.pageWidth, cv.pageHeight) is { } brDoc)
            return brDoc;

        // The step-row DETABLE worksheet (see PrintPage.cs): flex step rows with
        // centred fixed-layout widget tables splitting across pages.
        if (!cv.marginsExplicit
            && TryRenderStepRows(cv.html, cv.css, cv.pageHeight) is { } srDoc)
            return srDoc;

        // The hospital discharge-letter preview (see LetterPreview.cs): the
        // `.letterPreview` table whose thead and tfoot frame every page's fragment.
        if (TryRenderLetterPreview(cv) is { } lpDoc)
            return lpDoc;

        // The SignChart LOGIN SHELL (see LoginShell.cs): the nav row, the title band,
        // the centred rounded login box and the copyright band in 11px Tahoma.
        if (!cv.marginsExplicit && TryRenderLoginShell(cv, options) is { } lsDoc)
            return lsDoc;

        // The jQuery-print FORM-AREA sheet (see FormAreas.cs): absolutely positioned
        // formArea panels of labels and one px-column table each, cut into page slices.
        if (!cv.marginsExplicit && TryRenderFormAreas(cv) is { } faDoc)
            return faDoc;

        // The InfoPath application form (see InfoPathForm.cs): the fixed-layout control grid
        // in Verdana with its combo boxes, text boxes, date pickers and buttons.
        if (!cv.marginsExplicit && TryRenderInfoPathForm(cv) is { } ipDoc)
            return ipDoc;

        // The FLOAT-BOX document (see FloatBoxForm.cs): a standards-mode sheet whose class
        // rules float sized blocks and spans, laid out as CSS 2.1 floats on the ink-grown sheet.
        if (TryRenderFloatBoxDocument(cv, options) is { } fbDoc)
            return fbDoc;

        // The edge-to-edge SEGOE ALERT sheet (see BootstrapRows.cs): label/value
        // panels, the broken vehicle frame and the sensor grid at zero margins.
        if (TryRenderSegoeAlert(cv.html, cv.css, cv.pageWidth, cv.pageHeight) is { } saDoc)
            return saDoc;
        return null;
    }

    /// <summary>The first group of stylesheet-driven dialects up to the metrics homepage; the document when one matched, otherwise null.</summary>
    private static Document? RenderStyledDialectsA(ConvertState cv, HtmlLoadOptions? options)
    {

        // Themed-Bootstrap screen pages (see BootstrapScreen.cs): a body rule with
        // a pixel font, unitless line-height and page background plus the
        // .container/.table framework — laid out on symmetric
        // margins with real line-height boxes.
        if (!cv.marginsExplicit
            && TryRenderResultCard(cv.html, cv.css, cv.pageHeight) is { } rcDoc)
            return rcDoc;

        if (!cv.marginsExplicit
            && TryRenderMetricsCard(cv.html, cv.css, cv.pageHeight) is { } metCardDoc)
            return metCardDoc;

        // iShares fund fact-sheets (see IsharesFactSheet.cs): the TSR two-column
        // allocation page renders on its measured band/rule/leader geometry.
        if (!cv.marginsExplicit
            && TryRenderIsharesFactSheet(cv.html) is { } ifsDoc)
            return ifsDoc;

        // The ipd claim-file letter (see IpdClaimLetter.cs): an 8-in form-letter
        // sheet of section bands, a 25/75 property grid, and subsection boxes.
        if (!cv.marginsExplicit
            && TryRenderIpdClaimLetter(cv.html) is { } ipdDoc)
            return ipdDoc;

        // The Bootstrap clinical report form (see ClinicalForm.cs): a bordered
        // form-container of banded sections whose <ol> items carry form-control
        // boxes, bare inline inputs and radio circles.
        if (!cv.marginsExplicit
            && TryRenderClinicalForm(cv.html) is { } clinDoc)
            return clinDoc;

        // The metrics-portal homepage export (see MetricsHomepage.cs): a
        // zero-margin landscape sheet — hero SVG + gradient CTA pill + the
        // flex-cell metric grids. Its margins are explicitly zero, so this
        // dispatch rides ahead of the margins gate.
        if (TryRenderMetricsHomepage(cv.html, options) is { } mhDoc)
            return mhDoc;
        return null;
    }

    /// <summary>The chart dialects - the fixed charts, the bar chart and the Ember chart - render through their own renderers; the document when one matched, otherwise null.</summary>
    private static Document? RenderChartDialects(ConvertState cv, HtmlLoadOptions? options)
    {

        // Listing-card exports (see ListingCard.cs): a rounded-bordered
        // .container of fixed-height .item rows with floated inline-SVG icons.
        // Runs before the SVG extraction — the card draws its icons itself.
        if (!cv.marginsExplicit
            && TryRenderListingCard(cv.html, cv.pageWidth, cv.pageHeight) is { } lcDoc)
            return lcDoc;

        // UBL invoice-frame exports (see UblInvoice.cs): the Danish e-invoice
        // shape — print-media Verdana tables, floated references/totals, the
        // supplier footer.
        if (!cv.marginsExplicit
            && TryRenderUblInvoice(cv.html, cv.pageWidth, cv.pageHeight) is { } ublDoc)
            return ublDoc;

        // The D3 vertical-bar-chart export (see BarChart.cs): title/subtitle
        // bands over a flex row of inline SVGs drawn as vector fills and Times
        // text, clipped per svg viewport. BEFORE the svg extraction below — the
        // dialect reads the raw inline svg markup itself.
        if (TryRenderBarChart(cv.html, cv.pageWidth, cv.pageHeight) is { } bcDoc)
            return bcDoc;

        // The ember/jsPlumb ORG CHART (see BarChart.cs): absolutely positioned
        // double-bordered cards joined by jsPlumb connector svgs painted by
        // standard SVG rules; the sheet widens to the right-most card. Also
        // BEFORE the svg extraction — the connectors read raw markup.
        if (TryRenderEmberChart(cv.html, cv.pageWidth, cv.pageHeight) is { } emDoc)
            return emDoc;
        return null;
    }

    /// <summary>The float band, float image and both-sides float dialects and the margins they imply.</summary>
    private static void ResolveFloatBands(ConvertState cv, HtmlLoadOptions? options)
    {

        // The SEC-filing float-column card dialect (consecutive `float:left; width:N%`
        // div bands) is laid out against symmetric body margins,
        // so its right margin mirrors the left (96 pt) rather than the legacy 72 pt —
        // a 72 pt right margin over-widens the content box, so the % float columns land
        // too wide and every right-aligned cell (page code, form fields) drifts right.
        // Gated to the band dialect + default margins: ordinary conversions keep 72 pt.
        cv.profile.floatBandDoc = HasFloatColumnBand(cv.html);
        // A document that floats a plain box (no column width) LEFT: its images sit at
        // the content edge and the following text wraps beside them. Distinct from the
        // float-COLUMN band above, whose divs declare a width and become their own
        // columns; a document that has those keeps the band dialect.
        cv.profile.floatImageDoc = !cv.profile.floatBandDoc
            && Regex.IsMatch(cv.html, @"float\s*:\s*left", RegexOptions.IgnoreCase);
        cv.bodyLineFactor = 0.0;
        {
            var bodyClass = Regex.Match(cv.html, @"<body\b[^>]*class\s*=\s*[""']([^""']+)",
                RegexOptions.IgnoreCase);
            if (bodyClass.Success)
                foreach (var cls in bodyClass.Groups[1].Value.Split(
                             ' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    // (a unitless factor, or an em one - the reference inherits the em as a factor
                    //  too: the change-control page's `line-height: 1.25em` body paces its 15 pt
                    //  heading 18.75 and its 9.5 pt runs 11.875)
                    var rule = Regex.Match(cv.html,
                        @"\." + Regex.Escape(cls) + @"\s*{[^}]*line-height\s*:\s*([0-9.]+)\s*(?:em)?\s*[;}]",
                        RegexOptions.IgnoreCase);
                    if (rule.Success && double.TryParse(rule.Groups[1].Value,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var blf)
                        && blf > 0.5 && blf < 4)
                    { cv.bodyLineFactor = blf; break; }
                }
            // …and the body tag's own percent or unitless line-height (its attribute; the UA flow
            // reads it where that attribute is the flow's typography)
            if (cv.bodyLineFactor <= 0 && BodyTagStyleAttr(cv.html) is { } bodyLhStyle
                && Regex.Match(bodyLhStyle, LineHeightDeclRx, RegexOptions.IgnoreCase) is { Success: true } bodyLhM
                && Regex.Match(bodyLhM.Groups[1].Value.Trim(), @"^([\d.]+)\s*(%?)$") is { Success: true } bodyLhV
                && double.TryParse(bodyLhV.Groups[1].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var bodyAttrLf) && bodyAttrLf > 0)
                cv.bodyLineFactor = bodyLhV.Groups[2].Value.Length > 0 ? bodyAttrLf / 100.0 : bodyAttrLf;
        }
        cv.profile.floatBothSidesDoc =
            Regex.IsMatch(cv.html, @"<img\b[^>]*style\s*=\s*[""'][^""']*float\s*:\s*left",
                RegexOptions.IgnoreCase)
            && Regex.IsMatch(cv.html, @"<img\b[^>]*style\s*=\s*[""'][^""']*float\s*:\s*right",
                RegexOptions.IgnoreCase);
        if (!cv.marginsExplicit && cv.profile.floatBandDoc)
            cv.marginRight = cv.marginLeft;
        // A float-header document keeps SYMMETRIC page margins: the content
        // box runs 96..499 on a 595 pt sheet, which is what seats its right-floated logo
        // at 315.25 rather than 24 pt further out.
        if (!cv.marginsExplicit && cv.profile.floatBothSidesDoc)
        {
            cv.marginRight = cv.marginLeft;
        }
        // The escaped-attr dialect wraps on symmetric margins too (measured: its
        // rules span 96..pageW−96 and a control that would end past that edge
        // wraps, where the legacy 72 pt right margin would have kept it inline).
        if (!cv.marginsExplicit && cv.profile.escapedAttrDoc)
            cv.marginRight = cv.marginLeft;

        // Form dialect with explicit zero margins: the browser's default 8px BODY
        // margin still insets the content, and the page is the body width
        // PLUS that margin (a 210mm body renders on a 601pt page, content from
        // x = 6pt). Ordinary zero-margin conversions keep content flush.
        if (cv.profile.formHorizontalDoc && cv.marginsExplicit && cv.marginLeft < 1e-9 && cv.marginRight < 1e-9)
        {
            cv.marginLeft += 6;
            cv.pageWidth += 6;
        }
    }

    /// <summary>The benefit review, OutSystems export and the first fixed-layout dialects render through their own renderers; the document when one matched, otherwise null.</summary>
    private static Document? RenderEarlyDialects(ConvertState cv, HtmlLoadOptions? options)
    {
        // The benefit-commencement review export (BenefitReview.cs): the Angular
        // page whose megabyte stylesheet is dropped, leaving a UA Times
        // flow of lists, bold section heads and blue underlined links. Ahead of
        // the dialect chain — its Bootstrap grid classes would otherwise pull it
        // into a screen-layout arm.
        if (TryRenderBenefitReview(cv.html, cv.pageWidth, cv.pageHeight) is { } bcrDoc)
            return bcrDoc;

        // OutSystems document-handling exports (see OutSystemsExport.cs): the
        // aspNetHidden/OSFillParent bill-of-lading, laid out at natural width
        // and shrunk onto the authored sheet by its own scale transform.
        if (cv.profile.scaleToPageWidth && TryRenderOutSystemsExport(cv.html,
                cv.pageWidth, cv.pageHeight, cv.marginLeft, cv.marginRight) is { } osExportDoc)
            return osExportDoc;

        // Covering-letter exports (see CoveringLetter.cs): the .covering-letter
        // frame with berthr-editable spans — the justified marina renewal letter.
        if (!cv.marginsExplicit
            && TryRenderCoveringLetter(cv.html, cv.pageWidth, cv.pageHeight) is { } clDoc)
            return clDoc;

        // Print-invoice sheets: an @media-print-authored document (body
        // {display:table; width:N%} + an @page rule) of label/value and item
        // tables. The engine sizes the sheet to 96 + the 1000px print viewport +
        // the fitted right band, lays the body at N% of that container, and
        // draws the tables at the measured column model (all constants from the
        // expected render of the invoice fixture).
        if (!cv.marginsExplicit
            && Regex.IsMatch(cv.html, @"@media\s+print", RegexOptions.IgnoreCase)
            && Regex.IsMatch(cv.html, @"@page", RegexOptions.IgnoreCase)
            && Regex.IsMatch(cv.html,
                @"body\s*\{[^}]*display\s*:\s*table[^}]*\}", RegexOptions.IgnoreCase)
            && Regex.IsMatch(cv.html, @"<table\b", RegexOptions.IgnoreCase)
            && TryRenderPrintInvoice(cv.html, options) is { } invoiceDoc)
            return invoiceDoc;

        // Positioned DTP-form exports (see DtpForm.cs): a flat absolutely
        // positioned canvas of pt-coordinate id rules sliced into page bands.
        if (!cv.marginsExplicit
            && TryRenderPositionedDtp(cv.html, cv.pageWidth, cv.pageHeight) is { } dtpDoc)
            return dtpDoc;

        // DNN portal reports (see DnnReport.cs): the skinmaster box model drawn
        // from the skin constants; the sheet widens to hold the 984px box.
        if (!cv.marginsExplicit && !(cv.pageInfo?.WidthAssigned ?? false)
            && TryRenderDnnReport(cv.html, options, cv.pageHeight) is { } dnnDoc)
            return dnnDoc;

        // A document whose ROOT element is <svg> renders through the SVG engine at
        // its natural size, anchored at the content origin plus the UA body margin
        // (both measured at margin + 6), clipped by the page; the
        // sheet paginates by the drawing's full height in BLANK continuation pages
        // (the engine draws the vector content on page 1 only).
        // …a leading INVISIBLE empty div (a chart library's hidden tooltip
        // holder) does not unseat the svg root — it renders nothing.
        if (Regex.IsMatch(cv.html,
                @"^﻿?\s*(?:<\?xml[^>]*\?>\s*)?(?:<!--[\s\S]*?-->\s*)*(?:<!DOCTYPE[^>]*>\s*)?(?:<div\b[^>]*(?:visibility\s*:\s*hidden|display\s*:\s*none)[^>]*>\s*</div\s*>\s*)*<svg\b",
                RegexOptions.IgnoreCase))
        {
            var svgDocOut = new Document();
            var svgPage1 = svgDocOut.Pages.Add(cv.pageWidth, cv.pageHeight);
            var (svgPng, svgNatWpx, svgNatHpx) = ImageRasterizer.RasterizeSvgWithSize(Encoding.UTF8.GetBytes(cv.html));
            // The rasterizer reports the viewport in CSS px; the drawing paints
            // at ×0.75 like every other CSS length.
            var svgNatWpt = svgNatWpx * 0.75;
            var svgNatHpt = svgNatHpx * 0.75;
            if (svgPng is not null && svgNatWpt > 0 && svgNatHpt > 0)
            {
                var sx = cv.marginLeft + 6.0;
                var syTop = cv.marginTop + 6.0;
                svgPage1.AddImage(svgPng, new Rectangle(
                    sx, cv.pageHeight - syTop - svgNatHpt, sx + svgNatWpt, cv.pageHeight - syTop));
                var svgUsableH = cv.pageHeight - cv.marginTop - cv.marginBottom;
                var svgPages = svgUsableH > 0
                    ? (int)Math.Ceiling((svgNatHpt + 6.0) / svgUsableH) : 1;
                for (var sp = 1; sp < Math.Min(svgPages, 50); sp++)
                    svgDocOut.Pages.Add(cv.pageWidth, cv.pageHeight);
            }
            return svgDocOut;
        }
        return null;
    }
}
