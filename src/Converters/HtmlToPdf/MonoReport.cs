using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// The fixed-width report dump: a <blockquote> of `<font face="courier new">`
// `<nobr>` lines followed by one `rules=groups` table whose every cell is
// another such line. Every glyph in the document is the same monospace
// advance, so the whole page is a character grid — column widths are character
// counts, and the sheet grows to the widest unbreakable line rather than
// wrapping it.
internal static partial class HtmlToPdfConverter
{
    private const double MonoFontPt = 9.75;              // <font size=2> = 13px
    private const double MonoLinePt = 11.25;             // Courier New's line box at that size
    private const double MonoBlockquoteIndentPt = 30.0;  // the UA 40px blockquote inset
    // The flow's entry: the leading empty paragraph and the blockquote's own
    // top margin seat the first drawn baseline this far below the top margin
    // (measured: 96.24 under a 72 pt margin).
    private const double MonoFirstBaselinePt = 24.24;
    // The table's top border opens this far below the last header baseline,
    // its first row is one border taller than the rest, and a cell's baseline
    // sits this far under its row's top edge (measured: 113.47 / 13.5 / 12.75
    // / 9.35 against a first row baseline of 122.82).
    private const double MonoTableGapPt = 5.98;
    private const double MonoFirstRowPt = 13.5;
    private const double MonoRowPt = 12.75;
    private const double MonoCellDropPt = 9.35;
    private const double MonoCellInsetPt = 1.5;          // border + cellpadding=1
    // A column's BOX is its widest cell plus both cellpaddings and the one
    // border its collapsed edge contributes (measured off the header row's
    // own background fills: 152.03 | 189.38 | 226.74 | 269.95 | 365.81 |
    // 660.61 | 674.56 | 700.22 | 720.02 | 804.18 | 870.79 | 954.96).
    private const double MonoCellBoxPadPt = 2.25;
    // The sheet ends this far past the widest line, then one page margin
    // (measured: content box 90..1006.64 around a widest line ending 1003.64).
    private const double MonoRightSlackPt = 3.0;
    private const double MonoRulePt = 0.75;   // the collapsed group rule

