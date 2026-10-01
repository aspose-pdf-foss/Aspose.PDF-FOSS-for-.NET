using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Div-open stages: the chain selector, the lifted-table div style and the div attributes.</summary>
    private static void ApplyDivAttributes(DivOpenState dv)
    {
        if (dv.ps.cell is not null && dv.tok.Attributes is not null
            && dv.tok.Attributes.TryGetValue("style", out var dvSt) && dvSt is not null)
        {
            // A pre/pre-wrap box keeps the source newline that follows its
            // opening tag, which costs it a leading empty line box.
            if (dv.uaCellBoxes && Regex.IsMatch(dvSt,
                    @"white-space\s*:\s*(?:-\w+-)?pre(?:-wrap|-line)?\b", RegexOptions.IgnoreCase))
                dv.ps.preWrapPending = true;
            var dvW = dv.uaCellBoxes
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
                if (dvPt > dv.ps.cellFixedDivPt) dv.ps.cellFixedDivPt = dvPt;
            }
            // A fixed-height div occupies its box inside the cell, so it
            // floors the row the way a cell height does — plus its own top
            // margin, whose percent form resolves against the width of the
            // cell that contains it.
            if (Regex.Match(dvSt, @"(?<!\w-)height\s*:\s*([\d.]+\s*(?:px|pt|cm|mm|in))",
                    RegexOptions.IgnoreCase) is { Success: true } dvHm
                && TryParseLength(dvHm.Groups[1].Value.Replace(" ", "")) is { } dvHPt
                && dvHPt > 0)
            {
                var dvMt = 0.0;
                var dvMtm = Regex.Match(dvSt,
                    @"margin\s*:\s*(\d+(?:\.\d+)?)%|margin-top\s*:\s*(\d+(?:\.\d+)?)%",
                    RegexOptions.IgnoreCase);
                // A percent margin resolves against the containing block's
                // CONTENT width — the cell's declared width, without the
                // padding ResolveCellWidthPt folded into the column footprint.
                var dvBase = dv.uaCellBoxes
                    ? Math.Max(0, dv.ps.cellWidthPt - dv.ps.cellCssPadPt) : dv.ps.cellWidthPt;
                if (dvMtm.Success && dvBase > 0
                    && double.TryParse(dvMtm.Groups[dvMtm.Groups[1].Success ? 1 : 2].Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var dvMtPct))
                    dvMt = dvBase * dvMtPct / 100.0;
                // The box's own horizontal rule sits under its content, so a
                // bottom border adds to the height it claims in the row.
                var dvBb = 0.0;
                if (dv.uaCellBoxes && Regex.Match(dvSt,
                        @"border-bottom\s*:\s*(\d+(?:\.\d+)?)\s*px",
                        RegexOptions.IgnoreCase) is { Success: true } dvBbm)
                    dvBb = double.Parse(dvBbm.Groups[1].Value,
                        System.Globalization.CultureInfo.InvariantCulture) * PxToPtW;
                dv.ps.rowMinHeightPt = Math.Max(dv.ps.rowMinHeightPt, dvHPt + dvMt + dvBb);
                // A CSS height on a child is its CONTENT box: the cell's own
                // padding sits outside it, unlike a legacy height="N" floor.
                dv.ps.rowMinHeightIsContent = true;
            }
        }
    }

    /// <summary></summary>
    private static void ApplyLiftedDivStyle(DivOpenState dv)
    {
        if (dv.liftNestedTables && dv.ps.cell is not null && !dv.divChainStyled)
        {
            // …but a line holding ONLY the pending list marker stays open:
            // the ::marker rides the item's first CONTENT line even when
            // the item opens with a block child (`<LI>\n<DIV>caption…`
            // draws "1. caption" together, not an orphaned
            // marker line). A whitespace/&nbsp;-only line COLLAPSES at the
            // block boundary instead of becoming a phantom box.
            if (IsAllWhitespace(dv.ps.line)) dv.ps.line.Clear();
            else if (dv.ps.line.Length > 0
                && !Regex.IsMatch(dv.ps.line.ToString(), @"^\s*(?:\d+\.|•)\s*$"))
                PushLine(dv.ps, dv.redlineCells, dv.dwFormCells, dv.widenProbe);
            // …and a block box inside a paragraph is RE-PARENTED out of it:
            // `<p><span style="font-weight:bold"><div>…` closes the p, and
            // the span is not rebuilt around the div, so those lines take
            // the CELL's own font and none of the inline run's weight
            // (these set regular, not bold).
            if (dv.ps.styleStack.Count > 0)
            {
                dv.ps.curFontPt = dv.ps.styleStack[0].PrevPt;
                dv.ps.curFamily = dv.ps.styleStack[0].PrevFamily;
                dv.ps.curColor = dv.ps.styleStack[0].PrevColor;
                foreach (var sf in dv.ps.styleStack)
                    if (sf.BoldBump && dv.ps.boldDepth > 0) dv.ps.boldDepth--;
                foreach (var sf in dv.ps.styleStack)
                    if (sf.ItalicBump && dv.ps.italicDepth > 0) dv.ps.italicDepth--;
                dv.ps.styleStack.Clear();
            }
        }
        // …and a div's OWN inline font-size sizes its content whether or not
        // a selector reached it (`<div style="font-size:24px">` is the email
        // template's only headline size). The chain branch above already
        // stacked a restore frame; without one the div stacks its own.
        if (dv.liftNestedTables && dv.ps.cell is not null && dv.tok.Attributes is not null
            && dv.tok.Attributes.TryGetValue("style", out var dvFontSt) && dvFontSt is not null
            && Regex.Match(dvFontSt, @"(?<![-\w])font-size\s*:\s*([^;""']+)",
                RegexOptions.IgnoreCase) is { Success: true } dvFsm
            && TryParseLength(dvFsm.Groups[1].Value.Trim()) is { } dvFsp && dvFsp > 0)
        {
            if (dv.chainBase is null) dv.ps.styleStack.Add((dv.tag, dv.ps.curFontPt, dv.ps.curFamily, false, dv.ps.curColor, false, PositionedInline(dv.tok)));
            dv.ps.curFontPt = dvFsp;
        }
    }

    /// <summary></summary>
    private static void ApplyDivChainSelector(DivOpenState dv)
    {
        if (dv.chainBase is not null && dv.ps.cell is not null)
        {
            var chDivElem = ChainTokElem(dv.tag, dv.tok.Attributes);
            dv.ps.chainOpenElems!.Add(chDivElem);
            var dvPrevPt = dv.ps.curFontPt; var dvPrevFamily = dv.ps.curFamily; var dvBold = false;
            var dvPrevColor = dv.ps.curColor;
            if (dv.ps.chainTdElem is not null
                && MatchChainDecls(dv.chainRules, BuildOpenChain(dv.ps, dv.chainBase)) is { } dvd)
            {
                dv.divChainStyled = true;
                if (dvd.TryGetValue("display", out var ddisp))
                    chDivElem.Display = ddisp.Trim().ToLowerInvariant();
                if (dvd.TryGetValue("font-size", out var dfs2))
                {
                    var dBase = dv.ps.curFontPt > 0 ? dv.ps.curFontPt
                        : dv.ps.cellClassPt > 0 ? dv.ps.cellClassPt : dv.cellFontSize;
                    var dpm2 = Regex.Match(dfs2.Trim(), @"^([\d.]+)\s*%$");
                    if (dpm2.Success && double.TryParse(dpm2.Groups[1].Value,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var dPct)
                        && dPct > 0)
                        dv.ps.curFontPt = dBase * dPct / 100.0;
                    else if (ChainLenPt(dfs2, dBase) is > 0 and var dAbs)
                        dv.ps.curFontPt = dAbs;
                }
                if (dvd.TryGetValue("font-weight", out var dfw)
                    && Regex.IsMatch(dfw, @"bold|[6-9]00", RegexOptions.IgnoreCase))
                {
                    dvBold = true;
                    dv.ps.boldDepth++;
                    if (dv.widenProbe) dv.ps.line.Append('');
                }
                // An inline-block div with a background is a real box run
                // (title plates, badges); a BLOCK-level background still
                // tints the cell as the closest approximation — EXCEPT a
                // border-radius div (a rounded CAPSULE around the nested
                // grid it wraps): that paints behind the grid instead.
                ChainBoxOpenMaybe(dv.ps, dv.options, dv.cellFontSize, chDivElem, dvd);
                var divIsCapsule = dvd.TryGetValue("border-radius", out var capR)
                    && (dvd.ContainsKey("background-color") || dvd.ContainsKey("background"));
                if ((chDivElem.Display ?? "") != "inline-block"
                    && !divIsCapsule
                    && dv.ps.cell.BackgroundColor is null
                    && (dvd.TryGetValue("background-color", out var dbg)
                        || dvd.TryGetValue("background", out dbg))
                    && ParseCssColor(dbg) is { } dbgc)
                    dv.ps.cell.BackgroundColor = dbgc;
                if (divIsCapsule
                    && (dvd.TryGetValue("background-color", out var capBg)
                        || dvd.TryGetValue("background", out capBg))
                    && ParseCssColor(capBg) is { } capFill)
                {
                    var capBase = dv.ps.curFontPt > 0 ? dv.ps.curFontPt
                        : dv.ps.cellClassPt > 0 ? dv.ps.cellClassPt : dv.cellFontSize;
                    var (cpT2, cpR2, _, cpL2) = dvd.TryGetValue("padding", out var capPad)
                        ? ChainPadPt(capPad, capBase) : (0, 0, 0, 0);
                    // The capsule div's MARGIN is white space outside the
                    // pill: it insets the whole capsule from the host
                    // cell's content box (the risks td's `margin: 0.5ex`
                    // is the gap left above each pill).
                    var (cmT2, cmR2, _, cmL2) = dvd.TryGetValue("margin", out var capMar)
                        ? ChainPadPt(capMar, capBase) : (0, 0, 0, 0);
                    dv.ps.pendingCapsule = (capFill,
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
                    var dvBase = dv.ps.curFontPt > 0 ? dv.ps.curFontPt
                        : dv.ps.cellClassPt > 0 ? dv.ps.cellClassPt : dv.cellFontSize;
                    var (dpT, dpR, dpB, dpL) = ChainPadPt(dvPad2, dvBase);
                    if (dpL + dpR > 0)
                    {
                        dv.ps.cellPadLeftPt += dpL;
                        dv.ps.cellCssPadPt += dpL + dpR;
                    }
                    dv.ps.cellChainPadTopPt = Math.Max(dv.ps.cellChainPadTopPt, dpT);
                    dv.ps.cellChainPadBotPt = Math.Max(dv.ps.cellChainPadBotPt, dpB);
                }
            }
            dv.ps.styleStack.Add((dv.tag, dvPrevPt, dvPrevFamily, dvBold, dvPrevColor, false, PositionedInline(dv.tok)));
        }
    }
}
