using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    private static void CloseCell(TableParseState ps, TableColumnModel colModel, Table table, HtmlLoadOptions? options, double cellFontSize, bool dwFormCells, bool fullWidthCjkMin, bool breakAnywhereDoc, bool cellFontShorthand, List<CssElem>? chainBase, double chainSpacingPt, List<(string Tag, int PrevBoldDepth)> chainUnbold, string? cssBaseFamily, double cssBasePt, string? defaultCellFace, double formGridStrutDropPt, bool hasBorder, double inlineFaceRatio, bool overDeclaredDraw, double padSide, bool uaDocGrid, bool widenProbe, bool uaSerifMin, bool ptCellWidths, bool redlineCells, bool bandDialect, double cellLineHeightPt, string? cssRunFace, bool formGridDialect, double formGridStrutPt, bool liftNestedTables, bool tightExtras, bool uaCellBoxes, double borderWidth, double pad)
    {
        if (ps.cell is null || ps.row is null) return;
        // A declared width is the cell's CONTENT box: its own horizontal padding
        // rides on top of it in the column footprint, the way it does in every other
        // measure here. An image column declaring `width="150"` plus a 20 px
        // padding-right is a 170 px column — the gutter between it and the text
        // column beside it was being dropped.
        ps.rowWidths.Add(ps.cellWidthPt > 0 ? ps.cellWidthPt + ps.cellCssPadPt : ps.cellWidthPt);
        if (ps.colSpan > 1 || ps.cellWidthPt <= 0) ps.rowAllSingleExplicit = false;
        // Band dialect: an &nbsp;-only cell is a text row in a browser — a line
        // box at the cell's font size — unlike a truly empty <td></td>, which
        // collapses. Keep its blank line so the row spacing holds
        // (the corner-mark spacer rows of proxy cards are built from these).
        if (bandDialect && ps.lines.Count == 0
            && IsAllWhitespace(ps.line)
            && ps.line.ToString().IndexOf(' ') >= 0)
        {
            if (ps.lineFontPt <= 0) ps.lineFontPt = ps.curFontPt > 0 ? ps.curFontPt : ps.rowFontPt;
            if (ps.lineFontPt > 0) PushLine(ps, redlineCells, dwFormCells, widenProbe, keepIfBlank: true);
            else PushLine(ps, redlineCells, dwFormCells, widenProbe);
        }
        // The cell ended on a <br> with nothing after it — the break's own empty
        // line box is real vertical space, so it must not be swallowed here.
        else if (ps.cellPendingBrBlank && IsAllWhitespace(ps.line))
        {
            // The blank box takes the type the cell itself sets in — a cell that
            // declares no size of its own still has one, so the break's line is
            // real space rather than a zero-height line that gets swept away.
            if (ps.lineFontPt <= 0)
                ps.lineFontPt = ps.curFontPt > 0 ? ps.curFontPt
                    : ps.rowFontPt > 0 ? ps.rowFontPt
                    : uaDocGrid ? cellFontSize : 0;
            PushLine(ps, redlineCells, dwFormCells, widenProbe, keepIfBlank: ps.lineFontPt > 0);
        }
        else PushLine(ps, redlineCells, dwFormCells, widenProbe);
        // The div box is also the WRAP box: a run too long for it breaks inside the
        // div — after a hyphen when one fits, otherwise mid-token (break-word) —
        // instead of running the full width of the enclosing cell.
        if (ps.cellFixedDivPt > 0 && ps.lines.Count > 0)
        {
            var rewrapped = new List<(string Text, double FontPt, string? Family, bool Keep,
                bool JoinNext, List<(string Text, string Url)>? Anchors, bool Bold,
                double MarginTopPt, double MarginLeftPt, Color? Color, bool Italic)>();
            foreach (var spec in ps.lines)
            {
                var wPt = spec.FontPt > 0 ? spec.FontPt : 0.0;
                Func<string, double> boxMeasure = uaCellBoxes
                    ? s => MeasureSerifLine(cellFontSize, s, spec.Bold, wPt)
                    : s => MeasureLine(ps, options, cellFontSize, dwFormCells, fullWidthCjkMin, widenProbe, s, spec.Bold, wPt, spec.Family);
                if (spec.Text.Length == 0 || boxMeasure(spec.Text) <= ps.cellFixedDivPt)
                {
                    rewrapped.Add(spec);
                    continue;
                }
                var firstPiece = true;
                foreach (var piece in WrapToBox(spec.Text, ps.cellFixedDivPt, boxMeasure))
                {
                    rewrapped.Add((piece, spec.FontPt, spec.Family, spec.Keep,
                        firstPiece && spec.JoinNext, firstPiece ? spec.Anchors : null, spec.Bold,
                        firstPiece ? spec.MarginTopPt : 0, spec.MarginLeftPt, spec.Color,
                        spec.Italic));
                    firstPiece = false;
                }
            }
            ps.lines.Clear();
            ps.lines.AddRange(rewrapped);
        }
        // A cell whose lines carry explicit per-line styles (font-size on a <p>/<span>)
        // renders as CSS line boxes: every line gets its TRUE size (styled size, else the
        // stylesheet base), plus line-box metrics for the generator's mixed-size layout.
        // The CSS run dialect lays EVERY cell out as CSS line boxes, styled or not:
        // the uniform per-row grid takes the tallest cell's pitch, so one 24 pt
        // column would otherwise stretch every 10 pt column in its row to match.
        var anyStyled = cellFontShorthand || cssRunFace is not null;
        foreach (var l in ps.lines) if (l.FontPt > 0) anyStyled = true;
        // Unstyled cells keep the LEGACY line structure: lines split only at <br>/<img>/
        // cell close. Rejoin the paragraph (<p>) splits so plain-markup tables are
        // byte-identical to the pre-styled-cell behaviour.
        if (!anyStyled)
            for (var k = 0; k < ps.lines.Count - 1; k++)
                if (ps.lines[k].JoinNext)
                {
                    var merged = CollapseWs(ps.lines[k].Text + " " + ps.lines[k + 1].Text);
                    var mergedAnchors = ps.lines[k].Anchors;
                    if (ps.lines[k + 1].Anchors is { } nextAnchors)
                        (mergedAnchors ??= new()).AddRange(nextAnchors);
                    ps.lines[k] = (merged, 0, ps.lines[k].Family ?? ps.lines[k + 1].Family,
                        false, ps.lines[k + 1].JoinNext, mergedAnchors,
                        ps.lines[k].Bold && ps.lines[k + 1].Bold, ps.lines[k].MarginTopPt, ps.lines[k].MarginLeftPt,
                        ps.lines[k].Color ?? ps.lines[k + 1].Color,
                        ps.lines[k].Italic && ps.lines[k + 1].Italic);
                    ps.lines.RemoveAt(k + 1);
                    k--;
                }
        // Resolve the recorded box-run segments into per-line decorations. The
        // box model owns the pen: each box advances it by its full width (pads
        // take real space) plus a 3 pt sibling gap; text centres inside its box
        // (pill labels sit at the left pad, ahead of their circle). A run's
        // LATER segment (the ID line under a title plate) reuses the plate's
        // placed box — no rectangle, just its own centred text run.
        Dictionary<int, List<InlineBoxDecoration>>? boxByLine = null;
        if (ps.cellBoxSegs is { Count: > 0 })
        {
            // A cell whose only content is a box (a standalone badge circle in
            // an otherwise-empty td) never pushed a line — materialise the
            // blank line(s) its segments point at.
            var needLines = 0;
            foreach (var (bLi0, _, _, _) in ps.cellBoxSegs)
                if (bLi0 + 1 > needLines) needLines = bLi0 + 1;
            while (ps.lines.Count < needLines) PushLine(ps, redlineCells, dwFormCells, widenProbe);
            var runSegs = new Dictionary<ChainBoxRun, int>();
            foreach (var (_, bRun0, _, _) in ps.cellBoxSegs)
                runSegs[bRun0] = runSegs.TryGetValue(bRun0, out var n0) ? n0 + 1 : 1;
            Dictionary<ChainBoxRun, (double XOff, double W, int Seen, double FirstLineH)>? placed = null;
            var penByLine = new Dictionary<int, double>();
            foreach (var (bLi, bRun, _, bText) in ps.cellBoxSegs)
            {
                var bSpec = bLi < ps.lines.Count ? ps.lines[bLi] : default;
                var bPt = bSpec.FontPt > 0 ? bSpec.FontPt
                    : ps.cellClassPt > 0 ? ps.cellClassPt : cellFontSize;
                var bBold = bSpec.Bold || ps.cellBold || ps.isHeader;
                // Measure the box text with the REAL face the box renders in
                // (Arial advances run ~3% wider than the Standard-14 estimate on
                // these bold runs). MAX with the AFM estimate: an uninstalled
                // face degrades to a 0.5-em guess that must not narrow the box.
                var bTw = Math.Max(
                        MeasureFaceText(bBold ? "Arial Bold" : "Arial", bText, bPt),
                        MeasureLine(ps, options, cellFontSize, dwFormCells, fullWidthCjkMin, widenProbe, bText, bBold, bPt))
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
                if (boxByLine is null || !boxByLine.TryGetValue(bLi, out var bList))
                    (boxByLine ??= new Dictionary<int, List<InlineBoxDecoration>>())[bLi]
                        = bList = new List<InlineBoxDecoration>();
                bList.Add(deco);
            }
            ps.cellBoxSegs.Clear();
        }
        var tfLineIdx = -1;
        var cellHadNestedTable = false;
        foreach (var spec in ps.lines)
        {
            tfLineIdx++;
            var ln = spec.Text;
            // A nested grid anchored at (or before) this line joins the cell's
            // paragraphs HERE, so text that followed it in the markup stays
            // below it (an attachments grid draws between its caption and the
            // template list, not after both).
            while (ps.pendingCellTables is { Count: > 0 } && ps.pendingCellTables[0].AnchorLine <= tfLineIdx)
            {
                ps.cell.Paragraphs.Add(ps.pendingCellTables[0].T);
                ps.pendingCellTables.RemoveAt(0);
                cellHadNestedTable = true;
            }
            // A deliberately-kept blank line is a real line box (the newline a
            // pre-wrap box holds onto), so it survives in a UA-cell-box grid even
            // when the cell carries no per-line styling. The lifted dialect keeps
            // them too: an explicit <br> on an empty line (`<BR><BR>` between
            // form questions) is the vertical rhythm of the source document.
            // A blank line CARRYING inline boxes (a standalone badge circle in
            // an otherwise-empty cell) survives too — the decos ride the fragment.
            if (ln.Length == 0
                && !((anyStyled || uaCellBoxes
                        || (liftNestedTables
                            && !(ps.loneBrBlankLines?.Contains(tfLineIdx) ?? false))) && spec.Keep)
                && !(boxByLine is not null && boxByLine.ContainsKey(tfLineIdx))) continue;
            var tf = new Text.TextFragment(ln);
            tf.HtmlAnchors = spec.Anchors;
            tf.HtmlUnderline = ps.underlinedLines?.Contains(tfLineIdx) == true;
            // A class on the cell (`.header { font-size:16pt }`) sizes its text even
            // when no line carries a style of its own — the class rule is more
            // specific than the sheet's `table td` base.
            tf.TextState.FontSize = (float)(ps.cellClassPt > 0 ? ps.cellClassPt : cellFontSize);
            if (anyStyled)
            {
                var pt = spec.FontPt > 0 ? spec.FontPt
                    : ps.cellClassPt > 0 ? ps.cellClassPt
                    : (cssBasePt > 0 ? cssBasePt : cellFontSize);
                tf.TextState.FontSize = (float)pt;
                // The TextFragment ctor's segment carries its own default size which the
                // generator prefers — set it too so the styled size actually applies.
                foreach (var seg in tf.Segments)
                    if (!string.IsNullOrEmpty(seg.Text)) seg.TextState.FontSize = (float)pt;
                var (asc, desc) = CssFamilyMetrics(spec.Family ?? cssBaseFamily);
                tf.CssAscent = asc; tf.CssDescent = desc;
                tf.CssKeepBlank = spec.Keep;
                tf.CssLineBoxAlways = cellFontShorthand || cssRunFace is not null;
            }
            // The lifted dialect keeps deliberate blank line boxes (<br> on an
            // empty line) whether or not the cell is styled — the generator
            // needs the flag to price them as real boxes.
            if (liftNestedTables) tf.CssKeepBlank = spec.Keep;
            if (ps.isHeader || spec.Bold || ps.cellBold) tf.TextState.IsBold = true;
            if (spec.Italic) tf.TextState.IsItalic = true;
            // Mixed bold runs on this line (form-grid): the render draws each
            // segment in its own face variant.
            if (ps.lineRunsByIdx is not null
                && ps.lineRunsByIdx.TryGetValue(tfLineIdx, out var fgRuns))
                tf.FormGridRuns = fgRuns;
            // Source page's CSS line-height (e.g. body `font: 1em/1.4em …`):
            // wrapped cell lines pitch at the CSS box, not the bare font size.
            // UA cell boxes: every line takes the `line-height: normal` box of its
            // OWN size, so an 8 pt row pitches at 9 pt while a 10 pt one takes 11.25
            // — a single document-wide pitch oversizes every small-font row.
            if (uaCellBoxes)
                tf.CssLineHeightPt = NormalLineHeightPt(tf.TextState.FontSize);
            // Form-grid dialect: every line takes its own size's px-rounded
            // Verdana line box, floored at the cell's strut — the td's own
            // declared size when it styles one, else the chunk's ambient box.
            // The 8pt rows sit on the strut; the 36pt spacer row grows to
            // its 58px box (43.5).
            else if (formGridDialect)
            {
                var fgStrut = ps.cellFgStrutPt > 0 ? ps.cellFgStrutPt
                    : formGridStrutPt > 0 ? formGridStrutPt : VerdanaGridMinLinePt;
                tf.CssLineHeightPt = Math.Max(
                    PxLinePt(tf.TextState.FontSize, VerdanaWinLineRatio), fgStrut);
                // The line's baseline seat: the run's own drop within its box,
                // floored at the strut's (the td-own strut carries its own).
                var fgRunDrop = (tf.CssLineHeightPt
                    - tf.TextState.FontSize * VerdanaWinLineRatio) / 2
                    + tf.TextState.FontSize * VerdanaWinAscent;
                var fgStrutDrop = ps.cellFgStrutPt > 0
                    ? (ps.cellFgStrutPt - ps.cellFgStrutFontPt * VerdanaWinLineRatio) / 2
                        + ps.cellFgStrutFontPt * VerdanaWinAscent
                    : formGridStrutDropPt;
                tf.CssBaseDrop = Math.Max(fgStrutDrop, fgRunDrop);
                if (Environment.GetEnvironmentVariable("ASPOSE_HTML_DEBUG_FG") is not null
                    && spec.FontPt > 12)
                    Console.WriteLine($"[fg] specPt={spec.FontPt} tfPt={tf.TextState.FontSize} " +
                        $"lh={tf.CssLineHeightPt:0.##} anyStyled={anyStyled} txt='{ln}'");
            }
            // `line-height: normal` is the BASE FACE's own win-metric box, not the
            // serif-calibrated constant — a 24 pt run in a Segoe UI page pitches on
            // Segoe's ratio (32.25), a 9.75 pt one on 12.75.
            else if (cssRunFace is not null && WinMetricsFor(cssRunFace) is { } crm)
                tf.CssLineHeightPt = MetricLineHeight(tf.TextState.FontSize, crm.sum);
            else if (ps.cellOwnLineHPt > 0)
            {
                tf.CssLineHeightPt = ps.cellOwnLineHPt;
                // The cell's pitch becomes the LINE'S OWN BOX only for lines that
                // did not style their own font: a run carrying its own size usually
                // carries its own line-height context too (an embedded document's
                // `p { line-height:1.2 }` overriding the host cell's 19px), and we
                // do not model that cascade — the row-level pitch still applies.
                tf.CssLineHeightFromCell = spec.FontPt <= 0 && !ps.cellOwnHeightDecl;
            }
            // A grid whose face came from the DOCUMENT stacks on that face's normal
            // line box (a DECLARED cell line-height above wins, as in CSS). The
            // pitch is a property of the face — Arial 12 steps 13.50 whether or not
            // the table happens to mix sizes — so it does not ride the run-styling
            // gate above, which asks a different question.
            else if (uaDocGrid && defaultCellFace is { } dclf
                     && WinMetricsFor(dclf) is { } dcm)
                tf.CssLineHeightPt = MetricLineHeight(tf.TextState.FontSize, dcm.sum);
            // DataWorks form cells pace on the browser line box of their own
            // size (1.125 em: 13.5 at 12 pt); the h1 title row measures a
            // taller box (its row spans 33.8 with pads).
            else if (dwFormCells && tf.TextState.FontSize > 0)
            {
                var dwLn = spec.Text;
                tf.CssLineHeightPt = tf.TextState.FontSize >= DwH1FontPt - 0.1
                    ? DwH1LineBoxPt

                    // …and radio/checkmark option lines pitch at the widget
                    // box (14.06 measured between the options).
                    : dwLn.IndexOf(Table.InlineRadioChar) >= 0
                        || dwLn.IndexOf(Table.InlineRadioCheckedChar) >= 0
                        || dwLn.IndexOf(Table.InlineCheckChar) >= 0
                        || dwLn.IndexOf('✓') >= 0 ? DwOptionLinePt
                    : tf.TextState.FontSize * RedlineLineFactor;
            }
            else if (cellLineHeightPt > 0) tf.CssLineHeightPt = cellLineHeightPt;
            // Over-declared grid document: lines pitch on the CSS line box of
            // their own size — .875rem (10.5 pt) steps 12, and the sheet's
            // 1rem class declares line-height 16px = the same 12. The bare
            // font-size stack under-paces every row (measured on the spacing
            // ladder: 12 per line through the address block and title rows).
            else if (overDeclaredDraw)
                tf.CssLineHeightPt = Table.CssLineBoxPt(tf.TextState.FontSize);
            // pt-styled fragment: lines pitch on the plain 1.2em box
            // (measured: the wrapped header's inner lines step 12.0 at
            // 10 pt; the 8 pt card rows fall under their declared 10.25 tr
            // height instead). The COLLAPSED border's share of the row pitch
            // is billed once per row in the layout (see the halved borderV),
            // not off the line box.
            else if (ptCellWidths && tf.TextState.FontSize > 0)
                tf.CssLineHeightPt = PtFragmentLineFactor * tf.TextState.FontSize;
            // Redline cells pace on the flow's 1.125 em LINE box (probed:
            // the address rows step 11.25 at 10 pt).
            else if (redlineCells && tf.TextState.FontSize > 0)
                tf.CssLineHeightPt = RedlineLineFactor * tf.TextState.FontSize;
            if ((spec.Color ?? ps.cellChainColor ?? ps.cellTextColor) is { } tfColor)
                tf.TextState.ForegroundColor = tfColor;
            // Band dialect: the paragraph's explicit margins become the fragment's
            // margins — a gap above its first line and an indent that narrows its
            // wrap box in the cell layout.
            if ((bandDialect || liftNestedTables) && (spec.MarginTopPt > 0 || spec.MarginLeftPt > 0))
                tf.Margin = new MarginInfo { Top = spec.MarginTopPt, Left = spec.MarginLeftPt };
            // pt-styled fragment: the paragraph margin pair insets the WRAP
            // box (see Table.HtmlWrapInsetsCellMargins).
            if (ptCellWidths && (ps.cellPMarginRightPt > 0 || spec.MarginLeftPt > 0))
            {
                tf.HtmlWrapInsetPt = ps.cellPMarginRightPt + spec.MarginLeftPt;
                tf.HtmlMarginLeftPt = spec.MarginLeftPt;
            }
            if (redlineCells && ps.lineDecorsByIdx is not null
                && ps.lineDecorsByIdx.TryGetValue(tfLineIdx, out var tfDecs))
                tf.HtmlDecors = tfDecs;
            if (dwFormCells && ps.lineColorRunsByIdx is not null
                && ps.lineColorRunsByIdx.TryGetValue(tfLineIdx, out var tfCols))
                tf.HtmlColorRuns = tfCols;
            // The cell's own padding-left indents its text the way it widened the
            // column — a left-aligned run starts that far inside the cell box.
            if (ps.cellPadLeftPt > 0 && ps.cellAlign != HorizontalAlignment.Right)
                tf.Margin = new MarginInfo
                {
                    Top = tf.Margin?.Top ?? 0,
                    Left = (tf.Margin?.Left ?? 0) + ps.cellPadLeftPt,
                    Right = tf.Margin?.Right ?? 0,
                };
            var famForFont = spec.Family ?? ps.cellFamily;
            if (famForFont is not null && ln.Length > 0)
            {
                try
                {
                    var f = Text.FontRepository.TryFindFont(famForFont);
                    // CSS families are case-insensitive: the inline-face grid
                    // resolves "verdana" to the installed Verdana it pitches by.
                    if (f is null && inlineFaceRatio > 0)
                        f = Text.FontRepository.TryFindFont(famForFont, ignoreCase: true);
                    if (f is not null) tf.TextState.Font = f;
                }
                catch { }
            }
            // Non-WinAnsi text (CJK, Cyrillic, Greek, …) can't render in the Standard-14
            // WinAnsi fonts — it would collapse to '?'. Fall back to an embedded Unicode
            // face that covers the run so it flows through the Type0/CID render path.
            // RTL text is deliberately left alone (no font override, text unmodified):
            // the generator's cell pipeline shapes and embeds Arabic/Hebrew natively.
            if (ln.Length > 0 && tf.TextState.Font?.SourceFontData is null && NeedsUnicode(ln)
                && !Text.BidiReorderer.ContainsRtl(ln))
            {
                var uf = ResolveUnicodeFont(ln);
                if (uf is not null) tf.TextState.Font = uf;
            }
            // Inline boxes recorded for this line (plates/pills) ride the
            // fragment; the box line height reserves the pill's full height,
            // and a continuation line is centred via the fragment margin.
            if (boxByLine is not null && boxByLine.TryGetValue(tfLineIdx, out var tfBoxes))
            {
                tf.InlineBoxes = tfBoxes;
                double bxPadT = 0, bxPadB = 0, bxInsetV = 0;
                var bxCircle = false;
                foreach (var b4 in tfBoxes)
                {
                    bxPadT = Math.Max(bxPadT, b4.PadTop);
                    // A declared-height box (title plate) self-sizes its rect:
                    // its bottom pad lives inside the rect, not in the line stack
                    // (the continuation line follows at text pitch).
                    if (b4.Height <= 0) bxPadB = Math.Max(bxPadB, b4.PadBottom);
                    bxInsetV = Math.Max(bxInsetV, b4.InsetV);
                    if (b4.CircleFill is not null) bxCircle = true;
                }
                // The 15pt floor exists for the badge CIRCLE's diameter — a bar
                // with no circle keeps its text's own line box (circle-less h2
                // bars are ~12.8, not 15+).
                var bxLineH = bxPadT
                    + Math.Max(tf.TextState.FontSize * Table.CssNormalLineHeight,
                        bxCircle ? 15.0 : 0.0)
                    + bxPadB + 2 * bxInsetV;
                if (bxLineH > tf.CssLineHeightPt) tf.CssLineHeightPt = bxLineH;
            }
            // Hand this fragment the radio options its marker chars stand for
            // (in document order — options were queued as their inputs were
            // walked, and lines flush in the same order).
            if (ps.cellInlineOptions is { Count: > 0 })
            {
                var nMarks = 0;
                foreach (var mch in ln)
                    if (mch is Table.InlineRadioChar or Table.InlineRadioCheckedChar) nMarks++;
                if (nMarks > 0)
                {
                    var take = Math.Min(nMarks, ps.cellInlineOptions.Count);
                    tf.InlineOptions = ps.cellInlineOptions.GetRange(0, take);
                    ps.cellInlineOptions.RemoveRange(0, take);
                }
            }
            if (ps.cellInputBoxes is { Count: > 0 })
            {
                var nBoxMarks = 0;
                foreach (var mch in ln)
                    if (mch == Table.InlineInputChar) nBoxMarks++;
                if (nBoxMarks > 0)
                {
                    var take = Math.Min(nBoxMarks, ps.cellInputBoxes.Count);
                    tf.InlineInputBoxes = ps.cellInputBoxes.GetRange(0, take);
                    ps.cellInputBoxes.RemoveRange(0, take);
                }
            }
            ps.cell.Paragraphs.Add(tf);
        }
        // Images that FOLLOWED text in the markup were deferred so the cell's
        // paragraph order matches the source ("label<br><img>" draws the label
        // line above the image box, not underneath its blit).
        if (ps.pendingCellTables is { Count: > 0 })
        {
            foreach (var (ptbl, _) in ps.pendingCellTables) ps.cell.Paragraphs.Add(ptbl);
            ps.pendingCellTables.Clear();
            cellHadNestedTable = true;
        }
        if (cellHadNestedTable)
        {
            (colModel.nestedTableCols ??= new HashSet<int>()).Add(ps.row.Cells.Count);
            // This grid really does hold a lifted nested table — the browser line
            // box applies to it (see Table.HtmlLiftedGrid).
            table.HtmlLiftedGrid = true;
        }
        if (ps.pendingCellImgs is { Count: > 0 })
        {
            foreach (var pimg in ps.pendingCellImgs) ps.cell.Paragraphs.Add(pimg);
            ps.pendingCellImgs.Clear();
        }
        // The cell's own CSS padding becomes its box padding: it indents the drawn
        // text and narrows the wrap box exactly as it widened the column. A chain
        // rule's vertical padding rides the same box (the horizontal pair came in
        // through cellCssPadPt/cellPadLeftPt). A full Margin REPLACES the table's
        // DefaultCellPadding wholesale, so the chain dialect keeps the default's
        // vertical band (the cellspacing rhythm) when the cell adds none.
        if (ps.cellPadLeftPt > 0 || ps.cellCssPadPt > 0 || ps.cellChainPadTopPt > 0 || ps.cellChainPadBotPt > 0
            || (redlineCells && ps.cellFirstPMarginTopPt > 0))
        {
            var vTop = Math.Max(pad, ps.cellChainPadTopPt);
            var vBot = Math.Max(pad, ps.cellChainPadBotPt);
            var hExtra = 0.0;
            if (chainBase is not null && table.DefaultCellPadding is { } dcpM)
            {
                vTop = Math.Max(vTop, dcpM.Top);
                vBot = Math.Max(vBot, dcpM.Bottom);
            }
            // A declared border-spacing is a gap OUTSIDE the cell's own padding,
            // so it adds to it rather than competing with it (the pill's detail
            // button keeps its 1ex pad and still sits 2 pt off its neighbours).
            if (chainSpacingPt > 0)
            {
                vTop += chainSpacingPt / 2;
                vBot += chainSpacingPt / 2;
                hExtra = chainSpacingPt / 2;
            }
            // …and the table-level padSide is read from the SAME declaration that
            // gave the chain its cellCssPadPt, so adding both insets the text a
            // second pad in and narrows the wrap box by a whole pair — the drawn
            // twin of the column footprint's `padSideExtra` bill.
            var padSideBox = chainBase is not null && ps.cellCssPadPt > 0 ? 0 : padSide;
            // redline: the first paragraph's margin-top rides as cell pad
            // (a cell whose content is entirely hidden spends none of it)
            if (redlineCells && ps.cellFirstPMarginTopPt > 0)
            {
                var rlAnyText = false;
                foreach (var rlSpec in ps.lines)
                    if (rlSpec.Text.Trim(' ', ' ').Length > 0) { rlAnyText = true; break; }
                if (rlAnyText) vTop += ps.cellFirstPMarginTopPt;
            }
            ps.cell.Margin = new MarginInfo(padSideBox + ps.cellPadLeftPt + hExtra, vBot,
                padSideBox + (ps.cellCssPadPt - ps.cellPadLeftPt) + hExtra, vTop);
        }
        ps.cell.IsWordWrapped = true;
        ps.cell.ColSpan = Math.Max(1, ps.colSpan);
        // A lifted table is measured by the layout pass, which reads the span from
        // the cell; the legacy grid keeps its own calibrated row mapping.
        if (liftNestedTables && ps.cellRowSpan > 1) ps.cell.RowSpan = ps.cellRowSpan;
        ps.cell.Alignment = ps.alignSet ? ps.cellAlign
            : ps.rowAlign
            ?? (ps.isHeader ? ps.headerAlign ?? HorizontalAlignment.Center : HorizontalAlignment.Left);
        if (ps.isHeader && ps.headerBorder is not null) ps.cell.Border = ps.headerBorder;
        // Declared-zero vertical padding: the cell's box loses the table's
        // cellpadding on the BOTTOM side (over-declared grid dialect). The
        // top half stays — the rows keep their seat while the
        // inter-row pitch tightens (zeroing both drifted every row 3.5 high).
        if (overDeclaredDraw && ps.cellVPadZeroBot && ps.cell.Margin is null)
        {
            var vpSide = Math.Max(0, padSide);
            ps.cell.Margin = new MarginInfo(vpSide, 0, vpSide, vpSide);
        }
        ps.row.Cells.Add(ps.cell);
        // Record this cell's content width against the column(s) it spans, so a table with no
        // explicit widths auto-fits each column to its widest content (+ cell padding).
        double cellMin = 0, cellMax = 0, cellHdr = 0, cellMinBrk = 0;
        foreach (var spec in ps.lines)
        {
            var ln = spec.Text;
            // The probe measures each line with the styles it renders with: its own
            // font size (a 9pt header row in a 10pt table measures at 9), real bold
            // metrics for an all-bold line, and its own family.
            // …and so does the CSS run dialect, whose column floors must hold the
            // widest token AT ITS OWN SIZE (a 24 pt run in a 10 pt table).
            var mPt = (widenProbe || cssRunFace is not null || chainBase is not null
                || ptCellWidths || dwFormCells) && spec.FontPt > 0 ? spec.FontPt : 0.0;
            var mBold = ps.isHeader || ((widenProbe || chainBase is not null || ptCellWidths) && spec.Bold);
            var mFam = widenProbe ? spec.Family
                : ptCellWidths || dwFormCells ? spec.Family ?? ps.cellFamily : null;
            // pt-styled fragment: the paragraph's own margins ride the cell's
            // min-content footprint (probed: the squeeze sheds almost nothing
            // from the header column whose word + pads + margins ≈ its box).
            var mMargins = ptCellWidths
                ? spec.MarginLeftPt + ps.cellPMarginRightPt + ps.cellCssPadPt : 0;
            cellMin = Math.Max(cellMin, MeasureMinContent(ps, options, cellFontSize, dwFormCells, fullWidthCjkMin, widenProbe, ln, mBold, mPt, mFam) + mMargins);
            cellMinBrk = Math.Max(cellMinBrk, MeasureMinContent(ps, options, cellFontSize, dwFormCells, fullWidthCjkMin, widenProbe, ln, mBold, mPt, mFam, breakDashes: true) + mMargins);
            cellMax = Math.Max(cellMax, MeasureLine(ps, options, cellFontSize, dwFormCells, fullWidthCjkMin, widenProbe, ln, mBold, mPt, mFam));
            // A header cell's full (unwrapped) line width — used to keep <th> on one line when
            // the whole table still fits the available width (a browser does not wrap headers to
            // their widest word). Recorded separately so it never forces the page/table wider.
            if (ps.isHeader) cellHdr = Math.Max(cellHdr, MeasureLine(ps, options, cellFontSize, dwFormCells, fullWidthCjkMin, widenProbe, ln, bold: true));
        }
        // Break-anywhere sheet: the breakable min is one character — an em of
        // the cell's size (see breakAnywhereDoc above). The NO-BREAK min
        // shrinks the same way: with break-anywhere in force nothing is
        // unbreakable, so neither floor may eat a neighbour's declared share.
        if (breakAnywhereDoc)
        {
            var oneEm = ps.cellClassPt > 0 ? ps.cellClassPt : cellFontSize;
            if (cellMinBrk > 0) cellMinBrk = Math.Min(cellMinBrk, oneEm);
            if (cellMin > 0) cellMin = Math.Min(cellMin, oneEm);
        }
        // white-space:nowrap under the chain dialect: the cell's floor is its
        // whole unwrapped line — nowrap labels never wrap,
        // so a space-broken min under-sizes the column and the cell
        // fill stops mid-text. The page-width probe honours the same rule
        // everywhere: the page widens for a nowrap run rather than
        // wrapping it (the render dialects keep their calibrated floors).
        if (ps.cell.HtmlNoWrap && dwFormCells)
        {

            // A nowrap cell floors at its TRIMMED line — the
            // trailing spaces hang past the column instead of sizing it.
            double dwTrimMax = 0;
            foreach (var spec2 in ps.lines)
                dwTrimMax = Math.Max(dwTrimMax, MeasureLine(ps, options, cellFontSize, dwFormCells, fullWidthCjkMin, widenProbe, spec2.Text.TrimEnd(),
                    ps.isHeader || spec2.Bold, spec2.FontPt > 0 ? spec2.FontPt : 0.0,
                    spec2.Family ?? ps.cellFamily));
            cellMin = Math.Max(cellMin, dwTrimMax);
            cellMinBrk = Math.Max(cellMinBrk, dwTrimMax);
        }
        else if (ps.cell.HtmlNoWrap && (chainBase is not null || fullWidthCjkMin)
            && cellMax > cellMin)
        {
            cellMin = cellMax;
            cellMinBrk = Math.Max(cellMinBrk, cellMax);
        }
        // An inline-box line's width is its BOX extent (pads, circle and sibling
        // gaps included) — the flat text under-measures the plates by their
        // padding, and the column then leaves dead space beside its neighbour.
        if (boxByLine is not null)
        {
            double bxExt = 0;
            foreach (var bl3 in boxByLine.Values)
                foreach (var b6 in bl3)
                    bxExt = Math.Max(bxExt, b6.XOff + b6.Width);
            if (bxExt > 0)
            {
                cellMin = Math.Max(cellMin, bxExt + InlineBoxColumnSlackPt);
                cellMinBrk = Math.Max(cellMinBrk, bxExt + InlineBoxColumnSlackPt);
                cellMax = Math.Max(cellMax, bxExt + InlineBoxColumnSlackPt);
            }
        }
        // A fixed-width div inside the cell IS the cell's min-content: its long
        // token wraps inside the div box (break-word), so the div box — not the
        // token — sizes the column. The cell's own CSS padding rides on every
        // measure (the column footprint includes it).
        if (ps.cellFixedDivPt > 0)
        {
            cellMin = ps.cellFixedDivPt;
            cellMinBrk = Math.Min(cellMinBrk, ps.cellFixedDivPt);
            if (ps.cellFixedDivPt > cellMax) cellMax = ps.cellFixedDivPt;
        }
        // …and an image the cell draws claims its own box in every measure.
        if (ps.cellImgWidthPt > 0)
        {
            cellMin = Math.Max(cellMin, ps.cellImgWidthPt);
            cellMinBrk = Math.Max(cellMinBrk, ps.cellImgWidthPt);
            cellMax = Math.Max(cellMax, ps.cellImgWidthPt);
        }
        // A NESTED table sizes its cell: the grid's own natural width is the
        // cell's min- and max-content (its flattened text lines measure a
        // fraction of the real grid).
        if (ps.pendingCellTablesNatW > 0)
        {
            // Break-anywhere sheet: the nested grid's box-filling natural
            // width is NOT a floor on its host — every token inside it can
            // break, so the grid shrinks with its column (its 100% width is
            // 100% OF THAT COLUMN). It stays the cell's preference (cellMax).
            if (!breakAnywhereDoc)
            {
                cellMin = Math.Max(cellMin, ps.pendingCellTablesNatW);
                cellMinBrk = Math.Max(cellMinBrk, ps.pendingCellTablesNatW);
            }
            // The cell WANTS the grid's preferred (max-content) width — a
            // percent grid's natural is only its min floors, and sizing the
            // column off that leaves the grid squeezed below its due
            // width.
            cellMax = Math.Max(cellMax,
                Math.Max(ps.pendingCellTablesNatW, ps.pendingCellTablesPrefW));
            ps.pendingCellTablesNatW = 0;
            ps.pendingCellTablesPrefW = 0;
        }
        // Redline percent grids: the pads inset the DRAWN text only — the
        // columns are shares of the content box, and pads in the measure
        // would widen the sheet past the expected width.
        if (ps.cellCssPadPt > 0 && !redlineCells)
        {
            cellMin += ps.cellCssPadPt; cellMinBrk += ps.cellCssPadPt;
            cellMax += ps.cellCssPadPt; if (cellHdr > 0) cellHdr += ps.cellCssPadPt;
        }
        // A declared border-spacing is part of every column's footprint: the band
        // sits OUTSIDE the cell's box, half on each side (the draw insets the
        // border by the same half, so the box itself keeps its content width).
        if (chainSpacingPt > 0)
        {
            cellMin += chainSpacingPt; cellMinBrk += chainSpacingPt;
            cellMax += chainSpacingPt; if (cellHdr > 0) cellHdr += chainSpacingPt;
        }
        double cellMinSerif = 0;
        if (uaSerifMin)
            foreach (var spec in ps.lines)
                foreach (var seg1 in spec.Text.Split(''))
                foreach (var word in seg1.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    cellMinSerif = Math.Max(cellMinSerif,
                        MeasureSerifLine(cellFontSize, word, ps.isHeader || spec.Bold,
                            spec.FontPt > 0 ? spec.FontPt : 0.0));
        var span = Math.Max(1, ps.colSpan);
        // Probe mapping: skip columns still occupied by ROWSPAN cells from rows
        // above (the HTML grid placement rule), so a row following a row-spanning
        // name cell doesn't record its money cells one column early and double
        // the summed min-content with ghost twins. The chain dialect measures
        // through the same mapping (the budget's rowspan-2 label cell shifted
        // its whole second header row one column left).
        if (widenProbe || chainBase is not null || ptCellWidths)
        {
            bool Occupied(int c)
            {
                foreach (var (oc, os, orem) in ps.rowspanOcc)
                    if (orem > 0 && c >= oc && c < oc + os) return true;
                return false;
            }
            while (Occupied(colModel.colCursor)) colModel.colCursor++;
            // remaining counts the spanning row itself (aged at its own row close),
            // so the occupancy covers exactly the rowSpan−1 rows below it.
            if (ps.cellRowSpan > 1) ps.rowspanOcc.Add((colModel.colCursor, span, ps.cellRowSpan));
        }
        // Column footprint = content + cell padding (both sides) + the cell box border the
        // generator draws around it, so the summed natural width matches the rendered grid.
        // The page-widen probe measures BARE content: the widen pass adds no
        // per-column slack for zero-padding zero-border tables.
        // The CSS run dialect measures the browser's own box — cell padding plus the
        // cell's own border — with none of the legacy per-column slack.
        // A chain rule's own horizontal padding is ALREADY inside cellMin/cellMax
        // (added just above), and the table-level padSide is read from the SAME
        // declaration — adding it again bills the pair twice per column and widens
        // the sheet by a full padding pair per column.
        var padSideExtra = chainBase is not null && ps.cellCssPadPt > 0 ? 0 : 2 * padSide;
        // pt-styled fragment: the min-content footprint is exactly word +
        // pads + paragraph margins (already summed into cellMin above) — no
        // legacy slack tail, no border share (the collapsed borders live
        // outside the column boxes).
        var extra = widenProbe || ptCellWidths ? 0
            : padSideExtra + (hasBorder ? 2 * borderWidth : 0)
                + (tightExtras || cssRunFace is not null || dwFormCells ? 0 : 1.5);
        while (colModel.colMinW.Count < colModel.colCursor + span) { colModel.colMinW.Add(0); colModel.colMaxW.Add(0); colModel.colHdrW.Add(0); colModel.colMinBrkW.Add(0); colModel.colDeclW.Add(0); colModel.colMinSerifW.Add(0); }
        if (span == 1)
        {
            if (cellMin + extra > colModel.colMinW[colModel.colCursor]) colModel.colMinW[colModel.colCursor] = cellMin + extra;
            if (uaSerifMin && cellMinSerif + 2 * padSide > colModel.colMinSerifW[colModel.colCursor])
                colModel.colMinSerifW[colModel.colCursor] = cellMinSerif + 2 * padSide;
            if (cellMinBrk + extra > colModel.colMinBrkW[colModel.colCursor]) colModel.colMinBrkW[colModel.colCursor] = cellMinBrk + extra;
            if (cellMax + extra > colModel.colMaxW[colModel.colCursor]) colModel.colMaxW[colModel.colCursor] = cellMax + extra;
            if (cellHdr > 0 && cellHdr + extra > colModel.colHdrW[colModel.colCursor]) colModel.colHdrW[colModel.colCursor] = cellHdr + extra;
            // A cell holding NOTHING but an image declares its width as surely as a
            // `width=` attribute does: replaced content has one size and the column
            // must not stretch past it. Layout tables gutter with exactly this —
            // a `<td><img width="15" height="1"></td>` spacer.
            // (`cellMax` already carries the image's own box, so "no wider than its
            // image" is the test for a cell that holds nothing else.)
            var cellDeclPt = ps.cellImgWidthPt > 0 && cellMax <= ps.cellImgWidthPt + 0.01
                ? Math.Max(ps.cellWidthPt, ps.cellImgWidthPt) : ps.cellWidthPt;
            // pt-styled fragment: the declared width is the CONTENT box —
            // the cell's own pads ride on top of it in the column.
            if (ptCellWidths && cellDeclPt > 0) cellDeclPt += ps.cellCssPadPt;
            if (cellDeclPt > colModel.colDeclW[colModel.colCursor]) colModel.colDeclW[colModel.colCursor] = cellDeclPt;
        }
        else
        {
            // A spanning cell constrains the SUM of its columns — deferred so it does not
            // floor thin spacer columns it merely crosses (which would starve the wide
            // content column of the width a browser gives it).
            colModel.spanConstraints.Add((colModel.colCursor, span, cellMin + extra, cellMax + extra,
                cellHdr > 0 ? cellHdr + extra : 0));
        }
        if (ps.cellWidthPct > 0) ps.rowPctSum += ps.cellWidthPct;
        if (ps.colSpan == 1 && ps.cellWidthPct > 0)
        {
            while (colModel.colPctW.Count <= colModel.colCursor) colModel.colPctW.Add(0);
            if (redlineCells && colModel.colPctW[colModel.colCursor] > 0
                && Math.Abs(colModel.colPctW[colModel.colCursor] - ps.cellWidthPct) > 1)
                colModel.colPctConflict = true;
            if (ps.cellWidthPct > colModel.colPctW[colModel.colCursor]) colModel.colPctW[colModel.colCursor] = ps.cellWidthPct;
        }
        // A SPANNING cell's percent splits evenly over its columns (the
        // amounts grid's 35% period group over three columns) — the
        // over-declared grid dialect's fixed-layout draw resolves each
        // column at its share.
        else if (overDeclaredDraw && ps.colSpan > 1 && ps.cellWidthPct > 0)
        {
            while (colModel.colPctW.Count < colModel.colCursor + span) colModel.colPctW.Add(0);
            var perCol = ps.cellWidthPct / span;
            for (var k = 0; k < span; k++)
                if (perCol > colModel.colPctW[colModel.colCursor + k]) colModel.colPctW[colModel.colCursor + k] = perCol;
        }
        colModel.colCursor += span;
        ps.preWrapPending = false;
        ps.cell = null; ps.lines.Clear(); ps.loneBrBlankLines?.Clear(); ps.line.Clear(); ps.isHeader = false; ps.colSpan = 1; ps.cellRowSpan = 1; ps.alignSet = false; ps.cellWidthPt = 0; ps.cellWidthPct = 0; ps.cellCssPadPt = 0; ps.cellFixedDivPt = 0; ps.cellPadLeftPt = 0; ps.cellImgWidthPt = 0; ps.cellOwnLineHPt = 0; ps.cellPrevPBottomPt = 0; ps.cellPMarginRightPt = 0; ps.cellFirstPMarginTopPt = 0; ps.cellDecorActive = null; ps.lineDecorUnion = null; ps.lineDecorsByIdx = null; ps.lineColorRunsByIdx = null; ps.lineColorRuns = null; ps.cellInputBoxes = null; ps.dwSelectDepth = 0; ps.dwTextareaOpen = false; ps.cellOwnHeightDecl = false;
        ps.underlinedLines = null; ps.lineHadU = ps.uDepth > 0;
        ps.chainTdElem = null; ps.chainOpenElems?.Clear(); chainUnbold.Clear();
        ps.cellChainPadTopPt = 0; ps.cellChainPadBotPt = 0; ps.cellChainColor = null;
        ps.cellVPadZeroBot = false;
        ps.chainBoxOpen?.Clear(); ps.cellBoxSegs?.Clear();
        ps.chainTrafficElem = null; ps.chainTrafficRun = null; ps.pendingCapsule = null;
        ps.openAnchor = null; ps.lineAnchors = null;
    }
}
