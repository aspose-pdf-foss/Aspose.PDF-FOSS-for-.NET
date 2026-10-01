using Aspose.Pdf;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Generator;

/// <summary>A grid whose cells keep their own rules INSIDE the widths their
/// columns declare (<see cref="Table.RulesInsideColumnWidth"/>), held apart by a
/// cell spacing (<see cref="Table.HorizontalCellSpacing"/>,
/// <see cref="Table.VerticalCellSpacing"/>) -- the CSS separated-border model.
/// Every rule is a filled band, so the geometry is read back from the rectangles
/// the grid fills, and every number here is one a viewer would measure.</summary>
public sealed class SeparatedCellBoxTests
{
    private const double Margin = 36;
    private const double PageTop = 842 - Margin;
    private const double Band = 595 - 2 * Margin;
    private const double Rule = 0.5;

    [Fact]
    public void TheRulesStandInsideTheDeclaredWidth()
    {
        var page = Render(Grid("100 50", t => { }, "one", "two", "three", "four"))[0];
        var firstRow = page.Bands.Where(b => b.Vertical).GroupBy(b => Math.Round(b.Top, 2)).OrderByDescending(g => g.Key).First();
        // Cell 1 is 36..136 with its own rules on 36..36.5 and 135.5..136; cell 2 starts
        // at 136 with a rule of its own, so two rules stand side by side between them.
        Assert.Equal(new[] { 36.0, 135.5, 136, 185.5 }, firstRow.Select(b => Math.Round(b.X, 2)).OrderBy(x => x));
        Assert.All(firstRow, b => Assert.Equal(Rule, b.Width, 2));
        Assert.Equal(36 + Rule + 2, page.Texts.Single(t => t.Text == "one").X, 2);
        Assert.Equal(136 + Rule + 2, page.Texts.Single(t => t.Text == "two").X, 2);
    }

    [Fact]
    public void TheSpacingHoldsTheBoxesApartAndOffTheGridEdge()
    {
        var page = Render(Grid("100 50", t => { t.HorizontalCellSpacing = 10; t.VerticalCellSpacing = 6; },
            "one", "two", "three", "four"))[0];
        var rows = page.Bands.Where(b => b.Vertical).GroupBy(b => Math.Round(b.Top, 2)).OrderByDescending(g => g.Key).ToArray();
        Assert.Equal(2, rows.Length);
        Assert.Equal(new[] { 46.0, 145.5, 156, 205.5 }, rows[0].Select(b => Math.Round(b.X, 2)).OrderBy(x => x));
        // The first row stands one gap under the grid's top, the second one gap under the first.
        Assert.Equal(PageTop - 6, rows[0].Key, 2);
        Assert.Equal(rows[0].Key - rows[0].First().Height - 6, rows[1].Key, 2);
    }

    [Fact]
    public void ASpanningCellCoversTheGapsItSpans()
    {
        var table = Grid("80 80 80", t => { t.HorizontalCellSpacing = 4; t.VerticalCellSpacing = 4; });
        var first = table.Rows.Add();
        var across = first.Cells.Add();
        across.ColSpan = 2;
        across.Paragraphs.Add(new TextFragment("span two"));
        first.Cells.Add().Paragraphs.Add(new TextFragment("c"));
        var second = table.Rows.Add();
        var down = second.Cells.Add();
        down.RowSpan = 2;
        down.Paragraphs.Add(new TextFragment("tall"));
        second.Cells.Add().Paragraphs.Add(new TextFragment("d"));
        second.Cells.Add().Paragraphs.Add(new TextFragment("e"));
        var third = table.Rows.Add();
        third.Cells.Add().Paragraphs.Add(new TextFragment("f"));
        third.Cells.Add().Paragraphs.Add(new TextFragment("g"));
        var page = Render(table)[0];
        var horizontal = page.Bands.Where(b => !b.Vertical).ToArray();
        // The span's top rule runs over both columns and the gap between them.
        Assert.Contains(horizontal, b => Near(b.X, 40) && Near(b.Width, 80 + 4 + 80));
        // The row span's left rule runs down both rows and the gap between them.
        // (column 2's own left rule stands in the two rows under the span; the span covers it above)
        var plain = page.Bands.Where(b => b.Vertical && Near(b.X, 124)).OrderByDescending(b => b.Top).ToArray();
        Assert.Equal(2, plain.Length);
        var tall = page.Bands.Single(b => b.Vertical && Near(b.X, 40) && b.Height > plain[0].Height + 1);
        Assert.Equal(plain[0].Height + 4 + plain[1].Height, tall.Height, 2);
    }

