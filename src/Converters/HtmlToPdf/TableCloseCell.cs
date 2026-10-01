using System.Text;
using System.Text.RegularExpressions;
using CellLineSpec = (string Text, double FontPt, string? Family, bool Keep, bool JoinNext, System.Collections.Generic.List<(string Text, string Url)>? Anchors, bool Bold, double MarginTopPt, double MarginLeftPt, Aspose.Pdf.Color? Color, bool Italic);

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    private static void CloseCell(TableParseState ps, TableColumnModel colModel, Table table, HtmlLoadOptions? options, double cellFontSize, bool dwFormCells, bool fullWidthCjkMin, bool breakAnywhereDoc, bool cellFontShorthand, List<CssElem>? chainBase, double chainSpacingPt, List<(string Tag, int PrevBoldDepth)> chainUnbold, string? cssBaseFamily, double cssBasePt, string? defaultCellFace, double formGridStrutDropPt, bool hasBorder, double inlineFaceRatio, bool overDeclaredDraw, double padSide, bool uaDocGrid, bool widenProbe, bool uaSerifMin, bool ptCellWidths, bool redlineCells, bool bandDialect, double cellLineHeightPt, string? cssRunFace, bool formGridDialect, double formGridStrutPt, bool liftNestedTables, bool tightExtras, bool uaCellBoxes, double borderWidth, double pad)
    {
        if (ps.cell is null || ps.row is null) return;
        var cc = new CloseCellState();
        cc.ps = ps;
        cc.colModel = colModel;
        cc.table = table;
        cc.options = options;
        cc.cellFontSize = cellFontSize;
        cc.dwFormCells = dwFormCells;
        cc.fullWidthCjkMin = fullWidthCjkMin;
        cc.breakAnywhereDoc = breakAnywhereDoc;
        cc.cellFontShorthand = cellFontShorthand;
        cc.chainBase = chainBase;
        cc.chainSpacingPt = chainSpacingPt;
        cc.chainUnbold = chainUnbold;
        cc.cssBaseFamily = cssBaseFamily;
        cc.cssBasePt = cssBasePt;
        cc.defaultCellFace = defaultCellFace;
        cc.formGridStrutDropPt = formGridStrutDropPt;
        cc.hasBorder = hasBorder;
        cc.inlineFaceRatio = inlineFaceRatio;
        cc.overDeclaredDraw = overDeclaredDraw;
        cc.padSide = padSide;
        cc.uaDocGrid = uaDocGrid;
        cc.widenProbe = widenProbe;
        cc.uaSerifMin = uaSerifMin;
        cc.ptCellWidths = ptCellWidths;
        cc.redlineCells = redlineCells;
        cc.bandDialect = bandDialect;
        cc.cellLineHeightPt = cellLineHeightPt;
        cc.cssRunFace = cssRunFace;
        cc.formGridDialect = formGridDialect;
        cc.formGridStrutPt = formGridStrutPt;
        cc.liftNestedTables = liftNestedTables;
        cc.tightExtras = tightExtras;
        cc.uaCellBoxes = uaCellBoxes;
        cc.borderWidth = borderWidth;
        cc.pad = pad;
        // A declared width is the cell's CONTENT box: its own horizontal padding
        // rides on top of it in the column footprint, the way it does in every other
        // measure here. An image column declaring `width="150"` plus a 20 px
        // padding-right is a 170 px column — the gutter between it and the text
        // column beside it was being dropped.
        // The block margin the cell's last block leaves pending is space at the cell's foot: a
        // line of no height carrying it (a closing <p>'s 1.12em under the band it holds, measured
        // on the job status row).
        EmitCellBoxChrome(cc);
        PlaceDeferredCellContentAndPadding(cc);
        cc.ps.row.Cells.Add(cc.ps.cell);
        MeasureCellLineExtents(cc);
        // A fixed-width div inside the cell IS the cell's min-content: its long
        // token wraps inside the div box (break-word), so the div box — not the
        // token — sizes the column. The cell's own CSS padding rides on every
        // measure (the column footprint includes it).
        ApplyCellContentFloors(cc);
        cc.span = Math.Max(1, cc.ps.colSpan);
        // Probe mapping: skip columns still occupied by ROWSPAN cells from rows
        // above (the HTML grid placement rule), so a row following a row-spanning
        // name cell doesn't record its money cells one column early and double
        // the summed min-content with ghost twins. The chain dialect measures
        // through the same mapping (the budget's rowspan-2 label cell shifted
        // its whole second header row one column left).
        CommitCellColumnWidths(cc);
        cc.colModel.colCursor += cc.span;
        cc.ps.preWrapPending = false;
        cc.ps.cell = null; cc.ps.lines.Clear(); cc.ps.lineHeightPctByIdx = null; cc.ps.pendingCellImgAt = null; cc.ps.loneBrBlankLines?.Clear(); cc.ps.line.Clear(); cc.ps.isHeader = false; cc.ps.colSpan = 1; cc.ps.cellRowSpan = 1; cc.ps.alignSet = false; cc.ps.cellWidthPt = 0; cc.ps.cellWidthPct = 0; cc.ps.cellCssPadPt = 0; cc.ps.cellShortPadPt = 0; cc.ps.pendingCellTablesMinW = 0; cc.ps.pendingCellTablesMaxW = 0; cc.ps.cellFixedDivPt = 0; cc.ps.cellPadLeftPt = 0; cc.ps.cellImgWidthPt = 0; cc.ps.cellControlBoxPt = 0; cc.ps.cellOwnLineHPt = 0; cc.ps.cellPrevPBottomPt = 0; cc.ps.cellPMarginRightPt = 0; cc.ps.cellFirstPMarginTopPt = 0; cc.ps.cellDecorActive = null; cc.ps.lineDecorUnion = null; cc.ps.lineDecorsByIdx = null; cc.ps.lineAlignByIdx = null; cc.ps.hrRuleLines = null; cc.ps.uaHrRulePending = false; cc.ps.lineColorRunsByIdx = null; cc.ps.lineColorRuns = null; cc.ps.cellInputBoxes = null; cc.ps.dwSelectDepth = 0; cc.ps.dwTextareaOpen = false; cc.ps.cellOwnHeightDecl = false; cc.ps.cellUpper = false; cc.ps.uaUpperDepths.Clear(); cc.ps.uaBlockBottoms.Clear(); cc.ps.lineMarker = null; cc.ps.lineMarkerByIdx = null; cc.ps.lineRunsByIdx = null; cc.ps.uaRuleBands = null; cc.ps.cellRuleBelowPt = 0; cc.ps.cellRuleColor = null; cc.ps.cellRuleAbovePt = 0; cc.ps.cellRuleAboveColor = null;
        cc.ps.underlinedLines = null; cc.ps.lineHadU = cc.ps.uDepth > 0;
        cc.ps.chainTdElem = null; cc.ps.chainOpenElems?.Clear(); cc.chainUnbold.Clear();
        cc.ps.cellChainPadTopPt = 0; cc.ps.cellChainPadBotPt = 0; cc.ps.cellChainColor = null;
        cc.ps.cellVPadZeroBot = false;
        cc.ps.chainBoxOpen?.Clear(); cc.ps.cellBoxSegs?.Clear();
        cc.ps.chainTrafficElem = null; cc.ps.chainTrafficRun = null; cc.ps.pendingCapsule = null;
        cc.ps.openAnchor = null; cc.ps.lineAnchors = null;
    }

    /// <summary>Lays one line spec of the cell out as a paragraph: face, size and spacing resolution, box floors, anchors and the joins with its neighbours.</summary>
    private static void LayOutCellLine(CloseCellState cc, CellLineSpec spec)
    {
        cc.tfLineIdx++;
        cc.ln = spec.Text;
        // A nested grid anchored at (or before) this line joins the cell's
        // paragraphs HERE, so text that followed it in the markup stays
        // below it (an attachments grid draws between its caption and the
        // template list, not after both).
        while (cc.ps.pendingCellTables is { Count: > 0 } && cc.ps.pendingCellTables[0].AnchorLine <= cc.tfLineIdx)
        {
            cc.ps.cell!.Paragraphs.Add(cc.ps.pendingCellTables[0].T);
            cc.ps.pendingCellTables.RemoveAt(0);
            cc.cellHadNestedTable = true;
        }
        // A deliberately-kept blank line is a real line box (the newline a
        // pre-wrap box holds onto), so it survives in a UA-cell-box grid even
        // when the cell carries no per-line styling. The lifted dialect keeps
        // them too: an explicit <br> on an empty line (`<BR><BR>` between
        // form questions) is the vertical rhythm of the source document.
        // A blank line CARRYING inline boxes (a standalone badge circle in
        // an otherwise-empty cell) survives too — the decos ride the fragment.
        if (cc.ln.Length == 0
            && !((cc.anyStyled || cc.uaCellBoxes || cc.ps.uaControlGrid
                    || (cc.liftNestedTables
                        && (cc.uaCellBoxes || !(cc.ps.loneBrBlankLines?.Contains(cc.tfLineIdx) ?? false)))) && spec.Keep)
            && !(cc.boxByLine is not null && cc.boxByLine.ContainsKey(cc.tfLineIdx))) return;
        cc.tf = new Text.TextFragment(cc.ln);
        cc.tf.HtmlAnchors = spec.Anchors;
        cc.tf.HtmlUnderline = cc.ps.underlinedLines?.Contains(cc.tfLineIdx) == true;
        // A UA block's own alignment (a centred heading) is the line's, not the cell's.
        if (cc.ps.lineAlignByIdx is not null && cc.ps.lineAlignByIdx.TryGetValue(cc.tfLineIdx, out var uaLineAlign))
            cc.tf.TextState.HorizontalAlignment = uaLineAlign;
        // A class on the cell (`.header { font-size:16pt }`) sizes its text even
        // when no line carries a style of its own — the class rule is more
        // specific than the sheet's `table td` base.
        cc.tf.TextState.FontSize = (float)(cc.ps.cellClassPt > 0 ? cc.ps.cellClassPt : cc.cellFontSize);
        ResolveCellLineFaceAndPitch(cc, spec);
        StyleCellLineColorAndMargins(cc, spec);
        cc.famForFont = spec.Family ?? cc.ps.cellFamily;
        if (cc.famForFont is not null && cc.ln.Length > 0)
        {
            try
            {
                var f = Text.FontRepository.TryFindFont(cc.famForFont);
                // CSS families are case-insensitive: the inline-face grid
                // resolves "verdana" to the installed Verdana it pitches by.
                if (f is null && cc.inlineFaceRatio > 0)
                    f = Text.FontRepository.TryFindFont(cc.famForFont, ignoreCase: true);
                if (f is not null) cc.tf.TextState.Font = f;
            }
            catch { }
        }
        // Non-WinAnsi text (CJK, Cyrillic, Greek, …) can't render in the Standard-14
        // WinAnsi fonts — it would collapse to '?'. Fall back to an embedded Unicode
        // face that covers the run so it flows through the Type0/CID render path.
        // RTL text is deliberately left alone (no font override, text unmodified):
        // the generator's cell pipeline shapes and embeds Arabic/Hebrew natively.
        if (cc.ln.Length > 0 && cc.tf.TextState.Font?.SourceFontData is null && NeedsUnicode(cc.ln)
            && !Text.BidiReorderer.ContainsRtl(cc.ln))
        {
            var uf = ResolveUnicodeFont(cc.ln);
            if (uf is not null) cc.tf.TextState.Font = uf;
        }
        TraceCellLine(cc, spec);
        // Inline boxes recorded for this line (plates/pills) ride the
        // fragment; the box line height reserves the pill's full height,
        // and a continuation line is centred via the fragment margin.
        PlaceCellLineBoxesAndControls(cc);
        // The browser's <hr>: a 1.5 pt inset rule across the cell's content box - a black
        // stroke over a grey one (measured: 0.75 at 219.20 black, 0.75 at 219.95 #555555).
        if (cc.ps.hrRuleLines is not null && cc.ps.hrRuleLines.Contains(cc.tfLineIdx))
        {
            cc.tf.InlineBoxes = new List<InlineBoxDecoration>
            {
                new() { SpanCell = true, Height = UaHrStrokePt, Fill = Color.Black },
                new() { SpanCell = true, Height = UaHrStrokePt, InsetV = UaHrStrokePt, Fill = UaHrShadeColor },
            };
            cc.tf.CssLineHeightPt = UaHrRulePt;
        }
        if (cc.ps.uaRuleBands is not null && cc.ps.uaRuleBands.TryGetValue(cc.tfLineIdx, out var ruleBand))
        {
            cc.tf.InlineBoxes = new List<InlineBoxDecoration>
            {
                new() { SpanCell = true, Height = ruleBand.W, Fill = ruleBand.C },
            };
            cc.tf.CssLineHeightPt = ruleBand.W;
            // (the rule under a cell lies OUTSIDE its bottom padding: the pad rides above the band as the band
            //  line's top margin, and the cell keeps none below it - the mailing's 5 px padded underline cells)
            if (cc.uaCellBoxes && cc.ps.cellChainPadBotPt > 0)
            {
                cc.tf.Margin = new MarginInfo(0, 0, 0, cc.ps.cellChainPadBotPt);
                cc.ps.cellChainPadBotPt = 0;
            }
        }
        if (cc.ps.lineMarkerByIdx is not null && cc.ps.lineMarkerByIdx.TryGetValue(cc.tfLineIdx, out var tfMarker))
            cc.tf.HtmlListMarker = tfMarker;
        cc.ps.cell!.Paragraphs.Add(cc.tf);
    }

    /// <summary>A cell whose text arrived as measured box segments lays them out as one paragraph per segment, each on its own baseline.</summary>
    private static void LayOutCellBoxSegments(CloseCellState cc)
    {
        // A cell whose only content is a box (a standalone badge circle in
        // an otherwise-empty td) never pushed a line — materialise the
        // blank line(s) its segments point at.
        var needLines = 0;
        foreach (var (bLi0, _, _, _) in cc.ps.cellBoxSegs!)
            if (bLi0 + 1 > needLines) needLines = bLi0 + 1;
        while (cc.ps.lines.Count < needLines) PushLine(cc.ps, cc.redlineCells, cc.dwFormCells, cc.widenProbe);
        var runSegs = new Dictionary<ChainBoxRun, int>();
        foreach (var (_, bRun0, _, _) in cc.ps.cellBoxSegs)
            runSegs[bRun0] = runSegs.TryGetValue(bRun0, out var n0) ? n0 + 1 : 1;
        Dictionary<ChainBoxRun, (double XOff, double W, int Seen, double FirstLineH)>? placed = null;
        var penByLine = new Dictionary<int, double>();
        foreach (var (bLi, bRun, _, bText) in cc.ps.cellBoxSegs)
        {
            var bSpec = bLi < cc.ps.lines.Count ? cc.ps.lines[bLi] : default;
            var bPt = bSpec.FontPt > 0 ? bSpec.FontPt
                : cc.ps.cellClassPt > 0 ? cc.ps.cellClassPt : cc.cellFontSize;
            var bBold = bSpec.Bold || cc.ps.cellBold || cc.ps.isHeader;
            // Measure the box text with the REAL face the box renders in
            // (Arial advances run ~3% wider than the Standard-14 estimate on
            // these bold runs). MAX with the AFM estimate: an uninstalled
            // face degrades to a 0.5-em guess that must not narrow the box.
            var bTw = Math.Max(
                    MeasureFaceText(bBold ? "Arial Bold" : "Arial", bText, bPt),
                    MeasureLine(cc.ps, cc.options, cc.cellFontSize, cc.dwFormCells, cc.fullWidthCjkMin, cc.widenProbe, bText, bBold, bPt))
                + bRun.LetterSpacing * bText.Length;
            var bCircleD = bRun.CircleFill is not null
                ? (bRun.CircleD > 0 ? bRun.CircleD : 14.25) : 0;
            penByLine.TryGetValue(bLi, out var pen);
            InlineBoxDecoration deco;
            if (placed is not null && placed.TryGetValue(bRun, out var bAt))
            {
                // Continuation: the plate was already painted (declared
                // height); only the run's LAST segment keeps the bottom pad,
                // TRIMMED so the cell's line stack sums exactly to the plate
                // height (the row must not outgrow the plate).
                var contPadB = bAt.Seen + 1 == runSegs[bRun] ? bRun.PadB : 0;
                if (bRun.DeclH > 0)
                {
                    var plateTotal = bRun.PadT + bRun.DeclH + bRun.PadB;
                    contPadB = Math.Max(0, Math.Min(contPadB,
                        plateTotal - bAt.FirstLineH - bRun.ContPadTop
                        - Math.Max(bPt * 1.2, 15.0)));
                }
                deco = new InlineBoxDecoration
                {
                    XOff = bAt.XOff, Width = bAt.W, Fill = null,
                    PadTop = bRun.ContPadTop,
                    PadBottom = contPadB,
                    Text = bText, TextX = bAt.XOff + (bAt.W - bTw) / 2,
                    TextSize = bPt, TextBold = bBold,
                    TextLetterSpacing = bRun.LetterSpacing,
                };
                placed[bRun] = (bAt.XOff, bAt.W, bAt.Seen + 1, bAt.FirstLineH);
                pen = Math.Max(pen, bAt.XOff + bAt.W);
            }
            else
            {
                var bW = bRun.PadL + bTw
                    + (bCircleD > 0 ? (bTw > 0 ? BadgeLabelGapPt : 0) + bCircleD : 0)
                    + bRun.PadR;
                deco = new InlineBoxDecoration
                {
                    XOff = pen, Width = bW,
                    PadTop = bRun.PadT,
                    PadBottom = runSegs[bRun] > 1 ? 0 : bRun.PadB,
                    PadRight = bRun.PadR, Radius = bRun.Radius, Fill = bRun.Fill,
                    Height = bRun.DeclH > 0 ? bRun.PadT + bRun.DeclH + bRun.PadB : 0,
                    // A block bar (h1, margin:0) hugs its line box — no inset.
                    // A circle-only run (a standalone badge in its own cell)
                    // carries no pill chrome — its line is the circle.
                    InsetV = bRun.DeclH > 0 ? PlateBreathingPt
                        : bRun.FullWidth || bText.Length == 0 ? 0 : PillLineInsetPt,
                    Text = bText,
                    TextX = pen + (bCircleD > 0 ? bRun.PadL : (bW - bTw) / 2),
                    TextSize = bPt, TextBold = bBold,
                    TextLetterSpacing = bRun.LetterSpacing,
                    FullWidth = bRun.FullWidth,
                    TextCentered = bRun.TextCentered,
                    TextColor = bRun.TextColor,
                    CircleFill = bRun.CircleFill, CircleD = bCircleD,
                    CircleLetter = bRun.CircleLetter.Length > 0 ? bRun.CircleLetter : null,
                    CircleLetterColor = bRun.CircleLetterColor,
                };
                (placed ??= new Dictionary<ChainBoxRun, (double, double, int, double)>())[bRun]
                    = (pen, bW, 1, bRun.PadT + Math.Max(bPt * 1.2, 15.0));
                pen += bW + InlineBoxSiblingGapPt;
            }
            penByLine[bLi] = pen;
            if (cc.boxByLine is null || !cc.boxByLine.TryGetValue(bLi, out var bList))
                (cc.boxByLine ??= new Dictionary<int, List<InlineBoxDecoration>>())[bLi]
                    = bList = new List<InlineBoxDecoration>();
            bList.Add(deco);
        }
        cc.ps.cellBoxSegs.Clear();
    }

    /// <summary>Word mail: the deferred pictures met before this line join the cell now, ahead of the line.</summary>
    private static void PlaceWordMailPicturesBefore(CloseCellState cc, int lineNo)
    {
        if (cc.ps.pendingCellImgs is not { Count: > 0 } imgs || cc.ps.pendingCellImgAt is not { Count: > 0 } at) return;
        while (imgs.Count > 0 && at.Count > 0 && at[0] <= lineNo)
        {
            cc.ps.cell!.Paragraphs.Add(imgs[0]);
            imgs.RemoveAt(0); at.RemoveAt(0);
        }
    }
}
