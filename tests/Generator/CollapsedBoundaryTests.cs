using Aspose.Pdf;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Generator;

/// <summary>A collapsed grid whose cells bring rules of different widths to the
/// boundaries they share: each boundary is drawn once, with the WIDER rule (the
/// earlier cell keeps a tie, and a cell keeps a tie against the table's own
/// border), on the same line whatever its width; what hangs off the lines -- the
/// grid's start, each row's height, each box's inset -- follows the rules each
/// cell ended up with.</summary>
public sealed class CollapsedBoundaryTests
{
    private const double Margin = 36;
    private const double PageTop = 842 - Margin;
    private const double Column = 100;

    [Fact]
    public void TheWiderRuleWinsTheSharedBoundaryAndTheLineStaysPut()
    {
        var strokes = Render(Grid(new[] { Column, Column, Column }, t =>
        {
            t.Rows[0].Cells[0].Border = new BorderInfo(BorderSide.All, 2, Color.Red);
        }));

        // The grid's first line stands half the widest left-edge rule inside the
        // table's box; the rest follow at the declared pitch.
        var verticals = strokes.Where(s => s.Vertical).Select(s => Math.Round(s.X1, 2)).Distinct().OrderBy(x => x).ToArray();
        Assert.Equal(new[] { Margin + 1, Margin + 101, Margin + 201, Margin + 301 }, verticals);

        // The boundary between the heavy cell and its neighbour is ruled at 2 in
        // the heavy cell's row, and at the grid's own 0.5 below it.
        var shared = strokes.Where(s => s.Vertical && Near(s.X1, Margin + 101)).OrderByDescending(s => s.Top).ToArray();
        Assert.Equal(2, shared[0].Width, 3);
        Assert.Equal(0.5, shared[^1].Width, 3);
        Assert.Equal(1, shared[0].Red, 3);
    }

    [Fact]
    public void ARowIsAsTallAsTheBoxOfItsTallestCell()
    {
        double RowPitch(Action<Table> style)
        {
            var strokes = Render(Grid(new[] { Column, Column }, style));
            var ys = strokes.Where(s => !s.Vertical).Select(s => Math.Round(s.Y1, 2)).Distinct().OrderByDescending(y => y).ToArray();
            return ys[0] - ys[1];
        }

        // One heavy one-line cell beside a light cell that holds two lines: the
        // row is the light cell's box, not two lines plus the heavy rules.
        void TwoLines(Table t) => t.Rows[0].Cells[1].Paragraphs.Add(new TextFragment("second line"));
        var plain = RowPitch(TwoLines);
        var heavy = RowPitch(t =>
        {
            TwoLines(t);
            t.Rows[0].Cells[0].Border = new BorderInfo(BorderSide.All, 4, Color.Red);
        });
        Assert.Equal(plain, heavy, 2);
    }

    [Fact]
    public void TheTableBorderIsOneSideOfEveryOuterBoundary()
    {
        var strokes = Render(Grid(new[] { Column, Column }, t =>
        {
            t.Border = new BorderInfo(BorderSide.All, 3, Color.Blue);
        }));

        // No frame of its own: the left edge IS the grid's first line, ruled at 3,
        // half its width inside the table's box.
        var left = strokes.Where(s => s.Vertical && Near(s.X1, Margin + 1.5)).ToArray();
        Assert.NotEmpty(left);
        Assert.All(left, s => Assert.Equal(3, s.Width, 3));
        Assert.DoesNotContain(strokes, s => s.Vertical && s.X1 < Margin + 1);

        // A thin inner rule stops short of the heavy edge it meets.
        var inner = strokes.Where(s => s.Vertical && Near(s.X1, Margin + 101.5)).Max(s => s.Top);
        var edge = strokes.Where(s => !s.Vertical).Max(s => s.Y1);
        Assert.Equal(edge - 1.5, inner, 2);
    }

    [Fact]
    public void AnEqualRuleLeavesTheBoundaryToTheEarlierCell()
    {
        var strokes = Render(Grid(new[] { Column, Column }, t =>
        {
            t.Rows[0].Cells[0].Border = new BorderInfo(BorderSide.All, 2, Color.Red);
            t.Rows[0].Cells[1].Border = new BorderInfo(BorderSide.All, 2, Color.Green);
        }));

        var shared = strokes.Where(s => s.Vertical && Near(s.X1, Margin + 101)).OrderByDescending(s => s.Top).First();
        Assert.Equal(1, shared.Red, 3);
        Assert.Equal(0, shared.Green, 3);
    }

