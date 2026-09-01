using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Converters;

internal static partial class EdgarHtmlRenderer
{
    sealed partial class Engine
    {
        ColModel BuildColModel(Node table, List<Node> rows, int nCols, Style tableStyle)
        {
            var m = new ColModel
            {
                Min = new double[nCols],
                Max = new double[nCols],
                Pct = new double[nCols],
                NCols = nCols,
            };
            // pass 1: single-span cells
            foreach (var tr in rows)
            {
                var trStyle = tableStyle.Clone();
                ApplyStyleAttr(tr.Attr("style"), trStyle);
                int col = 0;
                foreach (var td in tr.Children.Where(x => x.Tag is "td" or "th"))
                {
                    int span = Math.Max(1, ParseIntAttr(td, "colspan"));
                    var wAttr = td.Attr("width");
                    if (wAttr.EndsWith("%") && double.TryParse(wAttr.TrimEnd('%'), out var p))
                        m.Pct[col] = Math.Max(m.Pct[col], p);
                    if (span == 1)
                    {
                        var cellStyle = trStyle.Clone();
                        ApplyStyleAttr(td.Attr("style"), cellStyle);
                        var (mn, mx) = CellContentWidths(td, cellStyle);
                        m.Min[col] = Math.Max(m.Min[col], mn);
                        m.Max[col] = Math.Max(m.Max[col], mx);
                    }
                    col += span;
                }
            }
            // pass 2: colspans replace spanned mins/maxes proportionally to MAX
            foreach (var tr in rows)
            {
                var trStyle = tableStyle.Clone();
                ApplyStyleAttr(tr.Attr("style"), trStyle);
                int col = 0;
                foreach (var td in tr.Children.Where(x => x.Tag is "td" or "th"))
                {
                    int span = Math.Max(1, ParseIntAttr(td, "colspan"));
                    if (span > 1)
                    {
                        var cellStyle = trStyle.Clone();
                        ApplyStyleAttr(td.Attr("style"), cellStyle);
                        var (mn, mx) = CellContentWidths(td, cellStyle);
                        int hi = Math.Min(col + span, nCols);
                        double sMin = 0, sMax = 0;
                        for (int i = col; i < hi; i++) { sMin += m.Min[i]; sMax += m.Max[i]; }
                        if (mn > sMin)
                        {
                            for (int i = col; i < hi; i++)
                                m.Min[i] = sMax > 0 ? mn * m.Max[i] / sMax : mn / (hi - col);
                        }
                        if (mx > sMax)
                        {
                            for (int i = col; i < hi; i++)
                                m.Max[i] = sMax > 0 ? mx * m.Max[i] / sMax : mx / (hi - col);
                        }
                        for (int i = col; i < hi; i++)
                            if (m.Max[i] < m.Min[i]) m.Max[i] = m.Min[i];
                    }
                    col += span;
                }
            }
            return m;
        }

