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
    private static void LayoutPositionedCard(
        PositionedCard pc, HtmlFlowCursor flow, double marginLeft)
    {
            const double PxPt = 0.75;
            var invC = System.Globalization.CultureInfo.InvariantCulture;
            var cSerifR = PosFace("Times New Roman");
            var cSerifB = PosFace("Times New Roman Bold");
            var fontDictC = flow.page.Dict.Get("Resources") is Core.PdfDictionary cres
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
                flow.page.AddContentStream(Encoding.ASCII.GetBytes(t.ToString()));
            }

            void COps(string ops) => flow.page.AddContentStream(Encoding.ASCII.GetBytes(ops));

            // the serif line's baseline seat inside its 13.5 box: half-leading
            // + winAscent (the same drop the form-grid strut model measured)
            double CSerifDrop(double pt)
            {
                var box = PxLinePt(pt, SerifWinLineRatio);
                return (box - pt * SerifWinLineRatio) / 2 + pt * SerifWinAscent;
            }

            var cx0 = marginLeft;                          // 90 + the UA body pad = 96
            var mediaTop = flow.y - CardBodyPadPt;              // content top + body margin
            var mediaBot = mediaTop - pc.MediaHPx * PxPt;
            var cardRight = cx0 + pc.MediaWPx * PxPt;

            // broken-image placeholder: white frame, 1px black border, the
            // torn-flow.page glyph (grey-stroked inner rect, like the cell path's)
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
                        Color.FromArgb(0, 0, 0));
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
                            boxTop - 9.0 * SerifWinAscent, 9.0, Color.FromArgb(0, 0, 0));
                    }
                }
                WalkColumn(pc.Labels, rightAlign: false);
                WalkColumn(pc.Values, rightAlign: true);
            }

            flow.y = mediaTop - (pc.ContainerHPx > 0 ? pc.ContainerHPx : pc.MediaHPx * 2) * PxPt;
            flow.lastWasHardBreak = false;
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
                t.Append('<').Append(System.Convert.ToHexString(hex)).Append("> Tj ET ");
                flow.page.AddContentStream(Encoding.ASCII.GetBytes(t.ToString()));
            }

            // Figure first (graphics only — contributes no text fragments).
            if (tp.SvgIdx >= 0 && tp.SvgIdx < inlineSvgs.Count)
            {
                var figBytes = ImageRasterizer.RasterizeSvg(inlineSvgs[tp.SvgIdx], out _, out _);
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
    private static void LayoutSearchForm(
        SearchForm sf, HtmlFlowCursor flow, HtmlDocProfile profile, Document doc, Core.PdfDictionary docFontDict, double marginBottom, double marginLeft, double marginTop, double pageHeight, double pageWidth, HtmlLoadOptions? options, List<(Page page, Aspose.Pdf.Rectangle rect, string url, string? text)> pendingLinks)
    {
            const double PxPt = 0.75;
            var invf = System.Globalization.CultureInfo.InvariantCulture;
            var totalH = (sf.MarginTopPx + sf.InputHeightPx + sf.GapPx + sf.ButtonHeightPx + sf.MarginBottomPx) * PxPt;
            if (flow.y - totalH < marginBottom)
            {
                flow.page = doc.Pages.Add(pageWidth, pageHeight);
                EnsureFonts(flow.page, docFontDict);
                flow.y = pageHeight - marginTop; flow.pendingTopDrop = profile.hasZeroTopMargin;
            }
            flow.y -= sf.MarginTopPx * PxPt;
            var cellW = sf.CellWidthPx * PxPt;
            var cellX = marginLeft + (flow.contentWidth - cellW) / 2;
            var inputW = sf.InputWidthPx * PxPt;
            var inputH = sf.InputHeightPx * PxPt;

            var fld = new Forms.TextBoxField(flow.page, new Rectangle(cellX, flow.y - inputH, cellX + inputW, flow.y));
            if (!string.IsNullOrEmpty(sf.InputName)) fld.PartialName = sf.InputName;
            doc.Form.Add(fld, flow.page.Number);
            DrawBox(flow.page, cellX, flow.y - inputH, inputW, inputH,
                border: Color.FromArgb(0, 0, 0), borderWidth: 0.75, fill: null);

            if (!string.IsNullOrEmpty(sf.IconSrc))
            {
                var ib = LoadConverterImage(sf.IconSrc, options);
                if (ib is not null)
                {
                    var iw = sf.IconWPx * PxPt;
                    var ih2 = sf.IconHPx * PxPt;
                    var ix = cellX + inputW - sf.IconRightPx * PxPt - iw;
                    var iy = flow.y - sf.IconTopPx * PxPt;
                    try { flow.page.AddImage(ib, new Rectangle(ix, iy - ih2, ix + iw, iy)); } catch { }
                }
            }

            var res0 = flow.page.Dict.Get("Resources") as Core.PdfDictionary;
            var fdict = res0?.Get("Font") as Core.PdfDictionary;
            var arial = PosFace("Arial");

            if (!string.IsNullOrEmpty(sf.LinkText) && arial.ttf is not null && fdict is not null)
            {
                var lx = cellX + cellW + sf.LinkMarginLeftPx * PxPt;
                var lf = sf.LinkFontPx * PxPt;
                var lbase = flow.y - 5 * PxPt - 0.85 * lf;
                var g0 = new StringBuilder();
                // clip at the content box so an overlong side link ends at the margin
                g0.Append("q ");
                g0.Append($"{marginLeft.ToString("F2", invf)} {(flow.y - inputH - 30).ToString("F2", invf)} {flow.contentWidth.ToString("F2", invf)} {(inputH + 60).ToString("F2", invf)} re W n ");
                var (rn, hex) = Text.Type0FontEmbedder.Embed(fdict, arial.ttf, "Arial", sf.LinkText!, stripSpacesInBaseFont: true);
                g0.Append("BT ");
                g0.Append($"{(sf.LinkColor.R / 255.0).ToString("F5", invf)} {(sf.LinkColor.G / 255.0).ToString("F5", invf)} {(sf.LinkColor.B / 255.0).ToString("F5", invf)} rg ");
                g0.Append($"/{rn} {lf.ToString("F1", invf)} Tf 1 0 0 1 {lx.ToString("F2", invf)} {lbase.ToString("F2", invf)} Tm ");
                g0.Append('<').Append(System.Convert.ToHexString(hex)).Append("> Tj ET Q ");
                flow.page.AddContentStream(Encoding.ASCII.GetBytes(g0.ToString()));
                if (!string.IsNullOrEmpty(sf.LinkUrl))
                    pendingLinks.Add((flow.page, new Rectangle(lx, lbase - 3,
                        Math.Min(lx + MeasureFaceText("Arial", sf.LinkText!, lf), marginLeft + flow.contentWidth),
                        lbase + lf), sf.LinkUrl!, sf.LinkText));
            }

            flow.y -= inputH + sf.GapPx * PxPt;

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
                    g1.Append($"{bx.ToString("F2", invf)} {(flow.y - bh).ToString("F2", invf)} {bw.ToString("F2", invf)} {bh.ToString("F2", invf)} re f ");
                    g1.Append("0 0 0 RG 0.75 w ");
                    g1.Append($"{(bx + 0.375).ToString("F2", invf)} {(flow.y - bh + 0.375).ToString("F2", invf)} {(bw - 0.75).ToString("F2", invf)} {(bh - 0.75).ToString("F2", invf)} re S Q ");
                    var label = sf.Buttons[bi].Label;
                    var (rn2, hex2) = Text.Type0FontEmbedder.Embed(fdict, arial.ttf, "Arial", label, stripSpacesInBaseFont: true);
                    var tw = MeasureFaceText("Arial", label, bfpt);
                    var tx = bx + (bw - tw) / 2;
                    var tbase = flow.y - (bh + 0.72 * bfpt) / 2;
                    g1.Append("BT ");
                    g1.Append($"{(sf.ButtonFg.R / 255.0).ToString("F5", invf)} {(sf.ButtonFg.G / 255.0).ToString("F5", invf)} {(sf.ButtonFg.B / 255.0).ToString("F5", invf)} rg ");
                    g1.Append($"/{rn2} {bfpt.ToString("F1", invf)} Tf 1 0 0 1 {tx.ToString("F2", invf)} {tbase.ToString("F2", invf)} Tm ");
                    g1.Append('<').Append(System.Convert.ToHexString(hex2)).Append("> Tj ET ");
                    bx += bw + sf.ButtonGapPx * PxPt;
                }
                flow.page.AddContentStream(Encoding.ASCII.GetBytes(g1.ToString()));
            }

            flow.y -= (sf.ButtonHeightPx + sf.MarginBottomPx) * PxPt;
            flow.lastWasHardBreak = false;
    }

    /// <summary>Lays out one right-to-left SVG diagram table and advances the flow cursor past it.</summary>
    /// <remarks>Lifted verbatim out of the block-dispatch loop in
    /// <see cref="ConvertFromHtml"/>.</remarks>
    private static void LayoutRtlSvgDiagram(
        RtlSvgTable dg, HtmlFlowCursor flow, HtmlDocProfile profile, Document doc, Core.PdfDictionary docFontDict, double marginBottom, double marginLeft, double marginTop, double pageHeight, double pageWidth, List<byte[]> inlineSvgs)
    {
            // The arm's row constants (the 49.3 px title-baseline drop and its
            // siblings) were measured at the LEGACY flow's section entry. The UA
            // serif flow reaches this arm ~12.2 pt lower - it charges the full
            // preceding h6 bottom margin the legacy flow did not - so the whole
            // calibrated canvas would shift down by that much. Re-anchor the
            // entry to the calibration's own convention (measured:
            // title-label ink 103.11 with the lift, 115.35 without).
            if (profile.uaStdSerif && !profile.deadExternalCss) flow.y += DgUaEntryLiftPt;
            const double PxPt = 0.75;
            var invd = System.Globalization.CultureInfo.InvariantCulture;
            var canvasW = dg.WidthPx * PxPt;
            var canvasRight = marginLeft + flow.contentWidth;
            var canvasLeft = canvasRight - canvasW;
            var arialD = PosFace("Arial");
            var fontDictD = flow.page.Dict.Get("Resources") is Core.PdfDictionary dres
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
                flow.page.AddContentStream(Encoding.ASCII.GetBytes(t.ToString()));
            }

            var titleRowH = (dg.TitleText is null ? 0 : 81.3) * PxPt;
            var figH = dg.MainSvgHPx * PxPt;
            var labelRowH = (dg.MidLabels.Count > 0 ? 66.7 : 0.0) * PxPt;
            var legendBoxH = dg.LegendWFrac[0] * canvasW; // widest legend svg's square
            var legendLabelH = 22 * PxPt;
            var totalH = titleRowH + figH + labelRowH + legendBoxH + legendLabelH;
            if (flow.y - totalH < marginBottom && flow.y < pageHeight - marginTop - 1e-3)
            {
                flow.page = doc.Pages.Add(pageWidth, pageHeight);
                EnsureFonts(flow.page, docFontDict);
                flow.y = pageHeight - marginTop; flow.pendingTopDrop = profile.hasZeroTopMargin;
                fontDictD = flow.page.Dict.Get("Resources") is Core.PdfDictionary dres2
                    ? dres2.Get("Font") as Core.PdfDictionary : null;
            }

            if (dg.TitleText is not null)
                DrawRtlText(dg.TitleText, 0, flow.y - 49.3 * PxPt, dg.TitleFontPx * PxPt, centerCanvas: true);
            flow.y -= titleRowH;

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
                        flow.page.AddImage(figBytes, new Rectangle(figX, flow.y - figH, figX + drawW, flow.y));
                    }
                    catch { }
                }
            }
            flow.y -= figH;

            if (dg.MidLabels.Count > 0)
                foreach (var (text, col) in dg.MidLabels)
                {
                    var k = Math.Min(col, dg.MidLabelRightFrac.Length - 1);
                    DrawRtlText(text, canvasLeft + dg.MidLabelRightFrac[k] * canvasW,
                        flow.y - 24 * PxPt, dg.LabelFontPx * PxPt);
                }
            flow.y -= labelRowH;

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
                            flow.page.AddImage(sw, new Rectangle(boxLeft, flow.y - legendBoxH,
                                boxLeft + boxW, flow.y));
                        }
                        catch { }
                }
                if (label.Length > 0)
                    DrawRtlText(label, canvasLeft + dg.LegendLabelRightFrac[k] * canvasW,
                        flow.y - legendBoxH - 16 * PxPt, dg.LabelFontPx * PxPt);
            }
            flow.y -= legendBoxH + legendLabelH;

            flow.lastWasHardBreak = false;
    }

    /// <summary>Lays out one flex-grid block and advances the flow cursor past it.</summary>
    /// <remarks>Lifted verbatim out of the block-dispatch loop in
    /// <see cref="ConvertFromHtml"/>.</remarks>
    private static void LayoutFlexGrid(
        FlexGrid fg, HtmlFlowCursor flow, Document doc, Core.PdfDictionary docFontDict, double marginBottom, double marginLeft, double marginRight, double marginTop, double pageHeight, double pageWidth)
    {
            var invF = System.Globalization.CultureInfo.InvariantCulture;
            var fSerifB = PosFace("Times New Roman Bold");
            var fontDictF = flow.page.Dict.Get("Resources") is Core.PdfDictionary fres
                ? fres.Get("Font") as Core.PdfDictionary : null;
            double FWidth(string s, double pt)
                => MeasureFaceText("Times New Roman Bold", s, pt);
            void FDraw(string s, double x, double glyphTopDown, double pt)
            {
                if (fontDictF is null || fSerifB.ttf is null || s.Length == 0) return;
                var baseline = pageHeight - (glyphTopDown + SerifAscEm * pt);
                var (rn, hex) = Text.Type0FontEmbedder.Embed(fontDictF, fSerifB.ttf,
                    "Times New Roman Bold", s, stripSpacesInBaseFont: true);
                flow.page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invF,
                    $"BT 0 0 0 rg /{rn} {pt:F1} Tf 1 0 0 1 {x:F2} {baseline:F2} Tm <{System.Convert.ToHexString(hex)}> Tj ET\n")));
            }
            void FLine(double x0, double y0d, double x1, double y1d)
                => flow.page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invF,
                    $"q 0 0 0 RG 0.75 w {x0:F2} {pageHeight - y0d:F2} m {x1:F2} {pageHeight - y1d:F2} l S Q\n")));

            var contL = marginLeft + CardBodyPadPt;
            // With a physical-width flow.page wrapper the container spans exactly
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
            // its full depth — past the flow.page bottom onto a continuation flow.page —
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
                    flow.page = doc.Pages.Add(pageWidth, pageHeight);
                    EnsureFonts(flow.page, docFontDict);
                    var t0 = marginTop;
                    FLine(contL + 0.38, t0, contL + 0.38, t0 + tail);
                    FLine(contR - 0.38, t0, contR - 0.38, t0 + tail);
                    FLine(contL + 0.38, t0 + tail, contR - 0.38, t0 + tail);
                    flow.y = pageHeight - (t0 + tail);
                }
            }
            else
            {
                FLine(contL + 0.38, fy, contR - 0.38, fy);
                FLine(contL + 0.38, contT + 0.38, contL + 0.38, fy);
                FLine(contR - 0.38, contT + 0.38, contR - 0.38, fy);
                flow.y = pageHeight - fy - FlexRowBorderPt;
            }
            flow.contentPage = flow.page;
            flow.lastWasHardBreak = false;
    }

}
