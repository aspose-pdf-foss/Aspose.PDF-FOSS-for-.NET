using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Metric table open: the declared table width read from the style or the width attribute.</summary>
    private static void ReadMetricTableWidth(MetricTableState mt, Dictionary<string, string> ta)
    {
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
                System.Globalization.CultureInfo.InvariantCulture, out mt.tablePct);
        // width="793" / "1000px": a pixel table width the grid
        // fills exactly (auto columns share the surplus).
        else if (ta.TryGetValue("width", out var twpx)
            && double.TryParse(twpx.Trim().TrimEnd('p', 'x'),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var twpxN)
            && twpxN > 0)
            mt.tableWpt = twpxN * PxPt;
        // An inline STYLE pixel width fills the grid the same way
        // (measured on the band: style="width:482px" lands
        // the 361.5 pt grid with the surplus shared ∝ content).
        if (mt.tableWpt <= 0 && mt.tablePct <= 0 && ta.TryGetValue("style", out var tst2)
            && Regex.Match(tst2, @"(?<![-\w])width\s*:\s*(\d+(?:\.\d+)?)\s*px",
                RegexOptions.IgnoreCase) is { Success: true } tswm)
            mt.tableWpt = DtpNum(tswm.Groups[1].Value) * PxPt;
        // (…and a UA grid's inline width in POINTS - the Words export's `width:341.25pt` - is the
        // same declared box: probed on the Words letter, the address grid's columns fill it, its
        // nbsp column standing 3 pt wide at the right edge)
        if (mt.tableWpt <= 0 && mt.tablePct <= 0 && mt.stdSerif && ta.TryGetValue("style", out var tstPt)
            && Regex.Match(tstPt, @"(?<![-\w])width\s*:\s*([\d.]+\s*(?:pt|in|cm|mm))", RegexOptions.IgnoreCase) is { Success: true } tswPt
            && TryParseLength(tswPt.Groups[1].Value.Replace(" ", "")) is { } tswPtV && tswPtV > 0)
            mt.tableWpt = tswPtV;
        // …and so does the sheet's rule on the table's class (`table.AdvTbl { width:
        // 800px }`): the grid fills the declared box, its auto columns sharing the
        // surplus, the way the attribute's width does.
        if (mt.tableWpt <= 0 && mt.tablePct <= 0 && ta.TryGetValue("class", out var twCls))
            foreach (var c in twCls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                foreach (var key in new[] { "." + c, "table." + c })
                    if (mt.css.TryGetValue(key, out var clsRule) && clsRule.TryGetValue("width", out var clsW))
                    {
                        var v = clsW.Trim();
                        if (v.EndsWith("%", StringComparison.Ordinal))
                            double.TryParse(v.TrimEnd('%'), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out mt.tablePct);
                        else if (TryParseLength(v) is { } clsPt && clsPt > 0)
                            mt.tableWpt = clsPt;
                    }
        // …its pixel height is the BAND: rows share it and centre
        // their content (probed: a 135px single-row band centres
        // the cell baselines on the band's middle)…
        if (ta.TryGetValue("style", out var tst3)
            && Regex.Match(tst3, @"(?<![-\w])height\s*:\s*(\d+(?:\.\d+)?)\s*px",
                RegexOptions.IgnoreCase) is { Success: true } tshm)
            mt.mps.tableStyleHPt = DtpNum(tshm.Groups[1].Value) * PxPt;
        // …its `white-space` inherits into every cell it holds (probed: a
        // `<table style="white-space:nowrap">` keeps its sentence on one line and pages 639.74).
        if (ta.TryGetValue("style", out var tst5)
            && Regex.IsMatch(tst5, @"white-space\s*:\s*(nowrap|pre(?![-\w]))", RegexOptions.IgnoreCase))
            mt.mps.tableNoWrap = true;
        // …and its background fills the whole band rectangle.
        if (ta.TryGetValue("style", out var tst4)
            && Regex.Match(tst4, @"background(?:-color)?\s*:\s*([^;]+)",
                RegexOptions.IgnoreCase) is { Success: true } tsbm
            && ParseCssColor(tsbm.Groups[1].Value.Trim()) is { } tsbg)
            mt.mps.tableStyleBg = tsbg;
    }

    /// <summary>A CSS font-size declaration in an absolute unit (pt/px/cm/mm/in), as against a
    /// percent, em or keyword that resolves against the flow base.</summary>
    private static bool CssSizeIsAbsolute(string decl)
        => Regex.IsMatch(decl.Trim(), @"^\d*\.?\d+\s*(pt|px|cm|mm|in)$", RegexOptions.IgnoreCase);

    /// <summary>Metric table open: the sheet class read off the table tag.</summary>
    private static void ReadMetricTableClass(MetricTableState mt, Dictionary<string, string> ta)
    {
        if (ta.TryGetValue("class", out var tcls))
            foreach (var c in tcls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                mt.mps.tableClassNames.Add(c);
                if (mt.wrapperStacks && mt.css.TryGetValue("." + c, out var cgRule)
                    && cgRule.TryGetValue("border-collapse", out var cgBc)
                    && cgBc.Contains("collapse", StringComparison.OrdinalIgnoreCase)
                    && cgRule.TryGetValue("border-top", out var cgBt))
                {
                    mt.mps.collapsedGrid = true;
                    if (ParseCssColor(cgBt) is { } cgCol) mt.mps.collapsedCol = cgCol;
                    if (cgRule.TryGetValue("line-height", out var cgLh)
                        && TryParseLength(cgLh) is { } cgLhPt)
                        mt.mps.collapsedLineH = cgLhPt;
                }
                if (mt.css.TryGetValue("." + c, out var cd)
                    && cd.TryGetValue("margin-left", out var cml)
                    && TryParseLength(cml) is { } cmlPt)
                    mt.indent += cmlPt;
                // a UA form grid's class chrome: its border sides frame the box it lays out
                // and its background fills it, page by page (measured on the test request's
                // `.formContainer`: 1 px #94a6b5 side rules and a whitesmoke fill from 93.75
                // on page 1 to the box's close on page 3)
                if (mt.mps.uaFormCells && cd is not null) ReadMetricTableClassChrome(mt, cd);
                if (mt.css.TryGetValue("table." + c, out var lcd)
                    && lcd.TryGetValue("table-layout", out var tlv)
                    && tlv.Contains("fixed", StringComparison.OrdinalIgnoreCase))
                    mt.mps.layoutFixed = true;
                // a width class on the table declares its fixed
                // box (the boleto's .w666 skin); such a class-
                // framework sheet also zeroes the grid chrome
                // (table { border-collapse; padding: 0 })
                // table class TYPOGRAPHY skins every cell that
                // has no closer declaration (the boleto's ctN table)
                if (mt.wrapperStacks
                    && mt.css.TryGetValue("." + c, out var tclsBag))
                {
                    var tProbe = new MetricCell();
                    ApplyCellClassBag(mt.mps, mt.css, mt.text, mt.reportCells, mt.stdSerif, tProbe, tclsBag);
                    if (tProbe.FontSize is { } tpFs)
                    {
                        mt.mps.fontSize = tpFs; mt.mps.tableClassFont = true;
                        if (tclsBag.TryGetValue("font-size", out var tpFsDecl) && CssSizeIsAbsolute(tpFsDecl))
                            mt.mps.tableClassAbsoluteSize = true;
                    }
                    if (tProbe.Face is { } tpFace) mt.mps.tableClassFace = tpFace;
                }
                if (mt.wrapperStacks
                    && mt.css.TryGetValue("." + c, out var wcd)
                    && wcd.TryGetValue("width", out var wcv)
                    && TryParseLength(wcv.Trim()) is { } wcPt
                    && wcPt > 0)
                {
                    mt.tableWpt = wcPt;
                    mt.mps.widthClassTable = true;
                    if (mt.css.TryGetValue("table", out var shT))
                    {
                        if (shT.TryGetValue("border-collapse", out var shBc)
                            && shBc.Contains("collapse", StringComparison.OrdinalIgnoreCase))
                            mt.s = 0;
                        if (shT.TryGetValue("padding", out var shPad))
                        {
                            // TryParseLength treats 0 as "no length";
                            // padding: 0 is a real declaration here
                            if (Regex.IsMatch(shPad.Trim(), @"^0(px)?$"))
                                mt.p = 0;
                            else if (TryParseLength(shPad.Trim()) is { } shPadPt)
                                mt.p = shPadPt;
                        }
                    }
                }
            }
    }

    /// <summary>Metric table open: align, border colour, background, cell spacing and cell padding read off the table tag.</summary>
    private static void ReadMetricTableChrome(MetricTableState mt, Dictionary<string, string> ta)
    {
        if (ta.TryGetValue("align", out var talv)
            && talv.Trim().Equals("center", StringComparison.OrdinalIgnoreCase))
            mt.mps.centerTable = true;
        if (ta.TryGetValue("bordercolor", out var tbcv)
            && ParseCssColor(tbcv.Trim()) is { } tbcol)
            mt.mps.borderColor = tbcol;
        if (ta.TryGetValue("bgcolor", out var tabg)
            && AttrColor(tabg) is { } tabgc)
            mt.mps.tableBg = tabgc;
        // …and the table's own CSS background is the same band (the e-mail cards'
        // `style="background: #fff; border: 1px solid #ccc"` paint white on the grey body).
        if (mt.mps.tableBg is null && ta.TryGetValue("style", out var tbgSt) && tbgSt is not null
            && Regex.Match(tbgSt, @"(?<![-\w])background(?:-color)?\s*:\s*([^;]+)", RegexOptions.IgnoreCase) is { Success: true } tbgM
            && ParseCssColor(tbgM.Groups[1].Value.Trim()) is { } tbgc)
            mt.mps.tableBg = tbgc;
        // (a collapsed grid has no spacing whatever its attribute says)
        if (ta.TryGetValue("cellspacing", out var cs) && PresentationalLengthPt(cs) is { } csv && mt.collapseBoxW <= 0)
            mt.s = csv;
        // an inline `border-spacing` is CSS's cellspacing
        // The table's own inline margin-left indents it, as a class rule's does.
        if (ta.TryGetValue("style", out var tmlSt) && tmlSt is not null
            && Regex.Match(tmlSt, @"margin-left\s*:\s*([^;]+)", RegexOptions.IgnoreCase) is { Success: true } tmlM
            && TryParseLength(tmlM.Groups[1].Value.Trim()) is { } tmlPt && tmlPt > 0)
            mt.indent += tmlPt;
        if (ta.TryGetValue("style", out var bspSt) && bspSt is not null
            && Regex.Match(bspSt, @"border-spacing\s*:\s*([\d.]+)\s*(px|pt)",
                RegexOptions.IgnoreCase) is { Success: true } bspM
            && double.TryParse(bspM.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var bspV))
        {
            mt.s = bspV * (bspM.Groups[2].Value.Equals("px",
                StringComparison.OrdinalIgnoreCase) ? PxPt : 1.0);
            // …and marks the saved-statement idiom when the cells
            // style themselves inline: rows pitch on their OWN
            // content (the 11 pt label ladder), not the table's
            // 12 pt strut.
            if (mt.stdSerif && Regex.IsMatch(mt.tableHtml,
                    @"<td[^>]*style\s*=\s*[""][^""]*font-size",
                    RegexOptions.IgnoreCase))
                mt.mps.inlineStatementGrid = true;
        }
        if (mt.rtl && ta.TryGetValue("height", out var thv)
            && double.TryParse(thv.TrimEnd('p', 'x'),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var thPx))
            mt.mps.tableHeightPt = thPx * PxPt;
        if (ta.TryGetValue("cellpadding", out var cp) && PresentationalLengthPt(cp) is { } cpv)
            mt.p = cpv;
        // …and a sheet rule on the cells outranks that presentational attribute.
        var sheetPad = SheetCellPaddingPt(mt.css);
        if (sheetPad >= 0) mt.p = sheetPad;
        // (…a td rule alone pads the td cells, not the th cells: the table keeps the UA padding
        // and each td carries the difference as its own extra - measured on the lab report:
        // `table.list td { padding: 5px }` heads seat 104.8 = 103.5 + the rule + 0.75, the data
        // cells 107.8, the head row 15.3 tall against the data rows' 21.3)
        if (mt.stdSerif && sheetPad > UaCellPaddingPt && SheetTdPaddingAlone(mt.css)
            && Regex.IsMatch(mt.tableHtml, @"<th\b", RegexOptions.IgnoreCase))
        {
            mt.p = UaCellPaddingPt;
            mt.mps.tdPadExtraPt = sheetPad - UaCellPaddingPt;
        }
    }

    /// <summary>The sheet pads the td cells and says nothing of the th cells.</summary>
    private static bool SheetTdPaddingAlone(IReadOnlyDictionary<string, Dictionary<string, string>> css)
        => css.TryGetValue("td", out var td) && td.ContainsKey("padding")
            && !(css.TryGetValue("th", out var th) && th.ContainsKey("padding"));

    /// <summary>Metric table open: the wrapper-stack width dialect read from the table's own style.</summary>
    private static void ReadMetricTableStyleWidth(MetricTableState mt, Dictionary<string, string> ta)
    {
        if (!(mt.stdSerif && !mt.mps.bordered && ta.TryGetValue("style", out var wtst)
        && wtst is not null
        && Regex.IsMatch(wtst, @"border-collapse\s*:\s*collapse",
            RegexOptions.IgnoreCase)
        && (Regex.IsMatch(mt.tableHtml,
                @"<td\b[^>]*style\s*=\s*[""'][^""']*border-bottom\s*:[^;""']*solid",
                RegexOptions.IgnoreCase)
            // …or the triplet spelling (`border-width: 1pt 1pt
            // 2.25pt; border-style: solid`) on the cells
            || Regex.IsMatch(mt.tableHtml,
                @"<td\b[^>]*style\s*=\s*[""][^""]*border-style\s*:\s*solid",
                RegexOptions.IgnoreCase)))) return;
        mt.mps.bordered = true;
        mt.mps.borderHugs = true;
        mt.mps.attrCollapse = true;
        mt.mps.wtInlineGrid = true;
        // collapsed borders leave no spacing between cells
        mt.s = 0;
        // The table's own inline width is the grid's
        // declared border box.
        var wtw = Regex.Match(wtst,
            @"(?<![-\w])width\s*:\s*([\d.]+)\s*(px|pt)", RegexOptions.IgnoreCase);
        if (wtw.Success && double.TryParse(wtw.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var wtwPx) && wtwPx > 0)
            mt.tableWpt = wtwPx * (wtw.Groups[2].Value.Equals("px",
                StringComparison.OrdinalIgnoreCase) ? PxPt : 1.0);
        ReadMetricCellPadding(mt);
        // The cells' declared border width (each row boundary
        // advances the grid by exactly one shared border).
        var wtbwM = Regex.Match(mt.tableHtml,
            @"<td[^>]*style\s*=\s*[""'][^""']*border-(?:bottom|width)\s*:[^;""']*?([\d.]+)\s*(px|pt)",
            RegexOptions.IgnoreCase);
        if (wtbwM.Success && double.TryParse(wtbwM.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var wtbwV) && wtbwV > 0)
            mt.mps.wtBw = wtbwM.Groups[2].Value.Equals("px",
                StringComparison.OrdinalIgnoreCase) ? wtbwV * PxPt : wtbwV;
        // An in-cell <p>'s margin-bottom is cell content height.
        var wtpm = Regex.Match(mt.tableHtml,
            @"<p\s+style\s*=\s*[""']margin:\s*[\d.]+px\s+[\d.]+px\s+([\d.]+)px",
            RegexOptions.IgnoreCase);
        if (wtpm.Success)
            mt.mps.wtPMarginB = double.Parse(wtpm.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture) * PxPt;
        // …and a cell paragraph with NO margin declaration
        // keeps the UA 1em bottom margin inside its cell
        // (probed: the email grid's rows run one em taller
        // than their line).
        else if (Regex.IsMatch(mt.tableHtml,
                @"<td[^>]*>\s*(?:<[^>]+>\s*)*<p\b(?![^>]*margin)",
                RegexOptions.IgnoreCase))
        {
            mt.mps.wtPMarginB = 12.0;
            mt.mps.wtPMarginDefaulted = true;
            // the grid's BOTTOM border (the triplet's third
            // value) and the top pad still close the last row
            var wtb3 = Regex.Match(mt.tableHtml,
                @"border-width\s*:\s*[\d.]+pt\s+[\d.]+pt\s+([\d.]+)pt",
                RegexOptions.IgnoreCase);
            if (wtb3.Success)
                mt.mps.wtBwBottom = double.Parse(wtb3.Groups[1].Value,
                    System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Metric table open: the legacy attribute grid - border, align and bordercolor.</summary>
    /// <summary>The sheet sizes AND borders the grid's cells: a td or th rule - bare, or a chain through the
    /// grid's own structure - declares an absolute width and a border that is neither none nor zero (the
    /// declared grid the bordered solve was probed on; a border-only sheet keeps the calibrated grid).</summary>
    private static bool SheetBordersCells(IReadOnlyDictionary<string, Dictionary<string, string>> css)
    {
        var bordered = false; var sized = false;
        foreach (var kv in css)
        {
            var segs = kv.Key.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (segs.Length == 0 || segs[^1].ToLowerInvariant() is not ("td" or "th")) continue;
            var structural = true;
            for (var i = 0; i < segs.Length - 1 && structural; i++)
                structural = segs[i].ToLowerInvariant() is "table" or "tbody" or "thead" or "tfoot" or "tr";
            if (!structural) continue;
            if (kv.Value.TryGetValue("border", out var b) && !Regex.IsMatch(b, @"^\s*(0\w*|none)\b", RegexOptions.IgnoreCase))
                bordered = true;
            if (kv.Value.TryGetValue("width", out var w) && !w.Contains('%') && TryParseLength(w.Trim()) is > 0)
                sized = true;
        }
        return bordered && sized;
    }

    private static void ReadMetricLegacyGrid(MetricTableState mt, Dictionary<string, string> ta)
    {
        // Legacy attribute grid: border=N draws the bordered grid,
        // align=center centres its box, bordercolor tints the strokes.
        if (mt.stdSerif && ta.TryGetValue("border", out var bav)
            && double.TryParse(bav.TrimEnd('p', 'x'),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var bavN)
            && bavN > 0)
        {
            mt.mps.bordered = true;
            mt.mps.borderHugs = true;
            // A thick attribute (2 px or more) frames the table that wide; the cells keep the 1 px grid.
            if (bavN >= ThickFrameAttrPx) mt.mps.frameW = bavN * PxPt;
            // …and the tag's own border-style dashes or dots that frame.
            if (ta.TryGetValue("style", out var fst) && fst is not null
                && Regex.Match(fst, @"border-style\s*:\s*(dashed|dotted)", RegexOptions.IgnoreCase) is { Success: true } fdm)
                mt.mps.frameDash = fdm.Groups[1].Value.ToLowerInvariant();
        }
        // …and a sheet whose td/th rule borders the cells (bare, or through the grid's own chain:
        // `table tr th, td { border: 1px solid black }`) draws the grid bordered the same way
        // (probed: every cell of the 20-column grid strokes its 0.75 box).
        if (mt.stdSerif && !mt.mps.bordered && SheetBordersCells(mt.css))
        {
            mt.mps.bordered = true;
            mt.mps.borderHugs = true;
        }
        if (mt.mps.bordered && ta.TryGetValue("style", out var tcst)
            && Regex.IsMatch(tcst, @"border-collapse\s*:\s*collapse",
                RegexOptions.IgnoreCase))
            mt.mps.attrCollapse = true;
        // Excel-fragment grid: border=0 but the CELLS carry inline
        // border longhands under the table's inline
        // border-collapse:collapse (the windowtext 0.5pt grid) —
        // same collapsed-borders draw as the attribute grid.
    }
}
