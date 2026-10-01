using System.Text;
using System.Text.RegularExpressions;
using CellLineSpec = (string Text, double FontPt, string? Family, bool Keep, bool JoinNext, System.Collections.Generic.List<(string Text, string Url)>? Anchors, bool Bold, double MarginTopPt, double MarginLeftPt, Aspose.Pdf.Color? Color, bool Italic);

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The UA cell box's own chrome - its pending margin, the rules above and below, the box segments, the line specs and the check boxes - is emitted around the closing cell.</summary>
    private static void EmitCellBoxChrome(CloseCellState cc)
    {
        if (cc.uaCellBoxes && cc.ps.line.Length > 0 && !UaBlankLine(cc.ps.line)
            && (cc.ps.uaPendingMarginPt > 0 || Math.Max(cc.ps.rowRuleBelowPt, cc.ps.cellRuleBelowPt) > 0))
        {
            if (!cc.ps.lineStyleSet) { cc.ps.lineFontPt = cc.ps.curFontPt; cc.ps.lineFamily = cc.ps.curFamily; }
            PushLine(cc.ps, cc.redlineCells, cc.dwFormCells, cc.widenProbe);
        }
        if (cc.uaCellBoxes && (cc.ps.uaPendingMarginPt > 0 || Math.Max(cc.ps.rowRuleBelowPt, cc.ps.cellRuleBelowPt) > 0)
            && UaBlankLine(cc.ps.line)) cc.ps.line.Clear();
        if (cc.uaCellBoxes && cc.ps.uaPendingMarginPt > 0)
        {
            cc.ps.lineFontPt = UaMarginOnlyFontPt; cc.ps.lineFamily = cc.ps.curFamily; cc.ps.lineStyleSet = true;
            PushLine(cc.ps, cc.redlineCells, cc.dwFormCells, cc.widenProbe, keepIfBlank: true);
        }
        // …and the rule ABOVE the cell (its own border-top, where the previous row drew no wider rule)
        // is a band at its head: a line inserted before the cell's first.
        if (cc.uaCellBoxes && cc.ps.cellRuleAbovePt > 0)
        {
            if (cc.ps.line.Length > 0 && !UaBlankLine(cc.ps.line))
            {
                if (!cc.ps.lineStyleSet) { cc.ps.lineFontPt = cc.ps.curFontPt; cc.ps.lineFamily = cc.ps.curFamily; }
                PushLine(cc.ps, cc.redlineCells, cc.dwFormCells, cc.widenProbe);
            }
            else cc.ps.line.Clear();
            UaShiftLineIndexMaps(cc.ps, 1);
            cc.ps.lines.Insert(0, ("", UaMarginOnlyFontPt, cc.ps.curFamily, true, false, null, false, 0, 0, null, false));
            (cc.ps.uaRuleBands ??= new())[0] = (cc.ps.cellRuleAbovePt, cc.ps.cellRuleAboveColor ?? Color.Black);
            cc.ps.cellRuleAbovePt = 0; cc.ps.cellRuleAboveColor = null;
        }
        // The collapsed rule under the row - the tr's or this cell's bottom border, whichever is
        // wider - is a band at the cell's foot (measured: a 10px tr rule over a 5px td rule draws ONE
        // 7.5 stroke, and the next row starts below it).
        if (cc.uaCellBoxes && Math.Max(cc.ps.rowRuleBelowPt, cc.ps.cellRuleBelowPt) is > 0 and var ruleW)
        {
            cc.ps.lineFontPt = UaMarginOnlyFontPt; cc.ps.lineFamily = cc.ps.curFamily; cc.ps.lineStyleSet = true;
            (cc.ps.uaRuleBands ??= new())[cc.ps.lines.Count] = (ruleW,
                (cc.ps.cellRuleBelowPt >= cc.ps.rowRuleBelowPt ? cc.ps.cellRuleColor : cc.ps.rowRuleColor) ?? Color.Black);
            PushLine(cc.ps, cc.redlineCells, cc.dwFormCells, cc.widenProbe, keepIfBlank: true);
        }
        RecordCellWidthAndBlankLines(cc);
        RejoinUnstyledParagraphSplits(cc);
        cc.boxByLine = null;
        if (cc.ps.cellBoxSegs is { Count: > 0 })
        {
            LayOutCellBoxSegments(cc);
        }
        cc.tfLineIdx = -1;
        cc.cellHadNestedTable = false;
        var wmLineNo = 0;
        foreach (var spec in cc.ps.lines)
        {
            if (cc.ps.wordMailCells) PlaceWordMailPicturesBefore(cc, wmLineNo++);
            LayOutCellLine(cc, spec);
        }
        // Images that FOLLOWED text in the markup were deferred so the cell's
        // paragraph order matches the source ("label<br><img>" draws the label
        // line above the image box, not underneath its blit).
        // the UA grid's checkboxes join the cell as paragraphs, after its text lines
        if (cc.ps.cellCheckboxes is { Count: > 0 })
        {
            foreach (var cb in cc.ps.cellCheckboxes) cc.ps.cell!.Paragraphs.Add(cb);
            cc.ps.cellCheckboxes = null;
        }
    }
}
