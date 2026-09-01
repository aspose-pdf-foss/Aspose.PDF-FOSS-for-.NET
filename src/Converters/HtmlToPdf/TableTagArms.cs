using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>One arm of BuildTableFromHtml's token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void HandleTextToken(TableParseState ps, TableColumnModel colModel, Table table, Token tok, HtmlLoadOptions? options, double cellFontSize, bool dwFormCells, bool fullWidthCjkMin, bool widenProbe, bool redlineCells, bool breakAnywhereDoc, bool cellFontShorthand, List<CssElem>? chainBase, double chainSpacingPt, List<(string Tag, int PrevBoldDepth)> chainUnbold, string? cssBaseFamily, double cssBasePt, string? defaultCellFace, double formGridStrutDropPt, bool hasBorder, double inlineFaceRatio, bool overDeclaredDraw, double padSide, bool uaDocGrid, bool uaSerifMin, bool ptCellWidths, bool bandDialect, double cellLineHeightPt, string? cssRunFace, bool formGridDialect, double formGridStrutPt, bool liftNestedTables, bool tightExtras, bool uaCellBoxes, double borderWidth, double pad, Dictionary<string, Dictionary<string, string>> css, IReadOnlyDictionary<string, Dictionary<string, string>>? docCss, List<CssChainRule>? chainRules, List<CssElem>? cssAncestors, List<byte[]>? inlineSvgs, List<string> nestedHtml, Func<string, bool, Aspose.Pdf.Forms.RadioButtonOptionField>? makeRadio, double availWidthPt, double defaultCellFontPt, Dictionary<string, string> tblStyle, bool docElementGrid, bool pinnedBodyGrid, bool authoredCellChrome, bool chainBorderSeparate, bool elemRuleBorder)
    {
        if (ps.uDepth > 0 && ps.cell is not null && ps.hiddenSubDepth == 0
            && !string.IsNullOrWhiteSpace(tok.Value))
            ps.lineHadU = true;
        if (ps.cell is not null && ps.hiddenSubDepth == 0
            && tok.Value.IndexOf(NestedMark, StringComparison.Ordinal) >= 0)
        {
            foreach (Match nm in Regex.Matches(tok.Value, Regex.Escape(NestedMark) + @"(\d+)\]"))
            {
                var ni = int.Parse(nm.Groups[1].Value);
                if (ni < 0 || ni >= nestedHtml.Count) continue;
                var inner = BuildTableFromHtml(nestedHtml[ni],
                    (ps.cellWidthPt > 0 ? ps.cellWidthPt : availWidthPt) - ps.liStandingIndentPt,
                    out var innerNatW, options, inlineSvgs,
                    docCss ?? css, bandDialect, false, cellLineHeightPt, defaultCellFontPt, tightExtras,
                    liftNestedTables: true,
                    ptCellWidths: ptCellWidths,
                    // The probe's measuring model must survive the nesting: a
                    // grid measured with legacy half-em CJK and no ideograph
                    // breaks reports artifact floors, and those floors — not
                    // the document's real widest content — size the page.
                    fullWidthCjkMin: fullWidthCjkMin,
                    overDeclaredDraw: overDeclaredDraw,
                    // The inner grid inherits this cell's ancestor chain, so
                    // tree-addressed rules keep matching through the nesting.
                    chainRules: chainRules,
                    cssAncestors: chainBase is null ? null : BuildOpenChain(ps, chainBase),
                    makeRadio: makeRadio,
                    // The DataWorks form dialect survives the nesting: an inner
                    // results grid keeps the serif cell face and control boxes.
                    dwFormCells: dwFormCells,
                    defaultCellFace: dwFormCells ? defaultCellFace : null);
                if (inner is not null)
                {
                    // The over-declared fingerprint bubbles to the outer table:
                    // the width probe only sees top-level segments.
                    if (inner.HtmlOverDeclaredGrid) table.HtmlOverDeclaredGrid = true;
                    PushLine(ps, redlineCells, dwFormCells, widenProbe);
                    // The grid's own CSS `margin-top` is real space above it in
                    // the host cell (`<table style="…margin-top:35px">` — the
                    // columns section clears its heading by exactly that band).
                    if (liftNestedTables
                        && Regex.Match(nestedHtml[ni], @"<table\b[^>]*>",
                            RegexOptions.IgnoreCase) is { Success: true } inTag
                        && Regex.Match(inTag.Value,
                            @"(?<![-\w])margin-top\s*:\s*([\d.]+)\s*px",
                            RegexOptions.IgnoreCase) is { Success: true } inMt
                        && double.TryParse(inMt.Groups[1].Value,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var inMtPx)
                        && inMtPx > 0)
                        inner.HtmlMarginTopPt = inMtPx * PxToPt;
                    // The rounded capsule the enclosing div declared paints
                    // behind this grid.
                    if (ps.pendingCapsule is { } cap)
                    {
                        inner.HtmlCapsuleFill = cap.Fill;
                        inner.HtmlCapsuleRadiusPt = cap.RadiusPt;
                        inner.HtmlCapsulePadHPt = cap.PadHPt;
                        inner.HtmlCapsulePadVPt = cap.PadVPt;
                        inner.HtmlCapsuleMarginPt = cap.MarginPt;
                        ps.pendingCapsule = null;
                    }
                    // A grid inside a list item sits ON the item's standing
                    // indent, like every other line of the item.
                    inner.HtmlListIndentPt = ps.liStandingIndentPt;
                    // DataWorks results grid: inner rows pitch on the 1.125-em
                    // line box while the grid total keeps the 16-per-row model
                    // (the last row absorbs the slack) — see DwNestedRowPitchPt.
                    if (dwFormCells && inner.HtmlDwGapReservePt > 0
                        && inner.Rows.Count > 1)
                    {
                        var nR = inner.Rows.Count;
                        for (var ri = 0; ri < nR - 1; ri++)
                            inner.Rows[ri].MinRowHeight = DwNestedRowPitchPt;
                        inner.Rows[nR - 1].MinRowHeight =
                            DwCheckboxRowHPt * nR - DwNestedRowPitchPt * (nR - 1);
                    }
                    (ps.pendingCellTables ??= new List<(Table, int)>()).Add((inner, ps.lines.Count));
                    // The nested grid's natural width IS this cell's content
                    // width — the flattened text lines under-measure it badly,
                    // and the page-widen probe needs the real number.
                    // A capsule wrapper is part of the grid's footprint in its
                    // host column (its padding/spacing/margin band).
                    var capOut = 2 * inner.HtmlCapsuleOutsetHPt;
                    // The host column's min EXCLUDES the widget reserve the
                    // grid still draws — the grid overflows its box by it.
                    var innerHostW = innerNatW + capOut
                        - (dwFormCells ? inner.HtmlDwGapReservePt : 0);
                    if (innerHostW > ps.pendingCellTablesNatW)
                        ps.pendingCellTablesNatW = innerHostW;
                    if (inner.HtmlPreferredWidthPt + capOut > ps.pendingCellTablesPrefW)
                        ps.pendingCellTablesPrefW = inner.HtmlPreferredWidthPt + capOut;
                }
            }
            return;
        }
        // A background-image badge's text is its letter — drawn inside the
        // badge circle by the render pass, never flowed into the line.
        if (ps.chainTrafficRun is not null && ps.cell is not null && ps.hiddenSubDepth == 0)
        {
            var badgeTxt = DecodeEntities(tok.Value).Trim();
            if (badgeTxt.Length > 0) ps.chainTrafficRun.CircleLetter += badgeTxt;
            return;
        }
        if (ps.cell is not null && ps.hiddenSubDepth == 0)
        {
            // <pre> content: split on the SOURCE newlines — each piece is a
            // hard line; the longest line's advance is recorded as the
            // cell's unbreakable width.
            if (ps.preDepth > 0)
            {
                var preText = DecodeEntities(tok.Value);
                var preFirst = true;
                foreach (var preLn in preText.Split('\n'))
                {
                    if (!preFirst)
                    {
                        colModel.preMaxLinePt = Math.Max(colModel.preMaxLinePt, MeasureLine(ps, options, cellFontSize, dwFormCells, fullWidthCjkMin, widenProbe, 
                            ps.line.ToString(),
                            pt: ps.lineFontPt > 0 ? ps.lineFontPt : ps.curFontPt,
                            fam: ps.lineFamily ?? ps.curFamily));
                        if (!ps.lineStyleSet)
                        { ps.lineFontPt = ps.curFontPt; ps.lineFamily = ps.curFamily; ps.lineStyleSet = true; }
                        PushLine(ps, redlineCells, dwFormCells, widenProbe, keepIfBlank: true);
                    }
                    preFirst = false;
                    var prePiece = preLn.TrimEnd('\r');
                    if (prePiece.Length == 0) continue;
                    if (!ps.lineStyleSet)
                    { ps.lineFontPt = ps.curFontPt; ps.lineFamily = ps.curFamily; ps.lineStyleSet = true; }
                    if (!string.IsNullOrWhiteSpace(prePiece))
                    { ps.lineHadText = true; ps.cellPendingBrBlank = false; }
                    ps.line.Append(prePiece);
                }
                return;
            }
            // Under `white-space: pre-wrap` the newline that follows the opening
            // tag is CONTENT, so the box's first line box is empty and the text
            // starts on the second. Collapsing whitespace would eat it.
            if (ps.preWrapPending)
            {
                ps.preWrapPending = false;
                if (tok.Value.StartsWith("\n") || tok.Value.StartsWith("\r"))
                    PushLine(ps, redlineCells, dwFormCells, widenProbe, keepIfBlank: true);
            }
            // First visible content of an htmlPage container span (an &nbsp;
            // counts — it holds a line box) starts on a fresh line.
            if (ps.htmlPageBreakPending)
            {
                var hpVis = false;
                foreach (var hpC in DecodeEntities(tok.Value))
                    if (!char.IsWhiteSpace(hpC) || hpC == ' ') { hpVis = true; break; }
                if (hpVis)
                {
                    ps.htmlPageBreakPending = false;
                    // A pending line that is itself only whitespace/&nbsp;
                    // COLLAPSES at the container boundary instead of pushing —
                    // only an explicit <br> materialises a whitespace box.
                    if (IsAllWhitespace(ps.line)) ps.line.Clear();
                    else if (ps.line.Length > 0) PushLine(ps, redlineCells, dwFormCells, widenProbe);
                }
            }
            // A run whose size differs from the one this line is already bound to
            // opens its OWN line box: the cell paragraph becomes a stack of
            // same-size runs, each wrapped and pitched on its own size. (A browser
            // reflows a mixed-size paragraph continuously; the sizes change on run
            // boundaries, which is where its lines break anyway.)
            if ((cssRunFace is not null || chainBase is not null) && ps.lineStyleSet
                && !string.IsNullOrWhiteSpace(tok.Value)
                && EffectiveChainDisplay(ps) != "inline-block"
                && Math.Abs((ps.curFontPt > 0 ? ps.curFontPt : cellFontSize)
                    - (ps.lineFontPt > 0 ? ps.lineFontPt : cellFontSize)) > 0.01)
            {
                // …unless all the line holds so far is zero-width spaces. Those are
                // invisible and carry no advance, so they are not a line of their
                // own: they ride along on the run that follows, and the size change
                // simply REBINDS this line instead of closing it. (A cell opening
                // "&#8203;<span class=…>" must not spend a whole line box on it.)
                if (ps.line.ToString().Trim(ZeroWidthSpace).Trim().Length == 0)
                    ps.lineStyleSet = false;
                else PushLine(ps, redlineCells, dwFormCells, widenProbe);
            }
            // Bind the currently active inline style to the line when its first
            // real text arrives, so a later close tag can't restyle it. In the
            // form-grid dialect a sized &nbsp; is real content too — the grid's
            // spacer row takes its declared 36pt line box (U+00A0 classes as
            // whitespace, so the plain test skips it).
            // DataWorks: option/textarea inner text is captured for the
            // control box, never flowed as cell text.
            if (dwFormCells && ps.dwSelectDepth > 0)
            {
                if (ps.dwOptSelected) ps.dwOptBuf.Append(DecodeEntities(tok.Value));
                return;
            }
            if (dwFormCells && ps.dwTextareaOpen)
            {
                ps.dwTaBuf.Append(DecodeEntities(tok.Value));
                return;
            }
            if (!ps.lineStyleSet && (!string.IsNullOrWhiteSpace(tok.Value)
                    || (formGridDialect
                        && tok.Value.IndexOf("&nbsp;", StringComparison.OrdinalIgnoreCase) >= 0)))
            { ps.lineFontPt = ps.curFontPt; ps.lineFamily = ps.curFamily; ps.lineStyleSet = true; }
            // DataWorks cells scope a span's colour to ITS OWN text run
            // (the red validation star beside black button captions) —
            // the first-colored-token-wins line colour would paint the
            // whole control line.
            if (ps.lineColor is null && !dwFormCells) ps.lineColor = ps.curColor;
            if (!string.IsNullOrWhiteSpace(tok.Value))
            {
                ps.lineHadText = true;
                ps.cellPendingBrBlank = false;
                if (ps.boldDepth == 0) ps.lineAllBold = false;
                if (ps.italicDepth == 0) ps.lineAllItalic = false;
            }
            var dwAppendText = DecodeEntities(tok.Value);
            if (dwFormCells && ps.curColor is { } dwRunCol && dwAppendText.Length > 0
                && !string.IsNullOrWhiteSpace(dwAppendText))
                (ps.lineColorRuns ??= new List<(int S, int L, Color C)>())
                    .Add((ps.line.Length, dwAppendText.Length, dwRunCol));
            ps.line.Append(dwAppendText);
        }
    }

    /// <summary>One arm of BuildTableFromHtml's token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void HandleCloseTag(TableParseState ps, TableColumnModel colModel, Table table, Token tok, string tag, HtmlLoadOptions? options, double cellFontSize, bool dwFormCells, bool fullWidthCjkMin, bool widenProbe, bool redlineCells, bool breakAnywhereDoc, bool cellFontShorthand, List<CssElem>? chainBase, double chainSpacingPt, List<(string Tag, int PrevBoldDepth)> chainUnbold, string? cssBaseFamily, double cssBasePt, string? defaultCellFace, double formGridStrutDropPt, bool hasBorder, double inlineFaceRatio, bool overDeclaredDraw, double padSide, bool uaDocGrid, bool uaSerifMin, bool ptCellWidths, bool bandDialect, double cellLineHeightPt, string? cssRunFace, bool formGridDialect, double formGridStrutPt, bool liftNestedTables, bool tightExtras, bool uaCellBoxes, double borderWidth, double pad, Dictionary<string, Dictionary<string, string>> css, IReadOnlyDictionary<string, Dictionary<string, string>>? docCss, List<CssChainRule>? chainRules, List<CssElem>? cssAncestors, List<byte[]>? inlineSvgs, List<string> nestedHtml, Func<string, bool, Aspose.Pdf.Forms.RadioButtonOptionField>? makeRadio, double availWidthPt, double defaultCellFontPt, Dictionary<string, string> tblStyle, bool docElementGrid, bool pinnedBodyGrid, bool authoredCellChrome, bool chainBorderSeparate, bool elemRuleBorder)
    {
        // DataWorks control captures end here (the switch below sees only
        // opens): the select emits its chosen option's box, the textarea
        // its content box.
        if (dwFormCells && tag == "option" && ps.dwSelectDepth > 0 && ps.dwOptBuf.Length > 0)
        {
            var dwOpt2 = ps.dwOptBuf.ToString().Trim();
            ps.dwFirstOpt ??= dwOpt2;
            if (ps.dwOptSelected) ps.dwSelectedOpt = dwOpt2;
            ps.dwOptBuf.Clear(); ps.dwOptSelected = false;
        }
        if (dwFormCells && tag == "select" && ps.dwSelectDepth > 0)
        {
            ps.dwSelectDepth = 0;
            if (ps.cell is not null)
            {
                var dwVal2 = (ps.dwSelectedOpt ?? ps.dwFirstOpt ?? "").Trim();
                ps.line.Append(Table.InlineInputChar);
                // The drop-down draws with its arrow-button chrome and a
                // higher seat (reference box 145.7×16.5 up 2.3); the width
                // model keeps the bare option width.
                (ps.cellInputBoxes ??= new()).Add((DwSelectBoxWPt + DwSelectChromeWPt,
                    DwInputBoxHPt, dwVal2, false, DwSelectLiftPt));
                ps.lineHadText = true;
                ps.cellImgWidthPt = Math.Max(ps.cellImgWidthPt, DwSelectBoxWPt + 4);
            }
        }
        if (dwFormCells && tag == "textarea" && ps.dwTextareaOpen)
        {
            ps.dwTextareaOpen = false;
            if (ps.cell is not null)
            {
                if (ps.line.Length > 0) PushLine(ps, redlineCells, dwFormCells, widenProbe, joinNext: true);
                ps.line.Append(Table.InlineInputChar);
                (ps.cellInputBoxes ??= new()).Add((ps.dwTaW, ps.dwTaH, ps.dwTaBuf.ToString().Trim(), true,
                    DwTextareaLiftPt));
                ps.lineHadText = true;
                ps.cellImgWidthPt = Math.Max(ps.cellImgWidthPt, ps.dwTaW + 4);
                PushLine(ps, redlineCells, dwFormCells, widenProbe, joinNext: true);
                ps.rowMinHeightPt = Math.Max(ps.rowMinHeightPt, ps.dwTaH + 4);
            }
        }
        // Structure tags of a table NESTED inside a cell do not drive the
        // outer grid — the nested content flows as the host cell's text,
        // with a line break per nested CELL, so each nested cell keeps its
        // own text run the way it holds its own grid box.
        if (tag == "table")
        {
            ps.tableDepth--;
            if (ps.tableDepth <= 0) CloseRow(ps, colModel, table, options, cellFontSize, dwFormCells, fullWidthCjkMin, breakAnywhereDoc, cellFontShorthand, chainBase, chainSpacingPt, chainUnbold, cssBaseFamily, cssBasePt, defaultCellFace, formGridStrutDropPt, hasBorder, inlineFaceRatio, overDeclaredDraw, padSide, uaDocGrid, widenProbe, uaSerifMin, ptCellWidths, redlineCells, bandDialect, cellLineHeightPt, cssRunFace, formGridDialect, formGridStrutPt, liftNestedTables, tightExtras, uaCellBoxes, borderWidth, pad);
            else if (ps.cell is not null && ps.line.Length > 0) PushLine(ps, redlineCells, dwFormCells, widenProbe);
        }
        else if (tag is "td" or "th")
        {
            if (ps.tableDepth <= 1) CloseCell(ps, colModel, table, options, cellFontSize, dwFormCells, fullWidthCjkMin, breakAnywhereDoc, cellFontShorthand, chainBase, chainSpacingPt, chainUnbold, cssBaseFamily, cssBasePt, defaultCellFace, formGridStrutDropPt, hasBorder, inlineFaceRatio, overDeclaredDraw, padSide, uaDocGrid, widenProbe, uaSerifMin, ptCellWidths, redlineCells, bandDialect, cellLineHeightPt, cssRunFace, formGridDialect, formGridStrutPt, liftNestedTables, tightExtras, uaCellBoxes, borderWidth, pad);
            else if (ps.cell is not null && ps.line.Length > 0) PushLine(ps, redlineCells, dwFormCells, widenProbe);
        }
        else if (tag == "tr")
        {
            if (ps.tableDepth <= 1) CloseRow(ps, colModel, table, options, cellFontSize, dwFormCells, fullWidthCjkMin, breakAnywhereDoc, cellFontShorthand, chainBase, chainSpacingPt, chainUnbold, cssBaseFamily, cssBasePt, defaultCellFace, formGridStrutDropPt, hasBorder, inlineFaceRatio, overDeclaredDraw, padSide, uaDocGrid, widenProbe, uaSerifMin, ptCellWidths, redlineCells, bandDialect, cellLineHeightPt, cssRunFace, formGridDialect, formGridStrutPt, liftNestedTables, tightExtras, uaCellBoxes, borderWidth, pad);
            else if (ps.cell is not null && ps.line.Length > 0) PushLine(ps, redlineCells, dwFormCells, widenProbe);
        }
        else if (tag == "a")
        {
            if (ps.cell is not null && ps.openAnchor is { } oaC)
            {
                var inner = CollapseWs(ps.line.ToString()[oaC.Start..]);
                if (inner.Length > 0) (ps.lineAnchors ??= new()).Add((inner, oaC.Url));
                ps.openAnchor = null;
            }
        }
        else if (tag is "ol" or "ul")
        {
            if (ps.cell is not null && ps.line.Length > 0) PushLine(ps, redlineCells, dwFormCells, widenProbe);
            if (ps.listNesting.Count > 0) ps.listNesting.RemoveAt(ps.listNesting.Count - 1);
            ps.liStandingIndentPt = ListItemIndentPt * ps.listNesting.Count;
            // UA margin-block-end of a TOP-LEVEL list closing mid-cell: one
            // line box below the last item, the twin of the open-side margin.
            if (liftNestedTables && ps.cell is not null && ps.listNesting.Count == 0
                && ps.lines.Count > 0)
            {
                if (!ps.lineStyleSet) { ps.lineFontPt = ps.curFontPt; ps.lineFamily = ps.curFamily; }
                PushLine(ps, redlineCells, dwFormCells, widenProbe, keepIfBlank: true);
            }
        }
        else if (tag == "li")
        {
            if (ps.cell is not null && ps.line.Length > 0) PushLine(ps, redlineCells, dwFormCells, widenProbe);
        }
        else if (tag is "strong" or "b")
        {
            if (ps.boldDepth > 0)
            {
                ps.boldDepth--;
                // Form-grid: the bold run CLOSES here - mark the boundary so
                // the tail of the line returns to the regular face.
                if (formGridDialect && ps.lineRunMarks is not null)
                    ps.lineRunMarks.Add((ps.line.Length, ps.boldDepth > 0));
                if (widenProbe && ps.cell is not null) ps.line.Append('\uE001');
            }
        }
        else if (tag is "sup" or "sub")
        {
            // Probe: close a superscript run (measured at 85% of the line size).
            if (widenProbe && ps.cell is not null) ps.line.Append('\uE003');
        }
        else if (tag == "pre")
        {
            if (ps.preDepth > 0 && --ps.preDepth == 0 && ps.cell is not null)
            {
                colModel.preMaxLinePt = Math.Max(colModel.preMaxLinePt, MeasureLine(ps, options, cellFontSize, dwFormCells, fullWidthCjkMin, widenProbe, ps.line.ToString(),
                    pt: ps.lineFontPt > 0 ? ps.lineFontPt : ps.curFontPt,
                    fam: ps.lineFamily ?? ps.curFamily));
                if (ps.line.Length > 0) PushLine(ps, redlineCells, dwFormCells, widenProbe);
                // restore the pre's own style push
                for (var k = ps.styleStack.Count - 1; k >= 0; k--)
                    if (ps.styleStack[k].Tag == "pre")
                    {
                        ps.curFontPt = ps.styleStack[k].PrevPt;
                        ps.curFamily = ps.styleStack[k].PrevFamily;
                        ps.curColor = ps.styleStack[k].PrevColor;
                        ps.styleStack.RemoveAt(k);
                        break;
                    }
            }
        }
        else if (tag is "p" or "span" or "font" or "label" or "div" or "h1" or "h2")
        {
            // A closing heading bar: its box segment records the line about
            // to push (the segment index is the PUSHED line's), then the
            // line closes — block semantics.
            if (tag is "h1" or "h2" && ps.cell is not null && chainBase is not null)
            {
                if (ps.chainOpenElems is { Count: > 0 })
                    for (var k = ps.chainOpenElems.Count - 1; k >= 0; k--)
                        if (ps.chainOpenElems[k].Tag == tag)
                        {
                            var hPopped = ps.chainOpenElems[k];
                            ps.chainOpenElems.RemoveAt(k);
                            ChainBoxCloseMaybe(ps, hPopped);
                            break;
                        }
                if (ps.line.Length > 0) PushLine(ps, redlineCells, dwFormCells, widenProbe);
            }
            // A closing paragraph ends its line; closing style tags restore the
            // enclosing style context. Band dialect: a whitespace-only <p>
            // (the styled &nbsp; spacer idiom) keeps its line box. A <div>
            // reaches here only when the chain pass pushed an entry for it —
            // nothing else stacks divs, so the scan finds nothing otherwise.
            if (tag == "p" && ps.cell is not null && ps.line.Length > 0)
            {
                var pBlank = bandDialect && IsAllWhitespace(ps.line)
                    && (ps.lineFontPt > 0 || ps.curFontPt > 0);
                if (pBlank && ps.lineFontPt <= 0) ps.lineFontPt = ps.curFontPt;
                PushLine(ps, redlineCells, dwFormCells, widenProbe, keepIfBlank: pBlank, joinNext: true);
            }
            for (var k = ps.styleStack.Count - 1; k >= 0; k--)
                if (ps.styleStack[k].Tag == tag)
                {
                    ps.curFontPt = ps.styleStack[k].PrevPt; ps.curFamily = ps.styleStack[k].PrevFamily;
                    ps.curColor = ps.styleStack[k].PrevColor;
                    if (ps.styleStack[k].BoldBump && ps.boldDepth > 0) ps.boldDepth--;
                    if (ps.styleStack[k].ItalicBump && ps.italicDepth > 0) ps.italicDepth--;
                    ps.styleStack.RemoveAt(k);
                    ps.cellDecorActive?.RemoveAll(d => d.Depth > ps.styleStack.Count);
                    break;
                }
            // …and closes its chain-ancestor entry (span/font/div opens
            // pushed one whenever the chain pass is active).
            if (ps.chainOpenElems is { Count: > 0 } && tag != "p")
                for (var k = ps.chainOpenElems.Count - 1; k >= 0; k--)
                    if (ps.chainOpenElems[k].Tag == tag)
                    {
                        var poppedElem = ps.chainOpenElems[k];
                        ps.chainOpenElems.RemoveAt(k);
                        ChainBoxCloseMaybe(ps, poppedElem);
                        break;
                    }
            // A font-weight:normal run ends: the enclosing bold resumes.
            if (chainUnbold.Count > 0 && chainUnbold[^1].Tag == tag)
            {
                ps.boldDepth = chainUnbold[^1].PrevBoldDepth;
                chainUnbold.RemoveAt(chainUnbold.Count - 1);
            }
        }
    }

    /// <summary>One arm of BuildTableFromHtml's token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void HandleRowOpen(TableParseState ps, TableColumnModel colModel, Table table, Token tok, string tag, HtmlLoadOptions? options, double cellFontSize, bool dwFormCells, bool fullWidthCjkMin, bool widenProbe, bool redlineCells, bool breakAnywhereDoc, bool cellFontShorthand, List<CssElem>? chainBase, double chainSpacingPt, List<(string Tag, int PrevBoldDepth)> chainUnbold, string? cssBaseFamily, double cssBasePt, string? defaultCellFace, double formGridStrutDropPt, bool hasBorder, double inlineFaceRatio, bool overDeclaredDraw, double padSide, bool uaDocGrid, bool uaSerifMin, bool ptCellWidths, bool bandDialect, double cellLineHeightPt, string? cssRunFace, bool formGridDialect, double formGridStrutPt, bool liftNestedTables, bool tightExtras, bool uaCellBoxes, double borderWidth, double pad, Dictionary<string, Dictionary<string, string>> css, IReadOnlyDictionary<string, Dictionary<string, string>>? docCss, List<CssChainRule>? chainRules, List<CssElem>? cssAncestors, List<byte[]>? inlineSvgs, List<string> nestedHtml, Func<string, bool, Aspose.Pdf.Forms.RadioButtonOptionField>? makeRadio, double availWidthPt, double defaultCellFontPt, Dictionary<string, string> tblStyle, bool docElementGrid, bool pinnedBodyGrid, bool authoredCellChrome, bool chainBorderSeparate, bool elemRuleBorder)
    {
        if (ps.tableDepth > 1)
        {
            if (ps.cell is not null && ps.line.Length > 0) PushLine(ps, redlineCells, dwFormCells, widenProbe);
            return;
        }
        // A hidden row (`<tr style="display:none">` — the empty-state
        // tfoot band of a data grid) is out of the layout entirely: no
        // cells, no height, no column measures. The in-cell hidden check
        // above never sees it because no cell is open at a row boundary.
        if (IsHiddenElement(tag, tok.Attributes, css))
        {
            ps.hiddenSubTag = tag;
            ps.hiddenSubDepth = 1;
            return;
        }
        CloseRow(ps, colModel, table, options, cellFontSize, dwFormCells, fullWidthCjkMin, breakAnywhereDoc, cellFontShorthand, chainBase, chainSpacingPt, chainUnbold, cssBaseFamily, cssBasePt, defaultCellFace, formGridStrutDropPt, hasBorder, inlineFaceRatio, overDeclaredDraw, padSide, uaDocGrid, widenProbe, uaSerifMin, ptCellWidths, redlineCells, bandDialect, cellLineHeightPt, cssRunFace, formGridDialect, formGridStrutPt, liftNestedTables, tightExtras, uaCellBoxes, borderWidth, pad); ps.row = new Row();
        ps.rowFontPt = 0; ps.rowMinHeightPt = 0; ps.rowMinHeightIsContent = false; ps.rowAlign = null;
        ps.rowVAlign = VerticalAlignment.None;
        if (liftNestedTables && tok.Attributes is not null
            && tok.Attributes.TryGetValue("valign", out var trVaAttr))
            ps.rowVAlign = trVaAttr.Trim().ToLowerInvariant() switch
            {
                "top" => VerticalAlignment.Top,
                "middle" or "center" => VerticalAlignment.Center,
                "bottom" => VerticalAlignment.Bottom,
                _ => VerticalAlignment.None,
            };
        // A row's declared fill paints its whole band behind the cells.
        if (tok.Attributes is not null)
        {
            if (tok.Attributes.TryGetValue("style", out var trSt) && trSt is not null
                && Regex.Match(trSt, @"background(?:-color)?\s*:\s*([^;]+)",
                    RegexOptions.IgnoreCase) is { Success: true } trBgm
                && ParseCssColor(trBgm.Groups[1].Value) is { } trBg)
                ps.row.BackgroundColor = trBg;
            else if (tok.Attributes.TryGetValue("bgcolor", out var trBgAttr)
                && ParseCssColor(trBgAttr) is { } trBgA)
                ps.row.BackgroundColor = trBgA;
            // A class rule's fill paints the row band too
            // (`<tr class="colourlightgreen">` — the filing form's
            // section headers). Scoped to the over-declared grid
            // dialect; legacy dialects were calibrated without it.
            if (ps.row.BackgroundColor is null && fullWidthCjkMin
                && tok.Attributes.TryGetValue("class", out var trBandCls))
                foreach (var cn in trBandCls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!css.TryGetValue("." + cn, out var trRule))
                        docCss?.TryGetValue("." + cn, out trRule);
                    if (trRule is not null
                        && trRule.TryGetValue("background-color", out var trCbg)
                        && ParseCssColor(trCbg) is { } trCbgc)
                    {
                        ps.row.BackgroundColor = trCbgc;
                        break;
                    }
                }
        }
        // ALIGN on the row is the default for every cell in it that
        // declares none of its own.
        if (liftNestedTables && tok.Attributes is not null
            && tok.Attributes.TryGetValue("align", out var trAl))
            ps.rowAlign = ParseAlignAttr(trAl);
        // A row's CSS height (a `tr {height:28px}` rule, a `.medium` class
        // variant, or an inline style) is a MINIMUM: content-driven rows
        // still grow past it, matching the browser's table model. The rule
        // usually lives in the document stylesheet, not the segment.
        if (TryGetCssLength(css, "tr", "height", out var trh) && trh > 0)
            ps.rowMinHeightPt = trh;
        else if (docCss is not null && TryGetCssLength(docCss, "tr", "height", out var dtrh) && dtrh > 0)
            ps.rowMinHeightPt = dtrh;
        // An INLINE height on the row is the same minimum
        // (`<tr style="white-space:nowrap;height:30px">` — the rental
        // question row) — over-declared grid dialect only.
        if ((overDeclaredDraw || ptCellWidths) && tok.Attributes is not null
            && tok.Attributes.TryGetValue("style", out var trHSt) && trHSt is not null
            && Regex.Match(trHSt, @"(?<![-\w])height\s*:\s*([\d.]+)\s*(px|pt)",
                RegexOptions.IgnoreCase) is { Success: true } trHm
            && double.TryParse(trHm.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var trHPx))
        {
            var trHPt2 = trHm.Groups[2].Value.Equals("px",
                StringComparison.OrdinalIgnoreCase) ? trHPx * PxToPt : trHPx;
            if (trHPt2 > ps.rowMinHeightPt) ps.rowMinHeightPt = trHPt2;
        }
        // Separate element-rule borders ride ON the CSS row height
        // (see elemRuleBorder above).
        if (elemRuleBorder && ps.rowMinHeightPt > 0)
            ps.rowMinHeightPt += borderWidth;
        if (tok.Attributes is not null && tok.Attributes.TryGetValue("class", out var trCls))
            foreach (var cls in trCls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (TryGetCssLength(css, "tr." + cls, "height", out var trch) && trch > 0)
                    ps.rowMinHeightPt = trch;
                else if (docCss is not null && TryGetCssLength(docCss, "tr." + cls, "height", out var dtrch) && dtrch > 0)
                    ps.rowMinHeightPt = dtrch;
            }
        if (tok.Attributes is not null && tok.Attributes.TryGetValue("style", out var trStyle))
        {
            var trfs = Regex.Match(trStyle, @"font-size\s*:\s*([^;""']+)", RegexOptions.IgnoreCase);
            if (trfs.Success && TryParseLength(trfs.Groups[1].Value.Trim(), out var trfp)) ps.rowFontPt = trfp;
            var trhm = Regex.Match(trStyle, @"height\s*:\s*([^;""']+)", RegexOptions.IgnoreCase);
            if (trhm.Success && TryParseLength(trhm.Groups[1].Value.Trim(), out var trhp) && trhp > 0)
                ps.rowMinHeightPt = trhp;
        }
    }

}
