using Aspose.Pdf;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Generator;

/// <summary>A <see cref="BoxBlock"/>: paragraphs laid inside its border and padding,
/// margins that add rather than collapse, a sized box standing where its alignment puts
/// it, and a box its page cannot finish closed under what the page holds and opened
/// again on the next. Positions and fills are read back from the pages.</summary>
public sealed class BoxBlockTests
{
    private const double Margin = 36;
    private const double PageTop = 842 - Margin;
    private const double Size = 12;
    // The test paragraphs' line box: 12 pt text in an 18 pt line, 0.8 em above the
    // baseline and 0.2 below, so the baseline sits 12.6 under the line's top.
    private const double LineBox = 18;
    private const double Seat = 12.6;
    private static readonly Color Yellow = Color.FromRgb(1, 1, 0);

    [Fact]
    public void TheParagraphsStandInsideTheBandAndThePadding()
    {
        var box = new BoxBlock { Border = new BorderInfo(BorderSide.All, 2), Padding = Pad(8), BackgroundColor = Yellow };
        box.Paragraphs.Add(Plain("inside"));
        var page = RenderPages(box)[0];
        var text = page.Texts.Single();
        Assert.Equal(Margin + 2 + 8, text.X, 2);
        Assert.Equal(PageTop - 2 - 8 - Seat, text.Y, 2);
        var fill = page.Fills.Single();
        Assert.Equal(Margin, fill.X, 2);
        Assert.Equal(595 - 2 * Margin, fill.Width, 2);
        Assert.Equal(PageTop, fill.Y + fill.Height, 2);
        Assert.Equal(2 + 8 + LineBox + 8 + 2, fill.Height, 2);
        Assert.True(page.FillsBeforeText, "the box is painted ahead of what it holds");
    }

    [Fact]
    public void MarginsAddAndNeverCollapse()
    {
        var box = new BoxBlock();
        box.Margin.Top = 10;
        box.Margin.Left = 20;
        var inner = Plain("inner");
        inner.Margin.Top = 4;
        box.Paragraphs.Add(inner);
        var after = Plain("after");
        var page = RenderPages(box, after)[0];
        Assert.Equal(Margin + 20, page.Texts.Single(t => t.Text == "inner").X, 2);
        Assert.Equal(PageTop - 10 - 4 - Seat, page.Texts.Single(t => t.Text == "inner").Y, 2);
        box.Margin.Bottom = 7;
        page = RenderPages(box, after)[0];
        Assert.Equal(PageTop - 10 - 4 - LineBox - 7 - Seat, page.Texts.Single(t => t.Text == "after").Y, 2);
    }

    [Theory]
    [InlineData(HorizontalAlignment.Left, 0)]
    [InlineData(HorizontalAlignment.Center, 0.5)]
    [InlineData(HorizontalAlignment.Right, 1)]
    public void ASizedBoxStandsWhereItsAlignmentPutsIt(HorizontalAlignment alignment, double share)
    {
        var box = new BoxBlock { Width = 200, HorizontalAlignment = alignment, Padding = Pad(4), BackgroundColor = Yellow };
        box.Paragraphs.Add(Plain("sized"));
        var page = RenderPages(box)[0];
        var slack = 595 - 2 * Margin - 208;
        Assert.Equal(Margin + share * slack, page.Fills.Single().X, 2);
        Assert.Equal(208, page.Fills.Single().Width, 2);
        Assert.Equal(Margin + share * slack + 4, page.Texts.Single().X, 2);
    }

