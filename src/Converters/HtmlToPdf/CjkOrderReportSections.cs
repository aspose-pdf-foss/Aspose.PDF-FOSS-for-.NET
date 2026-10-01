using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>CJK order report sections: the table scan, page-one info, activity tables, infrastructure table and the tail pages.</summary>
    private static void RenderTailPages(CjkOrderReportState co)
    {
        co.page = co.doc.Pages.Add(CjkPageW, co.pageHeight);
        EnsureFonts(co.page);
        co.yTd = 72.0;
        co.infraEnd = co.infraIdx >= 0 ? co.tables[co.infraIdx].start : 0;
        for (var ti = 0; ti < co.tables.Count; ti++)
        {
            if (co.tables[ti].start <= co.infraEnd || !Leaf(co, co.tables[ti].body)) continue;
            foreach (var row in Rows(co, co.tables[ti].body))
            {
                var line = string.Join("  ", row);
                if (line.Trim().Length == 0) continue;
                foreach (var ln in MeasuredWordWrapCjk(line, CjkContentR - CjkContentL, 10, co.MixedW, false))
                {
                    if (co.yTd + 13.5 > co.pageHeight - 72.0)
                    {
                        co.page = co.doc.Pages.Add(CjkPageW, co.pageHeight);
                        EnsureFonts(co.page);
                        co.yTd = 72.0;
                    }
                    Mixed(co, 10, CjkContentL, co.yTd, ln, false);
                    co.yTd += 13.5;
                }
            }
            co.yTd += 13.5;
        }
    }

    /// <summary></summary>
    private static void RenderInfraTable(CjkOrderReportState co)
    {
        co.routeM = Regex.Match(co.html, @">([⺀-鿿]{2,4}\s*\([^<)]{1,30}\))<");
        Mixed(co, 12.5, CjkContentL, 518.0, co.routeM.Success ? co.routeM.Groups[1].Value : "", true);
        co.infraHeadM = Regex.Match(co.html, @">([⺀-鿿]{6,12})<");
        Mixed(co, 12.5, CjkContentL, 543.4,
            "基础设施详细信息", true);
        _ = co.infraHeadM;
        co.infraIdx = co.tables.FindIndex(t => Leaf(co, t.body) && t.body.Contains("基础设施编号"));
        co.infraRows = co.infraIdx >= 0 ? Rows(co, co.tables[co.infraIdx].body) : new List<List<string>>();
        co.infraEdges = new[] { 554.5, 572.7, 590.0, 607.3, 624.5, 651.9, 669.2, 686.5 };
        for (var c = 0; c < 3; c++)
            VL(co, new[] { 96.0, 258.9, 502.4 }[c], co.infraEdges[0], co.infraEdges[^1]);
        foreach (var e in co.infraEdges) HL(co, 96.0, 502.4, e);
        for (var r = 0; r < co.infraRows.Count && r + 1 < co.infraEdges.Length; r++)
        {
            var cells = co.infraRows[r];
            var top = co.infraEdges[r];
            var h = co.infraEdges[r + 1] - top;
            for (var c = 0; c < cells.Count && c < 2; c++)
            {
                var cx = c == 0 ? 99.0 : 261.9;
                var cw = (c == 0 ? 258.9 - 96.0 : 502.4 - 258.9) - 6;
                var lines = MeasuredWordWrapCjk(cells[c], cw, 10,
                    co.MixedW, false);
                var y0 = top + (h - lines.Count * 13.5) / 2 + 1.0;
                for (var li = 0; li < lines.Count; li++)
                    Mixed(co, 10, cx, y0 + li * 13.5, lines[li], false);
            }
        }

    }

    /// <summary></summary>
    private static void RenderActivityTables(CjkOrderReportState co)
    {
        co.secM = Regex.Match(co.html, @">([⺀-鿿]{4,10})<[^>]*>?\s*<[^>]*>?\s*1-",
            RegexOptions.IgnoreCase);
        Mixed(co, 12.5, CjkContentL, 350.8, co.secM.Success ? co.secM.Groups[1].Value : "前道准备活动", true);
        co.actHeads = new List<string>();
        foreach (Match hm in Regex.Matches(co.html, @">\s*(\d-\s*[A-Za-z][^<]{2,40})<"))
            co.actHeads.Add(Flat(co, hm.Groups[1].Value));
        co.actIdx = new List<int>();
        for (var ti = 0; ti < co.tables.Count && co.actIdx.Count < 2; ti++)
            if (Leaf(co, co.tables[ti].body)
                && Regex.IsMatch(co.tables[ti].attrs, @"border\s*=\s*[""']?1")
                && co.tables[ti].attrs.Contains("text-bold", StringComparison.OrdinalIgnoreCase))
                co.actIdx.Add(ti);
        ActTable(co, co.actIdx.Count > 0 ? co.actIdx[0] : -1,
            co.actHeads.Count > 0 ? co.actHeads[0] : "1-", 364.3, 373.5, 402.8, 444.0);
        ActTable(co, co.actIdx.Count > 1 ? co.actIdx[1] : -1,
            co.actHeads.Count > 1 ? co.actHeads[1] : "2-", 447.5, 456.5, 488.6, 502.6);
    }

    /// <summary></summary>
    private static void RenderPageOneInfo(CjkOrderReportState co)
    {
        co.titleM = Regex.Match(co.html, @"<(?:div|p|span)\b[^>]*>(?<t>[^<]{0,20})</",
            RegexOptions.IgnoreCase);
        co.title = "产品订单";
        co.tM2 = Regex.Match(co.html, @">([⺀-鿿]{2,8})<");
        if (co.tM2.Success) co.title = co.tM2.Groups[1].Value;
        _ = co.titleM;
        for (var i = 0; i < co.title.Length && i < 8; i++)
            RunF(co, co.simsun!, "SimSun", 12.5, 99.0, 83.0 + i * 14.3, co.title[i].ToString());

        // the order-info box: measured row seats; content re-keyed by the CJK
        // labels from the box table's leaf cells (the markup nests the right
        // column pairs in a wrapper table)
        Box(co, CjkContentL, 141.1, CjkContentR, 334.6);
        co.rowSeats = new[] { 152.6, 185.7, 211.6, 237.5, 263.9, 290.8 };
        co.leftLabels = new[] { "服务", "产品", "订单号", "客户参考号", "订单数量", "公差" };
        co.rightRow = new Dictionary<string, int>
            { ["来源/单位"] = 0, ["目的地"] = 1, ["检测员"] = 2, ["请求开始"] = 5 };
        var infoIdx = co.tables.FindIndex(t => t.attrs.Contains("border=\"1\"")
            || t.attrs.Contains("border='1'") || Regex.IsMatch(t.attrs, @"border\s*=\s*1"));
        if (infoIdx >= 0)
        {
            var cellsInOrder = new List<string>();
            foreach (Match cM in Regex.Matches(co.tables[infoIdx].body,
                @"<t[dh]\b[^>]*>(?<c>(?:(?!<t[dh]\b|</t[dh]>|<table)[\s\S])*)</t[dh]>",
                RegexOptions.IgnoreCase))
            {
                var t = Flat(co, cM.Groups["c"].Value);
                if (t.Length > 0) cellsInOrder.Add(t);
            }
            string? curLabel = null;
            var rowVals = new Dictionary<(int row, bool right), List<string>>();
            var isRight = false;
            foreach (var t in cellsInOrder)
            {
                if (Array.IndexOf(co.leftLabels, t) >= 0) { curLabel = t; isRight = false; continue; }
                if (co.rightRow.ContainsKey(t)) { curLabel = t; isRight = true; continue; }
                if (curLabel is null) continue;
                var row = isRight ? co.rightRow[curLabel] : Array.IndexOf(co.leftLabels, curLabel);
                var key = (row, isRight);
                if (!rowVals.TryGetValue(key, out var list)) rowVals[key] = list = new List<string>();
                list.Add(t);
            }
            for (var r = 0; r < co.leftLabels.Length; r++)
            {
                Mixed(co, 10, 106.0, co.rowSeats[r] + 2.0, co.leftLabels[r], false);
                if (rowVals.TryGetValue((r, false), out var lv) && lv.Count > 0)
                    DrawInfoValue(co, 181.7, co.rowSeats[r], 110, string.Join(" ", lv));
            }
            foreach (var (lbl, r) in co.rightRow)
            {
                Mixed(co, 10, 295.0, co.rowSeats[r] + 2.0, lbl, false);
                if (rowVals.TryGetValue((r, true), out var rv) && rv.Count > 0)
                {
                    var extra = 0.0;
                    foreach (var ln in MeasuredWordWrap(rv[0], 145, "Arial", 12.5))
                    {
                        Mixed(co, 12.5, 355.0, co.rowSeats[r] - 2.0 + extra, ln, false);
                        extra += 16.7;
                    }
                    if (rv.Count > 1)
                        Mixed(co, 12.5, 466.7, co.rowSeats[r] - 2.0, rv[1], false);
                }
            }
        }
    }

    /// <summary></summary>
    private static void ScanTables(CjkOrderReportState co)
    {
        co.html = Regex.Replace(co.html, @"<style[^>]*>[\s\S]*?</style>", " ", RegexOptions.IgnoreCase);
        co.tables = new List<(int start, string attrs, string body)>();
        {
            var opens = new List<(int pos, int end, string attrs)>();
            foreach (Match tm in Regex.Matches(co.html, @"<table\b([^>]*)>", RegexOptions.IgnoreCase))
                opens.Add((tm.Index, tm.Index + tm.Length, tm.Groups[1].Value));
            foreach (var (pos, end, attrs) in opens)
            {
                var depth = 1;
                var close = co.html.Length;
                foreach (Match t in Regex.Matches(co.html[end..], @"<table\b|</table>",
                    RegexOptions.IgnoreCase))
                {
                    depth += t.Value.StartsWith("</") ? -1 : 1;
                    if (depth == 0) { close = end + t.Index; break; }
                }
                co.tables.Add((pos, attrs, co.html[end..close]));
            }
        }
    }
}
