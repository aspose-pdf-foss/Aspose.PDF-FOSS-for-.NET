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
        var mr = new MonoReportRenderState();
        mr.html = html;
        mr.pageWidth = pageWidth;
        mr.pageHeight = pageHeight;
        mr.bqM = Regex.Match(mr.html, @"<blockquote\b[^>]*>([\s\S]*)</blockquote\s*>",
            RegexOptions.IgnoreCase);
        if (!mr.bqM.Success) return null;
        mr.body = mr.bqM.Groups[1].Value;
        if (!Regex.IsMatch(mr.body, @"<font\b[^>]*face\s*=\s*[""']?courier new", RegexOptions.IgnoreCase))
            return null;
        if (Regex.Matches(mr.body, @"<nobr\b", RegexOptions.IgnoreCase).Count < 50) return null;
        mr.face = "Courier New";
        if (WinMetricsFor(mr.face) is null) return null;
        mr.adv = MeasureFaceText(mr.face, "0", MonoFontPt);
        if (mr.adv <= 0) return null;

        mr.tIdx = mr.body.IndexOf("<table", StringComparison.OrdinalIgnoreCase);
        mr.headMarkup = mr.tIdx >= 0 ? mr.body[..mr.tIdx] : mr.body;
        mr.header = new List<string>();
        foreach (Match nm in Regex.Matches(mr.headMarkup, @"<nobr\b[^>]*>([\s\S]*?)</nobr\s*>",
                     RegexOptions.IgnoreCase))
            mr.header.Add(MonoNobrText(nm.Groups[1].Value));
        if (mr.header.Count == 0) return null;

        mr.rows = new List<List<(string Text, Color? Bg)>>();
        if (mr.tIdx >= 0)
            foreach (Match rm in Regex.Matches(mr.body[mr.tIdx..], @"<tr\b[^>]*>([\s\S]*?)</tr\s*>",
                         RegexOptions.IgnoreCase))
            {
                if (!ReadMonoReportHeadRow(mr, rm)) break;
            }

        mr.nCols = 0;
        foreach (var r in mr.rows) mr.nCols = Math.Max(mr.nCols, r.Count);
        mr.colW = new double[mr.nCols];
        foreach (var r in mr.rows)
            for (var c = 0; c < r.Count; c++)
                mr.colW[c] = Math.Max(mr.colW[c], MeasureFaceText(mr.face, r[c].Text, MonoFontPt));

        mr.contentLeft = ColMarginX + MonoBlockquoteIndentPt;
        mr.widest = 0.0;
        foreach (var h in mr.header) mr.widest = Math.Max(mr.widest, MeasureFaceText(mr.face, h, MonoFontPt));
        mr.gridW = 0;
        foreach (var w in mr.colW) mr.gridW += w + MonoCellBoxPadPt;
        mr.widest = Math.Max(mr.widest, mr.gridW + MonoCellInsetPt);
        mr.neededPage = mr.contentLeft + mr.widest + MonoRightSlackPt + ColMarginTop + 18.0;
        if (mr.neededPage > mr.pageWidth) mr.pageWidth = mr.neededPage;

        mr.doc = new Document();
        mr.page = mr.doc.Pages.Add(mr.pageWidth, mr.pageHeight);
        EnsureFonts(mr.page);
        mr.res = "F8";
        EnsureFont(mr.page, mr.face.Replace(" ", ""), mr.res);

        mr.invc = System.Globalization.CultureInfo.InvariantCulture;
        mr.bodyBgM = Regex.Match(mr.html,
            @"<body\b[^>]*bgcolor\s*=\s*[""']?(#[0-9a-fA-F]{3,6})", RegexOptions.IgnoreCase);
        if (mr.bodyBgM.Success && ParseCssColor(mr.bodyBgM.Groups[1].Value) is { } bodyBg)
            mr.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(mr.invc,
                $"q {bodyBg.R / 255.0:0.###} {bodyBg.G / 255.0:0.###} {bodyBg.B / 255.0:0.###} rg " +
                $"{ColMarginX:F2} {ColMarginTop:F2} {mr.pageWidth - 2 * ColMarginX:F2} {mr.pageHeight - 2 * ColMarginTop:F2} re f Q\n")));

        mr.y = ColMarginTop + MonoFirstBaselinePt;
        foreach (var h in mr.header)
        {
            if (h.Trim().Length > 0)
                EmitPositionedRun(mr.page, mr.res, MonoFontPt, mr.contentLeft, mr.pageHeight - mr.y, h);
            mr.y += MonoLinePt;
        }
        mr.tableTop = mr.y - MonoLinePt + MonoTableGapPt;
        mr.rowTop = mr.tableTop;
        mr.lastRi = mr.rows.Count - 1;
        for (var ri = 0; ri < mr.rows.Count; ri++)
        {
            if (!RenderMonoReportRow(mr, ri)) break;
        }
        mr.sb = new StringBuilder("q 0 0 0 RG 0.75 w ");
        void MonoRule(double yTop) => mr.sb.Append(Compat.Format(mr.invc,
            $"{mr.contentLeft:F2} {mr.pageHeight - yTop:F2} m {mr.contentLeft + mr.gridW + MonoCellInsetPt:F2} {mr.pageHeight - yTop:F2} l S "));
        MonoRule(mr.tableTop);
        MonoRule(mr.tableTop + MonoFirstRowPt);
        MonoRule(mr.rowTop - MonoFirstRowPt);
        MonoRule(mr.rowTop);
        mr.sb.Append("Q\n");
        mr.page.AddContentStream(Encoding.ASCII.GetBytes(mr.sb.ToString()));
        return mr.doc;
    }
}
