using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Flow blocks: a single-column wrapper table holding a form control - a page's sections laid out
    /// one under another - flows each row's content as blocks of its own inside the wrapper's padding, its
    /// nested tables judged one by one and its own controls flowing with their text, instead of the whole
    /// wrapper flattened to one full-width line per cell because a control stands somewhere inside it.
    /// False when the table is not such a wrapper.</summary>
    private static bool UnwrapSectionWrapperTable(FlowBlocksState fbk, string seg)
    {
        if (!IsSectionWrapperTable(seg)) return false;
        var openTag = SafeRegex.Match(seg, @"<table\b[^>]*>", RegexOptions.IgnoreCase).Value;
        var box = new ElementBox(0);
        foreach (Match m in SafeRegex.Matches(BodyBoxDeclRx, DivStyleOf(openTag)))
            ApplyElementBoxDecl(box, m.Groups[1].Value, m.Groups[2].Value, DefaultBodyFontPt);
        // The wrapper's own padding and the UA cell chrome inset its rows on both sides (measured: the
        // sections of a 50px-padded wrapper open 37.5 + 2.25 inside the content box).
        fbk.list.Add(new Block
        {
            BoxStart = true, BoxPadTopPt = box.Padding[0] + UaCellChromePt,
            BoxPadSidePt = box.Padding[1] + box.Padding[3] + 2 * UaCellChromePt,
        });
        foreach (var (rowStyle, cellStyle, inner) in SectionWrapperRows(seg))
        {
            var before = fbk.list.Count;
            var style = (rowStyle + ";" + cellStyle).Trim(';');
            var rowOpen = style.Length > 0 ? "<div style=\"" + style.Replace("\"", "'") + "\">" : "<div>";
            fbk.list.AddRange(BuildFlowBlocks(fbk.cv, fbk.absSpanLedger, fbk.beforeMarkers, fbk.elementGridFace,
                fbk.htmlHasFormInput, fbk.inlineBlockColRules, fbk.perTableFormGate, fbk.rowBlocks, fbk.sheetChromesCells,
                rowOpen + inner + "</div>"));
            // A row that breaks the page before itself breaks it before its first block, a grid included.
            if (fbk.list.Count > before && SafeRegex.IsMatch(rowStyle, @"page-break-before\s*:\s*always", RegexOptions.IgnoreCase))
                fbk.list[before].PageBreakBefore = true;
        }
        fbk.list.Add(new Block { BoxEnd = true, BoxPadBottomPt = box.Padding[2] + UaCellChromePt });
        return true;
    }

    /// <summary>Whether a table is a section wrapper by its own markup, its nested tables set aside: every row
    /// one plain cell, no header cell, and no frame or fill on the wrapper or its cells.</summary>
    private static bool IsSectionWrapperTable(string seg)
    {
        var outer = OuterTableMarkup(seg);
        var open = SafeRegex.Match(outer, @"<table\b[^>]*>", RegexOptions.IgnoreCase);
        if (!open.Success) return false;
        if (SafeRegex.IsMatch(open.Value, @"\bborder\s*=\s*['""]?[1-9]|\b(cellspacing|cellpadding|bgcolor)\s*=", RegexOptions.IgnoreCase))
            return false;
        if (SafeRegex.IsMatch(outer, @"<th\b|\bbgcolor\s*=|background(-color)?\s*:", RegexOptions.IgnoreCase))
            return false;
        var anyRow = false;
        foreach (Match tr in SafeRegex.Matches(outer, @"<tr\b[^>]*>([\s\S]*?)</tr\s*>", RegexOptions.IgnoreCase))
        {
            if (SafeRegex.Matches(tr.Groups[1].Value, @"<td\b", RegexOptions.IgnoreCase).Count != 1) return false;
            anyRow = true;
        }
        return anyRow;
    }

    /// <summary>The rows of a wrapper table's OWN grid: each row's style, its cell's style and the cell's
    /// content, the tables nested in the cell left whole.</summary>
    private static IEnumerable<(string rowStyle, string cellStyle, string inner)> SectionWrapperRows(string seg)
    {
        var depth = 0;
        var rowStart = -1;
        var rowStyle = "";
        foreach (Match m in SafeRegex.Matches(seg, @"<(/?)(table|tr)\b[^>]*>", RegexOptions.IgnoreCase))
        {
            var close = m.Groups[1].Length > 0;
            if (m.Groups[2].Value.Equals("table", StringComparison.OrdinalIgnoreCase))
            {
                depth += close ? -1 : 1;
                continue;
            }
            if (depth != 1) continue;
            if (!close)
            {
                rowStart = m.Index + m.Length;
                rowStyle = DivStyleOf(m.Value);
                continue;
            }
            if (rowStart < 0) continue;
            var row = seg[rowStart..m.Index];
            rowStart = -1;
            var td = SafeRegex.Match(row, @"<td\b[^>]*>", RegexOptions.IgnoreCase);
            var tdClose = row.LastIndexOf("</td", StringComparison.OrdinalIgnoreCase);
            if (!td.Success || tdClose < td.Index + td.Length) continue;
            yield return (rowStyle, DivStyleOf(td.Value), row[(td.Index + td.Length)..tdClose]);
        }
    }
}
