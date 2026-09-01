using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Writes one block's wrapped lines into the flow: the text itself, the
    /// runs and faces it changes between, its links and anchors, and the decoration
    /// drawn under or through it.</summary>
    /// <remarks>Lifted verbatim out of the block-dispatch loop in
    /// <see cref="ConvertFromHtml"/>.</remarks>
    private static void WriteBlockTextLines(
        Block block, HtmlFlowCursor flow, HtmlDocProfile profile, HtmlBlockMetrics metrics,
        Document doc, Core.PdfDictionary docFontDict, StringBuilder sb,
        Dictionary<string, (Page page, double y)> anchorTargets,
        List<(Page page, Aspose.Pdf.Rectangle rect, string url, string? text)> pendingLinks,
        Dictionary<string, (string resName, Core.PdfIndirectRef fontRef)> embeddedFonts,
        Dictionary<string, (int objNum, string embedName)> fontFileCache,
        Stack<(double SavedML, double SavedCW, double TopY, double MinEndY, Page StartPage)> bandStack,
        bool articleFlow, bool uaFlow, double marginBottom, double marginLeft, double marginRight,
        double marginTop, double pageHeight, double pageWidth)
    {
        var bt = new BlockTextState();
        bt.block = block;
        bt.flow = flow;
        bt.profile = profile;
        bt.metrics = metrics;
        bt.doc = doc;
        bt.docFontDict = docFontDict;
        bt.sb = sb;
        bt.anchorTargets = anchorTargets;
        bt.pendingLinks = pendingLinks;
        bt.embeddedFonts = embeddedFonts;
        bt.fontFileCache = fontFileCache;
        bt.bandStack = bandStack;
        bt.articleFlow = articleFlow;
        bt.uaFlow = uaFlow;
        bt.marginBottom = marginBottom;
        bt.marginLeft = marginLeft;
        bt.marginRight = marginRight;
        bt.marginTop = marginTop;
        bt.pageHeight = pageHeight;
        bt.pageWidth = pageWidth;
        bt.lineIdx = -1;
    foreach (var line in bt.metrics.lines)
    {
        if (!WriteBlockTextLine(bt, line)) break;
    }
    }

    /// <summary>Writes one wrapped line of the block: its seat and alignment, its font runs, then its decorations and anchors; false when the block stops early.</summary>
    private static bool WriteBlockTextLine(BlockTextState bt, string line)
    {
        bt.lineIdx++;
        bt.lineNeedBelow = bt.profile.escapedAttrDoc && bt.block.InlineIconAfter ? SerifDescentRoomPt : bt.metrics.lineHeight;
        if (bt.flow.y - bt.lineNeedBelow < bt.marginBottom)
        {
            // Inside a float column the overflow is clipped, not paginated.
            if (bt.profile.floatBandDoc && bt.bandStack.Count > 0) { bt.flow.bandColClipped = true; return false; }
            bt.flow.page = bt.doc.Pages.Add(bt.pageWidth, bt.pageHeight);
            EnsureFonts(bt.flow.page, bt.docFontDict);
            bt.flow.y = FreshPageTopY(bt.profile.escapedAttrDoc, bt.pageHeight, bt.marginTop); bt.flow.pendingTopDrop = bt.profile.hasZeroTopMargin;
            // UA flow: a block pushed to a fresh page re-applies its margin-top at
            // the new page top (a continuation page's first paragraph baseline
            // = topMargin + p-gap + ascent, not topMargin + ascent).
            if (bt.uaFlow && bt.metrics.firstLineOfBlock) bt.flow.y -= bt.block.MarginTop;
        }
        bt.fontRes = ResolveFontRes(bt.flow.page, bt.block, bt.flow, bt.profile, bt.doc, bt.embeddedFonts, bt.fontFileCache);

        if (bt.metrics.firstLineOfBlock && !string.IsNullOrEmpty(bt.block.Marker) && !bt.block.MarkerAfter)
            EmitMarkerHere(bt);

        bt.invc = System.Globalization.CultureInfo.InvariantCulture;
        bt.lineXPos = bt.marginLeft + bt.block.LeftIndent + bt.metrics.floatLabelIndent;
        // UA-serif flow: the span's own margin-left and the element's
        // border inset the text within the element box.
        if (bt.profile.uaStdSerif && (bt.block.TextInsetPt > 0 || bt.block.BorderWidth > 0))
            bt.lineXPos += bt.block.TextInsetPt + bt.block.BorderWidth;
        // Lines still level with a left-floated image start past its right edge.
        if (bt.flow.floatIndentPt > 0 && bt.flow.y > bt.flow.floatBottomY + 1e-9)
            bt.lineXPos += bt.flow.floatIndentPt;
        // A CENTRED line in a declared float box centres between whichever float
        // it is level with and the box's own right edge - this seats the
        // certificate's first heading line at 367.33, the middle of 317.25 (the
        // left logo's right edge) .. 598.50 (the 550 px box from its 120 px inset).
        SeatLineInBoxes(bt, line);
        // Metric flow: y is the line-box TOP; the baseline sits half-leading +
        // ascent below it. A centered block (text-align:center class) centers its
        // measured line in the content box. Legacy: baseline at the cursor.
        AlignLine(bt, line);
        PrepareLinePaint(bt, line);
        bt.cjkFont = NeedsUnicode(line)
            && !(bt.profile.redlineDiffDoc && HasSymbolPua(line))
            // A line whose only non-WinAnsi characters are Specials
            // (U+FFFD from a mojibake decode) keeps the per-segment path:
            // the line draws in the flow face with only the
            // replacement glyph re-faced, never the whole line.
            && !OnlySpecialsNonAnsi(line) ? ResolveUnicodeFont(bt.uniSource) : null;
        bt.cjkTtf = bt.cjkFont?.SourceFontData?.TtfData;
        bt.cjkName = bt.cjkFont?.FontName ?? "Unicode";
        // RTL documents draw with the same face the right-align measurement
        // used (bold variant for bold blocks), so the anchored edge is exact.
        if (bt.profile.rtlDoc && bt.cjkTtf is not null && PosFace(bt.rtlFace).ttf is { } rtlTtf)
        {
            bt.cjkTtf = rtlTtf;
            bt.cjkName = bt.rtlFace;
        }
        if (!WriteLineRuns(bt, line)) return false;

        // CSS ::before marker: emitted after the item text so it is the later fragment.
        // UA-flow <u>/h1-underline: a stroke fs/10 thick, fs/10 under the
        // baseline, spanning the covered advance (probed: 2.4 w at +2.4
        // under the 24 pt worksheet title).
        DrawLineDecorations(bt, line);

        // Inline <a href> ranges overlapping this line get a link rect over
        // their run; resolved to a GoTo/URI action after layout.
        RegisterLineAnchors(bt, line);
        bt.metrics.cumChar += line.Length + 1;   // +1 for the space consumed at the wrap point
        bt.flow.y -= bt.metrics.lineHeight;
        if (bt.metrics.ptLeadExtraPt > 0) { bt.flow.y -= bt.metrics.ptLeadExtraPt; bt.metrics.ptLeadExtraPt = 0; }
        if (line.Length > 0) bt.flow.contentPage = bt.flow.page;
        return true;
    }

    /// <summary>Link rectangles for the line's anchors and the diff/form decoration runs drawn over it.</summary>
    private static void RegisterLineAnchors(BlockTextState bt, string line)
    {
        if (bt.block.Anchors is { Count: > 0 })
        {
            int lineStart = bt.metrics.cumChar, lineEnd = bt.metrics.cumChar + line.Length;
            foreach (var (aStart, aLen, url) in bt.block.Anchors)
            {
                int ov0 = Math.Max(aStart, lineStart), ov1 = Math.Min(aStart + aLen, lineEnd);
                if (ov1 > ov0 && !string.IsNullOrEmpty(url))
                {
                    double x0 = bt.metrics.lineX + (ov0 - lineStart) * bt.metrics.charW;
                    double x1 = bt.metrics.lineX + (ov1 - lineStart) * bt.metrics.charW;
                    // The link's description (annotation /Contents, surfaced as its
                    // tooltip) is the anchor's visible text.
                    string? aText = aStart >= 0 && aLen > 0 && aStart + aLen <= bt.block.Text.Length
                        ? bt.block.Text.Substring(aStart, aLen) : null;
                    bt.pendingLinks.Add((bt.flow.page, new Aspose.Pdf.Rectangle(x0, bt.flow.y, x1, bt.flow.y + bt.metrics.lineHeight), url, aText));
                }
            }
        }
        // Redline decoration ink: stroke each decoration run's share of
        // this line. text-decoration kinds ride the baseline (underline
        // 0.09 em below, strike 0.26 em above, 0.1 em stroke — probed on
        // the expected 18 pt struck headers and 10 pt underlines) and
        // skip inter-word spaces; the marker
        // borders draw one hairline 0.25 em under the baseline in their
        // own colour, dashed [1.5 0.75] for the changed-marker.
        if ((bt.profile.redlineDiffDoc || bt.profile.dwFormDoc) && bt.block.DecorRuns is { Count: > 0 } decRuns
            && line.Length > 0)
        {
            var decFace = (bt.block.FontFamily ?? "Times New Roman")
                + (bt.block.FontRes == "F2" || bt.block.EmBold ? " Bold" : "");
            var dsb = new StringBuilder();
            double XAt(int p) => bt.lineXPos + (p <= bt.metrics.cumChar ? 0
                : bt.block.SmallCaps
                ? MeasureSmallCapsText(decFace, line[..Math.Min(p - bt.metrics.cumChar, line.Length)], bt.metrics.blockFontSize)
                : MeasureFaceText(decFace, line[..Math.Min(p - bt.metrics.cumChar, line.Length)], bt.metrics.blockFontSize));
            Color DecColorAt(int p)
            {
                if (bt.block.ColorRuns is not null)
                    foreach (var (rs, rl, rc) in bt.block.ColorRuns)
                        if (p >= rs && p < rs + rl) return rc;
                return bt.block.ForeColor ?? Color.FromArgb(0, 0, 0);
            }
            foreach (var (dS, dL, dKind, dC) in decRuns)
            {
                var a0 = Math.Max(dS, bt.metrics.cumChar);
                var b0 = Math.Min(dS + dL, bt.metrics.cumChar + line.Length);
                if (b0 <= a0) continue;
                var dy = dKind switch
                {
                    // DataWorks links: the hairline rides higher — just under
                    // the descender line (measured: 1px at ink-bottom+1).
                    1 when bt.profile.dwFormDoc => bt.flow.y - RedlineUnderDropEm * bt.metrics.blockFontSize
                        + DwUnderRaisePt,
                    1 => bt.flow.y - RedlineUnderDropEm * bt.metrics.blockFontSize,
                    2 => bt.flow.y + RedlineStrikeRiseEm * bt.metrics.blockFontSize,
                    _ => bt.flow.y - RedlineBorderDropEm * bt.metrics.blockFontSize,
                };
                var dw = dKind == 1 && bt.profile.dwFormDoc ? DwUnderWidthPt
                    : dKind <= 2 ? RedlineDecorWidthEm * bt.metrics.blockFontSize : 0.75;
                var dcol = dKind <= 2 ? DecColorAt(a0) : dC ?? Color.FromArgb(0, 0, 0);
                dsb.Append(string.Create(bt.invc,
                    $"q {dcol.R / 255.0:0.###} {dcol.G / 255.0:0.###} {dcol.B / 255.0:0.###} RG {dw:0.##} w "));
                if (dKind == 4) dsb.Append("[1.5 0.75] 0 d ");
                dsb.Append(string.Create(bt.invc,
                    $"{XAt(a0):0.##} {dy:0.##} m {XAt(b0):0.##} {dy:0.##} l S "));
                dsb.Append("Q\n");
            }
            if (dsb.Length > 0)
                bt.flow.page.AddContentStream(Encoding.ASCII.GetBytes(dsb.ToString()));
        }
    }

    /// <summary>The line's underline runs, its CSS marker and its named anchor targets.</summary>
    private static void DrawLineDecorations(BlockTextState bt, string line)
    {
        if (bt.profile.uaStdSerif && bt.block.UnderlineRuns is { Count: > 0 } uaURuns && line.Length > 0)
        {
            int uLineStart = bt.metrics.cumChar, uLineEnd = bt.metrics.cumChar + line.Length;
            foreach (var (us0, ul0) in uaURuns)
            {
                var us1 = Math.Max(us0, uLineStart);
                var ue1 = Math.Min(us0 + ul0, uLineEnd);
                if (ue1 <= us1) continue;
                var uPre = MeasureFaceText(bt.metrics.metricMeasureFace,
                    line[..(us1 - uLineStart)], bt.metrics.blockFontSize);
                var uSeg = MeasureFaceText(bt.metrics.metricMeasureFace,
                    line[(us1 - uLineStart)..(ue1 - uLineStart)], bt.metrics.blockFontSize);
                var uy = (bt.profile.metricFlow && bt.metrics.metricDrop > 0 ? bt.flow.y - bt.metrics.metricDrop : bt.flow.y)
                    - bt.metrics.blockFontSize / 10.0;
                // The stroke takes the block's own ink (a linked line's
                // underline draws in the link colour).
                var uInk = bt.block.ForeColor is { } uCol
                    ? string.Create(System.Globalization.CultureInfo.InvariantCulture,
                        $"{uCol.R / 255.0:0.###} {uCol.G / 255.0:0.###} {uCol.B / 255.0:0.###}")
                    : "0 0 0";
                bt.flow.page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"q {uInk} RG {bt.metrics.blockFontSize / 10.0:0.##} w {bt.lineXPos + uPre:F2} {uy:F2} m {bt.lineXPos + uPre + uSeg:F2} {uy:F2} l S Q\n")));
            }
        }
        if (bt.metrics.firstLineOfBlock && !string.IsNullOrEmpty(bt.block.Marker) && bt.block.MarkerAfter)
            EmitMarkerHere(bt);

        // Restore the default black fill so the coloured run does not leak into
        // later content on this page.
        if (bt.lineForeColor is not null)
            bt.flow.page.AddContentStream(Encoding.ASCII.GetBytes("0 0 0 rg"));

        // A named anchor declared in this block resolves to the page + y of
        // its first rendered line, so a #fragment link lands here.
        if (bt.metrics.firstLineOfBlock && bt.block.AnchorNames is { Count: > 0 })
            foreach (var nm in bt.block.AnchorNames)
                bt.anchorTargets[nm] = (bt.flow.page, bt.flow.y + bt.metrics.lineHeight);
        bt.metrics.firstLineOfBlock = false;
    }

    /// <summary>Writes the line as font-segmented runs on the serif/print-grid/diff document classes; false when the block must stop.</summary>
    private static bool WriteSerifGridRuns(BlockTextState bt, string line)
    {
        // Full-document UA-default flow draws with the Standard-14 serif faces
        // (Times-Roman / -Bold / -Italic) — serif output with nothing embedded,
        // so a no-font-family document keeps its Standard-14-only resources.
        // The print grid draws the Standard-14 Helvetica pair instead.
        // The certificate dialect reaches this branch only so its
        // family-FREE text can take the UA serif as a real face; text that
        // names a family we cannot resolve keeps the sans fallback its own
        // font stack asks for, rather than dropping to the Standard-14 serif.
        var regRes = bt.profile.printGrid || bt.profile.floatBothSidesDoc ? "F1" : "F5";
        var boldRes = bt.profile.printGrid || bt.profile.floatBothSidesDoc ? "F2" : "F6";
        var stdRes = bt.block.FontRes == "F2" ? boldRes : bt.block.FontRes == "F3" ? (bt.profile.printGrid || bt.profile.floatBothSidesDoc ? "F3" : "F7") : regRes;
        // A <font face> block carries a RESOLVED family: its runs draw
        // in that face (embedded Type0), bold variant for bold blocks —
        // the std-serif override serves only family-free text.
        // The certificate dialect draws its family-free text in the UA
        // serif too, and unlike the Standard-14 resource table (which has no
        // bold-italic slot at all) a real face carries both emphases.
        if ((bt.profile.uaStdSerif || bt.profile.redlineDiffDoc || bt.profile.dwFormDoc || bt.profile.floatBothSidesDoc)
            && bt.block.FontFamily is { } uafFam
            && PosFace(uafFam
                    + (bt.block.FontRes == "F2" || bt.block.EmBold ? " Bold" : "")
                    + (bt.profile.floatBothSidesDoc && (bt.block.FontRes == "F3" || bt.block.EmItalic)
                        ? " Italic" : "")).ttf
                is { } uafTtf
            && bt.flow.page.Dict.Get("Resources") is Core.PdfDictionary uafRes
            && uafRes.Get("Font") is Core.PdfDictionary uafDict)
        {
            if (!WriteSerifClassRuns(bt, line, uafFam, uafTtf, uafDict)) return false;
        }
        else
        {
        if (!WritePrintGridRuns(bt, line, regRes, boldRes, stdRes)) return false;
        }
        return true;
    }

    /// <summary>The line's seat as content-stream text, its background and foreground colour, and its RTL visual order.</summary>
    private static void PrepareLinePaint(BlockTextState bt, string line)
    {
        bt.lnX = bt.lineXPos.ToString("F2", bt.invc);
        bt.lnY = (bt.profile.metricFlow && bt.metrics.metricDrop > 0 ? bt.flow.y - bt.metrics.metricDrop : bt.flow.y).ToString("F2", bt.invc);

        // CSS background-color: draw a fill rectangle behind this line, spanning the
        // block's content width, BEFORE the text (append order = draw order, so the
        // text lands on top). The rect covers the baseline origin of every fragment on
        // the line so text extraction recovers it as TextState.BackgroundColor. Fill
        // components are emitted at F5 so Color.FromRgb's Round(c*255) round-trips exactly.
        if (bt.block.BackgroundColor is { } bgc)
        {
            var bgSb = new StringBuilder();
            bgSb.Append("q ");
            bgSb.Append($"{(bgc.R / 255.0).ToString("F5", bt.invc)} {(bgc.G / 255.0).ToString("F5", bt.invc)} {(bgc.B / 255.0).ToString("F5", bt.invc)} rg ");
            // A painted box (tiny background tile × declared CSS size) fills its
            // whole declared rect once, on the block's first line. The element is
            // a body-level container, so its box origin sits one UA body margin
            // inside the content origin on both axes; the fill spans the declared
            // width × height no matter how the text inside wraps. (The Min clamps
            // the first-line-box top back to the content top at a page start,
            // where the flow's entry drop has already been spent.)
            if (bt.block.BgBoxHeightPt > 0)
            {
                if (bt.metrics.firstLineOfBlock)
                {
                    // A bordered painted box (background + width/height + border
                    // rule) fills its BORDER box — declared content + border on
                    // each side — and strokes the border centred on the box edge;
                    // it hangs from the flow's content origin. The borderless
                    // tile box keeps its calibrated one-body-margin inset.
                    var pbBw = bt.block.BorderWidth > 0 && bt.block.BorderColor is not null
                        ? bt.block.BorderWidth : 0;
                    var bbX = bt.marginLeft + bt.block.LeftIndent + (pbBw > 0 ? 0 : UaBodyMarginPt);
                    var bbTop = Math.Min(bt.pageHeight - bt.marginTop, bt.metrics.yBeforeBlockLines + bt.metrics.lineHeight)
                        - UaBodyMarginPt;
                    var pbW = bt.block.BgBoxWidthPt + 2 * pbBw;
                    var pbH = bt.block.BgBoxHeightPt + 2 * pbBw;
                    bgSb.Append($"{bbX.ToString("F2", bt.invc)} {(bbTop - pbH).ToString("F2", bt.invc)} {pbW.ToString("F2", bt.invc)} {pbH.ToString("F2", bt.invc)} re f ");
                    if (pbBw > 0 && bt.block.BorderColor is { } pbc)
                    {
                        bgSb.Append($"{(pbc.R / 255.0).ToString("F5", bt.invc)} {(pbc.G / 255.0).ToString("F5", bt.invc)} {(pbc.B / 255.0).ToString("F5", bt.invc)} RG {pbBw.ToString("F2", bt.invc)} w ");
                        bgSb.Append($"{(bbX + pbBw / 2).ToString("F2", bt.invc)} {(bbTop - pbH + pbBw / 2).ToString("F2", bt.invc)} {(pbW - pbBw).ToString("F2", bt.invc)} {(pbH - pbBw).ToString("F2", bt.invc)} re S ");
                    }
                    bgSb.Append('Q');
                    bt.flow.page.AddContentStream(Encoding.ASCII.GetBytes(bgSb.ToString()));
                }
            }
            // A floated box's background fills its shrink-to-fit box: exactly
            // the measured text advance wide, one line box tall, hanging from
            // the line-box top (metric y).
            else if (bt.uaFloatW > 0)
            {
                bgSb.Append($"{bt.lineXPos.ToString("F2", bt.invc)} {(bt.flow.y - bt.metrics.lineHeight).ToString("F2", bt.invc)} {bt.uaFloatW.ToString("F2", bt.invc)} {bt.metrics.lineHeight.ToString("F2", bt.invc)} re f Q");
                bt.flow.page.AddContentStream(Encoding.ASCII.GetBytes(bgSb.ToString()));
            }
            // Metric flow: y is the LINE BOX TOP (the text draws a drop
            // below it), so the band is the CSS line box exactly — from y
            // down one line height, one UA body margin short of the content
            // right edge (measured: the saved-page title strip fills
            // 96..646.5 x 97.2..110.8 around its 108.0 baseline).
            else if (bt.profile.metricFlow && bt.metrics.metricDrop > 0)
            {
                var bgX = bt.marginLeft + bt.block.LeftIndent;
                var bgW = bt.flow.contentWidth - bt.block.LeftIndent - UaBodyMarginPt;
                var bandUp = bt.metrics.firstLineOfBlock ? bt.block.BandPadPt : 0;
                var bandDn = bt.block.BandPadPt;
                bgSb.Append($"{bgX.ToString("F2", bt.invc)} {(bt.flow.y - bt.metrics.lineHeight - bandDn).ToString("F2", bt.invc)} {bgW.ToString("F2", bt.invc)} {(bt.metrics.lineHeight + bandUp + bandDn).ToString("F2", bt.invc)} re f Q");
                bt.flow.page.AddContentStream(Encoding.ASCII.GetBytes(bgSb.ToString()));
            }
            else
            {
                var bgX = bt.marginLeft + bt.block.LeftIndent;
                var bgW = bt.flow.contentWidth - bt.block.LeftIndent;
                // A band block's fill extends by the div paddings the flow
                // reserved around the line: up only on the first line (the
                // interior lines' fills already touch), down on every line
                // (interior overlaps merge invisibly, the last line closes
                // the band's bottom pad).
                var bandUp = bt.metrics.firstLineOfBlock ? bt.block.BandPadPt : 0;
                var bandDn = bt.block.BandPadPt;
                bgSb.Append($"{bgX.ToString("F2", bt.invc)} {(bt.flow.y - bt.metrics.blockFontSize * 0.25 - bandDn).ToString("F2", bt.invc)} {bgW.ToString("F2", bt.invc)} {(bt.metrics.blockFontSize * 1.15 + bandUp + bandDn).ToString("F2", bt.invc)} re f Q");
                bt.flow.page.AddContentStream(Encoding.ASCII.GetBytes(bgSb.ToString()));
            }
        }
        bt.lineForeColor = bt.block.ForeColor is { } fc0 && (fc0.R != 0 || fc0.G != 0 || fc0.B != 0)
            ? bt.block.ForeColor : null;
        if (bt.lineForeColor is { } fc)
            bt.flow.page.AddContentStream(Encoding.ASCII.GetBytes(
                $"{(fc.R / 255.0).ToString("F5", bt.invc)} {(fc.G / 255.0).ToString("F5", bt.invc)} {(fc.B / 255.0).ToString("F5", bt.invc)} rg"));
        bt.isRtlLine = IsPureRtl(line);
        bt.uniSource = bt.isRtlLine ? ToVisualRtl(line)
            : Text.BidiReorderer.ContainsRtl(line) ? VisualizeMixedRtl(line) : line;
    }

    /// <summary>Centred, right-aligned and indented lines take their x from the document class's rule.</summary>
    private static void AlignLine(BlockTextState bt, string line)
    {
        if (bt.profile.metricFlow && bt.metrics.metricDrop > 0 && bt.block.AlignCenter && line.Length > 0)
        {
            var mw = MeasureFaceText(bt.metrics.metricMeasureFace, line, bt.metrics.blockFontSize);
            // A class WIDTH is the element's own box — centring happens
            // inside it, not the whole content width (the ledger title).
            var ctrBox = bt.profile.uaStdSerif && bt.block.WidthPx > 0
                ? bt.block.WidthPx * 0.75 : bt.flow.contentWidth;
            bt.lineXPos = Math.Max(bt.marginLeft, bt.marginLeft + (ctrBox - mw) / 2);
        }
        // UA-default serif flow honours an INLINE text-align:center: the line
        // centres in its element's content box — from the block's indent to a
        // right edge one full left-margin (90 + 6 body) inside the page, the
        // frame symmetric to the flow's left content origin (measured: "test"
        // centred at (126+499)/2 inside <ul><li><div text-align:center>).
        else if (bt.profile.uaStdSerif && bt.metrics.metricDrop > 0 && bt.block.AlignCenterCss && line.Length > 0)
        {
            var mw = MeasureFaceText(bt.metrics.metricMeasureFace, line, bt.metrics.blockFontSize);
            var boxLeft = bt.marginLeft + bt.block.LeftIndent;
            var boxRight = bt.pageWidth - bt.marginLeft;
            bt.lineXPos = Math.Max(bt.marginLeft, boxLeft + (boxRight - boxLeft - mw) / 2);
        }
        // UA-default serif flow honours an inline text-align:right the
        // same way: the line pins to the body box's right edge (the
        // rating-date div ends at 96 + content = 517, measured).
        else if (bt.profile.uaStdSerif && bt.metrics.metricDrop > 0 && bt.block.AlignRight && line.Length > 0)
        {
            var mw = MeasureFaceText(bt.metrics.metricMeasureFace, line, bt.metrics.blockFontSize);
            bt.lineXPos = Math.Max(bt.marginLeft, bt.marginLeft + bt.flow.contentWidth - mw);
        }
        // Redline diff document: inline text-align centres/right-pins the
        // measured line (real face advances) in the content box.
        else if ((bt.profile.redlineDiffDoc || bt.profile.dwFormDoc) && (bt.block.AlignCenterCss || bt.block.AlignRight)
                 && line.Length > 0 && !string.IsNullOrEmpty(bt.block.FontFamily))
        {
            var rdMw = MeasureFaceText(
                bt.block.FontFamily + (bt.block.FontRes == "F2" || bt.block.EmBold ? " Bold" : ""),
                line, bt.metrics.blockFontSize);
            bt.lineXPos = Math.Max(bt.marginLeft, bt.block.AlignRight
                ? bt.marginLeft + bt.flow.contentWidth - rdMw
                : bt.marginLeft + (bt.flow.contentWidth - rdMw) / 2);
        }
        // Sectioned report: the browser honours a block's text-align, so a
        // right-aligned note pins to the content box's right edge and a
        // centred page footer sits on its middle.
        else if (bt.profile.sectionedReport && (bt.block.AlignRight || bt.block.AlignCenterCss) && line.Length > 0)
        {
            var mw = MeasureFaceText(
                bt.block.FontRes == "F2" ? "Arial Bold" : "Arial", line, bt.metrics.blockFontSize);
            bt.lineXPos = Math.Max(bt.marginLeft, bt.block.AlignRight
                ? bt.marginLeft + bt.flow.contentWidth - mw
                : bt.marginLeft + (bt.flow.contentWidth - mw) / 2);
        }
        // Print grid: text-align:right pins the measured line to the wrap
        // box's right edge.
        else if (bt.profile.printGrid && bt.block.AlignRight && line.Length > 0)
        {
            var mw = MeasureFaceText(bt.metrics.metricMeasureFace, line, bt.metrics.blockFontSize);
            bt.lineXPos = Math.Max(bt.marginLeft, bt.marginLeft + bt.flow.contentWidth - mw);
        }
        // The inline-body-margin dialect honours ALIGN="center" with the
        // metric face's real advances (its title divs centre on the sheet);
        // the pt-report flow centres its aligned paragraphs the same way.
        else if ((bt.profile.bodyBoxGridDoc || (bt.profile.metricFlow && bt.profile.emailNewsletterDoc))
                 && bt.block.AlignCenterAttr && line.Length > 0)
        {
            var mw = MeasureFaceText(bt.metrics.metricMeasureFace, line, bt.metrics.blockFontSize);
            bt.lineXPos = Math.Max(bt.marginLeft, bt.marginLeft + (bt.flow.contentWidth - mw) / 2);
        }
        // Legacy ALIGN="center" attribute: centre the measured line in the
        // content box (the box is the current float column inside a band).
        else if (!bt.profile.metricFlow && bt.block.AlignCenterAttr && line.Length > 0)
        {
            var mw = MeasureFaceText(
                bt.metrics.bandFace ?? (string.IsNullOrEmpty(bt.block.FontFamily) ? "Arial" : bt.block.FontFamily!),
                line, bt.metrics.blockFontSize);
            bt.lineXPos = Math.Max(bt.marginLeft + bt.block.LeftIndent,
                bt.marginLeft + bt.block.LeftIndent + (bt.flow.contentWidth - bt.block.LeftIndent - mw) / 2);
        }
        if (bt.profile.redlineDiffDoc && bt.block.TextIndentPt > 0 && bt.lineIdx == 0)
            bt.lineXPos += bt.block.TextIndentPt;
    }

    /// <summary>A float box, a right-aligned box, a centre band or an RTL page seats the line inside its box.</summary>
    private static void SeatLineInBoxes(BlockTextState bt, string line)
    {
        if (bt.metrics.floatBoxWidthPt > 0 && bt.block.AlignCenterCss && line.Length > 0)
        {
            var boxLeft = bt.marginLeft + bt.metrics.floatBoxLeftPt;
            var boxRight = boxLeft + bt.metrics.floatBoxWidthPt;
            var lineLeft = bt.metrics.besideLeftFloat
                ? Math.Max(boxLeft, bt.marginLeft + bt.flow.floatIndentPt) : boxLeft;
            var lineW = MeasureFaceText(
                string.IsNullOrEmpty(bt.block.FontFamily) ? "Arial" : bt.block.FontFamily!,
                line, bt.metrics.blockFontSize);
            if (boxRight - lineLeft > lineW)
                bt.lineXPos = lineLeft + (boxRight - lineLeft - lineW) / 2;
        }
        // Report label column: each wrapped line right-aligns inside its box,
        // measured in the report face's own metrics.
        if (bt.block.RightAlignBoxPt > 0 && line.Length > 0)
        {
            var raw = HeaderFooter.MeasureReportText(line, bt.metrics.blockFontSize,
                bt.block.FontRes == "F2");
            bt.lineXPos = bt.marginLeft + bt.block.LeftIndent + Math.Max(0, bt.block.RightAlignBoxPt - raw);
        }
        // Unwrapped wrapper-table cell with td align=center: the line centres
        // over the table's attribute-width band from the cell's chrome inset
        // (probed on the licensing letter: rows centre at 98.25 + (333-lw)/2
        // on its 450px table).
        if (bt.block.CenterBandW > 0 && line.Length > 0)
        {
            var cbw = MeasureFaceText(bt.metrics.metricMeasureFace, line, bt.metrics.blockFontSize);
            bt.lineXPos = bt.marginLeft + bt.block.LeftIndent
                + Math.Max(0, (bt.block.CenterBandW - cbw) / 2);
        }
        bt.rtlFace = bt.block.FontRes == "F2" ? "Arial Bold" : "Arial";
        // An RTL document's lines seat on the BODY box's right edge, which is the
        // UA body margin inside the page's right content edge - not the content
        // edge itself. Probed on one fixture at five page-margin settings: the
        // reference lands its lines at pageWidth - marginRight - 6.0 for margins
        // 0, 20, 40, the default 90, and the asymmetric 60/15, so the inset is
        // constant and reads the RIGHT margin only.
        if (bt.profile.rtlDoc && line.Length > 0)
        {
            var lw = MeasureFaceText(bt.rtlFace, line, bt.metrics.blockFontSize);
            var rtlEdge = bt.pageWidth - bt.marginRight - UaBodyMarginPt;
            bt.lineXPos = Math.Max(bt.marginLeft, rtlEdge - lw);
        }
        bt.uaFloatW = 0.0;
        if (bt.profile.uaStdSerif && bt.metrics.metricDrop > 0 && (bt.block.FloatLeft || bt.block.FloatRight)
            && line.Length > 0)
        {
            bt.uaFloatW = MeasureFaceText(bt.metrics.metricMeasureFace, line, bt.metrics.blockFontSize);
            if (bt.block.FloatRight)
                bt.lineXPos = Math.Max(bt.marginLeft, bt.pageWidth - bt.marginLeft - bt.uaFloatW);
        }
    }

    /// <summary>The print-grid document class writes the line as one run per font segment; false when the block must stop.</summary>
    private static bool WritePrintGridRuns(BlockTextState bt, string line, string regRes, string boldRes, string stdRes)
    {
        bt.sb.Clear();
        bt.sb.AppendLine("BT");
        if ((bt.block.BoldRuns is { Count: > 0 } || bt.block.ItalicRuns is { Count: > 0 })
            && bt.block.FontRes == "F1")
        {
            // Mixed-emphasis line: bold/italic RUNS inside a regular line,
            // emitted as consecutive Tf/Tj segments (the text position
            // advances naturally between them). Bold wins on overlap.
            var italRes = bt.profile.printGrid ? "F3" : "F7";
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
            bt.sb.Append($"1 0 0 1 {bt.lnX} {bt.lnY} Tm ");
            int lineStart = bt.metrics.cumChar, lineEnd = bt.metrics.cumChar + line.Length;
            int pos = lineStart;
            while (pos < lineEnd)
            {
                int segEnd = lineEnd;
                var boldSeg = InRuns(bt.block.BoldRuns, pos, ref segEnd);
                var italSeg = InRuns(bt.block.ItalicRuns, pos, ref segEnd);
                var segText = line.Substring(pos - lineStart, segEnd - pos);
                bt.sb.Append($"/{(boldSeg ? boldRes : italSeg ? italRes : regRes)} {bt.metrics.blockFontSize.ToString("F1", bt.invc)} Tf ");
                bt.sb.Append($"({EscapePdfString(segText)}) Tj ");
                pos = segEnd;
            }
        }
        else
        {
            bt.sb.Append($"/{stdRes} {bt.metrics.blockFontSize.ToString("F1", bt.invc)} Tf ");
            bt.sb.Append($"1 0 0 1 {bt.lnX} {bt.lnY} Tm ");
            bt.sb.Append($"({EscapePdfString(line)}) Tj ");
        }
        bt.sb.AppendLine("ET");
        bt.flow.page.AddContentStream(Encoding.ASCII.GetBytes(bt.sb.ToString()));
        return true;
    }

    /// <summary>The serif document classes write the line with per-run faces, sizes and colours; false when the block must stop.</summary>
    private static bool WriteSerifClassRuns(BlockTextState bt, string line, string uafFam, byte[] uafTtf, Core.PdfDictionary uafDict)
    {
        bt.sb.Clear();
        bt.sb.AppendLine("BT");
        if (bt.block.BoldRuns is { Count: > 0 } || bt.block.ItalicRuns is { Count: > 0 }
            || bt.block.ColorRuns is { Count: > 0 }
            // small-caps and symbol-PUA lines need the per-segment
            // emitter even without emphasis runs
            || (bt.profile.redlineDiffDoc && (bt.block.SmallCaps || HasSymbolPua(line))))
        {
            if (!WriteEmphasisRuns(bt, line, uafFam, uafTtf, uafDict)) return false;
        }
        else
        {
            var (uafRn, uafHex) = Text.Type0FontEmbedder.Embed(uafDict, uafTtf,
                uafFam.Replace(" ", "")
                + (bt.block.FontRes == "F2" || bt.block.EmBold ? "Bold" : "")
                // The face the certificate dialect resolved may be an
                // ITALIC one; its label has to say so or two different
                // programs share a name in the resource dictionary.
                + (bt.profile.floatBothSidesDoc && (bt.block.FontRes == "F3" || bt.block.EmItalic)
                    ? "Italic" : ""),
                line, stripSpacesInBaseFont: true);
            bt.sb.Append($"/{uafRn} {bt.metrics.blockFontSize.ToString("F1", bt.invc)} Tf ");
            if (bt.profile.redlineDiffDoc && bt.block.LetterSpacingPt != 0)
                bt.sb.Append(string.Create(bt.invc, $"{bt.block.LetterSpacingPt:0.##} Tc "));
            bt.sb.Append($"1 0 0 1 {bt.lnX} {bt.lnY} Tm ");
            bt.sb.Append('<').Append(System.Convert.ToHexString(uafHex)).Append("> Tj ");
            if (bt.profile.redlineDiffDoc && bt.block.LetterSpacingPt != 0)
                bt.sb.Append("0 Tc ");
        }
        bt.sb.AppendLine("ET");
        bt.flow.page.AddContentStream(Encoding.ASCII.GetBytes(bt.sb.ToString()));
        return true;
    }

    /// <summary>A max-width block's line is written scaled into its width.</summary>
    private static void WriteMaxWidthRuns(BlockTextState bt, string line)
    {
        // Report label/span dialect: drawn in the dialect's own face —
        // the real Segoe UI embedded when the system provides it (exact
        // shapes and advances); otherwise each word anchors at its
        // position in the baked Segoe metrics so the Standard-14 ink
        // never drifts more than one word's difference.
        bt.sb.Clear();
        bt.sb.AppendLine("BT");
        if (HeaderFooter.TryAppendReportLineOps(bt.sb, bt.docFontDict, line,
                bt.lineXPos, bt.lnY, bt.metrics.blockFontSize, bt.block.FontRes == "F2"))
        {
            // drawn kerned and word-anchored in the dialect's own face
        }
        else
        {
            bt.sb.Append($"/{bt.fontRes} {bt.metrics.blockFontSize.ToString("F1", bt.invc)} Tf ");
            var rwx = bt.lineXPos;
            foreach (var rword in line.Split(' '))
            {
                if (rword.Length > 0)
                {
                    bt.sb.Append($"1 0 0 1 {rwx.ToString("F2", bt.invc)} {bt.lnY} Tm ");
                    bt.sb.Append($"({EscapePdfString(rword)}) Tj ");
                }
                rwx += HeaderFooter.MeasureReportText(rword + " ", bt.metrics.blockFontSize,
                    bt.block.FontRes == "F2");
            }
        }
        bt.sb.AppendLine("ET");
        bt.flow.page.AddContentStream(Encoding.ASCII.GetBytes(bt.sb.ToString()));
    }

    /// <summary>A UA-flow line is written run by run with its inline faces.</summary>
    private static void WriteUaFlowRuns(BlockTextState bt, string line, byte[] uaTtf, Core.PdfDictionary uaFontDict)
    {
        // UA flow draws with the real serif face (embedded Type0) —
        // TimesNewRoman/-Bold output rather than Standard-14 Helvetica.
        // The escaped-attr dialect is serif UA output too (the real
        // TimesNewRoman faces are embedded — bold-italic included:
        // <b><i> notes render TimesNewRomanBoldItalic). The pt-report
        // flow embeds its own body face under that face's name.
        var (uaRn, uaHex) = Text.Type0FontEmbedder.Embed(uaFontDict, uaTtf,
            (bt.profile.ptReportDoc ? bt.profile.metricFace.Replace(" ", "") : "TimesNewRoman")
            + (bt.block.FontRes == "F2" || (bt.profile.escapedAttrDoc && bt.block.EmBold) ? "Bold" : "")
            + (bt.profile.escapedAttrDoc && (bt.block.EmItalic || bt.block.FontRes == "F3") ? "Italic" : ""),
            line, stripSpacesInBaseFont: true);
        bt.sb.Clear();
        bt.sb.AppendLine("BT");
        bt.sb.Append($"/{uaRn} {bt.metrics.blockFontSize.ToString("F1", bt.invc)} Tf ");
        bt.sb.Append($"1 0 0 1 {bt.lnX} {bt.lnY} Tm ");
        bt.sb.Append('<').Append(System.Convert.ToHexString(uaHex)).Append("> Tj ");
        bt.sb.AppendLine("ET");
        bt.flow.page.AddContentStream(Encoding.ASCII.GetBytes(bt.sb.ToString()));
    }

    /// <summary>A line needing a CJK or RTL face is written through its embedded TrueType.</summary>
    private static void WriteCjkRuns(BlockTextState bt, string line, Core.PdfDictionary cjkFontDict)
    {
        bt.sb.Clear();
        bt.sb.AppendLine("BT");
        // Thai mark stacking: a tone mark over an ABOVE vowel seats
        // higher than the run's baseline — the vowel keeps the
        // baseline slot, the tone stacks above it (measured:
        // +2.42 pt at 11 pt, drawn a small nudge right of
        // the pen). Such marks are zero-advance, so each becomes its
        // own raised run at the pen position while the remainder
        // continues where the prefix ended. Lines without the pair
        // keep the single-run emit byte-for-byte.
        var thaiChunks = SplitThaiStackedTones(bt.uniSource);
        if (thaiChunks is not null)
        {
            var penX = bt.lineXPos;
            foreach (var (chunkText, raised) in thaiChunks)
            {
                var (crn, chex) = Text.Type0FontEmbedder.Embed(
                    cjkFontDict, bt.cjkTtf!, bt.cjkName, chunkText, stripSpacesInBaseFont: true);
                var cx = raised ? penX + ThaiToneNudgeEm * bt.metrics.blockFontSize : penX;
                var cy = raised
                    ? (bt.profile.metricFlow && bt.metrics.metricDrop > 0 ? bt.flow.y - bt.metrics.metricDrop : bt.flow.y) + ThaiToneRaiseEm * bt.metrics.blockFontSize
                    : (bt.profile.metricFlow && bt.metrics.metricDrop > 0 ? bt.flow.y - bt.metrics.metricDrop : bt.flow.y);
                bt.sb.Append($"/{crn} {bt.metrics.blockFontSize.ToString("F1", bt.invc)} Tf ");
                bt.sb.Append($"1 0 0 1 {cx.ToString("F2", bt.invc)} {cy.ToString("F2", bt.invc)} Tm ");
                bt.sb.Append('<').Append(System.Convert.ToHexString(chex)).Append("> Tj ");
                if (!raised)
                    penX += MeasureFaceText(bt.cjkName, chunkText, bt.metrics.blockFontSize);
            }
        }
        else
        {
            var (rn, hex) = Text.Type0FontEmbedder.Embed(
                cjkFontDict, bt.cjkTtf!, bt.cjkName, bt.uniSource, stripSpacesInBaseFont: true);
            bt.sb.Append($"/{rn} {bt.metrics.blockFontSize.ToString("F1", bt.invc)} Tf ");
            bt.sb.Append($"1 0 0 1 {bt.lnX} {bt.lnY} Tm ");
            bt.sb.Append('<').Append(System.Convert.ToHexString(hex)).Append("> Tj ");
        }
        bt.sb.AppendLine("ET");
        bt.flow.page.AddContentStream(Encoding.ASCII.GetBytes(bt.sb.ToString()));
    }

    /// <summary>Dispatches the line to the run writer its font needs and its document class prescribes; false when the block must stop.</summary>
    private static bool WriteLineRuns(BlockTextState bt, string line)
    {
        if (bt.cjkTtf is not null
            && bt.flow.page.Dict.Get("Resources") as Core.PdfDictionary is { } cjkRes
            && cjkRes.Get("Font") as Core.PdfDictionary is { } cjkFontDict)
        {
            WriteCjkRuns(bt, line, cjkFontDict);
        }
        else if (NeedsUnicode(bt.uniSource)
            // redline symbol-PUA lines stay with the face writer below,
            // which draws those sub-runs in the symbol face itself
            && !(bt.profile.redlineDiffDoc && HasSymbolPua(line))
            && bt.flow.page.Dict.Get("Resources") as Core.PdfDictionary is { } segRes
            && segRes.Get("Font") as Core.PdfDictionary is { } segFontDict)
        {
            // Per-segment fallback: consecutive Tj runs advance the text position
            // naturally, so no per-segment measurement is needed.
            bt.sb.Clear();
            bt.sb.AppendLine("BT");
            bt.sb.Append($"1 0 0 1 {bt.lnX} {bt.lnY} Tm ");
            foreach (var (segText, segFont) in SegmentByFont(bt.uniSource))
            {
                var segTtf = segFont?.SourceFontData?.TtfData;
                if (segTtf is not null)
                {
                    var (rn, hex) = Text.Type0FontEmbedder.Embed(
                        segFontDict, segTtf, segFont!.FontName ?? "Unicode", segText, stripSpacesInBaseFont: true);
                    bt.sb.Append($"/{rn} {bt.metrics.blockFontSize.ToString("F1", bt.invc)} Tf ");
                    bt.sb.Append('<').Append(System.Convert.ToHexString(hex)).Append("> Tj ");
                }
                else
                {
                    bt.sb.Append($"/{bt.fontRes} {bt.metrics.blockFontSize.ToString("F1", bt.invc)} Tf ");
                    bt.sb.Append($"({EscapePdfString(segText)}) Tj ");
                }
            }
            bt.sb.AppendLine("ET");
            bt.flow.page.AddContentStream(Encoding.ASCII.GetBytes(bt.sb.ToString()));
        }
        else if (bt.profile.uaStdSerif || bt.profile.printGrid || bt.profile.redlineDiffDoc || bt.profile.floatBothSidesDoc)
        {
            if (!WriteSerifGridRuns(bt, line)) return false;
        }
        else if ((bt.uaFlow || bt.profile.escapedAttrDoc || bt.profile.ptReportDoc)
            && PosFace(
                (bt.profile.escapedAttrDoc ? "Times New Roman" : bt.profile.metricFace)
                + (bt.block.FontRes == "F2" || (bt.profile.escapedAttrDoc && bt.block.EmBold) ? " Bold" : "")
                + (bt.profile.escapedAttrDoc && (bt.block.EmItalic || bt.block.FontRes == "F3") ? " Italic" : "")
                ).ttf is { } uaTtf
            && bt.flow.page.Dict.Get("Resources") is Core.PdfDictionary uaRes
            && uaRes.Get("Font") is Core.PdfDictionary uaFontDict)
        {
            WriteUaFlowRuns(bt, line, uaTtf, uaFontDict);
        }
        else if (bt.block.MaxWidthPt > 0)
        {
            WriteMaxWidthRuns(bt, line);
        }
        else
        {
            // Justified block: stretch word gaps so every line but the
            // paragraph's last fills the content box. Word-spacing only —
            // wrap points and pagination stay identical to the unjustified
            // layout. Skipped when the crude wrap left implausible slack.
            var justTw = 0.0;
            if (bt.block.AlignJustify && bt.lineIdx < bt.metrics.lines.Length - 1)
            {
                var spaces = 0;
                foreach (var ch in line) if (ch == ' ') spaces++;
                if (spaces > 0)
                {
                    var natural = MeasureFaceText(
                        string.IsNullOrEmpty(bt.block.FontFamily) ? "Arial" : bt.block.FontFamily!,
                        line, bt.metrics.blockFontSize);
                    var slack = bt.flow.contentWidth - bt.block.LeftIndent - natural;
                    if (slack > 0 && slack < (bt.flow.contentWidth - bt.block.LeftIndent) * 0.35)
                        justTw = slack / spaces;
                }
            }
            bt.sb.Clear();
            bt.sb.AppendLine("BT");
            bt.sb.Append($"/{bt.fontRes} {bt.metrics.blockFontSize.ToString("F1", bt.invc)} Tf ");
            if (justTw > 0) bt.sb.Append($"{justTw.ToString("F3", bt.invc)} Tw ");
            bt.sb.Append($"1 0 0 1 {bt.lnX} {bt.lnY} Tm ");
            bt.sb.Append($"({EscapePdfString(line)}) Tj ");
            if (justTw > 0) bt.sb.Append("0 Tw ");
            bt.sb.AppendLine("ET");
            bt.flow.page.AddContentStream(Encoding.ASCII.GetBytes(bt.sb.ToString()));
        }
        return true;
    }

    /// <summary>A line carrying bold, italic or colour runs is written run by run in the matching faces; false when the block must stop.</summary>
    private static bool WriteEmphasisRuns(BlockTextState bt, string line, string uafFam, byte[] uafTtf, Core.PdfDictionary uafDict)
    {
        Color? fCurCol = null;
        if (bt.profile.redlineDiffDoc && bt.block.LetterSpacingPt != 0)
            bt.sb.Append(string.Create(bt.invc, $"{bt.block.LetterSpacingPt:0.##} Tc "));
        bt.sb.Append($"1 0 0 1 {bt.lnX} {bt.lnY} Tm ");
        int fLineStart = bt.metrics.cumChar, fLineEnd = bt.metrics.cumChar + line.Length;
        var fPos = fLineStart;
        while (fPos < fLineEnd)
        {
            if (!WriteEmphasisRun(bt, line, uafFam, uafTtf, uafDict, fLineStart, fLineEnd, ref fPos, ref fCurCol)) break;
        }
        if (bt.profile.redlineDiffDoc && bt.block.LetterSpacingPt != 0)
            bt.sb.Append("0 Tc ");
        // a line ending inside a colour run must not leak
        // its ink into the following content
        if (fCurCol is not null)
        {
            var fBase = bt.block.ForeColor ?? Color.FromArgb(0, 0, 0);
            bt.sb.Append(string.Create(bt.invc,
                $"{fBase.R / 255.0:0.###} {fBase.G / 255.0:0.###} {fBase.B / 255.0:0.###} rg "));
        }
        return true;
    }

    /// <summary>Writes the next run of the line - one face, one colour - and advances past it; false at the line's end.</summary>
    private static bool WriteEmphasisRun(BlockTextState bt, string line, string uafFam, byte[] uafTtf, Core.PdfDictionary uafDict, int fLineStart, int fLineEnd, ref int fPos, ref Color? fCurCol)
    {
        var fSegEnd = fLineEnd;
        var fBold = InFaceRuns(bt.block.BoldRuns, fPos, ref fSegEnd);
        var fItal = InFaceRuns(bt.block.ItalicRuns, fPos, ref fSegEnd);
        var fRunCol = ColorInRuns(bt, fPos, ref fSegEnd);
        if (fRunCol?.Equals(fCurCol) != true && (fRunCol is not null || fCurCol is not null))
        {
            var fEff = fRunCol ?? bt.block.ForeColor ?? Color.FromArgb(0, 0, 0);
            bt.sb.Append(string.Create(bt.invc,
                $"{fEff.R / 255.0:0.###} {fEff.G / 255.0:0.###} {fEff.B / 255.0:0.###} rg "));
            fCurCol = fRunCol;
        }
        var fSegText = line.Substring(fPos - fLineStart, fSegEnd - fPos);
        // A run can be BOTH bold and italic - the certificate
        // heading is <i><b>…</b></i> - and a real face has that
        // variant where the Standard-14 table has no slot for it.
        var fVariant = (fBold ? " Bold" : "") + (fItal ? " Italic" : "");
        // "<family> Bold" may not be an indexed NAME —
        // fall back to the styled repository lookup
        // (tahomabd.ttf answers to family+style, not to
        // the "Tahoma Bold" full name).
        var fSegTtf = uafTtf;
        if (fVariant.Length > 0)
        {
            fSegTtf = PosFace(uafFam + fVariant).ttf;
            if (fSegTtf is null)
                try
                {
                    fSegTtf = Text.FontRepository.FindFont(uafFam,
                            fBold ? Text.FontStyles.Bold : Text.FontStyles.Italic,
                            ignoreCase: true)
                        ?.SourceFontData?.TtfData;
                }
                catch { fSegTtf = null; }
            fSegTtf ??= uafTtf;
        }
        // Symbol PUA runs (U+F0xx — the Wingdings box
        // glyphs) draw with the symbol face at full size.
        WriteEmphasisSegment(bt, fSegText, fSegTtf, fVariant, uafFam, uafDict);
        fPos = fSegEnd;
        return true;
    }

    /// <summary>Writes one run's text in its face: symbol PUA glyphs, small caps, or the plain embedded face.</summary>
    private static void WriteEmphasisSegment(BlockTextState bt, string fSegText, byte[] fSegTtf, string fVariant, string uafFam, Core.PdfDictionary uafDict)
    {
        if (bt.profile.redlineDiffDoc && HasSymbolPua(fSegText))
        {
            var puaPos = 0;
            while (puaPos < fSegText.Length)
            {
                var isPua = IsSymbolPua(fSegText[puaPos]);
                var puaEnd = puaPos + 1;
                while (puaEnd < fSegText.Length
                       && IsSymbolPua(fSegText[puaEnd]) == isPua) puaEnd++;
                var puaText = fSegText[puaPos..puaEnd];
                var puaTtf = isPua ? PosFace("Wingdings").ttf : null;
                if (isPua && puaTtf is not null)
                {
                    var (pRn, pHex) = Text.Type0FontEmbedder.Embed(uafDict, puaTtf,
                        "Wingdings", puaText, stripSpacesInBaseFont: true);
                    bt.sb.Append($"/{pRn} {bt.metrics.blockFontSize.ToString("F1", bt.invc)} Tf ");
                    bt.sb.Append('<').Append(System.Convert.ToHexString(pHex)).Append("> Tj ");
                }
                else
                {
                    var nPos = 0;
                    while (nPos < puaText.Length)
                    {
                        var nLower = bt.block.SmallCaps && char.IsLower(puaText[nPos]);
                        var nEnd = nPos + 1;
                        while (nEnd < puaText.Length
                               && (bt.block.SmallCaps && char.IsLower(puaText[nEnd])) == nLower) nEnd++;
                        var nText = puaText[nPos..nEnd];
                        var (nRn, nHex) = Text.Type0FontEmbedder.Embed(uafDict, fSegTtf,
                            uafFam.Replace(" ", "") + fVariant.Replace(" ", ""),
                            nLower ? nText.ToUpperInvariant() : nText,
                            stripSpacesInBaseFont: true);
                        bt.sb.Append($"/{nRn} {(nLower ? bt.metrics.blockFontSize * RedlineSmallCapsEm : bt.metrics.blockFontSize).ToString("F2", bt.invc)} Tf ");
                        bt.sb.Append('<').Append(System.Convert.ToHexString(nHex)).Append("> Tj ");
                        nPos = nEnd;
                    }
                }
                puaPos = puaEnd;
            }
        }
        else if (bt.profile.redlineDiffDoc && bt.block.SmallCaps)
        {
            // small-caps: lowercase sub-runs draw UPPERCASE
            // at the small ratio on the shared baseline
            var scPos = 0;
            while (scPos < fSegText.Length)
            {
                var scLower = char.IsLower(fSegText[scPos]);
                var scEnd = scPos + 1;
                while (scEnd < fSegText.Length
                       && char.IsLower(fSegText[scEnd]) == scLower) scEnd++;
                var scText = fSegText[scPos..scEnd];
                var (scRn, scHex) = Text.Type0FontEmbedder.Embed(uafDict, fSegTtf,
                    uafFam.Replace(" ", "") + fVariant.Replace(" ", ""),
                    scLower ? scText.ToUpperInvariant() : scText,
                    stripSpacesInBaseFont: true);
                bt.sb.Append($"/{scRn} {(scLower ? bt.metrics.blockFontSize * RedlineSmallCapsEm : bt.metrics.blockFontSize).ToString("F2", bt.invc)} Tf ");
                bt.sb.Append('<').Append(System.Convert.ToHexString(scHex)).Append("> Tj ");
                scPos = scEnd;
            }
        }
        else
        {
            var (fRn, fHex) = Text.Type0FontEmbedder.Embed(uafDict, fSegTtf,
                uafFam.Replace(" ", "") + fVariant.Replace(" ", ""),
                fSegText, stripSpacesInBaseFont: true);
            bt.sb.Append($"/{fRn} {bt.metrics.blockFontSize.ToString("F1", bt.invc)} Tf ");
            bt.sb.Append('<').Append(System.Convert.ToHexString(fHex)).Append("> Tj ");
        }
    }
}
