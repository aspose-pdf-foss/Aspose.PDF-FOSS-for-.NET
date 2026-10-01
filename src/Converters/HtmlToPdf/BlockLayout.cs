using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Lays out one positioned card block: the media box with its placeholder
    /// icon and bottom-anchored bars, the clipped prose column, and the two-column info
    /// panel. Advances the flow cursor to the bottom of the card's container.</summary>
    /// <remarks>Lifted verbatim out of the block-dispatch loop in
    /// <see cref="ConvertFromHtml"/>; every quantity in it stays an empirical fixed
    /// value, as it was inline.</remarks>
    /// <summary>CSS pixels to points for the positioned card's authored sizes.</summary>
    private const double PosCardPxPt = 0.75;

    private static void LayoutPositionedCard(PositionedCard pc, HtmlFlowCursor flow, double marginLeft)
    {
        var pk = new PositionedCardLayoutState();
        pk.pc = pc;
        pk.flow = flow;
        pk.marginLeft = marginLeft;
        pk.invC = System.Globalization.CultureInfo.InvariantCulture;
        pk.cSerifR = PosFace("Times New Roman");
        pk.cSerifB = PosFace("Times New Roman Bold");
        pk.fontDictC = pk.flow.page.Dict.Get("Resources") is Core.PdfDictionary cres
            ? cres.Get("Font") as Core.PdfDictionary : null;

        // the serif line's baseline seat inside its 13.5 box: half-leading
        // + winAscent (the same drop the form-grid strut model measured)
        pk.cx0 = pk.marginLeft;                          // 90 + the UA body pad = 96
        pk.mediaTop = pk.flow.y - CardBodyPadPt;              // content top + body margin
        pk.mediaBot = pk.mediaTop - pk.pc.MediaHPx * PosCardPxPt;
        pk.cardRight = pk.cx0 + pk.pc.MediaWPx * PosCardPxPt;

        // broken-image placeholder: white frame, 1px black border, the
        // torn-flow.page glyph (grey-stroked inner rect, like the cell path's)
        if (pk.pc.HasImg)
        {
            var ib = CardIconBoxPt;
            var iy = pk.mediaTop - ib;
            COps(pk, $"q 1 1 1 rg {pk.cx0.ToString("F2", pk.invC)} {iy.ToString("F2", pk.invC)} {ib.ToString("F2", pk.invC)} {ib.ToString("F2", pk.invC)} re f "
                + $"0 0 0 RG 1 w {(pk.cx0 + 0.5).ToString("F2", pk.invC)} {(iy + 0.5).ToString("F2", pk.invC)} {(ib - 1).ToString("F2", pk.invC)} {(ib - 1).ToString("F2", pk.invC)} re S "
                + $"0.5 0.5 0.5 RG 1 w {(pk.cx0 + 6.5).ToString("F2", pk.invC)} {(iy + ib / 2 - 8).ToString("F2", pk.invC)} 12 16 re S Q ");
        }

        // bottom-anchored caption bars
        foreach (var bar in pk.pc.Bars)
        {
            var barH = bar.HPx * PosCardPxPt;
            var barTop = pk.mediaBot + (bar.BottomPx + bar.HPx) * PosCardPxPt;
            COps(pk, $"q {(bar.Fill.R / 255.0).ToString("0.###", pk.invC)} {(bar.Fill.G / 255.0).ToString("0.###", pk.invC)} {(bar.Fill.B / 255.0).ToString("0.###", pk.invC)} rg "
                + $"{pk.cx0.ToString("F2", pk.invC)} {(barTop - barH).ToString("F2", pk.invC)} {(pk.cardRight - pk.cx0).ToString("F2", pk.invC)} {barH.ToString("F2", pk.invC)} re f Q ");
            if (bar.Text.Length > 0)
                CDrawText(pk, bar.Text, false, pk.cx0, barTop - CSerifDrop(12.0), 12.0, bar.TextColor);
        }

        // float:left prose column — greedy serif wrap in its box, clipped
        // to the declared height (overflow:hidden drops whole lines)
        {
            var boxW = pk.pc.TextWPx * PosCardPxPt;
            var proseLines = new List<string>();
            var cur = "";
            foreach (var w in pk.pc.ParaText.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var cand = cur.Length == 0 ? w : cur + " " + w;
                if (cur.Length > 0 && CWidth(cand, false, 12.0) > boxW)
                { proseLines.Add(cur); cur = w; }
                else cur = cand;
            }
            if (cur.Length > 0) proseLines.Add(cur);
            var clipBot = pk.mediaBot - pk.pc.TextHPx * PosCardPxPt;
            var proseBox = PxLinePt(12.0, SerifWinLineRatio);
            for (var li = 0; li < proseLines.Count; li++)
            {
                var boxTop = pk.mediaBot - CardParaFirstPt - li * proseBox;
                if (boxTop - 12.0 * SerifWinLineRatio < clipBot) break;
                CDrawText(pk, proseLines[li], false, pk.cx0, boxTop - CSerifDrop(12.0), 12.0,
                    Color.FromArgb(0, 0, 0));
            }
        }

        // float:right info panel — label column left-anchored, value
        // column right-aligned on the card's right edge; both walk their
        // paragraph slots on the measured pitch chain
        {
            var infoX = pk.cardRight - pk.pc.InfoWPx * PosCardPxPt;
            var colTop = pk.mediaBot - pk.pc.InfoMtPx * PosCardPxPt - CardInfoStartPt;
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
                            ? slot.MtPx * PosCardPxPt + CardInfoLineBoxPt
                            : CardInfoPitchPt;
                    first = false;
                    var tx = rightAlign
                        ? pk.cardRight - CWidth(slot.Text, slot.Bold, 9.0)
                        : infoX;
                    CDrawText(pk, slot.Text, slot.Bold, tx,
                        boxTop - 9.0 * SerifWinAscent, 9.0, Color.FromArgb(0, 0, 0));
                }
            }
            WalkColumn(pk.pc.Labels, rightAlign: false);
            WalkColumn(pk.pc.Values, rightAlign: true);
        }

        pk.flow.y = pk.mediaTop - (pk.pc.ContainerHPx > 0 ? pk.pc.ContainerHPx : pk.pc.MediaHPx * 2) * PosCardPxPt;
        pk.flow.lastWasHardBreak = false;
    }

    /// <summary>Lays out one positioned slide block and advances the flow cursor past it.</summary>
    /// <remarks>Lifted verbatim out of the block-dispatch loop in
    /// <see cref="ConvertFromHtml"/>.</remarks>
    private static void LayoutPositionedSlide(
        PositionedSlide slide, HtmlFlowCursor flow, HtmlLoadOptions? options, System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string>> css, double marginLeft, double marginTop, double pageHeight)
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
            var slColor = Color.FromArgb(0, 0, 0);
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
                            stamp.ApplyTo(flow.page);
                        }
                        else
                            flow.page.AddImage(ibytes, new Rectangle(ix, iTop - ih, ix + iw, iTop));
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
                    flow.page.AddContentStream(Encoding.ASCII.GetBytes(ops));
                    flow.contentPage = flow.page;
                }
            }
            flow.y -= slide.MinHPx * PxPt;
            flow.lastWasHardBreak = false;
    }

    // Centre-crop an encoded image to a CSS-px box: background-repeat:no-repeat
    // without a background-size anchors the image at NATURAL size centre-centre,
    // so a box smaller than the image shows its middle. Returns null when no
    // crop is needed (or off-Windows) — the caller keeps the original bytes.
    private static byte[]? CenterCropToBox(byte[] bytes, double boxWpx, double boxHpx)
    {
        if (!Compat.IsWindows()) return null;
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

    /// <summary>Lays out one positioned topics-list block and advances the flow cursor past it.</summary>
    /// <remarks>Lifted verbatim out of the block-dispatch loop in
    /// <see cref="ConvertFromHtml"/>.</remarks>
    private static void LayoutTopicsList(
        RtlTopicsTable tp, HtmlFlowCursor flow, HtmlDocProfile profile, Document doc, Core.PdfDictionary docFontDict, double marginBottom, double marginLeft, double marginTop, double pageHeight, double pageWidth, List<byte[]> inlineSvgs)
    {
            const double PxPt = 0.75;
            var invT = System.Globalization.CultureInfo.InvariantCulture;
            var serifR = PosFace("Times New Roman");
            var serifB = PosFace("Times New Roman Bold");
            var fontDictT = flow.page.Dict.Get("Resources") is Core.PdfDictionary tres
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
            if (flow.y - blockH < marginBottom && flow.y < pageHeight - marginTop - 1e-3)
            {
                flow.page = doc.Pages.Add(pageWidth, pageHeight);
                EnsureFonts(flow.page, docFontDict);
                flow.y = pageHeight - marginTop; flow.pendingTopDrop = profile.hasZeroTopMargin;
                fontDictT = flow.page.Dict.Get("Resources") is Core.PdfDictionary tres2
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
                t.Append('<').Append(Compat.ToHexString(hex)).Append("> Tj ET ");
                flow.page.AddContentStream(Encoding.ASCII.GetBytes(t.ToString()));
            }

            // Figure first (graphics only — contributes no text fragments).
            if (tp.SvgIdx >= 0 && tp.SvgIdx < inlineSvgs.Count)
            {
                var figBytes = ImageRasterizer.RasterizeSvg(inlineSvgs[tp.SvgIdx]);
                if (figBytes is not null)
                {
                    var figRight = marginLeft + flow.contentWidth;
                    try
                    {
                        flow.page.AddImage(figBytes, new Rectangle(figRight - figW, flow.y - figH, figRight, flow.y));
                    }
                    catch { }
                }
            }

            var capBase = flow.y - CapDrop;
            if (tp.CaptionText is not null)
                DrawSerif(serifB.ttf is not null ? serifB : serifR, "TimesNewRomanBold",
                    tp.CaptionText, capPenRight, 0, capBase, CapPt);
            for (var ti = 0; ti < tp.Items.Count; ti++)
            {
                var ibase = capBase - CapToItem - ti * ItemPitch;
                DrawSerif(serifR, "TimesNewRoman", " •", 0, bulletX, ibase, ItemPt);
                DrawSerif(serifR, "TimesNewRoman", tp.Items[ti], itemPenRight, 0, ibase, ItemPt);
            }

            flow.y -= blockH;
            flow.lastWasHardBreak = false;
    }

    /// <summary>Lays out one search-form block and advances the flow cursor past it.</summary>
    /// <remarks>Lifted verbatim out of the block-dispatch loop in
    /// <see cref="ConvertFromHtml"/>.</remarks>
    private static void LayoutSearchForm(ConvertState cv, SearchForm sf, HtmlLoadOptions? options)
    {
        var sm = new SearchFormLayoutState();
        sm.cv = cv;
        sm.sf = sf;
        sm.options = options;
        const double PxPt = 0.75;
        sm.invf = System.Globalization.CultureInfo.InvariantCulture;
        sm.totalH = (sm.sf.MarginTopPx + sm.sf.InputHeightPx + sm.sf.GapPx + sm.sf.ButtonHeightPx + sm.sf.MarginBottomPx) * PxPt;
        if (sm.cv.flow.y - sm.totalH < sm.cv.marginBottom)
        {
            sm.cv.flow.page = sm.cv.doc.Pages.Add(sm.cv.pageWidth, sm.cv.pageHeight);
            EnsureFonts(sm.cv.flow.page, sm.cv.docFontDict);
            sm.cv.flow.y = sm.cv.pageHeight - sm.cv.marginTop; sm.cv.flow.pendingTopDrop = sm.cv.profile.hasZeroTopMargin;
        }
        sm.cv.flow.y -= sm.sf.MarginTopPx * PxPt;
        sm.cellW = sm.sf.CellWidthPx * PxPt;
        sm.cellX = sm.cv.marginLeft + (sm.cv.flow.contentWidth - sm.cellW) / 2;
        sm.inputW = sm.sf.InputWidthPx * PxPt;
        sm.inputH = sm.sf.InputHeightPx * PxPt;

        sm.fld = new Forms.TextBoxField(sm.cv.flow.page, new Rectangle(sm.cellX, sm.cv.flow.y - sm.inputH, sm.cellX + sm.inputW, sm.cv.flow.y));
        if (!string.IsNullOrEmpty(sm.sf.InputName)) sm.fld.PartialName = sm.sf.InputName;
        sm.cv.doc.Form.Add(sm.fld, sm.cv.flow.page.Number);
        DrawBox(sm.cv.flow.page, sm.cellX, sm.cv.flow.y - sm.inputH, sm.inputW, sm.inputH,
            border: Color.FromArgb(0, 0, 0), borderWidth: 0.75, fill: null);

        if (!string.IsNullOrEmpty(sm.sf.IconSrc))
        {
            var ib = LoadConverterImage(sm.sf.IconSrc, sm.options);
            if (ib is not null)
            {
                var iw = sm.sf.IconWPx * PxPt;
                var ih2 = sm.sf.IconHPx * PxPt;
                var ix = sm.cellX + sm.inputW - sm.sf.IconRightPx * PxPt - iw;
                var iy = sm.cv.flow.y - sm.sf.IconTopPx * PxPt;
                try { sm.cv.flow.page.AddImage(ib, new Rectangle(ix, iy - ih2, ix + iw, iy)); } catch { }
            }
        }

        sm.res0 = sm.cv.flow.page.Dict.Get("Resources") as Core.PdfDictionary;
        sm.fdict = sm.res0?.Get("Font") as Core.PdfDictionary;
        sm.arial = PosFace("Arial");

        if (!string.IsNullOrEmpty(sm.sf.LinkText) && sm.arial.ttf is not null && sm.fdict is not null)
        {
            LayoutSearchFormLink(sm);
        }

        sm.cv.flow.y -= sm.inputH + sm.sf.GapPx * PxPt;

        if (sm.sf.Buttons.Count > 0 && sm.arial.ttf is not null && sm.fdict is not null)
        {
            LayoutSearchFormButtons(sm);
        }

        sm.cv.flow.y -= (sm.sf.ButtonHeightPx + sm.sf.MarginBottomPx) * PxPt;
        sm.cv.flow.lastWasHardBreak = false;
    }

    /// <summary>Lays out one right-to-left SVG diagram table and advances the flow cursor past it.</summary>
    /// <remarks>Lifted verbatim out of the block-dispatch loop in
    /// <see cref="ConvertFromHtml"/>.</remarks>
    private static void LayoutRtlSvgDiagram(ConvertState cv, RtlSvgTable dg, List<byte[]> inlineSvgs)
    {
        var rd = new RtlSvgDiagramState();
        rd.cv = cv;
        rd.dg = dg;
        rd.inlineSvgs = inlineSvgs;
        // The arm's row constants (the 49.3 px title-baseline drop and its
        // siblings) were measured at the LEGACY cv.flow's section entry. The UA
        // serif cv.flow reaches this arm ~12.2 pt lower - it charges the full
        // preceding h6 bottom margin the legacy cv.flow did not - so the whole
        // calibrated canvas would shift down by that much. Re-anchor the
        // entry to the calibration's own convention (measured:
        // title-label ink 103.11 with the lift, 115.35 without).
        if (rd.cv.profile.uaStdSerif && !rd.cv.profile.deadExternalCss) rd.cv.flow.y += DgUaEntryLiftPt;
        const double PxPt = 0.75;
        rd.invd = System.Globalization.CultureInfo.InvariantCulture;
        rd.canvasW = rd.dg.WidthPx * PxPt;
        rd.canvasRight = rd.cv.marginLeft + rd.cv.flow.contentWidth;
        rd.canvasLeft = rd.canvasRight - rd.canvasW;
        rd.arialD = PosFace("Arial");
        rd.fontDictD = rd.cv.flow.page.Dict.Get("Resources") is Core.PdfDictionary dres
            ? dres.Get("Font") as Core.PdfDictionary : null;

        rd.titleRowH = (rd.dg.TitleText is null ? 0 : 81.3) * PxPt;
        rd.figH = rd.dg.MainSvgHPx * PxPt;
        rd.labelRowH = (rd.dg.MidLabels.Count > 0 ? 66.7 : 0.0) * PxPt;
        rd.legendBoxH = rd.dg.LegendWFrac[0] * rd.canvasW; // widest legend svg's square
        rd.legendLabelH = 22 * PxPt;
        rd.totalH = rd.titleRowH + rd.figH + rd.labelRowH + rd.legendBoxH + rd.legendLabelH;
        if (rd.cv.flow.y - rd.totalH < rd.cv.marginBottom && rd.cv.flow.y < rd.cv.pageHeight - rd.cv.marginTop - 1e-3)
        {
            rd.cv.flow.page = rd.cv.doc.Pages.Add(rd.cv.pageWidth, rd.cv.pageHeight);
            EnsureFonts(rd.cv.flow.page, rd.cv.docFontDict);
            rd.cv.flow.y = rd.cv.pageHeight - rd.cv.marginTop; rd.cv.flow.pendingTopDrop = rd.cv.profile.hasZeroTopMargin;
            rd.fontDictD = rd.cv.flow.page.Dict.Get("Resources") is Core.PdfDictionary dres2
                ? dres2.Get("Font") as Core.PdfDictionary : null;
        }

        if (rd.dg.TitleText is not null)
            DrawRtlText(rd, rd.dg.TitleText, 0, rd.cv.flow.y - 49.3 * PxPt, rd.dg.TitleFontPx * PxPt, centerCanvas: true);
        rd.cv.flow.y -= rd.titleRowH;

        if (rd.dg.MainSvgIdx >= 0 && rd.dg.MainSvgIdx < rd.inlineSvgs.Count)
        {
            PlaceRtlSvgFigure(rd);
        }
        rd.cv.flow.y -= rd.figH;

        if (rd.dg.MidLabels.Count > 0)
            foreach (var (text, col) in rd.dg.MidLabels)
            {
                var k = Math.Min(col, rd.dg.MidLabelRightFrac.Length - 1);
                DrawRtlText(rd, text, rd.canvasLeft + rd.dg.MidLabelRightFrac[k] * rd.canvasW,
                    rd.cv.flow.y - 24 * PxPt, rd.dg.LabelFontPx * PxPt);
            }
        rd.cv.flow.y -= rd.labelRowH;

        for (var k = 0; k < rd.dg.Legend.Count; k++)
        {
            DrawRtlSvgLegendEntry(rd, k);
        }
        rd.cv.flow.y -= rd.legendBoxH + rd.legendLabelH;

        rd.cv.flow.lastWasHardBreak = false;
    }

    /// <summary>Lays out one flex-grid block and advances the flow cursor past it.</summary>
    /// <remarks>Lifted verbatim out of the block-dispatch loop in
    /// <see cref="ConvertFromHtml"/>.</remarks>
    /// <summary>The flex-grid columns' 12px class size.</summary>
    private const double FlexCellFontPt = 9.0;

    private static void LayoutFlexGrid(FlexGrid fg, HtmlFlowCursor flow, Document doc, Core.PdfDictionary docFontDict, double marginBottom, double marginLeft, double marginRight, double marginTop, double pageHeight, double pageWidth)
    {
        var xg = new FlexGridLayoutState();
        xg.fg = fg;
        xg.flow = flow;
        xg.doc = doc;
        xg.docFontDict = docFontDict;
        xg.marginBottom = marginBottom;
        xg.marginLeft = marginLeft;
        xg.marginRight = marginRight;
        xg.marginTop = marginTop;
        xg.pageHeight = pageHeight;
        xg.pageWidth = pageWidth;
        xg.invF = System.Globalization.CultureInfo.InvariantCulture;
        xg.fSerifB = PosFace("Times New Roman Bold");
        xg.fontDictF = xg.flow.page.Dict.Get("Resources") is Core.PdfDictionary fres
            ? fres.Get("Font") as Core.PdfDictionary : null;
        xg.contL = xg.marginLeft + CardBodyPadPt;
        xg.contR = xg.fg.PageContentPt > 0
            ? xg.contL + xg.fg.PageContentPt
            : xg.pageWidth - xg.marginRight - CardBodyPadPt;
        xg.contT = xg.marginTop + CardBodyPadPt;
        xg.contW = xg.contR - xg.contL;
        xg.wrapL = xg.fg.TableFlavor
            ? xg.contL + 2.25
            : xg.contL + xg.contW * 0.01 + FlexRowBorderPt;
        xg.wrapW = xg.fg.TableFlavor
            ? xg.contW - 4.5
            : xg.contW * 0.98 - 2 * FlexRowBorderPt;
        if (xg.fg.Title.Length > 0)
            FDraw(xg, xg.fg.Title, xg.wrapL + (xg.wrapW - FWidth(xg.fg.Title, FlexTitleFontPt)) / 2,
                xg.contT + (xg.fg.TableFlavor ? 1.34 : 0.96), FlexTitleFontPt);
        xg.fy = xg.contT + FlexTitleBandPt + (xg.fg.TableFlavor ? 3.4 : 0.0);
        xg.rowGap = xg.fg.TableFlavor ? 1.5 : 0.0;
        xg.labelDy = xg.fg.TableFlavor ? 1.39 : 0.64;
        xg.valueInset = xg.fg.TableFlavor ? 2.62 : FlexValueInsetPt;
        foreach (var frow in xg.fg.Rows)
        {
            if (!LayoutFlexRow(xg, frow)) break;
        }
        // The container's own border box: a wrapper-declared height runs to
        // its full depth — past the flow.page bottom onto a continuation flow.page —
        // otherwise it closes at the last row.
        FLine(xg, xg.contL + 0.38, xg.contT + 0.38, xg.contR - 0.38, xg.contT + 0.38);
        if (xg.fg.PageContentHPt > 0)
        {
            var pageBottomTd = xg.pageHeight - xg.marginBottom;
            var contBottomTd = xg.contT + xg.fg.PageContentHPt;
            var b1 = Math.Min(contBottomTd, pageBottomTd);
            FLine(xg, xg.contL + 0.38, xg.contT + 0.38, xg.contL + 0.38, b1);
            FLine(xg, xg.contR - 0.38, xg.contT + 0.38, xg.contR - 0.38, b1);
            if (contBottomTd <= pageBottomTd)
                FLine(xg, xg.contL + 0.38, b1, xg.contR - 0.38, b1);
            else
            {
                var tail = contBottomTd - b1;
                xg.flow.page = xg.doc.Pages.Add(xg.pageWidth, xg.pageHeight);
                EnsureFonts(xg.flow.page, xg.docFontDict);
                var t0 = xg.marginTop;
                FLine(xg, xg.contL + 0.38, t0, xg.contL + 0.38, t0 + tail);
                FLine(xg, xg.contR - 0.38, t0, xg.contR - 0.38, t0 + tail);
                FLine(xg, xg.contL + 0.38, t0 + tail, xg.contR - 0.38, t0 + tail);
                xg.flow.y = xg.pageHeight - (t0 + tail);
            }
        }
        else
        {
            FLine(xg, xg.contL + 0.38, xg.fy, xg.contR - 0.38, xg.fy);
            FLine(xg, xg.contL + 0.38, xg.contT + 0.38, xg.contL + 0.38, xg.fy);
            FLine(xg, xg.contR - 0.38, xg.contT + 0.38, xg.contR - 0.38, xg.fy);
            xg.flow.y = xg.pageHeight - xg.fy - FlexRowBorderPt;
        }
        xg.flow.contentPage = xg.flow.page;
        xg.flow.lastWasHardBreak = false;
    }

}
