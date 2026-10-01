using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>A pre cell's longest unbreakable line grows the sheet past every declared width through an appended phantom column that only the pre cells span.</summary>
    private static void GrowForUnbreakablePreLine(TableWidthSolveState ws)
    {
        if (ws.colModel.preMaxLinePt > ws.naturalWidthPt && ws.naturalWidthPt > 0
            && ws.table.ColumnWidths is { Length: > 0 } preCw && !preCw.Contains('%'))
        {
            var preSurplus = ws.colModel.preMaxLinePt - ws.naturalWidthPt;
            ws.table.ColumnWidths = preCw + " " + preSurplus.ToString("0.###",
                System.Globalization.CultureInfo.InvariantCulture);
            if (ws.colModel.preCells is not null)
                foreach (var (preRow0, pc) in ws.colModel.preCells)
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
                                preLines * Table.CssLineBoxPt(ws.cellFontSize > 0 ? ws.cellFontSize
                                    : Table.DefaultCellFontPt) + 14.25);
                    }
                }
            ws.naturalWidthPt = ws.colModel.preMaxLinePt;
            ws.table.HtmlPreferredWidthPt = Math.Max(ws.table.HtmlPreferredWidthPt, ws.colModel.preMaxLinePt);
            ws.table.HtmlPreGrownGrid = true;
            // The grown grid rows pitch on the CSS line box plus the engine's
            // 2px default cellpadding pair (probed: 10 pt label rows step
            // 14.25 = the 15px line box + 3).
            ws.table.DefaultCellPadding ??= new MarginInfo { Top = 1.5, Bottom = 1.5 };
            // <hr> cells draw the separator bar across their spanned DECLARED
            // columns (the phantom surplus column carries no rule).
            if (ws.colModel.hrCells is not null)
            {
                var hrParts = ws.table.ColumnWidths.Split(' ');
                foreach (var (hrRow, hc) in ws.colModel.hrCells)
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
                        Table.CssLineBoxPt(ws.cellFontSize > 0 ? ws.cellFontSize : Table.DefaultCellFontPt)
                        + 3.0);
                }
            }
            foreach (Row preRow in ws.table.Rows)
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
    }

    /// <summary>Explicit widths fill the declared box, otherwise the content maxima are fitted into the available width, otherwise the columns share the sheet evenly.</summary>
    private static void ChooseColumnWidthStrategy(TableWidthSolveState ws)
    {
        if (ws.colModel.colWidthsPt is { Count: > 0 } cw && cw.Count == ws.colModel.maxCols)
        {
            FillDeclaredColumnWidths(ws, cw);
        }
        else if (ws.availWidthPt > 0 && ws.colModel.colMaxW.Count == ws.colModel.maxCols && ws.colModel.colMaxW.Count > 0
            && (ws.dwFormCells || ws.colModel.colMaxW.TrueForAll(w => w > 0)))
        {
            FitContentMaximaToAvailableWidth(ws);
        }
        else if (ws.colModel.maxCols > 0)
        {
            ShareSheetEvenly(ws);
        }
    }

    /// <summary>Percent and colgroup width models: when no explicit column widths exist, the declared percentages, the content minima and the available width settle the columns and the natural sheet width.</summary>
    private static void SolvePercentColumnWidths(TableWidthSolveState ws)
    {
        if (!(ws.colModel.colWidthsPt is null && ws.availWidthPt > 0 && ws.colModel.maxCols > 0 && ws.colModel.colPctW.Count > 0)) return;
        while (ws.colModel.colPctW.Count < ws.colModel.maxCols) ws.colModel.colPctW.Add(0);
        ws.sumPct = 0;
        ws.nSpec = 0;
        for (var i = 0; i < ws.colModel.maxCols; i++)
            if (ws.colModel.colPctW[i] > 0) { ws.sumPct += ws.colModel.colPctW[i]; ws.nSpec++; }
        // Form-document dialect: a LONE declared percent lays out the way a browser
        // does — the declared column takes its percent, the auto columns share the
        // remainder ("<td style='width:25%'>" beside an auto cell splits 75/25).
        // Outside the dialect the legacy majority guard holds.
        if (!(ws.nSpec * 2 >= ws.colModel.maxCols && ws.sumPct >= 50 || ws.cellFontShorthand && ws.nSpec > 0 && ws.sumPct < 100
                // Chain dialect: a LONE declared percent lays out browser-style too
                // (`.CategoryName { width: 80% }` — the name column takes its
                // percent, the detail buttons hug their min-content).
                || ws.chainBase is not null && ws.nSpec > 0 && ws.sumPct < 100
                // Over-declared grid dialect: same browser split for a lone
                // percent (`<td width="30%">` beside auto date cells).
                || ws.overDeclaredDraw && ws.nSpec > 0 && ws.sumPct < 100)) return;
        ws.rem = Math.Max(0, 100 - ws.sumPct) / Math.Max(1, ws.colModel.maxCols - ws.nSpec);
        ws.total = ws.sumPct + ws.rem * (ws.colModel.maxCols - ws.nSpec);
        ws.tableW = ws.colModel.tableWidthFrac * ws.availWidthPt;
        // A table that declares no width of its own is SHRINK-TO-FIT: its box is
        // only as wide as the content needs, and the declared percents split THAT,
        // not the page. The fitting width is the largest a column's own max-content
        // implies for the whole table (its share is pct/total of it), capped by
        // what is available.
        if (!ws.colModel.tableWidthDeclared && ws.uaDocGrid)
        {
            var fitW = 0.0;
            for (var i = 0; i < ws.colModel.maxCols; i++)
            {
                var pct = ws.colModel.colPctW[i] > 0 ? ws.colModel.colPctW[i] : ws.rem;
                var maxC = i < ws.colModel.colMaxW.Count ? ws.colModel.colMaxW[i] : 0;
                if (pct > 0 && maxC > 0) fitW = Math.Max(fitW, maxC * ws.total / pct);
            }
            if (fitW > 0) ws.tableW = Math.Min(ws.tableW, fitW);
        }
        ws.colModel.colWidthsPt = new List<double>(ws.colModel.maxCols);
        ws.mins = new double[ws.colModel.maxCols];
        ws.pctMinsForDraw = ws.mins;
        ws.sumW = 0;
        for (var i = 0; i < ws.colModel.maxCols; i++)
        {
            var w = (ws.colModel.colPctW[i] > 0 ? ws.colModel.colPctW[i] : ws.rem) / ws.total * ws.tableW;
            // Dash-aware floor: the percent grid lets a hyphenated token wrap
            // after its dashes, so the floor is the widest POST-BREAK segment.
            ws.mins[i] = i < ws.colModel.colMinBrkW.Count ? ws.colModel.colMinBrkW[i] : 0;
            var cwv = Math.Max(w, ws.mins[i]);
            ws.colModel.colWidthsPt.Add(cwv);
            ws.sumW += cwv;
        }
        ws.overExactShares = false;
        DrawOverDeclaredPercentGrid(ws);
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
        FitPercentSharesToTableWidth(ws);
        // A percent grid inside a table that DECLARES its own ABSOLUTE width
        // never widens the page: the declared width pins the box and any
        // residual floor overflow spills inside it (browser overflow). A
        // percent-declared or undeclared table has nothing absolute to pin
        // against — its columns keep their min-content floors and the grid
        // overflows the box, so the sheet grows to it.
        if (ws.colModel.tableWidthDeclaredAbs) ws.pctCapW = ws.tableW;
    }

    /// <summary>Without widths or content maxima the columns share the sheet evenly.</summary>
    private static void ShareSheetEvenly(TableWidthSolveState ws)
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
        if (ws.colModel.colMinW.Count == ws.colModel.maxCols)
        {
            foreach (var w in ws.colModel.colMinW)
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
        var nestedAbsorb = !minShares && ws.colModel.nestedTableCols is { Count: > 0 }
            && ws.colModel.nestedTableCols.Count < ws.colModel.maxCols && ws.colModel.colMinW.Count == ws.colModel.maxCols;
        double sumLabelMins = 0;
        if (nestedAbsorb)
            for (var i = 0; i < ws.colModel.maxCols; i++)
                if (!ws.colModel.nestedTableCols!.Contains(i)) sumLabelMins += ws.colModel.colMinW[i];
        var absorbBoxW = ws.colModel.tableWidthFrac * ws.availWidthPt;
        if (nestedAbsorb && (sumLabelMins <= 0 || sumLabelMins >= absorbBoxW * 0.5))
            nestedAbsorb = false;
        var sb = new StringBuilder();
        for (var i = 0; i < ws.colModel.maxCols; i++)
        {
            if (i > 0) sb.Append(' ');
            var share = nestedAbsorb
                ? (ws.colModel.nestedTableCols!.Contains(i)
                    ? Math.Max(0.01, (absorbBoxW - sumLabelMins) / absorbBoxW / ws.colModel.nestedTableCols.Count)
                    : ws.colModel.colMinW[i] / absorbBoxW) * ws.colModel.tableWidthFrac * 100.0
                : minShares
                ? ws.colModel.tableWidthFrac * 100.0 * ws.colModel.colMinW[i] / sumMinCols
                : ws.colModel.tableWidthFrac * 100.0 / ws.colModel.maxCols;
            sb.Append(share.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append('%');
        }
        ws.table.ColumnWidths = sb.ToString();
    }

    /// <summary>No explicit widths: the content maxima are fitted into the available width, shrinking the widest columns first and keeping every column at least its minimum.</summary>
    private static void FitContentMaximaToAvailableWidth(TableWidthSolveState ws)
    {
        ws.sumMax = 0; foreach (var w in ws.colModel.colMaxW) ws.sumMax += w;
        ws.minPref = new List<double>(ws.colModel.colMinW);
        ws.sumPref = 0;
        for (var i = 0; i < ws.minPref.Count; i++) { if (ws.colModel.colHdrW[i] > ws.minPref[i]) ws.minPref[i] = ws.colModel.colHdrW[i]; ws.sumPref += ws.minPref[i]; }
        ws.chosenMin = (ws.sumPref <= ws.availWidthPt) ? ws.minPref : ws.colModel.colMinW;
        ws.chosen = (ws.availWidthPt <= 0 || ws.sumMax <= ws.availWidthPt) ? ws.colModel.colMaxW : ws.chosenMin;
        // A declared cell width is honoured only while the table FITS its box — the
        // fixed columns keep their declared width (incl. cell padding) and the auto
        // columns absorb the leftover. Once the table overflows, min-content takes
        // over and the declarations contribute nothing.
        // Scoped to the UA-cell-box grids: elsewhere a fitting table keeps its
        // natural (max-content) columns, and stretching a width:100% one to the
        // full box re-wraps every calibrated legacy layout. The over-declared
        // grid dialect takes the same model — its 62px logo/spacer columns pin
        // and the auto title column absorbs the box.
        WidenChosenColumnsToBox(ws);
        ws.sb = new StringBuilder();
        ws.sumChosenAll = 0;
        foreach (var w in ws.chosen) ws.sumChosenAll += w;
        ws.emitPctCols = ws.colModel.tableWidthPctOfBox && ws.colModel.tableWidthDeclared
            && !ws.colModel.tableWidthDeclaredAbs && ws.sumChosenAll > 0;
        // The generator re-resolves these columns at DRAW time by the same
        // rule (fit → max-content + surplus; else floors + slack squeeze) —
        // hand it the per-column min/max the decision needs.
        EmitPercentColumnModel(ws);
        ws.lastAbsorbs = ws.emitPctCols && ws.chosen.Count > 1 && ws.emitBoxW > ws.sumChosenAll + 0.01
            // …and only when the last column HOLDS a nested grid (which fills
            // whatever it gets) — a text column's share must stay proportional,
            // and a nested build's availWidthPt may not be its real box anyway.
            && ws.colModel.nestedTableCols is not null && ws.colModel.nestedTableCols.Contains(ws.chosen.Count - 1);
        ws.fillCol = -1;
        DistributeFitWidths(ws);
        // The DataWorks grid widens the sheet to its OUTER box: the n+1
        // cellspacing gutters are part of the width the sheet grows to.
        if (ws.dwFormCells && ws.colModel.cellSpacingPt > 0 && ws.colModel.maxCols > 0)
            ws.naturalWidthPt += (ws.colModel.maxCols + 1) * ws.colModel.cellSpacingPt;
    }

    /// <summary>Explicit column widths fill the declared box: the declared values scale to the available width and the natural sheet width follows them.</summary>
    private static void FillDeclaredColumnWidths(TableWidthSolveState ws, List<double> cw)
    {
        // pt-styled fragment: an over-declared grid squeezes into its
        // declared table box (or the content width) instead of widening
        // the sheet — each column shedding in proportion to its slack
        // above min-content (same model as the auto branch's squeeze).
        if (ws.ptCellWidths && ws.availWidthPt > 0)
        {
            var ptCap = (ws.colModel.tableWidthDeclAbsPt > 0
                ? Math.Min(ws.colModel.tableWidthDeclAbsPt, ws.availWidthPt) : ws.availWidthPt)
                - ws.colModel.ptMaxCellBorderW;
            var ptSq = SqueezeBySlack(cw, ptCap, ws.colModel.colMinW);
            for (var i = 0; i < cw.Count; i++) cw[i] = ptSq[i];
        }
        ws.cwSum = 0;
        foreach (var w in cw) ws.cwSum += w;
        ws.emitPctHere = ws.chainBase is not null && ws.colModel.tableWidthDeclared
            && !ws.colModel.tableWidthDeclaredAbs && ws.cwSum > 0 && ws.colModel.colPctW.Count > 0;
        // Draw-time resolution data for the percent grid (see the fallback's
        // twin): declared shares floor at the dash-aware mins.
        ApplyPercentMinimaToDeclaredWidths(ws, cw);
        ws.table.ColumnWidths = ws.sb.ToString();
        if (ws.pctCapW > 0 && ws.naturalWidthPt > ws.pctCapW) ws.naturalWidthPt = ws.pctCapW;
        // Fixed columns inside a table whose DOCUMENT rule declares an
        // ABSOLUTE width squeeze into it, browser-fashion: the columns scale
        // down proportionally and the declared box is the preferred width —
        // the fixed sum never sizes the sheet past the declaration.
        FitDeclaredWidthsToDocumentRule(ws);
        // Content-driven natural for non-absolute percent grids (REPLACES the
        // box-filling sum): the layout keeps its percent columns, only the
        // reported preferred width changes.
        if (ws.pctNaturalW > 0) ws.naturalWidthPt = ws.pctNaturalW;
        // …and in the probe the PREFERRED width is capped the same way, or the
        // host cell of a nested grid takes the box-filling number right back
        // through max(natural, preferred).
        if (ws.fullWidthCjkMin && ws.pctNaturalW > 0 && ws.table.HtmlPreferredWidthPt > ws.pctNaturalW)
            ws.table.HtmlPreferredWidthPt = ws.pctNaturalW;
    }

    /// <summary>The percent shares are fitted to the table width: without an absolute declared width the sheet follows the shares, and shares that overflow the width are squeezed down to their minima first and proportionally after.</summary>
    private static void FitPercentSharesToTableWidth(TableWidthSolveState ws)
    {
        if (!ws.colModel.tableWidthDeclaredAbs)
        {
            // Shrink-to-fit: the grid's preferred width is its max-content sum
            // CLAMPED to the table box (a paragraph column's one-line max must
            // not size the sheet), floored at the min-content floors (an
            // unbreakable run still grows the sheet past the box).
            double cnat = 0, cmax = 0;
            for (var i = 0; i < ws.colModel.maxCols; i++)
            {
                cnat += ws.mins[i];
                cmax += Math.Max(ws.mins[i], i < ws.colModel.colMaxW.Count ? ws.colModel.colMaxW[i] : 0);
            }
            ws.pctNaturalW = Math.Max(cnat, Math.Min(cmax, ws.tableW));
            // The page-width PROBE reports the min-content floor sum alone:
            // a width:100% grid FILLS whatever box it gets, so a box-clamped
            // natural is circular — it echoes the stand-in page back and the
            // sheet widens by nothing but its own chrome. The sheet
            // widens only when the floors themselves overflow.
            if (ws.fullWidthCjkMin) ws.pctNaturalW = cnat;
            // An OVER-DECLARED fixed-layout attribute grid (one row's width
            // attributes sum past 100%) cannot fit any box: each percent
            // resolves against the DEFAULT page's content box
            // and widens the sheet to the resulting demand — content plays
            // no part (probed: 5/37/35/15/10 + a 25px column = the demand
            // below, exact on the probe ladder, +0.2pt on the shipped doc).
            if (ws.fullWidthCjkMin && ws.chainBase is null && !ws.colModel.tableWidthDeclaredAbs && ws.colModel.tableWidthDeclared
                && ws.rowPctDeclMax > 100.0 + 1e-6
                && ws.tblStyle.TryGetValue("table-layout", out var tlFix)
                && tlFix.Contains("fixed", StringComparison.OrdinalIgnoreCase))
            {
                // The percent base: the default content box is the
                // page minus margins minus the UA body gutter (595−96−90−6 =
                // 403); our caller's avail carries the margins already.
                var overPctBase = ws.availWidthPt - UaBodyMarginPt;
                // A pixel-declared column rides along at its width plus its
                // cell padding pair and one spacing unit.
                var overPxCols = ws.rowPxAtMax
                    + ws.rowPxCellsAtMax * (2 * Math.Max(0, ws.padSide) + ws.colModel.tblCellSpacingPt);
                // Measured residual of the column balancer for this
                // family (the 102%→596.885 / 110%→629.125 ladder solves
                // base 403 and this constant), minus the +8 body slack the
                // widen ladder adds back on top of the reported natural.
                const double OverDeclaredResidualPt = -6.175;
                const double WidenLadderSlackPt = 8.0;
                ws.pctNaturalW = ws.rowPctDeclMax / 100.0 * overPctBase + overPxCols
                    + OverDeclaredResidualPt - WidenLadderSlackPt;
                ws.table.HtmlOverDeclaredGrid = true;
            }
        }
        if (!ws.overExactShares && ws.sumW > ws.tableW + 0.01)
        {
            var excess = ws.sumW - ws.tableW;
            double waste = 0;
            var wasteCol = new double[ws.colModel.maxCols];
            for (var i = 0; i < ws.colModel.maxCols; i++)
            {
                var cap = Math.Max(ws.mins[i], i < ws.colModel.colMaxW.Count ? ws.colModel.colMaxW[i] : 0);
                wasteCol[i] = Math.Max(0, ws.colModel.colWidthsPt![i] - cap);
                waste += wasteCol[i];
            }
            var takeW = Math.Min(excess, waste);
            if (waste > 0)
                for (var i = 0; i < ws.colModel.maxCols; i++) ws.colModel.colWidthsPt![i] -= wasteCol[i] / waste * takeW;
            excess -= takeW;
            if (excess > 0.01)
            {
                double slack = 0;
                for (var i = 0; i < ws.colModel.maxCols; i++) slack += ws.colModel.colWidthsPt![i] - ws.mins[i];
                if (slack > 0)
                    for (var i = 0; i < ws.colModel.maxCols; i++)
                        ws.colModel.colWidthsPt![i] -= (ws.colModel.colWidthsPt[i] - ws.mins[i]) / slack * Math.Min(excess, slack);
            }
        }
    }

    /// <summary>Over-declared grid dialect: a lone declared percent takes its share and the auto columns split the rest browser-style.</summary>
    private static void DrawOverDeclaredPercentGrid(TableWidthSolveState ws)
    {
        if (ws.overDeclaredDraw && ws.nSpec > 0
            && !(ws.tblStyle.TryGetValue("table-layout", out var tlDraw)
                && tlDraw.Contains("fixed", StringComparison.OrdinalIgnoreCase)))
        {
            ws.overExactShares = true;
            // Every column declared but the sum under 100%: the shares
            // scale UP to fill the declared box (the note-box
            // row [33,10,50] draws at [35.5,10.75,53.8] — the box's right
            // border lands on the table's right edge).
            var pctScale = ws.nSpec == ws.colModel.maxCols && ws.sumPct is > 0 and < 100 && ws.colModel.tableWidthDeclared
                ? 100.0 / ws.sumPct : 1.0;
                                double fixedSum = 0;
            var autoN = 0;
            for (var i = 0; i < ws.colModel.maxCols; i++)
                if (ws.colModel.colPctW[i] > 0)
                {
                    ws.colModel.colWidthsPt![i] = Math.Max(ws.colModel.colPctW[i] * pctScale / 100.0 * ws.tableW, ws.mins[i]);
                    fixedSum += ws.colModel.colWidthsPt[i];
                }
                else autoN++;
            var autoW = Math.Max(0, ws.tableW - fixedSum) / Math.Max(1, autoN);
            ws.sumW = fixedSum;
            for (var i = 0; i < ws.colModel.maxCols; i++)
                if (ws.colModel.colPctW[i] <= 0)
                {
                    ws.colModel.colWidthsPt![i] = Math.Max(autoW, ws.mins[i]);
                    ws.sumW += ws.colModel.colWidthsPt[i];
                }
        }
    }

    /// <summary>Sum of the first <paramref name="count"/> entries of a width list.</summary>
    private static double SumPrev(List<double> list, int count)
    {
        double s = 0;
        for (var k = 0; k < count; k++) s += list[k];
        return s;
    }

    /// <summary>The fitted widths are written to the column model: a fill column absorbs what the others leave, each column takes its share, and the sheet's preferred width follows the natural width or the declared percent of the box.</summary>
    private static void DistributeFitWidths(TableWidthSolveState ws)
    {
        if (ws.emitPctCols && !ws.lastAbsorbs && ws.colModel.colPctW.Count <= ws.chosen.Count
            && ws.emitBoxW > ws.sumChosenAll + 0.01)
        {
            var declCount = 0;
            for (var i = 0; i < ws.colModel.colPctW.Count; i++)
                if (ws.colModel.colPctW[i] >= 100) { ws.fillCol = i; declCount++; }
            if (declCount != 1) ws.fillCol = -1;
        }
        ws.sumOtherMins = 0;
        if (ws.fillCol >= 0)
            for (var i = 0; i < ws.chosen.Count; i++)
                if (i != ws.fillCol) ws.sumOtherMins += ws.chosen[i];
        for (var i = 0; i < ws.chosen.Count; i++)
        {
            if (i > 0) ws.sb.Append(' ');
            if (ws.emitPctCols)
            {
                var share = ws.fillCol >= 0
                    ? (i == ws.fillCol
                        ? Math.Max(0.01, 1.0 - ws.sumOtherMins / ws.emitBoxW)
                        : ws.chosen[i] / ws.emitBoxW)
                    : ws.lastAbsorbs
                    ? (i == ws.chosen.Count - 1
                        ? Math.Max(0.01, 1.0 - SumPrev(ws.chosen, i) / ws.emitBoxW)
                        : ws.chosen[i] / ws.emitBoxW)
                    : ws.chosen[i] / ws.sumChosenAll;
                ws.sb.Append((share * 100.0 * ws.colModel.tableWidthFrac)
                    .ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append('%');
            }
            else
                ws.sb.Append(ws.chosen[i].ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            ws.naturalWidthPt += ws.chosen[i];
        }
        ws.table.ColumnWidths = ws.sb.ToString();
        // A chain-rule percent width (the `.Budget > table { width: 100% }`
        // idiom) resolves against its box at layout time and never sizes the
        // sheet — the reported natural is the PLAIN min-content floor sum
        // (the multi-word `Period / Cost type` header wraps;
        // single-word headers hold their width because one word
        // cannot wrap), the same rule the percent-column grids apply above.
        ws.table.HtmlPreferredWidthPt = ws.naturalWidthPt;
        if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_TABLEW") == "1")
        {
            double dbgMin = 0, dbgSerif = 0;
            foreach (var w in ws.colModel.colMinW) dbgMin += w;
            foreach (var w in ws.colModel.colMinSerifW) dbgSerif += w;
            Console.Error.WriteLine($"[natw] natural={ws.naturalWidthPt:0.##} pctOfBox={ws.colModel.tableWidthPctOfBox} serifMin={ws.uaSerifMin} declared={ws.colModel.tableWidthDeclared} declaredAbs={ws.colModel.tableWidthDeclaredAbs} sumMin={dbgMin:0.##} serifSum={dbgSerif:0.##} serifCols={ws.colModel.colMinSerifW.Count} serif=[{string.Join(",", ws.colModel.colMinSerifW.ConvertAll(v => v.ToString("0.#")))}] min=[{string.Join(",", ws.colModel.colMinW.ConvertAll(v => v.ToString("0.#")))}]");
        }
        // (an UNDECLARED width under the UA serif floors reports the same min floor sum: the
        // sheet grows to a table's min-content ink, never to its max-content - probed: three
        // wrappable sentences keep the 595 sheet, three unbreakable words page 1346.14)
        // (…and a percent width spelt in the table's own style is a percent of the box too)
        if ((ws.colModel.tableWidthPctOfBox || (ws.uaSerifMin && !ws.colModel.tableWidthDeclaredAbs))
            && !ws.colModel.tableWidthDeclaredAbs)
        {
            double sumMinPref = 0;
            foreach (var w in ws.colModel.colMinW) sumMinPref += w;
            ws.naturalWidthPt = sumMinPref;
            if (ws.uaSerifMin && ws.colModel.colMinSerifW.Count > 0)
            {
                double serifSum = 0;
                foreach (var w in ws.colModel.colMinSerifW) serifSum += w;
                if (serifSum > 0)
                {
                    ws.naturalWidthPt = serifSum;
                    ws.table.HtmlPctMinNatural = true;
                    ws.table.HtmlMinFloorTrailingPt = ws.colModel.colControlFloor.Count > 0 && ws.colModel.colControlFloor[^1]
                        ? UaTextControlChromePt : 0;
                }
            }
        }
    }

    /// <summary>Percent-column grids hand the generator the per-column minima and maxima it re-resolves at draw time; a declared absolute box without them lets the nested grid take the rest of the box.</summary>
    private static void EmitPercentColumnModel(TableWidthSolveState ws)
    {
        if (ws.emitPctCols && ws.colModel.colMinW.Count == ws.chosen.Count)
        {
            ws.table.HtmlColMinPt = ws.colModel.colMinW.ToArray();
            if (ws.colModel.colMaxW.Count == ws.chosen.Count)
            {
                var hMax = new double[ws.colModel.colMaxW.Count];
                for (var i = 0; i < ws.colModel.colMaxW.Count; i++)
                    hMax[i] = Math.Max(ws.colModel.colMaxW[i], ws.colModel.colMinW[i]);
                ws.table.HtmlColMaxPt = hMax;
            }
            // A cell that DECLARED `width="100%"` is a real box-filling target, so
            // the draw-time resolver must hand IT the surplus instead of spreading
            // it over the max-content proportions — without the mask the emitted
            // share was recomputed away and the declared column kept a quarter of
            // its row, shrinking every grid nested inside it in the same ratio.
            // A column whose width was DECLARED absolutely (`<td width="15">` — the
            // layout-table spacer idiom) is FIXED in CSS auto layout: it keeps that
            // width and the box's surplus goes to the auto columns beside it.
            if (ws.colModel.colDeclW.Count == ws.chosen.Count)
            {
                var fixedCols = new bool[ws.chosen.Count];
                var anyFixed = false;
                for (var i = 0; i < ws.chosen.Count; i++)
                    if (ws.colModel.colDeclW[i] > 0 && (i >= ws.colModel.colPctW.Count || ws.colModel.colPctW[i] <= 0))
                        fixedCols[i] = anyFixed = true;
                if (anyFixed) ws.table.HtmlColFixedCols = fixedCols;
            }
            var anyFillDecl = false;
            for (var i = 0; i < ws.colModel.colPctW.Count && i < ws.chosen.Count; i++)
                if (ws.colModel.colPctW[i] >= 100) { anyFillDecl = true; break; }
            if (anyFillDecl)
            {
                var pctDeclB = new bool[ws.chosen.Count];
                for (var i = 0; i < pctDeclB.Length && i < ws.colModel.colPctW.Count; i++)
                    pctDeclB[i] = ws.colModel.colPctW[i] >= 100;
                ws.table.HtmlColPctDeclared = true;
                ws.table.HtmlColPctDeclaredCols = pctDeclB;
                ws.table.HtmlUaAutoYield = ws.uaCellBoxes;
            }
            // A trailing nested-grid column absorbs ALL the surplus (it
            // stretches to fill; its siblings hug their content on the left —
            // the title row). A LEADING grid column (the risks pills) keeps
            // its floor and the surplus stays proportional in the text columns.
            // (…not in a UA-boxed grid: CSS auto layout hands the surplus to every column in proportion to its
            //  max-content - the mailing's remittance rows keep their 72/28 split beside the trailing grid)
            if (ws.colModel.nestedTableCols is not null && ws.colModel.nestedTableCols.Contains(ws.chosen.Count - 1) && !ws.uaCellBoxes)
                ws.table.HtmlSurplusCol = ws.chosen.Count - 1;
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
        if (ws.emitPctCols && ReferenceEquals(ws.chosen, ws.colModel.colMaxW)
            && ws.colModel.colMinW.Count == ws.chosen.Count)
        {
            // Plain min floors when max-content was chosen against the build's
            // stand-in box; a table already on its min/pref floors keeps them
            // (the Risks grid's wide text columns take the surplus).
            ws.chosen = ws.colModel.colMinW;
            ws.sumChosenAll = 0;
            foreach (var w in ws.chosen) ws.sumChosenAll += w;
        }
        ws.emitBoxW = ws.colModel.tableWidthFrac * ws.availWidthPt;
        // A DECLARED-width layout table whose surplus belongs to its nested-grid
        // column(s): the label cells keep their content width and the grid fills
        // the rest of the declared box (the "1." marker beside the bordered case
        // table). Without this the nested grid keeps its natural width and the
        // whole declared box goes unused.
        if (!ws.emitPctCols && ws.colModel.tableWidthDeclaredAbs && ws.colModel.nestedTableCols is { Count: > 0 }
            && ws.colModel.nestedTableCols.Count < ws.chosen.Count)
        {
            var absorbBox = ws.emitBoxW - (ws.chosen.Count + 1) * Math.Max(ws.colModel.cellSpacingPt, 0);
            var absorbSurplus = absorbBox - ws.sumChosenAll;
            if (absorbSurplus > 0.5)
            {
                ws.chosen = new List<double>(ws.chosen);
                foreach (var ci in ws.colModel.nestedTableCols)
                    if (ci < ws.chosen.Count) ws.chosen[ci] += absorbSurplus / ws.colModel.nestedTableCols.Count;
                ws.sumChosenAll += absorbSurplus;
            }
        }
    }

    /// <summary>The chosen widths grow into the box: box-sized dialects pin their fixed columns and let the auto column absorb the width, and a collapsed fit hands the surplus to the columns that still want to grow.</summary>
    private static void WidenChosenColumnsToBox(TableWidthSolveState ws)
    {
        // (…and a grid the sheet reaches through its ancestors: CSS auto layout hands a declared
        // box's leftover to the auto columns in proportion to their max-content - measured on the
        // 300 px grid: 39.3/41.2 of max-content become 108.76/114 of the 222.75 box)
        WidenUaCellBoxColumnsToBox(ws);
        // A width-declared table (WIDTH="N%") fills its box: when the natural columns
        // overflowed and collapsed to min-content, hand the leftover width to the
        // columns that still want to grow (room = max-content − chosen), proportionally,
        // so the flexible text column expands to fill instead of wrapping to a sliver.
        // Fixed columns (max ≈ min) keep their width. Only the overflow (collapsed) case.
        if (!ReferenceEquals(ws.chosen, ws.colModel.colMaxW) && !ws.dwFormCells)
        {
            var tableW = ws.colModel.tableWidthFrac * ws.availWidthPt;
            double sumChosen = 0; foreach (var w in ws.chosen) sumChosen += w;
            if (tableW > sumChosen + 0.01)
            {
                double sumRoom = 0;
                for (var i = 0; i < ws.chosen.Count; i++) sumRoom += Math.Max(0, ws.colModel.colMaxW[i] - ws.chosen[i]);
                if (sumRoom > 0)
                {
                    var filled = new List<double>(ws.chosen);
                    var leftover = tableW - sumChosen;
                    for (var i = 0; i < filled.Count; i++)
                    {
                        var room = Math.Max(0, ws.colModel.colMaxW[i] - ws.chosen[i]);
                        filled[i] += leftover * room / sumRoom;
                    }
                    ws.chosen = filled;
                }
            }
        }
    }

    /// <summary>A document-rule table width scales the declared columns to its box, and percent minima that exceed the content maxima widen the preferred sheet.</summary>
    private static void FitDeclaredWidthsToDocumentRule(TableWidthSolveState ws)
    {
        if (ws.colModel.tableWidthFromDocRule && ws.colModel.tableWidthDeclAbsPt > 0 && ws.naturalWidthPt > ws.colModel.tableWidthDeclAbsPt
            && ws.table.ColumnWidths is { Length: > 0 } cwDecl && !cwDecl.Contains('%'))
        {
            var declScale = ws.colModel.tableWidthDeclAbsPt / ws.naturalWidthPt;
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
            ws.table.ColumnWidths = declSb.ToString();
            ws.naturalWidthPt = ws.colModel.tableWidthDeclAbsPt;
        }
        // The PREFERRED width of a percent grid is its shrink-to-fit CONTENT
        // preference, NOT the box-filling share sum (resolved against the
        // build's stand-in box, that sum hands the HOST cell absurd max-content
        // room — the risks pill column balloons on it).
        ws.table.HtmlPreferredWidthPt = ws.naturalWidthPt;
        if (ws.pctMinsForDraw is { } prefMins && ws.colModel.colMaxW.Count == prefMins.Length)
        {
            double prefMin = 0, prefMax = 0, autoMax = 0, declFrac = 0;
            for (var i = 0; i < prefMins.Length; i++)
            {
                prefMin += prefMins[i];
                var cMax = Math.Max(ws.colModel.colMaxW[i], prefMins[i]);
                prefMax += cMax;
                if (i < ws.colModel.colPctW.Count && ws.colModel.colPctW[i] > 0) declFrac += ws.colModel.colPctW[i] / 100.0;
                else autoMax += cMax;
            }
            // CSS max-content of a grid with a DECLARED percent column: the auto
            // columns fill the remaining (1 − p) of the table, so the whole table
            // wants autoMax / (1 − p). It is what makes the risks pill's host
            // column ask for room beyond its content floors (the
            // 148.7 pt Risk-Category column) instead of pinning at min-content.
            if (ws.chainBase is not null && declFrac > 0 && declFrac < 1 && autoMax > 0)
                prefMax = Math.Max(prefMax, autoMax / (1 - declFrac));
            ws.table.HtmlPreferredWidthPt = Math.Max(prefMin,
                Math.Min(prefMax, ws.naturalWidthPt));
        }
    }

    /// <summary>Declared widths that came from percents keep the per-column minima the draw needs, and every column is floored at its own minimum.</summary>
    private static void ApplyPercentMinimaToDeclaredWidths(TableWidthSolveState ws, List<double> cw)
    {
        if (ws.emitPctHere && ws.pctMinsForDraw is { } pmins && pmins.Length == cw.Count)
        {
            ws.table.HtmlColMinPt = pmins;
            ws.table.HtmlColPctDeclared = true;
            // Which of the emitted shares were really declared: the rest carry the
            // even leftover this branch synthesises for the auto columns, and the
            // draw-time resolver must not treat those as fill targets.
            var pctDecl = new bool[cw.Count];
            for (var i = 0; i < pctDecl.Length && i < ws.colModel.colPctW.Count; i++)
                pctDecl[i] = ws.colModel.colPctW[i] > 0;
            ws.table.HtmlColPctDeclaredCols = pctDecl;
            if (ws.colModel.colMaxW.Count == cw.Count)
            {
                var hMax2 = new double[ws.colModel.colMaxW.Count];
                for (var i = 0; i < ws.colModel.colMaxW.Count; i++)
                    hMax2[i] = Math.Max(ws.colModel.colMaxW[i], pmins[i]);
                ws.table.HtmlColMaxPt = hMax2;
            }
        }
        ws.sb = new StringBuilder();
        for (var i = 0; i < cw.Count; i++)
        {
            if (i > 0) ws.sb.Append(' ');
            if (ws.emitPctHere)
                ws.sb.Append((cw[i] / ws.cwSum * 100.0 * ws.colModel.tableWidthFrac)
                    .ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append('%');
            else
                ws.sb.Append(cw[i].ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            ws.naturalWidthPt += cw[i];
        }
    }

    /// <summary>The rule an <c>&lt;hr&gt;</c> row draws: a two-pixel bar across the columns its cell
    /// spans, seated in the middle of the band the row was given (see HrRowsAsBands - the band is the
    /// rule's own box, this is the ink inside it).</summary>
    private static void DrawCellBoxSheetRules(Table table, TableColumnModel colModel)
    {
        if (!table.HtmlCellBoxSheet || colModel.hrCells is null
            || table.ColumnWidths is not { Length: > 0 } cw) return;
        var parts = cw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var (hrRow, hc) in colModel.hrCells)
        {
            double ruleW = 0;
            for (var i = 0; i < Math.Max(1, hc.ColSpan) && i < parts.Length; i++)
                if (double.TryParse(parts[i], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var pw)) ruleW += pw;
            if (ruleW <= 0) continue;
            var band = hrRow.MinRowHeight;
            table.HtmlRuleRows = true;
            hc.Paragraphs.Add(new Image
            {
                ImageStream = new System.IO.MemoryStream(HrBarPng()),
                FixWidth = ruleW,
                FixHeight = UaHrRulePt,
                Margin = new MarginInfo { Top = band > UaHrRulePt ? (band - UaHrRulePt) / 2 : 0 },
            });
        }
    }
}