    [Fact]
    public void AGridFillingTheBandSharesWhatItsBorderAndGapsLeave()
    {
        var table = Grid("100 50", t =>
        {
            t.HorizontalCellSpacing = 10;
            t.VerticalCellSpacing = 6;
            t.Border = new BorderInfo(BorderSide.All, 2f, Color.Red);
            t.SizesColumnsToContent = true;
            t.StretchesToBand = true;
        }, "one", "two", "three", "four");
        var page = Render(table)[0];
        var tops = page.Bands.Where(b => !b.Vertical && b.Width > 20).GroupBy(b => Math.Round(b.Top, 2)).OrderByDescending(g => g.Key).First().OrderBy(b => b.X).ToArray();
        // The cells start inside the border and the first gap, and together with the
        // three gaps fill what the border leaves of the band.
        Assert.Equal(Margin + 2 + 10, tops[0].X, 2);
        Assert.Equal(Band - 2 * 2 - 3 * 10, tops[0].Width + tops[1].Width, 2);
        // The frame stands wholly outside the gaps.
        var frame = page.Fills.Where(f => f.Red > 0.9 && f.Green < 0.1).ToArray();
        Assert.Contains(frame, f => Near(f.X, Margin) && Near(f.Width, 2));
        Assert.Contains(frame, f => Near(f.X, Margin) && Near(f.Width, Band) && Near(f.Top, PageTop));
    }

    [Fact]
    public void ACellRuledMoreHeavilyIsBoxedByItsOwnRulesAndMakesItsRowTaller()
    {
        var table = Grid("", t => { t.SizesColumnsToContent = true; t.HorizontalCellSpacing = 10; t.VerticalCellSpacing = 6; });
        var first = table.Rows.Add();
        var heavy = first.Cells.Add();
        heavy.Border = new BorderInfo(BorderSide.All, 3f, Color.Blue);
        heavy.Paragraphs.Add(new TextFragment("one"));
        first.Cells.Add().Paragraphs.Add(new TextFragment("two"));
        var second = table.Rows.Add();
        second.Cells.Add().Paragraphs.Add(new TextFragment("three"));
        second.Cells.Add().Paragraphs.Add(new TextFragment("four"));
        var page = Render(table)[0];
        var blue = page.Fills.Where(f => f.Blue > 0.9 && f.Red < 0.1).ToArray();
        var heavyLeft = blue.Single(f => Near(f.X, 46) && f.Height > 3.5);
        Assert.Equal(3, heavyLeft.Width, 2);
        // The heavy cell's text starts inside its own 3 pt rule and 2 pt padding.
        Assert.Equal(46 + 3 + 2, page.Texts.Single(t => t.Text == "one").X, 2);
        // Its neighbour is drawn at the row's full height, and the row is the heavy
        // cell's own box: 5 pt taller than the plain row below.
        var neighbourLeft = page.Bands.Single(b => b.Vertical && Near(b.Top, heavyLeft.Top) && b.X > 60 && b.X < 100);
        Assert.Equal(heavyLeft.Height, neighbourLeft.Height, 2);
        var plainRow = page.Bands.Where(b => b.Vertical && b.Top < heavyLeft.Top - 1).Max(b => b.Height);
        Assert.Equal(plainRow + 2 * 3 - 2 * Rule, heavyLeft.Height, 2);
    }

    [Fact]
    public void UnderCollapseTheSpacingChangesNothing()
    {
        var plain = Contents(Grid("100 50", t => { t.IsBordersCollapsed = true; t.RulesInsideColumnWidth = false; },
            "one", "two", "three", "four"));
        var spaced = Contents(Grid("100 50", t =>
        {
            t.IsBordersCollapsed = true;
            t.RulesInsideColumnWidth = false;
            t.HorizontalCellSpacing = 10;
            t.VerticalCellSpacing = 6;
        }, "one", "two", "three", "four"));
        Assert.Equal(plain, spaced);
    }

