using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// The rounded-corner report grid rendered CONTINUOUSLY (IsRenderToSinglePage):
// one table whose class rule declares a border radius over zero border-spacing.
// Its header phrases are wider than the sheet, so every column shrinks to its
// MIN-CONTENT — the longest single word — and the sheet then grows to the grid
// that produces, with the header cells wrapping and centring inside their band.
internal static partial class HtmlToPdfConverter
{
    private const double RgFontPt = 7.5;        // the `font-size: 10px !important` cells
    private const double RgLinePt = 8.25;       // its line box
    private const double RgDropPt = 6.7;        // baseline inside that box
    private const double RgCellPadPt = 3.75;    // the band a cell adds around its lines
    private const double RgColPadPt = 2.25;     // both cellpaddings + the shared border
    private const double RgOriginPt = 6.75;     // the UA body margin plus the frame's half border
    private const double RgPageMarginPt = 90.0; // the sheet's own margin, which the widen measures from

    /// <summary>Render the rounded-corner report grid, or null when the
    /// document is not one.</summary>
    private static Document? TryRenderRadiusGrid(string html, HtmlLoadOptions? options,
        IReadOnlyDictionary<string, Dictionary<string, string>> css,
        double pageWidth, double authoredHeightPt)
    {
        if (options?.IsRenderToSinglePage != true) return null;
        // the grid's own class rule: a radius over collapsed-away spacing
        string? gridCls = null;
        Color band = Color.FromArgb(209, 204, 204);
        foreach (var (sel, decls) in css)
            if (sel.StartsWith('.') && !sel.Contains(' ')
                && decls.ContainsKey("border-radius")
                && decls.TryGetValue("border-spacing", out var bs) && bs.Trim().StartsWith('0'))
            { gridCls = sel[1..]; break; }
        if (gridCls is null) return null;
        var tblM = Regex.Match(html,
            @"<table\b[^>]*class\s*=\s*[""'][^""']*\b" + Regex.Escape(gridCls)
            + @"\b[^""']*[""'][^>]*>([\s\S]*?)</table\s*>", RegexOptions.IgnoreCase);
        if (!tblM.Success) return null;
        const string face = "Arial";
        if (WinMetricsFor(face) is null) return null;

        // the header cells' own background, wherever the sheet declares it
        foreach (var (sel, decls) in css)
            if (decls.TryGetValue("background-color", out var bgv) || decls.TryGetValue("background", out bgv))
                if (sel.Contains(gridCls, StringComparison.OrdinalIgnoreCase) || sel.StartsWith('.'))
                    if (ParseCssColor(bgv) is { } bc) { band = bc; break; }

        // ── the grid ──────────────────────────────────────────────────────
        var rows = new List<List<(string Text, int Span, int RowSpan, bool Head)>>();
        foreach (Match rm in Regex.Matches(tblM.Groups[1].Value,
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
            if (cells.Count > 0) rows.Add(cells);
        }
        if (rows.Count < 2) return null;

        static int SpanOf(string attrs, string name)
        {
            var m = Regex.Match(attrs, name + @"\s*=\s*[""']?(\d+)", RegexOptions.IgnoreCase);
            return m.Success && int.TryParse(m.Groups[1].Value, out var n) && n > 1 ? n : 1;
        }

        var nCols = 0;
        foreach (var c in rows[0]) nCols += c.Span;
        if (nCols < 2) return null;

        // Resolve each cell's real column: a rowspan reserves its columns in
        // the rows below, so a later row's cells slot into what is left.
        var placed = new List<List<(string Text, int Col, int Span, int RowSpan, bool Head)>>();
        var occupied = new Dictionary<(int Row, int Col), bool>();
        for (var ri = 0; ri < rows.Count; ri++)
        {
            var line = new List<(string, int, int, int, bool)>();
            var ci = 0;
            foreach (var (txt, span, rspan, head) in rows[ri])
            {
                while (ci < nCols && occupied.ContainsKey((ri, ci))) ci++;
                if (ci >= nCols) break;
                line.Add((txt, ci, span, rspan, head));
                for (var rr = 0; rr < rspan; rr++)
                    for (var k = 0; k < span; k++)
                        occupied[(ri + rr, ci + k)] = true;
                ci += span;
            }
            placed.Add(line);
        }

        // A column takes its MIN-CONTENT — the widest single word any of its
        // own (unspanned) cells holds — plus the cell chrome.
        var boldFace = face + "-Bold";
        var colW = new double[nCols];
        foreach (var line in placed)
            foreach (var (txt, col, span, _, head) in line)
                if (span == 1 && col < nCols)
                    foreach (var w in txt.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                        colW[col] = Math.Max(colW[col],
                            MeasureFaceText(head ? boldFace : face, w, RgFontPt));
        double gridW = 0;
        foreach (var w in colW) gridW += w + RgColPadPt;

        var tableX = RgPageMarginPt + RgOriginPt;
        var gridRight = tableX + gridW;
        // The sheet ends one half-border past the grid's right EDGE, measured
        // from the page margin (measured: 90 + 536.19 + 0.75 = 626.94), and a
        // continuous render is exactly one content band tall.
        pageWidth = RgPageMarginPt + gridRight + 0.75;
        // a continuous render is exactly one content band of the AUTHORED
        // sheet tall (the model: 842 - 72 - 72 = 698)
        var pageHeight = authoredHeightPt - 2 * ColMarginTop;

        var doc = new Document();
        var page = doc.Pages.Add(pageWidth, pageHeight);
        EnsureFonts(page);
        const string res = "F8", resB = "F9";
        EnsureFont(page, face, res);
        EnsureFont(page, face + "-Bold", resB);
        var invc = System.Globalization.CultureInfo.InvariantCulture;
        void Fill(Color c, double x, double yTop, double w, double h) =>
            page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
                $"q {c.R / 255.0:0.###} {c.G / 255.0:0.###} {c.B / 255.0:0.###} rg " +
                $"{x:F2} {pageHeight - yTop - h:F2} {w:F2} {h:F2} re f Q\n")));

