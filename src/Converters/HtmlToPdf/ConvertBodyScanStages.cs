using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Every stylesheet rule is scanned for the layout properties that take the document out of the layout-free UA flow.</summary>
    private static void ScanStylesheetForLayoutFreedom(ConvertState cv, HtmlLoadOptions? options)
    {
        // (a sheet scoped under the body's own class: `<body class="X">` with rules rooted at `.X`)
        cv.bodyClassSheet = Regex.Match(cv.html, @"<body\b[^>]*\bclass\s*=\s*[""']?([\w-]+)", RegexOptions.IgnoreCase) is { Success: true } bodyClsM
            && Regex.IsMatch(cv.html, @"\." + Regex.Escape(bodyClsM.Groups[1].Value) + @"\s*[>\s][^{}]*\{", RegexOptions.IgnoreCase);
        foreach (var kv in cv.css)
        {
            // @page / @media at-rules do not drive this converter's layout (the
            // expected render keeps its UA margins under an authored @page —
            // measured: the sheet's 0.6in @page margins render at the
            // standard 96pt content origin), so they cannot disqualify the flow.
            if (kv.Key.TrimStart().StartsWith('@')) continue;
            if (!SelectorUsed(cv.html, kv.Key)) continue;
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
            if (TableScopedSelector(cv.html, cv, cv.bodyAllTables, cv.edgeToEdgePre, kv.Key, kv.Value)) continue;
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
            // An IMG-scoped class — every use of the class in the document sits
            // on an <img> tag — sizes the IMAGE, not the flow: the pure UA
            // flow is kept for the licensing letter whose only
            // stylesheet rule is the broken photo's width/height class (probed:
            // the flow is identical with the rule present, absent, or even
            // carrying a font-size).
            if (kv.Key.Trim() is { Length: > 1 } imgSel && imgSel[0] == '.'
                && !imgSel.Contains(' ') && ImgScopedClass(cv.html, imgSel[1..]))
                continue;
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
            // A WRAPPER container rule is not flow-driving geometry: it names a share of a box
            // the flow already owns. 100% was exempt because a full-width float never floats and
            // its overflow clips nothing; the reference says the rest of this shape is inert too.
            // PROBED (against the reference, one declaration added at a time over the same body): a
            // container declaring width:70%, then display:table, then height:20%, keeps its
            // paragraphs at the SAME 26.94 pt pitch throughout - the UA rhythm survives all three.
            // A percentage HEIGHT resolves to auto and changes nothing at all; display:table adds
            // one line box to the container BOX and leaves the rhythm alone. So none of them makes
            // the sheet layout-declaring, and treating them as such cost the whole UA structure.
            if (kv.Value.TryGetValue("width", out var fwCont)
                && Regex.IsMatch(fwCont.Trim(), @"^[0-9.]+%$")
                && kv.Value.Keys.All(pk => pk is "width" or "float" or "position"
                    or "overflow" or "color" or "background-color" or "background"
                    or "display" or "height" or "min-height"
                    || pk.StartsWith("margin", StringComparison.Ordinal)
                    || pk.StartsWith("padding", StringComparison.Ordinal)))
                continue;
            ScanRuleProperties(cv, kv, options);
            if (!cv.cssLayoutFree && Environment.GetEnvironmentVariable("ASPOSE_TRACE_PROFILE") != "1") break;
        }
    }

    /// <summary>The metric-flow line sum and box grid, the print-grid metric face, the UA MSHTML and edge-to-edge flags.</summary>
    private static void ResolveMetricFlowAndUaMshtml(ConvertState cv, HtmlLoadOptions? options)
    {
        if (!cv.profile.metricFlow && cv.marginsExplicit && !cv.css.ContainsKey("body")
            && Regex.Match(cv.html, @"<body\b[^>]*style\s*=\s*(?:""([^""]*)""|'([^']*)')",
                RegexOptions.IgnoreCase) is { Success: true } ibBodyStyle)
        {
            var ibDecl = ibBodyStyle.Groups[1].Success
                ? ibBodyStyle.Groups[1].Value : ibBodyStyle.Groups[2].Value;
            var ibBox = ParseInlineMarginBox(ibDecl, DefaultBodyFontPt);
            string? ibFam = null;
            var ibFamM = Regex.Match(ibDecl, @"font-family\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
            if (ibFamM.Success) ibFam = FirstFontFamily(ibFamM.Groups[1].Value);
            if (ibFam is null
                && Regex.Match(cv.html, @"<html\b[^>]*style\s*=\s*(?:""([^""]*)""|'([^']*)')",
                    RegexOptions.IgnoreCase) is { Success: true } ibHtmlStyle
                && Regex.Match(ibHtmlStyle.Groups[1].Success
                        ? ibHtmlStyle.Groups[1].Value : ibHtmlStyle.Groups[2].Value,
                    @"font-family\s*:\s*([^;]+)",
                    RegexOptions.IgnoreCase) is { Success: true } ibHtmlFam)
                ibFam = FirstFontFamily(ibHtmlFam.Groups[1].Value);
            if ((ibBox.left > 0 || ibBox.top > 0 || ibBox.right > 0)
                && ibFam is not null && WinMetricsFor(ibFam) is not null)
            {
                cv.profile.metricFlow = true;
                cv.profile.bodyBoxGridDoc = true;
                cv.profile.metricFace = ibFam;
                cv.marginLeft += ibBox.left;
                cv.marginRight += ibBox.right;
                cv.bodyMarT = ibBox.top;
                cv.profile.metricLineSum = HheaLineSumFor(ibFam) ?? 0;
                // These sheets separate blocks with INVALID `</br>` tags — a
                // browser treats each as a line break.
                cv.html = Regex.Replace(cv.html, @"</br\s*>", "<br>", RegexOptions.IgnoreCase);
            }
        }

        // Print-grid dialect: metric layout in the sans body face (the CSS
        // "Helvetica Neue"/Helvetica stack renders with Arial advances), CSS
        // line-height line boxes, standard-14 Helvetica output resources.
        if (cv.profile.printGrid && !cv.profile.metricFlow && WinMetricsFor("Arial") is not null)
        {
            cv.profile.metricFlow = true;
            cv.profile.metricFace = "Arial";
        }

        cv.uaMshtml = cv.css.Count == 0 && !cv.profile.metricFlow
            && Regex.IsMatch(cv.html,
                @"<meta\b[^>]*\bname\s*=\s*[""']?generator\b[""']?[^>]*\bcontent\s*=\s*[""']?MSHTML",
                RegexOptions.IgnoreCase)
            && WinMetricsFor("Times New Roman") is not null;

        cv.edgeToEdgePre = (cv.pageMargin?.IsTouched ?? false) && cv.pageMargin!.HtmlPerSideDefaults
            && cv.pageMargin.LeftTouched && cv.pageMargin.RightTouched
            && !cv.pageMargin.TopTouched && !cv.pageMargin.BottomTouched
            && cv.pageMargin.Left < 1e-9 && cv.pageMargin.Right < 1e-9;
        cv.bodyAllTables = false;
        {
            var bodyM = Regex.Match(cv.html, @"<body\b[^>]*>([\s\S]*?)</body", RegexOptions.IgnoreCase);
            var bodyHtml = bodyM.Success ? bodyM.Groups[1].Value : cv.html;
            var sansT = Regex.Replace(bodyHtml, @"<table\b[\s\S]*?</table\s*>", "",
                RegexOptions.IgnoreCase);
            cv.bodyAllTables = Regex.IsMatch(bodyHtml, @"<table\b", RegexOptions.IgnoreCase)
                && CollapseWs(DecodeEntities(Regex.Replace(sansT, "<[^>]+>", " ")))
                    .Trim().Length == 0;
        }
        cv.cssLayoutFree = true;
    }

    /// <summary>The pinned body width and face, inline-block column rules, and the metric-flow body face and top margin.</summary>
    private static void ResolveBodyWidthAndMetricFace(ConvertState cv, HtmlLoadOptions? options)
    {
        if (!cv.marginsExplicit)
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
                    var bw = Regex.Match(br.Groups[1].Value,
                        @"(?<![\w-])width\s*:\s*([\d.]+\s*(?:px|pt|in|cm|mm))", RegexOptions.IgnoreCase);
                    if (bw.Success && TryParseLength(bw.Groups[1].Value.Replace(" ", "")) is { } bwPt
                        && bwPt > cv.profile.bodyPinnedW)
                        cv.profile.bodyPinnedW = bwPt;
                }
            }
        if (cv.profile.bodyPinnedW > 0 && cv.css.TryGetValue("body", out var bpBody)
            && bpBody.TryGetValue("font-family", out var bpFam))
            foreach (var fam in bpFam.Split(','))
            {
                var f = fam.Trim().Trim('"', '\'');
                if (f.Length > 0 && WinMetricsFor(f) is not null) { cv.bodyPinnedFace = f; break; }
            }

        cv.inlineBlockColRules = false;
        foreach (var ibkv in cv.css)
            if (ibkv.Key.StartsWith('.')
                && ibkv.Value.TryGetValue("display", out var ibd)
                && ibd.Trim().Equals("inline-block", StringComparison.OrdinalIgnoreCase)
                && ibkv.Value.TryGetValue("width", out var ibwv)
                && ((TryParseLength(ibwv) is { } ibwPt2 && ibwPt2 > 0) || PercentFraction(ibwv) > 0))
            { cv.inlineBlockColRules = true; break; }

        // CSS-faithful metric flow (gated): a stylesheet that positions the page itself —
        // a BODY rule carrying a non-zero margin box — marks print-oriented HTML (MSHTML
        // "saved from" reports and the like) whose layout is reproduced from
        // the CSS itself: the body margin box adds to the page margins (top on the first
        // page only), line height is the browser rule round(px·(winAsc+winDesc)/em) with
        // half-leading baselines, MARGIN-LEFT class indents are honored, a <br> is one
        // full line box, and tables use real cellspacing/cellpadding geometry. Every
        // other document keeps the legacy calibrated flow byte-for-byte. Requires the
        // body font family to resolve to a real face (its win metrics drive the model).
        cv.profile.metricFlow = false;
        cv.bodyMarT = 0;
        cv.profile.metricFace = "";
        if (cv.marginsExplicit && cv.css.TryGetValue("body", out var mfBody)
            && mfBody.TryGetValue("margin", out var mfMargin)
            && TryParseCssMarginBox(mfMargin, out var mfBox)
            && (mfBox.top > 0 || mfBox.left > 0 || mfBox.right > 0))
        {
            var mfFam = mfBody.TryGetValue("font-family", out var mff) ? FirstFontFamily(mff) : null;
            if (mfFam is not null && WinMetricsFor(mfFam) is not null)
            {
                cv.profile.metricFlow = true;
                cv.profile.metricFace = mfFam;
                cv.marginLeft += mfBox.left;
                cv.marginRight += mfBox.right;
                cv.bodyMarT = mfBox.top;
            }
        }

        // Inline-styled body-margin sheets (gated): the margin box lives on the
        // BODY tag itself (em longhands) and the family on the <html> tag — no
        // stylesheet body rule exists for the standard metric gate above. The
        // metric flow lays these out with the face's real advances; their em
        // margins resolve against the UA 16px (12 pt) default (the body declares
        // no font-size of its own), and their line boxes pace on the face's hhea
        // line gap (Times New Roman: 17px lines at 11 pt where the win sum's
        // 16px stands a half-line short by mid-page — measured).
        cv.profile.bodyBoxGridDoc = false;
        cv.profile.metricLineSum = 0;
    }

    /// <summary>The body's CSS margin, zero-margin flag, left margin, CSS face and size, and the element-grid face.</summary>
    private static void ResolveBodyMargin(ConvertState cv, HtmlLoadOptions? options)
    {
        cv.profile.bodyZeroMargin = false;
        cv.bodyMargin = null;
        if (cv.css.TryGetValue("body", out var bodyDecls))
            bodyDecls.TryGetValue("margin", out cv.bodyMargin);
        if (cv.bodyMargin is null
            && Regex.Match(cv.html, @"<body\b[^>]*style\s*=\s*(['""])([^'""]*)\1",
                RegexOptions.IgnoreCase) is { Success: true } bodyTagStyle
            && Regex.Match(bodyTagStyle.Groups[2].Value, @"(?<![-\w])margin\s*:\s*([^;]+)",
                RegexOptions.IgnoreCase) is { Success: true } bodyTagMargin)
            cv.bodyMargin = bodyTagMargin.Groups[1].Value;
        // …and a universal reset (`* { margin: 0 }`) zeroes the body margin with
        // everything else — the same statement again.
        if (cv.bodyMargin is null && cv.css.TryGetValue("*", out var starDecls))
            starDecls.TryGetValue("margin", out cv.bodyMargin);
        if (!cv.marginsExplicit && cv.bodyMargin is not null
            // "0", "0px", or an all-zero shorthand list ("0 0 0 0").
            && Regex.IsMatch(cv.bodyMargin.Trim(), @"^0(px)?(\s+0(px)?){0,3}$"))
        {
            cv.profile.bodyZeroMargin = true;
            cv.marginLeft = 90.0;
            cv.marginRight = 90.0;
            cv.marginTop = 72.0;
        }
        // The legacy body attributes (leftmargin / marginwidth = 0) zero the SIDE margins
        // only: the content opens at the page margin, the top keeps its inset (measured:
        // the invoice's body box fills 90..813.75 while its spacer line still seats at 78).
        // Quirks documents only: a strict-doctype mail keeps its content at 96 with the same attribute.
        else if (!cv.marginsExplicit && cv.bodyMargin is null && ReadsInQuirksMode(cv.html)
            && Regex.IsMatch(cv.html, @"<body\b[^>]*\b(leftmargin|marginwidth)\s*=\s*[""']?0\b", RegexOptions.IgnoreCase))
        {
            cv.profile.bodyZeroMargin = true;
            cv.profile.bodySideOnlyZero = true;
            cv.marginLeft = 90.0;
            cv.marginRight = 90.0;
        }

        // A body that declares no margin keeps the UA default one (8 px = 6 pt), so the
        // chain-dialect sheet that grows to its widest grid still opens its content one
        // body margin inside the page margin (probed: 96 + 420 + 90 = 606 for a 560 px
        // grid under a margin-less body; the arm was measured at 90 + the AUTHORED margin).
        cv.bodyMarginLeftPt = cv.marginsExplicit || cv.profile.bodyZeroMargin ? 0.0 : UaBodyMarginPt;
        cv.bodyMarginAuthored = false;
        if (!cv.marginsExplicit && !cv.profile.bodyZeroMargin && cv.css.TryGetValue("body", out var bodyBoxDecls)
            && bodyBoxDecls.TryGetValue("margin", out var bodyMarginV))
        {
            cv.bodyMarginAuthored = true;
            var bodyEmPt = bodyBoxDecls.TryGetValue("font-size", out var bodyEmV)
                && TryParseLength(bodyEmV) is { } bodyEmParsed && bodyEmParsed > 0
                ? bodyEmParsed : DefaultBodyFontPt;
            cv.bodyMarginLeftPt = ChainPadPt(bodyMarginV, bodyEmPt).L;
        }

        // A stylesheet that positions the page itself also owns the document's base
        // text size: its `body { font-size }` seeds the cell grids, where the legacy
        // 11pt default would otherwise stand in. Only the size the BODY rule declares —
        // a table/td rule still wins the cascade inside BuildTableFromHtml.
        cv.profile.bodyCssFontPt = 0.0;
        cv.bodyCssFace = null;
        DetectBodyCssAndGrid(cv.css, cv.html, cv.profile, cv);
        cv.elementGridFace = null;
        if (cv.profile.elementGridDoc && cv.css.TryGetValue("body", out var egBody)
            && egBody.TryGetValue("font-family", out var egFam))
            foreach (var fam in egFam.Split(','))
            {
                var f = fam.Trim().Trim('"', '\'');
                if (f.Length > 0 && WinMetricsFor(f) is not null) { cv.elementGridFace = f; break; }
            }

        // A `body { width: Npx }` rule PINS the canvas: the author sized the page
        // itself, so a wide table overflows rather than growing the sheet, and the
        // grown page is page margin + the body box + page margin exactly
        // (measured: 90 + 570 + 90 = 750 on the fixed-body report).
        // Read off the STYLE BLOCKS like body min-width — a screen-only
        // sheet must not size paper.
        cv.profile.bodyPinnedW = 0;
        cv.bodyPinnedFace = null;
    }

    /// <summary>A default-page document with @font-face styling or an EDGAR filing renders through its own renderer; that document, or null to continue the general flow.</summary>
    private static Document? RenderStyledOrEdgarDocument(ConvertState cv, HtmlLoadOptions? options)
    {
        if (!(cv.pageMargin?.IsTouched ?? false)
            && (cv.pageInfo is null || (cv.pageInfo.Width == 595 && cv.pageInfo.Height == 842))
            && cv.html.IndexOf("@font-face", StringComparison.OrdinalIgnoreCase) >= 0
            && TryParseStyledDataFontDoc(cv.html) is { } styledBody
            && RenderStyledDataFontDoc(styledBody) is { } styledDoc)
        {
            var styledTitle = Regex.Match(cv.html, @"<title[^>]*>(.*?)</title>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (styledTitle.Success)
                styledDoc.Info.Title = DecodeEntities(styledTitle.Groups[1].Value).Trim();
            return styledDoc;
        }

        // EDGAR filing dialect (gated): stylesheet-less inline-styled filings with
        // explicit page-break paragraphs, beveled-rule + h5 page headers and named
        // TOC anchors render through the dedicated line-box-density flow engine.
        // Default page setup only — explicit PageInfo/margins keep the legacy flow.
        if (!(cv.pageMargin?.IsTouched ?? false)
            && (cv.pageInfo is null || (cv.pageInfo.Width == 595 && cv.pageInfo.Height == 842))
            && EdgarHtmlRenderer.IsEdgarFilingDoc(cv.html)
            && EdgarHtmlRenderer.TryConvert(cv.html, options) is { } edgarDoc)
        {
            var edgarTitle = Regex.Match(cv.html, @"<title[^>]*>(.*?)</title>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (edgarTitle.Success)
                edgarDoc.Info.Title = DecodeEntities(edgarTitle.Groups[1].Value).Trim();
            return edgarDoc;
        }
        return null;
    }

    /// <summary>Whether the document renders in the UA flow without a declared font: no metric flow, no MSHTML, no real font declarations, and markup that reads as a plain page.</summary>
    private static void ClassifyUaNoFontDocument(ConvertState cv, HtmlLoadOptions? options)
    {
        cv.uaNoFontDoc = HasNoDeclaredFontFlow(cv) && ReadsAsPlainUaPage(cv, options);
        DetectFieldListDoc(cv);
        if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_PROFILE") == "1")
            Console.Error.WriteLine($"[profile] uaNoFontDoc={cv.uaNoFontDoc} noDeclaredFontFlow={HasNoDeclaredFontFlow(cv)} plainPage={ReadsAsPlainUaPage(cv, options)} metricFlow={cv.profile.metricFlow} mshtml={cv.uaMshtml} cssLayoutFree={cv.cssLayoutFree} brokenBy=[{cv.cssLayoutFreeBrokenBy}] breakers=[{(cv.cssLayoutFreeBreakers is null ? "" : string.Join(" ;; ", cv.cssLayoutFreeBreakers))}] cssRealFamily={cv.cssRealFamily} by=[{cv.cssRealFamilyBy}] uaBodyFace={cv.uaBodyFace} mso={cv.profile.msoFilteredDoc} deadCss={cv.profile.deadExternalCss} absLedger={cv.absSpanLedger} fieldset={cv.fieldsetDoc} customFace={cv.customFontFaceDoc} mozEmail={cv.mozEmailDoc} uaGridBoxes={cv.profile.uaGridBoxes} sectioned={cv.profile.sectionedReport} floatBoth={cv.profile.floatBothSidesDoc} fieldList={cv.profile.fieldListDoc}/{cv.profile.fieldLabelFrac}/'{cv.profile.fieldLabelSuffix}'/{cv.profile.fieldsClass}/{cv.profile.fieldsInsetPt} keys=[{string.Join("|", cv.css.Keys)}]");
    }

    /// <summary>The CSS3 paged-media page names a sheet declares, selector -> page name, read from the
    /// RAW style blocks: the flows that carry named pages (the Word exports) are also the flows whose
    /// parsed rule map is dropped whole, so the parsed map cannot be the source. Only single-selector
    /// rules are read - a name is a page identity, not a cascade.</summary>
    private static Dictionary<string, string> ReadNamedPageRules(string html)
    {
        var named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Only the DOCUMENT'S OWN sheet names pages - the blocks standing before its body opens.
        // An e-mail payload pasted into a paragraph brings its own `<html><head><style>` along,
        // and the names in THAT sheet do not paginate the host (MEASURED: the filtered export whose
        // nested message declares the same `div.WordSection1 { page: WordSection1 }` pages 3 with
        // no break at it, while the host-declared one breaks).
        var bodyAt = Regex.Match(html, @"<body\b", RegexOptions.IgnoreCase);
        var ownSheets = bodyAt.Success ? html[..bodyAt.Index] : html;
        foreach (Match block in Regex.Matches(ownSheets, @"<style[^>]*>([\s\S]*?)</style>", RegexOptions.IgnoreCase))
            foreach (var (selector, body) in InnermostRules(block.Groups[1].Value))
            {
                var page = Regex.Match(body,
                    @"(?<![-\w])page\s*:\s*([A-Za-z_][\w-]*)\s*(?:;|$)", RegexOptions.IgnoreCase);
                if (!page.Success || page.Groups[1].Value.Equals("auto", StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var sel in selector.Split(','))
                {
                    var key = sel.Trim();
                    // (`@page NAME { … }` declares the page box, not an element's page)
                    if (key.Length == 0 || key.StartsWith("@", StringComparison.Ordinal)
                        || key.Contains(' ', StringComparison.Ordinal)) continue;
                    named[key] = page.Groups[1].Value;
                }
            }
        return named;
    }

    /// <summary>The innermost <c>selector { body }</c> blocks of a stylesheet, in
    /// order: each block whose braces enclose no other, with the text since the
    /// brace before it as its selector -- so a rule inside an <c>@media</c> block
    /// reads as its own selector and the block's head is left out. A block with
    /// nothing before its brace names no rule and is skipped.
    ///
    /// The same pairs the pattern <c>([^{}]+)\{([^{}]*)\}</c> matched, read in one
    /// pass over the text: on a nested sheet that pattern retries from every
    /// position of a block's head and backtracks the whole body each time, and on
    /// the .NET Framework interpreter a 1 MB Angular sheet took the better part of
    /// half an hour.</summary>
    private static IEnumerable<(string Selector, string Body)> InnermostRules(string css)
    {
        var previousBrace = -1;
        var position = 0;
        while (true)
        {
            var brace = css.IndexOfAny(Braces, position);
            if (brace < 0) yield break;
            if (css[brace] == '}')
            {
                // A closer: the next selector reads from here.
                previousBrace = brace;
                position = brace + 1;
                continue;
            }
            var next = css.IndexOfAny(Braces, brace + 1);
            if (next < 0) yield break;
            if (css[next] == '{')
            {
                // A block holding another: its head is no rule; read on inside it.
                previousBrace = brace;
                position = brace + 1;
                continue;
            }
            var selector = css.Substring(previousBrace + 1, brace - previousBrace - 1);
            if (selector.Length > 0) yield return (selector, css.Substring(brace + 1, next - brace - 1));
            previousBrace = next;
            position = next + 1;
        }
    }

    private static readonly char[] Braces = { '{', '}' };

    /// <summary>The Word-filtered, custom-font-face, pt-styled fragment, Mozilla e-mail, form and redline dialects are recognised from the markup.</summary>
    private static void DetectDocumentDialects(ConvertState cv, HtmlLoadOptions? options)
    {
        cv.profile.msoFilteredDoc = Regex.IsMatch(cv.html,
                @"<meta\s+name=[""']?Generator[""']?\s+content=[""']?Microsoft Word [^>]*\(filtered[^)>]*\)",
                RegexOptions.IgnoreCase)
            && !Regex.IsMatch(cv.html, @"<table\b", RegexOptions.IgnoreCase);
        // …and whether that filtered page carries the over-wide box its GROWN sheet is grown
        // for. The arm's 721.75 pt sheet and its 1.00 em paragraph margin were both measured on
        // grown pages; a plain filtered page keeps A4 and the ordinary UA 1.12 em.
        cv.profile.msoFilteredGrownSheet = cv.profile.msoFilteredDoc && MsoFilteredHasOverWideBox(cv);
        // The Word 2000-2003 export: the same Word-mail dialect (probed on the list document: hanging
        // labels at margin-left - 18, the sheet's h1 24 / MsoNormal 12 Arial, 16 pt list lines
        // pitched 18.75 by their face), laid on the UA page box (h1 baseline 100.2 = 78 + 0.925 x 24,
        // the 12 pt paragraph wraps at 505).
        cv.profile.wordExportDoc = Regex.IsMatch(cv.html,
                @"<meta\s+name=[""']?Generator[""']?\s+content=[""']?Microsoft Word\b", RegexOptions.IgnoreCase)
            && Regex.IsMatch(cv.html, @"class=[""']?Section1\b", RegexOptions.IgnoreCase)
            && cv.html.IndexOf("mso-list:", StringComparison.OrdinalIgnoreCase) >= 0
            && Regex.IsMatch(cv.html, @"class=[""']?MsoNormal\b", RegexOptions.IgnoreCase);
        cv.profile.wordMailDoc = cv.profile.wordExportDoc
            || cv.html.IndexOf("WordSection1", StringComparison.Ordinal) >= 0
            && cv.html.IndexOf("mso-tab-count", StringComparison.OrdinalIgnoreCase) >= 0
            && Regex.IsMatch(cv.html, @"class=[""']?MsoNormal\b", RegexOptions.IgnoreCase)
            // (…and a Word export that names itself: the Generator meta, a WordSection1 and MsoNormal
            //  paragraphs are the family, a tab stop or a list only witnesses of it - the mail with
            //  the 1289 pt grid carries neither and is laid out on the Word rules all the same)
            || cv.html.IndexOf("WordSection1", StringComparison.Ordinal) >= 0
            && Regex.IsMatch(cv.html, @"<meta\s+name=[""']?Generator[""']?\s+content=[""']?Microsoft Word\b", RegexOptions.IgnoreCase)
            && Regex.IsMatch(cv.html, @"class=[""']?MsoNormal\b", RegexOptions.IgnoreCase);
        cv.profile.namedPageRules = ReadNamedPageRules(cv.html);
        cv.customFontFaceDoc = !cv.cssRealFamily
            && Regex.IsMatch(cv.html, @"@font-face", RegexOptions.IgnoreCase)
            && !Regex.IsMatch(cv.html, @"<!doctype", RegexOptions.IgnoreCase)
            && !Regex.IsMatch(cv.html, @"<table\b", RegexOptions.IgnoreCase);
        // …and NOTHING from those sheets applies — no floats, no class boxes,
        // no typography (the whole report draws in the UA face at
        // the UA sizes). Every downstream consumer sees an empty rule map.
        if (cv.customFontFaceDoc)
            cv.css = new Dictionary<string, Dictionary<string, string>>(
                StringComparer.OrdinalIgnoreCase);
        // The browser-saved email idiom (a `<div dir="ltr">` root with -moz-
        // debris): its inline families are RUN faces the UA flow embeds
        // (Tahoma headers over the Times body), not authored typography that
        // would disqualify the flow.
        // The pt-styled table fragment (a CMS export: no doctype/body/stylesheet,
        // collapse tables whose cells declare pt widths inline): those declared
        // pt widths ARE the column grid — the px-only cell-width read leaves
        // such columns at min-content (a phone column wrapping one character
        // per line).
        cv.profile.ptStyledFragment = cv.css.Count == 0 && !cv.marginsExplicit
            && !Regex.IsMatch(cv.html, @"<!doctype|<body\b", RegexOptions.IgnoreCase)
            // …and not the browser-saved (moz) email — that family keeps its
            // own calibrated dialect (see mozEmailDoc below).
            && !cv.html.Contains("-moz-", StringComparison.OrdinalIgnoreCase)
            && Regex.IsMatch(cv.html, @"<table\b[^>]*border-collapse\s*:\s*collapse",
                RegexOptions.IgnoreCase)
            && Regex.IsMatch(cv.html, @"<td\b[^>]*style\s*=\s*[""'][^""']*width\s*:\s*[\d.]+\s*pt",
                RegexOptions.IgnoreCase);
        // A document with NO sheet whose GRID CELLS author their own
        // typography is not riding the UA defaults: its sheet grows to page
        // margin + the widest table's own ink + page margin, exactly like
        // the dead-stylesheet dialect (probed on the letter grid: a 620 px
        // table of `font-family: Calibri; line-height: 14px` cells lands a
        // 650 pt page, not the calibrated margin + box + margin sheet).
        cv.profile.cellAuthoredTypography = cv.css.Count == 0
            && CellStylesAuthorTypography(cv.html);
        cv.mozEmailDoc = Regex.IsMatch(cv.html, @"\A\s*(?:<!--.*?-->\s*)*<div dir=[""']ltr[""']", RegexOptions.IgnoreCase | RegexOptions.Singleline) && cv.html.Contains("-moz-", StringComparison.OrdinalIgnoreCase);
        // A DataWorks dispatch form (the DWControls workflow page): a form-table
        // of label cells and live controls rendered as a GRID with
        // the controls drawn at their declared pixel boxes.
        cv.profile.dwFormDoc = cv.html.Contains("dwroot/datawrks", StringComparison.OrdinalIgnoreCase)
            && cv.html.Contains("formRleft", StringComparison.OrdinalIgnoreCase);
        // A redline/diff review document (the daisydiff export): a <p>/<span> soup
        // whose spans carry the WHOLE typography inline — Times faces and pt sizes,
        // weights, colors (down to white-painted removed text), strike/underline
        // decorations and the diff markers' dotted underlines.
        cv.profile.redlineDiffDoc = cv.html.Contains("span.diff-tag-", StringComparison.OrdinalIgnoreCase)
            && cv.html.Contains("diff-html-", StringComparison.OrdinalIgnoreCase);
        cv.singleFamilyFaceSwap = false;
    }

    /// <summary>The absolute-span ledger, the layout-free flow's markup checks and the fieldset document's body percent and chrome.</summary>
    private static void ResolveLayoutFreeAndFieldsetFlow(ConvertState cv, HtmlLoadOptions? options)
    {
        cv.absSpanLedger = false;
        if (!cv.cssLayoutFree && !Regex.IsMatch(cv.html, @"<table\b", RegexOptions.IgnoreCase))
        {
            var ledgerAbs = false;
            var ledgerOk = true;
            foreach (var kv in cv.css)
            {
                if (kv.Key.TrimStart().StartsWith('@') || !SelectorUsed(cv.html, kv.Key)) continue;
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
            cv.absSpanLedger = ledgerAbs && ledgerOk;
        }
        cv.fieldsetDoc = false;
        cv.fsBodyPct = 0.0;
        cv.fsBodyChromePt = 0.0;
        if (!cv.profile.metricFlow
            && Regex.IsMatch(cv.html, @"<fieldset\b", RegexOptions.IgnoreCase)
            && Regex.IsMatch(cv.html, @"<legend\b", RegexOptions.IgnoreCase)
            && cv.css.TryGetValue("body", out var fsBodyRule)
            && fsBodyRule.TryGetValue("width", out var fsBodyW)
            && fsBodyW.Trim().EndsWith("%", StringComparison.Ordinal)
            && fsBodyRule.ContainsKey("padding"))
        {
            cv.fieldsetDoc = true;
            cv.fsBodyPct = double.Parse(Regex.Match(fsBodyW, @"[\d.]+").Value,
                System.Globalization.CultureInfo.InvariantCulture) / 100.0;
            var fsPadPt = fsBodyRule.TryGetValue("padding", out var fsPadV)
                && TryParseLength(fsPadV.Trim()) is { } fsPadParsed ? fsPadParsed : 37.5;
            var fsMarPt = fsBodyRule.TryGetValue("margin", out var fsMarV)
                && TryParseLength(fsMarV.Trim()) is { } fsMarParsed ? fsMarParsed : 1.5;
            cv.fsBodyChromePt = fsPadPt + fsMarPt;
        }
    }

    /// <summary>The dead-sheet class: an absolute http(s) stylesheet link, the sp-matrix report's absent relative sheet,
    /// a remote-@import-only style, or a MediaWiki export - see the clauses.</summary>
    private static bool IsDeadExternalCssDoc(ConvertState cv)
        => Regex.IsMatch(cv.html,
            @"<link\b[^>]*rel\s*=\s*[""']?stylesheet[^>]*href\s*=\s*[""']?https?://",
            RegexOptions.IgnoreCase)
            || Regex.IsMatch(cv.html,
                @"<link\b[^>]*href\s*=\s*[""']?https?://[^>]*rel\s*=\s*[""']?stylesheet",
                RegexOptions.IgnoreCase)
            // The sectioned .pdf-page report with the sp-matrix diagram: its
            // relative stylesheet is genuinely absent at conversion time
            // too — the document lays out in pure UA defaults, so
            // it joins the dead-CSS class despite the relative link. Only the
            // default-margin conversion: the report variant whose caller authors
            // page margins was calibrated green on the legacy flow and keeps it.
            || (!cv.marginsExplicit
                && cv.html.Contains("pdf-page", StringComparison.Ordinal)
                && cv.html.Contains("diagram-sp-matrix", StringComparison.Ordinal))
            // …and a <style> whose only rule is a remote @import: the sheet is as
            // unreachable at conversion time as a dead <link> and the document lays
            // out in pure UA defaults (the report corpus's
            // `@import "http://…/style.css"` idiom).
            || Regex.IsMatch(cv.html,
                @"<style\b[^>]*>\s*@import\s+[""']?https?://",
                RegexOptions.IgnoreCase)
            // A MediaWiki export's load.php links are RELATIVE under the page's
            // own URL — remote exactly like an absolute link, so the page
            // draws in the UA serif (its skin sheets restyle nothing
            // the flow draws; the Main-page hides were applied above).
            || cv.wikiExportDoc;

    /// <summary>A dead external stylesheet, a tag-free document, the edge-to-edge flag, the table-free markup and a real font family in the rules.</summary>
    private static void DetectDeadCssAndRealFamily(ConvertState cv, HtmlLoadOptions? options)
    {
        cv.profile.deadExternalCss = IsDeadExternalCssDoc(cv);
        cv.edgeToEdgeDoc = cv.edgeToEdgePre;
        cv.htmlSansTables = Regex.IsMatch(cv.html, @"<font\b|font-family", RegexOptions.IgnoreCase)
            ? Regex.Replace(cv.html, @"<table\b[\s\S]*?</table\s*>", "", RegexOptions.IgnoreCase)
            : cv.html;
        cv.cssRealFamily = false;
        foreach (var kv in cv.css)
            if (!kv.Key.TrimStart().StartsWith('@') && SelectorUsed(cv.html, kv.Key)
                && !TableScopedSelector(cv.html, cv, cv.bodyAllTables, cv.edgeToEdgePre, kv.Key, kv.Value)
                // A CLASS-scoped family rule (`p.subheader2 { font-family:
                // Calibri }`) rides its classed blocks like a tag rule rides its
                // element (the block-rule applier styles them) — the rest of the
                // document keeps UA structure, so it does not disqualify.
                && !Regex.IsMatch(kv.Key.Trim(), @"^[a-zA-Z]*[1-6]?\.[\w-]+$")
                // …and an ID-scoped one rides its element the same way (`#divGeral { font-family:
                // Verdana; font-size: 14px }` styles the wrapper's blocks and its grid's cells;
                // probed: the title VerdanaBold 10.5 at the UA top, the cells Verdana 10.5).
                && !Regex.IsMatch(kv.Key.Trim(), @"^[a-zA-Z]*#[\w-]+$")
                && !IsRunScopedFamilySelector(kv.Key)
                && kv.Value.TryGetValue("font-family", out var ffDecl)
                && FirstFontFamily(ffDecl) is { } ffName
                // A comma INSIDE the single (quoted) name is the junk-family
                // idiom — no real face carries one, whatever the repository's
                // lenient lookup happens to match it to.
                && !ffName.Contains(',')
                && WinMetricsFor(ffName) is not null)
            { cv.cssRealFamily = true; cv.cssRealFamilyBy = kv.Key.Trim() + " { font-family: " + ffDecl + " }"; break; }
        cv.uaBodyFace = null;
        cv.uaBodyFontPt = 0;
        cv.uaBodyFaceFromAttr = false;
    }

    /// <summary>Every property of one stylesheet rule is checked for the layout declarations that end the layout-free flow.</summary>
    private static void ScanRuleProperties(ConvertState cv, KeyValuePair<string, Dictionary<string, string>> kv, HtmlLoadOptions? options)
    {
        // a keyframe step (`0% { opacity: 0 }`, `from`, `to`) animates nothing on paper: the
        // whole rule is skipped, whatever it declares (a per-RULE fact, so it is decided once
        // here, not once per property)
        if (Regex.IsMatch(kv.Key.Trim(), @"^(?:\d+(?:\.\d+)?%|from|to)(?:\s*,\s*(?:\d+(?:\.\d+)?%|from|to))*$",
                RegexOptions.IgnoreCase)) return;
        foreach (var prop in kv.Value.Keys)
        {
            if (!ScanRulePropertyForLayout(cv, kv, prop)) break;
        }
    }

    /// <summary>The markup without its script and style bodies, computed once per document.</summary>
    private static string MarkupSansScripts(ConvertState cv)
        => cv.htmlSansScripts ??= Regex.Replace(cv.html, @"<(script|style)\b[\s\S]*?</\1\s*>", "", RegexOptions.IgnoreCase);

    /// <summary>Whether every CLASS the selector names sits on a VISIBLE element with text: a class the
    /// markup never carries, one carried only by `display: none` elements (a saved page's search
    /// suggestions box) or only by empty elements (a sidebar's icon span) styles no ink on the sheet.
    /// A selector naming only ids or tags is taken to apply - the form page whose menu ids its script
    /// builds keeps its calibrated flow.</summary>
    private static bool SelectorNamesMarkup(string html, string selector)
    {
        foreach (Match m in Regex.Matches(selector, @"\.([A-Za-z_][\w-]*)"))
        {
            var name = Regex.Escape(m.Groups[1].Value);
            var visible = false;
            foreach (Match el in Regex.Matches(html,
                @"<(\w+)\b[^>]*\bclass\s*=\s*[""'][^""']*(?<![\w-])" + name + @"(?![\w-])[^>]*>", RegexOptions.IgnoreCase))
            {
                if (Regex.IsMatch(el.Value, @"display\s*:\s*none", RegexOptions.IgnoreCase)) continue;
                var close = html.IndexOf("</" + el.Groups[1].Value, el.Index + el.Length, StringComparison.OrdinalIgnoreCase);
                var inner = close < 0 ? "" : html.Substring(el.Index + el.Length, close - el.Index - el.Length);
                if (Regex.Replace(inner, "<[^>]*>", "").Trim().Length > 0) { visible = true; break; }
            }
            if (!visible) return false;
        }
        return true;
    }

    /// <summary>The selector is a bare `#id` or `.class` (optionally tag-qualified) whose element opens
    /// directly with a table - a grid host.</summary>
    private static bool IsGridHostSelector(string html, string selector)
    {
        var m = Regex.Match(selector.Trim(), @"^[a-zA-Z]*#([\w-]+)$");
        if (!m.Success) return false;
        var name = Regex.Escape(m.Groups[1].Value);
        return Regex.IsMatch(html,
            @"<div\b[^>]*\bid\s*=\s*[""']?" + name + @"(?:\s|[""'>])[^>]*>\s*<table\b",
            RegexOptions.IgnoreCase);
    }

    /// <summary>A `margin` shorthand whose left and right values are zero (two to four values).</summary>
    private static bool MarginShorthandSidesZero(string value)
    {
        var parts = value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 2 or > 4) return false;
        var right = parts[1];
        var left = parts.Length == 4 ? parts[3] : parts[1];
        return IsZeroLength(right) && IsZeroLength(left);
    }

    /// <summary>A `margin` shorthand whose top value is negative and whose left value (the fourth of
    /// four, else the second of two or three, else the single value) is zero or auto.</summary>
    private static bool MarginShorthandPullsUp(string value)
    {
        var parts = value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 2 or > 4) return false;
        // (the length reader declines a non-positive value by design: the sign is read here)
        if (!parts[0].StartsWith('-') || TryParseLength(parts[0].Substring(1)) is not > 0) return false;
        var left = parts.Length == 4 ? parts[3] : parts[1];
        return left.Equals("auto", StringComparison.OrdinalIgnoreCase) || IsZeroLength(left);
    }

    /// <summary>No metric flow, no MSHTML, and no real font declaration that would take the document out of the UA default face.</summary>
    private static bool HasNoDeclaredFontFlow(ConvertState cv)
    {
        return !cv.profile.metricFlow && !cv.uaMshtml
        // A Word-filtered page's Mso style-definitions sheet (the MsoNormal
        // margin resets, the hyperlink colours, the @page section) is part
        // of the filtered idiom the UA flow renders — it does not disqualify.
        && (cv.cssLayoutFree || cv.profile.msoFilteredDoc || cv.absSpanLedger || cv.fieldsetDoc
            || cv.customFontFaceDoc)
        // A <font> tag only affects the flow through its FACE/SIZE attributes — a
        // bare <font color="…"> leaves the document font-family-free. A body
        // rule pinning a face at the UA base size keeps UA structure (the
        // uaBodyFace arm above) and stays in — and a Word-filtered page's
        // Mso sheet families are the filtered idiom itself, applied inline.
        && (!cv.cssRealFamily || cv.profile.msoFilteredDoc || cv.uaBodyFace is not null)
        && (cv.profile.msoFilteredDoc || cv.mozEmailDoc
            // A dead-stylesheet document keeps the UA flow whatever inline
            // families its spans carry: its bulk draws in the
            // UA serif and honours the odd styled span per run (measured on
            // the 60-page report: 2524 Times runs, 11 Arial).
            || cv.profile.deadExternalCss
            // …and an inline family naming the UA base face ITSELF (the
            // saved-document idiom that spells `font-family: 'Times New
            // Roman'` on every span) styles nothing the UA flow would not
            // already draw — only a DIFFERENT face disqualifies. The scan
            // walks whole quoted style attributes so a family value QUOTED
            // with the other quote kind still parses (a value capture that
            // stopped at the quote read `font-family:"Angsana New"` as "no
            // family here" and let the differently-faced document through);
            // a family that fails to parse disqualifies like any other
            // face, and so does TABLE-element styling that leaked through
            // the non-greedy nested-table strip — the allowance covers only
            // flow typography (the p/span soup), never grid cells.
            || !InlineFamiliesDisqualify(cv, cv.cssLayoutFree, cv.htmlSansTables)
                // …and the allowance covers TEXT statements only: a document
                // whose flow spells INLINE families AND places images
                // paginates by its image boxes — such a document stays
                // on the calibrated flow (the saved-statement
                // corpora the allowance was measured on carry no <img>).
                // Sheet rules stay out of the test — only style attributes
                // mark the span-typed statement idiom.
                && !(Regex.IsMatch(cv.html, @"<img\b", RegexOptions.IgnoreCase)
                     && Regex.Matches(cv.htmlSansTables,
                             @"\bstyle\s*=\s*(?:""(?<s>[^""]*)""|'(?<s>[^']*)')",
                             RegexOptions.IgnoreCase)
                         .Cast<Match>().Any(sm => Regex.IsMatch(sm.Groups["s"].Value,
                             @"font-family\s*:", RegexOptions.IgnoreCase)
                             // (the body tag's own attribute is the flow's typography, not a statement span)
                             && !(cv.uaBodyFaceFromAttr && IsBodyTagStyleAttr(cv.htmlSansTables, sm.Index)))));
    }

    /// <summary>The style attribute at <paramref name="attrIndex"/> sits on the body tag itself.</summary>
    private static bool IsBodyTagStyleAttr(string markup, int attrIndex)
    {
        var lt = markup.LastIndexOf('<', attrIndex);
        return lt >= 0 && Regex.IsMatch(markup.Substring(lt, Math.Min(6, markup.Length - lt)), @"^<body\b", RegexOptions.IgnoreCase);
    }

    /// <summary>The markup reads as a plain page: any fragment or document without a declared font flow, no styled table header and no Excel export classes, with the UA serif installed.</summary>
    private static bool ReadsAsPlainUaPage(ConvertState cv, HtmlLoadOptions? options)
    {
        // There is no bare-fragment mode: a fragment without html/head/body
        // wrappers or a stylesheet is laid out by the same UA model as a
        // full document (probed on the source renderer: html/body/doctype/
        // style wrappers give byte-identical output; the line-break note and
        // the threading page reproduce their references pixel-exact on it).
        // <font size=…>/<font face=…> style flow text inside the UA flow
        // itself (the ladder sizes; a resolvable face embeds for its runs).
        return
        // Table documents take the UA flow WITH their tables — the metric table
        // renderer draws them as real grids, the same model the expected render
        // applies (H-4: bordered cellspacing grids, centred tables, bgcolor
        // cells all render as authored, never as flattened text). Exception:
        // an unresolved RELATIVE stylesheet is a packaging gap (the sheet was
        // present when the page was authored — see the dead-CSS rule above),
        // so such a table document keeps the legacy calibrated flow.
        (!Regex.IsMatch(cv.html, @"<table\b", RegexOptions.IgnoreCase)
            || cv.profile.deadExternalCss
            || !Regex.Matches(cv.html,
                    @"<link\b[^>]*rel\s*=\s*[""']?stylesheet[^>]*href\s*=\s*[""']?([^""'\s>]+)",
                    RegexOptions.IgnoreCase)
                .Cast<Match>().Any(lm =>
                {
                    var relHref = lm.Groups[1].Value;
                    if (relHref.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        return false;
                    // A relative sheet that does not EXIST under the caller's
                    // base path is dead for EVERY renderer — the source
                    // renderer lays such a document out in pure UA defaults
                    // too. Only a sheet that is actually present (but failed
                    // to inline) marks the packaging gap that keeps the
                    // legacy calibrated flow. Inline SVGs no longer hold a
                    // document back: the diagram arm re-anchors its canvas
                    // to the UA entry (DgUaEntryLiftPt), and the Arabic
                    // diagram report's TEXT draws in the UA serif
                    // sizes only the UA flow produces (probed: h2 18/h6 9/
                    // body 12 against the calibrated flow's 15/10/9.4).
                    if (!string.IsNullOrEmpty(options?.BasePath))
                    {
                        try
                        {
                            if (!System.IO.File.Exists(System.IO.Path.Combine(
                                    options!.BasePath!, relHref.TrimStart('/', '\\'))))
                                return false;
                        }
                        catch { /* malformed href: treat as unresolved-present */ }
                    }
                    return true;
                }))
        // A sheet that pins `thead { display: table-header-group }` authors a
        // PAGINATED report — its header rows repeat on every page a table
        // spans, a behaviour the metric grid does not model; such documents
        // keep the legacy calibrated flow.
        && !(cv.css.TryGetValue("thead", out var theadRule)
            && theadRule.TryGetValue("display", out var theadDisp)
            && theadDisp.Contains("table-header-group", StringComparison.OrdinalIgnoreCase))
        // Excel-export markup (the xlNN cell classes) is its own dialect —
        // the legacy flow was calibrated on it, cell fonts and all. Only a
        // FRAGMENT that lost its Excel stylesheet — dead xl names, no rule
        // definitions, its typography carried by <font> faces — renders pure
        // UA (the expected render lays it out in UA defaults + those faces).
        // A dead-xl document WITHOUT <font> markup is an authored export
        // (inline pixel grids, anchor cells) and keeps the calibrated flow.
        && (cv.profile.deadExternalCss
            // A document whose stylesheet is DEAD renders pure UA whatever
            // Excel-class residue rides its markup (the report corpus's
            // xl24-classed paragraphs under a dead @import).
            || !(Regex.IsMatch(cv.html, @"class\s*=\s*[""']?xl\d+", RegexOptions.IgnoreCase)
             && (Regex.IsMatch(cv.html, @"\.xl\d+\s*[,{]", RegexOptions.IgnoreCase)
                 || !Regex.IsMatch(cv.html, @"<font\b", RegexOptions.IgnoreCase))))
        && WinMetricsFor("Times New Roman") is not null;
    }

    /// <summary>The UA body face: a body or div rule naming an installed face at the UA base size, unless a real family is declared outside the body.</summary>
    private static void ResolveUaBodyFace(ConvertState cv, HtmlLoadOptions? options)
    {
        var realFamilyOutsideBody = RealFamilyDeclaredOutsideBody(cv, out var bodyClassFace, out var bodyClassRule);
        const double uaBasePt = 12.0;   // the UA 16px root, in pt
        if (!realFamilyOutsideBody
            && cv.css.TryGetValue("body", out var uaBodyRule)
            && uaBodyRule.TryGetValue("font-family", out var uaBodyFam)
            && FirstFontFamily(uaBodyFam) is { } uaBodyName && !uaBodyName.Contains(',')
            && WinMetricsFor(uaBodyName) is not null
            // an explicit UA-base size, or none at all (the rule pins only
            // the face — the size stays the UA 16px root)
            && (!uaBodyRule.TryGetValue("font-size", out var uaBodyFsV)
                || (TryParseLength(uaBodyFsV) is { } uaBodyFsPt
                    && Math.Abs(uaBodyFsPt - uaBasePt) < 0.01)))
            cv.uaBodyFace = uaBodyName;
        // A sheet whose only inherited face is the UNIVERSAL rule states the flow's face
        // there: `* { font-family: Arial }` is the document's face exactly as a body rule
        // would be (measured on the invoice and the narrative sheet, whose runs the reference
        // draws in Arial throughout). A body rule is nearer and keeps precedence above.
        if (cv.uaBodyFace is null && !realFamilyOutsideBody
            && RootFamilyRule(cv.css) is { } uaStarRule
            && uaStarRule.TryGetValue("font-family", out var uaStarFam)
            && FirstFontFamily(uaStarFam) is { } uaStarName && !uaStarName.Contains(',')
            && WinMetricsFor(uaStarName) is not null
            && (!uaStarRule.TryGetValue("font-size", out var uaStarFsV)
                || (TryParseLength(uaStarFsV) is { } uaStarFsPt
                    && Math.Abs(uaStarFsPt - uaBasePt) < 0.01)))
            cv.uaBodyFace = uaStarName;
        // A document whose BODY attribute states a resolvable face and a size keeps the UA
        // structure at that face and size: the sheet-less fragment's h3 sets 1.17 em of the
        // 8 pt body in bold Arial, its lines 8 pt Arial (measured); and the same attribute
        // under a stylesheet beats the sheet's own body rule, as the cascade says it must
        // (MEASURED, the no-doctype evaluation form: `body { font-size: .75em }` in the sheet,
        // `style="font-size:11pt; font-family:arial"` on the tag - every body run and every
        // em heading resolves off the 11 pt, never the .75 em).
        if (cv.uaBodyFace is null && !realFamilyOutsideBody
            && Regex.Match(cv.html, @"<body\b[^>]*style\s*=\s*(['""])([^'""]*)\1", RegexOptions.IgnoreCase)
                is { Success: true } uaBodyAttr
            && Regex.Match(uaBodyAttr.Groups[2].Value, @"font-family\s*:\s*([^;]+)", RegexOptions.IgnoreCase)
                is { Success: true } uaAttrFam
            && Regex.Match(uaBodyAttr.Groups[2].Value, @"font-size\s*:\s*([^;]+)", RegexOptions.IgnoreCase)
                is { Success: true } uaAttrFs
            && FirstFontFamily(uaAttrFam.Groups[1].Value) is { } uaAttrName && !uaAttrName.Contains(',')
            && WinMetricsFor(uaAttrName) is not null
            && TryParseLength(uaAttrFs.Groups[1].Value.Trim()) is { } uaAttrPt && uaAttrPt > 0)
        {
            cv.uaBodyFace = uaAttrName;
            cv.uaBodyFontPt = uaAttrPt;
            cv.uaBodyFaceFromAttr = true;
        }
        // …and the body's own class rule, at whatever size it states (MEASURED, the change-control
        // print page: `.ev-print { Arial; 9.5pt }` draws every body run Arial 9.5 and the sheet's
        // h1 ArialBold 15 in the UA structure).
        if (cv.uaBodyFace is null && !realFamilyOutsideBody && !cv.css.ContainsKey("body") && bodyClassFace is not null)
        {
            cv.uaBodyFace = bodyClassFace;
            if (bodyClassRule!.TryGetValue("font-size", out var bcFs) && TryParseLength(bcFs.Trim()) is { } bcPt && bcPt > 0)
                cv.uaBodyFontPt = bcPt;
            cv.uaBodyFaceFromAttr = true;
        }
        // The same probe read through a DIV rule: a document whose only
        // family declaration is a div-scoped STACK, sized at the UA base
        // (no font-size at all), takes the stack's first RESOLVABLE member
        // as its face — the expected render walks the stack (calibri out of
        // "AvenirNext LT Com Regular", "Helvetica Neue", calibri) and keeps
        // the UA structure under it.
        if (cv.uaBodyFace is null && !realFamilyOutsideBody && !cv.css.ContainsKey("body")
            && cv.css.TryGetValue("div", out var uaDivRule)
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
                { cv.uaBodyFace = cand; break; }
            }
    }

    /// <summary>A selector that styles EVERY element of the document: the universal rule, or
    /// the universal rule scoped to the body (`body *`). Both state the root's own inherited
    /// typography, which is the level a `body` rule states - not a scoped family.</summary>
    private static bool IsRootFamilySelector(string selector)
    {
        var s = Regex.Replace(selector.Trim(), @"\s+", " ");
        return s == "*" || s.Equals("body *", StringComparison.OrdinalIgnoreCase)
            || s.Equals("body*", StringComparison.OrdinalIgnoreCase)
            || s.Equals("html *", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The root rule that names the document's inherited face, if the sheet has one.</summary>
    private static Dictionary<string, string>? RootFamilyRule(Dictionary<string, Dictionary<string, string>> css)
    {
        foreach (var kv in css)
            if (IsRootFamilySelector(kv.Key) && kv.Value.ContainsKey("font-family")) return kv.Value;
        return null;
    }
}
