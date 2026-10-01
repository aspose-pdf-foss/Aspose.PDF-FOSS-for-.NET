using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The cell's own inline style outranks every selector that reached it: its size and colour are read last.</summary>
    private static void ReadCellOwnInlineStyle(CellOpenState co)
    {
        // …and the cell's OWN inline style outranks every selector that
        // reached it: `<td style="font-size:10px;color:#9c9e9f">` sizes and
        // colours that cell's text. Read last so it wins over the class and
        // chain rules applied above.
        if (co.liftNestedTables && co.tok.Attributes is not null
            && co.tok.Attributes.TryGetValue("style", out var tdOwnSt) && tdOwnSt is not null)
        {
            // ⚠ PARTIAL, deliberately: only a SMALLER declared size is
            // honoured. Shrinking a cell's text can never wrap a line that
            // fitted before, so it is safe today; growing it needs the
            // column model to widen with the cell's own font, which it does
            // not yet do — a 16.5 pt header cell then wraps a title that
            // must stay whole. The PINNED-BODY report dialect lifts the
            // guard: its lines measure at their own size through the chain
            // path, so the column absorbs the growth (the 22px title cell).
            if (Regex.Match(tdOwnSt, @"(?<![-\w])font-size\s*:\s*([^;""']+)",
                    RegexOptions.IgnoreCase) is { Success: true } tdFsm
                && TryParseLength(tdFsm.Groups[1].Value.Trim()) is { } tdFsp && tdFsp > 0
                && (co.pinnedBodyGrid
                    || tdFsp < (co.ps.cellClassPt > 0 ? co.ps.cellClassPt : co.cellFontSize)))
                co.ps.cellClassPt = tdFsp;
            if (Regex.Match(tdOwnSt, @"(?<![-\w])color\s*:\s*([^;""']+)",
                    RegexOptions.IgnoreCase) is { Success: true } tdColm
                && ParseCssColor(tdColm.Groups[1].Value.Trim()) is { } tdCol)
                co.ps.cellChainColor = tdCol;
            // The cell's own `line-height` pitches its lines: an em (or bare
            // number) resolves against the cell's DECLARED font size even
            // when the applied size kept a larger base (the guard above) —
            // `line-height:1.1em; font-size:10px` is an 8.25 pt pitch.
            if (Regex.Match(tdOwnSt, @"(?<![-\w])line-height\s*:\s*([\d.]+)\s*(em|px|pt)?",
                    RegexOptions.IgnoreCase) is { Success: true } tdLhm
                && double.TryParse(tdLhm.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var tdLh)
                && tdLh > 0)
            {
                var tdDeclPt = Regex.Match(tdOwnSt,
                        @"(?<![-\w])font-size\s*:\s*([^;""']+)", RegexOptions.IgnoreCase)
                    is { Success: true } fsm2
                    && TryParseLength(fsm2.Groups[1].Value.Trim()) is { } declPt
                    && declPt > 0 ? declPt
                    : co.ps.cellClassPt > 0 ? co.ps.cellClassPt : co.cellFontSize;
                co.ps.cellOwnLineHPt = tdLhm.Groups[2].Value.ToLowerInvariant() switch
                {
                    "px" => tdLh * PxToPt,
                    "pt" => tdLh,
                    _ => tdLh * tdDeclPt,   // em or a bare number
                };
            }
        }
    }

    /// <summary>Chain-selector styling fills the slots no inline or attribute handler set.</summary>
    private static void ApplyChainCellRules(CellOpenState co)
    {
        // Chain-selector styling for this cell — the least specific
        // layer: every inline/attribute handler above already had its
        // say, so only the still-unset slots fill.
        if (!(co.chainBase is not null)) return;
        co.ps.chainTdElem = ChainTokElem(co.tag, co.tok.Attributes);
        co.ps.chainOpenElems?.Clear();
        co.tdChain = new List<CssElem>(co.chainBase) { co.ps.chainTdElem };
        if (!(MatchChainDecls(co.chainRules, co.tdChain) is { } cd)) return;
        // The stylesheet reaches this grid's cells (see
        // Table.HtmlChainStyledCells).
        co.table.HtmlChainStyledCells = true;
        // Font first: the ex/em pads below resolve on the cell size.
        if (co.ps.cellClassPt <= 0 && cd.TryGetValue("font-size", out var cfs))
        {
            var pcm = Regex.Match(cfs.Trim(), @"^([\d.]+)\s*%$");
            if (pcm.Success && double.TryParse(pcm.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var fsPct)
                && fsPct > 0)
                co.ps.cellClassPt = co.cellFontSize * fsPct / 100.0;
            else if (ChainLenPt(cfs, co.cellFontSize) is > 0 and var fsAbs)
                co.ps.cellClassPt = fsAbs;
        }
        if (!co.ps.cellBold && cd.TryGetValue("font-weight", out var cfw)
            && Regex.IsMatch(cfw, @"bold|[6-9]00", RegexOptions.IgnoreCase))
            co.ps.cellBold = true;
        if (co.ps.cell!.BackgroundColor is null
            && (cd.TryGetValue("background-color", out var cbg)
                || cd.TryGetValue("background", out cbg))
            && ParseCssColor(cbg) is { } cbgc)
            co.ps.cell.BackgroundColor = cbgc;
        if (cd.TryGetValue("color", out var ccol) && ParseCssColor(ccol) is { } ccolc)
            co.ps.cellChainColor = ccolc;
        ApplyChainCellBorderAndAlign(co, cd);
        if (cd.TryGetValue("white-space", out var cws)
            && cws.Contains("nowrap", StringComparison.OrdinalIgnoreCase))
            co.ps.cell.HtmlNoWrap = true;
        // A chain rule's percent width declares the column share
        // (`.CategoryName { width: 80% }` — the pill grid's name
        // column absorbs the slack, the detail box hugs its text).
        if (co.ps.cellWidthPct <= 0 && cd.TryGetValue("width", out var cwv2)
            && cwv2.TrimEnd().EndsWith("%", StringComparison.Ordinal)
            && double.TryParse(cwv2.Trim().TrimEnd('%'),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var cwPct)
            && cwPct > 0)
            co.ps.cellWidthPct = cwPct;
        if (co.ps.cell.VerticalAlignment == VerticalAlignment.None
            && cd.TryGetValue("vertical-align", out var cva))
            co.ps.cell.VerticalAlignment = cva.Trim().ToLowerInvariant() switch
            {
                "top" => VerticalAlignment.Top,
                "middle" => VerticalAlignment.Center,
                "bottom" => VerticalAlignment.Bottom,
                _ => VerticalAlignment.None,
            };
        if (co.ps.cellCssPadPt <= 0)
        {
            var padBase = co.ps.cellClassPt > 0 ? co.ps.cellClassPt : co.cellFontSize;
            var (cpT, cpR, cpB, cpL) = ChainPadSidesPt(cd, padBase, co.ps.sheetTdBoxRule);
            // The cell's own style attribute outranks the sheet SIDE BY SIDE: a cell that declares a
            // zero padding-left of its own keeps it at nothing while the rule still states the right one.
            if (co.ps.sheetTdBoxRule && co.ps.cellPadLeftInline) cpL = co.ps.cellPadLeftPt;
            if (co.ps.sheetTdBoxRule && co.ps.cellPadRightInline) cpR = 0;
            if (cpL + cpR > 0) { co.ps.cellCssPadPt = cpL + cpR; co.ps.cellPadLeftPt = cpL; }
            co.ps.cellChainPadTopPt = cpT; co.ps.cellChainPadBotPt = cpB;
        }
    }

    /// <summary>Flat and tag-qualified class rules on a lifted cell: backgrounds, borders and the cascade of later classes over earlier ones.</summary>
    private static void ApplyCellClassRules(CellOpenState co)
    {
        // Flat class rules on the cell (`.resulttableheadercelltables
        // { background-color: silver; border: 1px solid white }`) —
        // the grey header cells/columns of the report grids. Lifted
        // dialect only; legacy paths never read class backgrounds.
        // The selector may be TAG-QUALIFIED (`td.no-border { … }`), and a
        // LATER class overrides an earlier one's background (the cascade:
        // `class="header exhibit-name"` paints the exhibit row white).
        if (!(co.liftNestedTables && co.tok.Attributes is not null
            && co.tok.Attributes.TryGetValue("class", out var bgCls))) return;
        foreach (var cn in bgCls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
                ApplyCellClassRule(co, cn);
        }
    }

    /// <summary>The cell's style attribute: alignment, padding, borders, colours and fonts.</summary>
    private static void ReadCellStyleAttributes(CellOpenState co)
    {
        if (co.tok.Attributes is not null)
        {
            if (co.tok.Attributes.TryGetValue("colspan", out var cs) && int.TryParse(cs, out var csn) && csn > 0)
                co.ps.colSpan = csn;
            if (co.tok.Attributes.TryGetValue("rowspan", out var rs) && int.TryParse(rs, out var rsn) && rsn > 1)
                co.ps.cellRowSpan = rsn;
            if (co.tok.Attributes.TryGetValue("style", out var st))
            {
                // A cell opting out of the table's borders keeps its box
                // blank (`<td style="border-style:none">` in a bordered
                // table — the layout-table idiom).
                if (Regex.IsMatch(st, @"border(-style)?\s*:\s*none", RegexOptions.IgnoreCase))
                    co.ps.cell!.Border = new BorderInfo(BorderSide.None);
                if (co.uaCellBoxes && UaUppercaseDeclared(st)) co.ps.cellUpper = true;
                if (co.uaCellBoxes && UaRuleBelow(st) is (var tdRuleW, var tdRuleC))
                { co.ps.cellRuleBelowPt = tdRuleW; co.ps.cellRuleColor = tdRuleC; }
                if (co.uaCellBoxes && UaRuleAbove(st) is (var tdTopW, var tdTopC) && tdTopW > co.ps.uaPrevRowRuleBelowPt + 0.01)
                { co.ps.cellRuleAbovePt = tdTopW; co.ps.cellRuleAboveColor = tdTopC; }
                var am = Regex.Match(st, @"text-align\s*:\s*(left|right|center)", RegexOptions.IgnoreCase);
                if (am.Success)
                {
                    co.ps.alignSet = true;
                    co.ps.cellAlign = am.Groups[1].Value.ToLowerInvariant() switch
                    {
                        "right" => HorizontalAlignment.Right,
                        "center" => HorizontalAlignment.Center,
                        _ => HorizontalAlignment.Left,
                    };
                }
                // Band-dialect per-cell border sides (BORDER-LEFT:1px solid #000…):
                // proxy-card notice frames, corner marks and signature rules are
                // drawn as TD border sides.
                if (co.ps.wordMailCells) { ApplyWordMailCellBorders(co, st); ReadWordMailCellPadding(co, st); }
                else if (co.bandDialect || co.authoredCellChrome || co.ptCellWidths)
                {
                    BorderSide bsSides = 0; double bsW = 0; Color? bsColor = null;
                    foreach (var (bprop, bside) in new[]
                    {
                        ("border-left", BorderSide.Left), ("border-top", BorderSide.Top),
                        ("border-bottom", BorderSide.Bottom), ("border-right", BorderSide.Right),
                    })
                    {
                        if (TryParseBorderShorthand(st, bprop) is not (var bpt, var bcol))
                        {
                            // The pt-styled fragment spells each side as
                            // LONGHANDS (border-bottom-width/-style/-color).
                            if (!co.ptCellWidths
                                || Regex.Match(st,
                                    @"(?<![-\w])" + bprop + @"-style\s*:\s*(\w+)",
                                    RegexOptions.IgnoreCase) is not { Success: true } blS
                                || blS.Groups[1].Value.Equals("none",
                                    StringComparison.OrdinalIgnoreCase))
                                continue;
                            bpt = Regex.Match(st,
                                    @"(?<![-\w])" + bprop + @"-width\s*:\s*([\d.]+)\s*(px|pt)",
                                    RegexOptions.IgnoreCase) is { Success: true } blW
                                && double.TryParse(blW.Groups[1].Value,
                                    System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    out var blWv)
                                ? (blW.Groups[2].Value.Equals("px",
                                    StringComparison.OrdinalIgnoreCase) ? blWv * 0.75 : blWv)
                                : 0.75;
                            bcol = Regex.Match(st,
                                    @"(?<![-\w])" + bprop + @"-color\s*:\s*([^;]+)",
                                    RegexOptions.IgnoreCase) is { Success: true } blC
                                ? ParseCssColor(blC.Groups[1].Value.Trim())
                                : null;
                        }
                        bsSides |= bside;
                        if (bpt > bsW) bsW = bpt;
                        bsColor ??= bcol;
                    }
                    if (bsSides != 0)
                    {
                        co.ps.cell!.Border = new BorderInfo(bsSides, bsW <= 0 ? 0.75 : bsW,
                            bsColor ?? Color.Black);
                        if (co.ptCellWidths && bsW > co.colModel.ptMaxCellBorderW)
                            co.colModel.ptMaxCellBorderW = bsW;
                    }
                }
            }
        }
    }

    /// <summary>A pixel width attribute and an inline percent width feed the fixed-grid and percent-grid demands.</summary>
    private static void ReadCellWidthAttributes(CellOpenState co)
    {
        // A pixel width attribute ("25px" / "25") beside the percent row:
        // tracked for the over-declared fixed-grid demand — the legacy
        // cellWidthPt path deliberately ignores the attribute form.
        if (co.tok.Attributes is not null
            && co.tok.Attributes.TryGetValue("width", out var wPxAttr)
            && !wPxAttr.Trim().EndsWith('%'))
        {
            var wpx = wPxAttr.Trim();
            if (wpx.EndsWith("px", StringComparison.OrdinalIgnoreCase)) wpx = wpx[..^2].Trim();
            if (double.TryParse(wpx, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var wPxV) && wPxV > 0)
            {
                co.ps.rowPxSum += wPxV * PxToPt;
                co.ps.rowPxCells++;
                // The dialect honours the attribute as the column's
                // declared width (the 62px logo/spacer columns) — the
                // legacy cellWidthPt path deliberately ignores it.
                if (co.overDeclaredDraw && co.ps.cellWidthPt <= 0)
                    co.ps.cellWidthPt = wPxV * PxToPt;
            }
        }
        // An inline style="width: N%" declares the same percent grid the
        // width attribute does.
        if (co.ps.cellWidthPct <= 0 && co.tok.Attributes is not null
            && co.tok.Attributes.TryGetValue("style", out var wPctStyle))
        {
            var pm = Regex.Match(wPctStyle, @"width\s*:\s*(\d+(?:\.\d+)?)\s*%");
            if (pm.Success && double.TryParse(pm.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var wPctS)
                && wPctS > 0)
                co.ps.cellWidthPct = wPctS;
        }
    }

    /// <summary>A lifted grid reads the cell's CSS padding: the gutter beside an image column and the gap under a picture stack.</summary>
    private static void ReadLiftedCellPadding(CellOpenState co)
    {
        co.ps.cellCssPadPt = 0; co.ps.cellFixedDivPt = 0; co.ps.cellPadLeftPt = 0;
        co.ps.cellPadLeftInline = false; co.ps.cellPadRightInline = false;
        // …and a lifted grid reads it too: an image column's `padding-right`
        // is the gutter between it and the text column beside it, and its
        // `padding-bottom` the gap under each picture in a stack of them.
        if (co.liftNestedTables
            && co.tok.Attributes is not null && co.tok.Attributes.TryGetValue("style", out var tdVSt) && tdVSt is not null)
        {
            foreach (Match pm in Regex.Matches(tdVSt,
                @"padding-(top|bottom)\s*:\s*(\d+(?:\.\d+)?)\s*px", RegexOptions.IgnoreCase))
            {
                var vPadPt = double.Parse(pm.Groups[2].Value,
                    System.Globalization.CultureInfo.InvariantCulture) * PxToPt;
                if (pm.Groups[1].Value.Equals("top", StringComparison.OrdinalIgnoreCase))
                    co.ps.cellChainPadTopPt = Math.Max(co.ps.cellChainPadTopPt, vPadPt);
                else
                {
                    co.ps.cellChainPadBotPt = Math.Max(co.ps.cellChainPadBotPt, vPadPt);
                    // A DECLARED zero overrides the table's cellpadding
                    // (the filing shell's `padding-bottom: 0px` host
                    // cells) — over-declared grid dialect only.
                    if (co.overDeclaredDraw && vPadPt <= 0) co.ps.cellVPadZeroBot = true;
                }
            }
            // …and the SHORTHAND's vertical value (`padding: 8px 0px`) is
            // the same declaration in one token.
            if (Regex.Match(tdVSt, @"(?<![-\w])padding\s*:\s*(\d+(?:\.\d+)?)\s*px",
                    RegexOptions.IgnoreCase) is { Success: true } pshm
                && double.TryParse(pshm.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var pshPx)
                && pshPx > 0)
            {
                co.ps.cellChainPadTopPt = Math.Max(co.ps.cellChainPadTopPt, pshPx * PxToPt);
                co.ps.cellChainPadBotPt = Math.Max(co.ps.cellChainPadBotPt, pshPx * PxToPt);
            }
        }
        if ((co.uaCellBoxes || co.liftNestedTables)
            && co.tok.Attributes is not null && co.tok.Attributes.TryGetValue("style", out var tdSt2) && tdSt2 is not null)
            foreach (Match pm in Regex.Matches(tdSt2,
                @"padding-(left|right)\s*:\s*(\d+(?:\.\d+)?)\s*px", RegexOptions.IgnoreCase))
            {
                var padPt = double.Parse(pm.Groups[2].Value,
                    System.Globalization.CultureInfo.InvariantCulture) * PxToPtW;
                co.ps.cellCssPadPt += padPt;
                if (pm.Groups[1].Value.Equals("left", StringComparison.OrdinalIgnoreCase))
                { co.ps.cellPadLeftPt += padPt; co.ps.cellPadLeftInline = true; }
                else co.ps.cellPadRightInline = true;
            }
        // …and the SHORTHAND (`style="padding: 15px"`) stands a nested DECLARED grid past the host
        // box by its two pads (probed on the e-mail statement: the 600 px grid in 15 px pads inks
        // 22.5 pt past the 600 px class box); the calibrated pad consumers do not read it.
        // A UA-boxed cell's own padding shorthand is its box on every side - the cell's style attribute is the
        // most specific rule (measured on the mailing: the `style="padding: 15px"` column indents its text 11.25).
        if (co.uaCellBoxes && co.tok.Attributes is not null && co.tok.Attributes.TryGetValue("style", out var uaPadSt) && uaPadSt is not null
            && Regex.Match(uaPadSt, @"(?<![-\w])padding\s*:\s*([^;""]+)", RegexOptions.IgnoreCase) is { Success: true } uaPad)
        {
            var (uaT, uaR, uaB, uaL) = ChainPadPt(uaPad.Groups[1].Value, co.cellFontSize);
            co.ps.cellChainPadTopPt = Math.Max(co.ps.cellChainPadTopPt, uaT);
            co.ps.cellChainPadBotPt = Math.Max(co.ps.cellChainPadBotPt, uaB);
            if (uaL + uaR > 0 && co.ps.cellCssPadPt <= 0) { co.ps.cellCssPadPt = uaL + uaR; co.ps.cellPadLeftPt = uaL; }
        }
        if (co.uaCellBoxes) ApplyUaSheetCellPadding(co);
        if ((co.uaCellBoxes || co.liftNestedTables) && co.ps.cellCssPadPt <= 0
            && co.tok.Attributes is not null && co.tok.Attributes.TryGetValue("style", out var tdSt3) && tdSt3 is not null
            && Regex.Match(tdSt3, @"(?<![-\w])padding\s*:\s*([^;""]+)", RegexOptions.IgnoreCase) is { Success: true } shPad)
        {
            var (_, shR, _, shL) = ChainPadPt(shPad.Groups[1].Value, co.cellFontSize);
            if (shL + shR > 0) co.ps.cellShortPadPt = shL + shR;
        }
        // pt-styled fragment: the SAME horizontal pads in the pt
        // spelling — the column is the declared width plus its own
        // pads (content-box), and the left pad indents the text.
        if ((co.ptCellWidths || (co.redlineCells && !co.widenProbe))
            && co.tok.Attributes is not null && co.tok.Attributes.TryGetValue("style", out var tdStPt) && tdStPt is not null)
            foreach (Match pm in Regex.Matches(tdStPt,
                @"padding-(left|right)\s*:\s*(\d+(?:\.\d+)?)\s*pt", RegexOptions.IgnoreCase))
            {
                var padPt = double.Parse(pm.Groups[2].Value,
                    System.Globalization.CultureInfo.InvariantCulture);
                co.ps.cellCssPadPt += padPt;
                if (pm.Groups[1].Value.Equals("left", StringComparison.OrdinalIgnoreCase))
                    co.ps.cellPadLeftPt += padPt;
            }
        if (co.tok.Attributes is not null
            && co.tok.Attributes.TryGetValue("width", out var wPctAttr)
            && wPctAttr.Trim().EndsWith('%')
            && double.TryParse(wPctAttr.Trim().TrimEnd('%'),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var wPct)
            && wPct > 0)
            co.ps.cellWidthPct = wPct;
    }

    /// <summary>The cell's width in points from its attributes and rules, the pt-styled fragment's inline width, and the element-grid width.</summary>
    private static void ResolveCellWidth(CellOpenState co)
    {
        co.ps.cellWidthPt = ResolveCellWidthPt(co.tok.Attributes, co.css, contentBox: co.uaCellBoxes,
            readWidthAttr: co.liftNestedTables) * PxToPtW;
        // pt-styled fragment: the cell's inline pt width IS the
        // column width (already in points — no px scale).
        if (co.ptCellWidths && co.ps.cellWidthPt <= 0
            && co.tok.Attributes is not null
            && co.tok.Attributes.TryGetValue("style", out var ptwSt) && ptwSt is not null
            && Regex.Match(ptwSt, @"(?<![-\w])width\s*:\s*([\d.]+)\s*pt",
                RegexOptions.IgnoreCase) is { Success: true } ptwM
            && double.TryParse(ptwM.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var ptwV)
            && ptwV > 0)
            co.ps.cellWidthPt = ptwV;
        if (co.ps.cellWidthPt <= 0 && co.uaCellBoxes && UaSheetCellWidthPt(co) is > 0 and var uaClsW)
            co.ps.cellWidthPt = uaClsW;
        // A class width in the DOCUMENT sheet fixes the column too — the
        // fragment map is empty when the rules live in the page's own
        // <style> block — and the selector may be TAG-QUALIFIED
        // (`td.single { width: 82px }`), a key the bare-class lookup misses.
        if (co.ps.cellWidthPt <= 0 && co.docElementGrid && co.tok.Attributes is not null
            && co.tok.Attributes.TryGetValue("class", out var wCls))
            foreach (var cn in wCls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                Dictionary<string, string>? wRule = null;
                foreach (var wSrc in new[] { co.css, co.docCss })
                {
                    if (wSrc is null) continue;
                    if (wSrc.TryGetValue(co.tag + "." + cn, out wRule)
                        || wSrc.TryGetValue("." + cn, out wRule)) break;
                    wRule = null;
                }
                if (wRule is not null && wRule.TryGetValue("width", out var wV)
                    && !wV.Contains('%')
                    && TryParseLength(wV.Trim()) is { } wPtv && wPtv > 0)
                {
                    co.ps.cellWidthPt = wPtv / PxToPt * PxToPtW;
                    break;
                }
            }
        // The cell's own CSS padding is part of its column footprint — it
        // rides on the measured content, and on a fixed-width inner div.
    }

    /// <summary>Bold and class point size from the cell's class rules.</summary>
    private static void ReadCellClassStyle(CellOpenState co)
    {
        co.ps.cellBold = co.ps.tableBold;
        co.ps.cellClassPt = 0;
        co.ps.cellClassFamily = null;
        if (co.tok.Attributes is not null
            && co.tok.Attributes.TryGetValue("class", out var szCls))
            foreach (var cn in szCls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                Dictionary<string, string>? szRule = null;
                if (!co.css.TryGetValue("." + cn, out szRule)) co.docCss?.TryGetValue("." + cn, out szRule);
                // A class `font:` SHORTHAND states the cell's size and face in one declaration
                // (`.c0 { font: 8pt "Courier New" }`); the longhand probe below never sees it.
                if (szRule is not null && !szRule.ContainsKey("font-size")
                    && szRule.TryGetValue("font", out var fsh) && CssFontShorthandValue(fsh) is { sizePt: > 0 } sh)
                {
                    co.ps.cellClassPt = sh.sizePt;
                    if (sh.family is { Length: > 0 } shFam && WinMetricsFor(shFam) is not null) co.ps.cellClassFamily = shFam;
                    break;
                }
                if (szRule is null || !szRule.TryGetValue("font-size", out var szv)) continue;
                // rem/em sizes resolve through the length parser — the
                // pt/px regex below reads ".875rem" as bare 0.875 POINTS
                // and the whole grid draws at ant size. Bare numbers keep
                // their legacy points reading.
                if (Regex.IsMatch(szv, @"r?em", RegexOptions.IgnoreCase)
                    && TryParseLength(szv.Trim()) is { } szRelPt && szRelPt > 0)
                {
                    co.ps.cellClassPt = szRelPt;
                    break;
                }
                var szm = Regex.Match(szv, @"([\d.]+)\s*(pt|px)?", RegexOptions.IgnoreCase);
                if (!szm.Success || !double.TryParse(szm.Groups[1].Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var szn) || szn <= 0)
                    continue;
                co.ps.cellClassPt = szm.Groups[2].Value.Equals("px", StringComparison.OrdinalIgnoreCase)
                    ? szn * 0.75 : szn;
                break;
            }
        if (co.tok.Attributes is not null)
        {
            if (co.tok.Attributes.TryGetValue("style", out var bStyle)
                && Regex.IsMatch(bStyle, @"font-weight\s*:\s*(bold|[6-9]00)", RegexOptions.IgnoreCase))
                co.ps.cellBold = true;
            if (!co.ps.cellBold && co.tok.Attributes.TryGetValue("class", out var bCls))
                foreach (var cn in bCls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    if ((co.css.TryGetValue("." + cn, out var bRule)
                            || (co.docCss?.TryGetValue("." + cn, out bRule) ?? false))
                        && bRule.TryGetValue("font-weight", out var fw)
                        && Regex.IsMatch(fw, @"bold|[6-9]00", RegexOptions.IgnoreCase))
                    { co.ps.cellBold = true; break; }
        }
        co.ps.rowHasCell = true; if (co.tag == "td") co.ps.rowHasTd = true;
    }

    /// <summary>The legacy ALIGN and VALIGN attributes, the cell chrome dialects and the row's own vertical seat.</summary>
    private static void ApplyCellAlignmentAndChrome(CellOpenState co)
    {
        // The legacy ALIGN attribute aligns the cell's own content, exactly
        // like a `text-align` in its style (which, parsed below, still wins).
        if ((co.liftNestedTables || co.uaCellBoxes || co.authoredCellChrome || co.formGridDialect)
            && co.tok.Attributes is not null
            && co.tok.Attributes.TryGetValue("align", out var tdAl)
            && ParseAlignAttr(tdAl) is { } tdAlign)
        { co.ps.alignSet = true; co.ps.cellAlign = tdAlign; }
        // A cell HEIGHT="N" (px) is an HTML minimum on its row's height.
        // (a UA-boxed grid's rows stay their tallest line box: measured, `height="14"` cells at
        //  Verdana 6 pitch 9.75 = the 7.5 line + 1 px pads + 1 px spacing, the attribute unread)
        if (!(co.uaCellBoxes)
            && co.tok.Attributes is not null && co.tok.Attributes.TryGetValue("height", out var tdH)
            && double.TryParse(Regex.Match(tdH, @"[\d.]+").Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var tdHpx) && tdHpx > 0)
        {
            co.ps.rowMinHeightPt = Math.Max(co.ps.rowMinHeightPt, tdHpx * PxToPt);
            co.ps.cellOwnHeightDecl = true;
        }
        // A CSS height on the cell floors its row the same way the attribute
        // does — including the unit forms an authored spacer row uses.
        // …and a lifted grid floors its row on the cell's declared height
        // too (`<td style="height:105px">` under a 85px logo keeps the
        // 20px of band below the picture).
        if ((co.uaCellBoxes || co.liftNestedTables)
            && co.tok.Attributes is not null && co.tok.Attributes.TryGetValue("style", out var tdHSt)
            && tdHSt is not null
            && Regex.Match(tdHSt, @"(?<!\w-)height\s*:\s*([\d.]+\s*(?:px|pt|cm|mm|in))",
                RegexOptions.IgnoreCase) is { Success: true } tdHm
            && TryParseLength(tdHm.Groups[1].Value.Replace(" ", "")) is { } tdHPt && tdHPt > 0)
        {
            co.ps.rowMinHeightPt = Math.Max(co.ps.rowMinHeightPt, tdHPt);
            co.ps.cellOwnHeightDecl = true;
        }
        // The legacy VALIGN attribute is `vertical-align` by another
        // spelling: an explicit `valign="top"` beats the lifted dialect's
        // centre default (a 129 pt grid was floating 10.5 pt down inside
        // its 150 pt band cell that declared top).
        // pt-styled fragment: the STYLE spelling of the same
        // (`vertical-align:top` inline on the cell).
        var uaValign = co.uaCellBoxes;
        if ((co.ptCellWidths || co.redlineCells || uaValign) && (co.ps.cell!.VerticalAlignment == VerticalAlignment.None || uaValign)
            && co.tok.Attributes is not null
            && co.tok.Attributes.TryGetValue("style", out var vaSt) && vaSt is not null
            && Regex.Match(vaSt, @"vertical-align\s*:\s*(\w+)",
                RegexOptions.IgnoreCase) is { Success: true } vaM)
            co.ps.cell.VerticalAlignment = vaM.Groups[1].Value.ToLowerInvariant() switch
            {
                "top" => VerticalAlignment.Top,
                "middle" or "center" => VerticalAlignment.Center,
                "bottom" => VerticalAlignment.Bottom,
                _ => VerticalAlignment.None,
            };
        // …and the sheet's own td rule seats every UA-boxed cell that declares no seat of its own.
        if (uaValign && co.ps.cell!.VerticalAlignment == VerticalAlignment.None
            && UaSheetCellRuleValue(co, "vertical-align") is { } tdVa)
            co.ps.cell.VerticalAlignment = tdVa.Trim().ToLowerInvariant() switch
            {
                "top" => VerticalAlignment.Top,
                "middle" or "center" => VerticalAlignment.Center,
                "bottom" => VerticalAlignment.Bottom,
                _ => VerticalAlignment.None,
            };
        if (co.liftNestedTables && co.tok.Attributes is not null
            && co.tok.Attributes.TryGetValue("valign", out var tdVaAttr)
            && co.ps.cell!.VerticalAlignment == VerticalAlignment.None)
            co.ps.cell.VerticalAlignment = tdVaAttr.Trim().ToLowerInvariant() switch
            {
                "top" => VerticalAlignment.Top,
                "middle" or "center" => VerticalAlignment.Center,
                "bottom" => VerticalAlignment.Bottom,
                _ => VerticalAlignment.None,
            };
        // …and a UA-boxed cell that declares no seat at all takes the browser's default: middle
        // (measured on the royalty statement: a four-line address centres beside a taller nested grid).
        if (uaValign && co.ps.cell!.VerticalAlignment == VerticalAlignment.None)
            co.ps.cell.VerticalAlignment = VerticalAlignment.Center;
        // …and the row's own VALIGN seats every cell that has no seat
        // of its own (over-declared grid dialect: the owner grid's
        // header labels sit at the BOTTOM of their 4-line row).
        if (co.overDeclaredDraw && co.ps.cell!.VerticalAlignment == VerticalAlignment.None
            && co.ps.rowVAlign != VerticalAlignment.None)
            co.ps.cell.VerticalAlignment = co.ps.rowVAlign;
    }

    /// <summary>A new cell opens: the row and style state reset, and the cell's colspan, rowspan, nowrap, height and background attributes are read.</summary>
    private static void OpenCellAndReadAttributes(CellOpenState co)
    {
        co.ps.row ??= new Row();
        co.ps.styleStack.Clear(); co.ps.curFontPt = co.ps.rowFontPt; co.ps.curFamily = null;
        co.ps.uaCellClasses = co.uaCellBoxes && co.tok.Attributes is not null && co.tok.Attributes.TryGetValue("class", out var uaCls) && uaCls is not null
            ? uaCls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) : null;
        if (co.uaCellBoxes && co.ps.rowLineHPt > 0 && co.ps.cellOwnLineHPt <= 0) co.ps.cellOwnLineHPt = co.ps.rowLineHPt;
        co.ps.lineFontPt = 0; co.ps.lineFamily = null; co.ps.lineStyleSet = false;
        co.ps.boldDepth = 0; co.ps.lineHadText = false; co.ps.lineAllBold = true;
        co.ps.italicDepth = 0; co.ps.lineAllItalic = true; co.ps.lineRunMarks = null;
        co.ps.cell = new Cell(); co.ps.isHeader = co.tag == "th";
        // The page sheet's `th` rule: a header cell fills with its background and seats its text by its text-align.
        if (co.ps.isHeader && co.ps.thBackground is { } thBg) co.ps.cell.BackgroundColor = thBg;
        if (co.ps.isHeader && co.ps.thAlign is { } thAl) { co.ps.alignSet = true; co.ps.cellAlign = thAl; }
        // the UA sheet centres a header cell (measured on the checkbox worksheet)
        else if (co.ps.isHeader && co.ps.uaControlGrid) { co.ps.alignSet = true; co.ps.cellAlign = HorizontalAlignment.Center; }
        co.ps.cellPendingBrBlank = false;
        co.ps.cellInlineOptions = null;
        // The cell's OWN inline font declarations open the run style its
        // content inherits — a report table styles the td directly as often
        // as it wraps the text in a span.
        co.ps.cellFgStrutPt = 0; co.ps.cellFgStrutFontPt = 0;
        if (co.tok.Attributes is not null
            && co.tok.Attributes.TryGetValue("style", out var tdFontSt) && tdFontSt is not null)
        {
            var tdFs = Regex.Match(tdFontSt, @"(?<![-\w])font-size\s*:\s*([^;""']+)",
                RegexOptions.IgnoreCase);
            // …honoured only when SMALLER than the grid's base — the same
            // deliberate limit the pitch model keeps elsewhere: an ENLARGED
            // td (a letterhead's 16.5pt line) must not reflow the whole
            // sheet, which lays out on the base rhythm.
            if (tdFs.Success && TryParseLength(tdFs.Groups[1].Value.Trim()) is { } tdFsPt
                && tdFsPt > 0 && tdFsPt < (co.ps.curFontPt > 0 ? co.ps.curFontPt : co.cellFontSize))
                co.ps.curFontPt = tdFsPt;
            // A td styling its own size re-struts its cell at that size's
            // box (the Description band's 10pt td → 16px = 12.0).
            if (co.formGridDialect && tdFs.Success
                && TryParseLength(tdFs.Groups[1].Value.Trim()) is { } tdStrutPt
                && tdStrutPt > 0)
            {
                co.ps.cellFgStrutPt = PxLinePt(tdStrutPt, VerdanaWinLineRatio);
                co.ps.cellFgStrutFontPt = tdStrutPt;
            }
            // …and a td styling font-style italic sets its whole cell
            // italic (the Description band's own td style).
            if (co.formGridDialect && Regex.IsMatch(tdFontSt,
                    @"font-style\s*:\s*italic", RegexOptions.IgnoreCase))
                co.ps.italicDepth = 1;
            var tdFf = Regex.Match(tdFontSt, @"(?<![-\w])font-family\s*:\s*([^;""']+)",
                RegexOptions.IgnoreCase);
            if (tdFf.Success && FirstFontFamily(tdFf.Groups[1].Value) is { Length: > 0 } tdFam)
                co.ps.curFamily = tdFam;
        }
        if (co.tok.Attributes?.ContainsKey("nowrap") == true) co.ps.cell.HtmlNoWrap = true;
        // A cell's own fill paints over its row's band.
        if (co.tok.Attributes is not null)
        {
            if (co.tok.Attributes.TryGetValue("style", out var tdBgSt) && tdBgSt is not null
                && Regex.Match(tdBgSt, @"background(?:-color)?\s*:\s*([^;]+)",
                    RegexOptions.IgnoreCase) is { Success: true } tdBgm
                && ParseCssColor(tdBgm.Groups[1].Value) is { } tdBg)
                co.ps.cell.BackgroundColor = tdBg;
            else if (co.tok.Attributes.TryGetValue("bgcolor", out var tdBgAttr)
                && ParseCssColor(tdBgAttr) is { } tdBgA)
                co.ps.cell.BackgroundColor = tdBgA;
            else if (co.ps.uaRowBg is { } uaRowBg) co.ps.cell.BackgroundColor = uaRowBg;
        }
        // white-space:nowrap keeps a cell on one line whether it arrives
        // inline, through one of the cell's classes, or INHERITED from the table itself
        if (co.tblStyle.TryGetValue("white-space", out var tblWs)
            && Regex.IsMatch(tblWs, @"^\s*(nowrap|pre(?![-\w]))", RegexOptions.IgnoreCase))
            co.ps.cell.HtmlNoWrap = true;
        if (co.tok.Attributes is not null)
        {
            if (co.tok.Attributes.TryGetValue("style", out var nwStyle)
                && Regex.IsMatch(nwStyle, @"white-space\s*:\s*nowrap", RegexOptions.IgnoreCase))
                co.ps.cell.HtmlNoWrap = true;
            if (!co.ps.cell.HtmlNoWrap && co.tok.Attributes.TryGetValue("class", out var nwCls))
                foreach (var cn in nwCls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    if ((co.css.TryGetValue("." + cn, out var cRule)
                            || (co.docCss?.TryGetValue("." + cn, out cRule) ?? false))
                        && cRule.TryGetValue("white-space", out var ws)
                        && ws.Contains("nowrap", StringComparison.OrdinalIgnoreCase))
                    { co.ps.cell.HtmlNoWrap = true; break; }
        }
        // …or through the sheet's ELEMENT rule for the cell's own tag (`td { white-space: nowrap }`
        // keeps every cell of the land-register order on one line, and its widest line sizes the sheet).
        if (!co.ps.cell.HtmlNoWrap
            && (ElementRule(co.css, co.tag) ?? ElementRule(co.docCss, co.tag)) is { } tagRule
            && tagRule.TryGetValue("white-space", out var tagWs)
            && Regex.IsMatch(tagWs, @"^\s*(nowrap|pre(?![-\w]))", RegexOptions.IgnoreCase))
            co.ps.cell.HtmlNoWrap = true;
    }

    /// <summary>A missing image becomes a placeholder box or nothing, a form-cell image stays pending, and a loaded image joins the cell; false when the image was consumed early.</summary>
    private static bool PlaceCellImage(CellImgState ci)
    {
        SubstituteFontShorthandBox(ci);
        if (ConsumesMissingCellImage(ci)) return false;
        SubstituteMissingCellImageBox(ci);
        // The image's DECLARED box sizes its column whether or not the
        // bytes ever arrive — a spacer GIF that fails to load still holds
        // its gutter open, the way a browser reserves a broken image's box.
        // (…unless the picture stands in a positioned wrapper: out of flow, it lends the cell nothing)
        if (ci.liftNestedTables && ci.ciw > 0 && !InPositionedInline(ci.ps))
            ci.ps.cellImgWidthPt = Math.Max(ci.ps.cellImgWidthPt, ci.ciw * PxToPtW);
        // (the placeholder's column is its framed box: the 32 pt square and its 1 pt frame each side -
        //  measured: beside the 5.5in / 1.5in columns squeezed by their slack, the masthead centres at 280)
        if (ci.uaBrokenImage) ci.ps.cellImgWidthPt = Math.Max(ci.ps.cellImgWidthPt, UaBrokenImagePt + 2);

        // Over-declared grid dialect: a SMALL image amid real cell
        // text flows INLINE (the tick bitmap inside 「 」) — the
        // reference keeps the line whole with the mark's ink in
        // place; the paragraph-image path below breaks the line
        // around it. A checkmark glyph carries the ink. Cells that
        // hold ONLY the image (the data-row tick boxes) keep the
        // real bitmap.
        if (ci.overDeclaredDraw && ci.cellImgBytes is not null
            && ci.ciw is > 0 and <= 20 && ci.cih is > 0 and <= 20
            && ci.ps.line.ToString().Replace("&nbsp;", " ").Trim((char)0xA0, ' ').Length > 0)
        {
            ci.ps.line.Append('☑');
            return false;
        }
        if (ci.cellImgBytes is { } cellImgBytes) AddCellImage(ci, cellImgBytes);
        return true;
    }

    /// <summary>A cell whose font shorthand names a picture it has no bytes for draws the
    /// browser's own empty-image box: a framed white rectangle with the small picture mark
    /// inside it.</summary>
    private static void SubstituteFontShorthandBox(CellImgState ci)
    {
        if (ci.cellImgBytes is null && ci.cellFontShorthand && ci.ciw > 4 && ci.cih > 4)
        {
            var phInv = System.Globalization.CultureInfo.InvariantCulture;
            var phSvg = "<svg xmlns='http://www.w3.org/2000/svg' width='" + ci.ciw.ToString(phInv)
                + "' height='" + ci.cih.ToString(phInv) + "'>"
                + "<rect x='0.5' y='0.5' width='" + (ci.ciw - 1).ToString(phInv)
                + "' height='" + (ci.cih - 1).ToString(phInv)
                + "' fill='white' stroke='#000000' stroke-width='1'/>"
                + "<rect x='6.5' y='" + (ci.cih / 2 - 8).ToString("0.##", phInv)
                + "' width='12' height='16' fill='white' stroke='#808080' stroke-width='1'/>"
                + "</svg>";
            ci.cellImgBytes = System.Text.Encoding.UTF8.GetBytes(phSvg);
        }
    }

    /// <summary>The dialects that spend a missing image WITHOUT drawing a box: the form gap
    /// reserve, and the alt text a UA-boxed or Word-mail cell shows inline instead.</summary>
    private static bool ConsumesMissingCellImage(CellImgState ci)
    {
        // DataWorks form dialect: a DEAD image with NO declared box
        // still occupies the hidden-inline reserve (see
        // DwHiddenInlinePt) — the help-icon column holds one, the
        // results row's folder icon another.
        if (ci.dwFormCells && ci.cellImgBytes is null
            && !ci.tok.Attributes!.ContainsKey("width")
            && (!ci.tok.Attributes.TryGetValue("style", out var dwImgSt) || dwImgSt is null
                || !Regex.IsMatch(dwImgSt, @"(?<![-\w])width\s*:", RegexOptions.IgnoreCase)))
        {
            ci.ps.line.Append(Table.InlineCheckboxGapChar);
            ci.ps.cellImgWidthPt += Table.DwHiddenInlinePt;
            ci.table.HtmlDwGapReservePt += Table.DwHiddenInlinePt;
            ci.ps.lineHadText = true;
            return true;
        }
        // A UA-boxed cell's unloadable image shows its alt text INLINE, in the running style (the
        // browser's broken-image text: "Print this page" wraps in the cell like any run); its
        // margin-right is a gap to the caption touching it, no break opportunity (measured: "page"
        // and "PRINT" wrap as one word), stood in for by a non-breaking space.
        if (ci.ps.uaCellBoxes && ci.cellImgBytes is null
            && ci.tok.Attributes!.TryGetValue("alt", out var uaAlt) && !string.IsNullOrWhiteSpace(uaAlt))
        {
            if (!ci.ps.lineStyleSet) { ci.ps.lineFontPt = ci.ps.curFontPt; ci.ps.lineFamily = ci.ps.curFamily; ci.ps.lineStyleSet = true; }
            ci.ps.line.Append(DecodeEntities(uaAlt));
            if (ci.tok.Attributes.TryGetValue("style", out var uaImgSt)
                && Regex.IsMatch(uaImgSt, @"margin-right\s*:\s*[1-9]", RegexOptions.IgnoreCase))
                ci.ps.line.Append('\u00A0');
            ci.ps.lineHadText = true;
            if (ci.ps.boldDepth == 0) ci.ps.lineAllBold = false;
            if (ci.ps.italicDepth == 0) ci.ps.lineAllItalic = false;
            return true;
        }
        // A LOCAL image whose file cannot be loaded shows its alt text in its box (the expected
        // render's broken-image behaviour: a mail signature's missing pictures name their files
        // inside the band, and the band grows to the wrapped names).
        if (ci.wordMailCells && ci.cellImgBytes is null && ci.tok.Attributes!.TryGetValue("alt", out var altText)
            && !string.IsNullOrWhiteSpace(altText)
            && ci.tok.Attributes.TryGetValue("src", out var altSrc)
            && !altSrc.TrimStart().StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            && !altSrc.TrimStart().StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            AppendWordMailAltText(ci, DecodeEntities(altText));
            return true;
        }
        return false;
    }

    /// <summary>The box a missing image still holds: a Word-mail picture's declared box kept
    /// blank, the browser's broken-image placeholder, or a remote picture's declared box.</summary>
    private static void SubstituteMissingCellImageBox(CellImgState ci)
    {
        // Word mail: a missing local picture WITHOUT alt text keeps its declared box blank
        // (the logo slots above the address column hold their height).
        if (ci.wordMailCells && ci.cellImgBytes is null && ci.ciw > 0 && ci.cih > 0
            && (!ci.tok.Attributes!.TryGetValue("alt", out var wmAlt) || string.IsNullOrWhiteSpace(wmAlt))
            && ci.tok.Attributes.TryGetValue("src", out var wmSrc)
            && !wmSrc.TrimStart().StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            && !wmSrc.TrimStart().StartsWith("http", StringComparison.OrdinalIgnoreCase))
            ci.cellImgBytes = System.Text.Encoding.UTF8.GetBytes(BlankBoxSvg(ci.ciw, ci.cih));
        // A UA-boxed cell's unloadable LOCAL image with neither alt text nor a declared size draws the
        // browser's broken-image placeholder: a 32 pt square in a 1 pt inset frame (measured on the
        // royalty statement's missing logo: the frame 96.5..129.5, #555555 top/left over #aaaaaa).
        if (ci.ps.uaCellBoxes && ci.cellImgBytes is null && ci.ciw <= 0 && ci.cih <= 0
            && (!ci.tok.Attributes!.TryGetValue("alt", out var uaNoAlt) || string.IsNullOrWhiteSpace(uaNoAlt))
            && ci.tok.Attributes.TryGetValue("src", out var uaSrc)
            && !uaSrc.TrimStart().StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            && !uaSrc.TrimStart().StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            ci.ciw = UaBrokenImagePt / PxToPt;
            ci.cih = UaBrokenImagePt / PxToPt;
            ci.cellImgBytes = System.Text.Encoding.UTF8.GetBytes(BrokenImageSvg(UaBrokenImagePt));
            ci.uaBrokenImage = true;
        }
        // …and its unloadable REMOTE image with a declared box and no alt text holds that box blank - the
        // browser reserves a broken image's declared box (the mailing's 77 px logo row stands 57.75, nothing drawn).
        if (ci.ps.uaCellBoxes && ci.cellImgBytes is null && (ci.ciw > 0 || ci.cih > 0)
            && (!ci.tok.Attributes!.TryGetValue("alt", out var uaRemAlt) || string.IsNullOrWhiteSpace(uaRemAlt))
            && ci.tok.Attributes.TryGetValue("src", out var uaRemSrc)
            && uaRemSrc.TrimStart().StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            if (ci.ciw <= 0) ci.ciw = 1;
            if (ci.cih <= 0) ci.cih = 1;
            ci.cellImgBytes = System.Text.Encoding.UTF8.GetBytes(BlankBoxSvg(ci.ciw, ci.cih));
        }
    }

    /// <summary>Adds a loaded image to the cell: at its seat when a positioned wrapper takes
    /// it out of flow, deferred behind the lines already on the cell, or straight on.</summary>
    private static void AddCellImage(CellImgState ci, byte[] cellImgBytes)
    {
            PushLine(ci.ps, ci.redlineCells, ci.dwFormCells, ci.widenProbe);
            var cellImg = new Image { ImageStream = new System.IO.MemoryStream(cellImgBytes) };
            // A cell that declares an alignment aligns its IMAGE too, not
            // only its text — an `align="right"` logo cell hangs its logo
            // on the right edge of the cell the same way a right-aligned
            // run seats there.
            if (ci.ps.alignSet) cellImg.HorizontalAlignment = ci.ps.cellAlign;
            if (ci.liftNestedTables && ci.ciw > 0 && !InPositionedInline(ci.ps))
                ci.ps.cellImgWidthPt = Math.Max(ci.ps.cellImgWidthPt, ci.ciw * PxToPtW);
            if (IsSvgBytes(cellImgBytes)) cellImg.FileType = ImageFileType.Svg;
            if (ci.ciw > 0) cellImg.FixWidth = ci.ciw * PxToPt;
            if (ci.cih > 0) cellImg.FixHeight = ci.cih * PxToPt;
            // A picture in a positioned wrapper is out of flow: it draws at its seat and the rows below run
            // on underneath it (the Word mail's chart floats over the six rows of the small grid beside it,
            // and its 11.25 pt row keeps its declared height). A floating box in flow position is that seat.
            if (ci.ciw > 0 && ci.cih > 0 && InPositionedInline(ci.ps))
            {
                var seat = new FloatingBox(ci.ciw * PxToPt, ci.cih * PxToPt) { ZIndex = 1 };
                seat.Paragraphs.Add(cellImg);
                ci.ps.cell!.Paragraphs.Add(seat);
                return;
            }
            // Text already on the cell keeps its place ABOVE the image:
            // defer the paragraph add until CloseCell flushes the lines.
            if (ci.ps.lines.Count > 0)
            {
                (ci.ps.pendingCellImgs ??= new List<Image>()).Add(cellImg);
                // Word mail keeps the picture where the markup put it, between the lines around it.
                if (ci.wordMailCells) (ci.ps.pendingCellImgAt ??= new List<int>()).Add(ci.ps.lines.Count);
            }
            else ci.ps.cell!.Paragraphs.Add(cellImg);
    }

    /// <summary>The cell image's bytes from its data URI or file, and its width and height from the attributes, the style or the image itself.</summary>
    private static void LoadCellImage(CellImgState ci, string cellSrc)
    {
        if (cellSrc.StartsWith("inline-svg:", StringComparison.Ordinal)
            && int.TryParse(cellSrc["inline-svg:".Length..], out var cellSvgIdx)
            && ci.inlineSvgs is not null && cellSvgIdx >= 0 && cellSvgIdx < ci.inlineSvgs.Count)
            ci.cellImgBytes = ci.inlineSvgs[cellSvgIdx];
        else
            ci.cellImgBytes = LoadConverterImage(cellSrc, ci.options);
        ci.ciw = 0;
        ci.cih = 0;
        if (ci.tok.Attributes!.TryGetValue("width", out var ciwS))
            double.TryParse(Regex.Match(ciwS, @"[\d.]+").Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out ci.ciw);
        if (ci.tok.Attributes.TryGetValue("height", out var cihS))
            double.TryParse(Regex.Match(cihS, @"[\d.]+").Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out ci.cih);
        // A CSS-sized cell image (style="width:240px; height:45px") is as
        // explicit as the attribute form.
        if ((ci.ciw <= 0 || ci.cih <= 0) && ci.tok.Attributes.TryGetValue("style", out var ciStyle))
        {
            var cwm = Regex.Match(ciStyle, @"(?<![-\w])width\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
            if (ci.ciw <= 0 && cwm.Success)
                double.TryParse(cwm.Groups[1].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out ci.ciw);
            var chm = Regex.Match(ciStyle, @"(?<![-\w])height\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
            if (ci.cih <= 0 && chm.Success)
                double.TryParse(chm.Groups[1].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out ci.cih);
        }
        // An image that declares NO box of its own takes the file's own
        // pixel size: a browser lays an unsized image out at its
        // intrinsic dimensions. Without this the cell reserves nothing,
        // so the column collapses and the sheet is sized as though the
        // picture were not there.
        if ((ci.ciw <= 0 && ci.cih <= 0 || ci.ps.resetSheetGrid) && (ci.ciw <= 0 || ci.cih <= 0) && ci.cellImgBytes is { Length: > 0 }
            && TryReadImagePixelSize(ci.cellImgBytes) is (var intrinsicW, var intrinsicH) && intrinsicW > 0 && intrinsicH > 0)
        {
            // …and one declared side scales the other by the picture's own aspect (a
            // `height: 7px` logo is a sliver, not its full-size bitmap).
            if (ci.ciw <= 0 && ci.cih <= 0) { ci.ciw = intrinsicW; ci.cih = intrinsicH; }
            else if (ci.ciw <= 0) ci.ciw = ci.cih * intrinsicW / intrinsicH;
            else ci.cih = ci.ciw * intrinsicH / intrinsicW;
        }
    }

    /// <summary>One class of a lifted cell: its background, border and font rules, tag-qualified or flat, with a later class overriding an earlier one.</summary>
    private static void ApplyCellClassRule(CellOpenState co, string cn)
    {
        co.bgRule = null;
        foreach (var bgSrc in new[] { co.css, co.docCss })
        {
            if (bgSrc is null) continue;
            if ((co.docElementGrid && bgSrc.TryGetValue(co.tag + "." + cn, out co.bgRule))
                || bgSrc.TryGetValue("." + cn, out co.bgRule)) break;
            co.bgRule = null;
        }
        // (a UA-boxed cell's class may sit in a comma list: `.fine-print, .footer { font-size: 12px }`)
        if (co.bgRule is null && co.uaCellBoxes)
            foreach (var bgSrc in new[] { co.css, co.docCss })
            {
                if (bgSrc is null) continue;
                foreach (var kv in bgSrc)
                {
                    if (!kv.Key.Contains(',')) continue;
                    foreach (var sel in kv.Key.Split(','))
                        if (sel.Trim() == "." + cn || sel.Trim() == co.tag + "." + cn) { co.bgRule = kv.Value; break; }
                    if (co.bgRule is not null) break;
                }
                if (co.bgRule is not null) break;
            }
        if (co.bgRule is null) return;
        // Legacy path: first class with a background wins; the
        // element-grid dialect follows the cascade instead (a
        // LATER class overrides — `class="header exhibit-name"`
        // paints the exhibit row white).
        if ((co.ps.cell!.BackgroundColor is null || co.docElementGrid)
            && (co.bgRule.TryGetValue("background-color", out var clsBg)
                || co.bgRule.TryGetValue("background", out clsBg))
            && ParseCssColor(clsBg) is { } clsBgc)
            co.ps.cell.BackgroundColor = clsBgc;
        // A class HEIGHT floors the row (`.whiteline10 { height:
        // 10px }` spacer rows) — over-declared grid dialect only.
        // The declared height is the CONTENT box: the cell padding
        // pair (the UA's 1px when none is declared) and the border
        // spacing pair (the UA's separate-borders default when the
        // table declares no cellspacing) ride on top — measured:
        // a plain spacer table is 7.5+1.5+3 = 12, a cellpadding-5
        // zero-spacing one is 7.5+7.5 = 15.
        if (co.fullWidthCjkMin && co.bgRule.TryGetValue("height", out var clsHt)
            && TryParseLength(clsHt.Trim()) is { } clsHtPt && clsHtPt > 0)
        {
            var clsRowH = clsHtPt;
            // The cell padding pair rides on the declared content
            // height; the border-spacing pair now comes from the
            // table's REAL RowSpacingPt (no double count).
            if (co.overDeclaredDraw)
                clsRowH += co.padSide > 0 ? 2 * co.padSide : 2 * UaCellPadPt;
            if (clsRowH > co.ps.rowMinHeightPt) co.ps.rowMinHeightPt = clsRowH;
        }
        if (!ApplyClassBorderRules(co)) return;
        if (!co.ps.alignSet && co.bgRule.TryGetValue("text-align", out var clsTa))
        {
            var caF = clsTa.Trim().ToLowerInvariant() switch
            {
                "right" => HorizontalAlignment.Right,
                "center" => HorizontalAlignment.Center,
                "left" => HorizontalAlignment.Left,
                _ => (HorizontalAlignment?)null,
            };
            if (caF is { } caFv) { co.ps.alignSet = true; co.ps.cellAlign = caFv; }
        }
        if (co.ps.cellChainColor is null && co.bgRule.TryGetValue("color", out var clsCo)
            && ParseCssColor(clsCo) is { } clsCoc)
            co.ps.cellChainColor = clsCoc;
        if (co.ps.cellClassPt <= 0 && co.bgRule.TryGetValue("font-size", out var clsFs)
            && ChainLenPt(clsFs, co.cellFontSize) is > 0 and var clsFsPt)
            co.ps.cellClassPt = clsFsPt;
        if (co.ps.cellCssPadPt <= 0 && co.bgRule.TryGetValue("padding", out var clsPad))
        {
            var clsPadBase = co.ps.cellClassPt > 0 ? co.ps.cellClassPt : co.cellFontSize;
            var (fpT, fpR, fpB, fpL) = ChainPadPt(clsPad, clsPadBase);
            if (fpL + fpR > 0) { co.ps.cellCssPadPt = fpL + fpR; co.ps.cellPadLeftPt = fpL; }
            co.ps.cellChainPadTopPt = Math.Max(co.ps.cellChainPadTopPt, fpT);
            co.ps.cellChainPadBotPt = Math.Max(co.ps.cellChainPadBotPt, fpB);
        }
    }

    /// <summary>A class's border rules on a lifted cell: the shorthand border, the per-side borders and their colour; false when the element grid keeps its own borders.</summary>
    private static bool ApplyClassBorderRules(CellOpenState co)
    {
        if (co.ps.cell!.Border is null && co.bgRule!.TryGetValue("border", out var clsBrd))
        {
            // An explicit ZERO border opts the cell out of the
            // table's default box (`td.no-border { border: 0px }`)
            // — element-grid dialect only.
            var clsBrdT = clsBrd.Trim();
            if (co.docElementGrid
                && (clsBrdT.StartsWith("0", StringComparison.Ordinal)
                    || clsBrdT.IndexOf("none", StringComparison.OrdinalIgnoreCase) >= 0))
                co.ps.cell.Border = new BorderInfo(BorderSide.None);
            else if (ChainBorder(clsBrd) is { } clsBi)
                co.ps.cell.Border = clsBi;
        }
        if (!co.docElementGrid)
        {
            if (co.ps.cell.BackgroundColor is not null) return false;
            return false;
        }
        // Longhand SIDES on the class (`td.yes-border { border-top:
        // 1px solid #000; border-left: 0px }`) box only the sides
        // that declare a visible stroke.
        if (co.ps.cell.Border is null
            && (co.bgRule!.ContainsKey("border-top") || co.bgRule!.ContainsKey("border-bottom")
                || co.bgRule.ContainsKey("border-left") || co.bgRule.ContainsKey("border-right")))
        {
            BorderSide clsSides = 0; double clsW = 0; Color? clsCol = null;
            foreach (var (bprop, bside) in new[]
            {
                ("border-left", BorderSide.Left), ("border-top", BorderSide.Top),
                ("border-bottom", BorderSide.Bottom), ("border-right", BorderSide.Right),
            })
                if (co.bgRule.TryGetValue(bprop, out var sv)
                    && ChainBorder(sv) is { } sbi)
                {
                    clsSides |= bside;
                    if (sbi.Width > clsW) clsW = sbi.Width;
                    clsCol ??= sbi.Color;
                }
            co.ps.cell.Border = clsSides != 0
                ? new BorderInfo(clsSides, clsW <= 0 ? 0.75 : clsW,
                    clsCol ?? Color.Black)
                : new BorderInfo(BorderSide.None);
        }
        return true;
    }

}
