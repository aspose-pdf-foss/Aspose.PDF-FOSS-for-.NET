using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    // ── the PROCEDURE page of the step-row worksheet (the page that carries the header preamble):
    // bordered px-cell tables, inline fill-in widgets, an equation figure, a checkbox column and
    // an acknowledge column per step. Every constant below is measured on the procedure page.
    private const double SrProcSeatPt = 1.85;          // a run's glyph top stands 1.85 under the top it is given
    private const double SrProcFrameInsetPt = 0.7;     // a table's frame opens 0.7 into the content column
    private const double SrProcCellEdgePt = 0.4;       // the first cell and the row rules inset from the frame
    private const double SrProcCellGapPt = 1.5;        // cellspacing 1 + the two cell borders
    private const double SrProcRowGapPt = 0.8;         // one row's rect to the next
    private const double SrProcRowPadPt = 1.5;         // a row is its tallest cell block + the cell pads
    private const double SrProcCellTextInsetPt = 2.3;  // text inset from the cell rect
    private const double SrProcLabelIndentPt = 6.0;    // a widget label's continuation line indents
    private const double SrProcCellLinePt = 12.0;      // the 16 px in-cell line
    private const double SrProcParaGapPt = 13.5;       // a closed paragraph's margin before more content
    private const double SrProcCellSeatPt = -0.7;      // a cell line's glyph top over its box top
    private const double SrProcCellFsPt = 12.0;
    private const double SrProcPlainCellFsPt = 10.5;   // plain cell text sets at the form's 14 px
    private const double SrProcLabelGapPt = 6.0;       // the label run after a fill-in underline
    private const double SrProcAfterTablePt = 13.5;    // a paragraph under a table opens one line under its frame
    private const double SrProcWidgetLineLiftPt = 0.8; // …0.8 lower when it holds fill-in widgets
    private const double SrProcRowEndPt = 12.6;        // a step ends 12.6 under its last line's glyph top
    private const double SrProcUnitGapPt = 12.7;       // the space run between two inline widgets
    private const double SrProcAckLineX0Pt = 183.75;   // the acknowledge underline, from the right edge
    private const double SrProcAckLineX1Pt = 94.55;
    private const double SrProcAckLineDyPt = 13.9;     // …13.9 under the step top
    // (the equation widget as the template draws it: its figure file is missing and takes no box - the
    //  "=" opens the step's first line, the fill-in rule runs 10.5 under the step top, the step is 39 tall)
    private const double SrProcEqSignDxPt = 6.2;       // the "=" into the content column
    private const double SrProcEqLineDyPt = 0.2;       // the equation line's glyph top under the step top
    private const double SrProcEqRuleDyPt = 10.5;      // the fill-in rule under the step top
    private const double SrProcEqEndPt = 39.0;         // the equation step's height
    private const double SrProcCheckGlyphPt = 10.3;    // the ☐ glyph box
    private const double SrProcCheckSeatPt = 2.1;      // …2.1 over the line's glyph top
    private const double SrProcStepFreshTopPt = 6.45;  // a step moved whole to a fresh page opens 6.45 under the content top
    private const double SrProcDetableLabelPt = 13.5;  // a data-entry label line over its detable

    private static void RegisterProcedureFonts(Page page)
    {
        EnsureFont(page, "Arial", "FA");
        EnsureFont(page, "Arial-Bold", "FB");
        EnsureFont(page, "Arial-BoldItalic", "FBI");
    }

    private static bool IsProcedurePage(StepRowsState sw)
        => ExtractBalancedDivInner(sw.html, "header-center") is not null;

    private enum ProcKind { Table, Detable, Equation, Widgets, Para }

    private sealed class ProcItem
    {
        public ProcKind Kind;
        public string Html = "";
    }

    private static readonly Regex ProcOpenRx = new(
        "<table\\b[^>]*>|<p\\b[^>]*>|<span\\b[^>]*class\\s*=\\s*['\"]smart-widget-(?<k>equation|numeric|checkbox|text)\\b[^>]*>",
        RegexOptions.IgnoreCase);

    private static readonly Regex ProcLabelRx = new("class\\s*=\\s*['\"]sw[a-z]-label['\"][^>]*>([^<]*)<", RegexOptions.IgnoreCase);

    /// <summary>The content in source order: tables (a detable keeps its own renderer), equation widgets,
    /// runs of inline widgets, paragraphs. A widget span is taken to its balanced close.</summary>
    private static List<ProcItem> ProcedureItems(string content)
    {
        var items = new List<ProcItem>();
        var pos = 0;
        while (pos < content.Length)
        {
            var m = ProcOpenRx.Match(content, pos);
            if (!m.Success) break;
            var v = m.Value;
            string html;
            int end;
            if (v.StartsWith("<table", StringComparison.OrdinalIgnoreCase))
            {
                end = content.IndexOf("</table>", m.Index, StringComparison.OrdinalIgnoreCase);
                end = end < 0 ? content.Length : end + "</table>".Length;
                html = content[m.Index..end];
                items.Add(new ProcItem
                {
                    Kind = Regex.IsMatch(v, "class\\s*=\\s*['\"]swdt-table", RegexOptions.IgnoreCase) ? ProcKind.Detable : ProcKind.Table,
                    Html = html,
                });
            }
            else if (v.StartsWith("<p", StringComparison.OrdinalIgnoreCase))
            {
                end = content.IndexOf("</p>", m.Index, StringComparison.OrdinalIgnoreCase);
                end = end < 0 ? content.Length : end + "</p>".Length;
                html = content[m.Index..end];
                if (!Regex.IsMatch(v, "class\\s*=\\s*['\"]empty", RegexOptions.IgnoreCase))
                {
                    // a paragraph holding nothing but fill-in widgets is a widget line
                    var inner = html[(v.Length)..];
                    if (Regex.IsMatch(inner, "smart-widget-(numeric|checkbox|text)", RegexOptions.IgnoreCase)
                        && Regex.Replace(Regex.Replace(inner, "<span\\b[^>]*smart-widget[\\s\\S]*", ""), "<[^>]+>|\\s|&nbsp;", "").Length == 0)
                        AddWidgets(items, html);
                    else items.Add(new ProcItem { Kind = ProcKind.Para, Html = html });
                }
            }
            else
            {
                var (_, pastEnd) = ProcSpanInner(content, m.Index);
                end = pastEnd > m.Index ? pastEnd : m.Index + m.Length;
                html = content[m.Index..end];
                if (m.Groups["k"].Value.Equals("equation", StringComparison.OrdinalIgnoreCase))
                    items.Add(new ProcItem { Kind = ProcKind.Equation, Html = html });
                else AddWidgets(items, html);
            }
            pos = Math.Max(end, m.Index + 1);
        }
        return items;
    }

    /// <summary>The inner markup of the span opening at <paramref name="openIdx"/> and the index past its
    /// balanced close (nested spans honoured); (null, html.Length) without a close.</summary>
    private static (string? inner, int pastEnd) ProcSpanInner(string html, int openIdx)
    {
        var open = Regex.Match(html[openIdx..], @"^<span\b[^>]*>", RegexOptions.IgnoreCase);
        if (!open.Success) return (null, html.Length);
        var i = openIdx + open.Length;
        var depth = 1;
        foreach (Match t in Regex.Matches(html[i..], @"<(/?)span\b[^>]*>", RegexOptions.IgnoreCase))
        {
            if (t.Value.EndsWith("/>", StringComparison.Ordinal)) continue;
            depth += t.Groups[1].Value.Length > 0 ? -1 : 1;
            if (depth == 0) return (html.Substring(i, t.Index), i + t.Index + t.Length);
        }
        return (null, html.Length);
    }

    private static void AddWidgets(List<ProcItem> items, string html)
    {
        if (items.Count > 0 && items[^1].Kind == ProcKind.Widgets) items[^1].Html += html;
        else items.Add(new ProcItem { Kind = ProcKind.Widgets, Html = html });
    }

    /// <summary>A procedure step: its content's items in source order, then the acknowledge column; a step
    /// that cannot fit its page moves whole to a fresh one.</summary>
    private static void RenderProcedureStep(StepRowsState sw)
    {
        var items = ProcedureItems(sw.content);
        var h = ProcedureStepHeightPt(items);
        if (sw.rowTop + h > sw.limit && sw.rowTop > sw.contentTop + 0.1)
        {
            NewPage(sw);
            sw.rowTop = sw.contentTop + SrProcStepFreshTopPt;
        }
        if (sw.bullet.Length > 0) Run(sw, "FA", 12, sw.bulletX, sw.rowTop, sw.bullet);
        var top = sw.rowTop - SrProcSeatPt;             // the measured frame: glyph tops as the raster sees them
        var y = top;
        var first = true;
        foreach (var it in items)
        {
            switch (it.Kind)
            {
                case ProcKind.Table:
                    y = RenderProcedureTable(sw, it.Html, y);
                    break;
                case ProcKind.Detable:
                    var lblM = Regex.Match(sw.content, "class\\s*=\\s*['\"]swdt-label['\"][^>]*>(?<t>[^<]*)", RegexOptions.IgnoreCase);
                    var lbl = lblM.Success ? Flat(sw, lblM.Groups["t"].Value) : "";
                    if (lbl.Length > 0) Run(sw, "FA", 12, sw.contentX, y + SrProcSeatPt, lbl);
                    y = RenderDetable(sw, it.Html, y + SrProcSeatPt + SrProcDetableLabelPt, sw.limit) - SrProcSeatPt;
                    break;
                case ProcKind.Equation:
                    RenderProcedureEquation(sw, it.Html, top);
                    y = top + SrProcEqEndPt;
                    break;
                case ProcKind.Widgets:
                    y = first ? y : y + SrProcAfterTablePt + SrProcWidgetLineLiftPt;
                    y = RenderProcedureWidgetLines(sw, it.Html, y) + SrProcRowEndPt;
                    break;
                case ProcKind.Para:
                    y = first ? y : y + SrProcAfterTablePt;
                    y = RenderProcedureParagraph(sw, it.Html, y) + SrProcRowEndPt;
                    break;
            }
            first = false;
        }
        var rowEnd = Math.Max(y, top + SrLinePt);
        // the acknowledge column: its fill-in line 13.9 under the step top (its `border-right` is transparent
        // and draws nothing in the template)
        HLine(sw, sw.pageWidth - SrProcAckLineX0Pt, sw.pageWidth - SrProcAckLineX1Pt, top + SrProcAckLineDyPt);
        sw.yTd = rowEnd + SrProcSeatPt;
    }

    private static double ProcedureStepHeightPt(List<ProcItem> items)
    {
        double h = 0;
        var first = true;
        foreach (var it in items)
        {
            h += it.Kind switch
            {
                ProcKind.Table => ProcedureTableRows(it.Html, out _).heightPt + SrProcRowEndPt,
                ProcKind.Detable => SrProcDetableLabelPt + SrThRowHPt
                    + Math.Max(0, Regex.Matches(it.Html, "<tr\\b", RegexOptions.IgnoreCase).Count - 1) * SrDataRowHPt + SrProcRowEndPt,
                ProcKind.Equation => SrProcEqEndPt,
                _ => (first ? 0 : SrProcAfterTablePt) + SrProcCellLinePt + SrProcRowEndPt,
            };
            first = false;
        }
        return Math.Max(h, SrLinePt);
    }

    // ── the bordered px-cell table ──
    private sealed class ProcCell
    {
        public string Html = "";
        public List<(string text, string res, double fs, double dx)> Lines = new();
        public double DeclPt;
        public double MinPt;
        public double BlockPt;
    }

    private static (List<List<ProcCell>> rows, double heightPt) ProcedureTableRows(string tableHtml, out double[] colW)
    {
        var rows = new List<List<ProcCell>>();
        foreach (Match trM in Regex.Matches(tableHtml, "<tr\\b[^>]*>(?<r>[\\s\\S]*?)</tr\\s*>", RegexOptions.IgnoreCase))
        {
            var row = new List<ProcCell>();
            foreach (Match tdM in Regex.Matches(trM.Groups["r"].Value, "<td\\b(?<a>[^>]*)>(?<c>[\\s\\S]*?)</td\\s*>", RegexOptions.IgnoreCase))
            {
                var c = new ProcCell { Html = tdM.Groups["c"].Value };
                var wm = Regex.Match(tdM.Groups["a"].Value, "width\\s*:\\s*([0-9.]+)\\s*px", RegexOptions.IgnoreCase);
                c.DeclPt = wm.Success ? double.Parse(wm.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * 0.75 : 0;
                c.MinPt = ProcedureCellMinPt(c.Html);
                row.Add(c);
            }
            if (row.Count > 0) rows.Add(row);
        }
        var n = 0;
        foreach (var r in rows) n = Math.Max(n, r.Count);
        colW = new double[Math.Max(1, n)];
        var declared = new double[colW.Length];
        var mins = new double[colW.Length];
        foreach (var r in rows)
            for (var i = 0; i < r.Count && i < colW.Length; i++)
            {
                declared[i] = Math.Max(declared[i], r[i].DeclPt);
                mins[i] = Math.Max(mins[i], r[i].MinPt + 2 * SrProcCellTextInsetPt);
            }
        var tableTag = Regex.Match(tableHtml, "<table\\b[^>]*>", RegexOptions.IgnoreCase).Value;
        var tm = Regex.Match(tableTag, "(?<![-a-z])width\\s*:\\s*([0-9.]+)\\s*px", RegexOptions.IgnoreCase);
        if (tm.Success)
        {
            // the declared box: a cell narrower than its unbreakable content takes that content, the
            // declared cells share the rest in proportion (probed: 168 / 164 / 135 px in 475 px → 121.4 / 118.4 / 111.4)
            var inner = double.Parse(tm.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * 0.75
                - SrProcRowGapPt - SrProcCellGapPt * (colW.Length - 1);
            double fixedSum = 0, declSum = 0;
            for (var i = 0; i < colW.Length; i++)
            {
                if (mins[i] > declared[i]) { colW[i] = mins[i]; fixedSum += mins[i]; }
                else declSum += declared[i];
            }
            var rest = Math.Max(0, inner - fixedSum);
            for (var i = 0; i < colW.Length; i++)
                if (colW[i] <= 0) colW[i] = declSum > 0 ? rest * declared[i] / declSum : rest / colW.Length;
        }
        else
            for (var i = 0; i < colW.Length; i++) colW[i] = Math.Max(declared[i] + 2 * SrProcCellTextInsetPt - 0.1, mins[i]);
        double h = 0;
        foreach (var r in rows)
        {
            double block = SrProcCellLinePt;
            for (var i = 0; i < r.Count && i < colW.Length; i++)
            {
                ProcedureCellLines(r[i], colW[i] - 2 * SrProcCellTextInsetPt);
                block = Math.Max(block, r[i].BlockPt);
            }
            h += block + SrProcRowPadPt + SrProcRowGapPt;
        }
        return (rows, h);
    }

    /// <summary>The widest unbreakable token a cell holds, in its own face and size.</summary>
    private static double ProcedureCellMinPt(string cellHtml)
    {
        var plain = cellHtml.IndexOf("smart-widget", StringComparison.OrdinalIgnoreCase) < 0
            && !Regex.IsMatch(cellHtml, "<(em|strong|b|i)\\b", RegexOptions.IgnoreCase);
        var fs = plain ? SrProcPlainCellFsPt : SrProcCellFsPt;
        var text = DecodeEntities(Regex.Replace(cellHtml, "<[^>]+>", " "));
        double min = 0;
        foreach (var tok in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            min = Math.Max(min, MeasureFaceText("Arial", tok, fs));
        return min;
    }

    /// <summary>A cell's lines at its wrap width: a break is a blank slot, a fill-in widget its underline and
    /// its wrapped label, a closed paragraph a gap before more content, emphasised bold text sets bold-italic;
    /// plain text sets at 14 px.</summary>
    private static void ProcedureCellLines(ProcCell c, double wrapW)
    {
        c.Lines.Clear();
        c.BlockPt = 0;
        var s = Regex.Replace(c.Html, "<!--[\\s\\S]*?-->", "");
        var bold = 0;
        var italic = 0;
        var pendingGap = false;
        var plain = s.IndexOf("smart-widget", StringComparison.OrdinalIgnoreCase) < 0
            && !Regex.IsMatch(s, "<(em|strong|b|i)\\b", RegexOptions.IgnoreCase);
        var text = new System.Text.StringBuilder();
        wrapW = Math.Max(20, wrapW);
        void FlushText()
        {
            var t = Regex.Replace(DecodeEntities(text.ToString()), "\\s+", " ").Trim();
            text.Clear();
            if (t.Length == 0) return;
            var face = bold > 0 && italic > 0 ? "Arial-BoldItalic" : bold > 0 ? "Arial-Bold" : "Arial";
            var res = bold > 0 && italic > 0 ? "FBI" : bold > 0 ? "FB" : "FA";
            var fs = plain ? SrProcPlainCellFsPt : SrProcCellFsPt;
            if (pendingGap) { c.Lines.Add(("", "FA", 0, 0)); c.BlockPt += SrProcParaGapPt; pendingGap = false; }
            foreach (var ln in MeasuredWordWrap(t, wrapW, face, fs))
            {
                c.Lines.Add((ln.Trim(), res, fs, 0));
                c.BlockPt += SrProcCellLinePt;
            }
        }
        var i = 0;
        while (i < s.Length)
        {
            if (s[i] != '<')
            {
                var j = s.IndexOf('<', i);
                if (j < 0) j = s.Length;
                text.Append(s, i, j - i);
                i = j;
                continue;
            }
            var close = s.IndexOf('>', i);
            if (close < 0) break;
            var tag = s[i..(close + 1)];
            if (Regex.IsMatch(tag, "^<span\\b[^>]*smart-widget-text", RegexOptions.IgnoreCase))
            {
                var (inner, pastEnd) = ProcSpanInner(s, i);
                FlushText();
                if (pendingGap) { c.Lines.Add(("", "FA", 0, 0)); c.BlockPt += SrProcParaGapPt; pendingGap = false; }
                var under = Regex.Match(inner ?? "", "^\\s*(_+)").Groups[1].Value;
                if (under.Length == 0) under = "________________";
                c.Lines.Add((under, "FA", SrProcCellFsPt, 0));
                c.BlockPt += SrProcCellLinePt;
                var label = new System.Text.StringBuilder();
                foreach (Match lm in ProcLabelRx.Matches(inner ?? "")) label.Append(lm.Groups[1].Value);
                var lt = Regex.Replace(DecodeEntities(label.ToString()), "\\s+", " ").Trim();
                if (lt.Length > 0)
                    foreach (var ln in MeasuredWordWrap(lt, Math.Max(20, wrapW - SrProcLabelIndentPt), "Arial", SrProcCellFsPt))
                    {
                        c.Lines.Add((ln.Trim(), "FA", SrProcCellFsPt, SrProcLabelIndentPt));
                        c.BlockPt += SrProcCellLinePt;
                    }
                i = pastEnd > i ? pastEnd : close + 1;
                continue;
            }
            i = close + 1;
            if (tag.StartsWith("<br", StringComparison.OrdinalIgnoreCase))
            {
                FlushText();
                c.Lines.Add(("", "FA", SrProcCellFsPt, 0));
                c.BlockPt += SrProcCellLinePt;
            }
            else if (tag.StartsWith("</p", StringComparison.OrdinalIgnoreCase)) { FlushText(); pendingGap = c.Lines.Count > 0; }
            else if (Regex.IsMatch(tag, "^<(em|i)\\b", RegexOptions.IgnoreCase)) { FlushText(); italic++; }
            else if (Regex.IsMatch(tag, "^<(strong|b)\\b", RegexOptions.IgnoreCase)) { FlushText(); bold++; }
            else if (Regex.IsMatch(tag, "^</(em|i)\\b", RegexOptions.IgnoreCase)) { FlushText(); italic = Math.Max(0, italic - 1); }
            else if (Regex.IsMatch(tag, "^</(strong|b)\\b", RegexOptions.IgnoreCase)) { FlushText(); bold = Math.Max(0, bold - 1); }
            else if (tag.StartsWith("<p", StringComparison.OrdinalIgnoreCase)) FlushText();
        }
        FlushText();
        if (c.Lines.Count == 0) c.BlockPt = SrProcCellLinePt;   // an nbsp cell is one line
    }

    /// <summary>Draws the table at y (the measured frame): each row's cell rects and rules, every cell's line
    /// block centred in its row; returns the y under the table's frame.</summary>
    private static double RenderProcedureTable(StepRowsState sw, string tableHtml, double y)
    {
        var (rows, _) = ProcedureTableRows(tableHtml, out var colW);
        var x0 = sw.contentX + SrProcFrameInsetPt;
        var rowTop = y + SrProcCellEdgePt;
        foreach (var r in rows)
        {
            double block = SrProcCellLinePt;
            foreach (var c in r) block = Math.Max(block, c.BlockPt);
            var rowH = block + SrProcRowPadPt;
            var cx = x0 + SrProcCellEdgePt;
            for (var i = 0; i < r.Count && i < colW.Length; i++)
            {
                var c = r[i];
                // the cell rect (its rules 0.4 inside the row's top and bottom)
                HLine(sw, cx - SrProcCellEdgePt, cx + colW[i], rowTop + SrProcCellEdgePt);
                HLine(sw, cx - SrProcCellEdgePt, cx + colW[i], rowTop + rowH - SrProcCellEdgePt);
                VLine(sw, cx, rowTop, rowTop + rowH);
                VLine(sw, cx + colW[i], rowTop, rowTop + rowH);
                // the block centred in the row's content box
                var blockTop = rowTop + SrProcRowPadPt / 2 + (rowH - SrProcRowPadPt - c.BlockPt) / 2;
                var ly = blockTop;
                foreach (var (text, res, fs, dx) in c.Lines)
                {
                    if (text.Length > 0) Run(sw, res, fs, cx + SrProcCellTextInsetPt + dx, ly + SrProcCellSeatPt + SrProcSeatPt, text);
                    ly += fs > 0 ? SrProcCellLinePt : SrProcParaGapPt;   // (a zero-size slot is a paragraph gap)
                }
                cx += colW[i] + SrProcCellGapPt;
            }
            rowTop += rowH + SrProcRowGapPt;
        }
        return rowTop - SrProcRowGapPt + SrProcCellEdgePt;
    }

    /// <summary>Inline fill-in widgets flowing on the content column's lines: an underline and its label, or a
    /// checkbox and its label, each an unbreakable unit; returns the last line's glyph top.</summary>
    private static double RenderProcedureWidgetLines(StepRowsState sw, string html, double y)
    {
        var x = sw.contentX;
        var lineTop = y;
        var pos = 0;
        while (pos < html.Length)
        {
            var m = Regex.Match(html[pos..], "<span\\b[^>]*class\\s*=\\s*['\"]smart-widget-(?<k>numeric|checkbox|text)\\b[^>]*>", RegexOptions.IgnoreCase);
            if (!m.Success) break;
            var open = pos + m.Index;
            var (inner, pastEnd) = ProcSpanInner(html, open);
            pos = pastEnd > open ? pastEnd : open + m.Length;
            inner ??= "";
            var kind = m.Groups["k"].Value.ToLowerInvariant();
            var label = new System.Text.StringBuilder();
            foreach (Match lm in ProcLabelRx.Matches(inner)) label.Append(lm.Groups[1].Value);
            var lt = DecodeEntities(label.ToString()).TrimStart();
            double headW;
            string head;
            if (kind == "checkbox")
            {
                head = "";
                headW = SrProcCheckGlyphPt;
                lt = " " + lt;
            }
            else
            {
                head = Regex.Match(inner, "^\\s*(_+)").Groups[1].Value;
                if (head.Length == 0) head = "________________";
                headW = MeasureFaceText("Arial", head, 12) + SrProcLabelGapPt;
            }
            var labelW = MeasureFaceText("Arial", lt, 12);
            if (x > sw.contentX && x + headW + labelW > sw.contentX + SrContentWPt)
            {
                x = sw.contentX;
                lineTop += SrProcCellLinePt;
            }
            if (kind == "checkbox")
            {
                var gy = lineTop - SrProcCheckSeatPt;
                sw.sb.Append(Compat.Format(sw.invc,
                    $"q 0 0 0 RG 0.75 w {x + 0.4:0.##} {sw.pageHeight - gy - SrProcCheckGlyphPt + 0.4:0.##} {SrProcCheckGlyphPt - 0.8:0.##} {SrProcCheckGlyphPt - 0.8:0.##} re S Q\n"));
            }
            else Run(sw, "FA", 12, x, lineTop + SrProcSeatPt, head);
            Run(sw, "FA", 12, x + headW, lineTop + SrProcSeatPt, lt);
            // (the source whitespace between two widgets sets as a run of spaces after the label - measured 12.7)
            x += headW + labelW + SrProcUnitGapPt;
        }
        return lineTop;
    }

    private static double RenderProcedureParagraph(StepRowsState sw, string html, double y)
    {
        var bold = Regex.IsMatch(html, "<(strong|b)\\b", RegexOptions.IgnoreCase);
        var t = Flat(sw, html);
        var li = 0;
        if (t.Length > 0)
            foreach (var ln in MeasuredWordWrap(t, SrContentWPt, bold ? "Arial-Bold" : "Arial", 12))
                Run(sw, bold ? "FB" : "FA", 12, sw.contentX, y + li++ * SrProcCellLinePt + SrProcSeatPt, ln);
        return y + Math.Max(0, li - 1) * SrProcCellLinePt;
    }

    /// <summary>The equation widget: the figure's placeholder box (its file is not on this machine), the "=",
    /// the fill-in rule of the element's declared width, and the label.</summary>
    private static void RenderProcedureEquation(StepRowsState sw, string html, double top)
    {
        var x = sw.contentX;
        var labels = new List<string>();
        foreach (Match lm in Regex.Matches(html, "class\\s*=\\s*['\"]swe-label['\"][^>]*>([^<]*)<", RegexOptions.IgnoreCase))
        {
            var t = DecodeEntities(lm.Groups[1].Value).Trim();
            if (t.Length > 0) labels.Add(t);
        }
        var em = Regex.Match(html, "class\\s*=\\s*['\"]swe-element['\"][^>]*width\\s*:\\s*([0-9.]+)\\s*px", RegexOptions.IgnoreCase);
        var ruleW = em.Success ? double.Parse(em.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * 0.75 : 108.75;
        var lx = x + SrProcEqSignDxPt;
        if (labels.Count > 0)
        {
            Run(sw, "FA", 12, lx, top + SrProcEqLineDyPt + SrProcSeatPt, labels[0]);
            lx += MeasureFaceText("Arial", labels[0], 12) + SrProcLabelGapPt;
        }
        HLine(sw, lx, lx + ruleW, top + SrProcEqRuleDyPt);
        lx += ruleW + SrProcLabelGapPt;
        if (labels.Count > 1) Run(sw, "FA", 12, lx, top + SrProcEqLineDyPt + SrProcSeatPt, labels[1]);
    }
}
