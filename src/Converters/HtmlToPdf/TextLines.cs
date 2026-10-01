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
        // The Word export keeps a paragraph's lines together at the page edge (mso-pagination:
        // widow-orphan on every style): the first line needs room for a second, and the line before
        // the last needs room for the last - else the page breaks before the pair (probed: a two-line
        // 14 pt paragraph with 33 pt left opens the next page whole).
        var needRoom = bt.lineNeedBelow;
        if (bt.profile.wordExportDoc && bt.metrics.lines.Length >= 2
            && (bt.lineIdx == 0 || bt.lineIdx == bt.metrics.lines.Length - 2))
            needRoom += bt.metrics.lineHeight;
        if (!SeatLineOnPage(bt, needRoom)) return false;
        bt.fontRes = ResolveFontRes(bt.flow.page, bt.block, bt.flow, bt.profile, bt.doc, bt.embeddedFonts, bt.fontFileCache);
        // A size-run block's first line seats its own box: its baseline drops by however much
        // its largest run's ascent side exceeds a plain line's.
        if (bt.lineIdx == 0 && bt.profile.sheetTypographyDoc && bt.block.SizeRuns is { Count: > 0 }
            && bt.block.FontFamily is { Length: > 0 } sizeFace)
        {
            bt.lineExtents = LineExtents(bt.block, sizeFace, bt.metrics.lines, bt.metrics.blockFontSize, bt.metrics.lineHeight);
            bt.plainAbove = RunLineExtent(bt.block, sizeFace, bt.metrics.blockFontSize, bt.metrics.blockFontSize, bt.metrics.lineHeight).above;
            if (bt.lineExtents.Length > 0) bt.flow.y -= bt.lineExtents[0].above - bt.plainAbove;
        }

        if (bt.metrics.firstLineOfBlock && !string.IsNullOrEmpty(bt.block.Marker) && !bt.block.MarkerAfter)
            EmitMarkerHere(bt);

        bt.invc = System.Globalization.CultureInfo.InvariantCulture;
        bt.lineXPos = bt.marginLeft + bt.block.LeftIndent + bt.metrics.floatLabelIndent;
        // UA-serif flow: the span's own margin-left and the element's
        // border inset the text within the element box.
        if (bt.profile.uaStdSerif && (bt.block.TextInsetPt > 0 || bt.block.BorderWidth > 0))
            bt.lineXPos += bt.block.TextInsetPt + bt.block.BorderWidth;
        // Lines still level with a left-floated image start past its right edge.
        // (a size-run line is level with the float when its BOX top is, as its wrap decided)
        var lineTopAboveFloat = bt.lineExtents is { } fxt && bt.lineIdx < fxt.Length
            && bt.flow.y + fxt[bt.lineIdx].above > bt.flow.floatBottomY + 1e-9;
        if (bt.flow.floatIndentPt > 0 && (bt.flow.y > bt.flow.floatBottomY + 1e-9 || lineTopAboveFloat)
            && (!bt.profile.sheetBoxFlow || ReferenceEquals(bt.flow.page, bt.flow.floatPage)))
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
        // The UA serif flow's non-WinAnsi text (Cyrillic, Greek) draws in the UA serif face
        // itself where that face covers it, not in the sans fallback (measured on a saved
        // wiki page: every run TimesNewRoman / TimesNewRomanBold, none Arial).
        // (…and a block that names its own face - the body's Arial - draws it in that face, bold where
        //  the block is bold: the change-control page's `Kezdeményező szervezet:` label stays ArialBold)
        if (bt.profile.uaStdSerif && bt.cjkFont is not null
            && UaSerifFaceCovering(bt.uniSource, bt.block.FontRes == "F2", string.IsNullOrEmpty(bt.block.FontFamily) ? "Times New Roman" : bt.block.FontFamily) is { } uaSerif)
            bt.cjkFont = uaSerif;
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
        // A size-run block steps by the CSS line boxes: this line's descent side and the
        // next line's ascent side (a plain line's after the last).
        if (bt.lineExtents is { } stepExt && bt.lineIdx < stepExt.Length)
            bt.flow.y -= stepExt[bt.lineIdx].below
                + (bt.lineIdx + 1 < stepExt.Length ? stepExt[bt.lineIdx + 1].above : bt.plainAbove);
        else
            bt.flow.y -= bt.metrics.lineHeight;
        if (bt.metrics.ptLeadExtraPt > 0) { bt.flow.y -= bt.metrics.ptLeadExtraPt; bt.metrics.ptLeadExtraPt = 0; }
        if (line.Length > 0) bt.flow.contentPage = bt.flow.page;
        return true;
    }

    /// <summary>The face a line's text is measured in: the metric flow's measure face, else the
    /// block's family (Times by default), bold where the block is (<paramref name="plain"/>: the
    /// regular variant whatever the block, for measuring its emphasis runs one by one).</summary>
    private static string LineMeasureFace(BlockTextState bt, bool plain = false)
    {
        var bold = !plain && (bt.block.FontRes == "F2" || bt.block.EmBold);
        if (bt.metrics.metricMeasureFace.Length > 0)
        {
            var measure = bt.metrics.metricMeasureFace;
            if (plain && measure.EndsWith("-Bold", StringComparison.Ordinal)) return measure[..^"-Bold".Length];
            return bold && !measure.EndsWith("-Bold", StringComparison.Ordinal) ? measure + "-Bold" : measure;
        }
        return (bt.block.FontFamily ?? "Times New Roman") + (bold ? " Bold" : "");
    }

    /// <summary>Link rectangles for the line's anchors and the diff/form decoration runs drawn over it.</summary>
    private static void RegisterLineAnchors(BlockTextState bt, string line)
    {
        if (bt.block.Anchors is { Count: > 0 })
        {
            int lineStart = bt.metrics.cumChar, lineEnd = bt.metrics.cumChar + line.Length;
            // The rect spans the anchor's text as drawn: measured in the line's face from where
            // the line starts (the decoration runs measure the same way).
            var face = LineMeasureFace(bt);
            // The rect stands on the line's baseline (the metric flow seats it a drop under
            // the cursor, the legacy flow at the cursor), one line box tall.
            var baseline = bt.profile.metricFlow && bt.metrics.metricDrop > 0 ? bt.flow.y - bt.metrics.metricDrop : bt.flow.y;
            // ... from where the line starts to the anchor, measured as drawn: a block whose
            // emphasis runs put parts of the line in bold or italic measures each run in its own variant.
            double XAt(int p) => bt.lineXPos + (p <= 0 ? 0
                : bt.block.SmallCaps
                ? MeasureSmallCapsText(face, line[..Math.Min(p, line.Length)], bt.metrics.blockFontSize)
                : bt.block.BoldRuns is { Count: > 0 } || bt.block.ItalicRuns is { Count: > 0 }
                ? RunsMeasuredWidth(bt.block, lineStart, line[..Math.Min(p, line.Length)], LineMeasureFace(bt, plain: true), bt.metrics.blockFontSize)
                : MeasureFaceText(face, line[..Math.Min(p, line.Length)], bt.metrics.blockFontSize));
            foreach (var (aStart, aLen, url) in bt.block.Anchors)
            {
                int ov0 = Math.Max(aStart, lineStart), ov1 = Math.Min(aStart + aLen, lineEnd);
                if (ov1 > ov0 && !string.IsNullOrEmpty(url))
                {
                    double x0 = XAt(ov0 - lineStart);
                    double x1 = XAt(ov1 - lineStart);
                    // The link's description (annotation /Contents, surfaced as its
                    // tooltip) is the anchor's visible text.
                    string? aText = aStart >= 0 && aLen > 0 && aStart + aLen <= bt.block.Text.Length
                        ? bt.block.Text.Substring(aStart, aLen) : null;
                    bt.pendingLinks.Add((bt.flow.page, new Aspose.Pdf.Rectangle(x0, baseline, x1, baseline + bt.metrics.lineHeight), url, aText));
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
                dsb.Append(Compat.Format(bt.invc,
                    $"q {dcol.R / 255.0:0.###} {dcol.G / 255.0:0.###} {dcol.B / 255.0:0.###} RG {dw:0.##} w "));
                if (dKind == 4) dsb.Append("[1.5 0.75] 0 d ");
                dsb.Append(Compat.Format(bt.invc,
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
        // A strike run draws the same stroke through the run, 0.26 em over the baseline (the
        // probed strike seat), in the run's ink.
        if (bt.profile.uaStdSerif && bt.block.StrikeRuns is { Count: > 0 } uaSRuns && line.Length > 0)
            DrawUaDecorationRuns(bt, line, uaSRuns, RedlineStrikeRiseEm * bt.metrics.blockFontSize);
        if (bt.profile.uaStdSerif && bt.block.UnderlineRuns is { Count: > 0 } uaURuns && line.Length > 0)
            DrawUaDecorationRuns(bt, line, uaURuns, -bt.metrics.blockFontSize / 10.0);
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

    /// <summary>The UA flow's decoration runs (underline, strike) over this line: a stroke fs/10 thick at
    /// <paramref name="riseFromBaseline"/> over the baseline, spanning each run's measured extent.</summary>
    private static void DrawUaDecorationRuns(BlockTextState bt, string line, List<(int Start, int Length)> uaURuns, double riseFromBaseline)
    {
        {
            int uLineStart = bt.metrics.cumChar, uLineEnd = bt.metrics.cumChar + line.Length;
            // The run's extent is measured on the face the line is DRAWN in: a block with its
            // own family (a `h2 { font-family: 'Courier new' }` heading) underlines its whole
            // text, not the shorter width the flow face would give it.
            var uFace = !string.IsNullOrEmpty(bt.block.FontFamily) && WinMetricsFor(bt.block.FontFamily) is not null
                ? bt.block.FontFamily + (bt.block.FontRes == "F2" ? " Bold" : "")
                : bt.metrics.metricMeasureFace;
            foreach (var (us0, ul0) in uaURuns)
            {
                var us1 = Math.Max(us0, uLineStart);
                var ue1 = Math.Min(us0 + ul0, uLineEnd);
                if (ue1 <= us1) continue;
                var uPre = MeasureFaceText(uFace,
                    line[..(us1 - uLineStart)], bt.metrics.blockFontSize);
                var uSeg = MeasureFaceText(uFace,
                    line[(us1 - uLineStart)..(ue1 - uLineStart)], bt.metrics.blockFontSize);
                var uy = (bt.profile.metricFlow && bt.metrics.metricDrop > 0 ? bt.flow.y - bt.metrics.metricDrop : bt.flow.y)
                    + riseFromBaseline;
                // The stroke takes the block's own ink (a linked line's
                // underline draws in the link colour).
                // ...in the run's own ink when the line colours per run (a link's underline is blue).
                var uRunCol = bt.block.ForeColor;
                if (bt.block.ColorRuns is { } uCols)
                    foreach (var (ucs, ucl, ucc) in uCols)
                        if (us1 >= ucs && us1 < ucs + ucl) { uRunCol = ucc; break; }
                var uInk = uRunCol is { } uCol
                    ? Compat.Format(System.Globalization.CultureInfo.InvariantCulture,
                        $"{uCol.R / 255.0:0.###} {uCol.G / 255.0:0.###} {uCol.B / 255.0:0.###}")
                    : "0 0 0";
                bt.flow.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"q {uInk} RG {bt.metrics.blockFontSize / 10.0:0.##} w {bt.lineXPos + uPre:F2} {uy:F2} m {bt.lineXPos + uPre + uSeg:F2} {uy:F2} l S Q\n")));
            }
        }
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
        var sansStd = bt.profile.printGrid || bt.profile.floatBothSidesDoc || bt.profile.sheetTypographyDoc;
        var regRes = sansStd ? "F1" : "F5";
        var boldRes = sansStd ? "F2" : "F6";
        var stdRes = bt.block.FontRes == "F2" ? boldRes : bt.block.FontRes == "F3" ? (sansStd ? "F3" : "F7") : regRes;
        // A <font face> block carries a RESOLVED family: its runs draw
        // in that face (embedded Type0), bold variant for bold blocks —
        // the std-serif override serves only family-free text.
        // The certificate dialect draws its family-free text in the UA
        // serif too, and unlike the Standard-14 resource table (which has no
        // bold-italic slot at all) a real face carries both emphases.
        // The UA serif flow's family-free text draws the real Times New Roman too (probed: the
        // engine embeds times.ttf and kerns its pairs; the Standard-14 Times-Roman cannot).
        if ((bt.profile.uaStdSerif || bt.profile.redlineDiffDoc || bt.profile.dwFormDoc || bt.profile.floatBothSidesDoc
                || bt.profile.sheetTypographyDoc || bt.profile.wordMailDoc)
            && (bt.block.FontFamily ?? (bt.profile.uaStdSerif && !bt.profile.printGrid && bt.profile.embedFonts ? "Times New Roman" : null)) is { } uafFam
            && PosFace(uafFam
                    + (bt.block.FontRes == "F2" || bt.block.EmBold ? " Bold" : "")
                    + ((bt.profile.floatBothSidesDoc || bt.profile.sheetTypographyDoc) && (bt.block.FontRes == "F3" || bt.block.EmItalic)
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
        // A UA-grid heading box with rules but no fill (`h4 { border-top: 2px solid #000; padding-top: 10px;
        // height: 30px; border-bottom: 1px solid #333; width: 650px }`): its top rule stands the padding
        // above the line box, its bottom rule the declared height below the line top, both the declared
        // width wide (measured on the quotation: 242.38 / 273.51, 96..583.5).
        if (bt.block.BackgroundColor is null && bt.profile.uaGridSheet && bt.block.SheetBox
            && bt.metrics.firstLineOfBlock && bt.metrics.lineHeight > 0
            && (bt.block.UaRuleTopPt > 0 || bt.block.BorderBottomWidth > 0))
        {
            var rlX = bt.marginLeft + bt.block.LeftIndent;
            var rlW = bt.block.WidthPx > 0 ? bt.block.WidthPx * 0.75 : bt.flow.contentWidth - bt.block.LeftIndent;
            var rlFace = bt.block.FontFamily is { Length: > 0 } rlFaceName ? rlFaceName : "Times New Roman";
            var rlTop = bt.flow.y + LineBoxAbove(rlFace, bt.metrics.blockFontSize, bt.metrics.lineHeight) + bt.block.UaPadTopPt;
            var rlBot = rlTop - bt.block.UaPadTopPt - Math.Max(bt.metrics.lineHeight, bt.block.ExplicitHeight);
            var rlSb = new StringBuilder("q ");
            if (bt.block.UaRuleTopPt > 0 && bt.block.UaRuleTopColor is { } rtc)
                rlSb.Append($"{(rtc.R / 255.0).ToString("F3", bt.invc)} {(rtc.G / 255.0).ToString("F3", bt.invc)} {(rtc.B / 255.0).ToString("F3", bt.invc)} RG {bt.block.UaRuleTopPt.ToString("F2", bt.invc)} w {rlX.ToString("F2", bt.invc)} {(rlTop + bt.block.UaRuleTopPt / 2).ToString("F2", bt.invc)} m {(rlX + rlW).ToString("F2", bt.invc)} {(rlTop + bt.block.UaRuleTopPt / 2).ToString("F2", bt.invc)} l S ");
            if (bt.block.BorderBottomWidth > 0 && bt.block.BorderBottomColor is { } rbc)
                rlSb.Append($"{(rbc.R / 255.0).ToString("F3", bt.invc)} {(rbc.G / 255.0).ToString("F3", bt.invc)} {(rbc.B / 255.0).ToString("F3", bt.invc)} RG {bt.block.BorderBottomWidth.ToString("F2", bt.invc)} w {rlX.ToString("F2", bt.invc)} {(rlBot - bt.block.BorderBottomWidth / 2).ToString("F2", bt.invc)} m {(rlX + rlW).ToString("F2", bt.invc)} {(rlBot - bt.block.BorderBottomWidth / 2).ToString("F2", bt.invc)} l S ");
            rlSb.Append('Q');
            bt.flow.page.AddContentStream(Encoding.ASCII.GetBytes(rlSb.ToString()));
        }
        PaintLineBackgroundBox(bt);
        // The declared box's background IMAGE, over the fill (when both are declared) and on
        // the block's first line only, like the fill.
        if (bt.block.BgImageSrc is not null && bt.metrics.firstLineOfBlock
            && (bt.block.BgBoxHeightPt > 0 || bt.block.BgBoxHeightVh > 0))
        {
            var (ibX, ibTop, ibW, ibH, _) = PaintedBoxRect(bt);
            PaintBackgroundImageBox(bt, ibX, ibTop, ibW, ibH);
        }
        bt.lineForeColor = bt.block.ForeColor is { } fc0 && (fc0.R != 0 || fc0.G != 0 || fc0.B != 0)
            ? bt.block.ForeColor : null;
        if (bt.lineForeColor is { } fc)
            bt.flow.page.AddContentStream(Encoding.ASCII.GetBytes(
                $"{(fc.R / 255.0).ToString("F5", bt.invc)} {(fc.G / 255.0).ToString("F5", bt.invc)} {(fc.B / 255.0).ToString("F5", bt.invc)} rg"));
        bt.isRtlLine = IsPureRtl(line);
        bt.uniSource = bt.isRtlLine ? ToVisualRtl(line)
            : Text.BidiReorderer.ContainsRtl(line)
                ? (bt.profile.rtlDoc ? VisualizeRtlParagraph(line) : VisualizeMixedRtl(line))
                : line;
    }

    /// <summary>The family an RTL document's lines are measured and drawn in: the UA serif in the
    /// UA serif flow, the calibrated sans elsewhere.</summary>
    private static string RtlLineFamily(BlockTextState bt)
        => bt.profile.uaStdSerif ? "Times New Roman" : "Arial";

    /// <summary>The advance of an RTL document's line as it draws: its emphasis segments each in their
    /// own face, without the trailing whitespace the paragraph direction moves off the seat edge.</summary>
    private static double MeasureRtlLine(BlockTextState bt, string line)
    {
        var fs = bt.metrics.blockFontSize;
        if (RtlEmphasisSegments(bt, line) is not { } segs)
            return MeasureFaceText(bt.rtlFace, line.TrimEnd(), fs);
        var fam = RtlLineFamily(bt);
        double w = 0;
        foreach (var (text, bold) in segs)
            w += MeasureFaceText(fam + (bold ? " Bold" : ""), text, fs);
        return w;
    }

    /// <summary>A regular RTL-document line's visual segments by bold run, or null when the line has no
    /// emphasis runs to split on (it draws as one run).</summary>
    private static List<(string text, bool bold)>? RtlEmphasisSegments(BlockTextState bt, string line)
    {
        if (!bt.profile.rtlDoc || bt.block.FontRes != "F1" || bt.block.BoldRuns is not { Count: > 0 } runs
            || !Text.BidiReorderer.ContainsRtl(line))
            return null;
        var lineStart = bt.metrics.cumChar;
        bool BoldAt(int p)
        {
            foreach (var (rs, rl) in runs)
                if (lineStart + p >= rs && lineStart + p < rs + rl) return true;
            return false;
        }
        var segs = VisualizeRtlParagraphSegments(line.TrimEnd(), BoldAt);
        var any = false;
        foreach (var (_, bold) in segs) if (bold) { any = true; break; }
        return any ? segs : null;
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
        // centred at (126+499)/2 inside <ul><li><div text-align:center>) - and the
        // legacy align=center attribute the same way (probed: the h1 and h2 inside
        // `<div align=center>` centre at 197.16 / 214.74).
        else if (bt.profile.uaStdSerif && bt.metrics.metricDrop > 0
                 && (bt.block.AlignCenterCss || (bt.profile.uaBareDoc && bt.block.AlignCenterAttr)) && line.Length > 0)
        {
            var mw = MeasureFaceText(bt.metrics.metricMeasureFace, line, bt.metrics.blockFontSize);
            var boxLeft = bt.marginLeft + bt.block.LeftIndent;
            var boxRight = bt.pageWidth - bt.marginLeft;
            bt.lineXPos = Math.Max(bt.marginLeft, boxLeft + (boxRight - boxLeft - mw) / 2);
        }
        // UA-default serif flow honours an inline text-align:right the
        // same way: the line pins to the body box's right edge (the
        // rating-date div ends at 96 + content = 517, measured).
        // A right-floated inline run's lines pin to the content box's right edge.
        else if (bt.profile.uaStdSerif && bt.block.FloatRight && !bt.block.FloatLeft && line.Length > 0)
        {
            var fw = MeasureFaceText(bt.block.FontRes == "F2" ? "Times New Roman Bold" : "Times New Roman", line, bt.metrics.blockFontSize);
            bt.lineXPos = Math.Max(bt.marginLeft, bt.marginLeft + bt.flow.contentWidth - UaBodyMarginPt - fw);   // the flow's text box ends one body inset short
        }
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
            // (the sheet-typography flow centres a BOLD heading by its bold advance - the face
            // it draws in; measured: the 22 px Verdana title centres at 303 on the 606 sheet)
            var centreFace = bt.metrics.bandFace ?? (string.IsNullOrEmpty(bt.block.FontFamily) ? "Arial" : bt.block.FontFamily!);
            if (_quirksChainSheet && bt.metrics.bandFace is null
                && (bt.block.FontRes == "F2" || bt.block.EmBold) && !centreFace.EndsWith(" Bold", StringComparison.OrdinalIgnoreCase))
                centreFace += " Bold";
            var mw = MeasureFaceText(centreFace, line, bt.metrics.blockFontSize);
            bt.lineXPos = Math.Max(bt.marginLeft + bt.block.LeftIndent,
                bt.marginLeft + bt.block.LeftIndent + (bt.flow.contentWidth - bt.block.LeftIndent - mw) / 2);
        }
        if (bt.profile.redlineDiffDoc && bt.block.TextIndentPt > 0 && bt.lineIdx == 0)
            bt.lineXPos += bt.block.TextIndentPt;
        // Word mail: the hanging label's first line starts the indent to the left, never past the margin
        if (bt.profile.wordMailDoc && bt.block.TextIndentPt != 0 && bt.lineIdx == 0)
            bt.lineXPos = Math.Max(bt.marginLeft, bt.lineXPos + bt.block.TextIndentPt);
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
        // The UA serif flow draws an RTL document's lines in the UA serif itself (the
        // reference embeds Times New Roman for the Arabic runs of a dir="rtl" letter);
        // the calibrated flows keep the sans they were measured on.
        bt.rtlFace = RtlLineFamily(bt) + (bt.block.FontRes == "F2" ? " Bold" : "");
        // An RTL document's lines seat on the BODY box's right edge, which is the
        // UA body margin inside the page's right content edge - not the content
        // edge itself. Probed on one fixture at five page-margin settings: the
        // reference lands its lines at pageWidth - marginRight - 6.0 for margins
        // 0, 20, 40, the default 90, and the asymmetric 60/15, so the inset is
        // constant and reads the RIGHT margin only. A block's left inset (an
        // unwrapped cell's chrome) mirrors onto that edge, and the line's trailing
        // whitespace, which the paragraph direction moves to the visual left, is
        // not part of the seat.
        if (bt.profile.rtlDoc && line.Length > 0)
        {
            var lw = MeasureRtlLine(bt, line);
            var rtlEdge = bt.pageWidth - bt.marginRight - UaBodyMarginPt - bt.block.LeftIndent;
            // a centred cell of a right-anchored shrink-to-fit wrapper table centres its
            // line over the table's box
            bt.lineXPos = bt.block.CenterBandW > 0
                ? Math.Max(bt.marginLeft, rtlEdge - bt.block.CenterBandW + (bt.block.CenterBandW - lw) / 2)
                : Math.Max(bt.marginLeft, rtlEdge - lw);
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
            (bool inside, int upTo) InRuns(System.Collections.Generic.List<(int Start, int Length)>? runs,
                int p, int upTo)
            {
                var inside = false;
                if (runs is not null)
                    foreach (var (rs, rl) in runs)
                    {
                        var re = rs + rl;
                        if (p >= rs && p < re) { inside = true; upTo = Math.Min(upTo, re); }
                        else if (rs > p) upTo = Math.Min(upTo, rs);
                    }
                return (inside, upTo);
            }
            bt.sb.Append($"1 0 0 1 {bt.lnX} {bt.lnY} Tm ");
            int lineStart = bt.metrics.cumChar, lineEnd = bt.metrics.cumChar + line.Length;
            int pos = lineStart;
            while (pos < lineEnd)
            {
                int segEnd = lineEnd;
                (var boldSeg, segEnd) = InRuns(bt.block.BoldRuns, pos, segEnd);
                (var italSeg, segEnd) = InRuns(bt.block.ItalicRuns, pos, segEnd);
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

}