    [Fact]
    public void AFractionWidthIsOfTheRegion()
    {
        var box = new BoxBlock { Width = 0.5, WidthIsFraction = true, BackgroundColor = Yellow };
        box.Paragraphs.Add(Plain("half"));
        Assert.Equal((595 - 2 * Margin) / 2, RenderPages(box)[0].Fills.Single().Width, 2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ABoxThePageCannotFinishClosesAndOpensAgain(bool topMarginAfterBreak)
    {
        var box = new BoxBlock
        {
            Border = new BorderInfo(BorderSide.All, 1), Padding = Pad(5), BackgroundColor = Yellow,
            TopMarginAfterBreak = topMarginAfterBreak,
        };
        box.Margin.Top = 10;
        box.Margin.Bottom = 20;
        for (var i = 1; i <= 50; i++) box.Paragraphs.Add(Plain("line " + i));
        var pages = RenderPages(box);
        Assert.Equal(2, pages.Count);
        var first = pages[0].Fills.Single();
        var second = pages[1].Fills.Single();
        Assert.Equal(PageTop - 10, first.Y + first.Height, 2);
        // The part left behind closes under its last line, above the room kept for
        // the bottom padding, band and margin.
        var lastOnFirst = pages[0].Texts.Min(t => t.Y);
        Assert.Equal(lastOnFirst - (LineBox - Seat) - 5 - 1, first.Y, 2);
        Assert.True(first.Y - 20 >= Margin - 1e-6, "the bottom margin still fits the page");
        Assert.True(first.Y - 20 - LineBox < Margin, "no further line would have fitted");
        Assert.Equal(PageTop - (topMarginAfterBreak ? 10 : 0), second.Y + second.Height, 2);
        Assert.Equal(PageTop - (topMarginAfterBreak ? 10 : 0) - 1 - 5 - Seat, pages[1].Texts.Max(t => t.Y), 2);
        Assert.Equal(50, pages.Sum(p => p.Texts.Count));
    }

    [Fact]
    public void ABoxWhoseFirstParagraphCannotStartMovesOnWhole()
    {
        var spacer = Plain("spacer");
        spacer.Margin.Top = PageTop - Margin - LineBox - 10;
        var box = new BoxBlock { Padding = Pad(4), BackgroundColor = Yellow };
        box.Margin.Top = 3;
        box.Paragraphs.Add(Plain("moved"));
        var pages = RenderPages(spacer, box);
        Assert.Equal(2, pages.Count);
        Assert.Empty(pages[0].Fills);
        var fill = pages[1].Fills.Single();
        Assert.Equal(PageTop - 3, fill.Y + fill.Height, 2);
        Assert.Equal(PageTop - 3 - 4 - Seat, pages[1].Texts.Single().Y, 2);
    }

    [Fact]
    public void NestedBoxesCloseInnermostFirstAndOpenOutermostFirst()
    {
        var inner = new BoxBlock { Border = new BorderInfo(BorderSide.All, 1), TopMarginAfterBreak = true };
        inner.Margin.Top = 5;
        inner.Margin.Bottom = 5;
        for (var i = 1; i <= 50; i++) inner.Paragraphs.Add(Plain("line " + i));
        var outer = new BoxBlock { Padding = Pad(4), BackgroundColor = Yellow };
        outer.Paragraphs.Add(inner);
        var pages = RenderPages(outer);
        Assert.Equal(2, pages.Count);
        // The outer part closes under the inner part's bottom margin.
        var lastOnFirst = pages[0].Texts.Min(t => t.Y);
        Assert.Equal(lastOnFirst - (LineBox - Seat) - 1 - 5 - 4, pages[0].Fills.Single().Y, 2);
        // Overleaf: the outer padding, then the inner box's top margin again, its band.
        Assert.Equal(PageTop - 4 - 5 - 1 - Seat, pages[1].Texts.Max(t => t.Y), 2);
    }

    [Fact]
    public void ALeastHeightFloorsTheContentBox()
    {
        var box = new BoxBlock { MinHeight = 40, BackgroundColor = Yellow };
        box.Paragraphs.Add(Plain("short"));
        Assert.Equal(40, RenderPages(box)[0].Fills.Single().Height, 2);
    }

    [Fact]
    public void ATableInABoxStandsInItsContentBox()
    {
        var table = new Table { ColumnWidths = "100", DefaultCellBorder = new BorderInfo(BorderSide.All, 1) };
        table.Rows.Add().Cells.Add("cell");
        var box = new BoxBlock { Border = new BorderInfo(BorderSide.All, 2), Padding = Pad(6) };
        box.Paragraphs.Add(table);
        var cell = RenderPages(box)[0].Texts.Single();
        Assert.True(cell.X >= Margin + 2 + 6, "the grid starts inside the padding");
        Assert.True(cell.X < Margin + 2 + 6 + 10, "the grid starts at the content box's left");
    }

    [Fact]
    public void ATableBreakingInABoxClosesAndOpensTheBoxAroundEachPage()
    {
        var table = new Table { ColumnWidths = "100", DefaultCellBorder = new BorderInfo(BorderSide.All, 1) };
        for (var i = 1; i <= 150; i++) table.Rows.Add().Cells.Add("row " + i);
        var box = new BoxBlock
        {
            Border = new BorderInfo(BorderSide.All, 1), Padding = Pad(5), BackgroundColor = Yellow,
            TopMarginAfterBreak = true,
        };
        box.Margin.Top = 10;
        box.Paragraphs.Add(table);
        box.Paragraphs.Add(Plain("closes"));
        var pages = RenderPages(box);
        Assert.True(pages.Count >= 3, "the table runs over a whole page");
        Assert.Equal(151, pages.Sum(p => p.Texts.Count));
        for (var n = 0; n < pages.Count; n++)
        {
            var fill = pages[n].Fills.Single();
            var texts = pages[n].Texts;
            // Each part opens under the top margin (again, overleaf), its band and padding...
            Assert.Equal(PageTop - 10, fill.Y + fill.Height, 2);
            Assert.True(texts.Max(t => t.Y) < PageTop - 10 - 1 - 5, "rows start inside the reopened box");
            // ...and closes under what the page holds, above the page's bottom margin.
            Assert.True(fill.Y >= Margin - 1e-6, "the part closes above the page's bottom margin");
            Assert.True(fill.Y < texts.Min(t => t.Y), "the part closes under the page's last row");
            Assert.True(pages[n].FillsBeforeText, "each part is painted ahead of its rows");
        }
    }

    [Fact]
    public void AMiddleRuleIsStrokedThroughTheContentBoxOfAnEmptyBox()
    {
        var box = new BoxBlock
        {
            MinHeight = 4, Width = 200, HorizontalAlignment = HorizontalAlignment.Right, IsKeptTogether = true,
            MiddleRule = new GraphInfo { LineWidth = 4, Color = Color.FromRgb(1, 0, 0) },
        };
        box.Margin.Top = 10;
        box.Margin.Bottom = 5;
        var rule = Rules(RenderDocument(box, Plain("after")).Pages[1]).Single();
        var right = 595 - Margin;
        Assert.Equal((right - 200, PageTop - 10 - 2, right, PageTop - 10 - 2, 4.0),
            (rule.X1, rule.Y1, rule.X2, rule.Y2, rule.Width));
        // The box is the rule's height: what follows starts under it and its bottom margin.
        using var doc = RenderDocument(box, Plain("after"));
        var absorber = new TextFragmentAbsorber();
        absorber.Visit(doc.Pages[1]);
        Assert.Equal(PageTop - 10 - 4 - 5 - Seat, absorber.TextFragments.Single().Position!.YIndent, 2);
    }

    [Fact]
    public void AKeptRuleBoxThePageCannotHoldStartsTheNextPage()
    {
        // 42 lines of 18 pt leave 14 pt; the rule box needs 10 + 6.
        var paragraphs = Enumerable.Range(1, 42).Select(i => (BaseParagraph)Plain("line " + i)).ToList();
        var box = new BoxBlock { MinHeight = 6, IsKeptTogether = true, MiddleRule = new GraphInfo { LineWidth = 6 } };
        box.Margin.Top = 10;
        paragraphs.Add(box);
        using var doc = RenderDocument(paragraphs.ToArray());
        Assert.Empty(Rules(doc.Pages[1]));
        Assert.Equal(PageTop - 10 - 3, Rules(doc.Pages[2]).Single().Y1, 2);
    }

    private static Document RenderDocument(params BaseParagraph[] paragraphs)
    {
        using var doc = new Document();
        var page = doc.Pages.Add();
        page.PageInfo.Margin = new MarginInfo(Margin, Margin, Margin, Margin);
        foreach (var paragraph in paragraphs) page.Paragraphs.Add(paragraph);
        return Document.Open(doc.ToArray());
    }

    /// <summary>The stroked segments on a page with the line width each was stroked at.</summary>
    private static List<(double X1, double Y1, double X2, double Y2, double Width)> Rules(Page page)
    {
        var rules = new List<(double, double, double, double, double)>();
        var width = 1.0;
        MoveTo? from = null;
        LineTo? to = null;
        foreach (var op in page.Contents)
        {
            switch (op)
            {
                case SetLineWidth w: width = w.LineWidth; break;
                case MoveTo m: from = m; to = null; break;
                case LineTo l: to = l; break;
                case Stroke when from is not null && to is not null:
                    rules.Add((Math.Round(from.X, 3), Math.Round(from.Y, 3), Math.Round(to.X, 3), Math.Round(to.Y, 3), width));
                    break;
            }
        }
        return rules;
    }

    private static MarginInfo Pad(double all) => new(all, all, all, all);

    private static TextFragment Plain(string text)
    {
        var fragment = new TextFragment(text);
        fragment.TextState.FontSize = (float)Size;
        fragment.TextState.LineSpacing = (float)(LineBox - Size);
        fragment.TextState.FormattingOptions.LineSpacing = TextFormattingOptions.LineSpacingMode.LineBox;
        fragment.TextState.LineBoxAscentEm = 0.8;
        fragment.TextState.LineBoxDescentEm = 0.2;
        return fragment;
    }

    private static List<RenderedPage> RenderPages(params BaseParagraph[] paragraphs)
    {
        using var doc = new Document();
        var page = doc.Pages.Add();
        page.PageInfo.Margin = new MarginInfo(Margin, Margin, Margin, Margin);
        foreach (var paragraph in paragraphs) page.Paragraphs.Add(paragraph);
        using var reopened = Document.Open(doc.ToArray());
        var pages = new List<RenderedPage>();
        for (var n = 1; n <= reopened.Pages.Count; n++)
        {
            var absorber = new TextFragmentAbsorber();
            absorber.Visit(reopened.Pages[n]);
            var texts = absorber.TextFragments.Select(f => new Placed(f.Text, f.Position!.XIndent, f.Position.YIndent)).ToList();
            var (fills, before) = Fills(reopened.Pages[n]);
            pages.Add(new RenderedPage(texts, fills, before));
        }
        return pages;
    }

    /// <summary>The rectangles filled in <see cref="Yellow"/>, and whether all of them come
    /// before the first text object.</summary>
    private static (List<Re> Fills, bool BeforeText) Fills(Page page)
    {
        var fills = new List<Re>();
        var before = true;
        var seenText = false;
        bool yellow = false;
        Re? pending = null;
        foreach (var op in page.Contents)
        {
            switch (op)
            {
                case SetRGBColor rg: yellow = rg.R == 1 && rg.G == 1 && rg.B == 0; break;
                case Re re: pending = re; break;
                case Aspose.Pdf.Operators.Fill when pending is not null && yellow:
                    fills.Add(pending);
                    if (seenText) before = false;
                    pending = null;
                    break;
                case BT: seenText = true; break;
            }
        }
        return (fills, before);
    }

    private sealed record Placed(string Text, double X, double Y);

    private sealed record RenderedPage(List<Placed> Texts, List<Re> Fills, bool FillsBeforeText);
}
