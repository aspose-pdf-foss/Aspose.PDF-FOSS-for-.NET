using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Table text token: preformatted content split on its source newlines.</summary>
    private static bool AppendPreformattedText(TableStyleConfig cfg, TableParseState ps, TableColumnModel colModel, Token tok)
    {
        // <pre> content: split on the SOURCE newlines — each piece is a
        // hard line; the longest line's advance is recorded as the
        // cell's unbreakable width.
        // (a pre the sheet lets WRAP - `pre { white-space: pre-wrap }` - is ordinary text to the
        //  width probe: its lines break at their spaces, so no line of it is an unbreakable width -
        //  MEASURED, the change-control page: its 10,000-character pre-wrap comments wrap at their
        //  cell and the sheet is the 11-column grid's min-content, not the comment's line)
        if (ps.preDepth > 0 && !SheetPreWraps(cfg)) 
        {
            var preText = DecodeEntities(tok.Value);
            var preFirst = true;
            foreach (var preLn in preText.Split('\n'))
            {
                if (!preFirst)
                {
                    colModel.preMaxLinePt = Math.Max(colModel.preMaxLinePt, MeasureLine(ps, cfg.options, cfg.cellFontSize, cfg.dwFormCells, cfg.fullWidthCjkMin, cfg.widenProbe, 
                        ps.line.ToString(),
                        pt: ps.lineFontPt > 0 ? ps.lineFontPt : ps.curFontPt,
                        fam: ps.lineFamily ?? ps.curFamily));
                    if (!ps.lineStyleSet)
                    { ps.lineFontPt = ps.curFontPt; ps.lineFamily = ps.curFamily; ps.lineStyleSet = true; }
                    PushLine(ps, cfg.redlineCells, cfg.dwFormCells, cfg.widenProbe, keepIfBlank: true);
                }
                preFirst = false;
                var prePiece = preLn.TrimEnd('\r');
                if (prePiece.Length == 0) continue;
                if (!ps.lineStyleSet)
                { ps.lineFontPt = ps.curFontPt; ps.lineFamily = ps.curFamily; ps.lineStyleSet = true; }
                if (!string.IsNullOrWhiteSpace(prePiece))
                { ps.lineHadText = true; ps.cellPendingBrBlank = false; }
                ps.line.Append(prePiece);
            }
            return true;
        }
        return false;
    }

    /// <summary>Whether the sheet's `pre` element rule (bare, or under a table) declares `white-space: pre-wrap`.</summary>
    private static bool SheetPreWraps(TableStyleConfig cfg)
    {
        foreach (var sheet in new[] { cfg.css, cfg.docCss })
        {
            if (sheet is null) continue;
            foreach (var key in new[] { "pre", "table pre", "td pre" })
                if (sheet.TryGetValue(key, out var rule) && rule.TryGetValue("white-space", out var ws)
                    && ws.Contains("pre-wrap", StringComparison.OrdinalIgnoreCase))
                    return true;
        }
        return false;
    }
}
