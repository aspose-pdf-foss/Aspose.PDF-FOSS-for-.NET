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
        ApplyPrintGridBase(cv.css, cv.profile, ref cv.html, ref cv.marginLeft, ref cv.marginRight, ref cv.marginTop, ref cv.printGridLineFactor);

        // Styled-class data-font flow (gated): a stylesheet that embeds its faces as
        // data: URIs and styles a flat classed-paragraph body (the EDGAR TSR report
        // shape) renders through the styled HTML engine. Default page
        // setup only — explicit PageInfo/margins keep the legacy flow.
        if (!(cv.pageMargin?.IsTouched ?? false)
            && (cv.pageInfo is null || (cv.pageInfo.Width == 595 && cv.pageInfo.Height == 842))
            && cv.html.IndexOf("@font-face", StringComparison.OrdinalIgnoreCase) >= 0
            && TryParseStyledDataFontDoc(cv.html, out var styledBody)
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

        // body{margin:0}: the default 90pt side margins / 72pt content top
        // apply verbatim — the usual defaults (96/89) bake in the browser's 8px body
        // margin and the default first-baseline drop, which this page has switched off.
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

        cv.bodyMarginLeftPt = 0.0;
        if (!cv.marginsExplicit && !cv.profile.bodyZeroMargin && cv.css.TryGetValue("body", out var bodyBoxDecls)
            && bodyBoxDecls.TryGetValue("margin", out var bodyMarginV))
        {
            var bodyEmPt = bodyBoxDecls.TryGetValue("font-size", out var bodyEmV)
                && TryParseLength(bodyEmV, out var bodyEmParsed) && bodyEmParsed > 0
                ? bodyEmParsed : DefaultBodyFontPt;
            cv.bodyMarginLeftPt = ChainPadPt(bodyMarginV, bodyEmPt).L;
        }

        // A stylesheet that positions the page itself also owns the document's base
        // text size: its `body { font-size }` seeds the cell grids, where the legacy
        // 11pt default would otherwise stand in. Only the size the BODY rule declares —
        // a table/td rule still wins the cascade inside BuildTableFromHtml.
        cv.profile.bodyCssFontPt = 0.0;
        cv.bodyCssFace = null;
        DetectBodyCssAndGrid(cv.css, cv.html, cv.profile, ref cv.bodyCssFace);
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
                    if (bw.Success && TryParseLength(bw.Groups[1].Value.Replace(" ", ""), out var bwPt)
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
                && TryParseLength(ibwv, out var ibwPt2) && ibwPt2 > 0)
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
                // the flow — they do not disqualify it. A class margin-left
                // indents its block in the flow the same way.
                if (prop is "margin-top" or "margin-bottom" or "margin-left") continue;
                // A max-width at or beyond the UA content band cannot clamp
                // anything on this sheet — it is inert for the flow.
                if (prop == "max-width" && TryParseLength(kv.Value[prop].Trim(), out var mwInert)
                    && mwInert >= cv.pageWidth - 96.0 - 72.0) continue;
                cv.cssLayoutFree = false; break;
            }
            if (!cv.cssLayoutFree) break;
        }
        // Unresolved external stylesheets (InlineLinkedStylesheets leaves the <link>
        // tags of stylesheets it could not fetch): the converter falls back to
        // pure UA defaults for such documents — tables included, they lay out through
        // the metric table renderer. Only ABSOLUTE http(s) links qualify: those are
        // unreachable at render time by design, whereas an unresolved RELATIVE
        // link is a packaging gap — the sheet was present when the document
        // was authored, so the document must keep the legacy calibrated flow.
        cv.profile.deadExternalCss = Regex.IsMatch(cv.html,
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
        cv.tagFreeDoc = !Regex.IsMatch(cv.html, @"<[A-Za-z/!?]");
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
                && kv.Value.TryGetValue("font-family", out var ffDecl)
                && FirstFontFamily(ffDecl) is { } ffName
                // A comma INSIDE the single (quoted) name is the junk-family
                // idiom — no real face carries one, whatever the repository's
                // lenient lookup happens to match it to.
                && !ffName.Contains(',')
                && WinMetricsFor(ffName) is not null)
            { cv.cssRealFamily = true; break; }
        cv.uaBodyFace = null;
        {
            var realFamilyOutsideBody = false;
            foreach (var kv in cv.css)
                if (!kv.Key.TrimStart().StartsWith('@') && SelectorUsed(cv.html, kv.Key)
                    && !TableScopedSelector(cv.html, cv, cv.bodyAllTables, cv.edgeToEdgePre, kv.Key, kv.Value)
                    && !kv.Key.Trim().Equals("body", StringComparison.OrdinalIgnoreCase)
                    // a bare element-TAG rule's resolvable face rides its
                    // blocks (h6 { font-family: Verdana } styles the h6s, not
                    // the flow) — it does not disqualify the UA structure
                    && !(Regex.IsMatch(kv.Key.Trim(), @"^[a-zA-Z]+[1-6]?$")
                         && kv.Value.TryGetValue("font-family", out var tagFamDecl)
                         && FirstFontFamily(tagFamDecl) is { } tagFamName
                         && WinMetricsFor(tagFamName) is not null)
                    // …and a class-scoped rule rides its classed blocks the
                    // same way (see cssRealFamily above).
                    && !Regex.IsMatch(kv.Key.Trim(), @"^[a-zA-Z]*[1-6]?\.[\w-]+$")
                    && kv.Value.TryGetValue("font-family", out var nbDecl)
                    && FirstFontFamily(nbDecl) is { } nbName && !nbName.Contains(',')
                    && WinMetricsFor(nbName) is not null)
                { realFamilyOutsideBody = true; break; }
            const double uaBasePt = 12.0;   // the UA 16px root, in pt
            if (!realFamilyOutsideBody
                && cv.css.TryGetValue("body", out var uaBodyRule)
                && uaBodyRule.TryGetValue("font-family", out var uaBodyFam)
                && FirstFontFamily(uaBodyFam) is { } uaBodyName && !uaBodyName.Contains(',')
                && WinMetricsFor(uaBodyName) is not null
                // an explicit UA-base size, or none at all (the rule pins only
                // the face — the size stays the UA 16px root)
                && (!uaBodyRule.TryGetValue("font-size", out var uaBodyFsV)
                    || (TryParseLength(uaBodyFsV, out var uaBodyFsPt)
                        && Math.Abs(uaBodyFsPt - uaBasePt) < 0.01)))
                cv.uaBodyFace = uaBodyName;
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
                && TryParseLength(fsPadV.Trim(), out var fsPadParsed) ? fsPadParsed : 37.5;
            var fsMarPt = fsBodyRule.TryGetValue("margin", out var fsMarV)
                && TryParseLength(fsMarV.Trim(), out var fsMarParsed) ? fsMarParsed : 1.5;
            cv.fsBodyChromePt = fsPadPt + fsMarPt;
        }
        // Word-filtered TEXT pages (meta Generator "Microsoft Word N (filtered)",
        // no tables — the tabled forms take the MsoForm dialect): their styling is
        // all inline (pt sizes, % line-heights, span faces), which the UA flow
        // renders directly, so the inline-face disqualifier below does not apply.
        cv.profile.msoFilteredDoc = Regex.IsMatch(cv.html,
                @"<meta\s+name=[""']?Generator[""']?\s+content=[""']?Microsoft Word [^>]*\(filtered[^)>]*\)",
                RegexOptions.IgnoreCase)
            && !Regex.IsMatch(cv.html, @"<table\b", RegexOptions.IgnoreCase);
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
        cv.uaNoFontDoc = !cv.profile.metricFlow && !cv.uaMshtml
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
                             .Any(sm => Regex.IsMatch(sm.Groups["s"].Value,
                                 @"font-family\s*:", RegexOptions.IgnoreCase))))
            // <font size=…>/<font face=…> style flow text inside the UA flow
            // itself (the ladder sizes; a resolvable face embeds for its runs).
            // A body-less <html> wrapper still parses as a full document (the
            // parser synthesizes the body) — it takes the UA flow like one. A
            // fragment ROOTED at a list (<ul>/<ol> is its first tag) carries pure
            // UA structure by construction and takes the same flow, as does a
            // fragment CARRYING a table (its grid renders through the metric
            // table renderer); other bare fragments keep the legacy flow.
            && (cv.tagFreeDoc
                // an explicit <body> (with or without the <html> wrapper)
                // parses as a full document just the same
                || Regex.IsMatch(cv.html, @"<html\b|<body\b|<table\b", RegexOptions.IgnoreCase)
                || Regex.IsMatch(cv.html, @"\A\s*(?:<!--.*?-->\s*)*<[ou]l\b",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline)
                // A fragment ROOTED at a styled box div — inline width+height+
                // border — is a border-box drawing, pure UA structure by
                // construction; it takes the UA flow, which strokes the
                // declared box and flows the content inside it.
                || Regex.IsMatch(cv.html,
                    @"\A\s*(?:<!--.*?-->\s*)*<div\b[^>]*style\s*=\s*(['""])(?=(?:(?!\1).)*\bwidth\s*:)(?=(?:(?!\1).)*\bheight\s*:)(?=(?:(?!\1).)*\bborder)(?:(?!\1).)*\1",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline)
                // A body rule pinning the UA-base face marks a full styled page
                // whatever wrapper it ships in — it takes the UA flow in that face.
                || cv.uaBodyFace is not null)
            // Table documents take the UA flow WITH their tables — the metric table
            // renderer draws them as real grids, the same model the expected render
            // applies (H-4: bordered cellspacing grids, centred tables, bgcolor
            // cells all render as authored, never as flattened text). Exception:
            // an unresolved RELATIVE stylesheet is a packaging gap (the sheet was
            // present when the page was authored — see the dead-CSS rule above),
            // so such a table document keeps the legacy calibrated flow.
            && (!Regex.IsMatch(cv.html, @"<table\b", RegexOptions.IgnoreCase)
                || cv.profile.deadExternalCss
                || !Regex.Matches(cv.html,
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

        return null;
    }
}
