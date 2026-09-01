using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>One arm of BuildTableFromHtml's token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void HandleCellOpen(TableParseState ps, TableColumnModel colModel, Table table, Token tok, string tag, HtmlLoadOptions? options, double cellFontSize, bool dwFormCells, bool fullWidthCjkMin, bool widenProbe, bool redlineCells, bool breakAnywhereDoc, bool cellFontShorthand, List<CssElem>? chainBase, double chainSpacingPt, List<(string Tag, int PrevBoldDepth)> chainUnbold, string? cssBaseFamily, double cssBasePt, string? defaultCellFace, double formGridStrutDropPt, bool hasBorder, double inlineFaceRatio, bool overDeclaredDraw, double padSide, bool uaDocGrid, bool uaSerifMin, bool ptCellWidths, bool bandDialect, double cellLineHeightPt, string? cssRunFace, bool formGridDialect, double formGridStrutPt, bool liftNestedTables, bool tightExtras, bool uaCellBoxes, double borderWidth, double pad, Dictionary<string, Dictionary<string, string>> css, IReadOnlyDictionary<string, Dictionary<string, string>>? docCss, List<CssChainRule>? chainRules, List<CssElem>? cssAncestors, List<byte[]>? inlineSvgs, List<string> nestedHtml, Func<string, bool, Aspose.Pdf.Forms.RadioButtonOptionField>? makeRadio, double availWidthPt, double defaultCellFontPt, Dictionary<string, string> tblStyle, bool docElementGrid, bool pinnedBodyGrid, bool authoredCellChrome, bool chainBorderSeparate, bool elemRuleBorder)
    {
        if (ps.tableDepth > 1) return;   // nested cell: text flows into the host cell
        if (ps.cell is not null) CloseCell(ps, colModel, table, options, cellFontSize, dwFormCells, fullWidthCjkMin, breakAnywhereDoc, cellFontShorthand, chainBase, chainSpacingPt, chainUnbold, cssBaseFamily, cssBasePt, defaultCellFace, formGridStrutDropPt, hasBorder, inlineFaceRatio, overDeclaredDraw, padSide, uaDocGrid, widenProbe, uaSerifMin, ptCellWidths, redlineCells, bandDialect, cellLineHeightPt, cssRunFace, formGridDialect, formGridStrutPt, liftNestedTables, tightExtras, uaCellBoxes, borderWidth, pad);
        ps.row ??= new Row();
        ps.styleStack.Clear(); ps.curFontPt = ps.rowFontPt; ps.curFamily = null;
        ps.lineFontPt = 0; ps.lineFamily = null; ps.lineStyleSet = false;
        ps.boldDepth = 0; ps.lineHadText = false; ps.lineAllBold = true;
        ps.italicDepth = 0; ps.lineAllItalic = true; ps.lineRunMarks = null;
        ps.cell = new Cell(); ps.isHeader = tag == "th";
        ps.cellPendingBrBlank = false;
        ps.cellInlineOptions = null;
        // The cell's OWN inline font declarations open the run style its
        // content inherits — a report table styles the td directly as often
        // as it wraps the text in a span.
        ps.cellFgStrutPt = 0; ps.cellFgStrutFontPt = 0;
        if (tok.Attributes is not null
            && tok.Attributes.TryGetValue("style", out var tdFontSt) && tdFontSt is not null)
        {
            var tdFs = Regex.Match(tdFontSt, @"(?<![-\w])font-size\s*:\s*([^;""']+)",
                RegexOptions.IgnoreCase);
            // …honoured only when SMALLER than the grid's base — the same
            // deliberate limit the pitch model keeps elsewhere: an ENLARGED
            // td (a letterhead's 16.5pt line) must not reflow the whole
            // sheet, which lays out on the base rhythm.
            if (tdFs.Success && TryParseLength(tdFs.Groups[1].Value.Trim(), out var tdFsPt)
                && tdFsPt > 0 && tdFsPt < (ps.curFontPt > 0 ? ps.curFontPt : cellFontSize))
                ps.curFontPt = tdFsPt;
            // A td styling its own size re-struts its cell at that size's
            // box (the Description band's 10pt td → 16px = 12.0).
            if (formGridDialect && tdFs.Success
                && TryParseLength(tdFs.Groups[1].Value.Trim(), out var tdStrutPt)
                && tdStrutPt > 0)
            {
                ps.cellFgStrutPt = PxLinePt(tdStrutPt, VerdanaWinLineRatio);
                ps.cellFgStrutFontPt = tdStrutPt;
            }
            // …and a td styling font-style italic sets its whole cell
            // italic (the Description band's own td style).
            if (formGridDialect && Regex.IsMatch(tdFontSt,
                    @"font-style\s*:\s*italic", RegexOptions.IgnoreCase))
                ps.italicDepth = 1;
            var tdFf = Regex.Match(tdFontSt, @"(?<![-\w])font-family\s*:\s*([^;""']+)",
                RegexOptions.IgnoreCase);
            if (tdFf.Success && FirstFontFamily(tdFf.Groups[1].Value) is { Length: > 0 } tdFam)
                ps.curFamily = tdFam;
        }
        if (tok.Attributes?.ContainsKey("nowrap") == true) ps.cell.HtmlNoWrap = true;
        // A cell's own fill paints over its row's band.
        if (tok.Attributes is not null)
        {
            if (tok.Attributes.TryGetValue("style", out var tdBgSt) && tdBgSt is not null
                && Regex.Match(tdBgSt, @"background(?:-color)?\s*:\s*([^;]+)",
                    RegexOptions.IgnoreCase) is { Success: true } tdBgm
                && ParseCssColor(tdBgm.Groups[1].Value) is { } tdBg)
                ps.cell.BackgroundColor = tdBg;
            else if (tok.Attributes.TryGetValue("bgcolor", out var tdBgAttr)
                && ParseCssColor(tdBgAttr) is { } tdBgA)
                ps.cell.BackgroundColor = tdBgA;
        }
        // white-space:nowrap keeps a cell on one line whether it arrives
        // inline or through one of the cell's classes
        if (tok.Attributes is not null)
        {
            if (tok.Attributes.TryGetValue("style", out var nwStyle)
                && Regex.IsMatch(nwStyle, @"white-space\s*:\s*nowrap", RegexOptions.IgnoreCase))
                ps.cell.HtmlNoWrap = true;
            if (!ps.cell.HtmlNoWrap && tok.Attributes.TryGetValue("class", out var nwCls))
                foreach (var cn in nwCls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    if ((css.TryGetValue("." + cn, out var cRule)
                            || (docCss?.TryGetValue("." + cn, out cRule) ?? false))
                        && cRule.TryGetValue("white-space", out var ws)
                        && ws.Contains("nowrap", StringComparison.OrdinalIgnoreCase))
                    { ps.cell.HtmlNoWrap = true; break; }
        }
        // The legacy ALIGN attribute aligns the cell's own content, exactly
        // like a `text-align` in its style (which, parsed below, still wins).
        if ((liftNestedTables || uaCellBoxes || authoredCellChrome || formGridDialect)
            && tok.Attributes is not null
            && tok.Attributes.TryGetValue("align", out var tdAl)
            && ParseAlignAttr(tdAl) is { } tdAlign)
        { ps.alignSet = true; ps.cellAlign = tdAlign; }
        // A cell HEIGHT="N" (px) is an HTML minimum on its row's height.
        if (tok.Attributes is not null && tok.Attributes.TryGetValue("height", out var tdH)
            && double.TryParse(Regex.Match(tdH, @"[\d.]+").Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var tdHpx) && tdHpx > 0)
        {
            ps.rowMinHeightPt = Math.Max(ps.rowMinHeightPt, tdHpx * PxToPt);
            ps.cellOwnHeightDecl = true;
        }
        // A CSS height on the cell floors its row the same way the attribute
        // does — including the unit forms an authored spacer row uses.
        // …and a lifted grid floors its row on the cell's declared height
        // too (`<td style="height:105px">` under a 85px logo keeps the
        // 20px of band below the picture).
        if ((uaCellBoxes || liftNestedTables)
            && tok.Attributes is not null && tok.Attributes.TryGetValue("style", out var tdHSt)
            && tdHSt is not null
            && Regex.Match(tdHSt, @"(?<!\w-)height\s*:\s*([\d.]+\s*(?:px|pt|cm|mm|in))",
                RegexOptions.IgnoreCase) is { Success: true } tdHm
            && TryParseLength(tdHm.Groups[1].Value.Replace(" ", ""), out var tdHPt) && tdHPt > 0)
        {
            ps.rowMinHeightPt = Math.Max(ps.rowMinHeightPt, tdHPt);
            ps.cellOwnHeightDecl = true;
        }
        // The legacy VALIGN attribute is `vertical-align` by another
        // spelling: an explicit `valign="top"` beats the lifted dialect's
        // centre default (a 129 pt grid was floating 10.5 pt down inside
        // its 150 pt band cell that declared top).
        // pt-styled fragment: the STYLE spelling of the same
        // (`vertical-align:top` inline on the cell).
        if ((ptCellWidths || redlineCells) && ps.cell.VerticalAlignment == VerticalAlignment.None
            && tok.Attributes is not null
            && tok.Attributes.TryGetValue("style", out var vaSt) && vaSt is not null
            && Regex.Match(vaSt, @"vertical-align\s*:\s*(\w+)",
                RegexOptions.IgnoreCase) is { Success: true } vaM)
            ps.cell.VerticalAlignment = vaM.Groups[1].Value.ToLowerInvariant() switch
            {
                "top" => VerticalAlignment.Top,
                "middle" or "center" => VerticalAlignment.Center,
                "bottom" => VerticalAlignment.Bottom,
                _ => VerticalAlignment.None,
            };
        if (liftNestedTables && tok.Attributes is not null
            && tok.Attributes.TryGetValue("valign", out var tdVaAttr)
            && ps.cell.VerticalAlignment == VerticalAlignment.None)
            ps.cell.VerticalAlignment = tdVaAttr.Trim().ToLowerInvariant() switch
            {
                "top" => VerticalAlignment.Top,
                "middle" or "center" => VerticalAlignment.Center,
                "bottom" => VerticalAlignment.Bottom,
                _ => VerticalAlignment.None,
            };
        // …and the row's own VALIGN seats every cell that has no seat
        // of its own (over-declared grid dialect: the owner grid's
        // header labels sit at the BOTTOM of their 4-line row).
        if (overDeclaredDraw && ps.cell.VerticalAlignment == VerticalAlignment.None
            && ps.rowVAlign != VerticalAlignment.None)
            ps.cell.VerticalAlignment = ps.rowVAlign;
        ps.cellBold = false;
        ps.cellClassPt = 0;
        if (tok.Attributes is not null
            && tok.Attributes.TryGetValue("class", out var szCls))
            foreach (var cn in szCls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                Dictionary<string, string>? szRule = null;
                if (!css.TryGetValue("." + cn, out szRule)) docCss?.TryGetValue("." + cn, out szRule);
                if (szRule is null || !szRule.TryGetValue("font-size", out var szv)) continue;
                // rem/em sizes resolve through the length parser — the
                // pt/px regex below reads ".875rem" as bare 0.875 POINTS
                // and the whole grid draws at ant size. Bare numbers keep
                // their legacy points reading.
                if (Regex.IsMatch(szv, @"r?em", RegexOptions.IgnoreCase)
                    && TryParseLength(szv.Trim(), out var szRelPt) && szRelPt > 0)
                {
                    ps.cellClassPt = szRelPt;
                    break;
                }
                var szm = Regex.Match(szv, @"([\d.]+)\s*(pt|px)?", RegexOptions.IgnoreCase);
                if (!szm.Success || !double.TryParse(szm.Groups[1].Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var szn) || szn <= 0)
                    continue;
                ps.cellClassPt = szm.Groups[2].Value.Equals("px", StringComparison.OrdinalIgnoreCase)
                    ? szn * 0.75 : szn;
                break;
            }
        if (tok.Attributes is not null)
        {
            if (tok.Attributes.TryGetValue("style", out var bStyle)
                && Regex.IsMatch(bStyle, @"font-weight\s*:\s*(bold|[6-9]00)", RegexOptions.IgnoreCase))
                ps.cellBold = true;
            if (!ps.cellBold && tok.Attributes.TryGetValue("class", out var bCls))
                foreach (var cn in bCls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    if ((css.TryGetValue("." + cn, out var bRule)
                            || (docCss?.TryGetValue("." + cn, out bRule) ?? false))
                        && bRule.TryGetValue("font-weight", out var fw)
                        && Regex.IsMatch(fw, @"bold|[6-9]00", RegexOptions.IgnoreCase))
                    { ps.cellBold = true; break; }
        }
        ps.rowHasCell = true; if (tag == "td") ps.rowHasTd = true;
        ps.cellWidthPt = ResolveCellWidthPt(tok.Attributes, css, contentBox: uaCellBoxes,
            readWidthAttr: liftNestedTables) * PxToPtW;
        // pt-styled fragment: the cell's inline pt width IS the
        // column width (already in points — no px scale).
        if (ptCellWidths && ps.cellWidthPt <= 0
            && tok.Attributes is not null
            && tok.Attributes.TryGetValue("style", out var ptwSt) && ptwSt is not null
            && Regex.Match(ptwSt, @"(?<![-\w])width\s*:\s*([\d.]+)\s*pt",
                RegexOptions.IgnoreCase) is { Success: true } ptwM
            && double.TryParse(ptwM.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var ptwV)
            && ptwV > 0)
            ps.cellWidthPt = ptwV;
        // A class width in the DOCUMENT sheet fixes the column too — the
        // fragment map is empty when the rules live in the page's own
        // <style> block — and the selector may be TAG-QUALIFIED
        // (`td.single { width: 82px }`), a key the bare-class lookup misses.
        if (ps.cellWidthPt <= 0 && docElementGrid && tok.Attributes is not null
            && tok.Attributes.TryGetValue("class", out var wCls))
            foreach (var cn in wCls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                Dictionary<string, string>? wRule = null;
                foreach (var wSrc in new[] { css, docCss })
                {
                    if (wSrc is null) continue;
                    if (wSrc.TryGetValue(tag + "." + cn, out wRule)
                        || wSrc.TryGetValue("." + cn, out wRule)) break;
                    wRule = null;
                }
                if (wRule is not null && wRule.TryGetValue("width", out var wV)
                    && !wV.Contains('%')
                    && TryParseLength(wV.Trim(), out var wPtv) && wPtv > 0)
                {
                    ps.cellWidthPt = wPtv / PxToPt * PxToPtW;
                    break;
                }
            }
        // The cell's own CSS padding is part of its column footprint — it
        // rides on the measured content, and on a fixed-width inner div.
        ps.cellCssPadPt = 0; ps.cellFixedDivPt = 0; ps.cellPadLeftPt = 0;
        // …and a lifted grid reads it too: an image column's `padding-right`
        // is the gutter between it and the text column beside it, and its
        // `padding-bottom` the gap under each picture in a stack of them.
        if (liftNestedTables
            && tok.Attributes is not null && tok.Attributes.TryGetValue("style", out var tdVSt) && tdVSt is not null)
        {
            foreach (Match pm in Regex.Matches(tdVSt,
                @"padding-(top|bottom)\s*:\s*(\d+(?:\.\d+)?)\s*px", RegexOptions.IgnoreCase))
            {
                var vPadPt = double.Parse(pm.Groups[2].Value,
                    System.Globalization.CultureInfo.InvariantCulture) * PxToPt;
                if (pm.Groups[1].Value.Equals("top", StringComparison.OrdinalIgnoreCase))
                    ps.cellChainPadTopPt = Math.Max(ps.cellChainPadTopPt, vPadPt);
                else
                {
                    ps.cellChainPadBotPt = Math.Max(ps.cellChainPadBotPt, vPadPt);
                    // A DECLARED zero overrides the table's cellpadding
                    // (the filing shell's `padding-bottom: 0px` host
                    // cells) — over-declared grid dialect only.
                    if (overDeclaredDraw && vPadPt <= 0) ps.cellVPadZeroBot = true;
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
                ps.cellChainPadTopPt = Math.Max(ps.cellChainPadTopPt, pshPx * PxToPt);
                ps.cellChainPadBotPt = Math.Max(ps.cellChainPadBotPt, pshPx * PxToPt);
            }
        }
        if ((uaCellBoxes || liftNestedTables)
            && tok.Attributes is not null && tok.Attributes.TryGetValue("style", out var tdSt2) && tdSt2 is not null)
            foreach (Match pm in Regex.Matches(tdSt2,
                @"padding-(left|right)\s*:\s*(\d+(?:\.\d+)?)\s*px", RegexOptions.IgnoreCase))
            {
                var padPt = double.Parse(pm.Groups[2].Value,
                    System.Globalization.CultureInfo.InvariantCulture) * PxToPtW;
                ps.cellCssPadPt += padPt;
                if (pm.Groups[1].Value.Equals("left", StringComparison.OrdinalIgnoreCase))
                    ps.cellPadLeftPt += padPt;
            }
        // pt-styled fragment: the SAME horizontal pads in the pt
        // spelling — the column is the declared width plus its own
        // pads (content-box), and the left pad indents the text.
        if ((ptCellWidths || (redlineCells && !widenProbe))
            && tok.Attributes is not null && tok.Attributes.TryGetValue("style", out var tdStPt) && tdStPt is not null)
            foreach (Match pm in Regex.Matches(tdStPt,
                @"padding-(left|right)\s*:\s*(\d+(?:\.\d+)?)\s*pt", RegexOptions.IgnoreCase))
            {
                var padPt = double.Parse(pm.Groups[2].Value,
                    System.Globalization.CultureInfo.InvariantCulture);
                ps.cellCssPadPt += padPt;
                if (pm.Groups[1].Value.Equals("left", StringComparison.OrdinalIgnoreCase))
                    ps.cellPadLeftPt += padPt;
            }
        if (tok.Attributes is not null
            && tok.Attributes.TryGetValue("width", out var wPctAttr)
            && wPctAttr.Trim().EndsWith('%')
            && double.TryParse(wPctAttr.Trim().TrimEnd('%'),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var wPct)
            && wPct > 0)
            ps.cellWidthPct = wPct;
        // A pixel width attribute ("25px" / "25") beside the percent row:
        // tracked for the over-declared fixed-grid demand — the legacy
        // cellWidthPt path deliberately ignores the attribute form.
        if (tok.Attributes is not null
            && tok.Attributes.TryGetValue("width", out var wPxAttr)
            && !wPxAttr.Trim().EndsWith('%'))
        {
            var wpx = wPxAttr.Trim();
            if (wpx.EndsWith("px", StringComparison.OrdinalIgnoreCase)) wpx = wpx[..^2].Trim();
            if (double.TryParse(wpx, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var wPxV) && wPxV > 0)
            {
                ps.rowPxSum += wPxV * PxToPt;
                ps.rowPxCells++;
                // The dialect honours the attribute as the column's
                // declared width (the 62px logo/spacer columns) — the
                // legacy cellWidthPt path deliberately ignores it.
                if (overDeclaredDraw && ps.cellWidthPt <= 0)
                    ps.cellWidthPt = wPxV * PxToPt;
            }
        }
        // An inline style="width: N%" declares the same percent grid the
        // width attribute does.
        if (ps.cellWidthPct <= 0 && tok.Attributes is not null
            && tok.Attributes.TryGetValue("style", out var wPctStyle))
        {
            var pm = Regex.Match(wPctStyle, @"width\s*:\s*(\d+(?:\.\d+)?)\s*%");
            if (pm.Success && double.TryParse(pm.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var wPctS)
                && wPctS > 0)
                ps.cellWidthPct = wPctS;
        }
        if (tok.Attributes is not null)
        {
            if (tok.Attributes.TryGetValue("colspan", out var cs) && int.TryParse(cs, out var csn) && csn > 0)
                ps.colSpan = csn;
            if (tok.Attributes.TryGetValue("rowspan", out var rs) && int.TryParse(rs, out var rsn) && rsn > 1)
                ps.cellRowSpan = rsn;
            if (tok.Attributes.TryGetValue("style", out var st))
            {
                // A cell opting out of the table's borders keeps its box
                // blank (`<td style="border-style:none">` in a bordered
                // table — the layout-table idiom).
                if (Regex.IsMatch(st, @"border(-style)?\s*:\s*none", RegexOptions.IgnoreCase))
                    ps.cell.Border = new BorderInfo(BorderSide.None);
                var am = Regex.Match(st, @"text-align\s*:\s*(left|right|center)", RegexOptions.IgnoreCase);
                if (am.Success)
                {
                    ps.alignSet = true;
                    ps.cellAlign = am.Groups[1].Value.ToLowerInvariant() switch
                    {
                        "right" => HorizontalAlignment.Right,
                        "center" => HorizontalAlignment.Center,
                        _ => HorizontalAlignment.Left,
                    };
                }
                // Band-dialect per-cell border sides (BORDER-LEFT:1px solid #000…):
                // proxy-card notice frames, corner marks and signature rules are
                // drawn as TD border sides.
                if (bandDialect || authoredCellChrome || ptCellWidths)
                {
                    BorderSide bsSides = 0; double bsW = 0; Color? bsColor = null;
                    foreach (var (bprop, bside) in new[]
                    {
                        ("border-left", BorderSide.Left), ("border-top", BorderSide.Top),
                        ("border-bottom", BorderSide.Bottom), ("border-right", BorderSide.Right),
                    })
                    {
                        if (!TryParseBorderShorthand(st, bprop, out var bpt, out var bcol))
                        {
                            // The pt-styled fragment spells each side as
                            // LONGHANDS (border-bottom-width/-style/-color).
                            if (!ptCellWidths
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
                        ps.cell.Border = new BorderInfo(bsSides, bsW <= 0 ? 0.75 : bsW,
                            bsColor ?? Color.Black);
                        if (ptCellWidths && bsW > colModel.ptMaxCellBorderW)
                            colModel.ptMaxCellBorderW = bsW;
                    }
                }
            }
        }
        // Flat class rules on the cell (`.resulttableheadercelltables
        // { background-color: silver; border: 1px solid white }`) —
        // the grey header cells/columns of the report grids. Lifted
        // dialect only; legacy paths never read class backgrounds.
        // The selector may be TAG-QUALIFIED (`td.no-border { … }`), and a
        // LATER class overrides an earlier one's background (the cascade:
        // `class="header exhibit-name"` paints the exhibit row white).
        if (liftNestedTables && tok.Attributes is not null
            && tok.Attributes.TryGetValue("class", out var bgCls))
            foreach (var cn in bgCls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                Dictionary<string, string>? bgRule = null;
                foreach (var bgSrc in new[] { css, docCss })
                {
                    if (bgSrc is null) continue;
                    if ((docElementGrid && bgSrc.TryGetValue(tag + "." + cn, out bgRule))
                        || bgSrc.TryGetValue("." + cn, out bgRule)) break;
                    bgRule = null;
                }
                if (bgRule is null) continue;
                // Legacy path: first class with a background wins; the
                // element-grid dialect follows the cascade instead (a
                // LATER class overrides — `class="header exhibit-name"`
                // paints the exhibit row white).
                if ((ps.cell.BackgroundColor is null || docElementGrid)
                    && (bgRule.TryGetValue("background-color", out var clsBg)
                        || bgRule.TryGetValue("background", out clsBg))
                    && ParseCssColor(clsBg) is { } clsBgc)
                    ps.cell.BackgroundColor = clsBgc;
                // A class HEIGHT floors the row (`.whiteline10 { height:
                // 10px }` spacer rows) — over-declared grid dialect only.
                // The declared height is the CONTENT box: the cell padding
                // pair (the UA's 1px when none is declared) and the border
                // spacing pair (the UA's separate-borders default when the
                // table declares no cellspacing) ride on top — measured:
                // a plain spacer table is 7.5+1.5+3 = 12, a cellpadding-5
                // zero-spacing one is 7.5+7.5 = 15.
                if (fullWidthCjkMin && bgRule.TryGetValue("height", out var clsHt)
                    && TryParseLength(clsHt.Trim(), out var clsHtPt) && clsHtPt > 0)
                {
                    var clsRowH = clsHtPt;
                    // The cell padding pair rides on the declared content
                    // height; the border-spacing pair now comes from the
                    // table's REAL RowSpacingPt (no double count).
                    if (overDeclaredDraw)
                        clsRowH += padSide > 0 ? 2 * padSide : 2 * UaCellPadPt;
                    if (clsRowH > ps.rowMinHeightPt) ps.rowMinHeightPt = clsRowH;
                }
                if (ps.cell.Border is null && bgRule.TryGetValue("border", out var clsBrd))
                {
                    // An explicit ZERO border opts the cell out of the
                    // table's default box (`td.no-border { border: 0px }`)
                    // — element-grid dialect only.
                    var clsBrdT = clsBrd.Trim();
                    if (docElementGrid
                        && (clsBrdT.StartsWith("0", StringComparison.Ordinal)
                            || clsBrdT.IndexOf("none", StringComparison.OrdinalIgnoreCase) >= 0))
                        ps.cell.Border = new BorderInfo(BorderSide.None);
                    else if (ChainBorder(clsBrd) is { } clsBi)
                        ps.cell.Border = clsBi;
                }
                if (!docElementGrid)
                {
                    if (ps.cell.BackgroundColor is not null) break;
                    continue;
                }
                // Longhand SIDES on the class (`td.yes-border { border-top:
                // 1px solid #000; border-left: 0px }`) box only the sides
                // that declare a visible stroke.
                if (ps.cell.Border is null
                    && (bgRule.ContainsKey("border-top") || bgRule.ContainsKey("border-bottom")
                        || bgRule.ContainsKey("border-left") || bgRule.ContainsKey("border-right")))
                {
                    BorderSide clsSides = 0; double clsW = 0; Color? clsCol = null;
                    foreach (var (bprop, bside) in new[]
                    {
                        ("border-left", BorderSide.Left), ("border-top", BorderSide.Top),
                        ("border-bottom", BorderSide.Bottom), ("border-right", BorderSide.Right),
                    })
                        if (bgRule.TryGetValue(bprop, out var sv)
                            && ChainBorder(sv) is { } sbi)
                        {
                            clsSides |= bside;
                            if (sbi.Width > clsW) clsW = sbi.Width;
                            clsCol ??= sbi.Color;
                        }
                    ps.cell.Border = clsSides != 0
                        ? new BorderInfo(clsSides, clsW <= 0 ? 0.75 : clsW,
                            clsCol ?? Color.Black)
                        : new BorderInfo(BorderSide.None);
                }
                if (!ps.alignSet && bgRule.TryGetValue("text-align", out var clsTa))
                {
                    var caF = clsTa.Trim().ToLowerInvariant() switch
                    {
                        "right" => HorizontalAlignment.Right,
                        "center" => HorizontalAlignment.Center,
                        "left" => HorizontalAlignment.Left,
                        _ => (HorizontalAlignment?)null,
                    };
                    if (caF is { } caFv) { ps.alignSet = true; ps.cellAlign = caFv; }
                }
                if (ps.cellChainColor is null && bgRule.TryGetValue("color", out var clsCo)
                    && ParseCssColor(clsCo) is { } clsCoc)
                    ps.cellChainColor = clsCoc;
                if (ps.cellClassPt <= 0 && bgRule.TryGetValue("font-size", out var clsFs)
                    && ChainLenPt(clsFs, cellFontSize) is > 0 and var clsFsPt)
                    ps.cellClassPt = clsFsPt;
                if (ps.cellCssPadPt <= 0 && bgRule.TryGetValue("padding", out var clsPad))
                {
                    var clsPadBase = ps.cellClassPt > 0 ? ps.cellClassPt : cellFontSize;
                    var (fpT, fpR, fpB, fpL) = ChainPadPt(clsPad, clsPadBase);
                    if (fpL + fpR > 0) { ps.cellCssPadPt = fpL + fpR; ps.cellPadLeftPt = fpL; }
                    ps.cellChainPadTopPt = Math.Max(ps.cellChainPadTopPt, fpT);
                    ps.cellChainPadBotPt = Math.Max(ps.cellChainPadBotPt, fpB);
                }
            }
        // Chain-selector styling for this cell — the least specific
        // layer: every inline/attribute handler above already had its
        // say, so only the still-unset slots fill.
        if (chainBase is not null)
        {
            ps.chainTdElem = ChainTokElem(tag, tok.Attributes);
            ps.chainOpenElems?.Clear();
            var tdChain = new List<CssElem>(chainBase) { ps.chainTdElem };
            if (MatchChainDecls(chainRules, tdChain) is { } cd)
            {
                // The stylesheet reaches this grid's cells (see
                // Table.HtmlChainStyledCells).
                table.HtmlChainStyledCells = true;
                // Font first: the ex/em pads below resolve on the cell size.
                if (ps.cellClassPt <= 0 && cd.TryGetValue("font-size", out var cfs))
                {
                    var pcm = Regex.Match(cfs.Trim(), @"^([\d.]+)\s*%$");
                    if (pcm.Success && double.TryParse(pcm.Groups[1].Value,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var fsPct)
                        && fsPct > 0)
                        ps.cellClassPt = cellFontSize * fsPct / 100.0;
                    else if (ChainLenPt(cfs, cellFontSize) is > 0 and var fsAbs)
                        ps.cellClassPt = fsAbs;
                }
                if (!ps.cellBold && cd.TryGetValue("font-weight", out var cfw)
                    && Regex.IsMatch(cfw, @"bold|[6-9]00", RegexOptions.IgnoreCase))
                    ps.cellBold = true;
                if (ps.cell.BackgroundColor is null
                    && (cd.TryGetValue("background-color", out var cbg)
                        || cd.TryGetValue("background", out cbg))
                    && ParseCssColor(cbg) is { } cbgc)
                    ps.cell.BackgroundColor = cbgc;
                if (cd.TryGetValue("color", out var ccol) && ParseCssColor(ccol) is { } ccolc)
                    ps.cellChainColor = ccolc;
                if (ps.cell.Border is null && cd.TryGetValue("border", out var cbord)
                    && ChainBorder(cbord) is { } cbi)
                {
                    // Separate borders: the UA border-spacing shows as
                    // extra stroke between the cells' individual borders —
                    // but ONLY for white separator strokes (the Managers
                    // grid); a real coloured border (the detail buttons'
                    // 1px gray) keeps its declared width.
                    var cbWhite = cbi.Color is { R: > 240, G: > 240, B: > 240 };
                    var cbEff = chainBorderSeparate && cbWhite
                        ? new BorderInfo(BorderSide.Box,
                            cbi.Width + SeparateBorderSpacingPt, cbi.Color)
                        : cbi;
                    // border-radius rounds the cell's box (the detail
                    // buttons); the bg fill follows it at draw.
                    if (cd.TryGetValue("border-radius", out var cbr)
                        && ChainLenPt(cbr, ps.cellClassPt > 0 ? ps.cellClassPt : cellFontSize)
                            is > 0 and var cbrPt)
                        cbEff.RoundedBorderRadius = cbrPt;
                    ps.cell.Border = cbEff;
                }
                if (!ps.alignSet && cd.TryGetValue("text-align", out var cta))
                {
                    var ca = cta.Trim().ToLowerInvariant() switch
                    {
                        "right" => HorizontalAlignment.Right,
                        "center" => HorizontalAlignment.Center,
                        "left" => HorizontalAlignment.Left,
                        _ => (HorizontalAlignment?)null,
                    };
                    if (ca is { } cav) { ps.alignSet = true; ps.cellAlign = cav; }
                }
                if (cd.TryGetValue("white-space", out var cws)
                    && cws.Contains("nowrap", StringComparison.OrdinalIgnoreCase))
                    ps.cell.HtmlNoWrap = true;
                // A chain rule's percent width declares the column share
                // (`.CategoryName { width: 80% }` — the pill grid's name
                // column absorbs the slack, the detail box hugs its text).
                if (ps.cellWidthPct <= 0 && cd.TryGetValue("width", out var cwv2)
                    && cwv2.TrimEnd().EndsWith("%", StringComparison.Ordinal)
                    && double.TryParse(cwv2.Trim().TrimEnd('%'),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var cwPct)
                    && cwPct > 0)
                    ps.cellWidthPct = cwPct;
                if (ps.cell.VerticalAlignment == VerticalAlignment.None
                    && cd.TryGetValue("vertical-align", out var cva))
                    ps.cell.VerticalAlignment = cva.Trim().ToLowerInvariant() switch
                    {
                        "top" => VerticalAlignment.Top,
                        "middle" => VerticalAlignment.Center,
                        "bottom" => VerticalAlignment.Bottom,
                        _ => VerticalAlignment.None,
                    };
                if (ps.cellCssPadPt <= 0 && cd.TryGetValue("padding", out var cpad))
                {
                    var padBase = ps.cellClassPt > 0 ? ps.cellClassPt : cellFontSize;
                    var (cpT, cpR, cpB, cpL) = ChainPadPt(cpad, padBase);
                    if (cpL + cpR > 0) { ps.cellCssPadPt = cpL + cpR; ps.cellPadLeftPt = cpL; }
                    ps.cellChainPadTopPt = cpT; ps.cellChainPadBotPt = cpB;
                }
            }
        }
        // …and the cell's OWN inline style outranks every selector that
        // reached it: `<td style="font-size:10px;color:#9c9e9f">` sizes and
        // colours that cell's text. Read last so it wins over the class and
        // chain rules applied above.
        if (liftNestedTables && tok.Attributes is not null
            && tok.Attributes.TryGetValue("style", out var tdOwnSt) && tdOwnSt is not null)
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
                && TryParseLength(tdFsm.Groups[1].Value.Trim(), out var tdFsp) && tdFsp > 0
                && (pinnedBodyGrid
                    || tdFsp < (ps.cellClassPt > 0 ? ps.cellClassPt : cellFontSize)))
                ps.cellClassPt = tdFsp;
            if (Regex.Match(tdOwnSt, @"(?<![-\w])color\s*:\s*([^;""']+)",
                    RegexOptions.IgnoreCase) is { Success: true } tdColm
                && ParseCssColor(tdColm.Groups[1].Value.Trim()) is { } tdCol)
                ps.cellChainColor = tdCol;
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
                    && TryParseLength(fsm2.Groups[1].Value.Trim(), out var declPt)
                    && declPt > 0 ? declPt
                    : ps.cellClassPt > 0 ? ps.cellClassPt : cellFontSize;
                ps.cellOwnLineHPt = tdLhm.Groups[2].Value.ToLowerInvariant() switch
                {
                    "px" => tdLh * PxToPt,
                    "pt" => tdLh,
                    _ => tdLh * tdDeclPt,   // em or a bare number
                };
            }
        }
    }

    /// <summary>One arm of BuildTableFromHtml's token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void HandleImgOpen(TableParseState ps, TableColumnModel colModel, Table table, Token tok, string tag, HtmlLoadOptions? options, double cellFontSize, bool dwFormCells, bool fullWidthCjkMin, bool widenProbe, bool redlineCells, bool breakAnywhereDoc, bool cellFontShorthand, List<CssElem>? chainBase, double chainSpacingPt, List<(string Tag, int PrevBoldDepth)> chainUnbold, string? cssBaseFamily, double cssBasePt, string? defaultCellFace, double formGridStrutDropPt, bool hasBorder, double inlineFaceRatio, bool overDeclaredDraw, double padSide, bool uaDocGrid, bool uaSerifMin, bool ptCellWidths, bool bandDialect, double cellLineHeightPt, string? cssRunFace, bool formGridDialect, double formGridStrutPt, bool liftNestedTables, bool tightExtras, bool uaCellBoxes, double borderWidth, double pad, Dictionary<string, Dictionary<string, string>> css, IReadOnlyDictionary<string, Dictionary<string, string>>? docCss, List<CssChainRule>? chainRules, List<CssElem>? cssAncestors, List<byte[]>? inlineSvgs, List<string> nestedHtml, Func<string, bool, Aspose.Pdf.Forms.RadioButtonOptionField>? makeRadio, double availWidthPt, double defaultCellFontPt, Dictionary<string, string> tblStyle, bool docElementGrid, bool pinnedBodyGrid, bool authoredCellChrome, bool chainBorderSeparate, bool elemRuleBorder)
    {
        // An image inside a cell (a logo, an inline-<svg> placeholder, an SVG
        // diagram) becomes an Image paragraph; the generator's cell renderer
        // rasterizes SVG sources and sizes unfixed images by the document rule.
        if (ps.cell is not null && tok.Attributes is not null
            && tok.Attributes.TryGetValue("src", out var cellSrc) && !string.IsNullOrEmpty(cellSrc))
        {
            byte[]? cellImgBytes;
            if (cellSrc.StartsWith("inline-svg:", StringComparison.Ordinal)
                && int.TryParse(cellSrc["inline-svg:".Length..], out var cellSvgIdx)
                && inlineSvgs is not null && cellSvgIdx >= 0 && cellSvgIdx < inlineSvgs.Count)
                cellImgBytes = inlineSvgs[cellSvgIdx];
            else
                cellImgBytes = LoadConverterImage(cellSrc, options);
            double ciw = 0, cih = 0;
            if (tok.Attributes.TryGetValue("width", out var ciwS))
                double.TryParse(Regex.Match(ciwS, @"[\d.]+").Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out ciw);
            if (tok.Attributes.TryGetValue("height", out var cihS))
                double.TryParse(Regex.Match(cihS, @"[\d.]+").Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out cih);
            // A CSS-sized cell image (style="width:240px; height:45px") is as
            // explicit as the attribute form.
            if ((ciw <= 0 || cih <= 0) && tok.Attributes.TryGetValue("style", out var ciStyle))
            {
                var cwm = Regex.Match(ciStyle, @"(?<![-\w])width\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
                if (ciw <= 0 && cwm.Success)
                    double.TryParse(cwm.Groups[1].Value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out ciw);
                var chm = Regex.Match(ciStyle, @"(?<![-\w])height\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
                if (cih <= 0 && chm.Success)
                    double.TryParse(chm.Groups[1].Value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out cih);
            }
            // An image that declares NO box of its own takes the file's own
            // pixel size: a browser lays an unsized image out at its
            // intrinsic dimensions. Without this the cell reserves nothing,
            // so the column collapses and the sheet is sized as though the
            // picture were not there.
            if (ciw <= 0 && cih <= 0 && cellImgBytes is { Length: > 0 }
                && TryReadImagePixelSize(cellImgBytes, out var intrinsicW,
                    out var intrinsicH))
            {
                ciw = intrinsicW;
                cih = intrinsicH;
            }
            // Form-document dialect: an unreachable image with explicit CSS
            // dimensions still occupies its box — the layout draws the
            // broken-image frame (a bordered white box with a torn-page
            // glyph) at the declared size instead of collapsing the cell.
            if (cellImgBytes is null && cellFontShorthand && ciw > 4 && cih > 4)
            {
                var phInv = System.Globalization.CultureInfo.InvariantCulture;
                var phSvg = "<svg xmlns='http://www.w3.org/2000/svg' width='" + ciw.ToString(phInv)
                    + "' height='" + cih.ToString(phInv) + "'>"
                    + "<rect x='0.5' y='0.5' width='" + (ciw - 1).ToString(phInv)
                    + "' height='" + (cih - 1).ToString(phInv)
                    + "' fill='white' stroke='#000000' stroke-width='1'/>"
                    + "<rect x='6.5' y='" + (cih / 2 - 8).ToString("0.##", phInv)
                    + "' width='12' height='16' fill='white' stroke='#808080' stroke-width='1'/>"
                    + "</svg>";
                cellImgBytes = System.Text.Encoding.UTF8.GetBytes(phSvg);
            }
            // DataWorks form dialect: a DEAD image with NO declared box
            // still occupies the hidden-inline reserve (see
            // DwHiddenInlinePt) — the help-icon column holds one, the
            // results row's folder icon another.
            if (dwFormCells && cellImgBytes is null
                && !tok.Attributes.ContainsKey("width")
                && (!tok.Attributes.TryGetValue("style", out var dwImgSt) || dwImgSt is null
                    || !Regex.IsMatch(dwImgSt, @"(?<![-\w])width\s*:", RegexOptions.IgnoreCase)))
            {
                ps.line.Append(Table.InlineCheckboxGapChar);
                ps.cellImgWidthPt += Table.DwHiddenInlinePt;
                table.HtmlDwGapReservePt += Table.DwHiddenInlinePt;
                ps.lineHadText = true;
                return;
            }
            // The image's DECLARED box sizes its column whether or not the
            // bytes ever arrive — a spacer GIF that fails to load still holds
            // its gutter open, the way a browser reserves a broken image's box.
            if (liftNestedTables && ciw > 0)
                ps.cellImgWidthPt = Math.Max(ps.cellImgWidthPt, ciw * PxToPtW);

            // Over-declared grid dialect: a SMALL image amid real cell
            // text flows INLINE (the tick bitmap inside 「 」) — the
            // reference keeps the line whole with the mark's ink in
            // place; the paragraph-image path below breaks the line
            // around it. A checkmark glyph carries the ink. Cells that
            // hold ONLY the image (the data-row tick boxes) keep the
            // real bitmap.
            if (overDeclaredDraw && cellImgBytes is not null
                && ciw is > 0 and <= 20 && cih is > 0 and <= 20
                && ps.line.ToString().Replace("&nbsp;", " ").Trim((char)0xA0, ' ').Length > 0)
            {
                ps.line.Append('☑');
                return;
            }
            if (cellImgBytes is not null)
            {
                PushLine(ps, redlineCells, dwFormCells, widenProbe);
                var cellImg = new Image { ImageStream = new System.IO.MemoryStream(cellImgBytes) };
                // A cell that declares an alignment aligns its IMAGE too, not
                // only its text — an `align="right"` logo cell hangs its logo
                // on the right edge of the cell the same way a right-aligned
                // run seats there.
                if (ps.alignSet) cellImg.HorizontalAlignment = ps.cellAlign;
                if (liftNestedTables && ciw > 0)
                    ps.cellImgWidthPt = Math.Max(ps.cellImgWidthPt, ciw * PxToPtW);
                if (IsSvgBytes(cellImgBytes)) cellImg.FileType = ImageFileType.Svg;
                if (ciw > 0) cellImg.FixWidth = ciw * PxToPt;
                if (cih > 0) cellImg.FixHeight = cih * PxToPt;
                // Text already on the cell keeps its place ABOVE the image:
                // defer the paragraph add until CloseCell flushes the lines.
                if (ps.lines.Count > 0) (ps.pendingCellImgs ??= new List<Image>()).Add(cellImg);
                else ps.cell.Paragraphs.Add(cellImg);
            }
        }
    }
}