        double[] DistributeColumns(ColModel m, double W)
        {
            int n = m.NCols;
            var colW = new double[n];
            // cumulative-capped pct claims, left to right
            var claim = new double[n];
            double running = 0;
            for (int i = 0; i < n; i++)
            {
                if (m.Pct[i] > 0)
                {
                    var eff = Math.Min(m.Pct[i], Math.Max(0, 100 - running));
                    running += eff;
                    claim[i] = eff / 100.0 * W;
                }
            }
            double sumMin = m.Min.Sum();
            double B = W - sumMin;
            if (B <= 0)
            {
                for (int i = 0; i < n; i++) colW[i] = m.Min[i];
                return colW;
            }
            // stage 1: pct fill
            var need = new double[n];
            double sumNeed = 0;
            for (int i = 0; i < n; i++)
            {
                if (m.Pct[i] > 0) { need[i] = Math.Max(0, claim[i] - m.Min[i]); sumNeed += need[i]; }
            }
            if (sumNeed > B)
            {
                for (int i = 0; i < n; i++)
                    colW[i] = m.Min[i] + (m.Pct[i] > 0 ? B * need[i] / sumNeed : 0);
                return colW;
            }
            for (int i = 0; i < n; i++)
                colW[i] = m.Pct[i] > 0 ? Math.Max(m.Min[i], claim[i]) : m.Min[i];
            B -= sumNeed;
            // stage 2: auto fill toward max
            var needA = new double[n];
            double sumNeedA = 0;
            for (int i = 0; i < n; i++)
            {
                if (m.Pct[i] <= 0) { needA[i] = Math.Max(0, m.Max[i] - m.Min[i]); sumNeedA += needA[i]; }
            }
            if (sumNeedA > B)
            {
                for (int i = 0; i < n; i++)
                    if (m.Pct[i] <= 0) colW[i] = m.Min[i] + B * needA[i] / sumNeedA;
                return colW;
            }
            for (int i = 0; i < n; i++)
                if (m.Pct[i] <= 0) colW[i] = Math.Max(m.Min[i], m.Max[i]);
            B -= sumNeedA;
            if (B <= 0) return colW;
            // stage 3: surplus
            bool anyAuto = false;
            double sumAutoMax = 0;
            int autoCount = 0;
            for (int i = 0; i < n; i++)
                if (m.Pct[i] <= 0) { anyAuto = true; sumAutoMax += m.Max[i]; autoCount++; }
            if (anyAuto)
            {
                if (sumAutoMax > 0)
                {
                    for (int i = 0; i < n; i++)
                        if (m.Pct[i] <= 0) colW[i] += B * m.Max[i] / sumAutoMax;
                }
                else
                {
                    for (int i = 0; i < n; i++)
                        if (m.Pct[i] <= 0) colW[i] += B / autoCount;
                }
            }
            else
            {
                double sumP = 0;
                for (int i = 0; i < n; i++) sumP += m.Pct[i];
                if (sumP > 0)
                    for (int i = 0; i < n; i++) colW[i] += B * m.Pct[i] / sumP;
            }
            return colW;
        }

        /// <summary>Page width grows so the widest table's min-content fits:
        /// pageW = max(595, maxOverTables(Σ column mins) + 186).</summary>
        double MeasureWidestTable(Node body)
        {
            double best = 0;
            foreach (var table in body.Descendants().Where(n => n.Tag == "table"))
            {
                var st = new Style();
                ApplyStyleAttr(table.Attr("style"), st);
                var rows = new List<Node>();
                CollectRows(table, rows);
                if (rows.Count == 0) continue;
                int nCols = 0;
                foreach (var tr in rows)
                {
                    int c = 0;
                    foreach (var td in tr.Children.Where(x => x.Tag is "td" or "th"))
                        c += Math.Max(1, ParseIntAttr(td, "colspan"));
                    nCols = Math.Max(nCols, c);
                }
                if (nCols == 0) continue;
                var model = BuildColModel(table, rows, nCols, st);
                best = Math.Max(best, model.Min.Sum());
            }
            return best;
        }

        void LayoutTable(Node table, Style inherited)
        {
            var st = inherited.Clone();
            ApplyStyleAttr(table.Attr("style"), st);

            var rows = new List<Node>();
            CollectRows(table, rows);
            if (rows.Count == 0) return;

            int nCols = 0;
            foreach (var tr in rows)
            {
                int c = 0;
                foreach (var td in tr.Children.Where(x => x.Tag is "td" or "th"))
                    c += Math.Max(1, ParseIntAttr(td, "colspan"));
                nCols = Math.Max(nCols, c);
            }
            if (nCols == 0) return;

            var model = BuildColModel(table, rows, nCols, st);

            // table width: N% of the content box (100% default); absent → shrink
            // to max-content, capped at the content box
            double W = _contentW;
            var wAttr = table.Attr("width");
            if (wAttr.EndsWith("%") && double.TryParse(wAttr.TrimEnd('%'), out var wp))
                W = wp / 100.0 * _contentW;
            else if (wAttr.Length == 0)
                W = Math.Min(_contentW, Math.Max(model.Min.Sum(), model.Max.Sum()));

            var colW = DistributeColumns(model, W);

            // narrower tables with align=center sit centered in the content box
            var drawn = colW.Sum();
            _tableX = 96;
            if (drawn < _contentW - 0.01
                && table.Attr("align").Equals("center", StringComparison.OrdinalIgnoreCase))
                _tableX = 96 + (_contentW - drawn) / 2;

            // per-column half-border carried from a bordered row into the next
            // (BORDER-COLLAPSE: the border straddles the shared row edge)
            var carry = new double[nCols];
            foreach (var tr in rows)
                carry = LayoutRow(tr, colW, st, carry);
            if (carry.Length > 0 && carry.Max() > 0)
                _y += carry.Max();
            _tableX = 96;

            // table imposes no extra bottom margin of its own
        }

