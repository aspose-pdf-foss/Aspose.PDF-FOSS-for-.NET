using Aspose.Pdf;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Generator;

/// <summary>A table or cell with rounded corners (<see cref="Table.CornerRadii"/>,
/// <see cref="Cell.CornerRadii"/>): the backgrounds are filled inside rounded outlines;
/// a grid whose rules stand inside its column widths rounds its cells' and its own
/// rules too, a collapsed grid keeps its shared rules straight. Read back from the
/// page's own operators.</summary>
public sealed class RoundedGridTests
{
    private const double Margin = 36;

    [Fact]
    public void ASeparatedGridPaintsItsBackgroundOnceUnderTheWholeRoundedFrame()
    {
        var table = Grid(separated: true, t =>
        {
            t.Border = new BorderInfo(BorderSide.All, 3f, Color.Green);
            t.BackgroundColor = Color.LightGray;
            t.HorizontalCellSpacing = 4;
            t.VerticalCellSpacing = 4;
            t.CornerRadii = new CornerRadii(new CornerRadius(12));
        });
        var ops = Ops(table);
        // One fill in the background colour, over the frame's box: two 100 columns,
        // three 4 pt gaps and the 3 pt border either side.
        var backgrounds = Fills(ops, Color.LightGray);
        var background = Assert.Single(backgrounds);
        Assert.Equal(200 + 3 * 4 + 2 * 3, background.W, 2);
        // It is clipped to the outline corner by corner, and the frame is cut by the ring.
        Assert.True(ops.IndexOf("W") < ops.IndexOf(background.Op));
        Assert.Contains("W*", ops);
    }

    [Fact]
    public void ACollapsedGridRoundsItsBackgroundOutToItsOuterRulesAndKeepsTheRulesStraight()
    {
        var table = Grid(separated: false, t =>
        {
            t.Border = new BorderInfo(BorderSide.All, 2f);
            t.BackgroundColor = Color.Cyan;
            t.CornerRadii = new CornerRadii(new CornerRadius(10));
        });
        var ops = Ops(table);
        var background = Assert.Single(Fills(ops, Color.Cyan));
        // The columns plus half the 2 pt outer rule on each side.
        Assert.Equal(202, background.W, 2);
        Assert.Equal(Margin, background.X, 2);
        Assert.DoesNotContain("W*", ops);
    }

    [Fact]
    public void ARoundedCellInASeparatedGridHasItsRuleInTheRingAndInACollapsedOneOnlyItsBackground()
    {
        static void Round(Table t)
        {
            var cell = t.Rows[0].Cells[0];
            cell.BackgroundColor = Color.Yellow;
            cell.Border = new BorderInfo(BorderSide.All, 2f, Color.Red);
            cell.CornerRadii = new CornerRadii(new CornerRadius(8));
        }
        var separated = Ops(Grid(separated: true, Round));
        Assert.Contains("W*", separated);
        Assert.Equal(100, Assert.Single(Fills(separated, Color.Yellow)).W, 2);

        var collapsed = Ops(Grid(separated: false, Round));
        Assert.DoesNotContain("W*", collapsed);
        var fill = Assert.Single(Fills(collapsed, Color.Yellow));
        Assert.True(collapsed.IndexOf("W") < collapsed.IndexOf(fill.Op));
    }

    private static Table Grid(bool separated, Action<Table> style)
    {
        var table = new Table
        {
            ColumnWidths = "100 100",
            DefaultCellBorder = new BorderInfo(BorderSide.All, 0.5f),
            DefaultCellPadding = new MarginInfo(2, 2, 2, 2),
            RulesInsideColumnWidth = separated,
            IsBordersCollapsed = !separated,
        };
        for (var r = 0; r < 2; r++)
        {
            var row = table.Rows.Add();
            for (var c = 0; c < 2; c++) row.Cells.Add().Paragraphs.Add(new TextFragment($"r{r}c{c}"));
        }
        style(table);
        return table;
    }

    private static List<string> Ops(Table table)
    {
        using var doc = new Document();
        var page = doc.Pages.Add();
        page.PageInfo.Margin = new MarginInfo(Margin, Margin, Margin, Margin);
        page.Paragraphs.Add(table);
        using var reopened = Document.Open(doc.ToArray());
        return reopened.Pages[1].Contents.Select(op => op.ToPdf().Trim()).ToList();
    }

    /// <summary>The rectangles filled in the given colour, each with the operator that
    /// drew it (so its place in the stream can be compared).</summary>
    private static List<(double X, double W, string Op)> Fills(List<string> ops, Color colour)
    {
        var found = new List<(double, double, string)>();
        var inColour = false;
        string? rect = null;
        for (var i = 0; i < ops.Count; i++)
        {
            var op = ops[i];
            if (op.EndsWith(" rg"))
            {
                var v = op.Split(' ').Take(3).Select(s => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                inColour = Math.Abs(v[0] * 255 - colour.R) < 1 && Math.Abs(v[1] * 255 - colour.G) < 1 && Math.Abs(v[2] * 255 - colour.B) < 1;
            }
            else if (op.EndsWith(" re")) rect = op;
            else if (op == "f" && rect is not null)
            {
                if (inColour)
                {
                    var v = rect.Split(' ').Take(4).Select(s => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                    // The fill is renamed to a marker of its own, so IndexOf finds this one.
                    ops[i] = $"f#{i}";
                    found.Add((v[0], v[2], ops[i]));
                }
                rect = null;
            }
        }
        return found;
    }
}
