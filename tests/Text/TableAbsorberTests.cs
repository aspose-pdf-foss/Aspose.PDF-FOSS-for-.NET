using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Text;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Text;

public class TableAbsorberTests
{
    /// <summary>
    /// Build a PDF with grid lines (m/l/S) forming a 2x2 table with text in each cell.
    /// Grid: 2 columns (x=50, x=200, x=350), 3 horizontal lines (y=700, y=670, y=640).
    /// Text placed inside cells.
    /// </summary>
    [Fact]
    public void GridLines_DetectsTableWithCorrectCells()
    {
        var content = new StringBuilder();
        // Draw grid lines
        // 3 horizontal lines
        content.Append("50 700 m 350 700 l S\n");   // top
        content.Append("50 670 m 350 670 l S\n");   // middle
        content.Append("50 640 m 350 640 l S\n");   // bottom
        // 3 vertical lines
        content.Append("50 700 m 50 640 l S\n");    // left
        content.Append("200 700 m 200 640 l S\n");  // middle
        content.Append("350 700 m 350 640 l S\n");  // right

        // Text in cells
        content.Append("BT /F1 12 Tf 60 680 Td (Header1) Tj ET\n");
        content.Append("BT /F1 12 Tf 210 680 Td (Header2) Tj ET\n");
        content.Append("BT /F1 12 Tf 60 650 Td (Cell1) Tj ET\n");
        content.Append("BT /F1 12 Tf 210 650 Td (Cell2) Tj ET\n");

        var pdf = PdfBuilder.BuildWithTextContent(Encoding.ASCII.GetBytes(content.ToString()));
        using var doc = Document.Open(pdf);
        var absorber = new TableAbsorber();
        absorber.Visit(doc.Pages[1]);

        Assert.Single(absorber.Tables);
        var table = absorber.Tables[0];
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(2, table.Rows[0].Cells.Count);
        Assert.Equal("Header1", table.Rows[0].Cells[0].Text);
        Assert.Equal("Header2", table.Rows[0].Cells[1].Text);
        Assert.Equal("Cell1", table.Rows[1].Cells[0].Text);
        Assert.Equal("Cell2", table.Rows[1].Cells[1].Text);
    }

    [Fact]
    public void GridLines_CellRectsArePopulated()
    {
        var content = new StringBuilder();
        // 2x1 grid (1 row, 2 columns)
        content.Append("100 500 m 400 500 l S\n"); // top
        content.Append("100 470 m 400 470 l S\n"); // bottom
        content.Append("100 500 m 100 470 l S\n"); // left
        content.Append("250 500 m 250 470 l S\n"); // mid
        content.Append("400 500 m 400 470 l S\n"); // right

        content.Append("BT /F1 10 Tf 110 480 Td (A) Tj ET\n");
        content.Append("BT /F1 10 Tf 260 480 Td (B) Tj ET\n");

        var pdf = PdfBuilder.BuildWithTextContent(Encoding.ASCII.GetBytes(content.ToString()));
        using var doc = Document.Open(pdf);
        var absorber = new TableAbsorber();
        absorber.Visit(doc.Pages[1]);

        Assert.Single(absorber.Tables);
        var table = absorber.Tables[0];
        Assert.Single(table.Rows);
        var row = table.Rows[0];

        // Cell 0: LLX=100, LLY=470, URX=250, URY=500
        Assert.NotNull(row.Cells[0].Rect);
        Assert.Equal(100, row.Cells[0].Rect!.LLX, 1);
        Assert.Equal(470, row.Cells[0].Rect!.LLY, 1);
        Assert.Equal(250, row.Cells[0].Rect!.URX, 1);
        Assert.Equal(500, row.Cells[0].Rect!.URY, 1);

        // Cell 1: LLX=250, LLY=470, URX=400, URY=500
        Assert.NotNull(row.Cells[1].Rect);
        Assert.Equal(250, row.Cells[1].Rect!.LLX, 1);
        Assert.Equal(400, row.Cells[1].Rect!.URX, 1);
    }