        static void CollectRows(Node n, List<Node> rows)
        {
            foreach (var c in n.Children)
            {
                if (c.Tag == "tr") rows.Add(c);
                else if (c.Tag is "tbody" or "thead" or "tfoot") CollectRows(c, rows);
            }
        }

        (double mn, double mx) CellContentWidths(Node td, Style cellStyle)
        {
            // max = longest unwrapped line; min = longest unbreakable word (plus the
            // continuation-line indent for hanging paragraphs); an explicit
            // <p style="width:Xpt"> forces both to X
            double mx = 0, mn = 0;
            bool nowrap = td.Attrs is not null && td.Attrs.ContainsKey("nowrap");
            foreach (var block in EnumerateCellBlocks(td))
            {
                var st = cellStyle.Clone();
                st.MarginLeft = 0; st.TextIndent = 0;
                double? forcedW = null;
                if (block.Tag == "p")
                {
                    ApplyStyleAttr(block.Attr("style"), st);
                    var wDecl = Regex.Match(block.Attr("style"), @"(?:^|;)\s*width\s*:\s*([\d.]+)\s*pt",
                        RegexOptions.IgnoreCase);
                    if (wDecl.Success)
                        forcedW = double.Parse(wDecl.Groups[1].Value, CultureInfo.InvariantCulture);
                }
                if (forcedW is { } fw)
                {
                    mx = Math.Max(mx, fw);
                    mn = Math.Max(mn, fw);
                    continue;
                }
                var lineIndent = Math.Max(0, st.MarginLeft); // continuation lines
                var runs = CollectRuns(block, st);
                double w = 0, word = 0;
                bool firstWordOfBlock = true;
                void EndWord()
                {
                    if (word > 0)
                        mn = Math.Max(mn, word + (firstWordOfBlock ? Math.Max(0, st.MarginLeft + st.TextIndent) : lineIndent));
                    firstWordOfBlock = false;
                    word = 0;
                }
                foreach (var r in runs)
                {
                    if (r.Text == "\n")
                    {
                        EndWord();
                        mx = Math.Max(mx, w); w = 0;
                        continue;
                    }
                    w += r.Face.Measure(r.Text, r.Size);
                    int i = 0;
                    var text = r.Text;
                    while (i < text.Length)
                    {
                        if (text[i] == ' ') { EndWord(); i++; continue; }
                        int j = i;
                        while (j < text.Length && text[j] != ' ') j++;
                        word += r.Face.Measure(text.Substring(i, j - i), r.Size);
                        i = j;
                    }
                }
                EndWord();
                mx = Math.Max(mx, w + Math.Max(0, st.MarginLeft + st.TextIndent));
            }
            if (nowrap) mn = mx;
            return (mn, mx);
        }

        IEnumerable<Node> EnumerateCellBlocks(Node td)
        {
            // direct <p> children flow as separate blocks; loose inline content is one block
            var loose = new Node { Tag = "p" };
            foreach (var c in td.Children)
            {
                if (c.Tag == "p")
                {
                    if (loose.Children.Count > 0) { yield return loose; loose = new Node { Tag = "p" }; }
                    yield return c;
                }
                else loose.Children.Add(c);
            }
            if (loose.Children.Count > 0) yield return loose;
        }