    [Fact]
    public void EveryPageOpensWithTheGapAndTheRepeatedBandIsFollowedByOne()
    {
        var table = Grid("120 120", t => { t.HorizontalCellSpacing = 10; t.VerticalCellSpacing = 6; t.RepeatingRowsCount = 1; },
            "head a", "head b");
        for (var i = 0; i < 60; i++)
        {
            var row = table.Rows.Add();
            row.Cells.Add().Paragraphs.Add(new TextFragment("row " + i));
            row.Cells.Add().Paragraphs.Add(new TextFragment("value " + i));
        }
        var pages = Render(table);
        Assert.True(pages.Count > 1);
        var rows = pages[1].Bands.Where(b => b.Vertical).GroupBy(b => Math.Round(b.Top, 2)).OrderByDescending(g => g.Key).ToArray();
        Assert.Equal(PageTop - 6, rows[0].Key, 2);
        Assert.Equal(rows[0].Key - rows[0].First().Height - 6, rows[1].Key, 2);
    }

    private static Table Grid(string declared, Action<Table> style, params string[] texts)
    {
        var table = new Table
        {
            ColumnWidths = declared,
            DefaultCellBorder = new BorderInfo(BorderSide.All, (float)Rule),
            DefaultCellPadding = new MarginInfo(2, 2, 2, 2),
            RulesInsideColumnWidth = true,
        };
        var columns = declared.Length == 0 ? 2 : declared.Split(' ').Length;
        for (var i = 0; i < texts.Length; i += columns)
        {
            var row = table.Rows.Add();
            for (var c = 0; c < columns && i + c < texts.Length; c++)
                row.Cells.Add().Paragraphs.Add(new TextFragment(texts[i + c]));
        }
        style(table);
        return table;
    }

    private static string Contents(Table table)
    {
        using var doc = new Document();
        var page = doc.Pages.Add();
        page.PageInfo.Margin = new MarginInfo(Margin, Margin, Margin, Margin);
        page.Paragraphs.Add(table);
        using var reopened = Document.Open(doc.ToArray());
        return string.Join("\n", reopened.Pages[1].Contents.Select(op => op.ToPdf()));
    }

    private static List<RenderedPage> Render(Table table)
    {
        using var doc = new Document();
        var page = doc.Pages.Add();
        page.PageInfo.Margin = new MarginInfo(Margin, Margin, Margin, Margin);
        page.Paragraphs.Add(table);
        using var reopened = Document.Open(doc.ToArray());
        var pages = new List<RenderedPage>();
        for (var n = 1; n <= reopened.Pages.Count; n++)
        {
            var absorber = new TextFragmentAbsorber();
            absorber.Visit(reopened.Pages[n]);
            var texts = absorber.TextFragments.Select(f => new Placed(f.Text, f.Position!.XIndent, f.Position.YIndent)).ToList();
            pages.Add(new RenderedPage(Fills(reopened.Pages[n]), texts));
        }
        return pages;
    }

    /// <summary>Every rectangle the page fills, with the colour it was filled in.</summary>
    private static List<Rect> Fills(Page page)
    {
        var fills = new List<Rect>();
        double red = 0, green = 0, blue = 0;
        Re? pending = null;
        foreach (var op in page.Contents)
        {
            switch (op)
            {
                case SetRGBColor rg: red = rg.R; green = rg.G; blue = rg.B; break;
                case Re re: pending = re; break;
                case Fill or FillStroke or EOFill when pending is { } re:
                    fills.Add(new Rect(re.X, re.Y, re.Width, re.Height, red, green, blue));
                    pending = null;
                    break;
                case Stroke: pending = null; break;
            }
        }
        Assert.NotEmpty(fills);
        return fills;
    }

    private static bool Near(double a, double b) => Math.Abs(a - b) < 0.01;

    private sealed record Placed(string Text, double X, double Y);

    private sealed record RenderedPage(List<Rect> Fills, List<Placed> Texts)
    {
        /// <summary>The black rules: the bands the cells draw.</summary>
        public IEnumerable<Rect> Bands => Fills.Where(f => f.Red < 0.01 && f.Green < 0.01 && f.Blue < 0.01);
    }

    private sealed record Rect(double X, double Y, double Width, double Height, double Red, double Green, double Blue)
    {
        public bool Vertical => Height > Width;
        public double Top => Y + Height;
    }
}
