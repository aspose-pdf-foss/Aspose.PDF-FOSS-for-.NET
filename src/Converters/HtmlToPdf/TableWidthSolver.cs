using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>The width solver: turns the parse loop's column model (min/max/header/
/// declared/percent widths, colgroup, span constraints) into the table's final
/// column widths and its natural sheet width. Lifted verbatim out of
/// <see cref="BuildTableFromHtml"/>; one call, straight after the parse.</summary>
    private static void SolveColumnWidths(TableColumnModel colModel, Table table,
        Dictionary<string, string> tblStyle, Match tblTag, List<CssElem>? chainBase,
        double availWidthPt, double cellFontSize, bool cellFontShorthand, bool dwFormCells,
        bool fullWidthCjkMin, bool overDeclaredDraw, bool uaDocGrid, double padSide,
        double rowPctDeclMax, int headerRows, bool ptCellWidths, bool uaCellBoxes,
        bool uaSerifMin, double rowPxAtMax, int rowPxCellsAtMax, ref double naturalWidthPt)
    {
        const double PxToPt = 0.75;
        var pctCapW = 0.0;
        var pctNaturalW = 0.0;
        double[]? pctMinsForDraw = null;
        if (colModel.colWidthsPt is null && availWidthPt > 0 && colModel.maxCols > 0 && colModel.colPctW.Count > 0)
        {
            while (colModel.colPctW.Count < colModel.maxCols) colModel.colPctW.Add(0);
            double sumPct = 0; var nSpec = 0;
            for (var i = 0; i < colModel.maxCols; i++)
                if (colModel.colPctW[i] > 0) { sumPct += colModel.colPctW[i]; nSpec++; }
            // Form-document dialect: a LONE declared percent lays out the way a browser
            // does — the declared column takes its percent, the auto columns share the
            // remainder ("<td style='width:25%'>" beside an auto cell splits 75/25).
            // Outside the dialect the legacy majority guard holds.
            if (nSpec * 2 >= colModel.maxCols && sumPct >= 50 || cellFontShorthand && nSpec > 0 && sumPct < 100
                // Chain dialect: a LONE declared percent lays out browser-style too
                // (`.CategoryName { width: 80% }` — the name column takes its
                // percent, the detail buttons hug their min-content).
                || chainBase is not null && nSpec > 0 && sumPct < 100
                // Over-declared grid dialect: same browser split for a lone
                // percent (`<td width="30%">` beside auto date cells).
                || overDeclaredDraw && nSpec > 0 && sumPct < 100)
            {
                var rem = Math.Max(0, 100 - sumPct) / Math.Max(1, colModel.maxCols - nSpec);
                var total = sumPct + rem * (colModel.maxCols - nSpec);
                var tableW = colModel.tableWidthFrac * availWidthPt;
                // A table that declares no width of its own is SHRINK-TO-FIT: its box is
                // only as wide as the content needs, and the declared percents split THAT,
                // not the page. The fitting width is the largest a column's own max-content
                // implies for the whole table (its share is pct/total of it), capped by
                // what is available.
                if (!colModel.tableWidthDeclared && uaDocGrid)
                {
                    var fitW = 0.0;
                    for (var i = 0; i < colModel.maxCols; i++)
                    {
                        var pct = colModel.colPctW[i] > 0 ? colModel.colPctW[i] : rem;
                        var maxC = i < colModel.colMaxW.Count ? colModel.colMaxW[i] : 0;
                        if (pct > 0 && maxC > 0) fitW = Math.Max(fitW, maxC * total / pct);
                    }
                    if (fitW > 0) tableW = Math.Min(tableW, fitW);
                }
                colModel.colWidthsPt = new List<double>(colModel.maxCols);
                var mins = new double[colModel.maxCols];
                pctMinsForDraw = mins;
                double sumW = 0;
                for (var i = 0; i < colModel.maxCols; i++)
                {
                    var w = (colModel.colPctW[i] > 0 ? colModel.colPctW[i] : rem) / total * tableW;
                    // Dash-aware floor: the percent grid lets a hyphenated token wrap
                    // after its dashes, so the floor is the widest POST-BREAK segment.
                    mins[i] = i < colModel.colMinBrkW.Count ? colModel.colMinBrkW[i] : 0;
                    var cwv = Math.Max(w, mins[i]);
                    colModel.colWidthsPt.Add(cwv);
                    sumW += cwv;
                }
                // Over-declared grid dialect, AUTO layout: a declared percent column
                // holds its EXACT share of the box — only a min-content overflow
                // grows it — and the auto columns split what remains (measured on
                // the owner grid: [20,17,10,5→min,auto=remainder,14] of the
                // standard box). The legacy proportional squeeze must not re-shrink
                // the exact shares.
                var overExactShares = false;
                if (overDeclaredDraw && nSpec > 0
                    && !(tblStyle.TryGetValue("table-layout", out var tlDraw)
                        && tlDraw.Contains("fixed", StringComparison.OrdinalIgnoreCase)))
                {
                    overExactShares = true;
                    // Every column declared but the sum under 100%: the shares
                    // scale UP to fill the declared box (the note-box
                    // row [33,10,50] draws at [35.5,10.75,53.8] — the box's right
                    // border lands on the table's right edge).
                    var pctScale = nSpec == colModel.maxCols && sumPct is > 0 and < 100 && colModel.tableWidthDeclared
                        ? 100.0 / sumPct : 1.0;
                                        double fixedSum = 0;
                    var autoN = 0;
                    for (var i = 0; i < colModel.maxCols; i++)
                        if (colModel.colPctW[i] > 0)
                        {
                            colModel.colWidthsPt[i] = Math.Max(colModel.colPctW[i] * pctScale / 100.0 * tableW, mins[i]);
                            fixedSum += colModel.colWidthsPt[i];
                        }
                        else autoN++;
                    var autoW = Math.Max(0, tableW - fixedSum) / Math.Max(1, autoN);
                    sumW = fixedSum;
                    for (var i = 0; i < colModel.maxCols; i++)
                        if (colModel.colPctW[i] <= 0)
                        {
                            colModel.colWidthsPt[i] = Math.Max(autoW, mins[i]);
                            sumW += colModel.colWidthsPt[i];
                        }
                }
                // Min-content floors (an unbreakable header/word wider than its declared %)
                // can push the sum past the table width, which would cascade into the page
                // auto-widen. Squeeze it back inside — but in two tiers so a wide CONTENT
                // column is protected the way a browser's auto layout protects it:
                //   1. reclaim WASTE first — the width a column holds above its own
                //      max-content (an empty spacer column allocated a few % it never fills);
                //   2. only if that is not enough, squeeze the remaining above-min slack
                //      proportionally (the legacy behaviour).
                // Without tier 1 the big body column (huge %, huge slack-above-min) absorbs
                // almost all of the excess and its text over-wraps to a sliver.
                // The NATURAL width of a non-absolute percent grid is its MIN-CONTENT
                // floor sum: percents distribute at layout time and never size the
                // sheet, and a paragraph column's max-content (its whole text on one
                // line) must not either — the SHEET grows to the
                // floors, and the percents then re-resolve against the wider box.
                if (!colModel.tableWidthDeclaredAbs)
                {
                    // Shrink-to-fit: the grid's preferred width is its max-content sum
                    // CLAMPED to the table box (a paragraph column's one-line max must
                    // not size the sheet), floored at the min-content floors (an
                    // unbreakable run still grows the sheet past the box).
                    double cnat = 0, cmax = 0;
                    for (var i = 0; i < colModel.maxCols; i++)
                    {
                        cnat += mins[i];
                        cmax += Math.Max(mins[i], i < colModel.colMaxW.Count ? colModel.colMaxW[i] : 0);
                    }
                    pctNaturalW = Math.Max(cnat, Math.Min(cmax, tableW));
                    // The page-width PROBE reports the min-content floor sum alone:
                    // a width:100% grid FILLS whatever box it gets, so a box-clamped
                    // natural is circular — it echoes the stand-in page back and the
                    // sheet widens by nothing but its own chrome. The sheet
                    // widens only when the floors themselves overflow.
                    if (fullWidthCjkMin) pctNaturalW = cnat;
                    // An OVER-DECLARED fixed-layout attribute grid (one row's width
                    // attributes sum past 100%) cannot fit any box: each percent
                    // resolves against the DEFAULT page's content box
                    // and widens the sheet to the resulting demand — content plays
                    // no part (probed: 5/37/35/15/10 + a 25px column = the demand
                    // below, exact on the probe ladder, +0.2pt on the shipped doc).
                    if (fullWidthCjkMin && chainBase is null && !colModel.tableWidthDeclaredAbs && colModel.tableWidthDeclared
                        && rowPctDeclMax > 100.0 + 1e-6
                        && tblStyle.TryGetValue("table-layout", out var tlFix)
                        && tlFix.Contains("fixed", StringComparison.OrdinalIgnoreCase))
                    {
                        // The percent base: the default content box is the
                        // page minus margins minus the UA body gutter (595−96−90−6 =
                        // 403); our caller's avail carries the margins already.
                        var overPctBase = availWidthPt - UaBodyMarginPt;
                        // A pixel-declared column rides along at its width plus its
                        // cell padding pair and one spacing unit.
                        var overPxCols = rowPxAtMax
                            + rowPxCellsAtMax * (2 * Math.Max(0, padSide) + colModel.tblCellSpacingPt);
                        // Measured residual of the column balancer for this
                        // family (the 102%→596.885 / 110%→629.125 ladder solves
                        // base 403 and this constant), minus the +8 body slack the
                        // widen ladder adds back on top of the reported natural.
                        const double OverDeclaredResidualPt = -6.175;
                        const double WidenLadderSlackPt = 8.0;
                        pctNaturalW = rowPctDeclMax / 100.0 * overPctBase + overPxCols
                            + OverDeclaredResidualPt - WidenLadderSlackPt;
                        table.HtmlOverDeclaredGrid = true;
                    }
                }
                if (!overExactShares && sumW > tableW + 0.01)
                {
                    var excess = sumW - tableW;
                    double waste = 0;
                    var wasteCol = new double[colModel.maxCols];
                    for (var i = 0; i < colModel.maxCols; i++)
                    {
                        var cap = Math.Max(mins[i], i < colModel.colMaxW.Count ? colModel.colMaxW[i] : 0);
                        wasteCol[i] = Math.Max(0, colModel.colWidthsPt[i] - cap);
                        waste += wasteCol[i];
                    }
                    var takeW = Math.Min(excess, waste);
                    if (waste > 0)
                        for (var i = 0; i < colModel.maxCols; i++) colModel.colWidthsPt[i] -= wasteCol[i] / waste * takeW;
                    excess -= takeW;
                    if (excess > 0.01)
                    {
                        double slack = 0;
                        for (var i = 0; i < colModel.maxCols; i++) slack += colModel.colWidthsPt[i] - mins[i];
                        if (slack > 0)
                            for (var i = 0; i < colModel.maxCols; i++)
                                colModel.colWidthsPt[i] -= (colModel.colWidthsPt[i] - mins[i]) / slack * Math.Min(excess, slack);
                    }
                }
                // A percent grid inside a table that DECLARES its own ABSOLUTE width
                // never widens the page: the declared width pins the box and any
                // residual floor overflow spills inside it (browser overflow). A
                // percent-declared or undeclared table has nothing absolute to pin
                // against — its columns keep their min-content floors and the grid
                // overflows the box, so the sheet grows to it.
                if (colModel.tableWidthDeclaredAbs) pctCapW = tableW;
            }
        }
        if (colModel.colWidthsPt is { Count: > 0 } cw && cw.Count == colModel.maxCols)
        {
            // pt-styled fragment: an over-declared grid squeezes into its
            // declared table box (or the content width) instead of widening
            // the sheet — each column shedding in proportion to its slack
            // above min-content (same model as the auto branch's squeeze).
            if (ptCellWidths && availWidthPt > 0)
            {
                var ptCap = (colModel.tableWidthDeclAbsPt > 0
                    ? Math.Min(colModel.tableWidthDeclAbsPt, availWidthPt) : availWidthPt)
                    - colModel.ptMaxCellBorderW;
                var ptSq = SqueezeBySlack(cw, ptCap, colModel.colMinW);
                for (var i = 0; i < cw.Count; i++) cw[i] = ptSq[i];
            }
            // A chain-dialect percent grid (per-cell `width: N%`) resolves at DRAW
            // time against its real box — the build's available width is the outer
            // table's, and pt columns fixed against it come out ~2× wide and clip.
            double cwSum = 0;
            foreach (var w in cw) cwSum += w;
            var emitPctHere = chainBase is not null && colModel.tableWidthDeclared
                && !colModel.tableWidthDeclaredAbs && cwSum > 0 && colModel.colPctW.Count > 0;
            // Draw-time resolution data for the percent grid (see the fallback's
            // twin): declared shares floor at the dash-aware mins.
            if (emitPctHere && pctMinsForDraw is { } pmins && pmins.Length == cw.Count)
            {
                table.HtmlColMinPt = pmins;
                table.HtmlColPctDeclared = true;
                // Which of the emitted shares were really declared: the rest carry the
                // even leftover this branch synthesises for the auto columns, and the
                // draw-time resolver must not treat those as fill targets.
                var pctDecl = new bool[cw.Count];
                for (var i = 0; i < pctDecl.Length && i < colModel.colPctW.Count; i++)
                    pctDecl[i] = colModel.colPctW[i] > 0;
                table.HtmlColPctDeclaredCols = pctDecl;
                if (colModel.colMaxW.Count == cw.Count)
                {
                    var hMax2 = new double[colModel.colMaxW.Count];
                    for (var i = 0; i < colModel.colMaxW.Count; i++)
                        hMax2[i] = Math.Max(colModel.colMaxW[i], pmins[i]);
                    table.HtmlColMaxPt = hMax2;
                }
            }
            var sb = new StringBuilder();
            for (var i = 0; i < cw.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                if (emitPctHere)
                    sb.Append((cw[i] / cwSum * 100.0 * colModel.tableWidthFrac)
                        .ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append('%');
                else
                    sb.Append(cw[i].ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                naturalWidthPt += cw[i];
            }
            table.ColumnWidths = sb.ToString();
            if (pctCapW > 0 && naturalWidthPt > pctCapW) naturalWidthPt = pctCapW;
            // Fixed columns inside a table whose DOCUMENT rule declares an
            // ABSOLUTE width squeeze into it, browser-fashion: the columns scale
            // down proportionally and the declared box is the preferred width —
            // the fixed sum never sizes the sheet past the declaration.
            if (colModel.tableWidthFromDocRule && colModel.tableWidthDeclAbsPt > 0 && naturalWidthPt > colModel.tableWidthDeclAbsPt
                && table.ColumnWidths is { Length: > 0 } cwDecl && !cwDecl.Contains('%'))
            {
                var declScale = colModel.tableWidthDeclAbsPt / naturalWidthPt;
                var declParts = cwDecl.Split(' ');
                var declSb = new StringBuilder();
                for (var i = 0; i < declParts.Length; i++)
                {
                    if (i > 0) declSb.Append(' ');
                    declSb.Append((double.Parse(declParts[i],
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture) * declScale)
                        .ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                }
                table.ColumnWidths = declSb.ToString();
                naturalWidthPt = colModel.tableWidthDeclAbsPt;
            }
            // The PREFERRED width of a percent grid is its shrink-to-fit CONTENT
            // preference, NOT the box-filling share sum (resolved against the
            // build's stand-in box, that sum hands the HOST cell absurd max-content
            // room — the risks pill column balloons on it).
            table.HtmlPreferredWidthPt = naturalWidthPt;
            if (pctMinsForDraw is { } prefMins && colModel.colMaxW.Count == prefMins.Length)
            {
                double prefMin = 0, prefMax = 0, autoMax = 0, declFrac = 0;
                for (var i = 0; i < prefMins.Length; i++)
                {
                    prefMin += prefMins[i];
                    var cMax = Math.Max(colModel.colMaxW[i], prefMins[i]);
                    prefMax += cMax;
                    if (i < colModel.colPctW.Count && colModel.colPctW[i] > 0) declFrac += colModel.colPctW[i] / 100.0;
                    else autoMax += cMax;
                }
                // CSS max-content of a grid with a DECLARED percent column: the auto
                // columns fill the remaining (1 − p) of the table, so the whole table
                // wants autoMax / (1 − p). It is what makes the risks pill's host
                // column ask for room beyond its content floors (the
                // 148.7 pt Risk-Category column) instead of pinning at min-content.
                if (chainBase is not null && declFrac > 0 && declFrac < 1 && autoMax > 0)
                    prefMax = Math.Max(prefMax, autoMax / (1 - declFrac));
                table.HtmlPreferredWidthPt = Math.Max(prefMin,
                    Math.Min(prefMax, naturalWidthPt));
            }
            // Content-driven natural for non-absolute percent grids (REPLACES the
            // box-filling sum): the layout keeps its percent columns, only the
            // reported preferred width changes.
            if (pctNaturalW > 0) naturalWidthPt = pctNaturalW;
            // …and in the probe the PREFERRED width is capped the same way, or the
            // host cell of a nested grid takes the box-filling number right back
            // through max(natural, preferred).
            if (fullWidthCjkMin && pctNaturalW > 0 && table.HtmlPreferredWidthPt > pctNaturalW)
                table.HtmlPreferredWidthPt = pctNaturalW;
        }
        else if (availWidthPt > 0 && colModel.colMaxW.Count == colModel.maxCols && colModel.colMaxW.Count > 0
            && (dwFormCells || colModel.colMaxW.TrueForAll(w => w > 0)))
        {
            // No explicit widths: content-fit. Only when the caller opts in with a real available
            // width (the wide-table ConvertFromHtml path); legacy callers (header/footer & in-flow
            // HtmlFragment tables) keep the equal-% fallback below so their layout is unchanged.
            // Use max-content (no wrapping) when the table fits the available width; otherwise fall
            // back to min-content (columns shrink to their widest word and multi-word cells wrap) —
            // matching a browser's auto table layout.
            double sumMax = 0; foreach (var w in colModel.colMaxW) sumMax += w;
            // Min-content, but keep header cells on one line when the resulting table still fits the
            // available width; if even that overflows, fall back to the pure widest-word min so a wide
            // header never forces the page/table wider (that would override the caller's page size).
            var minPref = new List<double>(colModel.colMinW);
            double sumPref = 0;
            for (var i = 0; i < minPref.Count; i++) { if (colModel.colHdrW[i] > minPref[i]) minPref[i] = colModel.colHdrW[i]; sumPref += minPref[i]; }
            var chosenMin = (sumPref <= availWidthPt) ? minPref : colModel.colMinW;
            var chosen = (availWidthPt <= 0 || sumMax <= availWidthPt) ? colModel.colMaxW : chosenMin;
            // A declared cell width is honoured only while the table FITS its box — the
            // fixed columns keep their declared width (incl. cell padding) and the auto
            // columns absorb the leftover. Once the table overflows, min-content takes
            // over and the declarations contribute nothing.
            // Scoped to the UA-cell-box grids: elsewhere a fitting table keeps its
            // natural (max-content) columns, and stretching a width:100% one to the
            // full box re-wraps every calibrated legacy layout. The over-declared
            // grid dialect takes the same model — its 62px logo/spacer columns pin
            // and the auto title column absorbs the box.
            if ((uaCellBoxes || overDeclaredDraw || ptCellWidths) && ReferenceEquals(chosen, colModel.colMaxW))
            {
                List<double>? floored = null;
                for (var i = 0; i < chosen.Count && i < colModel.colDeclW.Count; i++)
                    if (colModel.colDeclW[i] > chosen[i])
                    {
                        floored ??= new List<double>(chosen);
                        floored[i] = colModel.colDeclW[i];
                    }
                if (floored is not null) chosen = floored;
                // A fitting table that DECLARES its width fills that box: the declared
                // columns keep exactly what they asked for and the AUTO columns absorb
                // all the leftover (an auto label column beside fixed date columns
                // stretches to the full container).
                if (colModel.tableWidthDeclared && availWidthPt > 0)
                {
                    var boxW = colModel.tableWidthFrac * availWidthPt;
                    double sumSel = 0; foreach (var w in chosen) sumSel += w;
                    double autoW = 0;
                    var autoCount = 0;
                    for (var i = 0; i < chosen.Count; i++)
                        if (i >= colModel.colDeclW.Count || colModel.colDeclW[i] <= 0) { autoW += chosen[i]; autoCount++; }
                    if (boxW > sumSel + 0.01 && autoCount > 0)
                    {
                        var grown = new List<double>(chosen);
                        var leftover = boxW - sumSel;
                        for (var i = 0; i < grown.Count; i++)
                            if (i >= colModel.colDeclW.Count || colModel.colDeclW[i] <= 0)
                                grown[i] += autoW > 0 ? leftover * chosen[i] / autoW : leftover / autoCount;
                        chosen = grown;
                    }
                }
                // pt-styled fragment: an over-declared grid SQUEEZES into its
                // box instead of widening the sheet (probed: the address card's
                // content-box columns sum 493 into the 485.4 content width),
                // each column giving up width in proportion to its slack above
                // MIN-CONTENT (probed: the report grid's five columns shed
                // 18/6.7/0.6/6/4.3 of a 35.5 deficit).
                if (ptCellWidths)
                {
                    var boxCap = (colModel.tableWidthDeclAbsPt > 0
                        ? Math.Min(colModel.tableWidthDeclAbsPt, availWidthPt) : availWidthPt)
                        - colModel.ptMaxCellBorderW;
                    chosen = SqueezeBySlack(chosen, boxCap, colModel.colMinW);
                }
            }
            // A width-declared table (WIDTH="N%") fills its box: when the natural columns
            // overflowed and collapsed to min-content, hand the leftover width to the
            // columns that still want to grow (room = max-content − chosen), proportionally,
            // so the flexible text column expands to fill instead of wrapping to a sliver.
            // Fixed columns (max ≈ min) keep their width. Only the overflow (collapsed) case.
            if (!ReferenceEquals(chosen, colModel.colMaxW) && !dwFormCells)
            {
                var tableW = colModel.tableWidthFrac * availWidthPt;
                double sumChosen = 0; foreach (var w in chosen) sumChosen += w;
                if (tableW > sumChosen + 0.01)
                {
                    double sumRoom = 0;
                    for (var i = 0; i < chosen.Count; i++) sumRoom += Math.Max(0, colModel.colMaxW[i] - chosen[i]);
                    if (sumRoom > 0)
                    {
                        var filled = new List<double>(chosen);
                        var leftover = tableW - sumChosen;
                        for (var i = 0; i < filled.Count; i++)
                        {
                            var room = Math.Max(0, colModel.colMaxW[i] - chosen[i]);
                            filled[i] += leftover * room / sumRoom;
                        }
                        chosen = filled;
                    }
                }
            }
            var sb = new StringBuilder();
            // A chain-rule percent-width table fills whatever box it lands in at
            // DRAW time (the outer cell's real width is unknown while it builds),
            // so its columns are emitted as PERCENT shares — surplus rides every
            // column proportionally (the surplus rule).
            double sumChosenAll = 0;
            foreach (var w in chosen) sumChosenAll += w;
            var emitPctCols = colModel.tableWidthPctOfBox && colModel.tableWidthDeclared
                && !colModel.tableWidthDeclaredAbs && sumChosenAll > 0;
            // The generator re-resolves these columns at DRAW time by the same
            // rule (fit → max-content + surplus; else floors + slack squeeze) —
            // hand it the per-column min/max the decision needs.
            if (emitPctCols && colModel.colMinW.Count == chosen.Count)
            {
                table.HtmlColMinPt = colModel.colMinW.ToArray();
                if (colModel.colMaxW.Count == chosen.Count)
                {
                    var hMax = new double[colModel.colMaxW.Count];
                    for (var i = 0; i < colModel.colMaxW.Count; i++)
                        hMax[i] = Math.Max(colModel.colMaxW[i], colModel.colMinW[i]);
                    table.HtmlColMaxPt = hMax;
                }
                // A cell that DECLARED `width="100%"` is a real box-filling target, so
                // the draw-time resolver must hand IT the surplus instead of spreading
                // it over the max-content proportions — without the mask the emitted
                // share was recomputed away and the declared column kept a quarter of
                // its row, shrinking every grid nested inside it in the same ratio.
                // A column whose width was DECLARED absolutely (`<td width="15">` — the
                // layout-table spacer idiom) is FIXED in CSS auto layout: it keeps that
                // width and the box's surplus goes to the auto columns beside it.
                if (colModel.colDeclW.Count == chosen.Count)
                {
                    var fixedCols = new bool[chosen.Count];
                    var anyFixed = false;
                    for (var i = 0; i < chosen.Count; i++)
                        if (colModel.colDeclW[i] > 0 && (i >= colModel.colPctW.Count || colModel.colPctW[i] <= 0))
                            fixedCols[i] = anyFixed = true;
                    if (anyFixed) table.HtmlColFixedCols = fixedCols;
                }
                var anyFillDecl = false;
                for (var i = 0; i < colModel.colPctW.Count && i < chosen.Count; i++)
                    if (colModel.colPctW[i] >= 100) { anyFillDecl = true; break; }
                if (anyFillDecl)
                {
                    var pctDeclB = new bool[chosen.Count];
                    for (var i = 0; i < pctDeclB.Length && i < colModel.colPctW.Count; i++)
                        pctDeclB[i] = colModel.colPctW[i] >= 100;
                    table.HtmlColPctDeclared = true;
                    table.HtmlColPctDeclaredCols = pctDeclB;
                }
                // A trailing nested-grid column absorbs ALL the surplus (it
                // stretches to fill; its siblings hug their content on the left —
                // the title row). A LEADING grid column (the risks pills) keeps
                // its floor and the surplus stays proportional in the text columns.
                if (colModel.nestedTableCols is not null && colModel.nestedTableCols.Contains(chosen.Count - 1))
                    table.HtmlSurplusCol = chosen.Count - 1;
            }
            // Surplus goes to the LAST column (whose nested grid stretches to fill):
            // the earlier columns keep their content share, so a title cell hugs its
            // plates instead of pooling dead space beside them.
            // A chain percent-width grid resolves against a box UNKNOWN at build
            // (the outer avail stands in) — max-content chosen against that box is
            // meaningless, so these grids lay out on MIN-content
            // floors (the budget wraps `Period / Cost type`); the shares then
            // re-resolve at draw. Cells with nowrap/box floors keep them (their
            // min IS the unwrapped width).
            if (emitPctCols && ReferenceEquals(chosen, colModel.colMaxW)
                && colModel.colMinW.Count == chosen.Count)
            {
                // Plain min floors when max-content was chosen against the build's
                // stand-in box; a table already on its min/pref floors keeps them
                // (the Risks grid's wide text columns take the surplus).
                chosen = colModel.colMinW;
                sumChosenAll = 0;
                foreach (var w in chosen) sumChosenAll += w;
            }
            var emitBoxW = colModel.tableWidthFrac * availWidthPt;
            // A DECLARED-width layout table whose surplus belongs to its nested-grid
            // column(s): the label cells keep their content width and the grid fills
            // the rest of the declared box (the "1." marker beside the bordered case
            // table). Without this the nested grid keeps its natural width and the
            // whole declared box goes unused.
            if (!emitPctCols && colModel.tableWidthDeclaredAbs && colModel.nestedTableCols is { Count: > 0 }
                && colModel.nestedTableCols.Count < chosen.Count)
            {
                var absorbBox = emitBoxW - (chosen.Count + 1) * Math.Max(colModel.cellSpacingPt, 0);
                var absorbSurplus = absorbBox - sumChosenAll;
                if (absorbSurplus > 0.5)
                {
                    chosen = new List<double>(chosen);
                    foreach (var ci in colModel.nestedTableCols)
                        if (ci < chosen.Count) chosen[ci] += absorbSurplus / colModel.nestedTableCols.Count;
                    sumChosenAll += absorbSurplus;
                }
            }
            var lastAbsorbs = emitPctCols && chosen.Count > 1 && emitBoxW > sumChosenAll + 0.01
                // …and only when the last column HOLDS a nested grid (which fills
                // whatever it gets) — a text column's share must stay proportional,
                // and a nested build's availWidthPt may not be its real box anyway.
                && colModel.nestedTableCols is not null && colModel.nestedTableCols.Contains(chosen.Count - 1);
            // ONE column declaring `width="100%"` is the layout-table idiom for "give me
            // everything my siblings' content does not need": its row-mates are spacer
            // cells that must keep exactly their content, and the whole remainder is the
            // declared column's. Spreading the row proportionally instead left a
            // 100 %-wide cell about a quarter of its row, and the grid nested inside it
            // shrank in the same proportion at every further level.
            var fillCol = -1;
            if (emitPctCols && !lastAbsorbs && colModel.colPctW.Count <= chosen.Count
                && emitBoxW > sumChosenAll + 0.01)
            {
                var declCount = 0;
                for (var i = 0; i < colModel.colPctW.Count; i++)
                    if (colModel.colPctW[i] >= 100) { fillCol = i; declCount++; }
                if (declCount != 1) fillCol = -1;
            }
            double sumOtherMins = 0;
            if (fillCol >= 0)
                for (var i = 0; i < chosen.Count; i++)
                    if (i != fillCol) sumOtherMins += chosen[i];
            for (var i = 0; i < chosen.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                if (emitPctCols)
                {
                    var share = fillCol >= 0
                        ? (i == fillCol
                            ? Math.Max(0.01, 1.0 - sumOtherMins / emitBoxW)
                            : chosen[i] / emitBoxW)
                        : lastAbsorbs
                        ? (i == chosen.Count - 1
                            ? Math.Max(0.01, 1.0 - sumPrev(chosen, i) / emitBoxW)
                            : chosen[i] / emitBoxW)
                        : chosen[i] / sumChosenAll;
                    sb.Append((share * 100.0 * colModel.tableWidthFrac)
                        .ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append('%');
                }
                else
                    sb.Append(chosen[i].ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                naturalWidthPt += chosen[i];
            }
            static double sumPrev(List<double> list, int count)
            {
                double s = 0;
                for (var k = 0; k < count; k++) s += list[k];
                return s;
            }
            table.ColumnWidths = sb.ToString();
            // A chain-rule percent width (the `.Budget > table { width: 100% }`
            // idiom) resolves against its box at layout time and never sizes the
            // sheet — the reported natural is the PLAIN min-content floor sum
            // (the multi-word `Period / Cost type` header wraps;
            // single-word headers hold their width because one word
            // cannot wrap), the same rule the percent-column grids apply above.
            table.HtmlPreferredWidthPt = naturalWidthPt;
            if (colModel.tableWidthPctOfBox && !colModel.tableWidthDeclaredAbs)
            {
                double sumMinPref = 0;
                foreach (var w in colModel.colMinW) sumMinPref += w;
                naturalWidthPt = sumMinPref;
                if (uaSerifMin && colModel.colMinSerifW.Count > 0)
                {
                    double serifSum = 0;
                    foreach (var w in colModel.colMinSerifW) serifSum += w;
                    if (serifSum > 0)
                    {
                        naturalWidthPt = serifSum;
                        table.HtmlPctMinNatural = true;
                    }
                }
            }
            // The DataWorks grid widens the sheet to its OUTER box: the n+1
            // cellspacing gutters are part of the width the sheet grows to.
            if (dwFormCells && colModel.cellSpacingPt > 0 && colModel.maxCols > 0)
                naturalWidthPt += (colModel.maxCols + 1) * colModel.cellSpacingPt;
        }
        else if (colModel.maxCols > 0)
        {
            // Even shares are the fallback — and they are RIGHT for a real grid whose
            // columns all hold content (a five-column signature table splits its box
            // five ways; min-content-proportional shares under-size the wordy columns
            // and wrap lines that must stay whole). The min-content vector takes
            // over only when it is DEGENERATE — some column measures (near) nothing,
            // the signature of colspan debris: a stray `<td colspan="3">` in one row
            // gives the table three columns while every other row fills only the first,
            // and an even split left the one real column a third of what its content
            // needs — a headline one letter wide. An EMPTY column takes no share; the
            // real ones divide the width in proportion to their content.
            double sumMinCols = 0;
            var anyEmptyCol = false;
            if (colModel.colMinW.Count == colModel.maxCols)
            {
                foreach (var w in colModel.colMinW)
                {
                    sumMinCols += w;
                    if (w <= 0.01) anyEmptyCol = true;
                }
            }
            var minShares = anyEmptyCol && sumMinCols > 0;
            // A layout row pairing label cells with a cell that HOLDS A NESTED GRID
            // ("1." beside the bordered case table): the grid cell absorbs everything
            // the labels' content does not need. An even split would hand the one-word
            // label half the box and squeeze the nested grid to match.
            var nestedAbsorb = !minShares && colModel.nestedTableCols is { Count: > 0 }
                && colModel.nestedTableCols.Count < colModel.maxCols && colModel.colMinW.Count == colModel.maxCols;
            double sumLabelMins = 0;
            if (nestedAbsorb)
                for (var i = 0; i < colModel.maxCols; i++)
                    if (!colModel.nestedTableCols!.Contains(i)) sumLabelMins += colModel.colMinW[i];
            var absorbBoxW = colModel.tableWidthFrac * availWidthPt;
            if (nestedAbsorb && (sumLabelMins <= 0 || sumLabelMins >= absorbBoxW * 0.5))
                nestedAbsorb = false;
            var sb = new StringBuilder();
            for (var i = 0; i < colModel.maxCols; i++)
            {
                if (i > 0) sb.Append(' ');
                var share = nestedAbsorb
                    ? (colModel.nestedTableCols!.Contains(i)
                        ? Math.Max(0.01, (absorbBoxW - sumLabelMins) / absorbBoxW / colModel.nestedTableCols.Count)
                        : colModel.colMinW[i] / absorbBoxW) * colModel.tableWidthFrac * 100.0
                    : minShares
                    ? colModel.tableWidthFrac * 100.0 * colModel.colMinW[i] / sumMinCols
                    : colModel.tableWidthFrac * 100.0 / colModel.maxCols;
                sb.Append(share.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append('%');
            }
            table.ColumnWidths = sb.ToString();
        }

        // A <pre> cell's longest source line is UNBREAKABLE content: the sheet
        // grows past every declared width to hold it (probed: page = margin +
        // longest line + margin), while the DECLARED columns keep their
        // geometry — headers still centre over them and the row bands keep the
        // declared box. The surplus rides an appended phantom column that only
        // the pre cells span, so their lines draw whole.
        if (colModel.preMaxLinePt > naturalWidthPt && naturalWidthPt > 0
            && table.ColumnWidths is { Length: > 0 } preCw && !preCw.Contains('%'))
        {
            var preSurplus = colModel.preMaxLinePt - naturalWidthPt;
            table.ColumnWidths = preCw + " " + preSurplus.ToString("0.###",
                System.Globalization.CultureInfo.InvariantCulture);
            if (colModel.preCells is not null)
                foreach (var (preRow0, pc) in colModel.preCells)
                {
                    pc.ColSpan = Math.Max(1, pc.ColSpan) + 1;
                    // the <pre> block's own vertical margins inside its cell
                    // (probed: the comment row runs ~9 pt taller than its lines,
                    // the first line seating ~2 pt below the padded top)
                    if (pc.Paragraphs.Count > 0)
                    {
                        if (pc.Paragraphs[0] is Text.TextFragment preTop)
                            preTop.Margin = new MarginInfo
                            { Top = 2.25, Left = preTop.Margin?.Left ?? 0 };
                        if (pc.Paragraphs[^1] is Text.TextFragment preBot)
                            preBot.Margin = new MarginInfo
                            {
                                Top = ReferenceEquals(pc.Paragraphs[0], pc.Paragraphs[^1])
                                    ? 2.25 : preBot.Margin?.Top ?? 0,
                                Bottom = 6.75,
                                Left = preBot.Margin?.Left ?? 0,
                            };
                        // …and the row floors at the pre box (lines + both
                        // margins) — a bottom margin alone does not grow it
                        var preLines = 0;
                        foreach (var prePar in pc.Paragraphs)
                            if (prePar is Text.TextFragment) preLines++;
                        // (short boxes only: a page-spanning pre must keep its
                        // natural height so the row can still paginate)
                        if (preLines is > 0 and <= 4)
                            preRow0.MinRowHeight = Math.Max(preRow0.MinRowHeight,
                                preLines * Table.CssLineBoxPt(cellFontSize > 0 ? cellFontSize
                                    : Table.DefaultCellFontPt) + 14.25);
                    }
                }
            naturalWidthPt = colModel.preMaxLinePt;
            table.HtmlPreferredWidthPt = Math.Max(table.HtmlPreferredWidthPt, colModel.preMaxLinePt);
            table.HtmlPreGrownGrid = true;
            // The grown grid rows pitch on the CSS line box plus the engine's
            // 2px default cellpadding pair (probed: 10 pt label rows step
            // 14.25 = the 15px line box + 3).
            table.DefaultCellPadding ??= new MarginInfo { Top = 1.5, Bottom = 1.5 };
            // <hr> cells draw the separator bar across their spanned DECLARED
            // columns (the phantom surplus column carries no rule).
            if (colModel.hrCells is not null)
            {
                var hrParts = table.ColumnWidths.Split(' ');
                foreach (var (hrRow, hc) in colModel.hrCells)
                {
                    double hrW = 0;
                    for (var hi = 0; hi < Math.Max(1, hc.ColSpan) && hi < hrParts.Length - 1; hi++)
                        if (double.TryParse(hrParts[hi], System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var hpw))
                            hrW += hpw;
                    if (hrW <= 0) continue;
                    hc.Paragraphs.Add(new Image
                    {
                        ImageStream = new System.IO.MemoryStream(HrBarPng()),
                        FixWidth = hrW - 3.0,
                        FixHeight = 1.5,
                        // the rule seats where a line's ink would (near the row's
                        // baseline), not at the padded row top
                        Margin = new MarginInfo { Top = 7.5 },
                    });
                    // the rule rides a normal line-box row (probed: the rule row
                    // keeps the label rows' 14.25 pitch)
                    // …at the full line-box-plus-padding pitch of its neighbours
                    hrRow.MinRowHeight = Math.Max(hrRow.MinRowHeight,
                        Table.CssLineBoxPt(cellFontSize > 0 ? cellFontSize : Table.DefaultCellFontPt)
                        + 3.0);
                }
            }
            foreach (Row preRow in table.Rows)
            {
                var preRowHasContent = false;
                foreach (Cell preC in preRow.Cells)
                    foreach (var prePara in preC.Paragraphs)
                    {
                        preRowHasContent = true;
                        if (prePara is Text.TextFragment preTf && preTf.CssLineHeightPt <= 0)
                            preTf.CssLineHeightPt = Table.CssLineBoxPt(preTf.TextState.FontSize);
                    }
                // an all-empty spacer row collapses to its padding pair (probed:
                // the case plan's spacer rows band ~3 pt, not a line box)
                if (!preRowHasContent && preRow.FixedRowHeight <= 0)
                    preRow.FixedRowHeight = 3.0;
            }

        }

        // The declared cellspacing is real horizontal space between and around the
        // columns, and each cell keeps the UA's 1px padding pair — both are part
        // of the sized sheet (the status report's pair
        // row + 3·cellspacing + 4·0.75 = its 698.62 content width).
        if (chainBase is not null && colModel.tblCellSpacingPt > 0 && colModel.maxCols > 0 && naturalWidthPt > 0)
            naturalWidthPt += (colModel.maxCols + 1) * colModel.tblCellSpacingPt + colModel.maxCols * 1.5;

        // A table declaring an ABSOLUTE width ATTRIBUTE (`width="680"`) FILLS it,
        // like a browser: when the columns' content fit stays narrower, they grow
        // proportionally — and a declared width beyond the available area carries
        // into the page auto-widen through the natural width. CSS width rules are
        // deliberately NOT fill targets here: the stylesheet grids resolve through
        // the percent/colgroup models above.
        double tableWidthAbsPt = 0;
        if (tblTag.Success)
        {
            var twAbs = Regex.Match(tblTag.Value, @"\bwidth\s*=\s*[""']?(\d+(?:\.\d+)?)\s*(px)?\s*[""'\s/>]",
                RegexOptions.IgnoreCase);
            if (twAbs.Success && double.TryParse(twAbs.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var twAbsPx))
                tableWidthAbsPt = twAbsPx * PxToPt;
        }
        // A DOCUMENT-rule element width (`table { width: 650px }`) is a fill
        // target the same way the attribute is: every grid on the page
        // stretches to the declared box (its one-cell banner rows span
        // the full width, and a narrow grid's columns scale up).
        if (tableWidthAbsPt <= 0 && colModel.tableWidthFromDocRule && colModel.tableWidthDeclAbsPt > 0)
            tableWidthAbsPt = colModel.tableWidthDeclAbsPt;
        if (tableWidthAbsPt > 0 && naturalWidthPt > 0 && tableWidthAbsPt > naturalWidthPt
            && table.ColumnWidths is { Length: > 0 } cwAbs && !cwAbs.Contains('%'))
        {
            var scale = tableWidthAbsPt / naturalWidthPt;
            var parts = cwAbs.Split(' ');
            var sb = new StringBuilder();
            for (var i = 0; i < parts.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                var w = double.Parse(parts[i], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture) * scale;
                sb.Append(w.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            }
            table.ColumnWidths = sb.ToString();
            naturalWidthPt = tableWidthAbsPt;
        }
        // Space and no-break space share one glyph, and the first of the two to occur
        // in the document's rendered text decides how BOTH read back out of the page —
        // a document that opens with an &nbsp; cell reports nbsp between all its words,
        // one that opens with plain text reports plain spaces even for &nbsp; entities.
        // ...and the decision belongs to the DOCUMENT, not to one grid: a lifted
        // nested table renders as its own Table, so scanning this one alone let an
        // inner grid that opens with plain text report plain spaces while the sheet's
        // very first cell held an &nbsp;. Walk the nested grids in document order too,
        // and hand every one of them the same winner.
        var grids = new List<Table>();
        CollectGrids(table, grids);
        foreach (var g in grids)
        {
            foreach (var r in g.Rows)
                foreach (Cell c in r.Cells)
                    foreach (var p in c.Paragraphs)
                        if (p is Text.TextFragment { Text: { Length: > 0 } t })
                            foreach (var ch in t)
                                if (ch is ' ' or ' ')
                                {
                                    foreach (var gg in grids) gg.HtmlSpaceClassFirst = ch;
                                    goto winnerFound;
                                }
        }
        winnerFound: ;   // the label needs a statement now that the method ends here
    }
}