        /// <summary>Lay out one cell as a mini block flow (same gap model as the page
        /// flow: desc + collapsed margins + borders + asc), returning line offsets
        /// from the cell top.</summary>
        CellFlow LayoutCellFlow(Node td, Style trStyle, double x0, double width, int col, int span)
        {
            var flow = new CellFlow { Col = col, Span = span, Valign = td.Attr("valign").ToLowerInvariant(), X0 = x0, Width = width };
            var cellStyle = trStyle.Clone();
            ApplyStyleAttr(td.Attr("style"), cellStyle);
            double y = 0;            // running content bottom (box bottoms)
            bool first = true;
            var margins = new List<double>();
            double prevBorderBottom = 0;
            foreach (var block in EnumerateCellBlocks(td))
            {
                var st = cellStyle.Clone();
                st.MarginLeft = 0; st.TextIndent = 0; st.MarginTop = 0; st.MarginBottom = 0;
                st.BorderTopW = 0; st.BorderBottomW = 0; st.LineHeight = null;
                st.Align = td.Attr("align").ToLowerInvariant();
                if (block.Tag == "p")
                {
                    var alignAttr = block.Attr("align");
                    if (alignAttr.Length > 0) st.Align = alignAttr.ToLowerInvariant();
                    ApplyStyleAttr(block.Attr("style"), st);
                }
                var runs = CollectRuns(block, st);
                if (runs.Count == 0 || runs.All(r => r.Text.Length == 0 && r.AnchorsBefore is null))
                {
                    margins.Add(st.MarginTop);
                    margins.Add(st.MarginBottom);
                    continue;
                }
                var metricRun = runs.Where(r => r.Text.Length > 0).OrderByDescending(r => r.Size).FirstOrDefault();
                if (metricRun is null) continue;
                var (pitch, asc, desc) = LineBox(metricRun.Face, metricRun.Size, st.LineHeight);
                var wrapped = WrapRuns(runs, Math.Max(1, width - st.MarginLeft), st.TextIndent);
                for (int li = 0; li < wrapped.Count; li++)
                {
                    double top;
                    if (first)
                    {
                        top = st.BorderTopW; // cell top: margins vanish (cellpadding 0)
                        first = false;
                    }
                    else if (li == 0)
                    {
                        margins.Add(st.MarginTop);
                        top = y + prevBorderBottom + margins.Max() + st.BorderTopW;
                    }
                    else
                    {
                        top = y; // consecutive lines of a block abut
                    }
                    margins.Clear();
                    prevBorderBottom = 0;
                    var line = new CellLine
                    {
                        Top = top,
                        Asc = asc,
                        Desc = desc,
                        St = st,
                        Pieces = wrapped[li],
                        FirstLine = li == 0,
                        BorderTopW = li == 0 ? st.BorderTopW : 0,
                        BorderTopColor = st.BorderTopColor,
                        BorderBottomW = li == wrapped.Count - 1 ? st.BorderBottomW : 0,
                        BorderBottomColor = st.BorderBottomColor,
                    };
                    flow.Lines.Add(line);
                    y = top + asc + desc;
                }
                margins.Add(st.MarginBottom);
                prevBorderBottom = st.BorderBottomW;
            }
            flow.Height = y + prevBorderBottom;
            return flow;
        }

