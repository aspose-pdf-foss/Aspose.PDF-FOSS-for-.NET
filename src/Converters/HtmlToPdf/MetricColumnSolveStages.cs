using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>A UA percentage grid solves its columns from the declared shares alone.</summary>
    /// <remarks>The share resolves against the column span itself — the available width less
    /// one border-spacing per boundary — and NOT against a body-margin inset of it. Probed on
    /// 50/50 grids under four bodies (17.59 cm, 500 px, none, and margin-zeroed): each time
    /// the drawn boundary sits at exactly half of that span, and the zero-margin body moves
    /// the table's ORIGIN rather than narrowing its share.</remarks>
    private static void SolveMetricPercentGrid(MetricTableState mt)
    {
        if (mt.uaPctGrid)
        {
            // (the shares resolve against the symmetric body box: the flow's right margin keeps
            // no body inset, the table's box does - measured: a 72 % pair on the default sheet
            // spans 290.16 = 0.72 x 403)
            mt.usableSym = mt.availW - mt.symInsetPt - (mt.nCols + 1) * mt.s;
            // (a grid that declares its own absolute box shares THAT box, whatever it stands in - MEASURED,
            //  the evaluation form's `width:7in` grids: the 100% column takes the 504 pt box less its
            //  empty neighbour's two pads, on the A4 sheet and the grown one alike)
            if (mt.stdSerif && mt.tableWpt > 0 && mt.tablePct <= 0)
                mt.usableSym = mt.tableWpt - 2 * mt.frameW - (mt.nCols + 1) * mt.s;
            mt.minCol = new double[mt.nCols];
            for (var c = 0; c < mt.nCols; c++)
            {
                foreach (var r in mt.rows)
                {
                    if (c < r.Count && r[c].SubTables is { Count: > 0 } pctSubs)
                        foreach (var sub in pctSubs)
                            foreach (var seg in DashSegments(CollapseWs(DecodeEntities(
                                Regex.Replace(sub, "<[^>]+>", " ")))))
                                mt.minCol[c] = Math.Max(mt.minCol[c], MeasureFaceText(
                                    CellFaceName(mt.face, mt.boldFace, r[c]), seg, r[c].FontSize ?? mt.mps.fontSize) + 2 * mt.p);
                    var sidePads = c < r.Count ? CellPadLeftExtra(r[c], mt.p) + CellPadRightExtra(r[c], mt.p) : 0;
                    // (the pt form column's min is its cells' min-content whole: the glued label,
                    //  the control's advance, an inline-block span's box, with the cell's pads)
                    if (mt.mps.ptFormCells && c < r.Count && r[c].ColSpan <= 1 && !r[c].Phantom)
                    {
                        mt.minCol[c] = Math.Max(mt.minCol[c], MetricCellMinContentPt(mt, r[c]));
                        continue;
                    }
                    if (c < r.Count && r[c].LeadCheckboxName is not null && r[c].ColSpan <= 1)
                        mt.minCol[c] = Math.Max(mt.minCol[c], MetricCheckboxMarginBoxPt(r[c]) + sidePads);
                    // a text input never wraps: its box is the column's floor
                    if (c < r.Count && r[c].InputBoxes is { Count: > 0 } minInputs && r[c].ColSpan <= 1)
                        mt.minCol[c] = Math.Max(mt.minCol[c], MetricInputsMaxContentPt(mt, r[c], minInputs));
                    if (c < r.Count && r[c].Text.Length > 0)
                    {
                        // a NOWRAP cell's min-content is its WHOLE text
                        if (r[c].NoWrap)
                        {
                            mt.minCol[c] = Math.Max(mt.minCol[c], MeasureFaceText(
                                CellFaceName(mt.face, mt.boldFace, r[c]), r[c].Text.Replace('\u0001', ' '),
                                r[c].FontSize ?? mt.mps.fontSize) + sidePads);
                            continue;
                        }
                        foreach (var seg in DashSegments(r[c].Text.Replace('\u0001', ' ')))
                            mt.minCol[c] = Math.Max(mt.minCol[c], MeasureFaceText(
                                CellFaceName(mt.face, mt.boldFace, r[c]), seg, r[c].FontSize ?? mt.mps.fontSize)
                                + sidePads);
                    }
                    // Div-stacked content is min-sized by its widest word too, in the
                    // segment's own face - without this the max-content set above is
                    // overwritten by a zero min and the column collapses, breaking the
                    // div text one character per line.
                    if (c < r.Count && r[c].DivSegs is { Count: > 0 } mnSegs)
                        foreach (var sg in mnSegs)
                            foreach (var seg in DashSegments(sg.Text))
                                mt.minCol[c] = Math.Max(mt.minCol[c], MeasureFaceText(
                                    sg.Bold || r[c].Bold ? mt.boldFace : sg.Face ?? mt.face, seg,
                                    sg.FontSize ?? r[c].FontSize ?? mt.mps.fontSize) + sg.PadLeft);
                }
                // A colgroup `min-width` is a floor the share cannot go under, and the
                // column keeps NO slack at it: the shortfall goes to the columns that have
                // some (measured: a 6 em floor over a 12.5 % share draws 72 pt exactly).
                if (mt.colMinWidthPt[c] > mt.minCol[c]) mt.minCol[c] = mt.colMinWidthPt[c];
                // (a pt form column's declared width is its MAX, never its min)
                if (mt.colFixed[c] && !mt.mps.ptFormCells) mt.minCol[c] = mt.colW[c];
            }
            if (mt.mps.ptFormCells) SolvePtFormGridColumns(mt);
            else if (mt.mps.uaFormCells) SolveUaFormGridColumns(mt);
            else SolveUaPercentGridColumns(mt);
        }
    }

    /// <summary>The UA form grid's columns (probed on the reference converter, twenty sweeps within
    /// 0.05 pt): a column no real cell reaches takes no box and no spacing; every other column holds
    /// its min-content (a control's advance, the widest word) and its max-content (a declared px
    /// width is a max, not a pin; a share is a target of the span); a spanning cell whose min-content
    /// the columns it crosses cannot hold raises their mins - the columns with slack under their max
    /// first, in proportion to the slack, the rest in proportion to the mins; the span then pays out
    /// above the mins in priority: the shares up to their targets (in proportion to the shortfalls),
    /// the declared px columns up to their declarations, the autos in proportion to their slack and
    /// beyond their maxes in proportion to the maxes (measured on the test request's dates grid:
    /// 39.99 / 101.47 / 90.0 / 140.54 / 0 in a 372 span, its 124 px column lifted from its 39.99
    /// word by the 217.5 input spanning it, the 48 % pair short of their 178.56 targets alike).</summary>
    private static void SolveUaFormGridColumns(MetricTableState mt)
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
        var pxBox = new double[n];
        for (var c = 0; c < n; c++)
        {
            if (mt.colVoid[c]) continue;
            minBox[c] = mt.minCol[c] + pad;
            maxBox[c] = Math.Max(MetricColumnMaxContentPt(mt, c), mt.minCol[c]) + pad;
            if (mt.colPx[c] > 0) { pxBox[c] = mt.colPx[c] + pad; maxBox[c] = Math.Max(maxBox[c], pxBox[c]); }
        }
        PushSpannedMaxContent(mt, maxBox, pad);
        PushSpannedMinContent(mt, minBox, maxBox, pad);
        var span = mt.availW - mt.symInsetPt - (live + 1) * mt.s;
        double box;
        if (mt.tableWpt > 0) box = mt.tableWpt - 2 * mt.frameW - (live + 1) * mt.s;
        else if (mt.tablePct > 0) box = span * mt.tablePct / 100.0;
        else
        {
            double want = 0, autoSum = 0, scaleUp = 0, pctShare = 0;
            for (var c = 0; c < n; c++)
            {
                if (mt.colVoid[c]) continue;
                want += maxBox[c];
                if (mt.colPct[c] > 0) { scaleUp = Math.Max(scaleUp, maxBox[c] / (mt.colPct[c] / 100.0)); pctShare += mt.colPct[c] / 100.0; }
                else autoSum += maxBox[c];
            }
            if (pctShare > 0 && pctShare < 1 - PercentShareEpsilon) scaleUp = Math.Max(scaleUp, autoSum / (1 - pctShare));
            box = Math.Min(span, Math.Max(want, scaleUp));
        }
        var w = (double[])minBox.Clone();
        var left = box;
        for (var c = 0; c < n; c++) if (!mt.colVoid[c]) left -= minBox[c];
        if (left > 0)
        {
            var need = new double[n];
            // (a) the shares, up to their targets
            for (var c = 0; c < n; c++) need[c] = !mt.colVoid[c] && mt.colPct[c] > 0 ? Math.Max(0, mt.colPct[c] / 100.0 * box - w[c]) : 0;
            left = PayOutColumnNeed(w, need, left);
            // (b) the declared px columns, up to their declarations
            for (var c = 0; c < n; c++) need[c] = !mt.colVoid[c] && mt.colPct[c] <= 0 && pxBox[c] > 0 ? Math.Max(0, pxBox[c] - w[c]) : 0;
            left = PayOutColumnNeed(w, need, left);
            // (c) the autos, up to their max-content, then beyond it in proportion to the max
            for (var c = 0; c < n; c++) need[c] = !mt.colVoid[c] && mt.colPct[c] <= 0 && pxBox[c] <= 0 ? Math.Max(0, maxBox[c] - w[c]) : 0;
            left = PayOutColumnNeed(w, need, left);
            for (var c = 0; c < n; c++) need[c] = !mt.colVoid[c] && mt.colPct[c] <= 0 && pxBox[c] <= 0 ? maxBox[c] : 0;
            PayOutColumnNeed(w, need, left);
        }
        for (var c = 0; c < n; c++)
        {
            // (a void column's box and spacing are nothing: its neighbours stand together)
            mt.colW[c] = mt.colVoid[c] ? -pad - mt.s : w[c] - pad;
            mt.colFixed[c] = true;
        }
    }

    /// <summary>Pays what is left out over the columns in proportion to their need, no column past
    /// its need; returns what remains.</summary>
    private static double PayOutColumnNeed(double[] w, double[] need, double left)
    {
        var sum = 0.0;
        foreach (var nd in need) sum += nd;
        if (sum <= 0 || left <= 0) return left;
        var give = Math.Min(left, sum);
        for (var c = 0; c < w.Length; c++) w[c] += give * need[c] / sum;
        return left - give;
    }

    /// <summary>A spanning cell whose min-content the columns it crosses cannot hold raises their
    /// mins: first the columns with slack under their max, in proportion to the slack and no further
    /// than the max, then all of them in proportion to their mins; narrower spans resolve first.</summary>
    private static void PushSpannedMinContent(MetricTableState mt, double[] minBox, double[] maxBox, double pad)
    {
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
            var need = MetricCellMinContentPt(mt, mc) + pad;
            var have = (cols.Count - 1) * mt.s;
            foreach (var k in cols) have += minBox[k];
            if (need <= have) continue;
            var excess = need - have;
            var slackSum = 0.0;
            foreach (var k in cols) slackSum += Math.Max(0, maxBox[k] - minBox[k]);
            if (slackSum > 0)
            {
                var toSlack = Math.Min(excess, slackSum);
                foreach (var k in cols) minBox[k] += toSlack * Math.Max(0, maxBox[k] - minBox[k]) / slackSum;
                excess -= toSlack;
            }
            if (excess <= 0) continue;
            var minSum = 0.0;
            foreach (var k in cols) minSum += minBox[k];
            foreach (var k in cols) minBox[k] += minSum > 0 ? excess * minBox[k] / minSum : excess / cols.Count;
        }
    }

    /// <summary>The UA percent grid's columns, on the probed rule (20 black-box probes, all
    /// within 0.01 pt): every column holds its min- and max-content; a SPANNING cell whose
    /// max-content exceeds what the columns it crosses hold scales their max-contents up to it,
    /// in proportion (the percent columns' own content counts in that sum, narrower spans first);
    /// the table's box is its declared percent of the span, or for a width-less grid the least
    /// of the span and the width the columns ask for (their sum, or a column's max-content scaled
    /// up to its share); the percent columns take their share of the box; the auto columns split
    /// what is left between their min-content and their pushed max-content - the surplus in
    /// proportion to the max, a shortfall in proportion to (max - min) - and when even their
    /// min-contents do not fit, they keep those and the percent columns shrink to the rest
    /// (measured: a 72 % pair beside `Phone:` and `(866) 331-3925` under an 867 pt reasons grid
    /// draws 290.16 / 47.70 / 65.14).</summary>
    private static void SolveUaPercentGridColumns(MetricTableState mt)
    {
        var n = mt.nCols;
        var pad = 2 * mt.p;
        var maxBox = new double[n];
        var minBox = new double[n];
        for (var c = 0; c < n; c++)
        {
            minBox[c] = mt.minCol[c] + pad;
            maxBox[c] = (mt.colFixed[c] ? mt.colW[c] : Math.Max(MetricColumnMaxContentPt(mt, c), mt.minCol[c])) + pad;
        }
        PushSpannedMaxContent(mt, maxBox, pad);
        var pctShare = 0.0;
        for (var c = 0; c < n; c++)
            if (!mt.colFixed[c] && mt.colPct[c] > 0) pctShare += mt.colPct[c] / 100.0;
        double box;
        if (mt.tablePct > 0) box = mt.usableSym * mt.tablePct / 100.0;
        else
        {
            var want = 0.0;
            var autoSum = 0.0;
            var scaleUp = 0.0;
            for (var c = 0; c < n; c++)
            {
                want += maxBox[c];
                if (mt.colFixed[c]) continue;
                if (mt.colPct[c] > 0) scaleUp = Math.Max(scaleUp, maxBox[c] / (mt.colPct[c] / 100.0));
                else autoSum += maxBox[c];
            }
            if (pctShare > 0 && pctShare < 1 - PercentShareEpsilon)
                scaleUp = Math.Max(scaleUp, autoSum / (1 - pctShare));
            box = Math.Min(mt.usableSym, Math.Max(want, scaleUp));
        }
        var rest = box;
        var autoMax = 0.0;
        var autoMin = 0.0;
        var pctSum = 0.0;
        var pctSlack = 0.0;
        var pctBox = new double[n];
        for (var c = 0; c < n; c++)
        {
            if (mt.colFixed[c]) { rest -= mt.colW[c] + pad; continue; }
            if (mt.colPct[c] > 0)
            {
                // a share never squeezes its column under its min-content
                pctBox[c] = Math.Max(mt.colPct[c] / 100.0 * box, minBox[c]);
                pctSum += pctBox[c];
                pctSlack += pctBox[c] - minBox[c];
                continue;
            }
            autoMax += maxBox[c];
            autoMin += minBox[c];
        }
        // the auto columns' min-contents come first: over-full shares give the deficit back from
        // their slack above min-content (a floored column keeps its content whole)
        var pctDeficit = pctSum - (rest - autoMin);
        if (pctDeficit > 0 && pctSlack > 0)
            for (var c = 0; c < n; c++)
                if (!mt.colFixed[c] && mt.colPct[c] > 0)
                    pctBox[c] -= Math.Min(pctDeficit, pctSlack) * (pctBox[c] - minBox[c]) / pctSlack;
        for (var c = 0; c < n; c++)
            if (!mt.colFixed[c] && mt.colPct[c] > 0) rest -= pctBox[c];
        if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_MCOL") == "1")
            Console.Error.WriteLine($"[upct] box={box:0.##} usableSym={mt.usableSym:0.##} rest={rest:0.##} autoMin={autoMin:0.##} autoMax={autoMax:0.##} min=[{string.Join(",", Array.ConvertAll(minBox, w => w.ToString("0.#")))}] max=[{string.Join(",", Array.ConvertAll(maxBox, w => w.ToString("0.#")))}] pct=[{string.Join(",", Array.ConvertAll(pctBox, w => w.ToString("0.#")))}] fixed=[{string.Join(",", mt.colFixed)}] w=[{string.Join(",", Array.ConvertAll(mt.colW, w => w.ToString("0.#")))}]");
        for (var c = 0; c < n; c++)
        {
            if (mt.colFixed[c]) continue;
            if (mt.colPct[c] > 0) { mt.colW[c] = pctBox[c] - pad; continue; }
            var w = maxBox[c];
            if (autoMax <= rest) w += autoMax > 0 ? (rest - autoMax) * maxBox[c] / autoMax : 0;
            else if (rest >= autoMin && autoMax - autoMin > 0)
                w = minBox[c] + (rest - autoMin) * (maxBox[c] - minBox[c]) / (autoMax - autoMin);
            else w = minBox[c];
            mt.colW[c] = w - pad;
        }
        for (var c = 0; c < n; c++) mt.colFixed[c] = true;
    }

    /// <summary>A spanning cell whose max-content the columns it crosses cannot hold scales their
    /// max-contents up to it, in proportion to what each holds (equally when none holds
    /// anything); narrower spans resolve first so a wide span scales the boxes the narrow ones
    /// left.</summary>
    private static void PushSpannedMaxContent(MetricTableState mt, double[] maxBox, double pad)
    {
        var spans = new List<(int c, int span, MetricCell mc)>();
        foreach (var r in mt.rows)
            for (var c = 0; c < r.Count; c++)
                if (r[c].ColSpan > 1 && !r[c].Phantom && Math.Min(r[c].ColSpan, mt.nCols - c) > 1)
                    spans.Add((c, Math.Min(r[c].ColSpan, mt.nCols - c), r[c]));
        spans.Sort((a, b) => a.span.CompareTo(b.span));
        foreach (var (c, span, mc) in spans)
        {
            var need = MetricCellMaxContentPt(mt, mc) + pad;
            var have = (span - 1) * mt.s;
            for (var k = c; k < c + span; k++) have += maxBox[k];
            if (need <= have) continue;
            var content = have - (span - 1) * mt.s;
            for (var k = c; k < c + span; k++)
            {
                if (mt.colFixed[k]) continue;
                maxBox[k] = content > 0 ? maxBox[k] * (need - (span - 1) * mt.s) / content
                    : (need - (span - 1) * mt.s) / span;
            }
        }
    }

    /// <summary>A collapsed grid fits its declared boxes into the available span before the columns are solved.</summary>
    private static void SolveMetricCollapseBoxes(MetricTableState mt)
    {
        if (mt.collapseBoxW > 0 && mt.nCols > 0)
        {
            mt.cbAvail = mt.availW - mt.symInsetPt - mt.collapseBoxW;
            mt.cbDeclBox = new double[mt.nCols];
            mt.cbMinBox = new double[mt.nCols];
            for (var c = 0; c < mt.nCols; c++)
            {
                double minC = 0;
                foreach (var r in mt.rows)
                    if (c < r.Count && r[c].ColSpan <= 1 && r[c].Text.Length > 0)
                        foreach (var word in r[c].Text.Replace('\u0001', ' ')
                                     .Split(' ', StringSplitOptions.RemoveEmptyEntries))
                            minC = Math.Max(minC, MeasureFaceText(
                                CellFaceName(mt.face, mt.boldFace, r[c]), word, r[c].FontSize ?? mt.mps.fontSize));
                mt.cbMinBox[c] = minC + 2 * mt.p;
                mt.cbDeclBox[c] = mt.colPx[c] > 0 ? mt.colPx[c] + 2 * mt.p : mt.cbMinBox[c];
                // (a UA cell's own inline padding past the grid's padding, and the collapsed rule,
                //  stand in the declared box too - probed on the e-mail cards: a 172 px cell with
                //  `padding-left: 20px` over 5 px class pads seats its neighbour 198 px on)
                if (mt.stdSerif && mt.colPx[c] > 0)
                {
                    double extra = 0;
                    foreach (var r in mt.rows)
                        if (c < r.Count && r[c].ColSpan <= 1 && r[c].WidthPx > 0)
                            extra = Math.Max(extra, Math.Max(0, r[c].PadLeft - mt.p) + Math.Max(0, r[c].PadRight - mt.p));
                    mt.cbDeclBox[c] += extra + 0.75;
                }
            }
            mt.cbSumDecl = 0;
            mt.cbSumSlack = 0;
            for (var c = 0; c < mt.nCols; c++)
            {
                mt.cbSumDecl += mt.cbDeclBox[c];
                mt.cbSumSlack += Math.Max(0, mt.cbDeclBox[c] - mt.cbMinBox[c]);
            }
            mt.cbDeficit = mt.cbSumDecl - mt.cbAvail;
            for (var c = 0; c < mt.nCols; c++)
            {
                var box = mt.cbDeclBox[c];
                if (mt.cbDeficit > 0 && mt.cbSumSlack > 0)
                    box = Math.Max(mt.cbMinBox[c], mt.cbDeclBox[c]
                        - mt.cbDeficit * Math.Max(0, mt.cbDeclBox[c] - mt.cbMinBox[c]) / mt.cbSumSlack);
                mt.colW[c] = box - 2 * mt.p;
                // (a column declaring no width stays an AUTO column: it takes the declared box's
                // surplus in proportion to its content like any other - measured on the enterprise
                // summary's collapsed 200 px label grids: 56 / 10 pt label and value columns solve
                // 126.4 / 23.6, not their min-contents)
                if (mt.colPx[c] > 0 || !mt.stdSerif) mt.colFixed[c] = true;
            }
        }
        mt.usableW = mt.availW - (mt.nCols + 1) * mt.s - mt.mps.tablePadLeftPt;
        mt.uaPctGrid = false;
        if (mt.stdSerif && !mt.mps.bordered)
            foreach (var pc in mt.colPct) if (pc > 0) { mt.uaPctGrid = true; break; }
        // (the pt form grid solves every table on its own rule, shares or none)
        if (mt.stdSerif && mt.mps.ptFormCells) mt.uaPctGrid = true;
    }

    /// <summary>A table's own COLGROUP states its columns' shares. The metric grid only ever
    /// read the CELLS, so `&lt;col style="width:49.75%"&gt;` was ignored and the column auto-sized to
    /// its text - the complaint report's heading column came out 330.7 wide against a declared
    /// 49.75% share and carried its heading 43 pt right of the reference. A cell's own declared
    /// width still wins below, as the nearer declaration should.
    /// The share resolves through the ordinary percent path, whose base reproduces the measured
    /// column pitch: probed across bodies of 10, 14, 17.59 and 20 cm, a 25% column's pitch is
    /// 0.25 x (declared body width + border-spacing) every time.</summary>
    private static void SeedMetricColumnsFromColGroup(MetricTableState mt)
    {
        if (string.IsNullOrEmpty(mt.tableHtml)) return;
        var col = 0;
        foreach (Match cm in MetricColTagRx.Matches(mt.tableHtml))
        {
            if (col >= mt.nCols) break;
            var span = 1;
            var spanM = Regex.Match(cm.Value, @"(?<![-\w])span\s*=\s*[""']?(\d+)", RegexOptions.IgnoreCase);
            if (spanM.Success && int.TryParse(spanM.Groups[1].Value, out var spanN) && spanN > 1) span = spanN;
            // CSS allows a leading-dot number: `.25%` is a quarter of a percent, and a pattern
            // demanding a leading digit silently drops such a column to auto width.
            var pctM = Regex.Match(cm.Value,
                @"style\s*=\s*[""'][^""']*(?<![-\w])width\s*:\s*(\d*\.?\d+)\s*%", RegexOptions.IgnoreCase);
            // An ABSOLUTE colgroup width is a MINIMUM on its column, kept in its own slot so it
            // cannot disturb the cell-declared widths (a declared width on a text column is
            // deliberately not pinned - pinning one once wrapped a cell onto a 61st page).
            // Probed through the reference on bodies declaring 5, 7, 9 and 12 em: the drawn
            // column PITCH is the declared width plus the border-spacing every time, so the
            // content box is the declared width less the two paddings.
            var absPt = 0.0;
            if (!pctM.Success)
            {
                var absM = Regex.Match(cm.Value,
                    @"style\s*=\s*[""'][^""']*(?<![-\w])width\s*:\s*(\d*\.?\d+)\s*(em|rem|px|pt|cm|mm|in)",
                    RegexOptions.IgnoreCase);
                if (absM.Success && double.TryParse(absM.Groups[1].Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var n) && n > 0)
                    absPt = CssColLengthPt(n, absM.Groups[2].Value, mt.mps.fontSize);
            }
            // …and its `min-width` is a FLOOR under whatever the share solves to, in the
            // same box terms as the width above (probed: a 6 em floor under a 12.5 % share
            // of a 486.6 pt span draws a 72 pt column and gives its share's shortfall back
            // to the columns that still have slack). It lives beside a percentage, so it is
            // read whether or not the column also declares a width.
            var minAbsPt = 0.0;
            var minM = Regex.Match(cm.Value,
                @"style\s*=\s*[""'][^""']*(?<![-\w])min-width\s*:\s*(\d*\.?\d+)\s*(em|rem|px|pt|cm|mm|in)",
                RegexOptions.IgnoreCase);
            if (minM.Success && double.TryParse(minM.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var mn) && mn > 0)
                minAbsPt = CssColLengthPt(mn, minM.Groups[2].Value, mt.mps.fontSize);
            for (var k = 0; k < span && col < mt.nCols; k++, col++)
            {
                if (minAbsPt > 0)
                    mt.colMinWidthPt[col] = Math.Max(mt.colMinWidthPt[col], minAbsPt - 2 * mt.p);
                if (pctM.Success)
                {
                    if (!double.TryParse(pctM.Groups[1].Value, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var pct) || pct <= 0) continue;
                    mt.colPct[col] = Math.Max(mt.colPct[col], pct);
                }
                else if (absPt > 0)
                {
                    mt.colGroupPt[col] = Math.Max(mt.colGroupPt[col], absPt - 2 * mt.p);
                }
            }
        }
    }

    /// <summary>A colgroup length in points: an `em` resolves against the table's own font
    /// size, everything else by its unit, and a unitless value is already points.</summary>
    private static double CssColLengthPt(double n, string unit, double emPt)
        => unit.ToLowerInvariant() switch
        {
            "em" or "rem" => n * emPt,
            "px" => n * PxPt,
            "cm" => n * 28.346457,
            "mm" => n * 2.8346457,
            "in" => n * 72.0,
            _ => n,
        };

    /// <summary>A `col` element of a table's column group.</summary>
    private static readonly Regex MetricColTagRx =
        new Regex(@"<col\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Scan every cell for its declared width and its natural minimum, column by column.</summary>
    private static void MeasureMetricColumnMinima(MetricTableState mt)
    {
        mt.colW = new double[mt.nCols];
        mt.colPct = new double[mt.nCols];
        mt.colPx = new double[mt.nCols];
        mt.colPxStyle = new bool[mt.nCols];
        mt.colZero = new bool[mt.nCols];
        mt.colFixed = new bool[mt.nCols];
        mt.colDeclared = new bool[mt.nCols];
        mt.colGroupPt = new double[mt.nCols];
        mt.colMinWidthPt = new double[mt.nCols];
        SeedMetricColumnsFromColGroup(mt);
        foreach (var r in mt.rows)
            for (var c = 0; c < r.Count; c++)
            {
                if (r[c].SpanW > 0 && r[c].SpanW > (mt.colFixed[c] ? mt.colW[c] : 0))
                { mt.colW[c] = r[c].SpanW; mt.colFixed[c] = true; }
                var cSpan = Math.Max(1, r[c].ColSpan);
                // RTL attribute grids and the pt-report mode: a SPANNING cell's
                // declared width never pins the slots it crosses — the
                // non-spanning cells' declared widths fix their columns and the
                // spanner rides over them (measured: the 600px colspan cell
                // lands at 561.75 − the 19/98/91 px columns; the report's
                // width=84% colspan=3 cell must not widen its middle columns).
                if ((mt.rtl || (!mt.stdSerif && mt.wrapperStacks)) && cSpan > 1) continue;
                // (a quirks UA grid's spanning cells floor their columns once every plain share is
                //  known - the pass below - so a span's width never widens columns that already
                //  hold it: MEASURED, the evaluation form's `width=552 colspan=2` cells over 360 + 192)
                if (mt.stdSerif && _quirksRowStrut && cSpan > 1 && r[c].WidthPx > 0 && !r[c].WidthPxStyle) continue;
                // a plain cell's share is its column's; a spanning cell's share is
                // distributed over its span once every plain share is known (below)
                if (r[c].WidthPct > 0 && cSpan == 1)
                    mt.colPct[c] = Math.Max(mt.colPct[c], r[c].WidthPct);
                // a UA form grid's class share is a share like the attribute's (measured on the
                // test request: a `width: 50%` label class beside a 216 pt input column takes
                // what the input leaves, 157.5 of the 375 span)
                if (mt.mps.uaFormCells && r[c].ClassWidthPct > 0 && cSpan == 1)
                    mt.colPct[c] = Math.Max(mt.colPct[c], r[c].ClassWidthPct);
                if (r[c].WidthZero && cSpan == 1) mt.colZero[c] = true;
                for (var k = 0; k < cSpan && c + k < mt.nCols; k++)
                {
                    if (r[c].WidthPx > 0)
                    {
                        mt.colPx[c + k] = Math.Max(mt.colPx[c + k], r[c].WidthPx / cSpan);
                        if (r[c].WidthPxStyle) mt.colPxStyle[c + k] = true;
                    }
                }
            }
        // (the quirks UA grid's spanning pixel cells, deferred above: a span whose columns already
        //  hold its width floors nothing; one they fall short of takes the calibrated equal split)
        if (mt.stdSerif && _quirksRowStrut)
            foreach (var r in mt.rows)
                for (var c = 0; c < r.Count; c++)
                {
                    var cSpan = Math.Max(1, r[c].ColSpan);
                    if (cSpan <= 1 || r[c].WidthPx <= 0 || r[c].WidthPxStyle) continue;
                    double held = 0;
                    for (var k = 0; k < cSpan && c + k < mt.nCols; k++) held += mt.colPx[c + k];
                    if (held >= r[c].WidthPx - 1e-6) continue;
                    for (var k = 0; k < cSpan && c + k < mt.nCols; k++)
                        mt.colPx[c + k] = Math.Max(mt.colPx[c + k], r[c].WidthPx / cSpan);
                }
        // A spanning cell's share covers the shares its columns already hold: only the
        // excess is shared out, equally over the share-less columns of the span, or by
        // scaling the span's shares up when every column already holds one.
        foreach (var r in mt.rows)
            for (var c = 0; c < r.Count; c++)
                if (r[c].WidthPct > 0 && r[c].ColSpan > 1 && !(mt.rtl || (!mt.stdSerif && mt.wrapperStacks)))
                    SpreadSpanningShare(mt.colPct, c, Math.Min(r[c].ColSpan, mt.nCols - c), r[c].WidthPct);
        // Percent shares are honoured in column order: once the shares before a column
        // reach the whole width, its own share is cut to what remains — an over-declared
        // grid (a 100 % spanning cell beside a 75 % cell) leaves the later column nothing
        // but its content, the way the browser resolves it.
        CapPercentSharesInOrder(mt.colPct);
        // Bordered mode: CSS table column resolution against the availW box.
        // FIXED layout: each column takes its declared percent of the table's
        // inner width (inside the outer border) — content neither wraps nor
        // widens it, so a long word OVERFLOWS across the neighbour (and the
        // table's chrome pushes its box past the declared width). AUTO layout:
        // a column is max(declared share, min-content) and — under width:100% —
        // the leftover goes to the LAST column (all measured).
        if (mt.mps.bordered) { FitBorderedColumns(mt.mps, mt.rows, mt.colW, mt.colFixed, mt.colPct, mt.colPx, mt.colPxStyle, mt.nCols, mt.availW, mt.bw, mt.p, mt.pageWidth, mt.pageHeight, mt.marginTop, mt.marginBottom, mt.tableWpt, mt.tablePct, mt.baseFontSize, mt.paragraphCells, mt.tableHtml, mt.s, mt.face, mt.boldFace, mt.symInsetPt, mt.tableFills, mt.stdSerif, mt); }
    }
}
