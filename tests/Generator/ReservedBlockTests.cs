using Aspose.Pdf;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Generator;

/// <summary>A <see cref="ReservedBlock"/>: the flow asks its caller for a part in the room
/// left on the page, keeps the height answered, breaks the page while the block continues,
/// and reports each part's page and rectangle once the document is saved; a block in a table
/// cell is measured at the cell's width and told where the cell put it.</summary>
public sealed class ReservedBlockTests
{
    private const double Margin = 36;
    private const double PageTop = 842 - Margin;
    private const double PageBottom = Margin;
    private const double Tolerance = 0.01;

    /// <summary>A block <paramref name="height"/> tall that fills each room it is offered and
    /// continues while anything is left; each part records the room it was given.</summary>
    private static (ReservedBlock Block, List<ReservedRoom> Rooms) Filler(double height)
    {
        var rooms = new List<ReservedRoom>();
        var block = new ReservedBlock();
        var left = height;
        block.LayOut = room =>
        {
            rooms.Add(room);
            var take = Math.Min(left, room.Top - room.Bottom);
            left -= take;
            return new ReservedPart { Height = take, Continues = left > 0 };
        };
        return (block, rooms);
    }

    private static TextFragment Line(string text) => new(text) { TextState = { FontSize = 12 } };

    private static Document Saved(params BaseParagraph[] paragraphs)
    {
        var doc = new Document();
        var page = doc.Pages.Add();
        page.PageInfo.Margin = new MarginInfo(Margin, Margin, Margin, Margin);
        foreach (var paragraph in paragraphs) page.Paragraphs.Add(paragraph);
        doc.Save(new MemoryStream());
        return doc;
    }

    [Fact]
    public void ThePartsTakeTheRoomsPageByPage()
    {
        var (block, rooms) = Filler(1000);
        var doc = Saved(Line("before"), block, Line("after"));

        Assert.Equal(2, block.Parts.Count);
        Assert.Equal(2, rooms.Count);
        Assert.Equal(1, block.Parts[0].Page!.Number);
        Assert.Equal(2, block.Parts[1].Page!.Number);
        // The first part starts under the line before it and ends at the page's bottom.
        var first = block.Parts[0].Rect!;
        Assert.True(first.URY < PageTop && first.URY > PageTop - 40, "the first part starts under the line before it");
        Assert.Equal(PageBottom, first.LLY, Tolerance);
        Assert.Equal(first.Height, block.Parts[0].Height, Tolerance);
        Assert.False(rooms[0].IsPageTop);
        // The second part starts at the top of the next page and takes what was left.
        var second = block.Parts[1].Rect!;
        Assert.Equal(PageTop, second.URY, Tolerance);
        Assert.Equal(1000 - first.Height, second.Height, Tolerance);
        Assert.True(rooms[1].IsPageTop);
        Assert.Equal(Margin, second.LLX, Tolerance);
        Assert.Equal(595 - 2 * Margin, second.Width, Tolerance);
        Assert.Equal(2, doc.Pages.Count);
    }

    [Fact]
    public void ABlockThatFitsNothingMovesToTheNextPage()
    {
        var asked = 0;
        var block = new ReservedBlock
        {
            LayOut = room =>
            {
                asked++;
                return room.IsPageTop ? new ReservedPart { Height = 100 } : null;
            },
        };
        var doc = Saved(Line("before"), block);

        Assert.Equal(2, asked);
        var part = Assert.Single(block.Parts);
        Assert.Equal(2, part.Page!.Number);
        Assert.Equal(PageTop, part.Rect!.URY, Tolerance);
        Assert.Equal(100, part.Rect.Height, Tolerance);
        Assert.Equal(2, doc.Pages.Count);
    }

    [Fact]
    public void ABlockInsideABoxTakesTheBoxsRoom()
    {
        var (block, rooms) = Filler(50);
        var box = new BoxBlock { Padding = new MarginInfo(10, 10, 10, 10), Border = new BorderInfo(BorderSide.All, 2) };
        box.Paragraphs.Add(block);
        Saved(box);

        var room = Assert.Single(rooms);
        Assert.Equal(Margin + 2 + 10, room.Left, Tolerance);
        Assert.Equal(595 - 2 * Margin - 2 * 12, room.Width, Tolerance);
        Assert.Equal(PageTop - 12, room.Top, Tolerance);
        Assert.Equal(PageTop - 12, block.Parts[0].Rect!.URY, Tolerance);
    }

    [Fact]
    public void ABlockInACellIsMeasuredAtTheCellsWidthAndToldItsPlace()
    {
        var (block, rooms) = Filler(50);
        var table = new Table { ColumnWidths = "100 200" };
        var row = table.Rows.Add();
        row.Cells.Add("first");
        var cell = row.Cells.Add();
        cell.Paragraphs.Add(block);
        var doc = Saved(table);

        var room = Assert.Single(rooms);
        Assert.True(room.Width > 180 && room.Width <= 200, $"measured at the cell's content width, not {room.Width}");
        var part = Assert.Single(block.Parts);
        Assert.Equal(1, part.Page!.Number);
        Assert.Equal(room.Width, part.Rect!.Width, Tolerance);
        Assert.Equal(50, part.Rect.Height, Tolerance);
        Assert.True(part.Rect.LLX >= Margin + 100 && part.Rect.LLX < Margin + 120, $"in the second column, not at {part.Rect.LLX}");
        Assert.True(part.Rect.URY <= PageTop && part.Rect.URY > PageTop - 20, $"at the row's top, not at {part.Rect.URY}");
        Assert.Single(doc.Pages);
    }
}