        double[] LayoutRow(Node tr, double[] colW, Style tableStyle, double[] carryIn)
        {
            int nCols = colW.Length;
            var carryOut = new double[nCols];
            var trStyle = tableStyle.Clone();
            ApplyStyleAttr(tr.Attr("style"), trStyle);
            var bg = tr.Attr("bgcolor");
            int bgColor = -1;
            if (bg.Length > 0)
            {
                var m = Regex.Match(bg, @"#?([0-9A-Fa-f]{6})");
                if (m.Success) bgColor = int.Parse(m.Groups[1].Value, NumberStyles.HexNumber);
            }

            var cells = tr.Children.Where(x => x.Tag is "td" or "th").ToList();
            if (cells.Count == 0) return carryOut;

            // spacer row: explicit height attr and no visible content
            int hAttr = 0;
            foreach (var td in cells)
                if (int.TryParse(td.Attr("height"), out var hv)) hAttr = Math.Max(hAttr, hv);
            bool anyBorderPara = cells.Any(td => td.Descendants().Any(d =>
                d.Tag == "p" && d.Attr("style").Contains("border", StringComparison.OrdinalIgnoreCase)));
            bool anyTdBorder = cells.Any(td => TdBorderBottom(td) > 0);
            bool empty = !anyBorderPara && !anyTdBorder
                && cells.All(td => CollectRuns(td, trStyle).All(r => !RunHasInk(r)));
            if (empty && hAttr > 0)
            {
                double h = hAttr * 0.75 + (carryIn.Length > 0 ? carryIn.Max() : 0);
                double top = _atPageTop ? _y + (_dropTopMargins ? 0 : (_margins.Count > 0 ? _margins.Max() : 0)) : _y + (_margins.Count > 0 ? _margins.Max() : 0) + _prevBorderBottom;
                _atPageTop = false;
                _margins.Clear();
                if (top + h > BottomLimit) { BreakPage(false); top = _y; }
                _y = top + h;
                EndBlock(0, 0);
                return carryOut;
            }
            if (empty && cells.All(td => td.Children.Count == 0))
                return carryIn; // width-definition row: zero height, borders pass on

            // per-cell mini flows
            var flows = new List<CellFlow>();
            var tdBorders = new List<double>();
            int colIdx = 0;
            foreach (var td in cells)
            {
                int span = Math.Max(1, ParseIntAttr(td, "colspan"));
                double x0 = _tableX;
                for (int i = 0; i < colIdx; i++) x0 += colW[i];
                double width = 0;
                for (int i = colIdx; i < Math.Min(colIdx + span, colW.Length); i++) width += colW[i];
                flows.Add(LayoutCellFlow(td, trStyle, x0, width, colIdx, span));
                tdBorders.Add(TdBorderBottom(td));
                colIdx += span;
            }

            // per-cell content top offset from the row top (half border carried in)
            var topOffsets = new List<double>();
            foreach (var f in flows)
            {
                double off = 0;
                for (int i = f.Col; i < Math.Min(f.Col + f.Span, nCols); i++)
                    off = Math.Max(off, i < carryIn.Length ? carryIn[i] : 0);
                topOffsets.Add(off);
            }

            // the row edge: max over cells of contentTop + stack + own half border
            double rowH = 0;
            for (int i = 0; i < flows.Count; i++)
                rowH = Math.Max(rowH, topOffsets[i] + flows[i].Height + tdBorders[i] / 2);
            if (rowH <= 0) return carryIn;

            // align cells vertically: bottom-valign content bottoms sit at the row
            // edge minus the cell's own half border; top-valign at its content top
            for (int i = 0; i < flows.Count; i++)
            {
                var f = flows[i];
                double contentBottomTarget = rowH - tdBorders[i] / 2;
                double dy = f.Valign switch
                {
                    "top" => topOffsets[i],
                    "middle" => topOffsets[i] + (contentBottomTarget - topOffsets[i] - f.Height) / 2,
                    _ => contentBottomTarget - f.Height, // bottom is the EDGAR default
                };
                if (dy > 0)
                    foreach (var ln in f.Lines) ln.Top += dy;
            }

            // place the row: it may straddle pages at line granularity
            double rowTop;
            if (_atPageTop) { rowTop = _y + (_dropTopMargins ? 0 : (_margins.Count > 0 ? _margins.Max() : 0)); _atPageTop = false; }
            else
            {
                rowTop = _y + _prevBorderBottom + (_margins.Count > 0 ? _margins.Max() : 0);
            }
            _margins.Clear();
            _prevBorderBottom = 0;

            // if even the shallowest first line misses the page, push the whole row
            double firstLineBottom = flows.Where(f => f.Lines.Count > 0)
                .Select(f => rowTop + f.Lines[0].Top + f.Lines[0].Asc + f.Lines[0].Desc)
                .DefaultIfEmpty(rowTop).Min();
            if (firstLineBottom > BottomLimit + 0.01)
            {
                BreakPage(false);
                rowTop = _y;
                _atPageTop = false;
            }

            // bg fill for the row region (clipped to this page)
            if (bgColor >= 0)
                _pg.Rects.Add(new RectFill { X = _tableX, TopTd = rowTop, W = colW.Sum(), H = Math.Min(rowH, BottomLimit - rowTop), Color = bgColor, Stroke = false });

            // place lines in vertical order; break mid-row when a line misses
            double pageShift = 0;
            var pending = flows.SelectMany(f => f.Lines.Select(l => (f, l)))
                .OrderBy(t => t.l.Top).ToList();
            foreach (var (f, ln) in pending)
            {
                var top = rowTop + ln.Top - pageShift;
                var bottom = top + ln.Asc + ln.Desc;
                if (bottom > BottomLimit + 0.01)
                {
                    BreakPage(false);
                    _atPageTop = false;
                    pageShift += top - _y;
                    top = rowTop + ln.Top - pageShift;
                }
                var baseline = top + ln.Asc;
                if (ln.BorderTopW > 0)
                    _pg.Rects.Add(new RectFill { X = f.X0, TopTd = top - ln.BorderTopW / 2, W = f.Width, H = 0, Color = ln.BorderTopColor, Stroke = true, LineW = ln.BorderTopW });
                double lineW = ln.Pieces.Sum(p => p.W);
                double x = f.X0 + ln.St.MarginLeft + (ln.FirstLine ? ln.St.TextIndent : 0);
                if (ln.St.Align == "center") x = f.X0 + (f.Width - lineW) / 2;
                else if (ln.St.Align == "right") x = f.X0 + f.Width - lineW;
                foreach (var piece in ln.Pieces)
                {
                    var r = piece.Run;
                    AddRun(new Run { Text = piece.Text, Face = r.Face, Size = r.Size, Color = r.Color, Sup = r.Sup, LinkId = r.LinkId, AnchorsBefore = r.AnchorsBefore }, x, baseline - (r.Sup ? 1.26 : 0));
                    r.AnchorsBefore = null;
                    if (r.LinkId >= 0 && RunHasInk(piece.Run) && piece.Text.Trim(' ', (char)0xA0).Length > 0)
                        AddLinkRect(r.LinkId, x, baseline, x + piece.W, r.Face, r.Size);
                    x += piece.W;
                }
                if (ln.BorderBottomW > 0)
                    _pg.Rects.Add(new RectFill { X = f.X0, TopTd = top + ln.Asc + ln.Desc + ln.BorderBottomW / 2, W = f.Width, H = 0, Color = ln.BorderBottomColor, Stroke = true, LineW = ln.BorderBottomW });
            }

            // collapsed td borders: stroke on the row edge; carry half into next row
            for (int i = 0; i < flows.Count; i++)
            {
                if (tdBorders[i] > 0)
                {
                    var f = flows[i];
                    _pg.Rects.Add(new RectFill { X = f.X0, TopTd = rowTop + rowH - pageShift, W = f.Width, H = 0, Color = 0, Stroke = true, LineW = tdBorders[i] });
                    for (int c = f.Col; c < Math.Min(f.Col + f.Span, nCols); c++)
                        carryOut[c] = tdBorders[i] / 2;
                }
            }

            _y = rowTop + rowH - pageShift;
            EndBlock(0, 0);
            return carryOut;
        }

        static double TdBorderBottom(Node td)
        {
            var style = td.Attr("style");
            if (style.Length == 0) return 0;
            var s = new Style();
            ApplyStyleAttr(style, s);
            return s.BorderBottomW;
        }

    }
}
