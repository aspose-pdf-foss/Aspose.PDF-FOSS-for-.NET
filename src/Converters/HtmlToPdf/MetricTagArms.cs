using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>One arm of BuildTableFromHtml's token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void HandleMetricTableOpen(MetricParseState mps, Token tok, StringBuilder text, IReadOnlyDictionary<string, Dictionary<string, string>> css, bool stdSerif, bool wrapperStacks, bool paragraphCells, bool serifReportCells, bool reportCells, double baseFontSize, List<List<MetricCell>> rows, string face, string boldFace, (double asc, double sum) fm, ref double indent, HtmlLoadOptions? loadOptions, ref double p, bool rtl, ref double s, string tableHtml, ref double tablePct, ref double tableWpt, string tag)
    {
        mps.sawTable = true;
        if (tok.Attributes is { } ta)
        {
            // Legacy attribute grid: border=N draws the bordered grid,
            // align=center centres its box, bordercolor tints the strokes.
            if (stdSerif && ta.TryGetValue("border", out var bav)
                && double.TryParse(bav.TrimEnd('p', 'x'),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var bavN)
                && bavN > 0)
            {
                mps.bordered = true;
                mps.borderHugs = true;
            }
            if (mps.bordered && ta.TryGetValue("style", out var tcst)
                && Regex.IsMatch(tcst, @"border-collapse\s*:\s*collapse",
                    RegexOptions.IgnoreCase))
                mps.attrCollapse = true;
            // Excel-fragment grid: border=0 but the CELLS carry inline
            // border longhands under the table's inline
            // border-collapse:collapse (the windowtext 0.5pt grid) —
            // same collapsed-borders draw as the attribute grid.
            if (stdSerif && !mps.bordered && ta.TryGetValue("style", out var wtst)
                && wtst is not null
                && Regex.IsMatch(wtst, @"border-collapse\s*:\s*collapse",
                    RegexOptions.IgnoreCase)
                && (Regex.IsMatch(tableHtml,
                        @"<td\b[^>]*style\s*=\s*[""'][^""']*border-bottom\s*:[^;""']*solid",
                        RegexOptions.IgnoreCase)
                    // …or the triplet spelling (`border-width: 1pt 1pt
                    // 2.25pt; border-style: solid`) on the cells
                    || Regex.IsMatch(tableHtml,
                        @"<td\b[^>]*style\s*=\s*[""][^""]*border-style\s*:\s*solid",
                        RegexOptions.IgnoreCase)))
            {
                mps.bordered = true;
                mps.borderHugs = true;
                mps.attrCollapse = true;
                mps.wtInlineGrid = true;
                // collapsed borders leave no spacing between cells
                s = 0;
                // The table's own inline width is the grid's
                // declared border box.
                var wtw = Regex.Match(wtst,
                    @"(?<![-\w])width\s*:\s*([\d.]+)\s*(px|pt)", RegexOptions.IgnoreCase);
                if (wtw.Success && double.TryParse(wtw.Groups[1].Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var wtwPx) && wtwPx > 0)
                    tableWpt = wtwPx * (wtw.Groups[2].Value.Equals("px",
                        StringComparison.OrdinalIgnoreCase) ? PxPt : 1.0);
                // Per-side cell padding on the cells: the CSS 1-4
                // value grammar in px/pt/cm (top and left matter here).
                var wtp = Regex.Match(tableHtml,
                    @"<td[^>]*style\s*=\s*[""'][^""']*padding:\s*((?:[\d.]+(?:px|pt|cm)\s*){1,4})",
                    RegexOptions.IgnoreCase);
                if (wtp.Success)
                {
                    double PadLen(string v)
                    {
                        var pm2 = Regex.Match(v, @"([\d.]+)(px|pt|cm)",
                            RegexOptions.IgnoreCase);
                        var n = double.Parse(pm2.Groups[1].Value,
                            System.Globalization.CultureInfo.InvariantCulture);
                        return pm2.Groups[2].Value.ToLowerInvariant() switch
                        {
                            "px" => n * PxPt,
                            "cm" => n * 72.0 / 2.54,
                            _ => n,
                        };
                    }
                    var padVals = Regex.Matches(wtp.Groups[1].Value,
                        @"[\d.]+(?:px|pt|cm)", RegexOptions.IgnoreCase);
                    if (padVals.Count > 0)
                    {
                        mps.wtPadV = PadLen(padVals[0].Value);
                        // CSS: 1 value = all; 2/3 = [T, LR(,B)]; 4 = T R B L
                        // (kept aside: the cellpadding ATTRIBUTE parse
                        // below must not override the cells' own style)
                        mps.wtPadH = PadLen(padVals[Math.Min(
                            padVals.Count == 4 ? 3 : padVals.Count == 1 ? 0 : 1,
                            padVals.Count - 1)].Value);
                        // …and the BOTTOM value: the email grid's
                        // `0.75pt 4.55pt 0cm` pads the top only.
                        mps.wtPadB = PadLen(padVals[
                            padVals.Count >= 3 ? 2 : 0].Value);
                    }
                }
                // The cells' declared border width (each row boundary
                // advances the grid by exactly one shared border).
                var wtbwM = Regex.Match(tableHtml,
                    @"<td[^>]*style\s*=\s*[""'][^""']*border-(?:bottom|width)\s*:[^;""']*?([\d.]+)\s*(px|pt)",
                    RegexOptions.IgnoreCase);
                if (wtbwM.Success && double.TryParse(wtbwM.Groups[1].Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var wtbwV) && wtbwV > 0)
                    mps.wtBw = wtbwM.Groups[2].Value.Equals("px",
                        StringComparison.OrdinalIgnoreCase) ? wtbwV * PxPt : wtbwV;
                // An in-cell <p>'s margin-bottom is cell content height.
                var wtpm = Regex.Match(tableHtml,
                    @"<p\s+style\s*=\s*[""']margin:\s*[\d.]+px\s+[\d.]+px\s+([\d.]+)px",
                    RegexOptions.IgnoreCase);
                if (wtpm.Success)
                    mps.wtPMarginB = double.Parse(wtpm.Groups[1].Value,
                        System.Globalization.CultureInfo.InvariantCulture) * PxPt;
                // …and a cell paragraph with NO margin declaration
                // keeps the UA 1em bottom margin inside its cell
                // (probed: the email grid's rows run one em taller
                // than their line).
                else if (Regex.IsMatch(tableHtml,
                        @"<td[^>]*>\s*(?:<[^>]+>\s*)*<p\b(?![^>]*margin)",
                        RegexOptions.IgnoreCase))
                {
                    mps.wtPMarginB = 12.0;
                    mps.wtPMarginDefaulted = true;
                    // the grid's BOTTOM border (the triplet's third
                    // value) and the top pad still close the last row
                    var wtb3 = Regex.Match(tableHtml,
                        @"border-width\s*:\s*[\d.]+pt\s+[\d.]+pt\s+([\d.]+)pt",
                        RegexOptions.IgnoreCase);
                    if (wtb3.Success)
                        mps.wtBwBottom = double.Parse(wtb3.Groups[1].Value,
                            System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            if (ta.TryGetValue("align", out var talv)
                && talv.Trim().Equals("center", StringComparison.OrdinalIgnoreCase))
                mps.centerTable = true;
            if (ta.TryGetValue("bordercolor", out var tbcv)
                && ParseCssColor(tbcv.Trim()) is { } tbcol)
                mps.borderColor = tbcol;
            if (ta.TryGetValue("bgcolor", out var tabg)
                && AttrColor(tabg) is { } tabgc)
                mps.tableBg = tabgc;
            if (ta.TryGetValue("cellspacing", out var cs) && double.TryParse(cs.TrimEnd('p', 'x'),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var csv))
                s = csv * PxPt;
            // an inline `border-spacing` is CSS's cellspacing
            if (ta.TryGetValue("style", out var bspSt) && bspSt is not null
                && Regex.Match(bspSt, @"border-spacing\s*:\s*([\d.]+)\s*(px|pt)",
                    RegexOptions.IgnoreCase) is { Success: true } bspM
                && double.TryParse(bspM.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var bspV))
            {
                s = bspV * (bspM.Groups[2].Value.Equals("px",
                    StringComparison.OrdinalIgnoreCase) ? PxPt : 1.0);
                // …and marks the saved-statement idiom when the cells
                // style themselves inline: rows pitch on their OWN
                // content (the 11 pt label ladder), not the table's
                // 12 pt strut.
                if (stdSerif && Regex.IsMatch(tableHtml,
                        @"<td[^>]*style\s*=\s*[""][^""]*font-size",
                        RegexOptions.IgnoreCase))
                    mps.inlineStatementGrid = true;
            }
            if (rtl && ta.TryGetValue("height", out var thv)
                && double.TryParse(thv.TrimEnd('p', 'x'),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var thPx))
                mps.tableHeightPt = thPx * PxPt;
            if (ta.TryGetValue("cellpadding", out var cp) && double.TryParse(cp.TrimEnd('p', 'x'),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var cpv))
                p = cpv * PxPt;
            if (ta.TryGetValue("class", out var tcls))
                foreach (var c in tcls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (wrapperStacks && css.TryGetValue("." + c, out var cgRule)
                        && cgRule.TryGetValue("border-collapse", out var cgBc)
                        && cgBc.Contains("collapse", StringComparison.OrdinalIgnoreCase)
                        && cgRule.TryGetValue("border-top", out var cgBt))
                    {
                        mps.collapsedGrid = true;
                        if (ParseCssColor(cgBt) is { } cgCol) mps.collapsedCol = cgCol;
                        if (cgRule.TryGetValue("line-height", out var cgLh)
                            && TryParseLength(cgLh, out var cgLhPt))
                            mps.collapsedLineH = cgLhPt;
                    }
                    if (css.TryGetValue("." + c, out var cd)
                        && cd.TryGetValue("margin-left", out var cml)
                        && TryParseLength(cml, out var cmlPt))
                        indent += cmlPt;
                    if (css.TryGetValue("table." + c, out var lcd)
                        && lcd.TryGetValue("table-layout", out var tlv)
                        && tlv.Contains("fixed", StringComparison.OrdinalIgnoreCase))
                        mps.layoutFixed = true;
                    // a width class on the table declares its fixed
                    // box (the boleto's .w666 skin); such a class-
                    // framework sheet also zeroes the grid chrome
                    // (table { border-collapse; padding: 0 })
                    // table class TYPOGRAPHY skins every cell that
                    // has no closer declaration (the boleto's ctN table)
                    if (wrapperStacks
                        && css.TryGetValue("." + c, out var tclsBag))
                    {
                        var tProbe = new MetricCell();
                        ApplyCellClassBag(mps, css, text, reportCells, stdSerif, tProbe, tclsBag);
                        if (tProbe.FontSize is { } tpFs)
                        { mps.fontSize = tpFs; mps.tableClassFont = true; }
                    }
                    if (wrapperStacks
                        && css.TryGetValue("." + c, out var wcd)
                        && wcd.TryGetValue("width", out var wcv)
                        && TryParseLength(wcv.Trim(), out var wcPt)
                        && wcPt > 0)
                    {
                        tableWpt = wcPt;
                        mps.widthClassTable = true;
                        if (css.TryGetValue("table", out var shT))
                        {
                            if (shT.TryGetValue("border-collapse", out var shBc)
                                && shBc.Contains("collapse", StringComparison.OrdinalIgnoreCase))
                                s = 0;
                            if (shT.TryGetValue("padding", out var shPad))
                            {
                                // TryParseLength treats 0 as "no length";
                                // padding: 0 is a real declaration here
                                if (Regex.IsMatch(shPad.Trim(), @"^0(px)?$"))
                                    p = 0;
                                else if (TryParseLength(shPad.Trim(), out var shPadPt))
                                    p = shPadPt;
                            }
                        }
                    }
                }
            // table width:N% (inline style or attribute): the column grid
            // scales up to fill the declared share of the content box.
            var twm = ta.TryGetValue("style", out var tst)
                ? Regex.Match(tst, @"width\s*:\s*(\d+(?:\.\d+)?)\s*%")
                : Match.Empty;
            if (!twm.Success && ta.TryGetValue("width", out var twa))
                twm = Regex.Match(twa, @"^\s*(\d+(?:\.\d+)?)\s*%");
            if (twm.Success)
                double.TryParse(twm.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out tablePct);
            // width="793" / "1000px": a pixel table width the grid
            // fills exactly (auto columns share the surplus).
            else if (ta.TryGetValue("width", out var twpx)
                && double.TryParse(twpx.Trim().TrimEnd('p', 'x'),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var twpxN)
                && twpxN > 0)
                tableWpt = twpxN * PxPt;
            // An inline STYLE pixel width fills the grid the same way
            // (measured on the band: style="width:482px" lands
            // the 361.5 pt grid with the surplus shared ∝ content).
            if (tableWpt <= 0 && tablePct <= 0 && ta.TryGetValue("style", out var tst2)
                && Regex.Match(tst2, @"(?<![-\w])width\s*:\s*(\d+(?:\.\d+)?)\s*px",
                    RegexOptions.IgnoreCase) is { Success: true } tswm)
                tableWpt = DtpNum(tswm.Groups[1].Value) * PxPt;
            // …its pixel height is the BAND: rows share it and centre
            // their content (probed: a 135px single-row band centres
            // the cell baselines on the band's middle)…
            if (ta.TryGetValue("style", out var tst3)
                && Regex.Match(tst3, @"(?<![-\w])height\s*:\s*(\d+(?:\.\d+)?)\s*px",
                    RegexOptions.IgnoreCase) is { Success: true } tshm)
                mps.tableStyleHPt = DtpNum(tshm.Groups[1].Value) * PxPt;
            // …and its background fills the whole band rectangle.
            if (ta.TryGetValue("style", out var tst4)
                && Regex.Match(tst4, @"background(?:-color)?\s*:\s*([^;]+)",
                    RegexOptions.IgnoreCase) is { Success: true } tsbm
                && ParseCssColor(tsbm.Groups[1].Value.Trim()) is { } tsbg)
                mps.tableStyleBg = tsbg;
        }
    }

    /// <summary>One arm of BuildTableFromHtml's token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void HandleMetricCellOpen(MetricParseState mps, Token tok, string tag, StringBuilder text, IReadOnlyDictionary<string, Dictionary<string, string>> css, bool stdSerif, bool wrapperStacks, bool paragraphCells, bool serifReportCells, bool reportCells, double baseFontSize, List<List<MetricCell>> rows, string face, string boldFace, (double asc, double sum) fm, double indent, HtmlLoadOptions? loadOptions, double p, bool rtl, double s, string tableHtml, double tablePct, double tableWpt)
    {
        CloseCell(mps, text, reportCells, stdSerif);
        mps.row ??= new List<MetricCell>();
        mps.cell = new MetricCell { Bold = tag == "th" };
        if (mps.pendingNestSpan > 1) { mps.cell.ColSpan = mps.pendingNestSpan; mps.pendingNestSpan = 0; }
        // Browser UA default: <th> content is centered.
        if (stdSerif && tag == "th") mps.cell.Align = HorizontalAlignment.Center;
        // The sheet's own th/td element rule styles the cell (the
        // order-ticket th { font-size: 80%; text-align: left }).
        if (stdSerif && css.TryGetValue(tag, out var cellTagRule))
            ApplyCellClassBag(mps, css, text, reportCells, stdSerif, mps.cell, cellTagRule);
        if (mps.rowFs is { } rfs) { mps.cell.FontSize = rfs; if (mps.rowFsFromClass) mps.cell.FontFromClass = true; }
        if (mps.rowAlign is { } ra) mps.cell.Align = ra;
        if (mps.rowBg is { } rbg) mps.cell.Bg = rbg;
        if (mps.rowFace is { } rfc) mps.cell.Face = rfc;
        if (mps.rowBold) mps.cell.Bold = true;
        if (mps.rowFore is { } rfo) mps.cell.Fore = rfo;
        if (mps.rowVTop) mps.cell.VAlignTop = true;
        if (mps.rowVBottom) mps.cell.VAlignBottom = true;
        if (mps.rowTdBags is not null)
            foreach (var tb in mps.rowTdBags) ApplyCellClassBag(mps, css, text, reportCells, stdSerif, mps.cell, tb);
        if (tok.Attributes is { } ca)
        {
            // NoWrap layout is part of the modern-nesting model;
            // the dead-css greens stay on their calibrated wrap.
            if (wrapperStacks && ca.ContainsKey("nowrap")) mps.cell.NoWrap = true;
            if (ca.TryGetValue("colspan", out var csp)
                && int.TryParse(csp.Trim(), out var cspN) && cspN > 1)
                mps.cell.ColSpan = cspN;
            if (ca.TryGetValue("rowspan", out var rsp)
                && int.TryParse(rsp.Trim(), out var rspN) && rspN > 1)
                mps.cell.RowSpan = rspN;
            if (ca.TryGetValue("bgcolor", out var tdbg)
                && AttrColor(tdbg) is { } tdbgc)
                mps.cell.Bg = tdbgc;
            if (ca.TryGetValue("class", out var tdcls))
            {
                mps.cell.ClassNames = new List<string>(
                    tdcls.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                foreach (var cn in tdcls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    // TAG-prefixed selectors (TD.rubric — the pt-report
                    // sheets) resolve like the bare class.
                    if (!css.TryGetValue("." + cn, out var cnr))
                        css.TryGetValue("td." + cn, out cnr);
                    if (cnr is null) continue;
                    if (cnr.TryGetValue("font-size", out var cnfs)
                        && TryParseCssFontSize(cnfs.Trim(), out var cnpt))
                        mps.cell.FontSize = cnpt;
                    // class-driven cell chrome (the header band
                    // and boleto skins): typography, fill, ink,
                    // geometry and per-side borders
                    if (wrapperStacks)
                        ApplyCellClassBag(mps, css, text, reportCells, stdSerif, mps.cell, cnr);
                }
            }
            if (ca.TryGetValue("style", out var tdst))
            {
                var twm2 = Regex.Match(tdst, @"width\s*:\s*(\d+(?:\.\d+)?)\s*%");
                if (twm2.Success)
                    mps.cell.WidthPct = double.Parse(twm2.Groups[1].Value,
                        System.Globalization.CultureInfo.InvariantCulture);
                // An ABSOLUTE inline width (the SSRS width-setter
                // rows: `WIDTH: 12.7mm; MIN-WIDTH: 12.7mm`) fixes
                // the column outright.
                var twAbs = Regex.Match(tdst,
                    @"(?<![-\w])width\s*:\s*([\d.]+)\s*(mm|cm|in|pt|px)",
                    RegexOptions.IgnoreCase);
                if (twAbs.Success && double.TryParse(twAbs.Groups[1].Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var twAbsV) && twAbsV > 0)
                {
                    mps.cell.WidthPx = twAbs.Groups[2].Value.ToLowerInvariant() switch
                    {
                        "mm" => twAbsV * 72.0 / 25.4,
                        "cm" => twAbsV * 72.0 / 2.54,
                        "in" => twAbsV * 72.0,
                        "px" => twAbsV * PxPt,
                        _ => twAbsV,
                    };
                    mps.cell.WidthPxStyle = true;
                }
                if (twAbs.Success
                    && Regex.IsMatch(tdst, @"min-width\s*:", RegexOptions.IgnoreCase))
                    mps.cell.WidthSetterCell = true;
                // An ABSOLUTE physical-unit inline height (the report
                // grid's row pacers: `HEIGHT: 6.35mm`) floors the row
                // band; an EMPTY spacer row is EXACTLY that height.
                var thAbs = Regex.Match(tdst,
                    @"(?<![-\w])height\s*:\s*([\d.]+)\s*(mm|cm|in|pt)\b",
                    RegexOptions.IgnoreCase);
                if (thAbs.Success && double.TryParse(thAbs.Groups[1].Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var thAbsV) && thAbsV >= 0)
                    mps.cell.HeightPt = Math.Max(mps.cell.HeightPt,
                        thAbs.Groups[2].Value.ToLowerInvariant() switch
                        {
                            "mm" => thAbsV * 72.0 / 25.4,
                            "cm" => thAbsV * 72.0 / 2.54,
                            "in" => thAbsV * 72.0,
                            _ => thAbsV,
                        });
                var tfm = Regex.Match(tdst, @"font-size\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
                if (tfm.Success && TryParseCssFontSize(tfm.Groups[1].Value.Trim(), out var tdfs))
                    mps.cell.FontSize = tdfs;
                // newsletter cells honor a style padding-top as box space
                var tptm = Regex.Match(tdst, @"padding-top\s*:\s*(\d+(?:\.\d+)?)\s*px",
                    RegexOptions.IgnoreCase);
                if (reportCells && tptm.Success)
                    mps.cell.PadTopPt = double.Parse(tptm.Groups[1].Value,
                        System.Globalization.CultureInfo.InvariantCulture) * PxPt;
                var tam = Regex.Match(tdst, @"text-align\s*:\s*(left|center|right)", RegexOptions.IgnoreCase);
                if (tam.Success)
                    mps.cell.Align = tam.Groups[1].Value.ToLowerInvariant() switch
                    {
                        "right" => HorizontalAlignment.Right,
                        "center" => HorizontalAlignment.Center,
                        _ => HorizontalAlignment.Left,
                    };
                var tbgm = Regex.Match(tdst, @"background(?:-color)?\s*:\s*([^;]+)",
                    RegexOptions.IgnoreCase);
                if (tbgm.Success && ParseCssColor(tbgm.Groups[1].Value.Trim()) is { } tdsbg)
                    mps.cell.Bg = tdsbg;
                var tcm = Regex.Match(tdst, @"(?<![-\w])color\s*:\s*([^;]+)",
                    RegexOptions.IgnoreCase);
                if (tcm.Success && ParseCssColor(tcm.Groups[1].Value.Trim()) is { } tdcol
                    && (tdcol.R != 0 || tdcol.G != 0 || tdcol.B != 0))
                    mps.cell.Fore = tdcol;
                if (Regex.IsMatch(tdst, @"font-weight\s*:\s*bold", RegexOptions.IgnoreCase))
                    mps.cell.Bold = true;
                if (Regex.IsMatch(tdst, @"white-space\s*:\s*nowrap", RegexOptions.IgnoreCase))
                    mps.cell.NoWrap = true;
                // a style border-bottom draws the rule under the cell
                // (the financial-statement idiom — order-free tokens,
                // `none` draws nothing)
                var bbm = Regex.Match(tdst,
                    @"border-bottom\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
                if (bbm.Success
                    && !bbm.Groups[1].Value.Contains("none", StringComparison.OrdinalIgnoreCase)
                    && Regex.IsMatch(bbm.Groups[1].Value, @"solid|double", RegexOptions.IgnoreCase))
                {
                    var bbv = bbm.Groups[1].Value;
                    var bbw = Regex.Match(bbv, @"([\d.]+)\s*(pt|px)", RegexOptions.IgnoreCase);
                    mps.cell.BorderBottomW = bbw.Success
                        ? double.Parse(bbw.Groups[1].Value,
                              System.Globalization.CultureInfo.InvariantCulture)
                          * (bbw.Groups[2].Value.Equals("px",
                              StringComparison.OrdinalIgnoreCase) ? PxPt : 1.0)
                        : 0.75;
                    mps.cell.BorderBottomDouble = bbv.Contains("double",
                        StringComparison.OrdinalIgnoreCase);
                }
                // a style border-right draws that one edge (the legacy
                // separator-column idiom: border-right: solid black 2px)
                var brm = Regex.Match(tdst,
                    @"border-right\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
                if (brm.Success)
                {
                    var brv = brm.Groups[1].Value;
                    var bwm = Regex.Match(brv, @"([\d.]+)\s*px");
                    if (bwm.Success && brv.Contains("solid", StringComparison.OrdinalIgnoreCase))
                    {
                        mps.cell.BorderRightW = DtpNum(bwm.Groups[1].Value) * PxPt;
                        if (ParseCssColor(Regex.Replace(brv,
                                @"solid|[\d.]+\s*px", "", RegexOptions.IgnoreCase).Trim())
                            is { } brc)
                            mps.cell.BorderRightCol = brc;
                    }
                }
            }
            if (ca.TryGetValue("valign", out var va))
            {
                if (va.Trim().Equals("top", StringComparison.OrdinalIgnoreCase))
                { mps.cell.VAlignTop = true; mps.cell.VAlignBottom = false; }
                else if (va.Trim().Equals("bottom", StringComparison.OrdinalIgnoreCase))
                { mps.cell.VAlignBottom = true; mps.cell.VAlignTop = false; }
            }
            if (ca.TryGetValue("align", out var al))
                mps.cell.Align = al.Trim().ToLowerInvariant() switch
                {
                    "right" => HorizontalAlignment.Right,
                    "center" => HorizontalAlignment.Center,
                    _ => HorizontalAlignment.Left,
                };
            if (ca.TryGetValue("width", out var wv) && wv.Trim().EndsWith('%')
                && double.TryParse(wv.Trim().TrimEnd('%'),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var pct))
                mps.cell.WidthPct = pct;
            // width="300" / width="300px": a pixel width fixes the
            // column's content width outright (legacy attribute grid).
            else if (ca.TryGetValue("width", out var wpv)
                && double.TryParse(wpv.Trim().TrimEnd('p', 'x'),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var wpx)
                && wpx > 0)
                mps.cell.WidthPx = wpx * PxPt;
            // height="69": a cell's pixel height floors its whole row
            // (the RTL attr grid's banded rows; the report flow's
            // spacer rows pace on it too).
            if ((rtl || reportCells) && ca.TryGetValue("height", out var hpv)
                && double.TryParse(hpv.Trim().TrimEnd('p', 'x'),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var hpx)
                && hpx * PxPt > mps.pendingRowH)
                mps.pendingRowH = hpx * PxPt;
        }
    }

    /// <summary>One arm of BuildTableFromHtml's token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void HandleMetricImgOpen(MetricParseState mps, Token tok, StringBuilder text, IReadOnlyDictionary<string, Dictionary<string, string>> css, bool stdSerif, bool wrapperStacks, bool reportCells, List<List<MetricCell>> rows, string face, string boldFace, (double asc, double sum) fm, double indent, HtmlLoadOptions? loadOptions, double p, bool rtl, double s, string tableHtml, double tablePct, double tableWpt, string tag)
    {
        // an image reserves its DECLARED box even when the file is
        // unreadable — the row paces on it (the boleto's 40px logo)
        if (wrapperStacks && mps.cell is not null)
        {
            double imgH = 0;
            if (tok.Attributes is { } ia && ia.TryGetValue("height", out var ihv)
                && double.TryParse(ihv.Trim().TrimEnd('p', 'x'),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var ihn))
                imgH = ihn * PxPt;
            if (imgH <= 0 && mps.cell.ClassNames is not null)
                foreach (var icn in mps.cell.ClassNames)
                    if (css.TryGetValue("." + icn + " img", out var imgRule)
                        && imgRule.TryGetValue("height", out var irh)
                        && TryParseLength(irh.Trim(), out var irhPt))
                        imgH = Math.Max(imgH, irhPt);
            mps.cell.ImgHPt = Math.Max(mps.cell.ImgHPt, imgH);
            if (tok.Attributes is { } iaw && iaw.TryGetValue("width", out var iwv)
                && double.TryParse(iwv.Trim().TrimEnd('p', 'x'),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var iwn))
                mps.cell.ImgWPt = Math.Max(mps.cell.ImgWPt, iwn * PxPt);
            // A SIZED mislabelled data URI (GIF bytes shipped as
            // image/png — base64 opening "R0lGOD") draws at its
            // attribute box: without bytes the cell reserves the box
            // and paints nothing. Truthful data URIs keep the legacy
            // calibrated paths (their own arms below).
            if (mps.cell.ImgBytes is null && (mps.cell.ImgWPt > 0 || mps.cell.ImgHPt > 0)
                && tok.Attributes is { } iaS
                && iaS.TryGetValue("src", out var ssrc)
                && ssrc.Contains("base64,R0lGOD", StringComparison.Ordinal)
                && LoadConverterImage(DecodeEntities(ssrc), loadOptions) is { Length: > 0 } sbytes)
                mps.cell.ImgBytes = sbytes;
            // An <img> that declares NO box of its own takes its file's
            // OWN pixel size: the browser lays an unsized image out at
            // its intrinsic dimensions, 1 css px to 0.75 pt. Without
            // this the picture is never drawn and its column collapses
            // to nothing, which then mis-sizes the row and the sheet.
            // …MISLABELLED data URIs included: a GIF shipped as
            // image/png (base64 opening "R0lGOD") loads here — the
            // loader normalises it to PNG bytes, so the intrinsic-size
            // read sees a decodable picture. Truthful data URIs keep
            // the legacy calibrated paths (JPEG viewport clamp etc.).
            if (mps.cell.ImgWPt <= 0 && mps.cell.ImgHPt <= 0
                && tok.Attributes is { } iaN
                && iaN.TryGetValue("src", out var nsrc)
                && (!nsrc.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                    || nsrc.Contains("base64,R0lGOD", StringComparison.Ordinal))
                && LoadConverterImage(DecodeEntities(nsrc), loadOptions) is { Length: > 0 } nbytes
                && TryReadImagePixelSize(nbytes, out var npw, out var nph))
            {
                mps.cell.ImgBytes = nbytes;
                mps.cell.ImgWPt = npw * PxPt;
                mps.cell.ImgHPt = nph * PxPt;
            }
            // A data-URI JPEG draws at its INTRINSIC aspect, ignoring
            // the width/height attributes: an oversized photo clamps
            // to the engine's image viewport and overflows its column
            // (measured: a 1024×768 px photo lands 612×459 pt).
            if (tok.Attributes is { } iaJ && iaJ.TryGetValue("src", out var jsrc)
                && Regex.Match(jsrc, @"^data:image/jpe?g;base64,(.+)$",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline)
                    is { Success: true } jdm)
            {
                byte[]? jb = null;
                try { jb = System.Convert.FromBase64String(jdm.Groups[1].Value); }
                catch { }
                if (jb is not null && JpegDims(jb) is { w: > 0, h: > 0 } jd)
                {
                    var jNatW = jd.w * PxPt;
                    var jDrawW = Math.Min(jNatW, JpegViewportPt);
                    mps.cell.ImgBytes = jb;
                    mps.cell.ImgWPt = jDrawW;
                    mps.cell.ImgHPt = jDrawW * jd.h / jd.w;
                }
            }
            // A data-URI PNG inside an abs-positioned div (left:N%):
            // drawn at natural size at the offset, out of the flow.
            if (mps.pendingAbsLeftFrac >= 0 && mps.cell.AbsPng is null
                && tok.Attributes is { } iaP
                && iaP.TryGetValue("src", out var psrc)
                && Regex.Match(psrc, @"^data:image/png;base64,(.+)$",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline)
                    is { Success: true } pdm)
            {
                try
                {
                    mps.cell.AbsPng = System.Convert.FromBase64String(pdm.Groups[1].Value);
                    mps.cell.AbsPngLeftFrac = mps.pendingAbsLeftFrac;
                }
                catch { }
            }
            // a REMOTE image the renderer cannot fetch shows its alt
            // text in the reserved box (the expected render's broken-
            // image behaviour — the header logo draws its name)
            if (reportCells && tok.Attributes is { } iaAlt
                && iaAlt.TryGetValue("alt", out var altT)
                && !string.IsNullOrWhiteSpace(altT)
                && iaAlt.TryGetValue("src", out var altSrc)
                && altSrc.TrimStart().StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                if (mps.curSeg is null && CollapseWs(text.ToString()).Trim(' ').Length == 0)
                    mps.cell.AltTextOnly = true;
                (mps.curSeg is not null ? mps.divText : text)
                    .Append(' ').Append(DecodeEntities(altT)).Append(' ');
            }
        }
    }

    /// <summary>One arm of BuildTableFromHtml's token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void HandleMetricParaOpen(MetricParseState mps, Token tok, StringBuilder text, IReadOnlyDictionary<string, Dictionary<string, string>> css, bool stdSerif, bool wrapperStacks, bool reportCells, List<List<MetricCell>> rows, string face, string boldFace, (double asc, double sum) fm, double indent, HtmlLoadOptions? loadOptions, double p, bool rtl, double s, string tableHtml, double tablePct, double tableWpt, string tag)
    {
        // `<b><p>…</p></b>` — a BLOCK opening inside an emphasis inline.
        // The HTML parser cannot nest them, so it closes the inline
        // before the block and reopens it after, leaving an empty
        // inline on each side (see CloseCell for the boxes they keep).
        if (mps.cell is not null && mps.boldDepth > 0
            && CollapseWs(text.ToString()).Trim(' ').Length == 0)
            mps.cell.OrphanInlineBoxes = true;
        // An in-cell paragraph's own margin-left indents the cell's
        // text (the financial statement's `margin: 0pt 0pt 0pt
        // 14.4pt` label ladder).
        if (stdSerif && mps.cell is not null && tok.Attributes is { } pmAttrs
            && pmAttrs.TryGetValue("style", out var pmSt) && pmSt is not null
            && Regex.Match(pmSt,
                @"margin\s*:\s*[\d.]+pt\s+[\d.]+pt\s+[\d.]+pt\s+([\d.]+)pt",
                RegexOptions.IgnoreCase) is { Success: true } pmM
            && double.TryParse(pmM.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var pmL)
            && pmL > 0)
            mps.cell.PadLeft = Math.Max(mps.cell.PadLeft, pmL);
        // The sheet's tag.class / class rules style the paragraph's
        // cell (`P.order { font-size: 120% }` on the 12 pt base).
        if (stdSerif && mps.cell is not null && tok.Attributes is { } pAttrs0
            && pAttrs0.TryGetValue("class", out var pCls0) && pCls0 is not null)
            foreach (var pc0 in pCls0.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (css.TryGetValue(tag + "." + pc0, out var pRule0)
                    || css.TryGetValue("." + pc0, out pRule0))
                    ApplyCellClassBag(mps, css, text, reportCells, stdSerif, mps.cell, pRule0);
        // Report cells: each paragraph is its own SEGMENT with the
        // typography its spans set. The lead text (a styled heading
        // span) becomes the first segment so a later span cannot
        // restyle it retroactively; sub-table markers stay in the
        // cell text for CloseCell's lift.
        if (reportCells && !mps.collapsedGrid && mps.cell is not null)
        {
            if (mps.curSeg is null && text.Length > 0)
            {
                var leadStr = text.ToString();
                var leadMarkers = string.Concat(
                    from Match lm in Regex.Matches(leadStr, "\u0002\\d+\u0003")
                    select lm.Value);
                leadStr = Regex.Replace(leadStr, "\u0002\\d+\u0003", " ");
                text.Clear();
                text.Append(leadMarkers);
                if (CollapseWs(leadStr).Trim(' ').Length > 0)
                {
                    // the lead draws with the typography its first
                    // ink SAW — its spans have closed and restored
                    // the cell state by now
                    mps.curSeg = new MetricDivSeg
                    {
                        FontSize = mps.leadSeen ? mps.leadFs : mps.cell.FontSize,
                        Face = mps.leadSeen ? mps.leadFace : mps.cell.Face,
                        Bold = mps.leadSeen ? mps.leadBold : mps.cell.Bold,
                        Fore = mps.leadSeen ? mps.leadFore : mps.cell.Fore,
                    };
                    mps.divText.Append(leadStr);
                    CloseSeg(mps, text, reportCells, stdSerif);
                }
                mps.leadSeen = false;
            }
            CloseSeg(mps, text, reportCells, stdSerif);
            mps.curSeg = new MetricDivSeg();
            // the paragraph's class authors its own margins
            // (`margin: 0pt …`) — they replace the UA block margins
            if (tok.Attributes is { } pMa
                && pMa.TryGetValue("class", out var pMCls) && pMCls is not null)
                foreach (var pmc in pMCls.Split(' ',
                    StringSplitOptions.RemoveEmptyEntries))
                    if (css.TryGetValue("." + pmc, out var pmr)
                        && pmr.TryGetValue("margin", out var pmv))
                    {
                        var pmParts = pmv.Trim().Split(' ',
                            StringSplitOptions.RemoveEmptyEntries);
                        if (pmParts.Length > 0)
                        {
                            mps.curSeg.MarginsExplicit = true;
                            mps.curSeg.MarginTopPt = TryParseLength(
                                pmParts[0], out var pmT) ? pmT : 0;
                            var pmBi = pmParts.Length >= 3 ? 2 : 0;
                            mps.curSeg.MarginBottomPt = TryParseLength(
                                pmParts[pmBi], out var pmB) ? pmB : 0;
                        }
                    }
        }
    }
}
