using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    // The letter's tables. The sheet gives every letter table 95% of its containing box and
    // zero cell spacing; a table nested in a letter cell shrinks to its content on the UA 2px
    // spacing (the demographics block declares 0). Auto layout follows the probed column law:
    // an auto column asks for its max-content, a percent column for its share, a deficit is
    // taken from the auto columns in proportion to their slack over min-content (an nbsp
    // never breaks) and only then from the percent columns, a surplus goes to the auto
    // columns in proportion to their widths. The fixed-layout TTO grid gives a percent
    // column its share of the width less one rule plus one rule, and the rest to the autos.
    private const double LpTablePct = 0.95;             // #tblPatient, #tblGP, #tblTTO …: width 95%
    private const double LpThPadTop = 1.5;              // th padding 2px …
    private const double LpThPadSide = 11.25;           // … 0 0 15px (#tblPatient th)
    private const double LpThPadRight = 7.5;            // … 10px 0 0 (#tblPatient2 th)
    private const double LpTdPadLeft = 7.5;             // #tblPatient td { padding-left: 10px }
    private const double LpUaCellPad = 0.75;            // the UA 1px cell padding
    private const double LpUaSpacing = 1.5;             // the UA 2px border-spacing
    private const double LpTtoThPadTop = 4.5;           // #tblTTO th { padding: 6px 0 8px 0 }
    private const double LpTtoThPadBottom = 6.0;
    private const double LpTtoTdPad = 1.5;              // #tblTTO td { padding: 2px }
    private const double LpTtoTdFs = 10.0;              // … font-size: 10pt
    private const double LpBlockTheadPad = 1.125;       // a block-displayed thead sits 1.5px lower and pushes its body 1.5px

    private static readonly Regex LpTableTagRx = new("<(/?)(table|tr|td|th|thead)\\b([^>]*)>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static bool LpIsDemographics(string id)
        => id is "tblPatient2" or "tblGP2" or "tblCompleter" or "tblOther";

    /// <summary>Parse a letter table (its outer html) at its em depth.</summary>
    private static LpTable LpParseTable(string html, int depth, string? parentId)
    {
        var t = new LpTable { Depth = depth };
        var open = Regex.Match(html, "<table\\b([^>]*)>", RegexOptions.IgnoreCase);
        var attrs = open.Groups[1].Value;
        t.Id = Regex.Match(attrs, "id\\s*=\\s*['\"]?([^'\"\\s>]+)", RegexOptions.IgnoreCase).Groups[1].Value;
        t.Inner = parentId is not null;
        t.Fixed = Regex.IsMatch(attrs, "table-layout\\s*:\\s*fixed", RegexOptions.IgnoreCase);
        t.Bordered = t.Id == "tblTTO";
        t.PctWidth = t.Inner ? 0 : LpTablePct;
        t.Spacing = t.Inner && parentId != "tblPatient2" ? LpUaSpacing : 0;
        t.BlockThead = t.Inner && parentId == "tblGP2" && Regex.IsMatch(html, "<thead\\b", RegexOptions.IgnoreCase);
        LpParseRows(t, html[(open.Index + open.Length)..], parentId ?? t.Id);
        LpAssignColumns(t);
        return t;
    }

    private static void LpParseRows(LpTable t, string body, string ruleId)
    {
        List<LpCell>? row = null;
        LpCell? cell = null;
        var cellStart = 0;
        var nested = 0;
        foreach (Match m in LpTableTagRx.Matches(body))
        {
            var close = m.Groups[1].Value == "/";
            var tag = m.Groups[2].Value.ToLowerInvariant();
            if (tag == "table") { nested += close ? -1 : 1; if (nested < 0) break; continue; }
            if (nested > 0 || tag == "thead") continue;
            if (cell is not null) { LpFinishCell(t, cell, body[cellStart..m.Index], ruleId); cell = null; }
            if (tag == "tr")
            {
                if (!close) { row = new List<LpCell>(); t.Rows.Add(row); }
                continue;
            }
            if (close) continue;
            row ??= LpNewRow(t);
            cell = LpOpenCell(m.Groups[3].Value, tag == "th");
            row.Add(cell);
            cellStart = m.Index + m.Length;
        }
        if (cell is not null) LpFinishCell(t, cell, body[cellStart..], ruleId);
    }

    private static List<LpCell> LpNewRow(LpTable t)
    {
        var row = new List<LpCell>();
        t.Rows.Add(row);
        return row;
    }

    private static LpCell LpOpenCell(string attrs, bool th)
    {
        var c = new LpCell { Th = th, Bold = th };
        if (int.TryParse(Regex.Match(attrs, "colspan\\s*=\\s*['\"]?(\\d+)", RegexOptions.IgnoreCase).Groups[1].Value, out var cs)) c.ColSpan = Math.Max(1, cs);
        if (int.TryParse(Regex.Match(attrs, "rowspan\\s*=\\s*['\"]?(\\d+)", RegexOptions.IgnoreCase).Groups[1].Value, out var rs)) c.RowSpan = Math.Max(1, rs);
        var pct = Regex.Match(attrs, "(?:^|[\\s;'\"])width\\s*[=:]\\s*['\"]?\\s*(\\d+(?:\\.\\d+)?)\\s*%", RegexOptions.IgnoreCase);
        if (pct.Success) c.Pct = double.Parse(pct.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) / 100.0;
        c.NoWrap = Regex.IsMatch(attrs, "\\bnowrap\\b", RegexOptions.IgnoreCase);
        c.Top = Regex.IsMatch(attrs, "valign\\s*=\\s*['\"]?top|vertical-align\\s*:\\s*top", RegexOptions.IgnoreCase);
        c.Bold |= Regex.IsMatch(attrs, "font-weight\\s*:\\s*bold", RegexOptions.IgnoreCase);
        c.Left = Regex.IsMatch(attrs, "text-align\\s*:\\s*left", RegexOptions.IgnoreCase);
        c.PadLeftZero = Regex.IsMatch(attrs, "padding-left\\s*:\\s*0", RegexOptions.IgnoreCase);
        var em = Regex.Match(attrs, "font-size\\s*:\\s*(\\d+(?:\\.\\d+)?)\\s*em", RegexOptions.IgnoreCase);
        if (em.Success) c.FsScale = double.Parse(em.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        return c;
    }

    private static void LpFinishCell(LpTable t, LpCell c, string content, string ruleId)
    {
        var inner = Regex.Match(content, "<table\\b", RegexOptions.IgnoreCase);
        if (inner.Success)
        {
            var end = LpTableClose(content, inner.Index);
            c.Inner = LpParseTable(content[inner.Index..end], t.Depth + 2, ruleId);
        }
        else c.Segments = LpSegments(content);
        LpCellPads(t, c, ruleId);
    }

    /// <summary>The index just past the balanced close of the table opening at <paramref name="at"/>.</summary>
    private static int LpTableClose(string s, int at)
    {
        var depth = 0;
        foreach (Match m in Regex.Matches(s[at..], "<(/?)table\\b", RegexOptions.IgnoreCase))
        {
            depth += m.Groups[1].Value == "/" ? -1 : 1;
            if (depth == 0)
            {
                var close = s.IndexOf('>', at + m.Index);
                return close < 0 ? s.Length : close + 1;
            }
        }
        return s.Length;
    }

    /// <summary>The cell paddings the sheet gives this letter table's cells.</summary>
    private static void LpCellPads(LpTable t, LpCell c, string ruleId)
    {
        if (t.Bordered)
        {
            if (c.Th) { c.PadT = LpTtoThPadTop; c.PadB = LpTtoThPadBottom; }
            else c.PadT = c.PadR = c.PadB = c.PadL = LpTtoTdPad;
            return;
        }
        if (c.Th)
        {
            c.PadT = LpThPadTop;
            if (LpIsDemographics(ruleId)) c.PadR = LpThPadRight;
            else c.PadL = c.PadLeftZero ? 0 : LpThPadSide;
            return;
        }
        if (t.Inner) { c.PadT = c.PadR = c.PadB = LpUaCellPad; return; }
        c.PadL = LpIsDemographics(ruleId) ? 0 : LpTdPadLeft;
    }

    /// <summary>Place every cell on the column grid, reserving the slots a span covers.</summary>
    private static void LpAssignColumns(LpTable t)
    {
        var taken = new List<HashSet<int>>();
        for (var r = 0; r < t.Rows.Count; r++)
        {
            while (taken.Count <= r) taken.Add(new HashSet<int>());
            var col = 0;
            foreach (var c in t.Rows[r])
            {
                while (taken[r].Contains(col)) col++;
                c.Col = col;
                c.RowSpan = Math.Min(c.RowSpan, t.Rows.Count - r);
                for (var rr = r; rr < r + c.RowSpan; rr++)
                {
                    while (taken.Count <= rr) taken.Add(new HashSet<int>());
                    for (var cc = col; cc < col + c.ColSpan; cc++) taken[rr].Add(cc);
                }
                col += c.ColSpan;
                t.Cols = Math.Max(t.Cols, col);
            }
        }
    }

    private static double LpCellFs(LpTable t, LpCell c)
    {
        if (t.Bordered && !c.Th) return LpTtoTdFs;
        var fs = LpFsAt(t.Depth + (c.Th ? 0 : 1));
        return fs * c.FsScale;
    }

    /// <summary>The min-content and max-content widths of a cell's own text (its inset added).</summary>
    private static (double min, double max) LpCellExtents(LetterPreviewState lp, LpTable t, LpCell c, double availW)
    {
        c.Fs = LpCellFs(t, c);
        var inset = c.PadL + c.PadR;
        if (c.Inner is { } inner)
        {
            // a nested table asks for its columns' max-content and yields to their min-content
            LpSolveColumns(lp, inner, availW - inset);
            return (inner.MinW + inset, inner.MaxW + inset);
        }
        var face = c.Bold ? LpBoldFace : LpFace;
        double min = 0, max = 0;
        foreach (var seg in c.Segments)
        {
            max = Math.Max(max, LpMeasure(face, seg, c.Fs));
            foreach (var tok in Regex.Split(seg, "(?<=-)| "))
                min = Math.Max(min, LpMeasure(face, tok, c.Fs));
        }
        if (c.NoWrap) min = max;
        return (min + inset, max + inset);
    }

    /// <summary>Solve the column widths of a table laid in a box <paramref name="availW"/> wide.</summary>
    private static void LpSolveColumns(LetterPreviewState lp, LpTable t, double availW)
    {
        var n = t.Cols;
        var min = new double[n]; var max = new double[n]; var pct = new double?[n];
        foreach (var row in t.Rows)
            foreach (var c in row)
            {
                (c.MinW, c.MaxW) = LpCellExtents(lp, t, c, availW);
                if (c.ColSpan != 1) continue;
                min[c.Col] = Math.Max(min[c.Col], c.MinW);
                max[c.Col] = Math.Max(max[c.Col], c.MaxW);
                if (c.Pct is { } p && pct[c.Col] is null) pct[c.Col] = p;
            }
        LpSpreadSpans(t, min, max);
        var declared = t.PctWidth > 0 ? t.PctWidth * availW : 0;
        var gaps = (n + 1) * t.Spacing;
        t.MinW = gaps; t.MaxW = gaps;
        for (var i = 0; i < n; i++) { t.MinW += min[i]; t.MaxW += max[i]; }
        if (t.Fixed) { t.ColW = LpFixedColumns(declared - gaps, pct); t.W = declared; return; }
        var w = new double[n];
        var avail = declared > 0 ? declared - gaps : availW - gaps;
        for (var i = 0; i < n; i++) w[i] = pct[i] is { } p ? p * avail : max[i];
        double sum = 0;
        for (var i = 0; i < n; i++) sum += w[i];
        if (declared == 0 && sum <= avail) { t.ColW = w; t.W = sum + gaps; return; }
        if (sum > avail) LpShareDeficit(w, min, pct, sum - avail);
        else if (sum < avail) LpShareSurplus(w, pct, avail - sum);
        t.ColW = w;
        t.W = avail + gaps;
    }

    /// <summary>A spanning cell wider than the columns it covers widens them in proportion
    /// to their own widths (measured: the demographics heading spreads over its two columns
    /// 88.6 : 23.1); columns with nothing of their own share it equally.</summary>
    private static void LpSpreadSpans(LpTable t, double[] min, double[] max)
    {
        foreach (var row in t.Rows)
            foreach (var c in row)
            {
                if (c.ColSpan <= 1) continue;
                var to = Math.Min(t.Cols, c.Col + c.ColSpan);
                var gaps = (c.ColSpan - 1) * t.Spacing;
                LpSpread(max, c.Col, to, c.MaxW - gaps);
                LpSpread(min, c.Col, to, c.MinW - gaps);
            }
    }

    private static void LpSpread(double[] w, int from, int to, double need)
    {
        double sum = 0;
        for (var i = from; i < to; i++) sum += w[i];
        if (need <= sum || to <= from) return;
        var extra = need - sum;
        for (var i = from; i < to; i++) w[i] += sum > 0 ? extra * w[i] / sum : extra / (to - from);
    }

    private static double[] LpFixedColumns(double w, double?[] pct)
    {
        var cols = new double[pct.Length];
        double taken = 0; var autos = 0;
        for (var i = 0; i < pct.Length; i++)
            if (pct[i] is { } p) { cols[i] = p * (w - LpRuleW) + LpRuleW; taken += cols[i]; }
            else autos++;
        for (var i = 0; i < pct.Length; i++)
            if (pct[i] is null) cols[i] = (w - LpRuleW - taken) / Math.Max(1, autos);
        return cols;
    }

    /// <summary>A deficit is taken from the auto columns in proportion to their slack over
    /// min-content, then from the percent columns the same way.</summary>
    private static void LpShareDeficit(double[] w, double[] min, double?[] pct, double excess)
    {
        foreach (var autoPass in new[] { true, false })
        {
            double slack = 0;
            for (var i = 0; i < w.Length; i++)
                if ((pct[i] is null) == autoPass) slack += Math.Max(0, w[i] - min[i]);
            if (slack <= 0) continue;
            var take = Math.Min(excess, slack);
            for (var i = 0; i < w.Length; i++)
                if ((pct[i] is null) == autoPass) w[i] -= take * Math.Max(0, w[i] - min[i]) / slack;
            excess -= take;
            if (excess <= 1e-9) return;
        }
    }

    private static void LpShareSurplus(double[] w, double?[] pct, double surplus)
    {
        var anyAuto = false;
        foreach (var p in pct) anyAuto |= p is null;
        double basis = 0;
        for (var i = 0; i < w.Length; i++) if (!anyAuto || pct[i] is null) basis += w[i];
        if (basis <= 0) return;
        for (var i = 0; i < w.Length; i++) if (!anyAuto || pct[i] is null) w[i] += surplus * w[i] / basis;
    }

    private static double LpColSpanW(LpTable t, LpCell c)
    {
        double w = 0;
        for (var i = c.Col; i < c.Col + c.ColSpan && i < t.ColW.Length; i++) w += t.ColW[i];
        return w + (c.ColSpan - 1) * t.Spacing;
    }

    private static double LpColX(LpTable t, int col)
    {
        var x = t.X + t.Spacing;
        for (var i = 0; i < col; i++) x += t.ColW[i] + t.Spacing;
        return x;
    }

    /// <summary>Lay the table out at <paramref name="x"/> in a box <paramref name="availW"/> wide:
    /// columns, every cell's lines, the row heights.</summary>
    private static void LpLayoutTable(LetterPreviewState lp, LpTable t, double x, double availW)
    {
        LpSolveColumns(lp, t, availW);
        t.X = x;
        var rule = t.Bordered ? LpRuleW : 0;
        t.RowH = new double[t.Rows.Count];
        for (var r = 0; r < t.Rows.Count; r++)
            foreach (var c in t.Rows[r])
            {
                LpLayoutCell(lp, t, c);
                if (c.RowSpan == 1) t.RowH[r] = Math.Max(t.RowH[r], c.PadT + c.ContentH + c.PadB + rule);
            }
        for (var r = 0; r < t.Rows.Count; r++)
            foreach (var c in t.Rows[r])
                if (c.RowSpan > 1)
                {
                    double have = (c.RowSpan - 1) * t.Spacing;
                    for (var rr = r; rr < r + c.RowSpan; rr++) have += t.RowH[rr];
                    var need = c.PadT + c.ContentH + c.PadB + rule;
                    if (need > have) t.RowH[r + c.RowSpan - 1] += need - have;
                }
        t.Height = (t.Rows.Count + 1) * t.Spacing + rule + (t.BlockThead ? 2 * LpBlockTheadPad : 0);
        foreach (var h in t.RowH) t.Height += h;
    }

    private static void LpLayoutCell(LetterPreviewState lp, LpTable t, LpCell c)
    {
        var cw = LpColSpanW(t, c) - c.PadL - c.PadR - (t.Bordered ? LpRuleW : 0);
        if (c.Inner is { } inner)
        {
            LpLayoutTable(lp, inner, 0, cw);
            c.ContentH = inner.Height;
            return;
        }
        var runs = new List<LpRun>();
        c.Lines.Clear();
        c.ContentH = 0;
        foreach (var seg in c.Segments)
        {
            runs.Clear();
            runs.Add(LpMakeRun(seg, c.Bold, c.Fs));
            var lines = seg.Length == 0
                ? new List<LpLine> { LpCloseLine(lp, new List<LpRun>(), c.Fs) }
                : LpWrap(lp, runs, c.NoWrap ? double.MaxValue : cw, c.Fs, t.Bordered);
            c.Lines.AddRange(lines);
            c.ContentH += LpLinesH(lines);
        }
    }

    /// <summary>Draw a laid-out table with its top edge at <paramref name="topTd"/>.</summary>
    private static void LpDrawTable(LetterPreviewState lp, LpTable t, double topTd)
    {
        var rule = t.Bordered ? LpRuleW : 0;
        var y = topTd + t.Spacing;
        for (var r = 0; r < t.Rows.Count; r++)
        {
            if (t.BlockThead && r <= 1) y += LpBlockTheadPad;
            foreach (var c in t.Rows[r])
            {
                double span = (c.RowSpan - 1) * t.Spacing;
                for (var rr = r; rr < r + c.RowSpan; rr++) span += t.RowH[rr];
                var box = span - rule - c.PadT - c.PadB;
                var cy = y + rule + c.PadT + (c.Top ? 0 : Math.Max(0, (box - c.ContentH) / 2));
                // a block thead's row sits one spacing further in; a bordered cell's text
                // starts inside its rule, its centred text between the rule centres
                var cx = LpColX(t, c.Col) + c.PadL + (t.BlockThead && r == 0 ? t.Spacing : 0);
                var cwid = LpColSpanW(t, c) - c.PadL - c.PadR;
                if (t.Bordered && c.Left) { cx += rule; cwid -= rule; }
                else if (t.Bordered) cx += rule / 2;
                if (c.Inner is { } inner) { inner.X = cx; LpDrawTable(lp, inner, cy); }
                else LpDrawLines(lp, c.Lines, cx, cwid, cy, t.Bordered && !c.Left, false);
            }
            y += t.RowH[r] + t.Spacing;
        }
        if (t.Bordered) LpDrawGrid(lp, t, topTd);
    }

    private static void LpDrawGrid(LetterPreviewState lp, LpTable t, double topTd)
    {
        var x0 = t.X; var x1 = t.X + t.W;
        var y = topTd + LpRuleW / 2;
        LpHLine(lp, x0, x1, y);
        foreach (var h in t.RowH) { y += h; LpHLine(lp, x0, x1, y); }
        var yTop = topTd; var yBot = topTd + t.Height;
        var x = t.X + LpRuleW / 2;
        LpVLine(lp, x, yTop, yBot);
        foreach (var w in t.ColW) { x += w; LpVLine(lp, x, yTop, yBot); }
    }
}