    [Fact]
    public void ASpanOnALaterPageIsNotRuledOffWhereThePageDoesNotEnd()
    {
        // A long grid whose second page holds a two-row span well clear of the
        // page's foot: the boundary under the span is ruled once, by the row
        // below it -- the span only closes itself where the page cuts it.
        var table = Grid(new[] { Column, Column }, _ => { });
        for (var r = 2; r < 100; r++)
        {
            var row = table.Rows.Add();
            for (var c = 0; c < 2; c++) row.Cells.Add().Paragraphs.Add(new TextFragment("r" + r + "c" + c));
        }
        const int spanRow = 70;
        table.Rows[spanRow].Cells[0].RowSpan = 2;
        table.Rows[spanRow + 1].Cells.RemoveRange(0, 1);
        table.Rows[spanRow].Cells[0].Paragraphs.Clear();
        table.Rows[spanRow].Cells[0].Paragraphs.Add(new TextFragment("span"));

        var pages = RenderPages(table);
        Assert.True(pages.Count >= 2);
        var second = pages[1];
        var spanText = second.Texts.Single(t => t.Text == "span");
        // The two row boundaries directly under the span's text, in its column.
        var ruled = second.Strokes.Where(s => !s.Vertical && s.Y1 < spanText.Y && Math.Min(s.X1, s.X2) < Margin + 50)
            .GroupBy(s => Math.Round(s.Y1, 2)).OrderByDescending(g => g.Key).Take(2).ToArray();
        Assert.All(ruled, boundary => Assert.Single(boundary));
    }

    [Fact]
    public void ACellBesideOrAboveAnEmptySlotStillClosesItsBox()
    {
        // Three cells across two columns: the second row holds one cell and the slot
        // beside it is empty. The cell above the slot keeps its bottom rule, the lone
        // cell its right one, and nothing is ruled round the empty slot itself.
        var table = new Table
        {
            ColumnWidths = "100 100",
            DefaultCellBorder = new BorderInfo(BorderSide.All, 0.5f),
            IsBordersCollapsed = true,
        };
        var first = table.Rows.Add();
        first.Cells.Add().Paragraphs.Add(new TextFragment("a"));
        first.Cells.Add().Paragraphs.Add(new TextFragment("b"));
        table.Rows.Add().Cells.Add().Paragraphs.Add(new TextFragment("short"));
        var strokes = Render(table);

        var rowLines = strokes.Where(s => !s.Vertical).Select(s => Math.Round(s.Y1, 2)).Distinct().OrderByDescending(y => y).ToArray();
        Assert.Equal(3, rowLines.Length);
        var underB = strokes.Where(s => !s.Vertical && Near(s.Y1, rowLines[1]) && Math.Min(s.X1, s.X2) > Margin + 90).ToArray();
        Assert.Single(underB);
        var lastLine = strokes.Where(s => !s.Vertical && Near(s.Y1, rowLines[2])).ToArray();
        Assert.All(lastLine, s => Assert.True(Math.Max(s.X1, s.X2) < Margin + 110));
        var besideShort = strokes.Where(s => s.Vertical && s.Top <= rowLines[1] + 0.5 && Near(s.X1, Margin + 100.25)).ToArray();
        Assert.Single(besideShort);
        Assert.DoesNotContain(strokes, s => s.Vertical && s.Top <= rowLines[1] + 0.5 && s.X1 > Margin + 150);
    }

    private static Table Grid(double[] widths, Action<Table> style)
    {
        var table = new Table
        {
            ColumnWidths = string.Join(" ", widths.Select(w => w.ToString(System.Globalization.CultureInfo.InvariantCulture))),
            DefaultCellBorder = new BorderInfo(BorderSide.All, 0.5f),
            DefaultCellPadding = new MarginInfo(2, 2, 2, 2),
            IsBordersCollapsed = true,
        };
        for (var r = 0; r < 2; r++)
        {
            var row = table.Rows.Add();
            for (var c = 0; c < widths.Length; c++)
                row.Cells.Add().Paragraphs.Add(new TextFragment("r" + r + "c" + c));
        }
        style(table);
        return table;
    }

    private static List<Stroke> Render(Table table) => RenderPages(table)[0].Strokes;

    private static List<RenderedPage> RenderPages(Table table)
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
            pages.Add(new RenderedPage(Strokes(reopened.Pages[n]), texts));
        }
        return pages;
    }

    private static List<Stroke> Strokes(Page page)
    {
        var strokes = new List<Stroke>();
        double width = 1, red = 0, green = 0;
        double? fromX = null, fromY = null;
        foreach (var op in page.Contents)
        {
            switch (op)
            {
                case SetLineWidth w: width = w.LineWidth; break;
                case SetRGBColorStroke rg: red = rg.R; green = rg.G; break;
                case MoveTo m: fromX = m.X; fromY = m.Y; break;
                case LineTo l when fromX is { } x0 && fromY is { } y0:
                    strokes.Add(new Stroke(x0, y0, l.X, l.Y, width, red, green));
                    fromX = null;
                    break;
            }
        }
        Assert.NotEmpty(strokes);
        Assert.True(strokes.All(s => s.Y1 <= PageTop + 1e-6 && s.Y2 <= PageTop + 1e-6));
        return strokes;
    }

    private sealed record Placed(string Text, double X, double Y);

    private sealed record RenderedPage(List<Stroke> Strokes, List<Placed> Texts);

    private static bool Near(double a, double b) => Math.Abs(a - b) < 0.01;

    private sealed record Stroke(double X1, double Y1, double X2, double Y2, double Width, double Red, double Green)
    {
        public bool Vertical => Math.Abs(X1 - X2) < 1e-6;
        public double Top => Math.Max(Y1, Y2);
    }
}
