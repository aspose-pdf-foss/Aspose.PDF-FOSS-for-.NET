using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>One arm of ParseBlocks' token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void HandleBlockOpen(ParseBlocksState pb, Token tok, string tag, IReadOnlyDictionary<string, Dictionary<string, string>>? css, IReadOnlyList<BeforeMarker>? beforeMarkers, bool floatFlow, bool absSpanLedger, bool articleRhythm, bool bandDialect, bool browserUa, bool containerBoxIndents, bool controlBoxes, bool coverStyles, bool divBandBg, bool html5UaHeadings, bool metricLayout, bool msoParagraphs, bool spanPtTypography, bool uaBlockRhythm, bool uaDefaults, bool uaPMargins)
    {
        // Browser-UA flow: a self-closed <p/> is an EMPTY paragraph —
        // its UA margin max-collapses onto the next block; nothing is
        // pushed. A real <p> open is counted so a matching </p> is told
        // apart from the stray-close quirk above.
        if (browserUa && tag.Equals("p", StringComparison.OrdinalIgnoreCase))
        {
            if (tok.IsSelfClosing)
            {
                Flush(pb, controlBoxes, uaBlockRhythm, articleRhythm, spanPtTypography, false, pb.styleStack.Peek());
                pb.pendingEmptyPMarginPt = Math.Max(pb.pendingEmptyPMarginPt,
                    UaBlockMarginEm * pb.styleStack.Peek().FontSize);
                return;
            }
            pb.pOpenDepth++;
        }
        // Metric flow: a body-level <p> opens one UA block margin above
        // it (the same 1.12 em the browser flow gives every paragraph).
        else if (metricLayout && uaPMargins && tag.Equals("p", StringComparison.OrdinalIgnoreCase)
                 && !tok.IsSelfClosing)
            pb.pendingEmptyPMarginPt = Math.Max(pb.pendingEmptyPMarginPt,
                UaBlockMarginEm * pb.styleStack.Peek().FontSize);
        // A div the sheet explicitly sets `display:inline` — directly or
        // through a descendant rule from an enclosing class
        // (`.content-center-text .bold { display:inline }`, the panel
        // header) — rides the current line like a span: no flush, no
        // block break, no style push.
        if (articleRhythm && css is not null
            && tag.Equals("div", StringComparison.OrdinalIgnoreCase)
            && tok.Attributes is not null
            && tok.Attributes.TryGetValue("class", out var inlDivCls))
        {
            var divInline = false;
            foreach (var c in inlDivCls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (css.TryGetValue("." + c, out var idr)
                    && idr.TryGetValue("display", out var idd)
                    && idd.Trim().Equals("inline", StringComparison.OrdinalIgnoreCase))
                    divInline = true;
                for (var di = pb.divClassStack.Count - 1; di >= 0 && !divInline; di--)
                    foreach (var ec in pb.divClassStack[di].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                        if (css.TryGetValue("." + ec + " ." + c, out var edr)
                            && edr.TryGetValue("display", out var edd)
                            && edd.Trim().Equals("inline", StringComparison.OrdinalIgnoreCase))
                        { divInline = true; break; }
                if (divInline) break;
            }
            if (divInline)
            {
                pb.inlineDivDepth++;
                return;
            }
        }
        // Start a new block: flush any pending inline text at the
        // outer style, then push the new style.
        Flush(pb, controlBoxes, uaBlockRhythm, articleRhythm, spanPtTypography, false,pb.styleStack.Peek());
        pb.inlineRunId = 0; pb.runPrevWasControl = false;
        var parent = pb.styleStack.Peek();
        var style = new BlockStyle
        {
            BlocksAtOpen = pb.blocks.Count,
            FontSize = parent.FontSize,
            FontRes = parent.FontRes,
            FontFamily = parent.FontFamily,
            // The stack travels with the family it resolved, so a child can fall
            // through it too when the named face is not installed.
            FontFamilyStack = parent.FontFamilyStack,
            MarginTop = 0,
            MarginBottom = 0,
            LeftIndent = parent.LeftIndent,
            BillPadPt = parent.BillPadPt,
            CardShadowColor = parent.CardShadowColor,
            CardChromePt = parent.CardChromePt,
            FormDialect = parent.FormDialect,
            ParentFontSize = parent.FontSize,
            WidthFrac = parent.WidthFrac,
            WidthPx = parent.WidthPx,
            AlignRight = parent.AlignRight,
            // A float is inherited by the boxes inside it: the image that
            // actually gets taken out of the flow is usually nested a few
            // wrappers below the element the rule names.
            FloatLeft = parent.FloatLeft,
            FloatRight = parent.FloatRight,
            ArticleRhythm = parent.ArticleRhythm,
            UaSerif = parent.UaSerif,
        };
        // A container's pending padding-top spaces the FIRST block that
        // actually flushes — hand it to the child opening now so a <p>
        // inside a padded div does not orphan it on the div's own style.
        if (uaPMargins && parent.PadTop > 0)
        {
            style.PadTop += parent.PadTop;
            parent.PadTop = 0;
        }
        // A wrapper's OWN padding-top LONGHAND is box space before its first
        // child (probed: the certificate sheet's 20px Title band pad is in
        // the expected h1 seat). Handed down the open chain, it lands on
        // the first block that flushes; a wrapper that stays childless keeps
        // it for the empty-close spacer instead. The `padding:` SHORTHAND
        // stays out on purpose — a shorthand-padded container's pad is NOT
        // spent (the print-grid sheet's 30px container pins that).
        if (parent.OwnPadTopPt > 0)
        {
            style.OwnPadTopPt += parent.OwnPadTopPt;
            parent.OwnPadTopPt = 0;
        }
        ApplyBlockTagStyle(tag, style, uaDefaults, browserUa, bandDialect, uaBlockRhythm,
            articleRhythm, msoParagraphs, emHeadings: floatFlow,
            html5UaHeadings: html5UaHeadings);
        // pt-styled fragment: headings carry NO extra margins — the
        // reference stacks the h2 title line-on-line with its nbsp
        // spacer paragraphs (the spans size it; the tag only bolds).
        if (spanPtTypography && tag.Length == 2
            && (tag[0] is 'h' or 'H') && char.IsDigit(tag[1]))
        {
            style.MarginTop = 0;
            style.MarginBottom = 0;
        }
        // Redline: a paragraph's 1-3 value margin shorthand opens a top
        // margin (`margin: 8pt 0pt 0pt`), and a positive text-indent
        // shifts its (single) line right.
        if (spanPtTypography && tok.Attributes is { } rlpAttrs
            && rlpAttrs.TryGetValue("style", out var rlpSt) && rlpSt is not null)
        {
            if (style.MarginTop <= 0
                && Regex.Match(rlpSt, @"(?<![-\w])margin\s*:\s*([\d.]+)\s*pt",
                    RegexOptions.IgnoreCase) is { Success: true } rlpM
                && double.TryParse(rlpM.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var rlpMt)
                && rlpMt > 0)
                style.MarginTop = rlpMt;
            if (Regex.Match(rlpSt, @"text-indent\s*:\s*([\d.]+)\s*pt",
                    RegexOptions.IgnoreCase) is { Success: true } rlpTi
                && double.TryParse(rlpTi.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var rlpTiv)
                && rlpTiv > 0)
                style.TextIndentPt = rlpTiv;
        }
        // Redline divider: a paragraph's border-top LONGHANDS declare
        // the cover rule (border-top-style: solid; -width: 4.5pt) — the
        // shorthand parser never sees the spelled-out triplet.
        if (spanPtTypography && tok.Attributes is { } btAttrs
            && btAttrs.TryGetValue("style", out var btSt) && btSt is not null
            && Regex.IsMatch(btSt, @"border-top-style\s*:\s*solid", RegexOptions.IgnoreCase))
        {
            style.BorderTopOnly = true;
            style.BorderWidth = Regex.Match(btSt, @"border-top-width\s*:\s*([\d.]+)\s*pt",
                    RegexOptions.IgnoreCase) is { Success: true } btw
                && double.TryParse(btw.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var btwv)
                ? btwv : 0.75;
            style.BorderColor = Regex.Match(btSt, @"border-top-color\s*:\s*([^;]+)",
                    RegexOptions.IgnoreCase) is { Success: true } btc
                ? ParseCssColor(btc.Groups[1].Value.Trim()) ?? Color.FromArgb(0, 0, 0)
                : Color.FromArgb(0, 0, 0);
        }
        // pt-styled fragment: a flow paragraph's pt margin shorthand
        // (top right bottom left) insets its wrap box — these paragraphs
        // open at 96 + 1.7 and wrap 6.4 inside
        // the right content edge.
        if (spanPtTypography && tok.Attributes is { } ptpAttrs
            && ptpAttrs.TryGetValue("style", out var ptpSt) && ptpSt is not null
            && Regex.Match(ptpSt,
                @"margin\s*:\s*[\d.]+pt\s+([\d.]+)pt\s+[\d.]+pt\s+([\d.]+)pt",
                RegexOptions.IgnoreCase) is { Success: true } ptpM)
        {
            var ptpCi = System.Globalization.CultureInfo.InvariantCulture;
            if (double.TryParse(ptpM.Groups[2].Value,
                    System.Globalization.NumberStyles.Float, ptpCi, out var ptpL))
                style.LeftIndent += ptpL;
            if (double.TryParse(ptpM.Groups[1].Value,
                    System.Globalization.NumberStyles.Float, ptpCi, out var ptpR))
                style.RightInsetPt = ptpR;
        }
        // A sheet rule addressed at this block — `p.MsoNormal { margin:
        // 0cm; margin-bottom: .0001pt }` (the Word-filtered idiom) or a
        // bare element rule — replaces the UA paragraph margins: the
        // sheet authors its own rhythm.
        if (browserUa && css is not null)
        {
            Dictionary<string, string>? bmRule = null;
            if (tok.Attributes is { } bmAttrs0
                && bmAttrs0.TryGetValue("class", out var bmCls) && bmCls is not null)
                foreach (var pc in bmCls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                    if (css.TryGetValue(tag.ToLowerInvariant() + "." + pc, out bmRule)
                        || css.TryGetValue("." + pc, out bmRule))
                        break;
            if (bmRule is null && css.TryGetValue(tag.ToLowerInvariant(), out var bmBare))
                bmRule = bmBare;
            if (bmRule is not null && (bmRule.ContainsKey("margin")
                || bmRule.ContainsKey("margin-top") || bmRule.ContainsKey("margin-bottom")))
            {
                var bmSb = new StringBuilder();
                foreach (var kv in bmRule)
                    bmSb.Append(kv.Key).Append(':').Append(kv.Value).Append(';');
                var bmDecl = bmSb.ToString();
                var bmBox = ParseInlineMarginBox(bmDecl, style.FontSize);
                if (bmRule.ContainsKey("margin") || bmRule.ContainsKey("margin-top"))
                    style.MarginTop = bmBox.top;
                if (bmRule.ContainsKey("margin") || bmRule.ContainsKey("margin-bottom"))
                    style.MarginBottom = bmBox.bottom;
                // …and a class rule's margin-left indents the block.
                if (bmBox.left > 0) style.LeftIndent += bmBox.left;
            }
            // …and its typography: a PERCENT font-size resolves against
            // the inherited size (h1 { font-size: 120% } = 14.4 on the
            // UA base), a length replaces it, and a RESOLVABLE family
            // rides the block's runs (h6 { font-family: Verdana }).
            if (bmRule is not null)
            {
                if (bmRule.TryGetValue("font-size", out var bmFsV))
                {
                    var bmFs = bmFsV.Trim();
                    if (bmFs.EndsWith("%", StringComparison.Ordinal)
                        && double.TryParse(bmFs.TrimEnd('%'),
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out var bmPct) && bmPct > 0)
                        style.FontSize *= bmPct / 100.0;
                    else if (TryParseCssFontSize(bmFs, out var bmPt) && bmPt > 0)
                        style.FontSize = bmPt;
                }
                if (bmRule.TryGetValue("font-family", out var bmFamV)
                    && FirstFontFamily(bmFamV) is { Length: > 0 } bmFam
                    && WinMetricsFor(bmFam) is not null)
                    style.FontFamily = bmFam;
                if (bmRule.TryGetValue("font-weight", out var bmFwV)
                    && (bmFwV.Trim() is "bold" or "bolder"
                        || (int.TryParse(bmFwV.Trim(), out var bmFwN) && bmFwN >= 600)))
                    style.FontRes = "F2";
                if (bmRule.TryGetValue("text-align", out var bmTaV))
                {
                    var bmTa = bmTaV.Trim().ToLowerInvariant();
                    if (bmTa == "center") style.AlignCenterAttr = true;
                    else if (bmTa == "justify") style.AlignJustify = true;
                }
            }
        }
        // The sheet's own element reset ("h1, h2, …, p { margin: 0 }") beats
        // the legacy calibrated heading/paragraph margins — the widget card
        // measures its header purely from the class-rule chrome
        // (containerBoxIndents mode only).
        if (containerBoxIndents && css is not null
            && css.TryGetValue(tag.ToLowerInvariant(), out var tagReset)
            && tagReset.TryGetValue("margin", out var tagResetMargin)
            && Regex.IsMatch(tagResetMargin.Trim(), @"^0(px)?(\s+0(px)?){0,3}$"))
        {
            style.MarginTop = 0;
            style.MarginBottom = 0;
        }
        // Control-box dialect: headings render at the UA scale of the 12 pt
        // base with the dialect's heading gaps (27.34 pt above
        // an h3 = 13.5 line + 13.84 margin; 25.97 below = 16.5 line + 9.47).
        if (controlBoxes && tag.ToLowerInvariant() is "h1" or "h2" or "h3" or "h4" or "h5" or "h6")
        {
            style.FontSize = tag.ToLowerInvariant() switch
            {
                "h1" => 24, "h2" => 18, "h3" => 14.039, "h4" => 12,
                "h5" => 9.96, _ => 8.04,
            };
            style.FontRes = "F2";
            style.MarginTop = 13.84;
            style.MarginBottom = 9.47;
        }
        pb.divClassStack.Add(tok.Attributes is not null
            && tok.Attributes.TryGetValue("class", out var openCls) ? openCls : "");
        // Container box chrome from CLASS rules (containerBoxIndents mode):
        // padding+border-left indent the content; the vertical chrome stacks
        // onto the next block's top margin; a class-rule HEIGHT (the widget
        // header band) floors the next block's height. A width:100%
        // container's horizontal chrome overflows its parent (CSS content-box:
        // its content box equals the parent's, the chrome paints outside), so
        // it indents but must NOT bill the page-widen; width:auto chrome does.
        if (containerBoxIndents && css is not null && !string.IsNullOrEmpty(pb.divClassStack[^1]))
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
                if (css.TryGetValue("." + bc, out var bcr)) ReadBoxRule(bcr);
            // Compound two-class selectors (".card.default { border: … }").
            foreach (var ca in bxClasses)
                foreach (var cb in bxClasses)
                    if (!ReferenceEquals(ca, cb) && css.TryGetValue("." + ca + "." + cb, out var ccr))
                        ReadBoxRule(ccr);
            if (bxPadL + bxBorder > 0) style.LeftIndent += bxPadL + bxBorder;
            if (bxPadT + bxBorder > 0) pb.pendingBoxPadTop += bxPadT + bxBorder;
            if (bxHeight > 0) pb.pendingBoxHeight = Math.Max(pb.pendingBoxHeight, bxHeight);
            if (!bxPctWidth) style.BillPadPt += bxPadL + bxPadR + 2 * bxBorder;
            // A box-shadow'd container is the widget CARD: remember its shadow
            // colour and its own chrome so the chart image can frame it.
            if (bxShadow is not null)
            {
                style.CardShadowColor = bxShadow;
                style.CardChromePt = bxPadL + bxBorder;
            }
        }
        // Band annotation injected by the print-grid pre-pass (the ancestry was
        // resolved before segmentation split the host div away).
        if (browserUa && tok.Attributes is not null
            && tok.Attributes.TryGetValue("band", out var bandSpec))
        {
            var bandParts = bandSpec.Split('|');
            var rgbParts = bandParts[0].Split(',');
            if (rgbParts.Length == 3
                && int.TryParse(rgbParts[0], out var bandR)
                && int.TryParse(rgbParts[1], out var bandG)
                && int.TryParse(rgbParts[2], out var bandB))
            {
                style.BandColor = Color.FromRgbBytes(bandR, bandG, bandB);
                style.BandPx = bandParts.Length > 1 && double.TryParse(bandParts[1],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var bandPxA) ? bandPxA : 1;
                style.BandPadPx = bandParts.Length > 2 && double.TryParse(bandParts[2],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var bandPadA) ? bandPadA : 0;
            }
        }
        // A ".cls h4"-style descendant rule with a border-bottom paints a band
        // under the heading (the print-grid section-header underline).
        else if (browserUa && css is not null && tag.ToLowerInvariant() is "h4" or "h3" or "h2")
        {
            for (var di = pb.divClassStack.Count - 1; di >= 0 && style.BandColor is null; di--)
                foreach (var c in pb.divClassStack[di].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                    if (css.TryGetValue("." + c + " " + tag.ToLowerInvariant(), out var bandRule)
                        && bandRule.TryGetValue("border-bottom", out var bandDecl))
                    {
                        var bw = Regex.Match(bandDecl, @"(\d+(?:\.\d+)?)\s*px");
                        style.BandColor = ParseCssColor(bandDecl);
                        style.BandPx = bw.Success ? double.Parse(bw.Groups[1].Value,
                            System.Globalization.CultureInfo.InvariantCulture) : 1;
                        if (bandRule.TryGetValue("padding-bottom", out var bandPad)
                            && TryParseLength(bandPad, out var bandPadPt))
                            style.BandPadPx = bandPadPt / 0.75;
                        break;
                    }
        }
        // Browser-UA flow: a div's style="width:N%" narrows the wrap box (the
        // expected render stacks such divs but wraps at the declared width),
        // and its padding-top is non-collapsing space above the content.
        if (browserUa && tok.Attributes is not null
            && tok.Attributes.TryGetValue("style", out var uaSt) && !string.IsNullOrEmpty(uaSt))
        {
            var uwm = Regex.Match(uaSt, @"(?:^|[;\s])width\s*:\s*(\d+(?:\.\d+)?)\s*%");
            if (uwm.Success && double.TryParse(uwm.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var uwPct)
                && uwPct is > 0 and < 100)
                style.WidthFrac = uwPct / 100.0;
            // Shorthand only — the padding-top LONGHAND is OwnPadTopPt's
            // (parsed below for every flow): it cascades down wrapper
            // opens and the first flushing block spends it once.
            var upm = Regex.Match(uaSt, @"(?<![-\w])padding\s*:\s*(\d+(?:\.\d+)?)\s*(px|pt)");
            if (upm.Success && double.TryParse(upm.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var upPx)
                && upPx > 0)
                style.PadTop += upm.Groups[2].Value.Equals("pt", StringComparison.OrdinalIgnoreCase)
                    ? upPx : upPx * 0.75;
        }
        // Metric flow: a div's inline padding-top is real space above its
        // first block (the newsletter's #body_style 7px frame).
        else if (metricLayout && uaPMargins && tag.Equals("div", StringComparison.OrdinalIgnoreCase)
            && tok.Attributes is not null
            && tok.Attributes.TryGetValue("style", out var mpSt) && !string.IsNullOrEmpty(mpSt))
        {
            var mpm = Regex.Match(mpSt, @"padding(?:-top)?\s*:\s*(\d+(?:\.\d+)?)\s*px");
            if (mpm.Success && double.TryParse(mpm.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var mpPx)
                && mpPx > 0)
                style.PadTop += mpPx * 0.75;
        }
        // The element's OWN padding-top LONGHAND (any flow, longhand only —
        // the `padding:` shorthand stays with each dialect's own rules): fuel
        // for the childless-empty close spacer alone. Probed (bench d1): an
        // empty <div style="padding-top:70px"></div> between two paragraphs
        // is real box space; a padded element WITH content
        // keeps its dialect's existing behaviour untouched.
        if (!(metricLayout && uaPMargins)
            && tok.Attributes is not null
            && tok.Attributes.TryGetValue("style", out var optSt) && !string.IsNullOrEmpty(optSt)
            && Regex.Match(optSt, @"padding-top\s*:\s*(\d+(?:\.\d+)?)\s*(px|pt)",
                RegexOptions.IgnoreCase) is { Success: true } optM
            && double.TryParse(optM.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var optV)
            && optV > 0)
            style.OwnPadTopPt = optM.Groups[2].Value.Equals("pt", StringComparison.OrdinalIgnoreCase)
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
                style.WidthPx = awPx;
        }
        // CSS rules: type selector then class selector(s), each overriding the
        // previous, before the inline style="…" (highest specificity).
        ApplyCssRules(css, tag, tok.Attributes, style, metricLayout, coverStyles,
            floatFlow: floatFlow);
        // Ledger: a class WIDTH on a block element is that element's box —
        // the wrap/centring frame its lines lay out in.
        if (absSpanLedger && css is not null && tok.Attributes is not null
            && tok.Attributes.TryGetValue("class", out var lgDivCls) && lgDivCls is not null)
            foreach (var dc in lgDivCls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                if (css.TryGetValue("." + dc, out var dcr)
                    && dcr.TryGetValue("width", out var dcw)
                    && Regex.Match(dcw, @"([\d.]+)\s*px", RegexOptions.IgnoreCase)
                        is { Success: true } dcwM
                    && double.TryParse(dcwM.Groups[1].Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var dcwPx))
                    style.WidthPx = dcwPx;
        // Styled-article panel (a div class declaring border + background,
        // the `.td-toc` box): its vertical box is real space — a pad above
        // its header now, the bottom pad + panel margin when it closes.
        if (articleRhythm && css is not null
            && tag.Equals("div", StringComparison.OrdinalIgnoreCase)
            && tok.Attributes is not null
            && tok.Attributes.TryGetValue("class", out var panelCls))
            foreach (var pc in panelCls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                if (css.TryGetValue("." + pc, out var panelRule)
                    && panelRule.ContainsKey("border")
                    && (panelRule.ContainsKey("background-color")
                        || panelRule.ContainsKey("background")))
                {
                    pb.blocks.Add(new Block
                    {
                        Text = "", IsHardBreak = true,
                        ExplicitHeight = ArticlePanelPadTopPt,
                        FontSize = style.FontSize,
                    });
                    style.MarginBottom +=
                        ArticlePanelPadBottomPt + ArticlePanelMarginBottomPt;
                    break;
                }
        // Inline style="…" overrides tag defaults: if the author
        // explicitly set padding-left / margin-left we drop the
        // list-style indent the tag default added so that e.g.
        // `<ol style="padding-left:0">` sits flush with body text.
        if (HasInlineIndentOverride(tok.Attributes))
            style.LeftIndent = parent.LeftIndent;
        ApplyInlineStyle(tok.Attributes, style);
        // Pinned-body report band: the wrapper's paint reaches the
        // inline-block child that carries the band's text (see divBandBg),
        // and the two padded div levels (the sheet's `div { padding: 4px }`
        // on wrapper AND child) reserve their pad above the line. Runs
        // AFTER the inline style so the wrapper's own background/margins
        // are already resolved.
        if (divBandBg)
        {
            // A painted child of a painted wrapper carries the wrapper's
            // top margin too — the band's text flushes under the CHILD's
            // style, where a fresh zero would drop the wrapper's
            // `margin-top: 5px`.
            if (parent.BackgroundColor is not null && parent.MarginTop > style.MarginTop)
                style.MarginTop = parent.MarginTop;
            style.BackgroundColor ??= parent.BackgroundColor;
            style.ForeColor ??= parent.ForeColor;
            if (style.BackgroundColor is not null && style.BandPadPt <= 0
                && css is not null && css.TryGetValue("div", out var dbr)
                && dbr.TryGetValue("padding", out var dbp)
                && TryParseLength(dbp, out var dbpPt) && dbpPt > 0)
            {
                style.BandPadPt = 2 * dbpPt;
                style.MarginTop = Math.Max(style.MarginTop, style.BandPadPt);
                // No bottom margin: the flow hands the cursor back at the
                // fill's bottom edge when the band closes (see the band
                // rewind in the render loop) — the next element's own
                // margin-top is the whole gap.
            }
            // A painted panel's own inline margin-top is REAL space above
            // its box, on top of the pad the fill reserves (the report's
            // `margin-top: 5px` boxes).
            if (style.BackgroundColor is not null && tok.Attributes is not null
                && tok.Attributes.TryGetValue("style", out var dbSt) && dbSt is not null
                && Regex.Match(dbSt, @"(?<![-\w])margin-top\s*:\s*([\d.]+)\s*px",
                    RegexOptions.IgnoreCase) is { Success: true } dbMt
                && double.TryParse(dbMt.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var dbMtPx)
                && dbMtPx > 0)
                style.MarginTop += dbMtPx * 0.75;
            // An EMPTY div is the report's authored spacer ("<!--empty
            // divs are for spacing--><div></div>") — remember this open
            // so its close can emit the padding box it renders as.
            pb.emptyDivDepthMark = tag.Equals("div", StringComparison.OrdinalIgnoreCase)
                && (tok.Attributes is null || !tok.Attributes.ContainsKey("style"))
                ? pb.styleStack.Count + 1 : -1;
            pb.emptyDivBlocksAt = pb.blocks.Count;
            pb.emptyDivTextAt = pb.currentText.Length;
        }
        // A border-TOP-only element is a DIVIDER: one rule above its
        // content, emitted as its own marker block so the border never
        // rides the element's text blocks.
        if (browserUa && style.BorderTopOnly && style.BorderColor is { } tdCol
            && style.BorderWidth > 0)
        {
            Flush(pb, controlBoxes, uaBlockRhythm, articleRhythm, spanPtTypography, false, pb.styleStack.Peek());
            pb.blocks.Add(new Block
            {
                Text = "", IsHardBreak = true,
                BorderTopOnly = true, BorderColor = tdCol,
                BorderWidth = style.BorderWidth,
                // the divider wrapper's padding-top is box space under
                // the rule, before its content
                PadTop = style.PadTop,
            });
            style.BorderColor = null;
            style.BorderWidth = 0;
            style.BorderTopOnly = false;
            style.PadTop = 0;
        }
        // Border-only declared box (browser-UA flow): inline width+height+
        // border with no background. The box is handed to the first block
        // that flushes inside this element (style's own height/border are
        // cleared so the close emits no trailing spacer and the line-box
        // border model stays off).
        if (browserUa && pb.pendingBorderBox is null
            && style.BorderWidth > 0 && style.BorderColor is not null
            && style.BackgroundColor is null && style.BgBoxHeightPt <= 0
            && style.ExplicitHeight > 0
            && tok.Attributes is not null
            && tok.Attributes.TryGetValue("style", out var bbSt) && bbSt is not null
            && Regex.Match(bbSt, @"(?<![-\w])width\s*:\s*([^;""']+)",
                RegexOptions.IgnoreCase) is { Success: true } bbW
            && TryParseLength(bbW.Groups[1].Value.Trim(), out var bbWPt))
        {
            pb.pendingBorderBox = (bbWPt, style.ExplicitHeight, style.BorderWidth,
                style.BorderColor, style.BorderRadiusPt);
            pb.pendingBorderBoxDepth = pb.styleStack.Count + 1;
            style.ExplicitHeight = 0;
            style.BorderWidth = 0;
            style.BorderColor = null;
        }
        // The legacy ALIGN attribute: justify stretches word gaps at draw time,
        // center centres each measured line — both layout-neutral (wrap points
        // and pagination are unchanged).
        if (tok.Attributes is not null && tok.Attributes.TryGetValue("align", out var alignAttr))
        {
            var alignVal = alignAttr.Trim();
            if (alignVal.Equals("justify", StringComparison.OrdinalIgnoreCase))
                style.AlignJustify = true;
            else if (alignVal.Equals("center", StringComparison.OrdinalIgnoreCase))
                style.AlignCenterAttr = true;
        }
        // An element opening with page-break-before must break even when it emits
        // no block itself (the `<div style="page-break-before:always"></div>` idiom):
        // carry the break to whatever block flushes next.
        if (style.PageBreakBefore) pb.pendingPageBreak = true;
        // List context: an <ol>/<ul> style carries a counter its <li> children
        // draw from; an <li> takes the next marker from its enclosing list. A list
        // whose CSS supplies its own `li:nth-child(..)::before { content }` markers uses
        // those (indexed by child position) instead of the numeric/bullet default.
        if (tag is "ol" or "ul")
        {
            style.ListKind = tag == "ol" ? 1 : 2;
            // A list nested inside another list keeps NO block margin (the
            // UA `ol ol, ul ul { margin-block-start: 0 }` reset — probed:
            // nested items continue at bare line pitch). Browser-UA flow
            // only; the legacy calibrated flows keep their stacking.
            if (browserUa)
                foreach (var anc in pb.styleStack)
                    if (anc.ListKind != 0)
                    {
                        style.MarginTop = 0;
                        style.MarginBottom = 0;
                        break;
                    }
            // Root stack depth 1 = body level; anything deeper means the
            // list opened inside another block element (div/h1/…).
            style.ListNestedInBlock = pb.styleStack.Count > 1;
            if (tag == "ol")
            {
                style.ListCounter = ParseListStart(tok.Attributes);
                // list-style-type from the list's own inline style (or the
                // legacy type= attribute): alpha/roman ordinals instead of
                // the decimal default.
                style.ListStyleType = "";
                if (tok.Attributes is not null)
                {
                    if (tok.Attributes.TryGetValue("style", out var olSt) && olSt is not null
                        && Regex.Match(olSt,
                            @"list-style-type\s*:\s*(upper-alpha|lower-alpha|upper-roman|lower-roman|upper-latin|lower-latin)",
                            RegexOptions.IgnoreCase) is { Success: true } lst)
                        style.ListStyleType = lst.Groups[1].Value.ToLowerInvariant()
                            .Replace("latin", "alpha");
                    else if (tok.Attributes.TryGetValue("type", out var olTy))
                        style.ListStyleType = olTy?.Trim() switch
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
            if (articleRhythm && css is not null)
            {
                Dictionary<string, string>? tocList = null, tocLink = null;
                for (var di = pb.divClassStack.Count - 1;
                     di >= 0 && tocList is null; di--)
                    foreach (var c in pb.divClassStack[di].Split((char[]?)null,
                                 StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (tocList is null
                            && css.TryGetValue("." + c + " ol", out var clr)
                            && clr.ContainsKey("list-style-type"))
                            tocList = clr;
                        if (tocLink is null
                            && css.TryGetValue("." + c + " a", out var cla)
                            && cla.ContainsKey("padding-bottom"))
                            tocLink = cla;
                    }
                if (tocList is not null
                    && tocList["list-style-type"].Trim()
                        .Equals("disc", StringComparison.OrdinalIgnoreCase))
                    style.ListKind = 2;
                if (tocList is not null || tocLink is not null)
                {
                    // Panel list geometry: replace the plain-article indent
                    // with the panel's own (level 1 at 33.3, +36 per level),
                    // and drop the article list margin — panel items pitch
                    // uniformly across nesting boundaries (measured 24
                    // between EVERY pair, group ends included).
                    style.LeftIndent += ArticleTocIndentPt - ArticleListIndentPt
                        + (parent.ListKind != 0 ? ArticleTocLevelPt - ArticleTocIndentPt : 0);
                    style.MarginBottom = 0;
                    style.TocLinkPadPt = tocLink is not null
                        && TryParseLength(tocLink["padding-bottom"], out var tlp)
                        ? tlp : 0;
                }
            }
            if (parent.TocLinkPadPt > 0) style.TocLinkPadPt = parent.TocLinkPadPt;
            style.BeforeRules = ResolveListBeforeRules(beforeMarkers,
                tok.Attributes is not null && tok.Attributes.TryGetValue("class", out var lc) ? lc : null);
            style.ChildIndex = 0;
        }
        else if (tag == "li" && parent.ListKind != 0)
        {
            // The enclosing list's own top margin lands on its FIRST item
            // block (one-shot). At the document top a body-level list's
            // margin then vanishes with the other UA defaults, but a list
            // nested inside another block keeps it like an authored margin
            // — max-collapsed with the UA body margin (probed on div- and
            // h1..h3-wrapped lists). Browser-UA flow only; the legacy
            // calibrated flows keep their line-on-line stacking.
            if (browserUa && parent.MarginTop > 0)
            {
                style.MarginTop = parent.MarginTop;
                if (parent.ListNestedInBlock) style.MarginTopAuthored = true;
                parent.MarginTop = 0;
            }
            // Panel items pitch one line box + the link's block pad.
            if (articleRhythm && parent.TocLinkPadPt > 0)
                style.MarginBottom = parent.TocLinkPadPt;
            parent.ChildIndex++;
            BeforeMarker? before = null;
            if (parent.BeforeRules is not null)
                foreach (var r in parent.BeforeRules)
                    if (r.Matches(parent.ChildIndex)) { before = r; break; }
            if (before is not null)
            {
                // CSS-supplied generated marker (list-style:none + ::before): render it as
                // its own run AFTER the item text so, on an RTL line, the text is the earlier
                // fragment and the marker the later one.
                pb.pendingMarker = before.Content;
                pb.pendingMarkerAfter = true;
            }
            else if (parent.BeforeRules is null)
            {
                // No CSS markers for this list → ordinal (decimal, or the
                // list's alpha/roman list-style-type) / bullet default.
                pb.pendingMarker = parent.ListKind == 1
                    ? FormatListOrdinal(++parent.ListCounter, parent.ListStyleType) + "."
                    : "•";
                pb.pendingMarkerAfter = false;
            }
            // BeforeRules present but no rule matched this index → no marker.
        }
        // A declared height/min-height opens a FLOOR: remember the flow
        // cursor here, and at the element's close drop it to (start - H)
        // if the content has not already reached that far.
        if (style.HeightFloorPt > 0)
        {
            style.HeightFloorDeferred = true;
            pb.heightFloors.Push((pb.blocks.Count, style.HeightFloorPt, pb.styleStack.Count + 1));
            pb.blocks.Add(new Block
            {
                Text = "",
                HeightFloorStart = true,
                FontSize = style.FontSize,
                FontRes = style.FontRes,
                LeftIndent = style.LeftIndent,
            });
        }
        pb.styleStack.Push(style);
    }
}