    [Fact]
    public void GridLines_TableRectCoversFullGrid()
    {
        var content = new StringBuilder();
        // 3x2 grid
        content.Append("50 600 m 300 600 l S\n");
        content.Append("50 570 m 300 570 l S\n");
        content.Append("50 540 m 300 540 l S\n");
        content.Append("50 600 m 50 540 l S\n");
        content.Append("175 600 m 175 540 l S\n");
        content.Append("300 600 m 300 540 l S\n");

        content.Append("BT /F1 10 Tf 60 580 Td (R1C1) Tj ET\n");
        content.Append("BT /F1 10 Tf 185 580 Td (R1C2) Tj ET\n");
        content.Append("BT /F1 10 Tf 60 550 Td (R2C1) Tj ET\n");
        content.Append("BT /F1 10 Tf 185 550 Td (R2C2) Tj ET\n");

        var pdf = PdfBuilder.BuildWithTextContent(Encoding.ASCII.GetBytes(content.ToString()));
        using var doc = Document.Open(pdf);
        var absorber = new TableAbsorber();
        absorber.Visit(doc.Pages[1]);

        Assert.Single(absorber.Tables);
        var table = absorber.Tables[0];
        Assert.NotNull(table.Rect);
        Assert.Equal(50, table.Rect!.LLX, 1);
        Assert.Equal(540, table.Rect!.LLY, 1);
        Assert.Equal(300, table.Rect!.URX, 1);
        Assert.Equal(600, table.Rect!.URY, 1);
    }

    [Fact]
    public void NoGridLines_FallsBackToTextHeuristic()
    {
        var content = new StringBuilder();
        // Two columns of text with a gap, no line drawing
        content.Append("BT /F1 10 Tf 50 700 Td (Name) Tj ET\n");
        content.Append("BT /F1 10 Tf 200 700 Td (Age) Tj ET\n");
        content.Append("BT /F1 10 Tf 50 685 Td (Alice) Tj ET\n");
        content.Append("BT /F1 10 Tf 200 685 Td (30) Tj ET\n");
        content.Append("BT /F1 10 Tf 50 670 Td (Bob) Tj ET\n");
        content.Append("BT /F1 10 Tf 200 670 Td (25) Tj ET\n");

        var pdf = PdfBuilder.BuildWithTextContent(Encoding.ASCII.GetBytes(content.ToString()));
        using var doc = Document.Open(pdf);
        var absorber = new TableAbsorber();
        absorber.Visit(doc.Pages[1]);

        // Should detect table via text heuristic
        Assert.Single(absorber.Tables);
        var table = absorber.Tables[0];
        Assert.True(table.Rows.Count >= 2);
        // First row should have Name and Age
        Assert.Contains(table.Rows[0].Cells, c => c.Text.Contains("Name"));
        Assert.Contains(table.Rows[0].Cells, c => c.Text.Contains("Age"));
    }

    [Fact]
    public void RectOperator_CreatesGridFromRectangle()
    {
        var content = new StringBuilder();
        // Use re operator to draw cell rectangles
        // Two adjacent rectangles forming a 1x2 grid
        content.Append("100 500 150 30 re S\n"); // rect at (100,500) w=150 h=30
        content.Append("250 500 150 30 re S\n"); // rect at (250,500) w=150 h=30

        content.Append("BT /F1 10 Tf 110 510 Td (Left) Tj ET\n");
        content.Append("BT /F1 10 Tf 260 510 Td (Right) Tj ET\n");

        var pdf = PdfBuilder.BuildWithTextContent(Encoding.ASCII.GetBytes(content.ToString()));
        using var doc = Document.Open(pdf);
        var absorber = new TableAbsorber();
        absorber.Visit(doc.Pages[1]);

        Assert.Single(absorber.Tables);
        var table = absorber.Tables[0];
        Assert.Single(table.Rows);
        Assert.Equal(2, table.Rows[0].Cells.Count);
        Assert.Equal("Left", table.Rows[0].Cells[0].Text);
        Assert.Equal("Right", table.Rows[0].Cells[1].Text);
    }

