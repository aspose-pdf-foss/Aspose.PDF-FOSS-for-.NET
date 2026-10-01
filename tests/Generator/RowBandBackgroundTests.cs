using Aspose.Pdf;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Generator;

/// <summary>A row whose background paints as ONE band (<see cref="Row.BackgroundIsBand"/>):
/// the whole row box across every column, from grid line to grid line, under the
/// rules -- not one fill per cell inside them.</summary>
public sealed class RowBandBackgroundTests
{
    [Fact]
    public void ABandFillSpansTheRowFromLineToLine()
    {
        var fills = Render(band: true);
        var fill = Assert.Single(fills);
        // Two 100 pt columns on a 0.5 pt collapsed grid: the band starts half a
        // rule in and is exactly the row's pitch tall.
        Assert.Equal(36.25, fill.X, 3);
        Assert.Equal(200, fill.W, 3);
        Assert.Equal(RowPitch(), fill.H, 2);
    }

    [Fact]
    public void WithoutTheBandEachCellFillsInsideItsRules()
    {
        var fills = Render(band: false);
        Assert.Equal(2, fills.Count);
        Assert.All(fills, f => Assert.True(f.W < 100));
    }

    private static double RowPitch()
    {
        var fills = Render(band: true, rows: 2);
        return fills.Max(f => f.Y) - fills.Min(f => f.Y);
    }

    private static List<(double X, double Y, double W, double H)> Render(bool band, int rows = 1)
    {
        using var doc = new Document();
        var page = doc.Pages.Add();
        page.PageInfo.Margin = new MarginInfo(36, 36, 36, 36);
        var table = new Table
        {
            ColumnWidths = "100 100",
            DefaultCellBorder = new BorderInfo(BorderSide.All, 0.5f),
            DefaultCellPadding = new MarginInfo(2, 2, 2, 2),
            IsBordersCollapsed = true,
        };
        for (var r = 0; r < rows; r++)
        {
            var row = table.Rows.Add();
            row.BackgroundColor = Color.LightGray;
            row.BackgroundIsBand = band;
            row.Cells.Add().Paragraphs.Add(new TextFragment("a"));
            row.Cells.Add().Paragraphs.Add(new TextFragment("b"));
        }
        page.Paragraphs.Add(table);
        using var reopened = Document.Open(doc.ToArray());

        var fills = new List<(double, double, double, double)>();
        (double X, double Y, double W, double H)? rect = null;
        foreach (var op in reopened.Pages[1].Contents)
        {
            switch (op)
            {
                case Re re: rect = (re.X, re.Y, re.Width, re.Height); break;
                case Fill when rect is { } r: fills.Add(r); rect = null; break;
                case EndPath: rect = null; break;
            }
        }
        return fills;
    }
}
