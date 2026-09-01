using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    // Continuation pages of the escaped-attr dialect start at the REAL page margin
    // plus one 0.9em first-baseline drop. The dialect's nominal top margin is a
    // page-1 calibration — it bakes in the UA body inset and the first line's
    // ascent — so reusing it on every later page starts each one 6.2 pt low.
    /// <summary>Top of a continuation page for the flow.</summary>
    private static double FreshPageTopY(bool escapedAttrDoc, double pageHeight, double marginTop)
        => escapedAttrDoc
        ? pageHeight - 72 - 0.9 * 12
        : pageHeight - marginTop;

    // Shared control placement: the AcroForm field plus its visible box at
    // (xLeft, baseline). Under the control-box dialect the box STRADDLES its
    // line — top edge above the text baseline, bottom hanging just under
    // it. The legacy dialects keep
    // their calibrated top-at-cursor box. Used by the standalone control
    // branch, the inline-run layout, and the dialect grid's in-cell controls.
    private static void EmitControlAt(Block ctl, double xLeft, double baseY,
        HtmlFlowCursor flow, Document doc, double lineHeight, double? aboveOverride = null)
        {
            var cW = ctl.InputWidth > 0
                ? System.Math.Min(ctl.InputWidth, flow.contentWidth - ctl.LeftIndent)
                : flow.contentWidth - ctl.LeftIndent;
            var cH = ctl.InputHeight > 0 ? ctl.InputHeight : lineHeight;
            var above = aboveOverride ?? (!ctl.InputDrawValue ? 0
                : ctl.IsSelectBox ? SelectBoxAboveBaselinePt : InputBoxAboveBaselinePt);
            var lx = xLeft + (ctl.IsSelectBox ? SelectSideBearingPt : 0);
            var field = new Forms.TextBoxField(flow.page,
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
            doc.Form.Add(field, flow.page.Number);
            // Draw a visible border box for the input so it reads as a form field
            // in the rendered page (the widget's own appearance is not rasterised).
            // The 1 pt stroke runs HALF A POINT INSIDE the widget rect, so
            // the visible box is exactly the widget's 15.75 — stroking the rect
            // itself runs a point taller and crowds the label below.
            if (ctl.InputDrawValue)
                DrawBox(flow.page, lx + 0.5, baseY + above - cH + 0.5, cW - 1, cH - 1,
                    border: Color.Black, borderWidth: 1.0, fill: null);
            else
                DrawBox(flow.page, lx, baseY + above - cH, cW, cH,
                    border: Color.FromArgb(130, 130, 130), borderWidth: 0.75, fill: null);
        }

    // A positioned serif fragment for the escaped-attr dialect: the REAL
    // TimesNewRoman faces, embedded Type0 (their glyph shapes are what
    // reaches the rendered page); Standard-14 serif as the fallback.
    private static void EmitSerifRun(string text, string res, double pt, double x, double baseY,
        HtmlFlowCursor flow)
        {
            var famName = res == "F6" ? "Times New Roman Bold"
                : res == "F7" ? "Times New Roman Italic" : "Times New Roman";
            var baseName = res == "F6" ? "TimesNewRomanBold"
                : res == "F7" ? "TimesNewRomanItalic" : "TimesNewRoman";
            if (PosFace(famName).ttf is { } srTtf
                && flow.page.Dict.Get("Resources") is Core.PdfDictionary srRes
                && srRes.Get("Font") is Core.PdfDictionary srFd)
            {
                var (rn, hex) = Text.Type0FontEmbedder.Embed(srFd, srTtf, baseName,
                    text, stripSpacesInBaseFont: true);
                flow.page.AddContentStream(Encoding.ASCII.GetBytes(FormattableString.Invariant(
                    $"BT /{rn} {pt:0.##} Tf 1 0 0 1 {x:0.##} {baseY:0.##} Tm <{System.Convert.ToHexString(hex)}> Tj ET\n")));
            }
            else
                flow.page.AddContentStream(Encoding.ASCII.GetBytes(FormattableString.Invariant(
                    $"BT /{res} {pt:0.##} Tf 1 0 0 1 {x:0.##} {baseY:0.##} Tm ({EscapePdfString(text)}) Tj ET\n")));
        }

    /// <summary>Lays out one input-field or inline-items block and advances the flow past it.</summary>
    /// <remarks>Lifted verbatim out of the block-dispatch loop in
    /// <see cref="ConvertFromHtml"/>.</remarks>
    private static void LayoutInputFieldBlock(
        Block block, HtmlFlowCursor flow, HtmlDocProfile profile, Document doc, Core.PdfDictionary docFontDict, double marginBottom, double marginLeft, double marginTop, double pageHeight, double pageWidth, double blockFontSize, double lineHeight)
    {
            // An inline run: label text and controls share wrapping line boxes
            // with a pen, so label|input|label|select rows stay inline.
            if (block.InlineItems is { Count: > 0 } runItems)
            {
                // Directly under a section rule the run drops extra so its
                // control boxes clear the rule (baseline rule+17.2, not +11.9).
                if (flow.afterEscapedRule) { flow.y -= RuleToRunExtraPt; flow.afterEscapedRule = false; }
                var lineLeft = marginLeft;
                var lineRight = marginLeft + flow.contentWidth;
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
                            ? System.Math.Min(it.InputWidth, flow.contentWidth) : flow.contentWidth;
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
                if (flow.y - runTotalAdv < marginBottom
                    && runTotalAdv <= FreshPageTopY(profile.escapedAttrDoc, pageHeight, marginTop) - marginBottom
                    && flow.y < FreshPageTopY(profile.escapedAttrDoc, pageHeight, marginTop) - 1e-3)
                {
                    flow.page = doc.Pages.Add(pageWidth, pageHeight);
                    EnsureFonts(flow.page, docFontDict);
                    flow.y = FreshPageTopY(profile.escapedAttrDoc, pageHeight, marginTop); flow.pendingTopDrop = profile.hasZeroTopMargin;
                }
                foreach (var (items, hasText, maxAdv, maxAbove) in runLines)
                {
                    // A line with a mid-line TALL control drops extra first so
                    // the box top clears the content above it.
                    if (maxAbove > InputBoxAboveBaselinePt)
                        flow.y -= maxAbove - InputBoxAboveBaselinePt;
                    // A control line advances by the control's flow cost; carrying
                    // body text beside it adds the descent clearance. Text-only
                    // (wrap remainder) lines keep the normal line box.
                    var adv = maxAdv > 0 ? maxAdv + (hasText ? InlineMixedExtraPt : 0)
                        : NormalLineHeightPt(blockFontSize > 0 ? blockFontSize : EscapedBodyFontPt);
                    if (flow.y - (maxAbove > InputBoxAboveBaselinePt ? SerifDescentRoomPt : adv) < marginBottom)
                    {
                        flow.page = doc.Pages.Add(pageWidth, pageHeight);
                        EnsureFonts(flow.page, docFontDict);
                        flow.y = FreshPageTopY(profile.escapedAttrDoc, pageHeight, marginTop); flow.pendingTopDrop = profile.hasZeroTopMargin;
                    }
                    foreach (var (ctl, txt, x, fpt, res) in items)
                    {
                        if (ctl is not null)
                            EmitControlAt(ctl, x, flow.y, flow, doc, lineHeight,
                                aboveOverride: ctl.InputMultiline && x > lineLeft + 1e-6
                                    && ctl.InputHeight > 0
                                    ? ctl.InputHeight - TextareaBottomHangPt : null);
                        else if (!string.IsNullOrEmpty(txt))
                            EmitSerifRun(txt, res, fpt, x, flow.y, flow);
                    }
                    flow.contentPage = flow.page;
                    flow.y -= adv;
                }
                flow.lastWasHardBreak = false;
                flow.prevFlowMarginBottom = 0;
                flow.prevFlowLineHeight = 0;
                return;   // the block is laid out; the loop this came from would continue
            }

            if (flow.y < pageHeight - marginTop - 1e-3)
                flow.y -= block.MarginTop;
            var fieldH = block.InputHeight > 0 ? block.InputHeight : lineHeight;
            var boxAbove = !block.InputDrawValue ? 0
                : block.IsSelectBox ? SelectBoxAboveBaselinePt : InputBoxAboveBaselinePt;
            if (flow.y + boxAbove - fieldH < marginBottom)
            {
                flow.page = doc.Pages.Add(pageWidth, pageHeight);
                EnsureFonts(flow.page, docFontDict);
                flow.y = FreshPageTopY(profile.escapedAttrDoc, pageHeight, marginTop); flow.pendingTopDrop = profile.hasZeroTopMargin;
            }
            EmitControlAt(block, marginLeft + block.LeftIndent, flow.y, flow, doc, lineHeight);
            flow.y -= (block.InputAdvance > 0 ? block.InputAdvance : fieldH) + block.MarginBottom;
            flow.lastWasHardBreak = false;
    }

    /// <summary>Lays out one image block - floats, bands, placeholders included - and advances the flow past it.</summary>
    /// <remarks>Lifted verbatim out of the block-dispatch loop in
    /// <see cref="ConvertFromHtml"/>.</remarks>
    private static void LayoutImageBlock(
        Block block, HtmlFlowCursor flow, HtmlDocProfile profile, Document doc, Core.PdfDictionary docFontDict, HtmlLoadOptions? options, List<byte[]> inlineSvgs, Stack<(double SavedML, double SavedCW, double TopY, double MinEndY, Page StartPage)> bandStack, double marginBottom, double marginLeft, double marginTop, double pageHeight, double pageWidth, double lineHeight)
    {
            // Form dialect: a block image sits at the preceding text's CSS box
            // bottom plus that block's bottom margin/padding — rewind the legacy
            // full-line-box advance (same correction the <hr> branch makes).
            if (profile.formHorizontalDoc && flow.prevFlowLineHeight > 0)
            {
                flow.y += flow.prevFlowLineHeight - flow.prevFlowFontSize * 0.3;
                flow.prevFlowLineHeight = 0;
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
                    var vpWpx = Math.Max(100.0, flow.contentWidth - 2 * UaBodyMarginPt) / 0.75;
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
                // (CSS px → pt; measured: x = 90 + left·0.75, top = 72 + top·0.75)
                // and leaves the flow — no width clamp, no cursor advance.
                if (profile.uaStdSerif && block.ImageAbsPos)
                {
                    var apX = marginLeft + block.ImageAbsLeftPx * 0.75;
                    var apTop = pageHeight - marginTop - block.ImageAbsTopPx * 0.75;
                    try
                    {
                        flow.page.AddImage(bytes, new Rectangle(apX, apTop - h, apX + w, apTop));
                    }
                    catch { /* undecodable image: skip, keep the flow going */ }
                    flow.contentPage = flow.page;
                    flow.lastWasHardBreak = false;
                    return;   // the block is laid out; the loop this came from would continue
                }
                var availW = flow.contentWidth;
                // an inline %-max-width caps the drawn box at its share of the
                // content width (aspect kept)
                if (block.ImageMaxWFrac > 0 && availW > 0 && w > availW * block.ImageMaxWFrac)
                {
                    h *= availW * block.ImageMaxWFrac / w;
                    w = availW * block.ImageMaxWFrac;
                }
                var rtlOverflow = profile.rtlDoc && availW > 0 && w > availW;
                // Chart-card: the widened page fits the chart at NATURAL size; its
                // indented right edge may pass the content box by the container
                // chrome (it draws there, unclipped) — never downscale.
                if (availW > 0 && w > availW && !rtlOverflow && !profile.chartCardDoc)
                { h *= availW / w; w = availW; }
                var padTop = block.ImagePadTopPx * 0.75;
                var padBottom = block.ImagePadBottomPx * 0.75;
                // Word-filtered pages: an over-tall image draws AT the flow
                // position and CROSSES the page boundary — each continuation
                // page redraws it shifted up by one content band (measured:
                // the snip capture runs 290..1076 across two sheets).
                if (profile.msoFilteredDoc && flow.y - h - padTop - padBottom < marginBottom)
                {
                    flow.y -= padTop;
                    var crossX = block.ImageCentered
                        ? marginLeft + (flow.contentWidth - w) / 2
                        : marginLeft + block.ImageIndentPt;
                    var crossBand = pageHeight - marginTop - marginBottom;
                    var crossInv = System.Globalization.CultureInfo.InvariantCulture;
                    // Each sheet's content clips at the margin
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
                    CrossDraw(flow.page, flow.y, clipTop: false);
                    var crossTop = flow.y;
                    while (crossBand > 0 && crossTop - h < marginBottom - 0.01)
                    {
                        flow.page = doc.Pages.Add(pageWidth, pageHeight);
                        EnsureFonts(flow.page, docFontDict);
                        crossTop += crossBand;
                        CrossDraw(flow.page, crossTop, clipTop: true);
                    }
                    flow.y = crossTop - h - padBottom;
                    flow.contentPage = flow.page;
                    flow.lastWasHardBreak = false;
                    return;   // the block is laid out; the loop this came from would continue
                }
                if (flow.y - h - padTop - padBottom < marginBottom)
                {
                    // Inside a float column the overflow is clipped, not paginated.
                    if (profile.floatBandDoc && bandStack.Count > 0)
                    {
                        flow.bandColClipped = true;
                        flow.lastWasHardBreak = false;
                        return;   // the block is laid out; the loop this came from would continue
                    }
                    flow.page = doc.Pages.Add(pageWidth, pageHeight);
                    EnsureFonts(flow.page, docFontDict);
                    flow.y = pageHeight - marginTop; flow.pendingTopDrop = profile.hasZeroTopMargin;
                }
                flow.y -= padTop;
                // A RIGHT-floated image hangs off the right content edge, inset by
                // its own margin, instead of starting at the flow cursor.
                var imgX = profile.floatImageDoc && block.FloatRight
                    ? marginLeft + flow.contentWidth - w - block.ImageIndentPt
                    : rtlOverflow ? marginLeft + flow.contentWidth - w
                    : block.ImageCentered ? marginLeft + (flow.contentWidth - w) / 2
                    : marginLeft + block.ImageIndentPt;
                // Two floats that do not fit side by side do not overlap: the later
                // one drops to below the earlier one and takes its own edge there.
                // Measured on the certificate page - a 168.75 pt logo floated left
                // reaches 317.25 and the 131.25 pt logo floated right would start at
                // 315.25, so the second seats at y = the first's bottom
                // rather than beside it.
                var floatDropY = flow.y;
                if (profile.floatImageDoc && block.FloatRight && flow.floatIndentPt > 0
                    && imgX < marginLeft + flow.floatIndentPt
                    && !double.IsNegativeInfinity(flow.floatBottomY))
                    floatDropY = flow.floatBottomY;
                var imgFlowY = flow.y;
                flow.y = floatDropY;
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
                        var cy = flow.y - h / 2;
                        var stamp = ImageStamp.FromEncodedBytes(bytes);
                        stamp.XIndent = cx - bw / 2;
                        stamp.YIndent = cy - bh / 2;
                        stamp.DisplayWidth = w;
                        stamp.DisplayHeight = h;
                        stamp.RotateAngle = -block.ImageRotateDeg;
                        stamp.ApplyTo(flow.page);
                    }
                    else
                        flow.page.AddImage(bytes, new Rectangle(imgX, flow.y - h, imgX + w, flow.y));
                }
                catch { /* undecodable image: skip, keep the flow going */ }
                // Chart-card: the widget CARD around the chart paints a soft grey
                // box-shadow (2px offset, 2px blur — the only visible chrome, the
                // card's fill and border being white). Approximate the bitmap
                // a browser renders with the offset right/bottom bars plus a hairline
                // ring. Card box recovered from the content position: its left chrome
                // insets the image, its inner box is the col's content width, and it
                // closes one chrome below the chart.
                if (profile.chartCardDoc && block.ImageCardShadow is { } cardShadow
                    && block.ImageCardChromePt > 0)
                {
                    var chrome = block.ImageCardChromePt;
                    var cardL = marginLeft + block.ImageIndentPt - chrome;
                    var cardInnerW = flow.contentWidth - block.ImageWidenPadPt;
                    var cardR = cardL + cardInnerW + 2 * chrome;
                    var cardTopPdf = pageHeight - marginTop;
                    var cardBottomPdf = flow.y - h - chrome;
                    const double ShadowOffPt = 1.5;   // 2px offset
                    const double ShadowExtPt = 2.75;  // offset + blur extent, measured on the expected bitmap
                    var inv2 = System.Globalization.CultureInfo.InvariantCulture;
                    var sr = cardShadow.R / 255.0; var sg = cardShadow.G / 255.0; var sbv = cardShadow.B / 255.0;
                    var ops = string.Create(inv2,
                        $"q {sr:0.###} {sg:0.###} {sbv:0.###} rg " +
                        $"{cardR:0.##} {cardBottomPdf - ShadowExtPt:0.##} {ShadowExtPt:0.##} {cardTopPdf - ShadowOffPt - (cardBottomPdf - ShadowExtPt):0.##} re f " +
                        $"{cardL + ShadowOffPt:0.##} {cardBottomPdf - ShadowExtPt:0.##} {cardR - cardL - ShadowOffPt + ShadowExtPt:0.##} {ShadowExtPt:0.##} re f " +
                        $"{sr:0.###} {sg:0.###} {sbv:0.###} RG 0.5 w " +
                        $"{cardL:0.##} {cardBottomPdf:0.##} {cardR - cardL:0.##} {cardTopPdf - cardBottomPdf:0.##} re S Q\n");
                    flow.page.AddContentStream(Encoding.ASCII.GetBytes(ops));
                }
                flow.contentPage = flow.page;
                // A LEFT-FLOATED image leaves the flow: the cursor stays where it
                // was and the block boxes below keep starting at the content top —
                // only their LINES are shortened, on the right of the image, until
                // the flow has passed its bottom edge.
                if (profile.floatImageDoc && block.FloatLeft)
                {
                    flow.floatBottomY = double.IsNegativeInfinity(flow.floatBottomY)
                        ? flow.y - h - padBottom
                        : System.Math.Min(flow.floatBottomY, flow.y - h - padBottom);
                    // The float's occupied width is measured from the content edge,
                    // so it counts the image's OWN margin as well as its box - and the
                    // margin facing the flow, which is where wrapped text stops.
                    flow.floatIndentPt = block.ImageIndentPt + w
                        + (block.ImageFloatGutterPt ?? FloatGutterPt);
                    flow.y = imgFlowY;
                    flow.lastWasHardBreak = false;
                    return;   // the block is laid out; the loop this came from would continue
                }
                // A RIGHT float leaves the flow the same way: the cursor stays put and
                // the blocks below keep their top, with their lines shortened on the
                // RIGHT until the flow has passed the image's bottom edge. Two floats
                // in a row share the deepest bottom.
                if (profile.floatImageDoc && block.FloatRight)
                {
                    flow.floatRightTopY = flow.y;
                    flow.floatRightBottomY = flow.y - h - padBottom;
                    flow.floatRightInsetPt = block.ImageIndentPt + w
                        + (block.ImageFloatGutterPt ?? FloatGutterPt);
                    // The float left the flow: the cursor never moved for it.
                    flow.y = imgFlowY;
                    flow.lastWasHardBreak = false;
                    return;   // the block is laid out; the loop this came from would continue
                }
                flow.y -= h + padBottom;
                // Inline image in a band column: the image sits on a text line box,
                // so the line's tail (descent + leading) separates it from the next
                // paragraph — without it the following text's ascent rises to the
                // image's bottom edge (the legacy baseline-at-cursor model).
                if (profile.floatBandDoc && bandStack.Count > 0) flow.y -= 9;
                // Form dialect: same baseline-at-cursor problem in the main flow —
                // the following section heading's ascent plus the heading gap
                // kept below a block image.
                else if (profile.formHorizontalDoc) flow.y -= 25.5;
            }
            else if (profile.uaStdSerif && !profile.msoFilteredDoc && !profile.escapedAttrDoc
                && block.ImageWidth > 0 && block.ImageHeight > 0)
            {
                // UA flow: an unloadable image with a DECLARED box (class or
                // attribute size) reserves that box — it draws the
                // browser's bordered placeholder frame with the 32×32 icon at
                // its top-left, and the flow resumes below it (the licensing
                // letter's 453×271 px photo box).
                var phW = block.ImageWidth * 0.75;
                var phH = block.ImageHeight * 0.75;
                var phX = marginLeft + block.LeftIndent;
                var phTop = flow.y;
                if (phTop - phH < marginBottom)
                {
                    flow.page = doc.Pages.Add(pageWidth, pageHeight);
                    EnsureFonts(flow.page, docFontDict);
                    flow.y = FreshPageTopY(profile.escapedAttrDoc, pageHeight, marginTop); flow.pendingTopDrop = profile.hasZeroTopMargin;
                    phTop = flow.y;
                }
                var uaPhDark = ParseCssColor("#555555");
                var uaPhLite = ParseCssColor("#AAAAAA");
                DrawBox(flow.page, phX, phTop, phW, 1, null, 0, uaPhDark);
                DrawBox(flow.page, phX, phTop - phH, phW, 1, null, 0, uaPhLite);
                DrawBox(flow.page, phX, phTop - phH, 1, phH, null, 0, uaPhDark);
                DrawBox(flow.page, phX + phW - 1, phTop - phH, 1, phH, null, 0, uaPhLite);
                var uaPhName = RegisterPlaceholderIcon(doc, flow.page, ref flow.flowIconRef, masked: true);
                flow.page.AddContentStream(Encoding.ASCII.GetBytes(FormattableString.Invariant(
                    $"q 32 0 0 32 {phX + 3:0.##} {phTop - 3 - 32:0.##} cm /{uaPhName} Do Q\n")));
                flow.contentPage = flow.page;
                flow.y -= phH;
            }
            else if (profile.msoFilteredDoc)
            {
                // Word-filtered pages: an unloadable image leaves the browser's
                // 32×32 placeholder while its paragraph keeps ONE empty UA line
                // of flow. Both lead placeholders ride 14.4 under the top margin
                // (measured); the banner in the absolutely positioned span seats
                // at 90 + (left 0 + margin-left −96px)·0.75 + the 1 pt frame
                // inset = 19, the inline one at the content edge.
                var mphX = flow.msoBrokenImgCount == 0 ? 90.0 - 72.0 + 1.0 : marginLeft + 0.75;
                flow.msoBrokenImgCount++;
                var mphTop = pageHeight - 72.0 - MsoBrokenImgDropPt;
                var mphName = RegisterPlaceholderIcon(doc, flow.page, ref flow.flowIconRef, masked: true);
                flow.page.AddContentStream(Encoding.ASCII.GetBytes(FormattableString.Invariant(
                    $"q 32 0 0 32 {mphX:0.##} {mphTop - 32:0.##} cm /{mphName} Do Q\n")));
                var mphDark = ParseCssColor("#555555");
                var mphLite = ParseCssColor("#AAAAAA");
                DrawBox(flow.page, mphX - 1, mphTop + 1, 34, 1, null, 0, mphDark);
                DrawBox(flow.page, mphX - 1, mphTop - 32, 34, 1, null, 0, mphLite);
                DrawBox(flow.page, mphX - 1, mphTop - 32, 1, 34, null, 0, mphDark);
                DrawBox(flow.page, mphX + 32, mphTop - 32, 1, 34, null, 0, mphLite);
                flow.contentPage = flow.page;
                // the paragraph's one empty UA text line (an image block
                // carries no font size, so the per-block line height is 0 here)
                flow.y -= lineHeight > 1 ? lineHeight : PpLineBoxPt;
            }
            else if (profile.escapedAttrDoc)
            {
                // A broken image renders the browser's 32×32 placeholder icon at
                // the content edge (the escaped float:/size styles can never
                // apply). Measured: the icon's top rides 9.47 pt above the flow
                // cursor — a heading's bottom margin does not span a replaced
                // box — and a following grid's top border lands 1.38 pt under
                // the icon (the cursor sits one 0.9em ascent below that edge).
                var iconTop = flow.y + 9.47;
                if (iconTop - 32 < marginBottom)
                {
                    flow.page = doc.Pages.Add(pageWidth, pageHeight);
                    EnsureFonts(flow.page, docFontDict);
                    flow.y = FreshPageTopY(profile.escapedAttrDoc, pageHeight, marginTop); flow.pendingTopDrop = profile.hasZeroTopMargin;
                    iconTop = flow.y;
                }
                var phName = RegisterPlaceholderIcon(doc, flow.page, ref flow.flowIconRef, masked: true);
                flow.page.AddContentStream(Encoding.ASCII.GetBytes(FormattableString.Invariant(
                    $"q 32 0 0 32 {marginLeft + 1:0.##} {iconTop - 32:0.##} cm /{phName} Do Q\n")));
                // The browser frames a broken image with a 1 pt INSET border —
                // top/left #555, bottom/right #aaa — half a point outside the icon.
                var phDark = ParseCssColor("#555555");
                var phLite = ParseCssColor("#AAAAAA");
                DrawBox(flow.page, marginLeft, iconTop, 34, 1, null, 0, phDark);
                DrawBox(flow.page, marginLeft, iconTop - 33, 34, 1, null, 0, phLite);
                DrawBox(flow.page, marginLeft, iconTop - 33, 1, 34, null, 0, phDark);
                DrawBox(flow.page, marginLeft + 33, iconTop - 33, 1, 34, null, 0, phLite);
                flow.contentPage = flow.page;
                flow.y = iconTop - 33 - 0.9 * 12;
            }
            flow.lastWasHardBreak = false;
            flow.prevFlowMarginBottom = 0;
            flow.prevFlowLineHeight = 0;
            flow.afterRuleDrop = false;
            flow.afterFhTable = false;
    }

    /// <summary>How far a Word-filtered page drops the flow for a broken image.</summary>
    private const double MsoBrokenImgDropPt = 14.4;

    /// <summary>The font resource name a block draws with, embedding the family variant
    /// its weight and slant call for.</summary>
    private static string ResolveFontRes(Page pg, Block blk, HtmlFlowCursor flow, HtmlDocProfile profile,
        Document doc, Dictionary<string, (string resName, Core.PdfIndirectRef fontRef)> embeddedFonts,
        Dictionary<string, (int objNum, string embedName)> fontFileCache)
    {
        if (string.IsNullOrEmpty(blk.FontFamily)) return blk.FontRes;
        var family = blk.FontFamily!;
        // pt-styled fragment: a bold block embeds the family's BOLD variant
        // (the h2 title draws VerdanaBold).
        // The DataWorks header sets its reference bold-italic — both
        // variants promote together.
        if (profile.dwFormDoc && (blk.FontRes == "F2" || blk.EmBold) && blk.EmItalic)
            family += " Bold Italic";
        else if ((profile.ptStyledFragment || profile.dwFormDoc) && (blk.FontRes == "F2" || blk.EmBold)
            && !family.EndsWith(" Bold", StringComparison.OrdinalIgnoreCase))
            family += " Bold";
        // …and an italic block its ITALIC variant (the 8 pt footnote).
        else if ((profile.ptStyledFragment || profile.dwFormDoc) && blk.EmItalic
            && !family.EndsWith(" Italic", StringComparison.OrdinalIgnoreCase))
            family += " Italic";
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
        flow.usedCustomFont = true;
        return entry.resName;
    }

    /// <summary>Lays out one checkbox block and advances the flow past it.</summary>
    /// <remarks>Lifted verbatim out of the block-dispatch loop in
    /// <see cref="ConvertFromHtml"/>.</remarks>
    private static void LayoutCheckboxBlock(Block block, HtmlFlowCursor flow, HtmlDocProfile profile, Document doc, Core.PdfDictionary docFontDict, double marginBottom, double marginLeft, double marginTop, double pageHeight, double pageWidth)
    {
            // Emit an AcroForm checkbox at the flow cursor (a small fixed box; the
            // HTML→PDF tests inspect the field, not its pixel position).
            const double boxSize = 10.0;
            if (flow.y - boxSize < marginBottom)
            {
                flow.page = doc.Pages.Add(pageWidth, pageHeight);
                EnsureFonts(flow.page, docFontDict);
                flow.y = pageHeight - marginTop; flow.pendingTopDrop = profile.hasZeroTopMargin;
            }
            var cbx = marginLeft + block.LeftIndent;
            var checkbox = new Forms.CheckboxField(flow.page, new Rectangle(cbx, flow.y - boxSize, cbx + boxSize, flow.y))
            {
                Checked = block.Checked,
            };
            doc.Form.Add(checkbox, flow.page.Number);
            flow.y -= boxSize + 2;
            flow.lastWasHardBreak = false;
    }

    /// <summary>Lays out one button block and advances the flow past it.</summary>
    /// <remarks>Lifted verbatim out of the block-dispatch loop in
    /// <see cref="ConvertFromHtml"/>.</remarks>
    private static void LayoutButtonBlock(Block block, HtmlFlowCursor flow, HtmlDocProfile profile, Document doc, Core.PdfDictionary docFontDict, double marginBottom, double marginLeft, double marginTop, double pageHeight, double pageWidth, Color? dialectButtonFill, string dialectButtonTextRg)
    {
            var capW = block.ButtonCaption.Length > 0
                ? MeasureStd14("Helvetica", block.ButtonCaption, 10) + ButtonChromeWPt : EmptyButtonWPt;
            var bh = block.ButtonCaption.Length > 0 ? ButtonHeightPt : EmptyButtonHPt;
            var btnTop = flow.y + 12.3;
            if (btnTop - bh < marginBottom)
            {
                flow.page = doc.Pages.Add(pageWidth, pageHeight);
                EnsureFonts(flow.page, docFontDict);
                flow.y = FreshPageTopY(profile.escapedAttrDoc, pageHeight, marginTop); flow.pendingTopDrop = profile.hasZeroTopMargin;
                btnTop = flow.y;
            }
            // DataWorks: the Completed submit right-aligns on its legacy
            // align attribute inside the 98% form-element box (template: box
            // right edge 514.6 = content right 527.5 - 12.9).
            var btnX = profile.dwFormDoc && block.AlignRight
                ? marginLeft + flow.contentWidth - capW - DwCompletedRightInsetPt
                : marginLeft - 2;
            // Thin outline, a 1.5–2 pt white gap, then the fill — the
            // button chrome (outer 60.42×18.75, inner fill 56.42×15.75).
            DrawBox(flow.page, btnX, btnTop - bh, capW, bh,
                border: Color.Black, borderWidth: 1, fill: null);
            if (capW > 4 && bh > 3)
                DrawBox(flow.page, btnX + 2, btnTop - bh + 1.5, capW - 4, bh - 3,
                    null, 0, dialectButtonFill);
            if (block.ButtonCaption.Length > 0)
                flow.page.AddContentStream(Encoding.ASCII.GetBytes(FormattableString.Invariant(
                    $"q BT /F1 10 Tf {dialectButtonTextRg} 1 0 0 1 {btnX + ButtonCaptionInsetXPt:0.##} {btnTop - ButtonCaptionDropPt:0.##} Tm ({EscapePdfString(block.ButtonCaption)}) Tj ET Q\n")));
            flow.contentPage = flow.page;
            // DataWorks: the flow resumes tighter under the submit (the
            // list opens 32.5 under the Completed caption).
            flow.y = btnTop - bh - (profile.dwFormDoc ? DwAfterButtonDropPt : 9.3);
            flow.lastWasHardBreak = false;
            flow.prevFlowMarginBottom = 0;
            flow.prevFlowLineHeight = 0;
    }

    /// <summary>Lays out a hard break, or a block whose text is empty, and advances the
    /// flow past the space it occupies.</summary>
    /// <remarks>Lifted verbatim out of the block-dispatch loop in
    /// <see cref="ConvertFromHtml"/>.</remarks>
    private static void LayoutHardBreakBlock(Block block, HtmlFlowCursor flow, HtmlDocProfile profile,
        HtmlBlockMetrics metrics, Document doc, Core.PdfDictionary docFontDict, bool uaFlow,
        bool breakAfterTable, bool wasRow, string? bodyCssFace, double marginBottom, double marginLeft, double marginTop,
        double pageHeight, double pageWidth)
    {
            // Border-top divider marker: the div's rule strokes here, above
            // its content, and spends only its own width.
            if (block.BorderTopOnly && block.BorderColor is { } topRule
                && block.BorderWidth > 0)
            {
                var invtr = System.Globalization.CultureInfo.InvariantCulture;
                flow.page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invtr,
                    $"q {topRule.R / 255.0:0.###} {topRule.G / 255.0:0.###} {topRule.B / 255.0:0.###} RG " +
                    $"{block.BorderWidth:0.##} w {marginLeft:0.##} {flow.y - block.BorderWidth / 2:0.##} m " +
                    $"{pageWidth - marginLeft:0.##} {flow.y - block.BorderWidth / 2:0.##} l S Q\n")));
                // The rule spends its own width plus the wrapper's
                // padding-top under it (measured: the From block opens
                // pad + margin below the rule).
                flow.y -= block.BorderWidth + block.PadTop;
                flow.contentPage = flow.page;
                flow.lastWasHardBreak = false;
                return;   // the block is laid out; the loop this came from would continue
            }
            // Prefer the explicit CSS height over the default half-line
            // spacer — CMS template HTML often uses empty styled divs as
            // visual separator bars, and ignoring their height would
            // collapse intended pagination.
            // A <br> directly after a styled row ends a full default-size line box
            // (the browser's 16px body line), not the usual half-line spacer.
            var spacer = block.ExplicitHeight > 0
                ? block.ExplicitHeight
                // Redline: an empty paragraph occupies its FULL 1.15 em box
                // (probed: 13.8 between the FORM header and the first grid).
                : profile.redlineDiffDoc && metrics.blockFontSize > 0
                ? RedlineEmptyParaPt
                : (flow.lastWasHardBreak ? 0 : (wasRow ? 13.5 : metrics.lineHeight * 0.5));
            // Form dialect: this document family separates sections with CSS
            // margins, and its bare <br>s are all float-clears (`clear:both`) that
            // collapse to the float bottom — they add no line boxes of their own.
            if (profile.formHorizontalDoc && block.ExplicitHeight <= 0) spacer = 0;
            // Form-document dialect: every standalone <br> is one full line box at
            // its enclosing size — consecutive <br>s stack (no half-line coalescing).
            else if (profile.formDialectTables && block.IsLineBreak)
                spacer = (block.FontSize > 0 ? block.FontSize : metrics.blockFontSize) * 1.3;
            // CSS run dialect: a standalone <br> is one full line box of the page
            // stylesheet's own base face and size — the same rule its cells pitch on.
            else if (bodyCssFace is not null && block.IsLineBreak
                     && WinMetricsFor(bodyCssFace) is { } brFace)
                spacer = MetricLineHeight(
                    block.FontSize > 0 ? block.FontSize : profile.bodyCssFontPt, brFace.sum);
            // Float flow: a <br> ENDS the line it sits on, and the flow has already
            // spent that line's advance — so the first of a run is free and every one
            // after it stands a full line box of the paragraph's own pitch. Measured on
            // the certificate: its `<br><br>` sub-paragraph separator puts the next
            // glyph top exactly two pitches below the last one (440.71 against 440.72).
            else if (profile.floatBothSidesDoc && block.IsLineBreak)
                spacer = flow.lastWasHardBreak ? metrics.lineHeight : 0;
            // Metric flow: a real <br> is one full line box at the size of its enclosing
            // style — every <br> counts (no coalescing). Styled spacers keep their CSS
            // height; other empty containers collapse to nothing.
            if (profile.metricFlow)
                spacer = block.IsLineBreak && WinMetricsFor(profile.metricFace) is { } brm
                    ? (uaFlow
                        ? (block.FontSize > 0 ? block.FontSize : 12.0) * 1.125
                          // A break paragraph that FOLLOWS a metric table takes
                          // the UA paragraph margin a text neighbour would have
                          // opened (probed: table -> <p><br/></p> -> table gaps
                          // 13.44 + 13.5 + 13.44 exactly; between text
                          // paragraphs the margins come from the text path and
                          // nothing is added here).
                          // The first REAL line break of a post-table tail stands
                          // the UA margin its table neighbour never read (probed:
                          // table, bare <br>, <p><br/></p>, text spaces
                          // 13.44+13.5 / 13.5 / 13.44+asc - the second break's
                          // margin arrives through the following text block's own
                          // margin-top).
                          + (profile.uaBareDoc && breakAfterTable
                              && block.UaSpacerPara ? UaParagraphMarginPt : 0)
                        : MetricLineHeight(block.FontSize > 0 ? block.FontSize : 11.0, brm.sum))
                    : block.IsLineBreak ? block.ExplicitHeight
                    : block.IsHardBreak && block.ExplicitHeight > 0 ? block.ExplicitHeight
                    : 0;
            if (spacer > 0)
            {
                if (flow.y - spacer < marginBottom)
                {
                    flow.page = doc.Pages.Add(pageWidth, pageHeight);
                    EnsureFonts(flow.page, docFontDict);
                    flow.y = FreshPageTopY(profile.escapedAttrDoc, pageHeight, marginTop); flow.pendingTopDrop = profile.hasZeroTopMargin;
                }
                flow.y -= spacer;
            }
            flow.lastWasHardBreak = true;
            flow.lastBreakWasUaSpacer = block.UaSpacerPara;
            // A zero-space break (the form dialect's float-clears) is layout-inert:
            // it must not hide the preceding text block from the <hr>/image rewind.
            if (spacer > 0)
            {
                flow.prevFlowMarginBottom = 0;
                flow.prevFlowLineHeight = 0;
            }
    }
}
