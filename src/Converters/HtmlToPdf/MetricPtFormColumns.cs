using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// The pt-sized FORM grid (MEASURED, the helpdesk request form: `BODY { font-family: Tahoma;
// font-size: 8pt }`, cell class rules in the same face, text inputs and selects in the cells,
// six 98 % tables): its columns solve on the probed auto-table rule below, and a grid whose
// MIN-content is wider than the body lays out at that min-content and grows the sheet to one
// page margin past it. Probed on the reference converter (~90 synthetic fixtures, every fixture
// column within 0.03 pt).
internal static partial class HtmlToPdfConverter
{
    /// <summary>The pt form grid's columns. Every live column holds its min-content box (the widest
    /// unbreakable unit, a control's advance, with the cell's paddings) and its max-content box - a
    /// declared cell width is the column's MAX (the largest declaration over the rows, with that
    /// cell's paddings) and makes the column FIXED, else the widest cell's max-content. A spanning
    /// cell whose min-content the columns it crosses cannot hold raises their mins (narrower spans
    /// first): up to the maxes in proportion to the slack, beyond them every column at its max plus
    /// the rest in proportion to the maxes. Then, in the cells region A (the table box less its frame,
    /// padding and border-spacings): a grid whose mins exceed A takes its min-content whole (the box
    /// grows - see <see cref="MetricTableState.ptFormMinContentPt"/>); one whose maxes fit gives the
    /// fixed columns their max and the autos the rest in proportion to their max; otherwise the
    /// autos give first (down to their mins, in proportion to their slack), then the fixed columns
    /// share what is left over their mins by ONE factor of their slack.</summary>
    private static void SolvePtFormGridColumns(MetricTableState mt)
    {
        var n = mt.nCols;
        var pad = 2 * mt.p;
        mt.colVoid = new bool[n];
        foreach (var r in mt.rows)
            for (var c = 0; c < Math.Min(r.Count, n); c++)
                if (!r[c].Phantom) mt.colVoid[c] = true;
        var live = 0;
        for (var c = 0; c < n; c++) { mt.colVoid[c] = !mt.colVoid[c]; if (!mt.colVoid[c]) live++; }
        var minBox = new double[n];
        var maxBox = new double[n];
        var isFixed = new bool[n];
        for (var c = 0; c < n; c++)
        {
            if (mt.colVoid[c]) continue;
            minBox[c] = mt.minCol[c] + pad;
            var declared = 0.0;
            foreach (var r in mt.rows)
                if (c < r.Count && r[c].ColSpan <= 1 && !r[c].Phantom && r[c].WidthPx > 0)
                    declared = Math.Max(declared, r[c].WidthPx + pad + CellPadLeftExtra(r[c], mt.p) + CellPadRightExtra(r[c], mt.p));
            isFixed[c] = declared > 0;
            maxBox[c] = declared > 0 ? declared : Math.Max(MetricColumnMaxContentPt(mt, c), mt.minCol[c]) + pad;
            maxBox[c] = Math.Max(maxBox[c], minBox[c]);
        }
        PtFormRaiseSpannedMins(mt, minBox, maxBox);

        var chrome = 2 * mt.frameW + PtFormSideFramesWidthPt(mt) + 2 * (mt.mps.tablePadLeftPt + mt.s);
        var sumMin = 0.0;
        for (var c = 0; c < n; c++) if (!mt.colVoid[c]) sumMin += minBox[c];
        mt.ptFormMinContentPt = sumMin + (live - 1) * mt.s + chrome;

        // (a percent table shares the BODY: the sheet less the page margins and the body margin each
        //  side - probed: 98 % of 544.609 on the 736.609 sheet, centred by the difference)
        var body = mt.pageWidth - 2 * (mt.mps.pageMarginLeft + mt.symInsetPt);
        var box = mt.tableWpt > 0 ? mt.tableWpt : mt.tablePct > 0 ? body * mt.tablePct / 100.0 : body;
        var region = box - chrome - (live - 1) * mt.s;
        var w = PtFormDistribute(mt, minBox, maxBox, isFixed, region);
        if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_MCOL") == "1")
            Console.Error.WriteLine($"[ptform] live={live} box={box:0.###} region={region:0.###} min=[{string.Join(",", Array.ConvertAll(minBox, v => v.ToString("0.###")))}] max=[{string.Join(",", Array.ConvertAll(maxBox, v => v.ToString("0.###")))}] fixed=[{string.Join(",", isFixed)}] w=[{string.Join(",", Array.ConvertAll(w, v => v.ToString("0.###")))}] minContent={mt.ptFormMinContentPt:0.###}");
        for (var c = 0; c < n; c++)
        {
            // (a void column's box and spacing are nothing: its neighbours stand together)
            mt.colW[c] = mt.colVoid[c] ? -pad - mt.s : w[c] - pad;
            mt.colFixed[c] = true;
        }
    }

    /// <summary>The column boxes for a cells region: the mins whole when they do not fit, else the
    /// fixed columns at their max and the autos sharing by max, else the autos giving first.</summary>
    private static double[] PtFormDistribute(MetricTableState mt, double[] minBox, double[] maxBox, bool[] isFixed, double region)
    {
        var n = minBox.Length;
        var w = (double[])minBox.Clone();
        double sumMin = 0, sumMax = 0, sumMaxFixed = 0, sumMinAuto = 0, sumMinFixed = 0, sumMaxAuto = 0, slackAuto = 0, slackFixed = 0;
        for (var c = 0; c < n; c++)
        {
            if (mt.colVoid![c]) continue;
            sumMin += minBox[c];
            sumMax += maxBox[c];
            if (isFixed[c]) { sumMaxFixed += maxBox[c]; sumMinFixed += minBox[c]; slackFixed += maxBox[c] - minBox[c]; }
            else { sumMinAuto += minBox[c]; sumMaxAuto += maxBox[c]; slackAuto += maxBox[c] - minBox[c]; }
        }
        if (sumMin >= region - PtFormFitEpsilonPt) return w;
        if (sumMax <= region)
        {
            var rest = region - sumMaxFixed;
            for (var c = 0; c < n; c++)
            {
                if (mt.colVoid![c]) continue;
                w[c] = isFixed[c] ? maxBox[c] : sumMaxAuto > 0 ? rest * maxBox[c] / sumMaxAuto : maxBox[c];
            }
            return w;
        }
        if (region >= sumMaxFixed + sumMinAuto)
        {
            var give = region - sumMaxFixed - sumMinAuto;
            for (var c = 0; c < n; c++)
            {
                if (mt.colVoid![c]) continue;
                w[c] = isFixed[c] ? maxBox[c] : minBox[c] + (slackAuto > 0 ? give * (maxBox[c] - minBox[c]) / slackAuto : 0);
            }
            return w;
        }
        var k = slackFixed > 0 ? (region - sumMinAuto - sumMinFixed) / slackFixed : 0;
        for (var c = 0; c < n; c++)
        {
            if (mt.colVoid![c]) continue;
            w[c] = isFixed[c] ? minBox[c] + (maxBox[c] - minBox[c]) * k : minBox[c];
        }
        return w;
    }

    /// <summary>The left and right widths of the grid's class side frames (its `.Form-table` border), 0 without them.</summary>
    private static double PtFormSideFramesWidthPt(MetricTableState mt)
        => mt.mps.sideFrames is { } sf ? sf[3].W + sf[1].W : 0;

    /// <summary>A grid whose mins reach the cells region within this much lays out at min-content.</summary>
    private const double PtFormFitEpsilonPt = 0.001;

    /// <summary>The spanned-min raise of the pt form grid: R = the spanning cell's min-content box less
    /// the spacings inside the span; R over the spanned mins is paid to the slack (min to max, in
    /// proportion); R over the spanned maxes puts every spanned column at its max and shares the
    /// rest in proportion to the maxes, the raised value becoming min AND max.</summary>
    private static void PtFormRaiseSpannedMins(MetricTableState mt, double[] minBox, double[] maxBox)
    {
        var pad = 2 * mt.p;
        var spans = new List<(int c, int span, MetricCell mc)>();
        foreach (var r in mt.rows)
            for (var c = 0; c < r.Count; c++)
                if (r[c].ColSpan > 1 && !r[c].Phantom && Math.Min(r[c].ColSpan, mt.nCols - c) > 1)
                    spans.Add((c, Math.Min(r[c].ColSpan, mt.nCols - c), r[c]));
        spans.Sort((a, b) => a.span.CompareTo(b.span));
        foreach (var (c, span, mc) in spans)
        {
            var cols = new List<int>();
            for (var k = c; k < c + span; k++) if (!mt.colVoid![k]) cols.Add(k);
            if (cols.Count == 0) continue;
            var need = MetricCellMinContentPt(mt, mc) + pad - (cols.Count - 1) * mt.s;
            double haveMin = 0, haveMax = 0;
            foreach (var k in cols) { haveMin += minBox[k]; haveMax += maxBox[k]; }
            if (need <= haveMin) continue;
            if (need <= haveMax)
            {
                var slack = haveMax - haveMin;
                foreach (var k in cols) minBox[k] += (need - haveMin) * (maxBox[k] - minBox[k]) / slack;
                continue;
            }
            var over = need - haveMax;
            foreach (var k in cols)
            {
                var raised = maxBox[k] + (haveMax > 0 ? over * maxBox[k] / haveMax : over / cols.Count);
                minBox[k] = raised;
                maxBox[k] = raised;
            }
        }
    }

    /// <summary>The min-content box of a pt form grid parsed from its markup - what the sheet grows to
    /// when it is wider than the body (MEASURED: 96 + 550.609 + 90 for the request grid whose colspan-5
    /// file input raises four columns). 0 when nothing can be measured.</summary>
    private static double PtFormTableMinContentPt(ConvertState cv, HtmlLoadOptions? options, string tableHtml, double boxW)
    {
        var profile = cv.profile;
        var face = profile.metricFace;
        if (WinMetricsFor(face) is not { } fm) return 0;
        try
        {
            var mt = ParseMetricTable(cv.doc ?? Document.Create(), tableHtml, cv.css, 0, boxW, cv.pageWidth, cv.pageHeight,
                cv.marginTop, cv.marginBottom, face, fm, new Core.PdfDictionary(), stdSerif: true,
                baseFontSize: profile.formBodyFontPt > 0 ? profile.formBodyFontPt : UaDefaultFontPt,
                wrapperStacks: true, symInsetPt: UaBodyMarginPt, rtl: profile.rtlDoc,
                paragraphCells: profile.uaBlockCells, serifReportCells: profile.uaBlockCells,
                loadOptions: options, siblingCellRules: profile.docSiblingCellRules,
                uaBlockCells: profile.uaBlockCells, uaFormCells: true, ptFormCells: true);
            mt.cursor = new FlowPosition { y = cv.pageHeight };
            CloseRow(mt.mps, mt.rows, mt.text, mt.reportCells, mt.stdSerif);
            if (mt.rows.Count == 0) return 0;
            SolveMetricColumns(mt);
            if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_PAGEW") == "1")
                Console.Error.WriteLine($"[ptformmin] cols={mt.nCols} min={mt.ptFormMinContentPt:0.###} box={boxW:0.###}");
            return mt.ptFormMinContentPt;
        }
        catch { return 0; }
    }
}
