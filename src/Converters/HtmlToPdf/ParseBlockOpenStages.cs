using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>UA body and metric paragraph tags and the article-rhythm div wrapper open through their own paths; false when one of them took the block.</summary>
    private static bool OpensAsSpecialBlock(ParseBlocksState pb, Token tok, string tag)
    {
        if (pb.browserUa && tag.Equals("p", StringComparison.OrdinalIgnoreCase))
        {
            if (tok.IsSelfClosing)
            {
                Flush(pb, pb.controlBoxes, pb.uaBlockRhythm, pb.articleRhythm, pb.spanPtTypography, false, pb.styleStack.Peek());
                pb.pendingEmptyPMarginPt = Math.Max(pb.pendingEmptyPMarginPt,
                    UaBlockMarginEm * pb.styleStack.Peek().FontSize);
                return false;
            }
            pb.pOpenDepth++;
        }
        // Metric flow: a body-level <p> opens one UA block margin above
        // it (the same 1.12 em the browser flow gives every paragraph).
        else if (pb.metricLayout && pb.uaPMargins && tag.Equals("p", StringComparison.OrdinalIgnoreCase)
                 && !tok.IsSelfClosing)
            pb.pendingEmptyPMarginPt = Math.Max(pb.pendingEmptyPMarginPt,
                UaBlockMarginEm * pb.styleStack.Peek().FontSize);
        // A div the sheet explicitly sets `display:inline` — directly or
        // through a descendant rule from an enclosing class
        // (`.content-center-text .bold { display:inline }`, the panel
        // header) — rides the current line like a span: no flush, no
        // block break, no style push.
        if (pb.articleRhythm && pb.css is not null
            && tag.Equals("div", StringComparison.OrdinalIgnoreCase)
            && tok.Attributes is not null
            && tok.Attributes.TryGetValue("class", out var inlDivCls))
        {
            var divInline = false;
            foreach (var c in inlDivCls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (pb.css.TryGetValue("." + c, out var idr)
                    && idr.TryGetValue("display", out var idd)
                    && idd.Trim().Equals("inline", StringComparison.OrdinalIgnoreCase))
                    divInline = true;
                for (var di = pb.divClassStack.Count - 1; di >= 0 && !divInline; di--)
                    foreach (var ec in pb.divClassStack[di].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                        if (pb.css.TryGetValue("." + ec + " ." + c, out var edr)
                            && edr.TryGetValue("display", out var edd)
                            && edd.Trim().Equals("inline", StringComparison.OrdinalIgnoreCase))
                        { divInline = true; break; }
                if (divInline) break;
            }
            if (divInline)
            {
                pb.inlineDivDepth++;
                return false;
            }
        }
        return true;
    }

    /// <summary>Top-only borders, pending border boxes, the align attribute and a page-break-before are recorded on the block.</summary>
    private static void ApplyBorderBoxes(ParseBlocksState pb, Token tok)
    {
        if (pb.browserUa && pb.style.BorderTopOnly && pb.style.BorderColor is { } tdCol
            && pb.style.BorderWidth > 0)
        {
            Flush(pb, pb.controlBoxes, pb.uaBlockRhythm, pb.articleRhythm, pb.spanPtTypography, false, pb.styleStack.Peek());
            pb.blocks.Add(new Block
            {
                Text = "", IsHardBreak = true,
                BorderTopOnly = true, BorderColor = tdCol,
                BorderWidth = pb.style.BorderWidth,
                // the divider wrapper's padding-top is box space under
                // the rule, before its content
                PadTop = pb.style.PadTop,
            });
            pb.style.BorderColor = null;
            pb.style.BorderWidth = 0;
            pb.style.BorderTopOnly = false;
            pb.style.PadTop = 0;
        }
        // Border-only declared box (browser-UA flow): inline width+height+
        // border with no background. The box is handed to the first block
        // that flushes inside this element (style's own height/border are
        // cleared so the close emits no trailing spacer and the line-box
        // border model stays off).
        if (pb.browserUa && pb.pendingBorderBox is null
            && pb.style.BorderWidth > 0 && pb.style.BorderColor is not null
            && pb.style.BackgroundColor is null && pb.style.BgBoxHeightPt <= 0 && pb.style.BgBoxHeightVh <= 0
            && pb.style.ExplicitHeight > 0
            && tok.Attributes is not null
            && tok.Attributes.TryGetValue("style", out var bbSt) && bbSt is not null
            && Regex.Match(bbSt, @"(?<![-\w])width\s*:\s*([^;""']+)",
                RegexOptions.IgnoreCase) is { Success: true } bbW
            && TryParseLength(bbW.Groups[1].Value.Trim()) is { } bbWPt)
        {
            pb.pendingBorderBox = (bbWPt, pb.style.ExplicitHeight, pb.style.BorderWidth,
                pb.style.BorderColor, pb.style.BorderRadiusPt);
            pb.pendingBorderBoxDepth = pb.styleStack.Count + 1;
            pb.style.ExplicitHeight = 0;
            pb.style.BorderWidth = 0;
            pb.style.BorderColor = null;
        }
        // The legacy ALIGN attribute: justify stretches word gaps at draw time,
        // center centres each measured line — both layout-neutral (wrap points
        // and pagination are unchanged).
        if (tok.Attributes is not null && tok.Attributes.TryGetValue("align", out var alignAttr))
        {
            var alignVal = alignAttr.Trim();
            if (alignVal.Equals("justify", StringComparison.OrdinalIgnoreCase))
                pb.style.AlignJustify = true;
            else if (alignVal.Equals("center", StringComparison.OrdinalIgnoreCase))
                pb.style.AlignCenterAttr = true;
        }
        // An element opening with page-break-before must break even when it emits
        // no block itself (the `<div style="page-break-before:always"></div>` idiom):
        // carry the break to whatever block flushes next.
        if (pb.style.PageBreakBefore) pb.pendingPageBreak = true;
        // Entering a differently NAMED page (CSS3 paged media) breaks like a declared
        // page break: the flow carries a page name, and an element whose own `page`
        // names another one starts a fresh sheet and renames the flow (MEASURED, the Word
        // e-mail export: its prologue paragraphs stand on page 1 and `div.WordSection1`
        // takes the body to page 2). The break rule below drops the break on a page that
        // holds nothing yet, so a document opening inside a named page loses no sheet.
        if ((pb.style.PageName ?? NamedPageFor(pb, tok)) is { Length: > 0 } wanted
            && !string.Equals(wanted, pb.pageName, StringComparison.Ordinal))
        {
            if (pb.pageName is not null || pb.blocks.Count > 0) pb.pendingPageBreak = true;
            pb.pageName = wanted;
        }
    }

    /// <summary>The article-rhythm div rules, the inline indent override and the div band background apply.</summary>
    /// <summary>The page NAME an opening element wants, from the document's raw `page:` rules: its
    /// own tag, its classes, `tag.class`, and its id. Null when the document names no page or this
    /// element matches none of its rules.</summary>
    private static string? NamedPageFor(ParseBlocksState pb, Token tok)
    {
        if (pb.cv?.profile.namedPageRules is not { Count: > 0 } rules) return null;
        var tag = tok.Tag ?? "";
        if (rules.TryGetValue(tag, out var byTag)) return byTag;
        if (tok.Attributes is null) return null;
        if (tok.Attributes.TryGetValue("class", out var cls))
            foreach (var c in cls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                if (rules.TryGetValue("." + c, out var byClass)) return byClass;
                else if (rules.TryGetValue(tag + "." + c, out var byTagClass)) return byTagClass;
        if (tok.Attributes.TryGetValue("id", out var id) && rules.TryGetValue("#" + id, out var byId))
            return byId;
        return null;
    }

    private static void ApplyArticleRhythmAndBands(ParseBlocksState pb, Token tok, string tag)
    {
        if (pb.articleRhythm && pb.css is not null
            && tag.Equals("div", StringComparison.OrdinalIgnoreCase)
            && tok.Attributes is not null
            && tok.Attributes.TryGetValue("class", out var panelCls))
            foreach (var pc in panelCls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                if (pb.css.TryGetValue("." + pc, out var panelRule)
                    && panelRule.ContainsKey("border")
                    && (panelRule.ContainsKey("background-color")
                        || panelRule.ContainsKey("background")))
                {
                    pb.blocks.Add(new Block
                    {
                        Text = "", IsHardBreak = true,
                        ExplicitHeight = ArticlePanelPadTopPt,
                        FontSize = pb.style.FontSize,
                    });
                    pb.style.MarginBottom +=
                        ArticlePanelPadBottomPt + ArticlePanelMarginBottomPt;
                    break;
                }
        // Inline style="…" overrides tag defaults: if the author
        // explicitly set padding-left / margin-left we drop the
        // list-style indent the tag default added so that e.g.
        // `<ol style="padding-left:0">` sits flush with body text.
        if (HasInlineIndentOverride(tok.Attributes))
            pb.style.LeftIndent = pb.parent.LeftIndent;
        ApplyInlineStyle(tok.Attributes, pb.style, pb);
        ApplyUaPercentBoxWidth(pb, tok);
        // `color` is an INHERITED property: a block that declares none draws in its
        // parent's ink, however many levels down (probed: `div { color: White }` with
        // the text one nested div deeper still draws white; only the declaring block
        // drew white before this). Runs after the inline style so the child's own
        // declaration, which ApplyInlineStyle has already set, keeps precedence.
        pb.style.ForeColor ??= pb.parent.ForeColor;
        // Pinned-body report band: the wrapper's paint reaches the
        // inline-block child that carries the band's text (see divBandBg),
        // and the two padded div levels (the sheet's `div { padding: 4px }`
        // on wrapper AND child) reserve their pad above the line. Runs
        // AFTER the inline style so the wrapper's own background/margins
        // are already resolved.
        if (pb.divBandBg)
        {
            // A painted child of a painted wrapper carries the wrapper's
            // top margin too — the band's text flushes under the CHILD's
            // style, where a fresh zero would drop the wrapper's
            // `margin-top: 5px`.
            if (pb.parent.BackgroundColor is not null && pb.parent.MarginTop > pb.style.MarginTop)
                pb.style.MarginTop = pb.parent.MarginTop;
            pb.style.BackgroundColor ??= pb.parent.BackgroundColor;
            pb.style.ForeColor ??= pb.parent.ForeColor;
            if (pb.style.BackgroundColor is not null && pb.style.BandPadPt <= 0
                && pb.css is not null && pb.css.TryGetValue("div", out var dbr)
                && dbr.TryGetValue("padding", out var dbp)
                && TryParseLength(dbp) is { } dbpPt && dbpPt > 0)
            {
                pb.style.BandPadPt = 2 * dbpPt;
                pb.style.MarginTop = Math.Max(pb.style.MarginTop, pb.style.BandPadPt);
                // No bottom margin: the flow hands the cursor back at the
                // fill's bottom edge when the band closes (see the band
                // rewind in the render loop) — the next element's own
                // margin-top is the whole gap.
            }
            // A painted panel's own inline margin-top is REAL space above
            // its box, on top of the pad the fill reserves (the report's
            // `margin-top: 5px` boxes).
            if (pb.style.BackgroundColor is not null && tok.Attributes is not null
                && tok.Attributes.TryGetValue("style", out var dbSt) && dbSt is not null
                && Regex.Match(dbSt, @"(?<![-\w])margin-top\s*:\s*([\d.]+)\s*px",
                    RegexOptions.IgnoreCase) is { Success: true } dbMt
                && double.TryParse(dbMt.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var dbMtPx)
                && dbMtPx > 0)
                pb.style.MarginTop += dbMtPx * 0.75;
            // An EMPTY div is the report's authored spacer ("<!--empty
            // divs are for spacing--><div></div>") — remember this open
            // so its close can emit the padding box it renders as.
            pb.emptyDivDepthMark = tag.Equals("div", StringComparison.OrdinalIgnoreCase)
                && (tok.Attributes is null || !tok.Attributes.ContainsKey("style"))
                ? pb.styleStack.Count + 1 : -1;
            pb.emptyDivBlocksAt = pb.blocks.Count;
            pb.emptyDivTextAt = pb.currentText.Length;
        }
    }

    /// <summary>The stylesheet rules, the inline attributes and the absolute-span ledger settle the block's remaining style.</summary>
    private static void ApplyCssAndLedgerRules(ParseBlocksState pb, Token tok, string tag)
    {
        // The field-list dialect's fields box: its padding insets every row on both sides.
        if (pb.cv?.profile is { fieldListDoc: true, fieldsClass: { } fieldsCls } flp
            && tag.Equals("div", StringComparison.OrdinalIgnoreCase)
            && tok.Attributes is { } fbAttrs && fbAttrs.TryGetValue("class", out var fbCls) && fbCls is not null
            && Regex.IsMatch(fbCls, @"(^|\s)" + Regex.Escape(fieldsCls) + @"(\s|$)"))
        {
            pb.style.FieldsBox = true;
            pb.style.LeftIndent += flp.fieldsInsetPt;
            pb.style.RightInsetPt += flp.fieldsInsetPt;
            pb.style.OwnPadTopPt += flp.fieldsInsetPt;
            // (the box's `> div` row margins are its children's, not its own: a bare span in it stands no margin)
            pb.style.MarginTop = 0;
            pb.style.MarginBottom = 0;
        }
        if (!(pb.metricLayout && pb.uaPMargins)
            && tok.Attributes is not null
            && tok.Attributes.TryGetValue("style", out var optSt) && !string.IsNullOrEmpty(optSt)
            && Regex.Match(optSt, @"padding-top\s*:\s*(\d+(?:\.\d+)?)\s*(px|pt)",
                RegexOptions.IgnoreCase) is { Success: true } optM
            && double.TryParse(optM.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var optV)
            && optV > 0)
            pb.style.OwnPadTopPt = optM.Groups[2].Value.Equals("pt", StringComparison.OrdinalIgnoreCase)
                ? optV : optV * 0.75;
        // A div's ABSOLUTE width (style="width:680" — quirks unitless = px, or
        // "width:680px") is recorded on every flow; only the form-document
        // dialect honors it as the wrap box at layout time.
        if (tok.Attributes is not null
            && tok.Attributes.TryGetValue("style", out var awSt) && !string.IsNullOrEmpty(awSt))
        {
            var awm = Regex.Match(awSt, @"(?:^|[;\s])width\s*:\s*(\d+(?:\.\d+)?)\s*(?:px)?\s*(?:;|$)");
            if (awm.Success && double.TryParse(awm.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var awPx)
                && awPx > 0)
                pb.style.WidthPx = awPx;
        }
        // CSS rules: type selector then class selector(s), each overriding the
        // previous, before the inline style="…" (highest specificity).
        ApplyCssRules(pb.css, tag, tok.Attributes, pb.style, pb.metricLayout, pb.coverStyles,
            floatFlow: pb.floatFlow);
        // Ledger: a class WIDTH on a block element is that element's box —
        // the wrap/centring frame its lines lay out in.
        if (pb.absSpanLedger && pb.css is not null && tok.Attributes is not null
            && tok.Attributes.TryGetValue("class", out var lgDivCls) && lgDivCls is not null)
            foreach (var dc in lgDivCls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                if (pb.css.TryGetValue("." + dc, out var dcr)
                    && dcr.TryGetValue("width", out var dcw)
                    && Regex.Match(dcw, @"([\d.]+)\s*px", RegexOptions.IgnoreCase)
                        is { Success: true } dcwM
                    && double.TryParse(dcwM.Groups[1].Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var dcwPx))
                    pb.style.WidthPx = dcwPx;
    }

    /// <summary>The band attribute, the browser-UA inline style attribute and the metric paragraph margins apply to the block.</summary>
    private static void ApplyUaBandAndStyleAttributes(ParseBlocksState pb, Token tok, string tag)
    {
        if (pb.browserUa && tok.Attributes is not null
            && tok.Attributes.TryGetValue("band", out var bandSpec))
        {
            var bandParts = bandSpec.Split('|');
            var rgbParts = bandParts[0].Split(',');
            if (rgbParts.Length == 3
                && int.TryParse(rgbParts[0], out var bandR)
                && int.TryParse(rgbParts[1], out var bandG)
                && int.TryParse(rgbParts[2], out var bandB))
            {
                pb.style.BandColor = Color.FromRgbBytes(bandR, bandG, bandB);
                pb.style.BandPx = bandParts.Length > 1 && double.TryParse(bandParts[1],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var bandPxA) ? bandPxA : 1;
                pb.style.BandPadPx = bandParts.Length > 2 && double.TryParse(bandParts[2],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var bandPadA) ? bandPadA : 0;
            }
        }
        // A ".cls h4"-style descendant rule with a border-bottom paints a band
        // under the heading (the print-grid section-header underline).
        else if (pb.browserUa && pb.css is not null && tag.ToLowerInvariant() is "h4" or "h3" or "h2")
        {
            for (var di = pb.divClassStack.Count - 1; di >= 0 && pb.style.BandColor is null; di--)
                foreach (var c in pb.divClassStack[di].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                    if (pb.css.TryGetValue("." + c + " " + tag.ToLowerInvariant(), out var bandRule)
                        && bandRule.TryGetValue("border-bottom", out var bandDecl))
                    {
                        var bw = Regex.Match(bandDecl, @"(\d+(?:\.\d+)?)\s*px");
                        pb.style.BandColor = ParseCssColor(bandDecl);
                        pb.style.BandPx = bw.Success ? double.Parse(bw.Groups[1].Value,
                            System.Globalization.CultureInfo.InvariantCulture) : 1;
                        if (bandRule.TryGetValue("padding-bottom", out var bandPad)
                            && TryParseLength(bandPad) is { } bandPadPt)
                            pb.style.BandPadPx = bandPadPt / 0.75;
                        break;
                    }
        }
        // Browser-UA flow: a div's style="width:N%" narrows the wrap box (the
        // expected render stacks such divs but wraps at the declared width),
        // and its padding-top is non-collapsing space above the content.
        if (pb.browserUa && tok.Attributes is not null
            && tok.Attributes.TryGetValue("style", out var uaSt) && !string.IsNullOrEmpty(uaSt))
        {
            var uwm = Regex.Match(uaSt, @"(?:^|[;\s])width\s*:\s*(\d+(?:\.\d+)?)\s*%");
            if (uwm.Success && double.TryParse(uwm.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var uwPct)
                && uwPct is > 0 and < 100)
                pb.style.WidthFrac = uwPct / 100.0;
            // Shorthand only — the padding-top LONGHAND is OwnPadTopPt's
            // (parsed below for every flow): it cascades down wrapper
            // opens and the first flushing block spends it once.
            var upm = Regex.Match(uaSt, @"(?<![-\w])padding\s*:\s*(\d+(?:\.\d+)?)\s*(px|pt)");
            if (upm.Success && double.TryParse(upm.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var upPx)
                && upPx > 0)
                pb.style.PadTop += upm.Groups[2].Value.Equals("pt", StringComparison.OrdinalIgnoreCase)
                    ? upPx : upPx * 0.75;
        }
        // Metric flow: a div's inline padding-top is real space above its
        // first block (the newsletter's #body_style 7px frame).
        else if (pb.metricLayout && pb.uaPMargins && tag.Equals("div", StringComparison.OrdinalIgnoreCase)
            && tok.Attributes is not null
            && tok.Attributes.TryGetValue("style", out var mpSt) && !string.IsNullOrEmpty(mpSt))
        {
            var mpm = Regex.Match(mpSt, @"padding(?:-top)?\s*:\s*(\d+(?:\.\d+)?)\s*px");
            if (mpm.Success && double.TryParse(mpm.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var mpPx)
                && mpPx > 0)
                pb.style.PadTop += mpPx * 0.75;
        }
    }

    /// <summary>A classed container's stylesheet indents and widths become the block's indents.</summary>
    private static void ApplyContainerBoxIndents(ParseBlocksState pb)
    {
        if (pb.containerBoxIndents && pb.css is not null && !string.IsNullOrEmpty(pb.divClassStack[^1]))
        {
            double bxPadL = 0, bxPadR = 0, bxPadT = 0, bxBorder = 0, bxHeight = 0;
            var bxPctWidth = false;
            Color? bxShadow = null;
            var bxClasses = pb.divClassStack[^1].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            void ReadBoxRule(Dictionary<string, string> rule)
            {
                if ((rule.TryGetValue("box-shadow", out var bsh)
                     || rule.TryGetValue("-webkit-box-shadow", out bsh))
                    && ParseCssColor(bsh) is { } bshCol)
                    bxShadow = bshCol;
                if (rule.TryGetValue("padding", out var pSh) && BoxChromeLen(pSh) is > 0 and var pv)
                { bxPadL = Math.Max(bxPadL, pv); bxPadR = Math.Max(bxPadR, pv); bxPadT = Math.Max(bxPadT, pv); }
                if (rule.TryGetValue("padding-left", out var pl)) bxPadL = Math.Max(bxPadL, BoxChromeLen(pl));
                if (rule.TryGetValue("padding-right", out var pr)) bxPadR = Math.Max(bxPadR, BoxChromeLen(pr));
                if (rule.TryGetValue("padding-top", out var pt)) bxPadT = Math.Max(bxPadT, BoxChromeLen(pt));
                if (rule.TryGetValue("border", out var bd)) bxBorder = Math.Max(bxBorder, BoxChromeLen(bd));
                if (rule.TryGetValue("height", out var bh)) bxHeight = Math.Max(bxHeight, BoxChromeLen(bh));
                // Only width:100% marks the chrome-overflow case (its content
                // box equals the parent's). Any other percent is a responsive
                // grid column's @media width leaking into the flattened map —
                // on paper the column is width:auto and its chrome bills.
                if (rule.TryGetValue("width", out var bw) && bw.Trim() == "100%") bxPctWidth = true;
            }
            foreach (var bc in bxClasses)
                if (pb.css.TryGetValue("." + bc, out var bcr)) ReadBoxRule(bcr);
            // Compound two-class selectors (".card.default { border: … }").
            foreach (var ca in bxClasses)
                foreach (var cb in bxClasses)
                    if (!ReferenceEquals(ca, cb) && pb.css.TryGetValue("." + ca + "." + cb, out var ccr))
                        ReadBoxRule(ccr);
            if (bxPadL + bxBorder > 0) pb.style.LeftIndent += bxPadL + bxBorder;
            if (bxPadT + bxBorder > 0) pb.pendingBoxPadTop += bxPadT + bxBorder;
            if (bxHeight > 0) pb.pendingBoxHeight = Math.Max(pb.pendingBoxHeight, bxHeight);
            if (!bxPctWidth) pb.style.BillPadPt += bxPadL + bxPadR + 2 * bxBorder;
            // A box-shadow'd container is the widget CARD: remember its shadow
            // colour and its own chrome so the chart image can frame it.
            if (bxShadow is not null)
            {
                pb.style.CardShadowColor = bxShadow;
                pb.style.CardChromePt = bxPadL + bxBorder;
            }
        }
    }

    /// <summary>Browser-UA stylesheet rules apply to the block (their typography also on the calibrated
    /// flow when the document admits it), container indents and control boxes register, and the div class stack grows.</summary>
    private static void ApplyBrowserUaCssAndClassStack(ParseBlocksState pb, Token tok, string tag)
    {
        ApplyBrowserUaTagCss(pb, tok, tag);
        // The sheet's own element reset ("h1, h2, …, p { margin: 0 }") beats
        // the legacy calibrated heading/paragraph margins — the widget card
        // measures its header purely from the class-rule chrome
        // (containerBoxIndents mode only).
        if (pb.containerBoxIndents && pb.css is not null
            && pb.css.TryGetValue(tag.ToLowerInvariant(), out var tagReset)
            && tagReset.TryGetValue("margin", out var tagResetMargin)
            && Regex.IsMatch(tagResetMargin.Trim(), @"^0(px)?(\s+0(px)?){0,3}$"))
        {
            pb.style.MarginTop = 0;
            pb.style.MarginBottom = 0;
        }
        // Control-box dialect: headings render at the UA scale of the 12 pt
        // base with the dialect's heading gaps (27.34 pt above
        // an h3 = 13.5 line + 13.84 margin; 25.97 below = 16.5 line + 9.47).
        // (the browser-UA flow keeps its em heading margins: measured on a saved wiki page, h1 0.67em
        //  above and below, h2 0.83em - not the control-box dialect's calibrated pair)
        if (pb.controlBoxes && !(pb.browserUa) && tag.ToLowerInvariant() is "h1" or "h2" or "h3" or "h4" or "h5" or "h6")
        {
            pb.style.FontSize = tag.ToLowerInvariant() switch
            {
                "h1" => 24, "h2" => 18, "h3" => 14.039, "h4" => 12,
                "h5" => 9.96, _ => 8.04,
            };
            pb.style.FontRes = "F2";
            pb.style.MarginTop = 13.84;
            pb.style.MarginBottom = 9.47;
        }
        // UA flow, tag rules the reference honours without leaving its flow (probed on
        // `h1 { display: inline }` + `h2 { text-decoration: underline }`): an inline-displayed
        // heading keeps its UA size and weight but contributes no block margins, and a
        // block's own underline decoration runs over its whole text.
        if (pb.browserUa && pb.css is not null && pb.css.TryGetValue(tag.ToLowerInvariant(), out var uaTagRule))
        {
            if (tag.Length == 2 && (tag[0] is 'h' or 'H') && tag[1] is >= '1' and <= '6'
                && uaTagRule.TryGetValue("display", out var uaTagDisp)
                && uaTagDisp.Trim().Equals("inline", StringComparison.OrdinalIgnoreCase))
            {
                pb.style.MarginTop = 0;
                pb.style.MarginBottom = 0;
            }
            if (uaTagRule.TryGetValue("text-decoration", out var uaTagDeco)
                && Regex.IsMatch(uaTagDeco, "(^|[^a-z-])underline([^a-z-]|$)", RegexOptions.IgnoreCase))
            {
                pb.blockUnderStart = pb.currentText.Length;
            }
        }
        pb.divClassStack.Add(tok.Attributes is not null
            && tok.Attributes.TryGetValue("class", out var openCls) ? openCls : "");
        pb.divTagStack.Add(tag.ToLowerInvariant());
        pb.divIdStack.Add(tok.Attributes is not null
            && tok.Attributes.TryGetValue("id", out var openId) && openId is not null ? openId : "");
        pb.divStyleStack.Add(tok.Attributes is not null
            && tok.Attributes.TryGetValue("style", out var openSt) && openSt is not null ? openSt : "");
    }

    /// <summary>Word mail: a paragraph's text-indent (negative = a hanging label) rides its style, signed, in points.</summary>
    private static void ApplyWordMailIndent(ParseBlocksState pb, Token tok)
    {
        if (!pb.wordMail || tok.Attributes is not { } wmAttrs
            || !wmAttrs.TryGetValue("style", out var wmSt) || wmSt is null) return;
        var wmTi = Regex.Match(wmSt, @"text-indent\s*:\s*(-?[\d.]+)\s*(pt|px)", RegexOptions.IgnoreCase);
        if (wmTi.Success && double.TryParse(wmTi.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var wmTiv))
            pb.style.TextIndentPt = wmTi.Groups[2].Value.Equals("px", StringComparison.OrdinalIgnoreCase) ? wmTiv * PxPt : wmTiv;
    }

    /// <summary>The span-pt typography dialect reads its line-pitch, bottom and paragraph attributes into the style.</summary>
    private static void ApplySpanPtTypography(ParseBlocksState pb, Token tok)
    {
        if (pb.spanPtTypography && tok.Attributes is { } rlpAttrs
            && rlpAttrs.TryGetValue("style", out var rlpSt) && rlpSt is not null)
        {
            if (pb.style.MarginTop <= 0
                && Regex.Match(rlpSt, @"(?<![-\w])margin\s*:\s*([\d.]+)\s*pt",
                    RegexOptions.IgnoreCase) is { Success: true } rlpM
                && double.TryParse(rlpM.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var rlpMt)
                && rlpMt > 0)
                pb.style.MarginTop = rlpMt;
            if (Regex.Match(rlpSt, @"text-indent\s*:\s*([\d.]+)\s*pt",
                    RegexOptions.IgnoreCase) is { Success: true } rlpTi
                && double.TryParse(rlpTi.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var rlpTiv)
                && rlpTiv > 0)
                pb.style.TextIndentPt = rlpTiv;
        }
        // Redline divider: a paragraph's border-top LONGHANDS declare
        // the cover rule (border-top-style: solid; -width: 4.5pt) — the
        // shorthand parser never sees the spelled-out triplet.
        if (pb.spanPtTypography && tok.Attributes is { } btAttrs
            && btAttrs.TryGetValue("style", out var btSt) && btSt is not null
            && Regex.IsMatch(btSt, @"border-top-style\s*:\s*solid", RegexOptions.IgnoreCase))
        {
            pb.style.BorderTopOnly = true;
            pb.style.BorderWidth = Regex.Match(btSt, @"border-top-width\s*:\s*([\d.]+)\s*pt",
                    RegexOptions.IgnoreCase) is { Success: true } btw
                && double.TryParse(btw.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var btwv)
                ? btwv : 0.75;
            pb.style.BorderColor = Regex.Match(btSt, @"border-top-color\s*:\s*([^;]+)",
                    RegexOptions.IgnoreCase) is { Success: true } btc
                ? ParseCssColor(btc.Groups[1].Value.Trim()) ?? Color.FromArgb(0, 0, 0)
                : Color.FromArgb(0, 0, 0);
        }
        // pt-styled fragment: a flow paragraph's pt margin shorthand
        // (top right bottom left) insets its wrap box — these paragraphs
        // open at 96 + 1.7 and wrap 6.4 inside
        // the right content edge.
        if (pb.spanPtTypography && tok.Attributes is { } ptpAttrs
            && ptpAttrs.TryGetValue("style", out var ptpSt) && ptpSt is not null
            && Regex.Match(ptpSt,
                @"margin\s*:\s*[\d.]+pt\s+([\d.]+)pt\s+[\d.]+pt\s+([\d.]+)pt",
                RegexOptions.IgnoreCase) is { Success: true } ptpM)
        {
            var ptpCi = System.Globalization.CultureInfo.InvariantCulture;
            if (double.TryParse(ptpM.Groups[2].Value,
                    System.Globalization.NumberStyles.Float, ptpCi, out var ptpL))
                pb.style.LeftIndent += ptpL;
            if (double.TryParse(ptpM.Groups[1].Value,
                    System.Globalization.NumberStyles.Float, ptpCi, out var ptpR))
                pb.style.RightInsetPt = ptpR;
        }
    }

    /// <summary>The pending run flushes, the parent style is read, and the block's style starts from the tag defaults, the parent's padding and the UA margins.</summary>
    private static void BeginBlockStyle(ParseBlocksState pb, string tag)
    {
        Flush(pb, pb.controlBoxes, pb.uaBlockRhythm, pb.articleRhythm, pb.spanPtTypography, false,pb.styleStack.Peek());
        pb.inlineRunId = 0; pb.runPrevWasControl = false;
        pb.parent = pb.styleStack.Peek();
        pb.parent.ChildOpened = true;
        // (the field-list dialect: a BLOCK opening after a label - a pre, a grid's div - stands on its
        //  own line under it; the label gives its row to a span value only)
        if (pb.cv?.profile.fieldListDoc == true && pb.pendingColIndentFrac > 0)
        {
            pb.pendingColIndentFrac = 0; pb.pendingColIndent = 0;
            if (pb.blocks.Count > 0) pb.blocks[^1].NoAdvanceY = false;
        }
        SeedBlockStyleFromParent(pb, tag);
        // A painted box paints once, on the first block that flushes inside its
        // element - a child opening here takes it with it, and the parent lets it go.
        if (pb.parent.BgBoxHeightPt > 0 || pb.parent.BgBoxHeightVh > 0)
        {
            pb.style.BackgroundColor ??= pb.parent.BackgroundColor;
            pb.style.BgBoxWidthPt = pb.parent.BgBoxWidthPt;
            pb.style.BgBoxHeightPt = pb.parent.BgBoxHeightPt;
            pb.style.BgBoxHeightVh = pb.parent.BgBoxHeightVh;
            pb.style.BgBoxIndentPt = pb.parent.BgBoxIndentPt;
            pb.style.BgImageSrc ??= pb.parent.BgImageSrc;
            if (pb.style.BgImageSize.Length == 0) pb.style.BgImageSize = pb.parent.BgImageSize;
            pb.parent.BgImageSrc = null;
            pb.parent.BgImageSize = "";
            pb.parent.BgBoxWidthPt = 0;
            pb.parent.BgBoxHeightPt = 0;
            pb.parent.BgBoxHeightVh = 0;
        }
        // A container's pending padding-top spaces the FIRST block that
        // actually flushes — hand it to the child opening now so a <p>
        // inside a padded div does not orphan it on the div's own style.
        if ((pb.uaPMargins || pb.uaBoxes) && pb.parent.PadTop > 0)
        {
            pb.style.PadTop += pb.parent.PadTop;
            pb.parent.PadTop = 0;
        }
        // A wrapper's OWN padding-top LONGHAND is box space before its first
        // child (probed: the certificate sheet's 20px Title band pad is in
        // the expected h1 seat). Handed down the open chain, it lands on
        // the first block that flushes; a wrapper that stays childless keeps
        // it for the empty-close spacer instead. The `padding:` SHORTHAND
        // stays out on purpose — a shorthand-padded container's pad is NOT
        // spent (the print-grid sheet's 30px container pins that).
        if (pb.parent.OwnPadTopPt > 0)
        {
            pb.style.OwnPadTopPt += pb.parent.OwnPadTopPt;
            pb.parent.OwnPadTopPt = 0;
        }
        // (the Word-filtered arm's own 1.00 em paragraph margin belongs to its GROWN sheet - the
        //  shape it was calibrated on; a plain filtered page takes the UA 1.12 em like every
        //  other flow, as measured)
        ApplyBlockTagStyle(tag, pb.style, pb.uaDefaults, pb.browserUa, pb.bandDialect, pb.uaBlockRhythm,
            pb.articleRhythm, pb.msoParagraphs && (pb.cv?.profile.msoFilteredGrownSheet ?? true),
            emHeadings: pb.floatFlow, html5UaHeadings: pb.html5UaHeadings);
        // pt-styled fragment: headings carry NO extra margins — the
        // reference stacks the h2 title line-on-line with its nbsp
        // spacer paragraphs (the spans size it; the tag only bolds).
        if (pb.spanPtTypography && tag.Length == 2
            && (tag[0] is 'h' or 'H') && char.IsDigit(tag[1]))
        {
            pb.style.MarginTop = 0;
            pb.style.MarginBottom = 0;
        }
        // Root stack depth 1 = body level; anything deeper means the block
        // opened inside another block element (div/li/td/h1/…) — its own
        // top margin is a nested one.
        pb.style.MarginTopNested = pb.styleStack.Count > 1;
        CollapseParentTopMargin(pb);
    }

    /// <summary>Adjoining top margins collapse: a parent's own top margin lands on the FIRST block that opens inside it, the larger of the two winning and keeping its origin.</summary>
    private static void CollapseParentTopMargin(ParseBlocksState pb)
    {
        // A list's margin rides its first item and a wrapped heading's rides
        // the paragraph it opens with. At the document top the margin of a
        // BODY-LEVEL element then vanishes with the other UA defaults
        // wherever it landed, but a nested element's survives like an
        // authored margin — max-collapsed with the UA body margin (probed on
        // div- and h1..h3-wrapped lists; measured on the td-rooted report, on
        // the div-wrapped list whose item opens with a div of its own, and on
        // the body-level list whose item does the same and opens at the bare
        // body inset). Browser-UA flow only; the legacy calibrated flows keep
        // their line-on-line stacking.
        if (!pb.browserUa || pb.parent.MarginTop <= 0 || pb.parent.BlocksAtOpen != pb.blocks.Count)
            return;
        if (pb.parent.MarginTop > pb.style.MarginTop)
        {
            pb.style.MarginTop = pb.parent.MarginTop;
            pb.style.MarginTopNested = pb.parent.MarginTopNested;
            pb.style.MarginTopAuthored |= pb.parent.MarginTopAuthored;
        }
        pb.parent.MarginTop = 0;
    }

    /// <summary>A list item opens with its marker, its indent from the list depth and its item spacing.</summary>
    private static void OpenListItemBlock(ParseBlocksState pb)
    {
        // Panel items pitch one line box + the link's block pad.
        if (pb.articleRhythm && pb.parent.TocLinkPadPt > 0)
            pb.style.MarginBottom = pb.parent.TocLinkPadPt;
        pb.parent.ChildIndex++;
        BeforeMarker? before = null;
        if (pb.parent.BeforeRules is not null)
            foreach (var r in pb.parent.BeforeRules)
                if (r.Matches(pb.parent.ChildIndex)) { before = r; break; }
        if (before is not null)
        {
            // CSS-supplied generated marker (list-style:none + ::before): render it as
            // its own run AFTER the item text so, on an RTL line, the text is the earlier
            // fragment and the marker the later one.
            pb.pendingMarker = before.Content;
            pb.pendingMarkerAfter = true;
        }
        else if (pb.parent.BeforeRules is null)
        {
            // No CSS markers for this list → ordinal (decimal, or the
            // list's alpha/roman list-style-type) / bullet default.
            pb.pendingMarker = pb.parent.ListKind == 1
                ? FormatListOrdinal(++pb.parent.ListCounter, pb.parent.ListStyleType) + "."
                : UaBulletMarker(pb);
            pb.pendingMarkerAfter = false;
        }
        // BeforeRules present but no rule matched this index → no marker.
    }

    /// <summary>The UA bullet for an item at its list nesting depth: disc, then circle, then
    /// square for every deeper level (<c>ul ul { list-style-type: circle }</c>,
    /// <c>ul ul ul { list-style-type: square }</c>). The legacy calibrated flows keep the
    /// disc at every depth.</summary>
    private static string UaBulletMarker(ParseBlocksState pb)
    {
        if (!pb.browserUa) return "•";
        var depth = 0;
        foreach (var anc in pb.styleStack)
            if (anc.ListKind == 2) depth++;
        return depth switch { <= 1 => "•", 2 => "◦", _ => "▪" };
    }

    /// <summary>An ordered or unordered list opens: its kind, start number, marker style, nesting depth and the indents that follow from them.</summary>
    private static void OpenListBlock(ParseBlocksState pb, Token tok, string tag)
    {
        pb.style.ListKind = tag == "ol" ? 1 : 2;
        // A list nested inside another list keeps NO block margin (the
        // UA `ol ol, ul ul { margin-block-start: 0 }` reset — probed:
        // nested items continue at bare line pitch). Browser-UA flow
        // only; the legacy calibrated flows keep their stacking.
        if (pb.browserUa)
            foreach (var anc in pb.styleStack)
                if (anc.ListKind != 0)
                {
                    pb.style.MarginTop = 0;
                    pb.style.MarginBottom = 0;
                    break;
                }
        if (tag == "ol")
        {
            pb.style.ListCounter = ParseListStart(tok.Attributes);
            // list-style-type from the list's own inline style (or the
            // legacy type= attribute): alpha/roman ordinals instead of
            // the decimal default.
            pb.style.ListStyleType = "";
            if (tok.Attributes is not null)
            {
                if (tok.Attributes.TryGetValue("style", out var olSt) && olSt is not null
                    && Regex.Match(olSt,
                        @"list-style-type\s*:\s*(upper-alpha|lower-alpha|upper-roman|lower-roman|upper-latin|lower-latin)",
                        RegexOptions.IgnoreCase) is { Success: true } lst)
                    pb.style.ListStyleType = lst.Groups[1].Value.ToLowerInvariant()
                        .Replace("latin", "alpha");
                else if (tok.Attributes.TryGetValue("type", out var olTy))
                    pb.style.ListStyleType = olTy?.Trim() switch
                    {
                        "A" => "upper-alpha", "a" => "lower-alpha",
                        "I" => "upper-roman", "i" => "lower-roman",
                        _ => "",
                    };
            }
        }
        // Styled-article: an enclosing container class may restyle the
        // list wholesale (`.td-toc ol { list-style-type: disc }` bullets
        // an <ol>), and its `a { padding-bottom }` block-link rule sets
        // the item pitch. Measured: panel items sit 33.3pt in with a
        // 36pt step per nesting level, one line box + the link pad apart.
        if (pb.articleRhythm && pb.css is not null)
        {
            Dictionary<string, string>? tocList = null, tocLink = null;
            for (var di = pb.divClassStack.Count - 1;
                 di >= 0 && tocList is null; di--)
                foreach (var c in pb.divClassStack[di].Split((char[]?)null,
                             StringSplitOptions.RemoveEmptyEntries))
                {
                    if (tocList is null
                        && pb.css.TryGetValue("." + c + " ol", out var clr)
                        && clr.ContainsKey("list-style-type"))
                        tocList = clr;
                    if (tocLink is null
                        && pb.css.TryGetValue("." + c + " a", out var cla)
                        && cla.ContainsKey("padding-bottom"))
                        tocLink = cla;
                }
            if (tocList is not null
                && tocList["list-style-type"].Trim()
                    .Equals("disc", StringComparison.OrdinalIgnoreCase))
                pb.style.ListKind = 2;
            if (tocList is not null || tocLink is not null)
            {
                // Panel list geometry: replace the plain-article indent
                // with the panel's own (level 1 at 33.3, +36 per level),
                // and drop the article list margin — panel items pitch
                // uniformly across nesting boundaries (measured 24
                // between EVERY pair, group ends included).
                pb.style.LeftIndent += ArticleTocIndentPt - ArticleListIndentPt
                    + (pb.parent.ListKind != 0 ? ArticleTocLevelPt - ArticleTocIndentPt : 0);
                pb.style.MarginBottom = 0;
                pb.style.TocLinkPadPt = tocLink is not null
                    && TryParseLength(tocLink["padding-bottom"]) is { } tlp
                    ? tlp : 0;
            }
        }
        if (pb.parent.TocLinkPadPt > 0) pb.style.TocLinkPadPt = pb.parent.TocLinkPadPt;
        pb.style.BeforeRules = ResolveListBeforeRules(pb.beforeMarkers,
            tok.Attributes is not null && tok.Attributes.TryGetValue("class", out var lc) ? lc : null);
        pb.style.ChildIndex = 0;
    }

    /// <summary>The browser's default block margin, in em of the element's own size (p and
    /// the lists 1, the headings 0.67 down to 2.33, anything else 0).</summary>
    /// <summary>The UA em margins the reference engine steps its UA-grid blocks by: an h3 0.83em, an h4 1.12em
    /// (measured on the quotation), the rest as the UA sheet's.</summary>
    private static double UaGridElementMarginEm(string tag) => tag.ToLowerInvariant() switch
    {
        "h3" => 0.83,
        "h4" => 1.12,
        _ => UaElementMarginEm(tag),
    };

    private static double UaElementMarginEm(string tag) => tag.ToLowerInvariant() switch
    {
        "p" or "ul" or "ol" or "blockquote" or "dl" or "figure" or "h3" => 1.0,
        "h1" => 0.67,
        "h2" => 0.83,
        "h4" => 1.33,
        "h5" => 1.67,
        "h6" => 2.33,
        _ => 0,
    };

    /// <summary>The block elements whose own margins the sheet-typography flow reads (the text-carrying
    /// ones: paragraphs, headings, list items and lists, quotes); containers keep the calibrated flow's.</summary>
    private static bool IsTextElementTag(string tag) => tag.ToLowerInvariant() is
        "p" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "li" or "ul" or "ol" or "dl" or "dd" or "dt"
        or "blockquote" or "figure" or "pre";
}
