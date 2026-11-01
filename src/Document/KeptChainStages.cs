namespace Aspose.Pdf;

public sealed partial class Document
{
    // The size an HTML fragment's text is set at when its own text state names none.
    private const double HtmlFragmentBodyFontPt = 12;

    /// <summary>Keep-with-next over a run: a kept text fragment or heading, the kept text
    /// fragments and headings straight after it, and the start of the paragraph the run is
    /// kept with move to the next page together when they no longer fit the room left but
    /// would fit a fresh page. The run is judged once, at its head; a lone kept fragment
    /// before a text fragment or a table is judged by its own pair rule
    /// (<see cref="LayoutTextFragmentAt"/>).</summary>
    private void KeepRunWithNext(PageContentLayoutState lc, BaseParagraph para)
    {
        var list = lc.pl.paraList;
        var i = lc.paraIdx;
        if (!IsRunMember(para) || i + 1 >= list.Count) return;
        if (i > 0 && IsRunMember(list[i - 1])) return;
        var next = list[i + 1];
        if (para is Text.TextFragment && !IsRunMember(next) && next is Text.TextFragment or Table) return;
        var flow = lc.pl.flow;
        if (flow.CurrentY >= flow.ContentTop - LayoutEpsilonPt) return;
        // (a head that does not fit at all breaks by its own kind's rule, as before)
        var need = MemberHeight(lc, para);
        if (flow.CurrentY - need < flow.BottomMargin) return;
        var j = i + 1;
        for (; IsRunMember(list[j]); j++)
        {
            need += MemberHeight(lc, list[j]);
            if (j + 1 >= list.Count) return;
        }
        if (ParagraphIsInNewPage(list[j]) || StartNeed(lc, list[j], list[j - 1]) is not { } start) return;
        need += start;
        if (flow.CurrentY - need < flow.BottomMargin
            && need <= flow.ContentTop - flow.BottomMargin + LayoutEpsilonPt)
            flow.ForceNewPage();
    }

    private const double LayoutEpsilonPt = 0.5;

    private static bool IsRunMember(BaseParagraph p) => p is Text.TextFragment or Heading && p.IsKeptWithNext;

    /// <summary>The room a member of a kept run takes: all its lines and both margins.</summary>
    private static double MemberHeight(PageContentLayoutState lc, BaseParagraph p)
    {
        var margins = (p.Margin?.Top ?? 0) + (p.Margin?.Bottom ?? 0);
        return margins + (p is Heading heading
            ? heading.MeasureHeight(lc.pl.flow.CurrentPage, lc.pl.marginLeft)
            : lc.pl.flow.LinesHeightOf((Text.TextFragment)p));
    }

    /// <summary>The room the paragraph a run is kept with needs to start on the page: a
    /// fragment's first line, a heading, a table's first row under its repeated header - or the
    /// whole table after a text fragment, as that pair's own rule weighs it - an HTML fragment's
    /// first line at its size; null for a kind the run cannot weigh.</summary>
    private static double? StartNeed(PageContentLayoutState lc, BaseParagraph p, BaseParagraph last)
    {
        var top = p.Margin?.Top ?? 0;
        return p switch
        {
            Text.TextFragment tf => top + FlowLayout.FirstLineNeedOf(tf),
            Heading heading => top + heading.MeasureHeight(lc.pl.flow.CurrentPage, lc.pl.marginLeft),
            Table table => last is Text.TextFragment ? table.GetHeight() : table.GetStartHeight(lc.pl.flow.CurrentPage),
            HtmlFragment html => top + (html.TextState?.FontSize > 0 ? html.TextState.FontSize : HtmlFragmentBodyFontPt)
                * Table.CssNormalLineHeight,
            _ => null,
        };
    }
}
