using Aspose.Pdf;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Generator;

/// <summary>A content-sized grid laid by its declarations alone
/// (<see cref="Table.FixedLayout"/>) once it has a width to fill -- the band, or a
/// box of its own (<see cref="Table.BoxWidth"/>) -- and the box a collapsed grid
/// occupies at its edges. The pitches are read back from the rules the grid
/// draws, so every number here is one a viewer would measure.</summary>
public sealed class FixedLayoutColumnTests
{
    private const double Margin = 36;
    private const double Rule = 0.5;
    private const double Band = 595 - 2 * Margin - Rule;

    [Fact]
    public void DeclarationsShareTheBandInTheirOwnProportions()
    {
        var widths = Pitches(Grid("100 50", fixedLayout: true, stretch: true, "one", "two"));
        Assert.Equal(Band, widths[0] + widths[1], 2);
        Assert.Equal(widths[1] * 2, widths[0], 2);
    }

    [Fact]
    public void TheAutomaticLayoutSharesTheBandByTheRoomAboveEachWidestWord()
    {
        var widths = Pitches(Grid("100 50", fixedLayout: false, stretch: true, "one", "two"));
        Assert.Equal(Band, widths[0] + widths[1], 2);
        Assert.NotEqual(widths[1] * 2, widths[0], 1);
    }

    [Fact]
    public void ADeclaredBoxIsFilledLikeTheBand()
    {
        var table = Grid("100 50", fixedLayout: true, stretch: false, "one", "two");
        table.BoxWidth = 300;
        var widths = Pitches(table);
        Assert.Equal(300 - Rule, widths[0] + widths[1], 2);
        Assert.Equal(widths[1] * 2, widths[0], 2);
    }

    [Fact]
    public void UndeclaredColumnsShareWhatTheDeclaredLeaveEqually()
    {
        var table = Grid("100 auto auto", fixedLayout: true, stretch: false, "one", "two", "three");
        table.BoxWidth = 400;
        var widths = Pitches(table);
        Assert.Equal(100, widths[0], 2);
        Assert.Equal((400 - Rule - 100) / 2, widths[1], 2);
        Assert.Equal(widths[1], widths[2], 2);
    }

    [Fact]
    public void WithoutAWidthToFillTheDeclarationsStandAsTheyAre()
    {
        var widths = Pitches(Grid("100 50", fixedLayout: true, stretch: false, "one", "two"));
        Assert.Equal(100, widths[0], 2);
        Assert.Equal(50, widths[1], 2);
    }

    [Fact]
    public void DeclarationsWiderThanTheBoxAreKept()
    {
        var table = Grid("100 50 100", fixedLayout: true, stretch: false, "one", "two", "three");
        table.BoxWidth = 200;
        var widths = Pitches(table);
        Assert.Equal(new[] { 100.0, 50, 100 }, widths.Select(w => Math.Round(w, 2)));
    }

    [Fact]
    public void ATableBorderWiderThanTheCellRulesOwnsTheOuterEdge()
    {
        var table = Grid("100 50", fixedLayout: true, stretch: false, "one", "two");
        table.BoxWidth = 300;
        table.Border = new BorderInfo(BorderSide.All, 3f);
        var widths = Pitches(table);
        // The span between the outermost rule centres is the box less half the
        // 3 pt border at each side, not half a cell rule.
        Assert.Equal(300 - 3, widths[0] + widths[1], 2);
    }

    [Fact]
    public void TheFlowResumesUnderAContinuedGridAtItsBoxBottom()
    {
        using var doc = new Document();
        var page = doc.Pages.Add();
        page.PageInfo.Margin = new MarginInfo(Margin, Margin, Margin, Margin);
        // Enough rows to run onto a second page, then a paragraph under the grid.
        var table = Grid("100 50", fixedLayout: false, stretch: false, "one", "two");
        for (var i = 0; i < 60; i++)
        {
            var row = table.Rows.Add();
            row.Cells.Add().Paragraphs.Add(new TextFragment("a"));
            row.Cells.Add().Paragraphs.Add(new TextFragment("b"));
        }
        page.Paragraphs.Add(table);
        var after = new TextFragment("after");
        page.Paragraphs.Add(after);
        using var reopened = Document.Open(doc.ToArray());
        var last = reopened.Pages[reopened.Pages.Count];
        var lowestRule = double.MaxValue;
        foreach (var op in last.Contents)
            if (op is MoveTo m) lowestRule = Math.Min(lowestRule, m.Y);
        var absorber = new TextFragmentAbsorber("after");
        last.Accept(absorber);
        var baseline = absorber.TextFragments[1].Position!.YIndent;
        // The paragraph's first baseline sits its margin and ascent under the
        // grid's box bottom, which is half a rule under the lowest rule centre --
        // the same seat it takes under a grid that never left its first page.
        var single = new Document();
        var singlePage = single.Pages.Add();
        singlePage.PageInfo.Margin = new MarginInfo(Margin, Margin, Margin, Margin);
        singlePage.Paragraphs.Add(Grid("100 50", fixedLayout: false, stretch: false, "one", "two"));
        singlePage.Paragraphs.Add(new TextFragment("after"));
        using var singleReopened = Document.Open(single.ToArray());
        var singleLowest = double.MaxValue;
        foreach (var op in singleReopened.Pages[1].Contents)
            if (op is MoveTo m) singleLowest = Math.Min(singleLowest, m.Y);
        var singleAbsorber = new TextFragmentAbsorber("after");
        singleReopened.Pages[1].Accept(singleAbsorber);
        var singleBaseline = singleAbsorber.TextFragments[1].Position!.YIndent;
        Assert.Equal(singleLowest - singleBaseline, lowestRule - baseline, 2);
    }

    private static Table Grid(string declared, bool fixedLayout, bool stretch, params string[] texts)
    {
        var table = new Table
        {
            ColumnWidths = declared,
            DefaultCellBorder = new BorderInfo(BorderSide.All, (float)Rule),
            DefaultCellPadding = new MarginInfo(2, 2, 2, 2),
            IsBordersCollapsed = true,
            SizesColumnsToContent = true,
            StretchesToBand = stretch,
            FixedLayout = fixedLayout,
        };
        var row = table.Rows.Add();
        foreach (var text in texts) row.Cells.Add().Paragraphs.Add(new TextFragment(text));
        return table;
    }

    /// <summary>The column pitches read back from the rules: the distinct vertical
    /// lines of the grid, in order.</summary>
    private static double[] Pitches(Table table)
    {
        using var doc = new Document();
        var page = doc.Pages.Add();
        page.PageInfo.Margin = new MarginInfo(Margin, Margin, Margin, Margin);
        page.Paragraphs.Add(table);
        using var reopened = Document.Open(doc.ToArray());
        var xs = new SortedSet<double>();
        double? x = null;
        foreach (var op in reopened.Pages[1].Contents)
        {
            switch (op)
            {
                case MoveTo m: x = m.X; break;
                case LineTo l when x is { } startX && Math.Abs(l.X - startX) < 1e-6:
                    xs.Add(Math.Round(startX, 3));
                    break;
            }
        }
        var lines = xs.ToArray();
        var pitches = new double[lines.Length - 1];
        for (var i = 0; i < pitches.Length; i++) pitches[i] = lines[i + 1] - lines[i];
        return pitches;
    }
}
