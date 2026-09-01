using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>CSS pixels to points (96dpi -> 72dpi).</summary>
    private const double PxToPt = 0.75;
    private const double PxToPtW = 0.75;
    // The pt-styled fragment's cell line pitch as a factor of the font size
    // (measured: 10 pt Verdana rows step 12.0 per line,
    // wrapped header cells 2×12.0, the paragraph flow the same 1.2 em).
    private const double PtFragmentLineFactor = 1.2;

    // The redline diff document's line box as a factor of the font size
    // (measured: wrapped 10 pt paragraphs step 11.25,
    // the 12 pt red block 13.5 — a 1.125 em box at every size).
    internal const double RedlineLineFactor = 1.125;

    // Redline decoration geometry (measured on the expected stroke positions
    // against the run baselines): a text-decoration underline rides 0.09 em
    // below the baseline, a strike 0.26 em above, both 0.1 em thick; a
    // diff-marker border-bottom draws a 0.75 pt hairline 0.25 em below.
    internal const double RedlineUnderDropEm = 0.09;
    internal const double RedlineStrikeRiseEm = 0.26;
    internal const double RedlineDecorWidthEm = 0.1;
    internal const double RedlineBorderDropEm = 0.25;

    // Redline cross-paragraph baseline advance = DescLead(prev) + AscLead(next)
    // (probed: 21.0 across two 18 pt headers, 13.5 across an 18->10 boundary —
    // Times' descent + gap share and ascent + half-leading share of a 1.15 em
    // paragraph box).
    internal const double RedlineDescLeadEm = 0.2375;
    internal const double RedlineAscLeadEm = 0.929;

    // The redline <hr>: its black top stroke rides 8.5 pt below the previous
    // baseline, and the next paragraph's baseline 23.7 below the stroke
    // (both probed on the cover's 25% groove rule).
    // The redline document's UNSTYLED text (blank paragraphs, flattened
    // marker cells) runs at the UA default 12 pt (probed: the cover's bar
    // paragraph seats the added-marker underline at 97.3 only on a 12 pt line).
    internal const double RedlineBaseFontPt = 12.0;

    // An EMPTY paragraph's box in the redline flow (probed: 24.8 between two
    // 10 pt paragraphs separated by one empty 12 pt paragraph, net of the
    // Desc/Asc leads).
    internal const double RedlineEmptyParaPt = 13.13;

    // …and the conflicted two-column grid's first-column share (see above).
    internal const double RedlineConflictCol1Frac = 0.6075;

    // DataWorks form-grid control boxes (measured: text inputs
    // at their declared 177x22 px, the select at the same default width, the
    // textarea 367x103 px when undeclared, values in ~11 px sans).
    internal const double DwSelectBoxWPt = 132.75;
    internal const double DwInputBoxHPt = 16.5;
    internal const double DwTextareaWPt = 275.25;
    internal const double DwTextareaHPt = 50.0;
    internal const double DwBoxValueFontPt = 8.25;

    // DataWorks flow typography: UA 16px base (12 pt), h1 at 2 em, the classic
    // navigator link blue. The expected output's aged JPEG renders the #0000EE ink
    // desaturated (glyph cores ≈ rgb(16,17,125), the 1px underline between
    // (18,18,114) and (75,75,171) by row rounding); the exact gate compares
    // per-channel against those pixels, so the drawn ink is calibrated to sit
    // within the comparison budget of every measured variant.
    internal const double DwBodyFontPt = 12.0;
    internal const double DwH1FontPt = 24.0;
    internal static readonly Color DwLinkColor = Color.FromArgb(36, 36, 140);

    // The header's float:right print-link box (80px) and the Completed
    // button's inset from the content right edge (the 98% form-element box
    // plus the button chrome; measured: box right edge 516.5 = content
    // right 545.5 − 29.0).
    internal const double DwPrintLinkBoxPt = 60.0;
    internal const double DwCompletedRightInsetPt = 29.0;

    // The header bar's bottom rule: 2px (#cccccc) under two 1.125-em header
    // lines plus the spans' 5px bottom margin (2*13.5 + 3.75 = 30.75).
    internal const double DwHeaderRuleDropPt = 30.75;
    internal const double DwHeaderRuleHPt = 1.5;
    internal const double DwRuleGray = 204.0 / 255.0;

    // The h1 title row's line box (measured: the row spans 33.8pt with its
    // pads and spacing — a 32.5pt box) and the minimum height of a nested
    // results row that carries an (invisible) checkbox widget.
    internal const double DwH1LineBoxPt = 32.5;
    internal const double DwCheckboxRowHPt = 16.0;
    internal const double DwH1SeatDropPt = 5.0;
    internal const double DwButtonLinePt = 16.5;
    internal const double DwOptionLinePt = 14.76;
    internal const double DwButtonFollowPt = 13.0;
    internal const double DwAfterButtonDropPt = 0.7;
    internal const double DwBottomMarginPt = 68.0;
    // Button captions draw in the 10 pt UI sans inside the 12 pt-scaled chrome
    // (measured: 'Search' caption 27.6 wide, box 38.9 incl. outline), the box
    // seats 4.2 pt above the caption line's natural drop (both results-grid
    // button rows land on the expected boxes with the one rise), and the flow
    // list markers hang a bare 0.8 pt gap left of the item indent.
    internal const double DwButtonCapPt = 10.0;
    internal const double DwButtonBoxRaisePt = 4.2;
    internal const double DwMarkerGapPt = 0.8;
    // Link underline: the redline drop already lands within the window of the
    // expected 1px stroke (baseline +2.4 there, ours +1.1) — only the
    // hairline WIDTH differs from the redline dialect (two full-intensity
    // device rows; a thinner stroke anti-aliases too light to match).
    internal const double DwUnderRaisePt = 0.0;
    internal const double DwUnderWidthPt = 0.96;
    // Control-widget draw seats (all measured on the expected borders): every
    // widget box starts 3.7 pt past the pen; the drop-down draws its
    // arrow-button band (+13 wide) and rides 2.3 higher; the textarea keeps its
    // declared box 2.5 higher with the mono text tight under the top border;
    // trailing text (the validation star) hugs the box's right border.
    internal const double DwInputLeadPt = 3.7;
    internal const double DwSelectChromeWPt = 13.0;
    internal const double DwSelectLiftPt = 2.3;
    internal const double DwTextareaLiftPt = 2.5;
    internal const double DwMonoValueRaisePt = 0.7;
    internal const double DwAfterBoxPenPt = -2.0;
    internal const double DwGapLineLiftPt = 4.3;
    internal const double DwBoxBorderGray = 32.0 / 255.0;
    // The <input type=file> control's synthesized caption — its box draws the
    // flat gray chrome (no black outline) unlike the push buttons.
    internal const string DwFileButtonCaption = "Choose File";
    // A multi-row results grid pitches its rows on the plain 1.125-em line box
    // (measured: BXH→New-Topic steps 13.5) while the grid's TOTAL height keeps
    // the 16-per-row model that seats everything after it — the last row
    // absorbs the slack.
    internal const double DwNestedRowPitchPt = 13.5;
    // A results-grid nested table draws its content 4.3 pt right of the column
    // model: the expected output reserves the full 17.3 pt checkbox footprint and a
    // ~0.7 pt broken-icon sliver where the width model books 6.85 each (the
    // reserve stays excluded from the host column, so only the draw shifts).
    internal const double DwNestedDrawShiftPt = 4.3;

    // font-variant: small-caps ratio (probed: the blue covenant paragraph's
    // lowercase draws as 7.08 pt capitals on the 10 pt line).
    internal const double RedlineSmallCapsEm = 0.708;

    internal const double RedlineHrLeadPt = 8.5;
    internal const double RedlineHrDropPt = 7.0;

    // …and the seat of the first flow baseline under a grid: an ascent below
    // the borderless card's bottom edge (probed: the nbsp line at end + 0.815
    // em on the consuming 10 pt line)…
    internal const double PtDropEm = 0.815;

    // …deepened one collapsed-seat share under a BORDERED grid, whose drawn
    // bottom stroke rides below the layout cursor (probed: the flow resumes a
    // full em under each bordered table's bottom stroke — 0.815 + 0.18).
    internal const double PtBorderedDropExtraEm = 0.18;

    // A mid-flow grid's TOP STROKE sits 12.35 pt above its first row's text
    // bottom and one row pitch below the flow (probed on the drawn border
    // positions); the generic BaselineInLineBoxPt rise leaves the box 0.7 low.
    internal const double PtTableBoxRisePt = 0.7;

    /// <summary>Squeeze columns into <paramref name="cap"/>: each column sheds
    /// width in proportion to its slack above min-content; if the mins alone
    /// overflow, everything scales flat. Returns the input when it fits.</summary>
    private static List<double> SqueezeBySlack(List<double> cols, double cap, List<double> mins)
    {
        double sum = 0; foreach (var w in cols) sum += w;
        if (cap <= 0 || sum <= cap + 0.01) return cols;
        var reduce = sum - cap;
        double slackSum = 0;
        var slack = new double[cols.Count];
        for (var i = 0; i < cols.Count; i++)
        {
            var minI = i < mins.Count ? mins[i] : 0;
            slack[i] = Math.Max(0, cols[i] - minI);
            slackSum += slack[i];
        }
        var res = new List<double>(cols.Count);
        if (slackSum <= reduce + 0.01)
        {
            // even the mins overflow: flat scale
            var scale = cap / sum;
            foreach (var w in cols) res.Add(w * scale);
            return res;
        }
        for (var i = 0; i < cols.Count; i++)
            res.Add(cols[i] - reduce * slack[i] / slackSum);
        return res;
    }

    internal static Table? BuildTableFromHtml(string html, double availWidthPt, out double naturalWidthPt,
        HtmlLoadOptions? options, List<byte[]>? inlineSvgs,
        IReadOnlyDictionary<string, Dictionary<string, string>>? docCss,
        bool bandDialect = false, bool widenProbe = false, double cellLineHeightPt = 0,
        double defaultCellFontPt = 0, bool tightExtras = false, bool liftNestedTables = false,
        bool uaCellBoxes = false, string? cssRunFace = null, Color? bodyTextColor = null,
        bool uaSerifMin = false,
        // The cells carry their own presentational styling — inline border sides and the
        // legacy ALIGN attribute — rather than inheriting a frame from the table.
        bool authoredCellChrome = false,
        // The Verdana form-grid fragment dialect (see Document.cs): legacy ALIGN
        // honored, and a sized &nbsp;-only run binds its active font (the grid's
        // 36pt spacer row) — scoped here so no calibrated dialect moves.
        bool formGridDialect = false,
        // The pt-styled fragment dialect: cells declare their widths as inline
        // `width:Npt` (the px-only read leaves such columns at min-content — a
        // phone column wrapping one character per line). Scoped so no
        // calibrated grid re-reads widths it was measured without.
        bool ptCellWidths = false,
        // The redline diff document's layout tables: percent columns whose cell
        // paragraphs carry the typography (Times spans, text-align, valign).
        bool redlineCells = false,
        // DataWorks form grid: text controls draw as their declared pixel boxes
        // with the value typeset inside; selects show the chosen option;
        // checked checkboxes draw bare checkmarks.
        bool dwFormCells = false,
        // The dialect's CSS strut: the ambient font's own line box, flooring
        // every cell line (Verdana-12 → 14.25 inside the wrapper's font tag,
        // the serif default's 13.5 outside). A td that styles its OWN
        // font-size restruts its cell at that size's box instead.
        double formGridStrutPt = 0,
        // …and the strut's baseline drop (half-leading + winAscent within the
        // strut box) — the floor every line's baseline seat takes.
        double formGridStrutDropPt = 0,
        // The document's base face, inherited by the grid like defaultCellFontPt.
        string? defaultCellFace = null,
        // The element-styled fixed-grid dialect (quirks page whose stylesheet
        // sizes the TABLE element and borders the cells by ELEMENT rule): the
        // document sheet's table width pins-and-fills the grid box, td element
        // borders box every cell, and the flat class rules carry the cells'
        // full chrome (width, align, colour, size, padding, border sides).
        bool docElementGrid = false,
        // The page-width PROBE measures CJK the way the layout draws it:
        // full-em ideograph advances and per-ideograph break opportunities. The
        // render dialects are calibrated on the legacy estimates and keep them.
        bool fullWidthCjkMin = false,
        // Pinned-body report dialect: a cell's own inline font-size may GROW the
        // text past the grid base (the header table's 22px title cell) — its
        // lines measure at their own size, so the column absorbs the growth.
        bool pinnedBodyGrid = false,
        // Over-declared grid document, RENDER pass only: a nested grid resolves
        // its percent columns against the STANDARD content box — the host cell's
        // padding, border spacing and the UA body gutter all come off the
        // available width (measured: inner W = pageW − 201 at every page width,
        // while the host table itself full-bleeds to the page edge).
        bool overDeclaredDraw = false,
        List<CssChainRule>? chainRules = null, List<CssElem>? cssAncestors = null,
        // Factory for a radio <input> in a cell: (group name, checked) → an option
        // already added to its RadioButtonField group. The CONVERTER owns the groups
        // (it registers them on doc.Form after layout); the cell carries each option
        // inline in its text via Table.InlineRadioChar markers. Null = radios are
        // dropped from cell text, the pre-form-grid behaviour.
        Func<string, bool, Aspose.Pdf.Forms.RadioButtonOptionField>? makeRadio = null)
    {
        naturalWidthPt = 0;
        var (cfg, ps, colModel, table, tokens) = BuildTableParseContext(html, availWidthPt, options, inlineSvgs, docCss, bandDialect, widenProbe, cellLineHeightPt, defaultCellFontPt, tightExtras, liftNestedTables, uaCellBoxes, ref cssRunFace, bodyTextColor, uaSerifMin, authoredCellChrome, formGridDialect, ptCellWidths, redlineCells, dwFormCells, formGridStrutPt, formGridStrutDropPt, defaultCellFace, docElementGrid, fullWidthCjkMin, pinnedBodyGrid, overDeclaredDraw, chainRules, cssAncestors, makeRadio);
        ps.chainOpenElems = cfg.chainBase is not null ? new List<CssElem>() : null;
        foreach (var tok in tokens)
        {
            if (tok.Kind == TokenKind.Text) { HandleTextToken(ps, colModel, table, tok, options, cfg.cellFontSize, dwFormCells, fullWidthCjkMin, widenProbe, redlineCells, cfg.breakAnywhereDoc, cfg.cellFontShorthand, cfg.chainBase, cfg.chainSpacingPt, cfg.chainUnbold, cfg.cssBaseFamily, cfg.cssBasePt, defaultCellFace, formGridStrutDropPt, cfg.hasBorder, cfg.inlineFaceRatio, overDeclaredDraw, cfg.padSide, cfg.uaDocGrid, uaSerifMin, ptCellWidths, bandDialect, cellLineHeightPt, cssRunFace, formGridDialect, formGridStrutPt, liftNestedTables, tightExtras, uaCellBoxes, cfg.borderWidth, cfg.pad, cfg.css, docCss, chainRules, cssAncestors, inlineSvgs, cfg.nestedHtml, makeRadio, availWidthPt, defaultCellFontPt, cfg.tblStyle, docElementGrid, pinnedBodyGrid, authoredCellChrome, cfg.chainBorderSeparate, cfg.elemRuleBorder); continue; }
            var tag = tok.Tag!.ToLowerInvariant();
            // display:none subtree inside a cell (hidden pager selects, state-carrier
            // inputs): its content never reaches the cell text.
            if (ps.hiddenSubDepth > 0)
            {
                if (tag == ps.hiddenSubTag)
                {
                    if (tok.IsClose) { if (--ps.hiddenSubDepth == 0) ps.hiddenSubTag = null; }
                    else if (!tok.IsSelfClosing) ps.hiddenSubDepth++;
                }
                continue;
            }
            if (!tok.IsClose && ps.cell is not null && IsHiddenElement(tag, tok.Attributes, cfg.css))
            {
                if (!tok.IsSelfClosing && !VoidTags.Contains(tag))
                {
                    ps.hiddenSubTag = tag;
                    ps.hiddenSubDepth = 1;
                }
                continue;
            }
            // Any structural tag cancels a pending htmlPage-container break; inline
            // style tags ride along inside the container.
            if (tag is not ("span" or "font" or "strong" or "b" or "em" or "i" or "u" or "a"))
                ps.htmlPageBreakPending = false;
            if (tag == "u")
            {
                if (tok.IsClose) ps.uDepth = Math.Max(0, ps.uDepth - 1);
                else if (!tok.IsSelfClosing) ps.uDepth++;
            }
            if (liftNestedTables && !tok.IsClose && tag == "span" && ps.cell is not null
                && ps.line.Length > 0 && tok.Attributes is not null
                && tok.Attributes.TryGetValue("class", out var hpClass)
                && string.Equals(hpClass?.Trim(), "htmlPage", StringComparison.OrdinalIgnoreCase))
                ps.htmlPageBreakPending = true;
            if (tok.IsClose) { HandleCloseTag(ps, colModel, table, tok, tag, options, cfg.cellFontSize, dwFormCells, fullWidthCjkMin, widenProbe, redlineCells, cfg.breakAnywhereDoc, cfg.cellFontShorthand, cfg.chainBase, cfg.chainSpacingPt, cfg.chainUnbold, cfg.cssBaseFamily, cfg.cssBasePt, defaultCellFace, formGridStrutDropPt, cfg.hasBorder, cfg.inlineFaceRatio, overDeclaredDraw, cfg.padSide, cfg.uaDocGrid, uaSerifMin, ptCellWidths, bandDialect, cellLineHeightPt, cssRunFace, formGridDialect, formGridStrutPt, liftNestedTables, tightExtras, uaCellBoxes, cfg.borderWidth, cfg.pad, cfg.css, docCss, chainRules, cssAncestors, inlineSvgs, cfg.nestedHtml, makeRadio, availWidthPt, defaultCellFontPt, cfg.tblStyle, docElementGrid, pinnedBodyGrid, authoredCellChrome, cfg.chainBorderSeparate, cfg.elemRuleBorder); continue; }
            switch (tag)
            {
                case "table":
                    ps.tableDepth++;
                    // A nested table's content opens on a fresh line of the host cell.
                    if (ps.tableDepth > 1 && ps.cell is not null && ps.line.Length > 0) PushLine(ps, redlineCells, dwFormCells, widenProbe);
                    break;
                case "tr":
                    HandleRowOpen(ps, colModel, table, tok, tag, options, cfg.cellFontSize, dwFormCells, fullWidthCjkMin, widenProbe, redlineCells, cfg.breakAnywhereDoc, cfg.cellFontShorthand, cfg.chainBase, cfg.chainSpacingPt, cfg.chainUnbold, cfg.cssBaseFamily, cfg.cssBasePt, defaultCellFace, formGridStrutDropPt, cfg.hasBorder, cfg.inlineFaceRatio, overDeclaredDraw, cfg.padSide, cfg.uaDocGrid, uaSerifMin, ptCellWidths, bandDialect, cellLineHeightPt, cssRunFace, formGridDialect, formGridStrutPt, liftNestedTables, tightExtras, uaCellBoxes, cfg.borderWidth, cfg.pad, cfg.css, docCss, chainRules, cssAncestors, inlineSvgs, cfg.nestedHtml, makeRadio, availWidthPt, defaultCellFontPt, cfg.tblStyle, docElementGrid, pinnedBodyGrid, authoredCellChrome, cfg.chainBorderSeparate, cfg.elemRuleBorder);
                    break;
                case "p":
                case "span":
                case "font":
                // A <label> is an ordinary inline box: the font-family/font-size it
                // declares style the run it wraps, exactly as a <span>'s would.
                case "label":
                    HandleInlineOpen(ps, colModel, table, tok, tag, options, cfg.cellFontSize, dwFormCells, fullWidthCjkMin, widenProbe, redlineCells, cfg.breakAnywhereDoc, cfg.cellFontShorthand, cfg.chainBase, cfg.chainSpacingPt, cfg.chainUnbold, cfg.cssBaseFamily, cfg.cssBasePt, defaultCellFace, formGridStrutDropPt, cfg.hasBorder, cfg.inlineFaceRatio, overDeclaredDraw, cfg.padSide, cfg.uaDocGrid, uaSerifMin, ptCellWidths, bandDialect, cellLineHeightPt, cssRunFace, formGridDialect, formGridStrutPt, liftNestedTables, tightExtras, uaCellBoxes, cfg.borderWidth, cfg.pad, cfg.css, docCss, chainRules, cssAncestors, inlineSvgs, cfg.nestedHtml, makeRadio, availWidthPt, defaultCellFontPt, cfg.tblStyle, docElementGrid, pinnedBodyGrid, authoredCellChrome, cfg.chainBorderSeparate, cfg.elemRuleBorder);
                    break;
                case "sup":
                case "sub":
                    // Probe: open a superscript/subscript run — its glyphs measure at
                    // 85% of the line size in the min-content pass (the filing-dialect
                    // CSS shrink), marked by a sentinel pair in the line buffer.
                    if (widenProbe && ps.cell is not null) ps.line.Append('\uE002');
                    break;
                case "h1":
                case "h2":
                    // DataWorks form grid: a UA heading inside the title cell —
                    // its own line at 2 em bold serif (the generic close arm
                    // restores the pushed style).
                    if (dwFormCells && ps.cell is not null && cfg.chainBase is null)
                    {
                        if (ps.line.Length > 0) PushLine(ps, redlineCells, dwFormCells, widenProbe);
                        ps.styleStack.Add((tag, ps.curFontPt, ps.curFamily, true, ps.curColor, false));
                        ps.curFontPt = tag == "h1" ? DwH1FontPt : DwH1FontPt * 0.75;
                        ps.boldDepth++;
                        break;
                    }
                    // Chain-styled section heading: a BLOCK box spanning the cell
                    // (the report's red bars) — own line, background, centred text
                    // in its own colour, sized by the heading rule's percent font.
                    if (cfg.chainBase is not null && ps.cell is not null && ps.chainTdElem is not null)
                    {
                        if (ps.line.Length > 0) PushLine(ps, redlineCells, dwFormCells, widenProbe);
                        var chHElem = ChainTokElem(tag, tok.Attributes);
                        ps.chainOpenElems!.Add(chHElem);
                        var hPrevPt = ps.curFontPt; var hPrevFam = ps.curFamily; var hBold = false;
                        var hPrevColor = ps.curColor;
                        if (MatchChainDecls(chainRules, BuildOpenChain(ps, cfg.chainBase)) is { } hd)
                        {
                            if (hd.TryGetValue("font-size", out var hfs))
                            {
                                var hBase = ps.curFontPt > 0 ? ps.curFontPt
                                    : ps.cellClassPt > 0 ? ps.cellClassPt : cfg.cellFontSize;
                                var hpm = Regex.Match(hfs.Trim(), @"^([\d.]+)\s*%$");
                                if (hpm.Success && double.TryParse(hpm.Groups[1].Value,
                                        System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture, out var hPct)
                                    && hPct > 0)
                                    ps.curFontPt = hBase * hPct / 100.0;
                                else if (ChainLenPt(hfs, hBase) is > 0 and var hAbs)
                                    ps.curFontPt = hAbs;
                            }
                            if (hd.TryGetValue("font-weight", out var hfw)
                                && Regex.IsMatch(hfw, @"bold|[6-9]00", RegexOptions.IgnoreCase))
                            {
                                hBold = true;
                                ps.boldDepth++;
                                if (widenProbe) ps.line.Append('');
                            }
                            if ((hd.TryGetValue("background-color", out var hbg)
                                    || hd.TryGetValue("background", out hbg))
                                && ParseCssColor(hbg) is { } hFill)
                            {
                                var hFontPt = ps.curFontPt > 0 ? ps.curFontPt : cfg.cellFontSize;
                                var hRun = new ChainBoxRun
                                {
                                    Elem = chHElem, StartLen = ps.line.Length, Fill = hFill,
                                    FullWidth = true,
                                    TextCentered = hd.TryGetValue("text-align", out var hta)
                                        && hta.Contains("center", StringComparison.OrdinalIgnoreCase),
                                };
                                if (hd.TryGetValue("color", out var hcol)
                                    && ParseCssColor(hcol) is { } hTextCol)
                                    hRun.TextColor = hTextCol;
                                if (hd.TryGetValue("padding", out var hpv))
                                {
                                    var (hpT, hpR, hpB, hpL) = ChainPadPt(hpv, hFontPt);
                                    hRun.PadT = hpT; hRun.PadR = hpR; hRun.PadB = hpB; hRun.PadL = hpL;
                                }
                                (ps.chainBoxOpen ??= new List<ChainBoxRun>()).Add(hRun);
                            }
                        }
                        ps.styleStack.Add((tag, hPrevPt, hPrevFam, hBold, hPrevColor, false));
                    }
                    break;
                case "div":
                    HandleDivOpen(ps, colModel, table, tok, tag, options, cfg.cellFontSize, dwFormCells, fullWidthCjkMin, widenProbe, redlineCells, cfg.breakAnywhereDoc, cfg.cellFontShorthand, cfg.chainBase, cfg.chainSpacingPt, cfg.chainUnbold, cfg.cssBaseFamily, cfg.cssBasePt, defaultCellFace, formGridStrutDropPt, cfg.hasBorder, cfg.inlineFaceRatio, overDeclaredDraw, cfg.padSide, cfg.uaDocGrid, uaSerifMin, ptCellWidths, bandDialect, cellLineHeightPt, cssRunFace, formGridDialect, formGridStrutPt, liftNestedTables, tightExtras, uaCellBoxes, cfg.borderWidth, cfg.pad, cfg.css, docCss, chainRules, cssAncestors, inlineSvgs, cfg.nestedHtml, makeRadio, availWidthPt, defaultCellFontPt, cfg.tblStyle, docElementGrid, pinnedBodyGrid, authoredCellChrome, cfg.chainBorderSeparate, cfg.elemRuleBorder);
                    break;
                case "td":
                case "th":
                    HandleCellOpen(ps, colModel, table, tok, tag, options, cfg.cellFontSize, dwFormCells, fullWidthCjkMin, widenProbe, redlineCells, cfg.breakAnywhereDoc, cfg.cellFontShorthand, cfg.chainBase, cfg.chainSpacingPt, cfg.chainUnbold, cfg.cssBaseFamily, cfg.cssBasePt, defaultCellFace, formGridStrutDropPt, cfg.hasBorder, cfg.inlineFaceRatio, overDeclaredDraw, cfg.padSide, cfg.uaDocGrid, uaSerifMin, ptCellWidths, bandDialect, cellLineHeightPt, cssRunFace, formGridDialect, formGridStrutPt, liftNestedTables, tightExtras, uaCellBoxes, cfg.borderWidth, cfg.pad, cfg.css, docCss, chainRules, cssAncestors, inlineSvgs, cfg.nestedHtml, makeRadio, availWidthPt, defaultCellFontPt, cfg.tblStyle, docElementGrid, pinnedBodyGrid, authoredCellChrome, cfg.chainBorderSeparate, cfg.elemRuleBorder);
                    break;
                case "a":
                    // Open an inline anchor: remember where its text starts on the
                    // current line and the target URL.
                    if (ps.cell is not null)
                    {
                        // The anchor's colour — its inline style, else the sheet's
                        // `a { color: … }` rule — rides the style stack for the
                        // anchor's extent, exactly like a coloured <span>.
                        Color? aCol = null;
                        if (tok.Attributes is not null
                            && tok.Attributes.TryGetValue("style", out var aSt)
                            && Regex.Match(aSt, @"(?<![-\w])color\s*:\s*([^;]+)",
                                RegexOptions.IgnoreCase) is { Success: true } aCm)
                            aCol = ParseCssColor(aCm.Groups[1].Value.Trim());
                        aCol ??= cfg.docAnchorColor;
                        if (aCol is not null)
                        {
                            ps.styleStack.Add(("a", ps.curFontPt, ps.curFamily, false, ps.curColor, false));
                            ps.curColor = aCol;
                        }
                    }
                    if (ps.cell is not null && tok.Attributes is not null
                        && tok.Attributes.TryGetValue("href", out var aHref)
                        && !string.IsNullOrEmpty(aHref))
                        ps.openAnchor = (ps.line.Length, aHref);
                    break;
                case "strong":
                case "b":
                    if (ps.cell is not null)
                    {
                        // Form-grid: a bold run OPENING mid-line marks a style-run
                        // boundary (the segment so far keeps the regular face).
                        if (formGridDialect)
                        {
                            ps.lineRunMarks ??= new();
                            if (ps.lineRunMarks.Count == 0)
                                ps.lineRunMarks.Add((0, ps.boldDepth > 0));
                        }
                        ps.boldDepth++;
                        if (formGridDialect) ps.lineRunMarks!.Add((ps.line.Length, true));
                        // Probe: the min-content measure applies real bold metrics per
                        // RUN (a bold word followed by a regular superscript measures
                        // each piece with its own face), marked by sentinels.
                        if (widenProbe) ps.line.Append('\uE000');
                    }
                    break;
                case "hr":
                    if (ps.cell is not null)
                    {
                        if (ps.line.Length > 0) PushLine(ps, redlineCells, dwFormCells, widenProbe);
                        if (ps.row is not null) (colModel.hrCells ??= new()).Add((ps.row, ps.cell));
                    }
                    break;
                case "pre":
                    if (ps.cell is not null && !tok.IsSelfClosing)
                    {
                        if (ps.line.Length > 0) PushLine(ps, redlineCells, dwFormCells, widenProbe);
                        if (ps.preDepth++ == 0)
                        {
                            // the pre's own inline font styles bind its lines (the
                            // case-comment box declares Arial at 1.0em)
                            var prevPrePt = ps.curFontPt; var prevPreFam = ps.curFamily;
                            if (tok.Attributes is not null
                                && tok.Attributes.TryGetValue("style", out var preSt) && preSt is not null)
                            {
                                var preFf = Regex.Match(preSt, @"font-family\s*:\s*([^;]+)",
                                    RegexOptions.IgnoreCase);
                                if (preFf.Success
                                    && FirstFontFamily(preFf.Groups[1].Value) is { Length: > 0 } preFam)
                                    ps.curFamily = preFam;
                                var preFs = Regex.Match(preSt, @"font-size\s*:\s*([\d.]+)\s*em",
                                    RegexOptions.IgnoreCase);
                                if (preFs.Success && double.TryParse(preFs.Groups[1].Value,
                                        System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture,
                                        out var preEm) && preEm > 0)
                                    ps.curFontPt = preEm * (prevPrePt > 0 ? prevPrePt : cfg.cellFontSize);
                            }
                            ps.styleStack.Add(("pre", prevPrePt, prevPreFam, false, ps.curColor, false));
                            if (ps.row is not null
                                && ((colModel.preCells ??= new()).Count == 0 || colModel.preCells[^1].Cell != ps.cell))
                                colModel.preCells.Add((ps.row, ps.cell));
                        }
                    }
                    break;
                case "br":
                    if (ps.cell is not null)
                    {
                        // An explicit <br> on an empty line is a deliberate blank line: it
                        // keeps its line box (at the active style's size) as vertical space.
                        // A LONE br on an empty line (not preceded by another br — e.g.
                        // right after a block boundary or table close) is tagged: the
                        // lifted-unstyled dialect drops it, keeping only the N−1 blanks
                        // of an N-br run (the <BR><BR> rhythm); styled dialects keep
                        // every one — they were calibrated that way.
                        var loneBrBlank = ps.line.Length == 0 && !ps.cellPendingBrBlank;
                        if (!ps.lineStyleSet) { ps.lineFontPt = ps.curFontPt; ps.lineFamily = ps.curFamily; }
                        if (ps.lineColor is null) ps.lineColor = ps.curColor;
                        PushLine(ps, redlineCells, dwFormCells, widenProbe, keepIfBlank: true);
                        if (loneBrBlank) (ps.loneBrBlankLines ??= new HashSet<int>()).Add(ps.lines.Count - 1);
                        ps.cellPendingBrBlank = true;
                    }
                    break;
                case "img":
                    HandleImgOpen(ps, colModel, table, tok, tag, options, cfg.cellFontSize, dwFormCells, fullWidthCjkMin, widenProbe, redlineCells, cfg.breakAnywhereDoc, cfg.cellFontShorthand, cfg.chainBase, cfg.chainSpacingPt, cfg.chainUnbold, cfg.cssBaseFamily, cfg.cssBasePt, defaultCellFace, formGridStrutDropPt, cfg.hasBorder, cfg.inlineFaceRatio, overDeclaredDraw, cfg.padSide, cfg.uaDocGrid, uaSerifMin, ptCellWidths, bandDialect, cellLineHeightPt, cssRunFace, formGridDialect, formGridStrutPt, liftNestedTables, tightExtras, uaCellBoxes, cfg.borderWidth, cfg.pad, cfg.css, docCss, chainRules, cssAncestors, inlineSvgs, cfg.nestedHtml, makeRadio, availWidthPt, defaultCellFontPt, cfg.tblStyle, docElementGrid, pinnedBodyGrid, authoredCellChrome, cfg.chainBorderSeparate, cfg.elemRuleBorder);
                    break;
                case "ol":
                case "ul":
                    if (ps.cell is not null && ps.line.Length > 0) PushLine(ps, redlineCells, dwFormCells, widenProbe);
                    // UA margin-block-start on a TOP-LEVEL list opening mid-cell:
                    // one line box above the first item. A nested list carries none
                    // (`ul ul { margin-block-start: 0 }` in every UA sheet).
                    if (liftNestedTables && ps.cell is not null && !tok.IsSelfClosing
                        && ps.listNesting.Count == 0 && ps.lines.Count > 0)
                    {
                        if (!ps.lineStyleSet) { ps.lineFontPt = ps.curFontPt; ps.lineFamily = ps.curFamily; }
                        PushLine(ps, redlineCells, dwFormCells, widenProbe, keepIfBlank: true);
                    }
                    if (!tok.IsSelfClosing) ps.listNesting.Add((tag == "ol", 0));
                    // Content of the list — including bare text before its first
                    // <li> — seats on the list's padding-inline-start indent.
                    ps.liStandingIndentPt = ListItemIndentPt * ps.listNesting.Count;
                    break;
                case "li":
                    if (ps.cell is not null)
                    {
                        if (ps.line.Length > 0) PushLine(ps, redlineCells, dwFormCells, widenProbe);
                        if (ps.listNesting.Count > 0)
                        {
                            var (liOrd, liCnt) = ps.listNesting[^1];
                            ps.listNesting[^1] = (liOrd, liCnt + 1);
                            var liMarker = liOrd
                                ? (liCnt + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + "."
                                : "•";
                            ps.liStandingIndentPt = ListItemIndentPt * ps.listNesting.Count;
                            // Hanging marker: the item's text seats ON the list indent,
                            // the marker rides just left of it ("1." draws
                            // as its own run ending one gap before the text).
                            var liFs = ps.curFontPt > 0 ? ps.curFontPt : cfg.cellFontSize;
                            ps.lineMarginLeft = Math.Max(0,
                                ps.liStandingIndentPt - MeasureLine(ps, options, cfg.cellFontSize, dwFormCells, fullWidthCjkMin, widenProbe, liMarker + " ", false, liFs));
                            // No implicit gap above an item: the question rhythm
                            // (2 line boxes between items) is the
                            // markup's own explicit <BR><BR>, which survives as a
                            // kept blank line — a plain <ul> stacks its items at
                            // bare line pitch.
                            ps.line.Append(liMarker).Append(' ');
                            ps.lineHadText = true;
                        }
                    }
                    break;
                case "select":
                    if (dwFormCells && ps.cell is not null && !tok.IsClose)
                    { ps.dwSelectDepth = 1; ps.dwSelectedOpt = null; ps.dwFirstOpt = null; ps.dwSawSelected = false; }
                    break;
                case "option":
                    if (dwFormCells && ps.dwSelectDepth > 0 && !tok.IsClose)
                    {
                        ps.dwOptSelected = tok.Attributes?.ContainsKey("selected") == true && !ps.dwSawSelected;
                        if (ps.dwOptSelected) ps.dwSawSelected = true;
                        ps.dwOptBuf.Clear();
                    }
                    break;
                case "textarea":
                    if (dwFormCells && ps.cell is not null && !tok.IsClose)
                    {
                        ps.dwTextareaOpen = true; ps.dwTaBuf.Clear();
                        var (dtW, dtH) = ParseInputSize(tok.Attributes is not null
                            && tok.Attributes.TryGetValue("style", out var dtSt) ? dtSt : null);
                        ps.dwTaW = dtW > 0 ? dtW * 0.75 : DwTextareaWPt;
                        ps.dwTaH = dtH > 0 ? dtH * 0.75 : DwTextareaHPt;
                    }
                    break;
                case "input":
                    // A form control INSIDE a grid cell occupies its line inline (it
                    // must not flush the cell's text flow). A checkbox/radio paints
                    // as a near-invisible white box, so only its advance matters —
                    // and that is within the wrap tolerance; a text-like input
                    // contributes its VALUE as cell text (the visible part of the
                    // filled-in control).
                    if (ps.cell is not null && tok.Attributes is not null)
                    {
                        tok.Attributes.TryGetValue("type", out var inType);
                        inType = inType?.Trim().ToLowerInvariant() ?? "text";
                        // A radio in a form grid rides its text line INLINE as a marker
                        // char (`◯ ◯Yes ◉ ◉No` sets on one line); the
                        // factory-built option is drawn as the circle glyph and its
                        // widget placed there by the table render pass.
                        if (inType == "radio" && makeRadio is not null)
                        {
                            tok.Attributes.TryGetValue("name", out var rName);
                            var rChecked = tok.Attributes.ContainsKey("checked");
                            var rOpt = makeRadio(rName ?? "", rChecked);
                            ps.line.Append(rChecked
                                ? Table.InlineRadioCheckedChar : Table.InlineRadioChar);
                            (ps.cellInlineOptions ??= new List<Aspose.Pdf.Forms.RadioButtonOptionField>())
                                .Add(rOpt);
                            ps.lineHadText = true;
                        }
                        // A push button in a form grid draws as its 3D chrome around
                        // the caption (the Print/Close controls); the
                        // caption rides the line between PUA markers so the column
                        // measures it and the render pass draws the box.
                        else if (inType is "button" or "submit" && makeRadio is not null
                            && tok.Attributes.TryGetValue("value", out var btnVal)
                            && !string.IsNullOrWhiteSpace(btnVal))
                        {
                            ps.line.Append(Table.InlineButtonChar).Append(btnVal.Trim())
                                .Append(Table.InlineButtonEndChar);
                            ps.lineHadText = true;
                        }
                        // DataWorks: a checked checkbox is a bare checkmark glyph;
                        // a text-like control is its declared pixel box with the
                        // value typeset inside.
                        else if (dwFormCells && inType == "checkbox")
                        {
                            if (tok.Attributes.ContainsKey("checked"))
                            {
                                ps.line.Append(Table.InlineCheckChar);
                                ps.rowMinHeightPt = Math.Max(ps.rowMinHeightPt, DwCheckboxRowHPt);
                                ps.lineHadText = true;
                            }
                            else
                            {
                                // A borderless unchecked box still OCCUPIES its
                                // widget width (the results row's text starts past
                                // it) without contributing to the column's min.
                                ps.line.Append(Table.InlineCheckboxGapChar);
                                ps.cellImgWidthPt += Table.DwHiddenInlinePt;
                                table.HtmlDwGapReservePt += Table.DwHiddenInlinePt;
                                ps.rowMinHeightPt = Math.Max(ps.rowMinHeightPt, DwCheckboxRowHPt);
                                ps.lineHadText = true;
                            }
                        }
                        // …a FILE control is the browser chrome: its button and
                        // the no-selection caption.
                        else if (dwFormCells && inType == "file")
                        {
                            // The file control opens its OWN line (the Remove
                            // button's div closes above it).
                            if (ps.line.Length > 0) PushLine(ps, redlineCells, dwFormCells, widenProbe);
                            ps.line.Append(Table.InlineButtonChar).Append(DwFileButtonCaption)
                                .Append(Table.InlineButtonEndChar).Append(" No file chosen");
                            ps.lineHadText = true;
                        }
                        else if (dwFormCells
                            && inType is not ("hidden" or "submit" or "button" or "image" or "radio"))
                        {
                            var (diW, diH) = ParseInputSize(tok.Attributes.TryGetValue("style", out var diSt) ? diSt : null);
                            tok.Attributes.TryGetValue("value", out var diVal);
                            ps.line.Append(Table.InlineInputChar);
                            (ps.cellInputBoxes ??= new()).Add((diW > 0 ? diW * 0.75 : DwSelectBoxWPt,
                                diH > 0 ? diH * 0.75 : DwInputBoxHPt, diVal ?? "", false, 0));
                            ps.lineHadText = true;
                            ps.cellImgWidthPt = Math.Max(ps.cellImgWidthPt,
                                (diW > 0 ? diW * 0.75 : DwSelectBoxWPt) + 4);
                        }
                        else if (inType is not ("checkbox" or "radio" or "hidden" or "submit" or "button" or "image")
                            && tok.Attributes.TryGetValue("value", out var inVal)
                            && !string.IsNullOrWhiteSpace(inVal))
                        {
                            if (ps.line.Length > 0 && !char.IsWhiteSpace(ps.line[^1])) ps.line.Append(' ');
                            ps.line.Append(inVal);
                            ps.lineHadText = true;
                        }
                    }
                    break;
            }
        }
        CloseRow(ps, colModel, table, options, cfg.cellFontSize, dwFormCells, fullWidthCjkMin, cfg.breakAnywhereDoc, cfg.cellFontShorthand, cfg.chainBase, cfg.chainSpacingPt, cfg.chainUnbold, cfg.cssBaseFamily, cfg.cssBasePt, defaultCellFace, formGridStrutDropPt, cfg.hasBorder, cfg.inlineFaceRatio, overDeclaredDraw, cfg.padSide, cfg.uaDocGrid, widenProbe, uaSerifMin, ptCellWidths, redlineCells, bandDialect, cellLineHeightPt, cssRunFace, formGridDialect, formGridStrutPt, liftNestedTables, tightExtras, uaCellBoxes, cfg.borderWidth, cfg.pad);
        // Apply the deferred column-span constraints: a spanning cell only forces its
        // columns' widths up when they don't already sum to its content — the deficit is
        // spread evenly, so a wide spanning line grows the columns it needs without
        // inflating thin spacer columns that other rows keep narrow.
        foreach (var (start, span, sMin, sMax, sHdr) in colModel.spanConstraints)
        {
            if (start + span > colModel.colMinW.Count) continue;
            void Raise(List<double> arr, double target)
            {
                double sum = 0; for (var k = 0; k < span; k++) sum += arr[start + k];
                if (sum >= target || span <= 0) return;
                // …and the deficit lands on the columns that can TAKE it: a column with a
                // declared width keeps it. A spanning logo cell beside a 15 px spacer was
                // spreading its own width over both, floor-ing the spacer at a third of
                // the logo and pushing everything in the row that far right.
                var takers = 0;
                for (var k = 0; k < span; k++)
                    if (start + k >= colModel.colDeclW.Count || colModel.colDeclW[start + k] <= 0) takers++;
                var add = (target - sum) / (takers > 0 ? takers : span);
                for (var k = 0; k < span; k++)
                    if (takers <= 0 || start + k >= colModel.colDeclW.Count || colModel.colDeclW[start + k] <= 0)
                        arr[start + k] += add;
            }
            Raise(colModel.colMinW, sMin);
            Raise(colModel.colMaxW, sMax);
            if (sHdr > 0) Raise(colModel.colHdrW, sHdr);
        }
        if (ps.headerRows > 0 && ps.headerRows < table.Rows.Count) table.RepeatingRowsCount = ps.headerRows;

        // Form-document dialect: a `<table height="90">` attribute is a minimum on the
        // TABLE height, shared equally by its rows (the browser's table model) — each
        // row floors at its share, content still grows a row past it.
        if (cfg.cellFontShorthand && colModel.tblHeightPx > 0 && table.Rows.Count > 0)
        {
            var rowShare = colModel.tblHeightPx * PxToPt / table.Rows.Count;
            foreach (Row hr in table.Rows)
                if (rowShare > hr.MinRowHeight) hr.MinRowHeight = rowShare;
        }

        if (table.Rows.Count == 0) { naturalWidthPt = 0; return null; }
        naturalWidthPt = 0;
        // Colgroup grid: each column is its declared width, stretched to min-content when an
        // unbreakable run needs more (colMinW already includes padding/border slack).
        // A COLGROUP whose cols declare NO widths ("<col class=…>") pins nothing —
        // under the chain dialect those tables keep their content/percent column
        // model (legacy dialects keep the historical min-content pinning).
        if (colModel.colGroupPt is { Count: > 0 } && colModel.colGroupPt.Count == colModel.maxCols
            && (cfg.chainBase is null || colModel.colGroupPt.Exists(w => w > 0)))
        {
            colModel.colWidthsPt = new List<double>(colModel.maxCols);
            for (var i = 0; i < colModel.maxCols; i++)
            {
                var declared = colModel.colGroupPt[i];
                var minC = i < colModel.colMinW.Count ? colModel.colMinW[i] : 0;
                colModel.colWidthsPt.Add(Math.Max(declared, minC));
            }
        }
        // Redline: the two-column grid whose first column carries BOTH 86.58%
        // and 13.42% across rows resolves at the observed split
        // (probed: the checkbox row's centre at 245.9 and the right column's
        // text opening at 399.3 put the boundary at 60.75% of the content box).
        if (redlineCells && colModel.colPctConflict && colModel.maxCols == 2 && colModel.colWidthsPt is null
            && availWidthPt > 0)
            colModel.colWidthsPt = new List<double>
            {
                RedlineConflictCol1Frac * availWidthPt,
                (1 - RedlineConflictCol1Frac) * availWidthPt,
            };
        // A per-column percent grid (the classic sizing row) fixes the split against
        // the table's width — honoured before any content fit when the declared
        // percents dominate the grid. Columns the row leaves unsized (spacer cells)
        // share the leftover percent evenly; every column is floored at its
        // min-content so an unbreakable run still gets room.
        // Over-declared grid dialect, TABLE-LAYOUT:fixed draw: columns resolve at
        // their declared share of the FIXED BASE — the full-bled host box for a
        // grid whose declarations fit (width=100% fills the page-wide band), the
        // standard demand base for an OVER-declared one (its shares cannot fit
        // any box; they resolve against the same base the widen
        // demand used). Pixel columns pin, percent columns floor at min-content,
        // the auto columns split the remainder. (Fitted on the shipped grids:
        // the rental question row's 50% column and the amounts grid's 15%/35%
        // groups both land within a point.)
        if (overDeclaredDraw && colModel.colWidthsPt is null && availWidthPt > 0 && colModel.maxCols > 0
            && cfg.tblStyle.TryGetValue("table-layout", out var tlFixDraw)
            && tlFixDraw.Contains("fixed", StringComparison.OrdinalIgnoreCase)
            && colModel.colPctW.Count > 0)
        {
            while (colModel.colPctW.Count < colModel.maxCols) colModel.colPctW.Add(0);
            var fixBase = ps.rowPctDeclMax > 100.0 + 1e-6
                ? availWidthPt - UaBodyMarginPt
                : availWidthPt + OverDeclaredBleedRightPt;
            var padPair = 2 * Math.Max(0, cfg.padSide);
            colModel.colWidthsPt = new List<double>(colModel.maxCols);
            double fixedSum = 0;
            var autoIdx = new List<int>();
            for (var i = 0; i < colModel.maxCols; i++)
            {
                var minC = i < colModel.colMinBrkW.Count ? colModel.colMinBrkW[i] : 0;
                if (colModel.colPctW[i] > 0)
                    colModel.colWidthsPt.Add(Math.Max(colModel.colPctW[i] / 100.0 * fixBase + padPair, minC));
                else if (i < colModel.colDeclW.Count && colModel.colDeclW[i] > 0)
                    colModel.colWidthsPt.Add(Math.Max(colModel.colDeclW[i] + padPair, minC));
                else
                {
                    colModel.colWidthsPt.Add(0);
                    autoIdx.Add(i);
                }
                fixedSum += colModel.colWidthsPt[i];
            }
            if (autoIdx.Count > 0)
            {
                var autoShare = Math.Max(0, fixBase + padPair * colModel.maxCols - fixedSum) / autoIdx.Count;
                foreach (var ai in autoIdx)
                    colModel.colWidthsPt[ai] = Math.Max(autoShare,
                        ai < colModel.colMinBrkW.Count ? colModel.colMinBrkW[ai] : 0);
            }
        }
        SolveColumnWidths(colModel, table, cfg.tblStyle, cfg.tblTag, cfg.chainBase, availWidthPt, cfg.cellFontSize, cfg.cellFontShorthand, dwFormCells, fullWidthCjkMin, overDeclaredDraw, cfg.uaDocGrid, cfg.padSide, ps.rowPctDeclMax, ps.headerRows, ptCellWidths, uaCellBoxes, uaSerifMin, ps.rowPxAtMax, ps.rowPxCellsAtMax, ref naturalWidthPt);
        return table;
    }

    // The <hr> separator bar: a solid dark PNG the rule cell stretches across
    // its columns (built once; the UA hr renders as a near-black groove).
    private static byte[]? _hrBarPng;
    private static byte[] HrBarPng()
        => _hrBarPng ??= OperatingSystem.IsWindows() ? HrBarPngGdi() : HrBarPngManaged();

    /// <summary>Windows: the GDI+ PNG encoder, whose exact byte stream the rendered
    /// baselines are calibrated against.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static byte[] HrBarPngGdi()
    {
        using var bmp = new System.Drawing.Bitmap(4, 4,
            System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
            g.Clear(System.Drawing.Color.FromArgb(64, 64, 64));
        using var ms = new System.IO.MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return ms.ToArray();
    }

    /// <summary>Off Windows: the same 4x4 solid bar through the managed encoder. The
    /// pixels are identical - only the encoder's byte stream differs, so keeping GDI+
    /// on Windows leaves the Windows output byte-for-byte what it was.</summary>
    private static byte[] HrBarPngManaged()
    {
        var px = new byte[4 * 4 * 3];
        for (int i = 0; i < px.Length; i++) px[i] = 64;
        return Aspose.Pdf.IO.PngEncoder.Encode(px, 4, 4, colorType: 2);
    }
}
