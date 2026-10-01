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
    private static double FreshPageTopY(HtmlDocProfile profile, double pageHeight, double marginTop)
        => profile.escapedAttrDoc
        ? pageHeight - 72 - 0.9 * 12
        // The Word export's continuation page starts at the page margin: the UA body inset is the
        // body's, spent once at the document top (probed: page 2's first baseline 84.73 = 72 + 0.905 x 14).
        : profile.wordExportDoc
        ? pageHeight - (marginTop - UaBodyMarginPt)
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
            // the UA text input beside a label column draws its black border
            else if (ctl.InputInColumn)
                DrawBox(flow.page, lx + UaInputBorderPt / 2, baseY + above - cH + UaInputBorderPt / 2,
                    cW - UaInputBorderPt, cH - UaInputBorderPt,
                    border: Color.Black, borderWidth: UaInputBorderPt, fill: null);
            // A textarea's UA border is the same 1 pt black stroke half a point inside its
            // widget rect that the control-box dialect draws (probed: 96.5..218.52 x
            // 77.75..103.75 around a 96..219.02 x 77.25..104.25 widget).
            else if (ctl.InputMultiline)
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
                    $"BT /{rn} {pt:0.##} Tf 1 0 0 1 {x:0.##} {baseY:0.##} Tm {KernedTj(srTtf, hex)}ET\n")));
            }
            else
                flow.page.AddContentStream(Encoding.ASCII.GetBytes(FormattableString.Invariant(
                    $"BT /{res} {pt:0.##} Tf 1 0 0 1 {x:0.##} {baseY:0.##} Tm ({EscapePdfString(text)}) Tj ET\n")));
        }

    /// <summary>Lays out one input-field or inline-items block and advances the flow past it.</summary>
    /// <remarks>Lifted verbatim out of the block-dispatch loop in
    /// <see cref="ConvertFromHtml"/>.</remarks>
    private static void LayoutInputFieldBlock(Block block, HtmlFlowCursor flow, HtmlDocProfile profile, Document doc, Core.PdfDictionary docFontDict, double marginBottom, double marginLeft, double marginTop, double pageHeight, double pageWidth, double blockFontSize, double lineHeight)
    {
        var ib = new InputFieldBlockState();
        ib.block = block;
        ib.flow = flow;
        ib.profile = profile;
        ib.doc = doc;
        ib.docFontDict = docFontDict;
        ib.marginBottom = marginBottom;
        ib.marginLeft = marginLeft;
        ib.marginTop = marginTop;
        ib.pageHeight = pageHeight;
        ib.pageWidth = pageWidth;
        ib.blockFontSize = blockFontSize;
        ib.lineHeight = lineHeight;
        // An inline run: label text and controls share wrapping line boxes
        // with a pen, so label|input|label|select rows stay inline.
        if (ib.block.InlineItems is { Count: > 0 } runItems)
        {
            LayoutInlineFieldRun(ib, runItems);
            return;   // the block is laid out; the loop this came from would continue
        }

        if (ib.flow.y < ib.pageHeight - ib.marginTop - 1e-3)
            ib.flow.y -= ib.block.MarginTop;
        ib.fieldH = ib.block.InputHeight > 0 ? ib.block.InputHeight : ib.lineHeight;
        ib.boxAbove = !ib.block.InputDrawValue ? 0
            : ib.block.IsSelectBox ? SelectBoxAboveBaselinePt : InputBoxAboveBaselinePt;
        if (ib.flow.y + ib.boxAbove - ib.fieldH < ib.marginBottom)
        {
            ib.flow.page = ib.doc.Pages.Add(ib.pageWidth, ib.pageHeight);
            EnsureFonts(ib.flow.page, ib.docFontDict);
            ib.flow.y = FreshPageTopY(ib.profile, ib.pageHeight, ib.marginTop); ib.flow.pendingTopDrop = ib.profile.hasZeroTopMargin;
        }
        double? columnAbove = null;
        if (ib.block.InputInColumn)
        {
            // the cursor is the label's line top: the baseline sits one ascent under it
            var asc = (WinMetricsFor(UaSerifFaceName) is { } uaFm ? uaFm.asc : UaSerifAscentEm) * ib.blockFontSize;
            columnAbove = ib.fieldH - asc - UaInputBoxBelowBaselinePt;
        }
        EmitControlAt(ib.block, ib.marginLeft + ib.block.LeftIndent, ib.flow.y, ib.flow, ib.doc, ib.lineHeight, columnAbove);
        ib.flow.y -= (ib.block.InputAdvance > 0 ? ib.block.InputAdvance : ib.fieldH) + ib.block.MarginBottom;
        ib.flow.lastWasHardBreak = false;
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
        Document? svgVector = null;
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
                    Compat.Format(System.Globalization.CultureInfo.InvariantCulture,
                        $" width=\"{vpWpx:0.##}\" height=\"{vpHpx:0.##}\""));
                svgSrc = Encoding.UTF8.GetBytes(svgFull
                    .Remove(svgHeadM.Index, svgHeadM.Length)
                    .Insert(svgHeadM.Index, vpTag));
            }
            (bytes, var vw, var vh, svgVector) = ImageRasterizer.RasterizeSvgWithDocument(svgSrc);
            svgNatW = vw * 0.75; svgNatH = vh * 0.75;
        }
        else
        {
            bytes = LoadConverterImage(block.ImageSrc, options);
            if (IsSvgBytes(bytes))
            {
                (bytes, var vw, var vh, svgVector) = ImageRasterizer.RasterizeSvgWithDocument(bytes!);
                svgNatW = vw * 0.75; svgNatH = vh * 0.75;
            }
        }
        if (bytes is not null)
        {
            if (!PlaceImage(bytes, svgNatW, svgNatH, block, flow, profile, doc, docFontDict, bandStack, marginBottom, marginLeft, marginTop, pageHeight, pageWidth, svgVector?.Pages.Count > 0 ? svgVector.Pages[1] : null)) return;
        }
        else if (profile.uaStdSerif && !profile.msoFilteredDoc && !profile.escapedAttrDoc
            && block.ImageWidth > 0 && block.ImageHeight > 0)
        {
            PlaceUaMissingImage(block, flow, profile, doc, docFontDict, marginBottom, marginLeft, marginTop, pageHeight, pageWidth);
        }
        else if (profile.msoFilteredDoc)
        {
            PlaceMsoMissingImage(flow, doc, marginLeft, pageHeight, lineHeight);
        }
        else if (profile.escapedAttrDoc)
        {
            PlaceEscapedMissingImage(flow, profile, doc, docFontDict, marginBottom, marginLeft, marginTop, pageHeight, pageWidth);
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
                flow.y = FreshPageTopY(profile, pageHeight, marginTop); flow.pendingTopDrop = profile.hasZeroTopMargin;
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
    private static void LayoutHardBreakBlock(ConvertState cv, Block block, HtmlBlockMetrics metrics, bool breakAfterTable, bool wasRow)
    {
            // Border-top divider marker: the div's rule strokes here, above
            // its content, and spends only its own width.
            if (block.BorderTopOnly && block.BorderColor is { } topRule
                && block.BorderWidth > 0)
            {
                var invtr = System.Globalization.CultureInfo.InvariantCulture;
                cv.flow.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(invtr,
                    $"q {topRule.R / 255.0:0.###} {topRule.G / 255.0:0.###} {topRule.B / 255.0:0.###} RG " +
                    $"{block.BorderWidth:0.##} w {cv.marginLeft:0.##} {cv.flow.y - block.BorderWidth / 2:0.##} m " +
                    $"{cv.pageWidth - cv.marginLeft:0.##} {cv.flow.y - block.BorderWidth / 2:0.##} l S Q\n")));
                // The rule spends its own width plus the wrapper's
                // padding-top under it (measured: the From block opens
                // pad + margin below the rule).
                cv.flow.y -= block.BorderWidth + block.PadTop;
                cv.flow.contentPage = cv.flow.page;
                cv.flow.lastWasHardBreak = false;
                return;   // the block is laid out; the loop this came from would continue
            }
            // Prefer the explicit CSS height over the default half-line
            // spacer — CMS template HTML often uses empty styled divs as
            // visual separator bars, and ignoring their height would
            // collapse intended pagination.
            // A <br> directly after a styled row ends a full default-size line box
            // (the browser's 16px body line), not the usual half-line spacer.
            TraceHardBreakSeat(cv, block, metrics, wasRow);
            // A break that OPENS an unwrapped wrapper-table row stands the row chrome
            // the unwrap padded its first block by, exactly as a text block spends it
            // (probed on the RTL letter: the cell's leading <br/> line starts 2.25
            // under the table top, the next table's leading nbsp line 4.5 under the
            // previous table's last line).
            if (cv.profile.uaStdSerif && block.PadTop > 0) cv.flow.y -= block.PadTop;
            var spacer = HardBreakSpacerPt(cv, block, metrics, breakAfterTable, wasRow);
            PaintSpacerBox(cv, block, spacer);
            if (spacer > 0)
            {
                if (cv.flow.y - spacer < cv.marginBottom)
                {
                    cv.flow.page = cv.doc.Pages.Add(cv.pageWidth, cv.pageHeight);
                    EnsureFonts(cv.flow.page, cv.docFontDict);
                    cv.flow.y = FreshPageTopY(cv.profile, cv.pageHeight, cv.marginTop); cv.flow.pendingTopDrop = cv.profile.hasZeroTopMargin;
                }
                cv.flow.y -= spacer;
            }
            cv.flow.lastWasHardBreak = true;
            cv.flow.lastBreakWasUaSpacer = block.UaSpacerPara;
            // A zero-space break (the form dialect's float-clears) is layout-inert:
            // it must not hide the preceding text block from the <hr>/image rewind.
            if (spacer > 0)
            {
                cv.flow.prevFlowMarginBottom = 0;
                cv.flow.prevFlowLineHeight = 0;
            }
    }

    /// <summary>The UA paragraph margin a post-table tail's first real line break stands for the
    /// paragraph that follows (see HardBreakSpacerPt); the flow remembers it stood it.</summary>
    private static double UaTailMarginPt(ConvertState cv, Block block, bool breakAfterTable)
    {
        if (!(cv.profile.uaBareDoc && breakAfterTable && block.UaSpacerPara)) return 0;
        cv.flow.uaTailMarginSpent = true;
        return UaParagraphMarginPt;
    }

    /// <summary>ASPOSE_TRACE_SEAT=1: the state a hard break spends its spacer from (sheet-typography flow).</summary>
    private static void TraceHardBreakSeat(ConvertState cv, Block block, HtmlBlockMetrics metrics, bool wasRow)
    {
        if (!cv.profile.sheetTypographyDoc || Environment.GetEnvironmentVariable("ASPOSE_TRACE_SEAT") != "1") return;
        Console.WriteLine(FormattableString.Invariant($"[seat-hb] y={cv.flow.y:0.##} fs={block.FontSize:0.##} mfs={metrics.blockFontSize:0.##} lh={metrics.lineHeight:0.##} explicit={block.ExplicitHeight:0.##} br={block.IsLineBreak} lastHb={cv.flow.lastWasHardBreak} wasRow={wasRow} mt={block.MarginTop:0.##} mb={block.MarginBottom:0.##}"));
    }

    /// <summary>The vertical space a hard break spends: its declared height, its dialect's
    /// empty-paragraph box, or the line box of the face and size it stands in.</summary>
    /// <summary>The percent line-height factor a UA break's line box resolves at: the break's own
    /// (inherited) factor, else the body tag's where its attribute is the flow's typography; 0 = none.</summary>
    private static double UaBreakLineFactor(ConvertState cv, Block block)
        => block.UaLineFactor > 0 ? block.UaLineFactor
            : cv.uaBodyFaceFromAttr && cv.bodyLineFactor > 0 ? cv.bodyLineFactor : 0;

    private static double HardBreakSpacerPt(ConvertState cv, Block block, HtmlBlockMetrics metrics, bool breakAfterTable, bool wasRow)
    {
        var spacer = block.ExplicitHeight > 0
            ? block.ExplicitHeight
            // Redline: an empty paragraph occupies its FULL 1.15 em box
            // (probed: 13.8 between the FORM header and the first grid).
            : cv.profile.redlineDiffDoc && metrics.blockFontSize > 0
            ? RedlineEmptyParaPt
            : (cv.flow.lastWasHardBreak ? 0 : (wasRow ? 13.5 : metrics.lineHeight * 0.5));
        // Form dialect: this document family separates sections with CSS
        // margins, and its bare <br>s are all float-clears (`clear:both`) that
        // collapse to the float bottom — they add no line boxes of their own.
        if (cv.profile.formHorizontalDoc && block.ExplicitHeight <= 0) spacer = 0;
        // Form-document dialect: every standalone <br> is one full line box at
        // its enclosing size — consecutive <br>s stack (no half-line coalescing).
        else if (cv.profile.formDialectTables && block.IsLineBreak)
            spacer = (block.FontSize > 0 ? block.FontSize : metrics.blockFontSize) * 1.3;
        // CSS run dialect: a standalone <br> is one full line box of the page
        // stylesheet's own base face and size — the same rule its cells pitch on.
        else if (cv.bodyCssFace is not null && block.IsLineBreak
                 && WinMetricsFor(cv.bodyCssFace) is { } brFace)
            spacer = MetricLineHeight(
                block.FontSize > 0 ? block.FontSize : cv.profile.bodyCssFontPt, brFace.sum);
        // Float flow: a <br> ENDS the line it sits on, and the cv.flow has already
        // spent that line's advance — so the first of a run is free and every one
        // after it stands a full line box of the paragraph's own pitch. Measured on
        // the certificate: its `<br><br>` sub-paragraph separator puts the next
        // glyph top exactly two pitches below the last one (440.71 against 440.72).
        else if (cv.profile.floatBothSidesDoc && block.IsLineBreak)
            spacer = cv.flow.lastWasHardBreak ? metrics.lineHeight : 0;
        // Metric flow: a real <br> is one full line box at the size of its enclosing
        // style — every <br> counts (no coalescing). Styled spacers keep their CSS
        // height; other empty containers collapse to nothing.
        if (cv.profile.metricFlow)
            spacer = block.IsLineBreak && WinMetricsFor(cv.profile.metricFace) is { } brm
                ? (cv.uaFlow
                    // A declared line box is the break's WHOLE box, nothing added.
                    ? block.LineBoxPt > 0 ? block.LineBoxPt
                    // (…or a percent line-height's factor at the break's size - MEASURED, the evaluation
                    //  form: the 11 pt body's `line-height: 100%` paces its inter-table <br>s 11)
                    : UaBreakLineFactor(cv, block) is > 0 and var brFactor
                    ? (block.FontSize > 0 ? block.FontSize : 12.0) * brFactor + UaTailMarginPt(cv, block, breakAfterTable)
                    // …otherwise the break's line box is the flow face's px-rounded hhea
                    // line at the break's size, the box every text line of the flow stands
                    // in (probed: a 14px cell's leading <br/> line is 16px = 12 pt, where
                    // 1.125 em gives 11.81; at 12 pt both give 13.5).
                    : MetricLineHeight(block.FontSize > 0 ? block.FontSize : 12.0,
                        HheaLineSumFor(cv.profile.metricFace) ?? brm.sum)
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
                      + UaTailMarginPt(cv, block, breakAfterTable)
                    // (the pt form's bare <br> is one line box of the body's own size: 8 pt Tahoma -> 13 px)
                    : MetricLineHeight(block.FontSize > 0 ? block.FontSize
                        : cv.profile.ptFormDoc && cv.profile.formBodyFontPt > 0 ? cv.profile.formBodyFontPt : 11.0, brm.sum))
                : block.IsLineBreak ? block.ExplicitHeight
                : block.IsHardBreak && block.ExplicitHeight > 0 ? block.ExplicitHeight
                : 0;
        return spacer;
    }
}
