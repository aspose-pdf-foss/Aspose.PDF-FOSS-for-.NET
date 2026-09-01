using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// The CSS multi-column dialect (a container rule declaring `columns: N`): the
// reference flows the paragraphs down N equal columns — UA serif on the hhea
// line box, a 1.12 em paragraph margin (applied at a column's top when a
// paragraph STARTS there, not for a continuation), a one-em column gap — and
// balances them against a target of one Nth of the total flow height, with the
// CSS orphan rule (a paragraph will not open a column fragment with fewer than
// two of its lines) pushing a tight paragraph whole into the next column. The
// LAST column takes the remainder past the target. All measured on the
// expected render.
internal static partial class HtmlToPdfConverter
{
    // UA paragraph margin as a fraction of the font size (the browser's 1.12 em
    // block rhythm — the inter-paragraph white band measures 30.24 at 27 pt).
    private const double ColParaMarginEm = 1.12;
    private const double ColMarginX = 96.0;
    private const double ColMarginTop = 72.0;

    /// <summary>Render a CSS multi-column document, or null when no container
    /// declares `columns: N`.</summary>
    private static Document? TryRenderCssColumns(string html,
        IReadOnlyDictionary<string, Dictionary<string, string>> css,
        double pageWidth, double pageHeight)
    {
        // the columns rule and the class that carries it
        var nCols = 0;
        string? colClass = null;
        foreach (var (sel, decls) in css)
            if (sel.StartsWith('.') && decls.TryGetValue("columns", out var cv)
                && int.TryParse(cv.Trim(), out var n) && n is > 1 and <= 12)
            { nCols = n; colClass = sel[1..]; break; }
        if (nCols == 0 || colClass is null) return null;
        var contM = Regex.Match(html,
            @"class\s*=\s*[""']" + Regex.Escape(colClass) + @"[""'][^>]*>(?<body>[\s\S]*)</div>",
            RegexOptions.IgnoreCase);
        if (!contM.Success) return null;
        // the dialect models plain paragraph flow only
        if (Regex.IsMatch(contM.Groups["body"].Value, @"<(table|img|ul|ol|h\d)\b", RegexOptions.IgnoreCase))
            return null;
        if (WinMetricsFor("Times New Roman") is not { } fm) return null;

        var fs = 12.0;
        if (css.TryGetValue("body", out var bodyRule)
            && bodyRule.TryGetValue("font-size", out var bfsV)
            && TryParseLength(bfsV, out var bfs)) fs = bfs;
        var lineH = MetricLineHeight(fs, HheaLineSumFor("Times New Roman") ?? fm.sum);
        var drop = MetricBaselineDrop(fs, lineH, fm);
        var paraMargin = ColParaMarginEm * fs;
        var gap = fs;                              // column-gap: normal = 1 em

        var paras = new List<string[]>();
        var contentW = pageWidth - 2 * ColMarginX;
        var colW = (contentW - (nCols - 1) * gap) / nCols;
        foreach (Match pm in Regex.Matches(contM.Groups["body"].Value,
            @"<p\b[^>]*>(?<c>[\s\S]*?)</p>", RegexOptions.IgnoreCase))
        {
            var text = Regex.Replace(DecodeEntities(
                Regex.Replace(pm.Groups["c"].Value, @"<[^>]+>", " ")), @"\s+", " ").Trim();
            if (text.Length > 0)
                paras.Add(MeasuredWordWrap(text, colW, "Times New Roman", fs));
        }
        if (paras.Count == 0) return null;

        // the balance target: one Nth of the total flow height
        double total = 0;
        foreach (var p in paras) total += paraMargin + p.Length * lineH;
        var target = total / nCols;

        var doc = new Document();
        var page = doc.Pages.Add(pageWidth, pageHeight);
        EnsureFonts(page);

        var col = 0;
        var y = 0.0;
        void EmitLine(string text)
        {
            var x = ColMarginX + col * (colW + gap);
            EmitPositionedRun(page, "F5", fs, x,
                pageHeight - (ColMarginTop + y + drop), text);
            y += lineH;
        }
        void NextColumn()
        {
            if (col < nCols - 1) { col++; y = 0; }
        }
        for (var pi = 0; pi < paras.Count; pi++)
        {
            var lines = paras[pi];
            // the orphan rule: a paragraph needs its margin plus two lines to
            // open in this column; a LAST column takes anything
            if (col < nCols - 1
                && y + paraMargin + Math.Min(2, lines.Length) * lineH > target + 0.1)
                NextColumn();
            y += paraMargin;
            foreach (var ln in lines)
            {
                if (col < nCols - 1 && y + lineH > target + 0.1) NextColumn();
                EmitLine(ln);
            }
        }
        return doc;
    }
}
