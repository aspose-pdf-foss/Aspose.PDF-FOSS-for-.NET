using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// CJK order report drawing helpers: runs, mixed-script text, boxes, rules, table rows and the activity table.
    private static void RunF(CjkOrderReportState co, byte[] ttf, string name, double fs, double x, double glyphTopTd,
        string text, bool fakeBold = false)
    {
        if (text.Length == 0) return;
        if (co.page.Dict.Get("Resources") is not Core.PdfDictionary res
            || res.Get("Font") is not Core.PdfDictionary fd) return;
        var (rn, hex) = Text.Type0FontEmbedder.Embed(fd, ttf, name, text,
            stripSpacesInBaseFont: true);
        var baseTd = glyphTopTd + fs * 0.88;
        co.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(co.invc,
            $"BT 0 0 0 rg /{rn} {fs:0.##} Tf ")
            + (fakeBold ? Compat.Format(co.invc, $"2 Tr {fs * 0.03:0.###} w 0 0 0 RG ") : "")
            + Compat.Format(co.invc,
            $"1 0 0 1 {x:0.##} {co.pageHeight - baseTd:0.##} Tm ")
            + "<" + Compat.ToHexString(hex) + "> Tj "
            + (fakeBold ? "0 Tr " : "") + "ET\n"));
    }

    // mixed-script emit: CJK runs in SimSun, latin in Arial, at one baseline
    private static double Mixed(CjkOrderReportState co, double fs, double x, double glyphTopTd, string text, bool bold)
    {
        var i = 0;
        while (i < text.Length)
        {
            var cjk = text[i] >= 0x2E80;
            var j = i;
            while (j < text.Length && (text[j] >= 0x2E80) == cjk) j++;
            var seg = text[i..j];
            if (cjk)
            {
                RunF(co, co.simsun!, "SimSun", fs, x, glyphTopTd, seg, bold);
                x += co.MeasureF(co.simsun!, "SimSun", seg, fs);
            }
            else
            {
                RunF(co, bold ? co.arialBold! : co.arial!, bold ? "ArialBold" : "Arial",
                    fs, x, glyphTopTd, seg);
                x += co.MeasureF(bold ? co.arialBold! : co.arial!,
                    bold ? "ArialBold" : "Arial", seg, fs);
            }
            i = j;
        }
        return x;
    }

    private static void Box(CjkOrderReportState co, double x0, double topTd, double x1, double botTd)
        => co.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(co.invc,
            $"q 0 0 0 RG 0.75 w {x0:0.##} {co.pageHeight - botTd:0.##} {x1 - x0:0.##} {botTd - topTd:0.##} re S Q\n")));

    private static void HL(CjkOrderReportState co, double x0, double x1, double yTd)
        => co.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(co.invc,
            $"q 0 0 0 RG 0.75 w {x0:0.##} {co.pageHeight - yTd:0.##} m {x1:0.##} {co.pageHeight - yTd:0.##} l S Q\n")));

    private static void VL(CjkOrderReportState co, double x, double y0Td, double y1Td)
        => co.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(co.invc,
            $"q 0 0 0 RG 0.75 w {x:0.##} {co.pageHeight - y0Td:0.##} m {x:0.##} {co.pageHeight - y1Td:0.##} l S Q\n")));

    private static string Flat(CjkOrderReportState co, string frag)
        => Regex.Replace(DecodeEntities(Regex.Replace(frag, @"<[^>]+>", " ")), @"\s+", " ").Trim();

    private static bool Leaf(CjkOrderReportState co, string body) => !body.Contains("<table", StringComparison.OrdinalIgnoreCase);

    private static List<List<string>> Rows(CjkOrderReportState co, string t)
    {
        var rows = new List<List<string>>();
        foreach (Match trM in Regex.Matches(t, @"<tr\b[^>]*>(?<r>[\s\S]*?)</tr>",
            RegexOptions.IgnoreCase))
        {
            var cells = new List<string>();
            foreach (Match cM in Regex.Matches(trM.Groups["r"].Value,
                @"<t[dh]\b[^>]*>(?<c>[\s\S]*?)</t[dh]>", RegexOptions.IgnoreCase))
                cells.Add(Flat(co, cM.Groups["c"].Value));
            if (cells.Count > 0) rows.Add(cells);
        }
        return rows;
    }

    private static void DrawInfoValue(CjkOrderReportState co, double x, double seat, double wrapW, string val)
    {
        // a parenthesised tail sets at the 10pt sub-size — on the same line
        // for a short lead, on its own lines for the tolerance cell
        var pIdx = val.IndexOf('(');
        var lead = pIdx > 0 ? val[..pIdx].Trim() : val;
        var tail = pIdx > 0 ? val[pIdx..].Trim() : "";
        var extra = 0.0;
        foreach (var ln in MeasuredWordWrap(lead, wrapW, "Arial", 12.5))
        {
            Mixed(co, 12.5, x, seat - 2.0 + extra, ln, false);
            extra += 16.7;
        }
        if (tail.Length > 0)
        {
            if (lead.Length <= 8 && !tail.Contains("Less"))
                Mixed(co, 10, x + co.MixedW(12.5, lead, false) + 4, seat, tail, false);
            else
                foreach (var seg in Regex.Split(tail, @"(?<=\))\s+"))
                {
                    Mixed(co, 10, x, seat + extra - 4, seg, false);
                    extra += 13.0;
                }
        }
    }

    private static void ActTable(CjkOrderReportState co, int ti, string head, double headTop, double tableTop,
        double headerBot, double tableBot)
    {
        RunF(co, co.arialBold!, "ArialBold", 10, CjkContentL, headTop, head);
        HL(co, CjkContentL, CjkContentR, tableTop);
        HL(co, CjkContentL, CjkContentR, headerBot);
        HL(co, CjkContentL, CjkContentR, tableBot);
        for (var c = 0; c < CjkActCols.Length; c++)
            VL(co, CjkActCols[c], tableTop, tableBot);
        if (ti < 0 || ti >= co.tables.Count) return;
        var rows = Rows(co, co.tables[ti].body);
        if (rows.Count == 0) return;
        // header cells: bold CJK centred, wrapping per character
        for (var c = 0; c < rows[0].Count && c + 1 < CjkActCols.Length; c++)
        {
            var cw = CjkActCols[c + 1] - CjkActCols[c] - 6;
            var lines = MeasuredWordWrapCjk(rows[0][c], cw, 10, co.MixedW, true);
            var y0 = tableTop + (headerBot - tableTop - lines.Count * 13.5) / 2 + 1.2;
            for (var li = 0; li < lines.Count; li++)
            {
                var lw = co.MixedW(10, lines[li], true);
                Mixed(co, 10, CjkActCols[c] + 3 + (cw - lw) / 2, y0 + li * 13.5, lines[li], true);
            }
        }
        // one data row: bold centred values
        if (rows.Count > 1)
            for (var c = 0; c < rows[1].Count && c + 1 < CjkActCols.Length; c++)
            {
                var cw = CjkActCols[c + 1] - CjkActCols[c] - 6;
                var lines = MeasuredWordWrapCjk(rows[1][c], cw, 10, co.MixedW, true);
                var y0 = headerBot + (tableBot - headerBot - lines.Count * 13.5) / 2 + 1.2;
                for (var li = 0; li < lines.Count; li++)
                {
                    var lw = co.MixedW(10, lines[li], true);
                    Mixed(co, 10, CjkActCols[c] + 3 + (cw - lw) / 2, y0 + li * 13.5, lines[li], true);
                }
            }
    }
}
