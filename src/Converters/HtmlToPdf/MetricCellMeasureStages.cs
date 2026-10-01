using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>One cell's wrap: its face, its line boxes and the natural width its column must hold.</summary>
    private static bool MeasureMetricCell(MetricTableState mt, List<MetricCell> r, int c)
    {
        var mc = r[c];
        var cellFs0 = mc.FontSize ?? mt.mps.fontSize;
        if (mc.Text.Length == 0 && mc.SubTables is not { Count: > 0 }
            && mc.DivSegs is not { Count: > 0 })
        {
            mc.Lines = [];
            // an <hr> cell is the rule's groove inside its UA margins (0.5 em above and below,
            // in the cell's own size or the browser's default: the loose-doctype form's
            // rating rows pitch 27.75 = a 12.75 text row + a 15 pt rule row)
            var emptyContent = mc.HrRule
                ? Math.Max(mc.ImgHPt, 2 * HrCellMarginEm * (mc.FontSize ?? UaDefaultFontPt) + (mc.HrBoxPt > 0 ? mc.HrBoxPt : HrGrooveH))
                // a cell holding only its checkbox holds the checkbox's line box
                : mc.LeadCheckboxName is not null
                ? Math.Max(mc.ImgHPt, MetricCheckboxLineHeight(mt.mps, mt.stdSerif, mt.wrapperStacks, mt.hheaSum, mt.face, mt.fm, mc, cellFs0))
                : mc.ImgHPt;
            // …and a cell holding only its text input holds the input's box
            if (mc.InputBoxes is { Count: > 0 }) emptyContent = Math.Max(emptyContent, MetricInputsLineBoxPt(mc));
            mc.ContentH = emptyContent + mc.PadTopPt + mc.PadBottomPt;
            return true;
        }
        if (mc.Text.Length == 0) mc.Lines = [];
        var cellFs = mc.FontSize ?? mt.mps.fontSize;

        // FIXED layout never wraps — the content overflows its column.
        var effW = mt.colW[c];
        for (var k = 1; k < mc.ColSpan && c + k < mt.nCols; k++)
            effW += 2 * mt.p + mt.s + mt.colW[c + k];
        // class padding/border-left eat into the wrap width, and so does a padding-right
        if (mc.PadLeft > 0 || mc.BorderLeftW > 0 || mc.PadRight > 0)
            effW -= CellPadLeftExtra(mc, mt.p) + mc.BorderLeftW + CellPadRightExtra(mc, mt.p);
        // div-stacked content: each div is one styled band — its class
        // height floors the band, wrapped lines grow it
        if (MeasureMetricCellDivSegments(mt, mc, ref effW)) return true;
        // newsletter cells: whitespace GLUE between nested tables
        // (the &nbsp; separators the markup leaves in the container td)
        // holds no line box of its own
        WrapMetricCellContent(mt, mc, effW, cellFs);
        return true;
    }

    /// <summary>A nested grid's height, solved the way its render will solve it: its rows' bands
    /// (content or strut, padding, a declared height) and the spacing around them.</summary>
    private static double MetricGridHeightPt(MetricTableState host, string html, MetricCell hostCell, double availW)
    {
        var mt = ParseMetricTable(host.doc, html, host.css, host.marginLeft, availW, host.pageWidth,
            host.pageHeight, host.marginTop, host.marginBottom, GridTypoCell(hostCell).Face ?? host.face, CellFm(host.fm, GridTypoCell(hostCell)),
            host.docFontDict, host.stdSerif, GridTypoCell(hostCell).FontSize ?? host.mps.fontSize, wrapperStacks: true,
            symInsetPt: 0, host.rtl, host.paragraphCells, host.serifReportCells, host.loadOptions, host.siblingCellRules,
            uaBlockCells: host.mps.uaBlockCells, uaFormCells: host.mps.uaFormCells,
            pageMargin: (host.mps.pageMarginLeft, host.mps.pageMarginTop), hostCellPadPt: host.p);
        // a BORDERED nested grid paints as it solves - it keeps the wrapped-extent estimate
        // (the solve here has no page to paint on)
        if (mt.mps.bordered)
            return NestedTableWrappedHeight(html, host.lineH, GridTypoCell(hostCell).Face ?? host.face,
                GridTypoCell(hostCell).FontSize ?? host.mps.fontSize, availW);
        mt.cursor = new FlowPosition { y = host.pageHeight };
        if (!SolveMetricTable(mt)) return 0;
        var h = mt.s + mt.mps.tablePadTopPt + mt.mps.tablePadBottomPt;
        for (var ri = 0; ri < mt.rows.Count; ri++)
        {
            var r = mt.rows[ri];
            var band = 0.0;
            if (r.Count > 0)
            {
                var content = mt.tableHasText && !mt.mps.uaBlockCells && !MetricRowTextAllRowStyled(r) ? mt.lineH : 0;
                foreach (var mc in r)
                {
                    if (mc.RowSpan <= 1) content = Math.Max(content, mc.ContentH);
                    if (mt.mps.uaBlockCells) content = Math.Max(content, mc.HeightStylePt);
                }
                band = content + 2 * mt.p;
                if (ri < mt.mps.rowHeights.Count && mt.mps.rowHeights[ri] > band) band = mt.mps.rowHeights[ri];
            }
            h += band + mt.s;
        }
        return h;
    }

    /// <summary>A UA block band's height: every hard line wrapped at its own size, each on its own line box.</summary>
    private static double UaBlockBandHeight(MetricTableState mt, MetricCell mc, MetricDivSeg sg, double wrapW)
        => UaBlockBandHeightPt(mt.mps, mt.stdSerif, mt.wrapperStacks, mt.hheaSum, mt.face, mt.boldFace, mt.fm, sg, wrapW);

    /// <summary>A UA block band's height: every hard line wrapped at the width in its own bold and size.</summary>
    private static double UaBlockBandHeightPt(MetricParseState mps, bool stdSerif, bool wrapperStacks, double hheaSum,
        string face, string boldFace, (double asc, double sum) fm, MetricDivSeg sg, double wrapW)
    {
        var h = 0.0;
        var hardLines = sg.Text.Split('\u0001');
        for (var hi = 0; hi < hardLines.Length; hi++)
        {
            var (lineBold, lineFsOpt) = hi < sg.LineTypo.Count ? sg.LineTypo[hi] : (false, null);
            var probe = new MetricCell { Face = sg.Face, Bold = sg.Bold || lineBold, FontSize = lineFsOpt ?? sg.FontSize };
            var fs = probe.FontSize ?? mps.fontSize;
            var lineH = CellLineOf(mps, stdSerif, wrapperStacks, hheaSum, face, fm, probe, fs);
            var text = hardLines[hi].Trim(' ');
            var n = text.Length == 0 ? 1 : MeasuredWordWrap(text, wrapW, CellFaceName(face, boldFace, probe), fs, dashBreaks: true).Length;
            h += n * lineH;
        }
        return h;
    }

    /// <summary>The overflow comes off the auto columns - proportionally when several share it, off the widest alone when one does.</summary>
    private static void ShareMetricColumnDeficit(MetricTableState mt)
    {
        YieldSpacerColumnsToDeficit(mt);
        if (mt.autoCols > 1)
        {
            var minW = new double[mt.nCols];
            for (var c = 0; c < mt.nCols; c++)
            {
                if (mt.colFixed[c] || mt.colW[c] <= 0) continue;
                minW[c] = MetricColumnMinContentPt(mt, c);
            }
            // A class PERCENT column pins at max(its share, min-content) in
            // an over-constrained table (probed: the worksheet's 10% label
            // grid wraps one word per line while its 2-column sibling —
            // which FITS — keeps max-content untouched).
            var colClassPct = new double[mt.nCols];
            foreach (var r in mt.rows)
                for (var c = 0; c < Math.Min(r.Count, mt.nCols); c++)
                    if (r[c].ColSpan <= 1 && r[c].ClassWidthPct > 0)
                        colClassPct[c] = Math.Max(colClassPct[c], r[c].ClassWidthPct);
            double fixedSumB = (mt.nCols + 1) * mt.s + mt.nCols * 2 * mt.p, minSum = 0, excessSum = 0;
            for (var c = 0; c < mt.nCols; c++)
            {
                if (mt.colFixed[c] || mt.colW[c] <= 0)
                {
                    if (!mt.colFixed[c] && colClassPct[c] > 0)
                    {
                        // an EMPTY percent column still takes its share
                        mt.colW[c] = colClassPct[c] / 100.0 * mt.availW;
                        mt.colFixed[c] = true;
                    }
                    fixedSumB += mt.colW[c];
                    continue;
                }
                if (colClassPct[c] > 0)
                {
                    mt.colW[c] = Math.Max(colClassPct[c] / 100.0 * mt.availW, minW[c]);
                    mt.colFixed[c] = true;
                    fixedSumB += mt.colW[c];
                    continue;
                }
                minSum += minW[c];
                excessSum += Math.Max(0, mt.colW[c] - minW[c]);
            }
            var room = MetricFitWidth(mt) - fixedSumB - minSum;
            if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_MCOL") == "1")
                Console.Error.WriteLine($"[mdeficit] fit={MetricFitWidth(mt):0.##} fixedSumB={fixedSumB:0.##} minSum={minSum:0.##} room={room:0.##} excess={excessSum:0.##} min=[{string.Join(",", Array.ConvertAll(minW, w => w.ToString("0.#")))}] w=[{string.Join(",", Array.ConvertAll(mt.colW, w => w.ToString("0.#")))}] fixed=[{string.Join(",", mt.colFixed)}]");
            // A grid over-constrained by less than one CSS reference pixel is not
            // over-constrained at all: the browser lays its columns out on whole device
            // pixels, so a shortfall under that grid cannot take width off a column. Letting
            // it costs a whole LINE, because a column pushed even a hundredth of a point
            // below its own max-content wraps a word — measured on a 571 pt grid whose
            // columns wanted 571.215: the proportional shrink took 0.017 pt off one of them,
            // broke `ZIP Code & City` across two lines, and paginated a 60 page document to
            // 61. The grid keeps its content and overruns its declared width by the
            // sub-pixel instead, which is what the reference draws.
            if (excessSum > room && excessSum - room < UaFace.CssPxToPt) room = excessSum;
            if (room > 0 && excessSum > 0)
            {
                for (var c = 0; c < mt.nCols; c++)
                    if (!mt.colFixed[c] && mt.colW[c] > 0)
                        mt.colW[c] = minW[c] + room * Math.Max(0, mt.colW[c] - minW[c]) / excessSum;
            }
            else if (excessSum > 0)
            {
                for (var c = 0; c < mt.nCols; c++)
                    if (!mt.colFixed[c] && mt.colW[c] > 0)
                        mt.colW[c] = Math.Max(mt.mps.fontSize, minW[c]);
            }
        }
        else
            for (var c = mt.nCols - 1; c >= 0; c--)
                if (!mt.colFixed[c])
                {
                    var others = (mt.nCols + 1) * mt.s;
                    for (var o = 0; o < mt.nCols; o++) if (o != c) others += mt.colW[o] + 2 * mt.p;
                    // (a UA column never gives up its min-content: a one-word cell keeps its word)
                    var floor = mt.stdSerif ? Math.Max(mt.mps.fontSize, MetricColumnMinContentPt(mt, c)) : mt.mps.fontSize;
                    mt.colW[c] = Math.Max(floor, Math.Min(mt.availW, MetricDeclaredCapPt(mt)) - others - 2 * mt.p);
                    break;
                }
    }

    /// <summary>An over-constrained UA grid takes its declared SPACER columns down to their ink: a
    /// whitespace-only cell's declared width is a preference, not a floor, once the columns' own
    /// min-contents overflow the fit (probed on the Words letter: the 15 pt nbsp spacer between a
    /// 341.25 pt grid and a 150 pt block draws 3 pt wide - its nbsp - while the two grids keep every
    /// point they declare). A declared column holding anything else keeps its width (the corpus's
    /// declared label and grid columns hold theirs under our deficits).</summary>
    private static void YieldSpacerColumnsToDeficit(MetricTableState mt)
    {
        if (!mt.stdSerif || !mt.wrapperStacks) return;
        var need = (mt.nCols + 1) * mt.s + mt.nCols * 2 * mt.p;
        for (var c = 0; c < mt.nCols; c++)
            need += mt.colFixed[c] || mt.colW[c] <= 0 ? mt.colW[c] : MetricColumnMinContentPt(mt, c);
        if (need <= MetricFitWidth(mt)) return;
        for (var c = 0; c < mt.nCols; c++)
        {
            // (a width ATTRIBUTE is the calibrated box a sizer ladder builds its grid from: only a STYLE
            // width - the content-box preference - yields)
            if (!mt.colFixed[c] || mt.colW[c] <= 0 || mt.colPx[c] <= 0 || !mt.colPxStyle[c] || mt.colPct[c] > 0 || !MetricColumnIsSpacer(mt, c)) continue;
            var ink = MetricColumnMinContentPt(mt, c);
            if (ink < mt.colW[c]) mt.colW[c] = ink;
        }
    }

    /// <summary>Whether a column holds nothing but white space (a non-breaking space included) in
    /// every cell - no grid, band, image, control or checkbox.</summary>
    private static bool MetricColumnIsSpacer(MetricTableState mt, int c)
    {
        foreach (var r in mt.rows)
        {
            if (c >= r.Count || r[c].ColSpan > 1) continue;
            var mc = r[c];
            if (mc.Text.Trim(' ', '\t', '\r', '\n', '\u00A0', '\u0001').Length > 0 || mc.SubTables is { Count: > 0 }
                || mc.DivSegs is { Count: > 0 } || mc.ImgWPt > 0 || mc.InputBoxes is { Count: > 0 } || mc.LeadCheckboxName is not null)
                return false;
        }
        return true;
    }

    /// <summary>A cell's STYLE width is its content box: its own paddings and side borders stand
    /// outside it (probed on the Words letter's bordered table: `width:82.5pt; padding:5.25pt` cells
    /// with 0.75 pt sides draw 94.5 wide). A width attribute keeps the calibrated box.</summary>
    private static double MetricColumnStyleChromePt(MetricTableState mt, int c)
    {
        if (!mt.colPxStyle[c]) return 0;
        var chrome = 0.0;
        foreach (var r in mt.rows)
            if (c < r.Count && r[c].ColSpan <= 1 && r[c].WidthPxStyle)
                chrome = Math.Max(chrome, Math.Max(0, r[c].PadLeft) + Math.Max(0, r[c].PadRight) + r[c].BorderLeftW + r[c].BorderRightW);
        return chrome;
    }

    /// <summary>The narrowest content a column can hold: its widest word in each cell's face, a
    /// leading checkbox's margin box, a text input's box, a div band's widest word - each with the
    /// cell's own side paddings.</summary>
    /// <summary>The box a broken image holds a column open to: its declared box, bevel, remote
    /// gutters and the cell's pads (0 when no cell of the column holds one).</summary>
    private static double MetricColumnBrokenImageFloorPt(MetricTableState mt, int c)
    {
        var floor = 0.0;
        foreach (var r in mt.rows)
            if (c < r.Count && r[c].ColSpan <= 1 && r[c].ImgPlaceholder && r[c].ImgWPt > 0)
                // (the image box carries its bevel already; a remote one keeps a gutter either side -
                // probed: 72 px box -> 65 = 54 + 2 + 2 x 4.5)
                floor = Math.Max(floor, r[c].ImgWPt
                    + (r[c].ImgRemoteBroken ? 2 * RemoteBrokenImageGutterPt : 0)
                    + CellPadLeftExtra(r[c], mt.p) + CellPadRightExtra(r[c], mt.p));
        return floor;
    }

    private static double MetricColumnMinContentPt(MetricTableState mt, int c)
    {
        var minW = 0.0;
        foreach (var r in mt.rows)
            if (c < r.Count && r[c].ColSpan <= 1)
                minW = Math.Max(minW, MetricCellMinContentPt(mt, r[c]));
        return minW;
    }

    /// <summary>One cell's min-content: its widest word, a leading checkbox's margin box, a control's
    /// advance, a declared nested grid with its fieldset chrome, a div segment's widest word.</summary>
    private static double MetricCellMinContentPt(MetricTableState mt, MetricCell mc)
    {
        var minW = 0.0;
        var mcFs = mc.FontSize ?? mt.mps.fontSize;
        var mcPads = CellPadLeftExtra(mc, mt.p) + CellPadRightExtra(mc, mt.p);
        // a class `min-width` floors the content box
        if (mc.MinWidthPt > 0) minW = Math.Max(minW, mc.MinWidthPt + mcPads);
        // a leading checkbox's margin box is the cell's floor before any word
        if (mc.LeadCheckboxName is not null)
            minW = Math.Max(minW, MetricCheckboxMarginBoxPt(mc) + mcPads);
        if (mc.InputBoxes is { Count: > 0 } minInputs)
            minW = Math.Max(minW, MetricInputsMaxContentPt(mt, mc, minInputs));
        // a UA form cell's nested grid declared in absolute units is that wide at least,
        // its fieldset's chrome around it (measured on the test request: the 506 px comment
        // table holds its 400 pt container open around the 506 px it declares itself)
        // (…and every UA cell's, form or not: a table declaring its width takes it - measured on the
        // enterprise summary: three 200 px wrappers in an 800 px table split it 200 pt apart, each
        // column its 150 pt declaration plus an equal third of the surplus)
        if (mt.stdSerif && mc.SubTables is { Count: > 0 } minSubs)
            foreach (var sub in minSubs)
                if (NestedGridDeclaredWidthPt(mt, sub) is { } subW && subW > 0)
                    minW = Math.Max(minW, subW + mcPads
                        + (mc.HasFieldset ? 2 * MetricFieldsetSideInsetPt(mt.mps, mcFs) : 0));
        if (mc.Runs is not null)
            minW = Math.Max(minW, StyledMinContentPt(mt.face, mt.boldFace, mc, mcFs,
                mt.mps.ptFormCells ? GlueColonPieces(mc.Text) : mc.Text, mc.Runs) + mcPads);
        else
        foreach (var word in MinContentPieces(mt, mc.Text))
            minW = Math.Max(minW, MeasureFaceText(
                CellFaceName(mt.face, mt.boldFace, mc), word, mcFs)
                + mcPads);
        // a UA cell's nested grid holds the column open at the grid's own min-content (measured
        // on the lab report: the result column beside a two-line date column solves 213 of 385,
        // the deficit shared against the nested grid's 150 floor - at a zero floor the date
        // column kept its whole max-content)
        if (mt.stdSerif && !mt.mps.uaFormCells && mc.SubTables is { Count: > 0 } gridSubs)
            foreach (var sub in gridSubs)
                minW = Math.Max(minW, MetricGridMinContentPt(mt, sub, mc) + mcPads);
        // a broken image's box, bevel and (remote) gutters hold the column open
        if (mc.ImgPlaceholder && mc.ImgWPt > 0)
            minW = Math.Max(minW, mc.ImgWPt + 2 * BrokenImageBevelPt
                + (mc.ImgRemoteBroken ? 2 * RemoteBrokenImageGutterPt : 0) + mcPads);
        if (mc.DivSegs is { Count: > 0 } mSegs)
            foreach (var mSeg in mSegs)
            {
                // (a div stating its own box is that wide at least)
                if (mSeg.BoxOpen && mSeg.BoxWidthPt > 0) minW = Math.Max(minW, mSeg.BoxWidthPt + mcPads);
                if (mSeg.Runs is not null)
                {
                    minW = Math.Max(minW, StyledMinContentPt(mt.face, mt.boldFace,
                        new MetricCell { Face = mSeg.Face, Bold = mSeg.Bold || mc.Bold, FontSize = mSeg.FontSize, Italic = mSeg.Italic },
                        mSeg.FontSize ?? mcFs, mSeg.Text, mSeg.Runs) + mSeg.PadLeft + mSeg.PadRight);
                    continue;
                }
                foreach (var word in mSeg.Text.Split(' ',
                    StringSplitOptions.RemoveEmptyEntries))
                    minW = Math.Max(minW, MeasureFaceText(
                        mSeg.Bold || mc.Bold ? mt.boldFace : mSeg.Face ?? mt.face,
                        word, mSeg.FontSize ?? mcFs));
            }
        return minW;
    }

    /// <summary>The unbreakable pieces of a cell's text: a UA cell breaks at spaces, after a hyphen and
    /// at a zero-width space (the probed UA min-content); the calibrated grids at spaces.</summary>
    private static IEnumerable<string> MinContentPieces(MetricTableState mt, string text)
        => mt.stdSerif
            ? DashSegments(mt.mps.ptFormCells ? GlueColonPieces(text.Replace('\u0001', ' ')) : text.Replace('\u0001', ' '))
            : text.Split(new[] { ' ', '\u0001' }, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>The pt form's label text with no break before a colon: `Last name :` breaks into
    /// `Last` and `name :` (probed: the min-content of the label column is `Description :` whole
    /// and `name :` of the two-word labels, never a lone colon).</summary>
    private static string GlueColonPieces(string text) => text.Replace(" :", "\u00A0:");

    /// <summary>Wrap the cell's text to its content box and add up the height its lines, image and nested grids need.</summary>
    private static void WrapMetricCellContent(MetricTableState mt, MetricCell mc, double effW, double cellFs)
    {
        // (a cell whose only text is the whitespace between its nested grids holds no line
        // of its own in any dialect: the grids start at the cell's top; an nbsp is content
        // and keeps its line, except in the paragraph-cell dialect, which glues it too)
        if (mc.SubTables is { Count: > 0 } && mc.Text.Length > 0)
        {
            var glueWs = true;
            foreach (var ch in mc.Text)
                if (ch is not (' ' or '\u0001') && !((mt.paragraphCells || mc.TextTrailsGrids) && ch == '\u00A0')) { glueWs = false; break; }
            if (glueWs) { mc.Text = ""; mc.Lines = []; }
        }
        // A leading checkbox spends its margin box on the FIRST line only; when the cell's
        // first word cannot follow it there, the box keeps that line to itself and the text
        // wraps into the whole content width from the next one (measured on the reference:
        // `Anonymous` needs 84.34 beside the box in an 83.40 box and drops below it).
        var leadW = 0.0;
        if (mc.LeadCheckboxName is not null)
        {
            leadW = MetricCheckboxMarginBoxPt(mc);
            var leadWords = mc.Text.Replace('\u0001', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
            mc.LeadCheckboxOwnLine = leadWords.Length > 0
                && MeasureFaceText(CellFaceName(mt.face, mt.boldFace, mc), leadWords[0], cellFs)
                   > effW - leadW + MetricWrapFitSlackPt;
            if (mc.LeadCheckboxOwnLine) leadW = 0;
        }
        if (mc.Text.Length > 0)
            mc.Lines = mt.mps.bordered && mt.mps.layoutFixed
                ? new[] { mc.Text.Replace('\u0001', ' ') }
                // (nowrap keeps each source line whole; an explicit <br> still breaks it)
                : mc.NoWrap ? Array.ConvertAll(mc.Text.Split('\u0001'), l => l.TrimStart())
                // +0.05: a column sized to its own max-content must not
                // wrap on the equality boundary
                // (a cell whose ink is in several styles wraps on each run's own measure)
                : mc.Runs is not null ? MeasuredWordWrapStyled(mc.Text, effW - leadW + MetricWrapFitSlackPt, mt.face, mt.boldFace, mc, cellFs, mc.Runs)
                : MeasuredWordWrap(mc.Text, effW - leadW + MetricWrapFitSlackPt, CellFaceName(mt.face, mt.boldFace, mc), cellFs,
                    dashBreaks: mt.stdSerif);
        // A <p> inside a UA-grid cell keeps its UA block margins: 1.12 em above the first
        // line and below the last (measured: the surgery-lights rows band 43.0 = 13.44 +
        // 13.5 + 13.44 + the cell chrome; the calibrated blank-line gap stays between p's).
        var paraMargin = mt.stdSerif && mc.ParaBlocks > 0 ? UaParagraphMarginPt : 0;
        if (paraMargin > 0) mc.PadTopPt = Math.Max(mc.PadTopPt, paraMargin);
        mc.ContentH = mc.Lines.Length * CellLineOf(mt.mps, mt.stdSerif, mt.wrapperStacks, mt.hheaSum, mt.face, mt.fm, mc, cellFs)
                      // the checkbox's own line box is the em plus its strut descent, and it
                      // REPLACES the text line it shares (or stands above the text it displaced)
                      + (mc.LeadCheckboxName is not null
                          ? MetricCheckboxLineHeight(mt.mps, mt.stdSerif, mt.wrapperStacks, mt.hheaSum, mt.face, mt.fm, mc, cellFs)
                            - (mc.LeadCheckboxOwnLine ? 0 : CellLineOf(mt.mps, mt.stdSerif, mt.wrapperStacks, mt.hheaSum, mt.face, mt.fm, mc, cellFs))
                          : 0)
                      + (mc.HasSpan ? 3.0 : 0) + mc.PadTopPt + mc.PadBottomPt + paraMargin
                      // Excel-fragment grid: the in-cell <p>'s margin-bottom
                      // is content height (probed: every row carries it).
                      // The margin-free email grid's rows are sized by
                      // their DECLARED tr heights instead (the bordered
                      // draw applies them) — no margin in the content.
                      + (mt.mps.wtInlineGrid && !mt.mps.wtPMarginDefaulted ? mt.mps.wtPMarginB : 0);
        // a text input's box stands in the cell's content band (its line, when text shares it)
        if (mc.InputBoxes is { Count: > 0 })
            mc.ContentH = Math.Max(mc.ContentH, MetricInputsLineBoxPt(mc) + mc.PadTopPt + mc.PadBottomPt);
        // a remote image's alt text stands in its own 1 px-bordered inline box (UA form cells)
        if (mc.AltBoxed) mc.ContentH += 2 * UaAltBoxBorderPt;
        // a sized broken image after the last line's text stands on its baseline and lifts that line
        // by what it rises above the ascent (measured: two 16/17 px icons make a 6.75 pt line 16.16)
        if (mc.ImgAfterText && mc.Lines.Length > 0)
        {
            mc.ContentH += Math.Max(0, mc.ImgHPt - CellFm(mt.fm, mc).asc * cellFs);
        }
        if (mc.ImgHPt > 0)
            mc.ContentH = mc.ImgBytes is not null
                ? mc.ContentH + mc.ImgHPt
                : Math.Max(mc.ContentH, mc.ImgHPt);
        if (mc.SubTables is { Count: > 0 })
            foreach (var sub in mc.SubTables)
                mc.ContentH += mt.mps.bordered
                    // the bordered draw strokes the row box up front — it
                    // needs the sub-grid's REAL wrapped extent
                    ? NestedTableWrappedHeight(sub, mt.lineH, mt.face, mt.mps.fontSize, effW)
                    // a UA cell's nested grid is as tall as its render SOLVES it (measured on the
                    // test form: a cellspacing=5 grid of two-line cells bands its row 34.5 and
                    // centres the sibling cell 3.75 down; the row-count estimate gave 21)
                    // (a grid inside a fieldset solves in the frame's content box)
                    : mt.stdSerif ? MetricGridHeightPt(mt, sub, mc, effW - 2 * (mc.HasFieldset ? MetricFieldsetSideInsetPt(mt.mps, cellFs) : 0))
                    : EstimateNestedTableHeight(sub,
                        CellLineOf(mt.mps, mt.stdSerif, mt.wrapperStacks, mt.hheaSum, mt.face, mt.fm, mc, cellFs) + 2 * mt.p,
                        2 * mt.p);
        if (mc.TrailingBreakLines > 0)
            mc.ContentH += mc.TrailingBreakLines * CellLineOf(mt.mps, mt.stdSerif, mt.wrapperStacks, mt.hheaSum, mt.face, mt.fm, mc, cellFs);
        // …and the fieldset box the grid stands in closes above and below it.
        if (mc.HasFieldset)
            mc.ContentH += MetricFieldsetTopInsetPt(mt.mps, mc, cellFs,
                    CellLineOf(mt.mps, mt.stdSerif, mt.wrapperStacks, mt.hheaSum, mt.face, mt.fm, mc, cellFs))
                + MetricFieldsetBottomInsetPt(cellFs);
    }

    /// <summary>A spanning cell's content sizes the columns it crosses: when the span's solved
    /// widths hold less than the cell's longest unbreakable run, the shortfall spreads equally
    /// over the span's free columns (CSS automatic table layout).</summary>
    private static void SpreadSpanningContentWidths(MetricTableState mt)
    {
        if (mt.mps.bordered) return;
        foreach (var r in mt.rows)
            for (var c = 0; c < r.Count; c++)
            {
                var mc = r[c];
                if (mc.ColSpan <= 1 || mc.Text.Length == 0) continue;
                var span = Math.Min(mc.ColSpan, mt.nCols - c);
                if (span <= 1) continue;
                double need = 0;
                foreach (var seg in mc.Text.Split('\u0001'))
                    // a cell that keeps its source line whole needs the WHOLE line, not its
                    // widest word: the pre-formatted address in the complaint report's
                    // fieldset grid sizes the two columns it spans by its full extent
                    if (mc.NoWrap)
                        need = Math.Max(need, MeasureFaceText(CellFaceName(mt.face, mt.boldFace, mc),
                            seg, mc.FontSize ?? mt.mps.fontSize));
                    else
                        foreach (var word in seg.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                            need = Math.Max(need, MeasureFaceText(CellFaceName(mt.face, mt.boldFace, mc), word,
                                mc.FontSize ?? mt.mps.fontSize));
                double have = (span - 1) * (2 * mt.p + mt.s);
                var free = 0;
                for (var k = c; k < c + span; k++)
                {
                    have += mt.colW[k];
                    if (!mt.colFixed[k]) free++;
                }
                if (need <= have) continue;
                // An UNBREAKABLE line cannot be refused: its shortfall spreads EQUALLY over
                // every column it crosses, a declared one included (measured on the reference:
                // the fieldset grid's 7 em label column and its auto neighbour each take half
                // of the pre-formatted address's overflow). A cell that can still WRAP asks
                // only the columns that have not been declared.
                if (mc.NoWrap)
                {
                    var wholeShare = (need - have) / span;
                    for (var k = c; k < c + span; k++) mt.colW[k] += wholeShare;
                    continue;
                }
                if (free == 0) continue;
                var share = (need - have) / free;
                for (var k = c; k < c + span; k++)
                    if (!mt.colFixed[k]) mt.colW[k] += share;
            }
        SpreadSpanningMaxContent(mt);
    }

    /// <summary>A spanning cell's max-content S over columns that are all DECLARED: the declared
    /// boxes are the columns' max-contents and their text their min-contents (a declared width is
    /// no floor), S scales every max in proportion (M = max · S / Σmax), the span's row takes
    /// R = min(S, the box), and each column stands at min + (R − Σmin)·(M − min)/Σ(M − min) - the
    /// percent-grid span law on declared columns (probed
    /// on the Words letter's bordered 82.5 / 262.5 pt table: 125.0 / 340.75 under a ~38-word
    /// paragraph, 119.9 under 19 words, 105.3 / 305.3 under 16 = the paragraph's own 410.6; a
    /// 100 pt first column 137.9, a 60 pt one 106.3, a 200 pt second 145.0 - all within 2 pt).</summary>
    /// <summary>Every column a spanning cell crosses is sized by a width ATTRIBUTE (none by style).</summary>
    private static bool SpannedColumnsAllAttribute(MetricTableState mt, int c, int span)
    {
        for (var k = c; k < c + span && k < mt.nCols; k++)
            if (mt.colPx[k] <= 0 || mt.colPxStyle[k]) return false;
        return true;
    }

    private static void PushSpannedDeclaredColumns(MetricTableState mt, int c, int span, double spanMax, double spanBox)
    {
        var chrome = (span - 1) * (2 * mt.p + mt.s) + 2 * mt.p;
        var r = spanBox - chrome;
        double sumMax = 0, sumMin = 0;
        var mins = new double[span];
        for (var k = 0; k < span; k++)
        {
            mins[k] = Math.Min(MetricColumnMinContentPt(mt, c + k), mt.colW[c + k]);
            sumMax += mt.colW[c + k];
            sumMin += mins[k];
        }
        if (sumMax <= 0 || r <= sumMin) return;
        var scale = Math.Max(1.0, (spanMax - chrome) / sumMax);
        double sumSlack = 0;
        for (var k = 0; k < span; k++) sumSlack += mt.colW[c + k] * scale - mins[k];
        if (sumSlack <= 0) return;
        for (var k = 0; k < span; k++)
            mt.colW[c + k] = mins[k] + (r - sumMin) * (mt.colW[c + k] * scale - mins[k]) / sumSlack;
    }

    /// <summary>The UA grid's automatic layout goes further: a spanning cell's whole MAX-content
    /// (its longest unwrapped line, a nested grid's widest row) grows the free columns it
    /// crosses, equally, as far as the table's box has room - the grid shrink-wraps to the widest
    /// of its rows before anything wraps (measured: `Named Insured: MATERIAL HANDLING EQUIPMENT
    /// ERECTORS INC.` spanning two columns of 76.7 and 0 pt draws on one line, the grid 324.8 wide).</summary>
    private static void SpreadSpanningMaxContent(MetricTableState mt)
    {
        if (!mt.stdSerif || mt.uaPctGrid) return;
        foreach (var r in mt.rows)
            for (var c = 0; c < r.Count; c++)
            {
                var mc = r[c];
                if (mc.ColSpan <= 1 || mc.Phantom) continue;
                var span = Math.Min(mc.ColSpan, mt.nCols - c);
                if (span <= 1) continue;
                var need = MetricCellMaxContentPt(mt, mc);
                double have = (span - 1) * (2 * mt.p + mt.s);
                var free = 0;
                for (var k = c; k < c + span; k++)
                {
                    have += mt.colW[k];
                    if (!mt.colFixed[k]) free++;
                }
                if (need <= have) continue;
                var total = (mt.nCols + 1) * mt.s;
                foreach (var w in mt.colW) total += w + 2 * mt.p;
                var room = mt.usableW + (mt.nCols + 1) * mt.s - total;
                if (room <= 0) continue;
                if (free == 0)
                {
                    // (a quirks grid's width-ATTRIBUTE columns are not pushed by what a cell spanning them
                    //  holds - MEASURED, the evaluation form: the 24 / 72 / 624 px remarks grid keeps its
                    //  first column at 14.5 under a 696 px spanning paragraph, where a push to the
                    //  paragraph's max-content would leave it at its 2.75 pt image)
                    if (!(_quirksRowStrut && SpannedColumnsAllAttribute(mt, c, span)))
                        PushSpannedDeclaredColumns(mt, c, span, need, Math.Min(need, have + room));
                    continue;
                }
                var share = Math.Min(need - have, room) / free;
                for (var k = c; k < c + span; k++)
                    if (!mt.colFixed[k]) mt.colW[k] += share;
            }
    }
}