        // ── the header band, then the body ────────────────────────────────
        var tableTop = ColMarginTop + RgOriginPt;
        var headRows = 0;
        foreach (var r in rows) { if (r.Count > 0 && r[0].Head) headRows++; else break; }
        if (headRows == 0) headRows = 1;

        double Lines(string t, double w) =>
            Math.Max(1, MeasuredWordWrap(t, w, face, RgFontPt).Length);

        // the band is the tallest cell that spans every header row
        var bandH = 0.0;
        for (var ri = 0; ri < headRows; ri++)
            foreach (var (txt, col, span, rspan, _) in placed[ri])
            {
                double w = 0;
                for (var k = 0; k < span && col + k < nCols; k++) w += colW[col + k] + RgColPadPt;
                if (rspan >= headRows)
                    bandH = Math.Max(bandH, Lines(txt, w - RgColPadPt) * RgLinePt + RgCellPadPt);
            }
        var lastHeadH = headRows > 1 ? RgLinePt + RgCellPadPt - 1.5 : bandH;
        var firstHeadH = bandH - (headRows > 1 ? lastHeadH : 0);

        var rowTop = tableTop;
        for (var ri = 0; ri < rows.Count; ri++)
        {
            var head = ri < headRows;
            var rowH = head ? (ri == 0 ? firstHeadH : lastHeadH) : 2 * RgLinePt + RgCellPadPt;
            foreach (var (txt, col, span, rspan, isHead) in placed[ri])
            {
                double w = 0, cx = tableX;
                for (var k = 0; k < col; k++) cx += colW[k] + RgColPadPt;
                for (var k = 0; k < span && col + k < nCols; k++) w += colW[col + k] + RgColPadPt;
                var cellH = rspan > 1 ? bandH : rowH;
                if (isHead) Fill(band, cx, rowTop, w, cellH);
                var lines = txt.Length == 0
                    ? System.Array.Empty<string>()
                    : MeasuredWordWrap(txt, w - RgColPadPt, face, RgFontPt);
                var stack = lines.Length * RgLinePt;
                var ly = rowTop + (cellH - stack) / 2 + RgDropPt;
                foreach (var ln in lines)
                {
                    var lw = MeasureFaceText(isHead ? face + " Bold" : face, ln, RgFontPt);
                    var lx = isHead ? cx + (w - lw) / 2 : cx + RgColPadPt / 2;
                    EmitPositionedRun(page, isHead ? resB : res, RgFontPt, lx, pageHeight - ly, ln);
                    ly += RgLinePt;
                }
            }
            rowTop += rowH;
        }
        return doc;
    }
}