    /// <summary>Render a fixed-width monospace report dump, or null when the
    /// document is not one.</summary>
    private static Document? TryRenderMonoReport(string html, double pageWidth, double pageHeight)
    {
        var bqM = Regex.Match(html, @"<blockquote\b[^>]*>([\s\S]*)</blockquote\s*>",
            RegexOptions.IgnoreCase);
        if (!bqM.Success) return null;
        var body = bqM.Groups[1].Value;
        if (!Regex.IsMatch(body, @"<font\b[^>]*face\s*=\s*[""']?courier new", RegexOptions.IgnoreCase))
            return null;
        if (Regex.Matches(body, @"<nobr\b", RegexOptions.IgnoreCase).Count < 50) return null;
        const string face = "Courier New";
        if (WinMetricsFor(face) is null) return null;
        var adv = MeasureFaceText(face, "0", MonoFontPt);
        if (adv <= 0) return null;

        static string NobrText(string markup) =>
            DecodeEntities(Regex.Replace(markup, @"<[^>]+>", "")).Trim('\r', '\n', '\t');

        // ── the header lines, then the table's rows ───────────────────────
        var tIdx = body.IndexOf("<table", StringComparison.OrdinalIgnoreCase);
        var headMarkup = tIdx >= 0 ? body[..tIdx] : body;
        var header = new List<string>();
        foreach (Match nm in Regex.Matches(headMarkup, @"<nobr\b[^>]*>([\s\S]*?)</nobr\s*>",
                     RegexOptions.IgnoreCase))
            header.Add(NobrText(nm.Groups[1].Value));
        if (header.Count == 0) return null;

        var rows = new List<List<(string Text, Color? Bg)>>();
        if (tIdx >= 0)
            foreach (Match rm in Regex.Matches(body[tIdx..], @"<tr\b[^>]*>([\s\S]*?)</tr\s*>",
                         RegexOptions.IgnoreCase))
            {
                var cells = new List<(string Text, Color? Bg)>();
                foreach (Match cm in Regex.Matches(rm.Groups[1].Value,
                             @"<t[dh]([^>]*)>([\s\S]*?)</t[dh]\s*>", RegexOptions.IgnoreCase))
                {
                    // the cell's own background, however its unquoted style
                    // attribute happens to be spelled
                    var bgM = Regex.Match(cm.Groups[1].Value,
                        @"back\s*ground\s*:\s*(#[0-9a-fA-F]{3,6})", RegexOptions.IgnoreCase);
                    cells.Add((NobrText(cm.Groups[2].Value),
                        bgM.Success ? ParseCssColor(bgM.Groups[1].Value) : null));
                }
                if (cells.Count > 0) rows.Add(cells);
            }

        // ── the character grid ────────────────────────────────────────────
        var nCols = 0;
        foreach (var r in rows) nCols = Math.Max(nCols, r.Count);
        var colW = new double[nCols];
        foreach (var r in rows)
            for (var c = 0; c < r.Count; c++)
                colW[c] = Math.Max(colW[c], MeasureFaceText(face, r[c].Text, MonoFontPt));

        var contentLeft = ColMarginX + MonoBlockquoteIndentPt;
        var widest = 0.0;
        foreach (var h in header) widest = Math.Max(widest, MeasureFaceText(face, h, MonoFontPt));
        double gridW = 0;
        foreach (var w in colW) gridW += w + MonoCellBoxPadPt;
        widest = Math.Max(widest, gridW + MonoCellInsetPt);
        var neededPage = contentLeft + widest + MonoRightSlackPt + ColMarginTop + 18.0;
        if (neededPage > pageWidth) pageWidth = neededPage;

        var doc = new Document();
        var page = doc.Pages.Add(pageWidth, pageHeight);
        EnsureFonts(page);
        // the report's own face, as a WinAnsi Type1 resource on this page
        const string res = "F8";
        EnsureFont(page, face.Replace(" ", ""), res);

        // ── draw ──────────────────────────────────────────────────────────
        var invc = System.Globalization.CultureInfo.InvariantCulture;
        // The body's own background paints the CONTENT BOX — page margin to
        // page margin, top margin to bottom (measured: 90..1006.64 × 72..770).
        var bodyBgM = Regex.Match(html,
            @"<body\b[^>]*bgcolor\s*=\s*[""']?(#[0-9a-fA-F]{3,6})", RegexOptions.IgnoreCase);
        if (bodyBgM.Success && ParseCssColor(bodyBgM.Groups[1].Value) is { } bodyBg)
            page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
                $"q {bodyBg.R / 255.0:0.###} {bodyBg.G / 255.0:0.###} {bodyBg.B / 255.0:0.###} rg " +
                $"{ColMarginX:F2} {ColMarginTop:F2} {pageWidth - 2 * ColMarginX:F2} {pageHeight - 2 * ColMarginTop:F2} re f Q\n")));

        var y = ColMarginTop + MonoFirstBaselinePt;
        foreach (var h in header)
        {
            if (h.Trim().Length > 0)
                EmitPositionedRun(page, res, MonoFontPt, contentLeft, pageHeight - y, h);
            y += MonoLinePt;
        }
        var tableTop = y - MonoLinePt + MonoTableGapPt;
        // The first and last rows are their own groups under `rules=groups`:
        // each is one rule taller, and a rule opens the last one.
        var rowTop = tableTop;
        var lastRi = rows.Count - 1;
        for (var ri = 0; ri < rows.Count; ri++)
        {
            var rowH = ri == 0 || ri == lastRi ? MonoFirstRowPt : MonoRowPt;
            if (ri == lastRi && lastRi > 0) rowTop += MonoRulePt;
            var boxLeft = contentLeft;
            for (var c = 0; c < rows[ri].Count && c < nCols; c++)
            {
                var boxW = colW[c] + MonoCellBoxPadPt;
                if (rows[ri][c].Bg is { } cbg)
                    page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
                        $"q {cbg.R / 255.0:0.###} {cbg.G / 255.0:0.###} {cbg.B / 255.0:0.###} rg " +
                        $"{boxLeft + MonoCellInsetPt:F2} {pageHeight - rowTop - rowH:F2} {boxW:F2} {rowH:F2} re f Q\n")));
                var txt = rows[ri][c].Text;
                if (txt.Trim().Length > 0)
                    EmitPositionedRun(page, res, MonoFontPt, boxLeft + MonoCellInsetPt,
                        pageHeight - (rowTop + MonoCellDropPt), txt);
                boxLeft += boxW;
            }
            rowTop += rowH;
        }
        // `rules=groups` draws a rule between row GROUPS only — here the frame's
        // own top and bottom plus the two that fence the first and last rows.
        var sb = new StringBuilder("q 0 0 0 RG 0.75 w ");
        void Rule(double yTop) => sb.Append(string.Create(invc,
            $"{contentLeft:F2} {pageHeight - yTop:F2} m {contentLeft + gridW + MonoCellInsetPt:F2} {pageHeight - yTop:F2} l S "));
        Rule(tableTop);
        Rule(tableTop + MonoFirstRowPt);
        Rule(rowTop - MonoFirstRowPt);
        Rule(rowTop);
        sb.Append("Q\n");
        page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
        return doc;
    }
}