    [Fact]
    public void GridLines_3x3Table_AllCellsPopulated()
    {
        var content = new StringBuilder();
        // 3x3 grid: x = 50, 150, 250, 350; y = 700, 670, 640, 610
        for (var y = 700; y >= 610; y -= 30)
            content.Append($"50 {y} m 350 {y} l S\n");
        for (var x = 50; x <= 350; x += 100)
            content.Append($"{x} 700 m {x} 610 l S\n");

        // Fill cells
        var labels = new[] { "A1", "B1", "C1", "A2", "B2", "C2", "A3", "B3", "C3" };
        var idx = 0;
        for (var r = 0; r < 3; r++)
        {
            for (var c = 0; c < 3; c++)
            {
                var x = 60 + c * 100;
                var y = 680 - r * 30;
                content.Append($"BT /F1 10 Tf {x} {y} Td ({labels[idx++]}) Tj ET\n");
            }
        }

        var pdf = PdfBuilder.BuildWithTextContent(Encoding.ASCII.GetBytes(content.ToString()));
        using var doc = Document.Open(pdf);
        var absorber = new TableAbsorber();
        absorber.Visit(doc.Pages[1]);

        Assert.Single(absorber.Tables);
        var table = absorber.Tables[0];
        Assert.Equal(3, table.Rows.Count);
        Assert.Equal(3, table.Rows[0].Cells.Count);
        Assert.Equal("A1", table.Rows[0].Cells[0].Text);
        Assert.Equal("C3", table.Rows[2].Cells[2].Text);
    }

    [Fact]
    public void EmptyCells_ArePreservedInGrid()
    {
        var content = new StringBuilder();
        // 2x2 grid, only one cell has text
        content.Append("50 500 m 250 500 l S\n");
        content.Append("50 470 m 250 470 l S\n");
        content.Append("50 440 m 250 440 l S\n");
        content.Append("50 500 m 50 440 l S\n");
        content.Append("150 500 m 150 440 l S\n");
        content.Append("250 500 m 250 440 l S\n");

        // Only one cell has text
        content.Append("BT /F1 10 Tf 60 480 Td (OnlyHere) Tj ET\n");

        var pdf = PdfBuilder.BuildWithTextContent(Encoding.ASCII.GetBytes(content.ToString()));
        using var doc = Document.Open(pdf);
        var absorber = new TableAbsorber();
        absorber.Visit(doc.Pages[1]);

        Assert.Single(absorber.Tables);
        var table = absorber.Tables[0];
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(2, table.Rows[0].Cells.Count);
        Assert.Equal("OnlyHere", table.Rows[0].Cells[0].Text);
        // Other cells should be empty
        Assert.Equal("", table.Rows[0].Cells[1].Text);
        Assert.Equal("", table.Rows[1].Cells[0].Text);
        Assert.Equal("", table.Rows[1].Cells[1].Text);
    }

    [Fact]
    public void SingleRowGrid_NoFalseTable()
    {
        var content = new StringBuilder();
        // Only 1 horizontal line and 2 vertical lines — not enough for a row
        content.Append("50 500 m 250 500 l S\n");
        content.Append("50 500 m 50 470 l S\n");
        content.Append("250 500 m 250 470 l S\n");

        content.Append("BT /F1 10 Tf 60 480 Td (Text) Tj ET\n");

        var pdf = PdfBuilder.BuildWithTextContent(Encoding.ASCII.GetBytes(content.ToString()));
        using var doc = Document.Open(pdf);
        var absorber = new TableAbsorber();
        absorber.Visit(doc.Pages[1]);

        // Only 1 horizontal line, so grid detection fails.
        // Fallback heuristic won't find multiple columns either.
        Assert.Empty(absorber.Tables);
    }
}
