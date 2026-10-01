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
    private static Document? TryRenderRadiusGrid(string html, HtmlLoadOptions? options, IReadOnlyDictionary<string, Dictionary<string, string>> css, double pageWidth, double authoredHeightPt)
    {
        var rq = new RadiusGridRenderState();
        rq.html = html;
        rq.options = options;
        rq.css = css;
        rq.pageWidth = pageWidth;
        rq.authoredHeightPt = authoredHeightPt;
        if (rq.options?.IsRenderToSinglePage != true) return null;
        rq.gridCls = null;
        rq.band = Color.FromArgb(209, 204, 204);
        foreach (var (sel, decls) in rq.css)
            if (sel.StartsWith('.') && !sel.Contains(' ')
                && decls.ContainsKey("border-radius")
                && decls.TryGetValue("border-spacing", out var bs) && bs.Trim().StartsWith('0'))
            { rq.gridCls = sel[1..]; break; }
        if (rq.gridCls is null) return null;
        rq.tblM = Regex.Match(rq.html,
            @"<table\b[^>]*class\s*=\s*[""'][^""']*\b" + Regex.Escape(rq.gridCls)
            + @"\b[^""']*[""'][^>]*>([\s\S]*?)</table\s*>", RegexOptions.IgnoreCase);
        if (!rq.tblM.Success) return null;
        rq.face = "Arial";
        if (WinMetricsFor(rq.face) is null) return null;

        // the header cells' own background, wherever the sheet declares it
        foreach (var (sel, decls) in rq.css)
            if (decls.TryGetValue("background-color", out var bgv) || decls.TryGetValue("background", out bgv))
                if (sel.Contains(rq.gridCls, StringComparison.OrdinalIgnoreCase) || sel.StartsWith('.'))
                    if (ParseCssColor(bgv) is { } bc) { rq.band = bc; break; }

        rq.rows = new List<List<(string Text, int Span, int RowSpan, bool Head)>>();
        ReadRadiusGridRows(rq);
        if (rq.rows.Count < 2) return null;

        rq.nCols = 0;
        foreach (var c in rq.rows[0]) rq.nCols += c.Span;
        if (rq.nCols < 2) return null;

        rq.placed = new List<List<(string Text, int Col, int Span, int RowSpan, bool Head)>>();
        rq.occupied = new Dictionary<(int Row, int Col), bool>();
        LayoutRadiusGridRows(rq);

        rq.boldFace = rq.face + "-Bold";
        rq.colW = new double[rq.nCols];
        foreach (var line in rq.placed)
            foreach (var (txt, col, span, _, head) in line)
                if (span == 1 && col < rq.nCols)
                    foreach (var w in txt.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                        rq.colW[col] = Math.Max(rq.colW[col],
                            MeasureFaceText(head ? rq.boldFace : rq.face, w, RgFontPt));
        rq.gridW = 0;
        foreach (var w in rq.colW) rq.gridW += w + RgColPadPt;

        rq.tableX = RgPageMarginPt + RgOriginPt;
        rq.gridRight = rq.tableX + rq.gridW;
        // The sheet ends one half-border past the grid's right EDGE, measured
        // from the page margin (measured: 90 + 536.19 + 0.75 = 626.94), and a
        // continuous render is exactly one content band tall.
        rq.pageWidth = RgPageMarginPt + rq.gridRight + 0.75;
        rq.pageHeight = rq.authoredHeightPt - 2 * ColMarginTop;

        rq.doc = new Document();
        rq.page = rq.doc.Pages.Add(rq.pageWidth, rq.pageHeight);
        EnsureFonts(rq.page);
        rq.res = "F8";
        rq.resB = "F9";
        EnsureFont(rq.page, rq.face, rq.res);
        EnsureFont(rq.page, rq.face + "-Bold", rq.resB);
        rq.invc = System.Globalization.CultureInfo.InvariantCulture;
        rq.tableTop = ColMarginTop + RgOriginPt;
        rq.headRows = 0;
        foreach (var r in rq.rows) { if (r.Count > 0 && r[0].Head) rq.headRows++; else break; }
        if (rq.headRows == 0) rq.headRows = 1;

        rq.bandH = 0.0;
        DrawRadiusGridHead(rq);
        rq.lastHeadH = rq.headRows > 1 ? RgLinePt + RgCellPadPt - 1.5 : rq.bandH;
        rq.firstHeadH = rq.bandH - (rq.headRows > 1 ? rq.lastHeadH : 0);

        rq.rowTop = rq.tableTop;
        DrawRadiusGridRows(rq);
        return rq.doc;
    }
}
