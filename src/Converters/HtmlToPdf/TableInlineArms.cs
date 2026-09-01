using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>One arm of BuildTableFromHtml's token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void HandleInlineOpen(TableParseState ps, TableColumnModel colModel, Table table, Token tok, string tag, HtmlLoadOptions? options, double cellFontSize, bool dwFormCells, bool fullWidthCjkMin, bool widenProbe, bool redlineCells, bool breakAnywhereDoc, bool cellFontShorthand, List<CssElem>? chainBase, double chainSpacingPt, List<(string Tag, int PrevBoldDepth)> chainUnbold, string? cssBaseFamily, double cssBasePt, string? defaultCellFace, double formGridStrutDropPt, bool hasBorder, double inlineFaceRatio, bool overDeclaredDraw, double padSide, bool uaDocGrid, bool uaSerifMin, bool ptCellWidths, bool bandDialect, double cellLineHeightPt, string? cssRunFace, bool formGridDialect, double formGridStrutPt, bool liftNestedTables, bool tightExtras, bool uaCellBoxes, double borderWidth, double pad, Dictionary<string, Dictionary<string, string>> css, IReadOnlyDictionary<string, Dictionary<string, string>>? docCss, List<CssChainRule>? chainRules, List<CssElem>? cssAncestors, List<byte[]>? inlineSvgs, List<string> nestedHtml, Func<string, bool, Aspose.Pdf.Forms.RadioButtonOptionField>? makeRadio, double availWidthPt, double defaultCellFontPt, Dictionary<string, string> tblStyle, bool docElementGrid, bool pinnedBodyGrid, bool authoredCellChrome, bool chainBorderSeparate, bool elemRuleBorder)
    {
        if (ps.cell is null) return;
        // NOTE: no keep here — the line flushed at a <p> OPEN is the
        // inter-tag residue before the paragraph (e.g. "<td> <p>"), not
        // paragraph content; keeping it would give every such cell a
        // phantom blank first line.
        if (tag == "p" && ps.line.Length > 0) PushLine(ps, redlineCells, dwFormCells, widenProbe, joinNext: true);
        {
            var prevPt = ps.curFontPt; var prevFamily = ps.curFamily;
            var prevColor = ps.curColor;
            var styleBold = false;
            var styleItalic = false;
            // Chain-selector run styling (`.Title span.SmallerTitle`,
            // `.RiskCategory` pills): the class/inline handlers below win.
            if (chainBase is not null && tag != "p")
            {
                var chSpanElem = ChainTokElem(tag, tok.Attributes);
                ps.chainOpenElems!.Add(chSpanElem);
                if (ps.chainTdElem is not null
                    && MatchChainDecls(chainRules, BuildOpenChain(ps, chainBase)) is { } srd)
                {
                    if (srd.TryGetValue("display", out var sdisp))
                        chSpanElem.Display = sdisp.Trim().ToLowerInvariant();
                    if (srd.TryGetValue("font-size", out var sfs))
                    {
                        var sBase = ps.curFontPt > 0 ? ps.curFontPt
                            : ps.cellClassPt > 0 ? ps.cellClassPt : cellFontSize;
                        var spm = Regex.Match(sfs.Trim(), @"^([\d.]+)\s*%$");
                        if (spm.Success && double.TryParse(spm.Groups[1].Value,
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var sPct)
                            && sPct > 0)
                            ps.curFontPt = sBase * sPct / 100.0;
                        else if (ChainLenPt(sfs, sBase) is > 0 and var sAbs)
                            ps.curFontPt = sAbs;
                    }
                    // font-weight:normal CANCELS an enclosing bold for
                    // this run (`.SmallerTitle` under a bold plate).
                    if (srd.TryGetValue("font-weight", out var sfwN)
                        && ps.boldDepth > 0
                        && Regex.IsMatch(sfwN, @"^\s*(normal|[1-5]00)", RegexOptions.IgnoreCase))
                    {
                        chainUnbold.Add((tag, ps.boldDepth));
                        ps.boldDepth = 0;
                    }
                    ChainBoxOpenMaybe(ps, options, cellFontSize, chSpanElem, srd);
                    if (!styleBold && srd.TryGetValue("font-weight", out var sfw)
                        && Regex.IsMatch(sfw, @"bold|[6-9]00", RegexOptions.IgnoreCase))
                    {
                        styleBold = true;
                        ps.boldDepth++;
                        if (widenProbe) ps.line.Append('');
                    }
                }
            }
            // A stylesheet class named on the RUN itself ("<span
            // class='rteFontSize-5'>") sizes that run inside the cell's
            // paragraph. The run's own inline style still wins below.
            if (cssRunFace is not null && tok.Attributes is not null
                && tok.Attributes.TryGetValue("class", out var runCls))
                foreach (var rc in runCls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                {
                    var rk = "." + rc;
                    if (!css.TryGetValue(rk, out var rcd)
                        && (docCss is null || !docCss.TryGetValue(rk, out rcd))) continue;
                    if (rcd.TryGetValue("font-size", out var rfs)
                        && TryParseLength(rfs, out var rfsp) && rfsp > 0) ps.curFontPt = rfsp;
                    if (rcd.TryGetValue("font-family", out var rff)
                        && FirstFontFamily(rff) is { Length: > 0 } rfam) ps.curFamily = rfam;
                }
            // Legacy `<font size="1".."7">` attribute in a grid cell —
            // browser-parsed (leading digits of junk like "7pt" count,
            // clamped to the 1..7 scale, 7 = 36pt). The form grid's
            // spacer row is sized from exactly this. Form-grid
            // dialect only — the calibrated grids ignore the attribute.
            if (formGridDialect && tag == "font" && tok.Attributes is not null
                && tok.Attributes.TryGetValue("size", out var fSizeAttr))
            {
                var fst = fSizeAttr.Trim();
                var fDigits = 0;
                while (fDigits < fst.Length && (char.IsDigit(fst[fDigits])
                       || (fDigits == 0 && fst[0] is '+' or '-'))) fDigits++;
                if (fDigits > 0 && int.TryParse(fst[..fDigits], out var fSz))
                {
                    if (fst[0] is '+' or '-') fSz = 3 + fSz;
                    ps.curFontPt = HtmlFontSizeToPt(Math.Clamp(fSz, 1, 7));
                }
            }
            if (tok.Attributes is not null && tok.Attributes.TryGetValue("style", out var inl))
            {
                var fsm = Regex.Match(inl, @"font-size\s*:\s*([^;""']+)", RegexOptions.IgnoreCase);
                if (fsm.Success && TryParseLength(fsm.Groups[1].Value.Trim(), out var fsp)) ps.curFontPt = fsp;
                var ffm = Regex.Match(inl, @"font-family\s*:\s*([^;""']+)", RegexOptions.IgnoreCase);
                if (ffm.Success && FirstFontFamily(ffm.Groups[1].Value) is { Length: > 0 } fam) ps.curFamily = fam;
                // The redline document QUOTES its families
                // (`font-family: 'Times New Roman'`) — read through
                // the quotes where the bare read came up empty.
                else if (redlineCells
                    && Regex.Match(inl, @"font-family\s*:\s*['""]([^'"";]+)['""]",
                        RegexOptions.IgnoreCase) is { Success: true } ffq
                    && FirstFontFamily(ffq.Groups[1].Value) is { Length: > 0 } famq)
                    ps.curFamily = famq;
                // …and its own colour: `<p style="color:#004178">` paints THIS
                // paragraph, while its black sibling in the same cell stays black.
                var colm = Regex.Match(inl, @"(?<![-\w])color\s*:\s*([^;""']+)",
                    RegexOptions.IgnoreCase);
                if (colm.Success && ParseCssColor(colm.Groups[1].Value.Trim()) is { } inlCol)
                    ps.curColor = inlCol;
                // An inline font-weight (the expanded `font: bold …` shorthand)
                // opens a bold run like <b> does, restored at the closing tag.
                if (Regex.IsMatch(inl, @"font-weight\s*:\s*(bold|[7-9]00)", RegexOptions.IgnoreCase))
                {
                    styleBold = true;
                    ps.boldDepth++;
                    if (widenProbe) ps.line.Append('');
                }
                // An inline font-style italic opens an italic run the same
                // way (the form-grid band titles), restored at the close.
                if (formGridDialect
                    && Regex.IsMatch(inl, @"font-style\s*:\s*italic", RegexOptions.IgnoreCase))
                {
                    styleItalic = true;
                    ps.italicDepth++;
                }
                // Lifted dialect: a paragraph's vertical margins are real
                // space between the cell's paragraphs. The gap ABOVE this
                // one is the CSS-collapsed max of its own margin-top and
                // the previous paragraph's margin-bottom; an em resolves
                // against the paragraph's OWN font size (its inline
                // font-size when declared, since it may not have applied
                // to curFontPt yet).
                if (liftNestedTables && !bandDialect && tag == "p")
                {
                    var pEmBase = Regex.Match(inl, @"(?<![-\w])font-size\s*:\s*([^;""']+)",
                            RegexOptions.IgnoreCase) is { Success: true } pfm
                        && TryParseLength(pfm.Groups[1].Value.Trim(), out var pfsPt) && pfsPt > 0
                        ? pfsPt
                        : ps.curFontPt > 0 ? ps.curFontPt
                        : ps.cellClassPt > 0 ? ps.cellClassPt : cellFontSize;
                    double pTop = 0, pBot = 0;
                    if (Regex.Match(inl, @"(?<![-\w])margin\s*:\s*([^;""']+)",
                            RegexOptions.IgnoreCase) is { Success: true } pShm)
                    {
                        var parts = pShm.Groups[1].Value
                            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        double PartPt(string v) =>
                            v.EndsWith("em", StringComparison.OrdinalIgnoreCase)
                            && double.TryParse(v[..^2], System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var em)
                                ? em * pEmBase
                                : TryParseLength(v, out var abs) ? abs : 0;
                        if (parts.Length > 0) pTop = pBot = PartPt(parts[0]);
                        if (parts.Length >= 3) pBot = PartPt(parts[2]);
                    }
                    if (Regex.Match(inl, @"(?<![-\w])margin-top\s*:\s*([^;""']+)",
                            RegexOptions.IgnoreCase) is { Success: true } pTm)
                    {
                        var v = pTm.Groups[1].Value.Trim();
                        pTop = v.EndsWith("em", StringComparison.OrdinalIgnoreCase)
                            && double.TryParse(v[..^2], System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var emT)
                            ? emT * pEmBase
                            : TryParseLength(v, out var absT) ? absT : pTop;
                    }
                    if (Regex.Match(inl, @"(?<![-\w])margin-bottom\s*:\s*([^;""']+)",
                            RegexOptions.IgnoreCase) is { Success: true } pBm)
                    {
                        var v = pBm.Groups[1].Value.Trim();
                        pBot = v.EndsWith("em", StringComparison.OrdinalIgnoreCase)
                            && double.TryParse(v[..^2], System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var emB)
                            ? emB * pEmBase
                            : TryParseLength(v, out var absB) ? absB : pBot;
                    }
                    var pCollapsed = Math.Max(pTop, ps.cellPrevPBottomPt);
                    if (pCollapsed > 0) ps.lineMarginTop = pCollapsed;
                    ps.cellPrevPBottomPt = pBot;
                }
                // Band dialect: a paragraph's explicit top margin survives as a
                // gap above its first line in the cell layout.
                // (The pt-styled fragment reads its margin shorthand
                // through the same parse.)
                if ((bandDialect || ptCellWidths || redlineCells) && tag == "p")
                {
                    var mtm = Regex.Match(inl, @"margin-top\s*:\s*([\d.]+)\s*pt", RegexOptions.IgnoreCase);
                    if (mtm.Success && double.TryParse(mtm.Groups[1].Value,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var pmt) && pmt > 0)
                        ps.lineMarginTop = pmt;
                    // margin-left in pt or em (em against the paragraph's own
                    // resolved font size), netted against a NEGATIVE text-indent —
                    // the "margin-left:2em; text-indent:-2em" hanging-indent idiom
                    // leaves the first line at the content edge.
                    var mlm = Regex.Match(inl, @"margin-left\s*:\s*([\d.]+)\s*(pt|em)", RegexOptions.IgnoreCase);
                    if (mlm.Success && double.TryParse(mlm.Groups[1].Value,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var pml) && pml > 0)
                    {
                        var emBase = ps.curFontPt > 0 ? ps.curFontPt : 8;
                        var ml = mlm.Groups[2].Value.Equals("em", StringComparison.OrdinalIgnoreCase)
                            ? pml * emBase : pml;
                        var tim = Regex.Match(inl, @"text-indent\s*:\s*(-?[\d.]+)\s*(pt|em)", RegexOptions.IgnoreCase);
                        if (tim.Success && double.TryParse(tim.Groups[1].Value,
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var ti) && ti < 0)
                            ml = Math.Max(0, ml + (tim.Groups[2].Value.Equals("em", StringComparison.OrdinalIgnoreCase)
                                ? ti * emBase : ti));
                        ps.lineMarginLeft = ml;
                    }
                    // Redline cell paragraphs spell margin-top in a
                    // 1-3 value shorthand (`margin: 4pt 0pt 0pt`).
                    if (redlineCells && ps.cellFirstPMarginTopPt <= 0
                        && Regex.Match(inl,
                            @"(?<![-\w])margin\s*:\s*([\d.]+)\s*pt",
                            RegexOptions.IgnoreCase) is { Success: true } rlcm
                        && double.TryParse(rlcm.Groups[1].Value,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var rlcmv)
                        && rlcmv > 0)
                        ps.cellFirstPMarginTopPt = rlcmv;
                    if (redlineCells && ps.lineMarginTop <= 0
                        && Regex.Match(inl,
                            @"(?<![-\w])margin\s*:\s*([\d.]+)\s*pt",
                            RegexOptions.IgnoreCase) is { Success: true } rlmt
                        && double.TryParse(rlmt.Groups[1].Value,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var rlmtv)
                        && rlmtv > 0)
                        ps.lineMarginTop = rlmtv;
                    // pt-styled fragment: the margin SHORTHAND's
                    // LEFT (4th) value indents the paragraph the
                    // same way (`margin:0pt 6.4pt 0pt 1.7pt`), and
                    // the RIGHT (2nd) narrows its wrap box.
                    if (ptCellWidths
                        && Regex.Match(inl,
                            @"(?<![-\w])margin\s*:\s*[\d.]+\w*\s+([\d.]+)\s*pt\s+[\d.]+\w*\s+([\d.]+)\s*pt",
                            RegexOptions.IgnoreCase) is { Success: true } msh4)
                    {
                        if (ps.lineMarginLeft <= 0
                            && double.TryParse(msh4.Groups[2].Value,
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var msh4L)
                            && msh4L > 0)
                            ps.lineMarginLeft = msh4L;
                        if (double.TryParse(msh4.Groups[1].Value,
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var msh4R)
                            && msh4R > 0)
                            ps.cellPMarginRightPt = Math.Max(ps.cellPMarginRightPt, msh4R);
                    }
                    // …and the paragraph's own text-align seats the
                    // cell (the numeric columns' `text-align:right`
                    // rides the <p>, not the <td>).
                    if ((ptCellWidths || redlineCells)
                        && Regex.Match(inl, @"text-align\s*:\s*(left|right|center)",
                            RegexOptions.IgnoreCase) is { Success: true } ptaM)
                    {
                        ps.alignSet = true;
                        ps.cellAlign = ptaM.Groups[1].Value.ToLowerInvariant() switch
                        {
                            "right" => HorizontalAlignment.Right,
                            "center" => HorizontalAlignment.Center,
                            _ => HorizontalAlignment.Left,
                        };
                    }
                }
            }
            ps.styleStack.Add((tag, prevPt, prevFamily, styleBold, prevColor, styleItalic));
            // Redline decorations: the span's text-decoration and
            // its diff-marker class's border-bottom scope ink runs
            // to the cell lines they cover.
            if (redlineCells && ps.cell is not null && tok.Attributes is { } rdA)
            {
                void RdOpen(int kind, Color? c)
                {
                    (ps.cellDecorActive ??= new()).Add((ps.styleStack.Count, kind, c));
                    if (ps.lineDecorUnion is null || !ps.lineDecorUnion.Contains((kind, c)))
                        (ps.lineDecorUnion ??= new()).Add((kind, c));
                }
                if (rdA.TryGetValue("style", out var rdSt) && rdSt is not null
                    && Regex.Match(rdSt, @"text-decoration\s*:\s*([^;]+)",
                        RegexOptions.IgnoreCase) is { Success: true } rdTd)
                {
                    var rdv = rdTd.Groups[1].Value;
                    if (rdv.Contains("underline", StringComparison.OrdinalIgnoreCase)) RdOpen(1, null);
                    if (rdv.Contains("line-through", StringComparison.OrdinalIgnoreCase)) RdOpen(2, null);
                }
                if (rdA.TryGetValue("class", out var rdCls) && rdCls is not null)
                    foreach (var rdSc in rdCls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (!css.TryGetValue("." + rdSc, out var rdr)
                            && !css.TryGetValue("span." + rdSc, out rdr)
                            && docCss is not null
                            && !docCss.TryGetValue("." + rdSc, out rdr))
                            docCss.TryGetValue("span." + rdSc, out rdr);
                        if (rdr is null) continue;
                        if (rdr.TryGetValue("text-decoration", out var rdd))
                        {
                            if (rdd.Contains("underline", StringComparison.OrdinalIgnoreCase)) RdOpen(1, null);
                            if (rdd.Contains("line-through", StringComparison.OrdinalIgnoreCase)) RdOpen(2, null);
                        }
                        if (rdr.TryGetValue("border-bottom", out var rdb)
                            && !rdb.Contains("none", StringComparison.OrdinalIgnoreCase))
                            RdOpen(rdb.Contains("dashed", StringComparison.OrdinalIgnoreCase)
                                || rdb.Contains("dotted", StringComparison.OrdinalIgnoreCase) ? 4 : 3,
                                ParseCssColor(rdb));
                    }
            }
        }
    }

    /// <summary>One arm of BuildTableFromHtml's token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void HandleDivOpen(TableParseState ps, TableColumnModel colModel, Table table, Token tok, string tag, HtmlLoadOptions? options, double cellFontSize, bool dwFormCells, bool fullWidthCjkMin, bool widenProbe, bool redlineCells, bool breakAnywhereDoc, bool cellFontShorthand, List<CssElem>? chainBase, double chainSpacingPt, List<(string Tag, int PrevBoldDepth)> chainUnbold, string? cssBaseFamily, double cssBasePt, string? defaultCellFace, double formGridStrutDropPt, bool hasBorder, double inlineFaceRatio, bool overDeclaredDraw, double padSide, bool uaDocGrid, bool uaSerifMin, bool ptCellWidths, bool bandDialect, double cellLineHeightPt, string? cssRunFace, bool formGridDialect, double formGridStrutPt, bool liftNestedTables, bool tightExtras, bool uaCellBoxes, double borderWidth, double pad, Dictionary<string, Dictionary<string, string>> css, IReadOnlyDictionary<string, Dictionary<string, string>>? docCss, List<CssChainRule>? chainRules, List<CssElem>? cssAncestors, List<byte[]>? inlineSvgs, List<string> nestedHtml, Func<string, bool, Aspose.Pdf.Forms.RadioButtonOptionField>? makeRadio, double availWidthPt, double defaultCellFontPt, Dictionary<string, string> tblStyle, bool docElementGrid, bool pinnedBodyGrid, bool authoredCellChrome, bool chainBorderSeparate, bool elemRuleBorder)
    {
        // Pinned-body report: the sheet's `div { padding: 4px }` boxes a
        // div INSIDE a cell too — its vertical pads grow the row the way
        // the pill row sits taller than its plain siblings.
        if (pinnedBodyGrid && ps.cell is not null && docCss is not null
            && docCss.TryGetValue("div", out var cdivR)
            && cdivR.TryGetValue("padding", out var cdivP)
            && TryParseLength(cdivP, out var cdivPt) && cdivPt > 0)
        {
            // The div's own box pads sit INSIDE the cell's padding —
            // they stack on it, they do not compete with it.
            ps.cellChainPadTopPt = Math.Max(ps.cellChainPadTopPt, pad + cdivPt);
            ps.cellChainPadBotPt = Math.Max(ps.cellChainPadBotPt, pad + cdivPt);
        }
        // True once a chain rule has styled this div — a styled div may be
        // an inline-block plate or a box run, and those keep riding their
        // line; only a PLAIN div takes the block break below.
        var divChainStyled = false;
        // Chain-selector block styling (`.Title > div` silver plates,
        // `.TrafficLight` boxes): fonts ride the styleStack exactly like
        // an inline span style; a styled box's fill is approximated as
        // the cell's own until block boxes render for real.
        if (chainBase is not null && ps.cell is not null)
        {
            var chDivElem = ChainTokElem(tag, tok.Attributes);
            ps.chainOpenElems!.Add(chDivElem);
            var dvPrevPt = ps.curFontPt; var dvPrevFamily = ps.curFamily; var dvBold = false;
            var dvPrevColor = ps.curColor;
            if (ps.chainTdElem is not null
                && MatchChainDecls(chainRules, BuildOpenChain(ps, chainBase)) is { } dvd)
            {
                divChainStyled = true;
                if (dvd.TryGetValue("display", out var ddisp))
                    chDivElem.Display = ddisp.Trim().ToLowerInvariant();
                if (dvd.TryGetValue("font-size", out var dfs2))
                {
                    var dBase = ps.curFontPt > 0 ? ps.curFontPt
                        : ps.cellClassPt > 0 ? ps.cellClassPt : cellFontSize;
                    var dpm2 = Regex.Match(dfs2.Trim(), @"^([\d.]+)\s*%$");
                    if (dpm2.Success && double.TryParse(dpm2.Groups[1].Value,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var dPct)
                        && dPct > 0)
                        ps.curFontPt = dBase * dPct / 100.0;
                    else if (ChainLenPt(dfs2, dBase) is > 0 and var dAbs)
                        ps.curFontPt = dAbs;
                }
                if (dvd.TryGetValue("font-weight", out var dfw)
                    && Regex.IsMatch(dfw, @"bold|[6-9]00", RegexOptions.IgnoreCase))
                {
                    dvBold = true;
                    ps.boldDepth++;
                    if (widenProbe) ps.line.Append('');
                }
                // An inline-block div with a background is a real box run
                // (title plates, badges); a BLOCK-level background still
                // tints the cell as the closest approximation — EXCEPT a
                // border-radius div (a rounded CAPSULE around the nested
                // grid it wraps): that paints behind the grid instead.
                ChainBoxOpenMaybe(ps, options, cellFontSize, chDivElem, dvd);
                var divIsCapsule = dvd.TryGetValue("border-radius", out var capR)
                    && (dvd.ContainsKey("background-color") || dvd.ContainsKey("background"));
                if ((chDivElem.Display ?? "") != "inline-block"
                    && !divIsCapsule
                    && ps.cell.BackgroundColor is null
                    && (dvd.TryGetValue("background-color", out var dbg)
                        || dvd.TryGetValue("background", out dbg))
                    && ParseCssColor(dbg) is { } dbgc)
                    ps.cell.BackgroundColor = dbgc;
                if (divIsCapsule
                    && (dvd.TryGetValue("background-color", out var capBg)
                        || dvd.TryGetValue("background", out capBg))
                    && ParseCssColor(capBg) is { } capFill)
                {
                    var capBase = ps.curFontPt > 0 ? ps.curFontPt
                        : ps.cellClassPt > 0 ? ps.cellClassPt : cellFontSize;
                    var (cpT2, cpR2, _, cpL2) = dvd.TryGetValue("padding", out var capPad)
                        ? ChainPadPt(capPad, capBase) : (0, 0, 0, 0);
                    // The capsule div's MARGIN is white space outside the
                    // pill: it insets the whole capsule from the host
                    // cell's content box (the risks td's `margin: 0.5ex`
                    // is the gap left above each pill).
                    var (cmT2, cmR2, _, cmL2) = dvd.TryGetValue("margin", out var capMar)
                        ? ChainPadPt(capMar, capBase) : (0, 0, 0, 0);
                    ps.pendingCapsule = (capFill,
                        Math.Max(0, ChainLenPt(capR!, capBase)),
                        Math.Max(cpL2, cpR2), cpT2, Math.Max(cmT2, Math.Max(cmL2, cmR2)));
                }
                // A BLOCK div's padding insets the cell's text on all
                // sides (the description body's `div { padding: 1em }`).
                // The sibling heading bar is immune: a full-width bar
                // anchors at the cell's BORDER BOX at draw time.
                // A CAPSULE div is exempt: its padding is already the
                // pill's own outset around the grid it wraps, and folding
                // it into the cell too would inset the pill twice.
                if ((chDivElem.Display ?? "") != "inline-block" && !divIsCapsule
                    && dvd.TryGetValue("padding", out var dvPad2))
                {
                    var dvBase = ps.curFontPt > 0 ? ps.curFontPt
                        : ps.cellClassPt > 0 ? ps.cellClassPt : cellFontSize;
                    var (dpT, dpR, dpB, dpL) = ChainPadPt(dvPad2, dvBase);
                    if (dpL + dpR > 0)
                    {
                        ps.cellPadLeftPt += dpL;
                        ps.cellCssPadPt += dpL + dpR;
                    }
                    ps.cellChainPadTopPt = Math.Max(ps.cellChainPadTopPt, dpT);
                    ps.cellChainPadBotPt = Math.Max(ps.cellChainPadBotPt, dpB);
                }
            }
            ps.styleStack.Add((tag, dvPrevPt, dvPrevFamily, dvBold, dvPrevColor, false));
        }
        // A PLAIN div is a BLOCK box: it opens on a line of its own, so a
        // run of them stacks. `<div>IntroText</div>` eight times is eight
        // lines, not one — they were running together and the section that
        // holds them came out a single line tall.
        if (liftNestedTables && ps.cell is not null && !divChainStyled)
        {
            // …but a line holding ONLY the pending list marker stays open:
            // the ::marker rides the item's first CONTENT line even when
            // the item opens with a block child (`<LI>\n<DIV>caption…`
            // draws "1. caption" together, not an orphaned
            // marker line). A whitespace/&nbsp;-only line COLLAPSES at the
            // block boundary instead of becoming a phantom box.
            if (IsAllWhitespace(ps.line)) ps.line.Clear();
            else if (ps.line.Length > 0
                && !Regex.IsMatch(ps.line.ToString(), @"^\s*(?:\d+\.|•)\s*$"))
                PushLine(ps, redlineCells, dwFormCells, widenProbe);
            // …and a block box inside a paragraph is RE-PARENTED out of it:
            // `<p><span style="font-weight:bold"><div>…` closes the p, and
            // the span is not rebuilt around the div, so those lines take
            // the CELL's own font and none of the inline run's weight
            // (these set regular, not bold).
            if (ps.styleStack.Count > 0)
            {
                ps.curFontPt = ps.styleStack[0].PrevPt;
                ps.curFamily = ps.styleStack[0].PrevFamily;
                ps.curColor = ps.styleStack[0].PrevColor;
                foreach (var sf in ps.styleStack)
                    if (sf.BoldBump && ps.boldDepth > 0) ps.boldDepth--;
                foreach (var sf in ps.styleStack)
                    if (sf.ItalicBump && ps.italicDepth > 0) ps.italicDepth--;
                ps.styleStack.Clear();
            }
        }
        // …and a div's OWN inline font-size sizes its content whether or not
        // a selector reached it (`<div style="font-size:24px">` is the email
        // template's only headline size). The chain branch above already
        // stacked a restore frame; without one the div stacks its own.
        if (liftNestedTables && ps.cell is not null && tok.Attributes is not null
            && tok.Attributes.TryGetValue("style", out var dvFontSt) && dvFontSt is not null
            && Regex.Match(dvFontSt, @"(?<![-\w])font-size\s*:\s*([^;""']+)",
                RegexOptions.IgnoreCase) is { Success: true } dvFsm
            && TryParseLength(dvFsm.Groups[1].Value.Trim(), out var dvFsp) && dvFsp > 0)
        {
            if (chainBase is null) ps.styleStack.Add((tag, ps.curFontPt, ps.curFamily, false, ps.curColor, false));
            ps.curFontPt = dvFsp;
        }
        // A fixed-width div inside a cell: its box sizes the column (the
        // content wraps inside it — see the CloseCell measurement). A
        // percent margin-left resolves against the div's own box:
        // x = W + p·x  ⇒  x = W / (1 − p).
        // A block inside a cell contributes its own box to the row. The WIDTH
        // half of that is content-box sizing (uaCellBoxes only); the HEIGHT is
        // plain CSS — a fixed-height block floors its row in any dialect.
        if (ps.cell is not null && tok.Attributes is not null
            && tok.Attributes.TryGetValue("style", out var dvSt) && dvSt is not null)
        {
            // A pre/pre-wrap box keeps the source newline that follows its
            // opening tag, which costs it a leading empty line box.
            if (uaCellBoxes && Regex.IsMatch(dvSt,
                    @"white-space\s*:\s*(?:-\w+-)?pre(?:-wrap|-line)?\b", RegexOptions.IgnoreCase))
                ps.preWrapPending = true;
            var dvW = uaCellBoxes
                ? Regex.Match(dvSt, @"width\s*:\s*(\d+(?:\.\d+)?)\s*px", RegexOptions.IgnoreCase)
                : Match.Empty;
            if (dvW.Success && double.TryParse(dvW.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var dvPx) && dvPx > 0)
            {
                // Content-box: the div's own padding widens its box.
                foreach (Match dpm in Regex.Matches(dvSt,
                    @"padding-(left|right)\s*:\s*(\d+(?:\.\d+)?)\s*px", RegexOptions.IgnoreCase))
                    dvPx += double.Parse(dpm.Groups[2].Value,
                        System.Globalization.CultureInfo.InvariantCulture);
                var mlPct = 0.0;
                var dvMl = Regex.Match(dvSt,
                    @"margin\s*:\s*[\d.]+%?\s+[\d.]+%?\s+[\d.]+%?\s+(\d+(?:\.\d+)?)%|margin-left\s*:\s*(\d+(?:\.\d+)?)%",
                    RegexOptions.IgnoreCase);
                if (dvMl.Success)
                    double.TryParse(dvMl.Groups[dvMl.Groups[1].Success ? 1 : 2].Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out mlPct);
                if (mlPct is > 0 and < 100) dvPx /= 1 - mlPct / 100;
                var dvPt = dvPx * PxToPtW;
                if (dvPt > ps.cellFixedDivPt) ps.cellFixedDivPt = dvPt;
            }
            // A fixed-height div occupies its box inside the cell, so it
            // floors the row the way a cell height does — plus its own top
            // margin, whose percent form resolves against the width of the
            // cell that contains it.
            if (Regex.Match(dvSt, @"(?<!\w-)height\s*:\s*([\d.]+\s*(?:px|pt|cm|mm|in))",
                    RegexOptions.IgnoreCase) is { Success: true } dvHm
                && TryParseLength(dvHm.Groups[1].Value.Replace(" ", ""), out var dvHPt)
                && dvHPt > 0)
            {
                var dvMt = 0.0;
                var dvMtm = Regex.Match(dvSt,
                    @"margin\s*:\s*(\d+(?:\.\d+)?)%|margin-top\s*:\s*(\d+(?:\.\d+)?)%",
                    RegexOptions.IgnoreCase);
                // A percent margin resolves against the containing block's
                // CONTENT width — the cell's declared width, without the
                // padding ResolveCellWidthPt folded into the column footprint.
                var dvBase = uaCellBoxes
                    ? Math.Max(0, ps.cellWidthPt - ps.cellCssPadPt) : ps.cellWidthPt;
                if (dvMtm.Success && dvBase > 0
                    && double.TryParse(dvMtm.Groups[dvMtm.Groups[1].Success ? 1 : 2].Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var dvMtPct))
                    dvMt = dvBase * dvMtPct / 100.0;
                // The box's own horizontal rule sits under its content, so a
                // bottom border adds to the height it claims in the row.
                var dvBb = 0.0;
                if (uaCellBoxes && Regex.Match(dvSt,
                        @"border-bottom\s*:\s*(\d+(?:\.\d+)?)\s*px",
                        RegexOptions.IgnoreCase) is { Success: true } dvBbm)
                    dvBb = double.Parse(dvBbm.Groups[1].Value,
                        System.Globalization.CultureInfo.InvariantCulture) * PxToPtW;
                ps.rowMinHeightPt = Math.Max(ps.rowMinHeightPt, dvHPt + dvMt + dvBb);
                // A CSS height on a child is its CONTENT box: the cell's own
                // padding sits outside it, unlike a legacy height="N" floor.
                ps.rowMinHeightIsContent = true;
            }
        }
    }
}
