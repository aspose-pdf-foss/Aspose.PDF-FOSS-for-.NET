using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    private static Document ConvertFromHtml(string html, HtmlLoadOptions? options)
    {
        // HTML produced by this library's own PDF→HTML converter (absolutely-positioned
        // pdf-text spans inside fixed-size pdf-page divs) round-trips through a
        // dedicated geometric path. The PNG-page-background stl_ dialect re-imports
        // through the padded POSITIONED path (each line keeps its
        // 6pt-inset offset and the page widens to the pinned content). Otherwise,
        // when the page's stylesheet is resolvable (inline, or linked and reachable)
        // the content re-imports at its fixed positions onto print sheets; when it
        // is not, the spans are regrouped into source lines and reflowed as text.
        //
        // The stl_ dialect's geometry lives ENTIRELY in its stylesheet — class boxes,
        // font sizes, the background wrapper. When that stylesheet is a linked file
        // and the base path was not one the caller supplied, it is not
        // reached (external resources resolve only against an explicit base
        // path, not one auto-derived from the loaded file's own directory): none of
        // the fixed geometry is then available, and the converter reflows the
        // positioned spans into text rather than replaying an empty fixed layout.
        // An auto-derived base path is therefore treated as absent when deciding
        // whether the stl_ CSS resolves (mirrors TryConvertPositionedFixedLayout).
        // A binary file fed through HtmlLoadOptions (an OLE2 document renamed .html):
        // the mojibake lays out as ONE anonymous Times 12 pt
        // paragraph on a page WIDENED to its min-content width. C0 control bytes are
        // the signature — real HTML text never carries them.
        var c0Controls = 0;
        foreach (var bch in html)
            if (bch < 0x20 && bch is not ('\t' or '\n' or '\r')) c0Controls++;
        if (c0Controls >= 4 && TryConvertBinaryText(html) is { } binaryDoc)
            return binaryDoc;

        var stlPositioned = IsStlPositionedHtml(html);
        var stlCssOptions = options?.BasePathAutoDerived == true ? null : options;
        var stlCssResolvable = stlPositioned && !string.IsNullOrWhiteSpace(GatherStlCss(html, stlCssOptions));
        if (stlCssResolvable && HasStlRasterBackground(html))
            return ConvertStlPositioned(html, options);
        if (IsPositionedSpanHtml(html) || stlPositioned)
        {
            // The pdf-text dialect carries its geometry inline (always self-contained);
            // the stl_ dialect only re-imports fixed when its stylesheet resolved.
            if (!stlPositioned || stlCssResolvable)
            {
                var fixedDoc = TryConvertPositionedFixedLayout(html, options);
                if (fixedDoc is not null) return fixedDoc;
            }
            return ConvertPositionedSpans(html, options);
        }

        // The archaic <image> tag parses as <img> (the HTML standard's alias) —
        // without it a legacy page's pictures never reach the image pipeline.
        html = Regex.Replace(html, @"<image\b", "<img", RegexOptions.IgnoreCase);

        // The source renderer executes page scripts before layout: a straight-line
        // script that only builds a string and appends a text node contributes that
        // text to the flow. The micro-interpreter replaces each fully-evaluable
        // <script> with its appendChild output in place; every other script keeps
        // the existing strip (see HtmlToPdfConverter.Script.cs).
        if (html.Contains("<script", StringComparison.OrdinalIgnoreCase))
            html = ApplyTrivialDomScripts(html);

        // Fold external <link rel="stylesheet"> files into the document as inline <style>
        // blocks so the legacy flow's CSS scan (ParseStyleSheet, ParseBeforeMarkers, …) sees
        // their rules — a browser applies a linked stylesheet identically to an inline one.
        // Resolved through the same loader as images (CustomLoaderOfExternalResources first,
        // then the BasePath); an unreachable stylesheet leaves the tag untouched.
        html = InlineLinkedStylesheets(html, options);

        // A rowless <table> is a wrapper tag, not a grid — the HTML parser
        // foster-parents its illegal children out of the table and the table
        // itself generates no boxes. Unwrapping it here lets every downstream
        // gate and flow see the document's real structure.
        html = UnwrapRowlessTables(html);

        // Bootstrap form-horizontal label/value rows become 2-column tables (the
        // float-label pattern the flow renderer would stack). Remember the marker
        // BEFORE the transform removes it — it also unlocks px-width float columns.
        // (Computed before the shorthand expansion: the dialect also opts in to the
        // source renderer's familyless-`font:`-shorthand reset semantics.)
        var formHorizontalDoc = html.IndexOf("control-group", StringComparison.OrdinalIgnoreCase) >= 0
                                && html.IndexOf("<label", StringComparison.OrdinalIgnoreCase) >= 0;

        // Expand the CSS `font:` shorthand (font: [style weight] size[/line-height]
        // family, …) into its longhands once, up front — every downstream dialect
        // gate and style regex reads font-size / font-family / line-height only, so
        // a shorthand-styled document (body font: 1em/1.4em Tahoma …) would
        // otherwise pass for "font-family-free" and take the UA serif flow.
        html = ExpandFontShorthands(html, familylessResets: formHorizontalDoc);

        if (formHorizontalDoc)
        {
            html = TransformFormHorizontalRows(html);
            // An empty div styled with only a border-bottom is a section divider —
            // exactly what <hr> renders as. The divider's CSS vertical margins ride
            // along: they are the section rhythm of this dialect.
            html = Regex.Replace(html,
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
            html = Regex.Replace(html, @"<legend\b([^>]*?)style\s*=\s*""",
                "<h2$1style=\"font-weight: normal;", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<(/?)legend\b", "<$1h2", RegexOptions.IgnoreCase);
        }
        // The body's CSS line-height (the `font: 1em/1.4em …` shorthand expands to
        // it) sets the pitch of the synthesized form-row cells.
        var bodyLineHeightPt = 0.0;
        // Sectioned-report shape: a document whose sections are divided by rules that
        // break the page. Such a document is laid out on the browser's own block
        // rhythm — real UA margins, collapsed between siblings, and a <br> holding its
        // line box — rather than the legacy line-on-line stack.
        var sectionedReport = Regex.IsMatch(html,
            @"<hr\b[^>]*(page-)?break-after\s*:\s*(always|page)", RegexOptions.IgnoreCase);

        // A JSON-escaped export defeats every style= attribute — the value truncates at its
        // first space — so nothing in the markup can size a control or the flow. Such a
        // document falls back to the UA base (16px = 12pt) and to the character-grid box
        // each control's size/cols/rows declares, which is what gets drawn.
        var escapedAttrDoc = html.IndexOf("=\\\"", StringComparison.Ordinal) >= 0;

        var formBodyFontPt = 0.0;
        if (formHorizontalDoc || sectionedReport || escapedAttrDoc)
        {
            var bodyTag = Regex.Match(html, @"<body\b[^>]*>", RegexOptions.IgnoreCase);
            var bodyStyle = bodyTag.Success ? DivStyleOf(bodyTag.Value) : "";
            var lhm = Regex.Match(bodyStyle, @"line-height\s*:\s*([\d.]+)\s*(em|px|pt)?", RegexOptions.IgnoreCase);
            if (lhm.Success && double.TryParse(lhm.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var lhv))
                bodyLineHeightPt = lhm.Groups[2].Value.ToLowerInvariant() switch
                {
                    "px" => lhv * 0.75,
                    "pt" => lhv,
                    _ => lhv * 12.0,   // em / unitless of the 16px = 12pt UA base
                };
            // The body font size seeds the whole flow (an em-sized body resolves
            // against the browser's 16px = 12pt base, not this flow's legacy 11pt).
            formBodyFontPt = 12.0;
            var bfm = Regex.Match(bodyStyle, @"font-size\s*:\s*([\d.]+)\s*(em|px|pt)?", RegexOptions.IgnoreCase);
            if (bfm.Success && double.TryParse(bfm.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var bfv))
                formBodyFontPt = bfm.Groups[2].Value.ToLowerInvariant() switch
                {
                    "px" => bfv * 0.75,
                    "pt" => bfv,
                    _ => bfv * 12.0,
                };
        }

        var pageInfo = options?.PageInfo;
        var pageWidth   = pageInfo?.Width  is > 0 ? pageInfo.Width  : 612.0;
        var pageHeight  = pageInfo?.Height is > 0 ? pageInfo.Height : 792.0;
        // PageInfo.IsLandscape is a no-op for HTML imports — the page lays
        // out portrait regardless — so undo the SETTER's dimension swap (the
        // caller authored portrait and only set the flag). Width/Height the
        // caller authored landscape stand, flag or no flag.
        if (pageInfo?.LandscapeSwapApplied == true && pageWidth > pageHeight)
            (pageWidth, pageHeight) = (pageHeight, pageWidth);
        var pageMargin  = pageInfo?.Margin;
        // Respect user-set margins verbatim (including explicit zeros); fall back to
        // the HTML-renderer defaults only when MarginInfo was never touched.
        // Unstyled content sits
        // ~96 pt from the left and the first baseline ~89 pt from the top of an
        // A4 page; the previous 72 pt left/top shifted every conversion up-and-left
        // by ~24 pt / ~17 pt. Right/bottom keep 72 pt.
        // Margins are explicit when values were SET — or when the caller ASSIGNED a
        // MarginInfo object at all: `options.PageInfo.Margin = new MarginInfo()` is
        // the public API idiom for "zero margins", distinct from the untouched
        // default that gets the renderer's fallback margins.
        bool marginsExplicit = (pageMargin?.IsTouched ?? false) || (pageInfo?.MarginAssigned ?? false);
        // A margin authored on the DEFAULT PageInfo an HtmlLoadOptions constructs
        // resolves PER SIDE — setting only Top keeps the renderer defaults for the
        // other three sides. A caller-REPLACED PageInfo (or MarginInfo) is authored
        // as a whole: its untouched sides are deliberate zeros.
        bool perSide = (pageMargin?.IsTouched ?? false) && pageMargin!.HtmlPerSideDefaults;
        var marginLeft   = marginsExplicit ? (perSide && !pageMargin!.LeftTouched   ? 96.0 : pageMargin!.Left)   : 96.0;
        var marginRight  = marginsExplicit ? (perSide && !pageMargin!.RightTouched  ? 72.0 : pageMargin!.Right)  : 72.0;
        var marginTop    = marginsExplicit ? (perSide && !pageMargin!.TopTouched    ? 89.0 : pageMargin!.Top)    : 89.0;
        var marginBottom = marginsExplicit ? (perSide && !pageMargin!.BottomTouched ? 72.0 : pageMargin!.Bottom) : 72.0;

        // IsRenderToSinglePage: the reference lays the whole flow out CONTINUOUSLY
        // (no page breaks, uninterrupted paragraph rhythm) and sizes the one sheet
        // to whole CONTENT BANDS of the authored page: height = N × (authored
        // height − vertical margins). Layout runs against the PDF coordinate
        // ceiling so no break fires; the tail fix-up below shifts the finished
        // content onto the final sheet and sets its real MediaBox.
        var singlePage = options?.IsRenderToSinglePage == true;
        var singlePageRealH = pageHeight;
        if (singlePage) pageHeight = 14400.0;   // the PDF page-dimension ceiling

        // ScaleToPageWidth: the caller asks for the layout at its NATURAL width,
        // shrunk uniformly onto the authored sheet — the reference lays such a
        // document out at the UA base size on its min-content width, then scales
        // the finished pages down with the content pinned at the left margin and
        // the page top. The natural width comes from the widest table's natural
        // probe below; scalePendingS carries the shrink to the end of the flow.
        var scaleToPageWidth = options?.PageLayoutOption == HtmlPageLayoutOption.ScaleToPageWidth;
        double scalePendingS = 0, scaleReqPageW = 0, scaleReqPageH = 0;

        // OutSystems document-handling exports (see OutSystemsExport.cs): the
        // aspNetHidden/OSFillParent bill-of-lading, laid out at natural width
        // and shrunk onto the authored sheet by its own scale transform.
        if (scaleToPageWidth && TryRenderOutSystemsExport(html,
                pageWidth, pageHeight, marginLeft, marginRight) is { } osExportDoc)
            return osExportDoc;

        // Covering-letter exports (see CoveringLetter.cs): the .covering-letter
        // frame with berthr-editable spans — the justified marina renewal letter.
        if (!marginsExplicit
            && TryRenderCoveringLetter(html, pageWidth, pageHeight) is { } clDoc)
            return clDoc;

        // Print-invoice sheets: an @media-print-authored document (body
        // {display:table; width:N%} + an @page rule) of label/value and item
        // tables. The engine sizes the sheet to 96 + the 1000px print viewport +
        // the fitted right band, lays the body at N% of that container, and
        // draws the tables at the measured column model (all constants from the
        // reference render of the invoice fixture).
        if (!marginsExplicit
            && Regex.IsMatch(html, @"@media\s+print", RegexOptions.IgnoreCase)
            && Regex.IsMatch(html, @"@page", RegexOptions.IgnoreCase)
            && Regex.IsMatch(html,
                @"body\s*\{[^}]*display\s*:\s*table[^}]*\}", RegexOptions.IgnoreCase)
            && Regex.IsMatch(html, @"<table\b", RegexOptions.IgnoreCase)
            && TryRenderPrintInvoice(html, options) is { } invoiceDoc)
            return invoiceDoc;

        // Positioned DTP-form exports (see DtpForm.cs): a flat absolutely
        // positioned canvas of pt-coordinate id rules sliced into page bands.
        if (!marginsExplicit
            && TryRenderPositionedDtp(html, pageWidth, pageHeight) is { } dtpDoc)
            return dtpDoc;

        // DNN portal reports (see DnnReport.cs): the skinmaster box model drawn
        // from the skin constants; the sheet widens to hold the 984px box.
        if (!marginsExplicit && !(pageInfo?.WidthAssigned ?? false)
            && TryRenderDnnReport(html, options, pageHeight) is { } dnnDoc)
            return dnnDoc;

        // A document whose ROOT element is <svg> renders through the SVG engine at
        // its natural size, anchored at the content origin plus the UA body margin
        // (both measured at margin + 6 on the reference), clipped by the page; the
        // sheet paginates by the drawing's full height in BLANK continuation pages
        // (the engine draws the vector content on page 1 only).
        // …a leading INVISIBLE empty div (a chart library's hidden tooltip
        // holder) does not unseat the svg root — it renders nothing.
        if (Regex.IsMatch(html,
                @"^﻿?\s*(?:<\?xml[^>]*\?>\s*)?(?:<!--[\s\S]*?-->\s*)*(?:<!DOCTYPE[^>]*>\s*)?(?:<div\b[^>]*(?:visibility\s*:\s*hidden|display\s*:\s*none)[^>]*>\s*</div\s*>\s*)*<svg\b",
                RegexOptions.IgnoreCase))
        {
            var svgDocOut = new Document();
            var svgPage1 = svgDocOut.Pages.Add(pageWidth, pageHeight);
            var svgPng = ImageRasterizer.RasterizeSvg(Encoding.UTF8.GetBytes(html),
                out var svgNatWpx, out var svgNatHpx);
            // The rasterizer reports the viewport in CSS px; the drawing paints
            // at ×0.75 like every other CSS length.
            var svgNatWpt = svgNatWpx * 0.75;
            var svgNatHpt = svgNatHpx * 0.75;
            if (svgPng is not null && svgNatWpt > 0 && svgNatHpt > 0)
            {
                var sx = marginLeft + 6.0;
                var syTop = marginTop + 6.0;
                svgPage1.AddImage(svgPng, new Rectangle(
                    sx, pageHeight - syTop - svgNatHpt, sx + svgNatWpt, pageHeight - syTop));
                var svgUsableH = pageHeight - marginTop - marginBottom;
                var svgPages = svgUsableH > 0
                    ? (int)Math.Ceiling((svgNatHpt + 6.0) / svgUsableH) : 1;
                for (var sp = 1; sp < Math.Min(svgPages, 50); sp++)
                    svgDocOut.Pages.Add(pageWidth, pageHeight);
            }
            return svgDocOut;
        }

        // The SEC-filing float-column card dialect (consecutive `float:left; width:N%`
        // div bands) is laid out by the source renderer against symmetric body margins,
        // so its right margin mirrors the left (96 pt) rather than the legacy 72 pt —
        // a 72 pt right margin over-widens the content box, so the % float columns land
        // too wide and every right-aligned cell (page code, form fields) drifts right.
        // Gated to the band dialect + default margins: ordinary conversions keep 72 pt.
        var floatBandDoc = HasFloatColumnBand(html);
        // A document that floats a plain box (no column width) LEFT: its images sit at
        // the content edge and the following text wraps beside them. Distinct from the
        // float-COLUMN band above, whose divs declare a width and become their own
        // columns; a document that has those keeps the band dialect.
        var floatImageDoc = !floatBandDoc
            && Regex.IsMatch(html, @"float\s*:\s*left", RegexOptions.IgnoreCase);
        if (!marginsExplicit && floatBandDoc)
            marginRight = marginLeft;
        // The escaped-attr dialect wraps on symmetric margins too (measured: its
        // rules span 96..pageW−96 and a control that would end past that edge
        // wraps, where the legacy 72 pt right margin would have kept it inline).
        if (!marginsExplicit && escapedAttrDoc)
            marginRight = marginLeft;

        // Form dialect with explicit zero margins: the browser's default 8px BODY
        // margin still insets the content, and the page is the body width
        // PLUS that margin (a 210mm body renders on a 601pt page, content from
        // x = 6pt). Ordinary zero-margin conversions keep content flush.
        if (formHorizontalDoc && marginsExplicit && marginLeft < 1e-9 && marginRight < 1e-9)
        {
            marginLeft += 6;
            pageWidth += 6;
        }

        // Listing-card exports (see ListingCard.cs): a rounded-bordered
        // .container of fixed-height .item rows with floated inline-SVG icons.
        // Runs before the SVG extraction — the card draws its icons itself.
        if (!marginsExplicit
            && TryRenderListingCard(html, pageWidth, pageHeight) is { } lcDoc)
            return lcDoc;

        // UBL invoice-frame exports (see UblInvoice.cs): the Danish e-invoice
        // shape — print-media Verdana tables, floated references/totals, the
        // supplier footer.
        if (!marginsExplicit
            && TryRenderUblInvoice(html, pageWidth, pageHeight) is { } ublDoc)
            return ublDoc;

        // The D3 vertical-bar-chart export (see BarChart.cs): title/subtitle
        // bands over a flex row of inline SVGs drawn as vector fills and Times
        // text, clipped per svg viewport. BEFORE the svg extraction below — the
        // dialect reads the raw inline svg markup itself.
        if (TryRenderBarChart(html, pageWidth, pageHeight) is { } bcDoc)
            return bcDoc;

        // The ember/jsPlumb ORG CHART (see BarChart.cs): absolutely positioned
        // double-bordered cards joined by jsPlumb connector svgs painted by
        // standard SVG rules; the sheet widens to the right-most card. Also
        // BEFORE the svg extraction — the connectors read raw markup.
        if (TryRenderEmberChart(html, pageWidth, pageHeight) is { } emDoc)
            return emDoc;

        // Inline <svg> elements become image placeholders drawn through the SVG engine.
        html = ExtractInlineSvgs(html, out var inlineSvgs);

        var css = ParseStyleSheet(html);

        // Themed-Bootstrap screen pages (see BootstrapScreen.cs): a body rule with
        // a pixel font, unitless line-height and page background plus the
        // .container/.table framework — laid out on the reference's symmetric
        // margins with real line-height boxes.
        if (!marginsExplicit
            && TryRenderResultCard(html, css, pageHeight) is { } rcDoc)
            return rcDoc;

        if (!marginsExplicit
            && TryRenderMetricsCard(html, css, pageHeight) is { } metCardDoc)
            return metCardDoc;

        // Styled XML-dump viewer sheets (see MetricsCard.cs): nested per-element
        // divs under one root class whose `.root *` rule blocks-and-pads every
        // descendant at the root's keyword font size — the em padding/margin
        // chain positions every line.
        if (!marginsExplicit
            && TryRenderXmlViewer(html, pageWidth, pageHeight) is { } xvDoc)
            return xvDoc;

        // Print-media job ads (see MetricsCard.cs): a zero-margin conversion of
        // an @media-print document whose container class sizes the sheet.
        if (marginsExplicit
            && TryRenderPrintAd(html, css, pageHeight, options) is { } paDoc)
            return paDoc;

        // Angular audit-report exports (see MetricsCard.cs): the finding sheet
        // under its authored margins, on the kept A4.
        if (marginsExplicit
            && TryRenderAuditReport(html, pageWidth, pageHeight, options) is { } arDoc)
            return arDoc;

        // Resume-builder document sheets (see MetricsCard.cs): the div#document
        // export whose dynamic stylesheet resolves the whole layout.
        if (marginsExplicit
            && TryRenderResumeDoc(html, pageWidth, pageHeight, options) is { } rdDoc)
            return rdDoc;

        // Contract-invoice sheets on remote faces (see MetricsCard.cs): the
        // Google-Fonts Lato invoice with its fetched font programs.
        if (!marginsExplicit
            && TryRenderContractInvoice(html, pageHeight) is { } ciDoc)
            return ciDoc;

        // Decision-notification letters (see MetricsCard.cs): the all-inline
        // TCI template — basis container, float header, boxSection panels.
        if (!marginsExplicit
            && TryRenderDecisionLetter(html, pageHeight) is { } dnDoc)
            return dnDoc;

        // Word-filtered FORM-GRID pages (see MsoForm.cs): one MsoNormalTable
        // whose columns solve to a landscape-wide single page.
        if (!marginsExplicit
            && TryRenderMsoWordForm(html) is { } msoFormDoc)
            return msoFormDoc;

        if (!marginsExplicit
            && TryRenderPositionedForm(html, options, pageHeight) is { } pfDoc)
            return pfDoc;

        // Orphan-rowspan split-panel recovery (see SplitPanel.cs): a stray
        // `<td rowspan=…>` outside any table wraps a two-panel sidebar/main band.
        if (!marginsExplicit
            && TryRenderSplitPanel(html, options, pageWidth, pageHeight) is { } spDoc)
            return spDoc;

        if (!marginsExplicit
            && TryRenderBootstrapScreen(html, css, pageWidth, pageHeight) is { } bsDoc)
            return bsDoc;

        // Container-less Bootstrap ROWS pages (see BootstrapRows.cs): body-level
        // col-xs grids and panel-success cards on the Site.css-padded sheet.
        if (!marginsExplicit
            && TryRenderBootstrapRows(html, css, pageWidth, pageHeight) is { } brDoc)
            return brDoc;

        // The step-row DETABLE worksheet (see PrintPage.cs): flex step rows with
        // centred fixed-layout widget tables splitting across pages.
        if (!marginsExplicit
            && TryRenderStepRows(html, css, pageHeight) is { } srDoc)
            return srDoc;

        // The edge-to-edge SEGOE ALERT sheet (see BootstrapRows.cs): label/value
        // panels, the broken vehicle frame and the sensor grid at zero margins.
        if (TryRenderSegoeAlert(html, css, pageWidth, pageHeight) is { } saDoc)
            return saDoc;

        // The CJK ORDER REPORT (see PrintPage.cs): the vertical title, the
        // order-info box and the activity/infrastructure grids on the shipped
        // template's measured geometry.
        if (!marginsExplicit
            && TryRenderCjkOrderReport(html, css, pageHeight) is { } cjkDoc)
            return cjkDoc;

        // The D3 vertical-bar-chart export (see BarChart.cs): title/subtitle
        // bands over a flex row of inline SVGs drawn as vector fills and Times
        // text, clipped per svg viewport.
        // The fixed-band print-page idiom (see PrintPage.cs): position:fixed
        // header/footer bands repeating per sheet, .page divs breaking after
        // themselves, the @media body width sizing the sheet.
        if (TryRenderPrintPage(html, css, pageHeight) is { } ppDoc)
            return ppDoc;

        // Article-PDF exports (see ArticlePdf.cs): the .article-pdf__* class
        // namespace — the red title band, the float column pair, the wrapper's
        // outsized paddings that pace the document onto its four pages.
        if (TryRenderArticlePdf(html, css, marginLeft, marginRight, marginTop,
                marginBottom, pageWidth, pageHeight, options?.BasePath) is { } apDoc)
            return apDoc;

        // CSS multi-column containers (see CssColumns.cs): `columns: N` flows
        // the paragraphs down N balanced columns.
        if (!marginsExplicit
            && TryRenderCssColumns(html, css, pageWidth, pageHeight) is { } mcDoc)
            return mcDoc;

        // …and a container declaring its columns in its OWN style attribute
        // pours the flow down them page after page instead (ColumnFlow.cs).
        if (TryRenderInlineCssColumns(html, options, pageWidth, pageHeight) is { } icDoc)
            return icDoc;

        // A fixed-width monospace report dump lays out on a character grid
        // and grows the sheet to its widest unbreakable line (MonoReport.cs).
        if (TryRenderMonoReport(html, pageWidth, pageHeight) is { } mrDoc)
            return mrDoc;

        // A continuously-rendered rounded-corner report grid shrinks its
        // columns to min-content and grows the sheet to them (RadiusGrid.cs).
        if (TryRenderRadiusGrid(html, options, css, pageWidth, singlePageRealH) is { } rgDoc)
            return rgDoc;

        // A percent-width till-slip invoice fills its body box with tables
        // whose columns take their max-content share (SlipInvoice.cs).
        if (TryRenderSlipInvoice(html, options, css, pageWidth, pageHeight) is { } siDoc)
            return siDoc;

        // An RTL Word export anchors its lines on the content box's right edge
        // and takes its rhythm from the margins its paragraphs keep (RtlForm.cs).
        if (TryRenderRtlForm(html, pageWidth, pageHeight) is { } rfDoc)
            return rfDoc;

        // The eValidator validation report paginates off its own print sheet -
        // the Details half and every numbered section open a page of their own,
        // and the frames between them spill (ValidationReport.cs).
        if (TryRenderValidationReport(html, pageWidth, pageHeight, marginLeft,
                marginRight, marginTop, marginBottom) is { } vrDoc)
            return vrDoc;

        // The Closing Disclosure addendum takes its sheet from the print
        // stylesheet's own @page height and grows it to the contact grid's
        // minimum columns (ClosingDisclosure.cs).
        if (TryRenderClosingDisclosure(html) is { } cdDoc)
            return cdDoc;

        // The infrastructure-assessment report hangs every block off one
        // percentage-resolved content width and flows them across its own
        // landscape sheet (AuditReport.cs).
        if (TryRenderAuditReport(html) is { } auditDoc)
            return auditDoc;

        // Full-chain rules (id-anchored / child-combinator / 3+-part selectors the
        // flat map drops) for the lifted-table builds — null when the document has
        // none, which keeps every legacy path untouched.
        var docChainRules = ParseChainRules(html);

        // <body bgcolor=…> / body { background(-color) } tints the page canvas. White is
        // the canvas default, so an explicit white paints nothing. Later declarations win:
        // the presentational attribute first, then an inline style, then the stylesheet.
        Color? bodyBackground = null;
        var bodyOpen = Regex.Match(html, @"<body\b[^>]*>", RegexOptions.IgnoreCase);
        if (bodyOpen.Success)
        {
            var bgAttr = Regex.Match(bodyOpen.Value,
                @"\bbgcolor\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))", RegexOptions.IgnoreCase);
            if (bgAttr.Success)
                bodyBackground = ParseCssColor(bgAttr.Groups[1].Success ? bgAttr.Groups[1].Value
                    : bgAttr.Groups[2].Success ? bgAttr.Groups[2].Value : bgAttr.Groups[3].Value);
            var inlineBg = Regex.Match(DivStyleOf(bodyOpen.Value),
                @"background(?:-color)?\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
            if (inlineBg.Success && ParseCssColor(inlineBg.Groups[1].Value) is { } inlineColor)
                bodyBackground = inlineColor;
        }
        if (css.TryGetValue("body", out var bodyRule))
            foreach (var prop in new[] { "background", "background-color" })
                if (bodyRule.TryGetValue(prop, out var bgv) && ParseCssColor(bgv) is { } cssBg)
                    bodyBackground = cssBg;
        if (bodyBackground is { R: 255, G: 255, B: 255 }) bodyBackground = null;

        // Print-grid dialect (gated): a bootstrap-style report — .col-xs-N percent
        // column classes plus an @media print reset (* { color:#000 !important }).
        // The conversion runs under PRINT media: all text black, backgrounds
        // transparent (borders kept), the grid columns stacked as blocks that keep
        // their declared width as the wrap box, class-bordered divs framed, and the
        // page widened to the widest table plus the wrapper chrome.
        var printGrid = !marginsExplicit
            && css.ContainsKey(".col-xs-6")
            && css.TryGetValue("*", out var uniRule)
            && uniRule.TryGetValue("color", out var uniColor)
            && uniColor.Contains("#000") && uniColor.Contains("!important");
        double printGridBase = 0, printGridLineFactor = 1.15;
        if (printGrid)
        {
            // Wrapper chrome: a whole-content wrapper div's inline padding lands
            // inside the page margins on BOTH sides (the UA body margin is already
            // baked into the 96pt default; the right margin mirrors the left).
            double wrapPad = 0;
            var wpm = Regex.Match(html,
                @"<div\b[^>]*class\s*=\s*[""'][^""']*container[^""']*[""'][^>]*style\s*=\s*[""'][^""']*padding\s*:\s*(\d+(?:\.\d+)?)\s*px",
                RegexOptions.IgnoreCase);
            if (wpm.Success && double.TryParse(wpm.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var wrapPx))
                wrapPad = wrapPx * 0.75;
            marginLeft += wrapPad;
            marginRight = marginLeft;
            marginTop += wrapPad;
            if (css.TryGetValue("body", out var pgBody))
            {
                if (pgBody.TryGetValue("font-size", out var pgFs) && TryParseLength(pgFs, out var pgPt) && pgPt > 0)
                    printGridBase = pgPt;
                if (pgBody.TryGetValue("line-height", out var pgLh)
                    && double.TryParse(pgLh, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var pgLf)
                    && pgLf is > 0.5 and < 3)
                    printGridLineFactor = pgLf;
            }
            if (printGridBase <= 0) printGridBase = 12;
            // The first line box sits ~5pt lower than the legacy
            // first-baseline calibration under the metric model.
            marginTop += 5.0;
            // Heading bands: a ".cls hN { border-bottom: … }" descendant rule paints a
            // bar under headings inside a .cls div. The grid segmentation splits those
            // divs away from their headings, so resolve the ancestry HERE by
            // annotating each in-scope heading with a band="r,g,b|px|padpx" attribute.
            var bandKeys = new List<string>();
            foreach (var k in css.Keys) bandKeys.Add(k);
            foreach (var bandKey in bandKeys)
            {
                var bkm = Regex.Match(bandKey, @"^\.([\w-]+) (h[1-6])$");
                if (!bkm.Success || !css[bandKey].TryGetValue("border-bottom", out var bandDecl2)) continue;
                var bandCol = ParseCssColor(bandDecl2);
                if (bandCol is null) continue;
                var bwm = Regex.Match(bandDecl2, @"(\d+(?:\.\d+)?)\s*px");
                var bandPxV = bwm.Success ? bwm.Groups[1].Value : "1";
                var bandPadV = "0";
                if (css[bandKey].TryGetValue("padding-bottom", out var bandPadDecl))
                {
                    var bpm = Regex.Match(bandPadDecl, @"(\d+(?:\.\d+)?)");
                    if (bpm.Success) bandPadV = bpm.Groups[1].Value;
                }
                var attr = FormattableString.Invariant(
                    $" band=\"{bandCol.R},{bandCol.G},{bandCol.B}|{bandPxV}|{bandPadV}\"");
                var hostRx = new Regex(@"<div\b[^>]*class\s*=\s*[""'][^""']*\b"
                    + Regex.Escape(bkm.Groups[1].Value) + @"\b[^""']*[""'][^>]*>", RegexOptions.IgnoreCase);
                var hTag = bkm.Groups[2].Value;
                var hosts = new List<Match>();
                foreach (Match hm in hostRx.Matches(html)) hosts.Add(hm);
                for (var hi = hosts.Count - 1; hi >= 0; hi--)
                {
                    var contentStart = hosts[hi].Index + hosts[hi].Length;
                    if (FindDivEnd(html, contentStart, out var hostEnd) < 0) continue;
                    var region = html[contentStart..hostEnd];
                    region = Regex.Replace(region, "<" + hTag + @"\b", "<" + hTag + attr, RegexOptions.IgnoreCase);
                    html = html[..contentStart] + region + html[hostEnd..];
                }
            }
        }

        // Styled-class data-font flow (gated): a stylesheet that embeds its faces as
        // data: URIs and styles a flat classed-paragraph body (the EDGAR TSR report
        // shape) renders through the styled HTML engine. Default page
        // setup only — explicit PageInfo/margins keep the legacy flow.
        if (!(pageMargin?.IsTouched ?? false)
            && (pageInfo is null || (pageInfo.Width == 595 && pageInfo.Height == 842))
            && html.IndexOf("@font-face", StringComparison.OrdinalIgnoreCase) >= 0
            && TryParseStyledDataFontDoc(html, out var styledBody)
            && RenderStyledDataFontDoc(styledBody) is { } styledDoc)
        {
            var styledTitle = Regex.Match(html, @"<title[^>]*>(.*?)</title>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (styledTitle.Success)
                styledDoc.Info.Title = DecodeEntities(styledTitle.Groups[1].Value).Trim();
            return styledDoc;
        }

        // EDGAR filing dialect (gated): stylesheet-less inline-styled filings with
        // explicit page-break paragraphs, beveled-rule + h5 page headers and named
        // TOC anchors render through the dedicated line-box-density flow engine.
        // Default page setup only — explicit PageInfo/margins keep the legacy flow.
        if (!(pageMargin?.IsTouched ?? false)
            && (pageInfo is null || (pageInfo.Width == 595 && pageInfo.Height == 842))
            && EdgarHtmlRenderer.IsEdgarFilingDoc(html)
            && EdgarHtmlRenderer.TryConvert(html, options) is { } edgarDoc)
        {
            var edgarTitle = Regex.Match(html, @"<title[^>]*>(.*?)</title>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (edgarTitle.Success)
                edgarDoc.Info.Title = DecodeEntities(edgarTitle.Groups[1].Value).Trim();
            return edgarDoc;
        }

        // body{margin:0}: the default 90pt side margins / 72pt content top
        // apply verbatim — the usual defaults (96/89) bake in the browser's 8px body
        // margin and the default first-baseline drop, which this page has switched off.
        var bodyZeroMargin = false;
        // …and the body tag's OWN inline style declares it just as well: `<body
        // style="margin: 0px">` is the same statement as `body { margin: 0 }`, and
        // reading only the stylesheet left such a page on the default top margin — its
        // whole document then sat 17 pt below the box its background paints.
        string? bodyMargin = null;
        if (css.TryGetValue("body", out var bodyDecls))
            bodyDecls.TryGetValue("margin", out bodyMargin);
        if (bodyMargin is null
            && Regex.Match(html, @"<body\b[^>]*style\s*=\s*(['""])([^'""]*)\1",
                RegexOptions.IgnoreCase) is { Success: true } bodyTagStyle
            && Regex.Match(bodyTagStyle.Groups[2].Value, @"(?<![-\w])margin\s*:\s*([^;]+)",
                RegexOptions.IgnoreCase) is { Success: true } bodyTagMargin)
            bodyMargin = bodyTagMargin.Groups[1].Value;
        // …and a universal reset (`* { margin: 0 }`) zeroes the body margin with
        // everything else — the same statement again.
        if (bodyMargin is null && css.TryGetValue("*", out var starDecls))
            starDecls.TryGetValue("margin", out bodyMargin);
        if (!marginsExplicit && bodyMargin is not null
            // "0", "0px", or an all-zero shorthand list ("0 0 0 0").
            && Regex.IsMatch(bodyMargin.Trim(), @"^0(px)?(\s+0(px)?){0,3}$"))
        {
            bodyZeroMargin = true;
            marginLeft = 90.0;
            marginRight = 90.0;
            marginTop = 72.0;
        }

        // A NON-zero `body { margin }` insets the content box on the LEFT. Only the
        // left: the widened sheet ends exactly one page margin past the last ink, so
        // the body's right margin never gets to push anything (the same asymmetry the
        // ink-widen rule below encodes). Resolved against the body's own declared
        // font size, the em a browser would use.
        var bodyMarginLeftPt = 0.0;
        if (!marginsExplicit && !bodyZeroMargin && css.TryGetValue("body", out var bodyBoxDecls)
            && bodyBoxDecls.TryGetValue("margin", out var bodyMarginV))
        {
            var bodyEmPt = bodyBoxDecls.TryGetValue("font-size", out var bodyEmV)
                && TryParseLength(bodyEmV, out var bodyEmParsed) && bodyEmParsed > 0
                ? bodyEmParsed : DefaultBodyFontPt;
            bodyMarginLeftPt = ChainPadPt(bodyMarginV, bodyEmPt).L;
        }

        // A stylesheet that positions the page itself also owns the document's base
        // text size: its `body { font-size }` seeds the cell grids, where the legacy
        // 11pt default would otherwise stand in. Only the size the BODY rule declares —
        // a table/td rule still wins the cascade inside BuildTableFromHtml.
        var bodyCssFontPt = 0.0;
        string? bodyCssFace = null;
        Color? bodyCssColor = null;
        if (bodyZeroMargin && css.TryGetValue("body", out var bodyFontDecls)
            && bodyFontDecls.TryGetValue("font-size", out var bodyFontSize)
            && TryParseLength(bodyFontSize, out var bodyFontPt) && bodyFontPt > 0)
        {
            bodyCssFontPt = bodyFontPt;
            // …its colour, which every block inherits (these pages set a soft grey where
            // our default is black — a visibly heavier ink)…
            if (bodyFontDecls.TryGetValue("color", out var bodyColorV))
                bodyCssColor = ParseCssColor(bodyColorV);
            // …and the first INSTALLED face of the stack that rule names. It carries the
            // document's real `line-height: normal` box, and it marks the cell grids as
            // CSS line boxes so a run's own size governs its own pitch.
            if (bodyFontDecls.TryGetValue("font-family", out var bodyFontFam))
                foreach (var fam in bodyFontFam.Split(','))
                {
                    var f = fam.Trim().Trim('"', '\'');
                    if (f.Length > 0 && WinMetricsFor(f) is not null) { bodyCssFace = f; break; }
                }
        }

        // Quirks-mode CSS-run documents: a resolvable body face but NO <!DOCTYPE>
        // (CKEditor notes, Outlook/Teams exports). Two behaviours hang off this:
        // their tables render at the UA 16px cell base through the metric layouter
        // (the body rule's pixel font does not inherit into cells in quirks mode),
        // and their text honours inline-block title columns and dash-break
        // overflow wrapping (both measured on the references).
        var quirksCssRun = bodyCssFace is not null
            && !Regex.IsMatch(html, @"<!doctype", RegexOptions.IgnoreCase);

        // Title-column stylesheets (the Outlook/Teams export shape): a class rule
        // declaring display:inline-block WITH a width marks label columns — the
        // label text is its own run and the value seats at the column's right
        // edge. Such documents also wrap their plain-text sections on the
        // dash-overflow model (see quirksWrapW below).
        var inlineBlockColRules = false;
        foreach (var ibkv in css)
            if (ibkv.Key.StartsWith('.')
                && ibkv.Value.TryGetValue("display", out var ibd)
                && ibd.Trim().Equals("inline-block", StringComparison.OrdinalIgnoreCase)
                && ibkv.Value.TryGetValue("width", out var ibwv)
                && TryParseLength(ibwv, out var ibwPt2) && ibwPt2 > 0)
            { inlineBlockColRules = true; break; }

        // CSS-faithful metric flow (gated): a stylesheet that positions the page itself —
        // a BODY rule carrying a non-zero margin box — marks print-oriented HTML (MSHTML
        // "saved from" reports and the like) whose layout is reproduced from
        // the CSS itself: the body margin box adds to the page margins (top on the first
        // page only), line height is the browser rule round(px·(winAsc+winDesc)/em) with
        // half-leading baselines, MARGIN-LEFT class indents are honored, a <br> is one
        // full line box, and tables use real cellspacing/cellpadding geometry. Every
        // other document keeps the legacy calibrated flow byte-for-byte. Requires the
        // body font family to resolve to a real face (its win metrics drive the model).
        var metricFlow = false;
        double bodyMarT = 0;
        string metricFace = "";
        if (marginsExplicit && css.TryGetValue("body", out var mfBody)
            && mfBody.TryGetValue("margin", out var mfMargin)
            && TryParseCssMarginBox(mfMargin, out var mfBox)
            && (mfBox.top > 0 || mfBox.left > 0 || mfBox.right > 0))
        {
            var mfFam = mfBody.TryGetValue("font-family", out var mff) ? FirstFontFamily(mff) : null;
            if (mfFam is not null && WinMetricsFor(mfFam) is not null)
            {
                metricFlow = true;
                metricFace = mfFam;
                marginLeft += mfBox.left;
                marginRight += mfBox.right;
                bodyMarT = mfBox.top;
            }
        }

        // Inline-styled body-margin sheets (gated): the margin box lives on the
        // BODY tag itself (em longhands) and the family on the <html> tag — no
        // stylesheet body rule exists for the standard metric gate above. The
        // metric flow lays these out with the face's real advances; their em
        // margins resolve against the UA 16px (12 pt) default (the body declares
        // no font-size of its own), and their line boxes pace on the face's hhea
        // line gap (Times New Roman: 17px lines at 11 pt where the win sum's
        // 16px stands a half-line short by mid-page — measured on the reference).
        var bodyBoxGridDoc = false;
        double metricLineSum = 0;
        if (!metricFlow && marginsExplicit && !css.ContainsKey("body")
            && Regex.Match(html, @"<body\b[^>]*style\s*=\s*(?:""([^""]*)""|'([^']*)')",
                RegexOptions.IgnoreCase) is { Success: true } ibBodyStyle)
        {
            var ibDecl = ibBodyStyle.Groups[1].Success
                ? ibBodyStyle.Groups[1].Value : ibBodyStyle.Groups[2].Value;
            var ibBox = ParseInlineMarginBox(ibDecl, DefaultBodyFontPt);
            string? ibFam = null;
            var ibFamM = Regex.Match(ibDecl, @"font-family\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
            if (ibFamM.Success) ibFam = FirstFontFamily(ibFamM.Groups[1].Value);
            if (ibFam is null
                && Regex.Match(html, @"<html\b[^>]*style\s*=\s*(?:""([^""]*)""|'([^']*)')",
                    RegexOptions.IgnoreCase) is { Success: true } ibHtmlStyle
                && Regex.Match(ibHtmlStyle.Groups[1].Success
                        ? ibHtmlStyle.Groups[1].Value : ibHtmlStyle.Groups[2].Value,
                    @"font-family\s*:\s*([^;]+)",
                    RegexOptions.IgnoreCase) is { Success: true } ibHtmlFam)
                ibFam = FirstFontFamily(ibHtmlFam.Groups[1].Value);
            if ((ibBox.left > 0 || ibBox.top > 0 || ibBox.right > 0)
                && ibFam is not null && WinMetricsFor(ibFam) is not null)
            {
                metricFlow = true;
                bodyBoxGridDoc = true;
                metricFace = ibFam;
                marginLeft += ibBox.left;
                marginRight += ibBox.right;
                bodyMarT = ibBox.top;
                metricLineSum = HheaLineSumFor(ibFam) ?? 0;
                // These sheets separate blocks with INVALID `</br>` tags — a
                // browser (and the reference) treats each as a line break.
                html = Regex.Replace(html, @"</br\s*>", "<br>", RegexOptions.IgnoreCase);
            }
        }

        // Print-grid dialect: metric layout in the sans body face (the CSS
        // "Helvetica Neue"/Helvetica stack renders with Arial advances), CSS
        // line-height line boxes, standard-14 Helvetica output resources.
        if (printGrid && !metricFlow && WinMetricsFor("Arial") is not null)
        {
            metricFlow = true;
            metricFace = "Arial";
        }

        // UA-default metric flow (gated): a STYLESHEET-LESS MSHTML export is laid out
        // from pure user-agent defaults — serif (Times New Roman) at the
        // 16px base, 1.125em line boxes with win-metric half-leading baselines, real
        // paragraph/heading gaps, 90/72pt page margins with the 8px body margin (6pt)
        // inside them (left on every page, top on page 1 only), and the page WIDTH
        // widened to fit a block image at its natural pixel size (see grp/T
        // notes); every other document
        // keeps its existing flow byte-for-byte.
        var uaMshtml = css.Count == 0 && !metricFlow
            && Regex.IsMatch(html,
                @"<meta\b[^>]*\bname\s*=\s*[""']?generator\b[""']?[^>]*\bcontent\s*=\s*[""']?MSHTML",
                RegexOptions.IgnoreCase)
            && WinMetricsFor("Times New Roman") is not null;

        // Full-document UA-default flow: a complete <html> document that declares NO
        // font-family anywhere (inline style, <font>, or stylesheet) inherits the source
        // renderer's UA stylesheet — Times serif, 2em/1.5em… headings, browser block gaps
        // and line boxes — the same model MSHTML exports use, extended to any font-family-
        // free full document (CSS colours and other rules still apply through the flow).
        // Its text draws with the Standard-14 serif faces, so nothing is embedded; a bare
        // fragment (no <html>/<body>) or a table document keeps the legacy calibrated flow.
        // Only pure UA-default documents qualify: the stylesheet may tint text
        // (color/background) but must not drive LAYOUT — any margin/width/position/
        // content/font rule means the page relies on authored geometry the legacy flow
        // is calibrated to, so forcing it through the UA metric flow would move it.
        // A rule whose selector matches nothing in the document cannot drive layout —
        // generated pages ship dormant style blocks (an unused .jumbotron kit) that
        // must not disqualify the UA-default flow. Presence is judged by the
        // selector's LAST simple selector: its class, id, or element type.
        bool SelectorUsed(string sel)
        {
            var last = sel.Trim();
            var sp = last.LastIndexOfAny(new[] { ' ', '>', '+', '~' });
            if (sp >= 0) last = last[(sp + 1)..].Trim();
            if (last.Length == 0) return true;
            if (last[0] == '.')
                return Regex.IsMatch(html,
                    @"class\s*=\s*[""'][^""']*\b" + Regex.Escape(last[1..]) + @"\b",
                    RegexOptions.IgnoreCase);
            // tag.class — the class decides presence: "br.altova-page-break"
            // matches only elements CARRYING the class, so a class nobody uses
            // cannot disqualify the flow no matter how common the tag is.
            if (last.IndexOf('.') > 0)
            {
                var cls = last[(last.IndexOf('.') + 1)..].Split('.')[0];
                return cls.Length > 0 && Regex.IsMatch(html,
                    @"class\s*=\s*[""'][^""']*\b" + Regex.Escape(cls) + @"\b",
                    RegexOptions.IgnoreCase);
            }
            if (last[0] == '#')
                return Regex.IsMatch(html,
                    @"id\s*=\s*[""']?" + Regex.Escape(last[1..]) + @"\b",
                    RegexOptions.IgnoreCase);
            var tagOnly = Regex.Match(last, @"^[A-Za-z][A-Za-z0-9]*").Value;
            if (tagOnly.Length == 0) return true;
            return Regex.IsMatch(html, @"<" + Regex.Escape(tagOnly) + @"\b", RegexOptions.IgnoreCase);
        }
        // (moved up: the css-layout scan below needs it)
        // Only the SIDE margins are authored (top/bottom keep the renderer
        // defaults) — a caller who sets all four sides authored a full custom
        // sheet, not an edge-to-edge one, and keeps the plain UA margin model.
        var edgeToEdgePre = (pageMargin?.IsTouched ?? false) && pageMargin!.HtmlPerSideDefaults
            && pageMargin.LeftTouched && pageMargin.RightTouched
            && !pageMargin.TopTouched && !pageMargin.BottomTouched
            && pageMargin.Left < 1e-9 && pageMargin.Right < 1e-9;
        // Body content that is entirely tables (whitespace aside): every styled
        // div/b/span then sits inside a table cell, so their class rules feed the
        // metric table renderer rather than the flow.
        var bodyAllTables = false;
        {
            var bodyM = Regex.Match(html, @"<body\b[^>]*>([\s\S]*?)</body", RegexOptions.IgnoreCase);
            var bodyHtml = bodyM.Success ? bodyM.Groups[1].Value : html;
            var sansT = Regex.Replace(bodyHtml, @"<table\b[\s\S]*?</table\s*>", "",
                RegexOptions.IgnoreCase);
            bodyAllTables = Regex.IsMatch(bodyHtml, @"<table\b", RegexOptions.IgnoreCase)
                && CollapseWs(DecodeEntities(Regex.Replace(sansT, "<[^>]+>", " ")))
                    .Trim().Length == 0;
        }
        // A selector is TABLE-SCOPED when its rules can only reach table content —
        // the metric table renderer owns those, so they neither disqualify the
        // UA flow nor make the document "authored-family" (cssRealFamily below).
        bool TableScopedSelector(string sel, IReadOnlyDictionary<string, string> decls)
        {
            // Authored-margin documents (beyond the edge-to-edge zero-margin
            // dialect) were calibrated on the legacy flow — their table skins
            // must keep disqualifying it.
            if (marginsExplicit && !edgeToEdgePre) return false;
            sel = sel.Trim();
            var selParts = sel.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (selParts.Length == 0) return false;
            var last = selParts[^1];
            var lastTag = last.Split('.')[0].ToLowerInvariant();
            if (lastTag is "table" or "td" or "th" or "tr" or "img") return true;
            string? scopeCls = null;
            if (last.StartsWith('.'))
                scopeCls = last[1..].Split('.')[0];
            // ".rc6 div" — a div/span/b/p under a table-scoped class ancestor is
            // itself table content in an all-table body.
            else if (selParts.Length > 1 && selParts[0].StartsWith('.') && bodyAllTables
                && lastTag is "div" or "span" or "b" or "p")
                scopeCls = selParts[0][1..].Split('.')[0];
            if (scopeCls is null || scopeCls.Length == 0) return false;
            var clsUses = Regex.Matches(html,
                @"<(\w+)\b[^>]*class\s*=\s*[""'][^""']*\b" + Regex.Escape(scopeCls) + @"\b",
                RegexOptions.IgnoreCase);
            if (clsUses.Count == 0) return false;
            // A div/b/span/p carrying the class is table content only when it
            // sits INSIDE a table (the boleto's in-cell skins); a wrapper div
            // AROUND the tables (the official-letter .Content) keeps its
            // calibrated flow.
            bool InsideTable(int pos)
            {
                var depth = 0;
                foreach (Match tm in Regex.Matches(html[..pos], @"<(/?)table\b",
                    RegexOptions.IgnoreCase))
                    depth += tm.Groups[1].Value.Length == 0 ? 1 : -1;
                return depth > 0;
            }
            return clsUses.All(u => u.Groups[1].Value.ToLowerInvariant()
                    is "table" or "td" or "th" or "tr" or "tbody" or "thead" or "tfoot"
                || (u.Groups[1].Value.ToLowerInvariant() is "div" or "b" or "span" or "p"
                    && InsideTable(u.Index)));
        }
        var cssLayoutFree = true;
        foreach (var kv in css)
        {
            // @page / @media at-rules do not drive this converter's layout (the
            // source renderer keeps its UA margins under an authored @page —
            // measured: the sheet's 0.6in @page margins render at the
            // standard 96pt content origin), so they cannot disqualify the flow.
            if (kv.Key.TrimStart().StartsWith('@')) continue;
            if (!SelectorUsed(kv.Key)) continue;
            // The flow's own margin machinery owns body margins, and a universal
            // zero reset only zeroes them — neither authors layout beyond what
            // the body-margin model already renders.
            if (kv.Key.Trim() is "body" or "*"
                && kv.Value.Keys.All(pk => pk is "color" or "background-color" or "background"
                    || pk.StartsWith("margin", StringComparison.Ordinal)
                    || pk.StartsWith("padding", StringComparison.Ordinal)))
                continue;
            // Table-scoped rules feed the metric TABLE renderer — they never
            // drive the FLOW, so they must not disqualify it: a rule whose last
            // simple selector is a table part, or whose CLASS the document uses
            // only on table tags (a `.collapseBorderTable` skin), rides along.
            if (TableScopedSelector(kv.Key, kv.Value)) continue;
            // Authored-margin documents: a bare STRUCTURAL table-part rule
            // (table/td/th/tr) still feeds the table renderer, not the flow —
            // the margin guard inside TableScopedSelector protects the legacy
            // class-skin dialects, not these parts.
            {
                var lfParts = kv.Key.Trim().Split((char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries);
                if (lfParts.Length > 0
                    && lfParts[^1].Split('.')[0].ToLowerInvariant()
                        is "table" or "td" or "th" or "tr") continue;
            }
            // A PAINTED-BOX rule — a visible background over a declared width ×
            // height, with nothing but box decoration alongside — renders as a
            // box IN the UA flow (the BgBox model): it authors a box the flow
            // already draws, not flow-driving geometry.
            if ((kv.Value.ContainsKey("background-color") || kv.Value.ContainsKey("background"))
                && kv.Value.ContainsKey("width") && kv.Value.ContainsKey("height")
                && kv.Value.Keys.All(pk => pk is "background-color" or "background" or "color"
                    or "width" or "height" or "min-height"
                    || pk.StartsWith("border", StringComparison.Ordinal)
                    || pk.StartsWith("margin", StringComparison.Ordinal)
                    || pk.StartsWith("padding", StringComparison.Ordinal)))
                continue;
            // A full-width container rule (width:100% with float/position/
            // overflow riders) is a plain block wrapper — a 100%-wide float
            // never floats and its overflow clips nothing the flow draws.
            if (kv.Value.TryGetValue("width", out var fwCont) && fwCont.Trim() == "100%"
                && kv.Value.Keys.All(pk => pk is "width" or "float" or "position"
                    or "overflow" or "color" or "background-color" or "background"
                    || pk.StartsWith("margin", StringComparison.Ordinal)
                    || pk.StartsWith("padding", StringComparison.Ordinal)))
                continue;
            foreach (var prop in kv.Value.Keys)
            {
                // Properties that cannot pull the document onto authored geometry keep
                // it UA-default: tints; `transform`/`filter` (transform is applied to
                // the element it decorates, never to the flow); `display` (none is
                // suppressed and a block-span breaks its line in this flow — both
                // UA-level behaviours, not authored geometry); and vendor-mangled
                // debris (a leading dash or an embedded space — "-webkit - transform")
                // that no engine would honour.
                if (prop is "color" or "background-color" or "background"
                    or "transform" or "filter" or "display"
                    // font-family cannot drive LAYOUT by itself; whether a
                    // declared face disqualifies the UA flow is the separate
                    // resolvable-family check below.
                    or "font-family"
                    // …and the metric flow HONOURS class typography (font-size,
                    // weight, centring) and page breaks — a class styled this
                    // way is rendered, not a reason to abandon the flow. clear
                    // only matters to float layouts, which are opt-in.
                    or "font" or "font-size" or "font-weight" or "font-style"
                    or "text-align" or "white-space"
                    // box-sizing switches a model neither flow implements —
                    // inert either way
                    or "box-sizing"
                    or "page-break-after" or "page-break-before" or "clear"
                    // height on a class = a spacer the flow already honours
                    // through ExplicitHeight (the clear-both float terminator).
                    or "height" or "min-height" or "vertical-align") continue;
                // a border declared NONE draws nothing — inert
                if (prop is "border" or "border-style"
                    && kv.Value[prop].Contains("none", StringComparison.OrdinalIgnoreCase)) continue;
                if (prop.Length == 0 || prop[0] == '-' || prop.Contains(' ')) continue;
                // `margin: 0 auto` (any mix of zeros and autos) authors no flow
                // geometry — auto centres a box no wider than the content band,
                // zero is the reset.
                if (prop == "margin"
                    && Regex.IsMatch(kv.Value[prop].Trim(), @"^(?:(?:0|auto)\s+)*(?:0|auto)$",
                        RegexOptions.IgnoreCase)) continue;
                // Vertical margins on a rule the block-margin override applies
                // (the h1/p margin resets of the order-ticket family) render in
                // the flow — they do not disqualify it.
                if (prop is "margin-top" or "margin-bottom") continue;
                // A max-width at or beyond the UA content band cannot clamp
                // anything on this sheet — it is inert for the flow.
                if (prop == "max-width" && TryParseLength(kv.Value[prop].Trim(), out var mwInert)
                    && mwInert >= pageWidth - 96.0 - 72.0) continue;
                cssLayoutFree = false; break;
            }
            if (!cssLayoutFree) break;
        }
        // Unresolved external stylesheets (InlineLinkedStylesheets leaves the <link>
        // tags of stylesheets it could not fetch): the converter falls back to
        // pure UA defaults for such documents — tables included, they lay out through
        // the metric table renderer. Only ABSOLUTE http(s) links qualify: those are
        // unreachable at render time by design, whereas an unresolved RELATIVE
        // link is a packaging gap — the sheet was present when the document
        // was authored, so the document must keep the legacy calibrated flow.
        var deadExternalCss = Regex.IsMatch(html,
            @"<link\b[^>]*rel\s*=\s*[""']?stylesheet[^>]*href\s*=\s*[""']?https?://",
            RegexOptions.IgnoreCase)
            || Regex.IsMatch(html,
                @"<link\b[^>]*href\s*=\s*[""']?https?://[^>]*rel\s*=\s*[""']?stylesheet",
                RegexOptions.IgnoreCase)
            // The sectioned .pdf-page report with the sp-matrix diagram: its
            // relative stylesheet is genuinely absent for the reference renderer
            // too — the reference lays the document out in pure UA defaults, so
            // it joins the dead-CSS class despite the relative link. Only the
            // default-margin conversion: the report variant whose caller authors
            // page margins was calibrated green on the legacy flow and keeps it.
            || (!marginsExplicit
                && html.Contains("pdf-page", StringComparison.Ordinal)
                && html.Contains("diagram-sp-matrix", StringComparison.Ordinal));
        // A document with no markup at all (a plain-text file fed through
        // HtmlLoadOptions) has nothing to disqualify it: it renders in pure UA
        // defaults exactly like a font-family-free <html><body> document.
        var tagFreeDoc = !Regex.IsMatch(html, @"<[A-Za-z/!?]");
        // A caller who zeroes BOTH side margins on the default PageInfo authored an
        // edge-to-edge sheet: such a document keeps the UA flow WITH its tables (the
        // metric table renderer draws them as real grids) — the table exclusion below
        // protects only the legacy calibrated flow, which these pages never had.
        var edgeToEdgeDoc = edgeToEdgePre;
        // Table interiors are excluded from the font scans: the metric table
        // renderer applies <font face/size> tags and inline font-family cell
        // styling itself, so they disqualify the UA flow only on FLOW text.
        var htmlSansTables = Regex.IsMatch(html, @"<font\b|font-family", RegexOptions.IgnoreCase)
            ? Regex.Replace(html, @"<table\b[\s\S]*?</table\s*>", "", RegexOptions.IgnoreCase)
            : html;
        // A stylesheet family disqualifies the flow only when a USED rule names a
        // face that actually RESOLVES — a quoted junk family ("ARIAL,HELVETICA,
        // SANS-SERIFF" is one literal name) falls back to the UA default exactly
        // like no declaration at all.
        var cssRealFamily = false;
        foreach (var kv in css)
            if (!kv.Key.TrimStart().StartsWith('@') && SelectorUsed(kv.Key)
                && !TableScopedSelector(kv.Key, kv.Value)
                && kv.Value.TryGetValue("font-family", out var ffDecl)
                && FirstFontFamily(ffDecl) is { } ffName
                // A comma INSIDE the single (quoted) name is the junk-family
                // idiom — no real face carries one, whatever the repository's
                // lenient lookup happens to match it to.
                && !ffName.Contains(',')
                && WinMetricsFor(ffName) is not null)
            { cssRealFamily = true; break; }
        // A document whose ONLY resolvable family comes from the BODY rule, at the
        // UA 16px base size, keeps UA structure wholesale: the rule swaps the face
        // under the same metrics (probed on the step-row sheet — a `body
        // { font-family: Arial; font-size: 12pt }` renders the UA line grid at
        // 13.5 with the UA paragraph margins, in Arial). Such a document rides
        // the UA flow with the body face as its metric/run face.
        string? uaBodyFace = null;
        {
            var realFamilyOutsideBody = false;
            foreach (var kv in css)
                if (!kv.Key.TrimStart().StartsWith('@') && SelectorUsed(kv.Key)
                    && !TableScopedSelector(kv.Key, kv.Value)
                    && !kv.Key.Trim().Equals("body", StringComparison.OrdinalIgnoreCase)
                    // a bare element-TAG rule's resolvable face rides its
                    // blocks (h6 { font-family: Verdana } styles the h6s, not
                    // the flow) — it does not disqualify the UA structure
                    && !(Regex.IsMatch(kv.Key.Trim(), @"^[a-zA-Z]+[1-6]?$")
                         && kv.Value.TryGetValue("font-family", out var tagFamDecl)
                         && FirstFontFamily(tagFamDecl) is { } tagFamName
                         && WinMetricsFor(tagFamName) is not null)
                    && kv.Value.TryGetValue("font-family", out var nbDecl)
                    && FirstFontFamily(nbDecl) is { } nbName && !nbName.Contains(',')
                    && WinMetricsFor(nbName) is not null)
                { realFamilyOutsideBody = true; break; }
            const double uaBasePt = 12.0;   // the UA 16px root, in pt
            if (!realFamilyOutsideBody
                && css.TryGetValue("body", out var uaBodyRule)
                && uaBodyRule.TryGetValue("font-family", out var uaBodyFam)
                && FirstFontFamily(uaBodyFam) is { } uaBodyName && !uaBodyName.Contains(',')
                && WinMetricsFor(uaBodyName) is not null
                // an explicit UA-base size, or none at all (the rule pins only
                // the face — the size stays the UA 16px root)
                && (!uaBodyRule.TryGetValue("font-size", out var uaBodyFsV)
                    || (TryParseLength(uaBodyFsV, out var uaBodyFsPt)
                        && Math.Abs(uaBodyFsPt - uaBasePt) < 0.01)))
                uaBodyFace = uaBodyName;
            // The same probe read through a DIV rule: a document whose only
            // family declaration is a div-scoped STACK, sized at the UA base
            // (no font-size at all), takes the stack's first RESOLVABLE member
            // as its face — the source renderer walks the stack (calibri out of
            // "AvenirNext LT Com Regular", "Helvetica Neue", calibri) and keeps
            // the UA structure under it.
            if (uaBodyFace is null && !realFamilyOutsideBody && !css.ContainsKey("body")
                && css.TryGetValue("div", out var uaDivRule)
                && !uaDivRule.ContainsKey("font-size")
                && uaDivRule.TryGetValue("font-family", out var uaDivFam))
                foreach (var uaDivName in uaDivFam.Split(','))
                {
                    // INSTALLED faces only — the substitution aliasing that
                    // resolves "Helvetica Neue" to Arial must not stop the walk
                    // before the stack's first really-present member.
                    var cand = uaDivName.Trim().Trim('"', '\'');
                    if (cand.Length > 0 && Text.FontRepository.FaceInstalled(cand)
                        && WinMetricsFor(cand) is not null)
                    { uaBodyFace = cand; break; }
                }
        }
        // The absolute-span LEDGER: a table-less stylesheet whose ONLY
        // layout-authoring properties lay label/value columns — display:block
        // rows, margin-left labels, position:absolute+left value columns,
        // class widths — authors geometry the UA flow implements directly,
        // so such a document renders in UA defaults with those mechanisms
        // rather than the legacy calibrated flow.
        var absSpanLedger = false;
        if (!cssLayoutFree && !Regex.IsMatch(html, @"<table\b", RegexOptions.IgnoreCase))
        {
            var ledgerAbs = false;
            var ledgerOk = true;
            foreach (var kv in css)
            {
                if (kv.Key.TrimStart().StartsWith('@') || !SelectorUsed(kv.Key)) continue;
                if (kv.Value.TryGetValue("position", out var lgPos)
                    && lgPos.Contains("absolute", StringComparison.OrdinalIgnoreCase)
                    && kv.Value.ContainsKey("left"))
                    ledgerAbs = true;
                foreach (var prop in kv.Value.Keys)
                    if (prop is not ("display" or "text-align" or "font-weight" or "font-size"
                        or "margin-left" or "width" or "position" or "left" or "text-decoration"
                        or "border-width" or "color" or "background-color" or "background"))
                    { ledgerOk = false; break; }
                if (!ledgerOk) break;
            }
            absSpanLedger = ledgerAbs && ledgerOk;
        }
        // The FIELDSET WORKSHEET: a %-width padded BODY around <fieldset>/<legend>
        // sections of class-labelled grids — the UA flow renders it with the body
        // box offsets and fieldset frames (probed: content x 129 = 90 + the 2px
        // margin + 50px padding, frames 0.75 gray at the body's 70% content box).
        var fieldsetDoc = false;
        var fsBodyPct = 0.0;
        var fsBodyChromePt = 0.0;
        if (!metricFlow
            && Regex.IsMatch(html, @"<fieldset\b", RegexOptions.IgnoreCase)
            && Regex.IsMatch(html, @"<legend\b", RegexOptions.IgnoreCase)
            && css.TryGetValue("body", out var fsBodyRule)
            && fsBodyRule.TryGetValue("width", out var fsBodyW)
            && fsBodyW.Trim().EndsWith("%", StringComparison.Ordinal)
            && fsBodyRule.ContainsKey("padding"))
        {
            fieldsetDoc = true;
            fsBodyPct = double.Parse(Regex.Match(fsBodyW, @"[\d.]+").Value,
                System.Globalization.CultureInfo.InvariantCulture) / 100.0;
            var fsPadPt = fsBodyRule.TryGetValue("padding", out var fsPadV)
                && TryParseLength(fsPadV.Trim(), out var fsPadParsed) ? fsPadParsed : 37.5;
            var fsMarPt = fsBodyRule.TryGetValue("margin", out var fsMarV)
                && TryParseLength(fsMarV.Trim(), out var fsMarParsed) ? fsMarParsed : 1.5;
            fsBodyChromePt = fsPadPt + fsMarPt;
        }
        // Word-filtered TEXT pages (meta Generator "Microsoft Word N (filtered)",
        // no tables — the tabled forms take the MsoForm dialect): their styling is
        // all inline (pt sizes, % line-heights, span faces), which the UA flow
        // renders directly, so the inline-face disqualifier below does not apply.
        var msoFilteredDoc = Regex.IsMatch(html,
                @"<meta\s+name=[""']?Generator[""']?\s+content=[""']?Microsoft Word [^>]*\(filtered[^)>]*\)",
                RegexOptions.IgnoreCase)
            && !Regex.IsMatch(html, @"<table\b", RegexOptions.IgnoreCase);
        // A QUIRKS document whose stylesheets exist to load UNRESOLVABLE custom
        // faces (@font-face) renders in pure UA defaults: the source renderer
        // ignores their layout wholesale (probed on the Zero-Trust report — TNR
        // 12 on the UA grid at the explicit margins, every class padding and
        // margin inert).
        var customFontFaceDoc = !cssRealFamily
            && Regex.IsMatch(html, @"@font-face", RegexOptions.IgnoreCase)
            && !Regex.IsMatch(html, @"<!doctype", RegexOptions.IgnoreCase)
            && !Regex.IsMatch(html, @"<table\b", RegexOptions.IgnoreCase);
        // …and NOTHING from those sheets applies — no floats, no class boxes,
        // no typography (the reference draws the whole report in the UA face at
        // the UA sizes). Every downstream consumer sees an empty rule map.
        if (customFontFaceDoc)
            css = new Dictionary<string, Dictionary<string, string>>(
                StringComparer.OrdinalIgnoreCase);
        var uaNoFontDoc = !metricFlow && !uaMshtml
            // A Word-filtered page's Mso style-definitions sheet (the MsoNormal
            // margin resets, the hyperlink colours, the @page section) is part
            // of the filtered idiom the UA flow renders — it does not disqualify.
            && (cssLayoutFree || msoFilteredDoc || absSpanLedger || fieldsetDoc
                || customFontFaceDoc)
            // A <font> tag only affects the flow through its FACE/SIZE attributes — a
            // bare <font color="…"> leaves the document font-family-free. A body
            // rule pinning a face at the UA base size keeps UA structure (the
            // uaBodyFace arm above) and stays in — and a Word-filtered page's
            // Mso sheet families are the filtered idiom itself, applied inline.
            && (!cssRealFamily || msoFilteredDoc || uaBodyFace is not null)
            && (msoFilteredDoc
                || !Regex.IsMatch(htmlSansTables, @"\bstyle\s*=\s*[""'][^""']*font-family",
                    RegexOptions.IgnoreCase))
            // <font size=…>/<font face=…> style flow text inside the UA flow
            // itself (the ladder sizes; a resolvable face embeds for its runs).
            // A body-less <html> wrapper still parses as a full document (the
            // parser synthesizes the body) — it takes the UA flow like one. A
            // fragment ROOTED at a list (<ul>/<ol> is its first tag) carries pure
            // UA structure by construction and takes the same flow, as does a
            // fragment CARRYING a table (its grid renders through the metric
            // table renderer); other bare fragments keep the legacy flow.
            && (tagFreeDoc
                || Regex.IsMatch(html, @"<html\b|<table\b", RegexOptions.IgnoreCase)
                || Regex.IsMatch(html, @"\A\s*(?:<!--.*?-->\s*)*<[ou]l\b",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline)
                // A fragment ROOTED at a styled box div — inline width+height+
                // border — is a border-box drawing, pure UA structure by
                // construction; it takes the UA flow, which strokes the
                // declared box and flows the content inside it.
                || Regex.IsMatch(html,
                    @"\A\s*(?:<!--.*?-->\s*)*<div\b[^>]*style\s*=\s*(['""])(?=(?:(?!\1).)*\bwidth\s*:)(?=(?:(?!\1).)*\bheight\s*:)(?=(?:(?!\1).)*\bborder)(?:(?!\1).)*\1",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline)
                // A body rule pinning the UA-base face marks a full styled page
                // whatever wrapper it ships in — it takes the UA flow in that face.
                || uaBodyFace is not null)
            // Table documents take the UA flow WITH their tables — the metric table
            // renderer draws them as real grids, the same model the source renderer
            // applies (H-4: bordered cellspacing grids, centred tables, bgcolor
            // cells all render as authored, never as flattened text). Exception:
            // an unresolved RELATIVE stylesheet is a packaging gap (the sheet was
            // present when the page was authored — see the dead-CSS rule above),
            // so such a table document keeps the legacy calibrated flow.
            && (!Regex.IsMatch(html, @"<table\b", RegexOptions.IgnoreCase)
                || deadExternalCss
                || !Regex.Matches(html,
                        @"<link\b[^>]*rel\s*=\s*[""']?stylesheet[^>]*href\s*=\s*[""']?([^""'\s>]+)",
                        RegexOptions.IgnoreCase)
                    .Any(lm =>
                    {
                        var relHref = lm.Groups[1].Value;
                        if (relHref.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                            return false;
                        // A relative sheet that does not EXIST under the caller's
                        // base path is dead for EVERY renderer — the source
                        // renderer lays such a document out in pure UA defaults
                        // too. Only a sheet that is actually present (but failed
                        // to inline) marks the packaging gap that keeps the
                        // legacy calibrated flow. An inline-SVG document keeps
                        // its calibrated flow either way — the UA flow does not
                        // model the svg block rhythm that pipeline was tuned on.
                        if (!string.IsNullOrEmpty(options?.BasePath))
                        {
                            try
                            {
                                if (!System.IO.File.Exists(System.IO.Path.Combine(
                                        options!.BasePath!, relHref.TrimStart('/', '\\'))))
                                    return inlineSvgs.Count > 0;
                            }
                            catch { /* malformed href: treat as unresolved-present */ }
                        }
                        return true;
                    }))
            // A sheet that pins `thead { display: table-header-group }` authors a
            // PAGINATED report — its header rows repeat on every page a table
            // spans, a behaviour the metric grid does not model; such documents
            // keep the legacy calibrated flow.
            && !(css.TryGetValue("thead", out var theadRule)
                && theadRule.TryGetValue("display", out var theadDisp)
                && theadDisp.Contains("table-header-group", StringComparison.OrdinalIgnoreCase))
            // Excel-export markup (the xlNN cell classes) is its own dialect —
            // the legacy flow was calibrated on it, cell fonts and all.
            && !Regex.IsMatch(html, @"class\s*=\s*[""']?xl\d+", RegexOptions.IgnoreCase)
            && WinMetricsFor("Times New Roman") is not null;

        var uaFlow = uaMshtml || uaNoFontDoc;
        // The full-document path draws Standard-14 serif (no embedding); MSHTML keeps its
        // embedded-Type0 serif output.
        var uaStdSerif = uaNoFontDoc && !uaMshtml;
        // A body styled width:100% — the sheet widens by the UA body inset and
        // its tables sit the measured gap below body text (both measured).
        var bodyWidthFullDoc = false;
        // The Word-filtered text column the reference justifies to (see the
        // margin override below: sheet = 96 + column + 96), and the drop of its
        // broken-image placeholders under the 72 pt top margin (both measured).
        const double MsoTextColumnWPt = 529.8;
        const double MsoBrokenImgDropPt = 14.4;
        if (uaFlow)
        {
            metricFlow = true;
            // The UA serif, unless the body rule pinned its own face at the UA base.
            metricFace = uaStdSerif && uaBodyFace is not null ? uaBodyFace : "Times New Roman";
            bodyMarT = 6.0;
            // Edge-to-edge sheets: the first paragraph's UA margin-top collapses
            // with the body margin — the content opens max(6, 12) below the top
            // margin, plus the engine's measured first-line seat (baseline lands
            // 96.2 on the reference: 72 + 15.35 + the metric drop).
            if (edgeToEdgeDoc) bodyMarT = 15.35;
            // Per-side-touched defaults: an untouched side keeps the renderer
            // default (the caller authored only the sides they set).
            var uaPerSide = marginsExplicit && (pageMargin?.IsTouched ?? false)
                && pageMargin!.HtmlPerSideDefaults;
            // a zero body margin keeps the bare page margin — no 6pt body inset
            if (bodyZeroMargin) bodyMarT = 0.0;
            marginLeft = (marginsExplicit
                ? (uaPerSide && !pageMargin!.LeftTouched ? 90.0 : pageMargin!.Left) : 90.0)
                + (bodyZeroMargin ? 0.0 : 6.0);
            marginRight = (marginsExplicit
                ? (uaPerSide && !pageMargin!.RightTouched ? 90.0 : pageMargin!.Right) : 90.0)
                // Edge-to-edge sheets get the UA body margin on the RIGHT too — a
                // width:100% table ends exactly one body margin short of the edge.
                + (edgeToEdgeDoc ? 6.0 : 0.0);
            marginTop = marginsExplicit
                ? (uaPerSide && !pageMargin!.TopTouched ? 72.0 : pageMargin!.Top) : 72.0;
            marginBottom = marginsExplicit
                ? (uaPerSide && !pageMargin!.BottomTouched ? 72.0 : pageMargin!.Bottom) : 72.0;
            // Fieldset worksheet: the body's own margin + padding ARE the content
            // offsets (no UA 6pt inset), and that padding blocks the doc-top
            // margin collapse — the first heading keeps its full margin.
            if (fieldsetDoc)
            {
                marginLeft = 90.0 + fsBodyChromePt;
                marginTop = 72.0 + fsBodyChromePt;
                bodyMarT = 0.0;
            }
            // Word-filtered pages: the reference lays them on a SYMMETRIC 96 pt
            // inset over its measured 529.8 pt text column — the sheet is
            // 96 + 529.8 + 96 = 721.75, and the justified lines stretch to
            // exactly that column (measured on the filtered-page reference).
            if (msoFilteredDoc && !marginsExplicit && !(pageInfo?.WidthAssigned ?? false))
            {
                marginRight = 90.0 + UaBodyMarginPt;
                pageWidth = marginLeft + MsoTextColumnWPt + marginRight;
            }
            // The custom-font report keeps the UA body margin on BOTH sides of
            // its explicit zero margins — its reference wraps at exactly
            // page − 2×6 (a 602 pt line breaks out of the 600 box).
            if (customFontFaceDoc && marginsExplicit)
                marginRight += UaBodyMarginPt;
            // A width:100% BODY spans the bare page margins instead of losing
            // the 6 pt UA body inset to its width — the sheet grows by that
            // inset so the offset box still fits (measured on the
            // reference: MediaBox 601 = 595 + 6, body box 96..517 = 421 wide).
            bodyWidthFullDoc = !(pageInfo?.WidthAssigned ?? false)
                && Regex.Match(html, @"<body\b[^>]*style\s*=\s*(['""])[^'""]*?(?<![-\w])width\s*:\s*100%[^'""]*\1",
                    RegexOptions.IgnoreCase).Success;
            if (bodyWidthFullDoc)
            {
                pageWidth += UaBodyMarginPt;
                marginRight -= UaBodyMarginPt;
            }
        }

        // Official-letter flow (gated): an explicit-zero-margin CJK letter —
        // a content class carries the family, table rows pace themselves with
        // inline font-size keywords and px heights, and no body rule exists
        // for the standard metric gate. The metric flow lays it out with the
        // face's real advances; an uninstalled family substitutes to SimSun,
        // the same fallback the reference engine draws it with.
        if (!metricFlow && marginsExplicit && !css.ContainsKey("body")
            && Regex.IsMatch(html, @"<tr[^>]*style\s*=\s*[""'][^""']*font-size\s*:",
                RegexOptions.IgnoreCase))
        {
            string? letterFam = null;
            foreach (var (sel, props) in css)
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
                    metricFlow = true;
                    metricFace = letterFace;
                    // the UA 8px body margin boxes the letter's tables
                    marginLeft += 6.0;
                    marginRight += 6.0;
                    bodyMarT = 6.0;
                }
            }
        }

        // Styled-article flow (gated): the modern docs-site fingerprint — a
        // `body { margin:0 }` page whose base font is REM-sized with a unitless
        // line-height and a resolvable sans face (a Hugo/Bootstrap bundle). The
        // metric flow lays it out with the face's real advances and the sheet's
        // own line factor; the legacy calibrated flow rendered these articles at
        // the 12pt/1.3 defaults and every block landed far from its true place.
        var articleFlow = false;
        var articleLineFactor = 0.0;
        if (!metricFlow && !marginsExplicit && bodyZeroMargin && bodyCssFontPt > 0
            && css.TryGetValue("body", out var artBody)
            && artBody.TryGetValue("font-size", out var artFs)
            && artFs.TrimEnd().EndsWith("rem", StringComparison.OrdinalIgnoreCase)
            && artBody.TryGetValue("line-height", out var artLh)
            && double.TryParse(artLh.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var artLhF)
            && artLhF is > 1.0 and < 2.5
            && bodyCssFace is not null && WinMetricsFor(bodyCssFace) is not null)
        {
            metricFlow = true;
            articleFlow = true;
            metricFace = bodyCssFace;
            articleLineFactor = artLhF;
        }

        // The pt-sized clinical REPORT: a BODY rule pinning a resolvable face at
        // an absolute pt size beside a TABLE rule carrying a family, on a
        // table-heavy sheet — the source renderer lays it out as a metric flow
        // in that face (hhea line boxes), css class typography driving both the
        // flow blocks and the cell grids.
        var ptReportDoc = false;
        // the pt-report family's NEWSLETTER arm (inline-body-styled email):
        // in-cell paragraph segments, UA p margins and the quirks body margin
        // are ITS dialect — the NHS/boleto report greens keep the whole-cell model.
        var emailNewsletterDoc = false;
        var ptTableFontPt = 0.0;
        if (!metricFlow && !marginsExplicit
            && css.TryGetValue("body", out var ptBody)
            && ptBody.TryGetValue("font-family", out var ptFam0)
            && FirstFontFamily(ptFam0) is { } ptFam && WinMetricsFor(ptFam) is not null
            && ptBody.TryGetValue("font-size", out var ptFs0)
            && Regex.IsMatch(ptFs0.Trim(), @"^[\d.]+\s*pt$", RegexOptions.IgnoreCase)
            && css.TryGetValue("table", out var ptTbl) && ptTbl.ContainsKey("font-family")
            && Regex.Matches(html, @"<table\b", RegexOptions.IgnoreCase).Count >= 5)
        {
            ptReportDoc = true;
            metricFlow = true;
            metricFace = ptFam;
            metricLineSum = HheaLineSumFor(ptFam) ?? 0;
            // The metric report opens at the raw 72 pt content top (the legacy
            // calibrated 89 belongs to the flow this document left).
            marginTop = 72.0;
            // The body rule authors MARGIN-TOP: 0cm — content opens at the page
            // margin with no UA body inset. (TryParseLength rejects an explicit
            // zero by design, so the zero idiom is matched first.)
            bodyMarT = ptBody.TryGetValue("margin-top", out var ptMt)
                ? Regex.IsMatch(ptMt.Trim(), @"^0(\.0+)?\s*(cm|mm|px|pt|em|in)?$")
                    ? 0.0
                    : TryParseLength(ptMt.Trim(), out var ptMtPt) ? ptMtPt : 6.0
                : 6.0;
            formBodyFontPt = double.Parse(Regex.Match(ptFs0, @"[\d.]+").Value,
                System.Globalization.CultureInfo.InvariantCulture);
            if (ptTbl.TryGetValue("font-size", out var ptTfs)
                && TryParseCssFontSize(ptTfs.Trim(), out var ptTfsPt))
                ptTableFontPt = ptTfsPt;
        }
        // …or the same declaration INLINE on the body tag: the NEWSLETTER shape
        // (an Arial px email with zero body margins whose whole layout is
        // table-built) renders through the same metric route.
        if (!ptReportDoc && !metricFlow && !marginsExplicit
            && Regex.Match(html, @"<body\b[^>]*style\s*=\s*[""']([^""']*)[""']",
                RegexOptions.IgnoreCase) is { Success: true } ebM
            && Regex.Match(ebM.Groups[1].Value, @"font-family\s*:\s*([^;]+)",
                RegexOptions.IgnoreCase) is { Success: true } ebFam0
            && FirstFontFamily(ebFam0.Groups[1].Value) is { } ebFam
            && WinMetricsFor(ebFam) is not null
            && Regex.Match(ebM.Groups[1].Value, @"font-size\s*:\s*([\d.]+)\s*px",
                RegexOptions.IgnoreCase) is { Success: true } ebFs
            && Regex.Matches(html, @"<table\b", RegexOptions.IgnoreCase).Count >= 5)
        {
            ptReportDoc = true;
            emailNewsletterDoc = true;
            metricFlow = true;
            metricFace = ebFam;
            metricLineSum = HheaLineSumFor(ebFam) ?? 0;
            // page margin + the quirks body's default 8px margin (the inline
            // style declares no margins of its own)
            marginTop = 72.0 + UaBodyMarginPt;
            formBodyFontPt = double.Parse(ebFs.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture) * 0.75;
        }

        // SSRS report export (the ReportingServices HTML renderer's
        // grow-rectangles wrapper): its cells run the paragraph-segment model
        // and its oversized data-URI JPEG widens the sheet (see the widen below).
        var ssrsReportDoc = html.Contains(
            "Microsoft_ReportingServices_HTMLRenderer", StringComparison.OrdinalIgnoreCase);

        // Chart-card documents: a body{margin:0} page whose visible content is an
        // inline-SVG chart in a padded widget card (the saved React/c3 report
        // shape). The container class chrome positions the blocks
        // (containerBoxIndents) and the page widens to the chart's natural size.
        // A metric/article-flow document keeps its own dialect even when it ships
        // decorative inline SVGs (a docs site's icons must not re-route it).
        var chartCardDoc = bodyZeroMargin && !metricFlow && inlineSvgs.Count > 0;

        // Print-authored cover documents: a body{margin:0} page that separates its
        // cover from the body with an explicit page-break-after — the cover
        // classes' own type scale, physical-unit margins, and line factors ARE the
        // layout (see coverStyles in ApplyCssRules).
        var printCoverDoc = bodyZeroMargin && !metricFlow && !chartCardDoc
            && Regex.IsMatch(html, @"page-break-after\s*:\s*always", RegexOptions.IgnoreCase);

        // Styled inline rows (nav bars, centered link lines) render from prebuilt
        // run blocks; their markup is replaced by <rowmark> placeholders.
        html = ExtractRowBlocks(html, css, out var rowBlocks);

        // Document-level RTL (dir="rtl" on <html>/<body>): a block image wider than
        // the content box keeps its NATIVE size with its right edge on the right
        // margin, overflowing (and clipping) off the left page edge — the mirror of
        // the LTR left-pinned overflow.
        var rtlDoc = Regex.IsMatch(html, @"<(?:html|body)[^>]*\bdir\s*=\s*[""']?rtl",
            RegexOptions.IgnoreCase);

        // A <header>/<footer> becomes a *running* region (repeated on every page) only when it is
        // pinned with position:fixed — the print idiom `@media print { header { position:fixed } }`.
        // A semantic <header>/<footer> that is ordinary flow content (display:block, or
        // position:absolute/static) is laid out once in document order, so it must stay in the flow;
        // pulling it out and repeating it per page would duplicate that content on every page.
        string? runHeader = null, runFooter = null;
        var hMatch = Regex.Match(html, @"<header([^>]*)>(.*?)</header>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (hMatch.Success && IsFixedRegion(hMatch.Groups[1].Value, "header", css))
        {
            runHeader = DecodeEntities(HtmlFragment.StripHtmlTags(hMatch.Groups[2].Value)).Trim();
            html = html.Remove(hMatch.Index, hMatch.Length);
        }
        var fMatch = Regex.Match(html, @"<footer([^>]*)>(.*?)</footer>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (fMatch.Success && IsFixedRegion(fMatch.Groups[1].Value, "footer", css))
        {
            runFooter = DecodeEntities(HtmlFragment.StripHtmlTags(fMatch.Groups[2].Value)).Trim();
            html = html.Remove(fMatch.Index, fMatch.Length);
        }
        if (!string.IsNullOrEmpty(runHeader)) marginTop += 24;
        if (!string.IsNullOrEmpty(runFooter)) marginBottom += 24;

        // A <div id="footer"> whose only rendered content is an <img> — the classic
        // letterhead-logo footer. That image is pulled out of the
        // flow and placed ONCE at page 1's bottom margin, left content edge, at
        // its CSS pixel size (a trailing 630×60px logo lands at
        // (marginLeft, marginBottom) + 472.5×45pt on page 1 only; no other page
        // carries it). A footer div with visible text stays in the flow.
        string? page1FooterImgSrc = null;
        double page1FooterImgW = 0, page1FooterImgH = 0;
        var dfMatch = Regex.Match(html, @"<div[^>]*\bid\s*=\s*[""']footer[""'][^>]*>(.*?)</div>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (dfMatch.Success)
        {
            var footerInner = dfMatch.Groups[1].Value;
            var footImg = Regex.Match(footerInner, @"<img\b[^>]*>", RegexOptions.IgnoreCase);
            var footerText = DecodeEntities(HtmlFragment.StripHtmlTags(footerInner)).Trim();
            if (footImg.Success && footerText.Length == 0)
            {
                var tag = footImg.Value;
                var srcM = Regex.Match(tag, @"\bsrc\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
                double PxOf(string prop)
                {
                    // CSS style wins; a bare width/height attribute is the fallback.
                    var m = Regex.Match(tag, prop + @"\s*:\s*([\d.]+)px", RegexOptions.IgnoreCase);
                    if (!m.Success)
                        m = Regex.Match(tag, "\\b" + prop + @"\s*=\s*[""']?([\d.]+)", RegexOptions.IgnoreCase);
                    return m.Success && double.TryParse(m.Groups[1].Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
                }
                if (srcM.Success)
                {
                    page1FooterImgSrc = srcM.Groups[1].Value;
                    page1FooterImgW = PxOf("width") * 0.75;  // CSS px → pt
                    page1FooterImgH = PxOf("height") * 0.75;
                    html = html.Remove(dfMatch.Index, dfMatch.Length);
                }
            }
        }
        var beforeMarkers = ParseBeforeMarkers(html);

        // Build the block list, segmenting out real data tables (no form inputs) so they render
        // as column grids instead of a flattened single column. Text between tables flows through
        // the normal block path; a table with form inputs stays on the flat path (BuildTableFromHtml
        // would swallow its <input>s). Table-free HTML yields a single text segment = unchanged.
        var blocks = new List<Block>();
        // Only VISIBLE form controls keep a table on the flat path — a page whose
        // inputs are all display:none (hidden state carriers in generated reports)
        // renders its tables as real grids.
        static bool HasVisibleFormControl(string markup)
        {
            foreach (Match fim in Regex.Matches(markup, @"<\s*(input|select|textarea)\b[^>]*>",
                         RegexOptions.IgnoreCase))
                if (!HiddenInlineRx.IsMatch(fim.Value)) return true;
            return false;
        }
        bool htmlHasFormInput = HasVisibleFormControl(html);

        // A control-bearing table still renders as a GRID when its visible controls
        // are all radios (plus button-family inputs, which draw nothing in a cell):
        // the radio factory carries the options into the cells as inline glyphs —
        // `◯ ◯Yes ◉ ◉No` on one line, the form-report shape. Any
        // text-like control (text input, select, textarea) keeps its table on the
        // flat path, whose blocks emit the AcroForm fields for it.
        static bool RadioGridableControls(string markup)
        {
            var hasRadio = false;
            foreach (Match fim in Regex.Matches(markup, @"<\s*(input|select|textarea)\b[^>]*>",
                         RegexOptions.IgnoreCase))
            {
                if (HiddenInlineRx.IsMatch(fim.Value)) continue;
                if (!fim.Groups[1].Value.Equals("input", StringComparison.OrdinalIgnoreCase))
                    return false;
                var tyM = Regex.Match(fim.Value, @"type\s*=\s*[""']?([A-Za-z]+)",
                    RegexOptions.IgnoreCase);
                var ty = tyM.Success ? tyM.Groups[1].Value.ToLowerInvariant() : "text";
                if (ty == "radio") hasRadio = true;
                else if (ty is not ("hidden" or "button" or "submit" or "reset" or "image")) return false;
            }
            return hasRadio;
        }

        // FORM-DOCUMENT dialect (document-level `td {font: …}` shorthand cells —
        // the application-form shape): EVERY table renders as a grid, its cells
        // laying form controls out inline. Outside the dialect the legacy gate
        // holds — a document with any form control keeps the whole flat path,
        // whose blocks emit the controls as AcroForm fields.
        var formDialectTables =
            (CssFontShorthand(css, "td") ?? CssFontShorthand(css, "table")) is not null;

        // A form control is a reason to keep ITS OWN table flat, not the whole document:
        // the grid path would swallow that table's controls, but a control three tables
        // away costs a data grid its columns for nothing. Documents that hold both — an
        // application form with a plain report grid at the end — segment per table.
        bool perTableFormGate = htmlHasFormInput && !formDialectTables
            && SegmentHtmlTables(html).Any(s => s.isTable && !HasVisibleFormControl(s.html));

        // A chrome-less SINGLE-COLUMN table is a layout wrapper, not a grid: the
        // source renderer flows the cell content inset by the default cell chrome
        // (UaCellChromePt) instead of drawing a grid — the SharePoint wiki shape.
        // Strict fingerprint: no border/spacing/padding attrs, no bgcolor or css
        // box decoration anywhere, no <th>, no nesting, every row exactly one td.
        static bool IsSingleColumnWrapperTable(string seg)
        {
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

        List<Block> BuildFlowBlocks(string frag)
        {
            var list = new List<Block>();
            if (ContainsTable(frag)
                && (formDialectTables || !htmlHasFormInput || perTableFormGate || escapedAttrDoc))
            {
                var segs = SegmentHtmlTables(frag);
                // A PAGE-BREAK-AFTER div wrapping tables: its close tag parses in
                // a LATER segment where the pending-break state cannot reach, so
                // the break attaches to the div's LAST table segment instead.
                HashSet<int>? breakAfterSegs = null;
                if (uaStdSerif && Regex.IsMatch(frag, @"page-break-after\s*:\s*always",
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
                    if (isTable && uaStdSerif && !escapedAttrDoc
                        && IsSingleColumnWrapperTable(seg))
                    {
                        var inner = Regex.Replace(seg,
                            @"</?(table|tbody|thead|tfoot|tr)\b[^>]*>", "", RegexOptions.IgnoreCase);
                        inner = Regex.Replace(inner, @"<td\b[^>]*>", "<div>", RegexOptions.IgnoreCase);
                        inner = Regex.Replace(inner, @"</td\s*>", "</div>", RegexOptions.IgnoreCase);
                        var beforeUnwrap = list.Count;
                        list.AddRange(ParseBlocks(inner, css, beforeMarkers, rowBlocks, metricFlow,
                            uaFlow || printGrid, uaStdSerif || printGrid,
                            printGrid ? printGridBase : articleFlow ? CssRootFontPt
                                : bodyBoxGridDoc || scaleToPageWidth ? DefaultBodyFontPt : formBodyFontPt,
                            bandDialect: floatBandDoc, formDialect: formHorizontalDoc,
                            brBlankLines: formDialectTables, uaBlockRhythm: sectionedReport,
                            controlBoxes: escapedAttrDoc, articleRhythm: articleFlow,
                            bodyBoxRhythm: bodyBoxGridDoc,
                            containerBoxIndents: chartCardDoc, coverStyles: printCoverDoc,
                            inlineBlockCols: inlineBlockColRules,
                            absSpanLedger: absSpanLedger,
                            spanClassTypography: ptReportDoc,
                            fieldsetBoxes: fieldsetDoc,
                            uaPMargins: emailNewsletterDoc,
                            msoParagraphs: msoFilteredDoc));
                        for (var ub = beforeUnwrap; ub < list.Count; ub++)
                            list[ub].LeftIndent += UaCellChromePt;
                        if (list.Count > beforeUnwrap)
                            list[beforeUnwrap].PadTop += UaCellChromePt;
                        continue;
                    }
                    // The escaped-attr dialect grids EVERY table — form controls
                    // draw INSIDE grid cells rather than flattening the table.
                    // A radio-only table grids too: its options ride the cells inline.
                    if (isTable && (escapedAttrDoc
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
                        list.AddRange(ParseBlocks(seg, css, beforeMarkers, rowBlocks, metricFlow,
                            uaFlow || printGrid, uaStdSerif || printGrid,
                            printGrid ? printGridBase : articleFlow ? CssRootFontPt
                                : bodyBoxGridDoc || scaleToPageWidth ? DefaultBodyFontPt : formBodyFontPt,
                            bandDialect: floatBandDoc, formDialect: formHorizontalDoc,
                            brBlankLines: formDialectTables, uaBlockRhythm: sectionedReport,
                            controlBoxes: escapedAttrDoc, articleRhythm: articleFlow,
                            bodyBoxRhythm: bodyBoxGridDoc,
                            containerBoxIndents: chartCardDoc, coverStyles: printCoverDoc,
                            inlineBlockCols: inlineBlockColRules,
                            absSpanLedger: absSpanLedger,
                            spanClassTypography: ptReportDoc,
                            fieldsetBoxes: fieldsetDoc,
                            uaPMargins: emailNewsletterDoc,
                            msoParagraphs: msoFilteredDoc));
                        // A stretch between tables that is nothing but <br>s carries no text,
                        // so the block parser yields nothing for it — yet each of those
                        // breaks is a line box the next table starts below.
                        if (list.Count == before && bodyCssFace is not null
                            && WinMetricsFor(bodyCssFace) is { } segBr)
                            foreach (Match _ in Regex.Matches(seg, @"<br\b[^>]*>", RegexOptions.IgnoreCase))
                                list.Add(new Block
                                {
                                    Text = "", IsHardBreak = true, IsLineBreak = true,
                                    ExplicitHeight = MetricLineHeight(bodyCssFontPt, segBr.sum),
                                });
                    }
                }
            }
            else list.AddRange(ParseBlocks(frag, css, beforeMarkers, rowBlocks, metricFlow,
                uaFlow || printGrid, uaStdSerif || printGrid,
                printGrid ? printGridBase : articleFlow ? CssRootFontPt
                    : bodyBoxGridDoc || scaleToPageWidth ? DefaultBodyFontPt : formBodyFontPt,
                bandDialect: floatBandDoc, formDialect: formHorizontalDoc,
                brBlankLines: formDialectTables, uaBlockRhythm: sectionedReport,
                controlBoxes: escapedAttrDoc, articleRhythm: articleFlow,
                bodyBoxRhythm: bodyBoxGridDoc,
                containerBoxIndents: chartCardDoc, coverStyles: printCoverDoc,
                inlineBlockCols: inlineBlockColRules,
                            absSpanLedger: absSpanLedger,
                            spanClassTypography: ptReportDoc,
                            fieldsetBoxes: fieldsetDoc,
                            uaPMargins: emailNewsletterDoc,
                            msoParagraphs: msoFilteredDoc));
            return list;
        }

        // Float-column groups and bordered divs become structural marker blocks with
        // their inner flow recursively segmented; HTML without those patterns takes
        // the flat path untouched.
        List<Block> BuildStructuredBlocks(string frag, int depth, double availPt = 0)
        {
            var structured = Regex.IsMatch(frag, @"float\s*:\s*left|border\s*:\s*solid", RegexOptions.IgnoreCase)
                || (printGrid && Regex.IsMatch(frag, @"col-xs-|infobox", RegexOptions.IgnoreCase));
            if (depth > 6 || !structured)
                return BuildFlowBlocks(frag);
            // px-width float columns convert to fractions of the box they live IN —
            // the page content at the top level, the enclosing column when nested.
            var segs = SegmentDivStructures(frag, printGrid ? css : null,
                availPt > 0 ? availPt : pageWidth - marginLeft - marginRight,
                allowPxCols: formHorizontalDoc);
            if (segs.Count == 0) return BuildFlowBlocks(frag);
            var list = new List<Block>();
            foreach (var seg in segs)
            {
                switch (seg.Kind)
                {
                    case DivSeg.Col:
                        list.Add(new Block { ColScopeStart = true, FloatWidthFrac = seg.WidthFrac, ColPadPt = seg.ColPadPt });
                        list.AddRange(BuildStructuredBlocks(seg.Html, depth + 1));
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
                            list.AddRange(BuildStructuredBlocks(inner, depth + 1,
                                widthFrac * (availPt > 0 ? availPt : pageWidth - marginLeft - marginRight)));
                        }
                        list.Add(new Block { FloatBandEnd = true });
                        break;
                    case DivSeg.Box:
                    {
                        var innerBlocks = BuildStructuredBlocks(seg.Html, depth + 1, availPt);
                        var firstTextSize = 0.0;
                        foreach (var ib in innerBlocks)
                        {
                            if (ib.IsTable || ib.IsImage) break;
                            if (!string.IsNullOrEmpty(ib.Text)) { firstTextSize = ib.FontSize; break; }
                        }
                        list.Add(new Block
                        {
                            BoxStart = true, BoxBorderPt = seg.BorderPt, BoxPadTopPt = seg.PadTopPt,
                            BoxAscentPt = printGrid ? 0 : firstTextSize * 0.9,
                            BoxPadSidePt = printGrid ? seg.PadSidePt : 0,
                            BoxBorderGray = seg.BorderGray,
                        });
                        list.AddRange(innerBlocks);
                        list.Add(new Block { BoxEnd = true, BoxPadBottomPt = seg.PadBottomPt, BoxMarginBottomPt = seg.MarginBottomPt });
                        break;
                    }
                    default:
                        list.AddRange(BuildFlowBlocks(seg.Html));
                        break;
                }
            }
            return list;
        }

        // Report label/span rows (gated with the physical-unit body width that also
        // sizes the sheet): each row is a bold right-aligned label column beside a
        // wrapped span column at the sheet's own small size; an hr divides sections.
        if (!marginsExplicit && !ContainsTable(html)
            && Regex.Match(html, @"<body\b[^>]*style\s*=\s*(['""])[^'""]*?(?<![-\w])width\s*:\s*([\d.]+\s*(?:cm|mm|in|pt))[^'""]*\1",
                RegexOptions.IgnoreCase) is { Success: true } repBodyM
            && TryParseLength(repBodyM.Groups[2].Value.Replace(" ", ""), out var repBodyW)
            && repBodyW > 0
            && TryBuildReportLabelBlocks(html, repBodyW, out var repBlocks))
            blocks = repBlocks;
        else
            blocks = BuildStructuredBlocks(html, 0);
        // A content-less document still ships ONE page at the configured size —
        // the source renderer never emits a zero-page PDF.
        if (blocks.Count == 0)
        {
            var blankDoc = Document.Create();
            blankDoc.Pages.Add(pageWidth, pageHeight);
            return blankDoc;
        }

        // Dash-overflow wrap box (quirks CSS-run and title-column docs): an
        // unbreakable dash-delimited segment wider than the content box widens the
        // WRAP LIMIT for the whole document — long tokens then break only after
        // dashes, overflowing the margin exactly as the source renderer does
        // (measured: the widest segment IS the limit, so the defining segment
        // always fits its line whole). Zero when every segment fits — the legacy
        // wrap then stands untouched. Measured in the body face when the sheet
        // declares one, else the UA serif the source renderer laid these out with.
        var dashWrapFace = bodyCssFace ?? "Times New Roman";
        var quirksWrapW = 0.0;
        if ((quirksCssRun || inlineBlockColRules) && WinMetricsFor(dashWrapFace) is not null)
        {
            foreach (var b in blocks)
            {
                if (b.IsTable || b.IsHardBreak || b.IsImage || string.IsNullOrEmpty(b.Text)) continue;
                var bfs = b.FontSize > 0 ? b.FontSize : 11.0;
                foreach (var seg in DashSegments(b.Text))
                    if (seg.Length > 2)
                        quirksWrapW = Math.Max(quirksWrapW,
                            MeasureFaceText(dashWrapFace, seg, bfs));
            }
        }

        // Print media reset (* { color:#000 !important; background: transparent }):
        // every block draws black on transparent; borders and the heading bands keep
        // their colours (the reset touches text and backgrounds only).
        if (printGrid)
            foreach (var b in blocks)
            {
                b.ForeColor = null;
                b.BackgroundColor = null;
            }

        // An <img> whose source cannot be loaded surfaces its alt text as an ordinary
        // text line (the browser fallback) — the block keeps its place in the flow.
        foreach (var b in blocks)
            if (b.IsImage && !string.IsNullOrWhiteSpace(b.ImageAlt)
                && !b.ImageSrc.StartsWith("inline-svg:", StringComparison.Ordinal)
                && LoadConverterImage(b.ImageSrc, options) is null)
            {
                b.IsImage = false;
                b.Text = b.ImageAlt!.Trim();
                if (b.FontSize <= 0) b.FontSize = uaFlow ? 12 : 11;
            }

        // Centre-crop an encoded image to a CSS-px box: background-repeat:no-repeat
        // without a background-size anchors the image at NATURAL size centre-centre,
        // so a box smaller than the image shows its middle. Returns null when no
        // crop is needed (or off-Windows) — the caller keeps the original bytes.
        static byte[]? CenterCropToBox(byte[] bytes, double boxWpx, double boxHpx)
        {
            if (!OperatingSystem.IsWindows()) return null;
#pragma warning disable CA1416 // guarded by the IsWindows check above
            try
            {
                using var ms = new MemoryStream(bytes);
                using var src = System.Drawing.Image.FromStream(ms);
                var cw = (int)Math.Round(Math.Min(src.Width, boxWpx));
                var chh = (int)Math.Round(Math.Min(src.Height, boxHpx));
                if (cw <= 0 || chh <= 0 || (cw >= src.Width && chh >= src.Height)) return null;
                using var bmp = new System.Drawing.Bitmap(cw, chh);
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                    g.DrawImage(src, (cw - src.Width) / 2, (chh - src.Height) / 2,
                        src.Width, src.Height);
                using var oms = new MemoryStream();
                bmp.Save(oms, System.Drawing.Imaging.ImageFormat.Png);
                return oms.ToArray();
            }
            catch { return null; }
#pragma warning restore CA1416
        }

        // Pure-table document with explicit page margins: the UA's 8px body margin
        // (6pt) still sits INSIDE the authored margins — it offsets the
        // table on the left and the top. The default-margin path already bakes
        // this into its calibrated 96/89 defaults; flow documents keep their
        // calibrated explicit-margin geometry untouched.
        if (marginsExplicit && blocks.TrueForAll(b => b.IsTable))
        {
            marginLeft += 6.0;
            marginTop += 6.0;
        }

        // Auto-size the page width to the widest data table's natural (content-fit) width when it
        // would otherwise overflow the content area — matching the layout engine, which widens
        // the page for a wide table rather than compressing/clipping it. Only widen (never shrink),
        // and only when a table genuinely overflows, so normal-width conversions are unchanged.
        double availContentW = pageWidth - marginLeft - marginRight;
        double widestTable = 0;
        foreach (var b in blocks)
        {
            // A wrapper-stack table lays out through the recursive metric path,
            // whose children fit the symmetric content frame — the flat probe
            // would measure the merged monster and widen a sheet the render
            // never fills.
            if (b.IsTable && uaStdSerif && !deadExternalCss
                && TrySplitWrapperStack(b.TableHtml ?? "", out _, out _))
                continue;
            if (b.IsTable && BuildTableFromHtml(b.TableHtml ?? "", availContentW, out var natW, options, inlineSvgs, css,
                    widenProbe: floatBandDoc,
                    // A scaled layout measures at the UA base size — the shrink
                    // factor multiplies it back to the reference's text size.
                    defaultCellFontPt: scaleToPageWidth ? DefaultBodyFontPt
                        : printGrid ? printGridBase
                        // UA-serif documents measure at the UA 16px base in the
                        // serif face — the 11pt Helvetica default under-measures
                        // the min-content the reference widens for.
                        : uaStdSerif && !deadExternalCss && bodyCssFontPt <= 0 ? 12
                        : bodyCssFontPt,
                    tightExtras: printGrid,
                    cssRunFace: bodyCssFace ?? (uaStdSerif && !deadExternalCss ? "Times New Roman" : null),
                    // The probe must measure the same cell boxes the render will build,
                    // or the page is sized off a grid nothing draws.
                    uaCellBoxes: sectionedReport,
                    // …which means the SAME lift setting: it also switches the whole
                    // chain-selector dialect on, so a probe that lifts while the render
                    // does not measures class-rule cell padding and borders the drawn
                    // grid never gets, and widens the sheet to a grid nothing draws.
                    liftNestedTables: true,
                    chainRules: docChainRules) is not null)
            {
                if (natW > widestTable) widestTable = natW;
            }
        }
        // A table that DECLARES an absolute width: read off the MARKUP, not the
        // block list — the metric flow lays its tables out through the table
        // renderer, so they never become table blocks and the natural-width
        // probe above never sees them. Percent widths never widen.
        double declaredTableW = 0;
        double collapseTableW = 0;
        foreach (Match tm in Regex.Matches(html, @"<table\b[^>]*>", RegexOptions.IgnoreCase))
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
                        if (css.TryGetValue("." + tcl, out var tclR)
                            && tclR.TryGetValue("width", out var tclW)
                            && TryParseLength(tclW.Trim(), out var tclPt))
                            w = Math.Max(w, tclPt);
            }
            if (w > declaredTableW) declaredTableW = w;
            // a bordered COLLAPSE grid (border=N + border-collapse:collapse)
            // keeps its declared width exactly — the sheet grows to page margin
            // + declared box + page margin, with no slack and no body inset
            if (w > collapseTableW
                && Regex.IsMatch(tm.Value, @"\bborder\s*=\s*[""']?[1-9]", RegexOptions.IgnoreCase)
                && Regex.IsMatch(tm.Value, @"border-collapse\s*:\s*collapse", RegexOptions.IgnoreCase))
                collapseTableW = w;
        }
        // The reference's natural box for a declared-width table carries a 2.25
        // chrome step over the declared width — the probe's symmetric border-
        // spacing counts 3.0 (measured: the UA sheet lands at 96 + 936px + 2.25
        // + 90); trim the difference so the widened page sizes like the reference.
        if (uaFlow && declaredTableW > 0
            && widestTable > declaredTableW + 2.25 && widestTable <= declaredTableW + 3.0 + 1e-6)
            widestTable = declaredTableW + 2.25;
        // …unless the caller AUTHORED the page width. A browser printing to a fixed
        // paper size overflows a too-wide table, it does not grow the paper, and the
        // public idiom for that is `options.PageInfo.Width = PageSize.A4.Width`.
        // Under ScaleToPageWidth an authored width no longer pins the sheet
        // during LAYOUT: the body grows to the widest table's min-content (the
        // percent tables resolve against the grown box, like the reference),
        // and the finished pages shrink back onto the authored page with the
        // content pinned at the left margin and the page top.
        if (scaleToPageWidth && widestTable > availContentW)
        {
            scaleReqPageW = pageWidth;
            scaleReqPageH = pageHeight;
            pageWidth = marginLeft + widestTable + marginRight;
            var pmL = pageMargin?.Left ?? 0;
            var pmR = pageMargin?.Right ?? 0;
            scalePendingS = (scaleReqPageW - pmL - pmR) / (pageWidth - pmL - pmR);
        }
        else if (uaStdSerif && collapseTableW > availContentW
            && !(pageInfo?.WidthAssigned ?? false))
        {
            pageHeight = Math.Max(pageWidth, pageHeight);
            pageWidth = marginLeft + collapseTableW + 90.0;
        }
        // Fieldset worksheet: the page grows to the widest declared table plus
        // the whole left chrome chain and the frame's right pad (probed:
        // 90 + 39 + 9.75 + 450 + 8.25 + 90.75 = 687.75).
        else if (uaStdSerif && fieldsetDoc && declaredTableW > 0
            && 90.0 + fsBodyChromePt + FsPadLeftPt + declaredTableW + FsPadRightPt
                + FsWidenRightPt > pageWidth
            && !(pageInfo?.WidthAssigned ?? false))
        {
            pageHeight = Math.Max(pageWidth, pageHeight);
            pageWidth = 90.0 + fsBodyChromePt + FsPadLeftPt + declaredTableW
                + FsPadRightPt + FsWidenRightPt;
        }
        // RTL attribute-grid sheet: the page grows to the widest DECLARED table
        // between the 90 pt page margin (its left edge) and the RTL right inset
        // the grids anchor against (measured: 90 + 600 + 91.78 = 781.78).
        else if (uaStdSerif && rtlDoc && declaredTableW > 0
            && 90.0 + declaredTableW + RtlGridRightInsetPt > pageWidth
            && !(pageInfo?.WidthAssigned ?? false))
        {
            pageHeight = Math.Max(pageWidth, pageHeight);
            pageWidth = 90.0 + declaredTableW + RtlGridRightInsetPt;
        }
        else if (widestTable > availContentW && !(pageInfo?.WidthAssigned ?? false))
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
            var inkWiden = !marginsExplicit && !printGrid && !floatBandDoc
                && (!uaFlow || deadExternalCss)
                && !bodyZeroMargin && !escapedAttrDoc;
            var neededContent = floatBandDoc ? widestTable - 6
                : bodyZeroMargin || printGrid || inkWiden ? widestTable : widestTable + 8;
            var widenRight = inkWiden ? 90.0 : marginRight;
            // Chain-dialect documents widen to PAGE margin + content + PAGE margin
            // exactly — the content box starts at x = 90 on the grown
            // sheet, not at the A4 flow's calibrated 96 left inset.
            var chainWiden = inkWiden && docChainRules is not null;
            var neededPage = neededContent + (chainWiden ? 90.0 : marginLeft) + widenRight
                // A UA-serif document's grown sheet keeps the symmetric body
                // margin on the RIGHT of the widest grid too (measured: the
                // register report's grid ends one body margin inside the frame).
                + (uaStdSerif && !deadExternalCss && !bodyZeroMargin ? UaBodyMarginPt : 0);
            // The escaped-attr dialect never widens: it keeps the default
            // page and SQUEEZES its grids into the content box instead.
            if (neededPage > pageWidth && !escapedAttrDoc)
            {
                // The layout engine keeps the page's larger (portrait-height) dimension as the
                // height and widens the width to fit the table, rather than the swapped landscape
                // short edge — so a wide table on an A4 page lands ~1129 × 842, not 1129 × 595.
                pageHeight = Math.Max(pageWidth, pageHeight);
                pageWidth = neededPage;
                if (chainWiden)
                {
                    marginLeft = 90.0 + bodyMarginLeftPt;
                    marginRight = widenRight;
                }
                // The body's own right margin still insets the content box, mirroring
                // the left — the page margin alone sized the sheet.
                else if (inkWiden) marginRight = widenRight + 6.0;
            }
        }

        // A declared table wider than the content box widens the page too: the
        // browser grows the canvas rather than squeezing a fixed-width table.
        // A table the natural-width probe already measured (and the page grew
        // for) must not re-widen the sheet — the stale second pass would also
        // capture the widened width as the page height.
        // The escaped-attr dialect's declared widths are JSON-mangled — they are
        // all ignored and the default page is kept.
        if (declaredTableW > pageWidth - marginLeft - marginRight
            && declaredTableW > widestTable && !escapedAttrDoc)
        {
            pageHeight = Math.Max(pageWidth, pageHeight);
            pageWidth = marginLeft + declaredTableW + marginRight;
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
        if (!marginsExplicit)
        {
            double bodyMinPt = 0;
            foreach (Match styleBlock in Regex.Matches(html, @"<style\b([^>]*)>([\s\S]*?)</style\s*>",
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
            if (bodyMinPt > pageWidth - marginLeft - marginRight)
            {
                pageHeight = Math.Max(pageWidth, pageHeight);
                pageWidth = marginLeft + bodyMinPt + marginRight;
            }
        }

        // Flex-grid widen: a positioned page wrapper above the waybill grid
        // declares the sheet's content width in physical units (width: 8in) —
        // the page grows to margins + body inset + that width (measured 762 =
        // 90 + 6 + 576 + 90 on the table-flavoured waybill).
        if (!(pageInfo?.WidthAssigned ?? false))
            foreach (var b in blocks)
                if (b.Flex is { PageContentPt: > 0 } fgw)
                {
                    var fgNeedW = 90.0 + CardBodyPadPt + fgw.PageContentPt + 90.0;
                    if (fgNeedW > pageWidth)
                    {
                        pageHeight = Math.Max(pageWidth, pageHeight);
                        pageWidth = fgNeedW;
                        marginLeft = 90.0;
                        marginRight = 90.0;
                        marginTop = 72.0;
                    }
                }

        // Positioned-slide widen: the page grows to the slide's CONTENT EXTENT —
        // the rightmost absolutely positioned child's edge — inside the page and
        // UA body margins. The canvas min-width does NOT drive it (measured:
        // 878.25 = 90 + 6 + (403 + 520)px·0.75 + 90 on a 960px-min-width slide).
        if (!(pageInfo?.WidthAssigned ?? false))
            foreach (var b in blocks)
                if (b.Slide is { } slw)
                {
                    double slExtentPx = 0;
                    foreach (var it in slw.Items)
                        slExtentPx = Math.Max(slExtentPx, it.LeftPx + it.WPx);
                    if (slExtentPx > 0)
                    {
                        var slNeedW = 90.0 + CardBodyPadPt + slExtentPx * 0.75 + 90.0;
                        if (slNeedW > pageWidth)
                        {
                            pageHeight = Math.Max(pageWidth, pageHeight);
                            pageWidth = slNeedW;
                            marginLeft = 90.0;
                            marginRight = 90.0;
                            marginTop = 72.0;
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
        if (edgeToEdgeDoc && blocks.Exists(b => b.IsTable))
        {
            const double A4TruePt = 210.0 / 25.4 * 72.0;   // 595.276
            var widenBase = Math.Abs(pageWidth - 595.0) < 0.5 ? A4TruePt : pageWidth;
            pageWidth = Math.Round((widenBase + ChartWidenSlackPt) * 4.0,
                MidpointRounding.AwayFromZero) / 4.0;
        }
        // Chart-card widen: the page grows so the inline-SVG chart fits at its
        // NATURAL size inside its width-billing container chrome, plus the engine's
        // growth allowance, quantized to the quarter-point grid. Both constants are
        // measured off the reference output: the chart report widens to exactly
        // round4(90 + (svg 419.72 + col pads 15) + 7.2 + 90) = 622.0, and the
        // zero-margin table pair grows by the same 7.2 (602.5 = 595.28 + 7.22
        // rounded) — a 0.1 in allowance on the content's natural width.
        if (chartCardDoc && !(pageInfo?.WidthAssigned ?? false))
        {
            double widestSvg = 0;
            foreach (var b in blocks)
                if (b.IsImage && b.ImageWidth > 0
                    && b.ImageSrc.StartsWith("inline-svg:", StringComparison.Ordinal))
                    widestSvg = Math.Max(widestSvg, b.ImageWidth * 0.75 + b.ImageWidenPadPt);
            if (widestSvg > 0)
            {
                var neededW = Math.Round(
                    (marginLeft + widestSvg + ChartWidenSlackPt + marginRight) * 4.0,
                    MidpointRounding.AwayFromZero) / 4.0;
                if (neededW > pageWidth)
                {
                    pageHeight = Math.Max(pageWidth, pageHeight);
                    pageWidth = neededW;
                }
            }
        }
        // The SSRS report opens at the raw 72 pt content top and flows to the
        // raw 72 pt bottom (measured: the first grid baseline seats at 86 =
        // 72 + the 2.05 mm spacer row + cell chrome, and page 1 runs to 753).
        if (ssrsReportDoc)
        {
            marginTop = 72.0;
            marginBottom = 72.0;
        }
        // SSRS report export: an oversized data-URI JPEG draws at the engine's
        // image viewport (612 pt — measured: the 1024 px photo lands 612×459 pt,
        // overflowing its column) and the sheet widens to hold it inside the
        // cell's 2 pt padding.
        if (ssrsReportDoc && !(pageInfo?.WidthAssigned ?? false)
            && Regex.Match(html, @"data:image/jpe?g;base64,([A-Za-z0-9+/=]+)",
                RegexOptions.IgnoreCase) is { Success: true } sjm)
        {
            try
            {
                var (sjw, sjh) = JpegDims(
                    System.Convert.FromBase64String(sjm.Groups[1].Value));
                if (sjw > 0)
                {
                    var sjNeed = marginLeft + 2.0
                        + Math.Min(sjw * 0.75, JpegViewportPt) + 2.0;
                    if (sjNeed > pageWidth) pageWidth = sjNeed;
                }
            }
            catch { }
        }

        // A body that declares its own width in PHYSICAL units widens the sheet to its
        // content. The width term is CONTENT-MEASURED
        // and pixel-quantized — it grows with the rows' laid-out extent and an
        // hr contributes its own percentage box — but a document whose label/span rows
        // wrap (hundreds of characters) SATURATES the measure at the declared body
        // width, and every such page then fits max(A4, 96 + bodyW + 87.3): the 96 is
        // this engine's own content left, the 87.3 the fitted right band, and the
        // sheet never falls below the A4 default (a 10 cm body keeps a 595 page).
        // Pixel-width bodies keep the ordinary flow; explicit page setup wins.
        if (!marginsExplicit && pageWidth <= 612.0)
        {
            var rbodyM = Regex.Match(html, @"<body\b[^>]*style\s*=\s*(['""])([^'""]*)\1",
                RegexOptions.IgnoreCase);
            if (rbodyM.Success)
            {
                var rbw = Regex.Match(rbodyM.Groups[2].Value,
                    @"(?<![-\w])width\s*:\s*([\d.]+\s*(?:cm|mm|in|pt))", RegexOptions.IgnoreCase);
                if (rbw.Success && TryParseLength(rbw.Groups[1].Value.Replace(" ", ""), out var rbwPt)
                    && rbwPt > 0 && marginLeft + rbwPt + ReportPageRightPt > pageWidth)
                {
                    pageWidth = marginLeft + rbwPt + ReportPageRightPt;
                    marginRight = ReportPageRightPt;
                }
            }
        }

        // UA flow: the page WIDTH also widens for a block image wider than the content
        // box — the image keeps its natural pixel size and the page expands
        // (753px image on default A4 → 90+6+564.75+90 = 750.75pt wide).
        // Word-filtered pages keep their measured sheet: an 817px absolutely
        // positioned banner does NOT grow the page (it overflows and clips).
        if (uaFlow)
        {
            double widestImg = 0;
            foreach (var b in blocks)
            {
                if (string.IsNullOrEmpty(b.ImageSrc)
                    // a %-max-width image clamps to the content box instead
                    || b.ImageMaxWFrac > 0) continue;
                // An inline-SVG placeholder widens by its viewport (CSS px) —
                // the 600px chart map opens a 96 + 450 + 90 sheet (measured).
                if (b.ImageSrc.StartsWith("inline-svg:", StringComparison.Ordinal))
                {
                    if (b.ImageWidth > 0 && b.ImageWidth * 0.75 > widestImg)
                        widestImg = b.ImageWidth * 0.75;
                    continue;
                }
                double iwPt = 0;
                // Word-filtered pages: only an image whose BYTES resolve grows
                // the sheet (the snip-capture email's base64 payload); the
                // 817px absolutely positioned banner whose file is missing
                // keeps the measured column sheet (it overflows and clips).
                if (b.ImageWidth > 0 && !msoFilteredDoc) iwPt = b.ImageWidth * 0.75;
                else if (msoFilteredDoc)
                {
                    // Word-filtered: only an image whose bytes RESOLVE grows the
                    // sheet, at its ATTRIBUTE width when one upscales the bitmap
                    // (the snip-capture email: a 615px PNG drawn at width=1920).
                    var ib = LoadConverterImage(b.ImageSrc, options);
                    if (ib is not null)
                        iwPt = b.ImageWidth > 0 ? b.ImageWidth * 0.75
                            : TryReadImagePixelSize(ib, out var ipw2, out _) && ipw2 > 0
                                ? ipw2 * 0.75 : 0;
                }
                else
                {
                    var ib = LoadConverterImage(b.ImageSrc, options);
                    if (ib is not null && TryReadImagePixelSize(ib, out var ipw, out _) && ipw > 0)
                        iwPt = ipw * 0.75;
                }
                if (iwPt > widestImg) widestImg = iwPt;
            }
            if (widestImg > 0)
            {
                // The widened filtered sheet closes at the bare UA right margin
                // (96 + image + 90, measured); other flows keep their own.
                var widenRight = msoFilteredDoc ? 90.0 : marginRight;
                var widenPage = marginLeft + widestImg + widenRight;
                if (widenPage > pageWidth)
                {
                    pageWidth = widenPage;
                    if (msoFilteredDoc) marginRight = widenRight;
                }
            }
        }

        // The body rule's face travels on every family-free block: the UA-flow
        // draw embeds it per run (the same path a <font face> block takes), so
        // the whole document sets in the body face instead of the UA serif. A
        // block whose captured family cannot draw (the stack's unresolvable
        // FIRST member) falls to the same face — that face IS the stack's first
        // resolvable member, exactly the CSS fallback the source renderer takes.
        if (uaStdSerif && uaBodyFace is not null)
            foreach (var ubb in blocks)
                if (string.IsNullOrEmpty(ubb.FontFamily)
                    || PosFace(ubb.FontFamily).ttf is null)
                    ubb.FontFamily = uaBodyFace;

        // One /Font resource dict shared by every page of this conversion (see EnsureFonts).
        var docFontDict = new Core.PdfDictionary();
        var doc = Document.Create();
        var page = doc.Pages.Add(pageWidth, pageHeight);
        EnsureFonts(page, docFontDict);

        // UA-serif flow with a NEGATIVE-margin element: the source renderer
        // clips page content at one body margin left of the content origin
        // (its content stream opens with `90 0 505 842 re W n`) — the box that
        // slid left of the clip loses its overhang there. Emitted un-nested so
        // it governs every stream appended after it; pages without negative
        // indents never carry ink there, so they need no clip.
        if (uaStdSerif && blocks.Any(b => b.LeftIndent < 0))
            page.AddContentStream(Encoding.ASCII.GetBytes(FormattableString.Invariant(
                $"{marginLeft - UaBodyMarginPt:0.##} 0 {pageWidth - marginLeft + UaBodyMarginPt:0.##} {pageHeight:0.##} re W n\n")));

        // Pull <title> for doc metadata before we lose it in stripping.
        var titleMatch = Regex.Match(html, @"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (titleMatch.Success)
            doc.Info.Title = DecodeEntities(titleMatch.Groups[1].Value).Trim();

        var contentWidth = pageWidth - marginLeft - marginRight;
        var y = pageHeight - marginTop;
        // Metric flow: the BODY top margin indents the first page only — continuation
        // pages resume at the raw content top.
        if (metricFlow) y -= bodyMarT;
        // body{margin:0}: with no body margin to collapse into, the first block's own UA
        // top margin (1em on a paragraph) reaches the canvas and pushes the first line
        // down; the flow otherwise drops a leading block's margin at the top of a page.
        // A leading EMPTY block carries no margin of its own, so it must not shift the
        // flow. Zeroed-body geometry only — the default-margin path bakes the drop into
        // its calibrated 89 pt top.
        if (bodyZeroMargin && !metricFlow && !printCoverDoc && blocks.Count > 0 && !blocks[0].IsTable
            && blocks[0].FontSize > 0 && !string.IsNullOrWhiteSpace(blocks[0].Text))
            y -= blocks[0].FontSize;
        // A browser aligns the TOP of the first line box to the content top, so the first
        // baseline sits further from the top for a larger first line (its line box is taller).
        // The default margin was calibrated to the HTML renderer for a default-size
        // (~11 pt) first line; when the first line's font is larger, lower the first baseline
        // by its font-size excess to give a top-aligned first line. Scoped
        // to the default-margin path so explicit-margin conversions stay byte-identical.
        // Print-cover documents own their geometry through the CSS box model (the
        // cover classes' margins and line factors) — the calibrated first-line
        // drops above and below must not shift their box chain.
        if (!marginsExplicit && !uaFlow && !printGrid && !printCoverDoc)
        {
            const double DefaultFirstFontSize = 11.0;
            const double FirstLineLeadingPerPt = 0.7647; // excess-pt → baseline drop
            var firstFontSize = blocks[0].FontSize;
            if (firstFontSize > DefaultFirstFontSize)
                y -= (firstFontSize - DefaultFirstFontSize) * FirstLineLeadingPerPt;
        }
        // An explicit zero TOP margin still lays a page's first line INSIDE the
        // page: its baseline drops by its own line box (+ its block margin), so the
        // ink top lands at the content top instead of clipping off the page edge.
        var hasZeroTopMargin = marginsExplicit && marginTop < 1e-9;
        var pendingTopDrop = hasZeroTopMargin;
        // Continuation pages of the escaped-attr dialect start at the REAL page margin
        // plus one 0.9em first-baseline drop. The dialect's nominal top margin is a
        // page-1 calibration — it bakes in the UA body inset and the first line's
        // ascent — so reusing it on every later page starts each one 6.2 pt low.
        double FreshPageTopY() => escapedAttrDoc
            ? pageHeight - 72 - 0.9 * 12
            : pageHeight - marginTop;
        // Shared 32×32 broken-image placeholder (drawn for unloadable images in the
        // escaped-attr dialect and on Word-filtered pages).
        Core.PdfIndirectRef? flowIconRef = null;
        // Word-filtered pages: the first broken image is the absolutely
        // positioned banner (its own seat), later ones sit at the content edge.
        var msoBrokenImgCount = 0;
        // The block just drawn was the escaped dialect's section rule: an inline RUN
        // right after it drops extra so its control box clears the rule (measured:
        // rule → run baseline = 17.2, a bare run line would sit at 11.9).
        var afterEscapedRule = false;
        // Push-button colours for the escaped-attr dialect: the page's button{} TAG
        // rule (the only kind of rule this dialect can match) supplies the fill —
        // its gradient's first stop — and the caption colour; UA grey otherwise.
        Color? dialectButtonFill = null;
        var dialectButtonTextRg = "0 0 0 rg";
        if (escapedAttrDoc)
        {
            dialectButtonFill = ParseCssColor("#F0F0F0");
            // The sheet splits the button rule: one rule carries the gradient (its
            // first stop is the fill), another the caption colour — read them all.
            var haveFill = false; var haveFg = false;
            foreach (Match btnRule in Regex.Matches(html,
                @"(?<![\w.#-])button\s*\{([^}]*)\}", RegexOptions.IgnoreCase))
            {
                var btnBody = btnRule.Groups[1].Value;
                if (!haveFill)
                {
                    var bgHex = Regex.Match(btnBody, "#[0-9a-fA-F]{6}");
                    if (bgHex.Success && ParseCssColor(bgHex.Value) is { } btnFill)
                    {
                        dialectButtonFill = btnFill;
                        haveFill = true;
                    }
                }
                if (!haveFg)
                {
                    var fgDecl = Regex.Match(btnBody, @"(?<![\w-])color\s*:\s*([^;}]+)", RegexOptions.IgnoreCase);
                    if (fgDecl.Success && ParseCssColor(fgDecl.Groups[1].Value.Trim()) is { } btnFg)
                    {
                        dialectButtonTextRg = FormattableString.Invariant(
                            $"{btnFg.R / 255.0:0.###} {btnFg.G / 255.0:0.###} {btnFg.B / 255.0:0.###} rg");
                        haveFg = true;
                    }
                }
            }
        }
        var sb = new StringBuilder();

        // CSS font-family → embedded TrueType. Each resolvable family is embedded once
        // (shared indirect font dict) and registered into each page's resources on first
        // use under a unique "FE<n>" resource name. Blocks whose family doesn't resolve
        // fall back to the Standard-14 FontRes (Helvetica/Courier).
        var embeddedFonts = new Dictionary<string, (string resName, Core.PdfIndirectRef fontRef)>(StringComparer.OrdinalIgnoreCase);
        var fontFileCache = new Dictionary<string, (int objNum, string embedName)>(StringComparer.Ordinal);
        bool usedCustomFont = false;

        string ResolveFontRes(Page pg, Block blk)
        {
            if (string.IsNullOrEmpty(blk.FontFamily)) return blk.FontRes;
            var family = blk.FontFamily!;
            if (!embeddedFonts.TryGetValue(family, out var entry))
            {
                var ttf = Text.FontRepository.GetTtfData(family);
                if (ttf is null) { embeddedFonts[family] = default; return blk.FontRes; }
                // /BaseFont: a style-qualified PostScript name (contains '-', e.g.
                // "HelveticaNeueLTStd-Roman") is used verbatim; an unqualified face
                // keeps the space-stripped family ("Arial", not "ArialMT").
                var baseName = family.Replace(" ", "");
                try
                {
                    var ttp = new Text.TrueTypeParser(ttf);
                    ttp.Parse();
                    if (!string.IsNullOrEmpty(ttp.PostScriptName) && ttp.PostScriptName != "Unknown"
                        && ttp.PostScriptName.Contains('-'))
                        baseName = ttp.PostScriptName.Replace(" ", "");
                }
                catch { /* keep the family-derived name */ }
                var fontDict = new Core.PdfDictionary();
                Text.FontEmbedder.EmbedIntoFontDict(doc, ttf, fontDict, baseName, fontFileCache);
                var objNum = doc.AllocateObjectNumber();
                doc.AddNewObject(objNum, fontDict, registerOverlay: true);
                entry = ($"FE{embeddedFonts.Count + 1}", new Core.PdfIndirectRef(objNum, 0));
                embeddedFonts[family] = entry;
            }
            if (entry.fontRef is null) return blk.FontRes; // unresolvable family (cached miss)
            RegisterPageFont(pg, entry.resName, entry.fontRef);
            usedCustomFont = true;
            return entry.resName;
        }

        // Radio inputs collected during layout, grouped (after the loop) into one
        // RadioButtonField per HTML `name` so each option surfaces as a
        // RadioButtonOptionField on Form.Fields.
        var radioOptions = new List<(string group, bool chk, Page page, Rectangle rect)>();

        // Radio groups built for GRID tables (the radio factory): one
        // RadioButtonField per HTML `name`, its options handed to the cells as
        // inline glyphs. Registered on doc.Form after the flow pass — the table
        // render pass places each option's widget at its drawn glyph.
        var gridRadioGroups = new Dictionary<string, Aspose.Pdf.Forms.RadioButtonField>(StringComparer.Ordinal);
        var gridRadioPages = new List<(Aspose.Pdf.Forms.RadioButtonField rbf, Page page)>();
        var gridRadioCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var gridRadioAnon = 0;

        // Internal-link support: where each named anchor (id / <a name>) rendered,
        // and the inline <a href> ranges pending link-annotation emission. Resolved
        // in a second pass after layout so #fragment links to later pages work.
        var anchorTargets = new Dictionary<string, (Page page, double y)>(StringComparer.Ordinal);
        var pendingLinks = new List<(Page page, Aspose.Pdf.Rectangle rect, string url, string? text)>();

        // Content streams of floated (align="left") tables, prepended to their pages after
        // the flow pass so float text leads the content order (floats paint first).
        var floatFirstOps = new List<(Page page, byte[] ops)>();

        // Fieldset frames: FsBox markers bracket each box; the gray frame draws
        // at the close over [top, cursor]. A legend leading the box pins the
        // frame's top under its own baseline. Table segments parse separately,
        // so they take the LIVE indent below.
        var fsStack = new Stack<(Page page, double topY)>();
        var fsIndentLive = 0.0;
        var fsBoxW = fieldsetDoc ? fsBodyPct * (pageWidth - 180.0) - 3.0 : 0.0;

        bool lastWasHardBreak = false;
        // Set when the flow cursor rests on a just-rendered table's bottom edge:
        // the next TEXT block's first baseline drops one line box first.
        bool pendingTableDrop = false;
        // Browser margin-collapse (UA-default flow): the gap between two adjacent
        // flow blocks is max(prev margin-bottom, next margin-top), not their sum.
        double uaPrevMarginBottom = 0;
        bool lastWasRow = false;
        double prevRowMarginBottomPx = 0;
        // Bottom margin, line height and font size the LAST text block applied — read
        // by the form-dialect <hr>/image branches to rewind the legacy full-line-box
        // advance to the CSS box bottom (baseline + descent) before their own spacing:
        // a rule sits desc + max(prev margin-bottom, rule margin-top) below the text
        // baseline, not a whole line box further.
        double prevFlowMarginBottom = 0;
        double prevFlowLineHeight = 0;
        double prevFlowFontSize = 0;
        // A left-floated image the flow has passed under, but not yet below: lines that
        // start above floatBottomY are laid out to its right.
        double floatBottomY = double.NegativeInfinity;
        double floatIndentPt = 0;
        // Set right after a form-dialect <hr> draws: the next text block must drop by
        // its own line box before its baseline (text hangs above the cursor; the rule
        // would be overprinted otherwise). Tables/images consume the flag as a no-op.
        bool afterRuleDrop = false;
        // Set after a synthesized form-row table: flow text following it (the next
        // section heading) needs its line-box drop plus the section gap kept
        // between a form block and the heading below it.
        bool afterFhTable = false;
        // Float-band / border-box layout state. A band narrows the content box per
        // column and rewinds the cursor to the band top between columns; a box draws
        // its border rectangle when it closes on the page it opened on. Bands and
        // boxes that fit the current page render structurally; one that page-breaks
        // mid-way degrades to sequential flow (no rewind across pages).
        var flowMarginLeft = marginLeft;
        var flowContentWidth = contentWidth;
        var bandStack = new Stack<(double SavedML, double SavedCW, double TopY, double MinEndY, Page StartPage)>();
        var boxStack = new Stack<(double XLeft, double TopY, double Width, double BorderPt, Page Page,
            double Gray, double PadSide, double SavedML, double SavedCW)>();
        // Print-grid stacked column scopes: narrow the ambient content width for the
        // enclosed blocks (no y reset — columns stack, unlike float-band columns).
        var colScopeStack = new Stack<(double SavedML, double SavedCW)>();
        // A float column is an overflow-hidden box: content past the page bottom is
        // CLIPPED, never paginated — the column ends at the page's content bottom and
        // the rest of its blocks are dropped until the next column/band boundary.
        var bandColClipped = false;
        // The last page any block drew on. A page-break-before must start a fresh page
        // whenever the current page already has content — the flow cursor alone can't
        // tell (a table continuation slice can leave y above the nominal content top).
        Page? contentPage = null;

        // The full-document UA-default flow collapses the first flow block's top margin
        // into the document top margin (browser margin-collapse); true until that block
        // is laid out. The fieldset worksheet's body PADDING blocks that collapse.
        var uaTopMarginPending = uaStdSerif && !fieldsetDoc;

        // UA-flow floats: consecutive floated text blocks share ONE line (a right
        // float takes no vertical space of its own — the next float lays out level
        // with it). Every float in such a run except the last rewinds the cursor.
        if (uaStdSerif)
            for (var fi = 0; fi + 1 < blocks.Count; fi++)
                if ((blocks[fi].FloatLeft || blocks[fi].FloatRight)
                    && (blocks[fi + 1].FloatLeft || blocks[fi + 1].FloatRight)
                    && !blocks[fi].IsTable && !blocks[fi + 1].IsTable
                    && !string.IsNullOrEmpty(blocks[fi].Text)
                    && !string.IsNullOrEmpty(blocks[fi + 1].Text))
                    blocks[fi].NoAdvanceY = true;

        // Real-metric measurement face for a text block inside a float-column band: its
        // declared family, the bold variant for a bold run, falling back to the plain
        // family then Arial. Used only by the band flow (see the wrap call below).
        string BandMeasureFace(Block b)
        {
            var fam = string.IsNullOrEmpty(b.FontFamily) ? "Times New Roman" : b.FontFamily!;
            if ((b.FontRes == "F2" || b.EmBold) && PosFace(fam + " Bold").ttf is not null)
                return fam + " Bold";
            return PosFace(fam).ttf is not null ? fam : "Arial";
        }

        // The priority-matrix section of the sectioned dead-stylesheet report
        // renders whole from its measured ladder (see SpMatrix.cs) — collect its
        // blocks up front so the loop can consume them in one piece.
        HashSet<Block>? spBlocks = null;
        Block? spFirst = null;
        if (uaStdSerif && deadExternalCss)
        {
            // The section is its own page: the diagram table sits at most a
            // heading and an intro behind its page break (an unrelated wrapper
            // table elsewhere can carry the class string too — skip those).
            for (var ti = 0; ti < blocks.Count; ti++)
            {
                if (!blocks[ti].IsTable || !IsSpMatrixTable(blocks[ti].TableHtml)) continue;
                var s0 = ti;
                while (s0 > 0 && !blocks[s0].PageBreakBefore && ti - s0 <= 3) s0--;
                if (!blocks[s0].PageBreakBefore || ti - s0 > 3) continue;
                var s1 = ti + 1;
                while (s1 < blocks.Count && !blocks[s1].PageBreakBefore) s1++;
                spBlocks = new HashSet<Block>();
                for (var sbi = s0; sbi < s1; sbi++) spBlocks.Add(blocks[sbi]);
                spFirst = blocks[s0];
                break;
            }
        }

        // A top-level table opening after a body TEXT line sits a small gap
        // below it (measured: the summary table's top = the rating
        // line's bottom + 3.5 — identical in the shipped template and the
        // current engine's render).
        var prevBlockWasText = false;
        // Float-label pairing state (see the consume site below).
        var pendingFloatLabelPt = 0.0;
        var pendingFloatLabelY = 0.0;

        // Word-filtered pages: an image too tall for one sheet spills over the page
        // boundary wherever it starts, so it begins on a FRESH sheet — and the quoted
        // section that introduces it (the rule-topped header block and the lines under
        // it) travels with it rather than being stranded at the foot of the previous
        // page. Collect the header blocks whose section ends in such an image; the flow
        // breaks before them.
        static bool IsBlankFlowBlock(Block b) =>
            string.IsNullOrWhiteSpace(b.Text) && string.IsNullOrEmpty(b.ImageSrc) && !b.IsTable;
        System.Collections.Generic.HashSet<Block>? msoKeepWithImage = null;
        if (msoFilteredDoc)
        {
            var blockList = blocks as System.Collections.Generic.IList<Block> ?? new System.Collections.Generic.List<Block>(blocks);
            var contentBand = pageHeight - marginTop - marginBottom;
            for (var bi = 0; bi < blockList.Count; bi++)
            {
                var b = blockList[bi];
                if (b.ImageHeight <= 0 || string.IsNullOrEmpty(b.ImageSrc)) continue;
                var bw0 = b.ImageWidth * 0.75;
                var bh0 = b.ImageHeight * 0.75;
                if (contentWidth > 0 && bw0 > contentWidth) bh0 *= contentWidth / bw0;
                if (bh0 <= contentBand) continue;   // fits a sheet: the normal break applies
                for (var back = bi - 1; back >= 0 && bi - back <= 12; back--)
                {
                    if (!blockList[back].BorderTopOnly) continue;
                    // The section starts at the run of blank paragraphs that separates it
                    // from the message above — those trailing blanks travel with it, so the
                    // section lands the same distance down the fresh sheet as it sat below
                    // the previous message.
                    var head = back;
                    while (head > 0 && IsBlankFlowBlock(blockList[head - 1])) head--;
                    (msoKeepWithImage ??= new System.Collections.Generic.HashSet<Block>()).Add(blockList[head]);
                    break;
                }
            }
        }
        foreach (var block in blocks)
        {
            // The quoted section heading an over-tall image opens its own sheet.
            if (msoKeepWithImage is not null && msoKeepWithImage.Contains(block)
                && y < pageHeight - marginTop - 0.01)
            {
                page = doc.Pages.Add(pageWidth, pageHeight);
                EnsureFonts(page, docFontDict);
                contentPage = page;
                y = pageHeight - marginTop;
            }
            var tableAfterText = block.IsTable && prevBlockWasText;
            if (!string.IsNullOrEmpty(block.Text) && !block.IsTable) prevBlockWasText = true;
            else if (block.IsTable) prevBlockWasText = false;
            if (spBlocks is not null && spBlocks.Contains(block))
            {
                if (ReferenceEquals(block, spFirst))
                {
                    page = doc.Pages.Add(pageWidth, pageHeight);
                    EnsureFonts(page, docFontDict);
                    contentPage = page;
                    var spHeads = new List<string>();
                    var spTable = "";
                    foreach (var secB in blocks)
                        if (spBlocks.Contains(secB))
                        {
                            if (secB.IsTable) spTable = secB.TableHtml ?? "";
                            else if (!string.IsNullOrEmpty(secB.Text)) spHeads.Add(secB.Text!);
                        }
                    RenderSpMatrixSection(page, pageHeight, marginLeft, spHeads, spTable, inlineSvgs);
                    y = marginBottom;
                    lastWasHardBreak = false;
                }
                continue;
            }
            // Margin-collapse partner: the previous FLOW block's bottom margin. Any
            // non-flow block (table, image, input, spacer) between two flow blocks
            // breaks the adjacency, so the pair no longer collapses.
            var uaPrevMB = uaPrevMarginBottom;
            uaPrevMarginBottom = 0;
            if (block.FloatBandStart)
            {
                bandStack.Push((marginLeft, contentWidth, y, y, page));
                continue;
            }
            if (block.FloatColStart && bandStack.Count > 0)
            {
                var band = bandStack.Pop();
                band.MinEndY = Math.Min(band.MinEndY, y);
                if (ReferenceEquals(page, band.StartPage)) y = band.TopY;
                marginLeft = band.SavedML + block.FloatStartFrac * band.SavedCW;
                contentWidth = Math.Max(20, block.FloatWidthFrac * band.SavedCW);
                bandStack.Push(band);
                y -= block.FloatPadTopPt;
                bandColClipped = false;
                continue;
            }
            if (block.FloatBandEnd && bandStack.Count > 0)
            {
                var band = bandStack.Pop();
                if (ReferenceEquals(page, band.StartPage)) y = Math.Min(band.MinEndY, y);
                marginLeft = band.SavedML;
                contentWidth = band.SavedCW;
                bandColClipped = false;
                continue;
            }
            if (block.ColScopeStart)
            {
                colScopeStack.Push((marginLeft, contentWidth));
                contentWidth = Math.Max(20, block.FloatWidthFrac * contentWidth - block.ColPadPt);
                continue;
            }
            if (block.ColScopeEnd)
            {
                if (colScopeStack.Count > 0)
                    (marginLeft, contentWidth) = colScopeStack.Pop();
                continue;
            }
            if (block.BoxStart)
            {
                boxStack.Push((marginLeft, y + block.BoxAscentPt, contentWidth, block.BoxBorderPt, page,
                    block.BoxBorderGray, block.BoxPadSidePt, marginLeft, contentWidth));
                y -= block.BoxPadTopPt;
                if (block.BoxPadSidePt > 0)
                {
                    marginLeft += block.BoxPadSidePt / 2;
                    contentWidth = Math.Max(20, contentWidth - block.BoxPadSidePt);
                }
                continue;
            }
            if (block.BoxEnd && boxStack.Count > 0)
            {
                var box = boxStack.Pop();
                marginLeft = box.SavedML;
                contentWidth = box.SavedCW;
                y -= block.BoxPadBottomPt;
                var strokeG = box.Gray > 0
                    ? box.Gray.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " G"
                    : "0 G";
                if (ReferenceEquals(page, box.Page) && box.BorderPt > 0 && box.TopY - y > 1)
                {
                    var rect = FormattableString.Invariant(
                        $"q {box.BorderPt:0.##} w {strokeG} {box.XLeft:0.##} {y:0.##} {box.Width:0.##} {box.TopY - y:0.##} re S Q\n");
                    page.AddContentStream(System.Text.Encoding.ASCII.GetBytes(rect));
                }
                else if (!ReferenceEquals(page, box.Page) && box.BorderPt > 0)
                {
                    // The box's content spilled past its start page: the visible part of the
                    // border is an open-bottom frame — top edge plus both sides running to
                    // just below the bottom content margin (the cut
                    // box's sides stop ~10 pt into the margin, not at the page edge).
                    var xr = box.XLeft + box.Width;
                    var yb = Math.Max(0, marginBottom - 10);
                    var frame = FormattableString.Invariant(
                        $"q {box.BorderPt:0.##} w {strokeG} {box.XLeft:0.##} {yb:0.##} m {box.XLeft:0.##} {box.TopY:0.##} l {xr:0.##} {box.TopY:0.##} l {xr:0.##} {yb:0.##} l S Q\n");
                    box.Page.AddContentStream(System.Text.Encoding.ASCII.GetBytes(frame));
                }
                if (block.BoxMarginBottomPt > 0) y -= block.BoxMarginBottomPt;
                continue;
            }
            // Remaining blocks of a clipped float column are dropped (overflow:hidden).
            if (bandColClipped && bandStack.Count > 0) continue;
            var wasRow = lastWasRow;
            lastWasRow = false;
            var prevRowBottomPx = prevRowMarginBottomPx;
            prevRowMarginBottomPx = 0;
            var blockFontSize = block.FontSize;
            var lineHeight = blockFontSize * 1.3;
            if ((sectionedReport || escapedAttrDoc) && blockFontSize > 0)
                lineHeight = NormalLineHeightPt(blockFontSize);
            // A class rule's unitless line-height (coverStyles mode): the cover
            // title's line-height:1 pitches at the font size, the date's 3 leaves
            // its authored air below.
            if (block.LineFactor > 0 && blockFontSize > 0)
                lineHeight = blockFontSize * block.LineFactor;
            // Text in a band column advances at the CSS line box of its font size
            // (round(pt·4/3·1.15)px·0.75 — 12 pt for a 10.5 pt line, the box the band
            // tables already use): centered heading stacks, small blank spacers, and
            // body-size (≥10 pt) text lines — the 1.3-em pitch accumulates a point per
            // line and pushes a full column several lines past its layout height.
            // Sub-10 pt text lines keep the legacy pitch (calibrated card flow).
            if (floatBandDoc && bandStack.Count > 0 && blockFontSize > 0
                && (block.AlignCenterAttr
                    || (blockFontSize >= 10 && !string.IsNullOrWhiteSpace(block.Text))
                    || (string.IsNullOrWhiteSpace(block.Text) && blockFontSize < 10)))
                lineHeight = Math.Round(blockFontSize * 4.0 / 3.0 * 1.15) * 0.75;
            // Metric flow: browser line box + half-leading baseline; measurement face is
            // the body face (bold variant for bold blocks).
            double metricDrop = 0;
            var metricMeasureFace = metricFace;
            if (metricFlow && WinMetricsFor(metricFace) is { } mfm)
            {
                // UA defaults use the serif's hhea line box, px-rounded (13.5pt @12
                // — same as 1.125em there — but 27.75 @24, 21 @18, 16.5 @14.04:
                // all probed against the source renderer's h1-h3 list items).
                // The print grid uses the CSS body line-height, px-rounded.
                lineHeight = printGrid
                    ? Math.Round(blockFontSize / 0.75 * printGridLineFactor, MidpointRounding.AwayFromZero) * 0.75
                    // The article sheet's own unitless line-height, resolved against
                    // each block's size and px-rounded the way the source renderer does.
                    : articleFlow
                        ? Math.Round(blockFontSize / 0.75 * articleLineFactor, MidpointRounding.AwayFromZero) * 0.75
                    : uaFlow ? MetricLineHeight(blockFontSize, HheaLineSumFor(metricFace) ?? mfm.sum)
                    : MetricLineHeight(blockFontSize, metricLineSum > 0 ? metricLineSum : mfm.sum);
                // A block that carries its OWN resolvable face lines on that
                // face's box (a Word-filtered span's Tahoma pitches 12 where the
                // serif box gives 11.25) and seats its baseline by its metrics.
                var blockFaceFm = mfm;
                if (uaFlow && block.FontFamily is { } bffFam
                    && !bffFam.Equals(metricFace, StringComparison.OrdinalIgnoreCase)
                    && WinMetricsFor(bffFam) is { } bffFm)
                {
                    lineHeight = MetricLineHeight(blockFontSize, HheaLineSumFor(bffFam) ?? bffFm.sum);
                    blockFaceFm = bffFm;
                }
                // An inline px line-height fixes the LINE BOX outright; the
                // baseline keeps its half-leading seat inside the bigger box.
                if (uaFlow && block.LineBoxPt > 0) lineHeight = block.LineBoxPt;
                metricDrop = MetricBaselineDrop(blockFontSize, lineHeight, blockFaceFm);
                if (block.FontRes == "F2") metricMeasureFace = metricFace + "-Bold";
            }

            // Shared control placement: the AcroForm field plus its visible box at
            // (xLeft, baseline). Under the control-box dialect the box STRADDLES its
            // line — top edge above the text baseline, bottom hanging just under
            // it. The legacy dialects keep
            // their calibrated top-at-cursor box. Used by the standalone control
            // branch, the inline-run layout, and the dialect grid's in-cell controls.
            void EmitControlAt(Block ctl, double xLeft, double baseY, double? aboveOverride = null)
            {
                var cW = ctl.InputWidth > 0
                    ? System.Math.Min(ctl.InputWidth, contentWidth - ctl.LeftIndent)
                    : contentWidth - ctl.LeftIndent;
                var cH = ctl.InputHeight > 0 ? ctl.InputHeight : lineHeight;
                var above = aboveOverride ?? (!ctl.InputDrawValue ? 0
                    : ctl.IsSelectBox ? SelectBoxAboveBaselinePt : InputBoxAboveBaselinePt);
                var lx = xLeft + (ctl.IsSelectBox ? SelectSideBearingPt : 0);
                var field = new Forms.TextBoxField(page,
                    new Rectangle(lx, baseY + above - cH, lx + cW, baseY + above))
                {
                    Multiline = ctl.InputMultiline,
                    ReadOnly = ctl.InputReadOnly,
                };
                // A textarea's first value line seats 10.11 under the
                // box top (2 pt inset + the face ascent), not one full line-height
                // down. Persist the pitch on /DS — Flatten re-wraps the field from
                // its dictionary, so an in-memory override would be lost.
                if (ctl.InputDrawValue && ctl.InputMultiline)
                {
                    field.StyleLineHeightPt = TextareaValuePitchPt;
                    field.Dict.Set("DS", new Core.PdfString(
                        Encoding.ASCII.GetBytes(FormattableString.Invariant($"line-height: {TextareaValuePitchPt}pt"))));
                }
                // Carry the HTML name/id through to the AcroForm field name so
                // callers can find the field by FullName.
                if (!string.IsNullOrEmpty(ctl.InputName)) field.PartialName = ctl.InputName;
                if (!string.IsNullOrEmpty(ctl.InputValue)) field.Value = ctl.InputValue;
                // A control the flow draws as a box shows its value at
                // 10 pt — a text input in the UI face, a textarea in the typewriter face.
                // The widget's own appearance is what a Flatten() stamps onto the page,
                // so setting it here is what puts the value inside the box.
                if (ctl.InputDrawValue)
                    field.DefaultAppearance = new Annotations.DefaultAppearance(
                        ctl.InputValueMono ? "Courier" : "Helvetica", 10);
                doc.Form.Add(field, page.Number);
                // Draw a visible border box for the input so it reads as a form field
                // in the rendered page (the widget's own appearance is not rasterised).
                // The 1 pt stroke runs HALF A POINT INSIDE the widget rect, so
                // the visible box is exactly the widget's 15.75 — stroking the rect
                // itself runs a point taller and crowds the label below.
                if (ctl.InputDrawValue)
                    DrawBox(page, lx + 0.5, baseY + above - cH + 0.5, cW - 1, cH - 1,
                        border: Color.Black, borderWidth: 1.0, fill: null);
                else
                    DrawBox(page, lx, baseY + above - cH, cW, cH,
                        border: Color.FromRgb(130, 130, 130), borderWidth: 0.75, fill: null);
            }

            // A positioned serif fragment for the escaped-attr dialect: the REAL
            // TimesNewRoman faces, embedded Type0 (their glyph shapes are what
            // reaches the rendered page); Standard-14 serif as the fallback.
            void EmitSerifRun(string text, string res, double pt, double x, double baseY)
            {
                var famName = res == "F6" ? "Times New Roman Bold"
                    : res == "F7" ? "Times New Roman Italic" : "Times New Roman";
                var baseName = res == "F6" ? "TimesNewRomanBold"
                    : res == "F7" ? "TimesNewRomanItalic" : "TimesNewRoman";
                if (PosFace(famName).ttf is { } srTtf
                    && page.Dict.Get("Resources") is Core.PdfDictionary srRes
                    && srRes.Get("Font") is Core.PdfDictionary srFd)
                {
                    var (rn, hex) = Text.Type0FontEmbedder.Embed(srFd, srTtf, baseName,
                        text, stripSpacesInBaseFont: true);
                    page.AddContentStream(Encoding.ASCII.GetBytes(FormattableString.Invariant(
                        $"BT /{rn} {pt:0.##} Tf 1 0 0 1 {x:0.##} {baseY:0.##} Tm <{System.Convert.ToHexString(hex)}> Tj ET\n")));
                }
                else
                    page.AddContentStream(Encoding.ASCII.GetBytes(FormattableString.Invariant(
                        $"BT /{res} {pt:0.##} Tf 1 0 0 1 {x:0.##} {baseY:0.##} Tm ({EscapePdfString(text)}) Tj ET\n")));
            }

            // CSS page-break-before:always — start this block on a fresh page (unless we're
            // already at the top of one, so a break as the very first content doesn't add a
            // blank leading page).
            var brokeForRule = false;
            if (block.PageBreakBefore
                && (ReferenceEquals(page, contentPage) || y < pageHeight - marginTop - 1e-3))
            {
                page = doc.Pages.Add(pageWidth, pageHeight);
                EnsureFonts(page, docFontDict);
                y = pageHeight - marginTop; pendingTopDrop = hasZeroTopMargin;
                brokeForRule = block.IsHorizontalRule;
            }

            // A band-dialect <hr> riding its page break: the rule paints inside the fresh
            // page's top margin (≈10 pt above the content top) and the following content
            // flows from the content top as if the rule weren't there. Mid-page rules
            // keep the legacy (spacing-only) path.
            if (block.IsHorizontalRule && floatBandDoc && brokeForRule)
            {
                // Thickness ≈ 0.48 pt per SIZE unit and a 3.6 pt rise above the content
                // top: a rule that carried its page break rides the top margin.
                var ruleH = sectionedReport ? 1.5 : Math.Max(0.75, block.RuleWidth * 0.48);
                DrawBox(page, marginLeft, y + 3.6 - ruleH, contentWidth, ruleH,
                    null, 0, block.RuleColor ?? ParseCssColor("#999999"));
                // The rule consumed the break itself — the next block flows from the
                // content top without inheriting a first-block top-margin drop.
                pendingTopDrop = false;
                lastWasHardBreak = false;
                contentPage = page;
                continue;
            }

            if (block.IsTable && escapedAttrDoc)
            {
                // Escaped-attr dialect grid — the HTML4 default frame:
                // outer OUTSET border (top/left
                // #555, bottom/right black), every cell INSET (top/left black,
                // bottom/right #555), 0.75 pt lines, 2.25 pt edge spacing and 1.5 pt
                // between cells; Times 12 cells with bold headers, columns sized to
                // the widest cell content + 1.5 pt side padding; form controls occupy
                // their control boxes INSIDE cells.
                afterEscapedRule = false;
                var trRows = new List<List<(bool Header, List<Block> Items)>>();
                foreach (Match trm in Regex.Matches(block.TableHtml ?? "",
                    @"<tr\b[^>]*>([\s\S]*?)</tr\s*>", RegexOptions.IgnoreCase))
                {
                    var cellsRow = new List<(bool, List<Block>)>();
                    foreach (Match cm in Regex.Matches(trm.Groups[1].Value,
                        @"<(td|th)\b[^>]*>([\s\S]*?)</\1\s*>", RegexOptions.IgnoreCase))
                    {
                        var isTh = cm.Groups[1].Value.Equals("th", StringComparison.OrdinalIgnoreCase);
                        var items = new List<Block>();
                        foreach (var cb in ParseBlocks(cm.Groups[2].Value, css,
                            bodyFontSize: 12, controlBoxes: true))
                        {
                            if (cb.InlineItems is { } inner) items.AddRange(inner);
                            else if (!cb.IsHardBreak || !string.IsNullOrEmpty(cb.Text)) items.Add(cb);
                        }
                        cellsRow.Add((isTh, items));
                    }
                    if (cellsRow.Count > 0) trRows.Add(cellsRow);
                }
                if (trRows.Count == 0) { lastWasHardBreak = false; continue; }

                const double GridEdgePad = 2.25, GridCellGap = 1.5, GridCellPad = 1.5;
                var nCols = 0;
                foreach (var r in trRows) nCols = System.Math.Max(nCols, r.Count);

                // Column widths: every column
                // floors at its MIN-CONTENT — the widest unbreakable piece across its
                // cells, where a hyphen IS a break opportunity, controls count whole,
                // and a BUTTON counts nothing (it overhangs its cell) — and the
                // remaining space distributes proportional to SLACK (the unwrapped
                // width still wanted over the floor). This sizes
                // all eight Employer-grid columns within a point: 'Employer Name'
                // rides one line while 'Contact Person' wraps. Widths measure in the
                // REAL TimesNewRoman metrics the cells draw with.
                double GridMeasure(bool bold, string s, double pt)
                    => MeasureFaceText(bold ? "Times New Roman Bold" : "Times New Roman", s, pt);
                (double Full, double Min) CellWidths(List<Block> items, bool header)
                {
                    double full = 0, min = 0;
                    foreach (var it in items)
                    {
                        if (it.IsButton) continue;
                        if (it.IsInputField)
                        {
                            var w = it.InputWidth + (it.IsSelectBox ? 2 * SelectSideBearingPt : 0);
                            full += w; min = System.Math.Max(min, w);
                        }
                        else if (!string.IsNullOrEmpty(it.Text))
                        {
                            var bold = header || it.FontRes == "F2";
                            var fpt = it.FontSize > 0 ? it.FontSize : EscapedBodyFontPt;
                            full += GridMeasure(bold, it.Text, fpt);
                            foreach (var word in it.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                            {
                                var hy = word.IndexOf('-');
                                if (hy > 0 && hy < word.Length - 1)
                                {
                                    min = System.Math.Max(min, GridMeasure(bold, word[..(hy + 1)], fpt));
                                    min = System.Math.Max(min, GridMeasure(bold, word[(hy + 1)..], fpt));
                                }
                                else
                                    min = System.Math.Max(min, GridMeasure(bold, word, fpt));
                            }
                        }
                    }
                    return (full, min);
                }
                var colFull = new double[nCols];
                var colMin = new double[nCols];
                foreach (var r in trRows)
                    for (int ci = 0; ci < r.Count; ci++)
                    {
                        var (cf, cm) = CellWidths(r[ci].Items, r[ci].Header);
                        colFull[ci] = System.Math.Max(colFull[ci], cf + 2 * GridCellPad);
                        colMin[ci] = System.Math.Max(colMin[ci], cm + 2 * GridCellPad);
                    }
                var colW = new double[nCols];
                var gridChrome = 2 * GridEdgePad + GridCellGap * (nCols - 1);
                {
                    // An empty column still keeps a sliver of a cell.
                    for (int ci = 0; ci < nCols; ci++)
                    {
                        colMin[ci] = System.Math.Max(colMin[ci], 10.5);
                        colFull[ci] = System.Math.Max(colFull[ci], colMin[ci]);
                    }
                    double fullSum = gridChrome, minSum = gridChrome;
                    foreach (var w in colFull) fullSum += w;
                    foreach (var w in colMin) minSum += w;
                    if (fullSum <= contentWidth)
                        for (int ci = 0; ci < nCols; ci++) colW[ci] = colFull[ci];
                    else if (minSum >= contentWidth)
                    {
                        var scale = (contentWidth - gridChrome) / (minSum - gridChrome);
                        for (int ci = 0; ci < nCols; ci++) colW[ci] = colMin[ci] * scale;
                    }
                    else
                    {
                        var surplus = contentWidth - minSum;
                        double slackSum = 0;
                        for (int ci = 0; ci < nCols; ci++)
                            slackSum += System.Math.Max(0, colFull[ci] - colMin[ci]);
                        for (int ci = 0; ci < nCols; ci++)
                        {
                            var s = System.Math.Max(0, colFull[ci] - colMin[ci]);
                            colW[ci] = colMin[ci] + (slackSum > 1e-6 ? surplus * s / slackSum : 0);
                        }
                    }
                }

                // Assemble every cell's wrapped lines: row height comes from the
                // tallest cell (16.5 floor = one 13.5 line + the cell's 3 pt of
                // vertical padding) and a shorter cell CENTRES in its row.
                (List<(List<(Block? Ctl, string? Txt, double XOff, double FontPt, string Res)> Items, double H)> Lines, double ContentH)
                    AssembleCell(List<Block> items, bool header, double availW)
                {
                    var cellLines = new List<(List<(Block? Ctl, string? Txt, double XOff, double FontPt, string Res)>, double)>();
                    var cl = new List<(Block? Ctl, string? Txt, double XOff, double FontPt, string Res)>();
                    double pen = 0, clH = 13.5;
                    void EndCellLine()
                    {
                        if (cl.Count == 0) return;
                        cellLines.Add((cl, clH));
                        cl = new List<(Block? Ctl, string? Txt, double XOff, double FontPt, string Res)>();
                        pen = 0; clH = 13.5;
                    }
                    foreach (var it in items)
                    {
                        if (it.IsInputField || it.IsButton)
                        {
                            var w = it.IsInputField
                                ? it.InputWidth + (it.IsSelectBox ? 2 * SelectSideBearingPt : 0)
                                : it.ButtonCaption.Length > 0
                                    ? MeasureStd14("Helvetica", it.ButtonCaption, 10) + ButtonChromeWPt : EmptyButtonWPt;
                            var h = it.IsInputField ? it.InputHeight
                                : it.ButtonCaption.Length > 0 ? ButtonHeightPt : EmptyButtonHPt;
                            if (cl.Count > 0 && pen + w > availW + 1e-6) EndCellLine();
                            cl.Add((it, null, pen, 0, ""));
                            // A control FILLS its cell: a 16.17 combo
                            // sits flush in a 16.4 cell (borders coincide), so the
                            // control's line costs its height minus the cell's own
                            // 3 pt of vertical padding.
                            clH = System.Math.Max(clH, h - 3);
                            pen += w;
                        }
                        else if (!string.IsNullOrEmpty(it.Text))
                        {
                            var res = header || it.FontRes == "F2" ? "F6"
                                : it.FontRes == "F3" ? "F7" : "F5";
                            var bold = res == "F6";
                            var fpt = it.FontSize > 0 ? it.FontSize : EscapedBodyFontPt;
                            int p = 0;
                            while (p < it.Text.Length)
                            {
                                var sp = it.Text.IndexOf(' ', p);
                                var wordEnd = sp < 0 ? it.Text.Length : sp + 1;
                                while (wordEnd < it.Text.Length && it.Text[wordEnd] == ' ') wordEnd++;
                                var word = it.Text.Substring(p, wordEnd - p);
                                p = wordEnd;
                                // A hyphen inside a word is a break opportunity too —
                                // 'Perfetto-Tullo' wraps after the hyphen.
                                var segStart = 0;
                                while (segStart < word.Length)
                                {
                                    var hy = word.IndexOf('-', segStart);
                                    var segEnd = hy >= 0 && hy < word.Length - 1 ? hy + 1 : word.Length;
                                    var token = word[segStart..segEnd];
                                    segStart = segEnd;
                                    var wTrim = GridMeasure(bold, token.TrimEnd(' '), fpt);
                                    if (cl.Count > 0 && pen + wTrim > availW + 1e-6) EndCellLine();
                                    var drawTok = cl.Count == 0 ? token.TrimStart(' ') : token;
                                    if (drawTok.Length == 0) continue;
                                    cl.Add((null, drawTok, pen, fpt, res));
                                    pen += GridMeasure(bold, drawTok, fpt);
                                }
                            }
                        }
                    }
                    EndCellLine();
                    double contentH = 3;
                    foreach (var (_, lh) in cellLines) contentH += lh;
                    if (cellLines.Count == 0) contentH = 16.5;
                    return (cellLines, contentH);
                }
                var planRows = new List<(List<(List<(Block? Ctl, string? Txt, double XOff, double FontPt, string Res)> Items, double H)> Lines, double ContentH)[]>();
                var rowHs = new List<double>();
                foreach (var r in trRows)
                {
                    var plans = new (List<(List<(Block? Ctl, string? Txt, double XOff, double FontPt, string Res)> Items, double H)> Lines, double ContentH)[r.Count];
                    double rh = 16.5;
                    for (int ci = 0; ci < r.Count; ci++)
                    {
                        plans[ci] = AssembleCell(r[ci].Items, r[ci].Header, colW[ci] - 2 * GridCellPad);
                        rh = System.Math.Max(rh, plans[ci].ContentH);
                    }
                    planRows.Add(plans);
                    rowHs.Add(rh);
                }
                double tableW = gridChrome;
                foreach (var w in colW) tableW += w;
                double tableH = 2 * GridEdgePad + GridCellGap * (trRows.Count - 1);
                foreach (var rh in rowHs) tableH += rh;

                // The grid's top edge sits one text ascent above the cursor (the flow
                // runs in baseline space); a grid that no longer fits moves whole.
                var gridTop = y + 0.9 * 12;
                if (gridTop - tableH < marginBottom
                    && tableH <= FreshPageTopY() - marginBottom
                    && y < FreshPageTopY() - 1e-3)
                {
                    page = doc.Pages.Add(pageWidth, pageHeight);
                    EnsureFonts(page, docFontDict);
                    y = FreshPageTopY(); pendingTopDrop = hasZeroTopMargin;
                    gridTop = y + 0.9 * 12;
                }
                var gridDark = ParseCssColor("#555555");
                var gx = marginLeft;
                // Outer OUTSET frame.
                DrawBox(page, gx, gridTop - 0.75, tableW, 0.75, null, 0, gridDark);
                DrawBox(page, gx, gridTop - tableH, tableW, 0.75, null, 0, Color.Black);
                DrawBox(page, gx, gridTop - tableH, 0.75, tableH, null, 0, gridDark);
                DrawBox(page, gx + tableW - 0.75, gridTop - tableH, 0.75, tableH, null, 0, Color.Black);
                var rowTop = gridTop - GridEdgePad;
                for (int ri = 0; ri < trRows.Count; ri++)
                {
                    var r = trRows[ri];
                    var rh = rowHs[ri];
                    var cx = gx + GridEdgePad;
                    for (int ci = 0; ci < r.Count; ci++)
                    {
                        var cw = colW[ci];
                        // Cell INSET frame.
                        DrawBox(page, cx, rowTop - 0.75, cw, 0.75, null, 0, Color.Black);
                        DrawBox(page, cx, rowTop - rh, cw, 0.75, null, 0, gridDark);
                        DrawBox(page, cx, rowTop - rh, 0.75, rh, null, 0, Color.Black);
                        DrawBox(page, cx + cw - 0.75, rowTop - rh, 0.75, rh, null, 0, gridDark);
                        var (cellLines, cellContentH) = planRows[ri][ci];
                        // vertical-align: middle — a shorter cell centres in its row.
                        var vaOff = System.Math.Max(0, (rh - cellContentH) / 2);
                        var lineBase = rowTop - vaOff - 12.3;
                        foreach (var (lineItems, lineH) in cellLines)
                        {
                            // A header cell centres each of its lines (the th default).
                            double cellHOff = 0;
                            if (r[ci].Header)
                            {
                                double lineW = 0;
                                foreach (var (lc, lt, lx, lp, lr) in lineItems)
                                    lineW = System.Math.Max(lineW, lx + (lc is null
                                        ? GridMeasure(lr == "F6", lt ?? "", lp)
                                        : lc.IsInputField
                                            ? lc.InputWidth + (lc.IsSelectBox ? 2 * SelectSideBearingPt : 0)
                                            : lc.ButtonCaption.Length > 0
                                                ? MeasureStd14("Helvetica", lc.ButtonCaption, 10) + ButtonChromeWPt
                                                : EmptyButtonWPt));
                                cellHOff = System.Math.Max(0, (cw - 2 * GridCellPad - lineW) / 2);
                            }
                            foreach (var (ctl, txt, xOff, fpt, res) in lineItems)
                            {
                                var ix = cx + GridCellPad + cellHOff + xOff;
                                if (ctl is null)
                                {
                                    if (!string.IsNullOrEmpty(txt)) EmitSerifRun(txt, res, fpt, ix, lineBase);
                                }
                                else if (ctl.IsInputField)
                                {
                                    // Centre the control box in its ROW — flush with
                                    // the cell borders for a full-height control.
                                    var ctlH = ctl.InputHeight > 0 ? ctl.InputHeight : 15.75;
                                    var ctlAbove = ctl.IsSelectBox
                                        ? SelectBoxAboveBaselinePt : InputBoxAboveBaselinePt;
                                    EmitControlAt(ctl, ix, rowTop - (rh - ctlH) / 2 - ctlAbove);
                                }
                                else
                                {
                                    var bcw = ctl.ButtonCaption.Length > 0
                                        ? MeasureStd14("Helvetica", ctl.ButtonCaption, 10) + ButtonChromeWPt : EmptyButtonWPt;
                                    var bch = ctl.ButtonCaption.Length > 0 ? ButtonHeightPt : EmptyButtonHPt;
                                    // A cell button CENTRES vertically in its row and
                                    // OVERHANGS horizontally: its
                                    // left edge sits half a point outside the cell box,
                                    // so its outline rides the cell's own border.
                                    var bcy = rowTop - (rh - bch) / 2 - bch;
                                    var bcx = ix - GridCellPad - 0.5;
                                    DrawBox(page, bcx, bcy, bcw, bch,
                                        border: Color.Black, borderWidth: 1, fill: null);
                                    if (bcw > 4 && bch > 3)
                                        DrawBox(page, bcx + 2, bcy + 1.5, bcw - 4, bch - 3,
                                            null, 0, dialectButtonFill);
                                    if (ctl.ButtonCaption.Length > 0)
                                        page.AddContentStream(Encoding.ASCII.GetBytes(FormattableString.Invariant(
                                            $"q BT /F1 10 Tf {dialectButtonTextRg} 1 0 0 1 {bcx + ButtonCaptionInsetXPt:0.##} {bcy + bch - ButtonCaptionDropPt:0.##} Tm ({EscapePdfString(ctl.ButtonCaption)}) Tj ET Q\n")));
                                }
                            }
                            lineBase -= lineH;
                        }
                        cx += cw + GridCellGap;
                    }
                    rowTop -= rh + GridCellGap;
                }
                contentPage = page;
                // Back into baseline space: the next text baseline sits one ascent
                // below the grid's bottom edge (plus its own margins).
                y = gridTop - tableH - 0.9 * 12;
                lastWasHardBreak = false;
                prevFlowMarginBottom = 0;
                prevFlowLineHeight = 0;
                continue;
            }

            // Fieldset frame markers: the open records the frame's top at the
            // cursor (a following legend re-pins it under its baseline); the
            // close pads the box bottom and strokes the gray frame.
            if (block.FsBox == 1)
            {
                fsStack.Push((page, y));
                fsIndentLive += FsPadLeftPt;
                lastWasHardBreak = false;
                continue;
            }
            if (block.FsBox == -1)
            {
                fsIndentLive = Math.Max(0, fsIndentLive - FsPadLeftPt);
                if (fsStack.Count > 0)
                {
                    var (fsPage, fsTopY) = fsStack.Pop();
                    y -= FsBoxBottomPadPt;
                    if (ReferenceEquals(fsPage, page) && fsBoxW > 0)
                    {
                        var fsX = marginLeft + 1.5;
                        fsPage.AddContentStream(Encoding.ASCII.GetBytes(string.Create(
                            System.Globalization.CultureInfo.InvariantCulture,
                            $"q {FsFrameGray:0.###} {FsFrameGray:0.###} {FsFrameGray:0.###} RG 0.75 w " +
                            $"{fsX:F2} {y:F2} {fsBoxW:F2} {fsTopY - y:F2} re S Q\n")));
                        contentPage = page;
                    }
                }
                lastWasHardBreak = false;
                continue;
            }
            // A legend re-pins its frame's top: the border runs under the
            // legend's baseline (probed: baseline drop + 4.86 below the line top).
            if (block.FsLegend && fsStack.Count > 0 && !string.IsNullOrEmpty(block.Text))
            {
                var fsTop = fsStack.Pop();
                fsStack.Push((fsTop.page, y - FsLegendFrameAdjPt));
            }

            if (block.IsTable)
            {
                // Metric flow: tables render through the metric layouter (real HTML
                // geometry + win-metric line boxes), not the generator table. A
                // RADIO-bearing table is the exception: the metric layouter neither
                // grids nested tables nor draws form controls, so it takes the
                // generator grid below (whose lift + slice pass carry both).
                // Quirks-mode CSS-run documents (no <!DOCTYPE>) take the same
                // layouter: the body rule's pixel font does not inherit into table
                // cells there — cells render at the UA 16px base in the body face
                // (measured on the reference: Calibri cells at 12 pt, 18 pt row pitch).
                var quirksRunTable = !metricFlow && quirksCssRun;
                var tableFace = metricFlow ? metricFace : quirksRunTable ? bodyCssFace : null;
                // The inline-body-margin dialect draws its tables as a collapsed
                // 1px grid with mid-row pagination — a shape the metric layouter
                // does not model (it paginates row-at-a-time and knows no
                // border-collapse).
                if (bodyBoxGridDoc && WinMetricsFor(metricFace) is { } bgm)
                {
                    RenderBodyBoxGridTable(doc, ref page, ref y, block.TableHtml ?? "",
                        marginLeft, contentWidth, pageWidth, pageHeight, marginBottom,
                        metricFace, bgm, metricLineSum, docFontDict);
                    lastWasHardBreak = false;
                    continue;
                }
                if (tableFace is not null && WinMetricsFor(tableFace) is { } tfm
                    && !RadioGridableControls(block.TableHtml ?? ""))
                {
                    // Measured on the width:100%-body sheet only — the plain-
                    // body serif docs are calibrated without this gap.
                    if (uaStdSerif && bodyWidthFullDoc && tableAfterText)
                        y -= TableAfterTextGapPt;
                    RenderMetricTable(doc, ref page, ref y, block.TableHtml ?? "", css,
                        marginLeft + fsIndentLive,
                        // inside a fieldset the FRAME's content box is the
                        // table's available width, not the page content box
                        fsIndentLive > 0 && fsBoxW > 0
                            ? fsBoxW - FsPadLeftPt - FsPadRightPt
                            : contentWidth - fsIndentLive,
                        pageWidth, pageHeight,
                        marginTop, marginBottom, tableFace, tfm, docFontDict,
                        stdSerif: uaStdSerif,
                        baseFontSize: printGrid ? printGridBase
                            : ptReportDoc && ptTableFontPt > 0 ? ptTableFontPt
                            : uaStdSerif || quirksRunTable ? 12 : 11,
                        // The wrapper-stack recursion serves the legacy nested-
                        // markup corpus; the dead-css greens were calibrated on
                        // the flat merge and keep it. A zero body margin has no
                        // symmetric body inset for the grid either.
                        wrapperStacks: (uaStdSerif && !deadExternalCss) || ptReportDoc,
                        symInsetPt: bodyZeroMargin ? 0.0 : UaBodyMarginPt,
                        rtl: rtlDoc,
                        // the SSRS report export drives the serif flow's cells
                        // through the paragraph-segment model too
                        paragraphCells: emailNewsletterDoc || ssrsReportDoc,
                        serifReportCells: ssrsReportDoc);
                    // A PAGE-BREAK-AFTER div closed with this table (the close tag
                    // parses in a later segment): open the fresh page here.
                    if (block.PageBreakAfterTable)
                    {
                        page = doc.Pages.Add(pageWidth, pageHeight);
                        EnsureFonts(page, docFontDict);
                        y = pageHeight - marginTop;
                        pendingTopDrop = hasZeroTopMargin;
                        contentPage = null;
                    }
                    lastWasHardBreak = false;
                    continue;
                }
                // The band dialect's table conventions (nbsp spacer rows keep their line
                // boxes, CSS row pitches, zero empty rows) hold for a filing document's
                // top-level tables too — the ToC listing and the shaded proposals grid
                // page on the same conventions.
                // A synthesized form-horizontal row: the control-group's CSS rhythm —
                // the value span's 5px margin-top, the controls div's 1px padding-top
                // and the collapsed 3px inter-group margins — separates it from the
                // block above (9px per row is the row pitch).
                var fhTableHtml = block.TableHtml ?? "";
                var fhRow = formHorizontalDoc
                    && fhTableHtml.Contains("class=\"fh-row\"", StringComparison.OrdinalIgnoreCase);
                if (fhRow) y -= 9 * 0.75;
                // A metric-flow doc's radio table grids through the generator (see the
                // metric bypass above); its cells keep the metric dialect's base font
                // and pitch on the browser line box the items lay out on.
                var radioGridTable = metricFlow && RadioGridableControls(fhTableHtml);
                var radioGridFontPt = uaStdSerif ? 12.0 : 11.0;
                // The radio factory for this grid: one RadioButtonField per HTML
                // `name` (an anonymous group per unnamed input), anchored on the page
                // the table starts on. Options join their group at creation so the
                // render pass can place each widget via OwnerRadio; the groups are
                // registered on doc.Form after the flow pass.
                var tablePage = page;
                Aspose.Pdf.Forms.RadioButtonOptionField MakeGridRadio(string group, bool chk)
                {
                    var key = string.IsNullOrEmpty(group) ? "__gridradio" + gridRadioAnon++ : group;
                    if (!gridRadioGroups.TryGetValue(key, out var rbf))
                    {
                        rbf = new Aspose.Pdf.Forms.RadioButtonField(tablePage);
                        gridRadioGroups[key] = rbf;
                        gridRadioPages.Add((rbf, tablePage));
                    }
                    gridRadioCounts.TryGetValue(key, out var optIdx);
                    gridRadioCounts[key] = optIdx + 1;
                    var ropt = new Aspose.Pdf.Forms.RadioButtonOptionField
                    {
                        Style = Aspose.Pdf.Forms.BoxStyle.Circle,
                        OptionName = key + "_" + optIdx,
                    };
                    ropt.Characteristics.Border = System.Drawing.Color.Black;
                    rbf.Add(ropt);
                    return ropt;
                }
                var table = BuildTableFromHtml(fhTableHtml, contentWidth, out _, options, inlineSvgs, css,
                    bandDialect: floatBandDoc,
                    makeRadio: MakeGridRadio,
                    // Sectioned-report rhythm: cell lines pitch on the browser's own
                    // line box too, not the flow's legacy em multiple.
                    cellLineHeightPt: radioGridTable ? Table.CssLineBoxPt(radioGridFontPt)
                        : sectionedReport && formBodyFontPt > 0
                        ? NormalLineHeightPt(formBodyFontPt)
                        // a scaled layout paces cells on the UA 18px line
                        : scaleToPageWidth ? NormalLineHeightPt(DefaultBodyFontPt)
                        : bodyLineHeightPt,
                    // The page stylesheet's own base size seeds the grid (see bodyCssFontPt);
                    // the probe above must measure the same cells this render builds.
                    defaultCellFontPt: radioGridTable ? radioGridFontPt
                        : scaleToPageWidth ? DefaultBodyFontPt : bodyCssFontPt,
                    cssRunFace: bodyCssFace, bodyTextColor: bodyCssColor,
                    // Sectioned reports lay their grids out on the browser's own cell
                    // box: the UA's 1px vertical cell padding and pre-wrap line boxes.
                    uaCellBoxes: sectionedReport,
                    // Nested tables render as real grids, and the chain-selector dialect
                    // that rides the same switch is on: a stylesheet's descendant rules
                    // reach the cells they address instead of being dropped.
                    liftNestedTables: true,
                    chainRules: docChainRules);
                if (table is not null)
                {
                    table.FlowLeftOffset = marginLeft;
                    // Inside a float column the usable width IS the column width —
                    // the symmetric-margin guess in GetTableUsableWidth reads a
                    // right column's offset as a right margin and collapses it.
                    if (bandStack.Count > 0) table.UsableWidthOverride = contentWidth;
                    // A form-horizontal row keeps its natural cell widths even when
                    // they overflow the float column (browser floats overflow; the
                    // squeeze would re-wrap value text that belongs on
                    // one line).
                    if (fhRow)
                    {
                        var fhw = Regex.Match(fhTableHtml, @"data-fhw=""([\d.]+)""");
                        if (fhw.Success && double.TryParse(fhw.Groups[1].Value,
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var fhwPx)
                            && fhwPx * 0.75 > contentWidth)
                            table.UsableWidthOverride = fhwPx * 0.75;
                    }
                    // Band-card tables render serif cell fragments in the real serif
                    // face (see Table.HonorCellFontFaces) — the Helvetica fallback
                    // over-wraps their serif-measured columns.
                    table.HonorCellFontFaces = floatBandDoc;
                    // Form-document dialect: cells wrap and draw in their resolved
                    // real faces (td { font: 10px Verdana }) — see HonorCellTtfFaces.
                    table.HonorCellTtfFaces = formDialectTables;
                    // Sectioned report: the cursor runs in baseline space, so the table's
                    // own box top — the top edge of its first row band — sits one baseline
                    // offset above it. Without this the whole grid hangs a full ascent too
                    // low and every row band misses the one a browser paints.
                    if (sectionedReport && prevFlowFontSize > 0)
                        y += BaselineInLineBoxPt(prevFlowFontSize);
                    // Paginate the table from the current cursor; the first slice lands on this
                    // page, further slices spill onto fresh pages (matching a browser splitting a
                    // long table across pages). Borders/graphics come back via LastGraphDraws.
                    // A table that spills onto a fresh page resumes at the page's TOP
                    // MARGIN, not at the sheet edge: without the page margin the
                    // continuation draws right off the top of the paper.
                    var slices = table.BuildMultiPage(page, y, marginBottom,
                        bodyCssFace is not null ? marginTop
                        // Escaped-attr dialect: continuation slices resume at the
                        // page's real top margin (the flow's fresh-page top), not at
                        // the sheet edge.
                        : escapedAttrDoc ? pageHeight - FreshPageTopY()
                        // Chain-dialect documents likewise resume below the top
                        // margin — a spilled report row must not draw at the sheet
                        // edge (the y≈9 artefact).
                        : docChainRules is not null ? marginTop
                        : 0);
                    var graphs = table.LastGraphDraws;
                    var imageDraws = table.LastImageDraws;
                    // A float-band column is an overflow:hidden box: a table reaching the
                    // page bottom CLIPS there instead of paginating (a browser never splits
                    // a float box), so the band's other columns stay on the band's page.
                    var bandClipped = false;
                    if (bandStack.Count > 0 && slices.Count > 1)
                    {
                        bandClipped = true;
                        slices = new List<byte[]> { slices[0] };
                        if (graphs.Count > 1) graphs = new List<List<byte[]>> { graphs[0] };
                        if (imageDraws.Count > 1)
                            imageDraws = new List<List<(byte[] data, Rectangle rect)>> { imageDraws[0] };
                    }
                    for (var si = 0; si < slices.Count; si++)
                    {
                        if (si > 0)
                        {
                            page = doc.Pages.Add(pageWidth, pageHeight);
                            EnsureFonts(page, docFontDict);
                        }
                        // A floated table's ops are collected and PREPENDED to its page's
                        // content after the flow pass — floats paint first,
                        // so their text leads the fragment order. Geometry is unchanged.
                        if (block.FloatFirst)
                        {
                            if (si < graphs.Count)
                                foreach (var g in graphs[si]) floatFirstOps.Add((page, g));
                            floatFirstOps.Add((page, slices[si]));
                        }
                        else
                        {
                            if (si < graphs.Count)
                                foreach (var g in graphs[si]) page.AddContentStream(g);
                            page.AddContentStream(slices[si]);
                        }
                        // Cell images (logos, SVG diagrams) recorded by the layout pass;
                        // blit them onto the slice's page at their resolved rectangles.
                        if (si < imageDraws.Count)
                            foreach (var (imgData, imgRect) in imageDraws[si])
                                try { page.AddImage(imgData, imgRect); }
                                catch { /* undecodable image: keep the table flow */ }
                    }
                    // A clipped band column consumed its page down to the bottom margin —
                    // LastRenderedHeight/LastPageEndY describe the discarded overflow pages.
                    y = bandClipped ? marginBottom
                        : slices.Count > 1 ? table.LastPageEndY : y - table.LastRenderedHeight;
                    // Back into baseline space: the cursor sits on the table's bottom
                    // EDGE, and the next text block draws its baseline one offset below
                    // its own box top — the mirror of the entry adjustment above.
                    if (sectionedReport && prevFlowFontSize > 0)
                        y -= BaselineInLineBoxPt(prevFlowFontSize);
                    contentPage = page;
                    // The cursor now sits ON the table's bottom edge. Text draws its
                    // first BASELINE at the cursor, so in the form-document dialect
                    // the next text block drops a line box first — else its ink rides
                    // up into the last (bordered) row. The legacy flow keeps its
                    // calibrated tight rhythm outside the dialect.
                    // The chain dialect owes the same drop: its report footnote drew its
                    // baseline ON the table's bottom border, striking through the last
                    // row — and never reached the due page break.
                    pendingTableDrop = formDialectTables || docChainRules is not null;
                }
                lastWasHardBreak = false;
                prevFlowMarginBottom = 0;
                prevFlowLineHeight = 0;
                afterRuleDrop = false;
                afterFhTable = fhRow;
                continue;
            }

            // <input>: place an interactive AcroForm TextBoxField at the cursor.
            // The test only inspects the field (type/Multiline), not its pixels, but
            // we size and position it from the CSS so the widget lands where the input
            // sits in the flow.
            if (block.IsCheckbox)
            {
                // Emit an AcroForm checkbox at the flow cursor (a small fixed box; the
                // HTML→PDF tests inspect the field, not its pixel position).
                const double boxSize = 10.0;
                if (y - boxSize < marginBottom)
                {
                    page = doc.Pages.Add(pageWidth, pageHeight);
                    EnsureFonts(page, docFontDict);
                    y = pageHeight - marginTop; pendingTopDrop = hasZeroTopMargin;
                }
                var cbx = marginLeft + block.LeftIndent;
                var checkbox = new Forms.CheckboxField(page, new Rectangle(cbx, y - boxSize, cbx + boxSize, y))
                {
                    Checked = block.Checked,
                };
                doc.Form.Add(checkbox, page.Number);
                y -= boxSize + 2;
                lastWasHardBreak = false;
                continue;
            }

            if (block.IsRadio)
            {
                const double boxSize = 10.0;
                if (y - boxSize < marginBottom)
                {
                    page = doc.Pages.Add(pageWidth, pageHeight);
                    EnsureFonts(page, docFontDict);
                    y = pageHeight - marginTop; pendingTopDrop = hasZeroTopMargin;
                }
                var rbx = marginLeft + block.LeftIndent;
                radioOptions.Add((block.RadioGroup, block.Checked, page,
                    new Rectangle(rbx, y - boxSize, rbx + boxSize, y)));
                y -= boxSize + 2;
                lastWasHardBreak = false;
                continue;
            }

            if (block.IsImage)
            {
                // Form dialect: a block image sits at the preceding text's CSS box
                // bottom plus that block's bottom margin/padding — rewind the legacy
                // full-line-box advance (same correction the <hr> branch makes).
                if (formHorizontalDoc && prevFlowLineHeight > 0)
                {
                    y += prevFlowLineHeight - prevFlowFontSize * 0.3;
                    prevFlowLineHeight = 0;
                }
                // Vector sources (an inline-<svg> placeholder or an SVG file behind <img src>)
                // rasterize through the SVG engine; their natural size is the SVG viewport in
                // CSS pixels (× 0.75 → pt), not the raster's pixel count.
                byte[]? bytes;
                double svgNatW = 0, svgNatH = 0;
                if (block.ImageSrc is { } bsrc && bsrc.StartsWith("inline-svg:", StringComparison.Ordinal)
                    && int.TryParse(bsrc["inline-svg:".Length..], out var svgIdx)
                    && svgIdx >= 0 && svgIdx < inlineSvgs.Count)
                {
                    var svgSrc = inlineSvgs[svgIdx];
                    // A root svg with no absolute width attribute (width:100%
                    // style or nothing) fills its containing block — the raster
                    // viewport is the content box in CSS px, so the artwork
                    // keeps its 0.75 pt/px scale unclipped (measured:
                    // ink to 850 px drawn from margin+6, never squeezed).
                    var svgHeadM = Regex.Match(Encoding.UTF8.GetString(svgSrc), @"<svg\b[^>]*>");
                    if (svgHeadM.Success
                        && !Regex.IsMatch(svgHeadM.Value, @"\bwidth\s*=\s*[""']?\d"))
                    {
                        var vpWpx = Math.Max(100.0, contentWidth - 2 * UaBodyMarginPt) / 0.75;
                        // height:100% of an AUTO parent is auto — the replaced
                        // element falls back to the CSS default 150 px, and the
                        // svg CLIPS at it (measured: the list box cuts
                        // at svg y=150, only row 1 and the ascenders of row 2
                        // survive). An absolute height attribute stands.
                        var svgHAttr = Regex.Match(svgHeadM.Value,
                            @"\bheight\s*=\s*[""']?([\d.]+)");
                        var vpHpx = svgHAttr.Success && double.TryParse(
                                svgHAttr.Groups[1].Value,
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out var svgHv) && svgHv > 0
                            ? svgHv : 150.0;
                        var svgFull = Encoding.UTF8.GetString(svgSrc);
                        var vpTag = svgHeadM.Value.Insert("<svg".Length,
                            string.Create(System.Globalization.CultureInfo.InvariantCulture,
                                $" width=\"{vpWpx:0.##}\" height=\"{vpHpx:0.##}\""));
                        svgSrc = Encoding.UTF8.GetBytes(svgFull
                            .Remove(svgHeadM.Index, svgHeadM.Length)
                            .Insert(svgHeadM.Index, vpTag));
                    }
                    bytes = ImageRasterizer.RasterizeSvg(svgSrc, out var vw, out var vh);
                    svgNatW = vw * 0.75; svgNatH = vh * 0.75;
                }
                else
                {
                    bytes = LoadConverterImage(block.ImageSrc, options);
                    if (IsSvgBytes(bytes))
                    {
                        bytes = ImageRasterizer.RasterizeSvg(bytes!, out var vw, out var vh);
                        svgNatW = vw * 0.75; svgNatH = vh * 0.75;
                    }
                }
                if (bytes is not null)
                {
                    double natW = 0, natH = 0;
                    if (svgNatW > 0 && svgNatH > 0) { natW = svgNatW; natH = svgNatH; }
                    else
                    {
                        TryReadImagePixelSize(bytes, out var pxW, out var pxH);
                        if (pxW > 0 && pxH > 0) { natW = pxW * 0.75; natH = pxH * 0.75; }
                    }
                    var w = block.ImageWidth > 0 ? block.ImageWidth * 0.75 : 0;
                    var h = block.ImageHeight > 0 ? block.ImageHeight * 0.75 : 0;
                    if (w <= 0 && h <= 0) { w = natW > 0 ? natW : 72; h = natH > 0 ? natH : 72; }
                    else if (h <= 0) h = (natW > 0 && natH > 0) ? w * natH / natW : w;
                    else if (w <= 0) w = (natW > 0 && natH > 0) ? h * natW / natH : h;
                    // Absolutely positioned image: seats at margins + left/top
                    // (CSS px → pt; reference: x = 90 + left·0.75, top = 72 + top·0.75)
                    // and leaves the flow — no width clamp, no cursor advance.
                    if (uaStdSerif && block.ImageAbsPos)
                    {
                        var apX = marginLeft + block.ImageAbsLeftPx * 0.75;
                        var apTop = pageHeight - marginTop - block.ImageAbsTopPx * 0.75;
                        try
                        {
                            page.AddImage(bytes, new Rectangle(apX, apTop - h, apX + w, apTop));
                        }
                        catch { /* undecodable image: skip, keep the flow going */ }
                        contentPage = page;
                        lastWasHardBreak = false;
                        continue;
                    }
                    var availW = contentWidth;
                    // an inline %-max-width caps the drawn box at its share of the
                    // content width (aspect kept)
                    if (block.ImageMaxWFrac > 0 && availW > 0 && w > availW * block.ImageMaxWFrac)
                    {
                        h *= availW * block.ImageMaxWFrac / w;
                        w = availW * block.ImageMaxWFrac;
                    }
                    var rtlOverflow = rtlDoc && availW > 0 && w > availW;
                    // Chart-card: the widened page fits the chart at NATURAL size; its
                    // indented right edge may pass the content box by the container
                    // chrome (the reference draws it there, unclipped) — never downscale.
                    if (availW > 0 && w > availW && !rtlOverflow && !chartCardDoc)
                    { h *= availW / w; w = availW; }
                    var padTop = block.ImagePadTopPx * 0.75;
                    var padBottom = block.ImagePadBottomPx * 0.75;
                    // Word-filtered pages: an over-tall image draws AT the flow
                    // position and CROSSES the page boundary — each continuation
                    // page redraws it shifted up by one content band (measured:
                    // the snip capture runs 290..1076 across two sheets).
                    if (msoFilteredDoc && y - h - padTop - padBottom < marginBottom)
                    {
                        y -= padTop;
                        var crossX = block.ImageCentered
                            ? marginLeft + (contentWidth - w) / 2
                            : marginLeft + block.ImageIndentPt;
                        var crossBand = pageHeight - marginTop - marginBottom;
                        var crossInv = System.Globalization.CultureInfo.InvariantCulture;
                        // The reference clips each sheet's content at the margin
                        // band — the rows past the bottom margin appear only on
                        // the continuation page (which clips above its top
                        // margin in turn: no row repeats).
                        void CrossDraw(Page cp, double topY, bool clipTop)
                        {
                            var clipLo = marginBottom;
                            var clipHi = clipTop ? pageHeight - marginTop : pageHeight;
                            cp.AddContentStream(Encoding.ASCII.GetBytes(string.Create(crossInv,
                                $"q 0 {clipLo:0.##} {pageWidth:0.##} {clipHi - clipLo:0.##} re W n\n")));
                            try { cp.AddImage(bytes, new Rectangle(crossX, topY - h, crossX + w, topY)); }
                            catch { /* undecodable image: keep the flow */ }
                            cp.AddContentStream(Encoding.ASCII.GetBytes("Q\n"));
                        }
                        CrossDraw(page, y, clipTop: false);
                        var crossTop = y;
                        while (crossBand > 0 && crossTop - h < marginBottom - 0.01)
                        {
                            page = doc.Pages.Add(pageWidth, pageHeight);
                            EnsureFonts(page, docFontDict);
                            crossTop += crossBand;
                            CrossDraw(page, crossTop, clipTop: true);
                        }
                        y = crossTop - h - padBottom;
                        contentPage = page;
                        lastWasHardBreak = false;
                        continue;
                    }
                    if (y - h - padTop - padBottom < marginBottom)
                    {
                        // Inside a float column the overflow is clipped, not paginated.
                        if (floatBandDoc && bandStack.Count > 0)
                        {
                            bandColClipped = true;
                            lastWasHardBreak = false;
                            continue;
                        }
                        page = doc.Pages.Add(pageWidth, pageHeight);
                        EnsureFonts(page, docFontDict);
                        y = pageHeight - marginTop; pendingTopDrop = hasZeroTopMargin;
                    }
                    y -= padTop;
                    var imgX = rtlOverflow ? marginLeft + contentWidth - w
                        : block.ImageCentered ? marginLeft + (contentWidth - w) / 2
                        : marginLeft + block.ImageIndentPt;
                    try
                    {
                        if (block.ImageRotateDeg != 0)
                        {
                            // CSS transform: rotate(θ) spins the image about its layout
                            // box centre and leaves the layout box (and the flow advance)
                            // unrotated. CSS angles are clockwise on the page; the stamp
                            // matrix is PDF counter-clockwise, hence the sign flip. The
                            // stamp anchors at the ROTATED bounding box's bottom-left.
                            var rad = block.ImageRotateDeg * Math.PI / 180.0;
                            var bw = Math.Abs(w * Math.Cos(rad)) + Math.Abs(h * Math.Sin(rad));
                            var bh = Math.Abs(w * Math.Sin(rad)) + Math.Abs(h * Math.Cos(rad));
                            var cx = imgX + w / 2;
                            var cy = y - h / 2;
                            var stamp = ImageStamp.FromEncodedBytes(bytes);
                            stamp.XIndent = cx - bw / 2;
                            stamp.YIndent = cy - bh / 2;
                            stamp.DisplayWidth = w;
                            stamp.DisplayHeight = h;
                            stamp.RotateAngle = -block.ImageRotateDeg;
                            stamp.ApplyTo(page);
                        }
                        else
                            page.AddImage(bytes, new Rectangle(imgX, y - h, imgX + w, y));
                    }
                    catch { /* undecodable image: skip, keep the flow going */ }
                    // Chart-card: the widget CARD around the chart paints a soft grey
                    // box-shadow (2px offset, 2px blur — the only visible chrome, the
                    // card's fill and border being white). Approximate the bitmap
                    // the reference renders with the offset right/bottom bars plus a hairline
                    // ring. Card box recovered from the content position: its left chrome
                    // insets the image, its inner box is the col's content width, and it
                    // closes one chrome below the chart.
                    if (chartCardDoc && block.ImageCardShadow is { } cardShadow
                        && block.ImageCardChromePt > 0)
                    {
                        var chrome = block.ImageCardChromePt;
                        var cardL = marginLeft + block.ImageIndentPt - chrome;
                        var cardInnerW = contentWidth - block.ImageWidenPadPt;
                        var cardR = cardL + cardInnerW + 2 * chrome;
                        var cardTopPdf = pageHeight - marginTop;
                        var cardBottomPdf = y - h - chrome;
                        const double ShadowOffPt = 1.5;   // 2px offset
                        const double ShadowExtPt = 2.75;  // offset + blur extent, off the reference bitmap
                        var inv2 = System.Globalization.CultureInfo.InvariantCulture;
                        var sr = cardShadow.R / 255.0; var sg = cardShadow.G / 255.0; var sbv = cardShadow.B / 255.0;
                        var ops = string.Create(inv2,
                            $"q {sr:0.###} {sg:0.###} {sbv:0.###} rg " +
                            $"{cardR:0.##} {cardBottomPdf - ShadowExtPt:0.##} {ShadowExtPt:0.##} {cardTopPdf - ShadowOffPt - (cardBottomPdf - ShadowExtPt):0.##} re f " +
                            $"{cardL + ShadowOffPt:0.##} {cardBottomPdf - ShadowExtPt:0.##} {cardR - cardL - ShadowOffPt + ShadowExtPt:0.##} {ShadowExtPt:0.##} re f " +
                            $"{sr:0.###} {sg:0.###} {sbv:0.###} RG 0.5 w " +
                            $"{cardL:0.##} {cardBottomPdf:0.##} {cardR - cardL:0.##} {cardTopPdf - cardBottomPdf:0.##} re S Q\n");
                        page.AddContentStream(Encoding.ASCII.GetBytes(ops));
                    }
                    contentPage = page;
                    // A LEFT-FLOATED image leaves the flow: the cursor stays where it
                    // was and the block boxes below keep starting at the content top —
                    // only their LINES are shortened, on the right of the image, until
                    // the flow has passed its bottom edge.
                    if (floatImageDoc && block.FloatLeft)
                    {
                        floatBottomY = y - h - padBottom;
                        floatIndentPt = w + FloatGutterPt;
                        lastWasHardBreak = false;
                        continue;
                    }
                    y -= h + padBottom;
                    // Inline image in a band column: the image sits on a text line box,
                    // so the line's tail (descent + leading) separates it from the next
                    // paragraph — without it the following text's ascent rises to the
                    // image's bottom edge (the legacy baseline-at-cursor model).
                    if (floatBandDoc && bandStack.Count > 0) y -= 9;
                    // Form dialect: same baseline-at-cursor problem in the main flow —
                    // the following section heading's ascent plus the heading gap
                    // kept below a block image.
                    else if (formHorizontalDoc) y -= 25.5;
                }
                else if (msoFilteredDoc)
                {
                    // Word-filtered pages: an unloadable image leaves the browser's
                    // 32×32 placeholder while its paragraph keeps ONE empty UA line
                    // of flow. Both lead placeholders ride 14.4 under the top margin
                    // (measured); the banner in the absolutely positioned span seats
                    // at 90 + (left 0 + margin-left −96px)·0.75 + the 1 pt frame
                    // inset = 19, the inline one at the content edge.
                    var mphX = msoBrokenImgCount == 0 ? 90.0 - 72.0 + 1.0 : marginLeft + 0.75;
                    msoBrokenImgCount++;
                    var mphTop = pageHeight - 72.0 - MsoBrokenImgDropPt;
                    var mphName = RegisterPlaceholderIcon(doc, page, ref flowIconRef, masked: true);
                    page.AddContentStream(Encoding.ASCII.GetBytes(FormattableString.Invariant(
                        $"q 32 0 0 32 {mphX:0.##} {mphTop - 32:0.##} cm /{mphName} Do Q\n")));
                    var mphDark = ParseCssColor("#555555");
                    var mphLite = ParseCssColor("#AAAAAA");
                    DrawBox(page, mphX - 1, mphTop + 1, 34, 1, null, 0, mphDark);
                    DrawBox(page, mphX - 1, mphTop - 32, 34, 1, null, 0, mphLite);
                    DrawBox(page, mphX - 1, mphTop - 32, 1, 34, null, 0, mphDark);
                    DrawBox(page, mphX + 32, mphTop - 32, 1, 34, null, 0, mphLite);
                    contentPage = page;
                    // the paragraph's one empty UA text line (an image block
                    // carries no font size, so the per-block line height is 0 here)
                    y -= lineHeight > 1 ? lineHeight : PpLineBoxPt;
                }
                else if (escapedAttrDoc)
                {
                    // A broken image renders the browser's 32×32 placeholder icon at
                    // the content edge (the escaped float:/size styles can never
                    // apply). Measured: the icon's top rides 9.47 pt above the flow
                    // cursor — a heading's bottom margin does not span a replaced
                    // box — and a following grid's top border lands 1.38 pt under
                    // the icon (the cursor sits one 0.9em ascent below that edge).
                    var iconTop = y + 9.47;
                    if (iconTop - 32 < marginBottom)
                    {
                        page = doc.Pages.Add(pageWidth, pageHeight);
                        EnsureFonts(page, docFontDict);
                        y = FreshPageTopY(); pendingTopDrop = hasZeroTopMargin;
                        iconTop = y;
                    }
                    var phName = RegisterPlaceholderIcon(doc, page, ref flowIconRef, masked: true);
                    page.AddContentStream(Encoding.ASCII.GetBytes(FormattableString.Invariant(
                        $"q 32 0 0 32 {marginLeft + 1:0.##} {iconTop - 32:0.##} cm /{phName} Do Q\n")));
                    // The browser frames a broken image with a 1 pt INSET border —
                    // top/left #555, bottom/right #aaa — half a point outside the icon.
                    var phDark = ParseCssColor("#555555");
                    var phLite = ParseCssColor("#AAAAAA");
                    DrawBox(page, marginLeft, iconTop, 34, 1, null, 0, phDark);
                    DrawBox(page, marginLeft, iconTop - 33, 34, 1, null, 0, phLite);
                    DrawBox(page, marginLeft, iconTop - 33, 1, 34, null, 0, phDark);
                    DrawBox(page, marginLeft + 33, iconTop - 33, 1, 34, null, 0, phLite);
                    contentPage = page;
                    y = iconTop - 33 - 0.9 * 12;
                }
                lastWasHardBreak = false;
                prevFlowMarginBottom = 0;
                prevFlowLineHeight = 0;
                afterRuleDrop = false;
                afterFhTable = false;
                continue;
            }

            // A push-button in the flow: caption + 10.4 chrome wide, 18.75 tall
            // (11.5×7.5 when empty), its LEFT edge 2 pt outside the margin, filled
            // from the button{} tag rule. Measured: box top 12.3 above the cursor,
            // the next baseline 9.3 under the box.
            if (block.IsButton)
            {
                var capW = block.ButtonCaption.Length > 0
                    ? MeasureStd14("Helvetica", block.ButtonCaption, 10) + ButtonChromeWPt : EmptyButtonWPt;
                var bh = block.ButtonCaption.Length > 0 ? ButtonHeightPt : EmptyButtonHPt;
                var btnTop = y + 12.3;
                if (btnTop - bh < marginBottom)
                {
                    page = doc.Pages.Add(pageWidth, pageHeight);
                    EnsureFonts(page, docFontDict);
                    y = FreshPageTopY(); pendingTopDrop = hasZeroTopMargin;
                    btnTop = y;
                }
                var btnX = marginLeft - 2;
                // Thin outline, a 1.5–2 pt white gap, then the fill — the
                // button chrome (outer 60.42×18.75, inner fill 56.42×15.75).
                DrawBox(page, btnX, btnTop - bh, capW, bh,
                    border: Color.Black, borderWidth: 1, fill: null);
                if (capW > 4 && bh > 3)
                    DrawBox(page, btnX + 2, btnTop - bh + 1.5, capW - 4, bh - 3,
                        null, 0, dialectButtonFill);
                if (block.ButtonCaption.Length > 0)
                    page.AddContentStream(Encoding.ASCII.GetBytes(FormattableString.Invariant(
                        $"q BT /F1 10 Tf {dialectButtonTextRg} 1 0 0 1 {btnX + ButtonCaptionInsetXPt:0.##} {btnTop - ButtonCaptionDropPt:0.##} Tm ({EscapePdfString(block.ButtonCaption)}) Tj ET Q\n")));
                contentPage = page;
                y = btnTop - bh - 9.3;
                lastWasHardBreak = false;
                prevFlowMarginBottom = 0;
                prevFlowLineHeight = 0;
                continue;
            }

            if (block.IsInputField || block.InlineItems is not null)
            {
                // An inline run: label text and controls share wrapping line boxes
                // with a pen, so label|input|label|select rows stay inline.
                if (block.InlineItems is { Count: > 0 } runItems)
                {
                    // Directly under a section rule the run drops extra so its
                    // control boxes clear the rule (baseline rule+17.2, not +11.9).
                    if (afterEscapedRule) { y -= RuleToRunExtraPt; afterEscapedRule = false; }
                    var lineLeft = marginLeft;
                    var lineRight = marginLeft + contentWidth;
                    // Assemble the wrapped lines first. A control is an atomic word
                    // whose pen advance is its box width; text splits into tokens that
                    // carry their trailing spaces, measured in the serif face.
                    var runLines = new List<(List<(Block? Ctl, string? Txt, double X, double FontPt, string Res)> Items, bool HasText, double MaxAdv, double MaxAbove)>();
                    var curItems = new List<(Block? Ctl, string? Txt, double X, double FontPt, string Res)>();
                    var pen = lineLeft; var curHasText = false; double curMaxAdv = 0, curMaxAbove = 0;
                    void EndRunLine()
                    {
                        if (curItems.Count == 0) return;
                        runLines.Add((curItems, curHasText, curMaxAdv, curMaxAbove));
                        curItems = new List<(Block? Ctl, string? Txt, double X, double FontPt, string Res)>();
                        pen = lineLeft; curHasText = false; curMaxAdv = 0; curMaxAbove = 0;
                    }
                    foreach (var it in runItems)
                    {
                        if (it.IsInputField)
                        {
                            var cW = it.InputWidth > 0
                                ? System.Math.Min(it.InputWidth, contentWidth) : contentWidth;
                            var penW = cW + (it.IsSelectBox ? 2 * SelectSideBearingPt : 0);
                            if (curItems.Count > 0 && pen + penW > lineRight + 1e-6) EndRunLine();
                            // A tall control MID-LINE anchors its box BOTTOM at the
                            // baseline and grows UP: it advances the flow like a
                            // one-row control, but its line drops extra so the box
                            // top clears the content above.
                            var midLineTall = curItems.Count > 0 && it.InputMultiline;
                            curItems.Add((it, null, pen, 0, ""));
                            curMaxAdv = System.Math.Max(curMaxAdv, midLineTall
                                ? ControlFirstRowAdvancePt
                                : it.InputAdvance > 0 ? it.InputAdvance : ControlFirstRowAdvancePt);
                            if (midLineTall && it.InputHeight > 0)
                                curMaxAbove = System.Math.Max(curMaxAbove, it.InputHeight - TextareaBottomHangPt);
                            pen += penW;
                        }
                        else if (!string.IsNullOrEmpty(it.Text))
                        {
                            var fpt = it.FontSize > 0 ? it.FontSize : EscapedBodyFontPt;
                            var res = it.FontRes == "F2" ? "F6" : it.FontRes == "F3" ? "F7" : "F5";
                            var face = res == "F6" ? "Times-Bold"
                                : res == "F7" ? "Times-Italic" : "Times-Roman";
                            int p = 0;
                            while (p < it.Text.Length)
                            {
                                var sp = it.Text.IndexOf(' ', p);
                                var wordEnd = sp < 0 ? it.Text.Length : sp + 1;
                                while (wordEnd < it.Text.Length && it.Text[wordEnd] == ' ') wordEnd++;
                                var token = it.Text.Substring(p, wordEnd - p);
                                p = wordEnd;
                                var wTrim = MeasureStd14(face, token.TrimEnd(' '), fpt);
                                if (curItems.Count > 0 && pen + wTrim > lineRight + 1e-6) EndRunLine();
                                // The space a wrap breaks at vanishes at the fresh line's start.
                                var draw = curItems.Count == 0 ? token.TrimStart(' ') : token;
                                if (draw.Length == 0) continue;
                                curItems.Add((null, draw, pen, fpt, res));
                                curHasText = true;
                                pen += MeasureStd14(face, draw, fpt);
                            }
                        }
                    }
                    EndRunLine();
                    // A run stays TOGETHER over a page boundary: a
                    // question whose control box no longer fits takes its label lines
                    // with it to the fresh page (a run taller than a page still
                    // paginates line by line).
                    // The room a run needs on this page: every line's pre-drop and
                    // advance, except that a LAST line whose box is bottom-anchored
                    // only needs descent room under its baseline (such a line sits
                    // 2.7 pt above the margin).
                    double runTotalAdv = 0;
                    for (var rl = 0; rl < runLines.Count; rl++)
                    {
                        var (_, rlHasText, rlMaxAdv, rlMaxAbove) = runLines[rl];
                        var rlAdv = rlMaxAdv > 0
                            ? rlMaxAdv + (rlHasText ? InlineMixedExtraPt : 0)
                            : NormalLineHeightPt(blockFontSize > 0 ? blockFontSize : EscapedBodyFontPt);
                        runTotalAdv += System.Math.Max(0, rlMaxAbove - InputBoxAboveBaselinePt)
                            + (rl == runLines.Count - 1 && rlMaxAbove > InputBoxAboveBaselinePt
                                ? SerifDescentRoomPt : rlAdv);
                    }
                    if (y - runTotalAdv < marginBottom
                        && runTotalAdv <= FreshPageTopY() - marginBottom
                        && y < FreshPageTopY() - 1e-3)
                    {
                        page = doc.Pages.Add(pageWidth, pageHeight);
                        EnsureFonts(page, docFontDict);
                        y = FreshPageTopY(); pendingTopDrop = hasZeroTopMargin;
                    }
                    foreach (var (items, hasText, maxAdv, maxAbove) in runLines)
                    {
                        // A line with a mid-line TALL control drops extra first so
                        // the box top clears the content above it.
                        if (maxAbove > InputBoxAboveBaselinePt)
                            y -= maxAbove - InputBoxAboveBaselinePt;
                        // A control line advances by the control's flow cost; carrying
                        // body text beside it adds the descent clearance. Text-only
                        // (wrap remainder) lines keep the normal line box.
                        var adv = maxAdv > 0 ? maxAdv + (hasText ? InlineMixedExtraPt : 0)
                            : NormalLineHeightPt(blockFontSize > 0 ? blockFontSize : EscapedBodyFontPt);
                        if (y - (maxAbove > InputBoxAboveBaselinePt ? SerifDescentRoomPt : adv) < marginBottom)
                        {
                            page = doc.Pages.Add(pageWidth, pageHeight);
                            EnsureFonts(page, docFontDict);
                            y = FreshPageTopY(); pendingTopDrop = hasZeroTopMargin;
                        }
                        foreach (var (ctl, txt, x, fpt, res) in items)
                        {
                            if (ctl is not null)
                                EmitControlAt(ctl, x, y,
                                    aboveOverride: ctl.InputMultiline && x > lineLeft + 1e-6
                                        && ctl.InputHeight > 0
                                        ? ctl.InputHeight - TextareaBottomHangPt : null);
                            else if (!string.IsNullOrEmpty(txt))
                                EmitSerifRun(txt, res, fpt, x, y);
                        }
                        contentPage = page;
                        y -= adv;
                    }
                    lastWasHardBreak = false;
                    prevFlowMarginBottom = 0;
                    prevFlowLineHeight = 0;
                    continue;
                }

                if (y < pageHeight - marginTop - 1e-3)
                    y -= block.MarginTop;
                var fieldH = block.InputHeight > 0 ? block.InputHeight : lineHeight;
                var boxAbove = !block.InputDrawValue ? 0
                    : block.IsSelectBox ? SelectBoxAboveBaselinePt : InputBoxAboveBaselinePt;
                if (y + boxAbove - fieldH < marginBottom)
                {
                    page = doc.Pages.Add(pageWidth, pageHeight);
                    EnsureFonts(page, docFontDict);
                    y = FreshPageTopY(); pendingTopDrop = hasZeroTopMargin;
                }
                EmitControlAt(block, marginLeft + block.LeftIndent, y);
                y -= (block.InputAdvance > 0 ? block.InputAdvance : fieldH) + block.MarginBottom;
                lastWasHardBreak = false;
                continue;
            }

            // RTL diagram table: one right-pinned canvas — stretched figure, centered
            // caption, per-column right-aligned labels, and a legend row whose
            // viewBox-only svgs stretch to column width at a common row height.
            if (block.Diagram is { } dg)
            {
                const double PxPt = 0.75;
                var invd = System.Globalization.CultureInfo.InvariantCulture;
                var canvasW = dg.WidthPx * PxPt;
                var canvasRight = marginLeft + contentWidth;
                var canvasLeft = canvasRight - canvasW;
                var arialD = PosFace("Arial");
                var fontDictD = page.Dict.Get("Resources") is Core.PdfDictionary dres
                    ? dres.Get("Font") as Core.PdfDictionary : null;

                void DrawRtlText(string text, double rightX, double baseline, double fontPt,
                    bool centerCanvas = false)
                {
                    if (fontDictD is null || arialD.ttf is null || text.Length == 0) return;
                    var visual = IsPureRtl(text) ? ToVisualRtl(text)
                        : Text.BidiReorderer.ContainsRtl(text) ? VisualizeMixedRtl(text) : text;
                    var tw = MeasureFaceText("Arial", visual, fontPt);
                    var tx = centerCanvas ? (canvasLeft + canvasRight - tw) / 2 : rightX - tw;
                    var (rn, hex) = Text.Type0FontEmbedder.Embed(fontDictD, arialD.ttf, "Arial",
                        visual, stripSpacesInBaseFont: true);
                    var t = new StringBuilder();
                    t.Append("BT 0 0 0 rg ");
                    t.Append($"/{rn} {fontPt.ToString("F1", invd)} Tf ");
                    t.Append($"1 0 0 1 {tx.ToString("F2", invd)} {baseline.ToString("F2", invd)} Tm ");
                    t.Append('<').Append(System.Convert.ToHexString(hex)).Append("> Tj ET ");
                    page.AddContentStream(Encoding.ASCII.GetBytes(t.ToString()));
                }

                var titleRowH = (dg.TitleText is null ? 0 : 81.3) * PxPt;
                var figH = dg.MainSvgHPx * PxPt;
                var labelRowH = (dg.MidLabels.Count > 0 ? 66.7 : 0.0) * PxPt;
                var legendBoxH = dg.LegendWFrac[0] * canvasW; // widest legend svg's square
                var legendLabelH = 22 * PxPt;
                var totalH = titleRowH + figH + labelRowH + legendBoxH + legendLabelH;
                if (y - totalH < marginBottom && y < pageHeight - marginTop - 1e-3)
                {
                    page = doc.Pages.Add(pageWidth, pageHeight);
                    EnsureFonts(page, docFontDict);
                    y = pageHeight - marginTop; pendingTopDrop = hasZeroTopMargin;
                    fontDictD = page.Dict.Get("Resources") is Core.PdfDictionary dres2
                        ? dres2.Get("Font") as Core.PdfDictionary : null;
                }

                if (dg.TitleText is not null)
                    DrawRtlText(dg.TitleText, 0, y - 49.3 * PxPt, dg.TitleFontPx * PxPt, centerCanvas: true);
                y -= titleRowH;

                if (dg.MainSvgIdx >= 0 && dg.MainSvgIdx < inlineSvgs.Count)
                {
                    // The figure keeps its viewBox aspect at the styled height and
                    // centers in the canvas (letterboxed, not stretched).
                    var figBytes = ImageRasterizer.RasterizeSvg(inlineSvgs[dg.MainSvgIdx],
                        out var figNatW, out var figNatH);
                    if (figBytes is not null)
                    {
                        var drawW = figNatW > 0 && figNatH > 0 ? figH * figNatW / figNatH : dg.MainSvgWPx * PxPt;
                        var figX = canvasLeft + (canvasW - drawW) / 2 - 10.3 * PxPt;
                        try
                        {
                            page.AddImage(figBytes, new Rectangle(figX, y - figH, figX + drawW, y));
                        }
                        catch { }
                    }
                }
                y -= figH;

                if (dg.MidLabels.Count > 0)
                    foreach (var (text, col) in dg.MidLabels)
                    {
                        var k = Math.Min(col, dg.MidLabelRightFrac.Length - 1);
                        DrawRtlText(text, canvasLeft + dg.MidLabelRightFrac[k] * canvasW,
                            y - 24 * PxPt, dg.LabelFontPx * PxPt);
                    }
                y -= labelRowH;

                for (var k = 0; k < dg.Legend.Count; k++)
                {
                    var (svgIdx, label) = dg.Legend[k];
                    var boxLeft = canvasLeft + dg.LegendXFrac[k] * canvasW;
                    var boxW = dg.LegendWFrac[k] * canvasW;
                    if (svgIdx >= 0 && svgIdx < inlineSvgs.Count && boxLeft + boxW > 0)
                    {
                        var sw = ImageRasterizer.RasterizeSvg(inlineSvgs[svgIdx], out _, out _);
                        if (sw is not null)
                            try
                            {
                                page.AddImage(sw, new Rectangle(boxLeft, y - legendBoxH,
                                    boxLeft + boxW, y));
                            }
                            catch { }
                    }
                    if (label.Length > 0)
                        DrawRtlText(label, canvasLeft + dg.LegendLabelRightFrac[k] * canvasW,
                            y - legendBoxH - 16 * PxPt, dg.LabelFontPx * PxPt);
                }
                y -= legendBoxH + legendLabelH;

                lastWasHardBreak = false;
                continue;
            }

            // RTL topics table: the matrix figure paints as graphics (right-pinned);
            // the caption and topic items stack on the left in the serif face, each
            // item right-aligned on a common pen edge with its bullet marker (one
            // " •" run) just right of that edge — marker before item text, so the
            // absorber reads caption, bullet, item, bullet, item, … Layout rule:
            // cell content right edge Rc = contentRight − 410.25 (405 pt
            // CSS svg column + 5.25 pt UA table chrome), items flush-right at
            // R = Rc − 30 (UA ul inline-start padding), caption flush-right at Rc,
            // marker pen at R + 4.5. contentRight nominally comes from an
            // 842-wide A3 page box; this library's A3 is the exact 841.89, so the
            // anchors are carried as content-LEFT offsets (equal under the
            // mirrored 96 pt insets) to keep the numbers verbatim.
            // Flex-row waybill grid: the bordered container, its centred serif
            // title, and every flex row's percent-width cells draw at absolute
            // geometry (all measures from the waybill reference render).
            if (block.Flex is { } fg)
            {
                var invF = System.Globalization.CultureInfo.InvariantCulture;
                var fSerifB = PosFace("Times New Roman Bold");
                var fontDictF = page.Dict.Get("Resources") is Core.PdfDictionary fres
                    ? fres.Get("Font") as Core.PdfDictionary : null;
                double FWidth(string s, double pt)
                    => MeasureFaceText("Times New Roman Bold", s, pt);
                void FDraw(string s, double x, double glyphTopDown, double pt)
                {
                    if (fontDictF is null || fSerifB.ttf is null || s.Length == 0) return;
                    var baseline = pageHeight - (glyphTopDown + SerifAscEm * pt);
                    var (rn, hex) = Text.Type0FontEmbedder.Embed(fontDictF, fSerifB.ttf,
                        "Times New Roman Bold", s, stripSpacesInBaseFont: true);
                    page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invF,
                        $"BT 0 0 0 rg /{rn} {pt:F1} Tf 1 0 0 1 {x:F2} {baseline:F2} Tm <{System.Convert.ToHexString(hex)}> Tj ET\n")));
                }
                void FLine(double x0, double y0d, double x1, double y1d)
                    => page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invF,
                        $"q 0 0 0 RG 0.75 w {x0:F2} {pageHeight - y0d:F2} m {x1:F2} {pageHeight - y1d:F2} l S Q\n")));

                var contL = marginLeft + CardBodyPadPt;
                // With a physical-width page wrapper the container spans exactly
                // that width; otherwise it fills to the body inset on the right.
                var contR = fg.PageContentPt > 0
                    ? contL + fg.PageContentPt
                    : pageWidth - marginRight - CardBodyPadPt;
                var contT = marginTop + CardBodyPadPt;
                var contW = contR - contL;
                // wrapper: the div flavour's 98%-wide 1%-padded inner div; the
                // table flavour's <table width=100%> inset by the UA 2px
                // border-spacing instead.
                var wrapL = fg.TableFlavor
                    ? contL + 2.25
                    : contL + contW * 0.01 + FlexRowBorderPt;
                var wrapW = fg.TableFlavor
                    ? contW - 4.5
                    : contW * 0.98 - 2 * FlexRowBorderPt;
                if (fg.Title.Length > 0)
                    FDraw(fg.Title, wrapL + (wrapW - FWidth(fg.Title, FlexTitleFontPt)) / 2,
                        contT + (fg.TableFlavor ? 1.34 : 0.96), FlexTitleFontPt);
                // first row top: the table flavour's h1 band runs 3.4pt deeper
                // (the table's own border-spacing above its first row).
                var fy = contT + FlexTitleBandPt + (fg.TableFlavor ? 3.4 : 0.0);
                const double CellFontPt = 9.0;      // the columns' 12px class size
                // the UA border-spacing between table rows (2px).
                var rowGap = fg.TableFlavor ? 1.5 : 0.0;
                var labelDy = fg.TableFlavor ? 1.39 : 0.64;
                var valueInset = fg.TableFlavor ? 2.62 : FlexValueInsetPt;
                foreach (var frow in fg.Rows)
                {
                    // Row height: the tallest cell's line bands + the border share.
                    double rowBands = 0;
                    var wraps = new List<string[]?>();
                    double cx0 = wrapL;
                    foreach (var fc in frow.Cells)
                    {
                        var cw = fc.WFrac * wrapW;
                        double bands;
                        string[]? wl = null;
                        if (fc.PlainWrap)
                        {
                            var availF = cw - fc.PadFrac * wrapW - 4;
                            wl = MeasuredWordWrap(fc.Label, Math.Max(20, availF),
                                "Times New Roman Bold", CellFontPt);
                            // Table flavour: only a CENTRED wrapping cell grows its
                            // row — a left-aligned prose cell OVERFLOWS it (both
                            // measured on the table-flavoured waybill: the wrapped
                            // header row is two bands tall, the certify row one).
                            bands = (fg.TableFlavor && !fc.Center ? 1 : wl.Length)
                                    * FlexLineBandPt;
                        }
                        else if (fc.ValueWide)
                            bands = 2 * FlexLineBandPt + 2 * fc.ValuePadPx * 0.75;
                        else
                            // An EMPTY dd collapses its line box; a filled one keeps it.
                            bands = (fc.HasDd && fc.Value.Trim().Length > 0 ? 2 : 1)
                                    * FlexLineBandPt;
                        rowBands = Math.Max(rowBands, bands);
                        wraps.Add(wl);
                        cx0 += cw;
                    }
                    var rowH = rowBands + FlexRowBorderPt;
                    var rowBottom = fy + rowH;
                    var nextRowTop = rowBottom + rowGap;
                    // Draw the cells.
                    var cx = wrapL;
                    for (var ci = 0; ci < frow.Cells.Count; ci++)
                    {
                        var fc = frow.Cells[ci];
                        var cw = fc.WFrac * wrapW;
                        var cellR = cx + cw;
                        if (fc.BL) FLine(cx + 0.38, fy - 0.38, cx + 0.38, rowBottom + 0.38);
                        if (fc.BR) FLine(cellR - 0.38, fy - 0.38, cellR - 0.38, rowBottom + 0.38);
                        if (fc.BT) FLine(cx, fy - 0.38, cellR, fy - 0.38);
                        if (fc.BB) FLine(cx, rowBottom - 0.38, cellR, rowBottom - 0.38);
                        var textX = cx + fc.PadFrac * wrapW + FlexRowBorderPt;
                        if (fc.PlainWrap)
                        {
                            var wl = wraps[ci] ?? Array.Empty<string>();
                            for (var li = 0; li < wl.Length; li++)
                            {
                                var lx = fc.Center
                                    ? cx + (cw - FWidth(wl[li], CellFontPt)) / 2
                                    : textX;
                                FDraw(wl[li], lx, fy + labelDy + li * FlexLineBandPt, CellFontPt);
                            }
                        }
                        else if (fc.ValueWide)
                        {
                            FDraw(fc.Label, textX, fy + labelDy, CellFontPt);
                            if (fc.LabelRight.Length > 0)
                                FDraw(fc.LabelRight,
                                    cellR - fc.LabelRightMrFrac * cw
                                          - FWidth(fc.LabelRight, CellFontPt),
                                    fy + labelDy, CellFontPt);
                            var vTop = fy + FlexLineBandPt + fc.ValuePadPx * 0.75 + labelDy;
                            if (fc.ValueLeft.Length > 0)
                                FDraw(fc.ValueLeft, textX, vTop, CellFontPt);
                            if (fc.ValueRight.Length > 0)
                                FDraw(fc.ValueRight,
                                    cellR - fc.ValueRightMrFrac * cw
                                          - FWidth(fc.ValueRight, CellFontPt),
                                    vTop, CellFontPt);
                        }
                        else
                        {
                            if (fc.Label.Length > 0)
                                FDraw(fc.Label, fc.Center
                                        ? cx + (cw - FWidth(fc.Label, CellFontPt)) / 2
                                        : textX,
                                    fy + labelDy, CellFontPt);
                            if (fc.Value.Length > 0)
                                FDraw(fc.Value,
                                    cellR - valueInset - FWidth(fc.Value, CellFontPt),
                                    fy + FlexLineBandPt + labelDy, CellFontPt);
                        }
                        cx = cellR;
                    }
                    fy = nextRowTop;
                }
                // The container's own border box: a wrapper-declared height runs to
                // its full depth — past the page bottom onto a continuation page —
                // otherwise it closes at the last row.
                FLine(contL + 0.38, contT + 0.38, contR - 0.38, contT + 0.38);
                if (fg.PageContentHPt > 0)
                {
                    var pageBottomTd = pageHeight - marginBottom;
                    var contBottomTd = contT + fg.PageContentHPt;
                    var b1 = Math.Min(contBottomTd, pageBottomTd);
                    FLine(contL + 0.38, contT + 0.38, contL + 0.38, b1);
                    FLine(contR - 0.38, contT + 0.38, contR - 0.38, b1);
                    if (contBottomTd <= pageBottomTd)
                        FLine(contL + 0.38, b1, contR - 0.38, b1);
                    else
                    {
                        var tail = contBottomTd - b1;
                        page = doc.Pages.Add(pageWidth, pageHeight);
                        EnsureFonts(page, docFontDict);
                        var t0 = marginTop;
                        FLine(contL + 0.38, t0, contL + 0.38, t0 + tail);
                        FLine(contR - 0.38, t0, contR - 0.38, t0 + tail);
                        FLine(contL + 0.38, t0 + tail, contR - 0.38, t0 + tail);
                        y = pageHeight - (t0 + tail);
                    }
                }
                else
                {
                    FLine(contL + 0.38, fy, contR - 0.38, fy);
                    FLine(contL + 0.38, contT + 0.38, contL + 0.38, fy);
                    FLine(contR - 0.38, contT + 0.38, contR - 0.38, fy);
                    y = pageHeight - fy - FlexRowBorderPt;
                }
                contentPage = page;
                lastWasHardBreak = false;
                continue;
            }

            // Positioned slide: every absolutely positioned item draws at its
            // canvas geometry — the canvas anchors at the content origin (page
            // margin + the UA body margin) on the extent-widened sheet.
            if (block.Slide is { } slide)
            {
                const double PxPt = 0.75;
                var slX = marginLeft + CardBodyPadPt;
                var slTopY = pageHeight - marginTop - CardBodyPadPt;
                // The slide sheet's own type for free text runs: the body rule's
                // px size and unitless line factor (UA 16px/normal otherwise).
                var slFontPt = 10.5;
                var slLinePt = 15.0;
                if (css.TryGetValue("body", out var slBody))
                {
                    if (slBody.TryGetValue("font-size", out var slFs)
                        && Regex.Match(slFs, @"([\d.]+)\s*px") is { Success: true } sfm
                        && double.TryParse(sfm.Groups[1].Value, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var sfPx) && sfPx > 0)
                        slFontPt = sfPx * PxPt;
                    if (slBody.TryGetValue("line-height", out var slLh)
                        && double.TryParse(slLh.Trim(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var slLhF) && slLhF > 0)
                        slLinePt = Math.Round(slFontPt / PxPt * slLhF) * PxPt;
                }
                var slColor = Color.FromRgb(0, 0, 0);
                if (css.TryGetValue("body", out var slBody2)
                    && slBody2.TryGetValue("color", out var slCol)
                    && ParseCssColor(slCol) is { } slC) slColor = slC;
                foreach (var it in slide.Items)
                {
                    var ix = slX + it.LeftPx * PxPt;
                    var iTop = slTopY - it.TopPx * PxPt;
                    if (it.IsImage && it.Src is not null)
                    {
                        var ibytes = LoadConverterImage(it.Src, options);
                        if (ibytes is null) continue;
                        var iw = it.WPx * PxPt;
                        var ih = it.HPx * PxPt;
                        // background-repeat:no-repeat without a size: the image sits
                        // at NATURAL size centre-anchored — crop it to the box.
                        if (!it.Stretch && CenterCropToBox(ibytes, it.WPx, it.HPx) is { } cropped)
                            ibytes = cropped;
                        try
                        {
                            if (it.RotDeg != 0)
                            {
                                // CSS rotation about the box centre (same convention
                                // as the flow image path).
                                var rad = it.RotDeg * Math.PI / 180.0;
                                var bw = Math.Abs(iw * Math.Cos(rad)) + Math.Abs(ih * Math.Sin(rad));
                                var bh = Math.Abs(iw * Math.Sin(rad)) + Math.Abs(ih * Math.Cos(rad));
                                var stamp = ImageStamp.FromEncodedBytes(ibytes);
                                stamp.XIndent = ix + iw / 2 - bw / 2;
                                stamp.YIndent = iTop - ih / 2 - bh / 2;
                                stamp.DisplayWidth = iw;
                                stamp.DisplayHeight = ih;
                                stamp.RotateAngle = -it.RotDeg;
                                stamp.ApplyTo(page);
                            }
                            else
                                page.AddImage(ibytes, new Rectangle(ix, iTop - ih, ix + iw, iTop));
                        }
                        catch { /* undecodable image: skip */ }
                    }
                    else if (it.Text.Length > 0)
                    {
                        // Baseline = line-box top + half-leading + the win ascent
                        // (Arial 1854/2048; descent 434/2048) — measured EXACT on
                        // the slide fixture's free-text run.
                        var halfLead = (slLinePt - slFontPt * (SlideTextAscEm + SlideTextDescEm)) / 2;
                        var baseline = iTop - halfLead - slFontPt * SlideTextAscEm;
                        var ops = FormattableString.Invariant(
                            $"BT /F1 {slFontPt:0.##} Tf {slColor.R / 255.0:0.###} {slColor.G / 255.0:0.###} {slColor.B / 255.0:0.###} rg 1 0 0 1 {ix:0.##} {baseline:0.##} Tm ({EscapePdfString(it.Text)}) Tj ET\n");
                        page.AddContentStream(Encoding.ASCII.GetBytes(ops));
                        contentPage = page;
                    }
                }
                y -= slide.MinHPx * PxPt;
                lastWasHardBreak = false;
                continue;
            }

            // Positioned media card: draw the whole card at absolute geometry —
            // media box with its placeholder icon and bottom-anchored bars, the
            // clipped prose column, and the two-column info panel (see
            // PositionedCard; every quantity an empirical fixed value).
            if (block.Card is { } pc)
            {
                const double PxPt = 0.75;
                var invC = System.Globalization.CultureInfo.InvariantCulture;
                var cSerifR = PosFace("Times New Roman");
                var cSerifB = PosFace("Times New Roman Bold");
                var fontDictC = page.Dict.Get("Resources") is Core.PdfDictionary cres
                    ? cres.Get("Font") as Core.PdfDictionary : null;

                double CWidth(string s, bool bold, double pt)
                    => MeasureFixedText(bold ? "Times New Roman Bold" : "Times New Roman", s, pt, 0);

                void CDrawText(string s, bool bold, double x, double baseline, double pt, Color col)
                {
                    var f = bold && cSerifB.ttf is not null ? cSerifB : cSerifR;
                    if (fontDictC is null || f.ttf is null || s.Length == 0) return;
                    var (rn, hex) = Text.Type0FontEmbedder.Embed(fontDictC, f.ttf,
                        bold ? "TimesNewRomanBold" : "TimesNewRoman", s, stripSpacesInBaseFont: true);
                    var t = new StringBuilder();
                    t.Append("BT ").Append((col.R / 255.0).ToString("0.###", invC)).Append(' ')
                        .Append((col.G / 255.0).ToString("0.###", invC)).Append(' ')
                        .Append((col.B / 255.0).ToString("0.###", invC)).Append(" rg ");
                    t.Append($"/{rn} {pt.ToString("F1", invC)} Tf ");
                    t.Append($"1 0 0 1 {x.ToString("F3", invC)} {baseline.ToString("F3", invC)} Tm ");
                    t.Append('<').Append(System.Convert.ToHexString(hex)).Append("> Tj ET ");
                    page.AddContentStream(Encoding.ASCII.GetBytes(t.ToString()));
                }

                void COps(string ops) => page.AddContentStream(Encoding.ASCII.GetBytes(ops));

                // the serif line's baseline seat inside its 13.5 box: half-leading
                // + winAscent (the same drop the form-grid strut model measured)
                double CSerifDrop(double pt)
                {
                    var box = PxLinePt(pt, SerifWinLineRatio);
                    return (box - pt * SerifWinLineRatio) / 2 + pt * SerifWinAscent;
                }

                var cx0 = marginLeft;                          // 90 + the UA body pad = 96
                var mediaTop = y - CardBodyPadPt;              // content top + body margin
                var mediaBot = mediaTop - pc.MediaHPx * PxPt;
                var cardRight = cx0 + pc.MediaWPx * PxPt;

                // broken-image placeholder: white frame, 1px black border, the
                // torn-page glyph (grey-stroked inner rect, like the cell path's)
                if (pc.HasImg)
                {
                    var ib = CardIconBoxPt;
                    var iy = mediaTop - ib;
                    COps($"q 1 1 1 rg {cx0.ToString("F2", invC)} {iy.ToString("F2", invC)} {ib.ToString("F2", invC)} {ib.ToString("F2", invC)} re f "
                        + $"0 0 0 RG 1 w {(cx0 + 0.5).ToString("F2", invC)} {(iy + 0.5).ToString("F2", invC)} {(ib - 1).ToString("F2", invC)} {(ib - 1).ToString("F2", invC)} re S "
                        + $"0.5 0.5 0.5 RG 1 w {(cx0 + 6.5).ToString("F2", invC)} {(iy + ib / 2 - 8).ToString("F2", invC)} 12 16 re S Q ");
                }

                // bottom-anchored caption bars
                foreach (var bar in pc.Bars)
                {
                    var barH = bar.HPx * PxPt;
                    var barTop = mediaBot + (bar.BottomPx + bar.HPx) * PxPt;
                    COps($"q {(bar.Fill.R / 255.0).ToString("0.###", invC)} {(bar.Fill.G / 255.0).ToString("0.###", invC)} {(bar.Fill.B / 255.0).ToString("0.###", invC)} rg "
                        + $"{cx0.ToString("F2", invC)} {(barTop - barH).ToString("F2", invC)} {(cardRight - cx0).ToString("F2", invC)} {barH.ToString("F2", invC)} re f Q ");
                    if (bar.Text.Length > 0)
                        CDrawText(bar.Text, false, cx0, barTop - CSerifDrop(12.0), 12.0, bar.TextColor);
                }

                // float:left prose column — greedy serif wrap in its box, clipped
                // to the declared height (overflow:hidden drops whole lines)
                {
                    var boxW = pc.TextWPx * PxPt;
                    var proseLines = new List<string>();
                    var cur = "";
                    foreach (var w in pc.ParaText.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var cand = cur.Length == 0 ? w : cur + " " + w;
                        if (cur.Length > 0 && CWidth(cand, false, 12.0) > boxW)
                        { proseLines.Add(cur); cur = w; }
                        else cur = cand;
                    }
                    if (cur.Length > 0) proseLines.Add(cur);
                    var clipBot = mediaBot - pc.TextHPx * PxPt;
                    var proseBox = PxLinePt(12.0, SerifWinLineRatio);
                    for (var li = 0; li < proseLines.Count; li++)
                    {
                        var boxTop = mediaBot - CardParaFirstPt - li * proseBox;
                        if (boxTop - 12.0 * SerifWinLineRatio < clipBot) break;
                        CDrawText(proseLines[li], false, cx0, boxTop - CSerifDrop(12.0), 12.0,
                            Color.FromRgb(0, 0, 0));
                    }
                }

                // float:right info panel — label column left-anchored, value
                // column right-aligned on the card's right edge; both walk their
                // paragraph slots on the measured pitch chain
                {
                    var infoX = cardRight - pc.InfoWPx * PxPt;
                    var colTop = mediaBot - pc.InfoMtPx * PxPt - CardInfoStartPt;
                    void WalkColumn(List<(string Text, bool Bold, double MtPx, int Kind)> slots, bool rightAlign)
                    {
                        var boxTop = colTop;
                        var first = true;
                        foreach (var slot in slots)
                        {
                            if (slot.Kind == 1) { boxTop -= CardInfoEmptyPt; continue; }
                            if (slot.Kind == 2) { boxTop -= CardInfoEmptyFullPt; continue; }
                            if (!first)
                                boxTop -= slot.MtPx > 0
                                    ? slot.MtPx * PxPt + CardInfoLineBoxPt
                                    : CardInfoPitchPt;
                            first = false;
                            var tx = rightAlign
                                ? cardRight - CWidth(slot.Text, slot.Bold, 9.0)
                                : infoX;
                            CDrawText(slot.Text, slot.Bold, tx,
                                boxTop - 9.0 * SerifWinAscent, 9.0, Color.FromRgb(0, 0, 0));
                        }
                    }
                    WalkColumn(pc.Labels, rightAlign: false);
                    WalkColumn(pc.Values, rightAlign: true);
                }

                y = mediaTop - (pc.ContainerHPx > 0 ? pc.ContainerHPx : pc.MediaHPx * 2) * PxPt;
                lastWasHardBreak = false;
                continue;
            }

            if (block.TopicsList is { } tp)
            {
                const double PxPt = 0.75;
                var invT = System.Globalization.CultureInfo.InvariantCulture;
                var serifR = PosFace("Times New Roman");
                var serifB = PosFace("Times New Roman Bold");
                var fontDictT = page.Dict.Get("Resources") is Core.PdfDictionary tres
                    ? tres.Get("Font") as Core.PdfDictionary : null;

                var itemPenRight = marginLeft + 209.75;
                var bulletX = itemPenRight + 4.5;
                var capPenRight = itemPenRight + 30.0;
                const double ItemPt = 12.0, CapPt = 9.0;
                const double ItemPitch = 13.5;   // 12pt × 1.125 leading
                const double CapDrop = 18.0;     // block top → caption baseline
                const double CapToItem = 28.04;  // caption baseline → first item baseline

                var figW = tp.SvgWPx * PxPt;
                var figH = tp.SvgHPx * PxPt;
                var listH = CapDrop + CapToItem + (tp.Items.Count - 1) * ItemPitch + 4;
                var blockH = Math.Max(figH, listH) + 8;
                if (y - blockH < marginBottom && y < pageHeight - marginTop - 1e-3)
                {
                    page = doc.Pages.Add(pageWidth, pageHeight);
                    EnsureFonts(page, docFontDict);
                    y = pageHeight - marginTop; pendingTopDrop = hasZeroTopMargin;
                    fontDictT = page.Dict.Get("Resources") is Core.PdfDictionary tres2
                        ? tres2.Get("Font") as Core.PdfDictionary : null;
                }

                double RawWidth((byte[]? ttf, Text.GlyphOutlineParser? parser, double upm) face,
                    string s, double pt)
                {
                    if (face.parser is null) return 0.5 * pt * s.Length;
                    double total = 0;
                    foreach (var ch in s)
                        total += face.parser.GetAdvanceWidth(
                            face.parser.CMap.TryGetValue(ch, out var g) ? g : 0);
                    return total * pt / face.upm;
                }

                // penRight > 0: right-align the shaped text on that pen edge;
                // penRight = 0 with leftX: draw left-anchored (bullet markers).
                void DrawSerif((byte[]? ttf, Text.GlyphOutlineParser? parser, double upm) face,
                    string baseName, string text, double penRight, double leftX,
                    double baseline, double pt)
                {
                    if (fontDictT is null || face.ttf is null || text.Length == 0) return;
                    var shaped = Text.ArabicTextShaper.ContainsArabic(text)
                        ? Text.ArabicTextShaper.Shape(text) : text;
                    var tx = penRight > 0 ? penRight - RawWidth(face, shaped, pt) : leftX;
                    var (rn, hex) = Text.Type0FontEmbedder.Embed(fontDictT, face.ttf, baseName,
                        shaped, stripSpacesInBaseFont: true);
                    var t = new StringBuilder();
                    t.Append("BT 0 0 0 rg ");
                    t.Append($"/{rn} {pt.ToString("F1", invT)} Tf ");
                    t.Append($"1 0 0 1 {tx.ToString("F3", invT)} {baseline.ToString("F3", invT)} Tm ");
                    t.Append('<').Append(System.Convert.ToHexString(hex)).Append("> Tj ET ");
                    page.AddContentStream(Encoding.ASCII.GetBytes(t.ToString()));
                }

                // Figure first (graphics only — contributes no text fragments).
                if (tp.SvgIdx >= 0 && tp.SvgIdx < inlineSvgs.Count)
                {
                    var figBytes = ImageRasterizer.RasterizeSvg(inlineSvgs[tp.SvgIdx], out _, out _);
                    if (figBytes is not null)
                    {
                        var figRight = marginLeft + contentWidth;
                        try
                        {
                            page.AddImage(figBytes, new Rectangle(figRight - figW, y - figH, figRight, y));
                        }
                        catch { }
                    }
                }

                var capBase = y - CapDrop;
                if (tp.CaptionText is not null)
                    DrawSerif(serifB.ttf is not null ? serifB : serifR, "TimesNewRomanBold",
                        tp.CaptionText, capPenRight, 0, capBase, CapPt);
                for (var ti = 0; ti < tp.Items.Count; ti++)
                {
                    var ibase = capBase - CapToItem - ti * ItemPitch;
                    DrawSerif(serifR, "TimesNewRoman", " •", 0, bulletX, ibase, ItemPt);
                    DrawSerif(serifR, "TimesNewRoman", tp.Items[ti], itemPenRight, 0, ibase, ItemPt);
                }

                y -= blockH;
                lastWasHardBreak = false;
                continue;
            }

            // Centered search form: fixed-width cell with the input widget (+overlay
            // icon), a centered push-button row, and a side link clipped at the
            // content-box edge.
            if (block.Form is { } sf)
            {
                const double PxPt = 0.75;
                var invf = System.Globalization.CultureInfo.InvariantCulture;
                var totalH = (sf.MarginTopPx + sf.InputHeightPx + sf.GapPx + sf.ButtonHeightPx + sf.MarginBottomPx) * PxPt;
                if (y - totalH < marginBottom)
                {
                    page = doc.Pages.Add(pageWidth, pageHeight);
                    EnsureFonts(page, docFontDict);
                    y = pageHeight - marginTop; pendingTopDrop = hasZeroTopMargin;
                }
                y -= sf.MarginTopPx * PxPt;
                var cellW = sf.CellWidthPx * PxPt;
                var cellX = marginLeft + (contentWidth - cellW) / 2;
                var inputW = sf.InputWidthPx * PxPt;
                var inputH = sf.InputHeightPx * PxPt;

                var fld = new Forms.TextBoxField(page, new Rectangle(cellX, y - inputH, cellX + inputW, y));
                if (!string.IsNullOrEmpty(sf.InputName)) fld.PartialName = sf.InputName;
                doc.Form.Add(fld, page.Number);
                DrawBox(page, cellX, y - inputH, inputW, inputH,
                    border: Color.FromRgb(0, 0, 0), borderWidth: 0.75, fill: null);

                if (!string.IsNullOrEmpty(sf.IconSrc))
                {
                    var ib = LoadConverterImage(sf.IconSrc, options);
                    if (ib is not null)
                    {
                        var iw = sf.IconWPx * PxPt;
                        var ih2 = sf.IconHPx * PxPt;
                        var ix = cellX + inputW - sf.IconRightPx * PxPt - iw;
                        var iy = y - sf.IconTopPx * PxPt;
                        try { page.AddImage(ib, new Rectangle(ix, iy - ih2, ix + iw, iy)); } catch { }
                    }
                }

                var res0 = page.Dict.Get("Resources") as Core.PdfDictionary;
                var fdict = res0?.Get("Font") as Core.PdfDictionary;
                var arial = PosFace("Arial");

                if (!string.IsNullOrEmpty(sf.LinkText) && arial.ttf is not null && fdict is not null)
                {
                    var lx = cellX + cellW + sf.LinkMarginLeftPx * PxPt;
                    var lf = sf.LinkFontPx * PxPt;
                    var lbase = y - 5 * PxPt - 0.85 * lf;
                    var g0 = new StringBuilder();
                    // clip at the content box so an overlong side link ends at the margin
                    g0.Append("q ");
                    g0.Append($"{marginLeft.ToString("F2", invf)} {(y - inputH - 30).ToString("F2", invf)} {contentWidth.ToString("F2", invf)} {(inputH + 60).ToString("F2", invf)} re W n ");
                    var (rn, hex) = Text.Type0FontEmbedder.Embed(fdict, arial.ttf, "Arial", sf.LinkText!, stripSpacesInBaseFont: true);
                    g0.Append("BT ");
                    g0.Append($"{(sf.LinkColor.R / 255.0).ToString("F5", invf)} {(sf.LinkColor.G / 255.0).ToString("F5", invf)} {(sf.LinkColor.B / 255.0).ToString("F5", invf)} rg ");
                    g0.Append($"/{rn} {lf.ToString("F1", invf)} Tf 1 0 0 1 {lx.ToString("F2", invf)} {lbase.ToString("F2", invf)} Tm ");
                    g0.Append('<').Append(System.Convert.ToHexString(hex)).Append("> Tj ET Q ");
                    page.AddContentStream(Encoding.ASCII.GetBytes(g0.ToString()));
                    if (!string.IsNullOrEmpty(sf.LinkUrl))
                        pendingLinks.Add((page, new Rectangle(lx, lbase - 3,
                            Math.Min(lx + MeasureFaceText("Arial", sf.LinkText!, lf), marginLeft + contentWidth),
                            lbase + lf), sf.LinkUrl!, sf.LinkText));
                }

                y -= inputH + sf.GapPx * PxPt;

                if (sf.Buttons.Count > 0 && arial.ttf is not null && fdict is not null)
                {
                    var bfpt = sf.ButtonFontPx * PxPt;
                    var widths = new double[sf.Buttons.Count];
                    double btotal = 0;
                    for (var bi = 0; bi < sf.Buttons.Count; bi++)
                    {
                        widths[bi] = MeasureFaceText("Arial", sf.Buttons[bi].Label, bfpt) + 2 * sf.ButtonPadPx * PxPt;
                        btotal += widths[bi];
                    }
                    btotal += (sf.Buttons.Count - 1) * sf.ButtonGapPx * PxPt;
                    var bx = cellX + (sf.InputContentPx * PxPt - btotal) / 2;
                    var bh = sf.ButtonHeightPx * PxPt;
                    var g1 = new StringBuilder();
                    for (var bi = 0; bi < sf.Buttons.Count; bi++)
                    {
                        var bw = widths[bi];
                        g1.Append("q ");
                        g1.Append($"{(sf.ButtonBg.R / 255.0).ToString("F5", invf)} {(sf.ButtonBg.G / 255.0).ToString("F5", invf)} {(sf.ButtonBg.B / 255.0).ToString("F5", invf)} rg ");
                        g1.Append($"{bx.ToString("F2", invf)} {(y - bh).ToString("F2", invf)} {bw.ToString("F2", invf)} {bh.ToString("F2", invf)} re f ");
                        g1.Append("0 0 0 RG 0.75 w ");
                        g1.Append($"{(bx + 0.375).ToString("F2", invf)} {(y - bh + 0.375).ToString("F2", invf)} {(bw - 0.75).ToString("F2", invf)} {(bh - 0.75).ToString("F2", invf)} re S Q ");
                        var label = sf.Buttons[bi].Label;
                        var (rn2, hex2) = Text.Type0FontEmbedder.Embed(fdict, arial.ttf, "Arial", label, stripSpacesInBaseFont: true);
                        var tw = MeasureFaceText("Arial", label, bfpt);
                        var tx = bx + (bw - tw) / 2;
                        var tbase = y - (bh + 0.72 * bfpt) / 2;
                        g1.Append("BT ");
                        g1.Append($"{(sf.ButtonFg.R / 255.0).ToString("F5", invf)} {(sf.ButtonFg.G / 255.0).ToString("F5", invf)} {(sf.ButtonFg.B / 255.0).ToString("F5", invf)} rg ");
                        g1.Append($"/{rn2} {bfpt.ToString("F1", invf)} Tf 1 0 0 1 {tx.ToString("F2", invf)} {tbase.ToString("F2", invf)} Tm ");
                        g1.Append('<').Append(System.Convert.ToHexString(hex2)).Append("> Tj ET ");
                        bx += bw + sf.ButtonGapPx * PxPt;
                    }
                    page.AddContentStream(Encoding.ASCII.GetBytes(g1.ToString()));
                }

                y -= (sf.ButtonHeightPx + sf.MarginBottomPx) * PxPt;
                lastWasHardBreak = false;
                continue;
            }

            // Styled inline row (nav bar / centered link line): bar rect + measured
            // horizontal runs, drawn directly at the flow cursor. Vertical margins
            // between adjacent rows collapse (CSS margin collapsing).
            if (block.RowRuns is { Count: > 0 })
            {
                var collapse = Math.Min(prevRowBottomPx, block.RowMarginTopPx);
                y += collapse * 0.75;
                RenderRowBlock(page, block, ref y, marginLeft, contentWidth, pendingLinks);
                prevRowMarginBottomPx = block.RowMarginBottomPx;
                lastWasHardBreak = false;
                lastWasRow = true;
                continue;
            }

            // Form dialect: a mid-flow <hr> (the section-divider div it replaced)
            // DRAWS its rule line across the content box, with its CSS margins around
            // it. The top margin collapses with the preceding block's bottom margin
            // (CSS adjacent-margin collapse — a heading right above a divider
            // contributes max(heading-bottom, divider-top), not their sum). Every
            // other dialect keeps the legacy spacing-only <hr>.
            // Report label/span dialect: the section divider draws at its own
            // percentage width from the content left — a GROOVE, a
            // black top line over a dark-grey one 0.75 lower.
            if (block.IsHorizontalRule && block.MaxWidthPt > 0)
            {
                // anchor on the preceding BASELINE: rewind the last line box, then
                // drop the measured baseline→groove distance
                if (prevFlowLineHeight > 0) { y += prevFlowLineHeight; }
                y -= ReportHrBelowBasePt;
                DrawBox(page, marginLeft + block.LeftIndent, y - ReportGroovePt,
                    block.MaxWidthPt, ReportGroovePt, null, 0, ParseCssColor("#000000"));
                DrawBox(page, marginLeft + block.LeftIndent, y - 2 * ReportGroovePt,
                    block.MaxWidthPt, ReportGroovePt, null, 0, ParseCssColor("#555555"));
                y -= 2 * ReportGroovePt + ReportHrAfterPt;
                contentPage = page;
                prevFlowMarginBottom = block.MarginBottom;
                prevFlowLineHeight = 0;
                continue;
            }
            if (formHorizontalDoc && block.IsHorizontalRule)
            {
                var fhRuleH = sectionedReport ? 1.5 : Math.Max(0.75, block.RuleWidth * 0.75);
                var fhTopGap = Math.Max(block.MarginTop - prevFlowMarginBottom, 0);
                if (prevFlowLineHeight > 0)
                {
                    // Rewind the preceding text block's full-line-box advance to its
                    // CSS box bottom (baseline + ~0.3em descent), then the collapsed
                    // margin pair — the heading→divider rhythm.
                    y += prevFlowLineHeight + prevFlowMarginBottom;
                    y -= prevFlowFontSize * 0.3 + Math.Max(block.MarginTop, prevFlowMarginBottom);
                    fhTopGap = 0;
                    prevFlowLineHeight = 0;
                }
                if (y - fhTopGap - fhRuleH < marginBottom)
                {
                    page = doc.Pages.Add(pageWidth, pageHeight);
                    EnsureFonts(page, docFontDict);
                    y = pageHeight - marginTop; pendingTopDrop = hasZeroTopMargin;
                    fhTopGap = 0;
                }
                y -= fhTopGap;
                DrawBox(page, marginLeft, y - fhRuleH, contentWidth, fhRuleH,
                    null, 0, block.RuleColor ?? ParseCssColor("#999999"));
                y -= fhRuleH + block.MarginBottom;
                contentPage = page;
                prevFlowMarginBottom = block.MarginBottom;
                // The next TEXT block draws its first baseline AT the cursor (glyphs
                // extend upward) — without a line-box drop it would overprint the
                // rule. Tables and images lay out downward from the cursor and clear
                // the flag untouched.
                afterRuleDrop = true;
                lastWasHardBreak = false;
                continue;
            }

            // Sectioned report: an <hr> is a real box, not just a gap. The UA rule
            // `hr { border: 1px inset }` paints a 0.75 pt black top border over a
            // 0.75 pt #555555 bottom one across the content box, and the legacy
            // size/color/noshade attributes are ignored. The box top sits one
            // baseline offset ABOVE the cursor (which runs in baseline space); the
            // space the rule reserves below is left exactly as it was, so this adds
            // ink without disturbing the surrounding rhythm.
            if (sectionedReport && block.IsHorizontalRule)
            {
                var hrTop = y + BaselineInLineBoxPt(
                    prevFlowFontSize > 0 ? prevFlowFontSize : blockFontSize);
                if (hrTop - 1.5 >= marginBottom)
                {
                    DrawBox(page, marginLeft, hrTop - 0.75, contentWidth, 0.75,
                        null, 0, Color.Black);
                    DrawBox(page, marginLeft, hrTop - 1.5, contentWidth, 0.75,
                        null, 0, ParseCssColor("#555555"));
                    contentPage = page;
                }
            }
            // Escaped-attr dialect: the section divider is the same UA groove — a
            // black hairline over a #555 one — spanning symmetric 96 pt margins
            // (measured: the rule sits 10 pt under the previous control line's
            // baseline, 4.4 pt above the cursor that line's advance left).
            else if (escapedAttrDoc && block.IsHorizontalRule)
            {
                var hrTop = y + 4.42;
                if (hrTop - 0.75 >= marginBottom)
                {
                    DrawBox(page, marginLeft, hrTop, pageWidth - 2 * marginLeft, 0.75,
                        null, 0, Color.Black);
                    DrawBox(page, marginLeft, hrTop - 0.75, pageWidth - 2 * marginLeft, 0.75,
                        null, 0, ParseCssColor("#555555"));
                    contentPage = page;
                }
                afterEscapedRule = true;
            }
            // UA-default serif flow: an <hr> is 0.5em of margin, a 1.5 pt groove
            // box (dark top+left stroke over a #555 bottom+right one) spanning
            // the symmetric content frame, and 0.5em more margin — 13.5 pt of
            // flow in all; the size/color attributes are ignored (measured:
            // size 2, 4 and 6 rules all draw the same 1.5 pt box at 96..499).
            else if ((uaStdSerif || ptReportDoc) && !sectionedReport && block.IsHorizontalRule)
            {
                var hrW = pageWidth - 2 * marginLeft;
                var hrBoxTop = y - UaBodyMarginPt;            // bottom-up box top edge
                if (hrBoxTop - 1.5 >= marginBottom)
                {
                    DrawBox(page, marginLeft, hrBoxTop - 0.75, hrW, 0.75, null, 0, Color.Black);
                    DrawBox(page, marginLeft, hrBoxTop - 1.5, hrW, 0.75, null, 0,
                        ParseCssColor("#555555"));
                    DrawBox(page, marginLeft, hrBoxTop - 1.5, 0.75, 1.5, null, 0, Color.Black);
                    DrawBox(page, marginLeft + hrW - 0.75, hrBoxTop - 1.5, 0.75, 1.5, null, 0,
                        ParseCssColor("#555555"));
                    contentPage = page;
                }
                y -= 2 * UaBodyMarginPt + 1.5;
                // The rule's trailing 0.5em is a MARGIN — it max-collapses with
                // the following block's own top margin (probed: hr then an empty
                // p's 13.44 opens 13.44 total, not 6 + 13.44).
                uaPrevMarginBottom = UaBodyMarginPt;
                lastWasHardBreak = false;
                continue;
            }

            // Hard-break blocks (<br>, empty <p>, <hr>) only consume vertical
            // space — never emit an empty BT/ET run, which would surface as
            // extra zero-length TextFragments to TextFragmentAbsorber. Coalesce
            // runs of consecutive hard-breaks so deeply-nested empty containers
            // don't explode page count (HTML like <div><div></div></div> emits
            // a chain of closes that would otherwise each become a blank line).
            if (block.IsHardBreak || string.IsNullOrEmpty(block.Text))
            {
                // Border-top divider marker: the div's rule strokes here, above
                // its content, and spends only its own width.
                if (block.BorderTopOnly && block.BorderColor is { } topRule
                    && block.BorderWidth > 0)
                {
                    var invtr = System.Globalization.CultureInfo.InvariantCulture;
                    page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invtr,
                        $"q {topRule.R / 255.0:0.###} {topRule.G / 255.0:0.###} {topRule.B / 255.0:0.###} RG " +
                        $"{block.BorderWidth:0.##} w {marginLeft:0.##} {y - block.BorderWidth / 2:0.##} m " +
                        $"{pageWidth - marginLeft:0.##} {y - block.BorderWidth / 2:0.##} l S Q\n")));
                    y -= block.BorderWidth;
                    contentPage = page;
                    lastWasHardBreak = false;
                    continue;
                }
                // Prefer the explicit CSS height over the default half-line
                // spacer — CMS template HTML often uses empty styled divs as
                // visual separator bars, and ignoring their height would
                // collapse intended pagination.
                // A <br> directly after a styled row ends a full default-size line box
                // (the browser's 16px body line), not the usual half-line spacer.
                var spacer = block.ExplicitHeight > 0
                    ? block.ExplicitHeight
                    : (lastWasHardBreak ? 0 : (wasRow ? 13.5 : lineHeight * 0.5));
                // Form dialect: this document family separates sections with CSS
                // margins, and its bare <br>s are all float-clears (`clear:both`) that
                // collapse to the float bottom — they add no line boxes of their own.
                if (formHorizontalDoc && block.ExplicitHeight <= 0) spacer = 0;
                // Form-document dialect: every standalone <br> is one full line box at
                // its enclosing size — consecutive <br>s stack (no half-line coalescing).
                else if (formDialectTables && block.IsLineBreak)
                    spacer = (block.FontSize > 0 ? block.FontSize : blockFontSize) * 1.3;
                // CSS run dialect: a standalone <br> is one full line box of the page
                // stylesheet's own base face and size — the same rule its cells pitch on.
                else if (bodyCssFace is not null && block.IsLineBreak
                         && WinMetricsFor(bodyCssFace) is { } brFace)
                    spacer = MetricLineHeight(
                        block.FontSize > 0 ? block.FontSize : bodyCssFontPt, brFace.sum);
                // Metric flow: a real <br> is one full line box at the size of its enclosing
                // style — every <br> counts (no coalescing). Styled spacers keep their CSS
                // height; other empty containers collapse to nothing.
                if (metricFlow)
                    spacer = block.IsLineBreak && WinMetricsFor(metricFace) is { } brm
                        ? (uaFlow
                            ? (block.FontSize > 0 ? block.FontSize : 12.0) * 1.125
                            : MetricLineHeight(block.FontSize > 0 ? block.FontSize : 11.0, brm.sum))
                        : block.IsLineBreak ? block.ExplicitHeight
                        : block.IsHardBreak && block.ExplicitHeight > 0 ? block.ExplicitHeight
                        : 0;
                if (spacer > 0)
                {
                    if (y - spacer < marginBottom)
                    {
                        page = doc.Pages.Add(pageWidth, pageHeight);
                        EnsureFonts(page, docFontDict);
                        y = FreshPageTopY(); pendingTopDrop = hasZeroTopMargin;
                    }
                    y -= spacer;
                }
                lastWasHardBreak = true;
                // A zero-space break (the form dialect's float-clears) is layout-inert:
                // it must not hide the preceding text block from the <hr>/image rewind.
                if (spacer > 0)
                {
                    prevFlowMarginBottom = 0;
                    prevFlowLineHeight = 0;
                }
                continue;
            }
            lastWasHardBreak = false;

            if (pendingTopDrop)
            {
                // First line of a zero-top-margin page: baseline = line box + block margin.
                // The metric flow needs no such drop — its baseline always sits inside the
                // line box (half-leading model), on every page.
                if (!metricFlow) y -= blockFontSize * 1.15 + block.MarginTop;
                pendingTopDrop = false;
                afterRuleDrop = false;
            }
            // First text after a form-dialect rule: line-box drop so the glyphs land
            // below the rule, not through it (0.9em puts the cap top
            // ~15px below the rule with the 10px rule margin already applied).
            else if (afterRuleDrop)
            {
                y -= blockFontSize * 0.9;
                afterRuleDrop = false;
                afterFhTable = false;
            }
            // Flow text directly after a synthesized form-row block: the line-box
            // drop plus the section gap kept above the next heading.
            else if (afterFhTable)
            {
                y -= blockFontSize * 1.15 + 10.3;
                afterFhTable = false;
            }
            else if (pendingTableDrop && !string.IsNullOrEmpty(block.Text))
            {
                y -= blockFontSize * 1.15;
                pendingTableDrop = false;
            }
            // Browser margin-collapse: the FIRST flow block's top margin collapses with the
            // page/body top margin at the document top — it does not stack on top of it (an
            // opening <h1> starts at the content top, not one h1-margin below it).
            else if (uaTopMarginPending)
            {
                // ...but an AUTHORED first-block margin MAX-collapses with the UA
                // body margin instead of vanishing: the content opens the excess
                // below the body inset (measured: 72 + max(6, 18.75) = 90.75).
                if (uaFlow && block.MarginTopAuthored && block.MarginTop > UaBodyMarginPt)
                    y -= block.MarginTop - UaBodyMarginPt;
                uaTopMarginPending = false;
            }
            // Apply top margin (unless we're at the start of a fresh page; a
            // MarginTopAlways block keeps it even there). The browser-UA flow
            // collapses it with the previous block's bottom margin.
            else if (block.MarginTopAlways || sectionedReport
                // the fieldset worksheet's padded body keeps margins at the top
                || (uaStdSerif && fieldsetDoc)
                || y < pageHeight - marginTop - 1e-3)
                y -= uaStdSerif || printGrid || sectionedReport || articleFlow
                    ? Math.Max(0, block.MarginTop - uaPrevMB) : block.MarginTop;
            // Padding is box space, not margin — it never collapses.
            if (block.PadTop > 0) y -= block.PadTop;
            // An inline broken image on this block's line grows the line box UP by
            // the icon: the baseline lands 29.2 pt lower than a bare text line
            // (measured: rule → icon-bearing label baseline = 41.12, bare = 11.9).
            if (escapedAttrDoc && block.InlineIconAfter) y -= InlineIconLineExtraPt;
            // Plain text directly under the section rule sits 17.9 below it, not the
            // bare 11.9 (headings carry their own margins and skip this).
            else if (afterEscapedRule && block.MarginTop <= 0) y -= RuleToTextExtraPt;
            afterEscapedRule = false;
            // Cover blocks (a class LineFactor set) lay out on the CSS box model:
            // the block enters at its line-box TOP, and each baseline seats one
            // descent above its line-box bottom (measured within 2pt across the
            // cover fixture's 1x and 3x line factors). The drop is repaid after
            // the lines so the next box starts at this box's bottom.
            var coverDrop = 0.0;
            if (block.LineFactor > 0 && blockFontSize > 0)
            {
                coverDrop = lineHeight - blockFontSize * SlideTextDescEm;
                y -= coverDrop;
            }

            var availWidth = contentWidth - block.LeftIndent;
            // Browser-UA flow: an enclosing div's width:N% narrows the wrap box.
            if (uaStdSerif && block.WidthFrac > 0)
                availWidth = Math.Min(availWidth, contentWidth * block.WidthFrac);
            // Form-document dialect: an enclosing div's ABSOLUTE width is the wrap box
            // (the state-notice divs wrap at their width:680 wrapper, not the page).
            if (formDialectTables && block.WidthPx > 0)
                availWidth = Math.Min(availWidth, block.WidthPx * 0.75);
            // Report label/span rows: the column's own box is the wrap box.
            if (block.MaxWidthPt > 0)
                availWidth = Math.Min(availWidth, block.MaxWidthPt);
            // A float:left LABEL with a pixel width: the next in-flow text
            // block sits BESIDE it on the same line, its text at the label's
            // declared box edge (measured: labels at 96, values at
            // 96 + 100px·0.75 = 171, one 13.5 pt line per pair).
            var floatLabelIndent = 0.0;
            if (uaStdSerif && pendingFloatLabelPt > 0 && !block.FloatLeft
                && !block.IsTable && !string.IsNullOrEmpty(block.Text))
            {
                y = pendingFloatLabelY;
                floatLabelIndent = pendingFloatLabelPt;
                availWidth = Math.Max(50, availWidth - floatLabelIndent);
            }
            pendingFloatLabelPt = 0;
            if (uaStdSerif && block.FloatLeft && !block.FloatRight
                && block.WidthPx > 0 && !block.IsTable
                && !string.IsNullOrEmpty(block.Text))
            {
                pendingFloatLabelPt = block.WidthPx * 0.75;
                pendingFloatLabelY = y;
            }
            var yBeforeBlockLines = y;
            // UA-serif flow: an element's inline border draws around its line
            // boxes, edges OUTSIDE them — the first line box opens one border
            // width below the border top and the box closes one width under
            // the last line (measured: border 90.75, line box 91.5, box bottom
            // 144.75 around a 52.5 line).
            var uaBorderBox = uaStdSerif && block.BorderWidth > 0
                && block.BorderColor is not null && !block.IsTable
                // A painted box draws its own border chrome with its fill,
                // and a border-only DECLARED box strokes its own frame below.
                && block.BgBoxHeightPt <= 0 && block.BorderBoxWPt <= 0
                && !string.IsNullOrEmpty(block.Text);
            if (uaBorderBox) y -= block.BorderWidth;
            // Border-only declared box: the border strokes the declared width ×
            // ExplicitHeight box (rounded by border-radius) hanging at the flow
            // position; the content flows INSIDE it — first line one border
            // width down, text inset one border width right, wrap clamped to
            // the declared content width.
            var uaDeclBox = uaStdSerif && block.BorderBoxWPt > 0
                && block.BorderColor is not null && block.BorderWidth > 0;
            if (uaDeclBox)
            {
                y -= block.BorderWidth;
                availWidth = Math.Min(availWidth, block.BorderBoxWPt);
            }
            // Inside a float-column band (the SEC-filing two-column card), wrap with the
            // block's real font metrics — the crude 0.52-em estimate mis-breaks the narrow
            // column (bold uppercase headings never wrap; body text wraps a line early).
            // Scoped to bands, so the calibrated flat-flow greens keep their 0.52-em breaks.
            // The form-document dialect wraps a family-declaring block the same way — its
            // notice divs (`font: bold 8pt Verdana`) break where the REAL face breaks.
            var bandFace = bandStack.Count > 0 ? BandMeasureFace(block)
                : formDialectTables && !string.IsNullOrEmpty(block.FontFamily) ? BandMeasureFace(block)
                // report label/span columns wrap on real advances too — the crude
                // 0.52-em estimate breaks their narrow boxes a word early
                : block.MaxWidthPt > 0 && !string.IsNullOrEmpty(block.FontFamily) ? BandMeasureFace(block)
                : null;
            // Beside a left-floated image the block still starts at the flow cursor, but
            // its first lines are shortened to the space left of the float's bottom edge.
            var floatLines = 0;
            if (floatIndentPt > 0 && y > floatBottomY + 1e-9 && lineHeight > 0)
                floatLines = (int)Math.Ceiling((y - floatBottomY) / lineHeight);
            var lines = block.MaxWidthPt > 0
                ? ReportWordWrap(block.Text, availWidth, blockFontSize, block.FontRes == "F2")
                : metricFlow && metricDrop > 0
                ? MeasuredWordWrap(block.Text, availWidth, metricMeasureFace, blockFontSize)
                : bandFace is not null
                    ? MeasuredWordWrap(block.Text, availWidth, bandFace, blockFontSize)
                    // Dash-overflow wrap: the doc carries a dash-delimited segment
                    // wider than the content box — every line then wraps at the
                    // widened limit on space/after-dash breakpoints with real face
                    // advances (see quirksWrapW above).
                    : quirksWrapW > availWidth
                    ? DashAwareWordWrap(block.Text, quirksWrapW, dashWrapFace, blockFontSize)
                    // The escaped-attr dialect draws serif — break lines on the real
                    // Times advances, not the 0.52-em estimate (17 % narrow).
                    : escapedAttrDoc
                        ? MeasuredWordWrap(block.Text, availWidth,
                            block.FontRes == "F2" ? "Times New Roman Bold" : "Times New Roman",
                            blockFontSize)
                        : floatLines > 0
                            ? WordWrapPastFloat(block.Text, availWidth - floatIndentPt, availWidth,
                                floatLines, blockFontSize * 0.52)
                            : WordWrap(block.Text, availWidth, blockFontSize * 0.52);
            // Pad the block's rendered area up to ExplicitHeight so styled
            // fixed-height elements keep their reserved vertical space even
            // when the text inside wraps to fewer lines.
            var textHeight = lines.Length * lineHeight;
            var paddingBelow = block.ExplicitHeight > textHeight ? block.ExplicitHeight - textHeight : 0;
            // A container-band height (the widget header's class-rule height) spans
            // from the LINE-BOX TOP, but this flow anchors y at the baseline — one
            // line box above the block was already spent getting there. Bill the
            // band's remainder below the baseline only, or the band gains a full
            // extra line (measured: the chart sat 18.8 pt low).
            if (block.BandBoxHeight && paddingBelow > 0)
                paddingBelow = Math.Max(0, paddingBelow - lineHeight);

            // Approximate per-char advance for mapping inline <a href> char ranges to
            // link-annotation rects (same crude metric WordWrap uses to break lines).
            var charW = blockFontSize * 0.52;
            var lineX = marginLeft + block.LeftIndent
                + (uaDeclBox ? block.BorderWidth : 0);
            var cumChar = 0;          // char offset of the current line's start within block.Text
            var firstLineOfBlock = true;
            var lineIdx = -1;
            foreach (var line in lines)
            {
                lineIdx++;
                // An icon-bearing line already spent its extra height ABOVE the
                // baseline — only the descent still needs room below (such a
                // line keeps its baseline 4.7 pt over the margin).
                var lineNeedBelow = escapedAttrDoc && block.InlineIconAfter ? SerifDescentRoomPt : lineHeight;
                if (y - lineNeedBelow < marginBottom)
                {
                    // Inside a float column the overflow is clipped, not paginated.
                    if (floatBandDoc && bandStack.Count > 0) { bandColClipped = true; break; }
                    page = doc.Pages.Add(pageWidth, pageHeight);
                    EnsureFonts(page, docFontDict);
                    y = FreshPageTopY(); pendingTopDrop = hasZeroTopMargin;
                    // UA flow: a block pushed to a fresh page re-applies its margin-top at
                    // the new page top (a continuation page's first paragraph baseline
                    // = topMargin + p-gap + ascent, not topMargin + ascent).
                    if (uaFlow && firstLineOfBlock) y -= block.MarginTop;
                }
                var fontRes = ResolveFontRes(page, block);

                // List marker: a separate text run in the item's left indent on the first line,
                // so it surfaces as its own TextFragment. A numeric/bullet marker is emitted BEFORE
                // the content (earlier fragment); a CSS ::before marker (MarkerAfter) is emitted
                // AFTER the content so, on an RTL line, the item text is the earlier fragment.
                // The marker itself may be Arabic/Hebrew/CJK, so it uses the same RTL/Type0 path
                // as body text.
                void EmitMarkerHere()
                {
                    // UA-default serif flow: the marker draws in the same Standard-14
                    // serif as its item text, hangs one marker advance + gap left of
                    // the item indent, and seats on the item's first baseline.
                    if (uaStdSerif)
                    {
                        var mAdv = MeasureFaceText(metricMeasureFace, block.Marker!, blockFontSize);
                        var uaX = Math.Max(marginLeft,
                            marginLeft + block.LeftIndent - mAdv - UaMarkerGapEm * blockFontSize);
                        // The marker inherits the item's weight (an h1-nested list
                        // draws bold bullets in the bold serif resource).
                        EmitPositionedRun(page, block.FontRes == "F2" ? "F6" : "F5",
                            blockFontSize, uaX,
                            metricDrop > 0 ? y - metricDrop : y, block.Marker!);
                        return;
                    }
                    var markerW = block.Marker!.Length * blockFontSize * 0.52;
                    var markerX = Math.Max(marginLeft, marginLeft + block.LeftIndent - markerW - 4);
                    // The marker sits on the SAME baseline as the item's first line —
                    // in the styled-article flow that line drops half-leading + ascent
                    // below the cursor, and the marker must drop with it. The OTHER
                    // metric dialects are calibrated with the raw-cursor marker.
                    EmitPositionedRun(page, fontRes, blockFontSize, markerX,
                        articleFlow && metricDrop > 0 ? y - metricDrop : y, block.Marker!);
                }
                if (firstLineOfBlock && !string.IsNullOrEmpty(block.Marker) && !block.MarkerAfter)
                    EmitMarkerHere();

                var invc = System.Globalization.CultureInfo.InvariantCulture;
                // dir=rtl documents lay flow lines out right-aligned: the line's right
                // edge sits on the right content margin (measured with real advances,
                // not the wrap estimate, so the anchor edge is exact).
                var lineXPos = marginLeft + block.LeftIndent + floatLabelIndent;
                // UA-serif flow: the span's own margin-left and the element's
                // border inset the text within the element box.
                if (uaStdSerif && (block.TextInsetPt > 0 || block.BorderWidth > 0))
                    lineXPos += block.TextInsetPt + block.BorderWidth;
                // Lines still level with a left-floated image start past its right edge.
                if (floatIndentPt > 0 && y > floatBottomY + 1e-9)
                    lineXPos += floatIndentPt;
                // Report label column: each wrapped line right-aligns inside its box,
                // measured in the report face's own metrics.
                if (block.RightAlignBoxPt > 0 && line.Length > 0)
                {
                    var raw = HeaderFooter.MeasureReportText(line, blockFontSize,
                        block.FontRes == "F2");
                    lineXPos = marginLeft + block.LeftIndent + Math.Max(0, block.RightAlignBoxPt - raw);
                }
                var rtlFace = block.FontRes == "F2" ? "Arial Bold" : "Arial";
                if (rtlDoc && line.Length > 0)
                {
                    var lw = MeasureFaceText(rtlFace, line, blockFontSize);
                    lineXPos = Math.Max(marginLeft, marginLeft + contentWidth - lw);
                }
                // UA-flow float: the block is a shrink-to-fit box on this line — a
                // left float sits on the left content edge, a right float against
                // the right one (pageWidth − marginLeft, the frame symmetric to the
                // flow's left content origin). Its background fills exactly the
                // measured advance (see the fill branch below).
                var uaFloatW = 0.0;
                if (uaStdSerif && metricDrop > 0 && (block.FloatLeft || block.FloatRight)
                    && line.Length > 0)
                {
                    uaFloatW = MeasureFaceText(metricMeasureFace, line, blockFontSize);
                    if (block.FloatRight)
                        lineXPos = Math.Max(marginLeft, pageWidth - marginLeft - uaFloatW);
                }
                // Metric flow: y is the line-box TOP; the baseline sits half-leading +
                // ascent below it. A centered block (text-align:center class) centers its
                // measured line in the content box. Legacy: baseline at the cursor.
                if (metricFlow && metricDrop > 0 && block.AlignCenter && line.Length > 0)
                {
                    var mw = MeasureFaceText(metricMeasureFace, line, blockFontSize);
                    // A class WIDTH is the element's own box — centring happens
                    // inside it, not the whole content width (the ledger title).
                    var ctrBox = uaStdSerif && block.WidthPx > 0
                        ? block.WidthPx * 0.75 : contentWidth;
                    lineXPos = Math.Max(marginLeft, marginLeft + (ctrBox - mw) / 2);
                }
                // UA-default serif flow honours an INLINE text-align:center: the line
                // centres in its element's content box — from the block's indent to a
                // right edge one full left-margin (90 + 6 body) inside the page, the
                // frame symmetric to the flow's left content origin (reference: "test"
                // centred at (126+499)/2 inside <ul><li><div text-align:center>).
                else if (uaStdSerif && metricDrop > 0 && block.AlignCenterCss && line.Length > 0)
                {
                    var mw = MeasureFaceText(metricMeasureFace, line, blockFontSize);
                    var boxLeft = marginLeft + block.LeftIndent;
                    var boxRight = pageWidth - marginLeft;
                    lineXPos = Math.Max(marginLeft, boxLeft + (boxRight - boxLeft - mw) / 2);
                }
                // UA-default serif flow honours an inline text-align:right the
                // same way: the line pins to the body box's right edge (the
                // rating-date div ends at 96 + content = 517, measured).
                else if (uaStdSerif && metricDrop > 0 && block.AlignRight && line.Length > 0)
                {
                    var mw = MeasureFaceText(metricMeasureFace, line, blockFontSize);
                    lineXPos = Math.Max(marginLeft, marginLeft + contentWidth - mw);
                }
                // Sectioned report: the browser honours a block's text-align, so a
                // right-aligned note pins to the content box's right edge and a
                // centred page footer sits on its middle.
                else if (sectionedReport && (block.AlignRight || block.AlignCenterCss) && line.Length > 0)
                {
                    var mw = MeasureFaceText(
                        block.FontRes == "F2" ? "Arial Bold" : "Arial", line, blockFontSize);
                    lineXPos = Math.Max(marginLeft, block.AlignRight
                        ? marginLeft + contentWidth - mw
                        : marginLeft + (contentWidth - mw) / 2);
                }
                // Print grid: text-align:right pins the measured line to the wrap
                // box's right edge.
                else if (printGrid && block.AlignRight && line.Length > 0)
                {
                    var mw = MeasureFaceText(metricMeasureFace, line, blockFontSize);
                    lineXPos = Math.Max(marginLeft, marginLeft + contentWidth - mw);
                }
                // The inline-body-margin dialect honours ALIGN="center" with the
                // metric face's real advances (its title divs centre on the sheet);
                // the pt-report flow centres its aligned paragraphs the same way.
                else if ((bodyBoxGridDoc || (metricFlow && emailNewsletterDoc))
                         && block.AlignCenterAttr && line.Length > 0)
                {
                    var mw = MeasureFaceText(metricMeasureFace, line, blockFontSize);
                    lineXPos = Math.Max(marginLeft, marginLeft + (contentWidth - mw) / 2);
                }
                // Legacy ALIGN="center" attribute: centre the measured line in the
                // content box (the box is the current float column inside a band).
                else if (!metricFlow && block.AlignCenterAttr && line.Length > 0)
                {
                    var mw = MeasureFaceText(
                        bandFace ?? (string.IsNullOrEmpty(block.FontFamily) ? "Arial" : block.FontFamily!),
                        line, blockFontSize);
                    lineXPos = Math.Max(marginLeft + block.LeftIndent,
                        marginLeft + block.LeftIndent + (contentWidth - block.LeftIndent - mw) / 2);
                }
                var lnX = lineXPos.ToString("F2", invc);
                var lnY = (metricFlow && metricDrop > 0 ? y - metricDrop : y).ToString("F2", invc);

                // CSS background-color: draw a fill rectangle behind this line, spanning the
                // block's content width, BEFORE the text (append order = draw order, so the
                // text lands on top). The rect covers the baseline origin of every fragment on
                // the line so text extraction recovers it as TextState.BackgroundColor. Fill
                // components are emitted at F5 so Color.FromRgb's Round(c*255) round-trips exactly.
                if (block.BackgroundColor is { } bgc)
                {
                    var bgSb = new StringBuilder();
                    bgSb.Append("q ");
                    bgSb.Append($"{(bgc.R / 255.0).ToString("F5", invc)} {(bgc.G / 255.0).ToString("F5", invc)} {(bgc.B / 255.0).ToString("F5", invc)} rg ");
                    // A painted box (tiny background tile × declared CSS size) fills its
                    // whole declared rect once, on the block's first line. The element is
                    // a body-level container, so its box origin sits one UA body margin
                    // inside the content origin on both axes; the fill spans the declared
                    // width × height no matter how the text inside wraps. (The Min clamps
                    // the first-line-box top back to the content top at a page start,
                    // where the flow's entry drop has already been spent.)
                    if (block.BgBoxHeightPt > 0)
                    {
                        if (firstLineOfBlock)
                        {
                            // A bordered painted box (background + width/height + border
                            // rule) fills its BORDER box — declared content + border on
                            // each side — and strokes the border centred on the box edge;
                            // it hangs from the flow's content origin. The borderless
                            // tile box keeps its calibrated one-body-margin inset.
                            var pbBw = block.BorderWidth > 0 && block.BorderColor is not null
                                ? block.BorderWidth : 0;
                            var bbX = marginLeft + block.LeftIndent + (pbBw > 0 ? 0 : UaBodyMarginPt);
                            var bbTop = Math.Min(pageHeight - marginTop, yBeforeBlockLines + lineHeight)
                                - UaBodyMarginPt;
                            var pbW = block.BgBoxWidthPt + 2 * pbBw;
                            var pbH = block.BgBoxHeightPt + 2 * pbBw;
                            bgSb.Append($"{bbX.ToString("F2", invc)} {(bbTop - pbH).ToString("F2", invc)} {pbW.ToString("F2", invc)} {pbH.ToString("F2", invc)} re f ");
                            if (pbBw > 0 && block.BorderColor is { } pbc)
                            {
                                bgSb.Append($"{(pbc.R / 255.0).ToString("F5", invc)} {(pbc.G / 255.0).ToString("F5", invc)} {(pbc.B / 255.0).ToString("F5", invc)} RG {pbBw.ToString("F2", invc)} w ");
                                bgSb.Append($"{(bbX + pbBw / 2).ToString("F2", invc)} {(bbTop - pbH + pbBw / 2).ToString("F2", invc)} {(pbW - pbBw).ToString("F2", invc)} {(pbH - pbBw).ToString("F2", invc)} re S ");
                            }
                            bgSb.Append('Q');
                            page.AddContentStream(Encoding.ASCII.GetBytes(bgSb.ToString()));
                        }
                    }
                    // A floated box's background fills its shrink-to-fit box: exactly
                    // the measured text advance wide, one line box tall, hanging from
                    // the line-box top (metric y).
                    else if (uaFloatW > 0)
                    {
                        bgSb.Append($"{lineXPos.ToString("F2", invc)} {(y - lineHeight).ToString("F2", invc)} {uaFloatW.ToString("F2", invc)} {lineHeight.ToString("F2", invc)} re f Q");
                        page.AddContentStream(Encoding.ASCII.GetBytes(bgSb.ToString()));
                    }
                    else
                    {
                        var bgX = marginLeft + block.LeftIndent;
                        var bgW = contentWidth - block.LeftIndent;
                        bgSb.Append($"{bgX.ToString("F2", invc)} {(y - blockFontSize * 0.25).ToString("F2", invc)} {bgW.ToString("F2", invc)} {(blockFontSize * 1.15).ToString("F2", invc)} re f Q");
                        page.AddContentStream(Encoding.ASCII.GetBytes(bgSb.ToString()));
                    }
                }
                // CSS color: set the fill colour for this line's text (and its list marker)
                // from the block's resolved foreground colour, emitted as its own content
                // stream so it applies across whichever text-emit branch below runs; reset
                // to black afterwards so later content is unaffected. Layout-neutral — only
                // the drawn ink changes.
                var lineForeColor = block.ForeColor is { } fc0 && (fc0.R != 0 || fc0.G != 0 || fc0.B != 0)
                    ? block.ForeColor : null;
                if (lineForeColor is { } fc)
                    page.AddContentStream(Encoding.ASCII.GetBytes(
                        $"{(fc.R / 255.0).ToString("F5", invc)} {(fc.G / 255.0).ToString("F5", invc)} {(fc.B / 255.0).ToString("F5", invc)} rg"));
                // Non-WinAnsi line (CJK, RTL, Cyrillic/Greek/Armenian, mixed-script): the
                // Standard-14 WinAnsi Tf/Tj path collapses these to '?'. Embed a covering
                // Unicode face as a Type0/CID font (deduped once per page) and emit hex glyph
                // ids. A pure Arabic/Hebrew line is written in VISUAL order (shaped Arabic /
                // reversed Hebrew) so it displays right-to-left; a mixed LTR+RTL line gets its
                // RTL segments visualized in place; the absorber logicalizes presentation forms
                // and pure-Hebrew runs back to logical reading order. When no single installed
                // face covers the whole line (e.g. Arabic + CJK on one line), the line is split
                // into per-script segments emitted as consecutive Tf/Tj runs.
                var isRtlLine = IsPureRtl(line);
                var uniSource = isRtlLine ? ToVisualRtl(line)
                    : Text.BidiReorderer.ContainsRtl(line) ? VisualizeMixedRtl(line) : line;
                var cjkFont = NeedsUnicode(line) ? ResolveUnicodeFont(uniSource) : null;
                var cjkTtf = cjkFont?.SourceFontData?.TtfData;
                var cjkName = cjkFont?.FontName ?? "Unicode";
                // RTL documents draw with the same face the right-align measurement
                // used (bold variant for bold blocks), so the anchored edge is exact.
                if (rtlDoc && cjkTtf is not null && PosFace(rtlFace).ttf is { } rtlTtf)
                {
                    cjkTtf = rtlTtf;
                    cjkName = rtlFace;
                }
                if (cjkTtf is not null
                    && page.Dict.Get("Resources") as Core.PdfDictionary is { } cjkRes
                    && cjkRes.Get("Font") as Core.PdfDictionary is { } cjkFontDict)
                {
                    sb.Clear();
                    sb.AppendLine("BT");
                    // Thai mark stacking: a tone mark over an ABOVE vowel seats
                    // higher than the run's baseline — the vowel keeps the
                    // baseline slot, the tone stacks above it (measured on the
                    // reference: +2.42 pt at 11 pt, drawn a small nudge right of
                    // the pen). Such marks are zero-advance, so each becomes its
                    // own raised run at the pen position while the remainder
                    // continues where the prefix ended. Lines without the pair
                    // keep the single-run emit byte-for-byte.
                    var thaiChunks = SplitThaiStackedTones(uniSource);
                    if (thaiChunks is not null)
                    {
                        var penX = lineXPos;
                        foreach (var (chunkText, raised) in thaiChunks)
                        {
                            var (crn, chex) = Text.Type0FontEmbedder.Embed(
                                cjkFontDict, cjkTtf, cjkName, chunkText, stripSpacesInBaseFont: true);
                            var cx = raised ? penX + ThaiToneNudgeEm * blockFontSize : penX;
                            var cy = raised
                                ? (metricFlow && metricDrop > 0 ? y - metricDrop : y) + ThaiToneRaiseEm * blockFontSize
                                : (metricFlow && metricDrop > 0 ? y - metricDrop : y);
                            sb.Append($"/{crn} {blockFontSize.ToString("F1", invc)} Tf ");
                            sb.Append($"1 0 0 1 {cx.ToString("F2", invc)} {cy.ToString("F2", invc)} Tm ");
                            sb.Append('<').Append(System.Convert.ToHexString(chex)).Append("> Tj ");
                            if (!raised)
                                penX += MeasureFaceText(cjkName, chunkText, blockFontSize);
                        }
                    }
                    else
                    {
                        var (rn, hex) = Text.Type0FontEmbedder.Embed(
                            cjkFontDict, cjkTtf, cjkName, uniSource, stripSpacesInBaseFont: true);
                        sb.Append($"/{rn} {blockFontSize.ToString("F1", invc)} Tf ");
                        sb.Append($"1 0 0 1 {lnX} {lnY} Tm ");
                        sb.Append('<').Append(System.Convert.ToHexString(hex)).Append("> Tj ");
                    }
                    sb.AppendLine("ET");
                    page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
                }
                else if (NeedsUnicode(uniSource)
                    && page.Dict.Get("Resources") as Core.PdfDictionary is { } segRes
                    && segRes.Get("Font") as Core.PdfDictionary is { } segFontDict)
                {
                    // Per-segment fallback: consecutive Tj runs advance the text position
                    // naturally, so no per-segment measurement is needed.
                    sb.Clear();
                    sb.AppendLine("BT");
                    sb.Append($"1 0 0 1 {lnX} {lnY} Tm ");
                    foreach (var (segText, segFont) in SegmentByFont(uniSource))
                    {
                        var segTtf = segFont?.SourceFontData?.TtfData;
                        if (segTtf is not null)
                        {
                            var (rn, hex) = Text.Type0FontEmbedder.Embed(
                                segFontDict, segTtf, segFont!.FontName ?? "Unicode", segText, stripSpacesInBaseFont: true);
                            sb.Append($"/{rn} {blockFontSize.ToString("F1", invc)} Tf ");
                            sb.Append('<').Append(System.Convert.ToHexString(hex)).Append("> Tj ");
                        }
                        else
                        {
                            sb.Append($"/{fontRes} {blockFontSize.ToString("F1", invc)} Tf ");
                            sb.Append($"({EscapePdfString(segText)}) Tj ");
                        }
                    }
                    sb.AppendLine("ET");
                    page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
                }
                else if (uaStdSerif || printGrid)
                {
                    // Full-document UA-default flow draws with the Standard-14 serif faces
                    // (Times-Roman / -Bold / -Italic) — serif output with nothing embedded,
                    // so a no-font-family document keeps its Standard-14-only resources.
                    // The print grid draws the Standard-14 Helvetica pair instead.
                    var regRes = printGrid ? "F1" : "F5";
                    var boldRes = printGrid ? "F2" : "F6";
                    var stdRes = block.FontRes == "F2" ? boldRes : block.FontRes == "F3" ? (printGrid ? "F3" : "F7") : regRes;
                    // A <font face> block carries a RESOLVED family: its runs draw
                    // in that face (embedded Type0), bold variant for bold blocks —
                    // the std-serif override serves only family-free text.
                    if (uaStdSerif && block.FontFamily is { } uafFam
                        && PosFace(uafFam + (block.FontRes == "F2" || block.EmBold ? " Bold" : "")).ttf
                            is { } uafTtf
                        && page.Dict.Get("Resources") is Core.PdfDictionary uafRes
                        && uafRes.Get("Font") is Core.PdfDictionary uafDict)
                    {
                        sb.Clear();
                        sb.AppendLine("BT");
                        if (block.BoldRuns is { Count: > 0 } || block.ItalicRuns is { Count: > 0 })
                        {
                            // Mixed-emphasis line in a real face: consecutive
                            // embedded-face segments (regular / Bold / Italic
                            // variants of the block family), the text position
                            // advancing naturally between them. The runs carry
                            // the emphasis truth even when a leading <b>
                            // promoted the whole block's FontRes.
                            bool InFaceRuns(System.Collections.Generic.List<(int Start, int Length)>? runs,
                                int p, ref int upTo)
                            {
                                var inside = false;
                                if (runs is not null)
                                    foreach (var (rs, rl) in runs)
                                    {
                                        var re = rs + rl;
                                        if (p >= rs && p < re) { inside = true; upTo = Math.Min(upTo, re); }
                                        else if (rs > p) upTo = Math.Min(upTo, rs);
                                    }
                                return inside;
                            }
                            sb.Append($"1 0 0 1 {lnX} {lnY} Tm ");
                            int fLineStart = cumChar, fLineEnd = cumChar + line.Length;
                            var fPos = fLineStart;
                            while (fPos < fLineEnd)
                            {
                                var fSegEnd = fLineEnd;
                                var fBold = InFaceRuns(block.BoldRuns, fPos, ref fSegEnd);
                                var fItal = InFaceRuns(block.ItalicRuns, fPos, ref fSegEnd);
                                var fSegText = line.Substring(fPos - fLineStart, fSegEnd - fPos);
                                var fVariant = fBold ? " Bold" : fItal ? " Italic" : "";
                                var fSegTtf = fVariant.Length > 0
                                    ? PosFace(uafFam + fVariant).ttf ?? uafTtf : uafTtf;
                                var (fRn, fHex) = Text.Type0FontEmbedder.Embed(uafDict, fSegTtf,
                                    uafFam.Replace(" ", "") + fVariant.Replace(" ", ""),
                                    fSegText, stripSpacesInBaseFont: true);
                                sb.Append($"/{fRn} {blockFontSize.ToString("F1", invc)} Tf ");
                                sb.Append('<').Append(System.Convert.ToHexString(fHex)).Append("> Tj ");
                                fPos = fSegEnd;
                            }
                        }
                        else
                        {
                            var (uafRn, uafHex) = Text.Type0FontEmbedder.Embed(uafDict, uafTtf,
                                uafFam.Replace(" ", "")
                                + (block.FontRes == "F2" || block.EmBold ? "Bold" : ""),
                                line, stripSpacesInBaseFont: true);
                            sb.Append($"/{uafRn} {blockFontSize.ToString("F1", invc)} Tf ");
                            sb.Append($"1 0 0 1 {lnX} {lnY} Tm ");
                            sb.Append('<').Append(System.Convert.ToHexString(uafHex)).Append("> Tj ");
                        }
                        sb.AppendLine("ET");
                        page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
                    }
                    else
                    {
                    sb.Clear();
                    sb.AppendLine("BT");
                    if ((block.BoldRuns is { Count: > 0 } || block.ItalicRuns is { Count: > 0 })
                        && block.FontRes == "F1")
                    {
                        // Mixed-emphasis line: bold/italic RUNS inside a regular line,
                        // emitted as consecutive Tf/Tj segments (the text position
                        // advances naturally between them). Bold wins on overlap.
                        var italRes = printGrid ? "F3" : "F7";
                        bool InRuns(System.Collections.Generic.List<(int Start, int Length)>? runs,
                            int p, ref int upTo)
                        {
                            var inside = false;
                            if (runs is not null)
                                foreach (var (rs, rl) in runs)
                                {
                                    var re = rs + rl;
                                    if (p >= rs && p < re) { inside = true; upTo = Math.Min(upTo, re); }
                                    else if (rs > p) upTo = Math.Min(upTo, rs);
                                }
                            return inside;
                        }
                        sb.Append($"1 0 0 1 {lnX} {lnY} Tm ");
                        int lineStart = cumChar, lineEnd = cumChar + line.Length;
                        int pos = lineStart;
                        while (pos < lineEnd)
                        {
                            int segEnd = lineEnd;
                            var boldSeg = InRuns(block.BoldRuns, pos, ref segEnd);
                            var italSeg = InRuns(block.ItalicRuns, pos, ref segEnd);
                            var segText = line.Substring(pos - lineStart, segEnd - pos);
                            sb.Append($"/{(boldSeg ? boldRes : italSeg ? italRes : regRes)} {blockFontSize.ToString("F1", invc)} Tf ");
                            sb.Append($"({EscapePdfString(segText)}) Tj ");
                            pos = segEnd;
                        }
                    }
                    else
                    {
                        sb.Append($"/{stdRes} {blockFontSize.ToString("F1", invc)} Tf ");
                        sb.Append($"1 0 0 1 {lnX} {lnY} Tm ");
                        sb.Append($"({EscapePdfString(line)}) Tj ");
                    }
                    sb.AppendLine("ET");
                    page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
                    }
                }
                else if ((uaFlow || escapedAttrDoc || ptReportDoc)
                    && PosFace(
                        (escapedAttrDoc ? "Times New Roman" : metricFace)
                        + (block.FontRes == "F2" || (escapedAttrDoc && block.EmBold) ? " Bold" : "")
                        + (escapedAttrDoc && (block.EmItalic || block.FontRes == "F3") ? " Italic" : "")
                        ).ttf is { } uaTtf
                    && page.Dict.Get("Resources") is Core.PdfDictionary uaRes
                    && uaRes.Get("Font") is Core.PdfDictionary uaFontDict)
                {
                    // UA flow draws with the real serif face (embedded Type0) —
                    // TimesNewRoman/-Bold output rather than Standard-14 Helvetica.
                    // The escaped-attr dialect is serif UA output too (the real
                    // TimesNewRoman faces are embedded — bold-italic included:
                    // <b><i> notes render TimesNewRomanBoldItalic). The pt-report
                    // flow embeds its own body face under that face's name.
                    var (uaRn, uaHex) = Text.Type0FontEmbedder.Embed(uaFontDict, uaTtf,
                        (ptReportDoc ? metricFace.Replace(" ", "") : "TimesNewRoman")
                        + (block.FontRes == "F2" || (escapedAttrDoc && block.EmBold) ? "Bold" : "")
                        + (escapedAttrDoc && (block.EmItalic || block.FontRes == "F3") ? "Italic" : ""),
                        line, stripSpacesInBaseFont: true);
                    sb.Clear();
                    sb.AppendLine("BT");
                    sb.Append($"/{uaRn} {blockFontSize.ToString("F1", invc)} Tf ");
                    sb.Append($"1 0 0 1 {lnX} {lnY} Tm ");
                    sb.Append('<').Append(System.Convert.ToHexString(uaHex)).Append("> Tj ");
                    sb.AppendLine("ET");
                    page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
                }
                else if (block.MaxWidthPt > 0)
                {
                    // Report label/span dialect: drawn in the dialect's own face —
                    // the real Segoe UI embedded when the system provides it (exact
                    // shapes and advances); otherwise each word anchors at its
                    // position in the baked Segoe metrics so the Standard-14 ink
                    // never drifts more than one word's difference.
                    sb.Clear();
                    sb.AppendLine("BT");
                    if (HeaderFooter.TryAppendReportLineOps(sb, docFontDict, line,
                            lineXPos, lnY, blockFontSize, block.FontRes == "F2"))
                    {
                        // drawn kerned and word-anchored in the dialect's own face
                    }
                    else
                    {
                        sb.Append($"/{fontRes} {blockFontSize.ToString("F1", invc)} Tf ");
                        var rwx = lineXPos;
                        foreach (var rword in line.Split(' '))
                        {
                            if (rword.Length > 0)
                            {
                                sb.Append($"1 0 0 1 {rwx.ToString("F2", invc)} {lnY} Tm ");
                                sb.Append($"({EscapePdfString(rword)}) Tj ");
                            }
                            rwx += HeaderFooter.MeasureReportText(rword + " ", blockFontSize,
                                block.FontRes == "F2");
                        }
                    }
                    sb.AppendLine("ET");
                    page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
                }
                else
                {
                    // Justified block: stretch word gaps so every line but the
                    // paragraph's last fills the content box. Word-spacing only —
                    // wrap points and pagination stay identical to the unjustified
                    // layout. Skipped when the crude wrap left implausible slack.
                    var justTw = 0.0;
                    if (block.AlignJustify && lineIdx < lines.Length - 1)
                    {
                        var spaces = 0;
                        foreach (var ch in line) if (ch == ' ') spaces++;
                        if (spaces > 0)
                        {
                            var natural = MeasureFaceText(
                                string.IsNullOrEmpty(block.FontFamily) ? "Arial" : block.FontFamily!,
                                line, blockFontSize);
                            var slack = contentWidth - block.LeftIndent - natural;
                            if (slack > 0 && slack < (contentWidth - block.LeftIndent) * 0.35)
                                justTw = slack / spaces;
                        }
                    }
                    sb.Clear();
                    sb.AppendLine("BT");
                    sb.Append($"/{fontRes} {blockFontSize.ToString("F1", invc)} Tf ");
                    if (justTw > 0) sb.Append($"{justTw.ToString("F3", invc)} Tw ");
                    sb.Append($"1 0 0 1 {lnX} {lnY} Tm ");
                    sb.Append($"({EscapePdfString(line)}) Tj ");
                    if (justTw > 0) sb.Append("0 Tw ");
                    sb.AppendLine("ET");
                    page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
                }

                // CSS ::before marker: emitted after the item text so it is the later fragment.
                // UA-flow <u>/h1-underline: a stroke fs/10 thick, fs/10 under the
                // baseline, spanning the covered advance (probed: 2.4 w at +2.4
                // under the 24 pt worksheet title).
                if (uaStdSerif && block.UnderlineRuns is { Count: > 0 } uaURuns && line.Length > 0)
                {
                    int uLineStart = cumChar, uLineEnd = cumChar + line.Length;
                    foreach (var (us0, ul0) in uaURuns)
                    {
                        var us1 = Math.Max(us0, uLineStart);
                        var ue1 = Math.Min(us0 + ul0, uLineEnd);
                        if (ue1 <= us1) continue;
                        var uPre = MeasureFaceText(metricMeasureFace,
                            line[..(us1 - uLineStart)], blockFontSize);
                        var uSeg = MeasureFaceText(metricMeasureFace,
                            line[(us1 - uLineStart)..(ue1 - uLineStart)], blockFontSize);
                        var uy = (metricFlow && metricDrop > 0 ? y - metricDrop : y)
                            - blockFontSize / 10.0;
                        page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(
                            System.Globalization.CultureInfo.InvariantCulture,
                            $"q 0 0 0 RG {blockFontSize / 10.0:0.##} w {lineXPos + uPre:F2} {uy:F2} m {lineXPos + uPre + uSeg:F2} {uy:F2} l S Q\n")));
                    }
                }
                if (firstLineOfBlock && !string.IsNullOrEmpty(block.Marker) && block.MarkerAfter)
                    EmitMarkerHere();

                // Restore the default black fill so the coloured run does not leak into
                // later content on this page.
                if (lineForeColor is not null)
                    page.AddContentStream(Encoding.ASCII.GetBytes("0 0 0 rg"));

                // A named anchor declared in this block resolves to the page + y of
                // its first rendered line, so a #fragment link lands here.
                if (firstLineOfBlock && block.AnchorNames is { Count: > 0 })
                    foreach (var nm in block.AnchorNames)
                        anchorTargets[nm] = (page, y + lineHeight);
                firstLineOfBlock = false;

                // Inline <a href> ranges overlapping this line get a link rect over
                // their run; resolved to a GoTo/URI action after layout.
                if (block.Anchors is { Count: > 0 })
                {
                    int lineStart = cumChar, lineEnd = cumChar + line.Length;
                    foreach (var (aStart, aLen, url) in block.Anchors)
                    {
                        int ov0 = Math.Max(aStart, lineStart), ov1 = Math.Min(aStart + aLen, lineEnd);
                        if (ov1 > ov0 && !string.IsNullOrEmpty(url))
                        {
                            double x0 = lineX + (ov0 - lineStart) * charW;
                            double x1 = lineX + (ov1 - lineStart) * charW;
                            // The link's description (annotation /Contents, surfaced as its
                            // tooltip) is the anchor's visible text.
                            string? aText = aStart >= 0 && aLen > 0 && aStart + aLen <= block.Text.Length
                                ? block.Text.Substring(aStart, aLen) : null;
                            pendingLinks.Add((page, new Aspose.Pdf.Rectangle(x0, y, x1, y + lineHeight), url, aText));
                        }
                    }
                }
                cumChar += line.Length + 1;   // +1 for the space consumed at the wrap point
                y -= lineHeight;
                if (line.Length > 0) contentPage = page;
            }
            // Close the UA border box: stroke its four edges (centred half a
            // width inside) from the element's box left to the symmetric
            // content right edge, and spend the bottom border width.
            if (uaBorderBox && block.BorderColor is { } ubCol)
            {
                var invc = System.Globalization.CultureInfo.InvariantCulture;
                var ubw = block.BorderWidth;
                var ubLeft = marginLeft + block.LeftIndent;
                var ubRight = pageWidth - marginLeft;
                var ubTop = yBeforeBlockLines;          // border box top (bottom-up)
                var ubBot = y - ubw;
                // border-top-only divider: one rule above the content, no frame
                // and no bottom width to spend.
                var ubStroke = block.BorderTopOnly
                    ? string.Create(invc,
                        $"q {ubCol.R / 255.0:0.###} {ubCol.G / 255.0:0.###} {ubCol.B / 255.0:0.###} RG {ubw:0.##} w ")
                      + string.Create(invc,
                        $"{ubLeft:0.##} {ubTop - ubw / 2:0.##} m {ubRight:0.##} {ubTop - ubw / 2:0.##} l S Q\n")
                    : string.Create(invc,
                        $"q {ubCol.R / 255.0:0.###} {ubCol.G / 255.0:0.###} {ubCol.B / 255.0:0.###} RG {ubw:0.##} w ")
                      + string.Create(invc,
                        $"{ubLeft:0.##} {ubTop - ubw / 2:0.##} m {ubRight:0.##} {ubTop - ubw / 2:0.##} l S ")
                      + string.Create(invc,
                        $"{ubLeft:0.##} {ubBot + ubw / 2:0.##} m {ubRight:0.##} {ubBot + ubw / 2:0.##} l S ")
                      + string.Create(invc,
                        $"{ubLeft + ubw / 2:0.##} {ubTop:0.##} m {ubLeft + ubw / 2:0.##} {ubBot:0.##} l S ")
                      + string.Create(invc,
                        $"{ubRight - ubw / 2:0.##} {ubTop:0.##} m {ubRight - ubw / 2:0.##} {ubBot:0.##} l S Q\n");
                page.AddContentStream(Encoding.ASCII.GetBytes(ubStroke));
                if (!block.BorderTopOnly) y = ubBot;
                contentPage = page;
            }
            // Print-grid heading band: the ".cls h4" border-bottom paints as a filled
            // bar across the wrap box, padding-bottom below the last line box.
            if (printGrid && block.BandColor is { } bandC && block.BandPx > 0)
            {
                var bandH = block.BandPx * 0.75;
                var bandPad = block.BandPadPx * 0.75;
                var bandY = y - bandPad - bandH;
                var bandRect = FormattableString.Invariant(
                    $"q {bandC.R / 255.0:0.###} {bandC.G / 255.0:0.###} {bandC.B / 255.0:0.###} rg {marginLeft + block.LeftIndent:0.##} {bandY:0.##} {contentWidth - block.LeftIndent:0.##} {bandH:0.##} re f Q\n");
                page.AddContentStream(Encoding.ASCII.GetBytes(bandRect));
                y = bandY;
            }
            if (paddingBelow > 0)
            {
                if (y - paddingBelow < marginBottom)
                {
                    page = doc.Pages.Add(pageWidth, pageHeight);
                    EnsureFonts(page, docFontDict);
                    y = pageHeight - marginTop; pendingTopDrop = hasZeroTopMargin;
                }
                else
                {
                    y -= paddingBelow;
                }
            }
            // Close the border-only declared box: stroke its (rounded) frame on the
            // centreline — outer edge at the flow position the box opened at, outer
            // size = declared content + a border width each side — then spend the
            // bottom border. Probed: the 200px radius box strokes w=1 on the 151 pt
            // centreline square [96.5,78.5..247.5,229.5].
            if (uaDeclBox && block.BorderColor is { } dbCol)
            {
                var invc = System.Globalization.CultureInfo.InvariantCulture;
                var bw = block.BorderWidth;
                var bL = marginLeft + block.LeftIndent + bw / 2;
                var bT = yBeforeBlockLines - bw / 2;
                var bR = bL + block.BorderBoxWPt + bw;
                var bB = bT - block.ExplicitHeight - bw;
                // Corner radius on the centreline: the declared radius minus half a
                // border, clamped to half the centreline box.
                var r = Math.Min(Math.Max(0, block.BorderRadiusPt - bw / 2),
                    Math.Min((bR - bL) / 2, (bT - bB) / 2));
                var dbSb = new StringBuilder();
                dbSb.Append(string.Create(invc,
                    $"q {dbCol.R / 255.0:0.###} {dbCol.G / 255.0:0.###} {dbCol.B / 255.0:0.###} RG {bw:0.##} w "));
                if (r > 0)
                {
                    const double k = 0.5522847498; // cubic-bezier circle-arc constant
                    var kr = k * r;
                    dbSb.Append(string.Create(invc, $"{bL + r:0.##} {bT:0.##} m "));
                    dbSb.Append(string.Create(invc, $"{bR - r:0.##} {bT:0.##} l "));
                    dbSb.Append(string.Create(invc, $"{bR - r + kr:0.##} {bT:0.##} {bR:0.##} {bT - r + kr:0.##} {bR:0.##} {bT - r:0.##} c "));
                    dbSb.Append(string.Create(invc, $"{bR:0.##} {bB + r:0.##} l "));
                    dbSb.Append(string.Create(invc, $"{bR:0.##} {bB + r - kr:0.##} {bR - r + kr:0.##} {bB:0.##} {bR - r:0.##} {bB:0.##} c "));
                    dbSb.Append(string.Create(invc, $"{bL + r:0.##} {bB:0.##} l "));
                    dbSb.Append(string.Create(invc, $"{bL + r - kr:0.##} {bB:0.##} {bL:0.##} {bB + r - kr:0.##} {bL:0.##} {bB + r:0.##} c "));
                    dbSb.Append(string.Create(invc, $"{bL:0.##} {bT - r:0.##} l "));
                    dbSb.Append(string.Create(invc, $"{bL:0.##} {bT - r + kr:0.##} {bL + r - kr:0.##} {bT:0.##} {bL + r:0.##} {bT:0.##} c s "));
                }
                else
                {
                    dbSb.Append(string.Create(invc,
                        $"{bL:0.##} {bB:0.##} {bR - bL:0.##} {bT - bB:0.##} re S "));
                }
                dbSb.Append("Q\n");
                page.AddContentStream(Encoding.ASCII.GetBytes(dbSb.ToString()));
                y = bB - bw / 2;
                contentPage = page;
            }
            // A deferred mid-line broken image: the 32×32 placeholder rides at the
            // last line's END — bottom one point above the baseline, rising over the
            // space above — and consumes no flow height of its own.
            if (escapedAttrDoc && block.InlineIconAfter && lines.Length > 0)
            {
                var iconBase = y + lineHeight;
                // The markup's collapsed space between the label and the image
                // survives — the icon starts one space past the text.
                var iconX = marginLeft + block.LeftIndent + MeasureFaceText(
                    block.FontRes == "F2" ? "Times New Roman Bold" : "Times New Roman",
                    lines[^1] + " ", blockFontSize);
                var phName = RegisterPlaceholderIcon(doc, page, ref flowIconRef, masked: true);
                page.AddContentStream(Encoding.ASCII.GetBytes(FormattableString.Invariant(
                    $"q 32 0 0 32 {iconX + 1:0.##} {iconBase + 1:0.##} cm /{phName} Do Q\n")));
                var inD = ParseCssColor("#555555");
                var inL = ParseCssColor("#AAAAAA");
                DrawBox(page, iconX, iconBase + 33, 34, 1, null, 0, inD);
                DrawBox(page, iconX, iconBase, 34, 1, null, 0, inL);
                DrawBox(page, iconX, iconBase, 1, 34, null, 0, inD);
                DrawBox(page, iconX + 33, iconBase, 1, 34, null, 0, inL);
            }
            // Repay the cover box-model drop: the next box starts at this box's
            // bottom edge, not one drop below it.
            if (coverDrop > 0) y += coverDrop;
            y -= block.MarginBottom;
            // a label gives its row's height back — the span beside it advances
            if (block.NoAdvanceY) y = yBeforeBlockLines;
            prevFlowMarginBottom = block.MarginBottom;
            prevFlowLineHeight = lineHeight;
            prevFlowFontSize = blockFontSize;
            uaPrevMarginBottom = block.MarginBottom;
        }
        // Unbalanced band/box markers must not leak a narrowed content box past the flow.
        marginLeft = flowMarginLeft;
        contentWidth = flowContentWidth;

        // Prepend float-table ops (in reverse, so their relative order is preserved) —
        // float content leads its page's content stream, the paint
        // order that the fragment-index tests depend on.
        for (var fi = floatFirstOps.Count - 1; fi >= 0; fi--)
            floatFirstOps[fi].page.PrependContentStream(floatFirstOps[fi].ops);

        // When custom (font-family) faces were embedded, drop the eager Standard-14
        // Helvetica/Courier resources that no content stream actually references, so the
        // document's font set reflects only the faces in use. Conversions that don't use
        // font-family keep the original eager fonts untouched (no behavioural change).
        // Build one RadioButtonField per HTML radio group (by name); each <input> becomes a
        // RadioButtonOptionField kid (circle style, visible border) so it surfaces on
        // Form.Fields after save+reload.
        EmitRadioGroups(doc, radioOptions);

        // Radio groups the GRID tables built through the factory: their options'
        // widgets were placed at the drawn glyphs by the table render pass; the
        // groups themselves surface on Form.Fields here.
        foreach (var (gridRbf, gridRbfPage) in gridRadioPages)
            try { doc.Form.Add(gridRbf, gridRbfPage.Number); }
            catch { /* best-effort radio emission */ }

        // Render the running <header>/<footer> on every page (pulled out of the flow above so
        // they repeat rather than appearing once). Emitted after body content in the reserved
        // top/bottom bands.
        if (!string.IsNullOrEmpty(runHeader) || !string.IsNullOrEmpty(runFooter))
        {
            var invHf = System.Globalization.CultureInfo.InvariantCulture;
            foreach (Page pg in doc.Pages)
            {
                EnsureFonts(pg, docFontDict);
                void EmitRunning(string text, double ty)
                {
                    var s = new StringBuilder();
                    s.AppendLine("BT");
                    s.Append($"/F1 11.0 Tf ");
                    s.Append($"1 0 0 1 {marginLeft.ToString("F2", invHf)} {ty.ToString("F2", invHf)} Tm ");
                    s.Append($"({EscapePdfString(text)}) Tj ");
                    s.AppendLine("ET");
                    pg.AddContentStream(Encoding.ASCII.GetBytes(s.ToString()));
                }
                if (!string.IsNullOrEmpty(runHeader)) EmitRunning(runHeader!, pageHeight - 24);
                if (!string.IsNullOrEmpty(runFooter)) EmitRunning(runFooter!, 24);
            }
        }

        // The hoisted image-only <div id="footer"> logo: page 1, bottom margin,
        // CSS size (see the extraction above; the div was removed from the flow).
        if (page1FooterImgSrc is not null && doc.Pages.Count > 0)
        {
            var footBytes = LoadConverterImage(page1FooterImgSrc, options);
            if (footBytes is not null)
            {
                var fw = page1FooterImgW;
                var fh = page1FooterImgH;
                if ((fw <= 0 || fh <= 0) && TryReadImagePixelSize(footBytes, out var npw, out var nph))
                {
                    if (fw <= 0) fw = npw * 0.75;  // CSS px → pt
                    if (fh <= 0) fh = nph * 0.75;
                }
                if (fw > 0 && fh > 0)
                    try
                    {
                        doc.Pages[1].AddImage(footBytes, new Rectangle(
                            marginLeft, marginBottom, marginLeft + fw, marginBottom + fh));
                    }
                    catch { /* unreadable image: footer logo is skipped */ }
            }
        }

        // Second pass: emit link annotations now that every anchor target's page is
        // known. A #fragment href resolves to the page/y its named anchor rendered
        // on (an internal GoTo); any other href becomes an external URI link.
        foreach (var (lp, rect, url, text) in pendingLinks)
        {
            Aspose.Pdf.Annotations.Annotation? annot = null;
            if (url.StartsWith("#", StringComparison.Ordinal))
            {
                var frag = url.Substring(1);
                if (frag.Length > 0 && anchorTargets.TryGetValue(frag, out var tgt))
                    annot = lp.Annotations.AddLinkAnnotation(rect,
                        new Aspose.Pdf.Annotations.GoToAction(
                            new Aspose.Pdf.Annotations.XYZExplicitDestination(
                                tgt.page.Number, 0, tgt.y, 0)));
                else if (frag.Length == 0 || frag.Equals("top", StringComparison.OrdinalIgnoreCase))
                    // "#" and "#top" are the HTML convention for the document top.
                    annot = lp.Annotations.AddLinkAnnotation(rect,
                        new Aspose.Pdf.Annotations.GoToAction(
                            new Aspose.Pdf.Annotations.XYZExplicitDestination(
                                1, 0, pageHeight - marginTop, 0)));
                // A dangling #fragment (no matching anchor) emits no link.
            }
            else
            {
                annot = lp.Annotations.AddLinkAnnotation(rect, url);
            }
            // The anchor's visible text becomes the link's /Contents (its tooltip).
            if (annot != null && !string.IsNullOrEmpty(text))
                annot.Contents = text;
        }

        // A body background colour paints the page canvas behind everything else: the
        // BODY box's background propagates to the canvas, so it covers the page's whole
        // content box (page margins, not the 6pt UA body margin the left/top defaults
        // bake in) on every page of the conversion. Prepended so the flow's own content
        // — text, rules, cell fills — draws over it.
        if (bodyBackground is { } canvasFill)
        {
            var canvasLeft = marginsExplicit ? marginLeft : 90.0;
            var canvasRight = pageWidth - (marginsExplicit ? marginRight : 72.0);
            var canvasTop = pageHeight - (marginsExplicit ? marginTop : 72.0);
            var canvasBottom = marginsExplicit ? marginBottom : 72.0;
            foreach (var bp in doc.Pages)
                DrawBox(bp, canvasLeft, canvasBottom, canvasRight - canvasLeft,
                    canvasTop - canvasBottom, null, 0, canvasFill, prepend: true);
        }

        if (usedCustomFont) PruneUnusedFonts(doc);

        // Build a logical-structure (tagged) tree when the caller asked for it.
        // The tree mirrors the HTML element hierarchy (headings, paragraphs, lists,
        // figures, links) rather than the flattened layout blocks.
        if (options?.CreateLogicalStructure == true)
            BuildLogicalStructure(doc, html);

        // IsRenderToSinglePage fix-up: the flow above ran unbroken against the
        // coordinate ceiling; the single sheet's height is the whole number of
        // authored content bands the flow fills (never less than one), and the
        // content shifts down from the ceiling onto it.
        if (singlePage && doc.Pages.Count == 1)
        {
            var band = singlePageRealH - marginTop - marginBottom;
            if (band > 0)
            {
                var contentBottomTd = pageHeight - y;
                var bands = Math.Max(1, (int)Math.Ceiling((contentBottomTd - marginTop) / band));
                var finalH = bands * band;
                var shift = pageHeight - finalH;
                var spInv = System.Globalization.CultureInfo.InvariantCulture;
                var spPage = doc.Pages[1];
                spPage.PrependContentStream(Encoding.ASCII.GetBytes(string.Create(spInv,
                    $"q 1 0 0 1 0 {-shift:F2} cm\n")));
                spPage.AddContentStream(Encoding.ASCII.GetBytes("Q\n"));
                spPage.MediaBox = new Rectangle(0, 0, pageWidth, finalH);
            }
        }

        // ScaleToPageWidth: shrink each natural-width page uniformly onto the
        // authored sheet — content pinned at the left page margin and the page
        // top (x' = mL + (x−mL)·s, top-down y' = y·s).
        if (scalePendingS is > 0 and < 1)
        {
            var s = scalePendingS;
            var sInv = System.Globalization.CultureInfo.InvariantCulture;
            var pmL = pageMargin?.Left ?? 0;
            foreach (var sp in doc.Pages)
            {
                sp.PrependContentStream(Encoding.ASCII.GetBytes(string.Create(sInv,
                    $"q {s:F5} 0 0 {s:F5} {pmL * (1 - s):F2} {scaleReqPageH * (1 - s):F2} cm\n")));
                sp.AddContentStream(Encoding.ASCII.GetBytes("Q\n"));
                sp.MediaBox = new Rectangle(0, 0, scaleReqPageW, scaleReqPageH);
            }
        }

        return doc;
    }
}
