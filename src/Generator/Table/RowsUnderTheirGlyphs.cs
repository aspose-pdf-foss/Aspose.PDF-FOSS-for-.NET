namespace Aspose.Pdf;

public partial class Table
{
    /// <summary>In a collapsed grid (<see cref="IsBordersCollapsed"/>), a row the page cannot
    /// hold whole stays on it when its lines fit down to the glyphs of its last line -- the
    /// half leading a declared line box keeps under them left out -- with its bottom padding
    /// and the grid's closing rule under them; its box then closes at the page's bottom and
    /// what of it would have fallen past is cut. A row fits whole only with that rule inside
    /// the page. Off (the default), such a row moves to the next page. Only lines that
    /// declare their line box (<see cref="Text.TextState.LineBoxAscentEm"/> and its pair)
    /// have leading under their glyphs. On, a row that may split (<see cref="Row.IsRowBroken"/>)
    /// keeps here as many lines as fit down to their glyphs in the same way - fewer when the
    /// line controls of its tallest cell's paragraph ask it
    /// (<see cref="Text.TextFormattingOptions.MinOrphanLines"/>,
    /// <see cref="Text.TextFormattingOptions.MinWidowLines"/>) - and the part left here closes
    /// under the glyphs of its last line, its padding and rule under them.</summary>
    public bool RowsCloseUnderTheirGlyphs { get; set; }

    /// <summary>When <see cref="RowsCloseUnderTheirGlyphs"/> is on, for a row that does not
    /// fit the page whole with the grid's closing rule under it: its remaining lines take
    /// their place here if all of them fit once the leading under the last line's glyphs
    /// is left out, and the row is marked to close at the page's bottom.</summary>
    private void FitRowUnderItsGlyphs(MultiPageBuildState mp)
    {
        mp.closesAtBottom = false;
        mp.closesUnderGlyphs = false;
        if (mp.breakAfterSlice)
        {
            mp.breakAfterSlice = false;
            mp.linesFit = 0;
            return;
        }
        if (!RowsCloseUnderTheirGlyphs || !IsBordersCollapsed || mp.plan.LineCount == 0 || mp.plan.LineHeight <= 0) return;
        var remaining = mp.plan.LineCount - mp.lineIdx;
        if (remaining <= 0) return;
        var whole = mp.lineIdx == 0 && mp.plan.ExactTotalH > 0
            ? mp.plan.ExactTotalH
            : remaining * mp.plan.LineHeight + mp.plan.VertPadding;
        // Room from the rule the row opens under down to the page's bottom, less the grid's
        // closing rule, which a row closing on the page must hold whole.
        var room = mp.currentY - mp.pageBottom - CollapsedGridEdgeHeight();
        var underGlyphs = LeadingUnderLastGlyphs(mp.plan);
        if (underGlyphs <= 0 || whole <= room + 1e-6) return;
        if (!mp.atFreshPage && whole - underGlyphs <= room + 1e-6)
        {
            mp.linesFit = remaining;
            mp.closesAtBottom = true;
            return;
        }
        if (!mp.plan.Row.IsRowBroken) return;
        var fit = (int)Math.Floor((room - mp.plan.VertPadding + underGlyphs) / mp.plan.LineHeight + 1e-6);
        fit = Math.Max(0, Math.Min(fit, remaining));
        if (Text.LineControls.Any(ControllingParagraph(mp.plan)?.TextState.FormattingOptions))
        {
            var kept = Text.LineControls.Kept(ControllingParagraph(mp.plan)!.TextState.FormattingOptions, fit, remaining,
                mp.lineIdx == 0, !mp.atFreshPage);
            mp.breakAfterSlice = kept > 0 && kept < fit;
            fit = kept;
        }
        if (fit == 0 && mp.atFreshPage) fit = 1;
        mp.linesFit = fit;
        mp.closesUnderGlyphs = fit > 0;
    }

    /// <summary>The paragraph whose line controls decide where a row breaks: the one paragraph of
    /// the cell holding the row's lines; null when that cell holds anything else.</summary>
    private static Text.TextFragment? ControllingParagraph(RowPlan plan)
    {
        for (var c = 0; c < plan.CellLines.Count && c < plan.Row.Cells.Count; c++)
            if (plan.CellLines[c].Count == plan.LineCount)
                return plan.Row.Cells[c].Paragraphs is { Count: 1 } paragraphs ? paragraphs[0] as Text.TextFragment : null;
        return null;
    }

    /// <summary>The leading under the glyphs of the row's last line: of the cells whose
    /// lines reach the row's last line, the least half surplus of a declared line box
    /// (the pitch less the box's height, halved); none when such a cell declares no box.</summary>
    private static double LeadingUnderLastGlyphs(RowPlan plan)
    {
        var least = double.MaxValue;
        foreach (var lines in plan.CellLines)
        {
            if (lines.Count < plan.LineCount || lines.Count == 0) continue;
            var last = lines[lines.Count - 1];
            if (last.LineBoxAscentEm <= 0 || last.LineBoxDescentEm <= 0 || last.FontSize <= 0) return 0;
            var pitch = last.Leading > 0 ? last.FontSize + last.Leading : plan.LineHeight;
            least = Math.Min(least, (pitch - (last.LineBoxAscentEm + last.LineBoxDescentEm) * last.FontSize) / 2);
        }
        return least == double.MaxValue ? 0 : Math.Max(0, least);
    }
}
