using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The stages of the radius-grid render: the span read, the fill, the line split, one data row's layout and one row's draw.
    private static int SpanOf(string attrs, string name)
    {
        var m = Regex.Match(attrs, name + @"\s*=\s*[""']?(\d+)", RegexOptions.IgnoreCase);
        return m.Success && int.TryParse(m.Groups[1].Value, out var n) && n > 1 ? n : 1;
    }

    private static void Fill(RadiusGridRenderState rq, Color c, double x, double yTop, double w, double h) =>
        rq.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(rq.invc,
            $"q {c.R / 255.0:0.###} {c.G / 255.0:0.###} {c.B / 255.0:0.###} rg " +
            $"{x:F2} {rq.pageHeight - yTop - h:F2} {w:F2} {h:F2} re f Q\n")));

    private static double Lines(RadiusGridRenderState rq, string t, double w) =>
        Math.Max(1, MeasuredWordWrap(t, w, rq.face, RgFontPt).Length);

    /// <summary></summary>
    private static void DrawRadiusGridRows(RadiusGridRenderState rq)
    {
        for (var ri = 0; ri < rq.rows.Count; ri++)
        {
            var head = ri < rq.headRows;
            var rowH = head ? (ri == 0 ? rq.firstHeadH : rq.lastHeadH) : 2 * RgLinePt + RgCellPadPt;
            foreach (var (txt, col, span, rspan, isHead) in rq.placed[ri])
            {
                double w = 0, cx = rq.tableX;
                for (var k = 0; k < col; k++) cx += rq.colW[k] + RgColPadPt;
                for (var k = 0; k < span && col + k < rq.nCols; k++) w += rq.colW[col + k] + RgColPadPt;
                var cellH = rspan > 1 ? rq.bandH : rowH;
                if (isHead) Fill(rq, rq.band, cx, rq.rowTop, w, cellH);
                var lines = txt.Length == 0
                    ? System.Array.Empty<string>()
                    : MeasuredWordWrap(txt, w - RgColPadPt, rq.face, RgFontPt);
                var stack = lines.Length * RgLinePt;
                var ly = rq.rowTop + (cellH - stack) / 2 + RgDropPt;
                foreach (var ln in lines)
                {
                    var lw = MeasureFaceText(isHead ? rq.face + " Bold" : rq.face, ln, RgFontPt);
                    var lx = isHead ? cx + (w - lw) / 2 : cx + RgColPadPt / 2;
                    EmitPositionedRun(rq.page, isHead ? rq.resB : rq.res, RgFontPt, lx, rq.pageHeight - ly, ln);
                    ly += RgLinePt;
                }
            }
            rq.rowTop += rowH;
        }
    }

    /// <summary></summary>
    private static void DrawRadiusGridHead(RadiusGridRenderState rq)
    {
        for (var ri = 0; ri < rq.headRows; ri++)
            foreach (var (txt, col, span, rspan, _) in rq.placed[ri])
            {
                double w = 0;
                for (var k = 0; k < span && col + k < rq.nCols; k++) w += rq.colW[col + k] + RgColPadPt;
                if (rspan >= rq.headRows)
                    rq.bandH = Math.Max(rq.bandH, Lines(rq, txt, w - RgColPadPt) * RgLinePt + RgCellPadPt);
            }
    }

    /// <summary></summary>
    private static void LayoutRadiusGridRows(RadiusGridRenderState rq)
    {
        for (var ri = 0; ri < rq.rows.Count; ri++)
        {
            var line = new List<(string, int, int, int, bool)>();
            var ci = 0;
            foreach (var (txt, span, rspan, head) in rq.rows[ri])
            {
                while (ci < rq.nCols && rq.occupied.ContainsKey((ri, ci))) ci++;
                if (ci >= rq.nCols) break;
                line.Add((txt, ci, span, rspan, head));
                for (var rr = 0; rr < rspan; rr++)
                    for (var k = 0; k < span; k++)
                        rq.occupied[(ri + rr, ci + k)] = true;
                ci += span;
            }
            rq.placed.Add(line);
        }
    }

    /// <summary></summary>
    private static void ReadRadiusGridRows(RadiusGridRenderState rq)
    {
        foreach (Match rm in Regex.Matches(rq.tblM.Groups[1].Value,
                     @"<tr\b[^>]*>([\s\S]*?)</tr\s*>", RegexOptions.IgnoreCase))
        {
            var cells = new List<(string, int, int, bool)>();
            foreach (Match cm in Regex.Matches(rm.Groups[1].Value,
                         @"<(t[dh])\b([^>]*)>([\s\S]*?)</t[dh]\s*>", RegexOptions.IgnoreCase))
            {
                var txt = CollapseWs(DecodeEntities(
                    Regex.Replace(cm.Groups[3].Value, @"<[^>]+>", " "))).Trim();
                cells.Add((txt, SpanOf(cm.Groups[2].Value, "colspan"),
                    SpanOf(cm.Groups[2].Value, "rowspan"),
                    cm.Groups[1].Value.Equals("th", StringComparison.OrdinalIgnoreCase)));
            }
            if (cells.Count > 0) rq.rows.Add(cells);
        }
    }
}
