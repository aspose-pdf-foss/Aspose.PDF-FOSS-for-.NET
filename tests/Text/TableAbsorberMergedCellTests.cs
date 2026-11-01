using System.Linq;
using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Tests.Helpers;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Text;

public class TableAbsorberMergedCellTests
{
    // A five-column grid, columns 60 pt wide from x 50, rows 20 pt high from y 700 down to 620.
    private static readonly double[] Columns = { 50, 110, 170, 230, 290, 350 };
    private static readonly double[] Rows = { 700, 680, 660, 640, 620 };

    /// <summary>The grid's rules - every column rule from the top except those listed with the height they start
    /// below, every row rule across except the stretches listed - and text at the given points.</summary>
    private static AbsorbedTable Table((double x, double fromY)[] shortColumns, (double y, double x1, double x2)[] gaps, params (string text, double x, double y)[] texts)
    {
        var content = new StringBuilder();
        foreach (var x in Columns)
        {
            var top = shortColumns.Where(s => s.x == x).Select(s => s.fromY).DefaultIfEmpty(Rows[0]).First();
            content.Append($"{x} {top} m {x} {Rows[^1]} l S\n");
        }
        foreach (var y in Rows)
        {
            var gap = gaps.FirstOrDefault(g => g.y == y);
            if (gap.x2 > gap.x1)
                content.Append($"{Columns[0]} {y} m {gap.x1} {y} l S\n{gap.x2} {y} m {Columns[^1]} {y} l S\n");
            else
                content.Append($"{Columns[0]} {y} m {Columns[^1]} {y} l S\n");
        }
        foreach (var (text, x, y) in texts)
            content.Append($"BT /F1 8 Tf {x} {y} Td ({text}) Tj ET\n");
        for (var r = 2; r < Rows.Length - 1; r++)
            for (var c = 0; c < Columns.Length - 1; c++)
                content.Append($"BT /F1 8 Tf {Columns[c] + 5} {Rows[r] - 14} Td (d{r}{c}) Tj ET\n");
        using var doc = Document.Open(PdfBuilder.BuildWithTextContent(Encoding.ASCII.GetBytes(content.ToString())));
        var absorber = new TableAbsorber();
        absorber.Visit(doc.Pages[1]);
        return Assert.Single(absorber.Tables);
    }

    [Fact]
    public void ACellAcrossThreeColumnsIsOneCellHoldingItsText()
    {
        var table = Table(new[] { (170.0, 680.0), (230.0, 680.0) }, new (double, double, double)[0],
            ("A", 55, 686), ("WIDE", 185, 686), ("E", 295, 686));

        var header = table.Rows[0].Cells;
        Assert.Equal(new[] { "A", "WIDE", "E" }, header.Select(c => c.Text).ToArray());
        Assert.Equal(110, header[1].Rect!.LLX, 0);
        Assert.Equal(290, header[1].Rect!.URX, 0);
    }

    [Fact]
    public void ACellMergedOverTwoRowsAndTwoColumnsIsOneCellHoldingItsText()
    {
        var table = Table(new[] { (170.0, 660.0) }, new[] { (680.0, 110.0, 230.0) },
            ("A", 55, 686), ("BOX", 125, 676), ("D", 235, 686), ("E", 295, 686), ("d", 235, 666), ("e", 295, 666));

        var box = table.Rows.SelectMany(r => r.Cells).Single(c => c.Text == "BOX");
        Assert.Equal(110, box.Rect!.LLX, 0);
        Assert.Equal(230, box.Rect!.URX, 0);
        Assert.Equal(660, box.Rect!.LLY, 0);
        Assert.Equal(700, box.Rect!.URY, 0);
    }
}
